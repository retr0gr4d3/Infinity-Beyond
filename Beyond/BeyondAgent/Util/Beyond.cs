using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace BeyondAgent.Util
{
    // Standalone runtime support for the Beyond agent. No third-party loader is
    // involved: the agent is injected by our own launcher (see
    // Launcher/AssemblyPatcher.cs) and ticked by BeyondLifecycle. These types
    // live in the root BeyondAgent namespace so every BeyondAgent.*
    // file resolves them via enclosing-namespace lookup, no using needed.

    // Static logging facade. Every line goes to our own file,
    // UserData/Beyond/logs/beyond_<time>_<pid>.log, as well as Unity's logger.
    // The file is the one to ask users for: the game sets
    // Debug.unityLogger.filterLogType = Warning (Main.cs), so Msg/Verbose never
    // reach Player.log. One file per process because the launcher can run
    // several game instances out of the same install.
    public static class BeyondLog
    {
        private const int KeepLogFiles = 10;

        private static readonly object _gate = new();
        private static StreamWriter _file;
        private static bool _openFailed;
        private static int _mainThreadId = -1;
        private static readonly System.Collections.Generic.Dictionary<string, int> _repeatCounts = new();

        public static string FilePath { get; private set; }

        public static void Msg(string msg)
        {
            Write("INFO", msg);
            Debug.Log("[Beyond] " + msg);
        }

        public static void Warning(string msg)
        {
            Write("WARN", msg);
            Debug.LogWarning("[Beyond] " + msg);
        }

        public static void Error(string msg)
        {
            Write("ERROR", msg);
            Debug.LogError("[Beyond] " + msg);
        }

        // Diagnostics detail: file only, it would just be filtered out of
        // Player.log anyway.
        public static void Verbose(string msg)
        {
            Write("VERB", msg);
        }

        // For catch blocks that run every frame: logs the first 5 hits of a
        // given key in full, then every 500th with a count, so a broken tick
        // shows up in the log without burying it.
        public static void Exception(string where, System.Exception ex)
        {
            int n;
            lock (_gate)
            {
                _repeatCounts.TryGetValue(where, out n);
                _repeatCounts[where] = ++n;
            }
            if (n <= 5 || n % 500 == 0)
            {
                Write("ERROR", $"{where} threw (occurrence #{n}): {ex}");
            }
        }

        // Unity's own log stream (game warnings/errors/exceptions). Beyond's
        // own lines are skipped: they were already written by the methods above.
        internal static void WriteUnity(string logString, string stackTrace, LogType type)
        {
            if (logString != null && logString.StartsWith("[Beyond]", System.StringComparison.Ordinal))
            {
                return;
            }
            string msg = "[game " + type + "] " + logString;
            if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
            {
                msg += "\n" + stackTrace.TrimEnd();
            }
            Write("UNITY", msg);
        }

        // Called first thing in BeyondLifecycle.Create (Unity main thread), so
        // lines can carry the frame number when written from that thread.
        internal static void Init()
        {
            _mainThreadId = System.Environment.CurrentManagedThreadId;
            Write("INFO", "Log opened");
        }

        private static void Write(string level, string msg)
        {
            string frame = "";
            int thread = System.Environment.CurrentManagedThreadId;
            if (thread == _mainThreadId)
            {
                try { frame = " f" + Time.frameCount; } catch { }
            }

            lock (_gate)
            {
                if (_file == null && !_openFailed)
                {
                    Open();
                }
                if (_file == null)
                {
                    return;
                }
                try
                {
                    _file.WriteLine($"{System.DateTime.Now:HH:mm:ss.fff} [{level}] [t{thread}{frame}] {msg}");
                }
                catch { }
            }
        }

        private static void Open()
        {
            try
            {
                string dir = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "logs");
                System.IO.Directory.CreateDirectory(dir);

                // ponytail: keep the newest N files, sorted by name (names start with a timestamp).
                string[] old = System.IO.Directory.GetFiles(dir, "beyond_*.log");
                System.Array.Sort(old, System.StringComparer.Ordinal);
                for (int i = 0; i < old.Length - (KeepLogFiles - 1); i++)
                {
                    try { File.Delete(old[i]); } catch { }
                }

                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                FilePath = Path.Combine(dir, $"beyond_{System.DateTime.Now:yyyyMMdd_HHmmss}_{pid}.log");
                _file = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }
            catch (System.Exception ex)
            {
                _openFailed = true;
                Debug.LogError("[Beyond] Could not open log file: " + ex.Message);
            }
        }
    }

    // Coroutine pump. We have no loader-provided host, so we run coroutines on
    // the game's AEC singleton (a long-lived MonoBehaviour).
    public static class BeyondCoroutines
    {
        public static void Start(IEnumerator routine)
        {
            if (AEC.Instance != null)
            {
                AEC.Instance.StartCoroutine(routine);
            }
            else
            {
                Debug.LogError("[Beyond] Cannot start coroutine: AEC.Instance is null");
            }
        }
    }

    // Path helper for persisted data. Mirrors the game's UserData layout next
    // to the executable.
    public static class BeyondEnv
    {
        public static string UserDataDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
    }
}
