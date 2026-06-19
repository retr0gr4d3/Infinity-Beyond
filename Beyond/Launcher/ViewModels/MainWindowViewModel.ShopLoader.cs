using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // ShopLoaderWindow: load a shop by id, optionally force-merging it.
    public partial class MainWindowViewModel
    {
        [ObservableProperty] private string _shopId = "";

        public bool ForceMergeShop
        {
            get;
            set => UpdateSetting(ref field, value, "forceMergeShop");
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
