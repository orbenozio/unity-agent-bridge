// McpTools.cs — thin MCP -> Unity forwarders. NO Unity logic here (SPEC §7).
//
// Each method is exposed to Claude as an MCP tool and simply forwards to the
// Unity bridge via UnityClient.CallAsync. Keep arg names identical to the wire
// `args` consumed by the matching [McpTool] method on the Unity side.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

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

    // --- scene/UI editing (extension tools) ---------------------------------

    [McpServerTool, Description("Reparent a GameObject under another (empty/null parent = scene root).")]
    public Task<string> set_parent(string target, string? parent = null, bool keepWorldPosition = false)
        => unity.CallAsync("set_parent", new { target, parent, keepWorldPosition });

    [McpServerTool, Description("Set a UI RectTransform: anchored position (x,y), size (width,height), and optional anchor preset (center|top|stretch|...).")]
    public Task<string> set_rect(string target, double x = 0, double y = 0, double width = 160, double height = 30, string? anchor = null)
        => unity.CallAsync("set_rect", new { target, x, y, width, height, anchor });

    [McpServerTool, Description("Set the label text of a UI Text on the target; creates a stretched child label (with a font) if none exists.")]
    public Task<string> set_text(string target, string text)
        => unity.CallAsync("set_text", new { target, text });

    [McpServerTool, Description("Set any serialized property on a component (generic). property accepts aliases: color, text, fontSize, enabled, interactable. value is a number, bool, string, or {r,g,b,a}/{x,y,z}.")]
    public Task<string> set_property(string target, string componentType, string property, JsonElement value)
        => unity.CallAsync("set_property", new { target, componentType, property, value });

    [McpServerTool, Description("Delete a GameObject by name or instanceId (Undo-able).")]
    public Task<string> delete_gameobject(string target)
        => unity.CallAsync("delete_gameobject", new { target });

    [McpServerTool, Description("List the active scene hierarchy (names + components), depth-limited and terse.")]
    public Task<string> list_scene(int maxDepth = 4)
        => unity.CallAsync("list_scene", new { maxDepth });

    [McpServerTool, Description("Save the active scene to disk (gives an unsaved scene a path so it survives Play Mode). Optional asset path.")]
    public Task<string> save_scene(string? path = null)
        => unity.CallAsync("save_scene", new { path });

    [McpServerTool, Description("Inspect a GameObject by name or instanceId: transform + each component's serialized properties (capped).")]
    public Task<string> get_object(string target)
        => unity.CallAsync("get_object", new { target });

    [McpServerTool, Description("List every available bridge tool with its parameters (discovery).")]
    public Task<string> list_tools()
        => unity.CallAsync("list_tools", new { });

    [McpServerTool, Description("Bridge status: Unity version, listen host/port, and connection state.")]
    public Task<string> bridge_info()
        => unity.CallAsync("bridge_info", new { });
}
