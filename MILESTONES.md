# MILESTONES — build plan for `unity-mcp-bridge`

A phased, checkable plan. Each milestone is **independently demoable** and ends with a
working end-to-end slice. Build top to bottom. See [`SPEC.md`](./SPEC.md) for detail.

> Tip for Claude Code: implement one milestone, verify its "Done when" criterion against
> a live Unity 6 Editor, then move on. Do not start M4 before M1–M2 work.

---

## M0 — Scaffolding ✅ (this commit)
- [x] Repo structure, SPEC, README, CLAUDE.md, LICENSE, .gitignore
- [x] Stub files with clear TODOs in `server/` and `unity-package/`

---

## M1 — The pipe (prove the channel)
Goal: Claude calls a tool, the request travels Claude → server → Unity → back.
- [ ] `unity-package/Editor/WebSocketServer.cs` — minimal RFC6455 server, single text
      client, `OnMessage(string, reply)`. Localhost only.
- [ ] `unity-package/Editor/McpBridge.cs` — `[InitializeOnLoad]`, start server, log
      `listening on ws://127.0.0.1:17890`, main-thread `Pump()` on `EditorApplication.update`.
- [ ] Implement a built-in `ping` tool (returns `{ pong: true, unityVersion }`).
- [ ] `server/UnityClient.cs` — `ClientWebSocket`, connect, `CallAsync(tool,args)` with
      id-correlated `TaskCompletionSource`, receive loop.
- [ ] `server/McpTools.cs` — `unity_status` + a `ping` forwarder.
- [ ] `server/Program.cs` — stdio MCP host (`ModelContextProtocol`), DI the `UnityClient`.

**Done when:** in Claude Code, asking it to "ping unity" returns the Unity version.

---

## M2 — `read_console` (the core + main-thread pump)
Goal: Claude can see Console output, including errors and stack traces.
- [ ] Console ring buffer hooked to `Application.logMessageReceivedThreaded` (cap ~1000),
      installed in `McpBridge` static ctor.
- [ ] `[McpTool]` / `[Param]` attributes + `ToolRegistry` (reflection scan, arg binding
      by name, try/catch → `ok:false`).
- [ ] `Tools/ConsoleTools.cs` → `read_console(levels?, limit=50)`.
- [ ] Forwarder `read_console` in `server/McpTools.cs`.

**Done when:** Claude reads back a `Debug.LogError` you trigger in the Editor, with its
stack trace.

---

## M3 — GameObjects (the visual demo)
Goal: Claude builds scene content you can watch appear.
- [ ] `Tools/GameObjectTools.cs` → `create_gameobject(name, primitive="None")`
      (with `Undo.RegisterCreatedObjectUndo`).
- [ ] `add_component(target, componentType)` — resolve target by name/instanceId,
      resolve `Type` across assemblies, `Undo.AddComponent`.
- [ ] Forwarders in `server/McpTools.cs`.

**Done when:** "create a cube named Player and add a Rigidbody" makes a Rigidbody-bearing
cube appear in the Hierarchy.

---

## M4 — Play Mode, tests, and resilience (close the arc)
Goal: live test loop + survives reload/Play Mode.
- [ ] `Tools/PlayModeTools.cs` → `run_playmode(seconds=3)`, capturing errors during play.
- [ ] `Tools/TestTools.cs` → `run_tests(platform, filter?)` via `TestRunnerApi`
      (async callback → single response).
- [ ] Domain-reload handling: `AssemblyReloadEvents.before/afterAssemblyReload`
      stop/start the server.
- [ ] Server-side reconnect with exponential backoff + parked-request queue + timeout.
- [ ] Forwarders `run_playmode`, `run_tests` in `server/McpTools.cs`.

**Done when:** Claude runs EditMode tests and reports pass/fail; recompiling scripts
mid-session does not break the next tool call.

---

## M5 — Webinar polish
- [ ] Add an **intentional bug** to `demo-unity-project/` (a `NullReferenceException`
      Claude will find via `read_console` and fix live).
- [ ] A couple of trivial EditMode tests in the demo project.
- [ ] Tune `CLAUDE.md` in the demo project so Claude follows project conventions.
- [ ] Fill in [`docs/webinar-script.md`](./docs/webinar-script.md) with the run-of-show.
- [ ] Optional: demo the "add a new tool in one line" moment on stage.

**Done when:** you can run the full webinar arc end to end without touching the keyboard
except to type prompts.
