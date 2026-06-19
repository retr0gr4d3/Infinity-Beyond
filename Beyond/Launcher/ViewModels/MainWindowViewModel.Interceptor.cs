using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // InterceptorWindow: block and log inbound server packets before dispatch.
    public partial class MainWindowViewModel
    {
        public bool InterceptActive
        {
            get;
            set => UpdateSetting(ref field, value, "interceptActive");
        }
        public bool InterceptorLoggingActive
        {
            get;
            set => UpdateSetting(ref field, value, "interceptorLoggingActive");
        }

        public ObservableCollection<InterceptPacketEntry> InterceptorLogs { get; } = [];

        private void OnInterceptedPacketReceived(string action, string typeName, string cmd, string logEntry)
        {
            InterceptPacketEntry entry = new()
            {
                Action = action,
                TypeName = typeName,
                Cmd = cmd,
                LogEntry = logEntry,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
            InterceptorLogs.Insert(0, entry);
            if (InterceptorLogs.Count > 100)
            {
                InterceptorLogs.RemoveAt(InterceptorLogs.Count - 1);
            }
        }

        [RelayCommand]
        private void ClearInterceptorLogs()
        {
            InterceptorLogs.Clear();
        }
    }
}
