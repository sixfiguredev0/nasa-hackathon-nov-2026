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
        public Vector2 sunPos = new Vector2(-300f, 250f);
        public Vector2 earthPos = new Vector2(330f, 330f);
        public float phobosHeight = 150f;
        public Vector2 deimosPos = new Vector2(120f, 380f);

        [Header("Motion")]
        [Tooltip("0 = fixed in the world, 1 = glued to the camera")]
        [Range(0f, 1f)] public float spaceFollow = 0.85f;
        [Range(0f, 1f)] public float hillsFollow = 0.4f;
        [Tooltip("Phobos crosses the sky from west (left) to east (right), seconds per crossing")]
        public float phobosCrossSeconds = 90f;
        public Vector2 shootingStarInterval = new Vector2(5f, 12f);

        public static readonly Color32 SpaceColor = new Color32(0x16, 0x0c, 0x2a, 0xff);
        static readonly Color32[] Glow = Hex("ee9a62", "d2706a", "a05278", "68386e", "3c2254", "26163f");
        static readonly Color32 FarHill = Hex("a24d63")[0], FarRim = Hex("c8687a")[0];
        static readonly Color32 NearHill = Hex("86384a")[0], NearRim = Hex("ad4c55")[0];
        static readonly Color32 Plain = Hex("c24e36")[0], PlainRim = Hex("dd6f3b")[0], Ink = Hex("2a1028")[0];
        static readonly Color32[] StarCols = Hex("3d2f63", "5e4f8c", "8f84bf", "c9c2ee", "ffe9b8", "ffffff");
        static readonly Color32[] Milky = Hex("1d1135", "251742");

        class Twinkle { public SpriteRenderer sr; public float phase, speed; public Color32[] cols; }

        readonly List<Object> owned = new List<Object>();
        readonly List<Twinkle> twinkles = new List<Twinkle>();
        Transform root, spaceLayer, hillsLayer;
        SpriteRenderer sun, phobosSr, shooting;
        float horizonY, worldLeft, worldRight, nextShoot, shootT = -1f;
        Vector3 shootFrom, shootDir;
        Camera cam;
        Vector3 camAnchor;
        SeededRandom rng;
        float phobosPhase;

        public float HorizonY => horizonY;
        public float SpaceTopY => horizonY + spaceHeight / Ppu;

        public void Build(int seed, Rect groundWorldRect)
        {
            Teardown();
            cam = Camera.main;
            rng = new SeededRandom(seed ^ 0x5CA1);
            horizonY = Mathf.Round((groundWorldRect.yMax - 20f / Ppu) * Ppu) / Ppu;
            worldLeft = groundWorldRect.xMin;
            worldRight = groundWorldRect.xMax;
            camAnchor = new Vector3(groundWorldRect.center.x, 0f, 0f);

            root = new GameObject("Sky") { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(transform, false);
            spaceLayer = Child("Space", root);
            hillsLayer = Child("Horizon", root);

            int widthPx = Mathf.CeilToInt(groundWorldRect.width * Ppu) + 1400;
            BuildStars(widthPx);
            BuildHorizon(widthPx);
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
            int stars = StarTileW * h / 140;
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
            int count = widthPx / 30;
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

        // ---------- horizon: atmosphere glow, far and near hills, distant plain ----------
        void BuildHorizon(int widthPx)
        {
            const int below = 16, above = 130;
            int h = below + above;
            var px = new Color32[widthPx * h];
            var n = new ValueNoise(rng.Range(1, 99999));
            var peaks = new List<Vector3>();
            for (int k = 0; k < widthPx / 500; k++) peaks.Add(new Vector3(rng.Range(0f, widthPx), rng.Range(60f, 180f), rng.Range(14f, 34f)));
            int[] glowRows = { 16, 8, 10, 14, 20, 28 };
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
                for (int y = 0; y < below; y++) px[y * widthPx + x] = y == below - 1 ? PlainRim : Plain;
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
            Make("Hills", hillsLayer, NewSprite(tex, new Vector2(0.5f, below / (float)h)), -900, Vector3.zero); // drawn over the ground's far edge
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

            float t = Now;
            foreach (var tw in twinkles)
            {
                int k = Mathf.FloorToInt(t * tw.speed + tw.phase) % tw.cols.Length;
                tw.sr.color = tw.cols[k];
            }
            if (sun != null && sunFrames.Length > 1) sun.sprite = sunFrames[Mathf.FloorToInt(t * 3f) % sunFrames.Length];
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
            if (shooting == null) return;
            if (shootT < 0f && t >= nextShoot)
            {
                shootT = 0f;
                float w = (worldRight - worldLeft) * Ppu;
                shootFrom = new Vector3(rng.Range(-w * 0.4f, w * 0.4f), rng.Range(spaceHeight * 0.5f, spaceHeight * 0.9f), 0f);
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
