using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Mars
{
    public struct Crater { public float i, j, r, depth, rim, age; }
    public struct Rock { public float i, j, r, h, a1, p1, a2, p2; }

    /// <summary>
    /// Builds the Mars ground as one baked pixel-art texture.
    /// Ground model (heights, sand, rocks, craters) lives in "ground pixels": x = lattice i*24 + j*12, y = j*12.
    /// Rendering is oblique: a ground point with height h is drawn h pixels higher, front-facing slopes become cliff faces.
    /// </summary>
    public class MarsTerrain
    {
        public const int CellW = 24, CellH = 12;
        const int Coarse = 4, Margin = 48, TopMargin = 64, ExtraRows = 48;

        // Mars surface ramps (dark to light): butterscotch regolith and dark basaltic sand / rock.
        // Small hue-shifted pixel-art palette: shadows lean purple, highlights lean yellow.
        static readonly Color32[] Dust = Pal("2b1029", "4a1734", "74233a", "9e3438", "c24e36", "dd6f3b", "ee9447", "f7b961", "fde093");
        static readonly Color32[] Sand = Pal("3a1838", "54203f", "702b45", "8c3b4b");
        static readonly Color32[] Rock = Pal("25142b", "3f1f3a", "5f2d45", "834150", "a85a5a", "cb7d68");
        static readonly Color32 Outline = Pal("2a1028")[0];

        public readonly int W, H, TexW, TexH, OriginX, OriginY;
        public readonly List<Crater> Craters = new List<Crater>();
        public readonly List<Rock> Rocks = new List<Rock>();
        public float MaxHeight { get; private set; }

        readonly MarsMapSettings s;
        readonly MapPlacementRules rules;
        readonly int rows;
        readonly float mesaT0;
        readonly ValueNoise nRoll, nMesa, nDetail, nAlb, nSand, nWarp, nCluster;
        readonly float[] grain;
        readonly List<int>[] craterBuckets, rockBuckets;
        float[] height, sand, ejecta, mesaMask;
        bool[] rock;

        public MarsTerrain(MarsMapSettings settings, MapPlacementRules placement, int seed, Texture2D grainTex)
        {
            s = settings;
            rules = placement;
            W = s.width;
            H = s.height;
            OriginX = Margin;
            OriginY = Margin;
            TexW = W * CellW + H * CellH + 2 * Margin;
            TexH = H * CellH + Margin + TopMargin;
            rows = TexH + ExtraRows;
            mesaT0 = 0.7f - 0.1f * Mathf.Clamp01(s.mesaAmount);
            nRoll = new ValueNoise(seed ^ 0x51ED);
            nMesa = new ValueNoise(seed ^ 0x3E5A);
            nDetail = new ValueNoise(seed ^ 0x0DE7);
            nAlb = new ValueNoise(seed ^ 0x0A1B);
            nSand = new ValueNoise(seed ^ 0x5A4D);
            nWarp = new ValueNoise(seed ^ 0x7A2F);
            nCluster = new ValueNoise(seed ^ 0x6B0C);
            grain = ReadGrain(grainTex);

            int bw = W + 2 * BucketPad, bh = H + 2 * BucketPad;
            craterBuckets = new List<int>[bw * bh];
            rockBuckets = new List<int>[bw * bh];
            var rng = new SeededRandom(seed ^ 0x2C47);
            PlaceCraters(rng);
            PlaceRocks(rng);
        }

        // ---------- lattice helpers ----------
        public void GroundToLattice(float gx, float gy, out float i, out float j)
        {
            j = (gy - OriginY) / CellH;
            i = (gx - OriginX - (gy - OriginY)) / CellW;
        }

        public Vector2 LatticeToGround(float i, float j) => new Vector2(OriginX + i * CellW + j * CellH, OriginY + j * CellH);

        float Flatten(float i, float j)
        {
            if (rules.zones.Count == 0) return 1f;
            var b = rules.OutpostBounds;
            float dx = Mathf.Max(0f, Mathf.Max(b.xMin - i, i - b.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(b.yMin - j, j - b.yMax));
            return MarsMath.Smooth(1.5f, 6f, Mathf.Sqrt(dx * dx + dy * dy));
        }

        float MesaField(float i, float j) => nMesa.Fbm(i * 0.028f + 11f, j * 0.045f - 5f, 4)
            + (nDetail.Fbm(i * 0.4f, j * 0.6f, 2) - 0.5f) * 0.05f - (1f - Flatten(i, j)) * 0.5f;

        bool OnMesaOrEdge(float i, float j, float pad) => MesaField(i, j) > mesaT0 - pad;

        // ---------- feature placement ----------
        const int BucketPad = 6;

        void AddToBuckets(List<int>[] buckets, int id, float i, float j, float reach)
        {
            int bw = W + 2 * BucketPad;
            int i0 = Mathf.FloorToInt(i - reach), i1 = Mathf.FloorToInt(i + reach);
            int j0 = Mathf.FloorToInt(j - reach), j1 = Mathf.FloorToInt(j + reach);
            for (int y = j0; y <= j1; y++)
                for (int x = i0; x <= i1; x++)
                {
                    int bx = x + BucketPad, by = y + BucketPad;
                    if (bx < 0 || by < 0 || bx >= bw || by >= H + 2 * BucketPad) continue;
                    int k = by * bw + bx;
                    (buckets[k] ?? (buckets[k] = new List<int>())).Add(id);
                }
        }

        List<int> Bucket(List<int>[] buckets, float i, float j)
        {
            int bx = Mathf.FloorToInt(i) + BucketPad, by = Mathf.FloorToInt(j) + BucketPad;
            if (bx < 0 || by < 0 || bx >= W + 2 * BucketPad || by >= H + 2 * BucketPad) return null;
            return buckets[by * (W + 2 * BucketPad) + bx];
        }

        static int Count(float perHundredCells, int w, int h) => Mathf.Max(0, Mathf.RoundToInt(perHundredCells * w * h / 100f));

        void PlaceCraters(SeededRandom rng)
        {
            int target = Count(s.craterDensity, W + 6, H + 6);
            for (int tries = 0; tries < target * 30 && Craters.Count < target; tries++)
            {
                float u = rng.Value();
                float r = Mathf.Min(s.maxCraterRadius, 0.45f * Mathf.Pow(1f / (0.02f + 0.98f * u), 0.72f));
                float ci = rng.Range(-3f, W + 3f), cj = rng.Range(-3f, H + 3f);
                if (Flatten(ci, cj) < 1f) continue;
                if (DistToOutpost(ci, cj) < r * 1.8f + 2f) continue;
                bool clash = false;
                foreach (var c in Craters)
                {
                    float d = Mathf.Sqrt((c.i - ci) * (c.i - ci) + (c.j - cj) * (c.j - cj));
                    if (d < (c.r + r) * 0.55f) { clash = true; break; }
                }
                if (clash) continue;
                float age = rng.Value();
                float depth = Mathf.Min(r * CellW * 0.13f, 16f) * (1f - 0.55f * age);
                var cr = new Crater { i = ci, j = cj, r = r, depth = depth, rim = depth * 0.42f * (1f - 0.6f * age), age = age };
                AddToBuckets(craterBuckets, Craters.Count, ci, cj, r * 3f);
                Craters.Add(cr);
            }
        }

        float DistToOutpost(float i, float j)
        {
            if (rules.zones.Count == 0) return 999f;
            var b = rules.OutpostBounds;
            float dx = Mathf.Max(0f, Mathf.Max(b.xMin - i, i - b.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(b.yMin - j, j - b.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        void AddRock(SeededRandom rng, float i, float j, float r, float hScale)
        {
            if (DistToOutpost(i, j) < rules.outpostClearance + 1f + r) return;
            var rk = new Rock
            {
                i = i, j = j, r = r, h = Mathf.Max(1f, r * CellW * 0.5f * hScale),
                a1 = rng.Range(0f, 0.28f), p1 = rng.Range(0f, 6.283f), a2 = rng.Range(0f, 0.18f), p2 = rng.Range(0f, 6.283f)
            };
            AddToBuckets(rockBuckets, Rocks.Count, i, j, r * 1.5f);
            Rocks.Add(rk);
        }

        void PlaceRocks(SeededRandom rng)
        {
            // ejecta blocks around young craters
            foreach (var c in Craters)
            {
                if (c.r < 1.1f || c.age > 0.65f) continue;
                int n = Mathf.RoundToInt(c.r * 2.5f * s.rockDensity / 6f);
                for (int k = 0; k < n; k++)
                {
                    float a = rng.Range(0f, 6.283f), t = 1f + Mathf.Pow(rng.Value(), 1.6f) * 1.3f;
                    AddRock(rng, c.i + Mathf.Cos(a) * c.r * t, c.j + Mathf.Sin(a) * c.r * t, 0.13f + Mathf.Pow(rng.Value(), 3f) * 0.2f, 1f);
                }
            }
            // boulder fields, clustered by noise
            int field = Count(s.rockDensity, W + 6, H + 6);
            for (int k = 0, placed = 0; k < field * 8 && placed < field; k++)
            {
                float i = rng.Range(-3f, W + 3f), j = rng.Range(-3f, H + 3f);
                if (nCluster.Fbm(i * 0.12f, j * 0.12f, 3) < 0.35f + rng.Value() * 0.35f) continue;
                if (OnMesaOrEdge(i, j, 0.0f)) continue;
                AddRock(rng, i, j, 0.16f + Mathf.Pow(rng.Value(), 2.5f) * 0.4f, 1f);
                placed++;
            }
            // talus at mesa feet
            if (s.mesaAmount > 0f)
                for (int k = 0; k < W * H / 6; k++)
                {
                    float i = rng.Range(-3f, W + 3f), j = rng.Range(-3f, H + 3f);
                    float m = MesaField(i, j);
                    if (m > mesaT0 - 0.004f || m < mesaT0 - 0.03f) continue;
                    AddRock(rng, i, j, 0.13f + Mathf.Pow(rng.Value(), 2f) * 0.22f, 0.9f);
                }
            // pebbles
            int peb = Count(s.pebbleDensity, W + 6, H + 6);
            for (int k = 0; k < peb; k++)
            {
                float i = rng.Range(-3f, W + 3f), j = rng.Range(-3f, H + 3f);
                if (nCluster.Fbm(i * 0.2f + 40f, j * 0.2f, 2) < rng.Value() * 0.8f) continue;
                AddRock(rng, i, j, rng.Range(0.09f, 0.12f), 1.2f);
            }
        }

        // ---------- baking ----------
        public Texture2D Bake()
        {
            BuildFields();
            var shadow = CastShadows();
            var px = Render(shadow);
            var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "MarsGround",
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        float[] CoarseField(System.Func<float, float, float> f)
        {
            int cw = TexW / Coarse + 2, ch = rows / Coarse + 2;
            var a = new float[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    GroundToLattice(x * Coarse, y * Coarse, out float i, out float j);
                    a[y * cw + x] = f(i, j);
                }
            return a;
        }

        float SampleCoarse(float[] a, int gx, int gy)
        {
            int cw = TexW / Coarse + 2;
            int x0 = gx / Coarse, y0 = gy / Coarse;
            float fx = (gx - x0 * Coarse) / (float)Coarse, fy = (gy - y0 * Coarse) / (float)Coarse;
            float a0 = Mathf.Lerp(a[y0 * cw + x0], a[y0 * cw + x0 + 1], fx);
            float a1 = Mathf.Lerp(a[(y0 + 1) * cw + x0], a[(y0 + 1) * cw + x0 + 1], fx);
            return Mathf.Lerp(a0, a1, fy);
        }

        void BuildFields()
        {
            int n = TexW * rows;
            height = new float[n];
            sand = new float[n];
            ejecta = new float[n];
            mesaMask = new float[n];
            rock = new bool[n];
            float variation = Mathf.Clamp01(s.terrainVariation);
            var cRoll = CoarseField((i, j) => nRoll.Fbm(i * 0.035f, j * 0.05f, 4));
            var cMesa = CoarseField((i, j) => nMesa.Fbm(i * 0.028f + 11f, j * 0.045f - 5f, 4));
            var cSand = CoarseField((i, j) => nSand.Fbm(i * 0.05f + 3f, j * 0.07f, 3));
            var cWarp = CoarseField((i, j) => nWarp.Fbm(i * 0.08f, j * 0.1f, 2));
            var cFlat = CoarseField(Flatten);
            float dune = Mathf.Clamp01(s.duneAmount);
            float maxH = 0f;

            // per-pixel invariants of SampleCoarse hoisted out of the loop (same arithmetic, so identical results)
            int cw = TexW / Coarse + 2;
            var cx0 = new int[TexW];
            var cfx = new float[TexW];
            for (int gx = 0; gx < TexW; gx++)
            {
                int x0 = gx / Coarse;
                cx0[gx] = x0;
                cfx[gx] = (gx - x0 * Coarse) / (float)Coarse;
            }
            var craterList = Craters;
            var rockList = Rocks;

            for (int gy = 0; gy < rows; gy++)
            {
                int y0 = gy / Coarse;
                float fy = (gy - y0 * Coarse) / (float)Coarse;
                int rowC = y0 * cw;
                float ly = gy - OriginY;          // GroundToLattice, row part
                float j = ly / CellH;
                for (int gx = 0; gx < TexW; gx++)
                {
                    float i = ((float)(gx - OriginX) - ly) / CellW;
                    int idx = gy * TexW + gx;
                    int c00 = rowC + cx0[gx];
                    float fx = cfx[gx];
                    float f = Bilerp(cFlat, c00, cw, fx, fy);
                    float roll = (Bilerp(cRoll, c00, cw, fx, fy) - 0.5f) * 26f * variation;
                    float m = Bilerp(cMesa, c00, cw, fx, fy) + (nDetail.Fbm(i * 0.4f, j * 0.6f, 2) - 0.5f) * 0.05f - (1f - f) * 0.5f;
                    float mesa = (m > mesaT0 ? 12f : 0f) + (m > mesaT0 + 0.075f ? 9f : 0f); // hard steps: one clean cliff face per column
                    float h = roll * f + mesa;
                    float sd = MarsMath.Smooth(0.55f, 0.72f, Bilerp(cSand, c00, cw, fx, fy)) + MarsMath.Smooth(-3f, -9f, roll) * 0.45f;
                    float ej = 0f;

                    var cb = Bucket(craterBuckets, i, j);
                    if (cb != null)
                        for (int q = 0; q < cb.Count; q++)
                        {
                            var c = craterList[cb[q]];
                            float di = i - c.i, dj = j - c.j;
                            float t = Mathf.Sqrt(di * di + dj * dj) / c.r;
                            if (t >= 3f) continue;
                            if (t < 1f)
                            {
                                float t2 = t * t;
                                h += -c.depth + (c.depth + c.rim) * t2 * t2;
                                if (c.r > 3.5f) h += c.depth * 0.45f * Mathf.Exp(-t2 / 0.02f);
                                sd = Mathf.Max(sd, MarsMath.Smooth(0.8f, 0.3f, t) * (0.25f + 0.7f * c.age));
                            }
                            else
                            {
                                h += c.rim * Mathf.Exp(-(t - 1f) * 3.5f);
                                ej += (1f - c.age) * 0.11f * Mathf.Exp(-(t - 1f) * 2.4f);
                            }
                        }

                    sd = Mathf.Clamp01(sd) * (mesa > 1f ? 0.2f : 1f);
                    if (sd > 0.2f && dune > 0f)
                    {
                        float u = (i * 0.55f - j * 1.1f) * 1.6f + Bilerp(cWarp, c00, cw, fx, fy) * 5f;
                        float fr = u - Mathf.Floor(u);
                        float tri = fr < 0.75f ? fr / 0.75f : (1f - fr) / 0.25f;
                        float breakup = MarsMath.Smooth(0.35f, 0.6f, nDetail.Sample(i * 0.7f + 50f, j * 1.4f));
                        h += tri * 1.6f * MarsMath.Smooth(0.2f, 0.7f, sd) * dune * f * (0.4f + 0.6f * breakup);
                    }

                    float rh = 0f;
                    var rb = Bucket(rockBuckets, i, j);
                    if (rb != null)
                        for (int q = 0; q < rb.Count; q++)
                        {
                            var r = rockList[rb[q]];
                            float di = i - r.i, dj = j - r.j;
                            float d2 = di * di + dj * dj;
                            if (d2 > r.r * r.r * 2.2f) continue;
                            float a = Mathf.Atan2(dj, di);
                            float rr = r.r * (1f + r.a1 * Mathf.Cos(a + r.p1) + r.a2 * Mathf.Cos(2f * a + r.p2));
                            float t = Mathf.Sqrt(d2) / rr;
                            if (t < 1f) rh = Mathf.Max(rh, r.h * Mathf.Pow(1f - t * t, 0.4f));
                        }
                    if (rh > 0.5f) { h += rh; rock[idx] = true; }

                    height[idx] = h;
                    sand[idx] = sd;
                    ejecta[idx] = ej;
                    mesaMask[idx] = mesa;
                    if (h > maxH) maxH = h;
                }
            }
            MaxHeight = maxH;
        }

        // SampleCoarse with the cell index and fractions precomputed by the caller (identical Lerp order)
        static float Bilerp(float[] a, int c00, int cw, float fx, float fy)
        {
            float a0 = Mathf.Lerp(a[c00], a[c00 + 1], fx);
            float a1 = Mathf.Lerp(a[c00 + cw], a[c00 + cw + 1], fx);
            return Mathf.Lerp(a0, a1, fy);
        }

        float[] CastShadows()
        {
            const float rise = 0.62f;
            const int Reach = 64, Blk = 16;
            var sh = new float[TexW * rows];

            // Local occluder bound. The ray from (gx, gy) only samples sx in [gx-64, gx-1], sy in [gy, gy+32].
            // A hit at step k needs height(s) - h0 > k*rise, so no step beyond ceil((localMax - h0)/rise) can hit:
            // marching only that far gives exactly the same result as marching up to the global MaxHeight bound.
            int nb = (TexW + Blk - 1) / Blk, back = Reach / Blk, up = Reach >> 1;
            var span = new float[rows * nb];   // per row: max over blocks b-back..b (covers [gx-64, gx-1] for gx in block b)
            var rowBlk = new float[nb];
            for (int gy = 0; gy < rows; gy++)
            {
                int row = gy * TexW;
                for (int b = 0; b < nb; b++)
                {
                    float mx = float.NegativeInfinity;
                    int x1 = Mathf.Min(TexW, (b + 1) * Blk);
                    for (int x = b * Blk; x < x1; x++) { float v = height[row + x]; if (v > mx) mx = v; }
                    rowBlk[b] = mx;
                }
                for (int b = 0; b < nb; b++)
                {
                    float mx = float.NegativeInfinity;
                    for (int bb = Mathf.Max(0, b - back); bb <= b; bb++) if (rowBlk[bb] > mx) mx = rowBlk[bb];
                    span[gy * nb + b] = mx;
                }
            }
            var local = new float[rows * nb];  // span maxed over rows gy..gy+32
            for (int gy = 0; gy < rows; gy++)
            {
                int y1 = Mathf.Min(rows - 1, gy + up);
                for (int b = 0; b < nb; b++)
                {
                    float mx = float.NegativeInfinity;
                    for (int y = gy; y <= y1; y++) { float v = span[y * nb + b]; if (v > mx) mx = v; }
                    local[gy * nb + b] = mx;
                }
            }

            for (int gy = 0; gy < rows; gy++)
                for (int gx = 0; gx < TexW; gx++)
                {
                    float h0 = height[gy * TexW + gx];
                    int maxK = Mathf.Min(Reach, Mathf.CeilToInt((MaxHeight - h0) / rise));
                    float lm = local[gy * nb + gx / Blk];
                    if (lm - h0 <= rise) maxK = 0; // nothing in reach rises above even the first step
                    else maxK = Mathf.Min(maxK, Mathf.CeilToInt((lm - h0) / rise));
                    float lit = 1f;
                    for (int k = 1; k <= maxK; k++)
                    {
                        int sx = gx - k, sy = gy + (k >> 1);
                        if (sx < 0 || sy >= rows) break;
                        if (height[sy * TexW + sx] - h0 > k * rise) { lit = 0f; break; }
                    }
                    sh[gy * TexW + gx] = lit;
                }
            return sh;
        }

        static float[] ReadGrain(Texture2D t)
        {
            var g = new float[4 * CellW * CellH];
            if (t == null || !t.isReadable) return g;
            var px = t.GetPixels32();
            int variants = Mathf.Min(4, t.width / CellW);
            float sum = 0;
            int n = 0;
            for (int v = 0; v < variants; v++)
                for (int y = 0; y < CellH; y++)
                    for (int x = 0; x < CellW; x++)
                    {
                        var c = px[y * t.width + v * CellW + x];
                        float l = (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / 255f;
                        g[(v * CellH + y) * CellW + x] = l;
                        sum += l; n++;
                    }
            float mean = sum / Mathf.Max(1, n), dev = 0;
            for (int k = 0; k < n; k++) dev += Mathf.Abs(g[k] - mean);
            dev = Mathf.Max(0.001f, dev / Mathf.Max(1, n));
            for (int k = 0; k < g.Length; k++) g[k] = k < n ? Mathf.Clamp((g[k] - mean) / (dev * 2.5f), -1f, 1f) : 0f;
            return g;
        }

        float Grain(float i, float j)
        {
            int ci = Mathf.FloorToInt(i), cj = Mathf.FloorToInt(j);
            uint hsh = (uint)(ci * 73856093 ^ cj * 19349663);
            int v = (int)((hsh ^ (hsh >> 13)) % 4u);
            int lx = Mathf.Clamp((int)((i - ci) * CellW), 0, CellW - 1);
            int ly = Mathf.Clamp((int)((j - cj) * CellH), 0, CellH - 1);
            return grain[(v * CellH + ly) * CellW + lx];
        }

        // ---------- pixel-art rendering ----------
        // Materials and their ramps; every pixel is (material, ramp index), outlines are added afterwards.
        const byte MatDust = 0, MatSand = 1, MatRock = 2, MatCliff = 3;

        static int Band(float lam, float shadowLit)
        {
            int b = lam < 0.72f ? -2 : lam < 0.9f ? -1 : lam < 1.13f ? 0 : lam < 1.36f ? 1 : 2;
            if (shadowLit < 0.5f) b = Mathf.Max(-2, Mathf.Min(b, 0) - 1);
            return b;
        }

        float Lambert(int gx, int gy)
        {
            int xl = Mathf.Max(0, gx - 1), xr = Mathf.Min(TexW - 1, gx + 1);
            int yd = Mathf.Max(0, gy - 1), yu = Mathf.Min(rows - 1, gy + 1);
            float sx = (height[gy * TexW + xr] - height[gy * TexW + xl]) * 0.5f;
            float sy = (height[yu * TexW + gx] - height[yd * TexW + gx]) * 0.25f;
            const float lx = -0.62f, ly = 0.42f, lz = 0.66f; // light from the upper left
            return (-sx * lx - sy * ly + lz) / Mathf.Sqrt(sx * sx + sy * sy + 1f) / lz;
        }

        void Top(int gx, int gy, float shadowLit, out byte mat, out sbyte tone)
        {
            int idx = gy * TexW + gx;
            int band = Band(Lambert(gx, gy), shadowLit);
            GroundToLattice(gx, gy, out float i, out float j);
            float g = Grain(i, j);
            uint ph = (uint)(gx * 73856093 ^ gy * 19349663); ph ^= ph >> 13; ph *= 0x5bd1e995u; ph ^= ph >> 15;
            int speck = (ph % 7u) != 0 ? 0 : g > 0.75f ? 1 : g < -0.75f ? -1 : 0; // sparse specks from the artist grain
            if (rock[idx]) { mat = MatRock; tone = (sbyte)Mathf.Clamp(4 + band, 1, Rock.Length - 1); return; }
            if (sand[idx] > 0.45f) { mat = MatSand; tone = (sbyte)Mathf.Clamp(2 + band, 0, Sand.Length - 1); return; }
            float alb = nAlb.Fbm(i * 0.035f, j * 0.055f, 3);
            int b = alb < 0.36f ? 4 : alb < 0.66f ? 5 : 6;
            if (ejecta[idx] > 0.045f || mesaMask[idx] > 1f) b++;
            mat = MatDust;
            tone = (sbyte)Mathf.Clamp(b + band + speck, 1, Dust.Length - 1);
        }

        void Face(int gx, int gy, int row, int faceBottom, int faceTop, out byte mat, out sbyte tone)
        {
            int idx = gy * TexW + gx;
            int d = faceTop - row; // rows below the lip, so strata follow the cliff edge
            if (rock[idx]) { mat = MatRock; tone = (sbyte)(d == 0 ? 2 : 1); return; }
            if (mesaMask[idx] < 1f) { mat = MatDust; tone = 3; return; } // crater walls, steep slopes
            mat = MatCliff;
            tone = (sbyte)(d == 0 ? 6 : d == 1 ? 5 : (d / 3) % 2 == 0 ? 4 : 3);
        }

        Color32[] Render(float[] shadow)
        {
            int n = TexW * TexH;
            var mats = new byte[n];
            var tones = new sbyte[n];
            var faceBottom = new bool[n];
            for (int gx = 0; gx < TexW; gx++)
            {
                int maxY = int.MinValue;
                byte lm = MatDust; sbyte lt = 5;
                for (int gy = 0; gy < rows; gy++)
                {
                    int idx = gy * TexW + gx;
                    int sy = gy + Mathf.RoundToInt(height[idx]);
                    if (sy <= maxY) continue;
                    Top(gx, gy, shadow[idx], out byte tm, out sbyte tt);
                    bool first = maxY == int.MinValue;
                    int start = first ? 0 : maxY + 1;
                    bool tall = sy - start > 3;
                    for (int r = Mathf.Max(0, start); r < sy && r < TexH; r++)
                    {
                        int k = r * TexW + gx;
                        if (first || !tall) { mats[k] = tm; tones[k] = tt; continue; }
                        Face(gx, gy, r, start, sy - 1, out mats[k], out tones[k]);
                        if (r == start) faceBottom[k] = true;
                    }
                    if (sy >= 0 && sy < TexH) { mats[sy * TexW + gx] = tm; tones[sy * TexW + gx] = tt; }
                    lm = tm; lt = tt;
                    maxY = sy;
                }
                for (int r = Mathf.Max(0, maxY + 1); r < TexH; r++) { mats[r * TexW + gx] = lm; tones[r * TexW + gx] = lt; }
            }

            var px = new Color32[n];
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    int k = y * TexW + x;
                    byte m = mats[k];
                    bool outline = faceBottom[k];
                    if (m == MatRock)
                    {
                        // outline the silhouette of rocks against non-rock neighbours
                        if (x > 0 && mats[k - 1] != MatRock || x < TexW - 1 && mats[k + 1] != MatRock ||
                            y > 0 && mats[k - TexW] != MatRock || y < TexH - 1 && mats[k + TexW] != MatRock) outline = true;
                    }
                    else if (m != MatCliff && y > 0 && mats[k - TexW] == MatCliff && m != MatRock)
                    {
                        outline = true; // ground just below a cliff
                    }
                    if (outline) { px[k] = Outline; continue; }
                    var ramp = m == MatDust ? Dust : m == MatSand ? Sand : m == MatRock ? Rock : Dust;
                    px[k] = ramp[Mathf.Clamp(tones[k], 0, ramp.Length - 1)];
                }
            return px;
        }

        public string Hash()
        {
            uint h = 2166136261u;
            void Mix(int v) { h = (h ^ (uint)v) * 16777619u; }
            Mix(W); Mix(H);
            foreach (var c in Craters) { Mix(Mathf.RoundToInt(c.i * 64)); Mix(Mathf.RoundToInt(c.j * 64)); Mix(Mathf.RoundToInt(c.r * 64)); }
            foreach (var r in Rocks) { Mix(Mathf.RoundToInt(r.i * 64)); Mix(Mathf.RoundToInt(r.j * 64)); Mix(Mathf.RoundToInt(r.r * 256)); }
            if (height != null) for (int k = 0; k < height.Length; k += 97) Mix(Mathf.RoundToInt(height[k] * 16));
            return h.ToString("x8");
        }

        public float HeightAtLattice(float i, float j)
        {
            if (height == null) return 0f;
            var g = LatticeToGround(i, j);
            int gx = Mathf.Clamp(Mathf.RoundToInt(g.x), 0, TexW - 1), gy = Mathf.Clamp(Mathf.RoundToInt(g.y), 0, rows - 1);
            return height[gy * TexW + gx];
        }

        /// <summary>
        /// Ground model at lattice (i, j), for building site surveys: height (ground px, boulders included),
        /// dark sand share 0..1, fresh-crater ejecta (0..~0.11), a boulder there, inside a crater bowl.
        /// </summary>
        public void Sample(float i, float j, out float h, out float sandShare, out float ejectaAmount, out bool boulder, out bool inCrater)
        {
            h = sandShare = ejectaAmount = 0f;
            boulder = inCrater = false;
            if (height == null) return;
            var g = LatticeToGround(i, j);
            int k = Mathf.Clamp(Mathf.RoundToInt(g.y), 0, rows - 1) * TexW + Mathf.Clamp(Mathf.RoundToInt(g.x), 0, TexW - 1);
            h = height[k]; sandShare = sand[k]; ejectaAmount = ejecta[k]; boulder = rock[k];
            var cb = Bucket(craterBuckets, i, j);
            if (cb != null)
                foreach (int id in cb)
                {
                    var c = Craters[id];
                    float di = i - c.i, dj = j - c.j;
                    if (di * di + dj * dj < c.r * c.r) { inCrater = true; break; }
                }
        }

        static Color32[] Pal(params string[] hex)
        {
            var p = new Color32[hex.Length];
            for (int k = 0; k < hex.Length; k++)
            {
                ColorUtility.TryParseHtmlString("#" + hex[k], out Color c);
                p[k] = c;
            }
            return p;
        }
    }
}
