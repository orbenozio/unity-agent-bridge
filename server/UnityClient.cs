// UnityClient.cs — the WebSocket pipe from the MCP server to the Unity bridge.
//
// Responsibilities (see SPEC.md §4 and §7, MILESTONES M1 & M4):
//   - Maintain a single ClientWebSocket to ws://127.0.0.1:17890.
//   - CallAsync(tool, args): id-correlate request/response via TaskCompletionSource.
//   - Background receive loop resolves pending calls by id.
//   - Reconnect with exponential backoff; PARK calls made while disconnected (M4).
//   - Expose ConnectionState for the unity_status tool.

using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace UnityMcpBridge;

public enum ConnectionState { Connected, Reconnecting, Down }

public sealed class UnityClient
{
    private const string Url = "ws://127.0.0.1:17890";
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public ConnectionState State { get; private set; } = ConnectionState.Down;

    // TODO(M1): connect, run receive loop, implement CallAsync with id correlation.
    // TODO(M4): exponential backoff reconnect (250ms..4s) + parked-request queue + per-call timeout.

    /// <summary>Send a tool request to Unity and await its JSON response.</summary>
    public Task<string> CallAsync(string tool, object args, CancellationToken ct = default)
    {
        // TODO(M1): build { id, tool, args }, register TCS in _pending, send, await.
        throw new NotImplementedException("UnityClient.CallAsync — implement in M1 (SPEC §7).");
    }
}
