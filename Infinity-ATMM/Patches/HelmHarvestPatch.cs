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
}
