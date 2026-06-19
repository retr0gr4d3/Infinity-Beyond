using HarmonyLib;

namespace BeyondAgent.Patches
{
    [HarmonyPatch(typeof(Player), "ComposeNameplateText")]
    public static class NameSpoofPatch
    {
        public static void Postfix(Player __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(BeyondAgentClass.spoofedName))
            {
                return;
            }

            if (__instance == null || Entity.mainPlayer == null || __instance != Entity.mainPlayer)
            {
                return;
            }

            string prefix = "";
            if (!string.IsNullOrEmpty(__result) && __result.StartsWith("(IGNORED) "))
            {
                prefix = "(IGNORED) ";
            }

            __result = prefix + BeyondAgentClass.spoofedName;
        }
    }
}
