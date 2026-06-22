// PlayModeTools.cs — run_playmode (SPEC §8, MILESTONES M4).
//
// Enters Play Mode, schedules an exit after `seconds`, and reports errors logged
// during play. NON-BLOCKING: never Thread.Sleep — schedule the exit via an
// EditorApplication.update timer and complete the response when Play Mode exits.
// Interacts with domain-reload handling (SPEC §4) — see the Reload Domain = off tip.

using System;
using UnityMcpBridge.Editor;

namespace UnityMcpBridge.Editor.Tools
{
    public static class PlayModeTools
    {
        [McpTool("run_playmode", "Enter Play Mode for N seconds, then exit; return errors logged during play.")]
        public static object RunPlayMode(
            [Param("How many seconds to stay in Play Mode.")] double seconds = 3)
        {
            // TODO(M4): clear play-mode error capture; EditorApplication.EnterPlaymode();
            //           schedule exit after `seconds`; collect errors; return when exited.
            //   return new { entered = true, exited = true, errors = new string[0] };
            throw new NotImplementedException("PlayModeTools.RunPlayMode — implement in M4.");
        }
    }
}
