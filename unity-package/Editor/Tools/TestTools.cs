// TestTools.cs — run_tests (SPEC §8, MILESTONES M4).
//
// Uses Unity's TestRunnerApi to run EditMode/PlayMode tests. The API is callback-
// based; collect results in an ICallbacks implementation and complete the single
// response when the run finishes. Ties the live story to the CLI/CI story (-runTests).

using System;
using UnityMcpBridge.Editor;

namespace UnityMcpBridge.Editor.Tools
{
    public static class TestTools
    {
        [McpTool("run_tests", "Run Unity tests (EditMode or PlayMode) and return pass/fail results.")]
        public static object RunTests(
            [Param("Test platform: EditMode or PlayMode.")] string platform = "EditMode",
            [Param("Optional name/category filter.")] string filter = null)
        {
            // TODO(M4): build TestRunnerApi + Filter(testMode); register ICallbacks that
            //           collect { name, status, message }; Execute; return summary when done.
            //   return new { passed, failed, skipped, results };
            throw new NotImplementedException("TestTools.RunTests — implement in M4.");
        }
    }
}
