// McpBridge.cs — Editor lifecycle, WebSocket server, main-thread pump, dispatch.
//
// This is the HEART of the project (SPEC §3, §4, §6). It:
//   - Starts a WebSocket server on ws://127.0.0.1:17890 when the Editor loads.
//   - Receives requests on a background thread and ENQUEUES them.
//   - Drains the queue on EditorApplication.update — i.e. the MAIN THREAD —
//     because nearly all Unity APIs require it.
//   - Tears down / restarts cleanly across domain reloads so the socket survives.

using System;
using System.Collections.Concurrent;
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
        // private static WebSocketServer _server;   // TODO(M1)

        static McpBridge()
        {
            // TODO(M1): create & start the WebSocket server; wire OnMessage -> HandleMessage.
            // TODO(M2): install the Console ring buffer (Application.logMessageReceivedThreaded).
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload; // TODO(M4): _server?.Stop();
            AssemblyReloadEvents.afterAssemblyReload  += OnAfterReload;  // TODO(M4): _server?.Start();

            Debug.Log($"[McpBridge] scaffold loaded — will listen on ws://{Host}:{Port} once M1 is implemented.");
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

        // Called on a BACKGROUND thread by the WebSocket server. Only enqueues.
        // TODO(M1): parse { id, tool, args }, enqueue ToolRegistry.Invoke, reply with JSON.
        // private static void HandleMessage(string json, Action<string> reply) { ... }

        private static void OnBeforeReload() { /* TODO(M4): stop server cleanly */ }
        private static void OnAfterReload()  { /* TODO(M4): restart server */ }
    }
}
