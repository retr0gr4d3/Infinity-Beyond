using HarmonyLib;
using UnityEngine;

namespace Infinity_TestMod.Patches
{
    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton))]
    public static class GetMouseButtonPatch
    {
        public static bool Prefix(int button, ref bool __result)
        {
            if (TestMod.IsMouseOverUI())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown))]
    public static class Patch_GetMouseButtonDown
    {
        public static bool Prefix(int button, ref bool __result)
        {
            if (TestMod.IsMouseOverUI())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp))]
    public static class Patch_GetMouseButtonUp
    {
        public static bool Prefix(int button, ref bool __result)
        {
            if (TestMod.IsMouseOverUI())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
