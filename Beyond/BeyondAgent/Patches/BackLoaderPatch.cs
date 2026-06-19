using BeyondAgent.Util;
using HarmonyLib;

namespace BeyondAgent.Patches
{
    // Local-only cape visual swap. BackLoader.GetBundleData returns
    // player.Back.Bundle directly. Receiver uses hardcoded "CapeGO" prefab,
    // so bundle-only override is enough.
    [HarmonyPatch(typeof(BackLoader), "GetBundleData")]
    public static class BackSpoofPatch
    {
        private static readonly AccessTools.FieldRef<charItemLoader, HumanoidAvatar> _avtRef =
            AccessTools.FieldRefAccess<charItemLoader, HumanoidAvatar>("avt");

        public static void Postfix(BackLoader __instance, ref AssetBundleData __result)
        {
            if (!BeyondAgentClass.backSpoofActive || string.IsNullOrWhiteSpace(BeyondAgentClass.backSpoofBundle))
            {
                return;
            }

            try
            {
                HumanoidAvatar avt = _avtRef(__instance);
                if (avt == null || avt.character == null)
                {
                    return;
                }

                if (avt.character != Entity.mainPlayer)
                {
                    return;
                }

                __result = BundleBuilder.Build(BeyondAgentClass.backSpoofBundle, ItemCatalog.Backs, avt.character.Back?.Bundle, __result);
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[BackSpoofPatch] {ex.Message}");
            }
        }
    }
}
