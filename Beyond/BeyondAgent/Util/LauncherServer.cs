using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using UnityEngine;

namespace BeyondAgent.Util
{
    // Named-pipe server for the launcher client. One launcher per game process,
    // so the pipe name is per-session: the launcher mints a unique name, spawns
    // the game with BEYOND_PIPE=<name> in its environment, and we listen on it.
    // No TCP, no fixed port — multiple game/launcher sessions coexist, each on
    // its own pipe, with no collision.
    //
    // Resilience model (mirrors the launcher client):
    //  - Per-connection outbound BlockingCollection + dedicated write thread.
    //    Send() only enqueues, so game/event threads never block on a slow
    //    client. Bounded queue drops oldest under flood.
    //  - Heartbeat: the write thread emits a Ping every IdlePingMs when idle.
    //  - Liveness: PipeStream has no ReadTimeout, so we use an inbound-activity
    //    watchdog — if no bytes (incl. the peer's pings) arrive within
    //    ReadStaleMs the connection is torn down. Peer process death is also
    //    detected promptly (the pipe read returns EOF / throws).
    //  - Self-healing accept loop: never dies while running.
    public static class LauncherServer
    {
        private const string EnvPipeName = "BEYOND_PIPE";
        private const string DefaultPipeName = "BeyondAgent";
        private const int OutboundCapacity = 1000; // drop-oldest beyond this
        private const int IdlePingMs = 2000;        // heartbeat cadence when idle
        private const int ReadStaleMs = 6000;       // no inbound in this window => dead

        private sealed class Conn
        {
            public NamedPipeServerStream Pipe;
            public BlockingCollection<string> Outbound;
            public long LastRxTicks;
        }

        private static string _pipeName;
        private static Conn _active;
        private static readonly object _connLock = new();
        private static Thread _listenThread;
        private static readonly ConcurrentQueue<string> _incomingQueue = new();
        private static bool _isRunning;

        private static readonly string PingLine = JsonConvert.SerializeObject(new { Type = "Ping" }) + "\n";

        public static bool IsConnected
        {
            get { Conn c = _active; return c?.Pipe.IsConnected == true; }
        }

        public static void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;

            _pipeName = Environment.GetEnvironmentVariable(EnvPipeName);
            if (string.IsNullOrWhiteSpace(_pipeName))
            {
                _pipeName = DefaultPipeName;
            }

            _listenThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "LauncherServerListenThread"
            };
            _listenThread.Start();
            Debug.Log($"[LauncherServer] Listen thread started (pipe '{_pipeName}').");
        }

        public static void Stop()
        {
            _isRunning = false;
            Conn old;
            lock (_connLock) { old = _active; _active = null; }
            Teardown(old);

            if (_listenThread?.IsAlive == true)
            {
                _listenThread.Join(1000);
            }
            Debug.Log("[LauncherServer] Stopped.");
        }

        // Self-healing accept loop. Any per-iteration failure is caught, logged,
        // and recovered from — the loop never dies while the server is running,
        // so the launcher can always (re)connect.
        private static void ListenLoop()
        {
            while (_isRunning)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    // Allow multiple instances so we can post the next waiter
                    // (WaitForConnection blocks) while a client is connected,
                    // instead of spinning on "all pipe instances are busy".
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    pipe.WaitForConnection();
                    Debug.Log("[LauncherServer] Launcher connected.");

                    // Drop any prior connection before adopting the new one.
                    Conn old;
                    lock (_connLock) { old = _active; _active = null; }
                    Teardown(old);

                    Conn conn = new()
                    {
                        Pipe = pipe,
                        Outbound = new BlockingCollection<string>(OutboundCapacity),
                        LastRxTicks = DateTime.UtcNow.Ticks
                    };
                    pipe = null; // ownership transferred to conn
                    lock (_connLock) { _active = conn; }

                    new Thread(WriteLoop) { IsBackground = true, Name = "LauncherServerWriteThread" }.Start(conn);
                    new Thread(ReadLoop) { IsBackground = true, Name = "LauncherServerReadThread" }.Start(conn);

                    // Initial sync — enqueued, flushed by the write thread.
                    try
                    {
                        BeyondAgentClass.activeInstance?.SendStatusUpdate();
                        BeyondAgentClass.activeInstance?.SendCatalogs();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[LauncherServer] Initial sync failed: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    try { pipe?.Dispose(); } catch { }
                    if (!_isRunning)
                    {
                        break;
                    }

                    Debug.LogError($"[LauncherServer] Accept loop error, recovering: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        }

        private static void ReadLoop(object o)
        {
            Conn conn = (Conn)o;
            StreamReader reader = null;
            try
            {
                reader = new StreamReader(conn.Pipe, Encoding.UTF8);
                while (_isRunning && IsActive(conn) && conn.Pipe.IsConnected)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        break; // peer closed / died
                    }

                    conn.LastRxTicks = DateTime.UtcNow.Ticks;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    // Pings keep LastRxTicks fresh; the command processor ignores
                    // unknown types, so enqueuing them is harmless.
                    _incomingQueue.Enqueue(line);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LauncherServer] Read loop ended: {ex.Message}");
            }
            finally
            {
                try { reader?.Dispose(); } catch { }
                bool wasActive = Detach(conn);
                Teardown(conn);
                if (wasActive)
                {
                    Debug.Log("[LauncherServer] Launcher disconnected.");
                }
            }
        }

        private static void WriteLoop(object o)
        {
            Conn conn = (Conn)o;
            try
            {
                while (_isRunning && IsActive(conn) && conn.Pipe.IsConnected)
                {
                    // Inbound-activity watchdog (PipeStream has no ReadTimeout):
                    // if the peer has gone silent past the heartbeat window, it's
                    // dead — tear down so the launcher reconnects.
                    if (DateTime.UtcNow.Ticks - conn.LastRxTicks > ReadStaleMs * TimeSpan.TicksPerMillisecond)
                    {
                        break;
                    }

                    if (!conn.Outbound.TryTake(out string msg, IdlePingMs))
                    {
                        msg = PingLine; // idle => heartbeat
                    }

                    byte[] data = Encoding.UTF8.GetBytes(msg);
                    conn.Pipe.Write(data, 0, data.Length);
                    conn.Pipe.Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LauncherServer] Write loop ended: {ex.Message}");
            }
            finally
            {
                Detach(conn);
                Teardown(conn);
            }
        }

        public static void Send(object payload)
        {
            if (payload == null)
            {
                return;
            }

            Conn conn = _active;
            if (conn == null)
            {
                return;
            }

            string json;
            try { json = JsonConvert.SerializeObject(payload) + "\n"; }
            catch { return; }

            try
            {
                BlockingCollection<string> q = conn.Outbound;
                if (!q.TryAdd(json))
                {
                    q.TryTake(out _, 0); // full: drop oldest
                    q.TryAdd(json);
                }
            }
            catch { /* queue completed during a concurrent close */ }
        }

        public static bool TryDequeueCommand(out string command)
        {
            return _incomingQueue.TryDequeue(out command);
        }

        private static bool IsActive(Conn conn)
        {
            lock (_connLock) { return _active == conn; }
        }

        private static bool Detach(Conn conn)
        {
            lock (_connLock)
            {
                if (_active == conn) { _active = null; return true; }
                return false;
            }
        }

        private static void Teardown(Conn conn)
        {
            if (conn == null)
            {
                return;
            }

            try { conn.Outbound.CompleteAdding(); } catch { }
            try { if (conn.Pipe.IsConnected) { conn.Pipe.Disconnect(); } } catch { }
            try { conn.Pipe.Dispose(); } catch { }
        }
    }
}
