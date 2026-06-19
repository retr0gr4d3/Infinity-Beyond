using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeyondAgent.Util
{
    /// <summary>
    /// Passive catalog of music tracks the game has loaded. Fed by
    /// MusicHarvestPatch (postfix on BGMusicManager.AddTrack), which fires
    /// for every track the game registers — area BGM, cutscene stings, our
    /// own Jukebox loads. Persisted to UserData/Beyond/music.json.
    /// Keyed by track ID. Each entry carries name + length so the Jukebox
    /// dropdown can render rich rows.
    /// </summary>
    public static class MusicCatalog
    {
        public class TrackEntry
        {
            public int id;
            public string name;
            public float length; // seconds
            public string prefab;
        }

        public static readonly Dictionary<int, TrackEntry> Tracks = [];

        // Highest known soundtrack ID — we pre-seed the in-memory dict
        // 1..SeedMax with placeholder entries so the Jukebox dropdown shows
        // every slot. Names/lengths fill in as the harvest patch discovers
        // them. Bumped a bit above the 318 the dev mentioned for headroom.
        public const int SeedMax = 350;

        private static string _filePath;
        private static bool _dirty;
        private static readonly object _lock = new();

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(dir);
                _filePath = Path.Combine(dir, "music.json");
                if (File.Exists(_filePath))
                {
                    JArray arr = JArray.Parse(File.ReadAllText(_filePath));
                    foreach (JToken t in arr)
                    {
                        if (t is JObject e)
                        {
                            TrackEntry entry = e.ToObject<TrackEntry>();
                            if (entry?.id > 0)
                            {
                                Tracks[entry.id] = entry;
                            }
                        }
                    }
                }
                // Pre-seed empty slots 1..SeedMax so the picker always shows
                // every ID. These placeholders aren't persisted (Save only
                // writes entries the harvest has filled in) — they exist
                // purely for UI completeness. A later harvest upgrades the
                // entry with real name + length and marks it dirty.
                int seeded = 0;
                for (int id = 1; id <= SeedMax; id++)
                {
                    if (!Tracks.ContainsKey(id))
                    {
                        Tracks[id] = new TrackEntry { id = id, name = "", length = 0f, prefab = "" };
                        seeded++;
                    }
                }
                BeyondLog.Msg($"[MusicCatalog] loaded {Tracks.Count - seeded} named tracks ({seeded} unseen placeholders) from {_filePath}");
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[MusicCatalog] init failed: {ex.Message}");
            }
        }

        public static void Save()
        {
            if (!_dirty || _filePath == null)
            {
                return;
            }

            try
            {
                lock (_lock)
                {
                    // Only persist entries the harvest has filled in —
                    // placeholders (empty name + zero length) are synthetic.
                    List<TrackEntry> payload = [.. Tracks.Values
                        .Where(e => !string.IsNullOrEmpty(e.name) || e.length > 0f)
                        .OrderBy(e => e.id)];
                    File.WriteAllText(_filePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
                    _dirty = false;
                    BeyondLog.Msg($"[MusicCatalog] saved {Tracks.Count} tracks");
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[MusicCatalog] save failed: {ex.Message}");
            }
        }

        public static void Record(int id, string name, float length, string prefab)
        {
            if (id <= 0)
            {
                return;
            }

            lock (_lock)
            {
                if (Tracks.TryGetValue(id, out TrackEntry existing))
                {
                    // Sticky-name: don't blank an already-named entry.
                    if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(existing.name))
                    {
                        name = existing.name;
                    }

                    if (existing.name == name && Math.Abs(existing.length - length) < 0.01f
                        && existing.prefab == (prefab ?? ""))
                    {
                        return;
                    }
                }
                Tracks[id] = new TrackEntry
                {
                    id = id,
                    name = name ?? "",
                    length = length,
                    prefab = prefab ?? "",
                };
                _dirty = true;
            }
            BeyondAgentClass.activeInstance?.SendCatalogs();
        }

        public static void Clear()
        {
            int removed;
            lock (_lock) { removed = Tracks.Count; Tracks.Clear(); _dirty = true; }
            Save();
            BeyondLog.Msg($"[MusicCatalog] cleared {removed} tracks");
        }
    }
}