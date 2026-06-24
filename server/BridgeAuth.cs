// BridgeAuth.cs (server) - the read-only half of the bridge security model.
//
// Unity is the source of truth: it generates a random token into a per-user,
// per-port file and validates it on the WebSocket handshake (see the Unity-side
// BridgeAuth for the full threat model: token + Host pinning + Origin rejection).
// The server's only job here is to find that same file and present the token as a
// header when it connects. The path is computed identically on both sides from the
// user profile, so nothing has to be passed between the two processes.

namespace UnityAgentBridge;

public static class BridgeAuth
{
    public const string TokenHeader = "X-Unity-Bridge-Token";

    public static string TokenPath(int port)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".unity-agent-bridge", $"bridge-{port}.token");
    }

    /// <summary>
    /// Read the current token for this port, or null if Unity has not provisioned it
    /// yet (e.g. the Editor is not open). Read fresh on every connect attempt so the
    /// server picks the token up as soon as the Editor comes online.
    /// </summary>
    public static string? ReadToken(int port)
    {
        try
        {
            var path = TokenPath(port);
            if (!File.Exists(path)) return null;
            var token = File.ReadAllText(path).Trim();
            return token.Length > 0 ? token : null;
        }
        catch
        {
            return null;
        }
    }
}
