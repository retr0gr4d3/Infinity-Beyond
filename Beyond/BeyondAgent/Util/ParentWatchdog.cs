using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BeyondAgent.Util
{
    // Binds the game's lifetime to the launcher's on macOS. Windows uses a Job
    // Object (ChildProcessTracker) so the OS kills the game when the launcher
    // exits; macOS has no equivalent, so a quit/crash of the launcher would
    // otherwise orphan the game window. Here the game polls the launcher pid
    // (passed via BEYOND_LAUNCHER_PID) and quits itself once that process is
    // gone — covering both a clean quit and a crash.
    //
    // Ticked from OnUpdate; throttled to ~1s. No-op on Windows (the Job Object
    // already handles it) and when no launcher pid was provided (e.g. the game
    // was launched directly rather than by the launcher).
    public static class ParentWatchdog
    {
        private const string EnvLauncherPid = "BEYOND_LAUNCHER_PID";

        private static readonly bool _isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static int _launcherPid = int.MinValue; // int.MinValue = not resolved yet
        private static int _throttle;
        private static bool _quitting;

        // kill(pid, 0) performs error checking without sending a signal: returns 0
        // if the process exists (ESRCH otherwise), so it's a cheap liveness probe.
        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);

        public static void Tick()
        {
            if (!_isMac || _quitting)
            {
                return;
            }

            if (_launcherPid == int.MinValue)
            {
                _launcherPid = int.TryParse(
                    Environment.GetEnvironmentVariable(EnvLauncherPid), out int p) ? p : -1;
                if (_launcherPid > 0)
                {
                    Debug.LogWarning($"[ParentWatchdog] watching launcher pid {_launcherPid}.");
                }
            }
            if (_launcherPid <= 0)
            {
                return; // no launcher to bind to
            }

            // Poll ~1s (OnUpdate runs at the frame rate).
            if (++_throttle < 60)
            {
                return;
            }
            _throttle = 0;

            if (kill(_launcherPid, 0) != 0)
            {
                _quitting = true;
                Debug.LogWarning($"[ParentWatchdog] launcher pid {_launcherPid} is gone — quitting game.");
                Application.Quit();
            }
        }
    }
}
