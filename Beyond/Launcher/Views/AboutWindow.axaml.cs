using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Launcher.Views
{
    public partial class AboutWindow : Window
    {
        private const string RepoUrl = "https://github.com/retr0gr4d3/Infinity-Beyond";

        public AboutWindow()
        {
            InitializeComponent();

            var asm = Assembly.GetExecutingAssembly();

            var version = asm.GetName().Version;
            if (version != null && this.FindControl<TextBlock>("VersionText") is { } versionText)
            {
                versionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
            }

            var buildDate = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                               .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
            if (!string.IsNullOrEmpty(buildDate) && this.FindControl<TextBlock>("BuildDateText") is { } buildText)
            {
                buildText.Text = $"Built {buildDate}";
            }
        }

        private void OpenGitHubClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = RepoUrl, UseShellExecute = true });
            }
            catch
            {
                // Ignore: no default browser / launch blocked.
            }
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnDragPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }
    }
}
