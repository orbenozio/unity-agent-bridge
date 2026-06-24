// SystemTools.cs - bridge self-description tools.

using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class SystemTools
    {
        [McpTool("list_tools", "List every available bridge tool with its parameters (discovery).")]
        public static object ListTools()
        {
            return new { tools = ToolRegistry.GetToolInfos() };
        }

        [McpTool("bridge_info", "Bridge status: Unity version, listen host/port, connection.")]
        public static object BridgeInfo()
        {
            return new
            {
                unityVersion = Application.unityVersion,
                host = McpBridge.Host,
                port = McpBridge.Port,
                listening = McpBridge.IsListening,
                clientConnected = McpBridge.ClientConnected,
            };
        }
    }
}
