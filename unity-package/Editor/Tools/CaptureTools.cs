// CaptureTools.cs - capture_screenshot. Renders a camera off-screen to a PNG so the
// agent gets VISUAL feedback (build -> see -> fix) without entering Play Mode.
//
// Renders via Camera.Render() into a RenderTexture (works in Edit Mode on the built-in
// render pipeline). Under URP/HDRP a manual Camera.Render() may produce an empty frame;
// that's a known SRP limitation, noted for v1.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgentBridge.Editor;

namespace UnityAgentBridge.Editor.Tools
{
    public static class CaptureTools
    {
        [McpTool("capture_screenshot", "Render a camera to a PNG and return its path so you can view what was built. camera empty = main camera; 'scene' = the Scene view camera.")]
        public static object CaptureScreenshot(
            [Param("Output .png path. Default: <project>/UnityAgentBridge/Screenshots/shot_<time>.png")] string path = null,
            [Param("Image width.")] int width = 1280,
            [Param("Image height.")] int height = 720,
            [Param("Camera to render: a GameObject name, 'scene' for the Scene view, or empty for the main camera.")] string camera = null)
        {
            if (width < 1 || height < 1) throw new ArgumentException("width and height must be positive");

            var cam = ResolveCamera(camera);
            if (cam == null) throw new InvalidOperationException("no camera found to capture (open a Scene view or add a Camera).");

            var rt = new RenderTexture(width, height, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            var swapped = new List<CanvasState>();
            Texture2D tex = null;
            try
            {
                // Camera.Render() to a RenderTexture does NOT composite Screen Space -
                // Overlay canvases, so the UI would be missing. Temporarily route each
                // root overlay canvas through this camera (Screen Space - Camera) so the
                // UI is captured, then restore it in the finally block.
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    swapped.Add(new CanvasState { canvas = canvas, mode = canvas.renderMode, cam = canvas.worldCamera, dist = canvas.planeDistance });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.01f, 1f);
                }
                if (swapped.Count > 0) Canvas.ForceUpdateCanvases();

                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
            }
            finally
            {
                foreach (var s in swapped)
                {
                    s.canvas.renderMode = s.mode;
                    s.canvas.worldCamera = s.cam;
                    s.canvas.planeDistance = s.dist;
                }
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            var outPath = string.IsNullOrEmpty(path)
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                    "UnityAgentBridge", "Screenshots", $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png")
                : path;
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, png);
            if (outPath.Replace('\\', '/').Contains("/Assets/")) AssetDatabase.Refresh();

            return new { path = outPath, width, height, camera = cam.name };
        }

        private struct CanvasState
        {
            public Canvas canvas;
            public RenderMode mode;
            public Camera cam;
            public float dist;
        }

        private static Camera ResolveCamera(string camera)
        {
            if (string.Equals(camera, "scene", StringComparison.OrdinalIgnoreCase))
                return SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;

            if (!string.IsNullOrEmpty(camera))
            {
                var go = GameObject.Find(camera);
                return go != null ? go.GetComponent<Camera>() : null;
            }

            return Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
        }
    }
}
