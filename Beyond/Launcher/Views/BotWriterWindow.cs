using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Controls.Templates;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Launcher.Views;

public sealed class BotWriterWindow : Window
{
    private readonly TextBox _name = new(), _description = new(), _shop = new(), _shopMap = new();
    private readonly ComboBox _type = new() { ItemsSource = new[] { "Quest chain", "Choose quest rewards", "Merge shop farming", "Monster drops" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _label = new(), _quest = new(), _monster = new(), _map = new(), _room = new(), _item = new(), _quantity = new() { Text = "1" };
    private readonly ObservableCollection<BotStepDraft> _steps = [];
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#A2AAB8")) };
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#A2AAB8")) };
    private readonly StackPanel _shopFields = new() { Spacing = 6 };
    private readonly Control _questField, _monsterField, _itemField, _quantityField, _labelField;
    private readonly QuestAuthoringCatalog _catalog;
    private readonly AutoCompleteBox _questPicker = new() { MinimumPrefixLength = 1, FilterMode = AutoCompleteFilterMode.Contains, PlaceholderText = "Search quests by name..." };
    private readonly TextBlock _explanation = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _editorHeading = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _overview = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new();
    private BotStepDraft? _editing;
    private bool _loading, _dirty, _documentDirty, _allowClose;
    private readonly string? _suggestedDirectory;
    private JObject? _document, _originalForm;
    private string? _loadedName, _loadedPath;
    private readonly ComboBox _activities = new() { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static readonly string[] Templates = [BotScriptDraft.QuestChain, BotScriptDraft.QuestRewards, BotScriptDraft.MergeShop, BotScriptDraft.MonsterDrops];

    public BotWriterWindow(string? suggestedDirectory = null, string? initialPath = null, string? activity = null, string? gameDirectory = null, IEnumerable<KeyValuePair<int, string>>? questNames = null)
    {
        _suggestedDirectory = suggestedDirectory;
        _catalog = new QuestAuthoringCatalog(gameDirectory, questNames);
        Title = "Bot writer"; Width = 1060; Height = 850; MinWidth = 880; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var header = new StackPanel { Spacing = 8 };
        var open = new Button { Content = "Open script..." };
        open.Click += async (_, _) => await OpenScript(); header.Children.Add(open); header.Children.Add(_activities);
        header.Children.Add(new TextBlock { Text = "Build or edit a bot", FontSize = 23, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = "1. Choose what to automate.   2. Build the plan below.   3. Save it and start it from the launcher.", TextWrapping = TextWrapping.Wrap });
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        identity.Children.Add(Field("Bot name", _name)); var kind = Field("What should this bot do?", _type); Grid.SetColumn(kind, 1); identity.Children.Add(kind); header.Children.Add(identity);
        header.Children.Add(Field("Description (optional)", _description)); header.Children.Add(_help);
        var shopGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        shopGrid.Children.Add(Field("Shop ID", _shop)); var mapField = Field("Shop map", _shopMap); Grid.SetColumn(mapField, 1); shopGrid.Children.Add(mapField); _shopFields.Children.Add(shopGrid); header.Children.Add(_shopFields);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16, Margin = new Thickness(0, 16, 0, 0) };
        var left = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        Button Action(string title, Action run) { var button = new Button { Content = title }; button.Click += (_, _) => run(); return button; }
        actions.Children.Add(Action("Add another step", NewStep)); actions.Children.Add(Action("Remove", Remove)); actions.Children.Add(Action("Up", () => Move(-1))); actions.Children.Add(Action("Down", () => Move(1)));
        DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions);
        var planHeading = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        planHeading.Children.Add(new TextBlock { Text = "Your plan", FontSize = 17, FontWeight = FontWeight.SemiBold }); planHeading.Children.Add(_overview); DockPanel.SetDock(planHeading, Dock.Top); left.Children.Add(planHeading);
        _list.ItemTemplate = new FuncDataTemplate<BotStepDraft>((step, _) =>
        {
            if (step == null) return new TextBlock();
            var row = new StackPanel { Spacing = 4, Margin = new Thickness(6) };
            string name = step.QuestId > 0 ? _catalog.Name(step.QuestId) : !string.IsNullOrWhiteSpace(step.Label) ? step.Label : step.Item;
            row.Children.Add(new TextBlock { Text = $"{_steps.IndexOf(step) + 1}. {name}", FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = step.QuestId > 0 ? "Complete quest" + (string.IsNullOrWhiteSpace(step.Area) ? "" : $" in /{step.Area}") : $"Farm from {step.Monster}", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#A2AAB8")), TextWrapping = TextWrapping.Wrap }); return row;
        });
        _list.ItemsSource = _steps; left.Children.Add(_list); body.Children.Add(left);
        var editor = new StackPanel { Spacing = 8 };
        editor.Children.Add(_editorHeading);
        editor.Children.Add(new Border { Child = _explanation, Background = new SolidColorBrush(Color.Parse("#1B2530")), Padding = new Thickness(12), CornerRadius = new CornerRadius(6) });
        _labelField = Field("Reward / choice label", _label); editor.Children.Add(_labelField);
        _questPicker.ItemsSource = _catalog.All;
        _questField = Field("Which quest?", _questPicker); editor.Children.Add(_questField);
        ToolTip.SetTip(_questPicker, "Type part of the quest name and choose a result. Its ID is filled in automatically.");
        _questPicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _questPicker.SelectedItem is not QuestAuthoringCatalog.QuestChoice choice) return;
            _quest.Text = choice.Id.ToString();
            if (!string.IsNullOrWhiteSpace(choice.Area)) _map.Text = choice.Area;
            if (!string.IsNullOrWhiteSpace(choice.Room)) _room.Text = choice.Room;
            UpdateExplanation();
        };
        _questPicker.TextChanged += (_, _) => { if (!_loading && _questPicker.SelectedItem == null) _quest.Text = ""; };
        _monsterField = Field("Monster name or ID", _monster); editor.Children.Add(_monsterField);
        editor.Children.Add(Field("Where should this step start?", _map));
        _map.PlaceholderText = "Map name, such as bludrut (blank = current map)";
        var advanced = new StackPanel { Spacing = 8 }; advanced.Children.Add(Field("Quest ID (filled from your quest choice)", _quest)); advanced.Children.Add(Field("Starting room (blank = current room)", _room));
        editor.Children.Add(new Expander { Header = "Advanced: quest ID and starting room", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        _itemField = Field("Drop / material name or ID", _item); editor.Children.Add(_itemField);
        _quantityField = Field("Desired inventory total", _quantity); editor.Children.Add(_quantityField);
        _apply.Click += (_, _) => ApplyEditor(); editor.Children.Add(_apply);
        var editorScroll = new ScrollViewer { Content = editor }; Grid.SetColumn(editorScroll, 1); body.Children.Add(editorScroll);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) }; footer.Children.Add(_status);
        var saveActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(new TextBlock { Text = "Save writes a script file. Save & use also loads it into the launcher; press Start Script there when you are ready. Nothing runs from this editor.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var save = new Button { Content = "Save file..." }; save.Click += async (_, _) => await Save(false);
        var load = new Button { Content = "Save & use in launcher..." }; load.Click += async (_, _) => await Save(true);
        var cancel = new Button { Content = "Close" }; cancel.Click += (_, _) => Close(null);
        saveActions.Children.Add(save); saveActions.Children.Add(load); saveActions.Children.Add(cancel); footer.Children.Add(saveActions);
        var layout = new DockPanel { Margin = new Thickness(20), LastChildFill = true }; DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(body); Content = layout;
        foreach (var box in new[] { _label, _quest, _monster, _map, _room, _item, _quantity }) box.TextChanged += (_, _) => { if (!_loading) { _dirty = true; UpdateExplanation(); } };
        _list.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var next = _list.SelectedItem as BotStepDraft;
            if (_dirty)
            {
                try
                {
                    if (_editing == null) throw new ArgumentException("Add this new step to the plan before selecting another step.");
                    CopyEditor(_editing);
                }
                catch (Exception ex)
                {
                    _status.Text = ex.Message;
                    var previous = _editing;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _loading = true;
                        _list.SelectedItem = previous;
                        _loading = false;
                    });
                    return;
                }
            }
            _editing = next;
            FillEditor(next);
            UpdateType();
            // Avalonia is still committing its selection transaction here.
            // Refresh item labels only after that transaction has completed.
            Dispatcher.UIThread.Post(() => RefreshList(_editing), DispatcherPriority.Background);
        };
        foreach (var field in new[] { _name, _description, _shop, _shopMap })
            field.TextChanged += (_, _) => { if (!_loading) _documentDirty = true; };
        _steps.CollectionChanged += (_, _) => { if (!_loading) _documentDirty = true; };
        Closing += async (_, e) =>
        {
            if (_allowClose || (!_dirty && !_documentDirty)) return;
            e.Cancel = true;
            if (await ConfirmDiscard()) { _allowClose = true; Close(null); }
        };
        _type.SelectionChanged += (_, _) => { if (!_loading) _documentDirty = true; UpdateType(); UpdateExplanation(); }; UpdateType(); UpdateExplanation();
        _activities.SelectionChanged += (_, _) => { if (!_loading && _document != null && _activities.SelectedItem is string selected) LoadActivity(selected); };
        if (initialPath != null) Opened += (_, _) => LoadFile(initialPath, activity);
    }
    private async Task<bool> ConfirmDiscard()
    {
        var dialog = new Window { Title = "Unsaved bot changes", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Your changes have not been saved to a file. Discard them?", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var keep = new Button { Content = "Keep editing" };
        var discard = new Button { Content = "Discard changes" };
        keep.Click += (_, _) => dialog.Close(false);
        discard.Click += (_, _) => dialog.Close(true);
        actions.Children.Add(keep); actions.Children.Add(discard); panel.Children.Add(actions); dialog.Content = panel;
        dialog.Opened += (_, _) => keep.Focus();
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task OpenScript()
    {
        var options = new FilePickerOpenOptions { Title = "Open bot script", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Beyond scripts") { Patterns = ["*.json"] }] };
        string? directory = _loadedPath == null ? _suggestedDirectory : Path.GetDirectoryName(_loadedPath);
        if (directory != null && Directory.Exists(directory)) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(directory);
        if ((_dirty || _documentDirty) && !await ConfirmDiscard()) return;
        var files = await StorageProvider.OpenFilePickerAsync(options); string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) LoadFile(path);
    }
    private void LoadFile(string path, string? activity = null)
    {
        try
        {
            var document = JObject.Parse(File.ReadAllText(path));
            var draft = BotScriptDraft.FromJson(document, activity);
            _document = document; _loadedPath = path;
            _loading = true; _activities.ItemsSource = document.Properties().Where(p => !p.Name.StartsWith('_') && (p.Value is JObject || p.Value is JArray)).Select(p => p.Name).ToArray(); _activities.SelectedItem = draft.Name; _activities.IsVisible = document.Properties().Count(p => !p.Name.StartsWith('_')) > 1; _loading = false;
            ShowDraft(draft);
            _documentDirty = false;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void LoadActivity(string name)
    {
        try
        {
            var next = BotScriptDraft.FromJson(_document!, name);
            if (_loadedName != null && _loadedName != name)
            {
                if (_dirty) { ApplyEditor(); if (_dirty) throw new ArgumentException("Finish the current step before changing activities."); }
                var current = CaptureDraft();
                if (current.Name.Trim() != _loadedName) throw new ArgumentException("Save this renamed activity before switching activities.");
                _document![_loadedName] = current.ToJson()[_loadedName]!.DeepClone();
            }
            ShowDraft(next);
        }
        catch (Exception ex) { _status.Text = ex.Message; _loading = true; _activities.SelectedItem = _loadedName; _loading = false; }
    }
    private void ShowDraft(BotScriptDraft draft)
    {
        _originalForm = draft.OriginalForm; _loadedName = draft.Name;
        _loading = true; _name.Text = draft.Name; _description.Text = draft.Description; _type.SelectedIndex = Array.IndexOf(Templates, draft.Template); _type.IsEnabled = false;
        _shop.Text = draft.ShopId > 0 ? draft.ShopId.ToString() : ""; _shopMap.Text = draft.ShopMap;
        _steps.Clear(); foreach (var step in draft.Steps) _steps.Add(step); _editing = null; _list.SelectedItem = null; _loading = false; FillEditor(null); UpdateType();
        if (_steps.Count > 0) _list.SelectedIndex = 0;
        Title = "Bot writer - " + draft.Name; _status.Text = "Editing " + Path.GetFileName(_loadedPath) + ". Choose a named step on the left to change it.";
    }

    private static StackPanel Field(string name, Control control) { var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#A2AAB8")) }); panel.Children.Add(control); return panel; }
    private void UpdateType()
    {
        int type = _type.SelectedIndex;
        _overview.Text = _steps.Count == 0 ? "No steps yet. Choose a quest or drop on the right, then add it to the plan." : $"{_steps.Count} steps. " + (type == 0 ? "Runs from top to bottom, then stops." : type is 1 or 3 ? "Players choose which targets to run when starting." : "Farm routes supply the rewards selected when starting.");
        _shopFields.IsVisible = type == 2; _questField.IsVisible = type != 3; _monsterField.IsVisible = type >= 2; _itemField.IsVisible = type >= 2; _quantityField.IsVisible = type == 3; _labelField.IsVisible = type is 1 or 3;
        _help.Text = type switch { 0 => "Steps run in order. Already-completed story quests are skipped.", 1 => "Each step becomes a separate reward toggle. Players choose which quests to run.", 2 => "Players choose shop rewards. The bot discovers their materials from live monster drop lists on the shop map. Optional steps below provide fallback material routes.", _ => "Each drop becomes a selectable target. Players choose the drops and desired totals before farming." };
    }
    private void FillEditor(BotStepDraft? step)
    {
        _loading = true;
        _label.Text = step?.Label ?? ""; _quest.Text = step?.QuestId > 0 ? step.QuestId.ToString() : ""; _monster.Text = step?.Monster ?? ""; _map.Text = step?.Area ?? ""; _room.Text = step?.Room ?? ""; _item.Text = step?.Item ?? ""; _quantity.Text = (step?.Quantity ?? 1).ToString();
        _questPicker.SelectedItem = step == null ? null : _catalog.Get(step.QuestId);
        _questPicker.Text = step?.QuestId > 0 ? _catalog.Name(step.QuestId) : "";
        _loading = false; _dirty = false; UpdateExplanation();
    }
    private void UpdateExplanation()
    {
        if (_loading) return;
        _editorHeading.Text = _editing == null ? "Add a step to your plan" : $"Editing step {_steps.IndexOf(_editing) + 1}";
        _apply.Content = _editing == null ? "Add to plan" : "Apply changes to this step";
        int.TryParse(_quest.Text, out int quest); int.TryParse(_quantity.Text, out int qty);
        var step = new BotStepDraft { QuestId = quest, Monster = _monster.Text ?? "", Area = _map.Text ?? "", Room = _room.Text ?? "", Item = _item.Text ?? "", Quantity = Math.Max(1, qty) };
        _explanation.Text = quest <= 0 && _type.SelectedIndex < 2 ? "Search for a quest by name below. The bot will accept it, complete its objectives and turn it in. Existing completed story quests are skipped." : "What this step will do\n" + _catalog.Describe(step, Templates[Math.Max(0, _type.SelectedIndex)]);
    }
    private void CopyEditor(BotStepDraft step)
    {
        int quest = 0, quantity = 1;
        if (_type.SelectedIndex != 3 && !string.IsNullOrWhiteSpace(_quest.Text) && (!int.TryParse(_quest.Text, out quest) || quest <= 0)) throw new ArgumentException("Quest ID must be a positive whole number.");
        if (_type.SelectedIndex == 3 && (!int.TryParse(_quantity.Text, out quantity) || quantity < 1)) throw new ArgumentException("Desired total must be a positive whole number.");
        var candidate = new BotStepDraft { Label = _label.Text ?? "", QuestId = quest, Monster = _monster.Text ?? "", Area = _map.Text ?? "", Room = _room.Text ?? "", Item = _item.Text ?? "", Quantity = quantity };
        new BotScriptDraft { Name = "Step", Template = Templates[_type.SelectedIndex], ShopId = 1, Steps = [candidate] }.ToJson();
        step.Label = candidate.Label; step.QuestId = candidate.QuestId; step.Monster = candidate.Monster; step.Area = candidate.Area; step.Room = candidate.Room; step.Item = candidate.Item; step.Quantity = candidate.Quantity; _dirty = false; _documentDirty = true;
    }
    private void ApplyEditor()
    {
        try { var step = _editing ?? new BotStepDraft(); CopyEditor(step); if (_editing == null) _steps.Add(step); RefreshList(step); _status.Text = "Changes applied to the plan. Save the file when you are finished."; }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void RefreshList(BotStepDraft? selected) { _loading = true; _list.ItemsSource = null; _list.ItemsSource = _steps; _list.SelectedItem = selected; _editing = selected; _loading = false; UpdateType(); UpdateExplanation(); }
    private void NewStep() { if (_dirty) { ApplyEditor(); if (_dirty) return; } RefreshList(null); FillEditor(null); }
    private void Remove() { if (_editing == null) return; var removed = _editing; RefreshList(null); _steps.Remove(removed); FillEditor(null); UpdateType(); }
    private void Move(int delta) { if (_editing == null) return; int from = _steps.IndexOf(_editing), to = from + delta; if (to < 0 || to >= _steps.Count) return; if (_dirty) { ApplyEditor(); if (_dirty) return; } _steps.Move(from, to); RefreshList(_editing); }
    private BotScriptDraft CaptureDraft()
    {
        int shop = 0;
        if (_type.SelectedIndex == 2 && (!int.TryParse(_shop.Text, out shop) || shop <= 0)) throw new ArgumentException("Enter a positive shop ID.");
        return new BotScriptDraft { Name = _name.Text ?? "", Description = _description.Text ?? "", Template = Templates[_type.SelectedIndex], ShopId = shop, ShopMap = _shopMap.Text ?? "", Steps = _steps.ToList(), OriginalForm = _originalForm };
    }
    private async Task Save(bool load)
    {
        try
        {
            if (_dirty) { ApplyEditor(); if (_dirty) return; }
            var draft = CaptureDraft();
            JObject activity = draft.ToJson();
            JObject document = _document == null ? new JObject() : (JObject)_document.DeepClone();
            if (_loadedName != null && _loadedName != draft.Name.Trim())
            {
                if (document.Property(draft.Name.Trim()) != null) throw new ArgumentException("Another activity already uses this name.");
                document.Remove(_loadedName);
            }
            document[draft.Name.Trim()] = activity[draft.Name.Trim()]!.DeepClone();
            string json = document.ToString(Newtonsoft.Json.Formatting.Indented);
            var options = new FilePickerSaveOptions { Title = "Save bot script", DefaultExtension = "json", SuggestedFileName = string.Concat(draft.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) + ".json", FileTypeChoices = [new FilePickerFileType("Beyond bot script") { Patterns = ["*.json"] }] };
            if (_suggestedDirectory != null && Directory.Exists(_suggestedDirectory)) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_suggestedDirectory);
            var file = await StorageProvider.SaveFilePickerAsync(options); if (file == null) return;
            string? localPath = file.TryGetLocalPath();
            if (localPath == null) throw new IOException("Choose a local folder so the previous script can be preserved if saving fails.");
            await BotScriptFile.SaveAsync(localPath, json);
            _document = document; _loadedName = draft.Name.Trim(); _loadedPath = file.TryGetLocalPath();
            _dirty = false; _documentDirty = false;
            _loading = true;
            _activities.ItemsSource = document.Properties().Where(p => !p.Name.StartsWith('_') && p.Value is JArray or JObject).Select(p => p.Name).ToArray();
            _activities.SelectedItem = _loadedName;
            _loading = false;
            _status.Text = "Script saved.";
            if (load) { string? path = file.TryGetLocalPath(); if (path != null) Close(path); else _status.Text = "Saved. Load this file from the script loader."; }
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
