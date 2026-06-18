using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Mirror of ArmorHarvestPatch for the Head slot. HelmLoader's constructor
    // pulls from player.Helm (when shown) or falls back to the character's
    // customization hair bundle. We only catalog the equipped Helm — hair
    // bundles aren't useful as spoof targets.
    [HarmonyPatch(typeof(HelmLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class HelmHarvestPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            try
            {
                if (p == null || p.character == null) return;
                EquipItem item = p.character.Helm;
                if (item == null || item.Bundle == null) return;
                string name = (item as Item)?.Name ?? "";
                ItemCatalog.RecordHelm(item.ID, name, item.Bundle, item.PrefabName);
            }
            catch { }
        }
    }

    // Local-only helm visual swap. HelmLoader caches BundleData in its
    // constructor; GetBundleData returns the cache. Receiver (HumanoidAvatar
    // .onBundleLoaded) resolves the prefab as the hardcoded "HelmGO" string,
    // so bundle-only override is sufficient — no PrefabName plumbing needed.
    [HarmonyPatch(typeof(HelmLoader), "GetBundleData")]
    public static class HelmSpoofPatch
    {
        private static readonly AccessTools.FieldRef<charItemLoader, HumanoidAvatar> _avtRef =
            AccessTools.FieldRefAccess<charItemLoader, HumanoidAvatar>("avt");

        public static void Postfix(HelmLoader __instance, ref AssetBundleData __result)
        {
            if (!TestMod.helmSpoofActive || string.IsNullOrWhiteSpace(TestMod.helmSpoofBundle))
                return;
            try
            {
                HumanoidAvatar avt = _avtRef(__instance);
                if (avt == null || avt.character == null) return;
                if (avt.character != Entity.mainPlayer) return;

                __result = BundleBuilder.Build(TestMod.helmSpoofBundle, ItemCatalog.Helms, avt.character.Helm?.Bundle, __result);
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[HelmSpoofPatch] {ex.Message}");
            }
        }
    }
}
