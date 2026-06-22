# unity-mcp-bridge

A **minimal-yet-real** [MCP](https://modelcontextprotocol.io) server connecting
**Claude Code** to the **Unity 6 Editor**. Built for a live webinar — small enough to
read on a slide, real enough to create GameObjects, read the Console, run Play Mode,
and run tests against a live Editor.

> Claude ⇄ *(MCP / stdio)* ⇄ **.NET 8 server** ⇄ *(WebSocket)* ⇄ **C# bridge in Unity**

📄 Full design: [`SPEC.md`](./SPEC.md) · 🛠 Build plan: [`MILESTONES.md`](./MILESTONES.md)
· 🎬 Run-of-show: [`docs/webinar-script.md`](./docs/webinar-script.md)

## Why another Unity MCP?
This is **not** trying to out-feature [the big ones](./SPEC.md#0-goals--non-goals).
It is a teaching scaffold: one language (C#) end to end, four tools done well, and the
two Unity pain points — domain reload and Play Mode disconnects — solved from day one.

## Tools (v1)
| Tool | What it does |
|---|---|
| `read_console` | Read recent Console entries + stack traces (live debugging) |
| `create_gameobject` | Create a GameObject / primitive in the active scene |
| `add_component` | Add a component to a GameObject |
| `run_playmode` | Enter Play Mode for N seconds, report errors |
| `run_tests` | Run EditMode/PlayMode tests, report pass/fail |
| `unity_status` | Live server⇄Unity connection state |

## Quickstart
```bash
# 1. Build the server
cd server && dotnet build

# 2. Open demo-unity-project in Unity 6.
#    Console should show: [McpBridge] listening on ws://127.0.0.1:17890

# 3. Register with Claude Code
claude mcp add unity-mcp-bridge -- dotnet run --project "$(pwd)/server"

# 4. Try it in Claude Code
#    > read the unity console
#    > create a cube named Player and add a Rigidbody to it
#    > run the edit-mode tests
```

## Architecture (one diagram)
```
Claude Code ──stdio(MCP)──> .NET server ──WebSocket──> Unity McpBridge ──main thread──> Unity API
```
See [`SPEC.md`](./SPEC.md) for the full breakdown, wire protocol, and the main-thread
marshalling that makes it all work.

## Status
✅ All six tools implemented and verified against a live Unity 6000.3.7f1 Editor
(M0–M5 complete). Per-milestone status in [`MILESTONES.md`](./MILESTONES.md);
verification notes in [`TASKS.md`](./TASKS.md).

## License
MIT. Not affiliated with Unity Technologies.
