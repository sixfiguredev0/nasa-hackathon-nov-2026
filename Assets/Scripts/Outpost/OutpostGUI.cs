using System.Text;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>IMGUI helpers for the placeholder HUD and upgrade bubbles.</summary>
    public static class OutpostGUI
    {
        static Texture2D white, circle;
        static GUIStyle style;
        internal static readonly Color32 InstrumentAccent = new Color32(217, 170, 106, 255);
        internal static readonly Color32 InstrumentWarning = new Color32(246, 190, 175, 255);
        internal static readonly Color32 InstrumentSecondary = new Color32(225, 216, 208, 255);

        /// <summary>UI scale relative to the 540 px reference height.</summary>
        public static float Scale => Mathf.Max(1f, Screen.height / 540f);

        public static Texture2D White
        {
            get
            {
                if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
                return white;
            }
        }

        public static Texture2D Circle
        {
            get
            {
                if (circle != null) return circle;
                const int n = 64;
                circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude;
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(n / 2f - d));
                    }
                circle.SetPixels(px);
                circle.Apply();
                return circle;
            }
        }

        /// <summary>Shared label style, reconfigured per call (use it right away).</summary>
        public static GUIStyle Text(float size, TextAnchor anchor, Color color, bool wrap = false)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { richText = true, clipping = TextClipping.Overflow };
            style.fontSize = Mathf.RoundToInt(size);
            style.alignment = anchor;
            style.normal.textColor = color;
            style.wordWrap = wrap;
            return style;
        }

        public static void Panel(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = Color.white;
        }

        internal static void Frame(Rect rect, Color face, Color edge, float scale)
        {
            float pixel = Mathf.Max(1f, Mathf.Round(scale));
            Panel(new Rect(rect.x + pixel, rect.y, rect.width - 2f * pixel, rect.height), edge);
            Panel(new Rect(rect.x, rect.y + pixel, rect.width, rect.height - 2f * pixel), edge);
            Panel(new Rect(rect.x + pixel, rect.y + pixel, rect.width - 2f * pixel, rect.height - 2f * pixel), face);
        }

        internal static void InstrumentFrame(Rect rect, float scale, bool active = false)
        {
            Frame(rect, active ? FrontierUi.PanelDark : FrontierUi.Shadow, FrontierUi.PanelLight, scale);
        }

        public static void Disc(Vector2 center, float radius, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), Circle);
            GUI.color = Color.white;
        }

        public static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            Panel(new Rect(a.x, a.y - width / 2, (b - a).magnitude, width), c);
            GUI.matrix = m;
        }

        public static Vector2 WorldToGui(Camera cam, Vector3 world)
        {
            var p = cam.WorldToScreenPoint(world);
            return new Vector2(p.x, Screen.height - p.y);
        }

        public static Color ResColor(Res r)
        {
            switch (r)
            {
                case Res.Energy: return new Color(1f, 0.85f, 0.2f);
                case Res.Water: return new Color(0.3f, 0.6f, 1f);
                case Res.Food: return new Color(0.4f, 0.85f, 0.35f);
                case Res.Oxygen: return new Color(0.6f, 0.95f, 1f);
                default: return new Color(0.7f, 0.7f, 0.75f);
            }
        }

        /// <summary>"20 Metal, 10 Water" (absolute values, optional suffix per item).</summary>
        public static string List(Amount[] list, string suffix = "")
        {
            var sb = new StringBuilder();
            foreach (var a in list)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Mathf.Abs(a.value).ToString("0.#")).Append(' ').Append(a.res).Append(suffix);
            }
            return sb.ToString();
        }
    }
}
