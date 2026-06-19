using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace BeyondAgent.Util
{
    /// <summary>
    /// Named ordered quest chains (e.g. "Lair" → [q19, q20, …]). Loaded from
    /// the embedded `Data/chains.json` bootstrap, then merged with a
    /// user-editable override at `UserData/Beyond/chains.json`. The user file
    /// wins entry-by-entry so collaborators can fix mistakes in the bootstrap
    /// without rebuilding the mod.
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

                return $"q{qid}{loc}" + (items > 1 ? $" ×{items}" : "");
            }
        }

        // Insertion-order map so the dropdown lists them in a stable order.
        public static readonly Dictionary<string, List<Entry>> All = [];

        public static IEnumerable<string> Names => All.Keys;

        public static List<Entry> Get(string name)
        {
            return (name != null && All.TryGetValue(name, out List<Entry> list)) ? list : null;
        }

        public static void Init()
        {
            try
            {
                All.Clear();          // wipe stale state so deletes/renames take effect
                LoadEmbedded();
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

        private static void LoadUserOverride()
        {
            string path = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "chains.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                MergeFromJson(File.ReadAllText(path), source: $"user:{path}");
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

                if (prop.Value is not JArray arr)
                {
                    continue;
                }

                List<Entry> entries = [];
                foreach (JToken elem in arr)
                {
                    if (elem is not JObject e)
                    {
                        continue;
                    }

                    int qid = (int?)e["qid"] ?? 0;
                    if (qid <= 0)
                    {
                        continue;
                    }

                    entries.Add(new Entry
                    {
                        qid = qid,
                        area = (string)e["area"] ?? "",
                        frame = (string)e["frame"] ?? "",
                        pad = string.IsNullOrEmpty((string)e["pad"]) ? "Spawn" : (string)e["pad"],
                        items = Math.Max(1, (int?)e["items"] ?? (int?)e["iters"] ?? 1),
                    });
                }
                if (entries.Count > 0)
                {
                    All[prop.Name] = entries;
                    BeyondLog.Msg($"[QuestChains] {source}: {prop.Name} ({entries.Count} entries)");
                }
            }
        }
    }
}
