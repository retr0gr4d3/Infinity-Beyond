using Infinity_TestMod.Patches;
using Infinity_TestMod.Util;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Infinity_TestMod
{
    public class TestMod : MelonMod
    {
        public static bool showWindow = false;
        public static Rect windowRect = new(20, 100, 300, 610);
        public static readonly Rect ToggleButtonRect = new(10, 20, 64, 64);

        // Auto-skip cutscenes — set true to have CutsceneSkipPatch end
        // every cutscene the moment Dialogger_Manager.StartCutscene fires.
        // Honors the cutscene's completeActions (quest progress etc) since
        // we invoke the same EndPressed() the End button does.
        public static bool autoSkipCutscenes = false;

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

        // Extra Fun — sibling to Fun for niche/experimental spoofs. Owns
        // catalog slot 6 (Monster→Pet); shared catalog state with Fun.
        public static bool showExtraFunWindow = false;
        public static Rect extraFunWindowRect = new(700, 410, 360, 360);

        public static bool showRetroTestsWindow = false;
        public static Rect retroTestsWindowRect = new(330, 350, 320, 640);

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

        public static bool weaponSpoofActive = false;
        public static string weaponSpoofBundle = "";
        private static string weaponSpoofInput = "";

        public static bool petSpoofActive = false;
        public static string petSpoofBundle = "";
        private static string petSpoofInput = "";

        // Monster transform — uses the game's built-in ApplyMonTransform
        // (transform-potion path). Caveat: entering Combat auto-removes
        // the transform (Entity.currentState setter does this), so it's
        // an out-of-combat cosmetic only.
        public static bool monTransformActive = false;
        public static string monTransformBundle = "";
        private static string monTransformInput = "";

        // While the player is in combat, cycle random animation clips on the
        // spoofed pet's Animator. Driven by PetCombatAnimDriver from OnUpdate.
        public static bool petCombatAnimActive = false;

        // Jukebox: play any soundtrack by ID (typically 1..318). Dropdown is
        // populated passively by MusicHarvestPatch — every track the game
        // registers with BGMusicManager (area BGM, cutscene stings, our own
        // loads) lands in MusicCatalog and shows up here.
        private static string jukeboxInput = "";
        private static int jukeboxSelectedId = 0;
        private static bool jukeboxPickerOpen = false;
        private static string jukeboxFilter = "";
        private static UnityEngine.Vector2 jukeboxScroll = UnityEngine.Vector2.zero;

        // Opens SkillForge and fills CharacterClass static caches with
        // synthetic data so the UI populates without a real sfUpdate from
        // the server. The window's Start() subscribes its onNodesLoaded
        // handler, so we defer the Invoke a couple of frames to make sure
        // it's hooked before we fire.
        private static void OpenForgeStubbed()
        {
            try
            {
                if (UIWindowManager.instance == null)
                {
                    MelonLogger.Warning("[SkillForge] UIWindowManager.instance is null — log in first");
                    return;
                }
                UIWindowManager.instance.ShowForge();

                // ClassNodes shape (per ResponseSkillForge "init"):
                //   { "<Display Name>": { "ID": "<n>", "Skills": { "<slot>": <skillId>, ... } }, ... }
                // Empty Skills is fine — SelectClass just iterates and does nothing.
                JObject classes = new()
                {
                    ["Stub: Dragonslayer"] = new JObject { ["ID"] = "101", ["Skills"] = new JObject() },
                    ["Stub: Necromancer"] = new JObject { ["ID"] = "102", ["Skills"] = new JObject() },
                    ["Stub: Pyromancer"] = new JObject { ["ID"] = "103", ["Skills"] = new JObject() },
                };
                CharacterClass.ClassNodes = classes;
                CharacterClass.SkillNodes = new System.Collections.Generic.Dictionary<string, JObject>
                {
                    ["headers"] = new JObject(),
                    ["nodes"] = new JObject(),
                    ["helpers"] = new JObject(),
                    ["conditionals"] = new JObject(),
                    ["activators"] = new JObject(),
                };
                // PerformSave's Editing branch accesses SkillData[SelectedSkill].
                // When the user clicks Save on a stub class without ever
                // selecting a real skill, SelectedSkill is 0 — so we seed
                // a placeholder at id 0 to avoid KeyNotFoundException.
                // The request still goes out to the server (and gets dropped).
                Skill stubSkill = new(
                    id: 0,
                    action: Skill.ActionType.Regular,
                    name: "Stub Skill",
                    description: "placeholder for stubbed Forge UI",
                    icon: "",
                    slot: 0,
                    data: new JArray(),
                    forgedata: new JArray(),
                    autohRange: 0f,
                    autovRange: 0f,
                    mana: 0);
                CharacterClass.AllSkills = new System.Collections.Generic.Dictionary<int, Skill>
                {
                    [0] = stubSkill,
                };

                MelonCoroutines.Start(InvokeNodesLoadedDeferred());
                MelonLogger.Msg("[SkillForge] stub injected (3 classes, empty skills/nodes)");
            }
            catch (System.Exception ex)
            {
                // Keep the full exception (stack trace) — stub open touches
                // reflection + coroutine paths where the call site alone
                // rarely tells you which step actually blew up.
                MelonLogger.Error($"[SkillForge] stub open failed: {ex}");
            }
        }

        private static System.Collections.IEnumerator InvokeNodesLoadedDeferred()
        {
            // Give Unity a couple of frames so SkillForge.Start() runs and
            // hooks CharacterClass.OnNodesLoaded before we fire it.
            yield return null;
            yield return null;
            try { CharacterClass.OnNodesLoaded?.Invoke(); }
            catch (System.Exception ex) { MelonLogger.Error($"[SkillForge] OnNodesLoaded invoke failed: {ex}"); }
        }

        private static string FormatTrackTime(float seconds)
        {
            if (seconds <= 0f) return "?";
            int s = (int)System.Math.Round(seconds);
            return $"{s / 60}:{(s % 60):D2}";
        }

        // Gender flip — mutates Entity.mainPlayer.Gender (enum field) while
        // active so every gender consumer (avatar rig prefab, pronouns,
        // hair option matchers) sees the flipped value uniformly. Original
        // is stashed in `genderSpoofOriginal` and restored on toggle off.
        public static bool genderSpoofActive = false;
        private static Player.genders genderSpoofOriginal = Player.genders.Male;

        // Shared catalog dropdown: only one slot's picker is expanded at a
        // time (0=none, 1=Helm, 2=Armor, 3=Back, 4=Weapon, 5=Pet). Filter+scroll
        // persist across openings so a search isn't lost when switching slots.
        private static int catalogOpenSlot = 0;
        private static string catalogFilter = "";
        private static Vector2 catalogScroll = Vector2.zero;
        // Two-click confirm for the catalog Clear button: holds the slot key
        // that's currently armed and the realtime timestamp when it became
        // armed. Auto-disarms after ~3s without the second click.
        private static int catalogClearArmedSlot = 0;
        private static float catalogClearArmedTime = 0f;

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
        public static QuestRunner questRunner = new();
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
        private static bool _showChainEditor = false;
        private static bool _showChainDropdown = false;
        private static Vector2 _chainDropdownScroll = Vector2.zero;
        private static ChainEditState _chainEditState = null;
        private static Rect _chainEditorWindowRect = new(680, 200, 540, 460);
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

        public static bool retroAutoskillsActive = false;
        private static System.Collections.Generic.Dictionary<int, float> retroSkillDelays = new()
        {
            { 0, 1f }, { 1, 1f }, { 2, 1f }, { 3, 1f }, { 4, 1f }
        };
        private static string[] retroDelayInputs = new string[] { "1000", "1000", "1000", "1000", "1000" };
        private static int retroCurrentSkillIndex = 0;
        private static float retroNextSkillTime = 0f;

        public class SkillsetEntry
        {
            public string Name { get; set; }
            public string Combo { get; set; }
            public string Delays { get; set; }
            public bool WaitForSkill { get; set; }
            public string Waits { get; set; }
            public string Frees { get; set; }
        }

        private static System.Collections.Generic.List<SkillsetEntry> savedSkillsets = new();
        private static int selectedSkillsetIndex = -1;
        private static string skillsetEditName = "Generic";
        private static string skillsetEditCombo = "1,2,3,4,5";
        private static bool[] retroSkillWaits = new bool[5] { false, false, false, false, false };
        private static bool[] retroSkillFrees = new bool[5] { false, false, false, false, false };
        private static bool lastCastWasFree = false;
        private static string skillsetImportExportText = "";
        private static string skillsetFileInput = "export_skillset.txt";
        private static string _skillsetFilePath;
        private static Vector2 retroSkillsetsScroll = Vector2.zero;
        private static System.Collections.Generic.List<int> activeComboList = new();

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
            MusicCatalog.Init();
            QuestChains.Init();

            string userDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
            System.IO.Directory.CreateDirectory(userDir);
            _skillsetFilePath = System.IO.Path.Combine(userDir, "skillsets.json");
            LoadSkillsets();

            HarmonyLib.Harmony harmony = new(nameof(TestMod));
            harmony.PatchAll();
            LoggerInstance.Msg("Harmony patches applied!");
            GenerateTextures();
        }

        public override void OnApplicationQuit()
        {
            Directory.Save();
            ItemCatalog.Save();
            MusicCatalog.Save();
            PacketLog.Close();
            SaveSkillsets();
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

            // Pet combat-anim driver — no-op when toggle off or no pet.
            try { PetCombatAnimDriver.Tick(); } catch (System.Exception ex) { LoggerInstance.Error($"PetCombatAnim tick: {ex.Message}"); }

            // Camera zoom — re-apply every frame so newly-spawned CameraFollow
            // instances (area changes) pick up the active multiplier. Cheap
            // when at default: just a multiplier compare, no FindObjectOfType.
            // Apply has its own try/catch — wrapping again would just dupe logs.
            if (CameraZoom.Multiplier != CameraZoom.Default)
            {
                CameraZoom.Apply();
            }

            // HUD toggle cluster — vertical skills + hide UI/players/monsters/NPCs.
            // Internally throttled so the scene scans don't run every frame.
            try { HudToggles.Tick(); } catch (System.Exception ex) { LoggerInstance.Error($"HudToggles tick: {ex.Message}"); }

            // Hotkeys for the same toggles. Single-letter binds chosen to
            // match the original button labels (V=Vertical, U=hide UI,
            // P=other Players, M=Monsters, N=NPCs). Guarded by
            // IsTypingInChat so the keys are inert while a chat or any
            // other input field is focused — otherwise typing "vampire"
            // would flicker every toggle.
            try
            {
                if (!IsTypingInChat())
                {
                    if (Input.GetKeyDown(KeyCode.V)) { HudToggles.VerticalSkillBar = !HudToggles.VerticalSkillBar; LoggerInstance.Msg($"[Hotkey] VerticalSkillBar={HudToggles.VerticalSkillBar}"); }
                    if (Input.GetKeyDown(KeyCode.U)) { HudToggles.HideUI = !HudToggles.HideUI; LoggerInstance.Msg($"[Hotkey] HideUI={HudToggles.HideUI}"); }
                    if (Input.GetKeyDown(KeyCode.P)) { HudToggles.HideOtherPlayers = !HudToggles.HideOtherPlayers; LoggerInstance.Msg($"[Hotkey] HideOtherPlayers={HudToggles.HideOtherPlayers}"); }
                    if (Input.GetKeyDown(KeyCode.M)) { HudToggles.HideMonsters = !HudToggles.HideMonsters; LoggerInstance.Msg($"[Hotkey] HideMonsters={HudToggles.HideMonsters}"); }
                    if (Input.GetKeyDown(KeyCode.N)) { HudToggles.HideNPCs = !HudToggles.HideNPCs; LoggerInstance.Msg($"[Hotkey] HideNPCs={HudToggles.HideNPCs}"); }
                }
            }
            catch (System.Exception ex) { LoggerInstance.Error($"HudToggles hotkey: {ex.Message}"); }

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

            if (retroAutoskillsActive)
            {
                if (Time.time >= retroNextSkillTime)
                {
                    bool playerExists = false;
                    try
                    {
                        playerExists = (Entity.mainPlayer != null);
                    }
                    catch { }

                    if (playerExists)
                    {
                        // Check if any "use when free" skill is off cooldown and ready
                        int freeCastSlot = -1;
                        if (!lastCastWasFree)
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                if (retroSkillFrees[i])
                                {
                                    if (UISkillSlots.Instance != null)
                                    {
                                        SkillSlotButton slotBtn = UISkillSlots.Instance.GetSlot(i);
                                        if (slotBtn != null && !IsSkillSlotButtonDisabled(slotBtn) && !IsSkillOnCooldown(slotBtn))
                                        {
                                            freeCastSlot = i;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (freeCastSlot != -1)
                        {
                            try
                            {
                                SkillSlotButton slotBtn = UISkillSlots.Instance.GetSlot(freeCastSlot);
                                if (slotBtn != null)
                                {
                                    slotBtn.UseSkill(true);
                                    slotBtn.UseSkill(false);
                                    LoggerInstance.Msg($"Retro Autoskill casted free slot: {freeCastSlot}");
                                    lastCastWasFree = true;

                                    float delay = 1f;
                                    if (retroSkillDelays.ContainsKey(freeCastSlot))
                                    {
                                        delay = retroSkillDelays[freeCastSlot];
                                    }
                                    retroNextSkillTime = Time.time + delay;
                                    return; // Wait for delay, do not execute normal combo
                                }
                            }
                            catch (System.Exception ex)
                            {
                                LoggerInstance.Error($"Error casting free retro autoskill: {ex}");
                            }
                        }

                        List<int> combo = activeComboList.Count > 0 ? activeComboList : new System.Collections.Generic.List<int>() { 0, 1, 2, 3, 4 };
                        if (combo.Count > 0)
                        {
                            int targetSkillSlot = -1;
                            int checkCount = 0;
                            bool found = false;

                            while (checkCount < combo.Count)
                            {
                                int tempSlot = combo[retroCurrentSkillIndex % combo.Count];
                                if (tempSlot >= 0 && tempSlot < 5)
                                {
                                    targetSkillSlot = tempSlot;
                                    found = true;
                                    break;
                                }
                                retroCurrentSkillIndex = (retroCurrentSkillIndex + 1) % combo.Count;
                                checkCount++;
                            }

                            if (found && targetSkillSlot != -1)
                            {
                                bool casted = false;
                                try
                                {
                                    if (UISkillSlots.Instance != null)
                                    {
                                        SkillSlotButton slotBtn = UISkillSlots.Instance.GetSlot(targetSkillSlot);
                                        if (slotBtn != null && !IsSkillSlotButtonDisabled(slotBtn) && !IsSkillOnCooldown(slotBtn))
                                        {
                                            slotBtn.UseSkill(true);
                                            slotBtn.UseSkill(false);
                                            LoggerInstance.Msg($"Retro Autoskill casted slot: {targetSkillSlot}");
                                            casted = true;
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    LoggerInstance.Error($"Error casting retro autoskill: {ex}");
                                }

                                if (casted)
                                {
                                    float delay = 1f;
                                    if (retroSkillDelays.ContainsKey(targetSkillSlot))
                                    {
                                        delay = retroSkillDelays[targetSkillSlot];
                                    }
                                    retroNextSkillTime = Time.time + delay;
                                    retroCurrentSkillIndex = (retroCurrentSkillIndex + 1) % combo.Count;
                                    lastCastWasFree = false;
                                }
                                else
                                {
                                    // Skill was on cooldown/disabled. Check again in 100ms.
                                    retroNextSkillTime = Time.time + 0.1f;
                                    bool waitThisSkill = false;
                                    if (targetSkillSlot >= 0 && targetSkillSlot < 5)
                                    {
                                        waitThisSkill = retroSkillWaits[targetSkillSlot];
                                    }
                                    if (!waitThisSkill)
                                    {
                                        // Advance index to not get stuck on this step
                                        retroCurrentSkillIndex = (retroCurrentSkillIndex + 1) % combo.Count;
                                    }
                                    lastCastWasFree = false;
                                }
                            }
                            else
                            {
                                // All skills disabled or invalid
                                retroNextSkillTime = Time.time + 1f;
                            }
                        }
                        else
                        {
                            retroNextSkillTime = Time.time + 1f;
                        }
                    }
                    else
                    {
                        retroNextSkillTime = Time.time + 1f;
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


            // Side-by-side mode: IMGUI menu always renders when showWindow
            // is true, regardless of native. The "Native UI" toggle just
            // controls whether the native menu ALSO renders — it doesn't
            // hide IMGUI. Earlier this gated IMGUI off when native was
            // active, which violated the user's explicit side-by-side
            // choice and left the screen blank when native failed to
            // appear visibly.
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
                windowRect = ResizableWindow.HandleResize(9999, windowRect);
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
                configWindowRect = ResizableWindow.HandleResize(9998, configWindowRect);
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
                interceptorWindowRect = ResizableWindow.HandleResize(9997, interceptorWindowRect);
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
                snifferWindowRect = ResizableWindow.HandleResize(9996, snifferWindowRect);
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
                senderWindowRect = ResizableWindow.HandleResize(9995, senderWindowRect);
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
                receiverWindowRect = ResizableWindow.HandleResize(9994, receiverWindowRect);
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
                fakeDevWindowRect = ResizableWindow.HandleResize(9992, fakeDevWindowRect);
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
                shopLoaderWindowRect = ResizableWindow.HandleResize(9991, shopLoaderWindowRect);
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
                questLoaderWindowRect = ResizableWindow.HandleResize(9990, questLoaderWindowRect);
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
                questRunnerWindowRect = ResizableWindow.HandleResize(9993, questRunnerWindowRect);
            }

            if (showWindow && showQuestRunnerWindow && _showChainEditor)
            {
                if (windowStyle != null)
                    _chainEditorWindowRect = GUI.Window(9985, _chainEditorWindowRect, DrawChainEditorWindow, "Chain Editor", windowStyle);
                else
                    _chainEditorWindowRect = GUI.Window(9985, _chainEditorWindowRect, DrawChainEditorWindow, "Chain Editor");
                _chainEditorWindowRect = ResizableWindow.HandleResize(9985, _chainEditorWindowRect);
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
                funWindowRect = ResizableWindow.HandleResize(9989, funWindowRect);
            }

            if (showWindow && showExtraFunWindow)
            {
                if (windowStyle != null)
                {
                    extraFunWindowRect = GUI.Window(9987, extraFunWindowRect, DrawExtraFunWindow, "Extra Fun", windowStyle);
                }
                else
                {
                    extraFunWindowRect = GUI.Window(9987, extraFunWindowRect, DrawExtraFunWindow, "Extra Fun");
                }
                extraFunWindowRect = ResizableWindow.HandleResize(9987, extraFunWindowRect);
            }

            if (showWindow && showRetroTestsWindow)
            {
                if (windowStyle != null)
                {
                    retroTestsWindowRect = GUI.Window(9988, retroTestsWindowRect, DrawRetroTestsWindow, "Retro Tests", windowStyle);
                }
                else
                {
                    retroTestsWindowRect = GUI.Window(9988, retroTestsWindowRect, DrawRetroTestsWindow, "Retro Tests");
                }
                retroTestsWindowRect = ResizableWindow.HandleResize(9988, retroTestsWindowRect);
            }
        }

        private float DrawSeparator(float y)
        {
            if (separatorTexture != null)
            {
                y += 6f;
                GUI.DrawTexture(new Rect(20, y, 260, 2), separatorTexture);
                y += 2f + 6f;
                return y;
            }
            else
            {
                return y + 10f;
            }
        }

        private void DrawWindow(int windowID)
        {
            float contentWidth = windowRect.width - 40f;  // -20px padding each side
            GUI.Label(new Rect(20, 35, contentWidth, 25), "Test Mod Implementation", labelStyle);
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

            curY = DrawSeparator(curY);

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

            curY = DrawSeparator(curY);

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

            curY = DrawSeparator(curY);

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

            curY = DrawSeparator(curY);

            // Section 5: Automation
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Automation</b>", labelStyle);
            curY += 22f;

            string runnerBtnText = showQuestRunnerWindow ? "Hide Quest Runner" : "Quest Runner";
            if (GUI.Button(new Rect(20, curY, 260, 35), runnerBtnText, closeButtonStyle))
            {
                showQuestRunnerWindow = !showQuestRunnerWindow;
            }
            curY += 35f;

            curY = DrawSeparator(curY);

            // Section 6: Spoofers — name, gear, future cosmetic-only tweaks.
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Spoofers</b>", labelStyle);
            curY += 22f;

            string funBtnText = showFunWindow ? "Hide Fun" : "Fun";
            if (GUI.Button(new Rect(20, curY, 125, 35), funBtnText, closeButtonStyle))
            {
                showFunWindow = !showFunWindow;
            }

            string extraFunBtnText = showExtraFunWindow ? "Hide Extra" : "Extra Fun";
            if (GUI.Button(new Rect(155, curY, 125, 35), extraFunBtnText, closeButtonStyle))
            {
                showExtraFunWindow = !showExtraFunWindow;
            }
            curY += 35f;

            curY = DrawSeparator(curY);

            // Section 7: Retro Tests
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Retro Tests</b>", labelStyle);
            curY += 22f;

            string retroTestsBtnText = showRetroTestsWindow ? "Hide" : "Open";
            if (GUI.Button(new Rect(20, curY, 260, 35), retroTestsBtnText, closeButtonStyle))
            {
                showRetroTestsWindow = !showRetroTestsWindow;
            }
            curY += 35f;

            curY = DrawSeparator(curY);

            // Section 8: View — camera zoom multiplier.
            GUI.Label(new Rect(20, curY, 260, 20), $"<b>View</b>  <size=11>Zoom: {Util.CameraZoom.Multiplier:0.00}x</size>", labelStyle);
            curY += 22f;

            float newZoom = GUI.HorizontalSlider(new Rect(20, curY + 8, 195, 20), Util.CameraZoom.Multiplier, Util.CameraZoom.Min, Util.CameraZoom.Max);
            if (!Mathf.Approximately(newZoom, Util.CameraZoom.Multiplier))
            {
                Util.CameraZoom.Multiplier = newZoom;
                Util.CameraZoom.Apply();
            }
            if (GUI.Button(new Rect(220, curY, 60, 30), "Reset", closeButtonStyle))
            {
                Util.CameraZoom.Reset();
            }
            curY += 30f;

            curY = DrawSeparator(curY);

            // Section 9: Cutscenes — auto-skip toggle + manual skip.
            // Skip Now is also useful when the toggle is off and you just
            // want to bail on the current cutscene without enabling auto.
            GUI.Label(new Rect(20, curY, 260, 20), "<b>Cutscenes</b>", labelStyle);
            curY += 22f;

            string autoSkipText = autoSkipCutscenes ? "Auto-Skip: ON" : "Auto-Skip: OFF";
            if (GUI.Button(new Rect(20, curY, 125, 35), autoSkipText, closeButtonStyle))
            {
                autoSkipCutscenes = !autoSkipCutscenes;
                LoggerInstance.Msg($"Cutscene auto-skip: {(autoSkipCutscenes ? "ON" : "OFF")}");
            }
            if (GUI.Button(new Rect(155, curY, 125, 35), "Skip Now", closeButtonStyle))
            {
                try
                {
                    Dialogger_Manager mgr = Dialogger_Manager.instance;
                    if (mgr != null)
                    {
                        mgr.EndPressed();
                        LoggerInstance.Msg("Cutscene: skipped");
                    }
                    else
                    {
                        LoggerInstance.Msg("Cutscene: no active Dialogger_Manager");
                    }
                }
                catch (System.Exception ex)
                {
                    LoggerInstance.Error($"Cutscene skip failed: {ex}");
                }
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

            if (!Util.ResizableWindow.WasManuallyResized(9999))
                windowRect.height = curY + 20f;

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(windowRect.width));
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(configWindowRect.width));
        }

        private void DrawRetroTestsWindow(int windowID)
        {
            float winWidth = retroTestsWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            float curY = 35f;

            // 1. Toggle Button for Retro Autoskills
            string autoSkillsText = retroAutoskillsActive ? "Retro Autoskills: ON" : "Retro Autoskills: OFF";
            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, curY, innerW, 35), autoSkillsText, closeButtonStyle))
                {
                    retroAutoskillsActive = !retroAutoskillsActive;
                    if (retroAutoskillsActive)
                    {
                        activeComboList = ParseCombo(skillsetEditCombo);
                        retroCurrentSkillIndex = 0;
                        retroNextSkillTime = Time.time;
                        lastCastWasFree = false;
                        MelonLogger.Msg("Retro Autoskills activated!");
                    }
                    else
                    {
                        MelonLogger.Msg("Retro Autoskills deactivated!");
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, 35, innerW, 35), "Retro Autoskills: OFF", closeButtonStyle);
                GUI.enabled = true;
                retroAutoskillsActive = false;
            }
            curY += 45f;

            // 2. Combo Sequence Input
            GUI.Label(new Rect(pad, curY, innerW, 20), "<b>Combo Sequence (e.g. 2,3,4,2,3,2,1):</b>", labelStyle);
            curY += 20f;
            string newCombo = GUI.TextField(new Rect(pad, curY, innerW, 30), skillsetEditCombo, textFieldStyle);
            if (newCombo != skillsetEditCombo)
            {
                skillsetEditCombo = newCombo;
                if (retroAutoskillsActive)
                {
                    activeComboList = ParseCombo(skillsetEditCombo);
                }
            }
            curY += 40f;

            // 3. Saved Skillsets Selector
            GUI.Label(new Rect(pad, curY, innerW, 20), "<b>Saved Skillsets:</b>", labelStyle);
            curY += 20f;

            float scrollHeight = 90f;
            GUI.Box(new Rect(pad, curY, innerW, scrollHeight), "", GUI.skin.box);

            float listHeight = Mathf.Max(scrollHeight - 10f, savedSkillsets.Count * 25f);
            retroSkillsetsScroll = GUI.BeginScrollView(
                new Rect(pad, curY, innerW, scrollHeight),
                retroSkillsetsScroll,
                new Rect(0, 0, innerW - 20, listHeight)
            );

            for (int i = 0; i < savedSkillsets.Count; i++)
            {
                float itemY = i * 25f;
                string selectLabel = savedSkillsets[i].Name;
                if (selectedSkillsetIndex == i)
                {
                    GUI.Box(new Rect(2, itemY, innerW - 24, 22), "");
                    selectLabel = "▶ " + selectLabel;
                }

                if (GUI.Button(new Rect(2, itemY, innerW - 24, 22), selectLabel, rowButtonStyle))
                {
                    selectedSkillsetIndex = i;
                    skillsetEditName = savedSkillsets[i].Name;
                    skillsetEditCombo = savedSkillsets[i].Combo;

                    // Parse waits
                    if (!string.IsNullOrEmpty(savedSkillsets[i].Waits))
                    {
                        string[] waitParts = savedSkillsets[i].Waits.Split(',');
                        for (int j = 0; j < 5; j++)
                        {
                            if (j < waitParts.Length)
                            {
                                bool.TryParse(waitParts[j], out retroSkillWaits[j]);
                            }
                            else
                            {
                                retroSkillWaits[j] = false;
                            }
                        }
                    }
                    else
                    {
                        // Fallback to old global WaitForSkill flag
                        bool globalWait = savedSkillsets[i].WaitForSkill;
                        for (int j = 0; j < 5; j++)
                        {
                            retroSkillWaits[j] = globalWait;
                        }
                    }

                    // Parse frees
                    if (!string.IsNullOrEmpty(savedSkillsets[i].Frees))
                    {
                        string[] freeParts = savedSkillsets[i].Frees.Split(',');
                        for (int j = 0; j < 5; j++)
                        {
                            if (j < freeParts.Length)
                            {
                                bool.TryParse(freeParts[j], out retroSkillFrees[j]);
                            }
                            else
                            {
                                retroSkillFrees[j] = false;
                            }
                        }
                    }
                    else
                    {
                        for (int j = 0; j < 5; j++)
                        {
                            retroSkillFrees[j] = false;
                        }
                    }

                    // Parse delays
                    string[] delParts = (savedSkillsets[i].Delays ?? "1000,1000,1000,1000,1000").Split(',');
                    for (int j = 0; j < 5; j++)
                    {
                        if (j < delParts.Length)
                        {
                            retroDelayInputs[j] = delParts[j];
                            if (float.TryParse(delParts[j], out float ms))
                            {
                                retroSkillDelays[j] = ms / 1000f;
                            }
                        }
                    }

                    if (retroAutoskillsActive)
                    {
                        activeComboList = ParseCombo(skillsetEditCombo);
                    }
                    MelonLogger.Msg($"Loaded skillset: {savedSkillsets[i].Name}");
                }
            }
            GUI.EndScrollView();
            curY += scrollHeight + 10f;

            // Name input + Save + Delete row
            GUI.Label(new Rect(pad, curY, 50, 30), "Name:", labelStyle);
            skillsetEditName = GUI.TextField(new Rect(pad + 50, curY, innerW - 190, 30), skillsetEditName, textFieldStyle);

            if (GUI.Button(new Rect(pad + innerW - 130, curY, 60, 30), "Save", closeButtonStyle))
            {
                if (!string.IsNullOrEmpty(skillsetEditName))
                {
                    string delStr = string.Join(",", retroDelayInputs);
                    string waitStr = string.Join(",", retroSkillWaits);
                    string freeStr = string.Join(",", retroSkillFrees);
                    int existingIdx = savedSkillsets.FindIndex(s => s.Name.Equals(skillsetEditName, System.StringComparison.OrdinalIgnoreCase));
                    if (existingIdx >= 0)
                    {
                        savedSkillsets[existingIdx].Combo = skillsetEditCombo;
                        savedSkillsets[existingIdx].Delays = delStr;
                        savedSkillsets[existingIdx].Waits = waitStr;
                        savedSkillsets[existingIdx].Frees = freeStr;
                        selectedSkillsetIndex = existingIdx;
                    }
                    else
                    {
                        savedSkillsets.Add(new SkillsetEntry
                        {
                            Name = skillsetEditName,
                            Combo = skillsetEditCombo,
                            Delays = delStr,
                            Waits = waitStr,
                            Frees = freeStr
                        });
                        selectedSkillsetIndex = savedSkillsets.Count - 1;
                    }
                    SaveSkillsets();
                }
            }

            if (GUI.Button(new Rect(pad + innerW - 60, curY, 60, 30), "Delete", closeButtonStyle))
            {
                if (selectedSkillsetIndex >= 0 && selectedSkillsetIndex < savedSkillsets.Count)
                {
                    savedSkillsets.RemoveAt(selectedSkillsetIndex);
                    selectedSkillsetIndex = -1;
                    SaveSkillsets();
                }
            }
            curY += 40f;

            // 4. Import / Export
            GUI.Label(new Rect(pad, curY, innerW, 20), "<b>Import / Export Tool:</b>", labelStyle);
            curY += 20f;

            skillsetImportExportText = GUI.TextField(new Rect(pad, curY, innerW - 140, 30), skillsetImportExportText, textFieldStyle);

            if (GUI.Button(new Rect(pad + innerW - 130, curY, 60, 30), "Import", closeButtonStyle))
            {
                string payload = skillsetImportExportText.Trim();
                if (!string.IsNullOrEmpty(payload))
                {
                    // Format: Name|Combo|Delays|Waits|Frees
                    string[] parts = payload.Split('|');
                    if (parts.Length >= 2)
                    {
                        skillsetEditName = parts[0];
                        skillsetEditCombo = parts[1];
                        string delStr = "1000,1000,1000,1000,1000";
                        if (parts.Length >= 3)
                        {
                            delStr = parts[2];
                            string[] delParts = delStr.Split(',');
                            for (int j = 0; j < 5; j++)
                            {
                                if (j < delParts.Length)
                                {
                                    retroDelayInputs[j] = delParts[j];
                                    if (float.TryParse(delParts[j], out float ms))
                                    {
                                        retroSkillDelays[j] = ms / 1000f;
                                    }
                                }
                            }
                        }

                        string waitStr = "false,false,false,false,false";
                        if (parts.Length >= 4)
                        {
                            string rawWait = parts[3];
                            if (rawWait.Contains(","))
                            {
                                waitStr = rawWait;
                                string[] waitParts = waitStr.Split(',');
                                for (int j = 0; j < 5; j++)
                                {
                                    if (j < waitParts.Length)
                                    {
                                        bool.TryParse(waitParts[j], out retroSkillWaits[j]);
                                    }
                                    else
                                    {
                                        retroSkillWaits[j] = false;
                                    }
                                }
                            }
                            else
                            {
                                // Old single boolean format
                                bool.TryParse(rawWait, out bool globalWait);
                                for (int j = 0; j < 5; j++)
                                {
                                    retroSkillWaits[j] = globalWait;
                                }
                                waitStr = string.Join(",", retroSkillWaits);
                            }
                        }
                        else
                        {
                            for (int j = 0; j < 5; j++)
                            {
                                retroSkillWaits[j] = false;
                            }
                        }

                        string freeStr = "false,false,false,false,false";
                        if (parts.Length >= 5)
                        {
                            freeStr = parts[4];
                            string[] freeParts = freeStr.Split(',');
                            for (int j = 0; j < 5; j++)
                            {
                                if (j < freeParts.Length)
                                {
                                    bool.TryParse(freeParts[j], out retroSkillFrees[j]);
                                }
                                else
                                {
                                    retroSkillFrees[j] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int j = 0; j < 5; j++)
                            {
                                retroSkillFrees[j] = false;
                            }
                        }

                        if (retroAutoskillsActive)
                        {
                            activeComboList = ParseCombo(skillsetEditCombo);
                        }
                        AddOrUpdateSkillset(skillsetEditName, skillsetEditCombo, delStr, waitStr, freeStr);
                        MelonLogger.Msg($"Imported skillset: {skillsetEditName}");
                    }
                    else
                    {
                        MelonLogger.Error("Invalid import format. Expected 'Name|Combo|Delays|Waits|Frees', 'Name|Combo|Delays|Waits', 'Name|Combo|Delays' or 'Name|Combo'.");
                    }
                }
            }

            if (GUI.Button(new Rect(pad + innerW - 60, curY, 60, 30), "Export", closeButtonStyle))
            {
                string delStr = string.Join(",", retroDelayInputs);
                string waitStr = string.Join(",", retroSkillWaits);
                string freeStr = string.Join(",", retroSkillFrees);
                skillsetImportExportText = $"{skillsetEditName}|{skillsetEditCombo}|{delStr}|{waitStr}|{freeStr}";
                UnityEngine.GUIUtility.systemCopyBuffer = skillsetImportExportText;
                MelonLogger.Msg("Exported skillset copied to clipboard!");
            }
            curY += 45f;

            if (separatorTexture != null)
            {
                GUI.DrawTexture(new Rect(pad, curY, innerW, 2), separatorTexture);
                curY += 15f;
            }

            // File I/O row
            GUI.Label(new Rect(pad, curY, 70, 30), "Filename:", labelStyle);
            skillsetFileInput = GUI.TextField(new Rect(pad + 70, curY, innerW - 210, 30), skillsetFileInput, textFieldStyle);

            if (GUI.Button(new Rect(pad + innerW - 130, curY, 60, 30), "Load File", closeButtonStyle))
            {
                try
                {
                    string userDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
                    System.IO.Directory.CreateDirectory(userDir);
                    string defaultFile = skillsetFileInput.Trim();
                    string fullPath = ShowOpenFileDialog(userDir, defaultFile);
                    if (!string.IsNullOrEmpty(fullPath))
                    {
                        skillsetFileInput = System.IO.Path.GetFileName(fullPath);
                        if (System.IO.File.Exists(fullPath))
                        {
                            string payload = System.IO.File.ReadAllText(fullPath).Trim();
                            if (!string.IsNullOrEmpty(payload))
                            {
                                string[] parts = payload.Split('|');
                                if (parts.Length >= 2)
                                {
                                    skillsetEditName = parts[0];
                                    skillsetEditCombo = parts[1];
                                    string delStr = "1000,1000,1000,1000,1000";
                                    if (parts.Length >= 3)
                                    {
                                        delStr = parts[2];
                                        string[] delParts = delStr.Split(',');
                                        for (int j = 0; j < 5; j++)
                                        {
                                            if (j < delParts.Length)
                                            {
                                                retroDelayInputs[j] = delParts[j];
                                                if (float.TryParse(delParts[j], out float ms))
                                                {
                                                    retroSkillDelays[j] = ms / 1000f;
                                                }
                                            }
                                        }
                                    }
                                    string waitStr = "false,false,false,false,false";
                                    if (parts.Length >= 4)
                                    {
                                        string rawWait = parts[3];
                                        if (rawWait.Contains(","))
                                        {
                                            waitStr = rawWait;
                                            string[] waitParts = waitStr.Split(',');
                                            for (int j = 0; j < 5; j++)
                                            {
                                                if (j < waitParts.Length)
                                                {
                                                    bool.TryParse(waitParts[j], out retroSkillWaits[j]);
                                                }
                                                else
                                                {
                                                    retroSkillWaits[j] = false;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Old single boolean format
                                            bool.TryParse(rawWait, out bool globalWait);
                                            for (int j = 0; j < 5; j++)
                                            {
                                                retroSkillWaits[j] = globalWait;
                                            }
                                            waitStr = string.Join(",", retroSkillWaits);
                                        }
                                    }
                                    else
                                    {
                                        for (int j = 0; j < 5; j++)
                                        {
                                            retroSkillWaits[j] = false;
                                        }
                                    }

                                    string freeStr = "false,false,false,false,false";
                                    if (parts.Length >= 5)
                                    {
                                        freeStr = parts[4];
                                        string[] freeParts = freeStr.Split(',');
                                        for (int j = 0; j < 5; j++)
                                        {
                                            if (j < freeParts.Length)
                                            {
                                                bool.TryParse(freeParts[j], out retroSkillFrees[j]);
                                            }
                                            else
                                            {
                                                retroSkillFrees[j] = false;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        for (int j = 0; j < 5; j++)
                                        {
                                            retroSkillFrees[j] = false;
                                        }
                                    }

                                    if (retroAutoskillsActive)
                                    {
                                        activeComboList = ParseCombo(skillsetEditCombo);
                                    }
                                    skillsetImportExportText = payload;
                                    AddOrUpdateSkillset(skillsetEditName, skillsetEditCombo, delStr, waitStr, freeStr);
                                    MelonLogger.Msg($"Imported skillset from file: {fullPath}");
                                }
                                else
                                {
                                    MelonLogger.Error("Invalid file content format. Expected Name|Combo|Delays|Waits|Frees");
                                }
                            }
                        }
                        else
                        {
                            MelonLogger.Error($"File does not exist: {fullPath}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Failed to load from file: {ex.Message}");
                }
            }

            if (GUI.Button(new Rect(pad + innerW - 60, curY, 60, 30), "Save File", closeButtonStyle))
            {
                try
                {
                    string userDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
                    System.IO.Directory.CreateDirectory(userDir);
                    string defaultFile = skillsetFileInput.Trim();
                    string fullPath = ShowSaveFileDialog(userDir, defaultFile);
                    if (!string.IsNullOrEmpty(fullPath))
                    {
                        skillsetFileInput = System.IO.Path.GetFileName(fullPath);
                        string delStr = string.Join(",", retroDelayInputs);
                        string waitStr = string.Join(",", retroSkillWaits);
                        string freeStr = string.Join(",", retroSkillFrees);
                        string payload = $"{skillsetEditName}|{skillsetEditCombo}|{delStr}|{waitStr}|{freeStr}";
                        System.IO.File.WriteAllText(fullPath, payload);
                        skillsetImportExportText = payload;
                        MelonLogger.Msg($"Saved skillset setup to file: {fullPath}");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Failed to save to file: {ex.Message}");
                }
            }
            curY += 40f;

            // 5. Skill Delay Configuration
            GUI.Label(new Rect(pad, curY, innerW, 20), "<b>Skill Delays:</b>", labelStyle);
            curY += 20f;

            for (int i = 0; i < 5; i++)
            {
                GUI.Label(new Rect(pad, curY, 80, 30), GetSkillKeyName(i), labelStyle);

                string delayStr = retroDelayInputs[i];
                string newDelayStr = GUI.TextField(new Rect(pad + 85, curY, 50, 30), delayStr, textFieldStyle);
                if (newDelayStr != delayStr)
                {
                    retroDelayInputs[i] = newDelayStr;
                    if (float.TryParse(newDelayStr, out float ms))
                    {
                        retroSkillDelays[i] = ms / 1000f;
                    }
                }

                bool oldWait = retroSkillWaits[i];
                bool newWait = GUI.Toggle(new Rect(pad + 140, curY + 5, 20, 20), oldWait, "");
                if (newWait != oldWait)
                {
                    retroSkillWaits[i] = newWait;
                    if (newWait)
                    {
                        retroSkillFrees[i] = false;
                    }
                }
                GUI.Label(new Rect(pad + 162, curY, 32, 30), "Wait", labelStyle);

                bool oldFree = retroSkillFrees[i];
                bool newFree = GUI.Toggle(new Rect(pad + 198, curY + 5, 20, 20), oldFree, "");
                if (newFree != oldFree)
                {
                    retroSkillFrees[i] = newFree;
                    if (newFree)
                    {
                        retroSkillWaits[i] = false;
                    }
                }
                GUI.Label(new Rect(pad + 220, curY, 32, 30), "Free", labelStyle);

                curY += 35f;
            }
            curY += 10f;

            if (GUI.Button(new Rect(pad, curY, innerW, 35), "Close Window", closeButtonStyle))
            {
                showRetroTestsWindow = false;
            }
            curY += 45f;

            if (!Util.ResizableWindow.WasManuallyResized(9988))
                retroTestsWindowRect.height = curY;
            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
        }

        private static System.Collections.Generic.List<int> ParseCombo(string comboStr)
        {
            List<int> list = new();
            if (string.IsNullOrEmpty(comboStr)) return list;
            string[] parts = comboStr.Split(',');
            foreach (string part in parts)
            {
                if (int.TryParse(part.Trim(), out int keyNum))
                {
                    int slot = keyNum - 1;
                    if (slot >= 0 && slot < 5)
                    {
                        list.Add(slot);
                    }
                }
            }
            return list;
        }

        private static void AddOrUpdateSkillset(string name, string combo, string delays, string waits, string frees)
        {
            if (string.IsNullOrEmpty(name)) return;
            int existingIdx = savedSkillsets.FindIndex(s => s.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0)
            {
                savedSkillsets[existingIdx].Combo = combo;
                savedSkillsets[existingIdx].Delays = delays;
                savedSkillsets[existingIdx].Waits = waits;
                savedSkillsets[existingIdx].Frees = frees;
                selectedSkillsetIndex = existingIdx;
            }
            else
            {
                savedSkillsets.Add(new SkillsetEntry
                {
                    Name = name,
                    Combo = combo,
                    Delays = delays,
                    Waits = waits,
                    Frees = frees
                });
                selectedSkillsetIndex = savedSkillsets.Count - 1;
            }
            SaveSkillsets();
        }

        private static void AddOrUpdateSkillset(string name, string combo, string delays, string waits)
        {
            AddOrUpdateSkillset(name, combo, delays, waits, "false,false,false,false,false");
        }

        private static void AddOrUpdateSkillset(string name, string combo, string delays, bool waitForSkill = false)
        {
            string waits = string.Join(",", new bool[] { waitForSkill, waitForSkill, waitForSkill, waitForSkill, waitForSkill });
            AddOrUpdateSkillset(name, combo, delays, waits, "false,false,false,false,false");
        }

        private static void LoadSkillsets()
        {
            try
            {
                if (System.IO.File.Exists(_skillsetFilePath))
                {
                    string json = System.IO.File.ReadAllText(_skillsetFilePath);
                    savedSkillsets = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<SkillsetEntry>>(json) ?? new();
                    MelonLogger.Msg($"Loaded {savedSkillsets.Count} saved skillsets.");
                }
                else
                {
                    savedSkillsets = new();
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Failed to load skillsets: {ex.Message}");
            }
        }

        private static void SaveSkillsets()
        {
            try
            {
                if (!string.IsNullOrEmpty(_skillsetFilePath))
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(savedSkillsets, Newtonsoft.Json.Formatting.Indented);
                    System.IO.File.WriteAllText(_skillsetFilePath, json);
                    MelonLogger.Msg("Saved skillsets successfully.");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Failed to save skillsets: {ex.Message}");
            }
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(interceptorWindowRect.width));
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(senderWindowRect.width));
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
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
            DrawFakeDevAccessTier(pad, btnW, "30", 30, currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + btnW + 4, btnW, "40", 40, currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + (btnW + 4) * 2, btnW, "50", 50, currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + (btnW + 4) * 3, btnW, "60", 60, currentLevel, playerExists);
            DrawFakeDevAccessTier(pad + (btnW + 4) * 4, btnW, "100", 100, currentLevel, playerExists);

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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
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

            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Weapon", 4,
                ref weaponSpoofInput, weaponSpoofActive,
                ApplyWeaponSpoof, ClearWeaponSpoof);

            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Pet", 5,
                ref petSpoofInput, petSpoofActive,
                ApplyPetSpoof, ClearPetSpoof);

            // Shared catalog panel — only this window's slots (1..5). Slot 6
            // (Monster→Pet) is owned by Extra Fun and renders its picker there.
            if (catalogOpenSlot >= 1 && catalogOpenSlot <= 5)
                curY = DrawCatalogPicker(curY, pad, innerW);

            if (GUI.Button(new Rect(pad, curY, innerW, 32), "Close", closeButtonStyle))
                showFunWindow = false;
            curY += 40f;

            // Auto-size window to fit current content (collapsed vs catalog-open).
            if (!Util.ResizableWindow.WasManuallyResized(9989))
                funWindowRect.height = curY + 10f;

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
        }

        // Extra Fun — sibling window for niche spoofs. Currently hosts the
        // Monster→Pet row, which reuses the Pet spoof state/handlers but
        // owns its own catalog slot (6 = Monsters bucket).
        private void DrawExtraFunWindow(int windowID)
        {
            float winWidth = extraFunWindowRect.width;
            float pad = 20f;
            float innerW = winWidth - pad * 2;

            bool playerExists = false;
            try { playerExists = (Entity.mainPlayer != null); } catch { }

            float curY = 35f;

            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Monster→Pet", 6,
                ref petSpoofInput, petSpoofActive,
                ApplyPetSpoof, ClearPetSpoof);

            curY = DrawGearSpoofSlot(curY, pad, innerW, playerExists,
                "Become Monster", 7,
                ref monTransformInput, monTransformActive,
                ApplyMonTransformSpoof, ClearMonTransformSpoof);

            // Jukebox — play any soundtrack by ID. Loads via SoundtrackLoader
            // (data/getsoundtracks?ids=<id>) on first request, cached after.
            // Known tracks come from MusicCatalog (harvested passively).
            int namedCount = MusicCatalog.Tracks.Values.Count(t => !string.IsNullOrEmpty(t.name));
            GUI.Label(new Rect(pad, curY, innerW, 18), $"Jukebox ({namedCount} / {MusicCatalog.Tracks.Count} named):", labelStyle);
            curY += 20f;

            // Selection toggle — clicking opens the picker panel.
            string selLabel = "▼ (select a track)";
            if (jukeboxSelectedId > 0 && MusicCatalog.Tracks.TryGetValue(jukeboxSelectedId, out MusicCatalog.TrackEntry curTrack))
            {
                string nm = string.IsNullOrEmpty(curTrack.name) ? "?" : curTrack.name;
                selLabel = $"{(jukeboxPickerOpen ? "▲" : "▼")} {curTrack.id} — {nm}  ({FormatTrackTime(curTrack.length)})";
            }
            else
            {
                selLabel = (jukeboxPickerOpen ? "▲" : "▼") + " (select a track)";
            }
            if (GUI.Button(new Rect(pad, curY, innerW, 26), selLabel, closeButtonStyle))
                jukeboxPickerOpen = !jukeboxPickerOpen;
            curY += 30f;

            if (jukeboxPickerOpen)
            {
                // Filter — matches against id or name, substring.
                GUI.Label(new Rect(pad, curY, 60, 22), "Filter:", labelStyle);
                jukeboxFilter = GUI.TextField(new Rect(pad + 60, curY, innerW - 60, 22), jukeboxFilter ?? "");
                curY += 26f;

                string filter = (jukeboxFilter ?? "").Trim();
                List<MusicCatalog.TrackEntry> entries = MusicCatalog.Tracks.Values
                    .Where(t => string.IsNullOrEmpty(filter)
                        || t.id.ToString().Contains(filter)
                        || (!string.IsNullOrEmpty(t.name)
                            && t.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(t => t.id)
                    .ToList();

                float listH = 160f;
                float rowH = 22f;
                float contentH = entries.Count * rowH;
                jukeboxScroll = GUI.BeginScrollView(
                    new Rect(pad, curY, innerW, listH),
                    jukeboxScroll,
                    new Rect(0, 0, innerW - 20, contentH));
                for (int i = 0; i < entries.Count; i++)
                {
                    MusicCatalog.TrackEntry t = entries[i];
                    string nm = string.IsNullOrEmpty(t.name) ? "?" : t.name;
                    string row = $"{t.id,4} — {nm}  ({FormatTrackTime(t.length)})";
                    if (GUI.Button(new Rect(0, i * rowH, innerW - 20, rowH - 2), row, closeButtonStyle))
                    {
                        jukeboxSelectedId = t.id;
                        jukeboxPickerOpen = false;
                    }
                }
                GUI.EndScrollView();
                curY += listH + 6f;
            }

            // Action row: Play (selected), Stop, Restore Area BGM.
            float jbW = (innerW - 20) / 3f;
            if (GUI.Button(new Rect(pad, curY, jbW, 30), "Play", closeButtonStyle))
            {
                if (jukeboxSelectedId > 0) Jukebox.Play(jukeboxSelectedId);
                else MelonLogger.Warning("[Jukebox] no track selected");
            }
            if (GUI.Button(new Rect(pad + jbW + 10, curY, jbW, 30), "Stop", closeButtonStyle))
                Jukebox.Stop();
            if (GUI.Button(new Rect(pad + (jbW + 10) * 2, curY, jbW, 30), "Restore Area", closeButtonStyle))
                Jukebox.RestoreAreaBGM();
            curY += 36f;

            // Escape hatch — type an ID that isn't in the catalog yet (so
            // there's no row to click) and play it. Once it loads, the
            // harvest patch records it for future dropdown visibility.
            GUI.Label(new Rect(pad, curY, 90, 22), "Play by ID:", labelStyle);
            jukeboxInput = GUI.TextField(new Rect(pad + 90, curY, innerW - 90 - 70, 22), jukeboxInput ?? "");
            if (GUI.Button(new Rect(pad + innerW - 65, curY, 65, 22), "Go", closeButtonStyle))
            {
                if (int.TryParse((jukeboxInput ?? "").Trim(), out int rawId))
                    Jukebox.Play(rawId);
                else
                    MelonLogger.Warning($"[Jukebox] '{jukeboxInput}' is not a number");
            }
            curY += 30f;

            // Pet combat-anim cycler — applies to the spoofed pet (Monster→Pet).
            // Only meaningful when there's a pet GO; toggle stays clickable
            // regardless so users can pre-arm it.
            string animBtn = petCombatAnimActive
                ? "Pet Combat Anims: ON"
                : "Pet Combat Anims: OFF";
            if (GUI.Button(new Rect(pad, curY, innerW, 30), animBtn, closeButtonStyle))
            {
                petCombatAnimActive = !petCombatAnimActive;
                MelonLogger.Msg($"[PetCombatAnim] {(petCombatAnimActive ? "ON" : "OFF")}");
            }
            curY += 40f;

            // Skill Forge — opens the in-game class designer. The legacy
            // DevConsole button is a no-op, but the feature moved to
            // UIMiniMenu.ToggleSkillForge and is fully alive. We bypass the
            // CanOpen() dialog-active check by calling ShowForge() directly.
            // No AccessLevel gate at this layer; submit calls (sfAdd/sfSave)
            // go straight to the live server.
            // Real open — server gates sfInit, panels stay invisible.
            float forgeW = (innerW - 10) / 2f;
            if (GUI.Button(new Rect(pad, curY, forgeW, 30), "Open Skill Forge", closeButtonStyle))
            {
                try
                {
                    if (UIWindowManager.instance != null)
                    {
                        UIWindowManager.instance.ShowForge();
                        MelonLogger.Msg("[SkillForge] opened (real sfInit fired)");
                    }
                    else
                    {
                        MelonLogger.Warning("[SkillForge] UIWindowManager.instance is null — log in first");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"[SkillForge] open failed: {ex}");
                }
            }
            // Stub open — inject synthetic ClassNodes/SkillNodes/AllSkills so
            // the UI populates client-side. Any sfAdd/sfSave will still be
            // silently rejected server-side; this is sightseeing only.
            if (GUI.Button(new Rect(pad + forgeW + 10, curY, forgeW, 30), "Open w/ Stub Data", closeButtonStyle))
            {
                OpenForgeStubbed();
            }
            curY += 40f;

            // Catalog pickers for Extra Fun's slots (6, 7) — Fun handles 1..5.
            if (catalogOpenSlot == 6 || catalogOpenSlot == 7)
                curY = DrawCatalogPicker(curY, pad, innerW);

            if (GUI.Button(new Rect(pad, curY, innerW, 32), "Close", closeButtonStyle))
                showExtraFunWindow = false;
            curY += 40f;

            if (!Util.ResizableWindow.WasManuallyResized(9987))
                extraFunWindowRect.height = curY + 10f;
            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
        }

        /// <summary>
        /// Renders the shared catalog dropdown for whatever slot is currently
        /// open (caller is responsible for gating). Returns the curY below
        /// the rendered block. Factored out so both Fun and Extra Fun can
        /// host their owned slots without duplicating the filter+list logic.
        /// </summary>
        private static float DrawCatalogPicker(float curY, float pad, float innerW)
        {
            System.Collections.Generic.Dictionary<string, ItemCatalog.ItemEntry> bucket;
            System.Action<string> onSelect;
            string slotLabel;
            switch (catalogOpenSlot)
            {
                case 1: bucket = ItemCatalog.Helms; onSelect = s => helmSpoofInput = s; slotLabel = "Helm"; break;
                case 2: bucket = ItemCatalog.Armors; onSelect = s => armorSpoofInput = s; slotLabel = "Armor"; break;
                case 3: bucket = ItemCatalog.Backs; onSelect = s => backSpoofInput = s; slotLabel = "Cape"; break;
                case 4: bucket = ItemCatalog.Weapons; onSelect = s => weaponSpoofInput = s; slotLabel = "Weapon"; break;
                case 5: bucket = ItemCatalog.Pets; onSelect = s => petSpoofInput = s; slotLabel = "Pet"; break;
                case 6: bucket = ItemCatalog.Monsters; onSelect = s => petSpoofInput = s; slotLabel = "Monster (Pet)"; break;
                case 7: bucket = ItemCatalog.Monsters; onSelect = s => monTransformInput = s; slotLabel = "Monster (Transform)"; break;
                default: return curY;
            }

            GUI.Label(new Rect(pad, curY, innerW, 20),
                $"{slotLabel} Catalog ({bucket.Count}) — filter:", labelStyle);
            curY += 22f;

            float clearBtnW = 90f;
            float filterW = innerW - clearBtnW - 6f;
            catalogFilter = GUI.TextField(new Rect(pad, curY, filterW, 28), catalogFilter, textFieldStyle);

            bool armed = catalogClearArmedSlot == catalogOpenSlot
                      && Time.realtimeSinceStartup - catalogClearArmedTime < 3f;
            string clearLabel = armed ? "Confirm?" : "Clear";
            if (GUI.Button(new Rect(pad + filterW + 6f, curY, clearBtnW, 28), clearLabel, closeButtonStyle))
            {
                if (armed)
                {
                    switch (catalogOpenSlot)
                    {
                        case 1: ItemCatalog.ClearHelms(); break;
                        case 2: ItemCatalog.ClearArmors(); break;
                        case 3: ItemCatalog.ClearBacks(); break;
                        case 4: ItemCatalog.ClearWeapons(); break;
                        case 5: ItemCatalog.ClearPets(); break;
                        case 6: ItemCatalog.ClearMonsters(); break;
                        case 7: ItemCatalog.ClearMonsters(); break;
                    }
                    catalogClearArmedSlot = 0;
                    catalogScroll = Vector2.zero;
                }
                else
                {
                    catalogClearArmedSlot = catalogOpenSlot;
                    catalogClearArmedTime = Time.realtimeSinceStartup;
                }
            }
            curY += 32f;

            string filt = catalogFilter?.ToLowerInvariant() ?? "";
            List<ItemCatalog.ItemEntry> matches = new();
            foreach (ItemCatalog.ItemEntry e in bucket.Values)
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
                ItemCatalog.ItemEntry e = matches[i];
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
            return curY;
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

            // Three buttons in a row: Apply / Clear / Browse-toggle. Labels
            // are intentionally generic — the section label above already
            // names the slot, so repeating it would overflow on long names
            // (e.g. "Monster→Pet", "Become Monster").
            float btnW = (innerW - 20) / 3f;
            string applyText = active ? "Update" : "Apply";
            string browseText = (catalogOpenSlot == slotKey) ? "Hide ▲" : "Browse ▼";

            if (playerExists)
            {
                if (GUI.Button(new Rect(pad, curY, btnW, 30), applyText, closeButtonStyle))
                    apply?.Invoke(input);
                if (GUI.Button(new Rect(pad + btnW + 10, curY, btnW, 30), "Clear", closeButtonStyle))
                    clear?.Invoke();
            }
            else
            {
                GUI.enabled = false;
                GUI.Button(new Rect(pad, curY, btnW, 30), applyText, closeButtonStyle);
                GUI.Button(new Rect(pad + btnW + 10, curY, btnW, 30), "Clear", closeButtonStyle);
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

        // Weapon spoof: bundle swap + temporary PrefabName/ItemType mutation
        // on Entity.mainPlayer.Weapon. Requires a catalog entry for the
        // target bundle since we can't synthesize PrefabName/ItemType without
        // having seen the weapon on some character. Originals are stashed
        // by WeaponSpoofState and restored on Clear.
        private static void ApplyWeaponSpoof(string desiredBundle)
        {
            if (Entity.mainPlayer == null) return;
            desiredBundle = (desiredBundle ?? "").Trim();
            if (desiredBundle.Length == 0)
            {
                ClearWeaponSpoof();
                return;
            }
            if (Entity.mainPlayer.Weapon == null)
            {
                MelonLogger.Warning("[WeaponSpoof] no weapon equipped — equip one before spoofing.");
                return;
            }
            if (!ItemCatalog.Weapons.TryGetValue(desiredBundle, out ItemCatalog.ItemEntry cat))
            {
                MelonLogger.Warning($"[WeaponSpoof] '{desiredBundle}' not in catalog. See it on a character first so PrefabName/ItemType can be captured.");
                return;
            }

            weaponSpoofActive = true;
            weaponSpoofBundle = desiredBundle;
            weaponSpoofInput = desiredBundle;
            WeaponSpoofState.Apply(Entity.mainPlayer.Weapon, cat.prefab, (iType)cat.itemType);
            try { Entity.mainPlayer.createAvatar(); } catch { }
            MelonLogger.Msg($"[WeaponSpoof] applied bundle '{desiredBundle}' (prefab={cat.prefab}, type={(iType)cat.itemType}).");
        }

        private static void ClearWeaponSpoof()
        {
            weaponSpoofActive = false;
            weaponSpoofBundle = "";
            WeaponSpoofState.Restore();
            try { Entity.mainPlayer?.createAvatar(); } catch { }
            MelonLogger.Msg("[WeaponSpoof] cleared.");
        }

        // Pet spoof: full field swap on Entity.mainPlayer.Pet (Bundle,
        // PrefabName, Scale, OffsetX, OffsetY). PetLoader.LoadItem reads
        // those directly into BundlePrefabLoader — no GetBundleData detour
        // — so the postfix path used by gear loaders doesn't apply here.
        // Catalog-required: scale/offsets can't be synthesized.
        private static void ApplyPetSpoof(string desiredBundle)
        {
            if (Entity.mainPlayer == null) return;
            desiredBundle = (desiredBundle ?? "").Trim();
            if (desiredBundle.Length == 0)
            {
                ClearPetSpoof();
                return;
            }
            if (Entity.mainPlayer.Pet == null)
            {
                MelonLogger.Warning("[PetSpoof] no pet equipped — equip one before spoofing.");
                return;
            }
            if (!ItemCatalog.TryGetPetOrMonster(desiredBundle, out ItemCatalog.ItemEntry cat))
            {
                MelonLogger.Warning($"[PetSpoof] '{desiredBundle}' not in Pets or Monsters catalog. See it in-world first so PrefabName/Scale can be captured.");
                return;
            }

            petSpoofActive = true;
            petSpoofBundle = desiredBundle;
            petSpoofInput = desiredBundle;
            Dictionary<string, ItemCatalog.ItemEntry> sourceBucket = ItemCatalog.Pets.ContainsKey(desiredBundle)
                ? ItemCatalog.Pets : ItemCatalog.Monsters;
            AssetBundleData spoofedBundle = BundleBuilder.Build(desiredBundle, sourceBucket, Entity.mainPlayer.Pet.Bundle, Entity.mainPlayer.Pet.Bundle);
            PetSpoofState.Apply(Entity.mainPlayer.Pet, spoofedBundle, cat.prefab, cat.scale, cat.offX, cat.offY);
            // Use the game's own re-equip-pet path. createAvatar doesn't help
            // because loadAllEquip only constructs a PetLoader when petGO is
            // null, and DestroyAsset leaves petGO alive (it's parented to the
            // entity container, not avtGO). Entity.EquipItem(Pet) destroys
            // petGO and calls BundlePrefabLoader.Load directly from the
            // (now mutated) EquipItem fields.
            try { Entity.mainPlayer.EquipItem(Entity.mainPlayer.Pet); } catch { }
            MelonLogger.Msg($"[PetSpoof] applied bundle '{desiredBundle}' (prefab={cat.prefab}).");
        }

        private static void ClearPetSpoof()
        {
            petSpoofActive = false;
            petSpoofBundle = "";
            PetSpoofState.Restore();
            try
            {
                if (Entity.mainPlayer?.Pet != null)
                    Entity.mainPlayer.EquipItem(Entity.mainPlayer.Pet);
            }
            catch { }
            MelonLogger.Msg("[PetSpoof] cleared.");
        }

        // Monster-transform spoof: piggybacks on the game's transform-potion
        // path (Entity.ApplyMonTransform). Needs the bundle, linkage (prefab
        // name) and scale from the Monsters catalog. Auto-reverts on Combat
        // state — Entity.currentState calls RemoveMonTransform when value
        // becomes Combat. That's the game's rule; we don't fight it.
        private static void ApplyMonTransformSpoof(string desiredBundle)
        {
            if (Entity.mainPlayer == null) return;
            desiredBundle = (desiredBundle ?? "").Trim();
            if (desiredBundle.Length == 0)
            {
                ClearMonTransformSpoof();
                return;
            }
            if (!ItemCatalog.Monsters.TryGetValue(desiredBundle, out ItemCatalog.ItemEntry cat))
            {
                MelonLogger.Warning($"[MonTransform] '{desiredBundle}' not in Monsters catalog. See the monster in-world first.");
                return;
            }

            float scale = (float)(cat.scale ?? 1.0);
            if (scale <= 0f) scale = 1f;
            AssetBundleData bundle = BundleBuilder.Build(desiredBundle, ItemCatalog.Monsters, null, null);

            try
            {
                Entity.mainPlayer.ApplyMonTransform(bundle, cat.prefab, scale);
                monTransformActive = true;
                monTransformBundle = desiredBundle;
                monTransformInput = desiredBundle;
                MelonLogger.Msg($"[MonTransform] applied '{desiredBundle}' (prefab={cat.prefab}, scale={scale}). Reverts on combat.");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[MonTransform] apply failed: {ex.Message}");
            }
        }

        private static void ClearMonTransformSpoof()
        {
            monTransformActive = false;
            monTransformBundle = "";
            try { Entity.mainPlayer?.RemoveMonTransform(); } catch { }
            MelonLogger.Msg("[MonTransform] cleared.");
        }

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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
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
                && Directory.Quests.TryGetValue(previewQid, out Directory.QuestEntry qe))
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
                        PlayerQuestData pq = Entity.mainPlayer?.Quests;
                        for (int i = 0; i < q.Turnins.Length && i < 6; i++)
                        {
                            QuestTurninItem t = q.Turnins[i];
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

            // ---- Chain selector row with dropdown + New/Edit/Run ----
            List<string> chainNames = new(QuestChains.Names);
            if (questChainPickerIndex >= chainNames.Count) questChainPickerIndex = 0;
            string currentChainName = chainNames.Count == 0
                ? "(no chains)"
                : chainNames[questChainPickerIndex];
            int currentEntryCount = chainNames.Count == 0 ? 0 : (QuestChains.Get(currentChainName)?.Count ?? 0);

            if (_chainEditState == null) _chainEditState = new ChainEditState();

            // Row: [Chain: v dropdown button] [New] [Edit] [Run Chain] [progress]
            GUI.Label(new Rect(pad, 382, 48, 22), "Chain:", labelStyle);

            // Dropdown toggle button
            if (GUI.Button(new Rect(pad + 50, 378, 188, 30),
                $"{currentChainName}  ({currentEntryCount})  v", closeButtonStyle))
                _showChainDropdown = !_showChainDropdown;

            if (GUI.Button(new Rect(pad + 244, 378, 44, 30), "New", closeButtonStyle))
            {
                _chainEditState.Open(chainNames, null);
                _showChainEditor = true;
                _showChainDropdown = false;
            }
            if (GUI.Button(new Rect(pad + 294, 378, 44, 30), "Edit", closeButtonStyle))
            {
                _chainEditState.Open(chainNames, chainNames.Count == 0 ? null : currentChainName);
                _showChainEditor = true;
                _showChainDropdown = false;
            }

            string chainProgress = (questRunner.ChainEntries != null)
                ? $"▶ {questRunner.ChainName} {questRunner.ChainIndex + 1}/{questRunner.ChainEntries.Count}"
                : "";
            GUI.Label(new Rect(pad + 344, 382, 140, 22), chainProgress, logTextStyle);

            bool isRunningC = questRunner.IsRunning;
            GUI.enabled = !isRunningC && chainNames.Count > 0;
            if (GUI.Button(new Rect(pad + 460, 378, 120, 30), "Run Chain", closeButtonStyle))
            {
                questRunnerLog.Clear();
                _showChainDropdown = false;
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

            // ---- Dropdown list (drawn on top, last in pass) ----
            if (_showChainDropdown && chainNames.Count > 0)
            {
                float ddX = pad + 50, ddY = 409f;
                float ddW = 188f, ddRowH = 24f;
                float ddH = Mathf.Min(chainNames.Count * ddRowH + 4, 200f);
                GUI.Box(new Rect(ddX - 2, ddY - 2, ddW + 4, ddH + 4), "");
                _chainDropdownScroll = GUI.BeginScrollView(
                    new Rect(ddX, ddY, ddW, ddH),
                    _chainDropdownScroll,
                    new Rect(0, 0, ddW - 16, chainNames.Count * ddRowH));
                for (int ci = 0; ci < chainNames.Count; ci++)
                {
                    bool selected = ci == questChainPickerIndex;
                    GUIStyle style = selected ? labelStyle : rowButtonStyle;
                    if (GUI.Button(new Rect(0, ci * ddRowH, ddW - 16, ddRowH - 2), chainNames[ci], style))
                    {
                        questChainPickerIndex = ci;
                        _showChainDropdown = false;
                    }
                }
                GUI.EndScrollView();
            }

            // ---- Chain Editor panel ---- drawn as separate floating window (see OnGUI)

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
                List<KeyValuePair<int, Directory.QuestEntry>> matches = new();
                foreach (KeyValuePair<int, Directory.QuestEntry> kv in Directory.Quests)
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
                    KeyValuePair<int, Directory.QuestEntry> kv = matches[i];
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

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(winWidth));
        }

        // ------- Chain Editor logic + GUI (INJECTED) -------
        private class ChainEditState
        {
            public string editingName;
            public string saveAsName = "";
            public List<QuestChains.Entry> entries = new();
            public int editingIdx = -1;
            public string errorMsg = null;
            public bool editingExisting = false;
            public Vector2 scroll = Vector2.zero;
            public List<string> chainNames = new();
            // Load dropdown
            public bool showLoadDropdown = false;
            public Vector2 loadDropScroll = Vector2.zero;
            public int loadSelectedIdx = -1;

            public void Open(List<string> allNames, string pick)
            {
                chainNames = new List<string>(allNames);
                editingName = pick ?? "NewChain";
                saveAsName = editingName;
                entries = (pick != null && QuestChains.Get(pick) != null) ?
                          QuestChains.Get(pick).Select(e => new QuestChains.Entry
                          {
                              qid = e.qid,
                              area = e.area,
                              frame = e.frame,
                              pad = e.pad,
                              items = e.items
                          }).ToList() :
                          new List<QuestChains.Entry>();
                errorMsg = null;
                editingExisting = (pick != null && QuestChains.Get(pick) != null);
                editingIdx = (pick == null ? -1 : allNames.IndexOf(pick));
                showLoadDropdown = false;
                loadSelectedIdx = editingIdx;
            }
        }

        private static void DrawChainEditorWindow(int windowID)
        {
            if (_chainEditState == null) return;
            float p = 10f;
            float W = _chainEditorWindowRect.width;
            float H = _chainEditorWindowRect.height;
            float y = 28f;

            // ---- Row 1: Load existing chain ----
            GUI.Label(new Rect(p, y + 3, 38, 22), "Load:", labelStyle);
            string loadLabel = (_chainEditState.loadSelectedIdx >= 0 && _chainEditState.loadSelectedIdx < _chainEditState.chainNames.Count)
                ? _chainEditState.chainNames[_chainEditState.loadSelectedIdx]
                : "(select chain)";
            if (GUI.Button(new Rect(p + 42, y, 180, 26), loadLabel + "  v", closeButtonStyle))
                _chainEditState.showLoadDropdown = !_chainEditState.showLoadDropdown;
            if (GUI.Button(new Rect(p + 228, y, 60, 26), "Load", closeButtonStyle))
            {
                if (_chainEditState.loadSelectedIdx >= 0 && _chainEditState.loadSelectedIdx < _chainEditState.chainNames.Count)
                {
                    string pick = _chainEditState.chainNames[_chainEditState.loadSelectedIdx];
                    _chainEditState.Open(_chainEditState.chainNames, pick);
                    _chainEditState.errorMsg = $"Loaded: {pick}";
                }
                else { _chainEditState.errorMsg = "Select a chain first"; }
            }
            if (GUI.Button(new Rect(p + 294, y, 60, 26), "New", closeButtonStyle))
            {
                _chainEditState.entries.Clear();
                _chainEditState.editingName = "NewChain";
                _chainEditState.saveAsName = "NewChain";
                _chainEditState.editingExisting = false;
                _chainEditState.errorMsg = null;
            }
            y += 32f;

            // ---- Row 2: chain name + Save As name ----
            GUI.Label(new Rect(p, y + 3, 82, 22), "Chain Name:", labelStyle);
            _chainEditState.editingName = GUI.TextField(new Rect(p + 86, y, 150, 26), _chainEditState.editingName, textFieldStyle);
            GUI.Label(new Rect(p + 244, y + 3, 56, 22), "Save As:", labelStyle);
            _chainEditState.saveAsName = GUI.TextField(new Rect(p + 302, y, 150, 26), _chainEditState.saveAsName, textFieldStyle);
            y += 32f;

            // ---- Status / error ----
            if (_chainEditState.errorMsg != null)
                GUI.Label(new Rect(p, y, W - p * 2, 20), _chainEditState.errorMsg, logTextStyle);
            y += 22f;

            // ---- Entries header ----
            GUI.Label(new Rect(p, y, W - p * 2, 18), "Entries:   qid | area | frame | pad | iters | -", labelStyle);
            y += 20f;

            // ---- Entries scroll list ----
            float entrH = H - y - 44f;
            _chainEditState.scroll = GUI.BeginScrollView(
                new Rect(p, y, W - p * 2, entrH),
                _chainEditState.scroll,
                new Rect(0, 0, W - p * 2 - 18, Mathf.Max(entrH - 4, _chainEditState.entries.Count * 32 + 36)));
            for (int i = 0; i < _chainEditState.entries.Count; i++)
            {
                QuestChains.Entry ent = _chainEditState.entries[i];
                float ey = i * 32f;
                string sqid = GUI.TextField(new Rect(0, ey, 50, 26), ent.qid.ToString(), textFieldStyle); int.TryParse(sqid, out ent.qid);
                string sarea = GUI.TextField(new Rect(56, ey, 78, 26), ent.area ?? "", textFieldStyle); ent.area = sarea;
                string sframe = GUI.TextField(new Rect(140, ey, 68, 26), ent.frame ?? "", textFieldStyle); ent.frame = sframe;
                string spad = GUI.TextField(new Rect(214, ey, 58, 26), ent.pad ?? "Spawn", textFieldStyle); ent.pad = spad;
                string sitems = GUI.TextField(new Rect(278, ey, 38, 26), ent.items.ToString(), textFieldStyle); int itemsval = ent.items; int.TryParse(sitems, out itemsval); ent.items = itemsval < 1 ? 1 : itemsval;
                if (GUI.Button(new Rect(322, ey, 28, 26), "-", closeButtonStyle)) { _chainEditState.entries.RemoveAt(i); break; }
                _chainEditState.entries[i] = ent;
            }
            if (GUI.Button(new Rect(0, _chainEditState.entries.Count * 32f, 28, 26), "+", closeButtonStyle))
                _chainEditState.entries.Add(new QuestChains.Entry { qid = 1, area = "", frame = "", pad = "Spawn", items = 1 });
            GUI.EndScrollView();
            y += entrH + 6f;

            // ---- Bottom buttons: Save / Save As / Delete / Export / Import / Close ----
            float bw = 72f;
            if (GUI.Button(new Rect(p, y, bw, 28), _chainEditState.editingExisting ? "Update" : "Save", closeButtonStyle)) SaveEditedChain(false);
            if (GUI.Button(new Rect(p + bw + 4, y, bw, 28), "Save As", closeButtonStyle)) SaveEditedChain(true);
            if (_chainEditState.editingExisting)
                if (GUI.Button(new Rect(p + bw * 2 + 8, y, bw, 28), "Delete", closeButtonStyle)) DeleteEditedChain();
            if (GUI.Button(new Rect(p + bw * 3 + 12, y, bw, 28), "Export", closeButtonStyle)) ExportChain();
            if (GUI.Button(new Rect(p + bw * 4 + 16, y, bw, 28), "Import", closeButtonStyle)) ImportChain();
            if (GUI.Button(new Rect(W - p - 58, y, 58, 28), "Close", closeButtonStyle)) _showChainEditor = false;

            // ---- Load dropdown (drawn on top of everything else) ----
            if (_chainEditState.showLoadDropdown && _chainEditState.chainNames.Count > 0)
            {
                float ddY = 56f;
                float ddH = Mathf.Min(_chainEditState.chainNames.Count * 24f + 4, 180f);
                GUI.Box(new Rect(p + 40, ddY - 2, 184, ddH + 4), "");
                _chainEditState.loadDropScroll = GUI.BeginScrollView(
                    new Rect(p + 42, ddY, 180, ddH),
                    _chainEditState.loadDropScroll,
                    new Rect(0, 0, 160, _chainEditState.chainNames.Count * 24f));
                for (int ci = 0; ci < _chainEditState.chainNames.Count; ci++)
                {
                    bool sel = ci == _chainEditState.loadSelectedIdx;
                    if (GUI.Button(new Rect(0, ci * 24f, 158, 22), _chainEditState.chainNames[ci], sel ? labelStyle : rowButtonStyle))
                    {
                        _chainEditState.loadSelectedIdx = ci;
                        _chainEditState.showLoadDropdown = false;
                    }
                }
                GUI.EndScrollView();
            }

            GUI.DragWindow(Util.ResizableWindow.TitleBarDragRect(W, 26f));
        }

        private static void SaveEditedChain(bool saveAs)
        {
            try
            {
                string nm = (saveAs ? _chainEditState.saveAsName : _chainEditState.editingName)?.Trim();
                if (string.IsNullOrEmpty(nm)) { _chainEditState.errorMsg = saveAs ? "Save As name required" : "Chain name required"; return; }
                if (_chainEditState.entries.Count == 0) { _chainEditState.errorMsg = "Add at least 1 entry"; return; }
                foreach (QuestChains.Entry e in _chainEditState.entries)
                    if (e.qid <= 0) { _chainEditState.errorMsg = "qid must be a positive number"; return; }

                string userDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(userDir);
                string chainFile = System.IO.Path.Combine(userDir, "chains.json");

                // Read existing user file as JObject so we preserve unknown keys / comments
                Newtonsoft.Json.Linq.JObject root;
                if (System.IO.File.Exists(chainFile))
                    root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(chainFile));
                else
                    root = new Newtonsoft.Json.Linq.JObject();

                root[nm] = EntriesToJArray(_chainEditState.entries);
                System.IO.File.WriteAllText(chainFile,
                    root.ToString(Newtonsoft.Json.Formatting.Indented));

                QuestChains.Init();

                // Refresh editor state
                _chainEditState.editingName = nm;
                _chainEditState.saveAsName = nm;
                _chainEditState.editingExisting = true;
                _chainEditState.chainNames = new List<string>(QuestChains.Names);
                _chainEditState.loadSelectedIdx = _chainEditState.chainNames.IndexOf(nm);
                _chainEditState.errorMsg = saveAs ? $"Saved as: {nm}" : $"Saved: {nm}";
            }
            catch (System.Exception ex)
            {
                _chainEditState.errorMsg = ex.Message;
            }
        }

        private static void DeleteEditedChain()
        {
            try
            {
                string userDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
                string chainFile = System.IO.Path.Combine(userDir, "chains.json");
                if (!System.IO.File.Exists(chainFile)) { _chainEditState.errorMsg = "User chains.json not found"; return; }

                JObject root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(chainFile));
                if (root.Remove(_chainEditState.editingName))
                {
                    System.IO.File.WriteAllText(chainFile, root.ToString(Newtonsoft.Json.Formatting.Indented));
                    QuestChains.Init();
                    _chainEditState.chainNames = new List<string>(QuestChains.Names);
                    _chainEditState.loadSelectedIdx = _chainEditState.chainNames.Count > 0 ? 0 : -1;
                    _chainEditState.editingExisting = false;
                    _chainEditState.errorMsg = "Deleted!";
                }
                else { _chainEditState.errorMsg = "Not found in user file (bootstrap-only chain can't be deleted here)"; }
            }
            catch (System.Exception ex)
            {
                _chainEditState.errorMsg = ex.Message;
            }
        }

        // Export the currently loaded entries as a standalone .json preset file
        private static void ExportChain()
        {
            try
            {
                string nm = _chainEditState.editingName?.Trim();
                if (string.IsNullOrEmpty(nm)) { _chainEditState.errorMsg = "Set a chain name before exporting"; return; }
                if (_chainEditState.entries.Count == 0) { _chainEditState.errorMsg = "Nothing to export"; return; }

                string defaultDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(defaultDir);
                string path = ShowSaveFileDialog(defaultDir, nm + ".json");
                if (path == null) return;  // user cancelled

                // Ensure .json extension
                if (!path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                    path += ".json";

                JObject obj = new();
                obj[nm] = EntriesToJArray(_chainEditState.entries);
                System.IO.File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
                _chainEditState.errorMsg = $"Exported to: {System.IO.Path.GetFileName(path)}";
            }
            catch (System.Exception ex)
            {
                _chainEditState.errorMsg = ex.Message;
            }
        }

        // Import a preset .json file — merges all chains found in it into UserData/Beyond/chains.json
        private static void ImportChain()
        {
            try
            {
                string defaultDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(defaultDir);
                string path = ShowOpenFileDialog(defaultDir, "");
                if (path == null) return;  // user cancelled

                string imported = System.IO.File.ReadAllText(path);
                JObject importObj = Newtonsoft.Json.Linq.JObject.Parse(imported);

                string chainFile = System.IO.Path.Combine(defaultDir, "chains.json");
                Newtonsoft.Json.Linq.JObject root;
                if (System.IO.File.Exists(chainFile))
                    root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(chainFile));
                else
                    root = new Newtonsoft.Json.Linq.JObject();

                int count = 0;
                string lastName = null;
                foreach (JProperty prop in importObj.Properties())
                {
                    if (prop.Name.StartsWith("_")) continue;
                    if (prop.Value is not Newtonsoft.Json.Linq.JArray) continue;
                    root[prop.Name] = prop.Value;
                    lastName = prop.Name;
                    count++;
                }
                if (count == 0) { _chainEditState.errorMsg = "No valid chains found in file"; return; }

                System.IO.File.WriteAllText(chainFile, root.ToString(Newtonsoft.Json.Formatting.Indented));
                QuestChains.Init();

                _chainEditState.chainNames = new List<string>(QuestChains.Names);
                _chainEditState.loadSelectedIdx = lastName != null ? _chainEditState.chainNames.IndexOf(lastName) : 0;

                // Auto-load the last imported chain into the editor
                if (lastName != null) _chainEditState.Open(_chainEditState.chainNames, lastName);
                _chainEditState.errorMsg = $"Imported {count} chain(s) from {System.IO.Path.GetFileName(path)}";
            }
            catch (System.Exception ex)
            {
                _chainEditState.errorMsg = ex.Message;
            }
        }

        // Shared helper: List<Entry> -> JArray
        private static Newtonsoft.Json.Linq.JArray EntriesToJArray(List<QuestChains.Entry> entries)
        {
            JArray arr = new();
            foreach (QuestChains.Entry ent in entries)
            {
                JObject o = new();
                o["qid"] = ent.qid;
                o["area"] = ent.area ?? "";
                o["frame"] = ent.frame ?? "";
                o["pad"] = string.IsNullOrEmpty(ent.pad) ? "Spawn" : ent.pad;
                o["items"] = ent.items < 1 ? 1 : ent.items;
                arr.Add(o);
            }
            return arr;
        }
        // True when the user has any text input field (chat, search box,
        // etc) focused. Used to gate single-letter hotkeys so typing in
        // chat doesn't flip every toggle on every keypress. Covers both
        // legacy UnityEngine.UI.InputField and TMP_InputField — the TMP
        // check is by type name to avoid a hard reference if the game
        // ever swaps it out.
        public static bool IsTypingInChat()
        {
            try
            {
                EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                if (es == null) return false;
                GameObject sel = es.currentSelectedGameObject;
                if (sel == null) return false;
                if (sel.GetComponent<UnityEngine.UI.InputField>() != null) return true;
                foreach (MonoBehaviour c in sel.GetComponents<UnityEngine.MonoBehaviour>())
                {
                    if (c == null) continue;
                    string n = c.GetType().Name;
                    if (n == "TMP_InputField" || n == "TMPro_InputField") return true;
                }
            }
            catch { }
            return false;
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

            if (showWindow && showRetroTestsWindow && retroTestsWindowRect.Contains(imguiMousePos))
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

        private static bool IsSkillOnCooldown(SkillSlotButton button)
        {
            if (button == null) return false;
            try
            {
                // Check pendingCooldown first
                System.Reflection.FieldInfo pendingField = typeof(SkillSlotButton).GetField("pendingCooldown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pendingField != null && (bool)pendingField.GetValue(button))
                {
                    return true;
                }

                // Check CooldownOverlay
                System.Reflection.FieldInfo cdField = typeof(SkillSlotButton).GetField("cooldown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cdField != null)
                {
                    object cdObj = cdField.GetValue(button);
                    if (cdObj != null)
                    {
                        System.Reflection.MethodInfo method = cdObj.GetType().GetMethod("cooldownActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (method != null)
                        {
                            return (bool)method.Invoke(cdObj, null);
                        }

                        System.Reflection.FieldInfo remainField = cdObj.GetType().GetField("cdRemain", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (remainField != null)
                        {
                            float remain = (float)remainField.GetValue(cdObj);
                            return remain > 0f;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        #region Win32 File Dialogs
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public class OpenFileName
        {
            public int lStructSize = 0;
            public System.IntPtr hwndOwner = System.IntPtr.Zero;
            public System.IntPtr hInstance = System.IntPtr.Zero;
            public string lpstrFilter = null;
            public string lpstrCustomFilter = null;
            public int nMaxCustFilter = 0;
            public int nFilterIndex = 0;
            public string lpstrFile = null;
            public int nMaxFile = 0;
            public string lpstrFileTitle = null;
            public int nMaxFileTitle = 0;
            public string lpstrInitialDir = null;
            public string lpstrTitle = null;
            public int Flags = 0;
            public short nFileOffset = 0;
            public short nFileExtension = 0;
            public string lpstrDefExt = null;
            public System.IntPtr lCustData = System.IntPtr.Zero;
            public System.IntPtr lpfnHook = System.IntPtr.Zero;
            public string lpTemplateName = null;
            public System.IntPtr pvReserved = System.IntPtr.Zero;
            public int dwReserved = 0;
            public int FlagsEx = 0;
        }

        [System.Runtime.InteropServices.DllImport("comdlg32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool GetOpenFileName([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] OpenFileName ofn);

        [System.Runtime.InteropServices.DllImport("comdlg32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool GetSaveFileName([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] OpenFileName ofn);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr GetActiveWindow();

        private static string ShowOpenFileDialog(string defaultDir, string defaultFilename)
        {
            OpenFileName ofn = new();
            ofn.lStructSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
            ofn.lpstrFilter = "Text Files (*.txt)\0*.txt\0All Files (*.*)\0*.*\0\0";

            string initialFile = defaultFilename;
            if (string.IsNullOrEmpty(initialFile))
            {
                initialFile = "";
            }
            char[] chars = new char[512];
            initialFile.CopyTo(0, chars, 0, System.Math.Min(initialFile.Length, chars.Length - 1));
            ofn.lpstrFile = new string(chars);
            ofn.nMaxFile = chars.Length;

            ofn.lpstrInitialDir = defaultDir;
            ofn.lpstrTitle = "Select Skillset File";
            ofn.hwndOwner = GetActiveWindow();

            // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            ofn.Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;

            if (GetOpenFileName(ofn))
            {
                int nullIdx = ofn.lpstrFile.IndexOf('\0');
                if (nullIdx >= 0)
                {
                    return ofn.lpstrFile.Substring(0, nullIdx);
                }
                return ofn.lpstrFile;
            }
            return null;
        }

        private static string ShowSaveFileDialog(string defaultDir, string defaultFilename)
        {
            OpenFileName ofn = new();
            ofn.lStructSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
            ofn.lpstrFilter = "Text Files (*.txt)\0*.txt\0All Files (*.*)\0*.*\0\0";

            string initialFile = defaultFilename;
            if (string.IsNullOrEmpty(initialFile))
            {
                initialFile = "skillset.txt";
            }
            char[] chars = new char[512];
            initialFile.CopyTo(0, chars, 0, System.Math.Min(initialFile.Length, chars.Length - 1));
            ofn.lpstrFile = new string(chars);
            ofn.nMaxFile = chars.Length;

            ofn.lpstrInitialDir = defaultDir;
            ofn.lpstrTitle = "Save Skillset File As";
            ofn.hwndOwner = GetActiveWindow();
            ofn.lpstrDefExt = "txt";

            // OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            ofn.Flags = 0x00080000 | 0x00000002 | 0x00000800 | 0x00000008;

            if (GetSaveFileName(ofn))
            {
                int nullIdx = ofn.lpstrFile.IndexOf('\0');
                if (nullIdx >= 0)
                {
                    return ofn.lpstrFile.Substring(0, nullIdx);
                }
                return ofn.lpstrFile;
            }
            return null;
        }
        #endregion
    }
}
