using BeyondAgent.Util;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace BeyondAgent.Patches
{
    [HarmonyPatch(typeof(AEC), nameof(AEC.GetResponse))]
    public static class AECGetResponsePatch
    {
        public static void Postfix(ref Response __result)
        {
            if (__result != null)
            {
                string cmd = "unknown";
                try
                {
                    cmd = __result.GetCommand();
                }
                catch { }

                string typeName = __result.GetType().Name;
                BeyondAgentClass.lastPacketInfo = $"{typeName} ({cmd})";

                bool shouldLog = BeyondAgentClass.interceptActive || BeyondAgentClass.interceptorLoggingActive;
                if (shouldLog)
                {
                    string logEntry = BeyondAgentClass.interceptActive
                        ? $"[<color=red>BLOCKED</color>] {typeName} ({cmd})"
                        : $"[<color=green>ALLOWED</color>] {typeName} ({cmd})";

                    lock (BeyondAgentClass.interceptedPacketsLog)
                    {
                        BeyondAgentClass.interceptedPacketsLog.Insert(0, logEntry);
                        if (BeyondAgentClass.interceptedPacketsLog.Count > 100)
                        {
                            BeyondAgentClass.interceptedPacketsLog.RemoveAt(BeyondAgentClass.interceptedPacketsLog.Count - 1);
                        }
                    }

                    // Forward to Launcher
                    LauncherServer.Send(new
                    {
                        Type = "InterceptedPacket",
                        Action = BeyondAgentClass.interceptActive ? "BLOCKED" : "ALLOWED",
                        TypeName = typeName,
                        Cmd = cmd,
                        LogEntry = logEntry
                    });
                }

                if (BeyondAgentClass.interceptActive)
                {
                    __result = null;
                }
            }
        }
    }

    [HarmonyPatch(typeof(AEC), "WrapAndQueueResponse")]
    public static class AECWrapAndQueueResponsePatch
    {
        public static void Prefix(byte[] data)
        {
            if (data == null)
            {
                return;
            }

            // Always-on disk log — independent of the in-memory sniffer toggle
            // so analysis tools (state.py, gui.py) get a complete capture.
            string rawJson;
            try
            {
                rawJson = System.Text.Encoding.UTF8.GetString(data);
                PacketLog.Write("s2c", rawJson);
                DirectoryMiner.Run(rawJson);
            }
            catch { return; }

            // Drop filter: queue any rejected reward drops for dusting.
            // Cheap prefilter so we only parse reward packets.
            if (rawJson.Contains("\"rewardPlayer\""))
            {
                try { DropFilterEngine.HandleRewardPacket(rawJson); }
                catch (System.Exception ex) { BeyondLog.Error($"[DropFilter] reward parse: {ex.Message}"); }
            }

            if (BeyondAgentClass.snifferServerActive)
            {
                try
                {
                    string cmd = global::Util.extractValueFromJsonString("Cmd", rawJson) ?? "unknown";

                    string typeName = "Response";
                    System.Type t = ResponseTypes.Get(cmd);
                    if (t != null)
                    {
                        typeName = t.Name;
                    }

                    string display = $"<color=cyan>[SERVER]</color> {typeName} ({cmd})";
                    lock (BeyondAgentClass.snifferLog)
                    {
                        BeyondAgentClass.snifferLog.Insert(0, new BeyondAgentClass.SniffEntry { DisplayText = display, RawJson = rawJson });
                        if (BeyondAgentClass.selectedSniffIndex >= 0)
                        {
                            BeyondAgentClass.selectedSniffIndex++;
                        }
                        if (BeyondAgentClass.snifferLog.Count > 200)
                        {
                            BeyondAgentClass.snifferLog.RemoveAt(BeyondAgentClass.snifferLog.Count - 1);
                            if (BeyondAgentClass.selectedSniffIndex >= 200)
                            {
                                BeyondAgentClass.selectedSniffIndex = -1;
                            }
                        }
                    }

                    // Forward to Launcher
                    LauncherServer.Send(new
                    {
                        Type = "SniffedPacket",
                        Direction = "s2c",
                        Cmd = cmd,
                        TypeName = typeName,
                        Raw = rawJson
                    });
                }
                catch (System.Exception ex)
                {
                    BeyondLog.Error("Sniffer failed to parse incoming server packet data: " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Skim each s2c packet for catalog-worthy data (quest defs, shops) and
    /// feed them into Directory for browsing. Kept narrow on purpose — we
    /// only parse when the Cmd matches, so this stays cheap on the hot path.
    /// </summary>
    internal static class DirectoryMiner
    {
        public static void Run(string rawJson)
        {
            // Quick prefilter — avoid parsing every packet just to find none
            if (rawJson == null)
            {
                return;
            }

            bool maybeQuests = rawJson.Contains("\"getQuests\"");
            bool maybeShop = rawJson.Contains("\"loadShop\"");
            if (!maybeQuests && !maybeShop)
            {
                return;
            }

            try
            {
                JObject obj = Newtonsoft.Json.Linq.JObject.Parse(rawJson);
                string cmd = (string)obj["Cmd"];
                if (cmd == "getQuests" && obj["quests"] is Newtonsoft.Json.Linq.JObject qs)
                {
                    foreach (JProperty p in qs.Properties())
                    {
                        if (int.TryParse(p.Name, out int qid) && p.Value is Newtonsoft.Json.Linq.JObject qdef)
                        {
                            Directory.RecordQuest(qid, qdef);
                        }
                    }
                }
                else if (cmd == "loadShop" && obj["shop"] is Newtonsoft.Json.Linq.JObject shop)
                {
                    Directory.RecordShop(shop);
                }
            }
            catch { /* malformed packet — log noise isn't worth surfacing */ }
        }
    }

    [HarmonyPatch(typeof(AEC), nameof(AEC.sendRequest))]
    public static class AECsendRequestPatch
    {
        private static System.Reflection.MethodInfo _serializeMethod;

        public static void Prefix(Request r)
        {
            if (r == null)
            {
                return;
            }

            // Serialize once — used for both the disk log and the in-memory
            // sniffer below. Mirrors AEC's own serializer when reachable.
            string rawData;
            try
            {
                if (_serializeMethod == null)
                {
                    _serializeMethod = typeof(AEC).GetMethod("Serialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                rawData = _serializeMethod != null && AEC.Instance != null
                    ? (string)_serializeMethod.Invoke(AEC.Instance, [r])
                    : Newtonsoft.Json.JsonConvert.SerializeObject(r);
            }
            catch
            {
                try { rawData = Newtonsoft.Json.JsonConvert.SerializeObject(r); }
                catch { rawData = null; }
            }

            // Always-on disk log
            if (!string.IsNullOrEmpty(rawData))
            {
                PacketLog.Write("c2s", rawData);
            }

            if (BeyondAgentClass.snifferClientActive)
            {
                string cmd = r.Cmd ?? "unknown";
                string typeName = r.GetType().Name;
                if (string.IsNullOrEmpty(rawData))
                {
                    rawData = "(serialization failed)";
                }

                string display = $"<color=orange>[CLIENT]</color> {typeName} ({cmd})";
                lock (BeyondAgentClass.snifferLog)
                {
                    BeyondAgentClass.snifferLog.Insert(0, new BeyondAgentClass.SniffEntry { DisplayText = display, RawJson = rawData });
                    if (BeyondAgentClass.selectedSniffIndex >= 0)
                    {
                        BeyondAgentClass.selectedSniffIndex++;
                    }
                    if (BeyondAgentClass.snifferLog.Count > 200)
                    {
                        BeyondAgentClass.snifferLog.RemoveAt(BeyondAgentClass.snifferLog.Count - 1);
                        if (BeyondAgentClass.selectedSniffIndex >= 200)
                        {
                            BeyondAgentClass.selectedSniffIndex = -1;
                        }
                    }
                }

                // Forward to Launcher
                LauncherServer.Send(new
                {
                    Type = "SniffedPacket",
                    Direction = "c2s",
                    Cmd = cmd,
                    TypeName = typeName,
                    Raw = rawData
                });
            }
        }
    }
}
