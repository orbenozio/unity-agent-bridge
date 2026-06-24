// BridgeAuth.cs - the security model that turns the bridge from a demo into a
// product you can leave running. The WebSocket port is localhost-only, but
// localhost is NOT a trust boundary: any other process running as the same user,
// and (crucially) any web page in a browser via `new WebSocket("ws://127.0.0.1:…")`,
// can reach it. We close that with three cheap, fail-closed checks at handshake:
//
//   1. Shared-secret TOKEN. Unity auto-generates a random token into a per-user,
//      per-port file. The MCP server reads the same file and presents it as a
//      header. No token / wrong token -> 401. This blesses the one client we trust
//      and is auto-provisioned, so there is zero manual setup.
//   2. HOST header pinning (anti DNS-rebinding). A malicious site can resolve its
//      domain to 127.0.0.1, but the browser still sends `Host: attacker.com`. We
//      accept only 127.0.0.1/localhost:<port> -> everything else is 403.
//   3. ORIGIN rejection. Native clients (our .NET ClientWebSocket) send no Origin;
//      browsers always do. Presence of Origin -> 403, killing the browser vector.
//
// The token file lives at:  ~/.unity-agent-bridge/bridge-<port>.token
// computed identically on both sides from SpecialFolder.UserProfile so the .NET 8
// server and Unity's Mono agree without any path being passed between them.

using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace UnityAgentBridge.Editor
{
    /// <summary>Handshake authorization shared by the bridge and the MCP server.</summary>
    public static class BridgeAuth
    {
        public const string TokenHeader = "X-Unity-Bridge-Token";

        /// <summary>Per-user, per-port token file path (identical on both sides).</summary>
        public static string TokenPath(int port)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".unity-agent-bridge", $"bridge-{port}.token");
        }

        /// <summary>
        /// Return the token for this port, generating and persisting a fresh random
        /// one if the file is missing or empty. Unity is the source of truth; it must
        /// call this BEFORE the listener starts so the server can read it on connect.
        /// </summary>
        public static string EnsureToken(int port)
        {
            var path = TokenPath(port);
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length > 0) return existing;
                }
            }
            catch { /* unreadable -> regenerate below */ }

            var token = NewToken();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, token);
            return token;
        }

        private static string NewToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            // url-safe base64, no padding - header-friendly.
            return Convert.ToBase64String(bytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Outcome of validating a handshake's headers.</summary>
        public readonly struct Result
        {
            public readonly bool Ok;
            public readonly int Status;   // HTTP status to return on rejection
            public readonly string Reason;
            public Result(bool ok, int status, string reason) { Ok = ok; Status = status; Reason = reason; }
            public static readonly Result Allow = new Result(true, 0, null);
            public static Result Deny(int status, string reason) => new Result(false, status, reason);
        }

        /// <summary>
        /// Apply all three checks to the parsed handshake headers. Header keys are
        /// expected lower-cased by the caller.
        /// </summary>
        public static Result Validate(IReadOnlyDictionary<string, string> headers, string expectedToken, string host, int port)
        {
            // 3. Browser vector: native clients never send Origin.
            if (headers.ContainsKey("origin"))
                return Result.Deny(403, "Origin header present (browser-originated connections are not allowed).");

            // 2. Anti DNS-rebinding: pin the Host header to our loopback endpoint.
            if (!headers.TryGetValue("host", out var hostHeader) || !IsAllowedHost(hostHeader, host, port))
                return Result.Deny(403, $"Host header '{(headers.TryGetValue("host", out var h) ? h : "")}' is not an accepted loopback host.");

            // 1. Shared secret.
            if (!headers.TryGetValue(TokenHeaderLower, out var presented) ||
                !FixedTimeEquals(presented, expectedToken))
                return Result.Deny(401, "Missing or invalid bridge token.");

            return Result.Allow;
        }

        private const string TokenHeaderLower = "x-unity-bridge-token";

        private static bool IsAllowedHost(string hostHeader, string host, int port)
        {
            if (string.IsNullOrEmpty(hostHeader)) return false;
            hostHeader = hostHeader.Trim();
            return hostHeader.Equals($"{host}:{port}", StringComparison.OrdinalIgnoreCase)
                || hostHeader.Equals($"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
                || hostHeader.Equals($"localhost:{port}", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Length-constant string compare to avoid leaking the token via timing.</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length) return false;
            int diff = 0;
            for (int i = 0; i < ba.Length; i++) diff |= ba[i] ^ bb[i];
            return diff == 0;
        }
    }
}
