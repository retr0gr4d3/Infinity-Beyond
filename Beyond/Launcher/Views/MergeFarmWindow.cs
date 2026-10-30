using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Launcher;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Launcher.Views;

public sealed class MergeFarmWindow : Window
{
    private readonly ModConnection _connection;
    private readonly int _shop;
    private readonly StackPanel _items = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<(CheckBox check, NumericUpDown qty, JObject item)> _rows = [];
    private readonly TextBlock _materials = new() { TextWrapping = TextWrapping.Wrap };
    public MergeFarmWindow(ModConnection connection, JObject plan)
    {
        _connection = connection; _shop = (int?)plan["shop"] ?? 0;
        Title = "Choose merge rewards"; Width = 560; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Which items would you like to farm?", FontSize = 20, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Select rewards and desired totals. Materials are calculated for your selections only. Material sources are discovered from monsters on the shop map. Story quests are not included.", TextWrapping = TextWrapping.Wrap });
        _status.Text = "Loading shop. If it does not load, visit the shop's map and try Reload shop.";
        panel.Children.Add(_status);
        panel.Children.Add(_materials);
        var reload = new Button { Content = "Reload shop" }; reload.Click += (_, _) => LoadShop(); panel.Children.Add(reload);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var start = new Button { Content = "Farm selected rewards" };
        start.Click += (_, _) =>
        {
            var selected = new JArray();
            foreach (var row in _rows)
                if (row.check.IsChecked == true) selected.Add(new JObject { ["item"] = (int)row.item["Id"]!, ["qty"] = (int)(row.qty.Value ?? 1) });
            if (selected.Count == 0) { _status.Text = "Select at least one reward."; return; }
            var result = (JObject)plan.DeepClone(); result["items"] = selected; Close(result);
        };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(start); buttons.Children.Add(cancel);
        var layout = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        panel.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(panel, Dock.Top); layout.Children.Add(panel);
        buttons.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = _items }); Content = layout;
        _connection.FarmingShopReceived += OnShop;
        Opened += (_, _) => LoadShop(); Closed += (_, _) => _connection.FarmingShopReceived -= OnShop;
    }
    private void LoadShop() => _connection.SendCommand("LoadShop", new JObject { ["ShopId"] = _shop });
    private void OnShop(JObject msg)
    {
        if ((int?)msg["Shop"] != _shop) return;
        // Preserve current selections when refreshing prices or inventory.
        var selected = _rows.Where(r => r.check.IsChecked == true).ToDictionary(r => (int)r.item["Id"]!, r => r.qty.Value ?? 1);
        _items.Children.Clear(); _rows.Clear();
        foreach (JObject item in (msg["Items"] as JArray ?? []).OfType<JObject>().GroupBy(i => (int)i["Id"]!).Select(g => g.First()))
        {
            var costs = (item["Costs"] as JArray ?? []).OfType<JObject>().ToArray();
            // This template farms merge materials, not gold/AC-only purchases.
            if (costs.Length == 0) continue;
            int id = (int)item["Id"]!;
            var check = new CheckBox { Content = $"{item["Name"]} (owned: {item["Owned"]})", IsChecked = selected.ContainsKey(id) };
            var qty = new NumericUpDown { Minimum = 1, Maximum = 9999, Value = selected.TryGetValue(id, out var n) ? n : 1, Width = 120 };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var label = new StackPanel { Spacing = 4 };
            label.Children.Add(new TextBlock { Text = (string?)item["Name"] ?? "Reward", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = $"Owned: {item["Owned"]}", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#9299A6")) });
            check.Content = label;
            AutomationProperties.SetName(check, (string?)item["Name"] ?? "Reward");
            AutomationProperties.SetName(qty, "Desired total for " + (string?)item["Name"]);
            Grid.SetColumn(qty, 1); row.Children.Add(check); row.Children.Add(qty);
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(row);
            card.Children.Add(new TextBlock { Text = "Requires " + string.Join(", ", costs.Select(c => $"{c["Quantity"]} {c["Name"]}")), FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#9299A6")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 0, 0, 0) });
            _items.Children.Add(new Border { Child = card, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(Color.Parse("#171A20")), CornerRadius = new CornerRadius(6) });
            _rows.Add((check, qty, item));
            check.IsCheckedChanged += (_, _) => UpdateMaterials();
            qty.ValueChanged += (_, _) => UpdateMaterials();
        }
        _status.Text = $"{msg["Name"]}: {_rows.Count} merge rewards. Desired totals include items already owned.";
        UpdateMaterials();
    }
    private void UpdateMaterials()
    {
        var totals = new Dictionary<int, (string name, int need, int owned)>();
        int selected = 0;
        foreach (var row in _rows)
        {
            if (row.check.IsChecked != true) continue;
            selected++;
            int count = Math.Max(0, (int)(row.qty.Value ?? 1) - ((int?)row.item["Owned"] ?? 0));
            foreach (JObject cost in (row.item["Costs"] as JArray ?? []).OfType<JObject>())
            {
                int id = (int)cost["Id"]!;
                totals.TryGetValue(id, out var previous);
                totals[id] = ((string?)cost["Name"] ?? id.ToString(), previous.need + count * ((int?)cost["Quantity"] ?? 0), (int?)cost["Owned"] ?? 0);
            }
        }
        _materials.Text = selected == 0 ? "No rewards selected."
            : $"{selected} selected. Materials: " + string.Join(", ", totals.Values.Select(t => $"{t.name}: {t.owned}/{t.need} ({Math.Max(0, t.need - t.owned)} more)"));
    }

}
