// PrefabTools.cs - the prefab workflow: instantiate a prefab into the scene, and save
// a scene GameObject back out as a prefab asset.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class PrefabTools
    {
        [McpTool("instantiate_prefab", "Instantiate a prefab asset into the active scene (keeps the prefab connection).")]
        public static object InstantiatePrefab(
            [Param("Prefab asset path, e.g. Assets/Prefabs/Enemy.prefab.")] string prefabPath,
            [Param("Optional name for the new instance.")] string name = null,
            [Param("Optional parent: name or instanceId.")] string parent = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new ArgumentException($"prefab not found at: {prefabPath}");

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "MCP InstantiatePrefab");
            if (!string.IsNullOrEmpty(name)) go.name = name;
            if (!string.IsNullOrEmpty(parent))
            {
                var p = GameObjectTools.ResolveTarget(parent)
                        ?? throw new ArgumentException($"parent not found: {parent}");
                go.transform.SetParent(p.transform, false);
            }

            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            return new { instanceId = go.GetInstanceID(), name = go.name, prefab = prefabPath };
        }

        [McpTool("create_prefab", "Save a scene GameObject as a prefab asset (and connect the scene object to it).")]
        public static object CreatePrefab(
            [Param("Target GameObject: name or instanceId.")] string target,
            [Param("Asset path to write, e.g. Assets/Prefabs/Enemy.prefab.")] string path)
        {
            var go = GameObjectTools.ResolveTarget(target)
                     ?? throw new ArgumentException($"GameObject not found: {target}");
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                throw new ArgumentException("path must be an Assets/... path ending in .prefab");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
            AssetDatabase.Refresh();
            return new { saved = prefab != null, path, name = go.name };
        }
    }
}
