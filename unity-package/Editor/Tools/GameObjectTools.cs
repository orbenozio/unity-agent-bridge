// GameObjectTools.cs - create_gameobject, add_component (SPEC §8, MILESTONES M3).
//
// Runs on the main thread (invoked from McpBridge.Pump). Uses Undo so the user can
// Ctrl+Z anything Claude creates - be a good Editor citizen.

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class GameObjectTools
    {
        [McpTool("create_gameobject", "Create a GameObject (optionally a primitive) in the active scene.")]
        public static object CreateGameObject(
            [Param("Name for the new object.")] string name,
            [Param("Primitive: Cube|Sphere|Capsule|Cylinder|Plane|Quad|None.")] string primitive = "None")
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required");

            GameObject go;
            if (string.IsNullOrEmpty(primitive) ||
                string.Equals(primitive, "None", StringComparison.OrdinalIgnoreCase))
            {
                go = new GameObject(name);
            }
            else
            {
                if (!Enum.TryParse<PrimitiveType>(primitive, ignoreCase: true, out var prim))
                    throw new ArgumentException(
                        $"unknown primitive: {primitive} (use Cube|Sphere|Capsule|Cylinder|Plane|Quad|None)");
                go = GameObject.CreatePrimitive(prim);
                go.name = name;
            }

            Undo.RegisterCreatedObjectUndo(go, "MCP Create " + name);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            return new
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = HierarchyPath(go),
                primitive = string.IsNullOrEmpty(primitive) ? "None" : primitive,
            };
        }

        [McpTool("add_component", "Add a component (e.g. Rigidbody) to a GameObject by name or instanceId.")]
        public static object AddComponent(
            [Param("Target GameObject: name or instanceId.")] string target,
            [Param("Component type, e.g. Rigidbody, BoxCollider.")] string componentType)
        {
            var go = ResolveTarget(target);
            if (go == null)
                throw new ArgumentException($"GameObject not found: {target}");

            var type = ResolveComponentType(componentType);
            if (type == null)
                throw new ArgumentException($"component type not found: {componentType}");

            var component = Undo.AddComponent(go, type);
            if (component == null)
                throw new InvalidOperationException(
                    $"could not add {type.Name} to {go.name} (invalid/duplicate component?)");

            EditorSceneManager.MarkSceneDirty(go.scene);

            return new
            {
                instanceId = go.GetInstanceID(),
                gameObject = go.name,
                component = type.Name,
                added = true,
            };
        }

        // --- helpers -----------------------------------------------------------

        internal static GameObject ResolveTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;

            // instanceId path
            if (int.TryParse(target, out var id))
            {
                var obj = EditorUtility.EntityIdToObject(id);
                if (obj is GameObject g) return g;
                if (obj is Component c) return c.gameObject;
                return null;
            }

            // by name (active scene)
            return GameObject.Find(target);
        }

        internal static Type ResolveComponentType(string name)
        {
            // 1) common case: a UnityEngine component referenced by short name.
            var t = Type.GetType($"UnityEngine.{name}, UnityEngine") ??
                    Type.GetType($"UnityEngine.{name}, UnityEngine.CoreModule") ??
                    Type.GetType($"UnityEngine.{name}, UnityEngine.PhysicsModule");
            if (IsComponent(t)) return t;

            // 2) fully-qualified or assembly-qualified name.
            t = Type.GetType(name);
            if (IsComponent(t)) return t;

            // 3) scan everything; prefer an exact full-name match, else a short-name match.
            Type shortMatch = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var candidate in types)
                {
                    if (!IsComponent(candidate)) continue;
                    if (candidate.FullName == name) return candidate;
                    if (shortMatch == null && candidate.Name == name) shortMatch = candidate;
                }
            }
            return shortMatch;
        }

        private static bool IsComponent(Type t)
            => t != null && typeof(Component).IsAssignableFrom(t) && !t.IsAbstract;

        private static string HierarchyPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return "/" + path;
        }
    }
}
