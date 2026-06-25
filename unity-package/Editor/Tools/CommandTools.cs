// CommandTools.cs - project-defined CUSTOM COMMANDS: no-code, shareable macros.
//
// A "command" is a named, parameterized sequence of existing bridge tool calls,
// stored as JSON in the project. It is the data-driven counterpart to a C# [McpTool]:
// the agent (or a human) composes built-in tools into a higher-level action without
// writing or compiling any code. Commands live under:
//
//     <project>/UnityAgentBridge/Commands/<name>.json
//
// and can be bundled into a shareable .json "pack" (export_commands) that drops into
// another project (import_commands). Claude discovers them with list_commands and
// runs them with run_command - no per-command MCP forwarder needed.
//
// Command shape:
//   { "name", "description",
//     "params": [ { "name", "default" } ],
//     "steps":  [ { "tool", "args": { ... ${param} ... } } ] }
//
// Substitution: in a step's args, "${p}" alone is replaced by param p with its JSON
// TYPE preserved (number stays a number); "${p}" inside a longer string is textual.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class CommandTools
    {
        // --- storage ------------------------------------------------------------

        public static string CommandsDir
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath).FullName; // <project>/ (parent of Assets)
                return Path.Combine(root, "UnityAgentBridge", "Commands");
            }
        }

        public static string DefaultPackPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UnityAgentBridge", "commands-pack.json");

        private static string PathFor(string name) => Path.Combine(CommandsDir, name + ".json");

        private static readonly Regex NameRx = new Regex(@"^[A-Za-z0-9_-]+$");
        private static readonly Regex WholeToken = new Regex(@"^\$\{(\w+)\}$");
        private static readonly Regex InlineToken = new Regex(@"\$\{(\w+)\}");

        private static List<JObject> LoadAll()
        {
            var list = new List<JObject>();
            if (!Directory.Exists(CommandsDir)) return list;
            foreach (var f in Directory.GetFiles(CommandsDir, "*.json"))
            {
                try
                {
                    var obj = JObject.Parse(File.ReadAllText(f));
                    if (obj["name"] == null) obj["name"] = Path.GetFileNameWithoutExtension(f);
                    list.Add(obj);
                }
                catch { /* skip an invalid file rather than fail the whole listing */ }
            }
            return list;
        }

        private static JObject Load(string name)
        {
            var p = PathFor(name);
            return File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : null;
        }

        private static void Persist(JObject cmd)
        {
            Directory.CreateDirectory(CommandsDir);
            File.WriteAllText(PathFor((string)cmd["name"]), cmd.ToString(Formatting.Indented));
        }

        // --- tools --------------------------------------------------------------

        [McpTool("list_commands", "List project-defined custom commands (named macros of tool calls) with their params.")]
        public static object ListCommands()
        {
            var commands = LoadAll()
                .Select(c => new
                {
                    name = (string)c["name"],
                    description = (string)c["description"] ?? "",
                    @params = (c["params"] as JArray)?
                        .Select(p => new { name = (string)p["name"], @default = p["default"] })
                        .ToList(),
                    steps = (c["steps"] as JArray)?.Count ?? 0,
                })
                .OrderBy(c => c.name)
                .ToList();
            return new { count = commands.Count, commands };
        }

        [McpTool("run_command", "Run a project-defined custom command by name. args is a JSON object of its params.")]
        public static object RunCommand(
            [Param("Command name.")] string name,
            [Param("Params object for the command, e.g. { \"name\": \"Enemy\" }.")] JToken args = null)
        {
            var cmd = Load(name);
            if (cmd == null) throw new Exception($"unknown command: {name}");

            // Param map: defaults first, then caller-supplied values override.
            var map = new Dictionary<string, JToken>();
            if (cmd["params"] is JArray ps)
                foreach (var p in ps)
                {
                    var pn = (string)p["name"];
                    if (pn != null) map[pn] = p["default"] ?? JValue.CreateNull();
                }
            if (args is JObject argObj)
                foreach (var prop in argObj.Properties())
                    map[prop.Name] = prop.Value;

            var steps = cmd["steps"] as JArray ?? new JArray();
            var results = new List<object>();
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i] as JObject;
                var tool = (string)step?["tool"];
                if (string.IsNullOrEmpty(tool))
                {
                    results.Add(new { index = i, ok = false, error = "step is missing 'tool'" });
                    return new { command = name, ok = false, failedAt = i, steps = results };
                }

                var stepArgs = step["args"] as JObject ?? new JObject();
                var bound = (JObject)Substitute(stepArgs.DeepClone(), map);
                try
                {
                    var result = ToolRegistry.InvokeDirect(tool, bound);
                    results.Add(new { index = i, tool, ok = true, result });
                }
                catch (Exception e)
                {
                    results.Add(new { index = i, tool, ok = false, error = e.Message });
                    return new { command = name, ok = false, failedAt = i, steps = results };
                }
            }
            return new { command = name, ok = true, steps = results };
        }

        [McpTool("save_command", "Create or update a custom command. steps = JSON array of {tool,args}; parameters = optional JSON array of {name,default}. Use ${param} in args.")]
        public static object SaveCommand(
            [Param("Command name (letters, digits, '_' or '-').")] string name,
            [Param("JSON array of steps: [{ tool, args }].")] JToken steps,
            [Param("Human description.")] string description = null,
            [Param("JSON array of params: [{ name, default }].")] JToken parameters = null)
        {
            if (string.IsNullOrEmpty(name) || !NameRx.IsMatch(name))
                throw new Exception("invalid command name; use letters, digits, '_' or '-'.");
            if (!(steps is JArray stepArr) || stepArr.Count == 0)
                throw new Exception("steps must be a non-empty JSON array of { tool, args }.");

            var cmd = new JObject
            {
                ["name"] = name,
                ["description"] = description ?? "",
                ["params"] = parameters as JArray ?? new JArray(),
                ["steps"] = stepArr,
            };
            Persist(cmd);
            return new { saved = true, name, path = PathFor(name) };
        }

        [McpTool("new_command", "Scaffold a new custom command from a template (writes <name>.json). Edit it to add your steps.")]
        public static object NewCommand(
            [Param("Command name (letters, digits, '_' or '-').")] string name,
            [Param("Human description.")] string description = null,
            [Param("Overwrite if it already exists.")] bool overwrite = false)
        {
            if (string.IsNullOrEmpty(name) || !NameRx.IsMatch(name))
                throw new Exception("invalid command name; use letters, digits, '_' or '-'.");
            if (File.Exists(PathFor(name)) && !overwrite)
                throw new Exception($"{name}.json already exists (pass overwrite=true to replace it).");

            var cmd = new JObject
            {
                ["name"] = name,
                ["description"] = description ?? $"Custom command {name}",
                ["params"] = new JArray { new JObject { ["name"] = "example", ["default"] = "value" } },
                ["steps"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "create_gameobject",
                        ["args"] = new JObject { ["name"] = "${example}", ["primitive"] = "Cube" },
                    },
                },
            };
            Persist(cmd);
            return new { created = true, name, path = PathFor(name) };
        }

        [McpTool("delete_command", "Delete a project-defined custom command by name.")]
        public static object DeleteCommand([Param("Command name.")] string name)
        {
            var p = PathFor(name);
            if (!File.Exists(p)) return new { deleted = false, name };
            File.Delete(p);
            return new { deleted = true, name };
        }

        [McpTool("export_commands", "Bundle custom commands into a shareable .json pack. names empty = all. Returns the pack path.")]
        public static object ExportCommands(
            [Param("Output .json path. Default: <project>/UnityAgentBridge/commands-pack.json")] string path = null,
            [Param("Command names to include. Empty/null = all.")] string[] names = null)
        {
            IEnumerable<JObject> sel = LoadAll();
            if (names != null && names.Length > 0)
            {
                var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                sel = sel.Where(c => set.Contains((string)c["name"]));
            }

            var arr = new JArray();
            foreach (var c in sel) arr.Add(c);
            var pack = new JObject { ["version"] = 1, ["commands"] = arr };

            var outPath = string.IsNullOrEmpty(path) ? DefaultPackPath : path;
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, pack.ToString(Formatting.Indented));
            return new { exported = arr.Count, path = outPath };
        }

        [McpTool("import_commands", "Import custom commands from a .json pack (a file path OR inline pack JSON). overwrite replaces existing names.")]
        public static object ImportCommands(
            [Param("A file path to a pack, or inline pack JSON.")] string pack,
            [Param("Overwrite commands whose name already exists.")] bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(pack)) throw new Exception("pack is empty.");

            var content = pack;
            var trimmed = pack.TrimStart();
            var looksInline = trimmed.StartsWith("{") || trimmed.StartsWith("[");
            if (!looksInline && File.Exists(pack)) content = File.ReadAllText(pack);

            var root = JToken.Parse(content);
            JArray commands =
                root is JObject o && o["commands"] is JArray ca ? ca :
                root is JArray ra ? ra :
                throw new Exception("pack must be { version, commands:[...] } or an array of commands.");

            int imported = 0, skipped = 0;
            foreach (var c in commands.OfType<JObject>())
            {
                var name = (string)c["name"];
                if (string.IsNullOrEmpty(name) || !NameRx.IsMatch(name)) { skipped++; continue; }
                if (!(c["steps"] is JArray)) { skipped++; continue; }
                if (!overwrite && File.Exists(PathFor(name))) { skipped++; continue; }

                if (c["params"] == null) c["params"] = new JArray();
                if (c["description"] == null) c["description"] = "";
                Persist(c);
                imported++;
            }
            return new { imported, skipped };
        }

        // --- ${param} substitution ---------------------------------------------

        // Returns the (possibly new) token for a leaf; mutates containers in place and
        // only reassigns a child when the replacement is a different reference (so we
        // never set a container token to itself, which Newtonsoft rejects).
        private static JToken Substitute(JToken node, IReadOnlyDictionary<string, JToken> map)
        {
            switch (node.Type)
            {
                case JTokenType.Object:
                    var o = (JObject)node;
                    foreach (var prop in o.Properties().ToList())
                    {
                        var replaced = Substitute(prop.Value, map);
                        if (!ReferenceEquals(replaced, prop.Value)) prop.Value = replaced;
                    }
                    return o;
                case JTokenType.Array:
                    var a = (JArray)node;
                    for (int i = 0; i < a.Count; i++)
                    {
                        var replaced = Substitute(a[i], map);
                        if (!ReferenceEquals(replaced, a[i])) a[i] = replaced;
                    }
                    return a;
                case JTokenType.String:
                    var s = (string)node;
                    var whole = WholeToken.Match(s);
                    if (whole.Success && map.TryGetValue(whole.Groups[1].Value, out var typed))
                        return typed.DeepClone(); // preserve the param's JSON type
                    return new JValue(InlineToken.Replace(s, m =>
                        map.TryGetValue(m.Groups[1].Value, out var v)
                            ? (v.Type == JTokenType.Null ? "" : v.ToString())
                            : m.Value));
                default:
                    return node;
            }
        }
    }
}
