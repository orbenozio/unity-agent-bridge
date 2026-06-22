// ConsoleTools.cs — read_console (SPEC §8, MILESTONES M2). The debugging star.
//
// A ring buffer is fed by Application.logMessageReceivedThreaded (installed in
// McpBridge's static ctor). read_console filters by level and returns the latest N.

using UnityMcpBridge.Editor;

namespace UnityMcpBridge.Editor.Tools
{
    public static class ConsoleTools
    {
        // TODO(M2): static ring buffer (capacity ~1000) of { level, message, stackTrace, timestamp }.
        //           Installed via Application.logMessageReceivedThreaded in McpBridge.

        [McpTool("read_console", "Read recent Unity Console entries (errors, warnings, logs) with stack traces.")]
        public static object ReadConsole(
            [Param("Levels to include: Error, Warning, Log. Empty/null = all.")] string[] levels = null,
            [Param("Max entries to return.")] int limit = 50)
        {
            // TODO(M2): filter the ring buffer by `levels`, take the latest `limit`, return.
            // return new { entries = filtered };
            return new { entries = new object[0], note = "ConsoleTools.ReadConsole not implemented (M2)." };
        }
    }
}
