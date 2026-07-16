using System;
using System.Runtime.InteropServices;

namespace BeyondAgent.Util
{
    /// <summary>
    /// macOS "overlay-follow" embedding. Windows embeds by handing Unity a parent
    /// HWND to reparent into; macOS has no cross-process window reparenting, so
    /// instead the launcher streams the on-screen rectangle of its tab's host
    /// panel and we — running inside the game process — restyle the game's own
    /// NSWindow to be chromeless and keep it positioned exactly over that panel.
    ///
    /// Keyboard "just works" without the AttachThreadInput dance the Windows path
    /// needs: the game stays a real top-level window the user clicks directly, so
    /// AppKit routes key focus to it natively. We keep NSWindowStyleMaskTitled in
    /// the style mask (a fully borderless NSWindow returns NO from
    /// canBecomeKeyWindow) but make the title bar transparent, hide the title, and
    /// hide the three traffic-light buttons so it reads as borderless.
    ///
    /// All coordinate/DPI math (top-left→bottom-left flip, points conversion) is
    /// done launcher-side, so Apply() receives an AppKit frame (points, bottom-left
    /// origin) and only has to call setFrame:display: — which keeps us to a single
    /// struct-by-value objc_msgSend and avoids the arch-dependent
    /// objc_msgSend_stret needed for struct-returning selectors.
    ///
    /// Must be called on the main thread (Unity's player loop thread == AppKit main
    /// thread); ProcessLauncherCommands satisfies that.
    /// </summary>
    public static class MacEmbed
    {
        private const string LIBOBJC = "/usr/lib/libobjc.A.dylib";

        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect
        {
            public double X;
            public double Y;
            public double W;
            public double H;
        }

        [DllImport(LIBOBJC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(LIBOBJC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(LIBOBJC)] private static extern IntPtr object_getClassName(IntPtr obj);

        // One typed objc_msgSend per call shape — the managed signature fixes the
        // native ABI, so mixing shapes through one import would corrupt arguments.
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr r, IntPtr sel);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool MsgB(IntPtr r, IntPtr sel);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgPtr(IntPtr r, IntPtr sel, IntPtr a);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgNint(IntPtr r, IntPtr sel, nint a);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern void MsgUlong(IntPtr r, IntPtr sel, ulong a);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern void MsgNintV(IntPtr r, IntPtr sel, nint a);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern void MsgBool(IntPtr r, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool a);
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern void MsgFrame(IntPtr r, IntPtr sel, CGRect frame, [MarshalAs(UnmanagedType.I1)] bool display);
        // processIdentifier returns pid_t (int32) — read from w0 as int.
        [DllImport(LIBOBJC, EntryPoint = "objc_msgSend")] private static extern int MsgIntRet(IntPtr r, IntPtr sel);

        // NSWindowStyleMask
        private const ulong NSWindowStyleMaskTitled = 1UL << 0;
        private const ulong NSWindowStyleMaskFullSizeContentView = 1UL << 15;
        // NSWindowTitleVisibility.NSWindowTitleHidden
        private const nint NSWindowTitleHidden = 1;
        // NSWindowButton indices
        private const nint NSWindowCloseButton = 0;
        private const nint NSWindowMiniaturizeButton = 1;
        private const nint NSWindowZoomButton = 2;
        // NSWindowLevel. The game window belongs to the game process; the launcher
        // is a separate app, so at NSNormalWindowLevel (0) activating the launcher
        // draws its window on top and the game "vanishes" behind the panel. We put
        // the game one level above normal (1) so it stays above the launcher's main
        // window, but BELOW NSFloatingWindowLevel (3) — the level Avalonia's
        // Topmost uses — so the launcher's tool windows (marked Topmost) still
        // render above the game instead of being buried by it. Frontmost gating
        // (see FrontmostAllowsShow) hides the game entirely when an unrelated app
        // is active, so a level-1 float doesn't hover over other apps.
        private const nint GameWindowLevel = 1;

        private static bool _isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static IntPtr _window = IntPtr.Zero; // cached game NSWindow
        private static bool _styled;
        private static bool _shown;
        private static bool _failed; // stop retrying/spamming once interop fails

        // Diagnostics: MacEmbed is otherwise silent, which makes "the window won't
        // embed" impossible to debug from a user's Player.log. These log the first
        // few Apply() calls and every state transition (window resolved, styled,
        // shown/hidden) exactly once, so a single run reveals where the chain
        // breaks without spamming the log every follow tick.
        //
        // NOTE: the shipping game sets Debug.unityLogger.filterLogType to Warning,
        // so Info-level Debug.Log (BeyondLog.Msg) never reaches Player.log — the
        // launcher-connect and init lines are all dropped, only Warnings/Errors
        // survive. So every MacEmbed lifecycle line goes through Diag() (== Warning)
        // to stay visible; using BeyondLog.Msg here would make these logs useless.
        private static int _applyCount;
        private static bool _loggedFirstApply;
        private static bool _loggedResolveMiss;

        // Warning-level so it survives the game's Info-log filter (see note above).
        private static void Diag(string msg) => BeyondLog.Warning(msg);

        // Last geometry pushed by the launcher, plus whether the launcher wants the
        // window visible (its tab is active and the launcher isn't minimized). The
        // window is only actually shown when this AND frontmost gating agree.
        private static double _lastX, _lastY, _lastW, _lastH;
        private static bool _launcherVisible;
        private static bool _hasGeometry;

        // Frontmost-app gating. The game floats above the launcher (level 1), so
        // without this it would also float over unrelated apps the user switches
        // to. We hide it whenever the frontmost app is neither the game itself nor
        // the launcher that spawned us (its pid arrives via BEYOND_LAUNCHER_PID).
        private const string EnvLauncherPid = "BEYOND_LAUNCHER_PID";
        private static int _ownPid;
        private static int _launcherPid = int.MinValue; // int.MinValue = not resolved yet
        private static int _frontThrottle;

        /// <summary>
        /// Position/show (or hide) the game window. <paramref name="x"/>/<paramref name="y"/>
        /// are the panel's bottom-left origin in AppKit points; <paramref name="visible"/>
        /// reflects whether this session's tab is the active one. Called when the
        /// launcher pushes new geometry.
        /// </summary>
        public static void Apply(double x, double y, double w, double h, bool visible)
        {
            if (!_isMac || _failed)
            {
                return;
            }

            _applyCount++;
            if (!_loggedFirstApply)
            {
                _loggedFirstApply = true;
                Diag($"[MacEmbed] first Apply(): x={x:0} y={y:0} w={w:0} h={h:0} visible={visible}");
            }

            _lastX = x;
            _lastY = y;
            _lastW = w;
            _lastH = h;
            _launcherVisible = visible;
            _hasGeometry = true;
            UpdateWindow(reposition: true);
        }

        /// <summary>
        /// Re-evaluate frontmost-app gating on the game's own frame loop (called
        /// from OnUpdate), so switching to an unrelated app hides the floating
        /// window promptly instead of waiting for the launcher's next geometry
        /// push. No-op until the launcher has pushed geometry at least once.
        /// </summary>
        public static void Tick()
        {
            if (!_isMac || _failed || !_hasGeometry)
            {
                return;
            }

            // Poll frontmost ~10x/sec, not every frame — cheap, but no need to
            // hammer NSWorkspace at the full frame rate.
            if (++_frontThrottle < 6)
            {
                return;
            }
            _frontThrottle = 0;
            UpdateWindow(reposition: false);
        }

        private static void UpdateWindow(bool reposition)
        {
            try
            {
                IntPtr window = ResolveWindow();
                if (window == IntPtr.Zero)
                {
                    // Unity hasn't created its window yet — try again next tick.
                    // Log this once so a persistent miss (never finds the window)
                    // is visible rather than a silent no-op.
                    if (!_loggedResolveMiss && _applyCount >= 10)
                    {
                        _loggedResolveMiss = true;
                        BeyondLog.Warning("[MacEmbed] no NSWindow resolved after 10 ticks — game window not found.");
                    }
                    return;
                }

                if (!_styled)
                {
                    ApplyChromelessStyle(window);
                    _styled = true;
                    Diag($"[MacEmbed] applied chromeless style to window 0x{window.ToInt64():x}");
                }

                bool effectiveVisible = _launcherVisible && FrontmostAllowsShow();

                if (!effectiveVisible)
                {
                    if (_shown)
                    {
                        MsgPtr(window, Sel("orderOut:"), IntPtr.Zero);
                        _shown = false;
                        Diag("[MacEmbed] window hidden (tab inactive or another app frontmost).");
                    }
                    return;
                }

                if (reposition || !_shown)
                {
                    CGRect frame = new() { X = _lastX, Y = _lastY, W = _lastW, H = _lastH };
                    MsgFrame(window, Sel("setFrame:display:"), frame, true);
                }

                if (!_shown)
                {
                    // orderFront (not makeKey) on the show transition only, so we
                    // don't steal keyboard focus on every follow tick.
                    MsgPtr(window, Sel("orderFront:"), IntPtr.Zero);
                    _shown = true;
                    Diag($"[MacEmbed] window shown + positioned at x={_lastX:0} y={_lastY:0} w={_lastW:0} h={_lastH:0}");
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                BeyondLog.Error($"[MacEmbed] disabled after interop error: {ex}");
            }
        }

        // True when the game may be shown: the frontmost app is the game itself or
        // the launcher that owns it. Fails open (returns true) if either pid is
        // unknown, so a missing BEYOND_LAUNCHER_PID degrades to "always float"
        // rather than "never show".
        private static bool FrontmostAllowsShow()
        {
            if (_ownPid == 0)
            {
                _ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            if (_launcherPid == int.MinValue)
            {
                _launcherPid = int.TryParse(
                    Environment.GetEnvironmentVariable(EnvLauncherPid), out int p) ? p : -1;
            }
            if (_launcherPid <= 0)
            {
                return true; // launcher pid unknown — don't gate
            }

            int front = FrontmostPid();
            if (front <= 0)
            {
                return true; // couldn't determine — don't gate
            }
            return front == _ownPid || front == _launcherPid;
        }

        // pid of the frontmost application via NSWorkspace, or -1 on failure.
        private static int FrontmostPid()
        {
            IntPtr ws = Msg(objc_getClass("NSWorkspace"), Sel("sharedWorkspace"));
            if (ws == IntPtr.Zero)
            {
                return -1;
            }
            IntPtr app = Msg(ws, Sel("frontmostApplication"));
            if (app == IntPtr.Zero)
            {
                return -1;
            }
            return MsgIntRet(app, Sel("processIdentifier"));
        }

        private static IntPtr ResolveWindow()
        {
            if (_window != IntPtr.Zero)
            {
                return _window;
            }

            IntPtr nsApp = Msg(objc_getClass("NSApplication"), Sel("sharedApplication"));
            if (nsApp == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Prefer the main window, then the key window. While the launcher is the
            // foreground app the game window is neither main nor key, so we also scan
            // [NSApp windows] — but the old code blindly took windows[0], which can be
            // a Unity auxiliary/helper NSWindow (tooltip, status) rather than the game
            // render window, leaving the real window un-styled and free-floating.
            // Instead pick the first *visible* window that can become the main window;
            // that filters out panels/helpers and lands on the actual game window.
            IntPtr window = Msg(nsApp, Sel("mainWindow"));
            if (window == IntPtr.Zero)
            {
                window = Msg(nsApp, Sel("keyWindow"));
            }
            if (window == IntPtr.Zero)
            {
                IntPtr windows = Msg(nsApp, Sel("windows"));
                long count = windows != IntPtr.Zero ? (long)Msg(windows, Sel("count")) : 0;
                IntPtr firstAny = IntPtr.Zero;
                for (long i = 0; i < count; i++)
                {
                    IntPtr w = MsgNint(windows, Sel("objectAtIndex:"), (nint)i);
                    if (w == IntPtr.Zero)
                    {
                        continue;
                    }
                    if (firstAny == IntPtr.Zero)
                    {
                        firstAny = w;
                    }
                    if (MsgB(w, Sel("isVisible")) && MsgB(w, Sel("canBecomeMainWindow")))
                    {
                        window = w;
                        break;
                    }
                }
                // Fall back to the first window if none matched the heuristic.
                if (window == IntPtr.Zero)
                {
                    window = firstAny;
                }
                Diag($"[MacEmbed] resolved window from [NSApp windows] (count={count}) -> 0x{window.ToInt64():x} class={ClassNameOf(window)}");
            }
            else
            {
                Diag($"[MacEmbed] resolved main/key window 0x{window.ToInt64():x} class={ClassNameOf(window)}");
            }

            _window = window;
            return _window;
        }

        private static void ApplyChromelessStyle(IntPtr window)
        {
            MsgUlong(window, Sel("setStyleMask:"), NSWindowStyleMaskTitled | NSWindowStyleMaskFullSizeContentView);
            MsgBool(window, Sel("setTitlebarAppearsTransparent:"), true);
            MsgNintV(window, Sel("setTitleVisibility:"), NSWindowTitleHidden);
            // We drive the frame ourselves — don't let the user drag it out of place.
            MsgBool(window, Sel("setMovable:"), false);
            MsgBool(window, Sel("setMovableByWindowBackground:"), false);
            // Float just above the (separate-process) launcher window so activating
            // the launcher doesn't bury the game behind the panel.
            MsgNintV(window, Sel("setLevel:"), GameWindowLevel);
            HideButton(window, NSWindowCloseButton);
            HideButton(window, NSWindowMiniaturizeButton);
            HideButton(window, NSWindowZoomButton);
        }

        private static void HideButton(IntPtr window, nint which)
        {
            IntPtr button = MsgNint(window, Sel("standardWindowButton:"), which);
            if (button != IntPtr.Zero)
            {
                MsgBool(button, Sel("setHidden:"), true);
            }
        }

        private static IntPtr Sel(string name) => sel_registerName(name);

        // Objective-C class name of an object, for diagnostics. Returns "?" for a
        // null pointer or if the lookup throws.
        private static string ClassNameOf(IntPtr obj)
        {
            if (obj == IntPtr.Zero)
            {
                return "?";
            }
            try { return Marshal.PtrToStringAnsi(object_getClassName(obj)) ?? "?"; }
            catch { return "?"; }
        }
    }
}
