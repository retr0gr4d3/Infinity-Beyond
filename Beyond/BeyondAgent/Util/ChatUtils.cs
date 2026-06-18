namespace Infinity_TestMod.Util
{
    public static class ChatUtils
    {
        public static void SendChat(string msg, string msg2 = "", string msg3 = "", string name = "Cheat", string channel = "Admin")
        {
            ResponseChat.ChatUpdate(new ResponseChat(string.Concat(
            [
                msg,
                msg2,
                msg3,
            ]), name, channel));
        }

        public static void SendChatAndLog(string msg, string msg2 = "", string msg3 = "", string name = "Cheat", string channel = "Admin")
        {
            SendChat(msg, msg2, msg3, name, channel);
            BeyondLog.Msg($"[ChatLog] {name} ({channel}): {string.Concat([msg, msg2, msg3])}");
        }
    }
}
