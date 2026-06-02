using MelonLoader;
using System.Reflection;
using UnityEngine;
using Infinity_TestMod.Patches;
using Infinity_TestMod.Util;


namespace Infinity_TestMod
{
    public class TestMod : MelonMod
    {
        public static bool showWindow = false;
        public static Rect windowRect = new(20, 100, 300, 610);
        public static readonly Rect ToggleButtonRect = new(10, 20, 64, 64);

        public static bool forceMergeShop = false;
        private static string shopIdInput = "";
        private static string questIdInput = "";

        public static bool autoskillsActive = false;
        public static bool showConfigWindow = false;
        public static Rect configWindowRect = new(330, 100, 320, 360);

        public static bool showFakeDevWindow = false;
        public static Rect fakeDevWindowRect = new(330, 410, 320, 280);
        private static bool defaultsCaptured = false;
        private static int defaultUpgradeDays = 0;
        private static int defaultAccessLevel = 0;
        private static string defaultPlayerName = "";
        private static string nameSpoofInput = "";
        public static bool nameSpoofActive = false;
        public static string spoofedName = "";

        // "Fun" window — home for visual/local spoofers (name, gear, future).
        // Sized to fit Name Spoof + Armor Spoof rows + armor catalog picker.
        public static bool showFunWindow = false;
        public static Rect funWindowRect = new(330, 410, 360, 560);

        // Gear Spoof — one entry per visual slot (Helm, Armor, Back/Cape).
        // Each holds the active spoof bundle Filename. Version metadata is
        // borrowed at load time from the real equipped item so CDN URLs
        // resolve. Cleared by user via the Clear button.
        public static bool helmSpoofActive = false;
        public static string helmSpoofBundle = "";
        private static string helmSpoofInput = "";

        public static bool armorSpoofActive = false;
        public static string armorSpoofBundle = "";
        private static string armorSpoofInput = "";

        public static bool backSpoofActive = false;
        public static string backSpoofBundle = "";
        private static string backSpoofInput = "";

        // Gender flip — mutates Entity.mainPlayer.Gender (enum field) while
        // active so every gender consumer (avatar rig prefab, pronouns,
        // hair option matchers) sees the flipped value uniformly. Original
        // is stashed in `genderSpoofOriginal` and restored on toggle off.
        public static bool genderSpoofActive = false;
        private static Player.genders genderSpoofOriginal = Player.genders.Male;

        // Shared catalog dropdown: only one slot's picker is expanded at a
        // time (0=none, 1=Helm, 2=Armor, 3=Back). Filter+scroll persist
        // across openings so a search isn't lost when switching slots.
        private static int catalogOpenSlot = 0;
        private static string catalogFilter = "";
        private static Vector2 catalogScroll = Vector2.zero;

        public static bool showShopLoaderWindow = false;
        public static Rect shopLoaderWindowRect = new(330, 100, 280, 205);

        public static bool showQuestLoaderWindow = false;
        public static Rect questLoaderWindowRect = new(330, 315, 280, 205);

        public static bool showInterceptorWindow = false;
        public static Rect interceptorWindowRect = new(660, 100, 500, 365);
        public static bool showSnifferWindow = false;
        public static Rect snifferWindowRect = new(660, 480, 500, 520);
        public static bool showSenderWindow = false;
        public static Rect senderWindowRect = new(660, 865, 500, 200);
        private static string senderCmdInput = "tfer";
        private static string senderParamsInput = "<charname>,lair,0,Enter,Spawn";
        // When true the Sender skips comma-splitting and sends the whole input
        // as a single Param string — needed for chat-style commands where the
        // payload contains literal commas (e.g. `message`: "hi, friend").
        private static bool senderSingleString = false;

        // Packet Receiver: inject server→client packets locally.
        public static bool showReceiverWindow = false;
        public static Rect receiverWindowRect = new(660, 1040, 500, 315);

        // QuestRunner: end-to-end automation. Single instance, ticked from
        // OnUpdate so all game-side calls (target setting, request sends)
        // stay on the Unity main thread.
        public static QuestRunner questRunner = new QuestRunner();
        public static bool showQuestRunnerWindow = false;
        public static Rect questRunnerWindowRect = new(20, 660, 640, 480);
        private static string questRunnerIdInput = "1";
        private static string questRunnerItersInput = "10";
        // Optional auto-travel before hunting. Empty Area = stay in current area
        // (no tfer); empty Frame = stay in current cell (no moveToCell).
        private static string questRunnerAreaInput = "";
        private static string questRunnerFrameInput = "";
        private static string questRunnerPadInput = "Spawn";
        public static System.Collections.Generic.List<string> questRunnerLog = new();
        private static Vector2 questRunnerLogScroll = Vector2.zero;
        private static bool showQuestPicker = false;
        private static string questPickerFilter = "";
        private static Vector2 questPickerScroll = Vector2.zero;
        // Chain picker: index into QuestChains.Names + button to run.
        private static int questChainPickerIndex = 0;
        private static string receiverJsonInput = "{\n  \"Cmd\": \"\",\n  \"Params\": {}\n}";
        private static Vector2 receiverScrollPosition = Vector2.zero;
        private static System.Reflection.MethodInfo _wrapAndQueueResponseMethod = null;
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
            PacketLog.Init();
            Directory.Init();
            ItemCatalog.Init();
            QuestChains.Init();
            var harmony = new HarmonyLib.Harmony(nameof(TestMod));
            harmony.PatchAll();
            LoggerInstance.Msg("Harmony patches applied!");
            GenerateTextures();
        }

        public override void OnApplicationQuit()
        {
            Directory.Save();
            ItemCatalog.Save();
            PacketLog.Close();
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
            // Tick the quest runner every frame. It's a no-op when Idle/Done/Failed.
            try { questRunner?.Tick(); } catch (System.Exception ex) { LoggerInstance.Error($"QuestRunner tick: {ex.Message}"); }

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
                 separatorTexture.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.08f, 1f));
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

            if (showWindow && showReceiverWindow)
            {
                if (windowStyle != null)
                {
                    receiverWindowRect = GUI.Window(9994, receiverWindowRect, DrawReceiverWindow, "Packet Receiver", windowStyle);
                }
                else
                {
                    receiverWindowRect = GUI.Window(9994, receiverWindowRect, DrawReceiverWindow, "Packet Receiver");
                }
            }

            if (showWindow && showFakeDevWindow)
            {
                if (windowStyle != null)
                {
                    fakeDevWindowRect = GUI.Window(9992, fakeDevWindowRect, DrawFakeDevWindow, "FakeDev Settings", windowStyle);
                }
                else
                {
                    fakeDevWindowRect = GUI.Window(9992, fakeDevWindowRect, DrawFakeDevWindow, "FakeDev Settings");
                }
            }

            if (showWindow && showShopLoaderWindow)
            {
                if (windowStyle != null)
                {
                    shopLoaderWindowRect = GUI.Window(9991, shopLoaderWindowRect, DrawShopLoaderWindow, "Shop Loader", windowStyle);
                }
                else
                {
                    shopLoaderWindowRect = GUI.Window(9991, shopLoaderWindowRect, DrawShopLoaderWindow, "Shop Loader");
                }
            }

            if (showWindow && showQuestLoaderWindow)
            {
                if (windowStyle != null)
                {
                    questLoaderWindowRect = GUI.Window(9990, questLoaderWindowRect, DrawQuestLoaderWindow, "Quest Loader", windowStyle);
                }
                else
                {
                    questLoaderWindowRect = GUI.Window(9990, questLoaderWindowRect, DrawQuestLoaderWindow, "Quest Loader");
                }
            }

            if (showWindow && showQuestRunnerWindow)
            {
                if (windowStyle != null)
                {
                    questRunnerWindowRect = GUI.Window(9993, questRunnerWindowRect, DrawQuestRunnerWindow, "Quest Runner", windowStyle);
                }
                else
                {
                    questRunnerWindowRect = GUI.Window(9993, questRunnerWindowRect, DrawQuestRunnerWindow, "Quest Runner");
                }
            }

            if (showWindow && showFunWindow)
            {
                if (windowStyle != null)
                {
                    funWindowRect = GUI.Window(9989, funWindowRect, DrawFunWindow, "Fun", windowStyle);
                }
                else
                {
                    funWindowRect = GUI.Window(9989, funWindowRect, DrawFunWindow, "Fun");
                }
            }
        }

        private void DrawWindow(int windowID)
        {
            GUI.Label(new Rect(20, 35, 260, 25), "Test Mod Implementation", labelStyle);

            // Read current access level once for the tier buttons' active marker.
            int currentLevel = -1;
            try
            {
                if (Entity.mainPlayer != null)
                {
                    currentLevel = Entity.mainPlayer.AccessLevel;
                    if (!defaultsCaptured)
                    {
                        defaultUpgradeDays = Entity.mainPlayer.UpgradeDays;
                        defaultAccessLevel = Entity.mainPlayer.AccessLevel;
                        defaultPlayerName = Entity.mainPlayer.Name ?? "";
                        nameSpoofInput = defaultPlayerName;
                        defaultsCaptured = true;
                        LoggerInstance.Msg($"Captured player defaults: Name={defaultPlayerName}, UpgradeDays={defaultUpgradeDays}, AccessLevel={defaultAccessLevel}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"Error reading Entity.mainPlayer properties: {ex}");
            }

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            float curY = 70f;

            // Section 1: FakeDev
            GUI.Label(new Rect(20, curY, 260, 20), "<b>FakeDev</b>", labelStyle);
            curY += 22f;

            string fakeDevBtnText = showFakeDevWindow ? "Hide FakeDev" : "FakeDev Settings";
            if (playerExists)
            {
                if (GUI.Button(new Rect(20, curY, 260, 35), fakeDevBtnText, closeButtonStyle))
                {
                    showFakeDevWindow = !showFakeDevWindow;
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(20, curY, 260, 35), "FakeDev (No Player)", closeButtonStyle);
                GUI.enabled = true;
            }
            curY += 35f;

            if (separatorTexture != null)
            {
                curY += 6f;
                GUI.DrawTexture(new Rect(20, curY, 260, 2), separatorTexture);
                curY += 2f + 6f;
            }
            else
            {
                curY += 10f;
            }

            // Section 2: Loaders
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Loaders</b>", labelStyle);
            curY += 22f;

            string shopLoaderBtnText = showShopLoaderWindow ? "Hide Shop" : "Shop Loader";
            if (GUI.Button(new Rect(20, curY, 125, 35), shopLoaderBtnText, closeButtonStyle))
            {
                showShopLoaderWindow = !showShopLoaderWindow;
            }

            string questLoaderBtnText = showQuestLoaderWindow ? "Hide Quest" : "Quest Loader";
            if (GUI.Button(new Rect(155, curY, 125, 35), questLoaderBtnText, closeButtonStyle))
            {
                showQuestLoaderWindow = !showQuestLoaderWindow;
            }
            curY += 35f;

            if (separatorTexture != null)
            {
                curY += 6f;
                GUI.DrawTexture(new Rect(20, curY, 260, 2), separatorTexture);
                curY += 2f + 6f;
            }
            else
            {
                curY += 10f;
            }

            // Section 3: Autoskills
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Autoskills</b>", labelStyle);
            curY += 22f;

            string autoSkillsText = autoskillsActive ? "Autoskills: ON" : "Autoskills: OFF";
            if (playerExists)
            {
                if (GUI.Button(new Rect(20, curY, 125, 35), autoSkillsText, closeButtonStyle))
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
                GUI.Button(new Rect(20, curY, 125, 35), "Autoskills: OFF", closeButtonStyle);
                GUI.enabled = true;
                autoskillsActive = false;
            }

            if (GUI.Button(new Rect(155, curY, 125, 35), "Config", closeButtonStyle))
            {
                showConfigWindow = !showConfigWindow;
            }
            curY += 35f;

            if (separatorTexture != null)
            {
                curY += 6f;
                GUI.DrawTexture(new Rect(20, curY, 260, 2), separatorTexture);
                curY += 2f + 6f;
            }
            else
            {
                curY += 10f;
            }

            // Section 4: Packets
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Packets</b>", labelStyle);
            curY += 22f;

            string interceptorBtnText = showInterceptorWindow ? "Hide Intercept" : "Interceptor";
            if (GUI.Button(new Rect(20, curY, 125, 35), interceptorBtnText, closeButtonStyle))
            {
                showInterceptorWindow = !showInterceptorWindow;
            }

            string snifferBtnText = showSnifferWindow ? "Hide Sniffer" : "Sniffer";
            if (GUI.Button(new Rect(155, curY, 125, 35), snifferBtnText, closeButtonStyle))
            {
                showSnifferWindow = !showSnifferWindow;
            }
            curY += 35f + 5f;

            string senderBtnText = showSenderWindow ? "Hide Sender" : "Sender";
            if (GUI.Button(new Rect(20, curY, 125, 35), senderBtnText, closeButtonStyle))
            {
                showSenderWindow = !showSenderWindow;
            }

            string receiverBtnText = showReceiverWindow ? "Hide Receiver" : "Receiver";
            if (GUI.Button(new Rect(155, curY, 125, 35), receiverBtnText, closeButtonStyle))
            {
                showReceiverWindow = !showReceiverWindow;
            }
            curY += 35f;

            if (separatorTexture != null)
            {
                curY += 6f;
                GUI.DrawTexture(new Rect(20, curY, 260, 2), separatorTexture);
                curY += 2f + 6f;
            }
            else
            {
                curY += 10f;
            }

            // Section 5: Automation
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Automation</b>", labelStyle);
            curY += 22f;

            string runnerBtnText = showQuestRunnerWindow ? "Hide Quest Runner" : "Quest Runner";
            if (GUI.Button(new Rect(20, curY, 260, 35), runnerBtnText, closeButtonStyle))
            {
                showQuestRunnerWindow = !showQuestRunnerWindow;
            }
            curY += 35f;

            if (separatorTexture != null)
            {
                curY += 6f;
                GUI.DrawTexture(new Rect(20, curY, 260, 2), separatorTexture);
                curY += 2f + 6f;
            }
            else
            {
                curY += 10f;
            }

            // Section 6: Spoofers — name, gear, future cosmetic-only tweaks.
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Spoofers</b>", labelStyle);
            curY += 22f;

            string funBtnText = showFunWindow ? "Hide Fun" : "Fun";
            if (GUI.Button(new Rect(20, curY, 260, 35), funBtnText, closeButtonStyle))
            {
                showFunWindow = !showFunWindow;
            }
            curY += 35f + 10f;

            if (closeButtonStyle != null)
            {
                if (GUI.Button(new Rect(20, curY, 260, 35), "Close", closeButtonStyle))
                {
                    showWindow = false;
                }
            }
            else
            {
                if (GUI.Button(new Rect(20, curY, 260, 35), "Close"))
                {
                    showWindow = false;
                }
            }
            curY += 35f;

            windowRect.height = curY + 20f;

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
                int currentY = startY + i * 42;

                GUI.Label(new Rect(20, currentY, 90, 35), GetSkillKeyName(slot), labelStyle);

                string delayStr = delayInputs[slot];
                string newDelayStr = GUI.TextField(new Rect(115, currentY, 65, 35), delayStr, textFieldStyle);
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
                    if (GUI.Button(new Rect(190, currentY, 32, 35), "▲", closeButtonStyle))
                    {
                        (skillOrder[i - 1], skillOrder[i]) = (skillOrder[i], skillOrder[i - 1]);
                    }
                }

                if (i < skillOrder.Count - 1)
                {
                    if (GUI.Button(new Rect(228, currentY, 32, 35), "▼", closeButtonStyle))
                    {
                        (skillOrder[i + 1], skillOrder[i]) = (skillOrder[i], skillOrder[i + 1]);
                    }
                }

                if (slot >= 0 && slot < skillEnabled.Length)
                {
                    skillEnabled[slot] = GUI.Toggle(new Rect(272, currentY + 8, 20, 20), skillEnabled[slot], "");
                }
            }

            if (GUI.Button(new Rect(20, 305, 280, 35), "Close Config", closeButtonStyle))
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

            float Y = 65f;

            GUI.Label(new Rect(20, Y + 5, 40, 25), "Cmd:", labelStyle);
            senderCmdInput = GUI.TextField(new Rect(60, Y, 70, 35), senderCmdInput, textFieldStyle);

            string paramsLabel = senderSingleString ? "Params (whole string):" : "Params (comma-sep):";
            GUI.Label(new Rect(140, Y + 5, 130, 25), paramsLabel, labelStyle);
            senderParamsInput = GUI.TextField(new Rect(270, Y, 160, 35), senderParamsInput, textFieldStyle);

            // Single-string toggle — for chat-style commands where the payload
            // contains literal commas (e.g. `message`: "hi, friend"), splitting
            // on comma would mangle them.
            senderSingleString = GUI.Toggle(new Rect(pad, 110, 20, 20), senderSingleString, "");
            GUI.Label(new Rect(pad + 25, 110, 220, 20), "Single string (no comma split)", labelStyle);

            if (GUI.Button(new Rect(440, Y, 40, 35), "Send", closeButtonStyle))
            {
                string cmd = senderCmdInput.Trim();
                string paramsRaw = senderParamsInput;

                System.Collections.Generic.List<string> paramsList = new();
                if (!string.IsNullOrEmpty(paramsRaw))
                {
                    if (senderSingleString)
                    {
                        paramsList.Add(paramsRaw);
                    }
                    else
                    {
                        string[] parts = paramsRaw.Split(',');
                        foreach (string part in parts)
                        {
                            paramsList.Add(part.Trim());
                        }
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

            if (GUI.Button(new Rect(pad, 145, innerW, 35), "Close Sender", closeButtonStyle))
            {
                showSenderWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, senderWindowRect.width, 30));
        }

        private void DrawReceiverWindow(int windowID)
        {
            float winWidth = receiverWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            GUI.Label(new Rect(pad, 35, innerW, 20), "Server Packet Injector (Fake Server -> Client)", labelStyle);

            GUI.Label(new Rect(pad, 55, innerW, 20), "Enter raw server JSON payload:", labelStyle);

            // Preset loaders
            float presetBtnW = (innerW - 10) / 3f;
            if (GUI.Button(new Rect(pad, 80, presetBtnW, 35), "Preset: rNotify", closeButtonStyle))
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                receiverJsonInput = "{\"Cmd\":\"rNotify\",\"msg\":\"Hello from the void\"}";
            }

            if (GUI.Button(new Rect(pad + presetBtnW + 5, 80, presetBtnW, 35), "Preset: Server Chat", closeButtonStyle))
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                receiverJsonInput = "{\"Cmd\":\"chatm\",\"msg\":\"Hello from the server!\",\"Name\":\"SERVER\",\"channel\":\"server\"}";
            }

            if (GUI.Button(new Rect(pad + (presetBtnW + 5) * 2, 80, presetBtnW, 35), "Preset: Zone Chat", closeButtonStyle))
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                string name = "Loader";
                try { if (Entity.mainPlayer != null) name = Entity.mainPlayer.Name; } catch { }
                receiverJsonInput = "{\"Cmd\":\"chatm\",\"msg\":\"Hello, zone!\",\"Name\":\"" + name + "\",\"channel\":\"zone\"}";
            }

            float contentWidth = innerW - 4;
            float contentHeight = 150f;

            receiverScrollPosition = GUI.BeginScrollView(
                new Rect(pad, 125, innerW, 120),
                receiverScrollPosition,
                new Rect(0, 0, contentWidth, contentHeight)
            );

            receiverJsonInput = GUI.TextArea(
                new Rect(0, 0, contentWidth, contentHeight),
                receiverJsonInput,
                previewTextStyle ?? GUI.skin.textArea
            );

            GUI.EndScrollView();

            float btnW = (innerW - 10) / 3f;

            if (GUI.Button(new Rect(pad, 255, btnW, 35), "Inject", closeButtonStyle))
            {
                string json = receiverJsonInput.Trim();
                if (string.IsNullOrEmpty(json))
                {
                    LoggerInstance.Error("[Packet Receiver] Cannot inject empty JSON.");
                }
                else
                {
                    FakeServerPacket(json);
                }
            }

            if (GUI.Button(new Rect(pad + btnW + 5, 255, btnW, 35), "Clear", closeButtonStyle))
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                receiverJsonInput = "{\n  \"Cmd\": \"\",\n  \"Params\": {}\n}";
            }

            if (GUI.Button(new Rect(pad + (btnW + 5) * 2, 255, btnW, 35), "Close", closeButtonStyle))
            {
                showReceiverWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
        }

        public static (bool ok, string info) FakeServerPacket(string json)
        {
            if (string.IsNullOrEmpty(json)) return (false, "empty JSON");
            try
            {
                if (AEC.Instance != null)
                {
                    if (_wrapAndQueueResponseMethod == null)
                    {
                        _wrapAndQueueResponseMethod = typeof(AEC).GetMethod("WrapAndQueueResponse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    }
                    if (_wrapAndQueueResponseMethod != null)
                    {
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                        _wrapAndQueueResponseMethod.Invoke(AEC.Instance, new object[] { data });
                        MelonLogger.Msg("[Packet Receiver] Successfully injected fake server packet.");
                        Infinity_TestMod.Util.PacketLog.Write("s2c", json, synthetic: true);
                        return (true, "AEC Queue");
                    }
                    else
                    {
                        MelonLogger.Error("[Packet Receiver] Could not find WrapAndQueueResponse method via reflection.");
                        return (false, "WrapAndQueueResponse not found");
                    }
                }
                else
                {
                    MelonLogger.Error("[Packet Receiver] AEC.Instance is null, cannot inject packet.");
                    return (false, "AEC.Instance is null");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[Packet Receiver] Error injecting fake packet: {ex.Message}");
                return (false, ex.Message);
            }
        }

        private void DrawFakeDevWindow(int windowID)
        {
            float winWidth = fakeDevWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            int currentLevel = -1;
            try { if (playerExists) currentLevel = Entity.mainPlayer.AccessLevel; } catch { }

            bool isMember = false;
            try { if (playerExists) isMember = Entity.mainPlayer.UpgradeDays > 0; } catch { }

            // 1. Membership section
            GUI.Label(new Rect(pad, 35, innerW, 20), "Membership:", labelStyle);
            string memLabel = isMember ? "▶ Member (Active)" : "Non-Member";
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, 55, innerW, 35), memLabel, closeButtonStyle))
                {
                    try
                    {
                        Entity.mainPlayer.UpgradeDays = isMember ? 0 : 30;
                        Entity.mainPlayer.updateNameColor();
                        LoggerInstance.Msg($"Set client UpgradeDays to {Entity.mainPlayer.UpgradeDays} (member={!isMember}).");
                    }
                    catch (System.Exception ex)
                    {
                        LoggerInstance.Error($"Error toggling membership: {ex}");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, 55, innerW, 35), memLabel, closeButtonStyle);
                GUI.enabled = true;
            }

            // 2. Access Levels section
            GUI.Label(new Rect(pad, 100, innerW, 20), "Access Levels (hasAccess checks):", labelStyle);
            float btnW = (innerW - 16) / 5f;
            DrawFakeDevAccessTier(pad,             btnW, "30",  30,  currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + btnW + 4,  btnW, "40",  40,  currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + (btnW + 4)*2, btnW, "50",  50,  currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + (btnW + 4)*3, btnW, "60",  60,  currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + (btnW + 4)*4, btnW, "100", 100, currentLevel, playerExists);

            // 3. Actions: Dev UI, Reset, Close. Name Spoof moved to the Fun
            // window; Reset still clears any active name spoof for symmetry.
            float actionBtnW = (innerW - 10) / 2f;
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, 180, actionBtnW, 35), "Open Dev UI", closeButtonStyle))
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

                if (GUI.Button(new Rect(pad + actionBtnW + 10, 180, actionBtnW, 35), "Reset to Default", closeButtonStyle))
                {
                    try
                    {
                        if (defaultsCaptured)
                        {
                            Entity.mainPlayer.UpgradeDays = defaultUpgradeDays;
                            Entity.mainPlayer.AccessLevel = defaultAccessLevel;
                            Entity.mainPlayer.updateNameColor();
                            ClearNameSpoof();
                            LoggerInstance.Msg($"Reset player defaults: Name={defaultPlayerName}, UpgradeDays={defaultUpgradeDays}, AccessLevel={defaultAccessLevel}");
                        }
                        else
                        {
                            LoggerInstance.Error("Cannot reset: Default player privileges were not captured.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        LoggerInstance.Error($"Error resetting privileges: {ex}");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, 180, actionBtnW, 35), "Open Dev UI", closeButtonStyle);
                GUI.Button(new Rect(pad + actionBtnW + 10, 180, actionBtnW, 35), "Reset to Default", closeButtonStyle);
                GUI.enabled = true;
            }

            if (GUI.Button(new Rect(pad, 225, innerW, 35), "Close", closeButtonStyle))
            {
                showFakeDevWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
        }

        private void DrawFunWindow(int windowID)
        {
            float winWidth = funWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            float curY = 35f;

            // 1. Name Spoof — local-only nameplate/HUD/chat substitution.
            GUI.Label(new Rect(pad, curY, innerW, 20), "Name Spoof:", labelStyle);
            curY += 20f;
            nameSpoofInput = GUI.TextField(new Rect(pad, curY, innerW, 30), nameSpoofInput, textFieldStyle);
            curY += 35f;

            float btnW = (innerW - 10) / 2f;
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, curY, btnW, 30), nameSpoofActive ? "Update Name" : "Apply Name", closeButtonStyle))
                    ApplyNameSpoof(nameSpoofInput);
                if (GUI.Button(new Rect(pad + btnW + 10, curY, btnW, 30), "Clear Name", closeButtonStyle))
                    ClearNameSpoof();
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, curY, btnW, 30), "Apply Name", closeButtonStyle);
                GUI.Button(new Rect(pad + btnW + 10, curY, btnW, 30), "Clear Name", closeButtonStyle);
                GUI.enabled = true;
            }
            curY += 40f;

            // 2. Gender flip — single toggle. Real gender stays for game logic
            // (pronouns, server-side checks); only the avatar rig flips.
            string realGender = "?";
            try { if (Entity.mainPlayer != null) realGender = Entity.mainPlayer.GetGenderString(); } catch { }
            string genderLabel = genderSpoofActive
                ? $"Flip Gender: ON (showing {(realGender == "M" ? "F" : (realGender == "F" ? "M" : "?"))})"
                : $"Flip Gender: OFF (real: {realGender})";
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, curY, innerW, 30), genderLabel, closeButtonStyle))
                    ToggleGenderSpoof();
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, curY, innerW, 30), genderLabel, closeButtonStyle);
                GUI.enabled = true;
            }
            curY += 40f;

            // 3-5. Gear spoof slots. Each row: label, input, three buttons
            // (Apply / Clear / Browse). Browse is a dropdown toggle — only
            // one slot's catalog is visible at a time. The picker panel is
            // drawn after all three slot rows so it can size against the
            // window's remaining vertical space without overlapping them.
            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Helm", 1,
                ref helmSpoofInput, helmSpoofActive,
                ApplyHelmSpoof, ClearHelmSpoof);

            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Armor", 2,
                ref armorSpoofInput, armorSpoofActive,
                ApplyArmorSpoof, ClearArmorSpoof);

            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Cape", 3,
                ref backSpoofInput, backSpoofActive,
                ApplyBackSpoof, ClearBackSpoof);

            // 5. Shared catalog panel — only drawn when a slot's Browse is open.
            if (catalogOpenSlot != 0)
            {
                System.Collections.Generic.Dictionary<string, ItemCatalog.ItemEntry> bucket;
                System.Action<string> onSelect;
                string slotLabel;
                switch (catalogOpenSlot)
                {
                    case 1: bucket = ItemCatalog.Helms;  onSelect = s => helmSpoofInput  = s; slotLabel = "Helm";  break;
                    case 2: bucket = ItemCatalog.Armors; onSelect = s => armorSpoofInput = s; slotLabel = "Armor"; break;
                    case 3: bucket = ItemCatalog.Backs;  onSelect = s => backSpoofInput  = s; slotLabel = "Cape";  break;
                    default: bucket = null; onSelect = null; slotLabel = ""; break;
                }
                if (bucket != null)
                {
                    GUI.Label(new Rect(pad, curY, innerW, 20),
                        $"{slotLabel} Catalog ({bucket.Count}) — filter:", labelStyle);
                    curY += 22f;
                    catalogFilter = GUI.TextField(new Rect(pad, curY, innerW, 28), catalogFilter, textFieldStyle);
                    curY += 32f;

                    string filt = catalogFilter?.ToLowerInvariant() ?? "";
                    var matches = new System.Collections.Generic.List<ItemCatalog.ItemEntry>();
                    foreach (var e in bucket.Values)
                    {
                        string display = !string.IsNullOrEmpty(e.name) ? e.name : ItemCatalog.ParseFriendlyName(e.bundle);
                        if (filt.Length == 0
                            || (display?.ToLowerInvariant().Contains(filt) ?? false)
                            || (e.bundle?.ToLowerInvariant().Contains(filt) ?? false))
                            matches.Add(e);
                    }
                    matches.Sort((a, b) =>
                    {
                        string an = !string.IsNullOrEmpty(a.name) ? a.name : ItemCatalog.ParseFriendlyName(a.bundle);
                        string bn = !string.IsNullOrEmpty(b.name) ? b.name : ItemCatalog.ParseFriendlyName(b.bundle);
                        return string.Compare(an, bn, System.StringComparison.OrdinalIgnoreCase);
                    });

                    float listH = 180f;
                    GUI.Box(new Rect(pad, curY, innerW, listH), "", GUI.skin.box);
                    float rowH = 22f;
                    float contentH = System.Math.Max(listH - 8, matches.Count * rowH + 4);
                    catalogScroll = GUI.BeginScrollView(
                        new Rect(pad, curY, innerW, listH),
                        catalogScroll,
                        new Rect(0, 0, innerW - 20, contentH));
                    for (int i = 0; i < matches.Count; i++)
                    {
                        var e = matches[i];
                        string display = !string.IsNullOrEmpty(e.name)
                            ? e.name
                            : ItemCatalog.ParseFriendlyName(e.bundle);
                        if (GUI.Button(new Rect(2, 2 + i * rowH, innerW - 28, rowH - 2), "  " + display, rowButtonStyle))
                        {
                            onSelect?.Invoke(e.bundle);
                            GUI.FocusControl(null);
                            GUIUtility.keyboardControl = 0;
                        }
                    }
                    GUI.EndScrollView();
                    curY += listH + 10f;
                }
            }

            if (GUI.Button(new Rect(pad, curY, innerW, 32), "Close", closeButtonStyle))
                showFunWindow = false;
            curY += 40f;

            // Auto-size window to fit current content (collapsed vs catalog-open).
            funWindowRect.height = curY + 10f;

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
        }

        /// <summary>
        /// Draws one gear-spoof row: label + input + Apply/Clear/Browse buttons.
        /// Returns the new curY below the row. Browse toggles the shared
        /// catalog dropdown for this slot.
        /// </summary>
        private static float DrawGearSpoofSlot(float curY, float pad, float innerW, bool playerExists,
                                              string slotName, int slotKey,
                                              ref string input, bool active,
                                              System.Action<string> apply,
                                              System.Action clear)
        {
            GUI.Label(new Rect(pad, curY, innerW, 20), $"{slotName} Spoof:", labelStyle);
            curY += 20f;
            input = GUI.TextField(new Rect(pad, curY, innerW, 30), input, textFieldStyle);
            curY += 35f;

            // Three buttons in a row: Apply / Clear / Browse-toggle.
            float btnW = (innerW - 20) / 3f;
            string applyText = active ? $"Update {slotName}" : $"Apply {slotName}";
            string browseText = (catalogOpenSlot == slotKey) ? "Hide ▲" : "Browse ▼";

            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, curY, btnW, 30), applyText, closeButtonStyle))
                    apply?.Invoke(input);
                if (GUI.Button(new Rect(pad + btnW + 10, curY, btnW, 30), $"Clear {slotName}", closeButtonStyle))
                    clear?.Invoke();
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, curY, btnW, 30), applyText, closeButtonStyle);
                GUI.Button(new Rect(pad + btnW + 10, curY, btnW, 30), $"Clear {slotName}", closeButtonStyle);
                GUI.enabled = true;
            }
            if (GUI.Button(new Rect(pad + (btnW + 10) * 2, curY, btnW, 30), browseText, closeButtonStyle))
            {
                catalogOpenSlot = (catalogOpenSlot == slotKey) ? 0 : slotKey;
                catalogScroll = Vector2.zero;
            }
            curY += 40f;
            return curY;
        }

        private static void ToggleGenderSpoof()
        {
            if (Entity.mainPlayer == null) return;
            if (!genderSpoofActive)
            {
                // Activate: stash original, flip the enum field. Every
                // consumer (GetGenderString, pronouns, EquipOptions, etc.)
                // reads from this field, so they all see the flipped value.
                genderSpoofOriginal = Entity.mainPlayer.Gender;
                Entity.mainPlayer.Gender = (genderSpoofOriginal == Player.genders.Male)
                    ? Player.genders.Female
                    : Player.genders.Male;
                genderSpoofActive = true;
            }
            else
            {
                // Deactivate: restore the stashed value.
                Entity.mainPlayer.Gender = genderSpoofOriginal;
                genderSpoofActive = false;
            }
            try { Entity.mainPlayer.createAvatar(); } catch { }
            MelonLogger.Msg($"[GenderSpoof] {(genderSpoofActive ? $"ON (now {Entity.mainPlayer.GetGenderString()})" : "OFF")}");
        }

        private static void ApplyArmorSpoof(string desiredBundle)
            => ApplyGearSpoof("Armor", desiredBundle, v => armorSpoofBundle = v, v => armorSpoofActive = v, v => armorSpoofInput = v);
        private static void ClearArmorSpoof()
            => ClearGearSpoof("Armor", v => armorSpoofBundle = v, v => armorSpoofActive = v);

        private static void ApplyHelmSpoof(string desiredBundle)
            => ApplyGearSpoof("Helm", desiredBundle, v => helmSpoofBundle = v, v => helmSpoofActive = v, v => helmSpoofInput = v);
        private static void ClearHelmSpoof()
            => ClearGearSpoof("Helm", v => helmSpoofBundle = v, v => helmSpoofActive = v);

        private static void ApplyBackSpoof(string desiredBundle)
            => ApplyGearSpoof("Cape", desiredBundle, v => backSpoofBundle = v, v => backSpoofActive = v, v => backSpoofInput = v);
        private static void ClearBackSpoof()
            => ClearGearSpoof("Cape", v => backSpoofBundle = v, v => backSpoofActive = v);

        private static void ApplyGearSpoof(string label, string desiredBundle,
                                           System.Action<string> setBundle,
                                           System.Action<bool> setActive,
                                           System.Action<string> setInput)
        {
            if (Entity.mainPlayer == null) return;
            desiredBundle = (desiredBundle ?? "").Trim();
            if (desiredBundle.Length == 0)
            {
                ClearGearSpoof(label, setBundle, setActive);
                return;
            }
            setActive(true);
            setBundle(desiredBundle);
            setInput(desiredBundle);
            // Force the avatar to rebuild so the loaders rerun and the
            // matching spoof postfix kicks in.
            try { Entity.mainPlayer.createAvatar(); } catch { }
            MelonLogger.Msg($"[{label}Spoof] applied bundle '{desiredBundle}'.");
        }

        private static void ClearGearSpoof(string label,
                                           System.Action<string> setBundle,
                                           System.Action<bool> setActive)
        {
            setActive(false);
            setBundle("");
            try { Entity.mainPlayer?.createAvatar(); } catch { }
            MelonLogger.Msg($"[{label}Spoof] cleared.");
        }

        private void DrawFakeDevAccessTier(float x, float width, string label, int level, int currentLevel, bool playerExists)
        {
            bool active = (currentLevel == level);
            string text = active ? "▶ " + label : label;
            if (!playerExists)
            {
                GUI.enabled = false;
                GUI.Button(new Rect(x, 125, width, 35), text, closeButtonStyle);
                GUI.enabled = true;
                return;
            }
            if (GUI.Button(new Rect(x, 125, width, 35), text, closeButtonStyle))
            {
                try
                {
                    Entity.mainPlayer.AccessLevel = level;
                    Entity.mainPlayer.updateNameColor();
                    LoggerInstance.Msg($"Set client AccessLevel to {level}.");
                }
                catch (System.Exception ex)
                {
                    LoggerInstance.Error($"Error setting access level: {ex}");
                }
            }
        }

        private static void ApplyNameSpoof(string desiredName)
        {
            if (Entity.mainPlayer == null) return;
            desiredName = (desiredName ?? "").Trim();
            if (desiredName.Length == 0)
            {
                ClearNameSpoof();
                return;
            }
            if (desiredName.Length > 24) desiredName = desiredName.Substring(0, 24);

            nameSpoofActive = true;
            spoofedName = desiredName;
            nameSpoofInput = desiredName;
            // Trigger a redraw: RefreshNameplate calls ComposeNameplateText
            // which our Postfix patches to return the spoofed string.
            // NOTE: do NOT mutate the nameplate GameObject's `name` field —
            // NameLabelManager.KillNonMainPlayerNames identifies "our"
            // nameplate by comparing GameObject.name against
            // Entity.mainPlayer.Name on every ResponseAreaJoin. If they
            // diverge it destroys our nameplate on the next map change.
            try { Entity.mainPlayer.RefreshNameplate(); } catch { }
            MelonLogger.Msg($"Set local nameplate spoof to '{desiredName}' for real character '{Entity.mainPlayer.Name}'.");
        }

        private static void ClearNameSpoof()
        {
            nameSpoofActive = false;
            spoofedName = "";
            if (!string.IsNullOrEmpty(defaultPlayerName)) nameSpoofInput = defaultPlayerName;
            try { Entity.mainPlayer?.RefreshNameplate(); } catch { }
            MelonLogger.Msg("Cleared local nameplate spoof.");
        }

        private void DrawShopLoaderWindow(int windowID)
        {
            float winWidth = shopLoaderWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            GUI.Label(new Rect(pad, 35, innerW, 20), "Shop ID:", labelStyle);
            shopIdInput = GUI.TextField(new Rect(pad, 60, innerW, 35), shopIdInput, textFieldStyle);

            float btnW = (innerW - 10) / 2f;
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, 105, btnW, 35), "Load Shop", closeButtonStyle))
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

                if (GUI.Button(new Rect(pad + btnW + 10, 105, btnW, 35), "Load Merge", closeButtonStyle))
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
                GUI.Button(new Rect(pad, 105, btnW, 35), "Load Shop", closeButtonStyle);
                GUI.Button(new Rect(pad + btnW + 10, 105, btnW, 35), "Load Merge", closeButtonStyle);
                GUI.enabled = true;
            }

            if (GUI.Button(new Rect(pad, 150, innerW, 35), "Close", closeButtonStyle))
            {
                showShopLoaderWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
        }

        private void DrawQuestLoaderWindow(int windowID)
        {
            float winWidth = questLoaderWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            GUI.Label(new Rect(pad, 35, innerW, 20), "Quest ID:", labelStyle);
            questIdInput = GUI.TextField(new Rect(pad, 60, innerW, 35), questIdInput, textFieldStyle);

            float btnW = (innerW - 10) / 2f;
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, 105, btnW, 35), "Load Quest", closeButtonStyle))
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

                if (GUI.Button(new Rect(pad + btnW + 10, 105, btnW, 35), "Abandon", closeButtonStyle))
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
                GUI.Button(new Rect(pad, 105, btnW, 35), "Load Quest", closeButtonStyle);
                GUI.Button(new Rect(pad + btnW + 10, 105, btnW, 35), "Abandon", closeButtonStyle);
                GUI.enabled = true;
            }

            if (GUI.Button(new Rect(pad, 150, innerW, 35), "Close", closeButtonStyle))
            {
                showQuestLoaderWindow = false;
            }

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
        }

        private void DrawQuestRunnerWindow(int windowID)
        {
            float winWidth = questRunnerWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            // Row 1: inputs
            GUI.Label(new Rect(pad, 35 + 5, 70, 25), "Quest ID:", labelStyle);
            questRunnerIdInput = GUI.TextField(new Rect(pad + 70, 35, 60, 35), questRunnerIdInput, textFieldStyle);
            // Browse button — opens the picker inline.
            string browseLabel = showQuestPicker ? "▼" : "▶";
            if (GUI.Button(new Rect(pad + 132, 35, 24, 35), browseLabel, closeButtonStyle))
            {
                showQuestPicker = !showQuestPicker;
            }
            GUI.Label(new Rect(pad + 160, 35 + 5, 70, 25), "Iters:", labelStyle);
            questRunnerItersInput = GUI.TextField(new Rect(pad + 210, 35, 60, 35), questRunnerItersInput, textFieldStyle);
            // Resolved-name preview to the right of Stop, replacing the
            // previous y=58 line that was colliding with the Frame row.
            string resolvedName = "?";
            if (int.TryParse(questRunnerIdInput, out int previewQid)
                && Directory.Quests.TryGetValue(previewQid, out var qe))
                resolvedName = qe.name ?? "?";
            GUI.Label(new Rect(pad + 460, 35 + 5, 200, 25),
                $"  ↳ {resolvedName}", logTextStyle);

            bool isRunning = questRunner.IsRunning;
            GUI.enabled = !isRunning;
            if (GUI.Button(new Rect(pad + 280, 35, 80, 35), "Start", closeButtonStyle))
            {
                if (int.TryParse(questRunnerIdInput, out int qid) && int.TryParse(questRunnerItersInput, out int iters))
                {
                    questRunnerLog.Clear();
                    questRunner.OnLog = line =>
                    {
                        lock (questRunnerLog)
                        {
                            questRunnerLog.Add($"{System.DateTime.Now:HH:mm:ss}  {line}");
                            if (questRunnerLog.Count > 200) questRunnerLog.RemoveAt(0);
                        }
                    };
                    questRunner.Start(qid, iters,
                                                  questRunnerAreaInput?.Trim() ?? "",
                                                  questRunnerFrameInput?.Trim() ?? "",
                                                  string.IsNullOrWhiteSpace(questRunnerPadInput) ? "Spawn" : questRunnerPadInput.Trim());
                }
                else
                {
                    LoggerInstance.Error("[QuestRunner] qid and iters must be integers");
                }
            }
            // Second input row: optional auto-travel. Leave Area empty to
            // stay in the current zone (no tfer); leave Frame empty to stay
            // in the current cell (no moveToCell). Live "here: area/frame"
            // shows current location so the user can copy for next entries.
            GUI.Label(new Rect(pad, 80 + 5, 50, 25), "Area:", labelStyle);
            questRunnerAreaInput = GUI.TextField(new Rect(pad + 45, 80, 75, 35), questRunnerAreaInput, textFieldStyle);
            GUI.Label(new Rect(pad + 128, 80 + 5, 50, 25), "Frame:", labelStyle);
            questRunnerFrameInput = GUI.TextField(new Rect(pad + 175, 80, 75, 35), questRunnerFrameInput, textFieldStyle);
            GUI.Label(new Rect(pad + 258, 80 + 5, 40, 25), "Pad:", labelStyle);
            questRunnerPadInput = GUI.TextField(new Rect(pad + 295, 80, 65, 35), questRunnerPadInput, textFieldStyle);
            string hereArea = "?", hereFrame = "?";
            try { hereArea = Area.currentArea?.Name ?? "?"; hereFrame = Entity.mainPlayer?.Frame ?? "?"; } catch { }
            GUI.Label(new Rect(pad + 370, 80 + 5, 220, 25), $"  here: {hereArea}/{hereFrame}", logTextStyle);
            GUI.enabled = true;

            GUI.enabled = isRunning;
            if (GUI.Button(new Rect(pad + 370, 35, 80, 35), "Stop", closeButtonStyle))
            {
                questRunner.Stop();
            }
            GUI.enabled = true;

            // Row 3: status
            string stateStr = $"<b>State:</b> {questRunner.State}    " +
                              $"<b>Iter:</b> {questRunner.CurrentIteration}/{questRunner.Iterations}";
            GUI.Label(new Rect(pad, 125, innerW, 20), stateStr, labelStyle);
            GUI.Label(new Rect(pad, 147, innerW, 20), $"<b>Status:</b> {questRunner.StatusLine}", labelStyle);

            // Row 4: per-objective progress (read live from in-process state).
            // When the runner is mid-flight (especially chain mode) show its
            // actual current quest, not the stale input field.
            float yObj = 175;
            try
            {
                int qid = questRunner.IsRunning && questRunner.QuestID > 0
                    ? questRunner.QuestID
                    : (int.TryParse(questRunnerIdInput, out int parsedQid) ? parsedQid : 0);
                if (qid > 0)
                {
                    Quest q = Quest.Get(qid);
                    if (q != null && q.Turnins != null)
                    {
                        var pq = Entity.mainPlayer?.Quests;
                        for (int i = 0; i < q.Turnins.Length && i < 6; i++)
                        {
                            var t = q.Turnins[i];
                            int have = pq?.getQuestObjective(t.QOID)?.Quantity ?? 0;
                            bool done = pq?.IsObjectiveComplete(t.QOID) ?? false;
                            string mark = done ? "<color=green>✓</color>" : " ";
                            GUI.Label(new Rect(pad, yObj + i * 18, innerW, 18),
                                $"  {mark} {t.QOType,-10} {t.Name}  [{have}/{t.Quantity}]  ref={t.RefIDs}",
                                logTextStyle);
                        }
                    }
                    else
                    {
                        GUI.Label(new Rect(pad, yObj, innerW, 18),
                             "  (no quest def cached — open the quest UI once)", logTextStyle);
                    }
                }
            }
            catch { /* layout-time read errors aren't worth surfacing */ }

            // Row 5: event log
            float logY = 295;
            GUI.Box(new Rect(pad, logY, innerW, 75), "", GUI.skin.box);
            float logH;
            lock (questRunnerLog) { logH = System.Math.Max(65f, questRunnerLog.Count * 16f); }
            questRunnerLogScroll = GUI.BeginScrollView(
                new Rect(pad, logY, innerW, 75),
                questRunnerLogScroll,
                new Rect(0, 0, innerW - 20, logH));
            lock (questRunnerLog)
            {
                for (int i = 0; i < questRunnerLog.Count; i++)
                {
                    GUI.Label(new Rect(5, i * 16, innerW - 30, 16), questRunnerLog[i], logTextStyle);
                }
            }
            GUI.EndScrollView();

            // Chain selector row — one click per chain plus a Run button.
            // The dropdown is a cycle button (clicking advances through chain
            // names) since IMGUI doesn't have a native combobox. Keeps the
            // single-quest fields above usable for one-off testing.
            var chainNames = new System.Collections.Generic.List<string>(QuestChains.Names);
            string currentChainName = chainNames.Count == 0
                ? "(none — edit chains.json)"
                : chainNames[questChainPickerIndex % chainNames.Count];
            int currentEntryCount = chainNames.Count == 0 ? 0 : (QuestChains.Get(currentChainName)?.Count ?? 0);

            GUI.Label(new Rect(pad, 380 + 5, 60, 25), "Chain:", labelStyle);
            if (GUI.Button(new Rect(pad + 60, 380, 180, 35),
                $"{currentChainName}  ({currentEntryCount} entries)", closeButtonStyle))
            {
                if (chainNames.Count > 0)
                    questChainPickerIndex = (questChainPickerIndex + 1) % chainNames.Count;
            }

            // In-chain progress readout while running.
            string chainProgress = (questRunner.ChainEntries != null)
                ? $"  ▶ {questRunner.ChainName} {questRunner.ChainIndex + 1}/{questRunner.ChainEntries.Count}"
                : "";
            GUI.Label(new Rect(pad + 250, 380 + 5, 200, 25), chainProgress, logTextStyle);

            bool isRunningC = questRunner.IsRunning;
            GUI.enabled = !isRunningC && chainNames.Count > 0;
            if (GUI.Button(new Rect(pad + 460, 380, 120, 35), "Run Chain", closeButtonStyle))
            {
                questRunnerLog.Clear();
                questRunner.OnLog = line =>
                {
                    lock (questRunnerLog)
                    {
                        questRunnerLog.Add($"{System.DateTime.Now:HH:mm:ss}  {line}");
                        if (questRunnerLog.Count > 200) questRunnerLog.RemoveAt(0);
                    }
                };
                questRunner.StartChain(currentChainName, QuestChains.Get(currentChainName));
            }
            GUI.enabled = true;

            if (GUI.Button(new Rect(pad, 425, innerW, 35), "Close Runner", closeButtonStyle))
            {
                showQuestRunnerWindow = false;
            }

            // Picker overlay — covers the lower half of the window when open.
            // Drawn last so it sits on top of the objective table + event log.
            if (showQuestPicker)
            {
                // Covers the lower content (status, objectives, log) when open.
                // Positioned just below the two input rows.
                float pickerY = 125;
                float pickerH = 290;
                GUI.Box(new Rect(pad - 2, pickerY - 2, innerW + 4, pickerH + 4), "");
                GUI.Label(new Rect(pad, pickerY + 5, 70, 25), "Filter:", labelStyle);
                questPickerFilter = GUI.TextField(new Rect(pad + 60, pickerY, 260, 35), questPickerFilter, textFieldStyle);
                GUI.Label(new Rect(pad + 330, pickerY + 5, 200, 25),
                    $"({Directory.Quests.Count} known)", labelStyle);

                // Filtered list — only enumerate Directory entries that match
                // (case-insensitive substring on id/name/storyline). Sorted by id.
                string filt = questPickerFilter?.ToLowerInvariant() ?? "";
                var matches = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, Directory.QuestEntry>>();
                foreach (var kv in Directory.Quests)
                {
                    if (filt.Length == 0
                        || kv.Key.ToString().Contains(filt)
                        || (kv.Value.name?.ToLowerInvariant().Contains(filt) ?? false)
                        || (kv.Value.storyline?.ToLowerInvariant().Contains(filt) ?? false))
                    {
                        matches.Add(kv);
                    }
                }
                matches.Sort((a, b) => a.Key.CompareTo(b.Key));

                float rowH = 20f;
                float contentH = System.Math.Max(pickerH - 50, matches.Count * rowH + 4);
                questPickerScroll = GUI.BeginScrollView(
                    new Rect(pad, pickerY + 40, innerW, pickerH - 45),
                    questPickerScroll,
                    new Rect(0, 0, innerW - 20, contentH));
                for (int i = 0; i < matches.Count; i++)
                {
                    var kv = matches[i];
                    string row = $"  {kv.Key,5}  {kv.Value.name}"
                               + (string.IsNullOrEmpty(kv.Value.storyline) ? "" : $"   <i>({kv.Value.storyline})</i>");
                    if (GUI.Button(new Rect(0, i * rowH, innerW - 25, rowH), row, rowButtonStyle))
                    {
                        questRunnerIdInput = kv.Key.ToString();
                        showQuestPicker = false;
                    }
                }
                GUI.EndScrollView();
            }

            GUI.DragWindow(new Rect(0, 0, winWidth, 30));
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
            if (showWindow && showFakeDevWindow && fakeDevWindowRect.Contains(imguiMousePos))
            {
                return true;
            }
            if (showWindow && showShopLoaderWindow && shopLoaderWindowRect.Contains(imguiMousePos))
            {
                return true;
            }
            if (showWindow && showQuestLoaderWindow && questLoaderWindowRect.Contains(imguiMousePos))
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

            if (showWindow && showReceiverWindow && receiverWindowRect.Contains(imguiMousePos))
            {
                return true;
            }

            if (showWindow && showQuestRunnerWindow && questRunnerWindowRect.Contains(imguiMousePos))
            {
                return true;
            }

            if (showWindow && showFunWindow && funWindowRect.Contains(imguiMousePos))
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
