using Avalonia.Threading;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher
{
    // Launcher-wide bot preferences shared by every session (UserData/bot.json):
    // whether the script library refreshes itself on launch, and the last
    // script + farm options so Start is one click next time.
    public sealed class BotSettings
    {
        public bool AutoUpdateLibrary { get; set; } = true;
        public string LastChain { get; set; } = "";
        public bool FarmAfterChain { get; set; } = true;
        public int FarmLoops { get; set; }
        // Load Script window: the last loaded script file (and chain within it).
        public string LastScriptPath { get; set; } = "";
        public string LastScriptChain { get; set; } = "";

        public static BotSettings Current { get; } = Load();

        private static string GetConfigPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "bot.json");

        private static BotSettings Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    return JsonConvert.DeserializeObject<BotSettings>(File.ReadAllText(path)) ?? new BotSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"BotSettings load failed: {ex.Message}");
            }
            return new BotSettings();
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"BotSettings save failed: {ex.Message}");
            }
        }
    }

    // Downloads the community content repo (Infinity-Files) into
    // <game dir>/UserData/Beyond/Infinity-Files, where the agent reads scripts
    // and autoskills straight from disk. Static and shared across sessions: they
    // all point at the same folder, so one update (manual or the once-per-launch
    // auto-update, à la Skua's "Auto Update Scripts") serves every session.
    public static class LibraryService
    {
        // GitHub codeload zip for the default branch. If the repo's default branch
        // is "master" instead of "main", the update falls back automatically.
        private const string LibraryZipUrlMain =
            "https://github.com/retr0gr4d3/Infinity-Files/archive/refs/heads/main.zip";
        private const string LibraryZipUrlMaster =
            "https://github.com/retr0gr4d3/Infinity-Files/archive/refs/heads/master.zip";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static readonly HashSet<string> _autoUpdated = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsBusy { get; private set; }
        public static string Status { get; private set; } =
            "Click \"Update Library\" to download the latest scripts and autoskills.";

        // Raised on the UI thread whenever IsBusy/Status change.
        public static event Action? StateChanged;
        // Raised on the UI thread after a successful update, with the game dir
        // whose library changed, so sessions can rescan and tell their agent.
        public static event Action<string>? LibraryUpdated;

        // <game dir>/UserData/Beyond/Infinity-Files — mirrors BeyondEnv.UserDataDirectory
        // (which is <game dir>/UserData) on the agent side. Null when no game is configured.
        public static string? Root(string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                return null;
            }

            return Path.Combine(gameDirectory, "UserData", "Beyond", "Infinity-Files");
        }

        // Once per launcher run per game dir, when the toggle is on. Fire-and-forget.
        public static void AutoUpdate(string? gameDirectory)
        {
            if (!BotSettings.Current.AutoUpdateLibrary
                || !GameLocator.Exists(gameDirectory)
                || !_autoUpdated.Add(gameDirectory!))
            {
                return;
            }

            _ = UpdateAsync(gameDirectory!);
        }

        public static async Task UpdateAsync(string? gameDirectory)
        {
            string? root = Root(gameDirectory);
            if (root == null)
            {
                SetState(IsBusy, "No game directory configured — set it in the Configurator first.");
                return;
            }

            await _gate.WaitAsync();
            try
            {
                SetState(true, "Downloading Infinity-Files…");
                await Task.Run(() => { DownloadAndExtract(root); BotStorage.Import(Path.Combine(root, "Scripts")); });
                SetState(false, $"Updated {DateTime.Now:HH:mm}.");
                Dispatcher.UIThread.Post(() => LibraryUpdated?.Invoke(gameDirectory!));
            }
            catch (Exception ex)
            {
                SetState(false, $"Update failed: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        private static void SetState(bool busy, string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsBusy = busy;
                Status = status;
                StateChanged?.Invoke();
            });
        }

        // Download the repo zip, extract it next to the library, then swap it in
        // with directory renames so a game reading the library mid-update never
        // sees a half-copied tree. Runs off the UI thread. Tries "main", then "master".
        private static void DownloadAndExtract(string root)
        {
            string tmpZip = Path.Combine(Path.GetTempPath(), $"infinity-files-{Guid.NewGuid():N}.zip");
            string tmpDir = Path.Combine(Path.GetTempPath(), $"infinity-files-{Guid.NewGuid():N}");
            string staging = root + ".download";
            string previous = root + ".old";
            bool installed = false;
            try
            {
                byte[] data = TryDownload(LibraryZipUrlMain) ?? TryDownload(LibraryZipUrlMaster)
                    ?? throw new Exception("could not download the repo (checked main and master branches).");
                File.WriteAllBytes(tmpZip, data);

                Directory.CreateDirectory(tmpDir);
                ZipFile.ExtractToDirectory(tmpZip, tmpDir);

                // GitHub archives wrap everything in a single "<repo>-<branch>" folder.
                string inner = Directory.GetDirectories(tmpDir).FirstOrDefault() ?? tmpDir;

                DeleteIfExists(staging);
                if (Directory.Exists(previous)) Directory.Move(previous, previous + ".recovery-" + Guid.NewGuid().ToString("N"));
                CopyDirectory(inner, staging);

                if (Directory.Exists(root))
                {
                    Directory.Move(root, previous);
                }
                try
                {
                    Directory.Move(staging, root);
                    installed = true;
                }
                catch
                {
                    // Put the old library back rather than leave none at all.
                    if (!Directory.Exists(root) && Directory.Exists(previous))
                    {
                        Directory.Move(previous, root);
                    }
                    throw;
                }
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
                try { DeleteIfExists(tmpDir); } catch { }
                try { DeleteIfExists(staging); } catch { }
                if (installed) { try { DeleteIfExists(previous); } catch { } }
            }
        }

        private static void DeleteIfExists(string dir)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static byte[]? TryDownload(string url)
        {
            try
            {
                using HttpResponseMessage resp = _http.GetAsync(url).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }

                return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }
    }
}
