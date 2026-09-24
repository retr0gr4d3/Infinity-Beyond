using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BeyondAgent.Util
{
    // Verbose input diagnostics for "keys don't work on my machine" reports.
    // Everything goes to BeyondLog's file (UserData/Beyond/logs). It logs:
    //  - a startup dump: which BeyondAgent.dll actually loaded, OS, keyboard
    //    layout, Input System settings, devices, whether legacy Input works;
    //  - every change of focus / game input gating (Unity + Win32 view);
    //  - a heartbeat every few seconds with event counts, which tell us whether
    //    native Raw Input is arriving at all or only the KeyboardBridge's
    //    events are;
    //  - device add/remove.
    // KeyboardBridge logs the per-key side (edges, queued states, and whether
    // the game's Keyboard.current actually picked them up).
    internal static class InputDiagnostics
    {
        private const float HeartbeatSeconds = 5f;

        private static float _nextHeartbeat;
        private static int _heartbeatFrames;
        private static string _lastState;

        // Counted in InputSystem.onEvent; reset every heartbeat.
        private static int _keyboardEvents;
        private static int _mouseEvents;
        private static int _otherEvents;
        private static int _textChars;
        private static int _imeChanges;
        private static int _bridgeQueuedAtLastHeartbeat;
        private static Keyboard _textHookedKeyboard;

        private static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private static readonly bool IsWindows =
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        // Key names are only logged while playing: no text field focused and a
        // player spawned (the login screen has none, so its password field is
        // covered too). Otherwise the log would be a keylogger.
        public static bool ShouldRedactKeys
        {
            get
            {
                try
                {
                    return Entity.mainPlayer == null || global::InputManager.IsTextInputFocused();
                }
                catch
                {
                    return true;
                }
            }
        }

        public static void Start()
        {
            DumpEnvironment();

            InputSystem.onDeviceChange += (device, change) =>
            {
                BeyondLog.Msg($"[InputDiag] device {change}: {Describe(device)}");
                if (device is Keyboard && change == InputDeviceChange.Added)
                {
                    HookTextInput();
                }
            };

            InputSystem.onEvent += new Action<InputEventPtr, InputDevice>(OnInputEvent);

            Application.focusChanged += focused =>
                BeyondLog.Msg($"[InputDiag] Application.focusChanged -> {focused}; {Win32FocusState()}");

            HookTextInput();
        }

        public static void Tick()
        {
            _heartbeatFrames++;

            // Log any change of the things that gate input, the frame it happens.
            string state = GatingState();
            if (state != _lastState)
            {
                BeyondLog.Verbose("[InputDiag] input gating changed: " + state);
                _lastState = state;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextHeartbeat)
            {
                return;
            }

            float elapsed = HeartbeatSeconds + (now - _nextHeartbeat);
            if (_nextHeartbeat > 0f)
            {
                int bridgeQueued = KeyboardBridge.QueuedCount - _bridgeQueuedAtLastHeartbeat;
                // Keyboard events beyond what the bridge queued came from the
                // OS directly (Raw Input). 0 native events while keys were in
                // use means the bridge is the only thing feeding the game.
                int native = _keyboardEvents - bridgeQueued;
                BeyondLog.Verbose(
                    $"[InputDiag] heartbeat: fps={_heartbeatFrames / elapsed:0.0} " +
                    $"kbdEvents={_keyboardEvents} (bridge={bridgeQueued}, native={native}) " +
                    $"mouseEvents={_mouseEvents} otherEvents={_otherEvents} textChars={_textChars} imeChanges={_imeChanges} | " +
                    state);
            }

            _bridgeQueuedAtLastHeartbeat = KeyboardBridge.QueuedCount;
            _keyboardEvents = _mouseEvents = _otherEvents = _textChars = _imeChanges = 0;
            _heartbeatFrames = 0;
            _nextHeartbeat = now + HeartbeatSeconds;
        }

        private static void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
            {
                return;
            }

            if (device is Keyboard)
            {
                _keyboardEvents++;
            }
            else if (device is Mouse)
            {
                _mouseEvents++;
            }
            else
            {
                _otherEvents++;
            }
        }

        private static void HookTextInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || keyboard == _textHookedKeyboard)
            {
                return;
            }

            // Counts only; the characters themselves are never logged.
            keyboard.onTextInput += _ => _textChars++;
            keyboard.onIMECompositionChange += _ => _imeChanges++;
            _textHookedKeyboard = keyboard;
            BeyondLog.Verbose("[InputDiag] text input hooked on " + Describe(keyboard));
        }

        private static string GatingState()
        {
            StringBuilder sb = new();
            sb.Append("appFocused=").Append(Application.isFocused);
            sb.Append(" runInBackground=").Append(Application.runInBackground);
            try { sb.Append(" bgBehavior=").Append(InputSystem.settings.backgroundBehavior); } catch (Exception ex) { sb.Append(" bgBehavior=<" + ex.GetType().Name + ">"); }
            sb.Append(" kbd=").Append(Keyboard.current == null ? "null" : (Keyboard.current.enabled ? "enabled" : "DISABLED"));
            sb.Append(" mouse=").Append(Mouse.current == null ? "null" : (Mouse.current.enabled ? "enabled" : "DISABLED"));
            try
            {
                sb.Append(" player=").Append(Entity.mainPlayer != null);
                if (Entity.mainPlayer != null)
                {
                    sb.Append(" playerState=").Append(Entity.mainPlayer.currentState);
                }
                sb.Append(" gameInputEnabled=").Append(global::InputManager.InputEnabled);
                sb.Append(" textFieldFocused=").Append(global::InputManager.IsTextInputFocused());
                sb.Append(" rebinding=").Append(KeyBindings.IsListening);
                if (GameInput.Instance != null)
                {
                    sb.Append(" uiMap=").Append(GameInput.Instance.IsUIMapActive);
                    sb.Append(" playerMap=").Append(GameInput.Instance.IsPlayerMapActive);
                }
                else
                {
                    sb.Append(" gameInput=null");
                }
            }
            catch (Exception ex)
            {
                sb.Append(" gameState=<" + ex.GetType().Name + ": " + ex.Message + ">");
            }
            sb.Append(' ').Append(Win32FocusState());
            return sb.ToString();
        }

        private static void DumpEnvironment()
        {
            BeyondLog.Msg("[InputDiag] ===== environment =====");
            Safe("agent", () =>
            {
                System.Reflection.Assembly asm = typeof(InputDiagnostics).Assembly;
                string loc = asm.Location;
                string stamp = string.IsNullOrEmpty(loc) ? "?" : System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss");
                return $"BeyondAgent {asm.GetName().Version} loaded from '{loc}' (file time {stamp})";
            });
            Safe("game", () => $"{Application.productName} {Application.version}, Unity {Application.unityVersion}, platform {Application.platform}");
            Safe("os", () => $"{SystemInfo.operatingSystem}; {SystemInfo.deviceModel}; {SystemInfo.processorType}; RAM {SystemInfo.systemMemorySize}MB; GPU {SystemInfo.graphicsDeviceName}");
            Safe("commandLine", () => Environment.CommandLine);
            // Only non-secret launcher vars; BEYOND_USER/BEYOND_PASS are never logged.
            Safe("launcherEnv", () => $"BEYOND_PIPE={Environment.GetEnvironmentVariable("BEYOND_PIPE") ?? "<unset>"} BEYOND_LAUNCHER_PID={Environment.GetEnvironmentVariable("BEYOND_LAUNCHER_PID") ?? "<unset>"}");
            Safe("culture", () => $"{System.Globalization.CultureInfo.CurrentCulture.Name} / UI {System.Globalization.CultureInfo.CurrentUICulture.Name}");
            Safe("screen", () => $"{Screen.width}x{Screen.height} mode={Screen.fullScreenMode} dpi={Screen.dpi} targetFps={Application.targetFrameRate} vSync={QualitySettings.vSyncCount}");
            Safe("inputSystem", () =>
            {
                InputSettings s = InputSystem.settings;
                return $"v{InputSystem.version} from '{typeof(InputSystem).Assembly.Location}'; updateMode={s.updateMode} backgroundBehavior={s.backgroundBehavior}";
            });
            Safe("devices", () =>
            {
                StringBuilder sb = new();
                sb.Append(InputSystem.devices.Count).Append(" device(s)");
                foreach (InputDevice d in InputSystem.devices)
                {
                    sb.Append("\n    ").Append(Describe(d));
                }
                return sb.ToString();
            });
            Safe("keyboardLayout", () => Keyboard.current == null ? "<no keyboard>" : $"'{Keyboard.current.keyboardLayout}'");
            if (IsWindows)
            {
                Safe("win32Layout", () => $"HKL=0x{GetKeyboardLayout(0).ToInt64():X8}");
                Safe("win32Focus", Win32FocusState);
            }
            // The bridge relies on legacy Input. If the build had it disabled,
            // GetKey throws InvalidOperationException and every key stays dead.
            Safe("legacyInput", () =>
            {
                bool _ = Input.GetKey(KeyCode.A);
                return "UnityEngine.Input usable";
            });
            Safe("gating", GatingState);
            BeyondLog.Msg("[InputDiag] ===== end environment =====");
        }

        private static void Safe(string label, Func<string> value)
        {
            try
            {
                BeyondLog.Msg($"[InputDiag] {label}: {value()}");
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[InputDiag] {label}: FAILED {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static string Describe(InputDevice d)
        {
            if (d == null)
            {
                return "<null>";
            }
            return $"#{d.deviceId} '{d.name}' layout={d.layout} enabled={d.enabled} native={d.native} " +
                   $"iface='{d.description.interfaceName}' product='{d.description.product}'";
        }

        // Windows' view of focus: which window is foreground (and whose), which
        // one is active and which one has keyboard focus on the game's thread.
        // Keys reach the game only when focus= is a game window.
        internal static string Win32FocusState()
        {
            if (!IsWindows)
            {
                return "win32=n/a";
            }
            try
            {
                IntPtr fg = GetForegroundWindow();
                GetWindowThreadProcessId(fg, out uint fgPid);
                string owner = fgPid == OwnPid
                    ? "game"
                    : fgPid.ToString() == Environment.GetEnvironmentVariable("BEYOND_LAUNCHER_PID") ? "launcher" : "other pid " + fgPid;
                return $"win32 fg={Hwnd(fg)} ({owner}) active={Hwnd(GetActiveWindow())} focus={Hwnd(GetFocus())}";
            }
            catch (Exception ex)
            {
                return "win32=<" + ex.GetType().Name + ">";
            }
        }

        private static string Hwnd(IntPtr h)
        {
            if (h == IntPtr.Zero)
            {
                return "0";
            }
            StringBuilder cls = new(64);
            GetClassName(h, cls, cls.Capacity);
            return $"0x{h.ToInt64():X}[{cls}]";
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    }
}
