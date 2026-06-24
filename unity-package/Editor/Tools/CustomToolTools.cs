// CustomToolTools.cs - manage project-defined CUSTOM C# TOOLS, with the same
// create / list / delete / export / import lifecycle as custom commands.
//
// Difference from a command: a command is a no-code macro of EXISTING tools; a
// custom tool is a real C# [McpTool] method, so it can do anything (loops, Unity
// APIs, event wiring) at the cost of a recompile. These tools live as .cs files at:
//
//     <project>/Assets/UnityAgentBridge/CustomTools/Editor/<name>.cs
//
// The "Editor" folder makes Unity compile them into the predefined
// Assembly-CSharp-Editor, which auto-references this package (so [McpTool] is
// visible) and UnityEngine.UI - no asmdef needed. ToolRegistry scans all assemblies,
// so a custom tool auto-registers on compile and is callable via call_tool(name,args).
//
// A shareable "tool pack" bundles the .cs SOURCE into one .json file (export_tools);
// import_tools writes each source back out. Because these change code, the mutating
// tools do NOT compile for you - they return a hint to call refresh_assets next.

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
    public static class CustomToolTools
    {
        public static string CustomToolsDir =>
            Path.Combine(Application.dataPath, "UnityAgentBridge", "CustomTools", "Editor");

        public static string DefaultPackPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UnityAgentBridge", "tools-pack.json");

        // Must be a valid C# identifier (used as the [McpTool] name, class and file name).
        private static readonly Regex NameRx = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        private const string RefreshHint = "this changed C# code - call refresh_assets, then compile_errors.";

        private static string PathFor(string name) => Path.Combine(CustomToolsDir, name + ".cs");

        private static string[] Files() =>
            Directory.Exists(CustomToolsDir) ? Directory.GetFiles(CustomToolsDir, "*.cs") : Array.Empty<string>();

        [McpTool("list_custom_tools", "List project-defined CUSTOM C# tools (the .cs files in the CustomTools folder).")]
        public static object ListCustomTools()
        {
            var tools = Files()
                .Select(f => new { name = Path.GetFileNameWithoutExtension(f), file = Path.GetFileName(f) })
                .OrderBy(t => t.name)
                .ToList();
            return new { count = tools.Count, dir = CustomToolsDir, tools };
        }

        [McpTool("new_custom_tool", "Scaffold a new custom C# [McpTool] from a template. Writes <name>.cs (does NOT compile - edit it, then call refresh_assets).")]
        public static object NewCustomTool(
            [Param("Tool name; must be a valid C# identifier (letters/digits/underscore, not starting with a digit).")] string name,
            [Param("One-line description shown to Claude.")] string description = null,
            [Param("Overwrite if the file already exists.")] bool overwrite = false)
        {
            if (string.IsNullOrEmpty(name) || !NameRx.IsMatch(name))
                throw new Exception("invalid name; use a valid C# identifier (letters, digits, '_', not starting with a digit).");

            Directory.CreateDirectory(CustomToolsDir);
            var path = PathFor(name);
            if (File.Exists(path) && !overwrite)
                throw new Exception($"{name}.cs already exists (pass overwrite=true to replace it).");

            File.WriteAllText(path, Template(name, description ?? $"Custom tool {name}"));
            return new { created = true, name, path, next = RefreshHint };
        }

        [McpTool("delete_custom_tool", "Delete a custom tool .cs file by name.")]
        public static object DeleteCustomTool([Param("Tool name.")] string name)
        {
            var path = PathFor(name ?? "");
            if (!File.Exists(path)) return new { deleted = false, name };
            File.Delete(path);
            var meta = path + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            return new { deleted = true, name, next = RefreshHint };
        }

        [McpTool("export_tools", "Bundle custom tool .cs files into a shareable .json pack (source embedded). names empty = all.")]
        public static object ExportTools(
            [Param("Output .json path. Default: <project>/UnityAgentBridge/tools-pack.json")] string path = null,
            [Param("Tool names to include. Empty/null = all.")] string[] names = null)
        {
            IEnumerable<string> sel = Files();
            if (names != null && names.Length > 0)
            {
                var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                sel = sel.Where(f => set.Contains(Path.GetFileNameWithoutExtension(f)));
            }

            var arr = new JArray();
            foreach (var f in sel)
                arr.Add(new JObject { ["name"] = Path.GetFileNameWithoutExtension(f), ["source"] = File.ReadAllText(f) });
            var pack = new JObject { ["version"] = 1, ["tools"] = arr };

            var outPath = string.IsNullOrEmpty(path) ? DefaultPackPath : path;
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, pack.ToString(Formatting.Indented));
            return new { exported = arr.Count, path = outPath };
        }

        [McpTool("import_tools", "Import custom tools from a .json pack (a file path OR inline pack JSON). overwrite replaces existing names.")]
        public static object ImportTools(
            [Param("A file path to a pack, or inline pack JSON.")] string pack,
            [Param("Overwrite tools whose name already exists.")] bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(pack)) throw new Exception("pack is empty.");

            var content = pack;
            var trimmed = pack.TrimStart();
            var looksInline = trimmed.StartsWith("{") || trimmed.StartsWith("[");
            if (!looksInline && File.Exists(pack)) content = File.ReadAllText(pack);

            var root = JToken.Parse(content);
            JArray tools =
                root is JObject o && o["tools"] is JArray ta ? ta :
                root is JArray ra ? ra :
                throw new Exception("pack must be { version, tools:[...] } or an array of { name, source }.");

            Directory.CreateDirectory(CustomToolsDir);
            int imported = 0, skipped = 0;
            foreach (var t in tools.OfType<JObject>())
            {
                var name = (string)t["name"];
                var source = (string)t["source"];
                if (string.IsNullOrEmpty(name) || !NameRx.IsMatch(name) || source == null) { skipped++; continue; }
                if (!overwrite && File.Exists(PathFor(name))) { skipped++; continue; }
                File.WriteAllText(PathFor(name), source);
                imported++;
            }
            return new { imported, skipped, next = RefreshHint };
        }

        // --- the scaffold template ---------------------------------------------

        private static string Template(string name, string description)
        {
            var desc = description.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return Stub
                .Replace("__NAME__", name)
                .Replace("__DESC__", desc);
        }

        // Verbatim so we don't fight brace escaping; tokens are substituted above.
        // Doubled quotes ("") are literal quotes in the generated file.
        private const string Stub = @"using UnityEditor;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.CustomTools
{
    // Custom bridge tool. Auto-discovered on compile (ToolRegistry scans every
    // assembly). Claude calls it via call_tool(""__NAME__"", { ... }), and it shows up
    // in list_tools. This runs on Unity's MAIN THREAD - any Unity API is safe here.
    // Return any object; it becomes the tool's JSON result. Throw to report an error.
    public static class __NAME__
    {
        [McpTool(""__NAME__"", ""__DESC__"")]
        public static object Invoke(/* e.g. */ string message = ""hello"", int count = 1)
        {
            return new { ok = true, message, count };
        }
    }
}
";
    }
}
