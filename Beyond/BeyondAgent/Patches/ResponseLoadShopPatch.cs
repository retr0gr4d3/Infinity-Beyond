using BeyondAgent.Util;
using HarmonyLib;

namespace BeyondAgent.Patches
{
    [HarmonyPatch(typeof(ResponseLoadShop), nameof(ResponseLoadShop.Execute))]
    public static class ResponseLoadShopExecutePatch
    {
        public static void Prefix(ResponseLoadShop __instance)
        {
            // Clear the flag unconditionally on the FIRST shop response after
            // it was set, even if __instance.shop is null. Previously the flag
            // could leak to the next shop load (or, if the targeted request
            // failed entirely, persist indefinitely until the next shop the
            // user opened got force-merged).
            if (!BeyondAgentClass.forceMergeShop)
            {
                return;
            }

            BeyondAgentClass.forceMergeShop = false;
            if (__instance.shop != null)
            {
                __instance.shop.mergeShop = true;
                BeyondLog.Msg("Intercepted ResponseLoadShop: Forced mergeShop = true");
            }
            else
            {
                BeyondLog.Msg("Intercepted ResponseLoadShop with null shop; flag cleared without effect.");
            }
        }
    }
}
