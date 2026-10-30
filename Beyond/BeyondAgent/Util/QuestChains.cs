using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeyondAgent.Util
{
    /// <summary>
    /// Named ordered quest chains (e.g. "Lair" → [q19, q20, …]). Loaded from
    /// the embedded `Data/chains.json` bootstrap, then every downloaded library
    /// script (`UserData/Beyond/Infinity-Files/Scripts/*.json`), then the
    /// user-editable override at `UserData/Beyond/chains.json`. Later sources
    /// win chain-by-chain, so a library script is runnable as soon as it's on
    /// disk (no import step) and the user can still override any of them.
    /// A chain may also carry a "farm" list — repeatable quests the runner
    /// loops once the chain itself is done.
    /// </summary>
    public static class QuestChains
    {
        public class Entry
        {
            public int qid;
            public string area = "";   // "" = stay in current area (no tfer)
            public string frame = "";  // "" = stay in current frame (no moveToCell)
            public string pad = "Spawn";
            public int items = 1;
            // Target monster filter for the hunt: display names (case-insensitive
            // substring match) and/or numeric catalog ids. Empty = any hostile in
            // frame. Names make the same entry work on live AQW, where the client
            // never sees the server's kill-credit RefIDs.
            public List<string> mons = [];

            // Hunt step ("hunt": [monsters]): no quest — kill `mons` for drops
            // of GoalItem until the inventory holds GoalQty. Quest steps can
            // carry the same goal via "until": {item, qty} to stop farming that
            // quest once its reward is stocked. GoalQty < 0 = "enough for the
            // script's buy list" (derived from the shop's prices at run time).
            public bool isHunt;
            public string goalItem = "";
            public int goalQty = -1;

            public bool HasGoal => goalItem.Length > 0;

            // Comma-joined view for the editors (both directions).
            public string MonText
            {
                get => string.Join(", ", mons);
                set => mons = [.. (value ?? "")
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)];
            }

            public override string ToString()
            {
                string loc = "";
                if (!string.IsNullOrEmpty(area))
                {
                    loc = $" @ {area}/{frame}/{pad}";
                }
                else if (!string.IsNullOrEmpty(frame))
                {
                    loc = $" @ {frame}/{pad}";
                }

                string goal = HasGoal ? $" until {(goalQty >= 0 ? goalQty.ToString() : "enough")} {goalItem}" : "";
                return isHunt
                    ? $"hunt {MonText}{loc}{goal}"
                    : $"q{qid}{loc}" + (items > 1 ? $" ×{items}" : "") + goal;
            }
        }

        // "buy": what to purchase from a shop once farming is done. Items are
        // matched by name (case-insensitive) or numeric item id.
        public class BuyPlan
        {
            public bool autoDiscover = true;
            public int shop;
            public string area = "";   // optional: tfer here before loading the shop
            public List<(string item, int qty)> items = [];

            public static BuyPlan Parse(JToken t)
            {
                if (t is not JObject o || ((int?)o["shop"] ?? 0) <= 0)
                {
                    return null;
                }
                BuyPlan plan = new() { shop = (int)o["shop"], area = (string)o["area"] ?? "", autoDiscover = (bool?)o["autoDiscover"] ?? true };
                if (o["items"] is JArray arr)
                {
                    foreach (JToken it in arr)
                    {
                        string name = ((string)it["item"] ?? "").Trim();
                        if (name.Length > 0)
                        {
                            plan.items.Add((name, Math.Max(1, (int?)it["qty"] ?? 1)));
                        }
                    }
                }
                return plan.items.Count > 0 ? plan : null;
            }
        }

        // Chain name -> buy plan (optional; object form "buy").
        public static readonly Dictionary<string, BuyPlan> Buys = [];

        public static BuyPlan GetBuy(string name)
        {
            return (name != null && Buys.TryGetValue(name, out BuyPlan b)) ? b : null;
        }

        // Insertion-order map so the dropdown lists them in a stable order.
        public static readonly Dictionary<string, List<Entry>> All = [];

        // Chain name -> recommended class/skillset (optional; from the object form).
        public static readonly Dictionary<string, string> Classes = [];

        // Chain name -> farm entries looped after the chain (optional; object form).
        public static readonly Dictionary<string, List<Entry>> Farms = [];

        // Chain name -> where the winning definition came from: "bootstrap",
        // "library" or "user". Lets the launcher label library scripts.
        public static readonly Dictionary<string, string> Sources = [];

        public static IEnumerable<string> Names => All.Keys;

        public static string LibraryScriptsDirectory =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BEYOND_BOTS"))
                ? Environment.GetEnvironmentVariable("BEYOND_BOTS")
                : Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "Infinity-Files", "Scripts");

        public static List<Entry> Get(string name)
        {
            return (name != null && All.TryGetValue(name, out List<Entry> list)) ? list : null;
        }

        public static List<Entry> GetFarm(string name)
        {
            return (name != null && Farms.TryGetValue(name, out List<Entry> list)) ? list : null;
        }

        public static void Init()
        {
            try
            {
                All.Clear();          // wipe stale state so deletes/renames take effect
                Classes.Clear();
                Farms.Clear();
                Buys.Clear();
                Sources.Clear();
                LoadEmbedded();
                LoadLibrary();
                LoadUserOverride();
                BeyondLog.Msg($"[QuestChains] loaded {All.Count} chain(s): {string.Join(", ", All.Keys)}");
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[QuestChains] init failed: {ex.Message}");
            }
        }

        private static void LoadEmbedded()
        {
            using Stream s = typeof(QuestChains).Assembly
                .GetManifestResourceStream("BeyondAgent.Data.chains.json");
            if (s == null) { BeyondLog.Warning("[QuestChains] embedded chains.json missing"); return; }
            using StreamReader r = new(s);
            MergeFromJson(r.ReadToEnd(), source: "bootstrap");
        }

        // Downloaded community scripts (Library window / auto-update). Each file
        // parses on its own so one bad script can't hide the rest.
        private static void LoadLibrary()
        {
            string dir = LibraryScriptsDirectory;
            if (!System.IO.Directory.Exists(dir))
            {
                return;
            }

            foreach (string file in System.IO.Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    MergeFromJson(File.ReadAllText(file), source: "library");
                }
                catch (Exception ex)
                {
                    BeyondLog.Error($"[QuestChains] library script {Path.GetFileName(file)} parse failed: {ex.Message}");
                }
            }
        }

        private static void LoadUserOverride()
        {
            // The launcher migrates legacy overrides into its central bots folder.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BEYOND_BOTS"))) return;
            string path = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "chains.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                MergeFromJson(File.ReadAllText(path), source: "user");
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[QuestChains] user override parse failed: {ex.Message}");
            }
        }

        private static void MergeFromJson(string json, string source)
        {
            JObject obj = JObject.Parse(json);
            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Name.StartsWith("_"))
                {
                    continue; // skip _doc and similar
                }

                // A chain value is either a bare array of entries (legacy) or an
                // object { "class": "...", "entries": [...], "farm": [...] } that
                // also names a recommended class/skillset and the repeatable
                // quests to farm once the chain is done.
                JArray arr;
                JArray farmArr = null;
                BuyPlan buy = null;
                string cls = null;
                if (prop.Value is JArray bareArr)
                {
                    arr = bareArr;
                }
                else if (prop.Value is JObject objVal)
                {
                    // These templates need the launcher's reward/drop picker.
                    // Do not expose a partial plan through legacy RunChain.
                    string template = (string)objVal["template"];
                    if (template != null && template != "quest-chain") continue;
                    arr = objVal["entries"] as JArray;
                    farmArr = objVal["farm"] as JArray;
                    buy = BuyPlan.Parse(objVal["buy"]);
                    cls = (string)objVal["class"];
                }
                else
                {
                    continue;
                }
                if (arr == null && farmArr == null && buy == null)
                {
                    continue;
                }
                arr ??= [];

                List<Entry> entries = ParseEntries(arr);
                // A farm-only script (empty "entries", non-empty "farm") is valid.
                if (entries.Count > 0 || (farmArr != null && ParseEntries(farmArr).Count > 0) || buy != null)
                {
                    All[prop.Name] = entries;
                    Sources[prop.Name] = source;
                    if (!string.IsNullOrEmpty(cls))
                    {
                        Classes[prop.Name] = cls;
                    }
                    else
                    {
                        Classes.Remove(prop.Name);
                    }
                    List<Entry> farm = farmArr != null ? ParseEntries(farmArr) : [];
                    if (farm.Count > 0)
                    {
                        Farms[prop.Name] = farm;
                    }
                    else
                    {
                        Farms.Remove(prop.Name);
                    }
                    if (buy != null)
                    {
                        Buys[prop.Name] = buy;
                    }
                    else
                    {
                        Buys.Remove(prop.Name);
                    }
                    BeyondLog.Msg($"[QuestChains] {source}: {prop.Name} ({entries.Count} entries"
                        + (farm.Count > 0 ? $", {farm.Count} farm)" : ")"));
                }
            }
        }

        // Also used for scripts the launcher sends inline (RunScript).
        public static List<Entry> ParseEntries(JArray arr)
        {
            List<Entry> entries = [];
            foreach (JToken elem in arr)
            {
                if (elem is not JObject e)
                {
                    continue;
                }

                int qid = (int?)e["qid"] ?? 0;
                bool isHunt = e["hunt"] != null;
                if (qid <= 0 && !isHunt)
                {
                    continue;
                }

                Entry entry = new()
                {
                    qid = isHunt ? 0 : qid,
                    isHunt = isHunt,
                    area = (string)e["area"] ?? "",
                    frame = (string)e["frame"] ?? "",
                    pad = string.IsNullOrEmpty((string)e["pad"]) ? "Spawn" : (string)e["pad"],
                    items = Math.Max(1, (int?)e["items"] ?? (int?)e["iters"] ?? 1),
                };
                // Goal: a hunt's own "item"/"qty", or a quest's "until".
                JToken goal = isHunt ? e : e["until"];
                if (goal is JObject g)
                {
                    entry.goalItem = ((string)g["item"] ?? "").Trim();
                    entry.goalQty = (int?)g["qty"] ?? -1;
                }

                // "mon" (or a hunt's "hunt") is an array of names/ids or one comma string.
                if (isHunt)
                {
                    e = (JObject)e.DeepClone();
                    e["mon"] = e["hunt"];
                }
                if (e["mon"] is JArray monArr)
                {
                    foreach (JToken m in monArr)
                    {
                        string s = ((string)m ?? "").Trim();
                        if (s.Length > 0)
                        {
                            entry.mons.Add(s);
                        }
                    }
                }
                else if (e["mon"] != null)
                {
                    entry.MonText = (string)e["mon"];
                }
                if (entry.isHunt && (!entry.HasGoal || entry.mons.Count == 0))
                    throw new FormatException("A drop farm needs a monster and an inventory item target.");
                if (entry.goalQty < -1)
                    throw new FormatException("Inventory target quantity must be zero or greater, or omitted for merge requirements.");
                entries.Add(entry);
            }
            return entries;
        }
    }
}
