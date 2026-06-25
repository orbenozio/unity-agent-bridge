# Getting started - connect a project to Agent Bridge

Connect a Unity 6 project to Claude Code (or any MCP client) through the bridge. The
bridge runs inside the Unity Editor; a tiny .NET server speaks MCP to Claude and
WebSocket to the bridge. The token-based handshake auto-provisions, so there is no
secret to manage.

## Prerequisites
- Unity 6 (6000.x)
- .NET 8 SDK
- Claude Code CLI

## 1. Add the package to your project
In `<your-project>/Packages/manifest.json`, add **one line** under `dependencies`. The
package declares its own dependencies (Newtonsoft.Json, uGUI, Test Framework), so UPM
pulls them for you - you don't list them.

From GitHub (recommended; pin a tag or branch):
```jsonc
"com.orbenozio.unity-agent-bridge": "https://github.com/orbenozio/unity-agent-bridge.git?path=/unity-package#v0.1.1"
```

Or from a local clone (for developing the package itself):
```jsonc
"com.orbenozio.unity-agent-bridge": "file:/abs/path/to/unity-agent-bridge/unity-package"
```

The Git URL needs git on your PATH; `?path=/unity-package` points UPM at the package
subfolder, and `#v0.1.1` pins a release (use `#main` to track the latest).

## 2. Open the project in Unity
- The package loads automatically. The Console prints:
  `[McpBridge] listening on ws://127.0.0.1:17890 (auth on; token at ...)`.
- The token is created automatically on first run - no manual setup.
- Open `Window > Unity Agent Bridge` for the control panel: port, start/stop, per-tool
  permissions, custom commands, and custom tools.

## 3. Build and register the server with Claude Code
Build the server once:

```bash
cd /abs/path/to/unity-agent-bridge/server && dotnet build
```

Then register the **built DLL** - not `dotnet run`, which can print build output to
stdout and corrupt the MCP JSON-RPC stream (the server connects but exposes 0 tools):

```bash
claude mcp add unity-agent-bridge -- dotnet "/abs/path/to/unity-agent-bridge/server/bin/Debug/net8.0/unity-agent-bridge-server.dll"
```
If you installed the global tool (step 4), register the bare command instead -
`claude mcp add unity-agent-bridge -- unity-agent-bridge`.

One server serves one Editor. If you change the port in the window, set the same value
via `UNITY_BRIDGE_PORT` in the `mcp add` command.

## 4. Basic usage
From **Claude Code** (natural language):
- `read the unity console` - live debugging.
- `create a cube named Player and add a Rigidbody` - build scene content.
- `run the edit-mode tests` - run tests.
- After editing a script: `refresh_assets` then `compile_errors` to drive an
  edit-compile-fix loop.

Or straight from the **CLI** - the same binary runs one tool call and exits, so every
tool is scriptable without an agent. Install it once as a global command:
```bash
cd server
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release unity-agent-bridge
```
Then call it anywhere:
```bash
unity-agent-bridge ping
unity-agent-bridge create_gameobject name=Player primitive=Cube
unity-agent-bridge list      # every tool and its parameters
```
JSON/number/bool values are sent as-is; anything else is a string. (Prefer not to
install? Run the DLL directly: `dotnet ".../bin/Debug/net8.0/unity-agent-bridge-server.dll" ping`.)

## 5. Extend it: build your own tools and commands
Two ways to add project-specific capabilities, both shareable between projects:

- **Custom commands** (no code): a named macro of existing tool calls with `${param}`
  substitution. Create with `save_command` or by hand-editing the JSON; share with
  `export_commands` / `import_commands`. Stored in
  `<project>/UnityAgentBridge/Commands/`.
- **Custom tools** (real C#): a `[McpTool]` method with full logic. Scaffold with
  `new_custom_tool` or the window's New field; call via `call_tool`; share with
  `export_tools` / `import_tools`. Stored in
  `<project>/Assets/UnityAgentBridge/CustomTools/Editor/`.

Rule of thumb: a fixed sequence of existing tools is a command; anything needing logic
(loops, conditionals, Unity APIs the tools do not expose) is a tool.

## 6. Day-to-day
- After a code change, the domain reloads and the socket drops briefly; the server
  reconnects on its own (with backoff). Just run the tool again.
- A tool not responding? Check `unity_status` and the Auth/Client lines in the window.
- The CLI is handy for quick checks: `unity-agent-bridge ping`,
  `unity-agent-bridge list`.

## Security note
The handshake is gated by a shared-secret token, Host-header pinning (anti
DNS-rebinding), and Origin rejection (blocks browser-originated connections), and is
localhost-only by design. File-writing tools are constrained to the project. See the
Security section in [`README.md`](./README.md) for details.
