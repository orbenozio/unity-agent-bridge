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
using System.Threading;
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
        private static readonly List<Act> _recent = new List<Act>();
        private static WebSocketServer _server;

        // One activity entry. A class so the reply closure can flip `error` later, even
        // after the entry has scrolled out of the capped list.
        private sealed class Act { public string text; public bool error; }

        /// <summary>An activity-log line plus whether that request failed.</summary>
        public readonly struct ActivityEntry
        {
            public readonly string Text;
            public readonly bool IsError;
            public ActivityEntry(string text, bool isError) { Text = text; IsError = isError; }
        }

        /// <summary>
        /// Configured listen port, persisted PER PROJECT (EditorUserSettings, not the
        /// global EditorPrefs) so two Editor instances keep separate ports instead of
        /// fighting over one shared value.
        /// </summary>
        public static int Port
        {
            get => int.TryParse(EditorUserSettings.GetConfigValue(PortPrefKey), out var p) ? p : DefaultPort;
            private set => EditorUserSettings.SetConfigValue(PortPrefKey, value.ToString());
        }

        public static bool IsListening { get; private set; }
        public static bool ClientConnected => _server != null && _server.HasClient;

        static McpBridge()
        {
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += BridgeDiscovery.Unpublish;

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
                // Bind starting at the configured port, so a SECOND Editor instance lands
                // on the next port instead of silently failing. The chosen port is
                // persisted (per project) and shown in the window.
                var desired = Port;
                var bound = BindListener(desired, 10);
                if (bound == null)
                {
                    IsListening = false;
                    Debug.LogError($"[McpBridge] no free port in {desired}-{desired + 9}; bridge not listening.");
                    return;
                }
                var (server, port) = bound.Value;
                if (port != desired)
                {
                    Port = port; // remember this project's port for next launch
                    Debug.Log($"[McpBridge] port {desired} was busy; using {port} for this project.");
                }

                // Provision the shared-secret token and wire the auth gate BEFORE we begin
                // accepting, so the very first client is served with auth in place (a
                // connection that raced in during bind waits in the OS backlog until now).
                // Capture port/token HERE on the main thread: the Authorize delegate runs on
                // the accept (background) thread, where EditorUserSettings would throw.
                var token = BridgeAuth.EnsureToken(port);
                server.OnMessage += HandleMessage;
                server.Authorize = headers => BridgeAuth.Validate(headers, token, Host, port);
                server.BeginAccept();

                _server = server;
                IsListening = true;
                // Publish the live port so a CLI/MCP client can discover it by --project
                // instead of hard-coding it - essential when several Editors run at once.
                BridgeDiscovery.Publish(port);
                Debug.Log($"[McpBridge] listening on ws://{Host}:{port} (auth on; token at {BridgeAuth.TokenPath(port)})");
            }
            catch (Exception e)
            {
                IsListening = false;
                Debug.LogError($"[McpBridge] failed to start server on ws://{Host}:{Port}: {e.Message}");
            }
        }

        // Bind the REAL listener on `desired`, retrying THAT port briefly before climbing.
        // Binding the real socket once (no separate probe-then-rebind) removes the TOCTOU
        // gap where the port was grabbed between the probe and the bind - the failure that
        // left the bridge dead on EADDRINUSE. The short retry on the desired port reclaims
        // our OWN previous-domain listener while it finishes releasing after a reload, so we
        // keep the SAME port instead of migrating; a port a DIFFERENT process holds stays
        // busy through the whole retry window and makes us climb (multi-Editor coexistence).
        private static (WebSocketServer server, int port)? BindListener(int desired, int tries)
        {
            for (int p = desired; p < desired + tries && p <= 65535; p++)
            {
                if (p < 1024) continue;
                int attempts = (p == desired) ? 8 : 1; // reclaim our own port; don't stall climbing
                for (int a = 0; a < attempts; a++)
                {
                    var server = new WebSocketServer(Host, p);
                    if (server.TryBind()) return (server, p);
                    if (a + 1 < attempts) Thread.Sleep(60); // ~0.5s max, only when the port is contended
                }
            }
            return null;
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

        public static ActivityEntry[] RecentActivity
        {
            get
            {
                lock (_logLock)
                {
                    var arr = new ActivityEntry[_recent.Count];
                    for (int i = 0; i < _recent.Count; i++)
                        arr[i] = new ActivityEntry(_recent[i].text, _recent[i].error);
                    return arr;
                }
            }
        }

        public static void ClearActivity()
        {
            lock (_logLock) _recent.Clear();
        }

        private static Act LogActivity(string tool)
        {
            var a = new Act { text = $"{DateTime.Now:HH:mm:ss}  {tool}", error = false };
            lock (_logLock)
            {
                _recent.Add(a);
                if (_recent.Count > 50) _recent.RemoveAt(0);
            }
            return a;
        }

        private static bool ResponseIsError(string resp)
        {
            try { return JObject.Parse(resp)["ok"]?.Value<bool>() == false; }
            catch { return false; }
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
            // Parse the request ONCE here and pass the JObject through to dispatch, rather
            // than re-parsing it to read the tool name and again inside ToolRegistry.
            JObject root = null;
            try { root = JObject.Parse(json); } catch { /* malformed - handled below */ }

            var act = LogActivity((string)root?["tool"] ?? "?");
            // Wrap the reply so we can mark the activity entry red on a failure.
            Action<string> tracked = resp =>
            {
                if (ResponseIsError(resp)) lock (_logLock) act.error = true;
                reply(resp);
            };
            _mainThreadJobs.Enqueue(() =>
            {
                // ToolRegistry replies via `tracked` - immediately for sync tools, or later
                // (from a callback) for async tools that take an McpToolContext.
                try
                {
                    if (root == null) { tracked(Protocol.Error("", "request was not valid JSON")); return; }
                    ToolRegistry.Invoke(root, tracked);
                }
                catch (Exception e) { tracked(Protocol.Error("", e.ToString())); }
            });
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
