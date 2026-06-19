using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // QuestLoaderWindow + QuestRunnerWindow: single quest load/accept/turn-in, the
    // looping quest runner, and the quest directory that feeds the pickers.
    public partial class MainWindowViewModel
    {
        // --- Quest loader ---
        [ObservableProperty] private string _questId = "";

        [RelayCommand]
        private void LoadQuest()
        {
            if (int.TryParse(QuestId, out int id))
            {
                _connection.SendCommand("LoadQuest", new JObject { ["QuestId"] = id });
            }
        }

        [RelayCommand]
        private void AcceptQuest()
        {
            if (int.TryParse(QuestId, out int id))
            {
                _connection.SendCommand("AcceptQuest", new JObject { ["QuestId"] = id });
            }
        }

        [RelayCommand]
        private void TurnInQuest()
        {
            if (int.TryParse(QuestId, out int id))
            {
                _connection.SendCommand("TurnInQuest", new JObject { ["QuestId"] = id });
            }
        }

        // --- Quest runner ---
        [ObservableProperty] private string _runnerQuestId = "1";
        [ObservableProperty] private string _runnerIters = "10";
        [ObservableProperty] private string _runnerArea = "";
        [ObservableProperty] private string _runnerFrame = "";
        [ObservableProperty] private string _runnerPad = "Spawn";

        public ObservableCollection<string> QuestRunnerLogs { get; } = [];

        [RelayCommand]
        private void StartQuestRunner()
        {
            if (int.TryParse(RunnerQuestId, out int qid) && int.TryParse(RunnerIters, out int iters))
            {
                QuestRunnerLogs.Clear();
                _connection.SendCommand("StartQuestRunner", new JObject
                {
                    ["QuestId"] = qid,
                    ["Iters"] = iters,
                    ["Area"] = RunnerArea?.Trim() ?? "",
                    ["Frame"] = RunnerFrame?.Trim() ?? "",
                    ["Pad"] = string.IsNullOrWhiteSpace(RunnerPad) ? "Spawn" : RunnerPad.Trim()
                });
            }
        }

        [RelayCommand]
        private void StopQuestRunner()
        {
            _connection.SendCommand("StopQuestRunner", null);
        }

        private void OnQuestRunnerLogReceived(string message)
        {
            QuestRunnerLogs.Insert(0, message);
            if (QuestRunnerLogs.Count > 200)
            {
                QuestRunnerLogs.RemoveAt(QuestRunnerLogs.Count - 1);
            }
        }

        // --- Quest directory ---
        public ObservableCollection<QuestDirectoryEntry> QuestDirectory { get; } = [];

        [ObservableProperty] private QuestDirectoryEntry? _selectedQuestDirectory;

        partial void OnSelectedQuestDirectoryChanged(QuestDirectoryEntry? value) { if (value != null) { QuestId = value.Id.ToString(); RunnerQuestId = value.Id.ToString(); } }

        private void OnQuestDirectoryReceived(JObject msg)
        {
            QuestDirectory.Clear();
            if (msg["Quests"] is JArray arr)
            {
                foreach (JToken t in arr)
                {
                    QuestDirectory.Add(new QuestDirectoryEntry
                    {
                        Id = (int?)t["id"] ?? 0,
                        Name = (string?)t["name"] ?? ""
                    });
                }
            }
        }
    }
}
