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
Editor keeps its own port and token, so two agent sessions never interfere.

Because the port shifts, **don't hard-code it** - target each Editor by its project name
and let the CLI discover the live port (each open Editor publishes it to
`~/.unity-agent-bridge/projects/`):
```bash
unity-agent-bridge --project NeonRunner ping     # matches the project folder name
unity-agent-bridge --project MyOtherGame list
```
`--project` accepts the project folder name or a full/partial path; on an ambiguous match
it lists the candidates so you can fall back to an explicit `--port N`. Precedence is
`--port` > `--project` > `$UNITY_BRIDGE_PORT` > `$UNITY_BRIDGE_PROJECT` > default 17890.

For an MCP server registration, pass the project (or a fixed port) via env:
```bash
claude mcp add unity-b -e UNITY_BRIDGE_PROJECT=NeonRunner -- dotnet ".../bin/Debug/net8.0/unity-agent-bridge-server.dll"
```

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
- **Port moved / can't connect?** The bridge auto-picks a free port, so don't assume
  17890 - check the exact one in **Window > Unity Agent Bridge**. Rather than chase it,
  target the Editor by name: `--project <name>` for the CLI, or `UNITY_BRIDGE_PROJECT` for
  an MCP registration. Pin an explicit `--port N` / `UNITY_BRIDGE_PORT` only to override.

## Security
Localhost-only, with a fail-closed handshake (auto-provisioned token + Host pinning +
Origin rejection); file writes stay inside the project. Details in the
[README](./README.md#security).
