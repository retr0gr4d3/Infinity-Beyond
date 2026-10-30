using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // ChainEditorWindow (+ the chain runner in QuestRunnerWindow, the Library's
    // Start button and the control panel's Bot card): build, run, import and
    // export quest chains, and track the running chain's progress.
    public partial class MainWindowViewModel
    {
        public ObservableCollection<string> ChainNames { get; } = [];
        public System.Collections.Generic.Dictionary<string, JArray> ChainDetails { get; } = [];

        // Chain name -> recommended class/skillset (from a script's object form).
        public System.Collections.Generic.Dictionary<string, string> ChainClasses { get; } = [];

        // Chain name -> repeatable quests farmed after the chain (object form "farm").
        public System.Collections.Generic.Dictionary<string, JArray> ChainFarms { get; } = [];

        // Chain name -> "bootstrap" / "library" / "user" (which file won).
        public System.Collections.Generic.Dictionary<string, string> ChainSources { get; } = [];

        // Picker shown on the chain tab: "(none)" + the user's saved skillset names.
        public const string NoChainClass = "(none)";
        public ObservableCollection<string> ChainSkillsetOptions { get; } = [NoChainClass];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedChainSummary))]
        private string? _selectedChainName;

        [ObservableProperty] private string? _selectedChainClass = NoChainClass;

        private bool _isRefreshingChains;

        // When a chain is picked, default the class to its recommended skillset (if any).
        partial void OnSelectedChainNameChanged(string? value)
        {
            if (_isRefreshingChains)
            {
                return;
            }

            if (value != null && ChainClasses.TryGetValue(value, out string? cls) && !string.IsNullOrEmpty(cls))
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
        }

        // One-line description of the picked chain for the run controls.
        public string SelectedChainSummary
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedChainName) || !ChainDetails.TryGetValue(SelectedChainName, out JArray? arr))
                {
                    return "";
                }

                int farm = ChainFarms.TryGetValue(SelectedChainName, out JArray? f) ? f.Count : 0;
                string s = $"{arr.Count} quest{(arr.Count == 1 ? "" : "s")}";
                s += farm > 0 ? $" · then farms {farm}" : " · no farm quests";
                if (ChainSources.TryGetValue(SelectedChainName, out string? src) && src == "library")
                {
                    s += " · library";
                }
                return s;
            }
        }

        // Rebuild the skillset picker from SavedSkillsets (called when status updates).
        public void RefreshChainSkillsetOptions()
        {
            string? prev = SelectedChainClass;
            ChainSkillsetOptions.Clear();
            ChainSkillsetOptions.Add(NoChainClass);
            foreach (SkillsetEntry s in SavedSkillsets)
            {
                if (!string.IsNullOrEmpty(s.Name) && !ChainSkillsetOptions.Contains(s.Name))
                {
                    ChainSkillsetOptions.Add(s.Name);
                }
            }
            SelectedChainClass = (prev != null && ChainSkillsetOptions.Contains(prev)) ? prev : NoChainClass;
        }

        // --- Farm options (per session, seeded from and saved to BotSettings) ---
        [ObservableProperty] private bool _farmAfterChain = BotSettings.Current.FarmAfterChain;

        // Farm passes after the chain; 0 / blank = until stopped.
        [ObservableProperty] private string _farmLoops = BotSettings.Current.FarmLoops.ToString();

        partial void OnFarmAfterChainChanged(bool value)
        {
            BotSettings.Current.FarmAfterChain = value;
            BotSettings.Current.Save();
        }

        partial void OnFarmLoopsChanged(string value)
        {
            BotSettings.Current.FarmLoops = ParseFarmLoops();
            BotSettings.Current.Save();
        }

        private int ParseFarmLoops()
        {
            return int.TryParse(FarmLoops, out int n) && n > 0 ? n : 0;
        }

        // --- Runner progress (from the agent's QuestRunnerState messages) ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChainToggleText))]
        private bool _isChainRunning;

        [ObservableProperty] private string _chainRunStatus = "";

        public string ChainToggleText => IsChainRunning ? "Stop" : "Start";

        private void OnQuestRunnerStateReceived(JObject msg)
        {
            IsChainRunning = (bool?)msg["Running"] ?? false;
            UpdateScriptStatus(msg);
            string chain = (string?)msg["Chain"] ?? "";
            int index = ((int?)msg["Index"] ?? 0) + 1;
            int count = (int?)msg["Count"] ?? 0;

            if (IsChainRunning)
            {
                if ((string?)msg["Phase"] is "shop" or "buy" or "discovery")
                {
                    ChainRunStatus = $"{chain} — {((string?)msg["Phase"] == "shop" ? "loading merge shop" : (string?)msg["Phase"] == "discovery" ? ((string?)msg["StatusLine"] ?? "discovering material sources") : "buying items")}";
                }
                else if (chain.Length == 0)
                {
                    ChainRunStatus = $"Running quest {(int?)msg["QuestID"]}";
                }
                else if ((bool?)msg["Farming"] == true)
                {
                    int pass = (int?)msg["FarmPass"] ?? 0;
                    int loops = (int?)msg["FarmLoops"] ?? 0;
                    ChainRunStatus = $"Farming {chain} — pass {pass}{(loops > 0 ? $"/{loops}" : "")}, target {index}/{count}";
                }
                else
                {
                    ChainRunStatus = $"Running {chain} — quest {index}/{count}";
                }
                string goal = (string?)msg["Goal"] ?? "";
                if (goal.Length > 0 && (string?)msg["Phase"] is not ("shop" or "buy" or "discovery"))
                {
                    ChainRunStatus += $" — {goal}";
                }
                return;
            }

            ChainRunStatus = (string?)msg["State"] switch
            {
                "Done" => "Finished.",
                "Failed" => $"Stopped: {(string?)msg["LastError"]}",
                _ => ""
            };
        }

        [ObservableProperty] private string _chainEditorName = "NewChain";
        public ObservableCollection<ChainEntryViewModel> ChainEditorEntries { get; } = [];

        [ObservableProperty] private string _chainImportExportText = "";

        [RelayCommand]
        private void RunChain()
        {
            if (string.IsNullOrEmpty(SelectedChainName))
            {
                return;
            }

            JObject p = new()
            {
                ["Name"] = SelectedChainName,
                ["Farm"] = FarmAfterChain,
                ["FarmLoops"] = ParseFarmLoops()
            };
            if (!string.IsNullOrEmpty(SelectedChainClass) && SelectedChainClass != NoChainClass)
            {
                p["Skillset"] = SelectedChainClass;
            }
            QuestRunnerLogs.Clear();
            _connection.SendCommand("RunChain", p);

            BotSettings.Current.LastChain = SelectedChainName;
            BotSettings.Current.Save();
        }

        [RelayCommand]
        private void StopChain()
        {
            // Chains run on the shared quest runner — stopping it cancels the chain.
            _connection.SendCommand("StopQuestRunner", null);
        }

        // Single Start/Stop button (control panel Bot card).
        [RelayCommand]
        private void ToggleChain()
        {
            if (IsChainRunning)
            {
                StopChain();
            }
            else
            {
                RunChain();
            }
        }

        [RelayCommand]
        private void LoadChainForEditing()
        {
            if (!string.IsNullOrEmpty(SelectedChainName) && ChainDetails.TryGetValue(SelectedChainName, out JArray? arr))
            {
                ChainEditorName = SelectedChainName;
                ChainFarms.TryGetValue(SelectedChainName, out JArray? farm);
                LoadEditorEntries(arr, farm);
            }
        }

        // Story entries first, then the farm entries (flagged Farm).
        private void LoadEditorEntries(JArray entries, JArray? farm)
        {
            ChainEditorEntries.Clear();
            foreach (JToken t in entries)
            {
                ChainEditorEntries.Add(EntryFromJson(t, isFarm: false));
            }
            if (farm != null)
            {
                foreach (JToken t in farm)
                {
                    ChainEditorEntries.Add(EntryFromJson(t, isFarm: true));
                }
            }
        }

        // "mon" round-trip: stored as an array of name/id strings, edited as one
        // comma-joined text field.
        private static ChainEntryViewModel EntryFromJson(JToken t, bool isFarm)
        {
            string mon = t["mon"] is JArray monArr
                ? string.Join(", ", monArr.Values<string>())
                : (string?)t["mon"] ?? "";
            return new ChainEntryViewModel
            {
                Qid = (int?)t["qid"] ?? 0,
                Area = (string?)t["area"] ?? "",
                Frame = (string?)t["frame"] ?? "",
                Pad = (string?)t["pad"] ?? "Spawn",
                Items = (int?)t["items"] ?? (int?)t["iters"] ?? 1,
                Mon = mon,
                Farm = isFarm
            };
        }

        private static JObject EntryToJson(ChainEntryViewModel ent)
        {
            JObject o = new()
            {
                ["qid"] = ent.Qid,
                ["area"] = ent.Area?.Trim() ?? "",
                ["frame"] = ent.Frame?.Trim() ?? "",
                ["pad"] = string.IsNullOrWhiteSpace(ent.Pad) ? "Spawn" : ent.Pad.Trim(),
                ["items"] = ent.Items < 1 ? 1 : ent.Items
            };
            string[] mons = (ent.Mon ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (mons.Length > 0)
            {
                o["mon"] = new JArray(mons);
            }
            return o;
        }

        // Editor rows -> (story entries, farm entries), skipping rows without a qid.
        private (JArray Entries, JArray Farm) EditorEntriesToJson()
        {
            JArray entries = [];
            JArray farm = [];
            foreach (ChainEntryViewModel ent in ChainEditorEntries)
            {
                if (ent.Qid <= 0)
                {
                    continue;
                }

                (ent.Farm ? farm : entries).Add(EntryToJson(ent));
            }
            return (entries, farm);
        }

        [RelayCommand]
        private void AddChainEditorEntry()
        {
            ChainEditorEntries.Add(new ChainEntryViewModel());
        }

        [RelayCommand]
        private void RemoveChainEditorEntry(ChainEntryViewModel entry)
        {
            ChainEditorEntries.Remove(entry);
        }

        [RelayCommand]
        private void SaveEditedChain()
        {
            if (string.IsNullOrWhiteSpace(ChainEditorName))
            {
                return;
            }

            string name = ChainEditorName.Trim();
            (JArray entries, JArray farm) = EditorEntriesToJson();
            JObject save = new()
            {
                ["Name"] = name,
                ["Entries"] = entries,
                ["Farm"] = farm
            };
            // Keep a library chain's recommended class when saving an edited copy.
            if (ChainClasses.TryGetValue(name, out string? cls))
            {
                save["Class"] = cls;
            }
            _connection.SendCommand("SaveChain", save);
        }

        [RelayCommand]
        private void DeleteEditedChain()
        {
            if (string.IsNullOrWhiteSpace(ChainEditorName))
            {
                return;
            }

            _connection.SendCommand("DeleteChain", new JObject { ["Name"] = ChainEditorName.Trim() });
        }

        [RelayCommand]
        private void ExportChainJson()
        {
            if (string.IsNullOrWhiteSpace(ChainEditorName))
            {
                return;
            }

            (JArray entries, JArray farm) = EditorEntriesToJson();
            ChainClasses.TryGetValue(ChainEditorName, out string? cls);
            JToken chain = entries;
            if (farm.Count > 0 || !string.IsNullOrEmpty(cls))
            {
                JObject form = [];
                if (!string.IsNullOrEmpty(cls))
                {
                    form["class"] = cls;
                }
                form["entries"] = entries;
                if (farm.Count > 0)
                {
                    form["farm"] = farm;
                }
                chain = form;
            }
            JObject obj = new()
            {
                [ChainEditorName] = chain
            };
            ChainImportExportText = obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        [RelayCommand]
        private void ImportChainJson()
        {
            if (string.IsNullOrWhiteSpace(ChainImportExportText))
            {
                return;
            }

            try
            {
                JObject obj = JObject.Parse(ChainImportExportText);
                foreach (JProperty prop in obj.Properties())
                {
                    // Bare entries array, or the object form with entries/farm.
                    JObject? form = prop.Value as JObject;
                    JArray? entries = prop.Value as JArray ?? form?["entries"] as JArray;
                    if (entries != null)
                    {
                        ChainEditorName = prop.Name;
                        LoadEditorEntries(entries, form?["farm"] as JArray);
                        break;
                    }
                }
            }
            catch (Exception) { }
        }

        private void OnQuestChainsReceived(JObject msg)
        {
            string? prevSelection = SelectedChainName;
            string? prevClass = SelectedChainClass;

            ChainNames.Clear();
            ChainDetails.Clear();
            ChainClasses.Clear();
            ChainFarms.Clear();
            ChainSources.Clear();
            if (msg["Chains"] is JObject chains)
            {
                foreach (JProperty prop in chains.Properties())
                {
                    ChainNames.Add(prop.Name);
                    if (prop.Value is JArray arr)
                    {
                        ChainDetails[prop.Name] = arr;
                    }
                }
            }
            if (msg["ChainClasses"] is JObject classes)
            {
                foreach (JProperty prop in classes.Properties())
                {
                    string? cls = (string?)prop.Value;
                    if (!string.IsNullOrEmpty(cls))
                    {
                        ChainClasses[prop.Name] = cls;
                    }
                }
            }
            if (msg["ChainFarms"] is JObject farms)
            {
                foreach (JProperty prop in farms.Properties())
                {
                    if (prop.Value is JArray arr && arr.Count > 0)
                    {
                        ChainFarms[prop.Name] = arr;
                    }
                }
            }
            if (msg["ChainSources"] is JObject sources)
            {
                foreach (JProperty prop in sources.Properties())
                {
                    ChainSources[prop.Name] = (string?)prop.Value ?? "";
                }
            }

            if (prevSelection != null && ChainNames.Contains(prevSelection))
            {
                _isRefreshingChains = true;
                SelectedChainName = prevSelection;
                if (prevClass != null && ChainSkillsetOptions.Contains(prevClass))
                {
                    SelectedChainClass = prevClass;
                }
                _isRefreshingChains = false;
            }
            else if (ChainNames.Contains(BotSettings.Current.LastChain))
            {
                // First catalog of the session: preselect the last script run, so
                // Start is the only click needed.
                SelectedChainName = BotSettings.Current.LastChain;
            }
            OnPropertyChanged(nameof(SelectedChainSummary));
        }
    }
}
