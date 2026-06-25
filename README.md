# unity-agent-bridge

Drive the **Unity 6 Editor** from **Claude Code** (or any [MCP](https://modelcontextprotocol.io)
client) **and straight from your terminal** - the one built server is both an MCP server
and a CLI. It talks to a C# package running inside the Editor, so you can build scenes,
edit objects, run Play Mode, run tests, and capture screenshots against a live Editor -
then extend it with your own tools and commands to do anything you want.

> **Claude Code** *(MCP)* or **your terminal** *(CLI)* → **.NET 8 server** ⇄ *(WebSocket)* ⇄ **C# bridge in the Unity Editor**

## What you can do
By category:

| Category | Tools |
|---|---|
| Debug & build loop | `read_console`, `refresh_assets`, `compile_errors`, `run_playmode`, `run_tests` |
| Scene & GameObjects | `create_gameobject`, `add_component`, `set_transform`, `set_parent`, `set_rect`, `set_text`, `set_property`, `delete_gameobject`, `list_scene`, `get_object`, `save_scene` |
| Prefabs & capture | `instantiate_prefab`, `create_prefab`, `capture_screenshot` |
| Editor control | `execute_menu_item`, `bridge_info`, `unity_status`, `list_tools`, `call_tool` |
| Extensibility | commands (`list_commands`, `run_command`, `save_command`, `new_command`, `delete_command`, `export_commands`, `import_commands`) and custom C# tools (`list_custom_tools`, `new_custom_tool`, `delete_custom_tool`, `export_tools`, `import_tools`) |

## Setup
You need **Unity 6**, the **.NET 8 SDK or newer**, and **git**. Five steps:

**1. Get it and build the server**
```bash
git clone https://github.com/orbenozio/unity-agent-bridge.git
cd unity-agent-bridge/server && dotnet build
```

**2. Open your project in Unity 6**

**3. Add the package**
In Unity, open **Window > Package Manager**, click **+** (top-left), choose **Install
package from git URL...**, paste this, and click **Install**:
```
https://github.com/orbenozio/unity-agent-bridge.git?path=/unity-package#v0.1.1
```
The package pulls its own dependencies. When it finishes importing, the Console shows a
line starting with:
```
[McpBridge] listening on ws://127.0.0.1:17890
```
Then enable **Edit > Project Settings > Player > Run In Background**, so tools keep running while the Editor is unfocused. (Paste `#main` instead of `#v0.1.1` for the latest version.)

**4. Connect Claude Code**
```bash
claude mcp add unity-agent-bridge -- dotnet "/abs/path/to/unity-agent-bridge/server/bin/Debug/net8.0/unity-agent-bridge-server.dll"
```
Use the built `.dll` shown above - not `dotnet run`, which corrupts the connection (you'd see 0 tools).

**5. Use it - just ask Claude**
```
> read the unity console
> create a cube named Player and add a Rigidbody to it
> capture a screenshot of the scene
```

## Prefer the terminal? Use the CLI
The same server is also a CLI - no agent needed. Install the command once:
```bash
cd server
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release unity-agent-bridge
```
Then run any tool from anywhere:
```bash
unity-agent-bridge ping
unity-agent-bridge create_gameobject name=Cube primitive=Cube
unity-agent-bridge list      # all tools and their parameters
```
Values are sent as their JSON type (`{...}`, numbers, bools); everything else is a string.
Once installed, you can also connect Claude Code with the short command:
`claude mcp add unity-agent-bridge -- unity-agent-bridge`.

Daily use, extending it, and troubleshooting: [`GETTING-STARTED.md`](./GETTING-STARTED.md).

## Security
The WebSocket port is localhost-only, but localhost is not a trust boundary - any local
process, and any web page via `new WebSocket("ws://127.0.0.1:...")`, can reach it. The
handshake is fail-closed and gated by three checks:

- **Shared-secret token.** Auto-provisioned per user/port to `~/.unity-agent-bridge/`
  (locked to your user account); compared in constant time.
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
