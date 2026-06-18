using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Infinity_TestMod.Util
{
    /// <summary>
    /// Passive catalog of gear seen on any character in the world. Fed by the
    /// harvester patches (e.g. ArmorHarvestPatch). Persisted to
    /// `BeyondEnv.UserDataDirectory/Beyond/items.json`. Keyed by bundle
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
            // Weapon-only: the iType enum value (Sword=6, Dagger=7, Axe=8, ...).
            // Weapon spoof needs this to drive CC.LoadWeapon's hold-pose
            // selection — wrong type = misaligned grip animation. 0 for non-weapons.
            public int itemType;
            // Pet-only sizing. PetLoader.LoadItem feeds these straight into
            // BundlePrefabLoader so a spoof has to reproduce them or the pet
            // ends up the wrong size / floating in the wrong spot. Null for
            // non-pets — they ride the EquipItem defaults (Scale=1, Off=0).
            public double? scale;
            public double? offX;
            public double? offY;
        }

        public static readonly Dictionary<string, ItemEntry> Armors = new();
        public static readonly Dictionary<string, ItemEntry> Helms = new();
        public static readonly Dictionary<string, ItemEntry> Backs = new();
        public static readonly Dictionary<string, ItemEntry> Weapons = new();
        public static readonly Dictionary<string, ItemEntry> Pets = new();
        // Monsters are catalogued for "wear as pet" — their bundle/linkage/
        // scale fit the same shape PetLoader consumes. Harvested separately
        // from Monster ctor (not via charItemLoader) since they aren't gear.
        public static readonly Dictionary<string, ItemEntry> Monsters = new();

        static string _liveFilePath;
        static bool _dirty;
        static readonly object _lock = new();

        /// <summary>
        /// Empty one slot's bucket and persist immediately. Used by the UI's
        /// per-slot Clear button so a wipe is durable across game restarts
        /// even if OnApplicationQuit doesn't fire (crash, hard kill).
        /// </summary>
        public static void ClearArmors() => ClearBucket(Armors);
        public static void ClearHelms() => ClearBucket(Helms);
        public static void ClearBacks() => ClearBucket(Backs);
        public static void ClearWeapons() => ClearBucket(Weapons);
        public static void ClearPets() => ClearBucket(Pets);
        public static void ClearMonsters() => ClearBucket(Monsters);

        /// <summary>
        /// Look up a bundle in the Pets bucket first, falling back to Monsters.
        /// Used by the pet spoof so users can equip catalogued monsters as
        /// pets — they share the BundlePrefabLoader call shape.
        /// </summary>
        public static bool TryGetPetOrMonster(string bundleFilename, out ItemEntry entry)
        {
            if (Pets.TryGetValue(bundleFilename, out entry)) return true;
            if (Monsters.TryGetValue(bundleFilename, out entry)) return true;
            entry = null;
            return false;
        }

        static void ClearBucket(Dictionary<string, ItemEntry> bucket)
        {
            int removed;
            lock (_lock)
            {
                removed = bucket.Count;
                bucket.Clear();
                _dirty = true;
            }
            Save();
            BeyondLog.Msg($"[ItemCatalog] cleared {removed} entries");
        }

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
                string userDir = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(userDir);
                _liveFilePath = Path.Combine(userDir, "items.json");
                if (File.Exists(_liveFilePath)) LoadLive(_liveFilePath);
                BeyondLog.Msg($"[ItemCatalog] loaded {Armors.Count} armors, {Helms.Count} helms, {Backs.Count} capes, {Weapons.Count} weapons, {Pets.Count} pets, {Monsters.Count} monsters from {_liveFilePath}");
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[ItemCatalog] init failed: {ex.Message}");
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
                        weapons = Weapons.Values.OrderBy(e => e.name).ToList(),
                        pets = Pets.Values.OrderBy(e => e.name).ToList(),
                        monsters = Monsters.Values.OrderBy(e => e.name).ToList(),
                    };
                    File.WriteAllText(_liveFilePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
                    _dirty = false;
                    BeyondLog.Msg($"[ItemCatalog] saved {Armors.Count} armors, {Helms.Count} helms, {Backs.Count} capes, {Weapons.Count} weapons, {Pets.Count} pets, {Monsters.Count} monsters");
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[ItemCatalog] save failed: {ex.Message}");
            }
        }

        public static void RecordArmor(int id, string name, AssetBundleData bundle, string prefabName)
            => Record(Armors, "Armor", id, name, bundle, prefabName);

        public static void RecordHelm(int id, string name, AssetBundleData bundle, string prefabName)
            => Record(Helms, "Head", id, name, bundle, prefabName);

        public static void RecordBack(int id, string name, AssetBundleData bundle, string prefabName)
            => Record(Backs, "Back", id, name, bundle, prefabName);

        public static void RecordWeapon(int id, string name, AssetBundleData bundle, string prefabName, int itemType)
            => Record(Weapons, "Weapon", id, name, bundle, prefabName, itemType);

        public static void RecordPet(int id, string name, AssetBundleData bundle, string prefabName,
                                     double? scale, double? offX, double? offY)
            => Record(Pets, "Pet", id, name, bundle, prefabName, 0, scale, offX, offY);

        public static void RecordMonster(int id, string name, AssetBundleData bundle, string linkage, double scale)
            => Record(Monsters, "Monster", id, name, bundle, linkage, 0, scale, null, null);

        static void Record(Dictionary<string, ItemEntry> bucket, string slot, int id, string name,
                           AssetBundleData bundle, string prefabName, int itemType = 0,
                           double? scale = null, double? offX = null, double? offY = null)
        {
            if (bundle == null || string.IsNullOrEmpty(bundle.Filename)) return;
            string key = bundle.Filename;
            ItemEntry entry = new()
            {
                id = id,
                name = name ?? "",
                slot = slot,
                bundle = key,
                prefab = prefabName ?? "",
                verC = bundle.VersionContent,
                verS = bundle.VersionStage,
                verL = bundle.VersionLive,
                itemType = itemType,
                scale = scale,
                offX = offX,
                offY = offY,
            };
            lock (_lock)
            {
                if (bucket.TryGetValue(key, out ItemEntry existing)
                    && existing.id == entry.id && existing.name == entry.name
                    && existing.prefab == entry.prefab && existing.verC == entry.verC
                    && existing.verS == entry.verS && existing.verL == entry.verL
                    && existing.itemType == entry.itemType
                    && existing.scale == entry.scale
                    && existing.offX == entry.offX && existing.offY == entry.offY)
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
            JObject obj = JObject.Parse(File.ReadAllText(path));
            LoadBucket(obj, "armors", Armors);
            LoadBucket(obj, "helms", Helms);
            LoadBucket(obj, "backs", Backs);
            LoadBucket(obj, "weapons", Weapons);
            LoadBucket(obj, "pets", Pets);
            LoadBucket(obj, "monsters", Monsters);
        }

        static void LoadBucket(JObject obj, string key, Dictionary<string, ItemEntry> bucket)
        {
            if (obj[key] is JArray arr)
            {
                foreach (JToken t in arr)
                {
                    if (t is JObject e)
                    {
                        ItemEntry entry = e.ToObject<ItemEntry>();
                        if (entry != null && !string.IsNullOrEmpty(entry.bundle))
                            bucket[entry.bundle] = entry;
                    }
                }
            }
        }
    }
}
