// ToolRegistry.cs — reflection-based [McpTool] discovery & invocation (SPEC §6).
//
// On first use, scan all loaded assemblies for static methods marked [McpTool],
// build a name -> (MethodInfo, parameters) map, then for each request:
//   - bind `args` JSON fields to parameters by NAME (with defaults for missing),
//   - invoke (already on the main thread, called from McpBridge.Pump),
//   - serialize the return value into { id, ok:true, result } — or, on throw,
//     into { id, ok:false, error } (NEVER let the exception escape).

using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor
{
    public static class ToolRegistry
    {
        private sealed class Tool
        {
            public MethodInfo Method;
            public ParameterInfo[] Params;
        }

        private static Dictionary<string, Tool> _tools;

        private static void EnsureScanned()
        {
            if (_tools != null) return;

            var map = new Dictionary<string, Tool>(StringComparer.Ordinal);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; } // skip assemblies that can't be reflected
                foreach (var t in types)
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        var attr = m.GetCustomAttribute<McpToolAttribute>();
                        if (attr == null) continue;
                        map[attr.Name] = new Tool { Method = m, Params = m.GetParameters() };
                    }
                }
            }
            _tools = map;
        }

        /// <summary>Invoke the tool named in the request and return the JSON response string.</summary>
        public static string Invoke(string requestJson)
        {
            string id = "";
            try
            {
                var root = JObject.Parse(requestJson);
                id = (string)root["id"] ?? "";
                var toolName = (string)root["tool"];
                if (string.IsNullOrEmpty(toolName))
                    return Protocol.Error(id, "missing tool");

                EnsureScanned();
                if (!_tools.TryGetValue(toolName, out var tool))
                    return Protocol.Error(id, $"unknown tool: {toolName}");

                var args = root["args"] as JObject ?? new JObject();
                var values = new object[tool.Params.Length];
                for (int i = 0; i < tool.Params.Length; i++)
                {
                    var p = tool.Params[i];
                    var token = args[p.Name];
                    if (token == null || token.Type == JTokenType.Null)
                    {
                        values[i] = p.HasDefaultValue
                            ? p.DefaultValue
                            : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
                    }
                    else
                    {
                        values[i] = token.ToObject(p.ParameterType);
                    }
                }

                var result = tool.Method.Invoke(null, values);
                var resultJson = JsonConvert.SerializeObject(result ?? new { });
                return Protocol.Ok(id, resultJson);
            }
            catch (TargetInvocationException tie)
            {
                // Unwrap so Claude sees the real tool exception + stack trace.
                return Protocol.Error(id, (tie.InnerException ?? tie).ToString());
            }
            catch (Exception e)
            {
                return Protocol.Error(id, e.ToString());
            }
        }
    }
}
