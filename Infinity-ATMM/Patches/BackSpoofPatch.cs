using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Local-only cape visual swap. BackLoader.GetBundleData returns
    // player.Back.Bundle directly. Receiver uses hardcoded "CapeGO" prefab,
    // so bundle-only override is enough.
    [HarmonyPatch(typeof(BackLoader), "GetBundleData")]
    public static class BackSpoofPatch
    {
        private static readonly AccessTools.FieldRef<charItemLoader, HumanoidAvatar> _avtRef =
            AccessTools.FieldRefAccess<charItemLoader, HumanoidAvatar>("avt");

        public static void Postfix(BackLoader __instance, ref AssetBundleData __result)
        {
            if (!TestMod.backSpoofActive || string.IsNullOrWhiteSpace(TestMod.backSpoofBundle))
                return;
            try
            {
                HumanoidAvatar avt = _avtRef(__instance);
                if (avt == null || avt.character == null) return;
                if (avt.character != Entity.mainPlayer) return;

                __result = SpoofBundleBuilder.Build(TestMod.backSpoofBundle, ItemCatalog.Backs, avt.character.Back?.Bundle, __result);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[BackSpoofPatch] {ex.Message}");
            }
        }
    }
}
