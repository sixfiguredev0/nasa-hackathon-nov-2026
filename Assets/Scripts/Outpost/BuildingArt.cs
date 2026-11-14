using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Animation frames per upgrade node. Real art wins: Resources/Buildings/&lt;node id&gt;, else Resources/Buildings/&lt;BuildingKind&gt;.
    /// A sheet is a horizontal strip of square frames (16 PPU); its pivot (the footprint centre) comes from <see cref="ArtPivots"/>.
    /// Otherwise a generated pixel-art placeholder sized to the zone footprint on the 24x12 oblique lattice.
    /// </summary>
    public static class BuildingArt
    {
        const int CellW = 24, CellH = 12, Pad = 2, MaxStructure = 110;
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();

        /// <summary>Pixel inside one frame (from the bottom-left) that sits on the footprint centre.</summary>
        static readonly Dictionary<string, Vector2> ArtPivots = new Dictionary<string, Vector2>
        {
            { "Rocket", new Vector2(64, 21) }, // centre of the base on the last (landed) frame
        };

        public static Sprite[] Get(BuildingDef def, UpgradeNode node, Vector2Int footprint)
        {
            if (cache.TryGetValue(node.id, out var s) && s != null && s[0] != null) return s;
            s = Sheet("Buildings/", node.id) ?? Sheet("Buildings/", def.kind.ToString())
                ?? new[] { Placeholder(def.kind, node, footprint) };
            cache[node.id] = s;
            return s;
        }

        /// <summary>Slices Resources/&lt;folder&gt;&lt;name&gt; into square frames; null when the texture is missing.</summary>
        public static Sprite[] Sheet(string folder, string name, Vector2? pivotPx = null)
        {
            var tex = Resources.Load<Texture2D>(folder + name);
            if (tex == null) return null;
            int fw = tex.height, n = Mathf.Max(1, tex.width / fw);
            var pv = pivotPx ?? (ArtPivots.TryGetValue(name, out var p) ? p : new Vector2(fw / 2f, fw / 2f));
            var frames = new Sprite[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(i * fw, 0, fw, fw), new Vector2(pv.x / fw, pv.y / fw), 16f, 0, SpriteMeshType.FullRect);
                frames[i].name = name + "_" + i;
            }
            return frames;
        }

        // ---- placeholder drawing ----

        static readonly Color32 Clear = new Color32(0, 0, 0, 0), Ink = new Color32(40, 22, 24, 255),
            PadCol = new Color32(150, 128, 112, 255), WhiteCol = new Color32(232, 232, 236, 255),
            DarkGrey = new Color32(62, 62, 72, 255), Blue = new Color32(44, 74, 150, 255),
            LightBlue = new Color32(100, 150, 215, 255), Yellow = new Color32(240, 200, 60, 255), Orange = new Color32(220, 120, 50, 255),
            Green = new Color32(64, 170, 72, 255), Brown = new Color32(140, 92, 58, 255);

        class Canvas
        {
            public readonly int w, h;
            public readonly Color32[] px;
            public Canvas(int w, int h) { this.w = w; this.h = h; px = new Color32[w * h]; }

            public void Set(int x, int y, Color32 c)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                px[y * w + x] = c;
            }

            /// <summary>Filled box with a light top row and a dark right column.</summary>
            public void Box(int x, int y, int bw, int bh, Color32 c)
            {
                for (int j = 0; j < bh; j++)
                    for (int i = 0; i < bw; i++)
                        Set(x + i, y + j, j == bh - 1 ? Tint(c, 1.2f) : i == bw - 1 ? Tint(c, 0.7f) : c);
            }

            /// <summary>Filled ellipse, right side shaded; top = only the upper half (a dome).</summary>
            public void Ellipse(int cx, int cy, int rx, int ry, Color32 c, bool top = false)
            {
                for (int j = top ? 0 : -ry; j <= ry; j++)
                    for (int i = -rx; i <= rx; i++)
                        if ((float)i * i / (rx * rx) + (float)j * j / (ry * ry) <= 1f)
                            Set(cx + i, cy + j, i > rx * 0.35f ? Tint(c, 0.75f) : c);
            }

            /// <summary>Triangle pointing up, base width bw at y.</summary>
            public void Spike(int cx, int y, int bw, int th, Color32 c)
            {
                for (int t = 0; t < th; t++)
                {
                    int ww = Mathf.Max(1, bw * (th - t) / th);
                    Box(cx - ww / 2, y + t, ww, 1, c);
                }
            }
        }

        static Color32 Tint(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), c.a);

        static Sprite Placeholder(BuildingKind kind, UpgradeNode node, Vector2Int fp)
        {
            int footW = fp.x * CellW + fp.y * CellH, footH = fp.y * CellH;
            var cv = new Canvas(footW + Pad * 2, footH + MaxStructure + Pad * 2);
            int cx = Pad + footW / 2, cy = Pad + footH / 2;
            int L = Mathf.Max(1, node.level);
            char last = node.id[node.id.Length - 1];
            char branch = last == 'a' || last == 'b' ? last : ' ';

            // footprint parallelogram: each row shifts 1 px right (lattice skew)
            for (int y = 0; y < footH; y++)
                for (int x = 0; x < fp.x * CellW; x++)
                {
                    bool edge = y == 0 || y == footH - 1 || x == 0 || x == fp.x * CellW - 1;
                    cv.Set(Pad + x + y, Pad + y, edge ? Tint(PadCol, 0.8f) : PadCol);
                }

            Draw(cv, kind, L, branch, cx, cy, fp);
            Outline(cv);

            int top = cv.h - 1;
            while (top > 0 && RowEmpty(cv, top)) top--;
            int texH = top + 2;
            var tex = new Texture2D(cv.w, texH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var crop = new Color32[cv.w * texH];
            System.Array.Copy(cv.px, crop, crop.Length);
            tex.SetPixels32(crop);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, cv.w, texH), new Vector2((float)cx / cv.w, (float)cy / texH), 16f, 0, SpriteMeshType.FullRect);
            sp.name = "PLACEHOLDER_" + node.id;
            return sp;
        }

        static bool RowEmpty(Canvas cv, int y)
        {
            for (int x = 0; x < cv.w; x++) if (cv.px[y * cv.w + x].a > 0) return false;
            return true;
        }

        static void Outline(Canvas cv)
        {
            var src = (Color32[])cv.px.Clone();
            for (int y = 0; y < cv.h; y++)
                for (int x = 0; x < cv.w; x++)
                {
                    if (src[y * cv.w + x].a > 0) continue;
                    bool near = (x > 0 && src[y * cv.w + x - 1].a > 100) || (x < cv.w - 1 && src[y * cv.w + x + 1].a > 100) ||
                                (y > 0 && src[(y - 1) * cv.w + x].a > 100) || (y < cv.h - 1 && src[(y + 1) * cv.w + x].a > 100);
                    if (near) cv.px[y * cv.w + x] = Ink;
                }
        }

        static void Draw(Canvas cv, BuildingKind kind, int L, char branch, int cx, int cy, Vector2Int fp)
        {
            switch (kind)
            {
                case BuildingKind.Solar:
                {
                    int rows = Mathf.Min(1 + L, fp.y * 2), pw = fp.x * CellW - 12, ph = branch == 'a' ? 6 : 4;
                    for (int r = rows - 1; r >= 0; r--)
                    {
                        int py = Pad + 3 + r * (fp.y * CellH - 6) / rows, px = Pad + 4 + (py - Pad);
                        cv.Box(px + pw / 2, py, 1, 3, DarkGrey);
                        if (branch == 'a') cv.Box(px + pw / 2 - 1, py, 3, 5, DarkGrey);
                        cv.Box(px, py + 3, pw, ph, Blue);
                        for (int gx = px + 5; gx < px + pw - 1; gx += 6) cv.Box(gx, py + 3, 1, ph, LightBlue);
                        if (branch == 'b') cv.Box(px, py + 3 + ph, pw, 1, WhiteCol);
                    }
                    break;
                }
                case BuildingKind.Battery:
                {
                    int n = branch == 'b' ? 2 : L, total = n * 9;
                    for (int k = 0; k < n; k++)
                    {
                        int bx = cx - total / 2 + k * 9 - (branch == 'b' ? 8 : 0);
                        cv.Box(bx, cy - 4, 7, 10 + (branch == 'a' ? 6 : 0), new Color32(90, 120, 100, 255));
                        cv.Box(bx, cy + 1, 7, 2, Yellow);
                    }
                    if (branch == 'b')
                    {
                        cv.Box(cx + 6, cy - 4, 9, 14, WhiteCol);
                        cv.Ellipse(cx + 10, cy + 10, 4, 2, WhiteCol);
                    }
                    break;
                }
                case BuildingKind.Water:
                {
                    int th = 8 + 4 * L, tower = 16 + 6 * L + (branch == 'a' ? 12 : 0);
                    var tank = new Color32(150, 190, 220, 255);
                    if (branch == 'b') { cv.Box(cx - 32, cy - 4, 12, th - 2, tank); cv.Ellipse(cx - 26, cy - 6 + th, 6, 2, tank); }
                    cv.Box(cx - 16, cy - 4, 14, th, tank);
                    cv.Ellipse(cx - 9, cy - 4 + th, 7, 3, Tint(tank, 1.15f));
                    cv.Box(cx + 6, cy - 4, 1, tower, Orange);
                    cv.Box(cx + 13, cy - 4, 1, tower, Orange);
                    for (int y = 0; y < tower; y += 4) cv.Box(cx + 6, cy - 4 + y, 8, 1, Orange);
                    cv.Spike(cx + 10, cy - 4 + tower, 9, 5, Orange);
                    cv.Box(cx + 9, cy - 8, 2, 6, DarkGrey);
                    break;
                }
                case BuildingKind.Greenhouse:
                {
                    int rx = 18 + 5 * L, ry = 10 + 4 * L;
                    if (branch == 'a') for (int k = -2; k <= 2; k++) cv.Box(cx + k * 8 - 3, cy - 3, 6, 2, Brown);
                    for (int k = -rx + 5; k < rx - 4; k += 6) cv.Ellipse(cx + k, cy - 1 + (k & 2), 2, 2, Green);
                    if (branch == 'b') for (int k = -2; k <= 2; k++) cv.Box(cx + k * 7, cy - 2, 3, ry - 3, new Color32(80, 200, 90, 230));
                    cv.Ellipse(cx, cy - 3, rx, ry, new Color32(175, 230, 240, 110), true);
                    for (int k = -rx + 6; k < rx; k += 8) cv.Box(cx + k, cy - 3, 1, (int)(ry * Mathf.Sqrt(1f - (float)k * k / (rx * rx))), new Color32(240, 250, 255, 170));
                    break;
                }
                case BuildingKind.Habitat:
                {
                    if (branch == 'a')
                    {
                        cv.Ellipse(cx, cy - 4, 34, 18, Brown, true);
                        cv.Box(cx - 5, cy - 4, 10, 10, DarkGrey);
                        break;
                    }
                    int n = branch == 'b' ? 2 : Mathf.Min(L, 3);
                    for (int k = 0; k < n; k++)
                    {
                        int dx = cx + k * 24 - (n - 1) * 12;
                        cv.Ellipse(dx, cy - 4, 11, 10, WhiteCol, true);
                        cv.Box(dx - 2, cy - 4, 4, 5, DarkGrey);
                    }
                    if (branch == 'b') for (int k = 0; k < 3; k++) cv.Box(cx + 26 + k * 6, cy - 6, 4, 14, LightBlue);
                    break;
                }
                case BuildingKind.Workshop:
                {
                    int bw = 24 + 4 * L, bh = 10 + 3 * L, x0 = cx - bw / 2, y0 = cy - 5;
                    cv.Box(x0 + bw - 7, y0 + bh, 3, 6 + 2 * L, DarkGrey);
                    cv.Box(x0, y0, bw, bh, new Color32(160, 160, 172, 255));
                    cv.Box(x0, y0 + bh - 2, bw, 2, Orange);
                    cv.Box(cx - 3, y0, 6, 7, DarkGrey);
                    if (branch == 'a') { cv.Box(x0 - 6, y0, 2, bh + 8, new Color32(150, 90, 190, 255)); cv.Box(x0 - 6, y0 + bh + 6, 8, 2, new Color32(150, 90, 190, 255)); }
                    if (branch == 'b') { cv.Box(x0 - 14, y0 - 2, 9, 5, Yellow); cv.Box(x0 - 13, y0 - 4, 2, 2, DarkGrey); cv.Box(x0 - 8, y0 - 4, 2, 2, DarkGrey); }
                    break;
                }
                case BuildingKind.Gym:
                {
                    // low hall with a dumbbell sign; 3a adds a weight rack, 3b a pool dome
                    int shift = branch == 'b' ? 8 : 0, bw = 22 + 3 * L, bh = 9 + 2 * L, x0 = cx - bw / 2 - shift, y0 = cy - 5;
                    cv.Box(x0, y0, bw, bh, new Color32(200, 120, 110, 255));
                    cv.Box(x0, y0 + bh - 2, bw, 2, WhiteCol);
                    cv.Box(cx - 3 - shift, y0, 6, 6, DarkGrey);
                    int sx = x0 + bw / 2, sy = y0 + bh + 3;
                    cv.Box(sx - 5, sy, 10, 1, DarkGrey);
                    cv.Box(sx - 6, sy - 2, 2, 5, DarkGrey);
                    cv.Box(sx + 4, sy - 2, 2, 5, DarkGrey);
                    if (branch == 'a') for (int k = 0; k < 3; k++) cv.Box(x0 + bw + 2, y0 + k * 4, 6, 2, DarkGrey);
                    if (branch == 'b') cv.Ellipse(x0 + bw + 12, cy - 4, 11, 8, new Color32(120, 200, 240, 170), true);
                    break;
                }
            }
        }
    }
}
