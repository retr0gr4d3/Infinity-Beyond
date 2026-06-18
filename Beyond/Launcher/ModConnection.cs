using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Launcher
{
    // Client side of the launcher <-> mod-agent link over a named pipe.
    //
    // The pipe name is per-session: the launcher mints a unique name, spawns the
    // game with BEYOND_PIPE=<name>, and connects here to the same name. No TCP,
    // no fixed port — multiple sessions (one per account) coexist, each on its
    // own pipe.
    //
    // Resilience model (mirrors LauncherServer):
    //  - Outbound BlockingCollection + dedicated write loop; SendCommand only
    //    enqueues (bounded, drop-oldest) so the UI thread never blocks on I/O.
    //  - Heartbeat: the write loop emits a Ping every IdlePingMs when idle.
    //  - Liveness: PipeStream has no ReadTimeout, so an inbound-activity watchdog
    //    tears the link down if no bytes (incl. the server's pings) arrive within
    //    ReadStaleMs. Server process death is also detected promptly (pipe EOF).
    //  - All state lives in a Conn object so a stale loop only tears down its own
    //    connection, never the active one.
    public class ModConnection
    {
        public const string DefaultPipeName = "BeyondAgent";

        private const int OutboundCapacity = 1000;
        private const int IdlePingMs = 2000;
        private const int ReadStaleMs = 6000;
        private const int ConnectTimeoutMs = 2000;
        private const int ReconnectDelayMs = 2000;
        private const int ConnectedPollMs = 1000;

        private sealed class Conn
        {
            public NamedPipeClientStream Pipe = null!;
            public BlockingCollection<string> Outbound = null!;
            public long LastRxTicks;
        }

        private readonly string _pipeName;
        private Conn? _active;
        private readonly object _connLock = new object();
        private CancellationTokenSource? _cts;
        private bool _isConnected;

        private static readonly string PingLine = JsonConvert.SerializeObject(new JObject { ["Type"] = "Ping" }) + "\n";

        public event Action<bool>? ConnectionStateChanged;
        public event Action<JObject>? StatusReceived;
        public event Action<string>? LogReceived;
        public event Action<string, string, string, string>? SniffedPacketReceived; // direction, cmd, typeName, raw
        public event Action<string, string, string, string>? InterceptedPacketReceived; // action, typeName, cmd, logEntry
        public event Action<string>? QuestRunnerLogReceived;
        public event Action<JObject>? ItemCatalogReceived;
        public event Action<JObject>? MusicCatalogReceived;
        public event Action<JObject>? QuestDirectoryReceived;
        public event Action<JObject>? QuestChainsReceived;

        public ModConnection(string pipeName = DefaultPipeName)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    Dispatcher.UIThread.Post(() => ConnectionStateChanged?.Invoke(_isConnected));
                }
            }
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ConnectLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            Conn? conn;
            lock (_connLock) { conn = _active; _active = null; }
            Teardown(conn);
            IsConnected = false;
        }

        private async Task ConnectLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!IsConnected)
                {
                    NamedPipeClientStream? pipe = null;
                    try
                    {
                        pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        pipe.Connect(ConnectTimeoutMs); // throws if the server pipe isn't up yet

                        Conn conn = new Conn
                        {
                            Pipe = pipe,
                            Outbound = new BlockingCollection<string>(OutboundCapacity),
                            LastRxTicks = Environment.TickCount64
                        };
                        pipe = null; // ownership transferred to conn
                        lock (_connLock) { _active = conn; }
                        IsConnected = true;

                        _ = Task.Run(() => ReadLoop(conn, ct), ct);
                        _ = Task.Run(() => WriteLoop(conn, ct), ct);

                        SendCommand("RequestStatus", null);
                    }
                    catch
                    {
                        try { pipe?.Dispose(); } catch { }
                        await Task.Delay(ReconnectDelayMs, ct);
                    }
                }
                else
                {
                    await Task.Delay(ConnectedPollMs, ct);
                }
            }
        }

        private void ReadLoop(Conn conn, CancellationToken ct)
        {
            StreamReader? reader = null;
            try
            {
                reader = new StreamReader(conn.Pipe, Encoding.UTF8);
                while (!ct.IsCancellationRequested && conn.Pipe.IsConnected)
                {
                    string? line = reader.ReadLine();
                    if (line == null) break; // server closed / died
                    conn.LastRxTicks = Environment.TickCount64;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Server Pings keep LastRxTicks fresh; ProcessMessage ignores
                    // unknown types, so Ping needs no special handling.
                    ProcessMessage(line);
                }
            }
            catch { }
            finally
            {
                try { reader?.Dispose(); } catch { }
                CloseConn(conn);
            }
        }

        private void WriteLoop(Conn conn, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && conn.Pipe.IsConnected)
                {
                    // Inbound-activity watchdog (PipeStream has no ReadTimeout).
                    if (Environment.TickCount64 - conn.LastRxTicks > ReadStaleMs) break;

                    string? msg;
                    if (!conn.Outbound.TryTake(out msg, IdlePingMs, ct))
                    {
                        msg = PingLine; // idle => heartbeat
                    }

                    if (msg != null)
                    {
                        byte[] data = Encoding.UTF8.GetBytes(msg);
                        conn.Pipe.Write(data, 0, data.Length);
                        conn.Pipe.Flush();
                    }
                }
            }
            catch { /* cancellation, CompleteAdding on close, or write error */ }
            finally
            {
                CloseConn(conn);
            }
        }

        private void ProcessMessage(string rawJson)
        {
            try
            {
                JObject msg = JObject.Parse(rawJson);
                string? type = (string?)msg["Type"];
                if (type == null) return;

                switch (type)
                {
                    case "Status":
                        JObject? settings = msg["Settings"] as JObject;
                        if (settings != null)
                        {
                            Dispatcher.UIThread.Post(() => StatusReceived?.Invoke(settings));
                        }
                        break;

                    case "Log":
                        string? logMsg = (string?)msg["Message"];
                        if (logMsg != null)
                        {
                            Dispatcher.UIThread.Post(() => LogReceived?.Invoke(logMsg));
                        }
                        break;

                    case "SniffedPacket":
                        string? dir = (string?)msg["Direction"];
                        string? cmd = (string?)msg["Cmd"];
                        string? tName = (string?)msg["TypeName"];
                        string? raw = (string?)msg["Raw"];
                        if (dir != null && cmd != null && tName != null && raw != null)
                        {
                            Dispatcher.UIThread.Post(() => SniffedPacketReceived?.Invoke(dir, cmd, tName, raw));
                        }
                        break;

                    case "InterceptedPacket":
                        string? action = (string?)msg["Action"];
                        string? typeName = (string?)msg["TypeName"];
                        string? packetCmd = (string?)msg["Cmd"];
                        string? logEntry = (string?)msg["LogEntry"];
                        if (action != null && typeName != null && packetCmd != null && logEntry != null)
                        {
                            Dispatcher.UIThread.Post(() => InterceptedPacketReceived?.Invoke(action, typeName, packetCmd, logEntry));
                        }
                        break;

                    case "QuestRunnerLog":
                        string? qrMsg = (string?)msg["Message"];
                        if (qrMsg != null)
                        {
                            Dispatcher.UIThread.Post(() => QuestRunnerLogReceived?.Invoke(qrMsg));
                        }
                        break;

                    case "ItemCatalog":
                        Dispatcher.UIThread.Post(() => ItemCatalogReceived?.Invoke(msg));
                        break;

                    case "MusicCatalog":
                        Dispatcher.UIThread.Post(() => MusicCatalogReceived?.Invoke(msg));
                        break;

                    case "QuestDirectory":
                        Dispatcher.UIThread.Post(() => QuestDirectoryReceived?.Invoke(msg));
                        break;

                    case "QuestChains":
                        JObject? chains = msg["Chains"] as JObject;
                        if (chains != null)
                        {
                            Dispatcher.UIThread.Post(() => QuestChainsReceived?.Invoke(chains));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => LogReceived?.Invoke($"[Launcher Error] Error parsing mod message: {ex.Message}"));
            }
        }

        public void SendCommand(string type, JObject? parameters)
        {
            Conn? conn = _active;
            if (conn == null) return;

            try
            {
                JObject payload = new JObject { ["Type"] = type };
                if (parameters != null)
                {
                    foreach (var prop in parameters.Properties())
                    {
                        payload[prop.Name] = prop.Value;
                    }
                }

                string json = JsonConvert.SerializeObject(payload) + "\n";
                BlockingCollection<string> q = conn.Outbound;
                if (!q.TryAdd(json))
                {
                    q.TryTake(out string? _, 0); // full: drop oldest
                    q.TryAdd(json);
                }
            }
            catch { /* queue completed during a concurrent close */ }
        }

        public void SetSetting(string name, object? value)
        {
            SendCommand("SetSetting", new JObject
            {
                ["Name"] = name,
                ["Value"] = value == null ? JValue.CreateNull() : JToken.FromObject(value)
            });
        }

        // Tear down a specific connection. Only flips global state if this Conn
        // is still the active one, so a stale loop can't disturb a newer link.
        private void CloseConn(Conn? conn)
        {
            if (conn == null) return;
            bool wasActive;
            lock (_connLock)
            {
                wasActive = _active == conn;
                if (wasActive) _active = null;
            }
            Teardown(conn);
            if (wasActive) IsConnected = false;
        }

        private static void Teardown(Conn? conn)
        {
            if (conn == null) return;
            try { conn.Outbound.CompleteAdding(); } catch { }
            try { conn.Pipe.Dispose(); } catch { }
        }
    }
}
