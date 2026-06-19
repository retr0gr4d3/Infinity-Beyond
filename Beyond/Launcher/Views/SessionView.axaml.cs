using Avalonia.Controls;

namespace Launcher.Views
{
    public partial class SessionView : UserControl
    {
        // The collapse handle is now an in-window Avalonia rail (see SessionView.axaml).
        // It used to be a borderless top-level Window floated over the native Unity host,
        // which showed up as its own capturable window in Discord/OBS — that is gone.
        public SessionView()
        {
            InitializeComponent();
        }
    }
}
