using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Launcher.ViewModels;
using System;

namespace Launcher.Views
{
    public partial class SessionView : UserControl
    {
        private Window? _parentWindow;
        private MainWindowViewModel? _viewModel;
        private Window? _childWindow;

        public SessionView()
        {
            InitializeComponent();

            Border? sidebar = this.FindControl<Border>("Sidebar");
            if (sidebar != null)
            {
                sidebar.PropertyChanged += (sender, args) =>
                {
                    if (args.Property == Border.IsVisibleProperty)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            UpdateChildWindowState();
                        }, DispatcherPriority.Render);
                    }
                };
            }

            LayoutUpdated += OnLayoutUpdated;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (_parentWindow != null)
            {
                _parentWindow.PropertyChanged += OnWindowPropertyChanged;
                _parentWindow.PositionChanged += OnWindowPositionChanged;
            }

            Dispatcher.UIThread.Post(() =>
            {
                UpdateChildWindowState();
            }, DispatcherPriority.Render);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_parentWindow != null)
            {
                _parentWindow.PropertyChanged -= OnWindowPropertyChanged;
                _parentWindow.PositionChanged -= OnWindowPositionChanged;
                _parentWindow = null;
            }

            if (_childWindow != null)
            {
                _childWindow.Close();
                _childWindow = null;
            }

            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            Dispatcher.UIThread.Post(() =>
            {
                UpdateChildWindowState();
            }, DispatcherPriority.Render);
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateChildWindowState();
                }, DispatcherPriority.Render);
            }
        }

        private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
        {
            UpdateChildWindowPosition();
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsSelected))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateChildWindowState();
                }, DispatcherPriority.Render);
            }
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            UpdateChildWindowPosition();
        }

        private Window CreateChildWindow()
        {
            ToggleButton btn = new()
            {
                Name = "CollapseButton",
                Width = 20,
                Height = 40,
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                BorderThickness = new Thickness(0, 1, 1, 1),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Padding = new Thickness(0),
                Focusable = false,
                Content = new TextBlock
                {
                    Text = "⇋",
                    FontSize = 14,
                    FontWeight = Avalonia.Media.FontWeight.Bold
                }
            };

            Border? sidebar = this.FindControl<Border>("Sidebar");
            if (sidebar != null)
            {
                btn.IsChecked = sidebar.IsVisible;
                btn.Click += (s, e) =>
                {
                    sidebar.IsVisible = btn.IsChecked ?? false;
                };

                sidebar.PropertyChanged += (s, e) =>
                {
                    if (e.Property == Border.IsVisibleProperty)
                    {
                        btn.IsChecked = sidebar.IsVisible;
                    }
                };
            }

            Window win = new()
            {
                WindowDecorations = WindowDecorations.None,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Topmost = false,
                CanResize = false,
                ShowInTaskbar = false,
                Focusable = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = btn
            };

            return win;
        }

        private void UpdateChildWindowState()
        {
            bool isSelected = DataContext is MainWindowViewModel vm && vm.IsSelected;

            bool isWindowVisible = _parentWindow != null && _parentWindow.IsVisible;
            bool isWindowNotMinimized = _parentWindow != null && _parentWindow.WindowState != WindowState.Minimized;

            bool shouldBeOpen = isSelected && isWindowVisible && isWindowNotMinimized;

            if (shouldBeOpen)
            {
                _childWindow ??= CreateChildWindow();

                if (!_childWindow.IsVisible)
                {
                    _childWindow.Show(_parentWindow!);
                }
                UpdateChildWindowPosition();
            }
            else
            {
                if (_childWindow != null && _childWindow.IsVisible)
                {
                    _childWindow.Hide();
                }
            }
        }

        private void UpdateChildWindowPosition()
        {
            if (_childWindow == null || _parentWindow == null || !_childWindow.IsVisible)
            {
                return;
            }

            try
            {
                Control? anchor = this.FindControl<Control>("PopupAnchor");
                if (anchor == null || !anchor.IsVisible || TopLevel.GetTopLevel(anchor) == null)
                {
                    return;
                }

                PixelPoint screenPos = anchor.PointToScreen(new Point(0, 0));
                PixelPoint targetPos = new(screenPos.X, screenPos.Y - 20);

                _childWindow.Position = targetPos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to update child window position: {ex.Message}");
            }
        }
    }
}
