using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Cape harvester. BackLoader.GetBundleData reads player.Back.Bundle; we
    // catalog that on construction so the spoof picker has options.
    [HarmonyPatch(typeof(BackLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class BackHarvestPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            try
            {
                if (p == null || p.character == null) return;
                EquipItem item = p.character.Back;
                if (item == null || item.Bundle == null) return;
                string name = (item as Item)?.Name ?? "";
                ItemCatalog.RecordBack(item.ID, name, item.Bundle, item.PrefabName);
            }
            catch { }
        }
    }
}
