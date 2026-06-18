using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // UIBank isn't a UIWindow subclass (it derives from MonoBehaviour
    // directly), so the generic UIWindow.Show hook doesn't cover it.
    // We attach DragPanel from UIBank.Init instead. Idempotent via
    // DragPanel.AttachToWindowRoot's GetComponent guard.
    [HarmonyPatch(typeof(UIBank), nameof(UIBank.Init))]
    public static class BankDraggablePatch
    {
        private static void Postfix(UIBank __instance)
        {
            DragPanel.AttachToWindowRoot(__instance, "BankDraggable");
        }
    }
}
