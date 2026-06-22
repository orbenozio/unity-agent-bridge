// PlayModeTools.cs — run_playmode (SPEC §8, MILESTONES M4).
//
// Enters Play Mode, schedules an exit after `seconds`, and reports errors logged
// during play. NON-BLOCKING: never Thread.Sleep — the exit is scheduled via an
// EditorApplication.update timer and the response completes (ctx.Complete) when Play
// Mode exits. Relies on Disable-Domain-Reload (set in McpBridge) so the socket and
// this session survive the play transition.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class PlayModeTools
    {
        private static Session _active;

        [McpTool("run_playmode", "Enter Play Mode for N seconds, then exit; return errors logged during play.")]
        public static void RunPlayMode(
            [Param("How many seconds to stay in Play Mode.")] double seconds = 3,
            McpToolContext ctx = null)
        {
            if (_active != null) { ctx.Fail("a run_playmode session is already in progress"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { ctx.Fail("Editor is already in or entering Play Mode"); return; }
            if (seconds <= 0) seconds = 3;

            _active = new Session(seconds, ctx);
            _active.Start();
        }

        private sealed class Session
        {
            private readonly double _seconds;
            private readonly McpToolContext _ctx;
            private readonly List<string> _errors = new List<string>();
            private readonly object _lock = new object();

            private double _startTime;
            private double _enterTime;
            private bool _entered;
            private bool _exitRequested;

            public Session(double seconds, McpToolContext ctx)
            {
                _seconds = seconds;
                _ctx = ctx;
            }

            public void Start()
            {
                _startTime = EditorApplication.timeSinceStartup;
                Application.logMessageReceivedThreaded += OnLog;
                EditorApplication.playModeStateChanged += OnState;
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }

            private void OnLog(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    lock (_lock) _errors.Add(message);
            }

            private void OnState(PlayModeStateChange change)
            {
                if (change == PlayModeStateChange.EnteredPlayMode)
                {
                    _entered = true;
                    _enterTime = EditorApplication.timeSinceStartup;
                }
                else if (change == PlayModeStateChange.EnteredEditMode)
                {
                    Finish(true);
                }
            }

            private void Tick()
            {
                var now = EditorApplication.timeSinceStartup;

                // Safety: if Play Mode never entered, give up rather than hang forever.
                if (!_entered && now - _startTime > 15.0)
                {
                    Cleanup();
                    _active = null;
                    _ctx.Fail("Play Mode did not start within 15s");
                    return;
                }

                if (_entered && !_exitRequested && now - _enterTime >= _seconds)
                {
                    _exitRequested = true;
                    EditorApplication.ExitPlaymode();
                }
            }

            private void Finish(bool exited)
            {
                Cleanup();
                string[] errs;
                lock (_lock) errs = _errors.ToArray();
                _active = null;
                _ctx.Complete(new { entered = _entered, exited, errors = errs });
            }

            private void Cleanup()
            {
                Application.logMessageReceivedThreaded -= OnLog;
                EditorApplication.playModeStateChanged -= OnState;
                EditorApplication.update -= Tick;
            }
        }
    }
}
