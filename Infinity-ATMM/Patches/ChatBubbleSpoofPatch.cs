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
            if (!TestMod.nameSpoofActive || string.IsNullOrEmpty(TestMod.spoofedName))
                return;
            if (Entity.mainPlayer == null) return;
            string realName = Entity.mainPlayer.Name;
            if (string.IsNullOrEmpty(realName) || string.IsNullOrEmpty(nameTitleCase)) return;

            if (string.Equals(nameTitleCase, TestMod.spoofedName, System.StringComparison.OrdinalIgnoreCase))
                nameTitleCase = realName;
        }
    }
}
