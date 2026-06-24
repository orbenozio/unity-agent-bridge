// ToolGate.cs - per-tool enable/disable (an allow-list you control from the window).
//
// Disabled tools are rejected by ToolRegistry before they run, so the gate applies to
// BOTH the MCP server and the CLI. State persists in EditorPrefs.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityAgentBridge.Editor
{
    public static class ToolGate
    {
        private const string Key = "McpBridge.DisabledTools";
        private static HashSet<string> _disabled;

        private static HashSet<string> Disabled
        {
            get
            {
                if (_disabled == null)
                {
                    _disabled = new HashSet<string>(
                        EditorPrefs.GetString(Key, "")
                            .Split(',')
                            .Where(s => !string.IsNullOrEmpty(s)));
                }
                return _disabled;
            }
        }

        public static bool IsEnabled(string tool) => !Disabled.Contains(tool);

        public static void SetEnabled(string tool, bool enabled)
        {
            if (enabled) Disabled.Remove(tool);
            else Disabled.Add(tool);
            EditorPrefs.SetString(Key, string.Join(",", Disabled));
        }
    }
}
