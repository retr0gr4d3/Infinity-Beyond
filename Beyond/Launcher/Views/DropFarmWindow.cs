using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Launcher.Views;

public sealed class DropFarmWindow : Window
{
    private sealed record MonsterChoice(int Id, string Name, string Frame)
    {
        public override string ToString() => Name;
    }
    private readonly ModConnection _connection;
    private readonly ComboBox _monsters = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a monster" };
    private readonly StackPanel _drops = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _start = new() { Content = "Farm selected drops", IsEnabled = false };
    private readonly List<(CheckBox check, NumericUpDown quantity, int id, string name)> _rows = [];
    // Drops queued from earlier monsters. The runner farms them in order and
    // travels to the next monster's room once a target total is reached.
    private readonly List<(string monster, string label, JObject entry)> _queue = [];
    private readonly TextBlock _queueText = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#A2AAB8")) };
    private readonly Button _add = new() { Content = "Add to queue", IsEnabled = false };
    private string _area = "";
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(15) };

    public DropFarmWindow(ModConnection connection)
    {
        _connection = connection;
        _timeout.Tick += (_, _) => { _timeout.Stop(); _status.Text = "No response yet. Check the game connection, then refresh monsters and try again."; };
        Title = "Farm monster drops"; Width = 540; Height = 620; MinWidth = 420; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(new TextBlock { Text = "Choose a monster. Pick your drops.", FontSize = 20, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = "Monsters on your current map. Queue drops from several monsters: each is farmed to its total, then the bot moves to the next monster's room.", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(_monsters);
        var refresh = new Button { Content = "Refresh monsters" };
        refresh.Click += (_, _) => Refresh(); header.Children.Add(refresh); header.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        _add.Click += (_, _) => AddToQueue(); actions.Children.Add(_add);
        _start.Click += (_, _) => Start(); actions.Children.Add(_start);
        var clear = new Button { Content = "Clear queue" }; clear.Click += (_, _) => { _queue.Clear(); UpdateQueue(); }; actions.Children.Add(clear);
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(null); actions.Children.Add(cancel);
        var layout = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top); header.Margin = new Thickness(0, 0, 0, 12); layout.Children.Add(header);
        var footer = new StackPanel { Spacing = 6 }; footer.Children.Add(_queueText); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = _drops }); Content = layout;
        _monsters.SelectionChanged += (_, _) => LoadDrops();
        _connection.FarmingMonstersReceived += OnMonsters;
        _connection.FarmingDropsReceived += OnDrops;
        Opened += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _timeout.Stop();
            _connection.FarmingMonstersReceived -= OnMonsters;
            _connection.FarmingDropsReceived -= OnDrops;
        };
    }

    private void Refresh()
    {
        _add.IsEnabled = false; _start.IsEnabled = _queue.Count > 0; _drops.Children.Clear(); _rows.Clear();
        _monsters.SelectedItem = null; _monsters.ItemsSource = null;
        _status.Text = "Loading monsters from your map...";
        _timeout.Stop(); _timeout.Start();
        _connection.SendCommand("RequestFarmingMonsters", null);
    }
    private void OnMonsters(JObject msg)
    {
        _timeout.Stop();
        _area = (string?)msg["Area"] ?? "";
        var choices = (msg["Monsters"] as JArray ?? []).OfType<JObject>()
            .Select(m => new MonsterChoice((int)m["Id"]!, (string?)m["Name"] ?? "Monster", (string?)m["Frame"] ?? "")).ToArray();
        _monsters.ItemsSource = choices;
        _status.Text = choices.Length == 0 ? "No monsters found. Travel to their map, then refresh." : $"{choices.Length} monsters found. Choose one to see its drops.";
        if (choices.Length == 1) _monsters.SelectedIndex = 0;
    }
    private void LoadDrops()
    {
        _add.IsEnabled = false; _start.IsEnabled = _queue.Count > 0; _drops.Children.Clear(); _rows.Clear();
        if (_monsters.SelectedItem is not MonsterChoice monster) return;
        _status.Text = $"Loading {monster.Name}'s drops...";
        _timeout.Stop(); _timeout.Start();
        _connection.SendCommand("RequestFarmingDrops", new JObject { ["MonsterId"] = monster.Id });
    }
    private void OnDrops(JObject msg)
    {
        if (_monsters.SelectedItem is not MonsterChoice monster || (int?)msg["MonsterId"] != monster.Id) return;
        _timeout.Stop();
        _drops.Children.Clear(); _rows.Clear();
        foreach (JObject item in (msg["Items"] as JArray ?? []).OfType<JObject>().Where(i => ((int?)i["Id"] ?? 0) > 0).GroupBy(i => (int)i["Id"]!).Select(g => g.First()))
        {
            var check = new CheckBox
            {
                IsChecked = false,
                Content = new TextBlock
                {
                    Text = $"{item["Name"]} (owned: {item["Owned"]})",
                    TextWrapping = TextWrapping.Wrap
                }
            };
            int max = Math.Max(1, (int?)item["StackSize"] ?? 1);
            var qty = new NumericUpDown { Minimum = 1, Maximum = max, Value = 1, Width = 120 };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(qty, 1); row.Children.Add(check); row.Children.Add(qty); _drops.Children.Add(row);
            _rows.Add((check, qty, (int)item["Id"]!, (string?)item["Name"] ?? item["Id"]!.ToString()));
        }
        _add.IsEnabled = _rows.Count > 0;
        _start.IsEnabled = _rows.Count > 0 || _queue.Count > 0;
        _status.Text = _rows.Count == 0 ? "The server returned no visible drops for this monster."
            : "Toggle the drops you want. Quantity is your desired inventory total; already-owned items count.";
    }
    private void Start()
    {
        JObject? plan = BuildPlan();
        if (plan == null) { _status.Text = "Select at least one drop."; return; }
        Close(plan);
    }
    private void AddToQueue()
    {
        var selected = Selected();
        if (selected.Count == 0) { _status.Text = "Select at least one drop to queue."; return; }
        foreach (var item in selected) Enqueue(_queue, item);
        foreach (var row in _rows) row.check.IsChecked = false;
        UpdateQueue();
        _status.Text = "Queued. Choose another monster to add more, or start farming.";
    }
    // Re-adding a drop updates its total instead of farming it twice.
    private static void Enqueue(List<(string monster, string label, JObject entry)> queue, (string monster, string label, JObject entry) item)
    {
        queue.RemoveAll(q => JToken.DeepEquals(q.entry["hunt"], item.entry["hunt"]) && (string?)q.entry["item"] == (string?)item.entry["item"]);
        queue.Add(item);
    }
    private void UpdateQueue()
    {
        _queueText.Text = _queue.Count == 0 ? "" : "Queue (farmed in order):\n" + string.Join("\n", _queue.Select((q, i) => $"{i + 1}. {q.label}"));
        _start.IsEnabled = _queue.Count > 0 || _rows.Count > 0;
    }
    private List<(string monster, string label, JObject entry)> Selected()
    {
        if (_monsters.SelectedItem is not MonsterChoice monster) return [];
        return _rows.Where(r => r.check.IsChecked == true).Select(row => (monster.Name, $"{row.name} ×{(int)(row.quantity.Value ?? 1)} from {monster.Name}", new JObject
        {
            ["hunt"] = new JArray(monster.Id.ToString()),
            ["area"] = _area,
            ["frame"] = monster.Frame,
            ["item"] = row.id.ToString(),
            ["qty"] = (int)(row.quantity.Value ?? 1)
        })).ToList();
    }
    internal JObject? BuildPlan()
    {
        var items = _queue.ToList();
        foreach (var item in Selected()) Enqueue(items, item);
        if (items.Count == 0) return null;
        var monsters = items.Select(i => i.monster).Distinct().ToList();
        string name = monsters.Count == 1 ? $"{monsters[0]} drops" : $"{monsters.Count} monster drops";
        return new JObject { ["Name"] = name, ["Entries"] = new JArray(), ["Farm"] = new JArray(items.Select(i => i.entry)), ["FarmEnabled"] = true, ["FarmLoops"] = 0 };
    }
}
