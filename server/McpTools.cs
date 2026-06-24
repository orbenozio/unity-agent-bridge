// McpTools.cs - thin MCP -> Unity forwarders. NO Unity logic here (SPEC §7).
//
// Each method is exposed to Claude as an MCP tool and simply forwards to the
// Unity bridge via UnityClient.CallAsync. Keep arg names identical to the wire
// `args` consumed by the matching [McpTool] method on the Unity side.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace UnityAgentBridge;

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

    [McpServerTool, Description("Import changed assets and recompile scripts (AssetDatabase.Refresh). May trigger a domain reload; follow with compile_errors.")]
    public Task<string> refresh_assets()
        => unity.CallAsync("refresh_assets", new { });

    [McpServerTool, Description("C# compile errors from the last compilation (empty = clean). Pair with refresh_assets for an edit-compile-fix loop.")]
    public Task<string> compile_errors(bool includeWarnings = false)
        => unity.CallAsync("compile_errors", new { includeWarnings });

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

    [McpServerTool, Description("List every available bridge tool with its parameters (discovery) - includes project-defined custom tools.")]
    public Task<string> list_tools()
        => unity.CallAsync("list_tools", new { });

    [McpServerTool, Description("Invoke ANY bridge tool by name with a JSON args object. This is how you call project-defined custom [McpTool] methods that have no dedicated forwarder. Discover names+params with list_tools.")]
    public Task<string> call_tool(string tool, JsonElement? args = null)
        => unity.CallAsync(tool, args.HasValue ? (object)args.Value : new { });

    [McpServerTool, Description("Bridge status: Unity version, listen host/port, and connection state.")]
    public Task<string> bridge_info()
        => unity.CallAsync("bridge_info", new { });

    // --- custom commands (no-code, shareable macros of tool calls) -----------

    [McpServerTool, Description("List project-defined custom commands (named macros of tool calls) with their params.")]
    public Task<string> list_commands()
        => unity.CallAsync("list_commands", new { });

    [McpServerTool, Description("Run a project-defined custom command by name; args is a JSON object of its params.")]
    public Task<string> run_command(string name, JsonElement? args = null)
        => unity.CallAsync("run_command", new { name, args = args.HasValue ? (object)args.Value : new { } });

    [McpServerTool, Description("Create or update a custom command (a named macro). steps = JSON array of {tool,args} using ${param} placeholders; parameters = optional JSON array of {name,default}.")]
    public Task<string> save_command(string name, JsonElement steps, string? description = null, JsonElement? parameters = null)
        => unity.CallAsync("save_command", new { name, steps, description, parameters });

    [McpServerTool, Description("Delete a project-defined custom command by name.")]
    public Task<string> delete_command(string name)
        => unity.CallAsync("delete_command", new { name });

    [McpServerTool, Description("Bundle custom commands into a shareable .json pack (drop it into another project to import). names empty = all. Returns the pack path.")]
    public Task<string> export_commands(string? path = null, string[]? names = null)
        => unity.CallAsync("export_commands", new { path, names });

    [McpServerTool, Description("Import custom commands from a .json pack (a file path OR inline pack JSON). overwrite replaces existing names.")]
    public Task<string> import_commands(string pack, bool overwrite = false)
        => unity.CallAsync("import_commands", new { pack, overwrite });

    // --- custom C# tools (real [McpTool] methods, managed like commands) ------

    [McpServerTool, Description("List project-defined custom C# tools (the .cs files in the project's CustomTools folder).")]
    public Task<string> list_custom_tools()
        => unity.CallAsync("list_custom_tools", new { });

    [McpServerTool, Description("Scaffold a new custom C# [McpTool] from a template (writes <name>.cs). Then edit the file and call refresh_assets to compile it.")]
    public Task<string> new_custom_tool(string name, string? description = null, bool overwrite = false)
        => unity.CallAsync("new_custom_tool", new { name, description, overwrite });

    [McpServerTool, Description("Delete a custom tool .cs file by name. Changes code; call refresh_assets after.")]
    public Task<string> delete_custom_tool(string name)
        => unity.CallAsync("delete_custom_tool", new { name });

    [McpServerTool, Description("Bundle custom tool .cs files into a shareable .json pack (source embedded). names empty = all.")]
    public Task<string> export_tools(string? path = null, string[]? names = null)
        => unity.CallAsync("export_tools", new { path, names });

    [McpServerTool, Description("Import custom tools from a .json pack (a file path OR inline pack JSON). overwrite replaces existing. Changes code; call refresh_assets after.")]
    public Task<string> import_tools(string pack, bool overwrite = false)
        => unity.CallAsync("import_tools", new { pack, overwrite });
}
