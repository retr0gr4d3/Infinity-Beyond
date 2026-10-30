using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Views;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Launcher.ViewModels
{
    // ScriptLoaderWindow (+ the control panel's Bot card): Skua-style "Load
    // Script" — pick a script .json from disk, Start/Stop/Pause it, tweak its
    // options, and keep its log. The loaded file is sent to the agent inline
    // (RunScript), so any script file runs; it doesn't have to be in a catalog.
    // The last loaded script is remembered, so after a restart Start is one click.
    public partial class MainWindowViewModel
    {
        private bool _monsterFarmingAvailable;
        private const string NoScriptStatus = "[No Script Loaded]";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoadedScript), nameof(LoadedScriptFileName))]
        private string? _loadedScriptPath;

        [ObservableProperty] private string _loadedScriptName = "";
        [ObservableProperty] private string _loadedScriptSummary = "";
        [ObservableProperty] private string _scriptStatus = NoScriptStatus;
        [ObservableProperty] private bool _showScriptOptions;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PauseScriptText))]
        private bool _isRunnerPaused;

        private JArray _loadedEntries = [];
        private JArray _loadedFarm = [];
        private JObject? _loadedBuy;
        private JArray _loadedOptions = [];
        private string _loadedTemplate = "quest-chain";

        public bool UsesStoryOptions => _loadedTemplate == "quest-chain";

        public bool HasLoadedScript => LoadedScriptPath != null;
        public string LoadedScriptFileName => LoadedScriptPath != null ? Path.GetFileName(LoadedScriptPath) : "";
        public string ScriptToggleText => IsChainRunning ? "Stop Script" : "Start Script";
        public string PauseScriptText => IsRunnerPaused ? "Resume Script" : "Pause Script";

        partial void OnIsChainRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(ScriptToggleText));
        }

        // Library scripts land here; the user's own scripts can live anywhere.
        private string? ScriptsDirectory()
        {
            return BotStorage.Prepare(GameDirectory);
        }

        // Re-load the last script on startup (silently; it may have been deleted).
        private void RestoreLastScript()
        {
            string path = BotSettings.Current.LastScriptPath;
            if (!string.IsNullOrEmpty(path)) path = BotStorage.Resolve(path);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                LoadScriptFile(path, BotSettings.Current.LastScriptChain);
            }
        }

        // Parse a script file and make it the loaded script. A file may hold
        // several chains; `chain` picks one, else the first is used.
        public bool LoadScriptFile(string path, string? chain = null)
        {
            try
            {
                JObject obj = JObject.Parse(File.ReadAllText(path));
                JProperty? prop = chain == null
                    ? obj.Properties().FirstOrDefault(p => !p.Name.StartsWith('_') && (p.Value is JArray || p.Value is JObject))
                    : obj.Property(chain);
                if (prop == null || prop.Name.StartsWith('_') || prop.Value is not (JArray or JObject))
                {
                    ScriptStatus = "[Error] no activity found in that file";
                    return false;
                }

                JObject? form = prop.Value as JObject;
                _loadedEntries = prop.Value as JArray ?? form?["entries"] as JArray ?? [];
                _loadedFarm = form?["farm"] as JArray ?? [];
                _loadedBuy = form?["buy"] as JObject;
                _loadedOptions = form?["options"] as JArray ?? [];
                _loadedTemplate = (string?)form?["template"] ?? "quest-chain";
                OnPropertyChanged(nameof(UsesStoryOptions));
                if (_loadedTemplate == "merge-shop") _loadedBuy = form?["shop"] as JObject ?? _loadedBuy;
                string cls = (string?)form?["class"] ?? "";

                LoadedScriptPath = path;
                LoadedScriptName = prop.Name;
                int steps = _loadedEntries.Count, farm = _loadedFarm.Count;
                LoadedScriptSummary = _loadedOptions.Count > 0
                    ? $"Choose from {_loadedOptions.Count} farming targets"
                    : _loadedTemplate == "merge-shop" ? "Choose merge rewards; materials calculated automatically"
                    : $"{steps} story steps, {farm} farming targets";
                if (!string.IsNullOrEmpty(cls)) LoadedScriptSummary += $" | class {cls}";

                // Default the class picker to the script's recommended skillset.
                if (!string.IsNullOrEmpty(cls))
                {
                    if (!ChainSkillsetOptions.Contains(cls))
                    {
                        ChainSkillsetOptions.Add(cls);
                    }
                    SelectedChainClass = cls;
                }
                else
                {
                    SelectedChainClass = NoChainClass;
                }

                if (!IsChainRunning)
                {
                    ScriptStatus = "[Script loaded]";
                }
                BotSettings.Current.LastScriptPath = path;
                BotSettings.Current.LastScriptChain = prop.Name;
                BotSettings.Current.Save();
                return true;
            }
            catch (Exception ex)
            {
                ScriptStatus = $"[Error] {ex.Message}";
                return false;
            }
        }

        private static Window? MainWindow =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        [RelayCommand]
        private async Task LoadScript()
        {
            Window? owner = MainWindow;
            if (owner == null)
            {
                return;
            }

            FilePickerOpenOptions options = new()
            {
                Title = "Load Script",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Beyond Scripts") { Patterns = ["*.json"] }]
            };
            string? start = LoadedScriptPath != null ? Path.GetDirectoryName(LoadedScriptPath) : ScriptsDirectory();
            if (start != null && Directory.Exists(start))
            {
                options.SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(start);
            }

            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path != null)
            {
                LoadScriptFile(path);
            }
        }

        [RelayCommand]
        private async Task ToggleScript()
        {
            if (IsChainRunning)
            {
                StopChain();
                return;
            }
            await StartLoadedScript();
        }

        private async Task StartLoadedScript()
        {
            Trace.WriteLine($"[ScriptLoader] Start pressed: script='{LoadedScriptPath}' connected={IsConnected} running={IsChainRunning}");
            if (LoadedScriptPath == null)
            {
                ScriptStatus = NoScriptStatus;
                return;
            }
            if (!IsConnected)
            {
                ScriptStatus = "[Error] game not connected yet";
                return;
            }

            if (IsChainRunning) { ScriptStatus = "Stop the current activity first."; return; }
            string? selectedClass = SelectedChainClass;
            // Re-read the selected activity without switching to another one.
            if (!LoadScriptFile(LoadedScriptPath, LoadedScriptName))
            {
                return;
            }

            SelectedChainClass = selectedClass;
            string activityName = LoadedScriptName;
            string template = _loadedTemplate;
            JArray entries = (JArray)_loadedEntries.DeepClone();
            JArray farm = (JArray)_loadedFarm.DeepClone();
            JObject? buy = _loadedBuy == null ? null : (JObject)_loadedBuy.DeepClone();
            bool chooseTargets = _loadedOptions.Count > 0;
            if (chooseTargets)
            {
                if (MainWindow == null) return;
                var chosen = await new FarmingTargetWindow(activityName, _loadedOptions).ShowDialog<JArray?>(MainWindow);
                if (chosen == null) return;
                entries = [];
                farm = [];
                buy = null;
                foreach (JObject option in chosen.OfType<JObject>())
                {
                    foreach (JToken step in option["farm"] as JArray ?? []) farm.Add(step.DeepClone());
                    foreach (JToken step in option["entries"] as JArray ?? []) farm.Add(step.DeepClone());
                }
            }

            if (template == "merge-shop")
            {
                if (MainWindow == null || buy == null || ((int?)buy["shop"] ?? 0) <= 0)
                { ScriptStatus = "[Error] this merge farm needs a shop."; return; }
                buy = await new MergeFarmWindow(_connection, buy).ShowDialog<JObject?>(MainWindow);
                if (buy == null) return;
                // Shop farming is independent of the release's story quests.
                entries = [];
            }

            JObject p = new()
            {
                ["Name"] = activityName,
                ["Entries"] = entries,
                ["Farm"] = farm,
                ["Buy"] = buy,
                ["FarmEnabled"] = chooseTargets || FarmAfterChain,
                ["FarmLoops"] = chooseTargets ? 1 : template is "merge-shop" or "monster-drops" ? 0 : ParseFarmLoops()
            };
            if (!string.IsNullOrEmpty(selectedClass) && selectedClass != NoChainClass)
            {
                p["Skillset"] = selectedClass;
            }
            if (!IsConnected || IsChainRunning) { ScriptStatus = "Game disconnected or another activity started."; return; }
            QuestRunnerLogs.Clear();
            ScriptStatus = "Starting...";
            Trace.WriteLine($"[ScriptLoader] RunScript '{LoadedScriptName}': {_loadedEntries.Count} entries, {_loadedFarm.Count} farm");
            _connection.SendCommand("RunScript", p);
        }

        [RelayCommand]
        private async Task FarmDrops()
        {
            if (!IsConnected || IsChainRunning || MainWindow == null)
            { ScriptStatus = IsChainRunning ? "Stop the current activity first." : "Start a game session first."; return; }
            if (!_monsterFarmingAvailable)
            { ScriptStatus = "The game loaded an older agent. Restart the launcher and game to use monster farming."; return; }
            JObject? plan = await new DropFarmWindow(_connection).ShowDialog<JObject?>(MainWindow);
            if (plan == null || IsChainRunning || !IsConnected) return;
            QuestRunnerLogs.Clear(); ScriptStatus = "Starting drop farm...";
            _connection.SendCommand("RunScript", plan);
        }

        [RelayCommand]
        private async Task OpenBotWriter()
        {
            if (MainWindow == null) return;
            string? path = await new BotWriterWindow(ScriptsDirectory(), gameDirectory: GameDirectory, questNames: QuestDirectory.Select(q => new KeyValuePair<int, string>(q.Id, q.Name))).ShowDialog<string?>(MainWindow);
            if (path != null) LoadScriptFile(path);
        }

        [RelayCommand]
        private void OpenFarmingTemplates()
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "FarmingTemplates");
            if (Directory.Exists(directory)) ShellOpen(directory);
        }

        [RelayCommand]
        private void PauseScript()
        {
            if (!IsChainRunning)
            {
                return;
            }
            _connection.SendCommand(IsRunnerPaused ? "ResumeQuestRunner" : "PauseQuestRunner", null);
        }

        [RelayCommand]
        private async Task EditScript()
        {
            if (MainWindow == null || LoadedScriptPath == null) return;
            string? path = await new BotWriterWindow(ScriptsDirectory(), LoadedScriptPath, LoadedScriptName, GameDirectory, QuestDirectory.Select(q => new KeyValuePair<int, string>(q.Id, q.Name))).ShowDialog<string?>(MainWindow);
            if (path != null) LoadScriptFile(path);
        }

        [RelayCommand]
        private void OpenScriptsFolder()
        {
            string? dir = LoadedScriptPath != null ? Path.GetDirectoryName(LoadedScriptPath) : ScriptsDirectory();
            if (dir != null && Directory.Exists(dir))
            {
                ShellOpen(dir);
            }
        }

        private static void ShellOpen(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ScriptLoader] couldn't open {target}: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleScriptOptions()
        {
            ShowScriptOptions = !ShowScriptOptions;
        }

        [RelayCommand]
        private void OpenScriptLoaderWindow()
        {
            ShowToolWindow<ScriptLoaderWindow>();
        }

        [RelayCommand]
        private void ClearScriptLog()
        {
            QuestRunnerLogs.Clear();
        }

        [RelayCommand]
        private async Task CopyScriptLog()
        {
            if (MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(string.Join(Environment.NewLine, QuestRunnerLogs));
            }
        }

        [RelayCommand]
        private async Task SaveScriptLog()
        {
            Window? owner = MainWindow;
            if (owner == null)
            {
                return;
            }

            IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Script Log",
                DefaultExtension = "txt",
                SuggestedFileName = $"script_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            });
            if (file == null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            await using StreamWriter writer = new(stream);
            foreach (string line in QuestRunnerLogs)
            {
                await writer.WriteLineAsync(line);
            }
        }

        // Status line in Skua's style, from the agent's runner state.
        private void UpdateScriptStatus(JObject msg)
        {
            Trace.WriteLine($"[ScriptLoader] runner state: {msg.ToString(Newtonsoft.Json.Formatting.None)}");
            IsRunnerPaused = (bool?)msg["Paused"] ?? false;
            if (IsChainRunning)
            {
                ScriptStatus = IsRunnerPaused ? "[Paused]" : "[Running]";
                return;
            }

            ScriptStatus = (string?)msg["State"] switch
            {
                "Done" => "[Finished]",
                "Failed" => "[Error]",
                _ => LoadedScriptPath != null ? (ScriptStatus == "[Running]" || ScriptStatus == "[Paused]" ? "[Stopped]" : ScriptStatus) : NoScriptStatus
            };
        }
    }
}
