// McpBridge.cs - Editor lifecycle, WebSocket server, main-thread pump, dispatch.
//
// This is the HEART of the project (SPEC §3, §4, §6). It:
//   - Starts a WebSocket server on ws://127.0.0.1:<port> when the Editor loads.
//   - Receives requests on a background thread and ENQUEUES them.
//   - Drains the queue on EditorApplication.update - i.e. the MAIN THREAD -
//     because nearly all Unity APIs require it.
//   - Tears down cleanly before a domain reload so the socket/port is released;
//     [InitializeOnLoad] re-runs this ctor on the new domain and restarts it.
//
// Status, port config and a recent-activity log are exposed for the Editor window.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityAgentBridge.Editor.Tools;

namespace UnityAgentBridge.Editor
{
    [InitializeOnLoad]
    public static class McpBridge
    {
        public const string Host = "127.0.0.1";
        public const int DefaultPort = 17890;
        private const string PortPrefKey = "McpBridge.Port";

        private static readonly ConcurrentQueue<Action> _mainThreadJobs = new();
        private static readonly object _logLock = new object();
        private static readonly List<string> _recent = new List<string>();
        private static WebSocketServer _server;

        /// <summary>Configured listen port (persisted in EditorPrefs).</summary>
        public static int Port
        {
            get => EditorPrefs.GetInt(PortPrefKey, DefaultPort);
            private set => EditorPrefs.SetInt(PortPrefKey, value);
        }

        public static bool IsListening { get; private set; }
        public static bool ClientConnected => _server != null && _server.HasClient;

        static McpBridge()
        {
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;

            ConsoleTools.Install();     // ring buffer must capture logs from load onward
            CompilationTools.Install();  // capture compile errors for the fix-loop tools
            EnsureFastPlayMode();       // so run_playmode doesn't drop the socket on a domain reload
            StartServer();
        }

        // Entering Play Mode normally triggers a domain reload, which would wipe the
        // bridge (and any in-flight request) mid-call. Disabling domain reload on play
        // keeps the socket and static state alive so run_playmode returns one clean
        // response. Script recompiles still reload the domain - handled separately.
        private static void EnsureFastPlayMode()
        {
            try
            {
                if (!EditorSettings.enterPlayModeOptionsEnabled ||
                    (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0)
                {
                    EditorSettings.enterPlayModeOptionsEnabled = true;
                    EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                    Debug.Log("[McpBridge] enabled Enter Play Mode Options (Disable Domain Reload).");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[McpBridge] could not set Enter Play Mode Options: " + e.Message);
            }
        }

        // The bootstrap tool. Every other capability is just another [McpTool]
        // discovered by ToolRegistry - this one proves the pipe is alive.
        [McpTool("ping", "Health check; returns the Unity version.")]
        public static object Ping() => new { pong = true, unityVersion = Application.unityVersion };

        // --- server lifecycle (also driven by the Editor window) ----------------

        public static void StartServer()
        {
            try
            {
                // Provision the shared-secret token BEFORE listening, so the server
                // can read it the moment it connects. Auth is on by default.
                // Capture port/token HERE on the main thread: the Authorize delegate
                // runs on the accept (background) thread, where EditorPrefs - which
                // McpBridge.Port reads via EditorPrefs.GetInt - would throw.
                var port = Port;
                var token = BridgeAuth.EnsureToken(port);

                _server = new WebSocketServer(Host, port);
                _server.OnMessage += HandleMessage;
                _server.Authorize = headers => BridgeAuth.Validate(headers, token, Host, port);
                _server.Start();
                IsListening = true;
                Debug.Log($"[McpBridge] listening on ws://{Host}:{port} (auth on; token at {BridgeAuth.TokenPath(port)})");
            }
            catch (Exception e)
            {
                IsListening = false;
                Debug.LogError($"[McpBridge] failed to start server on ws://{Host}:{Port}: {e.Message}");
            }
        }

        public static void Stop()
        {
            _server?.Stop();
            _server = null;
            IsListening = false;
        }

        public static void Restart()
        {
            Stop();
            StartServer();
        }

        /// <summary>Change the listen port (persisted) and restart the server.</summary>
        public static void SetPort(int port)
        {
            if (port < 1024 || port > 65535)
            {
                Debug.LogWarning($"[McpBridge] port {port} out of range (1024-65535).");
                return;
            }
            Port = port;
            Restart();
        }

        // --- activity log (for the Editor window) -------------------------------

        public static string[] RecentActivity
        {
            get { lock (_logLock) return _recent.ToArray(); }
        }

        public static void ClearActivity()
        {
            lock (_logLock) _recent.Clear();
        }

        private static void LogActivity(string tool)
        {
            lock (_logLock)
            {
                _recent.Add($"{DateTime.Now:HH:mm:ss}  {tool}");
                if (_recent.Count > 50) _recent.RemoveAt(0);
            }
        }

        // --- request handling ---------------------------------------------------

        // Runs on the MAIN THREAD every Editor tick.
        private static void Pump()
        {
            while (_mainThreadJobs.TryDequeue(out var job))
            {
                try { job(); }
                catch (Exception e) { Debug.LogError($"[McpBridge] job failed: {e}"); }
            }
        }

        // Called on a BACKGROUND thread by the WebSocket server. Marshal ALL tool
        // work onto the main thread, then reply with the JSON envelope. Parsing,
        // arg binding and dispatch all live in ToolRegistry (one code path).
        private static void HandleMessage(string json, Action<string> reply)
        {
            LogActivity(TryGetTool(json));
            _mainThreadJobs.Enqueue(() =>
            {
                // ToolRegistry replies via `reply` - immediately for sync tools, or later
                // (from a callback) for async tools that take an McpToolContext.
                try { ToolRegistry.Invoke(json, reply); }
                catch (Exception e) { reply(Protocol.Error("", e.ToString())); }
            });
        }

        private static string TryGetTool(string json)
        {
            try { return (string)JObject.Parse(json)["tool"] ?? "?"; }
            catch { return "?"; }
        }

        private static void OnBeforeReload()
        {
            Stop();
        }
    }

    // Tiny response-envelope helpers shared by the bridge and (from M2) ToolRegistry.
    // Wire format (SPEC §5): { id, ok, result | error }.
    internal static class Protocol
    {
        public static string Ok(string id, string resultJson)
            => $"{{\"id\":{JsonString(id)},\"ok\":true,\"result\":{resultJson}}}";

        public static string Error(string id, string message)
            => $"{{\"id\":{JsonString(id)},\"ok\":false,\"error\":{JsonString(message)}}}";

        /// <summary>Escape a string into a quoted JSON string literal.</summary>
        public static string JsonString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
