// SafePath.cs - guards file-writing tools against path traversal.
//
// Every tool runs behind the handshake auth, so these are not remote holes, but a
// leaked token (or any local caller) must not be able to coerce a tool into writing
// outside the Unity project. Asset paths must stay under Assets/; free-form output
// paths (e.g. screenshots) must stay under the project root.
//
// Two layers of defense: (1) a textual prefix check after Path.GetFullPath normalizes
// '..'; (2) a reparse-point check that rejects any path crossing a symlink/junction,
// because GetFullPath does NOT resolve links and Unity's Mono (.NET Standard 2.1) lacks
// Directory.ResolveLinkTarget. The link check is best-effort: it disallows (rare)
// legitimate in-project links and still leaves a narrow TOCTOU window if a link is
// created after the check. See ThrowIfCrossesReparsePoint.

using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UnityAgentBridge.Editor.Tools
{
    internal static class SafePath
    {
        // Path comparison must match the host file system: case-insensitive on Windows and
        // macOS, case-sensitive on Linux. Using OrdinalIgnoreCase everywhere would wrongly
        // accept (or reject) paths on a case-sensitive Editor host.
        private static readonly StringComparison PathComparison =
            Application.platform == RuntimePlatform.LinuxEditor
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

        /// <summary><project>/ - the parent of the Assets folder.</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>
        /// Validate a free-form output path and return its normalized absolute form,
        /// guaranteed to sit inside the project root. Throws on absolute paths or '..'
        /// segments that escape the project.
        /// </summary>
        public static string ResolveInProject(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is empty");
            var root = Path.GetFullPath(ProjectRoot);
            var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, PathComparison))
                throw new ArgumentException($"path escapes the project folder: {path}");
            ThrowIfCrossesReparsePoint(full, root);
            return full;
        }

        /// <summary>
        /// Validate a project-relative asset path (must start with 'Assets/' and contain
        /// no '..' segments). Returns the forward-slash-normalized path for use with the
        /// AssetDatabase APIs, which expect that exact form.
        /// </summary>
        public static string RequireAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is empty");
            var norm = path.Replace('\\', '/');
            if (norm.Split('/').Any(seg => seg == ".." || seg == "."))
                throw new ArgumentException($"asset path must not contain relative segments: {path}");
            if (!norm.Equals("Assets", PathComparison) &&
                !norm.StartsWith("Assets/", PathComparison))
                throw new ArgumentException($"asset path must stay under the project's Assets/ folder: {path}");
            // Same symlink/junction defense as ResolveInProject, applied to the real
            // on-disk location this asset path maps to.
            var rootFull = Path.GetFullPath(ProjectRoot);
            ThrowIfCrossesReparsePoint(Path.GetFullPath(Path.Combine(rootFull, norm)), rootFull);
            return norm;
        }

        // Reject any path whose existing components (from the target up to, but not
        // including, the project root) are a reparse point (symlink/junction). This is
        // the link-aware half of the traversal guard - the textual checks above only see
        // the normalized string, not where a link actually points.
        private static void ThrowIfCrossesReparsePoint(string full, string root)
        {
            var rootTrimmed = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (var cur = full;
                 !string.IsNullOrEmpty(cur) && cur.Length > rootTrimmed.Length;
                 cur = Path.GetDirectoryName(cur))
            {
                if (IsReparsePoint(cur))
                    throw new ArgumentException($"path crosses a symlink/junction, which is not allowed: {cur}");
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return false;
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; } // unreadable -> let the normal write path surface it
        }
    }
}
