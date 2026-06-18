using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // SenderWindow: send a raw client command, or build one from cmd + parameters.
    public partial class MainWindowViewModel
    {
        [ObservableProperty] private string _packetToSend = "";

        [RelayCommand]
        private void SendPacket()
        {
            if (!string.IsNullOrWhiteSpace(PacketToSend))
            {
                _connection.SendCommand("SendPacket", new JObject { ["Packet"] = PacketToSend });
            }
        }

        [ObservableProperty] private string _senderCmd = "tfer";
        [ObservableProperty] private string _senderParams = "<charname>,lair,0,Enter,Spawn";
        [ObservableProperty] private bool _senderSingleString;

        [RelayCommand]
        private void SendManuallyInjectedPacket()
        {
            JArray pArr;
            if (SenderSingleString)
            {
                pArr = new JArray { SenderParams };
            }
            else
            {
                pArr = new JArray();
                foreach (var p in SenderParams.Split(','))
                {
                    pArr.Add(p.Trim());
                }
            }
            _connection.SendCommand("SendPacket", new JObject
            {
                ["Cmd"] = SenderCmd,
                ["Params"] = pArr
            });
        }
    }
}
