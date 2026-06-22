// UnityClient.cs — the WebSocket pipe from the MCP server to the Unity bridge.
//
// Responsibilities (see SPEC.md §4 and §7, MILESTONES M1 & M4):
//   - Maintain a single ClientWebSocket to ws://127.0.0.1:17890.
//   - CallAsync(tool, args): id-correlate request/response via TaskCompletionSource.
//   - Background receive loop resolves pending calls by id.
//   - M1: lazy connect on first call + a per-call timeout.
//   - M4: exponential backoff reconnect + PARK calls made while disconnected.

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace UnityMcpBridge;

public enum ConnectionState { Connected, Reconnecting, Down }

public sealed class UnityClient : IAsyncDisposable
{
    private const string Url = "ws://127.0.0.1:17890";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;

    public ConnectionState State { get; private set; } = ConnectionState.Down;

    /// <summary>Send a tool request to Unity and await its JSON response.</summary>
    /// <returns>The `result` object as a JSON string on success.</returns>
    public async Task<string> CallAsync(string tool, object args, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            var envelope = JsonSerializer.Serialize(new { id, tool, args });
            await SendTextAsync(envelope, ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);
            using (timeout.Token.Register(static s =>
            {
                ((TaskCompletionSource<string>)s!).TrySetException(
                    new TimeoutException("Unity did not respond within the call timeout."));
            }, tcs))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    // --- connection --------------------------------------------------------

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open }) return;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_ws is { State: WebSocketState.Open }) return;

            _receiveCts?.Cancel();
            _ws?.Dispose();

            var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(Url), ct);
            }
            catch (Exception e)
            {
                State = ConnectionState.Down;
                ws.Dispose();
                throw new InvalidOperationException(
                    $"Could not connect to the Unity bridge at {Url}. " +
                    "Is the Unity 6 Editor open with the unity-mcp-bridge package loaded? " +
                    $"({e.Message})");
            }

            _ws = ws;
            State = ConnectionState.Connected;
            _receiveCts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoopAsync(ws, _receiveCts.Token));
            Log($"connected to Unity bridge at {Url}");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task SendTextAsync(string text, CancellationToken ct)
    {
        var ws = _ws ?? throw new InvalidOperationException("not connected");
        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(ct);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct); }
        finally { _sendLock.Release(); }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnDisconnected("Unity closed the connection");
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                Dispatch(sb.ToString());
            }
        }
        catch (OperationCanceledException) { /* shutting down / reconnecting */ }
        catch (Exception e) { OnDisconnected(e.Message); }
    }

    private void Dispatch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl)) return;
            var id = idEl.GetString();
            if (id is null || !_pending.TryRemove(id, out var tcs)) return;

            var ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            if (ok)
            {
                var result = root.TryGetProperty("result", out var r) ? r.GetRawText() : "{}";
                tcs.TrySetResult(result);
            }
            else
            {
                var err = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown error";
                tcs.TrySetException(new InvalidOperationException(err ?? "unknown error"));
            }
        }
        catch (Exception e)
        {
            Log("dropping unparseable response: " + e.Message);
        }
    }

    private void OnDisconnected(string reason)
    {
        State = ConnectionState.Down;
        Log("disconnected: " + reason);
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(new InvalidOperationException("Unity bridge disconnected: " + reason));
    }

    private static void Log(string msg) => Console.Error.WriteLine("[unity-mcp-bridge] " + msg);

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();
        if (_ws is { State: WebSocketState.Open })
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None); }
            catch { /* ignore */ }
        }
        _ws?.Dispose();
    }
}
