// McpBridgeWindow.cs — the in-Editor control panel for the bridge.
//
// Window > Unity MCP Bridge. Live status; set the port; start/stop/restart; an
// allow-list of tools (checkbox = permitted, description on hover as a tooltip);
// a draggable splitter to trade space between the tool list and recent activity.

using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor
{
    public class McpBridgeWindow : EditorWindow
    {
        private const string ToolsHeightKey = "McpBridge.Window.ToolsHeight";

        private int _portField;
        private string _filter = "";
        private Vector2 _toolsScroll;
        private Vector2 _logScroll;
        private float _toolsHeight = 200f;
        private bool _dragging;
        private GUIStyle _disabledLabel;
        private Texture _infoIcon;

        [MenuItem("Window/Unity MCP Bridge")]
        public static void Open()
        {
            var w = GetWindow<McpBridgeWindow>("MCP Bridge");
            w.minSize = new Vector2(360, 460);
            w.Show();
        }

        private void OnEnable()
        {
            _portField = McpBridge.Port;
            _toolsHeight = EditorPrefs.GetFloat(ToolsHeightKey, 200f);
        }

        // Repaint a few times a second so status/log stay live.
        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            _disabledLabel ??= new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.55f, 0.55f, 0.55f) } };
            _infoIcon ??= EditorGUIUtility.IconContent("console.infoicon").image;

            DrawStatus();
            EditorGUILayout.Space(6);
            DrawPortAndControls();
            EditorGUILayout.Space(6);
            DrawCliHints();
            EditorGUILayout.Space(6);

            DrawToolsHeader();
            DrawToolsList();
            DrawSplitter();
            DrawActivity();
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            var listening = McpBridge.IsListening;

            var dotStyle = new GUIStyle(EditorStyles.boldLabel);
            dotStyle.normal.textColor = listening ? new Color(0.30f, 0.80f, 0.36f) : new Color(0.7f, 0.7f, 0.7f);
            EditorGUILayout.LabelField(listening ? "● Listening" : "○ Stopped", dotStyle);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("URL", $"ws://{McpBridge.Host}:{McpBridge.Port}");
                EditorGUILayout.TextField("Client", McpBridge.ClientConnected ? "connected" : "—");
            }
        }

        private void DrawPortAndControls()
        {
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _portField = EditorGUILayout.IntField("Port", _portField);
                if (GUILayout.Button("Apply & Restart", GUILayout.Width(130)))
                {
                    McpBridge.SetPort(_portField);
                    _portField = McpBridge.Port;
                    GUI.FocusControl(null);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (McpBridge.IsListening)
                {
                    if (GUILayout.Button("Stop")) McpBridge.Stop();
                }
                else
                {
                    if (GUILayout.Button("Start")) McpBridge.StartServer();
                }
                if (GUILayout.Button("Restart")) McpBridge.Restart();
            }
        }

        private void DrawCliHints()
        {
            EditorGUILayout.LabelField("CLI  (run unity-bridge.cmd from the repo root)", EditorStyles.boldLabel);
            var portArg = McpBridge.Port == McpBridge.DefaultPort ? "" : $"--port {McpBridge.Port} ";
            CopyableRow($"unity-bridge {portArg}ping");
            CopyableRow($"unity-bridge {portArg}list");
        }

        private void DrawToolsHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Tools — check = allowed (hover for details)", EditorStyles.boldLabel);
                if (GUILayout.Button("All on", EditorStyles.miniButtonLeft, GUILayout.Width(54)))
                    foreach (var t in ToolRegistry.GetToolInfos()) ToolGate.SetEnabled(t.name, true);
                if (GUILayout.Button("All off", EditorStyles.miniButtonRight, GUILayout.Width(54)))
                    foreach (var t in ToolRegistry.GetToolInfos()) ToolGate.SetEnabled(t.name, false);
            }
            _filter = EditorGUILayout.TextField("Filter", _filter);
        }

        private void DrawToolsList()
        {
            _toolsScroll = EditorGUILayout.BeginScrollView(_toolsScroll, GUILayout.Height(_toolsHeight));
            foreach (var t in ToolRegistry.GetToolInfos())
            {
                if (!string.IsNullOrEmpty(_filter) &&
                    t.name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    var enabled = ToolGate.IsEnabled(t.name);
                    var now = EditorGUILayout.Toggle(enabled, GUILayout.Width(16));
                    if (now != enabled) ToolGate.SetEnabled(t.name, now);

                    GUILayout.Label(new GUIContent(_infoIcon, Tooltip(t)),
                        GUILayout.Width(20), GUILayout.Height(16));

                    EditorGUILayout.LabelField(
                        new GUIContent(t.name, Tooltip(t)),
                        now ? EditorStyles.label : _disabledLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSplitter()
        {
            var rect = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, rect.width, 2), new Color(0.4f, 0.4f, 0.4f, 0.6f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                    _dragging = true; e.Use(); break;
                case EventType.MouseDrag when _dragging:
                    _toolsHeight += e.delta.y; e.Use(); Repaint(); break;
                case EventType.MouseUp when _dragging:
                    _dragging = false; EditorPrefs.SetFloat(ToolsHeightKey, _toolsHeight); e.Use(); break;
            }

            _toolsHeight = Mathf.Clamp(_toolsHeight, 60f, Mathf.Max(60f, position.height - 240f));
        }

        private void DrawActivity()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Recent activity", EditorStyles.boldLabel);
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(54)))
                    McpBridge.ClearActivity();
            }
            var log = McpBridge.RecentActivity;
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
            if (log.Length == 0)
            {
                EditorGUILayout.LabelField("(no requests yet)", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = log.Length - 1; i >= 0; i--)
                    EditorGUILayout.LabelField(log[i], EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private static string Tooltip(ToolRegistry.ToolInfo t)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(t.description);
            foreach (var p in t.parameters)
            {
                var opt = p.optional ? " (optional)" : "";
                sb.Append($"\n  {p.name} : {p.type}{opt}");
                if (!string.IsNullOrEmpty(p.description)) sb.Append(" — ").Append(p.description);
            }
            return sb.ToString();
        }

        private static void CopyableRow(string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(text, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                    EditorGUIUtility.systemCopyBuffer = text;
            }
        }
    }
}
