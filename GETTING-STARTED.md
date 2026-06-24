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
In `<your-project>/Packages/manifest.json`, add under `dependencies` (point the path at
this repo's `unity-package` folder):

```jsonc
"com.webinar.unity-agent-bridge": "file:C:/Dev/UnityProjects/unity-agent-bridge/unity-package",
"com.unity.nuget.newtonsoft-json": "3.2.1",
"com.unity.ugui": "2.0.0",
"com.unity.test-framework": "1.6.0"
```

`newtonsoft-json` and `ugui` are required; `test-framework` only if you want `run_tests`.

## 2. Open the project in Unity
- The package loads automatically. The Console prints:
  `[McpBridge] listening on ws://127.0.0.1:17890 (auth on; token at ...)`.
- The token is created automatically on first run - no manual setup.
- Open `Window > Unity Agent Bridge` for the control panel: port, start/stop, per-tool
  permissions, custom commands, and custom tools.

## 3. Register the server with Claude Code
From your project folder (so the MCP server is scoped to it):

```bash
claude mcp add unity-agent-bridge -- dotnet run --project "C:/Dev/UnityProjects/unity-agent-bridge/server"
```

One server serves one Editor. If you change the port in the window, set the same value
via `UNITY_BRIDGE_PORT` in the `mcp add` command.

## 4. Basic usage (from Claude Code)
- `read the unity console` - live debugging.
- `create a cube named Player and add a Rigidbody` - build scene content.
- `run the edit-mode tests` - run tests.
- After editing a script: `refresh_assets` then `compile_errors` to drive an
  edit-compile-fix loop.

## 5. Extend it for your project
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
DNS-rebinding), and Origin rejection (blocks browser-originated connections). It is
localhost-only by design. See [`SPEC.md`](./SPEC.md) section 5.1 for the threat model.
