# CLAUDE.md - demo-unity-project

An example Unity 6 project driven by `unity-agent-bridge`. Drive it through the MCP
tools - not by editing the Editor by hand. The full tool set is available here; the
common ones:

- **Debug & build loop:** `read_console`, `refresh_assets`, `compile_errors`,
  `run_playmode`, `run_tests`, `unity_status`.
- **Scene & GameObjects:** `create_gameobject`, `add_component`, `set_transform`,
  `set_parent`, `set_rect`, `set_text`, `set_property`, `delete_gameobject`,
  `list_scene`, `get_object`, `save_scene`.
- **Prefabs & capture:** `instantiate_prefab`, `create_prefab`, `capture_screenshot`.
- **Editor control & extend:** `execute_menu_item`, custom command packs, and custom
  C# tools (`new_custom_tool` / `call_tool`).

Run `list_tools` for the live, complete list.

## Conventions
- When something errors, call `read_console` (filter to `Error`) and read the stack
  trace BEFORE changing code.
- After a code change, recompiling reloads the domain and the bridge reconnects on its
  own. Re-run `run_playmode` / `run_tests` to confirm the fix.
- Build scene content with `create_gameobject` / `add_component`. Everything is
  Undo-registered, so Ctrl+Z works - be a good Editor citizen.
- Prefer `capture_screenshot` to see what you built; keep edits surgical.

## Example: an edit-compile-fix loop
`Assets/Scripts/GameBoot.cs` has an intentional NullReferenceException on entering Play
Mode - a small example for the debugging loop:

1. `run_playmode` (a few seconds) -> see the NullReferenceException in the result.
2. `read_console` levels=["Error"] -> confirm the stack trace points at GameBoot.cs.
3. Fix GameBoot.cs with a minimal null check (don't rewrite the method).
4. `run_playmode` again -> no errors.
