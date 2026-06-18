using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.ViewModels
{
    // SnifferWindow: live capture of client/server packets with a JSON preview.
    public partial class MainWindowViewModel
    {
        private bool _snifferServerActive;
        public bool SnifferServerActive
        {
            get => _snifferServerActive;
            set => UpdateSetting(ref _snifferServerActive, value, "snifferServerActive");
        }

        private bool _snifferClientActive;
        public bool SnifferClientActive
        {
            get => _snifferClientActive;
            set => UpdateSetting(ref _snifferClientActive, value, "snifferClientActive");
        }

        public ObservableCollection<SniffPacketEntry> SnifferLogs { get; } = new ObservableCollection<SniffPacketEntry>();

        [ObservableProperty] private SniffPacketEntry? _selectedSnifferEntry;

        private void OnSniffedPacketReceived(string direction, string cmd, string typeName, string raw)
        {
            var entry = new SniffPacketEntry
            {
                Direction = direction,
                Cmd = cmd,
                TypeName = typeName,
                Raw = raw,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
            SnifferLogs.Insert(0, entry);
            if (SnifferLogs.Count > 100) SnifferLogs.RemoveAt(SnifferLogs.Count - 1);
        }

        [RelayCommand]
        private void ClearSnifferLogs()
        {
            SnifferLogs.Clear();
        }
    }
}
