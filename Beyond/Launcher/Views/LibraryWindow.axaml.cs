using Avalonia.Controls;
using Launcher.ViewModels;

namespace Launcher.Views
{
    public partial class LibraryWindow : Window
    {
        public LibraryWindow()
        {
            InitializeComponent();
            // Re-scan the on-disk library each time the window opens so it reflects
            // whatever's already been pulled (or nothing, before the first update).
            Opened += (_, _) => (DataContext as MainWindowViewModel)?.RefreshLibraryLists();
        }
    }
}
