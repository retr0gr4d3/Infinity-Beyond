using Infinity_TestMod.Util;
using System.Collections.Generic;

namespace Infinity_TestMod.Patches
{
    // Shared helper for gear spoof patches: builds an AssetBundleData for the
    // target Filename with correct version metadata.
    //
    // Order of preference:
    //   1. Catalog entry for the target bundle — captured when this exact
    //      bundle was seen on some character, so versions are authoritative.
    //   2. Currently equipped item's bundle — best-effort fallback when the
    //      target hasn't been catalogued yet (versions may or may not match).
    //   3. Whatever GetBundleData originally returned — last-ditch fallback.
    internal static class BundleBuilder
    {
        public static AssetBundleData Build(string targetFilename,
                                            Dictionary<string, ItemCatalog.ItemEntry> catalog,
                                            AssetBundleData equippedBundle,
                                            AssetBundleData fallback)
        {
            AssetBundleData bundle = new(targetFilename);

            if (catalog != null && catalog.TryGetValue(targetFilename, out ItemCatalog.ItemEntry cat))
            {
                bundle.ID = cat.id;
                bundle.Name = cat.name;
                bundle.VersionContent = cat.verC;
                bundle.VersionStage = cat.verS;
                bundle.VersionLive = cat.verL;
                return bundle;
            }

            AssetBundleData baseline = equippedBundle ?? fallback;
            if (baseline != null)
            {
                bundle.ID = baseline.ID;
                bundle.Name = baseline.Name;
                bundle.VersionContent = baseline.VersionContent;
                bundle.VersionStage = baseline.VersionStage;
                bundle.VersionLive = baseline.VersionLive;
            }
            return bundle;
        }
    }
}
