// WebSocketServer.cs — minimal RFC6455 server for the Editor side (SPEC §6).
//
// Deliberately tiny: localhost only, a single text client, no TLS, no auth.
// Accepts a connection, performs the RFC6455 handshake, then reads/writes text
// frames. Raises OnMessage(payload, reply) on a BACKGROUND thread — McpBridge is
// responsible for marshalling work onto Unity's main thread.
//
// Keep this ~200 LOC. It is not a general-purpose WS library.

using System;

namespace UnityMcpBridge.Editor
{
    public sealed class WebSocketServer
    {
        private readonly string _host;
        private readonly int _port;

        /// <summary>Raised on a background thread: (textPayload, replyCallback).</summary>
        public event Action<string, Action<string>> OnMessage;

        public WebSocketServer(string host, int port)
        {
            _host = host;
            _port = port;
        }

        // TODO(M1): TcpListener on _host:_port; accept; RFC6455 handshake
        //           (Sec-WebSocket-Accept = base64(sha1(key + GUID))); frame read/write loop.
        public void Start() { /* TODO(M1) */ }
        public void Stop()  { /* TODO(M1)/M4: close handshake, dispose listener */ }
    }
}
