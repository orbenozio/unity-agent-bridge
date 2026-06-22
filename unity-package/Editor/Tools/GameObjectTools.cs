// GameObjectTools.cs — create_gameobject, add_component (SPEC §8, MILESTONES M3).
//
// Runs on the main thread (invoked from McpBridge.Pump). Uses Undo so the user can
// Ctrl+Z anything Claude creates — be a good Editor citizen.

using System;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor;

namespace UnityMcpBridge.Editor.Tools
{
    public static class GameObjectTools
    {
        [McpTool("create_gameobject", "Create a GameObject (optionally a primitive) in the active scene.")]
        public static object CreateGameObject(
            [Param("Name for the new object.")] string name,
            [Param("Primitive: Cube|Sphere|Capsule|Cylinder|Plane|Quad|None.")] string primitive = "None")
        {
            // TODO(M3): implement. Reference shape:
            //   var go = primitive == "None"
            //       ? new GameObject(name)
            //       : GameObject.CreatePrimitive(Enum.Parse<PrimitiveType>(primitive));
            //   go.name = name;
            //   Undo.RegisterCreatedObjectUndo(go, "MCP Create");
            //   return new { instanceId = go.GetInstanceID(), path = HierarchyPath(go) };
            throw new NotImplementedException("GameObjectTools.CreateGameObject — implement in M3.");
        }

        [McpTool("add_component", "Add a component (e.g. Rigidbody) to a GameObject by name or instanceId.")]
        public static object AddComponent(
            [Param("Target GameObject: name or instanceId.")] string target,
            [Param("Component type, e.g. Rigidbody, BoxCollider.")] string componentType)
        {
            // TODO(M3): resolve target -> resolve Type across assemblies -> Undo.AddComponent.
            //   return new { instanceId, component = componentType, added = true };
            throw new NotImplementedException("GameObjectTools.AddComponent — implement in M3.");
        }
    }
}
