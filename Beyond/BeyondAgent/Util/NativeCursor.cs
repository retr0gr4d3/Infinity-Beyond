using System;
using System.Runtime.InteropServices;

namespace Infinity_TestMod.Util
{
    /// <summary>
    /// Win32 native cursor helpers. P/Invoke into user32 is guarded so that on non-Windows
    /// runtimes (Linux/macOS player, Wine without user32, etc.) the calls degrade to a no-op
    /// instead of throwing <see cref="DllNotFoundException"/>.
    /// </summary>
    public static class NativeCursor
    {
        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        // Standard Win32 cursor IDs
        private const int IDC_SIZENWSE = 32642; // ↘↖
        private const int IDC_SIZENESW = 32643; // ↗↙
        private const int IDC_SIZEWE = 32644; // ↔
        private const int IDC_SIZENS = 32645; // ↕
        private const int IDC_ARROW = 32512;

        // Cached after the first probe so we don't pay the exception cost every frame.
        private static readonly bool _available = ProbeAvailability();

        private static bool ProbeAvailability()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;
            try
            {
                // A successful load with IDC_ARROW confirms user32 is actually resolvable.
                _ = LoadCursor(IntPtr.Zero, IDC_ARROW);
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (Exception) { return false; }
        }

        /// <summary>True if user32 cursor APIs are usable on this runtime.</summary>
        public static bool IsSupported => _available;

        private static void TrySet(int cursorId)
        {
            if (!_available) return;
            try { SetCursor(LoadCursor(IntPtr.Zero, cursorId)); }
            catch { /* ignore — cursor is purely cosmetic */ }
        }

        public static void SetNWSE() => TrySet(IDC_SIZENWSE);
        public static void SetNESW() => TrySet(IDC_SIZENESW);
        public static void SetHorizontal() => TrySet(IDC_SIZEWE);
        public static void SetVertical() => TrySet(IDC_SIZENS);
        public static void SetArrow() => TrySet(IDC_ARROW);
    }
}
