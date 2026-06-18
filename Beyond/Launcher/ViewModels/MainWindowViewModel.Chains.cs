using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // ChainEditorWindow (+ the chain runner in QuestRunnerWindow): build, run,
    // import and export quest chains.
    public partial class MainWindowViewModel
    {
        public ObservableCollection<string> ChainNames { get; } = new();
        public System.Collections.Generic.Dictionary<string, JArray> ChainDetails { get; } = new();

        [ObservableProperty] private string? _selectedChainName;

        [ObservableProperty] private string _chainEditorName = "NewChain";
        public ObservableCollection<ChainEntryViewModel> ChainEditorEntries { get; } = new();

        [ObservableProperty] private string _chainImportExportText = "";

        [RelayCommand]
        private void RunChain()
        {
            if (!string.IsNullOrEmpty(SelectedChainName))
            {
                _connection.SendCommand("RunChain", new JObject { ["Name"] = SelectedChainName });
            }
        }

        [RelayCommand]
        private void LoadChainForEditing()
        {
            if (!string.IsNullOrEmpty(SelectedChainName) && ChainDetails.TryGetValue(SelectedChainName, out var arr))
            {
                ChainEditorName = SelectedChainName;
                ChainEditorEntries.Clear();
                foreach (var t in arr)
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
            if (string.IsNullOrWhiteSpace(ChainEditorName)) return;
            JArray entries = new JArray();
            foreach (var ent in ChainEditorEntries)
            {
                if (ent.Qid <= 0) continue;
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
            if (string.IsNullOrWhiteSpace(ChainEditorName)) return;
            _connection.SendCommand("DeleteChain", new JObject { ["Name"] = ChainEditorName.Trim() });
        }

        [RelayCommand]
        private void ExportChainJson()
        {
            if (string.IsNullOrWhiteSpace(ChainEditorName)) return;
            JArray entries = new JArray();
            foreach (var ent in ChainEditorEntries)
            {
                if (ent.Qid <= 0) continue;
                entries.Add(new JObject
                {
                    ["qid"] = ent.Qid,
                    ["area"] = ent.Area?.Trim() ?? "",
                    ["frame"] = ent.Frame?.Trim() ?? "",
                    ["pad"] = string.IsNullOrWhiteSpace(ent.Pad) ? "Spawn" : ent.Pad.Trim(),
                    ["items"] = ent.Items < 1 ? 1 : ent.Items
                });
            }
            JObject obj = new JObject
            {
                [ChainEditorName] = entries
            };
            ChainImportExportText = obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        [RelayCommand]
        private void ImportChainJson()
        {
            if (string.IsNullOrWhiteSpace(ChainImportExportText)) return;
            try
            {
                JObject obj = JObject.Parse(ChainImportExportText);
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value is JArray arr)
                    {
                        ChainEditorName = prop.Name;
                        ChainEditorEntries.Clear();
                        foreach (var t in arr)
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

        private void OnQuestChainsReceived(JObject chains)
        {
            ChainNames.Clear();
            ChainDetails.Clear();
            if (chains != null)
            {
                foreach (var prop in chains.Properties())
                {
                    ChainNames.Add(prop.Name);
                    if (prop.Value is JArray arr)
                    {
                        ChainDetails[prop.Name] = arr;
                    }
                }
            }
        }
    }
}
