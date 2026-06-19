using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // SnifferWindow: live capture of client/server packets with a JSON preview.
    public partial class MainWindowViewModel
    {
        public bool SnifferServerActive
        {
            get;
            set => UpdateSetting(ref field, value, "snifferServerActive");
        }
        public bool SnifferClientActive
        {
            get;
            set => UpdateSetting(ref field, value, "snifferClientActive");
        }

        public ObservableCollection<SniffPacketEntry> SnifferLogs { get; } = [];

        [ObservableProperty] private SniffPacketEntry? _selectedSnifferEntry;

        private void OnSniffedPacketReceived(string direction, string cmd, string typeName, string raw)
        {
            SniffPacketEntry entry = new()
            {
                Direction = direction,
                Cmd = cmd,
                TypeName = typeName,
                Raw = raw,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
            SnifferLogs.Insert(0, entry);
            if (SnifferLogs.Count > 100)
            {
                SnifferLogs.RemoveAt(SnifferLogs.Count - 1);
            }
        }

        [RelayCommand]
        private void ClearSnifferLogs()
        {
            SnifferLogs.Clear();
        }
    }
}
