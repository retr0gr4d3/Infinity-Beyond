using HarmonyLib;
using UnityEngine;

namespace Infinity_TestMod.Patches
{
    /// <summary>
    /// Static surface holding the most recent server-side quest signals.
    /// QuestRunner reads these to detect turn-in success and rate-limit
    /// errors instead of poll-and-pray.
    /// </summary>
    public static class RuntimeEvents
    {
        // ResponseQuestComplete (Cmd="QComp")
        public static int LastCompleteQid;
        public static bool LastCompleteSuccess;
        public static float LastCompleteTime = -1f;

        // ResponseNotify (Cmd="rNotify")
        public static string LastNotifyMsg = "";
        public static float LastNotifyTime = -1f;
    }

    [HarmonyPatch(typeof(ResponseQuestComplete), nameof(ResponseQuestComplete.Execute))]
    public static class ResponseQuestCompletePatch
    {
        public static void Postfix(ResponseQuestComplete __instance)
        {
            if (__instance == null) return;
            RuntimeEvents.LastCompleteQid = __instance.ID;
            RuntimeEvents.LastCompleteSuccess = __instance.Success;
            RuntimeEvents.LastCompleteTime = Time.time;
        }
    }

    [HarmonyPatch(typeof(ResponseNotify), nameof(ResponseNotify.Execute))]
    public static class ResponseNotifyPatch
    {
        public static void Postfix(ResponseNotify __instance)
        {
            if (__instance == null) return;
            RuntimeEvents.LastNotifyMsg = __instance.msg ?? "";
            RuntimeEvents.LastNotifyTime = Time.time;
        }
    }
}
