using HarmonyLib;

namespace Infinity_TestMod.Patches
{
    // UIChatActions.UpdateText composes every rendered chat line from rc.Name
    // and rc.msg. Mutating rc.Name here only affects the displayed string — no
    // packets are sent. ResponseChat is single-use (ResponseChat.Execute fires
    // ChatUpdate once and the object is discarded), so the mutation cannot
    // leak to other consumers.
    [HarmonyPatch(typeof(UIChatActions), "UpdateText")]
    public static class ChatNameSpoofPatch
    {
        public static void Prefix(ResponseChat rc)
        {
            if (!TestMod.nameSpoofActive || string.IsNullOrEmpty(TestMod.spoofedName))
                return;
            if (rc == null || Entity.mainPlayer == null) return;

            string realName = Entity.mainPlayer.Name;
            if (string.IsNullOrEmpty(realName)) return;

            if (rc.Name == realName)
                rc.Name = TestMod.spoofedName;
        }
    }
}
