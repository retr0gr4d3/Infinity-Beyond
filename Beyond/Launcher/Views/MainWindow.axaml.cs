using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Runtime.InteropServices;

namespace Launcher.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MinimizeWindowClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeWindowClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseWindowClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutButtonClick(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new();
            // macOS: the embedded game window floats above the launcher (level 1),
            // so like the tool windows the About dialog must be Topmost (level 3)
            // or the game covers it. Not needed on Windows (game reparents into the
            // panel instead of floating).
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                about.Topmost = true;
            }
            about.ShowDialog(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is ViewModels.ShellViewModel shell)
            {
                shell.Shutdown();
            }
            base.OnClosed(e);
        }
    }
}