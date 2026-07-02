// BridgeDiscovery.cs - publish THIS project's active bridge port so a CLI/MCP client
// can find it by project NAME or PATH instead of hard-coding a port.
//
// Why: the bridge auto-picks a free port (multiple Editors coexist), so with several
// projects open in parallel each one lands on a different port - and a client pinned to
// a fixed port talks to the wrong Editor (or none). Each Editor writes its own file:
//
//     ~/.unity-agent-bridge/projects/<key>.json  ->  { project, name, port, updated }
//
// One file PER PROJECT means no write contention when many Editors run at once. The
// server reads this directory and resolves --project <name|path> to the right port.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAgentBridge.Editor
{
    public static class BridgeDiscovery
    {
        /// <summary>Directory of per-project discovery files (identical path on both sides).</summary>
        public static string Dir
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, ".unity-agent-bridge", "projects");
            }
        }

        // The project root is the parent of Assets/. Its leaf is the friendly name the
        // client passes as --project (e.g. "NeonRunner").
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string ProjectName => new DirectoryInfo(ProjectRoot).Name;

        private static string FileFor(string root) => Path.Combine(Dir, Key(root) + ".json");

        // Stable per-project key: first 8 bytes of SHA-256 over the normalized full path,
        // computed the SAME way on the server so both name the file identically.
        private static string Key(string root)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(root)));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        /// <summary>Write (or overwrite) this project's discovery file with the live port.</summary>
        public static void Publish(int port)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var obj = new JObject
                {
                    ["project"] = ProjectRoot.Replace('\\', '/'),
                    ["name"] = ProjectName,
                    ["port"] = port,
                    ["updated"] = DateTime.UtcNow.ToString("o"),
                };
                File.WriteAllText(FileFor(ProjectRoot), obj.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[McpBridge] could not publish discovery info: " + e.Message);
            }
        }

        /// <summary>Remove this project's discovery file (called on Editor quit).</summary>
        public static void Unpublish()
        {
            try
            {
                var f = FileFor(ProjectRoot);
                if (File.Exists(f)) File.Delete(f);
            }
            catch { /* best-effort */ }
        }
    }
}
