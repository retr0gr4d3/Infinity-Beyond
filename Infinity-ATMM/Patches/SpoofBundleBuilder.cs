using System.Collections.Generic;
using Infinity_TestMod.Util;

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
    internal static class SpoofBundleBuilder
    {
        public static AssetBundleData Build(string targetFilename,
                                            Dictionary<string, ItemCatalog.ItemEntry> catalog,
                                            AssetBundleData equippedBundle,
                                            AssetBundleData fallback)
        {
            var spoofed = new AssetBundleData(targetFilename);

            if (catalog != null && catalog.TryGetValue(targetFilename, out var cat))
            {
                spoofed.ID = cat.id;
                spoofed.Name = cat.name;
                spoofed.VersionContent = cat.verC;
                spoofed.VersionStage = cat.verS;
                spoofed.VersionLive = cat.verL;
                return spoofed;
            }

            AssetBundleData baseline = equippedBundle ?? fallback;
            if (baseline != null)
            {
                spoofed.ID = baseline.ID;
                spoofed.Name = baseline.Name;
                spoofed.VersionContent = baseline.VersionContent;
                spoofed.VersionStage = baseline.VersionStage;
                spoofed.VersionLive = baseline.VersionLive;
            }
            return spoofed;
        }
    }
}
