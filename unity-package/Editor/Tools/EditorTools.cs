// EditorTools.cs - editor-control tools that reach the rest of the Editor with one
// generic call, instead of a bespoke tool per action.

using System;
using UnityEditor;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class EditorTools
    {
        [McpTool("execute_menu_item", "Execute an Editor menu item by its full path, e.g. 'GameObject/Align With View' or 'Assets/Refresh'. One call reaches any menu command, built-in or custom.")]
        public static object ExecuteMenuItem(
            [Param("Full menu path, e.g. 'Assets/Refresh'.")] string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath)) throw new ArgumentException("menuPath is required");
            var executed = EditorApplication.ExecuteMenuItem(menuPath);
            return new { executed, menuPath };
        }
    }
}
