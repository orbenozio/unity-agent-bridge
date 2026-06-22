# SPEC — `unity-mcp-bridge`

A **minimal-yet-real** MCP server that connects **Claude Code** to the **Unity 6
Editor**, built for a live webinar and extensible into a genuine tool.

> **One sentence:** Claude talks MCP-over-stdio to a tiny .NET server, which talks
> JSON-over-WebSocket to a C# bridge running inside the Unity Editor, which executes
> on Unity's main thread and answers back.

---

## 0. Goals & non-goals

### Goals
- **Teach the architecture.** Every layer is small enough to read on a slide.
- **Be real.** It actually creates GameObjects, reads the Console, runs Play Mode
  and tests against a live Unity 6 Editor — no mocks.
- **One language end-to-end.** C#/.NET on both sides (server + bridge) so there is
  one stack to learn and debug. Same language as Unity itself.
- **Survive the Unity pain points** that every existing server hits — domain reload
  and Play Mode disconnects — gracefully from day one.
- **Extensible in one line.** Adding a new capability = one C# method + one
  `[McpTool]` attribute.

### Non-goals (v1)
- Not 268 tools. Four, done well.
- No remote/cloud transport, no auth, no Docker. Localhost only.
- No runtime-in-game support (Editor only).
- No multi-instance routing (single Editor).

---

## 1. Target environment

| Component | Version / choice | Notes |
|---|---|---|
| Unity | **Unity 6 (6000.x)** | Uses `AssemblyReloadEvents`, `EditorApplication`, Test Framework |
| .NET (server) | **.NET 8 LTS** | `dotnet run`, AOT-friendly, instant startup |
| MCP SDK | **`ModelContextProtocol`** (official C# SDK) | stdio server transport |
| WebSocket (server side) | `System.Net.WebSockets.ClientWebSocket` | built into .NET |
| WebSocket (Unity side) | lightweight C# WS server (see §6) | single dependency, vendored |
| Claude client | **Claude Code** | `claude mcp add` |

---

## 2. Architecture

```
┌──────────────┐  stdio (MCP / JSON-RPC 2.0)  ┌──────────────────┐  WebSocket (our JSON)  ┌─────────────────────┐
│  Claude Code │ ◄─────────────────────────► │  MCP Server      │ ◄────────────────────► │  Unity Editor        │
│  (MCP client)│                             │  .NET 8 console  │  ws://127.0.0.1:17890  │  McpBridge (C#)      │
└──────────────┘                             │  ModelContext-   │                        │  + main-thread queue │
                                             │  Protocol SDK    │                        └─────────────────────┘
                                             └──────────────────┘
```

**Why two processes and two transports — on purpose:**

1. **Claude ↔ Server = stdio.** This is how Claude Code launches and speaks to MCP
   servers (`claude mcp add`). The server is a child process of Claude.
2. **Server ↔ Unity = WebSocket.** Unity is *not* a child of Claude; it runs
   independently and must reconnect cleanly across domain reloads. A persistent,
   bidirectional socket is the right tool. WebSocket (over raw TCP) gives us framing
   and a clean close handshake for free.

**Data flow for one tool call (`create_gameobject`):**

```
Claude → (MCP CallTool) → Server.McpTools.CreateGameObject()
       → UnityClient.SendAsync({tool:"create_gameobject", args:{...}})  [WebSocket]
       → Unity McpBridge receives on background thread
       → enqueues job on _mainThreadJobs
       → EditorApplication.update dequeues → runs GameObject.CreatePrimitive() [MAIN THREAD]
       → result JSON sent back over WebSocket
       → Server resolves the pending request by id → returns MCP tool result → Claude
```

---

## 3. The critical constraint: Unity's main thread

Almost every Unity API (`GameObject`, `EditorApplication`, `AssetDatabase`,
`EditorSceneManager`, the test runner) **must be called on the main thread**. The
WebSocket server receives messages on a background thread. Therefore:

```csharp
// background thread (WebSocket receive): just enqueue
_mainThreadJobs.Enqueue(() => result = ExecuteTool(msg));

// main thread (EditorApplication.update): drain & run
EditorApplication.update += () => {
    while (_mainThreadJobs.TryDequeue(out var job)) job();
};
```

This marshalling is **the heart of the demo** — it is what makes "an external AI
touch the Editor" possible, and it is ~6 lines. Show it on a slide.

---

## 4. The Unity pain point we solve up front: reload & Play Mode

Every existing Unity MCP server suffers the same break: **the WebSocket dies on every
domain reload (script recompile) and when entering/exiting Play Mode.** We handle it
on both sides:

**Unity side (graceful teardown + auto-restart):**
```csharp
AssemblyReloadEvents.beforeAssemblyReload += () => _server.Stop();   // clean close
AssemblyReloadEvents.afterAssemblyReload  += () => _server.Start();  // come back up
EditorApplication.playModeStateChanged    += OnPlayModeChanged;      // log + keep alive
```
- For Play Mode smoothness in the demo, optionally set
  **Project Settings → Editor → Enter Play Mode Settings → Reload Domain = off**.

**Server side (reconnect with backoff + request queue):**
- On socket close, the server transitions to `Reconnecting` and retries with
  exponential backoff (250 ms → 4 s, capped).
- Tool calls arriving while disconnected are **parked** (with a timeout, default 30 s)
  and flushed when the socket is back. Claude sees a slightly slower call, not an error.
- A `unity_status` tool exposes the live connection state for transparency.

This single feature is what makes the server feel solid instead of a toy.

---

## 5. Wire protocol (Server ↔ Unity)

A deliberately tiny JSON envelope over WebSocket text frames. **This is our own
protocol, not MCP** — the server translates between this and MCP.

### Request (Server → Unity)
```jsonc
{
  "id": "b1c3...-uuid",       // correlation id
  "tool": "read_console",     // tool name
  "args": { "levels": ["Error","Warning"], "limit": 50 }
}
```

### Response (Unity → Server)
```jsonc
// success
{ "id": "b1c3...-uuid", "ok": true,  "result": { "entries": [ /* ... */ ] } }
// failure
{ "id": "b1c3...-uuid", "ok": false, "error": "NullReferenceException: ...\n  at ..." }
```

### Rules
- One request → exactly one response, correlated by `id`.
- Unity never initiates (v1). (v2 may push log events; envelope reserves `"event"`.)
- All Unity-side execution is wrapped in try/catch; exceptions become `ok:false`.
- Unknown `tool` → `ok:false, error:"unknown tool: <name>"`.

---

## 6. Unity side — the Bridge (C# package)

Lives in `unity-package/` and is installed into the demo project's `Packages/`.

### Files
```
unity-package/
├─ package.json                     # UPM manifest: com.webinar.unity-mcp-bridge
├─ Editor/
│  ├─ McpBridge.cs                  # lifecycle, WS server, main-thread pump, dispatch
│  ├─ McpToolAttribute.cs           # [McpTool] + [Param] attributes
│  ├─ ToolRegistry.cs               # reflection scan of [McpTool] methods
│  ├─ WebSocketServer.cs            # minimal RFC6455 server (vendored, ~200 LOC)
│  └─ Tools/
│     ├─ ConsoleTools.cs            # read_console
│     ├─ GameObjectTools.cs         # create_gameobject, add_component
│     ├─ PlayModeTools.cs           # run_playmode
│     └─ TestTools.cs               # run_tests
└─ Runtime/                         # (empty in v1; reserved for runtime bridge)
```

### Lifecycle (`McpBridge.cs`)
```csharp
[InitializeOnLoad]
public static class McpBridge {
    static readonly ConcurrentQueue<Action> _jobs = new();
    static WebSocketServer _server;

    static McpBridge() {
        _server = new WebSocketServer("127.0.0.1", 17890);
        _server.OnMessage += HandleMessage;     // background thread
        _server.Start();
        EditorApplication.update += Pump;
        AssemblyReloadEvents.beforeAssemblyReload += () => _server.Stop();
        AssemblyReloadEvents.afterAssemblyReload  += () => _server.Start();
    }

    static void Pump() { while (_jobs.TryDequeue(out var j)) j(); }

    static void HandleMessage(string json, Action<string> reply) {
        var req = JsonUtility.FromJson<Request>(json);
        _jobs.Enqueue(() => {              // run on main thread
            string response = ToolRegistry.Invoke(req); // try/catch inside → ok:false on throw
            reply(response);
        });
    }
}
```

### Tool registration (the "one-line" extension point)
```csharp
public static class GameObjectTools {
    [McpTool("create_gameobject", "Create a GameObject in the active scene")]
    public static object CreateGameObject(
        [Param("Name for the new object")] string name,
        [Param("Primitive: Cube|Sphere|Capsule|Cylinder|Plane|Quad|None")] string primitive = "None")
    {
        var go = primitive == "None"
            ? new GameObject(name)
            : GameObject.CreatePrimitive(System.Enum.Parse<PrimitiveType>(primitive));
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "MCP Create");   // be a good Editor citizen
        return new { instanceId = go.GetInstanceID(), path = HierarchyPath(go) };
    }
}
```
`ToolRegistry` scans all `[McpTool]` methods via reflection on load, builds a
name→(method, params) map, deserializes `args` by parameter name, invokes, and
serializes the return value into `result`.

---

## 7. Server side — the MCP server (.NET 8)

Lives in `server/`. Speaks MCP to Claude (stdio) and our JSON to Unity (WebSocket).

### Files
```
server/
├─ server.csproj                    # net8.0, ModelContextProtocol package ref
├─ Program.cs                       # host builder, stdio MCP server, DI
├─ UnityClient.cs                   # ClientWebSocket + reconnect/backoff + pending map
└─ McpTools.cs                      # [McpServerTool] methods → forwarded to Unity
```

### `UnityClient.cs` responsibilities
- Maintain a single `ClientWebSocket` to `ws://127.0.0.1:17890`.
- `Task<JsonResult> CallAsync(string tool, object args, CancellationToken ct)`:
  - generate `id`, register a `TaskCompletionSource` in a `ConcurrentDictionary`,
  - send the envelope, await the TCS (resolved by the receive loop on matching `id`),
  - honor a per-call timeout.
- Background receive loop: parse responses, complete the matching TCS.
- Reconnect with exponential backoff; **park** calls made while disconnected.
- Expose `ConnectionState { Connected, Reconnecting, Down }`.

### `McpTools.cs` — thin MCP→Unity forwarders
```csharp
[McpServerToolType]
public class UnityMcpTools(UnityClient unity) {

    [McpServerTool, Description("Read recent Unity Console log entries (errors, warnings, logs).")]
    public Task<string> read_console(
        [Description("Levels to include: Error, Warning, Log")] string[]? levels = null,
        [Description("Max entries to return")] int limit = 50)
        => unity.CallAsync("read_console", new { levels, limit });

    [McpServerTool, Description("Create a GameObject (optionally a primitive) in the active scene.")]
    public Task<string> create_gameobject(string name, string primitive = "None")
        => unity.CallAsync("create_gameobject", new { name, primitive });

    [McpServerTool, Description("Add a component to a GameObject by name or instanceId.")]
    public Task<string> add_component(string target, string componentType)
        => unity.CallAsync("add_component", new { target, componentType });

    [McpServerTool, Description("Enter Play Mode for N seconds, then exit; returns any errors logged.")]
    public Task<string> run_playmode(double seconds = 3)
        => unity.CallAsync("run_playmode", new { seconds });

    [McpServerTool, Description("Run Unity tests (EditMode or PlayMode) and return pass/fail results.")]
    public Task<string> run_tests(string platform = "EditMode", string? filter = null)
        => unity.CallAsync("run_tests", new { platform, filter });

    [McpServerTool, Description("Report the live connection state between the server and Unity.")]
    public string unity_status() => unity.State.ToString();
}
```

The server is intentionally a **dumb, fast pipe**: zero Unity logic lives here. All
behavior is in the bridge. This keeps the perf story honest — the server is I/O only.

---

## 8. The four tools (v1) — full contracts

### `read_console` — *the star of the debugging demo*
- **args:** `levels?: ("Error"|"Warning"|"Log")[]` (default all), `limit?: int = 50`
- **Unity impl:** hook `Application.logMessageReceivedThreaded` into a ring buffer on
  load (capacity ~1000); filter & return latest `limit`.
- **result:** `{ entries: [{ level, message, stackTrace, timestamp }] }`
- **Why it matters:** lets Claude *see* a `NullReferenceException` and fix it live.

### `create_gameobject`
- **args:** `name: string`, `primitive?: string = "None"`, (v1.1: `position?`, `parent?`)
- **result:** `{ instanceId: int, path: string }`
- Wrapped in `Undo.RegisterCreatedObjectUndo`.

### `add_component`
- **args:** `target: string` (name or `instanceId`), `componentType: string`
  (e.g. `Rigidbody`, `BoxCollider`)
- **Unity impl:** resolve target → resolve `Type` across loaded assemblies →
  `Undo.AddComponent`.
- **result:** `{ instanceId, component, added: bool }`

### `run_playmode`
- **args:** `seconds?: double = 3`
- **Unity impl:** clear a play-mode error capture, `EditorApplication.EnterPlaymode()`,
  schedule exit after `seconds` (delayed via `EditorApplication.update` timer), collect
  errors logged during play.
- **result:** `{ entered: bool, exited: bool, errors: string[] }`
- **Note:** interacts with domain-reload handling (§4); document the Reload Domain = off
  tip for clean demos.

### `run_tests`
- **args:** `platform?: "EditMode"|"PlayMode" = "EditMode"`, `filter?: string`
- **Unity impl:** Unity Test Framework `TestRunnerApi`; register a callback collecting
  results; return summary. (Async — bridges the test-runner callback back to a single
  response.)
- **result:** `{ passed: int, failed: int, skipped: int, results: [{ name, status, message }] }`
- **Why it matters:** ties the live story to the CLI/CI story (`-runTests`).

---

## 9. Token-efficiency & tool-count strategy

MCP clients have practical tool-count limits and every schema costs context tokens.
v1 ships **6 tools** — fine. The growth plan (documented now, not built):
- **Two-tier lazy loading** (à la AnkleBreaker): keep a small core set exposed directly;
  put advanced tools behind one `unity_advanced(category, tool, args)` proxy with a
  `unity_list_advanced()` discovery call.
- Keep tool **results terse and structured** — no echoing whole scene trees.

---

## 10. Repo layout

```
unity-mcp-bridge/
├─ SPEC.md                  ← this file
├─ README.md               ← quickstart: build server, install package, claude mcp add
├─ CLAUDE.md               ← conventions for Claude Code working IN this repo
├─ MILESTONES.md           ← phased, checkable build plan (run on this)
├─ LICENSE                 ← MIT
├─ .gitignore
├─ .claude/                ← (optional) project settings / allowlist
├─ server/                 ← .NET 8 MCP server  (see §7)
├─ unity-package/          ← Unity Editor bridge package  (see §6)
├─ demo-unity-project/     ← tiny Unity 6 project for the demo (with an intentional bug)
└─ docs/
   └─ webinar-script.md    ← minute-by-minute run-of-show
```

---

## 11. Build & run (developer quickstart)

```bash
# 1. Server
cd server
dotnet build                      # restores ModelContextProtocol, builds net8.0

# 2. Unity: open demo-unity-project in Unity 6, the package auto-loads from Packages/.
#    Console should print: "[McpBridge] listening on ws://127.0.0.1:17890"

# 3. Register with Claude Code
claude mcp add unity-mcp-bridge -- dotnet run --project /abs/path/server

# 4. In Claude Code:
#    > read the unity console
#    > create a cube named Player and add a Rigidbody
#    > run the edit-mode tests
```

---

## 12. Milestones (summary — see MILESTONES.md for the checklist)

1. **Pipe** — bridge answers `ping`; server returns it to Claude. *Proves the channel.*
2. **`read_console`** + main-thread pump + ring buffer. *The core.*
3. **`create_gameobject` + `add_component`.** *The visual demo.*
4. **`run_playmode` + `run_tests`** + domain-reload/reconnect handling. *Closes the arc.*
5. **Webinar polish** — CLAUDE.md tuning, intentional bug, run-of-show script.

---

## 13. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Domain reload kills socket mid-demo | §4 graceful restart + server reconnect; Reload Domain off |
| Vendored WS server bugs | keep it tiny (~200 LOC), single client, text frames only |
| Main-thread deadlock if a tool blocks | tools must be non-blocking; `run_playmode`/`run_tests` use async callbacks, not sleeps |
| MCP tool schema drift | server tool signatures are the source of truth; keep arg names identical to wire `args` |
| Unity version API changes | pin to Unity 6 (6000.x); note APIs used in each tool |

---

## 14. License
MIT. Not affiliated with Unity Technologies. Educational/demo project.
