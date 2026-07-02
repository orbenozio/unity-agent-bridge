// BridgeDiscovery.cs (server) - the read-only half of port discovery.
//
// Unity publishes each open project's live bridge port to a per-project file under
// ~/.unity-agent-bridge/projects/ (see the Unity-side BridgeDiscovery). This side reads
// that directory and resolves a --project <name|path> to the right port, so a client
// does NOT have to hard-code a port that shifts when several Editors run in parallel.

using System.Text.Json;

namespace UnityAgentBridge;

public static class BridgeDiscovery
{
    public sealed record Entry(string Project, string Name, int Port, string? Updated);

    private static string Dir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".unity-agent-bridge", "projects");
    }

    /// <summary>Every discovery entry currently on disk (skips unreadable/partial files).</summary>
    public static IReadOnlyList<Entry> All()
    {
        var list = new List<Entry>();
        var dir = Dir();
        if (!Directory.Exists(dir)) return list;

        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                var port = r.TryGetProperty("port", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
                if (port <= 0) continue;
                var project = r.TryGetProperty("project", out var pr) ? pr.GetString() ?? "" : "";
                var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var updated = r.TryGetProperty("updated", out var u) ? u.GetString() : null;
                list.Add(new Entry(project, name, port, updated));
            }
            catch { /* skip a bad/partly-written file rather than fail resolution */ }
        }
        return list;
    }

    /// <summary>
    /// Resolve a --project value - a project NAME/leaf (e.g. "NeonRunner") or a full/partial
    /// PATH - to the matching discovery entries. Returns all matches so the caller can report
    /// ambiguity clearly instead of guessing.
    /// </summary>
    public static List<Entry> Match(string project)
    {
        var arg = Norm(project);
        var leaf = Leaf(arg);
        var matches = new List<Entry>();
        foreach (var e in All())
        {
            var proj = Norm(e.Project);
            var name = e.Name.ToLowerInvariant();
            if (name == leaf || proj == arg || proj == leaf || proj.EndsWith("/" + leaf))
                matches.Add(e);
        }
        return matches;
    }

    // Normalize a path/name for comparison: forward slashes, no trailing slash, lower-case,
    // and git-bash "/c/foo" -> "c:/foo" so a wrapper path matches Unity's "C:\foo".
    private static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var p = s.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        if (p.Length >= 3 && p[0] == '/' && char.IsLetter(p[1]) && p[2] == '/')
            p = p[1] + ":" + p.Substring(2);
        return p;
    }

    private static string Leaf(string norm)
    {
        var i = norm.LastIndexOf('/');
        return i < 0 ? norm : norm[(i + 1)..];
    }
}
