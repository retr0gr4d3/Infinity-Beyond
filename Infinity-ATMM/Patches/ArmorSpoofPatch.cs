using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Local-only armor visual swap. ArmorLoader caches BundleData in its
    // constructor from player.Armor (or player.Class as fallback); GetBundleData
    // just returns that cache. Postfix it: when the spoof is active and the
    // loader belongs to the local player, hand back a fresh AssetBundleData
    // pointing at the user-supplied filename. Version metadata is copied from
    // the currently equipped armor so the CDN URL resolves.
    //
    // No packets touched. Server still believes we're wearing whatever it
    // last gave us; only our local avatar's armor visual changes.
    [HarmonyPatch(typeof(ArmorLoader), "GetBundleData")]
    public static class ArmorSpoofPatch
    {
        // charItemLoader.avt is protected — reach it via Harmony's reflection
        // helpers rather than re-binding flags by hand.
        private static readonly AccessTools.FieldRef<charItemLoader, HumanoidAvatar> _avtRef =
            AccessTools.FieldRefAccess<charItemLoader, HumanoidAvatar>("avt");

        public static void Postfix(ArmorLoader __instance, ref AssetBundleData __result)
        {
            if (!TestMod.armorSpoofActive || string.IsNullOrWhiteSpace(TestMod.armorSpoofBundle))
                return;
            try
            {
                HumanoidAvatar avt = _avtRef(__instance);
                if (avt == null || avt.character == null) return;
                if (avt.character != Entity.mainPlayer) return;

                // Catalog-first version lookup; falls back to the equipped
                // armor (or class bundle when no armor) when the target hasn't
                // been catalogued yet.
                AssetBundleData equipped = avt.character.Armor?.Bundle ?? avt.character.Class?.Bundle;
                __result = SpoofBundleBuilder.Build(TestMod.armorSpoofBundle, ItemCatalog.Armors, equipped, __result);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[ArmorSpoofPatch] {ex.Message}");
            }
        }
    }
}
