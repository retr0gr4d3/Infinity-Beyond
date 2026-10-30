using BeyondAgent.Util;
using HarmonyLib;
using System.Linq;

namespace BeyondAgent.Patches
{
    [HarmonyPatch(typeof(ResponseLoadShop), nameof(ResponseLoadShop.Execute))]
    public static class ResponseLoadShopExecutePatch
    {
        public static void Postfix(ResponseLoadShop __instance)
        {
            var shop = __instance?.shop;
            if (shop?.items == null) return;
            try
            {
                LauncherServer.Send(new {
                    Type = "FarmingShop", Shop = shop.shopID, Name = shop.Name,
                    Items = shop.items.Where(si => si != null).Select(si => new {
                        Id = si.ID, Name = si.Name, Owned = ItemGoals.Count(si.ID.ToString()),
                        Costs = ItemGoals.Costs(si).Select(c => new { Id = c.id, Name = c.name, Quantity = c.qty, Owned = ItemGoals.Count(c.id.ToString()) }).ToArray()
                    }).ToArray()
                });
            }
            catch (System.Exception ex)
            {
                BeyondLog.Warning($"Shop farming preview failed: {ex.Message}");
            }
        }

        public static void Prefix(ResponseLoadShop __instance)
        {
            if (__instance?.shop != null)
            {
                RuntimeEvents.LastShop = __instance.shop;
                RuntimeEvents.LastShopTime = UnityEngine.Time.time;

            }

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
