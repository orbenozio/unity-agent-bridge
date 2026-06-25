# unity-agent-bridge

Drive the **Unity 6 Editor** from **Claude Code** (or any [MCP](https://modelcontextprotocol.io)
client). A small .NET server speaks MCP to the agent and WebSocket to a C# package
running inside the Editor, so an agent can build scenes, edit objects, run Play Mode,
run tests, and capture screenshots against a live Editor - and grow new tools you define.

> Claude Code ⇄ *(MCP / stdio)* ⇄ **.NET 8 server** ⇄ *(WebSocket)* ⇄ **C# bridge in the Unity Editor**

## What you can do
Every capability is one `[McpTool]` method on the Unity side plus a thin forwarder on
the server. By category:

| Category | Tools |
|---|---|
| Debug & build loop | `read_console`, `refresh_assets`, `compile_errors`, `run_playmode`, `run_tests` |
| Scene & GameObjects | `create_gameobject`, `add_component`, `set_transform`, `set_parent`, `set_rect`, `set_text`, `set_property`, `delete_gameobject`, `list_scene`, `get_object`, `save_scene` |
| Prefabs & capture | `instantiate_prefab`, `create_prefab`, `capture_screenshot` |
| Editor control | `execute_menu_item`, `bridge_info`, `unity_status`, `list_tools`, `call_tool` |
| Extensibility | custom command packs (`list`/`run`/`save`/`new`/`delete`/`export`/`import_commands`) + custom C# tools (`list`/`new`/`delete_custom_tool`, `export`/`import_tools`) |

## Requirements
- Unity 6 (6000.x)
- .NET 8 SDK
- Claude Code CLI (or any MCP client)

## Setup
Full walkthrough in [`GETTING-STARTED.md`](./GETTING-STARTED.md). The short version:

**1. Build the server**
```bash
git clone https://github.com/orbenozio/unity-agent-bridge.git
cd unity-agent-bridge/server && dotnet build
```

**2. Add the package to your Unity project**
In `<your-project>/Packages/manifest.json`, add under `dependencies` (point the path at
this repo's `unity-package` folder):
```jsonc
"com.orbenozio.unity-agent-bridge": "file:/abs/path/to/unity-agent-bridge/unity-package",
"com.unity.nuget.newtonsoft-json": "3.2.1",
"com.unity.ugui": "2.0.0",
"com.unity.test-framework": "1.6.0"
```
`newtonsoft-json` and `ugui` are required; add `test-framework` only if you want `run_tests`.

**3. Open the project in Unity 6**
The package auto-loads. The Console prints
`[McpBridge] listening on ws://127.0.0.1:17890 (auth on; token at ...)`. The auth token is
created automatically on first run - nothing to configure. Open
**Window > Unity Agent Bridge** for the control panel (port, start/stop, per-tool
permissions, custom commands and tools).

**4. Register the server with Claude Code**
Point Claude at the **built DLL**, not `dotnet run` - `dotnet run` can print build output
to stdout and corrupt the MCP JSON-RPC stream (the server connects but exposes 0 tools):
```bash
claude mcp add unity-agent-bridge -- dotnet "/abs/path/to/unity-agent-bridge/server/bin/Debug/net8.0/unity-agent-bridge-server.dll"
```

**5. Try it from Claude Code**
```
> read the unity console
> create a cube named Player and add a Rigidbody to it
> capture a screenshot of the scene
```

> **Tip:** enable **Edit > Project Settings > Player > Run In Background** (or keep the
> Editor focused). Unity throttles an unfocused Editor, so a `run_playmode`/`run_tests`
> call can stall mid-run until you click back into the window. The bridge stays
> connected - only the in-Play-Mode work pauses.

## Security
The WebSocket port is localhost-only, but localhost is not a trust boundary - any local
process, and any web page via `new WebSocket("ws://127.0.0.1:...")`, can reach it. The
handshake is fail-closed and gated by three checks:

- **Shared-secret token.** Auto-provisioned per user/port to `~/.unity-agent-bridge/`
  (owner-only file permissions); compared in constant time.
- **Host pinning** against `127.0.0.1`/`localhost` (anti DNS-rebinding).
- **Origin/Referer rejection** - native clients send neither; a browser sends at least
  one, so browser-originated connections are refused.

File-writing tools (screenshots, prefabs, scenes) are constrained to the project by a
path-traversal guard that rejects `..` escapes and symlink/junction crossings.

## Extending it for your project
Two ways to add project-specific capabilities, both shareable between projects:

- **Custom commands** (no code) - a named macro of existing tool calls with `${param}`
  substitution. Create with `save_command`; share with `export_commands`/`import_commands`.
- **Custom tools** (real C#) - a `[McpTool]` method with full logic. Scaffold with
  `new_custom_tool` or the window; call via `call_tool`; share with
  `export_tools`/`import_tools`.

Rule of thumb: a fixed sequence of existing tools is a command; anything needing logic
(loops, conditionals, Unity APIs the tools do not expose) is a tool.

## Architecture
```
Claude Code ──stdio(MCP)──> .NET server ──WebSocket──> Unity McpBridge ──main thread──> Unity API
```
The server is a thin, fast pipe - all behavior lives in the Unity package. Every Unity
API call is marshalled onto the Editor's main thread via a job queue drained in
`EditorApplication.update`, and the bridge survives domain reloads (the server reconnects
with backoff), so an edit-compile-fix loop just works.

## License
MIT - see [`LICENSE`](./LICENSE). Not affiliated with Unity Technologies.
