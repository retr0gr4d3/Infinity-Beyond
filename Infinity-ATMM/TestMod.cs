using MelonLoader;
using System.Reflection;
using UnityEngine;
using Infinity_TestMod.Patches;


namespace Infinity_TestMod
{
    public class TestMod : MelonMod
    {
        public static bool showWindow = false;
        public static Rect windowRect = new(20, 100, 300, 550);
        public static readonly Rect ToggleButtonRect = new(10, 20, 64, 64);

        public static bool forceMergeShop = false;
        private static string shopIdInput = "";
        private static string questIdInput = "";

        public static bool autoskillsActive = false;
        public static bool showConfigWindow = false;
        public static Rect configWindowRect = new(330, 100, 320, 300);

        public static bool showInterceptorWindow = false;
        public static Rect interceptorWindowRect = new(660, 100, 500, 365);
        public static bool showSnifferWindow = false;
        public static Rect snifferWindowRect = new(660, 480, 500, 520);
        public static bool showSenderWindow = false;
        public static Rect senderWindowRect = new(660, 865, 500, 165);
        private static string senderCmdInput = "tfer";
        private static string senderParamsInput = "<charname>,lair,0,Enter,Spawn";
        public static System.Collections.Generic.List<string> interceptedPacketsLog = new();
        private static Vector2 interceptorScrollPosition = Vector2.zero;

        public struct SniffEntry
        {
            public string DisplayText;
            public string RawJson;
        }

        public static bool snifferServerActive = false;
        public static bool snifferClientActive = false;
        public static System.Collections.Generic.List<SniffEntry> snifferLog = new();
        public static Vector2 snifferScrollPosition = Vector2.zero;
        public static int selectedSniffIndex = -1;
        public static string selectedPacketJson = "";
        public static Vector2 selectedPacketPreviewScroll = Vector2.zero;

        private static GUIStyle rowButtonStyle;
        private static GUIStyle previewTextStyle;

        private static System.Collections.Generic.List<int> skillOrder = new() { 0, 1, 2, 3, 4 };
        private static System.Collections.Generic.Dictionary<int, float> skillDelays = new()
        {
            { 0, 1f }, { 1, 1f }, { 2, 1f }, { 3, 1f }, { 4, 1f }
        };
        private static string[] delayInputs = new string[] { "1000", "1000", "1000", "1000", "1000" };
        private static bool[] skillEnabled = new bool[] { true, true, true, true, true };
        public static bool interceptActive = false;
        public static bool interceptorLoggingActive = false;
        public static string lastPacketInfo = "None";
        private static int currentSkillIndex = 0;
        private static float nextSkillTime = 0f;

        private static Texture2D buttonTexture;
        private static Texture2D buttonHoverTexture;
        private static Texture2D windowTexture;
        private static Texture2D buttonBgTexture;
        private static Texture2D buttonBgHoverTexture;
        private static Texture2D separatorTexture;

        private static GUIStyle buttonStyle;
        private static GUIStyle windowStyle;
        private static GUIStyle closeButtonStyle;
        private static GUIStyle labelStyle;
        private static GUIStyle textFieldStyle;
        private static GUIStyle logTextStyle;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Alpha Testing Mod Menu Initialized successfully!");
            var harmony = new HarmonyLib.Harmony(nameof(TestMod));
            harmony.PatchAll();
            LoggerInstance.Msg("Harmony patches applied!");
            GenerateTextures();
        }

        private static bool IsSkillSlotButtonDisabled(SkillSlotButton button)
        {
            try
            {
                FieldInfo field = typeof(SkillSlotButton).GetField("disabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return (bool)field.GetValue(button);
                }
            }
            catch { }
            return false;
        }

        public override void OnUpdate()
        {
            if (autoskillsActive)
            {
                if (Time.time >= nextSkillTime)
                {
                    bool playerExists = false;
                    try
                    {
                        playerExists = (Entity.mainPlayer != null);
                    }
                    catch { }

                    if (playerExists)
                    {
                        if (skillOrder.Count > 0)
                        {
                            int checkedCount = 0;
                            bool found = false;
                            int targetSkillSlot = -1;

                            while (checkedCount < skillOrder.Count)
                            {
                                int tempSlot = skillOrder[currentSkillIndex];
                                if (tempSlot >= 0 && tempSlot < skillEnabled.Length && skillEnabled[tempSlot])
                                {
                                    targetSkillSlot = tempSlot;
                                    found = true;
                                    break;
                                }
                                currentSkillIndex = (currentSkillIndex + 1) % skillOrder.Count;
                                checkedCount++;
                            }

                            if (found && targetSkillSlot != -1)
                            {
                                try
                                {
                                    if (UISkillSlots.Instance != null)
                                    {
                                        SkillSlotButton slotBtn = UISkillSlots.Instance.GetSlot(targetSkillSlot);
                                        if (slotBtn != null && !IsSkillSlotButtonDisabled(slotBtn))
                                        {
                                            slotBtn.UseSkill(true);
                                            slotBtn.UseSkill(false);
                                            LoggerInstance.Msg($"Autoskill casted slot: {targetSkillSlot}");
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    LoggerInstance.Error($"Error casting autoskill: {ex}");
                                }

                                float delay = 1f;
                                if (skillDelays.ContainsKey(targetSkillSlot))
                                {
                                    delay = skillDelays[targetSkillSlot];
                                }

                                nextSkillTime = Time.time + delay;
                                currentSkillIndex = (currentSkillIndex + 1) % skillOrder.Count;
                            }
                            else
                            {
                                nextSkillTime = Time.time + 1f;
                            }
                        }
                        else
                        {
                            nextSkillTime = Time.time + 1f;
                        }
                    }
                    else
                    {
                        nextSkillTime = Time.time + 1f;
                    }
                }
            }
        }

        private void GenerateTextures()
        {
            try
            {
                Color defaultBorder = new(0.08f, 0.08f, 0.08f, 1f);
                Color hoverBorder = Color.white;

                buttonTexture = CreateThemedButtonTexture(defaultBorder);
                buttonHoverTexture = CreateThemedButtonTexture(hoverBorder);

                windowTexture = CreateThemedWindowTexture();

                buttonBgTexture = CreateThemedButtonBgTexture(defaultBorder);
                buttonBgHoverTexture = CreateThemedButtonBgTexture(hoverBorder);

                separatorTexture = new Texture2D(1, 1);
                separatorTexture.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 1f));
                separatorTexture.Apply();

                LoggerInstance.Msg("Generated UI textures.");
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"Failed to generate textures: {ex}");
            }
        }

        public override void OnGUI()
        {
            if (buttonTexture != null && buttonHoverTexture != null && buttonStyle == null)
            {
                buttonStyle = new GUIStyle();
                buttonStyle.normal.background = buttonTexture;
                buttonStyle.hover.background = buttonHoverTexture;
                buttonStyle.active.background = buttonHoverTexture;
            }

            if (windowTexture != null && windowStyle == null)
            {
                windowStyle = new GUIStyle();
                windowStyle.normal.background = windowTexture;
                windowStyle.border = new RectOffset(24, 24, 24, 24);
                windowStyle.normal.textColor = Color.white;
                windowStyle.alignment = TextAnchor.UpperCenter;
                windowStyle.fontStyle = FontStyle.Bold;
                windowStyle.fontSize = 14;
                windowStyle.padding = new RectOffset(0, 0, 12, 0);
            }

            if (buttonBgTexture != null && buttonBgHoverTexture != null && closeButtonStyle == null)
            {
                closeButtonStyle = new GUIStyle();
                closeButtonStyle.normal.background = buttonBgTexture;
                closeButtonStyle.hover.background = buttonBgHoverTexture;
                closeButtonStyle.active.background = buttonBgHoverTexture;
                closeButtonStyle.border = new RectOffset(12, 12, 12, 12);
                closeButtonStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                closeButtonStyle.hover.textColor = Color.white;
                closeButtonStyle.alignment = TextAnchor.MiddleCenter;
                closeButtonStyle.fontStyle = FontStyle.Bold;
                closeButtonStyle.fontSize = 12;
            }

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle();
                labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.fontStyle = FontStyle.Normal;
                labelStyle.fontSize = 13;
                labelStyle.richText = true;
            }

            if (logTextStyle == null)
            {
                logTextStyle = new GUIStyle();
                logTextStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                logTextStyle.alignment = TextAnchor.MiddleLeft;
                logTextStyle.fontStyle = FontStyle.Normal;
                logTextStyle.fontSize = 12;
                logTextStyle.richText = true;
            }

            if (textFieldStyle == null)
            {
                textFieldStyle = new GUIStyle(GUI.skin.textField);
                textFieldStyle.alignment = TextAnchor.MiddleCenter;
                textFieldStyle.fontStyle = FontStyle.Normal;
                textFieldStyle.fontSize = 13;
                textFieldStyle.normal.textColor = Color.white;
                textFieldStyle.focused.textColor = Color.white;
            }

            if (rowButtonStyle == null)
            {
                rowButtonStyle = new GUIStyle(GUI.skin.button);
                rowButtonStyle.alignment = TextAnchor.MiddleLeft;
                rowButtonStyle.fontStyle = FontStyle.Normal;
                rowButtonStyle.fontSize = 12;
                rowButtonStyle.richText = true;
                rowButtonStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                rowButtonStyle.hover.textColor = Color.white;
            }

            if (previewTextStyle == null)
            {
                previewTextStyle = new GUIStyle(GUI.skin.textArea);
                previewTextStyle.wordWrap = false;
                previewTextStyle.richText = false;
                previewTextStyle.fontSize = 12;
                previewTextStyle.normal.textColor = Color.white;
            }

            if (buttonStyle != null)
            {
                if (GUI.Button(ToggleButtonRect, "", buttonStyle))
                {
                    showWindow = !showWindow;
                }
            }
            else
            {
                if (GUI.Button(ToggleButtonRect, "Toggle Menu"))
                {
                    showWindow = !showWindow;
                }
            }

            if (showWindow)
            {
                if (windowStyle != null)
                {
                    windowRect = GUI.Window(9999, windowRect, DrawWindow, "Mod Menu", windowStyle);
                }
                else
                {
                    windowRect = GUI.Window(9999, windowRect, DrawWindow, "Mod Menu");
                }
            }

            if (showWindow && showConfigWindow)
            {
                if (windowStyle != null)
                {
                    configWindowRect = GUI.Window(9998, configWindowRect, DrawConfigWindow, "Autoskills Config", windowStyle);
                }
                else
                {
                    configWindowRect = GUI.Window(9998, configWindowRect, DrawConfigWindow, "Autoskills Config");
                }
            }

            if (showWindow && showInterceptorWindow)
            {
                if (windowStyle != null)
                {
                    interceptorWindowRect = GUI.Window(9997, interceptorWindowRect, DrawInterceptorWindow, "Packet Interceptor", windowStyle);
                }
                else
                {
                    interceptorWindowRect = GUI.Window(9997, interceptorWindowRect, DrawInterceptorWindow, "Packet Interceptor");
                }
            }

            if (showWindow && showSnifferWindow)
            {
                if (windowStyle != null)
                {
                    snifferWindowRect = GUI.Window(9996, snifferWindowRect, DrawSnifferWindow, "Packet Sniffer", windowStyle);
                }
                else
                {
                    snifferWindowRect = GUI.Window(9996, snifferWindowRect, DrawSnifferWindow, "Packet Sniffer");
                }
            }

            if (showWindow && showSenderWindow)
            {
                if (windowStyle != null)
                {
                    senderWindowRect = GUI.Window(9995, senderWindowRect, DrawSenderWindow, "Packet Sender", windowStyle);
                }
                else
                {
                    senderWindowRect = GUI.Window(9995, senderWindowRect, DrawSenderWindow, "Packet Sender");
                }
            }
        }

        private void DrawWindow(int windowID)
        {
            GUI.Label(new Rect(20, 35, 260, 25), "Test Mod Implementation", labelStyle);

            string accessLevelText = "Player is null";
            bool is101 = false;
            int currentLevel = -1;

            try
            {
                if (Entity.mainPlayer != null)
                {
                    currentLevel = Entity.mainPlayer.AccessLevel;
                    is101 = (currentLevel == 101);
                    accessLevelText = is101 ? "Player: 101" : $"Access: {currentLevel}";
                }
            }
            catch (System.Exception ex)
            {
                accessLevelText = "Error reading player";
                LoggerInstance.Error($"Error reading Entity.mainPlayer properties: {ex}");
            }

            bool playerExists = false;
            try
            {
                playerExists = (Entity.mainPlayer != null);
            }
            catch { }

            if (playerExists)
            {
                if (GUI.Button(new Rect(20, 70, 80, 35), "FakeDev", closeButtonStyle))
                {
                    try
                    {
                        Entity.mainPlayer.AccessLevel = 101;
                        LoggerInstance.Msg("Set access level to 101.");
                    }
                    catch (System.Exception ex)
                    {
                        LoggerInstance.Error($"Error setting access level: {ex}");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, 70, 80, 35), "FakeDev", closeButtonStyle);
                GUI.enabled = true;
            }

            GUI.Label(new Rect(105, 70, 90, 35), accessLevelText, labelStyle);

            if (playerExists)
            {
                if (GUI.Button(new Rect(200, 70, 80, 35), "Dev UI", closeButtonStyle))
                {
                    try
                    {
                        new DevWindow(new System.Collections.Generic.List<string>()).Execute();
                        LoggerInstance.Msg("Opened dev window.");
                    }
                    catch (System.Exception ex)
                    {
                        LoggerInstance.Error($"Error executing DevWindow: {ex}");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(200, 70, 80, 35), "Dev UI", closeButtonStyle);
                GUI.enabled = true;
            }

            if (separatorTexture != null)
            {
                GUI.DrawTexture(new Rect(20, 111, 260, 2), separatorTexture);
            }

            GUI.Label(new Rect(150, 125, 130, 20), "Shop ID:", labelStyle);

            if (playerExists)
            {
                if (GUI.Button(new Rect(20, 120, 120, 35), "Load Shop", closeButtonStyle))
                {
                    if (int.TryParse(shopIdInput, out int shopId))
                    {
                        try
                        {
                            AEC.Instance.sendRequest(new RequestLoadShop(shopId));
                            LoggerInstance.Msg($"Requested load shop: {shopId}");
                        }
                        catch (System.Exception ex)
                        {
                            LoggerInstance.Error($"Error loading shop {shopId}: {ex}");
                        }
                    }
                    else
                    {
                        LoggerInstance.Error($"Invalid shop ID input: '{shopIdInput}'");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, 120, 120, 35), "Load Shop", closeButtonStyle);
                GUI.enabled = true;
            }

            shopIdInput = GUI.TextField(new Rect(150, 150, 130, 35), shopIdInput, textFieldStyle);

            if (playerExists)
            {
                if (GUI.Button(new Rect(20, 160, 120, 35), "Load Merge", closeButtonStyle))
                {
                    if (int.TryParse(shopIdInput, out int shopId))
                    {
                        try
                        {
                            forceMergeShop = true;
                            AEC.Instance.sendRequest(new RequestLoadShop(shopId));
                            LoggerInstance.Msg($"Requested load merge shop: {shopId}");
                        }
                        catch (System.Exception ex)
                        {
                            forceMergeShop = false;
                            LoggerInstance.Error($"Error loading merge shop {shopId}: {ex}");
                        }
                    }
                    else
                    {
                        LoggerInstance.Error($"Invalid shop ID input: '{shopIdInput}'");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, 160, 120, 35), "Load Merge", closeButtonStyle);
                GUI.enabled = true;
            }

            if (separatorTexture != null)
            {
                GUI.DrawTexture(new Rect(20, 202, 260, 2), separatorTexture);
            }

            GUI.Label(new Rect(150, 225, 130, 20), "Quest ID:", labelStyle);

            if (playerExists)
            {
                if (GUI.Button(new Rect(20, 220, 120, 35), "Load Quest", closeButtonStyle))
                {
                    if (int.TryParse(questIdInput, out int questId))
                    {
                        try
                        {
                            UIQuests.ShowQuestUI(new System.Collections.Generic.List<int> { questId }, QuestMode.Quest, null);
                            LoggerInstance.Msg($"Requested load quest: {questId}");
                        }
                        catch (System.Exception ex)
                        {
                            LoggerInstance.Error($"Error loading quest {questId}: {ex}");
                        }
                    }
                    else
                    {
                        LoggerInstance.Error($"Invalid quest ID input: '{questIdInput}'");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, 220, 120, 35), "Load Quest", closeButtonStyle);
                GUI.enabled = true;
            }
            questIdInput = GUI.TextField(new Rect(150, 250, 130, 35), questIdInput, textFieldStyle);

            if (playerExists)
            {
                if (GUI.Button(new Rect(20, 260, 120, 35), "Abandon", closeButtonStyle))
                {
                    if (int.TryParse(questIdInput, out int questId))
                    {
                        try
                        {
                            AEC.Instance.sendRequest(new RequestAbandonQuest(questId.ToString()));
                            LoggerInstance.Msg($"Requested abandon quest: {questId}");
                        }
                        catch (System.Exception ex)
                        {
                            LoggerInstance.Error($"Error abandoning quest {questId}: {ex}");
                        }
                    }
                    else
                    {
                        LoggerInstance.Error($"Invalid quest ID input for abandon: '{questIdInput}'");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, 260, 120, 35), "Abandon", closeButtonStyle);
                GUI.enabled = true;
            }
            if (separatorTexture != null)
            {
                GUI.DrawTexture(new Rect(20, 307, 260, 2), separatorTexture);
            }

            string autoSkillsText = autoskillsActive ? "Autoskills: ON" : "Autoskills: OFF";
            if (playerExists)
            {
                if (GUI.Button(new Rect(20, 315, 120, 35), autoSkillsText, closeButtonStyle))
                {
                    autoskillsActive = !autoskillsActive;
                    if (autoskillsActive)
                    {
                        currentSkillIndex = 0;
                        nextSkillTime = Time.time;
                        LoggerInstance.Msg("Autoskills activated!");
                    }
                    else
                    {
                        LoggerInstance.Msg("Autoskills deactivated!");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, 315, 120, 35), "Autoskills: OFF", closeButtonStyle);
                GUI.enabled = true;
                autoskillsActive = false;
            }

            if (GUI.Button(new Rect(150, 315, 130, 35), "Config", closeButtonStyle))
            {
                showConfigWindow = !showConfigWindow;
            }

            if (separatorTexture != null)
            {
                GUI.DrawTexture(new Rect(20, 362, 260, 2), separatorTexture);
            }

            string interceptorBtnText = showInterceptorWindow ? "Hide Interceptor" : "Packet Interceptor";
            if (GUI.Button(new Rect(20, 370, 260, 35), interceptorBtnText, closeButtonStyle))
            {
                showInterceptorWindow = !showInterceptorWindow;
            }

            string snifferBtnText = showSnifferWindow ? "Hide Sniffer" : "Packet Sniffer";
            if (GUI.Button(new Rect(20, 410, 260, 35), snifferBtnText, closeButtonStyle))
            {
                showSnifferWindow = !showSnifferWindow;
            }

            string senderBtnText = showSenderWindow ? "Hide Sender" : "Packet Sender";
            if (GUI.Button(new Rect(20, 450, 260, 35), senderBtnText, closeButtonStyle))
            {
                showSenderWindow = !showSenderWindow;
            }

            if (closeButtonStyle != null)
            {
                if (GUI.Button(new Rect(20, 490, 260, 35), "Close", closeButtonStyle))
                {
                    showWindow = false;
                }
            }
            else
            {
                if (GUI.Button(new Rect(20, 490, 260, 35), "Close"))
                {
                    showWindow = false;
                }
            }

            GUI.DragWindow(new Rect(0, 0, windowRect.width, 30));
        }

        private static string GetSkillKeyName(int slot)
        {
            if (slot == 0) return "Key 1 (Auto)";
            return $"Key {slot + 1}";
        }

        private void DrawConfigWindow(int windowID)
        {
            GUI.Label(new Rect(20, 35, 280, 20), "Configure Skill Delays & Order", labelStyle);
            GUI.Label(new Rect(20, 60, 90, 20), "Skill", labelStyle);
            GUI.Label(new Rect(115, 60, 65, 20), "Delay (ms)", labelStyle);
            GUI.Label(new Rect(190, 60, 70, 20), "Order", labelStyle);
            GUI.Label(new Rect(268, 60, 32, 20), "Auto", labelStyle);

            int startY = 85;
            for (int i = 0; i < skillOrder.Count; i++)
            {
                int slot = skillOrder[i];
                int currentY = startY + i * 32;

                GUI.Label(new Rect(20, currentY, 90, 25), GetSkillKeyName(slot), labelStyle);

                string delayStr = delayInputs[slot];
                string newDelayStr = GUI.TextField(new Rect(115, currentY, 65, 25), delayStr, textFieldStyle);
                if (newDelayStr != delayStr)
                {
                    delayInputs[slot] = newDelayStr;
                    if (float.TryParse(newDelayStr, out float ms))
                    {
                        skillDelays[slot] = ms / 1000f;
                    }
                }

                if (i > 0)
                {
                    if (GUI.Button(new Rect(190, currentY, 32, 25), "▲", closeButtonStyle))
                    {
                        (skillOrder[i - 1], skillOrder[i]) = (skillOrder[i], skillOrder[i - 1]);
                    }
                }

                if (i < skillOrder.Count - 1)
                {
                    if (GUI.Button(new Rect(228, currentY, 32, 25), "▼", closeButtonStyle))
                    {
                        (skillOrder[i + 1], skillOrder[i]) = (skillOrder[i], skillOrder[i + 1]);
                    }
                }

                if (slot >= 0 && slot < skillEnabled.Length)
                {
                    skillEnabled[slot] = GUI.Toggle(new Rect(272, currentY + 3, 20, 20), skillEnabled[slot], "");
                }
            }

            if (GUI.Button(new Rect(20, 250, 280, 30), "Close Config", closeButtonStyle))
            {
                showConfigWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, configWindowRect.width, 30));
        }

        private void DrawInterceptorWindow(int windowID)
        {
            float winWidth = interceptorWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            string interceptorStatus = interceptActive ? "<color=red>STATUS: INTERCEPTING</color>" : "<color=green>STATUS: PASSIVE</color>";
            GUI.Label(new Rect(pad, 35, innerW - 130, 20), interceptorStatus, labelStyle);

            interceptorLoggingActive = GUI.Toggle(new Rect(pad + innerW - 130, 35, 20, 20), interceptorLoggingActive, "");
            GUI.Label(new Rect(pad + innerW - 105, 35, 105, 20), "Log Allowed", labelStyle);

            float btnW = (innerW - 10) / 3f;
            if (GUI.Button(new Rect(pad, 65, btnW, 35), "Block Packets", closeButtonStyle))
            {
                interceptActive = true;
                LoggerInstance.Msg("Packet interception STARTED.");
            }

            if (GUI.Button(new Rect(pad + btnW + 5, 65, btnW, 35), "Allow Packets", closeButtonStyle))
            {
                interceptActive = false;
                LoggerInstance.Msg("Packet interception STOPPED.");
            }

            if (GUI.Button(new Rect(pad + (btnW + 5) * 2, 65, btnW, 35), "Clear Logs", closeButtonStyle))
            {
                lock (interceptedPacketsLog)
                {
                    interceptedPacketsLog.Clear();
                }
                LoggerInstance.Msg("Packet log cleared.");
            }

            GUI.Box(new Rect(pad, 115, innerW, 180), "", GUI.skin.box);

            float intContentHeight = 170f;
            lock (interceptedPacketsLog)
            {
                intContentHeight = Mathf.Max(170f, interceptedPacketsLog.Count * 22f);
            }

            interceptorScrollPosition = GUI.BeginScrollView(
                new Rect(pad, 115, innerW, 180),
                interceptorScrollPosition,
                new Rect(0, 0, innerW - 20, intContentHeight)
            );

            lock (interceptedPacketsLog)
            {
                for (int i = 0; i < interceptedPacketsLog.Count; i++)
                {
                    GUI.Label(new Rect(10, 5 + i * 22, innerW - 40, 20), interceptedPacketsLog[i], logTextStyle);
                }
            }

            GUI.EndScrollView();

            if (GUI.Button(new Rect(pad, 310, innerW, 35), "Close Interceptor", closeButtonStyle))
            {
                showInterceptorWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, interceptorWindowRect.width, 30));
        }

        private void DrawSnifferWindow(int windowID)
        {
            float winWidth = snifferWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            float sniffBtnW = (innerW - 15) / 4f;

            string serverBtnText = snifferServerActive ? "Server: ON" : "Server: OFF";
            if (GUI.Button(new Rect(pad, 35, sniffBtnW, 35), serverBtnText, closeButtonStyle))
            {
                snifferServerActive = !snifferServerActive;
                LoggerInstance.Msg($"Sniffer Server: {(snifferServerActive ? "ON" : "OFF")}");
            }

            string clientBtnText = snifferClientActive ? "Client: ON" : "Client: OFF";
            if (GUI.Button(new Rect(pad + sniffBtnW + 5, 35, sniffBtnW, 35), clientBtnText, closeButtonStyle))
            {
                snifferClientActive = !snifferClientActive;
                LoggerInstance.Msg($"Sniffer Client: {(snifferClientActive ? "ON" : "OFF")}");
            }

            bool bothActive = snifferServerActive && snifferClientActive;
            string allBtnText = bothActive ? "All: ON" : "All: OFF";
            if (GUI.Button(new Rect(pad + (sniffBtnW + 5) * 2, 35, sniffBtnW, 35), allBtnText, closeButtonStyle))
            {
                if (bothActive)
                {
                    snifferServerActive = false;
                    snifferClientActive = false;
                }
                else
                {
                    snifferServerActive = true;
                    snifferClientActive = true;
                }
                LoggerInstance.Msg($"Sniffer All: Server={snifferServerActive}, Client={snifferClientActive}");
            }

            if (GUI.Button(new Rect(pad + (sniffBtnW + 5) * 3, 35, sniffBtnW, 35), "Clear", closeButtonStyle))
            {
                lock (snifferLog)
                {
                    snifferLog.Clear();
                    selectedSniffIndex = -1;
                    selectedPacketJson = "";
                }
                LoggerInstance.Msg("Sniffer log cleared.");
            }

            GUI.Box(new Rect(pad, 80, innerW, 220), "", GUI.skin.box);

            float sniffContentHeight = 210f;
            lock (snifferLog)
            {
                sniffContentHeight = Mathf.Max(210f, snifferLog.Count * 26f + 10f);
            }

            snifferScrollPosition = GUI.BeginScrollView(
                new Rect(pad, 80, innerW, 220),
                snifferScrollPosition,
                new Rect(0, 0, innerW - 20, sniffContentHeight)
            );

            lock (snifferLog)
            {
                for (int i = 0; i < snifferLog.Count; i++)
                {
                    float yPos = 5 + i * 26;
                    if (selectedSniffIndex == i)
                    {
                        GUI.Box(new Rect(5, yPos, innerW - 90, 22), "");
                    }

                    if (GUI.Button(new Rect(5, yPos, innerW - 90, 22), snifferLog[i].DisplayText, rowButtonStyle))
                    {
                        selectedSniffIndex = i;
                        selectedPacketJson = snifferLog[i].RawJson;
                        selectedPacketPreviewScroll = Vector2.zero;
                    }

                    if (GUI.Button(new Rect(innerW - 80, yPos, 60, 22), "Copy", closeButtonStyle))
                    {
                        UnityEngine.GUIUtility.systemCopyBuffer = snifferLog[i].RawJson;
                        LoggerInstance.Msg("[Packet Sniffer] Copied packet JSON to clipboard.");
                    }
                }
            }

            GUI.EndScrollView();

            GUI.Label(new Rect(pad, 310, innerW, 20), "Selected Packet JSON Preview:", labelStyle);

            Vector2 previewSize = previewTextStyle != null ? previewTextStyle.CalcSize(new GUIContent(selectedPacketJson)) : Vector2.zero;
            float minContentW = innerW - 4;
            float minContentH = 120 - 4;
            float contentWidth = Mathf.Max(minContentW, previewSize.x + 20);
            float contentHeight = Mathf.Max(minContentH, previewSize.y + 20);

            selectedPacketPreviewScroll = GUI.BeginScrollView(
                new Rect(pad, 335, innerW, 120),
                selectedPacketPreviewScroll,
                new Rect(0, 0, contentWidth, contentHeight)
            );

            selectedPacketJson = GUI.TextArea(
                new Rect(0, 0, contentWidth, contentHeight),
                selectedPacketJson,
                previewTextStyle
            );

            GUI.EndScrollView();

            if (GUI.Button(new Rect(pad, 465, 160, 35), "Copy Selected JSON", closeButtonStyle))
            {
                if (!string.IsNullOrEmpty(selectedPacketJson))
                {
                    UnityEngine.GUIUtility.systemCopyBuffer = selectedPacketJson;
                    LoggerInstance.Msg("[Packet Sniffer] Copied selected packet JSON to clipboard.");
                }
            }

            if (GUI.Button(new Rect(pad + 170, 465, innerW - 170, 35), "Close Sniffer", closeButtonStyle))
            {
                showSnifferWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
        }

        private void DrawSenderWindow(int windowID)
        {
            float winWidth = senderWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            GUI.Label(new Rect(pad, 35, innerW, 20), "Manual Inject (Send one packet)", labelStyle);

            float Y = 70f;

            GUI.Label(new Rect(20, Y, 40, 25), "Cmd:", labelStyle);
            senderCmdInput = GUI.TextField(new Rect(60, Y, 70, 25), senderCmdInput, textFieldStyle);

            GUI.Label(new Rect(140, Y, 130, 25), "Params (comma-sep):", labelStyle);
            senderParamsInput = GUI.TextField(new Rect(270, Y, 160, 25), senderParamsInput, textFieldStyle);

            if (GUI.Button(new Rect(440, Y, 40, 25), "Send", closeButtonStyle))
            {
                string cmd = senderCmdInput.Trim();
                string paramsRaw = senderParamsInput;

                System.Collections.Generic.List<string> paramsList = new();
                if (!string.IsNullOrEmpty(paramsRaw))
                {
                    string[] parts = paramsRaw.Split(',');
                    foreach (string part in parts)
                    {
                        paramsList.Add(part.Trim());
                    }
                }

                // auto replace <charname> <username> groundwork, no idea. Going on a whim.
                for (int i = 0; i < paramsList.Count; i++)
                {
                    if (paramsList[i].Equals("<charname>", System.StringComparison.OrdinalIgnoreCase) ||
                        paramsList[i].Equals("<username>", System.StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (Entity.mainPlayer != null)
                            {
                                paramsList[i] = Entity.mainPlayer.Name;
                            }
                        }
                        catch { }
                    }
                }

                try
                {
                    if (AEC.Instance != null)
                    {
                        AEC.Instance.sendRequest(new Request(cmd, paramsList));
                        LoggerInstance.Msg($"[Packet Sender] Sent manually injected packet: Cmd='{cmd}', Params=[{string.Join(", ", paramsList)}]");
                    }
                    else
                    {
                        LoggerInstance.Error("AEC.Instance is null, cannot send packet.");
                    }
                }
                catch (System.Exception ex)
                {
                    LoggerInstance.Error($"Error sending manual packet: {ex.Message}");
                }
            }

            if (GUI.Button(new Rect(pad, 115, innerW, 35), "Close Sender", closeButtonStyle))
            {
                showSenderWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, senderWindowRect.width, 30));
        }

        public static bool IsMouseOverUI()
        {
            float mouseX = Input.mousePosition.x;
            float mouseY = Screen.height - Input.mousePosition.y;
            Vector2 imguiMousePos = new(mouseX, mouseY);

            if (ToggleButtonRect.Contains(imguiMousePos))
            {
                return true;
            }

            if (showWindow && windowRect.Contains(imguiMousePos))
            {
                return true;
            }
            if (showWindow && showConfigWindow && configWindowRect.Contains(imguiMousePos))
            {
                return true;
            }
            if (showWindow && showInterceptorWindow && interceptorWindowRect.Contains(imguiMousePos))
            {
                return true;
            }

            if (showWindow && showSnifferWindow && snifferWindowRect.Contains(imguiMousePos))
            {
                return true;
            }

            if (showWindow && showSenderWindow && senderWindowRect.Contains(imguiMousePos))
            {
                return true;
            }

            return false;
        }

        private static Texture2D CreateThemedButtonTexture(Color borderColor)
        {
            int size = 128;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;

                    float px = x - 64f;
                    float py = y - 64f;

                    Vector2 pBox = new(Mathf.Abs(px) - 56f + 18f, Mathf.Abs(py) - 56f + 18f);
                    float boxDist = Mathf.Min(Mathf.Max(pBox.x, pBox.y), 0.0f) + new Vector2(Mathf.Max(pBox.x, 0.0f), Mathf.Max(pBox.y, 0.0f)).magnitude - 18f;

                    if (boxDist > 0f)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    Color c;

                    if (boxDist > -4f)
                    {
                        c = borderColor;
                    }
                    else
                    {
                        float tBg = (y - 8f) / 112f;
                        Color topBg = new(0.35f, 0.35f, 0.35f, 1f);
                        Color bottomBg = new(0.12f, 0.12f, 0.12f, 1f);
                        c = Color.Lerp(bottomBg, topBg, tBg);

                        float angle = (x - 64f) * Mathf.PI / 112f;
                        float glossBoundary = 64f + 14f * Mathf.Cos(angle);
                        if (y > glossBoundary)
                        {
                            c += new Color(0.08f, 0.08f, 0.08f, 0f);
                        }
                    }

                    float hx = x;
                    float hy = y;

                    bool inExcl = IsInExclamationMark(hx, hy, out float exclDist);

                    if (inExcl)
                    {
                        if (exclDist >= -2f)
                        {
                            c = new Color(0.08f, 0.08f, 0.08f, 1f);
                        }
                        else
                        {

                            float tExcl = Mathf.Clamp01((hy - 30f) / 60f);
                            Color orangeSide = new(1.0f, 0.40f, 0.05f, 1f);
                            Color yellowSide = new(1.0f, 0.95f, 0.15f, 1f);
                            Color exclCol = Color.Lerp(orangeSide, yellowSide, tExcl);

                            if (hy >= 54f && hy <= 86f && hx >= 60f && hx < 64f && exclDist < -2.5f)
                            {
                                float edgeHighlight = (64f - hx) / 4f;
                                exclCol = Color.Lerp(exclCol, Color.white, edgeHighlight * 0.7f);
                            }

                            float distHighlightDot = Vector2.Distance(new Vector2(hx, hy), new Vector2(61f, 41f));
                            if (distHighlightDot < 3f)
                            {
                                float tHighlight = 1f - (distHighlightDot / 3f);
                                exclCol = Color.Lerp(exclCol, Color.white, tHighlight * 0.7f);
                            }

                            c = exclCol;
                        }
                    }
                    else
                    {
                        if (exclDist > 0f && exclDist < 2.5f)
                        {
                            float tBorder = exclDist / 2.5f;
                            c = Color.Lerp(new Color(0.05f, 0.05f, 0.05f, 1f), c, tBorder);
                        }
                    }

                    pixels[index] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateThemedWindowTexture()
        {
            int size = 128;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;

                    float px = x - 64f;
                    float py = y - 64f;

                    Vector2 pBox = new(Mathf.Abs(px) - 58f + 18f, Mathf.Abs(py) - 58f + 18f);
                    float boxDist = Mathf.Min(Mathf.Max(pBox.x, pBox.y), 0.0f) + new Vector2(Mathf.Max(pBox.x, 0.0f), Mathf.Max(pBox.y, 0.0f)).magnitude - 18f;

                    if (boxDist > 0f)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    Color c;

                    if (boxDist > -4f)
                    {
                        c = new Color(0.08f, 0.08f, 0.08f, 1f);
                    }
                    else
                    {
                        float tBg = (y - 4f) / 120f;
                        Color topBg = new(0.35f, 0.35f, 0.35f, 1f);
                        Color bottomBg = new(0.12f, 0.12f, 0.12f, 1f);
                        c = Color.Lerp(bottomBg, topBg, tBg);

                        if (y > 96)
                        {
                            c += new Color(0.08f, 0.08f, 0.08f, 0f);
                        }
                    }

                    pixels[index] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateThemedButtonBgTexture(Color borderColor)
        {
            int size = 64;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = (y * size) + x;

                    float px = x - 32f;
                    float py = y - 32f;

                    Vector2 pBox = new(Mathf.Abs(px) - 28f + 10f, Mathf.Abs(py) - 28f + 10f);
                    float boxDist = Mathf.Min(Mathf.Max(pBox.x, pBox.y), 0.0f) + new Vector2(Mathf.Max(pBox.x, 0.0f), Mathf.Max(pBox.y, 0.0f)).magnitude - 10f;

                    if (boxDist > 0f)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    Color c;

                    if (boxDist > -2f)
                    {
                        c = borderColor;
                    }
                    else
                    {
                        float tBg = (y - 2f) / 60f;
                        Color topBg = new(0.35f, 0.35f, 0.35f, 1f);
                        Color bottomBg = new(0.12f, 0.12f, 0.12f, 1f);
                        c = Color.Lerp(bottomBg, topBg, tBg);

                        if (y > 48)
                        {
                            c += new Color(0.08f, 0.08f, 0.08f, 0f);
                        }
                    }

                    pixels[index] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            Vector2 ap = p - a;
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab));
            return Vector2.Distance(p, a + t * ab);
        }

        private static bool IsInExclamationMark(float x, float y, out float distance)
        {
            float tSeg = Mathf.Clamp01((y - 54f) / 34f);
            float thickness = Mathf.Lerp(3.5f, 6.5f, tSeg);
            float dBar = DistanceToLineSegment(new Vector2(x, y), new Vector2(64f, 88f), new Vector2(64f, 54f)) - thickness;
            float dDot = Vector2.Distance(new Vector2(x, y), new Vector2(64f, 38f)) - 6.5f;

            distance = Mathf.Min(dBar, dDot);
            return distance <= 0f;
        }
    }
}
