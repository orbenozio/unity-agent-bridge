// UiTools.cs - higher-level UI scaffolding that composes the primitive tools into one
// correct call. Building a Canvas by hand is a brittle sequence: create_gameobject +
// add_component Canvas leaves renderMode = World Space, then you still need CanvasScaler +
// GraphicRaycaster and a scene EventSystem for input. create_canvas does it all, right, once.
//
// Runs on the main thread (via McpBridge.Pump) and is Undo-registered.

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class UiTools
    {
        [McpTool("create_canvas", "Create a UI Canvas correctly in one call: a Screen Space - Overlay Canvas + CanvasScaler (Scale With Screen Size, 1920x1080) + GraphicRaycaster, plus an EventSystem (the new Input System's InputSystemUIInputModule when that package is installed, else StandaloneInputModule) if the scene has none. Replaces the create_gameobject + add_component sequence that defaults Canvas to World Space.")]
        public static object CreateCanvas(
            [Param("Name for the Canvas GameObject (default 'Canvas').")] string name = "Canvas")
        {
            if (string.IsNullOrEmpty(name)) name = "Canvas";

            // Start with a RectTransform so Canvas/Scaler/Raycaster attach cleanly.
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "MCP CreateCanvas");

            var canvas = Undo.AddComponent<Canvas>(go);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; // the trap: AddComponent leaves this at World Space

            var scaler = Undo.AddComponent<CanvasScaler>(go);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.AddComponent<GraphicRaycaster>(go);

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            var eventSystem = EnsureEventSystem();

            return new
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                renderMode = canvas.renderMode.ToString(),
                components = new[] { nameof(Canvas), nameof(CanvasScaler), nameof(GraphicRaycaster) },
                eventSystem,
            };
        }

        // One EventSystem per scene set: reuse an existing one, otherwise create it with the
        // right input module for whichever input backend the project has.
        private static object EnsureEventSystem()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null)
                return new { created = false, name = existing.name, module = ModuleName(existing.gameObject) };

            var esGo = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(esGo, "MCP CreateEventSystem");
            Undo.AddComponent<EventSystem>(esGo);

            // Prefer the new Input System module when the package is installed (resolved by
            // assembly scan so this file doesn't hard-reference Unity.InputSystem); else legacy.
            string module;
            var moduleType = GameObjectTools.ResolveComponentType("InputSystemUIInputModule");
            if (moduleType != null)
            {
                Undo.AddComponent(esGo, moduleType);
                module = moduleType.Name;
            }
            else
            {
                Undo.AddComponent<StandaloneInputModule>(esGo);
                module = nameof(StandaloneInputModule);
            }

            EditorSceneManager.MarkSceneDirty(esGo.scene);
            return new { created = true, name = esGo.name, module };
        }

        private static string ModuleName(GameObject es)
        {
            foreach (var c in es.GetComponents<BaseInputModule>())
                if (c != null) return c.GetType().Name;
            return null;
        }
    }
}
