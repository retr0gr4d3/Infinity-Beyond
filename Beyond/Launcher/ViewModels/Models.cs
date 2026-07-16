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
        public int Qid
        {
            get;
            set => SetProperty(ref field, value);
        } = 1;
        public string Area
        {
            get;
            set => SetProperty(ref field, value);
        } = "";
        public string Frame
        {
            get;
            set => SetProperty(ref field, value);
        } = "";
        public string Pad
        {
            get;
            set => SetProperty(ref field, value);
        } = "Spawn";
        public int Items
        {
            get;
            set => SetProperty(ref field, value);
        } = 1;
        // Target monster names/ids, comma-separated ("Wyvern, 206"). Empty = any
        // hostile in frame. Names let the same chain work on live AQW where the
        // client never sees the server's kill-credit ids.
        public string Mon
        {
            get;
            set => SetProperty(ref field, value);
        } = "";
    }
}
