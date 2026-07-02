// Cli.cs - the command-line front-end to the same Unity bridge.
//
// Same binary as the MCP server: launched with NO args it is an MCP stdio server
// (for Claude); launched WITH args it runs a single tool call and prints the result.
//
//   unity-agent-bridge ping
//   unity-agent-bridge create_gameobject name=Cube primitive=Cube
//   unity-agent-bridge set_property target=QuitButton componentType=Image property=color value={"r":0,"g":1,"b":0,"a":1}
//   unity-agent-bridge list
//   unity-agent-bridge --port 17890 list

using System.Text.Json;

namespace UnityAgentBridge;

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        int port = UnityClient.DefaultPort;
        bool portExplicit = false;
        string? project = null;
        var positional = new List<string>();
        var toolArgs = new Dictionary<string, object?>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--help" or "-h") { PrintHelp(); return 0; }
            if (a == "--port") { port = int.Parse(args[++i]); portExplicit = true; continue; }
            if (a == "--project") { project = args[++i]; continue; }

            var eq = a.IndexOf('=');
            if (eq > 0) toolArgs[a[..eq]] = ParseValue(a[(eq + 1)..]);
            else positional.Add(a);
        }

        if (positional.Count == 0) { PrintHelp(); return 0; }
        var tool = positional[0];

        // Resolve the port. Precedence: --port (explicit) > --project discovery >
        // $UNITY_BRIDGE_PORT > $UNITY_BRIDGE_PROJECT discovery > default. Discovery lets a
        // wrapper target a project by name and follow its port across restarts, which is
        // what a fixed port can't do when several Editors run at once.
        if (!portExplicit)
        {
            var proj = project ?? Environment.GetEnvironmentVariable("UNITY_BRIDGE_PROJECT");
            if (project == null && int.TryParse(Environment.GetEnvironmentVariable("UNITY_BRIDGE_PORT"), out var envPort))
            {
                port = envPort;
            }
            else if (!string.IsNullOrEmpty(proj))
            {
                var matches = BridgeDiscovery.Match(proj);
                if (matches.Count == 1)
                {
                    port = matches[0].Port;
                }
                else if (matches.Count == 0)
                {
                    Console.Error.WriteLine($"error: no running bridge found for project '{proj}' - is that Editor open? " +
                        $"(falling back to port {port})");
                }
                else
                {
                    Console.Error.WriteLine($"error: '{proj}' matches {matches.Count} bridges: " +
                        string.Join(", ", matches.Select(m => $"{m.Name}:{m.Port}")) + ". Use --port to disambiguate.");
                    return 1;
                }
            }
        }

        await using var client = new UnityClient(port);
        try
        {
            if (tool == "list")
            {
                var json = await client.CallAsync("list_tools", new { });
                PrintToolList(json);
                return 0;
            }

            if (toolArgs.Count > 0)
                await CoerceArrayArgsAsync(client, tool, toolArgs);

            var result = await client.CallAsync(tool, toolArgs);
            Console.WriteLine(Pretty(result));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    // Scalars are passed as their JSON type; {...}/[...] are parsed as JSON; else string.
    private static object? ParseValue(string v)
    {
        var t = v.Trim();
        if (t.Length > 0 && (t[0] == '{' || t[0] == '['))
        {
            try { return JsonDocument.Parse(t).RootElement.Clone(); } catch { /* fall through */ }
        }
        if (bool.TryParse(t, out var b)) return b;
        if (long.TryParse(t, out var l)) return l;
        if (double.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return v;
    }

    // The CLI parses key=value generically, so it can't know a param is an array - a bare
    // `levels=Error` would arrive as a scalar string and fail (Unity expects string[]). Ask
    // the bridge for the tool's schema and split array-typed args, so both `levels=Error` and
    // `levels=Error,Warning` become a JSON array. JSON array values (levels=["Error"]) pass through.
    private static async Task CoerceArrayArgsAsync(UnityClient client, string tool, Dictionary<string, object?> toolArgs)
    {
        Dictionary<string, string> paramTypes;
        try { paramTypes = await FetchParamTypesAsync(client, tool); }
        catch { return; } // schema unavailable (e.g. Unity not connected) - let the real call surface that
        if (paramTypes.Count == 0) return;

        foreach (var key in toolArgs.Keys.ToList())
        {
            if (!paramTypes.TryGetValue(key, out var type) || !type.EndsWith("[]")) continue;
            if (toolArgs[key] is not string s) continue; // already JSON/array or a non-string scalar - leave it

            var parts = s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
            toolArgs[key] = type == "string[]"
                ? parts.Cast<object?>().ToArray()
                : parts.Select(ParseValue).ToArray();
        }
    }

    // Map of param name -> declared type for one tool, from the live bridge schema (list_tools).
    private static async Task<Dictionary<string, string>> FetchParamTypesAsync(UnityClient client, string tool)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var json = await client.CallAsync("list_tools", new { });
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tools", out var tools)) return map;
        foreach (var t in tools.EnumerateArray())
        {
            if (!t.TryGetProperty("name", out var n) || n.GetString() != tool) continue;
            if (t.TryGetProperty("parameters", out var ps))
                foreach (var p in ps.EnumerateArray())
                {
                    var pn = p.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    var pt = p.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                    if (pn != null && pt != null) map[pn] = pt;
                }
            break;
        }
        return map;
    }

    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private static void PrintToolList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var t in doc.RootElement.GetProperty("tools").EnumerateArray())
            {
                var name = t.GetProperty("name").GetString();
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() : "";
                Console.WriteLine($"{name,-22} {desc}");
                if (t.TryGetProperty("parameters", out var ps))
                    foreach (var p in ps.EnumerateArray())
                    {
                        var pn = p.GetProperty("name").GetString();
                        var pt = p.GetProperty("type").GetString();
                        var opt = p.TryGetProperty("optional", out var o) && o.GetBoolean() ? " (optional)" : "";
                        Console.WriteLine($"    {pn} : {pt}{opt}");
                    }
            }
        }
        catch { Console.WriteLine(Pretty(json)); }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
@"unity-agent-bridge - CLI for the Unity MCP bridge

USAGE
  unity-agent-bridge [--port N | --project NAME] <tool> [key=value ...]
  unity-agent-bridge [--port N | --project NAME] list   list every tool and its parameters
  unity-agent-bridge --help

EXAMPLES
  unity-agent-bridge ping
  unity-agent-bridge --project NeonRunner ping
  unity-agent-bridge create_gameobject name=Cube primitive=Cube
  unity-agent-bridge add_component target=Cube componentType=Rigidbody
  unity-agent-bridge set_property target=Cube componentType=Image property=color value={""r"":0,""g"":1,""b"":0,""a"":1}
  unity-agent-bridge read_console levels=Error,Warning limit=20
  unity-agent-bridge list_scene maxDepth=3

NOTES
  - Values that look like JSON ({...}/[...]) or numbers/bools are sent as-is; anything
    else is a string.
  - Array params take a comma list (levels=Error,Warning), a single value (levels=Error),
    or JSON (levels=[""Error""]) - the CLI splits them using the tool's schema.
  - Port resolution: --port > --project (discovers the port a running Editor published,
    matching NAME by project folder or path) > $UNITY_BRIDGE_PORT > $UNITY_BRIDGE_PROJECT
    > default 17890. Use --project when several Editors run at once - each has its own port.
  - With NO arguments the same binary runs as an MCP stdio server (for Claude Code).");
    }
}
