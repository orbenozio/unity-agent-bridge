// McpTools.cs — thin MCP -> Unity forwarders. NO Unity logic here (SPEC §7).
//
// Each method is exposed to Claude as an MCP tool and simply forwards to the
// Unity bridge via UnityClient.CallAsync. Keep arg names identical to the wire
// `args` consumed by the matching [McpTool] method on the Unity side.

using System.ComponentModel;
using ModelContextProtocol.Server;   // TODO(M1): confirm namespace against the installed SDK version.

namespace UnityMcpBridge;

[McpServerToolType]
public class UnityMcpTools(UnityClient unity)
{
    [McpServerTool, Description("Health check: ping the Unity bridge; returns Unity version.")]
    public Task<string> ping() => unity.CallAsync("ping", new { });

    [McpServerTool, Description("Read recent Unity Console log entries (Error, Warning, Log) with stack traces.")]
    public Task<string> read_console(
        [Description("Levels to include: Error, Warning, Log. Default: all.")] string[]? levels = null,
        [Description("Max entries to return.")] int limit = 50)
        => unity.CallAsync("read_console", new { levels, limit });

    [McpServerTool, Description("Create a GameObject (optionally a primitive: Cube|Sphere|Capsule|Cylinder|Plane|Quad|None) in the active scene.")]
    public Task<string> create_gameobject(string name, string primitive = "None")
        => unity.CallAsync("create_gameobject", new { name, primitive });

    [McpServerTool, Description("Add a component (e.g. Rigidbody, BoxCollider) to a GameObject by name or instanceId.")]
    public Task<string> add_component(string target, string componentType)
        => unity.CallAsync("add_component", new { target, componentType });

    [McpServerTool, Description("Enter Play Mode for N seconds, then exit; returns any errors logged during play.")]
    public Task<string> run_playmode(double seconds = 3)
        => unity.CallAsync("run_playmode", new { seconds });

    [McpServerTool, Description("Run Unity tests (EditMode or PlayMode) and return pass/fail results.")]
    public Task<string> run_tests(string platform = "EditMode", string? filter = null)
        => unity.CallAsync("run_tests", new { platform, filter });

    [McpServerTool, Description("Report the live connection state between this server and the Unity Editor.")]
    public string unity_status() => unity.State.ToString();
}
