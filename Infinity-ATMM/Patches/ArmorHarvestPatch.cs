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
}
