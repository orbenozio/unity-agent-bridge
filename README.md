# unity-agent-bridge

Drive the **Unity 6 Editor** from **Claude Code** (or any [MCP](https://modelcontextprotocol.io)
client) **and straight from your terminal** - the one built server is both an MCP server
and a CLI. It talks to a C# package running inside the Editor, so you can build scenes,
edit objects, run Play Mode, run tests, and capture screenshots against a live Editor -
then extend it with your own tools and commands to do anything you want.

> **Claude Code** *(MCP)* or **your terminal** *(CLI)* → **.NET 8 server** ⇄ *(WebSocket)* ⇄ **C# bridge in the Unity Editor**

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
- Claude Code, or any MCP client - optional; the bundled CLI needs neither

## Setup
Full walkthrough in [`GETTING-STARTED.md`](./GETTING-STARTED.md). The short version:

**1. Build the server**
```bash
git clone https://github.com/orbenozio/unity-agent-bridge.git
cd unity-agent-bridge/server && dotnet build
```

**2. Add the package to your Unity project**
In `<your-project>/Packages/manifest.json`, add **one line** under `dependencies`. The
package declares its own dependencies (Newtonsoft.Json, uGUI, Test Framework), so UPM
pulls them for you - you don't list them.

From GitHub (recommended; pin a tag or branch):
```jsonc
"com.orbenozio.unity-agent-bridge": "https://github.com/orbenozio/unity-agent-bridge.git?path=/unity-package#v0.1.1"
```
Or from a local clone (handy while developing the package itself):
```jsonc
"com.orbenozio.unity-agent-bridge": "file:/abs/path/to/unity-agent-bridge/unity-package"
```
The Git URL needs git on your PATH; `?path=/unity-package` points UPM at the package
subfolder and `#v0.1.1` pins a release (use `#main` to track the latest). You still build
the .NET server from a clone (step 1).

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

## Use it from the CLI (no agent needed)
The same built DLL is also a command-line tool - launched with arguments it runs **one
tool call** and prints the JSON result, then exits. Every tool the agent can call, you
can call from a shell or script:

```bash
dotnet /abs/path/to/server/bin/Debug/net8.0/unity-agent-bridge-server.dll ping
dotnet .../unity-agent-bridge-server.dll create_gameobject name=Cube primitive=Cube
dotnet .../unity-agent-bridge-server.dll add_component target=Cube componentType=Rigidbody
dotnet .../unity-agent-bridge-server.dll set_property target=Cube componentType=Image property=color value={"r":0,"g":1,"b":0,"a":1}
dotnet .../unity-agent-bridge-server.dll list      # every tool and its parameters
```

Values that look like JSON (`{...}`/`[...]`), numbers, or bools are sent as-is; anything
else is a string. Port defaults to `17890` (override with `--port N` or
`$UNITY_BRIDGE_PORT`). Alias it to `unity-agent-bridge` and it reads like a native
command - ideal for scripts, CI, batch edits, and quick checks without spinning up an
agent. Same tools, same bridge: use the agent for exploratory work, the CLI for anything
repeatable.

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

## Build anything: your own tools & commands
The built-in tools are the starting point, not the ceiling. Add your own capabilities -
tailored to your project, your workflow, your team - and **both the agent and the CLI
call them exactly like the built-ins**. Everything is shareable: export to a single JSON
file, drop it into another project, import.

- **Custom commands - no code.** A named, parameterized macro of existing tool calls,
  stored as a small JSON file (`${param}` substitution, JSON type preserved). Author it
  with `save_command`, by hand, or from the Editor window; run it with `run_command`;
  share with `export_commands`/`import_commands`. Perfect for repeatable setups - "spawn
  a player", "lay out the main menu", "reset the scene".

- **Custom tools - real C#, full power.** Any `[McpTool]` static method you add
  auto-registers on the next compile (the bridge scans every loaded assembly) and is
  immediately callable via `call_tool` - **no server-side wiring at all**. Scaffold with
  `new_custom_tool` (or the window), drop your logic into
  `Assets/UnityAgentBridge/CustomTools/Editor/<name>.cs` (no asmdef needed), share with
  `export_tools`/`import_tools` - the C# source travels inside the pack. If Unity's API
  can do it - loops, conditionals, editor automation, asset pipelines - a tool can do it.

Rule of thumb: a fixed sequence of existing tools is a command; anything needing real
logic is a tool. The demo ships a `create_button` custom tool (ensures a
Canvas/EventSystem and builds a uGUI button - logic a flat command can't express),
composed by a `main_menu` command into Play / Settings / Quit.

## Architecture
```
Claude Code (MCP/stdio) ─┐
                         ├─> .NET server ──WebSocket──> Unity McpBridge ──main thread──> Unity API
Terminal / CI (CLI) ─────┘
```
The server is a thin, fast pipe - all behavior lives in the Unity package. Every Unity
API call is marshalled onto the Editor's main thread via a job queue drained in
`EditorApplication.update`, and the bridge survives domain reloads (the server reconnects
with backoff), so an edit-compile-fix loop just works.

## License
MIT - see [`LICENSE`](./LICENSE). Not affiliated with Unity Technologies.
