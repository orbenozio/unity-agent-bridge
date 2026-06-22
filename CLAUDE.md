# CLAUDE.md — unity-agent-bridge

A teaching MCP server connecting **Claude Code** to the **Unity 6 Editor**. Read
[`SPEC.md`](./SPEC.md) before changing architecture; it is the source of truth.

## What this is
- `server/` — **.NET 8** MCP server. Speaks MCP-over-stdio to Claude, our JSON-over-
  WebSocket to Unity. It is a **dumb, fast pipe**: NO Unity logic lives here.
- `unity-package/` — **C# Unity Editor package** (`Editor/`). The bridge: a WebSocket
  server + main-thread job pump + the actual tool implementations. **All behavior lives
  here.**
- `demo-unity-project/` — a tiny Unity 6 project used for the live demo.

## Golden rules
1. **Unity main thread.** Any Unity API (`GameObject`, `EditorApplication`,
   `AssetDatabase`, `TestRunnerApi`, …) MUST run via the `_mainThreadJobs` queue
   drained in `EditorApplication.update`. Never call Unity APIs from the WebSocket
   receive thread.
2. **Never crash the Editor.** Every tool body is wrapped in try/catch; exceptions
   become `{ ok:false, error }`, never an unhandled throw on the main thread.
3. **One request → one response,** correlated by `id`. Tools are non-blocking;
   long operations (`run_playmode`, `run_tests`) complete via callbacks, never
   `Thread.Sleep`.
4. **Add a tool in one place.** A new capability = one static method with
   `[McpTool("name","desc")]` in `unity-package/Editor/Tools/` + a matching thin
   forwarder in `server/McpTools.cs`. Keep arg names identical on both sides.
5. **Keep the server dumb.** If you're tempted to put Unity logic in `server/`, stop —
   it belongs in the bridge.
6. **Survive reload.** Don't break the `AssemblyReloadEvents` teardown/restart or the
   server-side reconnect/backoff. They are what make this feel solid.

## Conventions
- Wire protocol: `{ id, tool, args }` → `{ id, ok, result | error }`. See SPEC §5.
- Port: `127.0.0.1:17890` (localhost only, no auth — by design for v1).
- Tool results are **terse and structured**. Never dump whole scene trees.
- C#: nullable enabled in server; the Unity side targets the project's C# version.

## Commands
```bash
# Build / run the server
cd server && dotnet build
dotnet run --project server          # launched by Claude via `claude mcp add`

# Register with Claude Code
claude mcp add unity-agent-bridge -- dotnet run --project /abs/path/server
```
Unity side: open `demo-unity-project/` in Unity 6; the package auto-loads from
`Packages/` (via a local file reference). Watch the Console for
`[McpBridge] listening on ws://127.0.0.1:17890`.

## Working order
Follow [`MILESTONES.md`](./MILESTONES.md) top to bottom. Don't build tool #4 before the
pipe (#1) and `read_console` (#2) work end to end against a live Editor.
