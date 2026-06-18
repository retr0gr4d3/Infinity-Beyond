using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // ReceiverWindow: inject a server response packet (S2C) locally.
    public partial class MainWindowViewModel
    {
        [ObservableProperty] private string _packetToInject = "";

        [RelayCommand]
        private void InjectPacket()
        {
            if (!string.IsNullOrWhiteSpace(PacketToInject))
            {
                _connection.SendCommand("InjectPacket", new JObject { ["Packet"] = PacketToInject });
            }
        }
    }
}
