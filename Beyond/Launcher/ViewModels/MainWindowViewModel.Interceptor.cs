using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.ViewModels
{
    // InterceptorWindow: block and log inbound server packets before dispatch.
    public partial class MainWindowViewModel
    {
        private bool _interceptActive;
        public bool InterceptActive
        {
            get => _interceptActive;
            set => UpdateSetting(ref _interceptActive, value, "interceptActive");
        }

        private bool _interceptorLoggingActive;
        public bool InterceptorLoggingActive
        {
            get => _interceptorLoggingActive;
            set => UpdateSetting(ref _interceptorLoggingActive, value, "interceptorLoggingActive");
        }

        public ObservableCollection<InterceptPacketEntry> InterceptorLogs { get; } = new ObservableCollection<InterceptPacketEntry>();

        private void OnInterceptedPacketReceived(string action, string typeName, string cmd, string logEntry)
        {
            var entry = new InterceptPacketEntry
            {
                Action = action,
                TypeName = typeName,
                Cmd = cmd,
                LogEntry = logEntry,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
            InterceptorLogs.Insert(0, entry);
            if (InterceptorLogs.Count > 100) InterceptorLogs.RemoveAt(InterceptorLogs.Count - 1);
        }

        [RelayCommand]
        private void ClearInterceptorLogs()
        {
            InterceptorLogs.Clear();
        }
    }
}
