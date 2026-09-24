using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BeyondAgent.Util
{
    // Windows key state for KeyboardBridge, read with GetAsyncKeyState so it
    // doesn't depend on Raw Input or legacy UnityEngine.Input, neither of which
    // reliably sees keys while the game is embedded in the launcher.
    //
    // GetAsyncKeyState reads the physical keyboard whatever window has focus,
    // so keys only count while keyboard focus is on one of the game's own
    // windows - typing in the launcher or another app never reaches the game.
    //
    // Input System Keys are physical positions (Key.W is the key where W sits
    // on US QWERTY, whatever the layout prints on it). Character keys are
    // therefore mapped by scan code through the active layout, so an AZERTY
    // user gets the same Key the Input System would have produced natively.
    internal static class Win32Keys
    {
        public static readonly bool Supported = Application.platform == RuntimePlatform.WindowsPlayer;

        private static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private static readonly Dictionary<Key, int> Vk = new();
        private static IntPtr _layout = new(-1);
        private static bool _focused;

        public static IEnumerable<Key> Keys => Vk.Keys;

        // Once per frame, before IsDown: follows keyboard layout switches and
        // returns whether the game currently has keyboard focus.
        public static bool Refresh()
        {
            IntPtr layout = GetKeyboardLayout(0);
            if (layout != _layout)
            {
                _layout = layout;
                Build(layout);
                BeyondLog.Msg($"[Win32Keys] key map built for layout HKL=0x{layout.ToInt64():X8}: {Vk.Count} keys");
            }

            IntPtr focus = GetFocus();
            bool focused = focus != IntPtr.Zero && GetWindowThreadProcessId(focus, out uint pid) != 0 && pid == OwnPid;
            if (focused != _focused)
            {
                _focused = focused;
                BeyondLog.Verbose($"[Win32Keys] game keyboard focus {(focused ? "gained" : "lost")} (focus=0x{focus.ToInt64():X})");
            }
            return focused;
        }

        public static bool IsDown(Key key)
        {
            return Vk.TryGetValue(key, out int vk) && (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private static void Build(IntPtr layout)
        {
            Vk.Clear();

            // Character keys: US-QWERTY position (scan code) -> whatever VK the
            // active layout puts there.
            MapRow("QWERTYUIOP", 0x10, layout);
            MapRow("ASDFGHJKL", 0x1E, layout);
            MapRow("ZXCVBNM", 0x2C, layout);
            for (int i = 1; i <= 9; i++)
            {
                MapScan((Key)Enum.Parse(typeof(Key), "Digit" + i), (uint)(0x01 + i), layout);
            }
            MapScan(Key.Digit0, 0x0B, layout);
            MapScan(Key.Minus, 0x0C, layout);
            MapScan(Key.Equals, 0x0D, layout);
            MapScan(Key.LeftBracket, 0x1A, layout);
            MapScan(Key.RightBracket, 0x1B, layout);
            MapScan(Key.Semicolon, 0x27, layout);
            MapScan(Key.Quote, 0x28, layout);
            MapScan(Key.Backquote, 0x29, layout);
            MapScan(Key.Backslash, 0x2B, layout);
            MapScan(Key.Comma, 0x33, layout);
            MapScan(Key.Period, 0x34, layout);
            MapScan(Key.Slash, 0x35, layout);

            // Everything else has the same VK on every layout. NumpadEnter is
            // left out: it shares VK_RETURN with Enter, so it reads as Enter.
            Vk[Key.Escape] = 0x1B;
            Vk[Key.Backspace] = 0x08;
            Vk[Key.Tab] = 0x09;
            Vk[Key.Enter] = 0x0D;
            Vk[Key.Space] = 0x20;
            Vk[Key.LeftShift] = 0xA0;
            Vk[Key.RightShift] = 0xA1;
            Vk[Key.LeftCtrl] = 0xA2;
            Vk[Key.RightCtrl] = 0xA3;
            Vk[Key.LeftAlt] = 0xA4;
            Vk[Key.RightAlt] = 0xA5;
            Vk[Key.LeftMeta] = 0x5B;
            Vk[Key.RightMeta] = 0x5C;
            Vk[Key.ContextMenu] = 0x5D;
            Vk[Key.CapsLock] = 0x14;
            Vk[Key.NumLock] = 0x90;
            Vk[Key.ScrollLock] = 0x91;
            Vk[Key.Pause] = 0x13;
            Vk[Key.PrintScreen] = 0x2C;
            Vk[Key.PageUp] = 0x21;
            Vk[Key.PageDown] = 0x22;
            Vk[Key.End] = 0x23;
            Vk[Key.Home] = 0x24;
            Vk[Key.LeftArrow] = 0x25;
            Vk[Key.UpArrow] = 0x26;
            Vk[Key.RightArrow] = 0x27;
            Vk[Key.DownArrow] = 0x28;
            Vk[Key.Insert] = 0x2D;
            Vk[Key.Delete] = 0x2E;
            for (int i = 0; i <= 9; i++)
            {
                Vk[(Key)Enum.Parse(typeof(Key), "Numpad" + i)] = 0x60 + i;
            }
            Vk[Key.NumpadMultiply] = 0x6A;
            Vk[Key.NumpadPlus] = 0x6B;
            Vk[Key.NumpadMinus] = 0x6D;
            Vk[Key.NumpadPeriod] = 0x6E;
            Vk[Key.NumpadDivide] = 0x6F;
            for (int i = 1; i <= 24; i++)
            {
                Vk[(Key)Enum.Parse(typeof(Key), "F" + i)] = 0x6F + i;
            }
        }

        private static void MapRow(string letters, uint firstScan, IntPtr layout)
        {
            for (int i = 0; i < letters.Length; i++)
            {
                MapScan((Key)Enum.Parse(typeof(Key), letters[i].ToString()), firstScan + (uint)i, layout);
            }
        }

        private static void MapScan(Key key, uint scan, IntPtr layout)
        {
            uint vk = MapVirtualKeyEx(scan, MAPVK_VSC_TO_VK, layout);
            if (vk != 0)
            {
                Vk[key] = (int)vk;
            }
            else
            {
                BeyondLog.Warning($"[Win32Keys] layout has no key at scan 0x{scan:X2} (Key.{key}) - that bind stays dead");
            }
        }

        private const uint MAPVK_VSC_TO_VK = 1;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);
    }
}
