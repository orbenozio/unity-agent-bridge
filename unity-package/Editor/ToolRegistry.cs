// ToolRegistry.cs — reflection-based [McpTool] discovery & invocation (SPEC §6).
//
// On first use, scan all loaded assemblies for static methods marked [McpTool],
// build a name -> (MethodInfo, parameters) map, then for each request:
//   - bind `args` JSON fields to parameters by NAME (with defaults for missing),
//   - invoke (already on the main thread, called from McpBridge.Pump),
//   - serialize the return value into { id, ok:true, result } — or, on throw,
//     into { id, ok:false, error } (NEVER let the exception escape).

using System;

namespace UnityMcpBridge.Editor
{
    public static class ToolRegistry
    {
        // TODO(M2): lazy reflection scan of [McpTool] static methods across assemblies.
        // TODO(M2): arg binding by name + defaults; JSON (de)serialization.

        /// <summary>Invoke the tool named in the request and return the JSON response string.</summary>
        public static string Invoke(string requestJson)
        {
            // TODO(M2): implement. Shape:
            //   var req = Parse(requestJson);              // { id, tool, args }
            //   if (!_tools.TryGetValue(req.tool, out var t))
            //       return Error(req.id, $"unknown tool: {req.tool}");
            //   try { var result = t.InvokeBound(req.args); return Ok(req.id, result); }
            //   catch (Exception e) { return Error(req.id, e.ToString()); }
            throw new NotImplementedException("ToolRegistry.Invoke — implement in M2 (SPEC §6).");
        }
    }
}
