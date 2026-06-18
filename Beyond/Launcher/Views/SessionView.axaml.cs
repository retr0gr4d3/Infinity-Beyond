using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;

namespace Launcher.Views
{
    public partial class SessionView : UserControl
    {
        public SessionView()
        {
            InitializeComponent();
            
            var sidebar = this.FindControl<Border>("Sidebar");
            var popup = this.FindControl<Popup>("CollapseButtonPopup");
            if (sidebar != null && popup != null)
            {
                sidebar.PropertyChanged += (sender, args) =>
                {
                    if (args.Property == Border.IsVisibleProperty)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (popup.IsOpen)
                            {
                                popup.IsOpen = false;
                                popup.IsOpen = true;
                            }
                        }, DispatcherPriority.Render);
                    }
                };
            }
        }
    }
}
