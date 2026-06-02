using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Infinity_TestMod.Util
{
    /// <summary>
    /// Passive catalog of gear seen on any character in the world. Fed by the
    /// harvester patches (e.g. ArmorHarvestPatch). Persisted to
    /// `MelonEnvironment.UserDataDirectory/AQWIB/items.json`. Keyed by bundle
    /// Filename so the same item across different character instances dedupes.
    /// Each entry carries the version triple so the URL builder can produce a
    /// correctly versioned CDN path when spoofing.
    /// </summary>
    public static class ItemCatalog
    {
        public class ItemEntry
        {
            public int id;
            public string name;
            public string slot;
            public string bundle;
            public string prefab;
            public int verC;
            public int verS;
            public int verL;
        }

        public static readonly Dictionary<string, ItemEntry> Armors = new();
        public static readonly Dictionary<string, ItemEntry> Helms = new();
        public static readonly Dictionary<string, ItemEntry> Backs = new();

        static string _liveFilePath;
        static bool _dirty;
        static readonly object _lock = new();

        /// <summary>
        /// Extract a friendly display name from a bundle filename when the
        /// server-side Name is empty. Bundle paths follow the pattern
        /// "&lt;dir&gt;/&lt;id&gt;_&lt;Name&gt;.unity3d" — pull the Name segment
        /// and return it; fall back to the basename if the pattern doesn't
        /// match.
        /// </summary>
        public static string ParseFriendlyName(string bundleFilename)
        {
            if (string.IsNullOrEmpty(bundleFilename)) return "";
            string basename = bundleFilename;
            int slash = basename.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < basename.Length) basename = basename.Substring(slash + 1);
            if (basename.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
                basename = basename.Substring(0, basename.Length - ".unity3d".Length);
            int underscore = basename.IndexOf('_');
            if (underscore >= 0 && underscore + 1 < basename.Length)
                return basename.Substring(underscore + 1);
            return basename;
        }

        public static void Init()
        {
            try
            {
                string userDir = Path.Combine(MelonEnvironment.UserDataDirectory, "AQWIB");
                System.IO.Directory.CreateDirectory(userDir);
                _liveFilePath = Path.Combine(userDir, "items.json");
                if (File.Exists(_liveFilePath)) LoadLive(_liveFilePath);
                MelonLogger.Msg($"[ItemCatalog] loaded {Armors.Count} armors, {Helms.Count} helms, {Backs.Count} capes from {_liveFilePath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ItemCatalog] init failed: {ex.Message}");
            }
        }

        public static void Save()
        {
            if (!_dirty || _liveFilePath == null) return;
            try
            {
                lock (_lock)
                {
                    var payload = new
                    {
                        armors = Armors.Values.OrderBy(e => e.name).ToList(),
                        helms = Helms.Values.OrderBy(e => e.name).ToList(),
                        backs = Backs.Values.OrderBy(e => e.name).ToList(),
                    };
                    File.WriteAllText(_liveFilePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
                    _dirty = false;
                    MelonLogger.Msg($"[ItemCatalog] saved {Armors.Count} armors, {Helms.Count} helms, {Backs.Count} capes");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ItemCatalog] save failed: {ex.Message}");
            }
        }

        public static void RecordArmor(int id, string name, AssetBundleData bundle, string prefabName)
            => Record(Armors, "Armor", id, name, bundle, prefabName);

        public static void RecordHelm(int id, string name, AssetBundleData bundle, string prefabName)
            => Record(Helms, "Head", id, name, bundle, prefabName);

        public static void RecordBack(int id, string name, AssetBundleData bundle, string prefabName)
            => Record(Backs, "Back", id, name, bundle, prefabName);

        static void Record(Dictionary<string, ItemEntry> bucket, string slot, int id, string name, AssetBundleData bundle, string prefabName)
        {
            if (bundle == null || string.IsNullOrEmpty(bundle.Filename)) return;
            string key = bundle.Filename;
            var entry = new ItemEntry
            {
                id = id,
                name = name ?? "",
                slot = slot,
                bundle = key,
                prefab = prefabName ?? "",
                verC = bundle.VersionContent,
                verS = bundle.VersionStage,
                verL = bundle.VersionLive,
            };
            lock (_lock)
            {
                if (bucket.TryGetValue(key, out var existing)
                    && existing.id == entry.id && existing.name == entry.name
                    && existing.prefab == entry.prefab && existing.verC == entry.verC
                    && existing.verS == entry.verS && existing.verL == entry.verL)
                    return;
                // Sticky-name: if a previous sighting captured a real name and
                // the new one is empty, keep the existing name. Nearby NPC
                // gear shows up as raw EquipItem without a Name field, so this
                // protects already-named entries from being overwritten with
                // blanks.
                if (existing != null && string.IsNullOrEmpty(entry.name) && !string.IsNullOrEmpty(existing.name))
                    entry.name = existing.name;
                bucket[key] = entry;
                _dirty = true;
            }
        }

        static void LoadLive(string path)
        {
            var obj = JObject.Parse(File.ReadAllText(path));
            LoadBucket(obj, "armors", Armors);
            LoadBucket(obj, "helms", Helms);
            LoadBucket(obj, "backs", Backs);
        }

        static void LoadBucket(JObject obj, string key, Dictionary<string, ItemEntry> bucket)
        {
            if (obj[key] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t is JObject e)
                    {
                        var entry = e.ToObject<ItemEntry>();
                        if (entry != null && !string.IsNullOrEmpty(entry.bundle))
                            bucket[entry.bundle] = entry;
                    }
                }
            }
        }
    }
}
