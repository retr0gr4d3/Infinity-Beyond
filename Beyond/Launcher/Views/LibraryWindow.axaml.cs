using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
            // Double-click a script to start it, like Skua's script repo. Only
            // on an item — not the scrollbar or the empty area below the list.
            ScriptList.DoubleTapped += (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
                {
                    (DataContext as MainWindowViewModel)?.StartLibraryScriptCommand.Execute(null);
                }
            };
        }
    }
}
