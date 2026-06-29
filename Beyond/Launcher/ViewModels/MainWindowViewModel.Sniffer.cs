using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
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

        // Comma-separated Cmd or type names to drop from the sniffer view,
        // separated by direction (e.g. C2S "RequestAttackInput", S2C
        // "ResponseAttack"). Display-only — the always-on packets.jsonl disk
        // capture stays complete. Committed via ApplySnifferFilter.
        [ObservableProperty] private string _ignoredClientPacketsInput = "";
        [ObservableProperty] private string _ignoredServerPacketsInput = "";

        private readonly HashSet<string> _ignoredClient = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignoredServer = new(StringComparer.OrdinalIgnoreCase);

        private static void ParseIgnoreList(string value, HashSet<string> into)
        {
            into.Clear();
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (string p in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                into.Add(p);
            }
        }

        private bool IsIgnored(string direction, string cmd, string typeName)
        {
            HashSet<string> set = direction == "c2s" ? _ignoredClient : _ignoredServer;
            return set.Count > 0 && (set.Contains(cmd) || set.Contains(typeName));
        }

        [RelayCommand]
        private void ApplySnifferFilter()
        {
            ParseIgnoreList(IgnoredClientPacketsInput, _ignoredClient);
            ParseIgnoreList(IgnoredServerPacketsInput, _ignoredServer);

            // Retroactively drop entries already in the view that now match.
            for (int i = SnifferLogs.Count - 1; i >= 0; i--)
            {
                if (IsIgnored(SnifferLogs[i].Direction, SnifferLogs[i].Cmd, SnifferLogs[i].TypeName))
                {
                    SnifferLogs.RemoveAt(i);
                }
            }
        }

        private void OnSniffedPacketReceived(string direction, string cmd, string typeName, string raw)
        {
            if (IsIgnored(direction, cmd, typeName))
            {
                return;
            }

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
