// ToolRegistry.cs - reflection-based [McpTool] discovery & invocation (SPEC §6).
//
// On first use, scan all loaded assemblies for static methods marked [McpTool],
// build a name -> (MethodInfo, parameters) map, then for each request:
//   - bind `args` JSON fields to parameters by NAME (with defaults for missing),
//   - invoke (already on the main thread, called from McpBridge.Pump),
//   - serialize the return value into { id, ok:true, result } - or, on throw,
//     into { id, ok:false, error } (NEVER let the exception escape).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.Editor
{
    public static class ToolRegistry
    {
        private sealed class Tool
        {
            public MethodInfo Method;
            public ParameterInfo[] Params;
            public string Description;
        }

        // Public metadata shapes (used by list_tools and the Editor window).
        public sealed class ToolInfo
        {
            public string name;
            public string description;
            public List<ParamInfo> parameters;
        }

        public sealed class ParamInfo
        {
            public string name;
            public string type;
            public bool optional;
            public string description;
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
                        map[attr.Name] = new Tool { Method = m, Params = m.GetParameters(), Description = attr.Description };
                    }
                }
            }
            _tools = map;
        }

        /// <summary>All discovered tools with their parameters (sorted by name).</summary>
        public static List<ToolInfo> GetToolInfos()
        {
            EnsureScanned();
            var list = new List<ToolInfo>();
            foreach (var kv in _tools)
            {
                var ps = new List<ParamInfo>();
                foreach (var p in kv.Value.Params)
                {
                    if (p.ParameterType == typeof(McpToolContext)) continue; // injected, not an arg
                    var pa = p.GetCustomAttribute<ParamAttribute>();
                    ps.Add(new ParamInfo
                    {
                        name = p.Name,
                        type = SimpleType(p.ParameterType),
                        optional = p.HasDefaultValue,
                        description = pa != null ? pa.Description : "",
                    });
                }
                list.Add(new ToolInfo { name = kv.Key, description = kv.Value.Description, parameters = ps });
            }
            return list.OrderBy(t => t.name).ToList();
        }

        private static string SimpleType(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "int";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string[])) return "string[]";
            if (typeof(JToken).IsAssignableFrom(t)) return "json";
            return t.Name;
        }

        /// <summary>
        /// Invoke a tool synchronously by name with a pre-parsed args object and RETURN
        /// its result (instead of replying over the wire). Used by custom commands to
        /// chain built-in tools. Async tools (those taking an McpToolContext) are not
        /// allowed here - a command step must complete inline. Throws on any failure.
        /// </summary>
        public static object InvokeDirect(string tool, JObject args)
        {
            EnsureScanned();
            if (string.IsNullOrEmpty(tool)) throw new Exception("step is missing 'tool'");
            if (!_tools.TryGetValue(tool, out var t)) throw new Exception($"unknown tool: {tool}");
            if (!ToolGate.IsEnabled(tool)) throw new Exception($"tool '{tool}' is disabled in the Unity Agent Bridge window");

            var values = new object[t.Params.Length];
            for (int i = 0; i < t.Params.Length; i++)
            {
                var p = t.Params[i];
                if (p.ParameterType == typeof(McpToolContext))
                    throw new Exception($"tool '{tool}' is async and cannot be used inside a command");

                var token = args?[p.Name];
                if (token == null || token.Type == JTokenType.Null)
                    values[i] = p.HasDefaultValue
                        ? p.DefaultValue
                        : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
                else if (typeof(JToken).IsAssignableFrom(p.ParameterType))
                    values[i] = token;
                else
                    values[i] = token.ToObject(p.ParameterType);
            }

            try { return t.Method.Invoke(null, values); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
        }

        /// <summary>
        /// Parse the request, bind args, invoke the tool, and reply via <paramref name="reply"/>.
        /// Synchronous tools reply with their return value; async tools that take an
        /// McpToolContext reply later via ctx.Complete/ctx.Fail (this method returns first).
        /// </summary>
        public static void Invoke(string requestJson, Action<string> reply)
        {
            JObject root;
            try { root = JObject.Parse(requestJson); }
            catch (Exception e) { reply(Protocol.Error("", e.ToString())); return; }
            Invoke(root, reply);
        }

        /// <summary>
        /// Same as <see cref="Invoke(string, Action{string})"/> but takes an already-parsed
        /// request object, so the hot path parses the JSON only once.
        /// </summary>
        public static void Invoke(JObject root, Action<string> reply)
        {
            string id = "";
            McpToolContext ctx = null;
            bool deferred = false;
            try
            {
                id = (string)root["id"] ?? "";
                ctx = new McpToolContext(id, reply);

                var toolName = (string)root["tool"];
                if (string.IsNullOrEmpty(toolName)) { ctx.Fail("missing tool"); return; }

                EnsureScanned();
                if (!_tools.TryGetValue(toolName, out var tool)) { ctx.Fail($"unknown tool: {toolName}"); return; }
                if (!ToolGate.IsEnabled(toolName)) { ctx.Fail($"tool '{toolName}' is disabled in the Unity Agent Bridge window"); return; }

                var args = root["args"] as JObject ?? new JObject();
                var values = new object[tool.Params.Length];
                for (int i = 0; i < tool.Params.Length; i++)
                {
                    var p = tool.Params[i];
                    if (p.ParameterType == typeof(McpToolContext))
                    {
                        values[i] = ctx;       // injected, not bound from args
                        deferred = true;
                        continue;
                    }

                    var token = args[p.Name];
                    if (token == null || token.Type == JTokenType.Null)
                    {
                        values[i] = p.HasDefaultValue
                            ? p.DefaultValue
                            : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
                    }
                    else if (typeof(JToken).IsAssignableFrom(p.ParameterType))
                    {
                        values[i] = token; // pass raw JSON through (generic tools like set_property)
                    }
                    else
                    {
                        values[i] = token.ToObject(p.ParameterType);
                    }
                }

                var result = tool.Method.Invoke(null, values);

                // Async tools own the reply; sync tools reply with their return value now.
                if (!deferred)
                    ctx.Complete(result);
            }
            catch (TargetInvocationException tie)
            {
                // Unwrap so Claude sees the real tool exception + stack trace.
                var msg = (tie.InnerException ?? tie).ToString();
                if (ctx != null) ctx.Fail(msg); else reply(Protocol.Error(id, msg));
            }
            catch (Exception e)
            {
                if (ctx != null) ctx.Fail(e.ToString()); else reply(Protocol.Error(id, e.ToString()));
            }
        }
    }
}
