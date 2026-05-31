using HarmonyLib;
using MelonLoader;

namespace Infinity_TestMod.Patches
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
                TestMod.lastPacketInfo = $"{typeName} ({cmd})";

                bool shouldLog = TestMod.interceptActive || TestMod.interceptorLoggingActive;
                if (shouldLog)
                {
                    string logEntry = TestMod.interceptActive
                        ? $"[<color=red>BLOCKED</color>] {typeName} ({cmd})"
                        : $"[<color=green>ALLOWED</color>] {typeName} ({cmd})";

                    lock (TestMod.interceptedPacketsLog)
                    {
                        TestMod.interceptedPacketsLog.Insert(0, logEntry);
                        if (TestMod.interceptedPacketsLog.Count > 100)
                        {
                            TestMod.interceptedPacketsLog.RemoveAt(TestMod.interceptedPacketsLog.Count - 1);
                        }
                    }
                }

                if (TestMod.interceptActive)
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
            if (data != null && TestMod.snifferServerActive)
            {
                try
                {
                    string rawJson = System.Text.Encoding.UTF8.GetString(data);
                    string cmd = Util.extractValueFromJsonString("Cmd", rawJson) ?? "unknown";

                    string typeName = "Response";
                    System.Type t = ResponseTypes.Get(cmd);
                    if (t != null)
                    {
                        typeName = t.Name;
                    }

                    string display = $"<color=cyan>[SERVER]</color> {typeName} ({cmd})";
                    lock (TestMod.snifferLog)
                    {
                        TestMod.snifferLog.Insert(0, new TestMod.SniffEntry { DisplayText = display, RawJson = rawJson });
                        if (TestMod.selectedSniffIndex >= 0)
                        {
                            TestMod.selectedSniffIndex++;
                        }
                        if (TestMod.snifferLog.Count > 200)
                        {
                            TestMod.snifferLog.RemoveAt(TestMod.snifferLog.Count - 1);
                            if (TestMod.selectedSniffIndex >= 200)
                            {
                                TestMod.selectedSniffIndex = -1;
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error("Sniffer failed to parse incoming server packet data: " + ex.Message);
                }
            }
        }
    }

    [HarmonyPatch(typeof(AEC), nameof(AEC.sendRequest))]
    public static class AECsendRequestPatch
    {
        private static System.Reflection.MethodInfo _serializeMethod;

        public static void Prefix(Request r)
        {
            if (r != null && TestMod.snifferClientActive)
            {
                string cmd = r.Cmd ?? "unknown";
                string typeName = r.GetType().Name;

                string rawData = "";
                try
                {
                    if (_serializeMethod == null)
                    {
                        _serializeMethod = typeof(AEC).GetMethod("Serialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    }

                    if (_serializeMethod != null && AEC.Instance != null)
                    {
                        rawData = (string)_serializeMethod.Invoke(AEC.Instance, new object[] { r });
                    }
                    else
                    {
                        rawData = Newtonsoft.Json.JsonConvert.SerializeObject(r);
                    }
                }
                catch
                {
                    try
                    {
                        rawData = Newtonsoft.Json.JsonConvert.SerializeObject(r);
                    }
                    catch
                    {
                        rawData = "(serialization failed)";
                    }
                }

                string display = $"<color=orange>[CLIENT]</color> {typeName} ({cmd})";
                lock (TestMod.snifferLog)
                {
                    TestMod.snifferLog.Insert(0, new TestMod.SniffEntry { DisplayText = display, RawJson = rawData });
                    if (TestMod.selectedSniffIndex >= 0)
                    {
                        TestMod.selectedSniffIndex++;
                    }
                    if (TestMod.snifferLog.Count > 200)
                    {
                        TestMod.snifferLog.RemoveAt(TestMod.snifferLog.Count - 1);
                        if (TestMod.selectedSniffIndex >= 200)
                        {
                            TestMod.selectedSniffIndex = -1;
                        }
                    }
                }
            }
        }
    }
}
