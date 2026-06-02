using HarmonyLib;

namespace Infinity_TestMod.Patches
{
    [HarmonyPatch(typeof(Player), "ComposeNameplateText")]
    public static class NameSpoofPatch
    {
        public static void Postfix(Player __instance, ref string __result)
        {
            if (!TestMod.nameSpoofActive || string.IsNullOrEmpty(TestMod.spoofedName))
                return;
            if (__instance == null || Entity.mainPlayer == null || __instance != Entity.mainPlayer)
                return;

            string prefix = "";
            if (!string.IsNullOrEmpty(__result) && __result.StartsWith("(IGNORED) "))
                prefix = "(IGNORED) ";

            __result = prefix + TestMod.spoofedName;
        }
    }
}
