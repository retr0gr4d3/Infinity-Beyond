using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Passive monster catalog feeder. Every Monster constructed in the world
    // gets its bundle, linkage (= prefab name) and scale recorded. Powers the
    // "wear monster as pet" path of the pet spoof — PetLoader's runtime call
    // shape (BundlePrefabLoader.Load with bundle+prefab+scale) matches what
    // a monster needs to render, so any catalogued monster can be substituted.
    [HarmonyPatch(typeof(Monster), MethodType.Constructor,
        new System.Type[] { typeof(int), typeof(Monbranch), typeof(bool) })]
    public static class MonsterHarvestPatch
    {
        public static void Postfix(Monster __instance, Monbranch b)
        {
            try
            {
                if (b == null || b.Bundle == null || string.IsNullOrEmpty(b.strLinkage)) return;
                ItemCatalog.RecordMonster(b.MonID, b.strMonName ?? "", b.Bundle, b.strLinkage, b.Scale);
            }
            catch { }
        }
    }
}
