using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Infinity_TestMod.Util
{
    /// <summary>
    /// Always-on persistent JSONL log of every observed packet. Format matches
    /// the AQWI-Bot proxy's packets.jsonl so existing analysis tools
    /// (state.py PacketTail, gui.py) keep working without changes:
    ///
    ///   {"ts": <float>, "dir": "c2s"|"s2c", "ok": true, "pkt": {...},
    ///    "src": "mod"?  // present for spoofed/injected packets only}
    ///
    /// Written to the standalone launcher/UserData/Beyond/packets.jsonl so collaborators
    /// can find it next to other the standalone launcher output.
    /// </summary>
    public static class PacketLog
    {
        private static StreamWriter _fh;
        private static readonly object _lock = new();
        public static bool Enabled = true;
        public static string LogPath { get; private set; }

        // Drop exact-duplicate writes within a short window. Root cause is
        // upstream — either the game's network callback re-entering with the
        // same buffer or a double Harmony patch — but the dedup keeps the
        // log readable while we investigate.
        private static string _lastRaw;
        private static double _lastTs;

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond");
                System.IO.Directory.CreateDirectory(dir);
                LogPath = Path.Combine(dir, "packets.jsonl");
                _fh = new StreamWriter(
                    new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(false));
                _fh.AutoFlush = true;
                BeyondLog.Msg($"[PacketLog] writing to {LogPath}");
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[PacketLog] init failed: {ex.Message}");
                Enabled = false;
            }
        }

        /// <summary>
        /// Append one packet to the log. rawJson should already be a valid
        /// JSON object (the on-wire bytes for real packets, or our synthetic
        /// payload for spoofs); we don't re-parse it, just frame it.
        /// </summary>
        public static void Write(string direction, string rawJson, bool synthetic = false)
        {
            if (!Enabled || _fh == null || string.IsNullOrEmpty(rawJson)) return;
            double ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            // Dedup: skip if identical bytes arrived within 50ms (real
            // back-to-back packets at higher rates can repeat, but never
            // with byte-for-byte equality unless something's firing twice).
            lock (_lock)
            {
                if (_lastRaw == rawJson && (ts - _lastTs) < 0.05) return;
                _lastRaw = rawJson;
                _lastTs = ts;
            }
            StringBuilder sb = new(rawJson.Length + 64);
            sb.Append("{\"ts\":")
              .Append(ts.ToString("F3", CultureInfo.InvariantCulture))
              .Append(",\"dir\":\"").Append(direction)
              .Append("\",\"ok\":true,\"pkt\":").Append(rawJson);
            if (synthetic) sb.Append(",\"src\":\"mod\"");
            sb.Append('}');
            lock (_lock)
            {
                try { _fh.WriteLine(sb.ToString()); }
                catch (Exception ex) { BeyondLog.Error($"[PacketLog] write failed: {ex.Message}"); }
            }
        }

        public static void Close()
        {
            lock (_lock)
            {
                try { _fh?.Flush(); _fh?.Close(); } catch { }
                _fh = null;
            }
        }
    }
}
