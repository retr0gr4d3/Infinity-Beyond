using HarmonyLib;

namespace Infinity_TestMod.Patches
{
    // Top-left UIPlayerPanel writes target.Name into a TMP_Text every Update via
    // setText(). Postfix and overwrite the label when the panel's target is the
    // local player. Purely a display swap — no packets, no state mutation on
    // Entity.mainPlayer.Name (which other code paths still rely on).
    [HarmonyPatch(typeof(UIPlayerPanel), "setText")]
    public static class HudNameSpoofPatch
    {
        public static void Postfix(UIPlayerPanel __instance)
        {
            if (!TestMod.nameSpoofActive || string.IsNullOrEmpty(TestMod.spoofedName))
                return;
            if (__instance == null || __instance.nameText == null) return;
            if (__instance.target == null || Entity.mainPlayer == null) return;
            if (__instance.target != Entity.mainPlayer) return;

            __instance.nameText.text = TestMod.spoofedName;
        }
    }
}
