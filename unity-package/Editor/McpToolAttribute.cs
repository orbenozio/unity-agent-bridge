// McpToolAttribute.cs — the one-line extension point (SPEC §6).
//
// Mark any static method with [McpTool("name","description")] and it becomes a
// callable tool. Annotate parameters with [Param("help")] for documentation.
// ToolRegistry scans these via reflection on load.

using System;

namespace UnityAgentBridge.Editor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class McpToolAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        public McpToolAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ParamAttribute : Attribute
    {
        public string Description { get; }
        public ParamAttribute(string description) => Description = description;
    }
}
