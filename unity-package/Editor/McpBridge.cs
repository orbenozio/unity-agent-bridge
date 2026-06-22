// McpBridge.cs — Editor lifecycle, WebSocket server, main-thread pump, dispatch.
//
// This is the HEART of the project (SPEC §3, §4, §6). It:
//   - Starts a WebSocket server on ws://127.0.0.1:17890 when the Editor loads.
//   - Receives requests on a background thread and ENQUEUES them.
//   - Drains the queue on EditorApplication.update — i.e. the MAIN THREAD —
//     because nearly all Unity APIs require it.
//   - Tears down cleanly before a domain reload so the socket/port is released;
//     [InitializeOnLoad] re-runs this ctor on the new domain and restarts it.

using System;
using System.Collections.Concurrent;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor
{
    [InitializeOnLoad]
    public static class McpBridge
    {
        private const string Host = "127.0.0.1";
        private const int Port = 17890;

        private static readonly ConcurrentQueue<Action> _mainThreadJobs = new();
        private static WebSocketServer _server;

        static McpBridge()
        {
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;

            StartServer();
            // TODO(M2): install the Console ring buffer (Application.logMessageReceivedThreaded).
        }

        private static void StartServer()
        {
            try
            {
                _server = new WebSocketServer(Host, Port);
                _server.OnMessage += HandleMessage;
                _server.Start();
                Debug.Log($"[McpBridge] listening on ws://{Host}:{Port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[McpBridge] failed to start server on ws://{Host}:{Port}: {e.Message}");
            }
        }

        // Runs on the MAIN THREAD every Editor tick.
        private static void Pump()
        {
            while (_mainThreadJobs.TryDequeue(out var job))
            {
                try { job(); }
                catch (Exception e) { Debug.LogError($"[McpBridge] job failed: {e}"); }
            }
        }

        // Called on a BACKGROUND thread by the WebSocket server. Parse, marshal the
        // actual work onto the main thread, then reply with the JSON envelope.
        private static void HandleMessage(string json, Action<string> reply)
        {
            Request req;
            try { req = JsonUtility.FromJson<Request>(json); }
            catch (Exception e) { reply(Protocol.Error("", "bad request json: " + e.Message)); return; }

            if (req == null || string.IsNullOrEmpty(req.tool))
            {
                reply(Protocol.Error(req?.id ?? "", "missing tool"));
                return;
            }

            _mainThreadJobs.Enqueue(() =>
            {
                string response;
                try { response = Dispatch(req); }
                catch (Exception e) { response = Protocol.Error(req.id, e.ToString()); }
                reply(response);
            });
        }

        // MAIN THREAD. M1 ships a single built-in tool: ping.
        // TODO(M2): route unknown tools through ToolRegistry.Invoke(json).
        private static string Dispatch(Request req)
        {
            switch (req.tool)
            {
                case "ping":
                    var result = $"{{\"pong\":true,\"unityVersion\":{Protocol.JsonString(Application.unityVersion)}}}";
                    return Protocol.Ok(req.id, result);
                default:
                    return Protocol.Error(req.id, $"unknown tool: {req.tool}");
            }
        }

        private static void OnBeforeReload()
        {
            _server?.Stop();
            _server = null;
        }

        [Serializable]
        private class Request
        {
            public string id;
            public string tool;
            // `args` is intentionally not parsed here in M1; ToolRegistry binds it in M2.
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
