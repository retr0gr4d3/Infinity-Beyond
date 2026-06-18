using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // ShopLoaderWindow: load a shop by id, optionally force-merging it.
    public partial class MainWindowViewModel
    {
        [ObservableProperty] private string _shopId = "";

        private bool _forceMergeShop;
        public bool ForceMergeShop
        {
            get => _forceMergeShop;
            set => UpdateSetting(ref _forceMergeShop, value, "forceMergeShop");
        }

        [RelayCommand]
        private void LoadShop()
        {
            if (int.TryParse(ShopId, out int id))
            {
                _connection.SendCommand("LoadShop", new JObject { ["ShopId"] = id });
            }
        }

        [RelayCommand]
        private void LoadMerge()
        {
            if (int.TryParse(ShopId, out int id))
            {
                _connection.SendCommand("SetSetting", new JObject { ["Name"] = "forceMergeShop", ["Value"] = true });
                _connection.SendCommand("LoadShop", new JObject { ["ShopId"] = id });
            }
        }
    }
}
