// McpBridgeWindow.cs - the in-Editor control panel for the bridge.
//
// Window > Unity Agent Bridge. Live status; set the port; start/stop/restart; manage
// custom commands and custom tools (create, delete, export/import - all in-window);
// an allow-list of tools. The upper area scrolls and folds; a splitter above the
// activity log resizes the log against everything above it.

using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityAgentBridge.Editor.Tools;

namespace UnityAgentBridge.Editor
{
    public class McpBridgeWindow : EditorWindow
    {
        private const string ActivityHeightKey = "McpBridge.Window.ActivityHeight";
        private const string CliOpenKey = "McpBridge.Window.CliOpen";
        private const string CmdOpenKey = "McpBridge.Window.CmdOpen";
        private const string ToolOpenKey = "McpBridge.Window.ToolOpen";
        private const string ToolsListOpenKey = "McpBridge.Window.ToolsListOpen";
        private const float LabelWidth = 92f;
        private const float BtnWide = 130f;
        private const float BtnNarrow = 56f;
        private const float IconBtn = 28f;

        private static readonly Regex ToolNameRx = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly Regex CmdNameRx = new Regex(@"^[A-Za-z0-9_-]+$");

        private int _portField;
        private string _filter = "";
        private string _newToolName = "";
        private string _newCommandName = "";
        private Vector2 _mainScroll;
        private Vector2 _logScroll;
        private float _activityHeight = 110f;
        private bool _dragging;
        private bool _cliOpen, _cmdOpen, _toolOpen, _toolsListOpen;

        private string _notice = "";
        private MessageType _noticeKind = MessageType.Info;

        private GUIStyle _disabledLabel;
        private GUIStyle _listeningStyle;
        private GUIStyle _stoppedStyle;
        private GUIStyle _errorLabel;
        private GUIStyle _headerStyle;
        private GUIStyle _foldoutHeader;
        private GUIStyle _iconButton;
        private Texture _infoIcon;

        private static Color HeaderColor => EditorGUIUtility.isProSkin
            ? new Color(0.46f, 0.73f, 0.96f) : new Color(0.13f, 0.40f, 0.75f);
        private readonly System.Collections.Generic.Dictionary<string, string> _tooltipCache = new();

        [MenuItem("Window/Unity Agent Bridge")]
        public static void Open()
        {
            var w = GetWindow<McpBridgeWindow>("Agent Bridge");
            w.minSize = new Vector2(360, 420);
            w.Show();
        }

        private void OnEnable()
        {
            // Force the tab label every enable, so a window persisted in an old layout
            // (titled "MCP Bridge") updates to "Agent Bridge" on the next reload.
            titleContent = new GUIContent("Agent Bridge");
            _portField = McpBridge.Port;
            _activityHeight = EditorPrefs.GetFloat(ActivityHeightKey, 110f);
            _cliOpen = EditorPrefs.GetBool(CliOpenKey, false);
            _cmdOpen = EditorPrefs.GetBool(CmdOpenKey, true);
            _toolOpen = EditorPrefs.GetBool(ToolOpenKey, true);
            _toolsListOpen = EditorPrefs.GetBool(ToolsListOpenKey, true);
        }

        private void OnInspectorUpdate() => Repaint();

        private void Notify(string message, MessageType kind = MessageType.Info)
        {
            _notice = message;
            _noticeKind = kind;
        }

        private void OnGUI()
        {
            EnsureStyles();
            EditorGUIUtility.labelWidth = LabelWidth;

            DrawNotice();  // outside the scroll view, so feedback is always visible

            // Upper region: scrolls and folds. Its height is whatever the resizable
            // activity log at the bottom leaves it - so dragging the splitter shrinks
            // ALL the content above, not just one section.
            var noticeH = string.IsNullOrEmpty(_notice) ? 0f : 46f;
            _activityHeight = Mathf.Clamp(_activityHeight, 60f, Mathf.Max(60f, position.height - 220f));
            var upperH = Mathf.Max(140f, position.height - _activityHeight - 60f - noticeH);

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.Height(upperH));
            DrawStatus();
            Separator();
            DrawServer();
            Separator();
            DrawCliHints();
            Separator();
            DrawCommands();
            Separator();
            DrawCustomTools();
            Separator();
            DrawTools();
            EditorGUILayout.EndScrollView();

            DrawActivitySplitter();
            DrawActivity();
        }

        private void EnsureStyles()
        {
            _disabledLabel ??= new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.55f, 0.55f, 0.55f) } };
            _listeningStyle ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.30f, 0.80f, 0.36f) } };
            _stoppedStyle ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.85f, 0.40f, 0.40f) } };
            _errorLabel ??= new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.92f, 0.46f, 0.46f) } };
            _headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = HeaderColor } };
            _foldoutHeader ??= MakeFoldoutHeader();
            _iconButton ??= new GUIStyle(EditorStyles.miniButton)
            {
                padding = new RectOffset(1, 1, 1, 1),   // minimal padding so the glyph fills the button
                imagePosition = ImagePosition.ImageOnly,
                alignment = TextAnchor.MiddleCenter,
            };
            _infoIcon ??= EditorGUIUtility.IconContent("console.infoicon").image;
        }

        private void DrawNotice()
        {
            if (string.IsNullOrEmpty(_notice)) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(_notice, _noticeKind);
                if (GUILayout.Button("x", GUILayout.Width(22), GUILayout.Height(38)))
                    _notice = "";
            }
            EditorGUILayout.Space(2);
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", _headerStyle);
            var listening = McpBridge.IsListening;
            EditorGUILayout.LabelField(listening ? "● Listening" : "○ Stopped",
                listening ? _listeningStyle : _stoppedStyle);

            KeyVal("URL", $"ws://{McpBridge.Host}:{McpBridge.Port}");
            KeyVal("Client", McpBridge.ClientConnected ? "connected" : "-");
            KeyVal("Auth", "token gate (Host + Origin checked)");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Token", EditorStyles.miniLabel, GUILayout.Width(LabelWidth));
                if (GUILayout.Button("Copy path", EditorStyles.miniButton, GUILayout.Width(72)))
                    EditorGUIUtility.systemCopyBuffer = BridgeAuth.TokenPath(McpBridge.Port);
                if (GUILayout.Button("Reveal", EditorStyles.miniButton, GUILayout.Width(58)))
                    EditorUtility.RevealInFinder(BridgeAuth.TokenPath(McpBridge.Port));
                GUILayout.FlexibleSpace();
            }
        }

        private static void KeyVal(string key, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(key, EditorStyles.miniLabel, GUILayout.Width(LabelWidth));
                EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.Height(14));
            }
        }

        private void DrawServer()
        {
            EditorGUILayout.LabelField("Server", _headerStyle);

            var portValid = _portField >= 1024 && _portField <= 65535;
            var portChanged = _portField != McpBridge.Port;
            using (new EditorGUILayout.HorizontalScope())
            {
                _portField = EditorGUILayout.IntField("Port", _portField, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(!portValid || !portChanged))
                    if (GUILayout.Button(new GUIContent("Apply", "Apply the port and restart the bridge"), GUILayout.Width(64)))
                    {
                        McpBridge.SetPort(_portField);
                        _portField = McpBridge.Port;
                        GUI.FocusControl(null);
                        Notify($"Bridge restarted on port {McpBridge.Port}.");
                    }
                if (McpBridge.IsListening)
                {
                    if (GUILayout.Button(new GUIContent(BridgeIcons.Stop, "Stop the bridge"), _iconButton, GUILayout.Width(40), GUILayout.Height(20))) { McpBridge.Stop(); Notify("Bridge stopped."); }
                }
                else
                {
                    if (GUILayout.Button(new GUIContent(BridgeIcons.Start, "Start the bridge"), _iconButton, GUILayout.Width(40), GUILayout.Height(20))) { McpBridge.StartServer(); Notify("Bridge started."); }
                }
                if (GUILayout.Button(new GUIContent(BridgeIcons.Restart, "Restart the bridge"), _iconButton, GUILayout.Width(40), GUILayout.Height(20))) { McpBridge.Restart(); Notify("Bridge restarted."); }
                GUILayout.FlexibleSpace();
            }
            if (!portValid)
                EditorGUILayout.HelpBox("Port must be between 1024 and 65535.", MessageType.Error);
        }

        private void DrawCliHints()
        {
            if (!Foldout(ref _cliOpen, CliOpenKey, "CLI")) return;
            EditorGUILayout.LabelField("run unity-agent-bridge.cmd from the repo root", EditorStyles.miniLabel);
            var portArg = McpBridge.Port == McpBridge.DefaultPort ? "" : $"--port {McpBridge.Port} ";
            CopyableRow($"unity-agent-bridge {portArg}ping");
            CopyableRow($"unity-agent-bridge {portArg}list");
        }

        private void DrawCommands()
        {
            var dir = CommandTools.CommandsDir;
            var count = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
            if (!Foldout(ref _cmdOpen, CmdOpenKey, $"Custom commands ({count})")) return;

            EditorGUILayout.LabelField("no-code macros of tool calls, shareable as a .json pack", EditorStyles.miniLabel);

            var nameValid = CmdNameRx.IsMatch(_newCommandName ?? "");
            using (new EditorGUILayout.HorizontalScope())
            {
                _newCommandName = EditorGUILayout.TextField("New command", _newCommandName);
                using (new EditorGUI.DisabledScope(!nameValid))
                    if (GUILayout.Button(new GUIContent(BridgeIcons.Plus, "Scaffold a new command from a template"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                        CreateCommand();
            }
            if (!string.IsNullOrEmpty(_newCommandName) && !nameValid)
                EditorGUILayout.LabelField("    use letters, digits, '_' or '-'", _disabledLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button(new GUIContent(BridgeIcons.Export, "Export all commands to a shareable .json pack"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                    {
                        var res = CommandTools.ExportCommands(null, null);
                        EditorUtility.RevealInFinder(CommandTools.DefaultPackPath);
                        Notify($"Exported {Field(res, "exported")} command(s) to {CommandTools.DefaultPackPath}.");
                    }
                if (GUILayout.Button(new GUIContent(BridgeIcons.Import, "Import commands from a .json pack"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                    ImportCommandsPack(dir);
                if (GUILayout.Button(Icon("Folder Icon", "Folder", "Open the commands folder"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                    RevealFolder(dir);
            }

            EditorGUILayout.Space(2);
            DrawItemRows(dir, "*.json", "(no commands yet)", "command", DeleteCommand, EditCommand, RunCommandFromWindow);
        }

        private void DrawCustomTools()
        {
            var dir = CustomToolTools.CustomToolsDir;
            var count = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cs").Length : 0;
            if (!Foldout(ref _toolOpen, ToolOpenKey, $"Custom tools ({count})")) return;

            EditorGUILayout.LabelField("real C# [McpTool]s, shareable as a .json pack", EditorStyles.miniLabel);

            var nameValid = ToolNameRx.IsMatch(_newToolName ?? "");
            using (new EditorGUILayout.HorizontalScope())
            {
                _newToolName = EditorGUILayout.TextField("New tool", _newToolName);
                using (new EditorGUI.DisabledScope(!nameValid))
                    if (GUILayout.Button(new GUIContent(BridgeIcons.Plus, "Scaffold a new custom C# tool"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                        CreateCustomTool();
            }
            if (!string.IsNullOrEmpty(_newToolName) && !nameValid)
                EditorGUILayout.LabelField("    use a C# identifier: letters, digits, '_' (not starting with a digit)", _disabledLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button(new GUIContent(BridgeIcons.Export, "Export all custom tools to a shareable .json pack"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                    {
                        var res = CustomToolTools.ExportTools(null, null);
                        EditorUtility.RevealInFinder(CustomToolTools.DefaultPackPath);
                        Notify($"Exported {Field(res, "exported")} tool(s) to {CustomToolTools.DefaultPackPath}.");
                    }
                if (GUILayout.Button(new GUIContent(BridgeIcons.Import, "Import custom tools from a .json pack"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                    ImportToolsPack(dir);
                if (GUILayout.Button(Icon("Folder Icon", "Folder", "Open the custom tools folder"), _iconButton, GUILayout.Width(IconBtn), GUILayout.Height(20)))
                    RevealFolder(dir);
            }

            EditorGUILayout.Space(2);
            DrawItemRows(dir, "*.cs", "(no custom tools yet)", "tool", DeleteCustomTool, EditCustomTool);
        }

        private void DrawTools()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var open = EditorGUILayout.Foldout(_toolsListOpen, "Tools", true, _foldoutHeader);
                if (open != _toolsListOpen) EditorPrefs.SetBool(ToolsListOpenKey, _toolsListOpen = open);
                if (_toolsListOpen)
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("All on", EditorStyles.miniButtonLeft, GUILayout.Width(BtnNarrow)))
                        foreach (var t in ToolRegistry.GetToolInfos()) ToolGate.SetEnabled(t.name, true);
                    if (GUILayout.Button("All off", EditorStyles.miniButtonRight, GUILayout.Width(BtnNarrow)))
                        foreach (var t in ToolRegistry.GetToolInfos()) ToolGate.SetEnabled(t.name, false);
                }
            }
            if (!_toolsListOpen) return;

            EditorGUILayout.LabelField("check = allowed; hover the icon for parameters", EditorStyles.miniLabel);
            _filter = EditorGUILayout.TextField("Filter", _filter);

            var shown = 0;
            foreach (var t in ToolRegistry.GetToolInfos())
            {
                if (!string.IsNullOrEmpty(_filter) &&
                    t.name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                shown++;

                using (new EditorGUILayout.HorizontalScope())
                {
                    var enabled = ToolGate.IsEnabled(t.name);
                    var now = EditorGUILayout.Toggle(enabled, GUILayout.Width(16));
                    if (now != enabled) ToolGate.SetEnabled(t.name, now);

                    var tip = Tooltip(t);
                    GUILayout.Label(new GUIContent(_infoIcon, tip), GUILayout.Width(20), GUILayout.Height(16));
                    if (GUILayout.Button(new GUIContent(t.name, tip),
                            now ? EditorStyles.label : _disabledLabel, GUILayout.ExpandWidth(true)))
                        ToolGate.SetEnabled(t.name, !now);
                }
            }
            if (shown == 0)
                EditorGUILayout.LabelField(string.IsNullOrEmpty(_filter) ? "(no tools)" : "(no tools match filter)",
                    EditorStyles.miniLabel);
        }

        private void DrawActivitySplitter()
        {
            var rect = GUILayoutUtility.GetRect(0, 8, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3, rect.width, 2), new Color(0.5f, 0.5f, 0.5f, 0.7f));
            var hx = rect.x + rect.width * 0.5f - 9;
            EditorGUI.DrawRect(new Rect(hx, rect.y + 2, 18, 4), new Color(0.6f, 0.6f, 0.6f, 0.9f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                    _dragging = true; e.Use(); break;
                case EventType.MouseDrag when _dragging:
                    // Drag up -> activity grows (and the scrolling area above shrinks).
                    _activityHeight -= e.delta.y; e.Use(); Repaint(); break;
                case EventType.MouseUp when _dragging:
                    _dragging = false; EditorPrefs.SetFloat(ActivityHeightKey, _activityHeight); e.Use(); break;
            }
        }

        private void DrawActivity()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Recent activity", _headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(BtnNarrow)))
                    McpBridge.ClearActivity();
            }
            var log = McpBridge.RecentActivity;
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(_activityHeight));
            if (log.Length == 0)
                EditorGUILayout.LabelField("(no requests yet)", EditorStyles.miniLabel);
            else
                for (int i = log.Length - 1; i >= 0; i--)
                    EditorGUILayout.LabelField(log[i].Text, log[i].IsError ? _errorLabel : EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        // --- item rows (name + delete) -----------------------------------------

        private void DrawItemRows(string dir, string pattern, string emptyLabel, string kind,
            Action<string> onDelete, Action<string> onEdit, Action<string> onRun = null)
        {
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>();
            if (files.Length == 0)
            {
                EditorGUILayout.LabelField("    " + emptyLabel, EditorStyles.miniLabel);
                return;
            }
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("    • " + name, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (onRun != null && RowIcon(BridgeIcons.Run, $"Run {kind} '{name}'")) onRun(name);
                    if (RowIcon(BridgeIcons.Edit, $"Edit {kind} '{name}' in your editor")) onEdit(name);
                    if (RowIcon(BridgeIcons.Delete, $"Delete {kind} '{name}'")
                        && EditorUtility.DisplayDialog($"Delete {kind}",
                            $"Delete {kind} '{name}'? This removes the file from disk.", "Delete", "Cancel"))
                        onDelete(name);
                }
            }
        }

        private bool RowIcon(Texture2D icon, string tooltip)
            => GUILayout.Button(new GUIContent(icon, tooltip), _iconButton, GUILayout.Width(26), GUILayout.Height(20));

        private void EditCommand(string name) =>
            EditorUtility.OpenWithDefaultApp(Path.Combine(CommandTools.CommandsDir, name + ".json"));

        private void EditCustomTool(string name) =>
            EditorUtility.OpenWithDefaultApp(Path.Combine(CustomToolTools.CustomToolsDir, name + ".cs"));

        private void RunCommandFromWindow(string name)
        {
            try
            {
                var res = CommandTools.RunCommand(name, null);
                var ok = res?.GetType().GetProperty("ok")?.GetValue(res) as bool? ?? true;
                Notify(ok ? $"Ran command '{name}'." : $"Command '{name}' failed at a step - see Recent activity.",
                    ok ? MessageType.Info : MessageType.Warning);
            }
            catch (Exception e) { Notify("Run failed: " + e.Message, MessageType.Error); }
        }

        private void DeleteCommand(string name)
        {
            CommandTools.DeleteCommand(name);
            Notify($"Deleted command '{name}'.");
        }

        private void DeleteCustomTool(string name)
        {
            CustomToolTools.DeleteCustomTool(name);
            AssetDatabase.Refresh();
            Notify($"Deleted tool '{name}'. Recompiling...");
        }

        private void CreateCommand()
        {
            try
            {
                var res = CommandTools.NewCommand(_newCommandName.Trim(), null, false);
                var path = Field(res, "path");
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
                Notify($"Created {_newCommandName.Trim()}.json - edit its steps.");
                _newCommandName = "";
                GUI.FocusControl(null);
            }
            catch (Exception e) { Notify("Create failed: " + e.Message, MessageType.Error); }
        }

        private void CreateCustomTool()
        {
            try
            {
                var res = CustomToolTools.NewCustomTool(_newToolName.Trim(), null, false);
                var path = Field(res, "path");
                AssetDatabase.Refresh();
                if (!string.IsNullOrEmpty(path))
                {
                    var rel = "Assets" + path.Replace(Application.dataPath, "").Replace('\\', '/');
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rel);
                    if (asset != null) AssetDatabase.OpenAsset(asset);
                }
                Notify($"Created {_newToolName.Trim()}.cs - edit it, then it compiles into a tool.");
                _newToolName = "";
                GUI.FocusControl(null);
            }
            catch (Exception e) { Notify("Create failed: " + e.Message, MessageType.Error); }
        }

        private void ImportCommandsPack(string dir)
        {
            var start = Directory.Exists(dir) ? dir : Environment.CurrentDirectory;
            var p = EditorUtility.OpenFilePanel("Import command pack", start, "json");
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                var res = CommandTools.ImportCommands(p, false);
                Notify($"Imported {Field(res, "imported")} command(s), skipped {Field(res, "skipped")}.");
            }
            catch (Exception e) { Notify("Import failed: " + e.Message, MessageType.Error); }
        }

        private void ImportToolsPack(string dir)
        {
            var start = Directory.Exists(dir) ? dir : Environment.CurrentDirectory;
            var p = EditorUtility.OpenFilePanel("Import tool pack", start, "json");
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                var res = CustomToolTools.ImportTools(p, false);
                AssetDatabase.Refresh();
                Notify($"Imported {Field(res, "imported")} tool(s), skipped {Field(res, "skipped")}. Recompiling...");
            }
            catch (Exception e) { Notify("Import failed: " + e.Message, MessageType.Error); }
        }

        // --- helpers -----------------------------------------------------------

        private bool Foldout(ref bool state, string prefKey, string title)
        {
            var open = EditorGUILayout.Foldout(state, title, true, _foldoutHeader);
            if (open != state) EditorPrefs.SetBool(prefKey, state = open);
            return open;
        }

        private static GUIStyle MakeFoldoutHeader()
        {
            var s = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            var c = HeaderColor;
            s.normal.textColor = c; s.onNormal.textColor = c;
            s.focused.textColor = c; s.onFocused.textColor = c;
            s.active.textColor = c; s.onActive.textColor = c;
            s.hover.textColor = c; s.onHover.textColor = c;
            return s;
        }

        private static GUIContent Icon(string iconName, string fallback, string tooltip)
        {
            var c = EditorGUIUtility.IconContent(iconName);
            return (c != null && c.image != null) ? new GUIContent(c.image, tooltip) : new GUIContent(fallback, tooltip);
        }

        private static void Separator()
        {
            EditorGUILayout.Space(5);
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.08f));
            EditorGUILayout.Space(5);
        }

        private static void RevealFolder(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        private static string Field(object obj, string name)
        {
            var v = obj?.GetType().GetProperty(name)?.GetValue(obj);
            return v?.ToString() ?? "";
        }

        private string Tooltip(ToolRegistry.ToolInfo t)
        {
            if (_tooltipCache.TryGetValue(t.name, out var cached)) return cached;
            var sb = new System.Text.StringBuilder();
            sb.Append(t.description);
            foreach (var p in t.parameters)
            {
                var opt = p.optional ? " (optional)" : "";
                sb.Append($"\n  {p.name} : {p.type}{opt}");
                if (!string.IsNullOrEmpty(p.description)) sb.Append(" - ").Append(p.description);
            }
            var s = sb.ToString();
            _tooltipCache[t.name] = s;
            return s;
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
