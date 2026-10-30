using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Launcher.Views;

public sealed class FarmingTargetWindow : Window
{
    public FarmingTargetWindow(string activity, JArray options)
    {
        Title = activity;
        Width = 480; Height = 480; MinWidth = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Which ones would you like to farm?", FontSize = 20, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Choose one or more targets. Only your selections will run.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var rows = new List<(CheckBox check, JObject option, NumericUpDown? quantity)>();
        foreach (JToken token in options)
        {
            if (token is not JObject option) continue;
            var check = new CheckBox { Content = (string?)option["name"] ?? "Farming target", IsChecked = false };
            panel.Children.Add(check);
            NumericUpDown? quantity = null;
            if (option["farm"] is JArray steps && steps.Count == 1 && steps[0] is JObject goal && goal["hunt"] != null && goal["qty"] != null)
            {
                quantity = new NumericUpDown { Minimum = 1, Maximum = 999999, Value = (int?)goal["qty"] ?? 1, Width = 140 };
                panel.Children.Add(new TextBlock { Text = "Desired inventory total", FontSize = 11 });
                panel.Children.Add(quantity);
            }
            panel.Children.Add(new TextBlock { Text = (string?)option["description"] ?? "", FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            rows.Add((check, option, quantity));
        }
        var error = new TextBlock { Text = "", Foreground = Avalonia.Media.Brushes.OrangeRed };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var start = new Button { Content = "Farm selected" };
        start.Click += (_, _) =>
        {
            var chosen = new JArray();
            foreach (var row in rows)
            {
                if (row.check.IsChecked != true) continue;
                var option = (JObject)row.option.DeepClone();
                if (row.quantity != null) option["farm"]![0]!["qty"] = (int)(row.quantity.Value ?? 1);
                chosen.Add(option);
            }
            if (chosen.Count == 0) { error.Text = "Select at least one target."; return; }
            Close(chosen);
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(start); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel };
    }
}
