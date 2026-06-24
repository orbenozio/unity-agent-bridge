# Webinar run-of-show - "From terminal to a running Unity game"

A ~40-minute arc demonstrating Claude Code driving Unity 6 through `unity-agent-bridge`.
Fill in timings to taste; the beats below are the spine.

## 0. Cold open (2 min)
- One sentence: *"I'm going to build and debug a Unity game without touching the code
  editor - just a terminal and Claude."*
- Show the architecture slide (SPEC §2): Claude → stdio → .NET server → WebSocket →
  Unity bridge → main thread.

## 1. The pipe is real (3 min)
- In Claude Code: **"ping unity"** → returns the Unity version.
- Show `unity_status`. Explain stdio vs WebSocket and *why two transports* (SPEC §2).

## 2. The main-thread reveal (4 min)
- Slide: the ~6-line `_mainThreadJobs` queue + `EditorApplication.update` pump (SPEC §3).
- Punchline: *"That's the whole trick that lets an external AI touch the Editor."*

## 3. Debugging live - the strongest moment (8 min)
- The demo project has an **intentional NullReferenceException**.
- Prompt: **"the game is throwing an error, read the console and fix it."**
- Claude calls `read_console`, sees the stack trace, opens the script, fixes it.
- This is the emotional peak - real debugging, not code generation.

## 4. Building scene content (6 min)
- **"create a cube named Player and add a Rigidbody and a BoxCollider."**
- Watch the GameObject appear in the Hierarchy (`create_gameobject`, `add_component`).
- Ctrl+Z to show Undo works - Claude is a good Editor citizen.

## 5. The test loop (6 min)
- **"run the edit-mode tests"** → `run_tests` reports pass/fail.
- **"enter play mode for 3 seconds and tell me if anything errors"** → `run_playmode`.
- Tie to CI: the same Test Framework runs headless via `-runTests` (mention SPEC §12).

## 6. Extend it on stage - the "wow" (5 min)
- Add a brand-new tool **live**: one `[McpTool]` method (e.g. `list_scene_objects`) +
  one forwarder. Recompile. Ask Claude to use it.
- Punchline: *"New capability for the AI in one line of C#."*

## 7. Reload resilience + close (4 min)
- Recompile scripts mid-session; show the next tool call still works (SPEC §4).
- Recap the arc; point to the repo, SPEC, and MILESTONES.
- Disclaimer: educational demo, not affiliated with Unity.

## Pre-flight checklist
- [ ] Unity 6 demo project open, bridge listening (`ws://127.0.0.1:17890`).
- [ ] `claude mcp add unity-agent-bridge` registered and green.
- [ ] Intentional bug present and reproducible.
- [ ] `Enter Play Mode Settings → Reload Domain = off` for smooth Play Mode.
- [ ] Font size up, Console visible, Hierarchy visible.
