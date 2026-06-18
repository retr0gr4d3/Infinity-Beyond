using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;
using Launcher.ViewModels;

namespace Launcher.Views
{
    public partial class ConfiguratorView : UserControl
    {
        public ConfiguratorView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (DataContext is ConfiguratorViewModel vm)
                {
                    vm.OnRequestFolderBrowse = BrowseFolderAsync;
                    vm.OnShowWarning = ShowWarning;
                }
            };
        }

        private async Task<string?> BrowseFolderAsync()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select AdventureQuest Worlds Infinity Installation Directory",
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }

        private void ShowWarning(string message)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                new MessageWindow("Warning", message).ShowDialog(owner);
            }
        }
    }
}
