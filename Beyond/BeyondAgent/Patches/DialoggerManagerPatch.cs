using BeyondAgent.Util;
using HarmonyLib;

namespace BeyondAgent.Patches
{
    // Auto-skip cutscenes by calling Dialogger_Manager.EndPressed() right
    // after StartCutscene fires. EndPressed is the same path the in-game
    // "End" button uses, so it invokes DoCompleteActions — quest hooks,
    // item grants, and any state changes baked into the cutscene's
    // completeActions still run. Dropping the getDialog/getCutscene packet
    // outright would skip those too and silently stall progression.
    //
    // We defer one frame: StartCutscene kicks off async asset loads
    // (LoadPageOneUponAssetLoadCompleteGuts) and EndPressed expects the
    // page state to exist. Same-frame end works most of the time but the
    // next-frame call is bullet-proof.
    [HarmonyPatch(typeof(Dialogger_Manager), nameof(Dialogger_Manager.StartCutscene))]
    public static class CutsceneSkipPatch
    {
        private static void Postfix(Dialogger_Manager __instance)
        {
            if (!BeyondAgentClass.autoSkipCutscenes)
            {
                return;
            }

            if (__instance == null)
            {
                return;
            }

            BeyondCoroutines.Start(EndNextFrame(__instance));
        }

        private static System.Collections.IEnumerator EndNextFrame(Dialogger_Manager mgr)
        {
            yield return null;
            try
            {
                mgr.EndPressed();
                CameraZoom.Reset();
                BeyondLog.Msg("[CutsceneSkip] auto-skipped (zoom reset)");
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[CutsceneSkip] EndPressed failed: {ex}");
            }
        }
    }
}
