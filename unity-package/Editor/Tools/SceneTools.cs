// SceneTools.cs - scene/hierarchy editing tools (extension beyond v1's four).
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
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
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

        [McpTool("set_transform", "Set a GameObject's LOCAL position / rotation (euler) / scale. Each is an optional {x,y,z}; omitted axes keep their current value.")]
        public static object SetTransform(
            [Param("Target: name or instanceId.")] string target,
            [Param("Local position {x,y,z}.")] JToken position = null,
            [Param("Local euler rotation {x,y,z}.")] JToken rotation = null,
            [Param("Local scale {x,y,z}.")] JToken scale = null)
        {
            var go = Resolve(target);
            var t = go.transform;
            Undo.RecordObject(t, "MCP SetTransform");
            if (position is JObject) t.localPosition = ReadVec3(position, t.localPosition);
            if (rotation is JObject) t.localEulerAngles = ReadVec3(rotation, t.localEulerAngles);
            if (scale is JObject) t.localScale = ReadVec3(scale, t.localScale);
            MarkDirty(go);

            return new
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                position = V3(t.localPosition),
                rotation = V3(t.localEulerAngles),
                scale = V3(t.localScale),
            };
        }

        [McpTool("set_property", "Set a serialized property on a component (generic). property accepts the public name (color, renderMode), the serialized name (m_Color), or an alias; the tool maps the public name to Unity's m_ convention. E.g. component=Image property=color value={r,g,b,a}.")]
        public static object SetProperty(
            [Param("Target: name or instanceId.")] string target,
            [Param("Component type, e.g. Image, Text, Button, Camera.")] string componentType,
            [Param("Property: public name (color, renderMode), serialized name (m_Color), or alias (fontSize). Public names auto-map to m_Name.")] string property,
            [Param("Value: number, bool, string, or {r,g,b,a}/{x,y,z}.")] JToken value)
        {
            var go = Resolve(target);
            var type = GameObjectTools.ResolveComponentType(componentType)
                       ?? throw new ArgumentException($"component type not found: {componentType}");
            var comp = go.GetComponent(type) ?? throw new ArgumentException($"{go.name} has no {type.Name}");

            var so = new SerializedObject(comp);
            var sp = ResolveProperty(so, property, out var propName)
                     ?? throw new ArgumentException(
                         $"serialized property not found: {property} (tried {string.Join(", ", PropertyCandidates(property))})");

            ApplyJson(sp, value);
            so.ApplyModifiedProperties();
            MarkDirty(go);

            return new { target = go.name, component = type.Name, property = propName, propertyType = sp.propertyType.ToString() };
        }

        [McpTool("set_color", "Set a color from a hex string (#RGB, #RRGGBB, or #RRGGBBAA; leading # optional; named colors like 'red' also work). Auto-targets a UI Graphic (Image/Text/RawImage) or a SpriteRenderer; pass componentType to set m_Color on a specific component.")]
        public static object SetColor(
            [Param("Target: name or instanceId.")] string target,
            [Param("Hex color: #RGB, #RRGGBB, or #RRGGBBAA (leading # optional). Named colors (red, white) also work.")] string hex,
            [Param("Optional component type, e.g. Image, Text, SpriteRenderer. Default: auto-detect a Graphic or SpriteRenderer.")] string componentType = null)
        {
            var go = Resolve(target);
            var color = ParseHexColor(hex);

            string applied;
            if (!string.IsNullOrEmpty(componentType))
            {
                var type = GameObjectTools.ResolveComponentType(componentType)
                           ?? throw new ArgumentException($"component type not found: {componentType}");
                var comp = go.GetComponent(type) ?? throw new ArgumentException($"{go.name} has no {type.Name}");
                Undo.RecordObject(comp, "MCP SetColor");
                if (!ApplyColor(comp, color))
                    throw new ArgumentException($"{type.Name} has no color (m_Color) to set");
                applied = type.Name;
            }
            else
            {
                var comp = (Component)go.GetComponent<Graphic>() ?? go.GetComponent<SpriteRenderer>();
                if (comp == null)
                    throw new ArgumentException($"{go.name} has no Graphic or SpriteRenderer; pass componentType to target a specific component.");
                Undo.RecordObject(comp, "MCP SetColor");
                ApplyColor(comp, color);
                applied = comp.GetType().Name;
            }

            MarkDirty(go);
            return new
            {
                target = go.name,
                component = applied,
                color = $"({color.r:0.##},{color.g:0.##},{color.b:0.##},{color.a:0.##})",
                hex = "#" + ColorUtility.ToHtmlStringRGBA(color),
            };
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

        [McpTool("save_scene", "Save the active scene to disk (gives an unsaved scene a path so it survives Play Mode).")]
        public static object SaveScene(
            [Param("Optional asset path, e.g. Assets/Scenes/Main.unity.")] string path = null)
        {
            var scene = SceneManager.GetActiveScene();
            var target = !string.IsNullOrEmpty(path)
                ? SafePath.RequireAssetPath(path) // no traversal outside Assets/
                : (string.IsNullOrEmpty(scene.path) ? "Assets/Scenes/Main.unity" : scene.path);

            var dir = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var ok = EditorSceneManager.SaveScene(scene, target);
            AssetDatabase.Refresh();
            return new { saved = ok, path = scene.path, name = scene.name };
        }

        [McpTool("new_scene", "Create a new scene in the Editor. setup='default' (Main Camera + Directional Light) or 'empty'. mode='single' (replace open scenes) or 'additive'. Optional path saves it to disk. Single mode discards unsaved changes in the current scene.")]
        public static object NewScene(
            [Param("Setup: 'default' (Main Camera + Directional Light) or 'empty'.")] string setup = "default",
            [Param("Mode: 'single' (replace open scenes) or 'additive' (open alongside).")] string mode = "single",
            [Param("Optional asset path to save the new scene, e.g. Assets/Scenes/Level2.unity.")] string path = null)
        {
            var sceneSetup = string.Equals(setup, "empty", StringComparison.OrdinalIgnoreCase)
                ? NewSceneSetup.EmptyScene
                : NewSceneSetup.DefaultGameObjects;
            var sceneMode = string.Equals(mode, "additive", StringComparison.OrdinalIgnoreCase)
                ? NewSceneMode.Additive
                : NewSceneMode.Single;

            var scene = EditorSceneManager.NewScene(sceneSetup, sceneMode);

            string savedPath = null;
            if (!string.IsNullOrEmpty(path))
            {
                var target = SafePath.RequireAssetPath(path); // no traversal outside Assets/
                var dir = System.IO.Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                EditorSceneManager.SaveScene(scene, target);
                AssetDatabase.Refresh();
                savedPath = scene.path;
            }

            return new
            {
                created = true,
                name = scene.name,
                path = savedPath,
                setup = sceneSetup.ToString(),
                mode = sceneMode.ToString(),
                rootCount = scene.rootCount,
            };
        }

        [McpTool("get_object", "Inspect a GameObject: transform + each component's serialized properties (capped).")]
        public static object GetObject(
            [Param("Target: name or instanceId.")] string target)
        {
            var go = Resolve(target);
            var comps = new List<object>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                comps.Add(new { type = c.GetType().Name, properties = DumpProps(c) });
            }

            object xform;
            var t = go.transform;
            if (t is RectTransform rt)
                xform = new { kind = "RectTransform", anchoredPosition = V2(rt.anchoredPosition), sizeDelta = V2(rt.sizeDelta), anchorMin = V2(rt.anchorMin), anchorMax = V2(rt.anchorMax), pivot = V2(rt.pivot) };
            else
                xform = new { kind = "Transform", position = V3(t.position), localPosition = V3(t.localPosition), eulerAngles = V3(t.eulerAngles), localScale = V3(t.localScale) };

            return new
            {
                name = go.name,
                active = go.activeSelf,
                layer = LayerMask.LayerToName(go.layer),
                tag = go.tag,
                transform = xform,
                components = comps,
            };
        }

        // --- helpers -----------------------------------------------------------

        private static Dictionary<string, object> DumpProps(Component c)
        {
            var d = new Dictionary<string, object>();
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            int count = 0;
            if (it.NextVisible(true))
            {
                do
                {
                    if (it.name == "m_Script") continue;
                    var val = PropVal(it);
                    if (val != null) { d[it.name] = val; count++; }
                } while (count < 30 && it.NextVisible(false));
            }
            return d;
        }

        private static object PropVal(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue;
                case SerializedPropertyType.Boolean: return p.boolValue;
                case SerializedPropertyType.Float: return p.floatValue;
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                        ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex;
                case SerializedPropertyType.Color:
                    var c = p.colorValue;
                    return $"({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue != null ? p.objectReferenceValue.name : null;
                case SerializedPropertyType.Vector2: return V2(p.vector2Value);
                case SerializedPropertyType.Vector3: return V3(p.vector3Value);
                case SerializedPropertyType.LayerMask: return p.intValue;
                default: return null; // skip complex/nested
            }
        }

        private static object V2(Vector2 v) => new { x = v.x, y = v.y };
        private static object V3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

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

        // Irregular/nested public->serialized names the simple "m_ + Capitalize" rule can't derive.
        // (color/text/enabled etc. need no entry - they fall out of the heuristic below.)
        private static readonly Dictionary<string, string> PropertyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "fontSize", "m_FontData.m_FontSize" },
            { "fontStyle", "m_FontData.m_FontStyle" },
            { "alignment", "m_FontData.m_Alignment" },
            { "lineSpacing", "m_FontData.m_LineSpacing" },
        };

        // Candidate serialized names for a property, in priority order:
        // 1) a known alias (nested/irregular), 2) the name as given (already m_X or directly findable),
        // 3) the m_ + Capitalized public-name convention (renderMode -> m_RenderMode, color -> m_Color).
        private static IEnumerable<string> PropertyCandidates(string property)
        {
            if (PropertyAliases.TryGetValue(property, out var aliased)) yield return aliased;
            yield return property;
            if (property.Length > 0 && !property.StartsWith("m_"))
                yield return "m_" + char.ToUpperInvariant(property[0]) + property.Substring(1);
        }

        // First candidate that resolves wins; resolvedName is the serialized path that matched.
        private static SerializedProperty ResolveProperty(SerializedObject so, string property, out string resolvedName)
        {
            foreach (var name in PropertyCandidates(property))
            {
                var sp = so.FindProperty(name);
                if (sp != null) { resolvedName = name; return sp; }
            }
            resolvedName = null;
            return null;
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

        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) throw new ArgumentException("hex color is empty");
            var s = hex.Trim();
            if (!s.StartsWith("#") && System.Text.RegularExpressions.Regex.IsMatch(s, "^[0-9a-fA-F]{3,8}$"))
                s = "#" + s; // bare hex digits get the leading # (named colors pass through as-is)
            if (ColorUtility.TryParseHtmlString(s, out var c)) return c;
            throw new ArgumentException($"invalid color: {hex} (expected #RGB, #RRGGBB, #RRGGBBAA, or a named color)");
        }

        // Set a color on a component: Graphic/SpriteRenderer directly, else m_Color via SerializedObject. False = no color to set.
        private static bool ApplyColor(Component comp, Color color)
        {
            switch (comp)
            {
                case Graphic g: g.color = color; return true;
                case SpriteRenderer sr: sr.color = color; return true;
                default:
                    var so = new SerializedObject(comp);
                    var sp = so.FindProperty("m_Color");
                    if (sp == null || sp.propertyType != SerializedPropertyType.Color) return false;
                    sp.colorValue = color;
                    so.ApplyModifiedProperties();
                    return true;
            }
        }

        // Read {x,y,z} from JSON, keeping the current value for any axis that is omitted.
        private static Vector3 ReadVec3(JToken v, Vector3 cur) => new Vector3(
            v["x"] != null ? v["x"].ToObject<float>() : cur.x,
            v["y"] != null ? v["y"].ToObject<float>() : cur.y,
            v["z"] != null ? v["z"].ToObject<float>() : cur.z);

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
