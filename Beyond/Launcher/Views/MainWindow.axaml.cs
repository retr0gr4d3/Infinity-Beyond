using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

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
            new AboutWindow().ShowDialog(this);
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