// SceneTools.cs — scene/hierarchy editing tools (extension beyond v1's four).
//
// set_parent, set_rect, set_text, set_property (generic), delete_gameobject, list_scene.
// All run on the main thread (via McpBridge.Pump) and are Undo-registered.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityMcpBridge.Editor;

namespace UnityMcpBridge.Editor.Tools
{
    public static class SceneTools
    {
        [McpTool("set_parent", "Reparent a GameObject under another (empty parent = scene root).")]
        public static object SetParent(
            [Param("Child: name or instanceId.")] string target,
            [Param("New parent: name or instanceId. Empty/null = move to scene root.")] string parent = null,
            [Param("Keep world position when reparenting (UI usually false).")] bool keepWorldPosition = false)
        {
            var go = Resolve(target);
            Transform parentT = null;
            if (!string.IsNullOrEmpty(parent))
                parentT = Resolve(parent).transform;

            Undo.SetTransformParent(go.transform, parentT, "MCP SetParent");
            go.transform.SetParent(parentT, keepWorldPosition);
            MarkDirty(go);

            return new
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = HierarchyPath(go),
                parent = parentT != null ? parentT.name : null,
            };
        }

        [McpTool("set_rect", "Set a UI RectTransform: anchored position, size, and optional anchor preset.")]
        public static object SetRect(
            [Param("Target: name or instanceId.")] string target,
            [Param("Anchored X.")] float x = 0f,
            [Param("Anchored Y.")] float y = 0f,
            [Param("Width (sizeDelta.x).")] float width = 160f,
            [Param("Height (sizeDelta.y).")] float height = 30f,
            [Param("Anchor preset: center|top|bottom|left|right|stretch|topleft|topright|bottomleft|bottomright.")] string anchor = null)
        {
            var go = Resolve(target);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) throw new ArgumentException($"{go.name} has no RectTransform (is it a UI object?)");

            Undo.RecordObject(rt, "MCP SetRect");
            if (!string.IsNullOrEmpty(anchor)) ApplyAnchor(rt, anchor);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, y);
            MarkDirty(go);

            return new { instanceId = go.GetInstanceID(), name = go.name, x, y, width, height, anchor = anchor ?? "(unchanged)" };
        }

        [McpTool("set_text", "Set the label of a UI Text on the target; creates a stretched child label (with a font) if none exists.")]
        public static object SetText(
            [Param("Target: name or instanceId.")] string target,
            [Param("The text to display.")] string text)
        {
            var go = Resolve(target);
            var label = go.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                var child = new GameObject("Label", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(child, "MCP CreateLabel");
                child.transform.SetParent(go.transform, false);
                label = Undo.AddComponent<Text>(child);
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.color = Color.black;
                label.alignment = TextAnchor.MiddleCenter;
                var rt = label.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            Undo.RecordObject(label, "MCP SetText");
            label.text = text;
            MarkDirty(go);

            return new { instanceId = go.GetInstanceID(), name = go.name, component = label.GetType().Name, text };
        }

        [McpTool("set_property", "Set a serialized property on a component (generic). E.g. component=Image property=color value={r,g,b,a}.")]
        public static object SetProperty(
            [Param("Target: name or instanceId.")] string target,
            [Param("Component type, e.g. Image, Text, Button, Camera.")] string componentType,
            [Param("Property: a serialized name (m_Color) or an alias (color, text, fontSize, enabled).")] string property,
            [Param("Value: number, bool, string, or {r,g,b,a}/{x,y,z}.")] JToken value)
        {
            var go = Resolve(target);
            var type = GameObjectTools.ResolveComponentType(componentType)
                       ?? throw new ArgumentException($"component type not found: {componentType}");
            var comp = go.GetComponent(type) ?? throw new ArgumentException($"{go.name} has no {type.Name}");

            var so = new SerializedObject(comp);
            var propName = MapAlias(property);
            var sp = so.FindProperty(propName)
                     ?? throw new ArgumentException($"serialized property not found: {property} (tried {propName})");

            ApplyJson(sp, value);
            so.ApplyModifiedProperties();
            MarkDirty(go);

            return new { target = go.name, component = type.Name, property = propName, propertyType = sp.propertyType.ToString() };
        }

        [McpTool("delete_gameobject", "Delete a GameObject (Undo-able).")]
        public static object DeleteGameObject(
            [Param("Target: name or instanceId.")] string target)
        {
            var go = Resolve(target);
            var name = go.name;
            var id = go.GetInstanceID();
            var scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            return new { deleted = true, instanceId = id, name };
        }

        [McpTool("list_scene", "List the active scene hierarchy (names + components), depth-limited and terse.")]
        public static object ListScene(
            [Param("Max depth to descend (default 4).")] int maxDepth = 4)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var tree = roots.Select(r => Node(r, 0, maxDepth)).ToList();
            return new { scene = scene.name, rootCount = roots.Length, hierarchy = tree };
        }

        // --- helpers -----------------------------------------------------------

        private static object Node(GameObject go, int depth, int maxDepth)
        {
            var comps = go.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name)
                .ToArray();

            List<object> children = null;
            if (depth < maxDepth && go.transform.childCount > 0)
            {
                children = new List<object>();
                foreach (Transform ch in go.transform)
                    children.Add(Node(ch.gameObject, depth + 1, maxDepth));
            }

            return new
            {
                name = go.name,
                instanceId = go.GetInstanceID(),
                active = go.activeSelf,
                components = comps,
                children,
            };
        }

        private static GameObject Resolve(string target)
            => GameObjectTools.ResolveTarget(target) ?? throw new ArgumentException($"GameObject not found: {target}");

        private static string MapAlias(string property)
        {
            switch (property)
            {
                case "color": return "m_Color";
                case "text": return "m_Text";
                case "fontSize": return "m_FontData.m_FontSize";
                case "enabled": return "m_Enabled";
                case "interactable": return "m_Interactable";
                default: return property;
            }
        }

        private static void ApplyJson(SerializedProperty sp, JToken value)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer: sp.intValue = value.ToObject<int>(); break;
                case SerializedPropertyType.Boolean: sp.boolValue = value.ToObject<bool>(); break;
                case SerializedPropertyType.Float: sp.floatValue = value.ToObject<float>(); break;
                case SerializedPropertyType.String: sp.stringValue = value.ToObject<string>(); break;
                case SerializedPropertyType.Enum: sp.enumValueIndex = value.ToObject<int>(); break;
                case SerializedPropertyType.Color: sp.colorValue = ToColor(value); break;
                case SerializedPropertyType.Vector2: sp.vector2Value = new Vector2(F(value, "x"), F(value, "y")); break;
                case SerializedPropertyType.Vector3: sp.vector3Value = new Vector3(F(value, "x"), F(value, "y"), F(value, "z")); break;
                default: throw new ArgumentException($"unsupported property type: {sp.propertyType}");
            }
        }

        private static Color ToColor(JToken v) => new Color(F(v, "r"), F(v, "g"), F(v, "b"), v["a"] != null ? F(v, "a") : 1f);
        private static float F(JToken v, string k) => v[k] != null ? v[k].ToObject<float>() : 0f;

        private static void ApplyAnchor(RectTransform rt, string anchor)
        {
            Vector2 min, max, pivot = new Vector2(0.5f, 0.5f);
            switch (anchor.ToLowerInvariant())
            {
                case "center": min = max = new Vector2(0.5f, 0.5f); break;
                case "top": min = max = new Vector2(0.5f, 1f); pivot = new Vector2(0.5f, 1f); break;
                case "bottom": min = max = new Vector2(0.5f, 0f); pivot = new Vector2(0.5f, 0f); break;
                case "left": min = max = new Vector2(0f, 0.5f); pivot = new Vector2(0f, 0.5f); break;
                case "right": min = max = new Vector2(1f, 0.5f); pivot = new Vector2(1f, 0.5f); break;
                case "topleft": min = max = new Vector2(0f, 1f); pivot = new Vector2(0f, 1f); break;
                case "topright": min = max = new Vector2(1f, 1f); pivot = new Vector2(1f, 1f); break;
                case "bottomleft": min = max = new Vector2(0f, 0f); pivot = new Vector2(0f, 0f); break;
                case "bottomright": min = max = new Vector2(1f, 0f); pivot = new Vector2(1f, 0f); break;
                case "stretch": min = Vector2.zero; max = Vector2.one; break;
                default: throw new ArgumentException($"unknown anchor: {anchor}");
            }
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
        }

        private static void MarkDirty(GameObject go)
        {
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static string HierarchyPath(GameObject go)
        {
            var path = go.name;
            var p = go.transform.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            return "/" + path;
        }
    }
}
