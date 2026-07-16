using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Launcher.ViewModels
{
    // LibraryWindow: pulls the community content repo (Infinity-Files) into the
    // agent's UserData/Beyond folder and loads its two content types individually.
    //
    //   - Scripts    : one quest-chain .json per file (Scripts/<Name>.json). Loading
    //                  one routes each chain through the existing "SaveChain" command,
    //                  so it merges into chains.json and re-pushes exactly like the
    //                  Chain Editor's save.
    //   - Autoskills : a single Autoskills/autoskills.txt holding one pipe-delimited
    //                  rotation per line. Loading one routes the line through the
    //                  existing "ImportSkillset" command.
    //
    // The download/extract/listing is pure launcher-side file IO; loading into the
    // live game requires an active session (SendCommand no-ops when disconnected).
    public partial class MainWindowViewModel
    {
        // GitHub codeload zip for the default branch. If the repo's default branch
        // is "master" instead of "main", UpdateLibrary falls back automatically.
        private const string LibraryZipUrlMain =
            "https://github.com/retr0gr4d3/Infinity-Files/archive/refs/heads/main.zip";
        private const string LibraryZipUrlMaster =
            "https://github.com/retr0gr4d3/Infinity-Files/archive/refs/heads/master.zip";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

        public ObservableCollection<string> LibraryScripts { get; } = [];
        public ObservableCollection<string> LibraryAutoskills { get; } = [];

        // Display name -> full pipe line, so Load can replay the exact import string.
        private readonly Dictionary<string, string> _autoskillLines = [];

        [ObservableProperty] private string? _selectedLibraryScript;
        [ObservableProperty] private string? _selectedLibraryAutoskill;
        [ObservableProperty] private string _libraryStatus = "Click \"Update Library\" to download the latest scripts and autoskills.";
        [ObservableProperty] private bool _libraryBusy;

        // <game dir>/UserData/Beyond/Infinity-Files — mirrors BeyondEnv.UserDataDirectory
        // (which is <game dir>/UserData) on the agent side. Null when no game is configured.
        private string? LibraryRoot()
        {
            if (string.IsNullOrWhiteSpace(GameDirectory))
            {
                return null;
            }

            return Path.Combine(GameDirectory, "UserData", "Beyond", "Infinity-Files");
        }

        [RelayCommand]
        private async Task UpdateLibrary()
        {
            string? root = LibraryRoot();
            if (root == null)
            {
                LibraryStatus = "No game directory configured — set it in the Configurator first.";
                return;
            }

            LibraryBusy = true;
            LibraryStatus = "Downloading Infinity-Files…";
            try
            {
                await Task.Run(() => DownloadAndExtract(root));
                RefreshLibraryLists();
                LibraryStatus = $"Updated — {LibraryScripts.Count} script(s), {LibraryAutoskills.Count} autoskill(s).";
            }
            catch (Exception ex)
            {
                LibraryStatus = $"Update failed: {ex.Message}";
            }
            finally
            {
                LibraryBusy = false;
            }
        }

        // Download the repo zip, extract it, and replace LibraryRoot with the
        // archive's inner folder. Runs off the UI thread (no ObservableCollection
        // touches here). Tries the "main" branch, then "master".
        private static void DownloadAndExtract(string root)
        {
            string tmpZip = Path.Combine(Path.GetTempPath(), $"infinity-files-{Guid.NewGuid():N}.zip");
            string tmpDir = Path.Combine(Path.GetTempPath(), $"infinity-files-{Guid.NewGuid():N}");
            try
            {
                byte[] data = TryDownload(LibraryZipUrlMain) ?? TryDownload(LibraryZipUrlMaster)
                    ?? throw new Exception("could not download the repo (checked main and master branches).");
                File.WriteAllBytes(tmpZip, data);

                Directory.CreateDirectory(tmpDir);
                ZipFile.ExtractToDirectory(tmpZip, tmpDir);

                // GitHub archives wrap everything in a single "<repo>-<branch>" folder.
                string inner = Directory.GetDirectories(tmpDir).FirstOrDefault() ?? tmpDir;

                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(root)!);
                CopyDirectory(inner, root);
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true); } catch { }
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

        // Re-scan the extracted tree. Safe to call when nothing's downloaded yet
        // (empty lists). Must run on the UI thread (touches ObservableCollections).
        public void RefreshLibraryLists()
        {
            LibraryScripts.Clear();
            LibraryAutoskills.Clear();
            _autoskillLines.Clear();

            string? root = LibraryRoot();
            if (root == null || !Directory.Exists(root))
            {
                return;
            }

            try
            {
                string scriptsDir = Path.Combine(root, "Scripts");
                if (Directory.Exists(scriptsDir))
                {
                    foreach (string file in Directory.GetFiles(scriptsDir, "*.json").OrderBy(f => f))
                    {
                        LibraryScripts.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }

                string autoskillsFile = Path.Combine(root, "Autoskills", "autoskills.txt");
                if (File.Exists(autoskillsFile))
                {
                    foreach (string raw in File.ReadAllLines(autoskillsFile))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//"))
                        {
                            continue;
                        }

                        string name = line.Split('|')[0].Trim();
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        _autoskillLines[name] = line;
                        if (!LibraryAutoskills.Contains(name))
                        {
                            LibraryAutoskills.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LibraryStatus = $"Couldn't read library: {ex.Message}";
            }

            if (IsConnected)
            {
                _connection.SendCommand("AutoLoadLibrarySkillsets", null);
            }
        }

        [RelayCommand]
        private void LoadScript(string? name)
        {
            name ??= SelectedLibraryScript;
            string? root = LibraryRoot();
            if (string.IsNullOrEmpty(name) || root == null)
            {
                return;
            }

            if (!IsConnected)
            {
                LibraryStatus = "Start a session before loading — the game must be running.";
                return;
            }

            string path = Path.Combine(root, "Scripts", name + ".json");
            if (!File.Exists(path))
            {
                LibraryStatus = $"Script file not found: {name}.json";
                return;
            }

            try
            {
                JObject obj = JObject.Parse(File.ReadAllText(path));
                int loaded = 0;
                foreach (JProperty prop in obj.Properties())
                {
                    // A chain value is either a bare entries array (legacy) or an
                    // object { "class": "...", "entries": [...] } with a recommended class.
                    JArray? arr;
                    string? cls = null;
                    if (prop.Value is JArray bareArr)
                    {
                        arr = bareArr;
                    }
                    else if (prop.Value is JObject objVal)
                    {
                        arr = objVal["entries"] as JArray;
                        cls = (string?)objVal["class"];
                    }
                    else
                    {
                        continue;
                    }
                    if (arr == null)
                    {
                        continue;
                    }

                    JArray entries = [];
                    foreach (JToken t in arr)
                    {
                        int qid = (int?)t["qid"] ?? 0;
                        if (qid <= 0)
                        {
                            continue;
                        }

                        JObject entry = new()
                        {
                            ["qid"] = qid,
                            ["area"] = (string?)t["area"] ?? "",
                            ["frame"] = (string?)t["frame"] ?? "",
                            ["pad"] = string.IsNullOrWhiteSpace((string?)t["pad"]) ? "Spawn" : (string?)t["pad"],
                            ["items"] = (int?)t["items"] ?? (int?)t["iters"] ?? 1
                        };
                        // Target monster filter (names/ids) — the hunt targets
                        // only these. Dropping it here would silently break
                        // any library chain authored with specific mobs.
                        if (t["mon"] != null)
                        {
                            entry["mon"] = t["mon"].DeepClone();
                        }
                        entries.Add(entry);
                    }

                    if (entries.Count > 0)
                    {
                        JObject save = new()
                        {
                            ["Name"] = prop.Name,
                            ["Entries"] = entries
                        };
                        if (!string.IsNullOrEmpty(cls))
                        {
                            save["Class"] = cls;
                        }
                        _connection.SendCommand("SaveChain", save);
                        loaded++;
                    }
                }

                LibraryStatus = loaded > 0
                    ? $"Loaded script '{name}' ({loaded} chain(s)) — open the Quest Runner to run it."
                    : $"Script '{name}' had no usable chains.";
            }
            catch (Exception ex)
            {
                LibraryStatus = $"Failed to load script '{name}': {ex.Message}";
            }
        }
    }
}
