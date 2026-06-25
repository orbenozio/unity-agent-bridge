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
//   3. ORIGIN/REFERER rejection. Native clients (our .NET ClientWebSocket) send
//      neither header; browsers set at least one. Presence of either -> 403, killing
//      the browser vector.
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
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, token);
            // The token IS the trust boundary; if it is world-readable any other local
            // user can replay it and drive the bridge (which can compile & run C#).
            // Lock the file (and its dir) down to the owner. Best-effort: never let a
            // permissions hiccup fail provisioning.
            TryRestrictToOwner(dir, path);
            return token;
        }

        // On Unix-like systems the default umask leaves new files world-readable, so
        // chmod the token to owner-only. On Windows the user-profile tree already
        // inherits an owner-restricted ACL, so this is a no-op there.
        private static void TryRestrictToOwner(string dir, string file)
        {
            try
            {
                var platform = Environment.OSVersion.Platform;
                if (platform != PlatformID.Unix && platform != PlatformID.MacOSX) return;
                RunChmod("700", dir);
                RunChmod("600", file);
            }
            catch { /* permissions are best-effort */ }
        }

        private static void RunChmod(string mode, string target)
        {
            try
            {
                // Build argv via ArgumentList, never a single Arguments string: the path
                // is derived from the user-profile dir, and manual quoting could be broken
                // by an odd home path to inject extra chmod arguments. ArgumentList passes
                // each token verbatim with no shell/quote parsing.
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    // Resolve chmod by absolute path so a hostile entry earlier in PATH
                    // can't shadow it with a malicious binary during provisioning.
                    FileName = ResolveChmodPath(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(mode);
                psi.ArgumentList.Add(target);
                using var p = System.Diagnostics.Process.Start(psi);
                // Runs once at token provisioning, not per-handshake; bound the wait so a
                // stuck chmod can't hang Editor startup.
                p?.WaitForExit(1000);
            }
            catch { /* chmod unavailable - best-effort */ }
        }

        // Prefer an absolute chmod over the PATH-resolved name; fall back to the bare
        // name only if neither standard location exists.
        private static string ResolveChmodPath()
        {
            if (File.Exists("/bin/chmod")) return "/bin/chmod";
            if (File.Exists("/usr/bin/chmod")) return "/usr/bin/chmod";
            return "chmod";
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
            // 3. Browser vector: native clients send neither Origin nor Referer; browsers
            //    set at least one. Presence of either kills the browser-originated path.
            if (headers.ContainsKey("origin") || headers.ContainsKey("referer"))
                return Result.Deny(403, "Origin/Referer header present (browser-originated connections are not allowed).");

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

        /// <summary>
        /// Constant-time compare of a presented token against the expected one. Both
        /// sides are first hashed to a fixed 32-byte SHA-256 digest, so the byte compare
        /// is always over 32 bytes regardless of the presented length - this removes the
        /// length channel entirely - and the compare itself is delegated to the
        /// platform's vetted constant-time primitive.
        /// </summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            // An empty/absent expected secret is a misconfiguration: never authorize.
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            using var sha = SHA256.Create();
            var ha = sha.ComputeHash(Encoding.UTF8.GetBytes(a)); // attacker-presented
            var hb = sha.ComputeHash(Encoding.UTF8.GetBytes(b)); // expected secret
            return CryptographicOperations.FixedTimeEquals(ha, hb);
        }
    }
}
