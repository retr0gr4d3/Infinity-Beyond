using HarmonyLib;

namespace Infinity_TestMod.Patches
{
    // SpawnBubble parents the speech bubble to the nameplate of the player it
    // looks up by name. When chat is spoofed, that lookup fails (no player
    // exists with the spoofed name) and the bubble silently does not spawn.
    // Remap the spoofed name back to the real name *only for the lookup* so the
    // bubble attaches to our actual nameplate; the message content is
    // unaffected.
    [HarmonyPatch(typeof(UIChatActions), "SpawnBubble")]
    public static class ChatBubbleSpoofPatch
    {
        public static void Prefix(ref string nameTitleCase)
        {
            if (string.IsNullOrEmpty(TestMod.spoofedName))
                return;
            if (Entity.mainPlayer == null) return;
            string realName = Entity.mainPlayer.Name;
            if (string.IsNullOrEmpty(realName) || string.IsNullOrEmpty(nameTitleCase)) return;

            if (string.Equals(nameTitleCase, TestMod.spoofedName, System.StringComparison.OrdinalIgnoreCase))
                nameTitleCase = realName;
        }
    }

    // UIChatActions.UpdateText composes every rendered chat line from rc.Name
    // and rc.msg. Mutating rc.Name here only affects the displayed string � no
    // packets are sent. ResponseChat is single-use (ResponseChat.Execute fires
    // ChatUpdate once and the object is discarded), so the mutation cannot
    // leak to other consumers.
    [HarmonyPatch(typeof(UIChatActions), "UpdateText")]
    public static class ChatNameSpoofPatch
    {
        public static void Prefix(ResponseChat rc)
        {
            if (string.IsNullOrEmpty(TestMod.spoofedName))
                return;
            if (rc == null || Entity.mainPlayer == null) return;

            string realName = Entity.mainPlayer.Name;
            if (string.IsNullOrEmpty(realName)) return;

            if (rc.Name == realName)
                rc.Name = TestMod.spoofedName;
        }
    }
}
