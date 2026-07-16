using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace BeyondAgent.Util
{
    /// <summary>
    /// <para>
    /// The server-compiled quest knowledge base: for every quest, which monsters
    /// credit each objective and where they stand (map + frame), resolved by the
    /// SERVER from its own placement + drop + kill-credit tables (GET
    /// data/questdb, see InfinityServer server/questdb.py). This is what lets the
    /// runner hunt any quest without a hand-authored chain entry.
    /// </para>
    /// <para>
    /// Fetched from <c>Main.WebApiURL + "data/questdb"</c> — the same base URL
    /// every game REST call uses, so it follows whatever server the client is
    /// pointed at. On a server without the endpoint (live AE) the fetch fails
    /// quietly and the DB stays empty; the runner then behaves exactly as before
    /// (manual chain entries). The last good payload is cached at
    /// UserData/Beyond/questdb.json and loaded on boot so the KB works offline.
    /// </para>
    /// </summary>
    public static class QuestDB
    {
        public class Location
        {
            public string Map = "";
            public string Frame = "";
            public int MonId;
            public int Count;
            public int Level;
        }

        public class Objective
        {
            public int QOID;
            public int Type;          // QuestObjectiveType: 0 Turnin, 1 Killcount, 2 Interact, 3 Talk, 4 Apop, 5 Cutscene
            public string Name = "";
            public int Qty;
            public int ItemId = -1;
            public string Via = "";
            public List<int> Monsters = [];
            public List<int> RefIds = [];
            public List<Location> Locations = [];
            public bool GlobalDrop;
            // Authored probabilistic drop roll (chance that a credited kill
            // advances the objective) — null means deterministic +1 per kill.
            public double? DropChance;

            public bool IsKill => Type == 1;
            public bool IsItemTurnin => Type == 0;
        }

        public class QuestInfo
        {
            public int Id;
            public string Name = "";
            public bool Once;
            public int PrevQuest = -1;
            public string Map = "";
            public string Frame = "";
            public string Pad = "";
            public string TurnInMap = "";
            public bool Huntable;
            public int Level;
            public List<Objective> Objectives = [];
        }

        public class MonsterInfo
        {
            public string Name = "";
            public int Level;
        }

        // Immutable snapshots swapped atomically after a load/refresh — game-thread
        // readers never see a half-built dictionary.
        private static Dictionary<int, QuestInfo> _quests = [];
        private static Dictionary<int, MonsterInfo> _monsters = [];
        private static readonly object _ioLock = new();
        private static int _refreshing;   // 0/1 via Interlocked — one fetch at a time

        public static string Status { get; private set; } = "not loaded";
        public static int Count => _quests.Count;

        public static bool TryGet(int qid, out QuestInfo q)
        {
            return _quests.TryGetValue(qid, out q);
        }

        public static MonsterInfo Monster(int monId)
        {
            return _monsters.TryGetValue(monId, out MonsterInfo m) ? m : null;
        }

        public static IEnumerable<QuestInfo> All => _quests.Values;

        private static string CacheFile =>
            Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "questdb.json");

        /// <summary>Load the on-disk cache, then kick a background refresh.</summary>
        public static void Init()
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    Parse(File.ReadAllText(CacheFile));
                    Status = $"cache: {Count} quests";
                    BeyondLog.Msg($"[QuestDB] loaded {Count} quests from cache");
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[QuestDB] cache load failed: {ex.Message}");
            }
            RefreshAsync();
        }

        /// <summary>
        /// Fetch data/questdb on a ThreadPool thread and swap the snapshot in.
        /// Safe to call repeatedly (spam-clicks collapse into one fetch).
        /// </summary>
        public static void RefreshAsync()
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            {
                return;
            }

            // Resolve the URL on the caller's (main) thread — Main.WebApiURL is
            // game state and shouldn't be touched from the pool thread.
            string baseUrl = null;
            try { baseUrl = Main.WebApiURL; } catch { }
            if (string.IsNullOrEmpty(baseUrl))
            {
                Status = "no WebApiURL yet";
                _refreshing = 0;
                return;
            }
            string url = baseUrl.TrimEnd('/') + "/data/questdb";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Status = "fetching…";
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 20000;
                    req.UserAgent = "BeyondAgent";
                    using WebResponse resp = req.GetResponse();
                    using StreamReader reader = new(resp.GetResponseStream());
                    string json = reader.ReadToEnd();
                    Parse(json);
                    lock (_ioLock)
                    {
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
                        File.WriteAllText(CacheFile, json);
                    }
                    Status = $"live: {Count} quests";
                    BeyondLog.Msg($"[QuestDB] refreshed {Count} quests from {url}");
                }
                catch (Exception ex)
                {
                    // Not an error state worth surfacing loudly: live AE (or an
                    // older server) simply doesn't have the endpoint.
                    Status = Count > 0 ? $"cache: {Count} quests (refresh failed)" : "unavailable";
                    BeyondLog.Msg($"[QuestDB] refresh failed ({ex.Message}) — keeping {Count} cached");
                }
                finally
                {
                    _refreshing = 0;
                }
            });
        }

        private static void Parse(string json)
        {
            JObject root = JObject.Parse(json);
            Dictionary<int, QuestInfo> quests = [];
            Dictionary<int, MonsterInfo> monsters = [];

            if (root["monsters"] is JObject mons)
            {
                foreach (JProperty p in mons.Properties())
                {
                    if (int.TryParse(p.Name, out int mid) && p.Value is JObject m)
                    {
                        monsters[mid] = new MonsterInfo
                        {
                            Name = (string)m["name"] ?? "",
                            Level = (int?)m["level"] ?? 1,
                        };
                    }
                }
            }

            if (root["quests"] is JObject qs)
            {
                foreach (JProperty p in qs.Properties())
                {
                    if (!int.TryParse(p.Name, out int qid) || p.Value is not JObject q)
                    {
                        continue;
                    }

                    QuestInfo info = new()
                    {
                        Id = qid,
                        Name = (string)q["name"] ?? "",
                        Once = (bool?)q["once"] ?? false,
                        PrevQuest = (int?)q["prevQuest"] ?? -1,
                        Map = (string)q["map"] ?? "",
                        Frame = (string)q["frame"] ?? "",
                        Pad = (string)q["pad"] ?? "",
                        TurnInMap = (string)q["turnInMap"] ?? "",
                        Huntable = (bool?)q["huntable"] ?? false,
                        Level = (int?)q["level"] ?? 0,
                    };
                    if (q["objectives"] is JArray objs)
                    {
                        foreach (JToken t in objs)
                        {
                            if (t is not JObject o)
                            {
                                continue;
                            }

                            Objective obj = new()
                            {
                                QOID = (int?)o["qoid"] ?? -1,
                                Type = (int?)o["type"] ?? 0,
                                Name = (string)o["name"] ?? "",
                                Qty = (int?)o["qty"] ?? 1,
                                ItemId = (int?)o["itemId"] ?? -1,
                                Via = (string)o["via"] ?? "",
                                GlobalDrop = (bool?)o["globalDrop"] ?? false,
                                DropChance = (double?)(o["drop"]?["chance"]),
                            };
                            if (o["monsters"] is JArray ms)
                            {
                                foreach (JToken m in ms)
                                {
                                    obj.Monsters.Add((int)m);
                                }
                            }
                            if (o["refIds"] is JArray rs)
                            {
                                foreach (JToken r in rs)
                                {
                                    obj.RefIds.Add((int)r);
                                }
                            }
                            if (o["locations"] is JArray locs)
                            {
                                foreach (JToken l in locs)
                                {
                                    obj.Locations.Add(new Location
                                    {
                                        Map = (string)l["map"] ?? "",
                                        Frame = (string)l["frame"] ?? "",
                                        MonId = (int?)l["monId"] ?? 0,
                                        Count = (int?)l["count"] ?? 0,
                                        Level = (int?)l["level"] ?? 1,
                                    });
                                }
                            }
                            info.Objectives.Add(obj);
                        }
                    }
                    quests[qid] = info;
                }
            }

            _monsters = monsters;
            _quests = quests;
        }

        /// <summary>
        /// The best place to hunt an objective: the (map, frame) holding the most
        /// target monsters, preferring the map the player is already in so we
        /// don't tfer across zones when the mob also spawns here.
        /// </summary>
        public static Location BestHuntSpot(Objective obj, string currentArea)
        {
            if (obj?.Locations == null || obj.Locations.Count == 0)
            {
                return null;
            }

            string here = BaseAreaName(currentArea);
            Location best = null;
            int bestScore = int.MinValue;
            foreach (Location l in obj.Locations)
            {
                int score = l.Count + (string.Equals(l.Map, here, StringComparison.OrdinalIgnoreCase) ? 1000 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = l;
                }
            }
            return best;
        }

        /// <summary>"lair-3" → "lair" (areas carry an instance suffix at runtime).</summary>
        public static string BaseAreaName(string area)
        {
            if (string.IsNullOrEmpty(area))
            {
                return "";
            }

            int dash = area.LastIndexOf('-');
            return dash > 0 && int.TryParse(area[(dash + 1)..], out _) ? area[..dash] : area;
        }
    }
}
