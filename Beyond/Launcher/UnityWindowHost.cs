using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Launcher
{
    public class UnityWindowHost : NativeControlHost
    {
        public UnityWindowHost()
        {
            Focusable = true;
            Loaded += OnHostLoaded;
        }

        private void OnHostLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            int width = (int)Bounds.Width;
            int height = (int)Bounds.Height;
            if (width <= 0)
            {
                width = 800;
            }

            if (height <= 0)
            {
                height = 600;
            }

            StartUnityProcessDeferred(width, height);
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x, int y,
            int nWidth, int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgti);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        // --- Native host panel background (replaces the default white static control) ---
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int crColor);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        private const string HostClassName = "BeyondUnityHost";

        // RGB(0x0F, 0x0F, 0x11) packed as a COLORREF (0x00BBGGRR).
        private const int HostBackgroundColor = 0x00110F0F;

        private static readonly Lock _hostClassLock = new();
        private static bool _hostClassReady;
        private static WndProcDelegate? _hostWndProc; // kept alive for the class registration
        private static IntPtr _hostBgBrush = IntPtr.Zero;

        // Registers a window class whose background brush is the app's dark color so
        // the hosted panel doesn't flash white before Unity attaches. Falls back to
        // the system "static" class if registration fails.
        private static string EnsureHostClass()
        {
            lock (_hostClassLock)
            {
                if (_hostClassReady)
                {
                    return HostClassName;
                }

                try
                {
                    _hostBgBrush = CreateSolidBrush(HostBackgroundColor);
                    _hostWndProc = (h, m, w, l) => DefWindowProc(h, m, w, l);
                    WNDCLASSEX wc = new()
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_hostWndProc),
                        hInstance = GetModuleHandle(null),
                        hbrBackground = _hostBgBrush,
                        lpszClassName = HostClassName
                    };
                    ushort atom = RegisterClassEx(ref wc);
                    if (atom != 0 || Marshal.GetLastWin32Error() == 1410) // ERROR_CLASS_ALREADY_EXISTS
                    {
                        _hostClassReady = true;
                    }
                }
                catch { }
                return _hostClassReady ? HostClassName : "static";
            }
        }

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;

        private Process? _unityProcess;
        private IntPtr _childHwnd = IntPtr.Zero;
        private DispatcherTimer? _resizeTimer;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        // Cross-process keyboard routing: the embedded game window lives on its
        // own thread, so the launcher UI thread's input queue must be attached
        // to it for click-to-focus to deliver keystrokes (login fields, chat).
        private bool _inputAttached;

        private bool _processStarted;
        private uint _gameThreadId;
        private uint _guiThreadId;

        // Env var the agent reads to learn which named pipe to serve. The
        // launcher mints a unique value per session so multiple games coexist
        // without a shared port.
        private const string EnvPipeName = "BEYOND_PIPE";

        // Per-session pipe name. Bound from the owning view-model so the agent
        // and this launcher's ModConnection agree on the endpoint. Falls back to
        // the default name when unset.
        public static readonly StyledProperty<string> PipeNameProperty =
            AvaloniaProperty.Register<UnityWindowHost, string>(nameof(PipeName));

        public string PipeName
        {
            get => GetValue(PipeNameProperty);
            set => SetValue(PipeNameProperty, value);
        }

        public static readonly StyledProperty<string> PresetUsernameProperty =
            AvaloniaProperty.Register<UnityWindowHost, string>(nameof(PresetUsername));

        public string PresetUsername
        {
            get => GetValue(PresetUsernameProperty);
            set => SetValue(PresetUsernameProperty, value);
        }

        public static readonly StyledProperty<string> PresetPasswordProperty =
            AvaloniaProperty.Register<UnityWindowHost, string>(nameof(PresetPassword));

        public string PresetPassword
        {
            get => GetValue(PresetPasswordProperty);
            set => SetValue(PresetPasswordProperty, value);
        }

        public static readonly StyledProperty<string> PresetNicknameProperty =
            AvaloniaProperty.Register<UnityWindowHost, string>(nameof(PresetNickname));

        public string PresetNickname
        {
            get => GetValue(PresetNicknameProperty);
            set => SetValue(PresetNicknameProperty, value);
        }

        public static readonly StyledProperty<string> GameDirectoryProperty =
            AvaloniaProperty.Register<UnityWindowHost, string>(nameof(GameDirectory));

        public string GameDirectory
        {
            get => GetValue(GameDirectoryProperty);
            set => SetValue(GameDirectoryProperty, value);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                int width = (int)Bounds.Width;
                int height = (int)Bounds.Height;
                if (width <= 0)
                {
                    width = 800;
                }

                if (height <= 0)
                {
                    height = 600;
                }

                // Create a WS_CHILD | WS_VISIBLE control to host the Unity window.
                // Uses a custom class with a dark (#0F0F11) background brush and no
                // window text so the panel doesn't show white before Unity attaches.
                string hostClass = EnsureHostClass();
                _childHwnd = CreateWindowEx(
                    0,
                    hostClass,
                    "",
                    0x40000000 | 0x10000000, // WS_CHILD | WS_VISIBLE
                    0, 0,
                    width, height,
                    parent.Handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (_childHwnd != IntPtr.Zero)
                {
                    // Start timer to keep unity window sized correctly to match this host panel
                    _resizeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _resizeTimer.Tick += (s, e) => ResizeUnityWindow();
                    _resizeTimer.Start();

                    return new PlatformHandle(_childHwnd, "HWND");
                }
            }

            return base.CreateNativeControlCore(parent);
        }

        private void StartUnityProcessDeferred(int width, int height)
        {
            // On Windows we require the host child HWND (the game re-parents into
            // it). On macOS there is no cross-process window embedding, so the game
            // launches as its own top-level window — everything else (Cecil patch,
            // named pipe, tool windows) works the same.
            // ponytail: mac runs the game un-embedded; true in-tab embedding is the
            // deferred window-model decision — layer it here when settled.
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if ((isWindows && _childHwnd == IntPtr.Zero) || _processStarted)
            {
                return;
            }

            _processStarted = true;
            try
            {
                // Write debug file
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "launch_debug.log");
                    string dir = Path.GetDirectoryName(logPath)!;
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.AppendAllText(logPath, $"[{System.DateTime.Now:HH:mm:ss}] Starting launch. GameDirectory='{GameDirectory}' PipeName='{PipeName}' Username='{PresetUsername}'\n");
                }
                catch { }

                GameLocator.TryResolveGameExe(GameDirectory, out string? gameExe);
                if (gameExe == null)
                {
                    Trace.WriteLine($"No game executable found in GameDirectory='{GameDirectory}'.");
                }

                if (gameExe != null)
                {
                    string? gameDir = Path.GetDirectoryName(gameExe);
                    string managedDir = GameLocator.GetManagedDir(gameExe)!;
                    bool isMelonLoaderDetected = false;
                    if (gameDir != null)
                    {
                        isMelonLoaderDetected = File.Exists(Path.Combine(gameDir, "version.dll")) && (Directory.Exists(Path.Combine(gameDir, "MelonLoader")) || File.Exists(Path.Combine(gameDir, "MelonLoader", "MelonLoader.dll")));
                    }

                    if (!isMelonLoaderDetected)
                    {
                        try
                        {
                            // Copy BeyondAgent.dll and 0Harmony.dll from launcher directory to game's managed directory
                            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
                            string sourceAgent = Path.Combine(launcherDir, "BeyondAgent.dll");
                            string sourceHarmony = Path.Combine(launcherDir, "0Harmony.dll");

                            if (File.Exists(sourceAgent))
                            {
                                string destAgent = Path.Combine(managedDir, "BeyondAgent.dll");
                                File.Copy(sourceAgent, destAgent, true);
                                Trace.WriteLine($"[Launcher] Copied BeyondAgent.dll to {destAgent}");
                            }
                            else
                            {
                                Trace.WriteLine($"[Launcher] Warning: BeyondAgent.dll not found in launcher directory: {sourceAgent}");
                            }

                            if (File.Exists(sourceHarmony))
                            {
                                string destHarmony = Path.Combine(managedDir, "0Harmony.dll");
                                File.Copy(sourceHarmony, destHarmony, true);
                                Trace.WriteLine($"[Launcher] Copied 0Harmony.dll to {destHarmony}");
                            }
                            else
                            {
                                Trace.WriteLine($"[Launcher] Warning: 0Harmony.dll not found in launcher directory: {sourceHarmony}");
                            }

                            AssemblyPatcher.Patch(managedDir);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[AssemblyPatcher] Failed to patch Assembly-CSharp.dll: {ex}");
                        }
                    }
                    else
                    {
                        Trace.WriteLine("MelonLoader detected. Disabling built-in modifications (restoring original assembly if needed).");
                        try
                        {
                            string assemblyPath = Path.Combine(managedDir, "Assembly-CSharp.dll");
                            string backupPath = assemblyPath + ".bak";
                            if (File.Exists(backupPath))
                            {
                                File.Copy(backupPath, assemblyPath, true);
                                Trace.WriteLine("Restored original Assembly-CSharp.dll from backup.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Failed to restore original Assembly-CSharp.dll: {ex}");
                        }
                    }

                    // -parentHWND is a Windows-only Unity arg; only pass it when we
                    // actually have a host window to embed into.
                    string embedArg = isWindows && _childHwnd != IntPtr.Zero ? $"-parentHWND {(long)_childHwnd} " : "";
                    ProcessStartInfo psi = new()
                    {
                        FileName = gameExe,
                        Arguments = $"{embedArg}-screen-width {width} -screen-height {height}",
                        WorkingDirectory = Path.GetDirectoryName(gameExe),
                        UseShellExecute = false
                    };
                    // Tell the agent which named pipe to serve for this session.
                    string pipeName = string.IsNullOrWhiteSpace(PipeName) ? "BeyondAgent" : PipeName;
                    psi.Environment[EnvPipeName] = pipeName;
                    if (!string.IsNullOrEmpty(PresetUsername))
                    {
                        psi.Environment["BEYOND_USER"] = PresetUsername;
                    }
                    if (!string.IsNullOrEmpty(PresetPassword))
                    {
                        psi.Environment["BEYOND_PASS"] = PresetPassword;
                    }
                    if (!string.IsNullOrEmpty(PresetNickname))
                    {
                        psi.Environment["BEYOND_NICK"] = PresetNickname;
                    }
                    _unityProcess = Process.Start(psi);

                    // Bind the game to the launcher's lifetime: the OS kills it when
                    // the launcher exits (even on crash), so it can never orphan.
                    if (_unityProcess != null)
                    {
                        try { ChildProcessTracker.AddProcess(_unityProcess.Handle); }
                        catch (Exception ex) { Trace.WriteLine($"[ChildProcessTracker] assign failed: {ex.Message}"); }
                    }
                }
                else
                {
                    Trace.WriteLine("No game executable found; configure the game directory in the Configurator.");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to start Unity process: {ex.Message}");
            }
        }

        private IntPtr GetUnityHwnd()
        {
            IntPtr unityHwnd = IntPtr.Zero;
            if (_childHwnd != IntPtr.Zero)
            {
                EnumChildWindows(_childHwnd, (hwnd, lParam) =>
                {
                    unityHwnd = hwnd;
                    return false; // Stop enumeration (we only care about the top-level Unity child window)
                }, IntPtr.Zero);
            }
            return unityHwnd;
        }

        private void ResizeUnityWindow()
        {
            if (_childHwnd != IntPtr.Zero)
            {
                int w = (int)Bounds.Width;
                int h = (int)Bounds.Height;
                if (w > 0 && h > 0)
                {
                    if (w != _lastWidth || h != _lastHeight)
                    {
                        MoveWindow(_childHwnd, 0, 0, w, h, true);
                        _lastWidth = w;
                        _lastHeight = h;
                    }
                    IntPtr unityHwnd = GetUnityHwnd();
                    if (unityHwnd != IntPtr.Zero)
                    {
                        bool needsResize = true;
                        if (GetWindowRect(unityHwnd, out RECT rect))
                        {
                            int actualWidth = rect.Right - rect.Left;
                            int actualHeight = rect.Bottom - rect.Top;
                            if (actualWidth == w && actualHeight == h)
                            {
                                needsResize = false;
                            }
                        }

                        if (needsResize)
                        {
                            MoveWindow(unityHwnd, 0, 0, w, h, true);
                        }
                        EnsureInputAttached(unityHwnd);
                        KeepUnityFocus(unityHwnd);
                    }
                }
            }
        }

        private void KeepUnityFocus(IntPtr unityHwnd)
        {
            if (unityHwnd == IntPtr.Zero)
            {
                return;
            }

            // Only redirect focus if the launcher app or Unity is the foreground window
            IntPtr fgWnd = GetForegroundWindow();
            if (fgWnd == IntPtr.Zero)
            {
                return;
            }

            uint currentPid = (uint)Environment.ProcessId;
            uint unityPid = GetWindowThreadProcessId(fgWnd, out uint fgPid);

            if (fgPid != currentPid && (unityPid == 0 || fgPid != unityPid))
            {
                return; // Foreground window is another application entirely
            }

            // Check if mouse left or right button is down
            bool isMouseDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 ||
                                (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;

            if (isMouseDown)
            {
                if (GetCursorPos(out POINT pt))
                {
                    IntPtr hwndUnderCursor = WindowFromPoint(pt);
                    IntPtr temp = hwndUnderCursor;
                    bool isUnityOrChild = false;
                    while (temp != IntPtr.Zero)
                    {
                        if (temp == unityHwnd)
                        {
                            isUnityOrChild = true;
                            break;
                        }
                        temp = GetParent(temp);
                    }

                    if (isUnityOrChild)
                    {
                        IntPtr currentFocus = GetGlobalFocusedWindow();
                        if (currentFocus != unityHwnd)
                        {
                            // Sync Avalonia focus and Win32 focus
                            this.Focus();
                            SetFocus(unityHwnd);
                        }
                    }
                }
            }
        }

        private static IntPtr GetGlobalFocusedWindow()
        {
            GUITHREADINFO gti = new();
            gti.cbSize = Marshal.SizeOf(gti);
            return GetGUIThreadInfo(0, ref gti) ? gti.hwndFocus : nint.Zero;
        }

        protected override void OnGotFocus(Avalonia.Input.FocusChangedEventArgs e)
        {
            base.OnGotFocus(e);
            IntPtr unityHwnd = GetUnityHwnd();
            if (unityHwnd != IntPtr.Zero)
            {
                SetFocus(unityHwnd);
            }
        }

        // Attach the launcher UI thread's input queue to the game's window
        // thread so the OS routes keyboard focus across the process boundary.
        // Without this, clicks reach the embedded game but keystrokes never do.
        // Done once, when the Unity child window first appears.
        private void EnsureInputAttached(IntPtr unityHwnd)
        {
            if (_inputAttached)
            {
                return;
            }

            uint gameThread = GetWindowThreadProcessId(unityHwnd, out _);
            uint guiThread = GetCurrentThreadId();
            if (gameThread == 0 || gameThread == guiThread)
            {
                return;
            }

            if (AttachThreadInput(guiThread, gameThread, true))
            {
                _inputAttached = true;
                _gameThreadId = gameThread;
                _guiThreadId = guiThread;
                // Give the game initial focus so the login field is typeable
                // immediately; clicking launcher controls moves focus back.
                SetFocus(unityHwnd);
            }
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _resizeTimer?.Stop();
            _resizeTimer = null;

            if (_inputAttached)
            {
                try { AttachThreadInput(_guiThreadId, _gameThreadId, false); } catch { }
                _inputAttached = false;
            }

            if (_unityProcess != null)
            {
                try { if (!_unityProcess.HasExited) { _unityProcess.Kill(true); } } catch { }
                try { _unityProcess.Dispose(); } catch { }
                _unityProcess = null;
            }

            if (_childHwnd != IntPtr.Zero)
            {
                DestroyWindow(_childHwnd);
                _childHwnd = IntPtr.Zero;
            }

            base.DestroyNativeControlCore(control);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty)
            {
                ResizeUnityWindow();
            }
        }
    }
}