# CLAUDE.md - demo-unity-project

The live-demo Unity 6 project driven by `unity-agent-bridge`. You drive it through MCP
tools (read_console, create_gameobject, add_component, run_playmode, run_tests,
unity_status) - not by editing the Editor by hand.

## Conventions
- When something errors, call `read_console` (filter to `Error`) and read the stack
  trace BEFORE changing code.
- There is an intentional bug in `Assets/Scripts/GameBoot.cs` - a NullReferenceException
  on entering Play Mode. The fix is a minimal null check; don't rewrite the method.
- After a code change, recompiling reloads the domain and the bridge reconnects on its
  own. Re-run `run_playmode` / `run_tests` to confirm the fix.
- Build scene content with `create_gameobject` / `add_component`. Everything is
  Undo-registered, so Ctrl+Z works - be a good Editor citizen.
- Keep edits surgical. This project exists for a webinar, not production.

## Quick loop for the debugging demo
1. `run_playmode` (a few seconds) -> see the NullReferenceException in the result.
2. `read_console` levels=["Error"] -> confirm the stack trace points at GameBoot.cs.
3. Fix GameBoot.cs with a null check.
4. `run_playmode` again -> no errors.
