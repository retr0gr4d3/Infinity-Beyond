using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Passive armor catalog feeder. Every time an ArmorLoader is constructed —
    // for the local player, party members, NPCs, or random players walking
    // through the area — read the character's equipped Armor (or Class as
    // fallback) and record bundle + version metadata. Best-effort: silently
    // ignores anything malformed.
    [HarmonyPatch(typeof(ArmorLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class ArmorHarvestPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            try
            {
                if (p == null || p.character == null) return;
                Entity ent = p.character;
                EquipItem item = ent.Armor ?? ent.Class;
                if (item == null || item.Bundle == null) return;

                // Item adds Name on top of EquipItem. At runtime the equip dict
                // typically stores Item/InventoryItem instances, so the cast
                // usually succeeds. When it doesn't, the catalog will store an
                // empty name and a later sighting can fill it in.
                string name = (item as Item)?.Name ?? "";
                ItemCatalog.RecordArmor(item.ID, name, item.Bundle, item.PrefabName);
            }
            catch { /* harvester is best-effort */ }
        }
    }

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
                __result = BundleBuilder.Build(TestMod.armorSpoofBundle, ItemCatalog.Armors, equipped, __result);
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[ArmorSpoofPatch] {ex.Message}");
            }
        }
    }
}
