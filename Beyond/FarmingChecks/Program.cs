using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using BeyondAgent.Util;
using Launcher;
using Launcher.Views;
using Newtonsoft.Json.Linq;
using System.Reflection;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--all"))
        {
            Checks(); WriterChecks(); SourceChecks(); DeploymentChecks(); StorageChecks(); CatalogChecks(); SaveChecks();
            AppBuilder.Configure<PreviewApp>().UsePlatformDetect().SetupWithoutStarting();
            DropChecks(); WriterFormChecks();
            Console.WriteLine("PASS: all offline farming checks"); return;
        }
        if (args.Contains("--storage")) { StorageChecks(); return; }
        if (args.Contains("--sources")) { SourceChecks(); return; }
        if (args.Contains("--catalog")) { CatalogChecks(); return; }
        if (args.Contains("--writer")) { WriterChecks(); return; }
        if (args.Contains("--deployment")) { DeploymentChecks(); return; }
        if (args.Contains("--logic")) { Checks(); return; }
        if (args.Contains("--writer-form"))
        {
            AppBuilder.Configure<PreviewApp>().UsePlatformDetect().SetupWithoutStarting(); WriterFormChecks(); return;
        }
        if (args.Contains("--drops"))
        {
            AppBuilder.Configure<PreviewApp>().UsePlatformDetect().SetupWithoutStarting(); DropChecks(); return;
        }
        AppBuilder.Configure<PreviewApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
    static void SaveChecks()
    {
        string folder = Path.Combine(Environment.CurrentDirectory, "Beyond", "FarmingChecks", "artifacts", "Save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "bot.json");
        File.WriteAllText(path, "original");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false;
            try { BotScriptFile.SaveAsync(path, "replacement").GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Assert(failed, "a locked destination must report a failed save");
        }
        Assert(File.ReadAllText(path) == "original", "failed saves must retain the previous script");
        BotScriptFile.SaveAsync(path, "replacement").GetAwaiter().GetResult();
        Assert(File.ReadAllText(path) == "replacement" && Directory.GetFiles(folder, "*.tmp").Length == 0, "successful atomic saves must replace the file and clean temporary data");
        Console.WriteLine("PASS: atomic saves and previous-file preservation on failure");
    }

    static void StorageChecks()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "Beyond", "FarmingChecks", "artifacts", "BeyondBots-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string legacy = Path.Combine(root, "FarmingPlans"); Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "Event.json"), "custom");
        string bots = BotStorage.Ensure(root); Assert(Directory.Exists(bots) && !Directory.Exists(legacy) && File.ReadAllText(Path.Combine(bots, "Event.json")) == "custom", "folder rename must preserve bot contents");
        string downloads = Path.Combine(root, "downloads"); Directory.CreateDirectory(downloads); File.WriteAllText(Path.Combine(downloads, "Event.json"), "upstream"); File.WriteAllText(Path.Combine(downloads, "New.json"), "new"); BotStorage.Import(downloads, bots);
        Assert(File.ReadAllText(Path.Combine(bots, "Event.json")) == "custom" && File.Exists(Path.Combine(bots, "New.json")), "downloads must add new bots without overwriting local edits");
        File.WriteAllText(Path.Combine(downloads, "New.json"), "updated"); BotStorage.Import(downloads, bots);
        Assert(File.ReadAllText(Path.Combine(bots, "New.json")) == "updated", "unchanged local imports must receive upstream updates");
        File.WriteAllText(Path.Combine(bots, "New.json"), "user edit"); File.WriteAllText(Path.Combine(downloads, "New.json"), "new upstream"); BotStorage.Import(downloads, bots);
        Assert(File.ReadAllText(Path.Combine(bots, "New.json")) == "user edit", "upstream updates must preserve locally edited imports");
        Assert(BotStorage.Resolve(Path.Combine(legacy, "Event.json"), root) == Path.Combine(bots, "Event.json"), "remembered old paths must follow the rename");
        Assert(BotStorage.Resolve(Path.Combine(root, "custom", "Event.json"), root) == Path.Combine(root, "custom", "Event.json"), "unrelated custom scripts must not be redirected");
        Assert(BotStorage.Ensure(root) == bots, "migration must be repeatable"); Console.WriteLine("PASS: bots folder migration, preserved files, safe imports and repeat startup");
    }
    static void SourceChecks()
    {
        var index = new MaterialSourceIndex(); DateTime utc = DateTime.UtcNow;
        var monsters = new List<MaterialMonster> { new() { Id = 10, Name = "Nearby", Area = "map", Frame = "R2", Alive = true }, new() { Id = 20, Name = "Far", Area = "map", Frame = "R3", Alive = true }, new() { Id = 30, Name = "Dead", Area = "map", Frame = "R2", Alive = false } };
        index.Record("A", 10, [new() { ItemId = 1, Name = "Corn", Rate = .33, Quantity = 3 }], utc);
        index.Record("A", 20, [new() { ItemId = 1, Name = "Corn", Rate = 1, Quantity = 3 }], utc);
        index.Record("A", 30, [new() { ItemId = 1, Name = "Corn", Rate = 1, Quantity = 10 }], utc);
        Assert(index.Choose("A", 1, monsters, "R2", utc)!.Id == 10, "prefer live current-room source");
        Assert(index.Choose("B", 1, monsters, "R2", utc) == null, "servers must never share source knowledge");
        Assert(index.Choose("A", 1, monsters, "R2", utc.AddMinutes(16)) == null, "expired drops must be refreshed");
        Assert(index.Choose("A", 2, monsters, "R2", utc) == null, "unreported materials must have no inferred source");
        var session = new MaterialDiscoverySession(index, "A", "map", monsters, 0, utc);
        Assert(session.Next(0, utc) == 0 && session.Finished, "fresh cached lists should avoid requests");
        var fresh = new MaterialSourceIndex(); session = new MaterialDiscoverySession(fresh, "A", "map", monsters, 0, utc);
        int first = session.Next(0, utc); Assert(first == 10 && session.Next(.1, utc) == 0, "requests must be sequential");
        fresh.Record("A", 10, [], utc); Assert(session.Next(.2, utc) == 0, "response completion must still respect rate limit");
        Assert(session.Next(1, utc) == 20, "next type requested after pacing interval");
        Assert(session.Next(5, utc) == 0 && session.Next(6, utc) == 30, "unanswered type must time out and continue");
        session.Next(100, utc); Assert(session.Finished, "search must have bounded duration");
        var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<MaterialSourceIndex>(Newtonsoft.Json.JsonConvert.SerializeObject(index))!;
        Assert(saved.Choose("A", 1, monsters, "R2", utc)!.Id == 10, "cached source data must survive serialization");
        Console.WriteLine("PASS: source selection, no invented sources, server isolation, expiry, cache reload, request pacing, negative results, timeouts and bounded discovery");
    }
    static void CatalogChecks()
    {
        string game = Path.Combine(Path.GetTempPath(), "BeyondCatalog-" + Guid.NewGuid().ToString("N")); string dir = Path.Combine(game, "UserData", "Beyond"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "questdb.json"), "{quests:{154:{name:'Talk to the Shadow Spirit',map:'bludrut4',frame:'Enter',prevQuest:153,objectives:[{qty:1,name:'Talk to the Shadow Spirit'}]},153:{name:'Earlier quest'}}}");
        var catalog = new QuestAuthoringCatalog(game); Assert(catalog.Name(154) == "Talk to the Shadow Spirit", "quest IDs must resolve to names");
        string explanation = catalog.Describe(new BotStepDraft { QuestId = 154, Area = "bludrut4", Room = "Enter" }, BotScriptDraft.QuestChain);
        Assert(explanation.Contains("Talk to the Shadow Spirit") && explanation.Contains("Earlier quest") && explanation.Contains("already completed") && explanation.Contains("1 x"), "step review must explain objectives, prerequisite, and skip behavior");
        Assert(catalog.Name(999999).Contains("unavailable"), "unknown names must be labelled honestly");
        var fresh = new QuestAuthoringCatalog(game, [new(154, "Updated quest name")]); Assert(fresh.Get(154)!.Area == "bludrut4" && fresh.Name(154) == "Updated quest name", "live names must retain local route data");
        Console.WriteLine("PASS: quest names, objectives, prerequisites, automation explanation, unknown metadata and live name overrides");
    }
    static void WriterFormChecks()
    {
        string path = Path.Combine(Path.GetTempPath(), "BeyondWriterImport-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{_doc:'keep',First:{template:'quest-chain',class:'Rogue',entries:[{qid:1,frame:'R2',pad:'Left'},{qid:3}]},Second:{template:'quest-chain',entries:[{qid:2}]}}");
        var window = new BotWriterWindow(); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object?[] args) => typeof(BotWriterWindow).GetMethod(name, flags)!.Invoke(window, args);
        T Field<T>(string name) => (T)typeof(BotWriterWindow).GetField(name, flags)!.GetValue(window)!;
        Call("LoadFile", path, "First");
        Assert(Field<TextBox>("_name").Text == "First" && !Field<ComboBox>("_type").IsEnabled, "opening must populate the selected activity");
        Field<TextBox>("_quest").Text = "invalid"; Dispatcher.UIThread.RunJobs(); var list = Field<ListBox>("_list"); list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert(list.SelectedIndex == 0 && Field<TextBox>("_quest").Text == "invalid", "invalid edits must remain visible when switching steps");
        Field<TextBox>("_quest").Text = "1";
        Field<TextBox>("_room").Text = "R7"; Call("ApplyEditor");
        var edited = (BotScriptDraft)Call("CaptureDraft")!; var json = edited.ToJson();
        Assert((string?)json["First"]!["entries"]![0]!["frame"] == "R7" && (string?)json["First"]!["entries"]![0]!["pad"] == "Left", "editing a field must preserve hidden step settings");
        Field<ComboBox>("_activities").SelectedItem = "Second";
        var document = Field<JObject>("_document");
        Assert((string?)document["First"]!["entries"]![0]!["frame"] == "R7" && (string?)document["_doc"] == "keep" && document["Second"] != null, "switching activities must retain edits and sibling data");
        Console.WriteLine("PASS: GUI import, step editing, hidden settings and multi-activity preservation");
    }
    static void WriterChecks()
    {
        BotScriptDraft Draft(string type) => new() { Name = "Test", Template = type, ShopId = 2797, ShopMap = "mystcroft", Steps = [new() { Label = "Reward", QuestId = 283, Monster = "Skeleton", Item = "104739", Area = "mystcroft", Room = "R2", Quantity = 100 }] };
        var switching = Draft(BotScriptDraft.MergeShop); switching.Steps[0].OriginalEntry = JObject.Parse("{hunt:['Old monster'],item:'Old material'}");
        var switchedQuest = switching.ToJson();
        Assert(switchedQuest["Test"]!["farm"]![0]!["hunt"] == null && (int?)switchedQuest["Test"]!["farm"]![0]!["qid"] == 283, "switching a merge route to a quest must remove the old hunt");
        var automatic = new BotScriptDraft { Name = "Automatic merge", Template = BotScriptDraft.MergeShop, ShopId = 2797, ShopMap = "mystcroft" };
        Assert(((JArray)automatic.ToJson()["Automatic merge"]!["farm"]!).Count == 0, "merge scripts must save without authored material routes");
        var chain = Draft(BotScriptDraft.QuestChain).ToJson(); Assert((int?)chain["Test"]!["entries"]![0]!["qid"] == 283, "chain order and quest ID must be emitted");
        var choices = Draft(BotScriptDraft.QuestRewards).ToJson(); Assert(choices["Test"]!["options"]![0]!["farm"] != null && choices["Test"]!["entries"] == null, "independent quests must remain selectable");
        var merge = Draft(BotScriptDraft.MergeShop).ToJson(); Assert(merge["Test"]!["farm"]![0]!["until"]!["qty"] == null && (int?)merge["Test"]!["shop"]!["shop"] == 2797, "merge goal must derive quantities from selected rewards");
        var drops = Draft(BotScriptDraft.MonsterDrops).ToJson(); var farm = (JArray)drops["Test"]!["options"]![0]!["farm"]!; var parsed = QuestChains.ParseEntries(farm); Assert(parsed[0].isHunt && parsed[0].goalQty == 100 && parsed[0].mons[0] == "Skeleton", "drop drafts must parse into real hunt goals");
        var invalid = Draft(BotScriptDraft.MonsterDrops); invalid.Steps[0].Item = ""; bool rejected = false; try { invalid.ToJson(); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, "missing drop targets must not save");
        foreach (string type in new[] { BotScriptDraft.QuestChain, BotScriptDraft.QuestRewards, BotScriptDraft.MergeShop, BotScriptDraft.MonsterDrops })
        {
            var original = Draft(type).ToJson(); original["Test"]!["class"] = "Rogue";
            var loaded = BotScriptDraft.FromJson(original, "Test"); loaded.Steps[0].Room = "R3";
            var edited = loaded.ToJson(); Assert((string?)edited["Test"]!["class"] == "Rogue", "editing must preserve script metadata");
            var again = BotScriptDraft.FromJson(edited, "Test"); Assert(again.Steps[0].Room == "R3" && again.Template == type, "existing scripts must reopen with edited values");
        }
        var complex = Newtonsoft.Json.Linq.JObject.Parse("{Multi:{template:'quest-rewards',options:[{name:'Composite',farm:[{qid:1},{qid:2}]}]}}");
        bool protectedFile = false; try { BotScriptDraft.FromJson(complex); } catch (ArgumentException) { protectedFile = true; }
        Assert(protectedFile, "unsupported compound choices must be refused rather than truncated");
        Console.WriteLine("PASS: existing-script imports and metadata preservation; four writer templates, selectable rewards, automatic merge amounts, real parser round trip and validation");
    }
    static void DeploymentChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "BeyondDeployChecks-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "launcher"), game = Path.Combine(root, "game"), managed = Path.Combine(game, "Managed");
        Directory.CreateDirectory(source); Directory.CreateDirectory(managed);
        foreach (string name in new[] { "BeyondAgent.dll", "0Harmony.dll" })
        {
            File.WriteAllText(Path.Combine(source, name), "new-" + name);
            File.WriteAllText(Path.Combine(managed, name), "old-managed");
            File.WriteAllText(Path.Combine(game, name), "old-root");
        }
        AgentDeployment.Install(source, game, managed);
        foreach (string name in new[] { "BeyondAgent.dll", "0Harmony.dll" })
        {
            Assert(File.ReadAllText(Path.Combine(game, name)) == "new-" + name, "legacy root copy must be replaced");
            Assert(File.ReadAllText(Path.Combine(managed, name)) == "new-" + name, "Managed copy must match");
            Assert(Directory.GetFiles(game, name + ".backup-*").Length == 1, "root backup must be preserved");
        }
        AgentDeployment.Install(source, game, managed);
        Assert(Directory.GetFiles(game, "*.backup-*").Length == 2, "unchanged deployment must not create more backups");
        string clean = Path.Combine(root, "clean"); Directory.CreateDirectory(clean);
        AgentDeployment.Install(source, clean, managed);
        Assert(!File.Exists(Path.Combine(clean, "BeyondAgent.dll")), "clean installs must not gain unnecessary root copies");
        AgentDeployment.Install(source, source, managed);
        Console.WriteLine("PASS: legacy root shadow repaired, Managed synchronized, backups retained, repeat/self-copy and clean installs safe");
    }
    static void DropChecks()
    {
        var window = new DropFarmWindow(new ModConnection("OfflineDropChecks"));
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Receive(string name, JObject data) => typeof(DropFarmWindow).GetMethod(name, flags)!.Invoke(window, [data]);
        var picker = (ComboBox)typeof(DropFarmWindow).GetField("_monsters", flags)!.GetValue(window)!;
        var rows = (StackPanel)typeof(DropFarmWindow).GetField("_drops", flags)!.GetValue(window)!;
        JObject? Plan() => (JObject?)typeof(DropFarmWindow).GetMethod("BuildPlan", flags)!.Invoke(window, null);
        Receive("OnMonsters", JObject.Parse("{Area:'mystcroft',Monsters:[{Id:534,Name:'Skeleton',Frame:'R12'},{Id:545,Name:'Red Skeleton',Frame:'R10'}]}"));
        picker.SelectedIndex = 0;
        Receive("OnDrops", JObject.Parse("{MonsterId:545,Items:[{Id:9,Name:'Wrong monster',Owned:0,StackSize:1}]}"));
        Assert(rows.Children.Count == 0, "late replies for another monster must not populate current drops");
        Receive("OnDrops", JObject.Parse("{MonsterId:534,Items:[{Id:104739,Name:'Corn',Owned:20,StackSize:9999},{Id:8,Name:'Sword',Owned:0,StackSize:1}]}"));
        Assert(rows.Children.Count == 2 && Plan() == null, "drops must load with nothing selected");
        var first = (Grid)rows.Children[0]; ((CheckBox)first.Children[0]).IsChecked = true; ((NumericUpDown)first.Children[1]).Value = 100;
        var plan = Plan()!; var farm = (JArray)plan["Farm"]!;
        Assert(farm.Count == 1 && (string?)farm[0]!["item"] == "104739" && (int?)farm[0]!["qty"] == 100, "only selected drops and inventory target should be sent");
        Assert((string?)farm[0]!["area"] == "mystcroft" && (string?)farm[0]!["frame"] == "R12" && (string?)farm[0]!["hunt"]![0] == "534", "map, room, monster and item IDs must resolve automatically");
        picker.SelectedIndex = 1;
        Assert(rows.Children.Count == 0 && Plan() == null, "changing monster must clear prior selections");
        Console.WriteLine("PASS: live-list selection, stale reply filtering, empty selection, automatic route/IDs, quantities and monster switching");
    }
    static void Assert(bool test, string why) { if (!test) throw new Exception(why); }
    static void Checks()
    {
        Entity.myPlayerData.playerInventory.Bag = [new() { ID = 1, Name = "Corn", Quantity = 40 }, new() { ID = 10, Name = "Pet", Quantity = 1 }];
        Entity.myPlayerData.playerInventory.Bank = [new() { ID = 1, Name = "Corn", Quantity = 999 }];
        Assert(ItemGoals.Count(" corn ") == 40 && ItemGoals.Count("1") == 40, "inventory targets must exclude bank and resolve names/IDs");
        var shop = new shopData { items = [new() { ID = 10, Name = "Pet", TokID = 1, TokName = "Corn", TokQty = 150 }, new() { ID = 11, Name = "Hat", TokID = 1, TokName = "Corn", TokQty = 30 }] };
        var plan = QuestChains.BuyPlan.Parse(JObject.Parse("{shop:2797,items:[{item:'10',qty:1},{item:'11',qty:2}]}"));
        var needs = ItemGoals.Requirements(shop, plan);
        Assert(needs["1"] == 60 && needs["Corn"] == 60, "owned pet must be excluded; two hats share one aggregate material goal");
        plan.items = [("11", 2), ("Hat", 2)];
        Assert(ItemGoals.Requirements(shop, plan)["1"] == 60, "duplicate reward aliases must represent one desired total");
        Assert(!ItemGoals.CanAfford(shop.items[0], out _), "insufficient materials must prevent purchase");
        Entity.myPlayerData.playerInventory.Bag[0].Quantity = 150;
        Assert(ItemGoals.CanAfford(shop.items[0], out _), "exact material count must allow purchase");
        var acItem = new ShopItem { ID = 9001, Name = "AC Cape", Coins = true, Cost = 500 };
        Assert(!ItemGoals.CanAfford(acItem, out string acMissing) && acMissing.Contains("AC"), "AC-priced items must never be bought");
        plan.items = [("10", 2), ("11", 1)]; needs = ItemGoals.Requirements(shop, plan);
        Assert(needs["1"] == 180, "remaining pet plus hat must add costs across selected rewards");
        var entries = QuestChains.ParseEntries(JArray.Parse("[{hunt:['Boss'],item:'Sword',qty:1},{qid:283,until:{item:'Pet',qty:1}}]"));
        Assert(entries.Count == 2 && entries[0].isHunt && entries[0].goalQty == 1 && entries[1].goalItem == "Pet", "drop and quest templates must preserve goals");
        typeof(QuestChains).GetMethod("MergeFromJson", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["{ShopOnly:{buy:{shop:2797,items:[{item:'11'}]}}}", "test"]);
        typeof(QuestChains).GetMethod("MergeFromJson", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["{UnsafeShortcut:{template:'merge-shop',farm:[{hunt:['Boss'],item:'Corn'}],shop:{shop:1}}}", "test"]);
        Assert(QuestChains.Get("UnsafeShortcut") == null, "legacy chain shortcuts must not bypass template reward choices");
        Assert(QuestChains.GetBuy("ShopOnly") != null, "buy-only plans must remain valid");
        Console.WriteLine("PASS: inventory/bank counts, shared materials, owned rewards, affordability, hunt goals, buy-only plans");
    }
}
public class PreviewApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
            var main = new Window { Title = "Farming UI checks", Width = 340, Height = 230, Content = panel };
            var quest = new Button { Content = "Quest reward picker" }; quest.Click += async (_, _) =>
            {
                var opts = JArray.Parse("[{name:'Manticore of Infinity',description:'Only the selected pet quest runs.',farm:[{qid:283}]},{name:'Manticore of Light',farm:[{qid:284}]},{name:'Manticore of Malice',farm:[{qid:285}]},{name:'Manticore of Darkness',farm:[{qid:286}]}]");
                var result = await new FarmingTargetWindow("Aranx Pets", opts).ShowDialog<JArray?>(main); Console.WriteLine("Choices: " + result?.Count);
            };
            var merge = new Button { Content = "Merge reward picker (fixture)" }; merge.Click += async (_, _) =>
            {
                var dialog = new MergeFarmWindow(new ModConnection("FarmingUiChecks"), JObject.Parse("{shop:2797,area:'mystcroft'}"));
                dialog.Opened += (_, _) =>
                {
                    var items = new JArray(); for (int i = 1; i <= 36; i++) items.Add(new JObject { ["Id"] = i, ["Name"] = i == 1 ? "Lil Enraged Gourdthulu" : "Mogloween reward " + i, ["Owned"] = i == 2 ? 1 : 0, ["Costs"] = new JArray(new JObject { ["Id"] = 104739, ["Name"] = "Mogloween Candy Corn", ["Quantity"] = i == 1 ? 1500 : 30 }) });
                    typeof(MergeFarmWindow).GetMethod("OnShop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [new JObject { ["Shop"] = 2797, ["Name"] = "Myx's Candycorn Shop", ["Items"] = items }]);
                };
                var result = await dialog.ShowDialog<JObject?>(main); Console.WriteLine("Merge selections: " + result);
            };
            var drops = new Button { Content = "Boss drop / material farm" }; drops.Click += async (_, _) => { var result = await new DropFarmWindow(new ModConnection("FarmingUiChecks")).ShowDialog<JObject?>(main); Console.WriteLine("Drop plan: " + result); };
            var writer = new Button { Content = "Bot writer" }; writer.Click += async (_, _) =>
            {
                string fixture = Path.Combine(Path.GetTempPath(), "BeyondWriterVisual-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(fixture, "{'Bludrut Keep':{template:'quest-chain',entries:[{qid:157,area:'bludrut',frame:'Enter'},{qid:154,area:'bludrut4',frame:'Enter'}]}}");
                await new BotWriterWindow(initialPath: fixture).ShowDialog<string?>(main);
            };
            panel.Children.Add(quest); panel.Children.Add(merge); panel.Children.Add(drops); panel.Children.Add(writer); main.Height = 300; desktop.MainWindow = main;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
