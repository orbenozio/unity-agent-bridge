// WebSocketServer.cs - minimal RFC6455 server for the Editor side (SPEC §6).
//
// Deliberately tiny: localhost only, a single text client, no TLS, no auth.
// Accepts a connection, performs the RFC6455 handshake, then reads/writes text
// frames. Raises OnMessage(payload, reply) on a BACKGROUND thread - McpBridge is
// responsible for marshalling work onto Unity's main thread.
//
// Assumptions (fine for our protocol, keep it tiny):
//   - Each request is a single, non-fragmented text frame (our .NET client sends
//     each message with endOfMessage:true). Continuation frames are not assembled.
//   - One client at a time; a new connection replaces the previous one.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace UnityAgentBridge.Editor
{
    public sealed class WebSocketServer
    {
        private const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        // DoS guards: both the handshake headers and each frame are bounded by a
        // client-controlled length field, so cap them before allocating. A localhost
        // port is reachable by any local process, so a hostile/buggy peer must not be
        // able to drive an unbounded allocation and OOM the Editor.
        private const int MaxHeaderBytes = 16 * 1024;        // 16 KB of request headers
        private const long MaxFrameBytes = 32L * 1024 * 1024; // 32 MB per message frame

        private readonly string _host;
        private readonly int _port;
        private readonly object _sendLock = new object();

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        private TcpClient _client;
        private NetworkStream _stream;

        /// <summary>Raised on a background thread: (textPayload, replyCallback).</summary>
        public event Action<string, Action<string>> OnMessage;

        /// <summary>
        /// Handshake gate. Given the parsed (lower-cased) request headers, returns
        /// whether the connection is allowed. Set by McpBridge before Start(). This is
        /// FAIL-CLOSED: if it is left null, every handshake is rejected, so an instance
        /// created on some other path can never open an unauthenticated port.
        /// </summary>
        public Func<IReadOnlyDictionary<string, string>, BridgeAuth.Result> Authorize;

        /// <summary>True while a client is connected (for status display).</summary>
        public bool HasClient => _client != null && _client.Connected;

        public WebSocketServer(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listener = new TcpListener(IPAddress.Parse(_host), _port);
            _listener.Start();
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "McpBridge-Accept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            CloseClient();
            _listener = null;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { break; } // listener stopped / disposed

                // Single-client policy: drop any previous connection.
                CloseClient();

                NetworkStream stream = null;
                try
                {
                    client.NoDelay = true;
                    stream = client.GetStream();
                    if (!Handshake(stream))
                    {
                        client.Close();
                        continue;
                    }

                    _client = client;
                    _stream = stream;
                    var reader = new Thread(() => ReadLoop(client, stream))
                        { IsBackground = true, Name = "McpBridge-Read" };
                    reader.Start();
                }
                catch (Exception ex)
                {
                    // Don't let a handshake bug fail silently (it hid a real one once).
                    // Log the detail to the Console; send the peer only a generic 500.
                    UnityEngine.Debug.LogError("[McpBridge] handshake failed: " + ex);
                    try { if (stream != null) WriteHttpError(stream, 500, "handshake error"); } catch { /* ignore */ }
                    try { client.Close(); } catch { /* ignore */ }
                }
            }
        }

        // --- RFC6455 handshake -------------------------------------------------

        private bool Handshake(NetworkStream stream)
        {
            var headerBytes = new List<byte>(512);
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) return false;
                headerBytes.Add((byte)b);
                int n = headerBytes.Count;
                // Bound the header read: a peer that never sends CRLF CRLF must not be
                // able to grow this list without limit (OOM before any auth runs).
                if (n > MaxHeaderBytes)
                {
                    WriteHttpError(stream, 431, "request headers too large");
                    return false;
                }
                if (n >= 4 &&
                    headerBytes[n - 4] == 13 && headerBytes[n - 3] == 10 &&
                    headerBytes[n - 2] == 13 && headerBytes[n - 1] == 10)
                    break; // CRLF CRLF -> end of headers
            }

            var request = Encoding.UTF8.GetString(headerBytes.ToArray());
            var headers = ParseHeaders(request);

            // Authorization gate (token + Host pinning + Origin rejection). Reject
            // BEFORE switching protocols so an attacker gets a plain HTTP error, not
            // an open socket. Fail-closed: a missing gate denies everything. See
            // BridgeAuth for the threat model.
            if (Authorize == null)
            {
                WriteHttpError(stream, 401, "bridge is not configured for authorization");
                return false;
            }
            var verdict = Authorize(headers);
            if (!verdict.Ok)
            {
                WriteHttpError(stream, verdict.Status, verdict.Reason);
                return false;
            }

            if (!headers.TryGetValue("sec-websocket-key", out var key) || string.IsNullOrEmpty(key))
                return false;
            key = key.Trim();
            string accept;
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(key + MagicGuid));
                accept = Convert.ToBase64String(hash);
            }

            var response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            var respBytes = Encoding.UTF8.GetBytes(response);
            stream.Write(respBytes, 0, respBytes.Length);
            stream.Flush();
            return true;
        }

        // Parse the request line + headers into a case-insensitive (lower-cased keys)
        // map. Duplicate headers keep the first value; that's fine for what we read.
        private static Dictionary<string, string> ParseHeaders(string request)
        {
            var headers = new Dictionary<string, string>();
            var lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 1; i < lines.Length; i++) // skip the request line
            {
                var line = lines[i];
                if (line.Length == 0) break; // end of headers
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (!headers.ContainsKey(name)) headers[name] = value;
            }
            return headers;
        }

        private static void WriteHttpError(NetworkStream stream, int status, string reason)
        {
            string text = status == 401 ? "Unauthorized" : status == 403 ? "Forbidden" : "Bad Request";
            var body = Encoding.UTF8.GetBytes(reason ?? text);
            var response =
                $"HTTP/1.1 {status} {text}\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n";
            try
            {
                var head = Encoding.UTF8.GetBytes(response);
                stream.Write(head, 0, head.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
            catch { /* peer gone */ }
        }

        // --- frame read loop ---------------------------------------------------

        private void ReadLoop(TcpClient client, NetworkStream stream)
        {
            try
            {
                while (_running && client.Connected)
                {
                    var frame = ReadFrame(stream);
                    if (frame == null) break; // closed / error

                    switch (frame.Opcode)
                    {
                        case 0x1: // text
                            var text = Encoding.UTF8.GetString(frame.Payload);
                            OnMessage?.Invoke(text, SendText);
                            break;
                        case 0x8: // close
                            SendFrame(0x8, Array.Empty<byte>());
                            return;
                        case 0x9: // ping -> pong
                            SendFrame(0xA, frame.Payload);
                            break;
                        case 0xA: // pong
                            break;
                        default:
                            break; // ignore binary / continuation in v1
                    }
                }
            }
            catch { /* connection error */ }
            finally
            {
                if (ReferenceEquals(_client, client)) CloseClient();
            }
        }

        private sealed class Frame
        {
            public int Opcode;
            public byte[] Payload;
        }

        private static Frame ReadFrame(NetworkStream stream)
        {
            int b0 = stream.ReadByte();
            if (b0 == -1) return null;
            int b1 = stream.ReadByte();
            if (b1 == -1) return null;

            int opcode = b0 & 0x0F;
            bool masked = (b1 & 0x80) != 0;
            // RFC6455 §5.3: every client-to-server frame MUST be masked. A peer sending an
            // unmasked frame is broken or hostile; drop the connection rather than treat
            // raw bytes as payload. (Returning null tears the connection down upstream.)
            if (!masked) return null;
            long len = b1 & 0x7F;

            if (len == 126)
            {
                var ext = ReadExact(stream, 2);
                if (ext == null) return null;
                len = (ext[0] << 8) | ext[1];
            }
            else if (len == 127)
            {
                var ext = ReadExact(stream, 8);
                if (ext == null) return null;
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | ext[i];
            }

            // Reject before allocating. A 64-bit length is client-controlled: without
            // this an oversized (or, after the int cast, negative) length would let any
            // local peer trigger a multi-GB allocation and crash the Editor.
            if (len < 0 || len > MaxFrameBytes) return null;

            var mask = ReadExact(stream, 4);
            if (mask == null) return null;

            byte[] payload;
            if (len > 0)
            {
                payload = ReadExact(stream, (int)len);
                if (payload == null) return null;
            }
            else
            {
                payload = Array.Empty<byte>();
            }

            // Client frames are masked (guaranteed above); unmask in place.
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(payload[i] ^ mask[i % 4]);

            return new Frame { Opcode = opcode, Payload = payload };
        }

        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            var buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int r = stream.Read(buf, read, count - read);
                if (r <= 0) return null;
                read += r;
            }
            return buf;
        }

        // --- frame write -------------------------------------------------------

        private void SendText(string text)
        {
            SendFrame(0x1, Encoding.UTF8.GetBytes(text));
        }

        // Server -> client frames are NOT masked (RFC6455 §5.1).
        private void SendFrame(int opcode, byte[] payload)
        {
            var stream = _stream;
            if (stream == null) return;

            lock (_sendLock)
            {
                try
                {
                    // Header is at most 10 bytes (1 opcode + up to 9 length bytes); build
                    // it in a fixed stack buffer to avoid a per-reply List/ToArray alloc.
                    var header = new byte[10];
                    int h = 0;
                    header[h++] = (byte)(0x80 | (opcode & 0x0F)); // FIN + opcode
                    int len = payload.Length;
                    if (len <= 125)
                    {
                        header[h++] = (byte)len;
                    }
                    else if (len <= 0xFFFF)
                    {
                        header[h++] = 126;
                        header[h++] = (byte)((len >> 8) & 0xFF);
                        header[h++] = (byte)(len & 0xFF);
                    }
                    else
                    {
                        header[h++] = 127;
                        for (int i = 7; i >= 0; i--) header[h++] = (byte)((len >> (8 * i)) & 0xFF);
                    }

                    stream.Write(header, 0, h);
                    if (len > 0) stream.Write(payload, 0, len);
                    stream.Flush();
                }
                catch { /* peer gone */ }
            }
        }

        private void CloseClient()
        {
            try { _stream?.Close(); } catch { /* ignore */ }
            try { _client?.Close(); } catch { /* ignore */ }
            _stream = null;
            _client = null;
        }
    }
}
