using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BeyondAgent.Util
{
    // Windows delivers Raw Input (WM_INPUT) only to the foreground window. The
    // launcher hosts the game as a child of its own window, so the game window
    // is never the foreground one, and Unity feeds the new Input System from
    // Raw Input ("<RI> Initializing input." in Player.log). Result: inside the
    // launcher Keyboard.current never sees a key, and every game bind that
    // polls it is dead - Enter-to-chat, WASD, skills 1-6.
    //
    // WM_KEYDOWN still reaches the focused child window, but legacy
    // UnityEngine.Input can't be relied on to see it either: on some machines
    // it reads nothing while WM_CHAR text still arrives (user logs,
    // 2026-09-24: textChars>0 with zero bridge events). So on Windows the key
    // state comes straight from Win32 (Win32Keys below), gated on the game
    // window having keyboard focus; legacy Input remains the source elsewhere.
    //
    // Either way, bridge it into the Input System: read the key state each
    // frame and queue it as a keyboard state event. One event feeds every consumer at once -
    // Keyboard.current polling, InputActions, the UI module - rather than
    // patching each call site in the game.
    //
    // Needs BeyondLifecycle's runInBackground + IgnoreFocus settings to stick:
    // without them InputManager.ShouldFlushEventBuffer drops the whole event
    // buffer each update while the game reads as unfocused, ours included.
    //
    // Hopefully, this shouldn't change again from here on out, but we can't
    // confirm that. Thanks, AE. Very cool. - retrograde.
    internal static class KeyboardBridge
    {
        // Key and KeyCode agree on almost every name. These are the families
        // that don't, plus Numpad->Keypad and Digit->Alpha handled in BuildMap.
        private static readonly Dictionary<string, string> Renames = new()
        {
            ["Enter"] = "Return",
            ["LeftCtrl"] = "LeftControl",
            ["RightCtrl"] = "RightControl",
            ["LeftMeta"] = "LeftWindows",
            ["RightMeta"] = "RightWindows",
            ["ContextMenu"] = "Menu",
            ["PrintScreen"] = "Print",
        };

        private static readonly Dictionary<Key, KeyCode> Keys = BuildMap();

        // Previous frame's pressed set as a bitfield, so we can tell "changed"
        // without reading KeyboardState's fixed buffer (that needs unsafe).
        private static ulong _lastLo;
        private static ulong _lastHi;

        // Diagnostics (see InputDiagnostics): number of state events queued so
        // far, and a pending check that the last one reached Keyboard.current.
        public static int QueuedCount { get; private set; }
        private static bool _verifyPending;
        private static bool _loggedNoKeyboard;

        public static void Tick()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                if (!_loggedNoKeyboard)
                {
                    BeyondLog.Warning("[KeyboardBridge] Keyboard.current is null - nothing to feed, all game binds dead until a keyboard device appears");
                    _loggedNoKeyboard = true;
                }
                return;
            }
            if (_loggedNoKeyboard)
            {
                BeyondLog.Msg("[KeyboardBridge] Keyboard.current is back: " + InputDiagnostics.Describe(keyboard));
                _loggedNoKeyboard = false;
            }

            if (_verifyPending)
            {
                _verifyPending = false;
                VerifyDelivered(keyboard);
            }

            bool win32 = Win32Keys.Supported;
            bool focused = true;
            if (win32)
            {
                focused = Win32Keys.Refresh();
                // Unfocused: report nothing held, which also releases any key
                // that was down when focus left (no stuck WASD).
                if (!focused && _lastLo == 0 && _lastHi == 0)
                {
                    return;
                }
            }
            else
            {
                // A key pressed and released between two frames is invisible to
                // GetKey, so the bridge never forwards it. Log it so short taps
                // being dropped shows up (low frame rates make this likelier).
                if (Input.anyKeyDown)
                {
                    LogSubFrameTaps();
                }

                // Idle fast path: nothing held now and nothing held last frame.
                if (!Input.anyKey && _lastLo == 0 && _lastHi == 0)
                {
                    return;
                }
            }

            KeyboardState state = default;
            ulong lo = 0;
            ulong hi = 0;
            foreach (Key key in ActiveKeys)
            {
                bool down = win32 ? focused && Win32Keys.IsDown(key) : Input.GetKey(Keys[key]);
                if (!down)
                {
                    continue;
                }

                state.Press(key);
                int bit = (int)key;
                if (bit < 64)
                {
                    lo |= 1UL << bit;
                }
                else
                {
                    hi |= 1UL << (bit - 64);
                }
            }

            // Queue only on change. wasPressedThisFrame is the edge between two
            // consecutive states, and a per-frame stream would also stomp real
            // Raw Input when the game runs outside the launcher.
            if (lo == _lastLo && hi == _lastHi)
            {
                return;
            }

            LogEdges(_lastLo, _lastHi, lo, hi);
            _lastLo = lo;
            _lastHi = hi;
            InputSystem.QueueStateEvent(keyboard, state);
            QueuedCount++;
            _verifyPending = true;
        }

        private static IEnumerable<Key> ActiveKeys => Win32Keys.Supported ? Win32Keys.Keys : Keys.Keys;

        private static bool IsSet(ulong lo, ulong hi, Key key)
        {
            int bit = (int)key;
            return bit < 64 ? (lo & (1UL << bit)) != 0 : (hi & (1UL << (bit - 64))) != 0;
        }

        private static void LogEdges(ulong oldLo, ulong oldHi, ulong lo, ulong hi)
        {
            bool redact = InputDiagnostics.ShouldRedactKeys;
            System.Text.StringBuilder sb = new();
            int down = 0;
            int up = 0;
            int held = 0;
            foreach (Key key in ActiveKeys)
            {
                bool was = IsSet(oldLo, oldHi, key);
                bool now = IsSet(lo, hi, key);
                if (now)
                {
                    held++;
                }
                if (was == now)
                {
                    continue;
                }
                if (now) { down++; } else { up++; }
                if (!redact)
                {
                    sb.Append(now ? " +" : " -").Append(key);
                }
            }

            string keys = redact ? $" +{down} -{up} (key names hidden: text field focused or not in game)" : sb.ToString();
            BeyondLog.Verbose($"[KeyboardBridge] queue #{QueuedCount + 1}:{keys}; {held} held");
        }

        // Runs the frame after a queue: the Input System has processed the
        // event by now, so Keyboard.current should match what we sent. A
        // mismatch means the event was dropped (focus flush, disabled device)
        // or something else wrote conflicting state.
        private static void VerifyDelivered(Keyboard keyboard)
        {
            bool redact = InputDiagnostics.ShouldRedactKeys;
            int mismatches = 0;
            System.Text.StringBuilder sb = new();
            foreach (Key key in ActiveKeys)
            {
                bool expected = IsSet(_lastLo, _lastHi, key);
                bool actual;
                try { actual = keyboard[key].isPressed; }
                catch { continue; }
                if (expected == actual)
                {
                    continue;
                }
                mismatches++;
                if (!redact)
                {
                    sb.Append($" {key}(sent {(expected ? "down" : "up")}, reads {(actual ? "down" : "up")})");
                }
            }

            if (mismatches == 0)
            {
                BeyondLog.Verbose($"[KeyboardBridge] queue #{QueuedCount} delivered: Keyboard.current matches");
            }
            else
            {
                BeyondLog.Warning($"[KeyboardBridge] queue #{QueuedCount} NOT reflected in Keyboard.current, {mismatches} key(s) differ:{sb}; " +
                                  $"kbd enabled={keyboard.enabled} appFocused={Application.isFocused} bg={InputSystem.settings.backgroundBehavior}");
            }
        }

        private static void LogSubFrameTaps()
        {
            bool redact = InputDiagnostics.ShouldRedactKeys;
            int count = 0;
            System.Text.StringBuilder sb = new();
            foreach (KeyValuePair<Key, KeyCode> pair in Keys)
            {
                if (Input.GetKeyDown(pair.Value) && !Input.GetKey(pair.Value))
                {
                    count++;
                    if (!redact)
                    {
                        sb.Append(' ').Append(pair.Key);
                    }
                }
            }
            if (count > 0)
            {
                BeyondLog.Warning($"[KeyboardBridge] {count} key tap(s) shorter than one frame, never forwarded:{(redact ? " (names hidden)" : sb.ToString())}");
            }
        }

        private static Dictionary<Key, KeyCode> BuildMap()
        {
            Dictionary<Key, KeyCode> map = [];
            foreach (Key key in (Key[])Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None)
                {
                    continue;
                }

                string name = key.ToString();
                if (name.StartsWith("Numpad", StringComparison.Ordinal))
                {
                    name = "Keypad" + name.Substring(6);
                }
                else if (name.StartsWith("Digit", StringComparison.Ordinal))
                {
                    name = "Alpha" + name.Substring(5);
                }
                else if (Renames.TryGetValue(name, out string renamed))
                {
                    name = renamed;
                }

                // Keys with no legacy equivalent (OEM1-5, F16+) just drop out.
                if (Enum.TryParse(name, true, out KeyCode code) && code != KeyCode.None)
                {
                    map[key] = code;
                }
            }

            // Self-check: one key per naming family (plain, rename,
            // Digit->Alpha, Numpad->Keypad). A miss means the enums drifted.
            foreach (Key probe in new[] { Key.W, Key.Enter, Key.Digit1, Key.NumpadEnter })
            {
                if (!map.ContainsKey(probe))
                {
                    BeyondLog.Error($"[KeyboardBridge] no KeyCode for Key.{probe} — binds using it stay dead");
                }
            }
            BeyondLog.Verbose($"[KeyboardBridge] mapped {map.Count} Input System keys to legacy KeyCodes");

            return map;
        }
    }
}
