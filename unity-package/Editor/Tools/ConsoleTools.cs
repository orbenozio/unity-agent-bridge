// ConsoleTools.cs - read_console. The debugging star.
//
// A ring buffer is fed by Application.logMessageReceivedThreaded (installed via
// ConsoleTools.Install() from McpBridge's static ctor). read_console filters by
// level and returns the latest N entries.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class ConsoleTools
    {
        private const int Capacity = 1000;

        private struct Entry
        {
            public string level;
            public string message;
            public string stackTrace;
            public string timestamp;
        }

        private static readonly object _lock = new object();
        private static readonly Entry[] _ring = new Entry[Capacity];
        private static int _count;   // number of valid entries (<= Capacity)
        private static int _next;    // next write index
        private static bool _installed;

        /// <summary>Hook the log callback. Idempotent; safe across domain reloads.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            // Threaded variant: fires on whatever thread logged. The ring is locked.
            Application.logMessageReceivedThreaded += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            var entry = new Entry
            {
                level = MapLevel(type),
                message = message,
                stackTrace = stackTrace,
                timestamp = DateTime.UtcNow.ToString("o"),
            };
            lock (_lock)
            {
                _ring[_next] = entry;
                _next = (_next + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }

        private static string MapLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return "Error";
                case LogType.Warning:
                    return "Warning";
                default:
                    return "Log";
            }
        }

        [McpTool("read_console", "Read recent Unity Console entries (errors, warnings, logs) with stack traces.")]
        public static object ReadConsole(
            [Param("Levels to include: Error, Warning, Log. Empty/null = all.")] string[] levels = null,
            [Param("Max entries to return.")] int limit = 50)
        {
            Entry[] snapshot;
            lock (_lock)
            {
                snapshot = new Entry[_count];
                int start = (_next - _count + Capacity) % Capacity;
                for (int i = 0; i < _count; i++)
                    snapshot[i] = _ring[(start + i) % Capacity];
            }

            IEnumerable<Entry> q = snapshot;
            if (levels != null && levels.Length > 0)
            {
                var set = new HashSet<string>(levels, StringComparer.OrdinalIgnoreCase);
                q = q.Where(e => set.Contains(e.level));
            }

            var filtered = q.ToList();
            if (limit > 0 && filtered.Count > limit)
                filtered = filtered.GetRange(filtered.Count - limit, limit);

            // Project to a clean shape (chronological: oldest -> newest).
            var entries = filtered
                .Select(e => new { e.level, e.message, e.stackTrace, e.timestamp })
                .ToList();

            return new { count = entries.Count, entries };
        }
    }
}
