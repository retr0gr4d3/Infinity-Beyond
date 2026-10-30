using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Launcher.ViewModels
{
    // LibraryWindow: browses the community content repo (Infinity-Files) that
    // LibraryService keeps in the agent's UserData/Beyond folder, and starts
    // scripts straight from the list (Skua's "Start Script" in its script repo).
    //
    //   - Scripts    : one or more quest chains per Scripts/<Name>.json. The agent
    //                  reads this folder directly, so a downloaded script is
    //                  runnable immediately — there is no per-script import step.
    //   - Autoskills : Autoskills/autoskills.txt, one rotation per line. The agent
    //                  also loads these itself (at boot and on ReloadLibrary).
    //
    // After every successful update each connected session is told to reload, so
    // new scripts show up without restarting the game.
    public partial class MainWindowViewModel
    {
        public ObservableCollection<LibraryScriptEntry> LibraryScripts { get; } = [];
        public ObservableCollection<string> LibraryAutoskills { get; } = [];

        [ObservableProperty] private LibraryScriptEntry? _selectedLibraryScript;
        [ObservableProperty] private string _libraryStatus = LibraryService.Status;
        [ObservableProperty] private bool _libraryBusy = LibraryService.IsBusy;

        public bool AutoUpdateLibrary
        {
            get => BotSettings.Current.AutoUpdateLibrary;
            set
            {
                if (BotSettings.Current.AutoUpdateLibrary != value)
                {
                    BotSettings.Current.AutoUpdateLibrary = value;
                    BotSettings.Current.Save();
                    OnPropertyChanged();
                }
            }
        }

        private void AttachLibraryService()
        {
            LibraryService.StateChanged += OnLibraryStateChanged;
            LibraryService.LibraryUpdated += OnLibraryUpdated;
        }

        private void DetachLibraryService()
        {
            LibraryService.StateChanged -= OnLibraryStateChanged;
            LibraryService.LibraryUpdated -= OnLibraryUpdated;
        }

        private void OnLibraryStateChanged()
        {
            LibraryBusy = LibraryService.IsBusy;
            LibraryStatus = LibraryService.Status;
        }

        private void OnLibraryUpdated(string gameDirectory)
        {
            if (!string.Equals(gameDirectory, GameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RefreshLibraryLists();
            LibraryStatus = $"{LibraryService.Status} {LibraryScripts.Count} script(s), {LibraryAutoskills.Count} autoskill(s).";
            // The game may have booted (and read the old library) before the
            // pipe connected — in that case reload as soon as it does.
            _libraryReloadPending = true;
            SendPendingLibraryReload();
        }

        private bool _libraryReloadPending;

        // Called on update and on (re)connect.
        private void SendPendingLibraryReload()
        {
            if (_libraryReloadPending && IsConnected)
            {
                _libraryReloadPending = false;
                _connection.SendCommand("ReloadLibrary", null);
            }
        }

        [RelayCommand]
        private Task UpdateLibrary()
        {
            return LibraryService.UpdateAsync(GameDirectory);
        }

        // Re-scan the extracted tree. Safe to call when nothing's downloaded yet
        // (empty lists). Must run on the UI thread (touches ObservableCollections).
        public void RefreshLibraryLists()
        {
            string? prevSelection = SelectedLibraryScript?.Name;
            LibraryScripts.Clear();
            LibraryAutoskills.Clear();

            string bundled = BotStorage.Prepare(GameDirectory);
            if (Directory.Exists(bundled))
                foreach (string file in Directory.GetFiles(bundled, "*.json").OrderBy(f => f)) AddLibraryScripts(file);
            string? root = LibraryService.Root(GameDirectory);
            if (root == null || !Directory.Exists(root))
            {
                return;
            }

            try
            {
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
                        if (name.Length > 0 && !LibraryAutoskills.Contains(name))
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

            SelectedLibraryScript = LibraryScripts.FirstOrDefault(s => s.Name == prevSelection);
        }

        // One list row per chain in the file (a file may hold several). Mirrors
        // the agent's parse in QuestChains so the list matches what will run.
        private void AddLibraryScripts(string file)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(File.ReadAllText(file));
            }
            catch (Exception)
            {
                return; // the agent logs the parse error; just leave it out here
            }

            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Name.StartsWith('_'))
                {
                    continue;
                }

                JObject? form = prop.Value as JObject;
                JArray? entries = prop.Value as JArray ?? form?["entries"] as JArray;
                if (entries == null && form?["farm"] is not JArray && form?["options"] is not JArray && form?["buy"] is not JObject)
                {
                    continue;
                }
                entries ??= [];

                if (LibraryScripts.Any(s => s.FullPath == file && s.Name == prop.Name)) continue;
                LibraryScripts.Add(new LibraryScriptEntry
                {
                    Name = prop.Name,
                    File = Path.GetFileName(file),
                    FullPath = file,
                    Class = (string?)form?["class"] ?? "",
                    Description = (string?)form?["description"] ?? "",
                    Steps = entries.Count(t => ((int?)t["qid"] ?? 0) > 0),
                    Template = (string?)form?["template"] ?? "quest-chain",
                    Choices = (form?["options"] as JArray)?.Count ?? 0,
                    FarmSteps = (form?["farm"] as JArray)?.Count(t => ((int?)t["qid"] ?? 0) > 0 || t["hunt"] != null) ?? 0
                });
            }
        }

        // Skua's script repo "Load": make it the loaded script in the Load Script window.
        [RelayCommand]
        private void LoadLibraryScript(LibraryScriptEntry? script)
        {
            script ??= SelectedLibraryScript;
            if (script != null && LoadScriptFile(script.FullPath, script.Name))
            {
                LibraryStatus = $"Loaded '{script.Name}' — press Start Script.";
            }
        }

        // One click (or double-click): load it and start it with the current options.
        [RelayCommand]
        private async Task StartLibraryScript(LibraryScriptEntry? script)
        {
            script ??= SelectedLibraryScript;
            if (script == null)
            {
                return;
            }

            if (!IsConnected)
            {
                LibraryStatus = "Start a session first — the game must be running.";
                return;
            }

            if (IsChainRunning)
            {
                LibraryStatus = "A script is already running — stop it first.";
                return;
            }

            if (LoadScriptFile(script.FullPath, script.Name))
            {
                await StartLoadedScript();
            }
        }
    }
}
