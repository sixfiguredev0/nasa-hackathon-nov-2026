using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Mars
{
    /// <summary>
    /// Space above the Mars horizon: thin atmosphere glow, distant hills, a tiled star field with the Milky Way,
    /// twinkling stars, the Sun, Earth, Phobos, Deimos and the odd shooting star.
    /// Sky layers follow the camera horizontally with parallax so space never runs out.
    /// </summary>
    [ExecuteAlways]
    public class MarsSky : MonoBehaviour
    {
        const float Ppu = 16f;
        const int StarTileW = 512;

        [Header("Art")]
        public Sprite[] sunFrames;
        public Sprite earth;
        public Sprite phobos;
        public Sprite deimos;
        public Sprite shootingStar;

        [Header("Layout (pixels above the horizon)")]
        public int spaceHeight = 480;
        // kept low so they show in the starting view (about 160 px of sky above the horizon)
        public Vector2 sunPos = new Vector2(-300f, 105f);
        public Vector2 earthPos = new Vector2(330f, 125f);
        public float phobosHeight = 70f;
        public Vector2 deimosPos = new Vector2(120f, 140f);

        [Header("Motion")]
        [Tooltip("0 = fixed in the world, 1 = glued to the camera")]
        [Range(0f, 1f)] public float spaceFollow = 0.85f;
        [Range(0f, 1f)] public float hillsFollow = 0.4f;
        [Tooltip("Phobos crosses the sky from west (left) to east (right), seconds per crossing")]
        public float phobosCrossSeconds = 90f;
        public Vector2 shootingStarInterval = new Vector2(5f, 12f);

        [Header("Horizon shape")]
        [Tooltip("Pixels the horizon drops at the screen edges (planet curvature)")]
        public float horizonCurve = 22f;
        [Tooltip("Height in pixels of the receding far-ground band between the playing field and the hills")]
        public int farGroundHeight = 56;

        public static readonly Color32 SpaceColor = new Color32(0x16, 0x0c, 0x2a, 0xff);
        static readonly Color32[] Glow = Hex("ee9a62", "d2706a", "a05278", "68386e", "3c2254", "26163f");
        static readonly Color32 FarHill = Hex("a24d63")[0], FarRim = Hex("c8687a")[0];
        static readonly Color32 NearHill = Hex("86384a")[0], NearRim = Hex("ad4c55")[0];
        static readonly Color32 Ink = Hex("2a1028")[0];
        // terrain palette (MarsTerrain Dust/Sand) for the far ground, and the haze it fades into
        // rows: haze level 0 (near) .. 3 (at the hills); columns: basalt, dark dust, mid dust, light dust
        static readonly Color32[][] FarRamp =
        {
            Hex("702b45", "c24e36", "dd6f3b", "ee9447"),
            Hex("74304b", "c24e36", "d2643f", "dd6f3b"),
            Hex("86384a", "b44e48", "c24e36", "c8687a"),
            Hex("8f4058", "a24d63", "a24d63", "b85a6c"),
        };
        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        // a textured strip cut into 1 px columns so each column can drop to bend the horizon
        // drop[] holds the drop (px) currently applied to each column so only columns whose drop changes are rewritten
        class Strip { public Transform t; public Mesh mesh; public Vector3[] baseV, v; public float[] drop; public float pivotPx; public int cols; }
        readonly List<Strip> strips = new List<Strip>();
        int curveKey = int.MinValue;
        float curveHalfW = float.NaN, curveSource = float.NaN, curveDepth; // invariants of the arc, recomputed only when the view width changes
        static readonly Color32[] StarCols = Hex("4c3d7a", "7262a6", "9f95cf", "d6d0f5", "ffe9b8", "ffffff");
        static readonly Color32[] Milky = Hex("26173f", "321f55");

        class Twinkle { public SpriteRenderer sr; public float phase, speed; public Color32[] cols; }

        readonly List<Object> owned = new List<Object>();
        readonly List<Twinkle> twinkles = new List<Twinkle>();
        Transform root, spaceLayer, hillsLayer, farLayer;
        SpriteRenderer sun, phobosSr, shooting;
        float horizonY, worldLeft, worldRight, nextShoot, shootT = -1f;
        Rect groundRect;
        /// <summary>The baked ground (set by MarsMapGenerator before Build) so the far ground can continue its colours.</summary>
        [System.NonSerialized] public Texture2D groundTexture;
        Vector3 shootFrom, shootDir;
        Camera cam;
        Vector3 camAnchor;
        SeededRandom rng;
        float phobosPhase;

        /// <summary>Set by the outpost's sol clock: 0 = sunrise, 1 = sunset, outside 0..1 = night (Sun hidden). NaN (default) = fixed Sun at <see cref="sunPos"/>.</summary>
        [System.NonSerialized] public float sunDayFraction = float.NaN;

        public float HorizonY => horizonY;
        public float SpaceTopY => horizonY + spaceHeight / Ppu;

        public void Build(int seed, Rect groundWorldRect)
        {
            Teardown();
            cam = Camera.main;
            rng = new SeededRandom(seed ^ 0x5CA1);
            horizonY = Mathf.Round((groundWorldRect.yMax - 20f / Ppu) * Ppu) / Ppu;
            groundRect = groundWorldRect;
            worldLeft = groundWorldRect.xMin;
            worldRight = groundWorldRect.xMax;
            camAnchor = new Vector3(groundWorldRect.center.x, 0f, 0f);

            root = new GameObject("Sky") { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(transform, false);
            spaceLayer = Child("Space", root);
            hillsLayer = Child("Horizon", root);
            farLayer = Child("Far Ground", root);

            int widthPx = Mathf.CeilToInt(groundWorldRect.width * Ppu) + 1400;
            curveKey = int.MinValue;
            BuildStars(widthPx);
            BuildHorizon(widthPx);
            BuildFarGround(widthPx);
            BuildBodies();
            nextShoot = Now + rng.Range(1f, 4f);
            phobosPhase = 0.35f + rng.Value() * 0.3f; // start somewhere near the middle of its pass
            LateUpdate();
        }

        static float Now => Application.isPlaying ? Time.time : (float)(System.DateTime.Now.TimeOfDay.TotalSeconds % 100000);

        Transform Child(string n, Transform parent)
        {
            var t = new GameObject(n) { hideFlags = HideFlags.DontSave }.transform;
            t.SetParent(parent, false);
            return t;
        }

        SpriteRenderer Make(string n, Transform parent, Sprite sp, int order, Vector3 localPx)
        {
            var t = Child(n, parent);
            t.localPosition = localPx / Ppu;
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = order;
            return sr;
        }

        Texture2D NewTex(int w, int h, string n)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = n, hideFlags = HideFlags.DontSave };
            owned.Add(t);
            return t;
        }

        Sprite NewSprite(Texture2D t, Vector2 pivot)
        {
            var s = UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, Ppu, 0, SpriteMeshType.FullRect);
            s.hideFlags = HideFlags.DontSave;
            owned.Add(s);
            return s;
        }

        // ---------- star field ----------
        void BuildStars(int widthPx)
        {
            int h = spaceHeight + 160;
            var px = new Color32[StarTileW * h];
            var nA = new ValueNoise(rng.Range(1, 99999));
            // Milky Way: a soft band that wraps seamlessly every StarTileW pixels
            for (int y = 0; y < h; y++)
                for (int x = 0; x < StarTileW; x++)
                {
                    float band = h * 0.62f + Mathf.Sin(x * 2f * Mathf.PI / StarTileW) * 46f;
                    float d = Mathf.Abs(y - band) / 52f;
                    if (d > 1f) continue;
                    float t = x / (float)StarTileW;
                    float n = Mathf.Lerp(nA.Fbm(x * 0.03f, y * 0.05f, 3), nA.Fbm((x - StarTileW) * 0.03f, y * 0.05f, 3), t);
                    float v = n * (1f - d * d);
                    bool checker = ((x + y) & 1) == 0;
                    if (v > 0.42f || v > 0.36f && checker) px[y * StarTileW + x] = Milky[v > 0.5f || v > 0.46f && checker ? 1 : 0];
                }
            int stars = StarTileW * h / 100;
            for (int k = 0; k < stars; k++)
            {
                int x = rng.Range(0, StarTileW), y = rng.Range(24, h);
                float band = h * 0.62f + Mathf.Sin(x * 2f * Mathf.PI / StarTileW) * 46f;
                bool inBand = Mathf.Abs(y - band) < 40f;
                if (!inBand && rng.Chance(0.45f)) continue;
                float fade = Mathf.Clamp01((y - 24f) / 90f); // the thin air near the horizon hides faint stars
                float r = rng.Value() * fade;
                int c = r > 0.97f ? 4 : r > 0.9f ? 3 : r > 0.65f ? 2 : r > 0.3f ? 1 : 0;
                if (r < 0.05f) continue;
                px[y * StarTileW + x] = StarCols[c];
            }
            var tex = NewTex(StarTileW, h, "MarsStars");
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.SetPixels32(px);
            tex.Apply(false, false);
            var sr = Make("Stars", spaceLayer, NewSprite(tex, new Vector2(0.5f, 0f)), -3000, Vector3.zero);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2((widthPx + StarTileW * 2) / Ppu, h / Ppu);

            // twinkling stars on top of the static field
            var big = PlusStar(false);
            var small = PlusStar(true);
            int count = widthPx / 18;
            for (int k = 0; k < count; k++)
            {
                float x = rng.Range(-widthPx * 0.5f, widthPx * 0.5f), y = rng.Range(70f, spaceHeight + 100f);
                bool plus = rng.Chance(0.25f);
                var s = Make("Twinkle", spaceLayer, plus ? big : small, -2990, new Vector3(Mathf.Round(x), Mathf.Round(y), 0f));
                twinkles.Add(new Twinkle
                {
                    sr = s, phase = rng.Range(0f, 10f), speed = rng.Range(0.6f, 2.2f),
                    cols = plus ? new[] { StarCols[3], StarCols[5], StarCols[4], StarCols[5] } : new[] { StarCols[1], StarCols[3], StarCols[2], StarCols[4] }
                });
            }
        }

        Sprite PlusStar(bool dot)
        {
            int n = dot ? 1 : 3;
            var t = NewTex(n, n, "Star");
            var c = new Color32[n * n];
            var w = new Color32(255, 255, 255, 255);
            if (dot) c[0] = w; else { c[1] = c[3] = c[4] = c[5] = c[7] = w; c[3].a = c[5].a = c[1].a = c[7].a = 170; }
            t.SetPixels32(c);
            t.Apply(false, false);
            return NewSprite(t, new Vector2(0.5f, 0.5f));
        }

        // ---------- horizon: atmosphere glow, far and near hills ----------
        void BuildHorizon(int widthPx)
        {
            const int below = 2, above = 130;
            int h = below + above;
            var px = new Color32[widthPx * h];
            var n = new ValueNoise(rng.Range(1, 99999));
            var peaks = new List<Vector3>();
            for (int k = 0; k < widthPx / 500; k++) peaks.Add(new Vector3(rng.Range(0f, widthPx), rng.Range(60f, 180f), rng.Range(14f, 34f)));
            int[] glowRows = { 10, 5, 6, 8, 10, 14 }; // thin: Mars air is ~1% of Earth's, and it leaves more stars on screen
            for (int x = 0; x < widthPx; x++)
            {
                // glow bands with one checker row between them
                int y0 = below;
                for (int b = 0; b < Glow.Length; b++)
                {
                    for (int r = 0; r < glowRows[b]; r++)
                    {
                        int y = y0 + r;
                        bool edge = r == glowRows[b] - 1 && ((x + y) & 1) == 0;
                        px[y * widthPx + x] = edge && b + 1 < Glow.Length ? Glow[b + 1] : Glow[b];
                    }
                    y0 += glowRows[b];
                }
                for (int y = y0; y < h; y++) if (((x + y) & 1) == 0 && y < y0 + 2) px[y * widthPx + x] = Glow[Glow.Length - 1];

                float peak = 0f;
                foreach (var pk in peaks) { float d = (x - pk.x) / pk.y; peak = Mathf.Max(peak, pk.z * Mathf.Max(0f, 1f - Mathf.Abs(d)) * (1f - 0.25f * d * d)); }
                int far = below + Mathf.RoundToInt(4f + 14f * Mathf.Pow(n.Fbm(x * 0.006f, 1.5f, 4), 2f) * 2f + peak + 1.5f * n.Sample(x * 0.08f, 9f));
                // near ridges come in flat-topped steps, like small mesas
                int near = below + 3 * Mathf.FloorToInt((1f + 14f * Mathf.Pow(n.Fbm(x * 0.01f + 40f, 3.5f, 3), 2.2f) * 2f) / 3f);
                for (int y = 0; y < far && y < h; y++) px[y * widthPx + x] = y >= far - 1 ? FarRim : FarHill;
                for (int y = 0; y < near && y < h; y++) px[y * widthPx + x] = y >= near - 1 ? NearRim : NearHill;
            }
            // outline the hill tops so they read as shapes
            var copy = (Color32[])px.Clone();
            for (int y = below; y < h - 1; y++)
                for (int x = 0; x < widthPx; x++)
                {
                    var c = copy[y * widthPx + x];
                    var up = copy[(y + 1) * widthPx + x];
                    bool hill = Same(c, FarRim) || Same(c, NearRim);
                    if (hill && !Same(up, FarHill) && !Same(up, NearHill) && !Same(up, FarRim) && !Same(up, NearRim)) px[(y + 1) * widthPx + x] = Ink;
                }
            var tex = NewTex(widthPx, h, "MarsHorizon");
            tex.SetPixels32(px);
            tex.Apply(false, false);
            MakeStrip("Hills", hillsLayer, tex, below, -900); // its opaque glow hides the ground's far edge
        }

        // ---------- far ground: the plain keeps going, rows shrinking exponentially into the haze ----------
        void BuildFarGround(int widthPx)
        {
            int h = Mathf.Max(16, farGroundHeight), fade = 8;
            var px = new Color32[widthPx * h];
            var n = new ValueNoise(rng.Range(1, 99999));
            // ground darkness along the band's bottom edge, so dark dunes carry on into the distance instead of meeting a seam
            var dark = new float[widthPx];
            if (groundTexture != null && groundTexture.isReadable)
            {
                int gy = Mathf.Clamp(Mathf.RoundToInt((horizonY - groundRect.yMin) * Ppu) - h + fade, 0, groundTexture.height - 1);
                int gx0 = Mathf.RoundToInt((camAnchor.x - groundRect.xMin) * Ppu) - widthPx / 2;
                for (int x = 0; x < widthPx; x++)
                {
                    int gx = gx0 + x;
                    if (gx < 0 || gx >= groundTexture.width) continue;
                    Color c = groundTexture.GetPixel(gx, gy);
                    dark[x] = c.a > 0f && c.grayscale < 0.3f ? 1f : 0f;
                }
                // box blur so single rocks and ripple lines don't leave streaks; only wide dark patches count
                const int r = 20;
                var sum = new float[widthPx + 1];
                for (int x = 0; x < widthPx; x++) sum[x + 1] = sum[x] + dark[x];
                for (int x = 0; x < widthPx; x++)
                {
                    int a = Mathf.Max(0, x - r), b = Mathf.Min(widthPx, x + r + 1);
                    dark[x] = Mathf.Clamp01(((sum[b] - sum[a]) / (b - a) - 0.4f) * 2.5f);
                }
            }
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)h;              // 0 = joins the playing field, 1 = horizon
                float z = 1f / (1f - 0.9f * t);      // distance: 1 .. 10, so far rows pack more ground each
                // haze comes in flat steps with one checker row between them, like the glow bands
                int level = Mathf.Min(3, Mathf.FloorToInt(Mathf.Pow(t, 1.3f) * 4f));
                bool stepRow = level < 3 && Mathf.FloorToInt(Mathf.Pow((y + 1f) / h, 1.3f) * 4f) > level;
                for (int x = 0; x < widthPx; x++)
                {
                    if (y < fade && Bayer[(y & 3) * 4 + (x & 3)] >= 16 * (y + 1) / (fade + 1)) continue; // dither into the field below
                    // features shrink with distance: wide near, thin streaks at the horizon
                    float u = x * 0.0045f * z, v = z * 0.55f;
                    float m = n.Fbm(u, v, 3) + (dark[x] - 0.35f) * 0.5f * Mathf.Clamp01(1f - t * 2.5f);
                    int tone = m > 0.62f ? 0 : m > 0.5f ? 1 : m > 0.36f ? 2 : 3;
                    int lv = stepRow && ((x + y) & 1) == 0 ? level + 1 : level;
                    px[y * widthPx + x] = FarRamp[lv][tone];
                }
            }
            var tex = NewTex(widthPx, h, "MarsFarGround");
            tex.SetPixels32(px);
            tex.Apply(false, false);
            MakeStrip("Far Ground", farLayer, tex, h, -950);
        }

        void MakeStrip(string name, Transform parent, Texture2D tex, int pivotYPx, int order)
        {
            int w = tex.width, h = tex.height;
            var go = Child(name, parent).gameObject;
            var verts = new Vector3[w * 4];
            var uvs = new Vector2[w * 4];
            var tris = new int[w * 6];
            float y0 = -pivotYPx / Ppu, y1 = (h - pivotYPx) / Ppu, half = w * 0.5f;
            for (int c = 0; c < w; c++)
            {
                float x0 = (c - half) / Ppu, x1 = (c + 1 - half) / Ppu, u0 = c / (float)w, u1 = (c + 1) / (float)w;
                int k = c * 4;
                verts[k] = new Vector3(x0, y0); verts[k + 1] = new Vector3(x0, y1); verts[k + 2] = new Vector3(x1, y1); verts[k + 3] = new Vector3(x1, y0);
                uvs[k] = new Vector2(u0, 0f); uvs[k + 1] = new Vector2(u0, 1f); uvs[k + 2] = new Vector2(u1, 1f); uvs[k + 3] = new Vector2(u1, 0f);
                int i = c * 6;
                tris[i] = k; tris[i + 1] = k + 1; tris[i + 2] = k + 2; tris[i + 3] = k; tris[i + 4] = k + 2; tris[i + 5] = k + 3;
            }
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, vertices = verts, uv = uvs, triangles = tris };
            mesh.MarkDynamic();
            mesh.bounds = new Bounds(new Vector3(0f, (y0 + y1) * 0.5f), new Vector3(w / Ppu, (h + 128) / Ppu * 2f, 1f));
            owned.Add(mesh);
            var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, hideFlags = HideFlags.DontSave };
            owned.Add(mat);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = order;
            strips.Add(new Strip { t = go.transform, mesh = mesh, baseV = verts, v = (Vector3[])verts.Clone(), drop = new float[w], pivotPx = half, cols = w });
        }

        // drop each column by the planet's curvature, measured from the camera so the arc stays centred on screen
        void BendHorizon(float camX)
        {
            float halfW = cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect * Ppu : 480f;
            int key = Mathf.RoundToInt(camX * Ppu) * 4099 + Mathf.RoundToInt(halfW);
            if (key == curveKey) return;
            curveKey = key;
            // shortcut: depth scales down when zoomed in so close views don't exaggerate it; capped so the hills' glow still hides the ground's edge
            if (halfW != curveHalfW || horizonCurve != curveSource)
            {
                curveHalfW = halfW;
                curveSource = horizonCurve;
                curveDepth = horizonCurve * Mathf.Clamp(halfW / 400f, 0.35f, 1f);
            }
            float depth = curveDepth;
            for (int si = 0; si < strips.Count; si++)
            {
                var s = strips[si];
                float leftPx = s.t.position.x * Ppu - s.pivotPx - camX * Ppu;
                bool dirty = false;
                for (int c = 0; c < s.cols; c++)
                {
                    float d = (leftPx + c + 0.5f) / halfW;
                    float dropPx = Mathf.Min(30f, Mathf.Round(depth * d * d));
                    // the drop is a whole-pixel step function: a one-pixel pan changes only the few columns at step edges
                    if (dropPx == s.drop[c]) continue;
                    s.drop[c] = dropPx;
                    float drop = dropPx / Ppu;
                    int k = c * 4;
                    for (int j = 0; j < 4; j++) s.v[k + j].y = s.baseV[k + j].y - drop;
                    dirty = true;
                }
                if (dirty) s.mesh.SetVertices(s.v); // reuses the cached array; skipped entirely when no column moved
            }
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        // ---------- Sun, Earth, moons ----------
        void BuildBodies()
        {
            if (sunFrames != null && sunFrames.Length > 0 && sunFrames[0] != null)
                sun = Make("Sun", spaceLayer, sunFrames[0], -2950, sunPos);
            if (earth != null)
            {
                Make("Earth", spaceLayer, earth, -2950, earthPos);
            }
            if (deimos != null) Make("Deimos", spaceLayer, deimos, -2950, deimosPos);
            if (phobos != null) phobosSr = Make("Phobos", spaceLayer, phobos, -2940, new Vector3(0f, phobosHeight, 0f));
            if (shootingStar != null)
            {
                shooting = Make("Shooting Star", spaceLayer, shootingStar, -2945, Vector3.zero);
                shooting.enabled = false;
            }
        }

        void LateUpdate()
        {
            if (root == null) return;
            if (cam == null) cam = Camera.main;
            float camX = cam != null ? cam.transform.position.x : camAnchor.x;
            float dx = camX - camAnchor.x;
            spaceLayer.position = Snap(new Vector3(camAnchor.x + dx * spaceFollow, horizonY, 0f));
            hillsLayer.position = Snap(new Vector3(camAnchor.x + dx * hillsFollow, horizonY, 0f));
            farLayer.position = Snap(new Vector3(camAnchor.x, horizonY, 0f)); // fixed in the world, like the field it continues
            BendHorizon(camX);

            float t = Now;
            foreach (var tw in twinkles)
            {
                int k = Mathf.FloorToInt(t * tw.speed + tw.phase) % tw.cols.Length;
                tw.sr.color = tw.cols[k];
            }
            if (sun != null && sunFrames.Length > 1) sun.sprite = sunFrames[Mathf.FloorToInt(t * 3f) % sunFrames.Length];
            if (sun != null && !float.IsNaN(sunDayFraction))
            {
                // East (left) to west across the sky; just under the horizon line at sunrise and sunset.
                float f = sunDayFraction;
                sun.enabled = f > 0f && f < 1f;
                float lift = Mathf.Sin(Mathf.Clamp01(f) * Mathf.PI);
                sun.transform.localPosition = Snap(new Vector3(sunPos.x + (f - 0.5f) * 640f, -12f + (sunPos.y + 40f) * lift, 0f) / Ppu);
            }
            if (phobosSr != null)
            {
                float span = (worldRight - worldLeft) * Ppu + 600f;
                float f = (t / Mathf.Max(5f, phobosCrossSeconds) + phobosPhase) % 1f;
                float x = -span * 0.5f + f * span;
                float arc = phobosHeight + Mathf.Sin(f * Mathf.PI) * 60f;
                phobosSr.transform.localPosition = Snap(new Vector3(x, arc, 0f) / Ppu);
            }
            UpdateShootingStar(t);
        }

        void UpdateShootingStar(float t)
        {
            if (shooting == null || rng == null) return; // rng is lost on domain reload until the next Build
            if (shootT < 0f && t >= nextShoot)
            {
                shootT = 0f;
                float w = (worldRight - worldLeft) * Ppu;
                shootFrom = new Vector3(rng.Range(-w * 0.4f, w * 0.4f), rng.Range(90f, spaceHeight * 0.5f), 0f);
                shootDir = new Vector3(-1f, -0.5f, 0f).normalized;
                shooting.flipX = false;
            }
            if (shootT >= 0f)
            {
                shootT += Application.isPlaying ? Time.deltaTime : 0.02f;
                float life = 0.9f;
                var p = shootFrom + shootDir * (shootT * 260f);
                shooting.transform.localPosition = Snap(p / Ppu);
                shooting.enabled = shootT < life;
                if (shootT >= life)
                {
                    shootT = -1f;
                    shooting.enabled = false;
                    nextShoot = t + rng.Range(shootingStarInterval.x, shootingStarInterval.y);
                }
            }
        }

        static Vector3 Snap(Vector3 v) => new Vector3(Mathf.Round(v.x * Ppu) / Ppu, Mathf.Round(v.y * Ppu) / Ppu, v.z);

        void Teardown()
        {
            twinkles.Clear();
            strips.Clear();
            foreach (var o in owned) if (o != null) Kill(o);
            owned.Clear();
            for (int k = transform.childCount - 1; k >= 0; k--)
                if (transform.GetChild(k).name == "Sky") Kill(transform.GetChild(k).gameObject);
            root = null;
        }

        void OnDestroy() => Teardown();

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        static Color32[] Hex(params string[] h)
        {
            var c = new Color32[h.Length];
            for (int i = 0; i < h.Length; i++)
            {
                ColorUtility.TryParseHtmlString("#" + h[i], out Color col);
                c[i] = col;
            }
            return c;
        }
    }
}
