using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Launcher.Views
{
    /// <summary>
    /// Reusable custom title bar for the tool windows. Drives minimize / maximize /
    /// close and window dragging on whichever <see cref="Window"/> hosts it.
    /// </summary>
    public partial class TitleBar : UserControl
    {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public TitleBar()
        {
            InitializeComponent();
        }

        private Window? Host => TopLevel.GetTopLevel(this) as Window;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // A fixed-size window can't be maximized, so hide that button.
            if (Host is { CanResize: false })
            {
                MaximizeButton.IsVisible = false;
            }
        }

        private void OnDragPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Host?.BeginMoveDrag(e);
            }
        }

        private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (Host is { CanResize: true } window)
            {
                ToggleMaximize(window);
            }
        }

        private void OnMinimize(object? sender, RoutedEventArgs e)
        {
            if (Host is { } window)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private void OnMaximize(object? sender, RoutedEventArgs e)
        {
            if (Host is { } window)
            {
                ToggleMaximize(window);
            }
        }

        private void OnClose(object? sender, RoutedEventArgs e) => Host?.Close();

        private static void ToggleMaximize(Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
