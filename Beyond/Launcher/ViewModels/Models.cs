namespace Launcher.ViewModels
{
    // Plain data records exchanged with the game mod and bound by the tool windows.

    public class SniffPacketEntry
    {
        public string Direction { get; set; } = "";
        public string Cmd { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Raw { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Color => Direction == "c2s" ? "#FFB74D" : "#4DD0E1";
        public string DisplayText => $"[{Timestamp}] {(Direction == "c2s" ? "CLIENT" : "SERVER")} {TypeName} ({Cmd})";
    }

    public class InterceptPacketEntry
    {
        public string Action { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Cmd { get; set; } = "";
        public string LogEntry { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Color => Action == "BLOCKED" ? "#E57373" : "#81C784";
        public string DisplayText => $"[{Timestamp}] {LogEntry}";
    }

    public class SkillsetEntry
    {
        public string Name { get; set; } = "";
        public string Combo { get; set; } = "";
        public string Delays { get; set; } = "";
        public string Waits { get; set; } = "";
        public string Frees { get; set; } = "";
        public bool WaitForSkill { get; set; }
    }

    public class CatalogEntry
    {
        public string Name { get; set; } = "";
        public string Bundle { get; set; } = "";
        public string DisplayText => string.IsNullOrEmpty(Name) ? Bundle : $"{Name} ({Bundle})";
    }

    public class JukeboxTrack
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public float Length { get; set; }
        public string DisplayText => $"{Id} - {Name} ({(Length > 0 ? $"{System.Math.Round(Length)}s" : "?")})";
    }

    public class QuestDirectoryEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string DisplayText => $"{Id} - {Name}";
    }

    public class ChainEntryViewModel : ViewModelBase
    {
        private int _qid = 1;
        public int Qid
        {
            get => _qid;
            set => SetProperty(ref _qid, value);
        }

        private string _area = "";
        public string Area
        {
            get => _area;
            set => SetProperty(ref _area, value);
        }

        private string _frame = "";
        public string Frame
        {
            get => _frame;
            set => SetProperty(ref _frame, value);
        }

        private string _pad = "Spawn";
        public string Pad
        {
            get => _pad;
            set => SetProperty(ref _pad, value);
        }

        private int _items = 1;
        public int Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }
    }
}
