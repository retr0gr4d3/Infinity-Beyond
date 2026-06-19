using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // Top-level view-model: hosts N independent sessions (one per account),
    // each a full MainWindowViewModel with its own named-pipe connection and
    // embedded game. Only the selected session's view is shown, but every
    // session stays alive so all games keep running. Broadcast commands fan an
    // action out to every session so accounts can act together.
    public partial class ShellViewModel : ObservableObject
    {
        private int _sessionCounter;

        public ObservableCollection<MainWindowViewModel> Sessions { get; } = [];

        [ObservableProperty] private MainWindowViewModel? _selectedSession;

        [ObservableProperty] private bool _isConfiguratorSelected = true;

        public ConfiguratorViewModel Configurator { get; } = new();

        public bool IsSessionActive => SelectedSession != null;

        public ShellViewModel()
        {
            Configurator.OnLaunchRequested += AddSessionWithCredentials;
            IsConfiguratorSelected = true;
        }

        public void AddSessionWithCredentials(string? user, string? pass, string? nickname = null)
        {
            _sessionCounter++;
            MainWindowViewModel session = new()
            {
                Title = $"Session {_sessionCounter}",
                PresetUsername = user,
                PresetPassword = pass,
                PresetNickname = nickname,
                GameDirectory = Configurator.GameDirectory
            };
            Sessions.Add(session);
            SelectedSession = session;
        }

        [RelayCommand]
        private void AddSession()
        {
            if (!Launcher.GameLocator.Exists(Configurator.GameDirectory))
            {
                Configurator.OnShowWarning?.Invoke(Launcher.GameLocator.NotFoundMessage);
                return;
            }

            AddSessionWithCredentials(null, null, null);
        }

        [RelayCommand]
        private void CloseSession(MainWindowViewModel session)
        {
            if (session == null)
            {
                return;
            }

            int index = Sessions.IndexOf(session);
            session.Shutdown();
            Sessions.Remove(session);

            if (SelectedSession == session)
            {
                // Select a neighbour so the view never goes blank while sessions remain.
                SelectedSession = Sessions.Count > 0
                    ? Sessions[index < Sessions.Count ? index : Sessions.Count - 1]
                    : null;
            }
        }

        // Fan-out: skip the active cutscene on every session at once.
        [RelayCommand]
        private void BroadcastSkipCutscene()
        {
            foreach (MainWindowViewModel s in Sessions)
            {
                s.SendCommandExternal("SkipCutscene");
            }
        }

        partial void OnIsConfiguratorSelectedChanged(bool value)
        {
            if (value)
            {
                SelectedSession = null;
            }
            OnPropertyChanged(nameof(IsSessionActive));
        }

        partial void OnSelectedSessionChanged(MainWindowViewModel? value)
        {
            if (value != null)
            {
                IsConfiguratorSelected = false;
            }
            OnPropertyChanged(nameof(IsSessionActive));
            foreach (var s in Sessions) s.IsSelected = s == value;
        }

        public void Shutdown()
        {
            foreach (MainWindowViewModel s in Sessions)
            {
                s.Shutdown();
            }
        }
    }
}
