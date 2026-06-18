using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Launcher.Views
{
    public partial class MessageWindow : Window
    {
        public MessageWindow()
        {
            InitializeComponent();
        }

        public MessageWindow(string title, string message) : this()
        {
            Title = title;
            if (this.FindControl<TextBlock>("MessageText") is { } text)
            {
                text.Text = message;
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e) => Close();
    }
}
