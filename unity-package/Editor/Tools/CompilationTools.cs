// CompilationTools.cs - the autonomous fix-loop tools: refresh_assets + compile_errors.
//
// The loop they enable: the agent edits a script on disk, calls refresh_assets to
// import + recompile, then compile_errors to see what (if anything) broke, fixes it,
// and repeats until clean. compile_errors reads from a buffer fed by the compilation
// pipeline (installed from McpBridge's static ctor, like ConsoleTools).
//
// Why a buffer and not the Console: a clean compile triggers a domain reload that
// wipes static state - but there are no errors to report then. A FAILED compile does
// NOT reload, so the buffer captured during assemblyCompilationFinished is still here
// when the agent asks. That is exactly the case we care about.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class CompilationTools
    {
        private struct Message
        {
            public string message;
            public string file;
            public int line;
            public int column;
        }

        private static readonly object _lock = new object();
        private static readonly List<Message> _errors = new List<Message>();
        private static readonly List<Message> _warnings = new List<Message>();
        private static bool _installed;

        /// <summary>Hook the compilation pipeline. Idempotent; re-run safely after reload.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        private static void OnCompilationStarted(object _)
        {
            lock (_lock) { _errors.Clear(); _warnings.Clear(); }
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            lock (_lock)
            {
                foreach (var m in messages)
                {
                    var entry = new Message { message = m.message, file = m.file, line = m.line, column = m.column };
                    if (m.type == CompilerMessageType.Error) _errors.Add(entry);
                    else if (m.type == CompilerMessageType.Warning) _warnings.Add(entry);
                }
            }
        }

        [McpTool("compile_errors", "C# compile errors from the last compilation (empty = clean). Pair with refresh_assets for an edit-compile-fix loop.")]
        public static object CompileErrors(
            [Param("Include warnings as well as errors.")] bool includeWarnings = false)
        {
            lock (_lock)
            {
                var errors = _errors
                    .Select(e => new { e.message, e.file, e.line, e.column })
                    .ToList();

                if (!includeWarnings)
                    return new { compiling = EditorApplication.isCompiling, errorCount = errors.Count, errors };

                var warnings = _warnings
                    .Select(e => new { e.message, e.file, e.line, e.column })
                    .ToList();
                return new
                {
                    compiling = EditorApplication.isCompiling,
                    errorCount = errors.Count,
                    warningCount = warnings.Count,
                    errors,
                    warnings,
                };
            }
        }

        [McpTool("refresh_assets", "Import changed assets and recompile scripts (AssetDatabase.Refresh). May trigger a domain reload; follow with compile_errors.")]
        public static object RefreshAssets()
        {
            AssetDatabase.Refresh();
            return new { refreshed = true, compiling = EditorApplication.isCompiling };
        }
    }
}
