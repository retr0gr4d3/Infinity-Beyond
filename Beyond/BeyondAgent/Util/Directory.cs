using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeyondAgent.Util
{
    /// <summary>
    ///<para>Catalog of known quests and shops, used by the in-game browser so the
    /// user doesn't have to memorize numeric IDs. Two data sources merged:
    ///
    ///   1. Bootstrap — embedded `Data/quests.json` and `Data/shops.json`,
    ///      mined from prior packet captures. Ships with the DLL so the
    ///      catalog is useful on first launch.
    ///   2. Live — `BeyondEnv.UserDataDirectory/Beyond/directory.json`,
    ///      grown by AECPatch as new getQuests / loadShop responses arrive.
    ///      Persisted on close, reloaded on next launch.
    ///
    /// Lookup order: live overrides bootstrap, so a renamed-on-server quest
    /// shows the updated name once the player has seen it once.
    /// </para>
    /// </summary>
    public static class Directory
    {
        public class QuestEntry
        {
            public string name;
            public string storyline;
        }

        public class ShopEntry
        {
            public string name;
            public string location;
            public int item_count;
        }

        public static readonly Dictionary<int, QuestEntry> Quests = [];
        public static readonly Dictionary<int, ShopEntry> Shops = [];

        private static string _liveFilePath;
        private static bool _dirty;
        private static readonly object _lock = new();

        public static void Init()
        {
            try
            {
                LoadEmbedded("quests.json", LoadQuestsJson);
                LoadEmbedded("shops.json", LoadShopsJson);
                BeyondLog.Msg($"[Directory] bootstrap loaded: {Quests.Count} quests, {Shops.Count} shops");

                string userDir = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(userDir);
                _liveFilePath = Path.Combine(userDir, "directory.json");
                if (File.Exists(_liveFilePath))
                {
                    LoadLive(_liveFilePath);
                    BeyondLog.Msg($"[Directory] merged live override: now {Quests.Count} quests, {Shops.Count} shops");
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[Directory] init failed: {ex.Message}");
            }
        }

        public static void Save()
        {
            if (!_dirty || _liveFilePath == null)
            {
                return;
            }

            try
            {
                lock (_lock)
                {
                    var payload = new
                    {
                        quests = Quests.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                        shops = Shops.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    };
                    File.WriteAllText(_liveFilePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
                    _dirty = false;
                    BeyondLog.Msg($"[Directory] saved {Quests.Count} quests, {Shops.Count} shops to {_liveFilePath}");
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[Directory] save failed: {ex.Message}");
            }
        }

        // --- live-mining entry points (called from AECPatch) ---

        /// <summary>Record from a `getQuests` response — qdef is the per-quest JObject.</summary>
        public static void RecordQuest(int qid, JObject qdef)
        {
            if (qid <= 0 || qdef == null)
            {
                return;
            }

            QuestEntry entry = new()
            {
                name = (string)qdef["Name"],
                storyline = (string)(qdef["storylineData"] is JObject sd ? sd["Name"] : null),
            };
            lock (_lock)
            {
                if (Quests.TryGetValue(qid, out QuestEntry existing)
                    && existing.name == entry.name && existing.storyline == entry.storyline)
                {
                    return; // no change
                }

                Quests[qid] = entry;
                _dirty = true;
            }
            BeyondAgentClass.activeInstance?.SendCatalogs();
        }

        /// <summary>Record from a `loadShop` response — shop is the inner shop JObject.</summary>
        public static void RecordShop(JObject shop)
        {
            if (shop == null)
            {
                return;
            }

            int? sid = (int?)shop["shopID"];
            if (sid == null || sid.Value <= 0)
            {
                return;
            }

            JArray items = shop["items"] as JArray;
            ShopEntry entry = new()
            {
                name = (string)shop["Name"],
                location = (string)shop["Location"],
                item_count = items?.Count ?? 0,
            };
            lock (_lock)
            {
                if (Shops.TryGetValue(sid.Value, out ShopEntry existing)
                    && existing.name == entry.name && existing.item_count == entry.item_count)
                {
                    return;
                }

                Shops[sid.Value] = entry;
                _dirty = true;
            }
        }

        // --- loaders ---

        private static void LoadEmbedded(string fileName, Action<string> apply)
        {
            // Resource name follows csproj layout: namespace + folder + file
            string resName = $"BeyondAgent.Data.{fileName}";
            using Stream s = typeof(Directory).Assembly.GetManifestResourceStream(resName);
            if (s == null)
            {
                BeyondLog.Warning($"[Directory] embedded resource missing: {resName}");
                return;
            }
            using StreamReader r = new(s);
            apply(r.ReadToEnd());
        }

        private static void LoadQuestsJson(string json)
        {
            JObject obj = JObject.Parse(json);
            foreach (JProperty prop in obj.Properties())
            {
                if (!int.TryParse(prop.Name, out int qid))
                {
                    continue;
                }

                Quests[qid] = prop.Value.ToObject<QuestEntry>();
            }
        }

        private static void LoadShopsJson(string json)
        {
            JObject obj = JObject.Parse(json);
            foreach (JProperty prop in obj.Properties())
            {
                if (!int.TryParse(prop.Name, out int sid))
                {
                    continue;
                }

                Shops[sid] = prop.Value.ToObject<ShopEntry>();
            }
        }

        private static void LoadLive(string path)
        {
            JObject obj = JObject.Parse(File.ReadAllText(path));
            if (obj["quests"] is JObject qs)
            {
                foreach (JProperty p in qs.Properties())
                {
                    if (int.TryParse(p.Name, out int qid))
                    {
                        Quests[qid] = p.Value.ToObject<QuestEntry>();
                    }
                }
            }
            if (obj["shops"] is JObject ss)
            {
                foreach (JProperty p in ss.Properties())
                {
                    if (int.TryParse(p.Name, out int sid))
                    {
                        Shops[sid] = p.Value.ToObject<ShopEntry>();
                    }
                }
            }
        }
    }
}
