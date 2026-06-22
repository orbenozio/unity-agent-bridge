// TestTools.cs — run_tests (SPEC §8, MILESTONES M4).
//
// Uses Unity's TestRunnerApi to run EditMode/PlayMode tests. The API is callback-
// based; collect results in an ICallbacks implementation and complete the single
// response (ctx.Complete) when the run finishes. Ties the live story to CI (-runTests).

using System;
using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class TestTools
    {
        // Keep a reference so the callbacks object + api aren't GC'd mid-run.
        private static Collector _active;

        [McpTool("run_tests", "Run Unity tests (EditMode or PlayMode) and return pass/fail results.")]
        public static void RunTests(
            [Param("Test platform: EditMode or PlayMode.")] string platform = "EditMode",
            [Param("Optional full-name filter (exact test name).")] string filter = null,
            McpToolContext ctx = null)
        {
            if (_active != null) { ctx.Fail("a run_tests session is already in progress"); return; }

            var mode = string.Equals(platform, "PlayMode", StringComparison.OrdinalIgnoreCase)
                ? TestMode.PlayMode
                : TestMode.EditMode;

            var testFilter = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(filter))
                testFilter.testNames = new[] { filter };

            var api = UnityEngine.ScriptableObject.CreateInstance<TestRunnerApi>();
            var collector = new Collector(api, ctx);
            _active = collector;

            api.RegisterCallbacks(collector);
            api.Execute(new ExecutionSettings(testFilter));
        }

        private sealed class Collector : ICallbacks
        {
            private readonly TestRunnerApi _api;
            private readonly McpToolContext _ctx;

            public Collector(TestRunnerApi api, McpToolContext ctx)
            {
                _api = api;
                _ctx = ctx;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                int passed = 0, failed = 0, skipped = 0;
                var results = new List<object>();
                Walk(result, ref passed, ref failed, ref skipped, results);

                try { _api.UnregisterCallbacks(this); } catch { /* ignore */ }
                _active = null;

                _ctx.Complete(new { passed, failed, skipped, results });
            }

            private static void Walk(ITestResultAdaptor node, ref int passed, ref int failed,
                ref int skipped, List<object> results)
            {
                var children = node.Children;
                bool hasChildren = false;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        hasChildren = true;
                        Walk(child, ref passed, ref failed, ref skipped, results);
                    }
                }

                if (hasChildren) return; // only record leaf test cases

                switch (node.TestStatus)
                {
                    case TestStatus.Passed: passed++; break;
                    case TestStatus.Failed: failed++; break;
                    case TestStatus.Skipped: skipped++; break;
                    default: break; // Inconclusive
                }

                results.Add(new
                {
                    name = node.Test != null ? node.Test.FullName : node.Name,
                    status = node.TestStatus.ToString(),
                    message = node.Message,
                });
            }
        }
    }
}
