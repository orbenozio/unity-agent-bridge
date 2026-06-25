# Using Agent Bridge

Setup (install + connect) is in the [README](./README.md). This page is what to do once
you're connected: everyday usage, extending it, and fixing the common hiccups.

## Everyday usage
From Claude Code, just ask:
- `read the unity console` - live debugging.
- `create a cube named Player and add a Rigidbody` - build scene content.
- `run the edit-mode tests`.
- After editing a script: `refresh_assets`, then `compile_errors` - an edit-compile-fix loop.

From the terminal (after installing the CLI - see the README):
```bash
unity-agent-bridge ping
unity-agent-bridge create_gameobject name=Player primitive=Cube
unity-agent-bridge list      # every tool and its parameters
```

## Running two Editors at once
Open a second Unity project and its bridge automatically grabs the next free port
(17891, 17892, ...) - check the actual port in **Window > Unity Agent Bridge**. Each
Editor keeps its own port and token, so two agent sessions never interfere. Register the
second session's MCP server with the matching port:
```bash
claude mcp add unity-b -e UNITY_BRIDGE_PORT=17891 -- dotnet ".../bin/Debug/net8.0/unity-agent-bridge-server.dll"
```
For the CLI, pass `--port 17891` (the window shows the exact command for its port).

## Extend it: your own tools and commands
Two ways, both shareable between projects:
- **Commands** (no code) - a named macro of existing tool calls with `${param}`
  substitution. Make one with `save_command`; share with `export_commands` /
  `import_commands`. Stored in `<project>/UnityAgentBridge/Commands/`.
- **Tools** (real C#) - a `[McpTool]` method with full logic. Scaffold with
  `new_custom_tool`; call via `call_tool`; share with `export_tools` / `import_tools`.
  Stored in `<project>/Assets/UnityAgentBridge/CustomTools/Editor/`.

A fixed sequence of existing tools is a command; anything needing logic is a tool.

## If something's off
- **Tool didn't respond?** After a script change Unity reloads and the socket drops for a
  moment - the server reconnects on its own. Just run the tool again.
- **Still stuck?** Check `unity_status`, and the Auth/Client lines in
  **Window > Unity Agent Bridge**.
- **Changed the port?** Set the same value via `UNITY_BRIDGE_PORT` in the `claude mcp add`
  command (and pass `--port N` to the CLI).

## Security
Localhost-only, with a fail-closed handshake (auto-provisioned token + Host pinning +
Origin rejection); file writes stay inside the project. Details in the
[README](./README.md#security).
