// BridgeIcons.cs - our OWN button icons, drawn procedurally into textures.
//
// Why not Unity's built-in icons or styled text? Two reasons the window hit:
//   - a GUIStyle text color (e.g. a red "x") is overridden by the dark/light skin,
//     so it came out black in dark mode;
//   - built-in icon names vary between versions, and the "save" diskette read as
//     "save", not "export".
// Drawing the glyphs ourselves makes them theme-independent and unambiguous. The
// colored icons (red delete/stop, green start) keep their color on any skin; the
// neutral ones adapt to the current skin. Generated once and cached.

using UnityEditor;
using UnityEngine;

namespace UnityAgentBridge.Editor
{
    internal static class BridgeIcons
    {
        private const int S = 32; // drawn at 2x and downscaled (bilinear) for smooth edges

        private static readonly Color Red = new Color(0.88f, 0.34f, 0.34f);
        private static readonly Color Green = new Color(0.40f, 0.80f, 0.45f);
        private static Color Neutral => EditorGUIUtility.isProSkin
            ? new Color(0.82f, 0.82f, 0.82f) : new Color(0.25f, 0.25f, 0.25f);

        private static Texture2D _delete, _stop, _start, _restart, _export, _import, _plus, _edit, _run;

        public static Texture2D Delete => _delete ? _delete : _delete = BuildX(Red);
        public static Texture2D Stop => _stop ? _stop : _stop = BuildSquare(Red);
        public static Texture2D Start => _start ? _start : _start = BuildPlay(Green);
        public static Texture2D Run => _run ? _run : _run = BuildPlay(Green);
        public static Texture2D Restart => _restart ? _restart : _restart = BuildRestart(Neutral);
        public static Texture2D Export => _export ? _export : _export = BuildArrow(Neutral, up: true);
        public static Texture2D Import => _import ? _import : _import = BuildArrow(Neutral, up: false);
        public static Texture2D Plus => _plus ? _plus : _plus = BuildPlus(Neutral);
        public static Texture2D Edit => _edit ? _edit : _edit = BuildEdit(Neutral);

        // --- glyphs ------------------------------------------------------------

        private static Texture2D BuildX(Color c)
        {
            var px = Canvas();
            Line(px, 9, 9, 23, 23, 3.2f, c);
            Line(px, 9, 23, 23, 9, 3.2f, c);
            return Bake(px);
        }

        private static Texture2D BuildSquare(Color c)
        {
            var px = Canvas();
            Rect(px, 9, 9, 23, 23, c);
            return Bake(px);
        }

        private static Texture2D BuildPlay(Color c)
        {
            var px = Canvas();
            Tri(px, new Vector2(11, 8), new Vector2(11, 24), new Vector2(24, 16), c);
            return Bake(px);
        }

        private static Texture2D BuildPlus(Color c)
        {
            var px = Canvas();
            Rect(px, 14, 7, 18, 25, c); // vertical bar
            Rect(px, 7, 14, 25, 18, c); // horizontal bar
            return Bake(px);
        }

        // Export = arrow up out of a baseline; Import = arrow down to a baseline.
        private static Texture2D BuildArrow(Color c, bool up)
        {
            var px = Canvas();
            if (up)
            {
                Rect(px, 14, 9, 18, 22, c);                                   // stem
                Tri(px, new Vector2(8, 19), new Vector2(24, 19), new Vector2(16, 27), c); // head up
                Rect(px, 8, 6, 24, 8, c);                                     // baseline (out of)
            }
            else
            {
                Rect(px, 14, 10, 18, 23, c);                                  // stem
                Tri(px, new Vector2(8, 13), new Vector2(24, 13), new Vector2(16, 5), c);  // head down
                Rect(px, 8, 24, 24, 26, c);                                   // baseline (into)
            }
            return Bake(px);
        }

        private static Texture2D BuildEdit(Color c)
        {
            var px = Canvas();
            Line(px, 13, 13, 24, 24, 4.5f, c);                                  // pencil body (diagonal)
            Tri(px, new Vector2(7, 7), new Vector2(14, 11), new Vector2(11, 14), c); // sharp tip toward lower-left
            Rect(px, 21, 21, 25, 25, c);                                        // eraser end (upper-right)
            return Bake(px);
        }

        private static Texture2D BuildRestart(Color c)
        {
            var px = Canvas();
            float cx = 16, cy = 16, r = 8;
            // ~270 degree arc, leaving a gap at the top-right for the arrowhead.
            for (float a = 60; a <= 60 + 270; a += 3f)
            {
                var rad = a * Mathf.Deg2Rad;
                Disc(px, cx + r * Mathf.Cos(rad), cy + r * Mathf.Sin(rad), 1.7f, c);
            }
            // Arrowhead at the open (start) end, pointing along the rotation.
            var sa = 60 * Mathf.Deg2Rad;
            var tip = new Vector2(cx + r * Mathf.Cos(sa), cy + r * Mathf.Sin(sa));
            Tri(px, tip + new Vector2(3.5f, 3.5f), tip + new Vector2(-4.5f, 1.5f), tip + new Vector2(1.5f, -4.5f), c);
            return Bake(px);
        }

        // --- tiny raster helpers ----------------------------------------------

        private static Color[] Canvas() => new Color[S * S]; // all transparent (0,0,0,0)

        private static void Set(Color[] px, int x, int y, Color c)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            px[y * S + x] = c;
        }

        private static void Rect(Color[] px, int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Set(px, x, y, c);
        }

        private static void Disc(Color[] px, float cx, float cy, float r, Color c)
        {
            int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
            int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r * r) Set(px, x, y, c);
                }
        }

        private static void Line(Color[] px, float x0, float y0, float x1, float y1, float thickness, Color c)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))) * 2 + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(px, Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), thickness * 0.5f, c);
            }
        }

        private static void Tri(Color[] px, Vector2 a, Vector2 b, Vector2 c2, Color col)
        {
            int minx = Mathf.FloorToInt(Mathf.Min(a.x, b.x, c2.x)), maxx = Mathf.CeilToInt(Mathf.Max(a.x, b.x, c2.x));
            int miny = Mathf.FloorToInt(Mathf.Min(a.y, b.y, c2.y)), maxy = Mathf.CeilToInt(Mathf.Max(a.y, b.y, c2.y));
            for (int y = miny; y <= maxy; y++)
                for (int x = minx; x <= maxx; x++)
                    if (InTri(a, b, c2, new Vector2(x + 0.5f, y + 0.5f)))
                        Set(px, x, y, col);
        }

        private static bool InTri(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Edge(Vector2 p1, Vector2 p2, Vector2 p3)
            => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        private static Texture2D Bake(Color[] px)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
