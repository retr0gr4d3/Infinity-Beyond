using HarmonyLib;
using MelonLoader;

namespace Infinity_TestMod.Patches
{
    [HarmonyPatch(typeof(ResponseLoadShop), nameof(ResponseLoadShop.Execute))]
    public static class ResponseLoadShopExecutePatch
    {
        public static void Prefix(ResponseLoadShop __instance)
        {
            if (TestMod.forceMergeShop && __instance.shop != null)
            {
                __instance.shop.mergeShop = true;
                TestMod.forceMergeShop = false;
                MelonLogger.Msg("Intercepted ResponseLoadShop: Forced mergeShop = true");
            }
        }
    }
}
