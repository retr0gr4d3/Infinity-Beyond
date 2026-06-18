using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // FakeDevWindow: spoof membership and access level, and trigger dev actions.
    public partial class MainWindowViewModel
    {
        [ObservableProperty] private int _playerAccessLevel;
        [ObservableProperty] private int _playerUpgradeDays;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MembershipText))]
        private bool _isMember;

        public string MembershipText => IsMember ? "Active Member" : "Non-Member";

        [RelayCommand]
        private void SetAccessLevel(string levelStr)
        {
            if (int.TryParse(levelStr, out int level))
            {
                _connection.SendCommand("SetAccessLevel", new JObject { ["Level"] = level });
            }
        }

        [RelayCommand]
        private void ToggleMembership()
        {
            _connection.SendCommand("SetMembership", new JObject { ["IsMember"] = !IsMember });
        }

        [RelayCommand]
        private void OpenDevUI()
        {
            _connection.SendCommand("OpenDevUI", null);
        }

        [RelayCommand]
        private void ResetFakeDev()
        {
            _connection.SendCommand("ResetFakeDev", null);
        }
    }
}
