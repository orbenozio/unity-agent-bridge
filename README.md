# unity-agent-bridge

A **minimal-yet-real** [MCP](https://modelcontextprotocol.io) server connecting
**Claude Code** to the **Unity 6 Editor**. Built for a live webinar - small enough to
read on a slide, real enough to create GameObjects, read the Console, run Play Mode,
and run tests against a live Editor.

> Claude ⇄ *(MCP / stdio)* ⇄ **.NET 8 server** ⇄ *(WebSocket)* ⇄ **C# bridge in Unity**

📄 Full design: [`SPEC.md`](./SPEC.md) · 🛠 Build plan: [`MILESTONES.md`](./MILESTONES.md)
· 🎬 Run-of-show: [`docs/webinar-script.md`](./docs/webinar-script.md)

## Why another Unity MCP?
This is **not** trying to out-feature [the big ones](./SPEC.md#0-goals--non-goals).
It is a teaching scaffold: one language (C#) end to end, four tools done well, and the
two Unity pain points - domain reload and Play Mode disconnects - solved from day one.

## Tools
Started at four; grew into a real toolbox. Every tool is one `[McpTool]` method + a
thin forwarder. By category:

| Category | Tools |
|---|---|
| Debug & build loop | `read_console`, `refresh_assets`, `compile_errors`, `run_playmode`, `run_tests` |
| Scene & GameObjects | `create_gameobject`, `add_component`, `set_transform`, `set_parent`, `set_rect`, `set_text`, `set_property`, `delete_gameobject`, `list_scene`, `get_object`, `save_scene` |
| Prefabs & capture | `instantiate_prefab`, `create_prefab`, `capture_screenshot` |
| Editor control | `execute_menu_item`, `bridge_info`, `unity_status`, `list_tools`, `call_tool` |
| Extensibility | custom command packs (`list`/`run`/`save`/`new`/`delete`/`export`/`import_commands`) + custom C# tools (`list`/`new`/`delete_custom_tool`, `export`/`import_tools`) |

## Quickstart
```bash
# 1. Build the server
cd server && dotnet build

# 2. Open demo-unity-project in Unity 6.
#    Console should show: [McpBridge] listening on ws://127.0.0.1:17890

# 3. Register with Claude Code - run the BUILT DLL, at user scope.
#    Do NOT use `dotnet run`: it prints build output to stdout and corrupts the
#    MCP JSON-RPC stream (the server connects but exposes 0 tools).
claude mcp add -s user unity-agent-bridge -- dotnet "<abs>/server/bin/Debug/net8.0/unity-agent-bridge-server.dll"

# 4. Try it in Claude Code
#    > read the unity console
#    > create a cube named Player and add a Rigidbody to it
#    > capture a screenshot of the scene
```

> **Tip:** enable **Edit > Project Settings > Player > Run In Background** (or keep the
> Editor focused). Unity throttles an unfocused Editor, so a `run_playmode`/`run_tests`
> call can stall mid-run until you click back into the window. The bridge itself stays
> connected - only the in-Play-Mode work pauses.

## Architecture (one diagram)
```
Claude Code ──stdio(MCP)──> .NET server ──WebSocket──> Unity McpBridge ──main thread──> Unity API
```
See [`SPEC.md`](./SPEC.md) for the full breakdown, wire protocol, and the main-thread
marshalling that makes it all work.

## Status
✅ Verified end to end against a live Unity 6000.3.7f1 Editor - over MCP (Claude Code)
and the CLI. Security handshake, the autonomous compile loop, custom command/tool packs,
and the full tool set above all working. Per-milestone status in
[`MILESTONES.md`](./MILESTONES.md); verification notes in [`TASKS.md`](./TASKS.md).

## License
MIT. Not affiliated with Unity Technologies.
