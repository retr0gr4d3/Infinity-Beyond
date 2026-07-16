using Avalonia.Controls;
using Launcher.ViewModels;

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

            // macOS overlay-follow embedding: the game host reports its on-screen
            // rect; forward it to the session's view-model, which relays it to the
            // agent over the pipe. Never fires on Windows (that path reparents the
            // native window instead), so the subscription is harmless there.
            GameHost.EmbedGeometryChanged += OnEmbedGeometryChanged;
        }

        private void OnEmbedGeometryChanged(double x, double y, double w, double h, bool visible)
        {
            (DataContext as MainWindowViewModel)?.SendMacEmbed(x, y, w, h, visible);
        }
    }
}
