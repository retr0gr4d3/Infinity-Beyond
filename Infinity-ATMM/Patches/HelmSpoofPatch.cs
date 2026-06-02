using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
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

                __result = SpoofBundleBuilder.Build(TestMod.helmSpoofBundle, ItemCatalog.Helms, avt.character.Helm?.Bundle, __result);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[HelmSpoofPatch] {ex.Message}");
            }
        }
    }
}
