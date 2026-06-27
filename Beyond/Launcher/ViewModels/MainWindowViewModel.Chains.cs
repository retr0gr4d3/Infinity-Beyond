using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // ChainEditorWindow (+ the chain runner in QuestRunnerWindow): build, run,
    // import and export quest chains.
    public partial class MainWindowViewModel
    {
        public ObservableCollection<string> ChainNames { get; } = [];
        public System.Collections.Generic.Dictionary<string, JArray> ChainDetails { get; } = [];

        // Chain name -> recommended class/skillset (from a script's object form).
        public System.Collections.Generic.Dictionary<string, string> ChainClasses { get; } = [];

        // Picker shown on the chain tab: "(none)" + the user's saved skillset names.
        public const string NoChainClass = "(none)";
        public ObservableCollection<string> ChainSkillsetOptions { get; } = [NoChainClass];

        [ObservableProperty] private string? _selectedChainName;

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

        [ObservableProperty] private string _chainEditorName = "NewChain";
        public ObservableCollection<ChainEntryViewModel> ChainEditorEntries { get; } = [];

        [ObservableProperty] private string _chainImportExportText = "";

        [RelayCommand]
        private void RunChain()
        {
            if (!string.IsNullOrEmpty(SelectedChainName))
            {
                JObject p = new() { ["Name"] = SelectedChainName };
                if (!string.IsNullOrEmpty(SelectedChainClass) && SelectedChainClass != NoChainClass)
                {
                    p["Skillset"] = SelectedChainClass;
                }
                _connection.SendCommand("RunChain", p);
            }
        }

        [RelayCommand]
        private void StopChain()
        {
            // Chains run on the shared quest runner — stopping it cancels the chain.
            _connection.SendCommand("StopQuestRunner", null);
        }

        [RelayCommand]
        private void LoadChainForEditing()
        {
            if (!string.IsNullOrEmpty(SelectedChainName) && ChainDetails.TryGetValue(SelectedChainName, out JArray? arr))
            {
                ChainEditorName = SelectedChainName;
                ChainEditorEntries.Clear();
                foreach (JToken t in arr)
                {
                    ChainEditorEntries.Add(new ChainEntryViewModel
                    {
                        Qid = (int?)t["qid"] ?? 0,
                        Area = (string?)t["area"] ?? "",
                        Frame = (string?)t["frame"] ?? "",
                        Pad = (string?)t["pad"] ?? "Spawn",
                        Items = (int?)t["items"] ?? (int?)t["iters"] ?? 1
                    });
                }
            }
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

            JArray entries = [];
            foreach (ChainEntryViewModel ent in ChainEditorEntries)
            {
                if (ent.Qid <= 0)
                {
                    continue;
                }

                entries.Add(new JObject
                {
                    ["qid"] = ent.Qid,
                    ["area"] = ent.Area?.Trim() ?? "",
                    ["frame"] = ent.Frame?.Trim() ?? "",
                    ["pad"] = string.IsNullOrWhiteSpace(ent.Pad) ? "Spawn" : ent.Pad.Trim(),
                    ["items"] = ent.Items < 1 ? 1 : ent.Items
                });
            }
            _connection.SendCommand("SaveChain", new JObject
            {
                ["Name"] = ChainEditorName.Trim(),
                ["Entries"] = entries
            });
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

            JArray entries = [];
            foreach (ChainEntryViewModel ent in ChainEditorEntries)
            {
                if (ent.Qid <= 0)
                {
                    continue;
                }

                entries.Add(new JObject
                {
                    ["qid"] = ent.Qid,
                    ["area"] = ent.Area?.Trim() ?? "",
                    ["frame"] = ent.Frame?.Trim() ?? "",
                    ["pad"] = string.IsNullOrWhiteSpace(ent.Pad) ? "Spawn" : ent.Pad.Trim(),
                    ["items"] = ent.Items < 1 ? 1 : ent.Items
                });
            }
            JObject obj = new()
            {
                [ChainEditorName] = entries
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
                    if (prop.Value is JArray arr)
                    {
                        ChainEditorName = prop.Name;
                        ChainEditorEntries.Clear();
                        foreach (JToken t in arr)
                        {
                            ChainEditorEntries.Add(new ChainEntryViewModel
                            {
                                Qid = (int?)t["qid"] ?? 0,
                                Area = (string?)t["area"] ?? "",
                                Frame = (string?)t["frame"] ?? "",
                                Pad = (string?)t["pad"] ?? "Spawn",
                                Items = (int?)t["items"] ?? (int?)t["iters"] ?? 1
                            });
                        }
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
        }
    }
}
