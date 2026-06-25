// create_button.cs - an EXAMPLE custom tool (lives in the project, not the package).
//
// This is the kind of thing that should be a TOOL, not a command: it has real logic
// the flat command format can't express - it ensures an EventSystem and a Canvas
// exist, builds the Button + its Text child, and wires the RectTransform correctly.
// A command then composes this (see the main_menu command) into a shareable recipe.
//
// Auto-discovered on compile; call from Claude via call_tool("create_button", {...}).

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.CustomTools
{
    public static class create_button
    {
        [McpTool("create_button", "Create a uGUI Button (with a Text label) under a target, creating a Canvas/EventSystem if needed.")]
        public static object Invoke(
            [Param("Button label and GameObject name.")] string label = "Button",
            [Param("Parent: name of an existing object. Empty = the scene's Canvas.")] string parent = null,
            [Param("Width.")] float width = 160f,
            [Param("Height.")] float height = 40f,
            [Param("Anchored X.")] float x = 0f,
            [Param("Anchored Y.")] float y = 0f)
        {
            // A clickable UI needs an EventSystem in the scene.
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "MCP EventSystem");
            }

            // Resolve a parent, and make sure there is a Canvas to live under.
            var parentGo = string.IsNullOrEmpty(parent) ? null : GameObject.Find(parent);
            var canvas = parentGo != null ? parentGo.GetComponentInParent<Canvas>() : Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(cgo, "MCP Canvas");
                canvas = cgo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            var host = parentGo != null ? parentGo : canvas.gameObject;

            var buttonGo = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonGo, "MCP CreateButton");
            buttonGo.transform.SetParent(host.transform, false);
            var rt = buttonGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, y);
            buttonGo.GetComponent<Image>().color = new Color(0.9f, 0.9f, 0.9f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textGo, "MCP CreateButtonText");
            textGo.transform.SetParent(buttonGo.transform, false);
            var txt = textGo.GetComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.black;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            EditorSceneManager.MarkSceneDirty(buttonGo.scene);
            return new { instanceId = buttonGo.GetInstanceID(), name = buttonGo.name, parent = host.name };
        }
    }
}
