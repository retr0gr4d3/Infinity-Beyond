using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.ViewModels;
using System;
using System.IO;

namespace Launcher.Views
{
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow()
        {
            InitializeComponent();
            
            SaveBtn.Click += OnSaveClick;
            ClearBtn.Click += OnClearClick;
            CloseBtn.Click += OnCloseClick;
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.GeneralLogs.Clear();
            }
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            try
            {
                var files = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save Event Log",
                    DefaultExtension = "txt",
                    SuggestedStartLocation = await StorageProvider.TryGetWellKnownFolderAsync(Avalonia.Platform.Storage.WellKnownFolder.Documents),
                    SuggestedFileName = $"event_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                });
                if (files != null)
                {
                    using var stream = await files.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    foreach (var line in vm.GeneralLogs)
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            }
            catch
            {
                // Fallback to documents folder if storage provider throws/fails
                try
                {
                    string fallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"event_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllLines(fallbackPath, vm.GeneralLogs);
                }
                catch { }
            }
        }
    }
}
