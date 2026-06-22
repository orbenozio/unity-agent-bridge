// WebSocketServer.cs — minimal RFC6455 server for the Editor side (SPEC §6).
//
// Deliberately tiny: localhost only, a single text client, no TLS, no auth.
// Accepts a connection, performs the RFC6455 handshake, then reads/writes text
// frames. Raises OnMessage(payload, reply) on a BACKGROUND thread — McpBridge is
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
using System.Text.RegularExpressions;
using System.Threading;

namespace UnityMcpBridge.Editor
{
    public sealed class WebSocketServer
    {
        private const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

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

                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();
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
                catch
                {
                    try { client.Close(); } catch { /* ignore */ }
                }
            }
        }

        // --- RFC6455 handshake -------------------------------------------------

        private static bool Handshake(NetworkStream stream)
        {
            var headerBytes = new List<byte>(512);
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) return false;
                headerBytes.Add((byte)b);
                int n = headerBytes.Count;
                if (n >= 4 &&
                    headerBytes[n - 4] == 13 && headerBytes[n - 3] == 10 &&
                    headerBytes[n - 2] == 13 && headerBytes[n - 1] == 10)
                    break; // CRLF CRLF -> end of headers
            }

            var request = Encoding.UTF8.GetString(headerBytes.ToArray());
            var match = Regex.Match(request, @"Sec-WebSocket-Key:\s*(.+)", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            var key = match.Groups[1].Value.Trim();
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

            byte[] mask = null;
            if (masked)
            {
                mask = ReadExact(stream, 4);
                if (mask == null) return null;
            }

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

            // Client frames are masked (RFC6455 §5.3); unmask in place.
            if (masked)
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
                    var header = new List<byte>(10) { (byte)(0x80 | (opcode & 0x0F)) }; // FIN + opcode
                    int len = payload.Length;
                    if (len <= 125)
                    {
                        header.Add((byte)len);
                    }
                    else if (len <= 0xFFFF)
                    {
                        header.Add(126);
                        header.Add((byte)((len >> 8) & 0xFF));
                        header.Add((byte)(len & 0xFF));
                    }
                    else
                    {
                        header.Add(127);
                        for (int i = 7; i >= 0; i--) header.Add((byte)((len >> (8 * i)) & 0xFF));
                    }

                    stream.Write(header.ToArray(), 0, header.Count);
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
