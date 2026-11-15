using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using static FrontierUi;

// The entry cutscene between the title screen and the crash, drawn as low-res pixel art (320 x 180 film,
// each film pixel 4 design pixels) under letterbox bars:
//   Orbit        0-2.4 s  a lit, slowly turning Mars fills the lower left; the rocket is a speck diving at the limb.
//   Entry        2.4-5.6 s close on the rocket, nose down-left, wrapped in plasma over the curved horizon.
//   Breakthrough 5.6-7.6 s a pixel dissolve into the dusty sky; the ground rises, smoke trails, the chute fails.
// Then a hard cut to black and the outpost scene, where the crash picks up on the same heading.
internal sealed class EntryCinematic : MonoBehaviour
{
    private const int W = 320, H = 180, Bar = 16, Rate = 22050;
    private const float OrbitEnd = 2.4f, EntryEnd = 5.6f, End = 7.6f, BlackHold = .4f, Film = 4, RocketScale = 3;
    private static readonly Vector2 Design = new Vector2(1280, 720);
    // Nose direction of the baked rocket (RocketWreck frame 0) on screen, y down; its nose tip in that frame.
    private static readonly Vector2 Forward = new Vector2(-.894f, .447f), NoseTipPx = new Vector2(52, 86), TailPx = new Vector2(136, 50);

    private static readonly Color32 Space = new Color32(12, 9, 18, 255), Night = new Color32(30, 6, 10, 255), Haze = new Color32(226, 88, 82, 255),
        Air = new Color32(255, 150, 112, 255), Streak = new Color32(255, 226, 180, 255), Cream32 = new Color32(248, 229, 200, 255);
    private static readonly Color32[] Surface =
    {
        new Color32(60, 22, 26, 255), new Color32(96, 36, 32, 255), new Color32(136, 52, 40, 255), new Color32(178, 78, 50, 255),
        new Color32(208, 112, 68, 255), new Color32(232, 152, 98, 255), new Color32(246, 196, 146, 255),
    };
    // The planet seen from space, sampled from the main menu's Mars art (Art/MainMenu/MarsPlanet.png): crimson, not butterscotch.
    // Surface (below) stays the dusty ground of the landing site.
    private static readonly Color32[] Planet =
    {
        new Color32(50, 8, 10, 255), new Color32(66, 11, 12, 255), new Color32(90, 18, 19, 255), new Color32(115, 23, 24, 255),
        new Color32(140, 34, 34, 255), new Color32(176, 59, 59, 255), new Color32(210, 83, 80, 255), new Color32(232, 104, 98, 255),
    };
    private static readonly Color32[] Sky =
    {
        new Color32(110, 62, 64, 255), new Color32(146, 82, 70, 255), new Color32(180, 108, 82, 255),
        new Color32(204, 136, 102, 255), new Color32(222, 166, 126, 255),
    };
    private static readonly float[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    private struct Mote { public Vector2 pos; public float length, speed, level; }

    private string destination;
    private float t;
    private bool cut;
    private RectTransform layout, rocketRect;
    private RawImage film, rocket;
    private Image tint, flash, black;
    private PixelLabel caption, readout;
    private Texture2D filmTexture;
    private readonly Color32[] px = new Color32[W * H];
    private PlasmaSheath plasma;
    private readonly Vector3[] stars = new Vector3[150];
    private readonly Mote[] streaks = new Mote[40], wisps = new Mote[22];
    private readonly Vector2[] trail = new Vector2[28];
    private readonly Vector4[] smoke = new Vector4[70]; // x, y, age, life
    private int trailHead, smokeNext;
    private float smokeAcc;
    private AudioSource sfx, wind;
    private AudioClip whoosh, roar, gust, beep, boom;
    private float beepClock;

    internal static void Play(string destination)
    {
        var go = new GameObject("Entry cinematic");
        go.AddComponent<EntryCinematic>().Build(destination);
    }

    private void Build(string scene)
    {
        destination = scene;
        var canvas = CreateCanvas(out _, false);
        canvas.GetComponent<Canvas>().sortingOrder = 90;
        canvas.SetParent(transform, false);
        Fill("Backdrop", canvas, Color.black);
        layout = FitLayout(canvas);
        Resize(canvas, Design, layout);

        filmTexture = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        film = Art("Film", layout, filmTexture);
        tint = Fill("Heat tint", layout, Color.clear);
        Place(tint.rectTransform, Design, 0, Bar * Film, Design.x, Design.y - 2 * Bar * Film); // between the bars
        plasma = new PlasmaSheath(layout);
        rocket = Art("Rocket", layout, Resources.Load<Texture2D>("Buildings/RocketWreck"));
        rocket.uvRect = new Rect(0, 0, .25f, 1); // frame 0: intact, nose to the bottom left
        rocketRect = rocket.rectTransform;
        rocket.enabled = false;

        caption = PixelLabel.Create("Caption", layout, "", 3, Cream, 0, true);
        readout = PixelLabel.Create("Readout", layout, "", 3, Cream, 1, true);
        Place(caption.Rect, Design, 40, Design.y - Bar * Film + 18, 700, 28);
        Place(readout.Rect, Design, Design.x - 740, Design.y - Bar * Film + 18, 700, 28);
        flash = Fill("Flash", layout, Color.clear);
        black = Fill("Cut", canvas, Color.clear);

        var rng = new System.Random(5);
        for (int i = 0; i < stars.Length; i++)
            stars[i] = new Vector3((float)rng.NextDouble() * W, (float)rng.NextDouble() * H, rng.Next(3));
        for (int i = 0; i < streaks.Length; i++) streaks[i] = NewMote(rng, true);
        for (int i = 0; i < wisps.Length; i++) wisps[i] = NewMote(rng, false);
        for (int i = 0; i < smoke.Length; i++) smoke[i].w = 0;

        sfx = gameObject.AddComponent<AudioSource>();
        wind = gameObject.AddComponent<AudioSource>();
        wind.loop = true;
        whoosh = TitleFx.Synth("Orbit whoosh", 2.4f, Whoosh);
        roar = TitleFx.Synth("Entry roar", 3.4f, Roar);
        gust = TitleFx.Synth("Wind", 2f, Wind);
        beep = TitleFx.Synth("Chute alarm", .12f, s => Mathf.Sign(Mathf.Sin(s * 2 * Mathf.PI * 880)) * .35f * (1 - s / .12f));
        boom = TitleFx.Synth("Impact", 1.2f, Boom);
        sfx.PlayOneShot(whoosh, .6f);
        Draw(0);
    }

    private static Mote NewMote(System.Random rng, bool streak) => new Mote
    {
        pos = new Vector2((float)rng.NextDouble() * W * 1.4f - W * .4f, (float)rng.NextDouble() * H * 1.4f),
        length = streak ? 6 + (float)rng.NextDouble() * 14 : 18 + (float)rng.NextDouble() * 40,
        speed = streak ? 180 + (float)rng.NextDouble() * 140 : 160 + (float)rng.NextDouble() * 160,
        level = (float)rng.NextDouble(),
    };

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (cut)
        {
            t += dt;
            if (t >= End + BlackHold)
            {
                MissionArrival.Load(destination);
                enabled = false;
            }
            return;
        }
        float before = t;
        t += dt;
        if (before < OrbitEnd && t >= OrbitEnd) sfx.PlayOneShot(roar, .9f);
        if (before < EntryEnd && t >= EntryEnd)
        {
            wind.clip = gust;
            wind.volume = .5f;
            wind.Play();
        }
        if (t >= End)
        {
            // Hard cut to black on impact; the crash scene opens on the same heading.
            cut = true;
            black.color = Color.black;
            wind.Stop();
            sfx.PlayOneShot(boom, 1f);
            return;
        }
        Draw(dt);
    }

    private void Draw(float dt)
    {
        bool orbit = t < OrbitEnd, entry = !orbit && t < EntryEnd;
        float heat = 0;

        if (orbit) DrawOrbit(t, dt);
        else if (entry)
        {
            float b = t - OrbitEnd;
            heat = Mathf.SmoothStep(0, 1, b / 1.4f) * (1 - .45f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(2.5f, 3.2f, b)));
            DrawEntry(b, dt, heat);
        }
        else
        {
            float k = t - EntryEnd;
            heat = .55f * (1 - Mathf.SmoothStep(0, 1, k / .6f));
            // Pixel dissolve from the burning sky of the entry shot into the dusty lower atmosphere.
            if (k < .4f) DrawEntry(EntryEnd - OrbitEnd + k, dt, heat);
            DrawBreakthrough(k, dt, k < .4f ? k / .4f : 1f);
        }

        // Letterbox bars.
        for (int y = 0; y < H; y++)
            if (y < Bar || y >= H - Bar)
                for (int x = 0; x < W; x++) px[(H - 1 - y) * W + x] = new Color32(0, 0, 0, 255);
        filmTexture.SetPixels32(px);
        filmTexture.Apply(false);

        // The rocket in the close shots, with smooth buffeting and a camera that rumbles with the heat.
        rocket.enabled = !orbit;
        float shake = orbit ? 0 : 2 + 5 * heat;
        float n = Time.unscaledTime * 13f;
        layout.anchoredPosition = new Vector2(Mathf.PerlinNoise(n, .3f) - .5f, Mathf.PerlinNoise(.7f, n) - .5f) * 2 * shake;
        if (!orbit)
        {
            float k = Mathf.Max(0, t - EntryEnd);
            var buffet = new Vector2(Mathf.PerlinNoise(t * 5, 1.1f) - .5f, Mathf.PerlinNoise(2.3f, t * 5) - .5f) * 10 * heat;
            Vector2 topLeft = Design / 2 - new Vector2(96, 64) * RocketScale + buffet + Forward * (k * 40);
            topLeft = new Vector2(Mathf.Round(topLeft.x), Mathf.Round(topLeft.y));
            Place(rocketRect, Design, topLeft.x, topLeft.y, 192 * RocketScale, 128 * RocketScale);
            plasma.Tick(Design, dt, topLeft + NoseTipPx * RocketScale, Forward, RocketScale, heat);
            if (!entry) EmitSmoke((topLeft + TailPx * RocketScale) / Film, dt);
        }
        else plasma.Tick(Design, dt, Vector2.zero, Forward, RocketScale, 0);
        tint.color = new Color(1f, .45f, .15f, .14f * heat);
        flash.color = new Color(1, 1, 1, t >= OrbitEnd && t < OrbitEnd + .06f ? .7f : 0);
        Caption(heat);
    }

    // ---- shots ----

    private void DrawOrbit(float c, float dt)
    {
        Clear(Space);
        Stars(c * 2f);
        float push = Mathf.SmoothStep(0, 1, c / OrbitEnd);
        var centre = new Vector2(Mathf.Lerp(70, 84, push), Mathf.Lerp(252, 246, push));
        float radius = Mathf.Lerp(176, 206, push);
        Sphere(centre, radius, c * .05f, 0, new Vector3(.55f, .45f, .7f).normalized, 8, 0);

        // The rocket: a speck diving at the lit limb, its engine glinting, a fading trail behind it.
        Vector2 from = new Vector2(316, 36), to = centre + new Vector2(.74f, -.67f) * (radius + 2);
        Vector2 at = Vector2.Lerp(from, to, Mathf.Pow(c / OrbitEnd, 1.4f));
        trail[trailHead] = at;
        trailHead = (trailHead + 1) % trail.Length;
        for (int i = 0; i < trail.Length; i++)
        {
            var p = trail[(trailHead + i) % trail.Length];
            if (p == Vector2.zero) continue;
            float a = i / (float)trail.Length;
            if (a * .8f > Threshold((int)p.x, (int)p.y)) Blend((int)p.x, (int)p.y, new Color32(200, 190, 200, 255), .5f);
        }
        float ember = Mathf.InverseLerp(1.6f, OrbitEnd, c);
        int glow = Mathf.RoundToInt(ember * 4);
        for (int y = -glow; y <= glow; y++)
            for (int x = -glow; x <= glow; x++)
            {
                float d = Mathf.Sqrt(x * x + y * y);
                if (d > glow) continue;
                Put((int)at.x + x, (int)at.y + y, d < glow * .4f ? Streak : (Color32)Air);
            }
        Put((int)at.x, (int)at.y, Cream32);
        Put((int)at.x + 1, (int)at.y, Cream32);
        Put((int)at.x, (int)at.y + 1, Cream32);
        if (Mathf.Repeat(c * 9, 1) < .5f) Put((int)at.x + 2, (int)at.y - 1, new Color32(255, 200, 120, 255));
    }

    private void DrawEntry(float b, float dt, float heat)
    {
        Clear(Space);
        Stars(6 + b * 30);
        // The limb rises up-right behind the rocket as it falls; the ground streams past underneath.
        var centre = new Vector2(-200 + 30 * b, 520 - 34 * b);
        Sphere(centre, 560 + 12 * b, -.3f * b, -.35f * b, new Vector3(.5f, .55f, .67f).normalized, 18 + 10 * heat, heat);
        // Burning air streaming past the camera.
        Vector2 dir = new Vector2(-Forward.x, -Forward.y);
        for (int i = 0; i < streaks.Length; i++)
        {
            ref var m = ref streaks[i];
            m.pos += dir * m.speed * dt;
            if (m.pos.x > W + 20 || m.pos.y < -20) { m.pos = new Vector2(Random.Range(-60f, W * .6f), Random.Range(H * .4f, H + 40f)); }
            if (m.level > heat) continue;
            Line(m.pos, m.pos - dir * m.length * (.5f + heat), Streak, .3f + .5f * heat);
        }
    }

    private void DrawBreakthrough(float k, float dt, float reveal)
    {
        float horizon = Mathf.Lerp(150, 110, Mathf.SmoothStep(0, 1, k / (End - EntryEnd)));
        for (int y = Bar; y < H - Bar; y++)
            for (int x = 0; x < W; x++)
            {
                if (reveal < 1 && Threshold(x, y) >= reveal) continue; // dissolve
                Color32 c;
                float ridge = horizon - 5 - 9 * Mathf.PerlinNoise(x * .022f + 4, 1.7f) - 3 * Mathf.PerlinNoise(x * .09f, 3.1f);
                if (y < ridge)
                {
                    float v = 1 - (ridge - y) / (ridge - Bar);
                    c = Pick(Sky, v * .95f, x, y);
                }
                else if (y < horizon)
                    c = Pick(Surface, .62f + .1f * Mathf.PerlinNoise(x * .1f, y * .1f), x, y); // far ridges, hazed
                else
                {
                    // Plain below, speckled, darker towards the camera, sliding as the rocket descends.
                    float depth = (y - horizon) / (H - Bar - horizon);
                    float grain = Mathf.PerlinNoise((x + k * 60) * .06f / (.3f + depth), y * .2f);
                    c = Pick(Surface, .52f - .3f * depth + .18f * grain, x, y);
                }
                px[(H - 1 - y) * W + x] = c;
            }
        if (reveal < 1) return;
        // Dust wisps rushing past.
        Vector2 dir = new Vector2(-Forward.x, -Forward.y);
        for (int i = 0; i < wisps.Length; i++)
        {
            ref var m = ref wisps[i];
            m.pos += dir * m.speed * dt;
            if (m.pos.x > W + 40 || m.pos.y < -20) m.pos = new Vector2(Random.Range(-80f, W * .5f), Random.Range(H * .5f, H + 40f));
            Line(m.pos, m.pos - dir * m.length, new Color32(240, 204, 168, 255), .35f + .3f * m.level);
        }
        DrawSmoke(dt);
    }

    private void EmitSmoke(Vector2 at, float dt)
    {
        for (smokeAcc += dt * 40; smokeAcc >= 1; smokeAcc--)
        {
            smoke[smokeNext] = new Vector4(at.x + Random.Range(-2f, 2f), at.y + Random.Range(-2f, 2f), 0, Random.Range(.6f, 1.1f));
            smokeNext = (smokeNext + 1) % smoke.Length;
        }
    }

    private void DrawSmoke(float dt)
    {
        Vector2 drift = new Vector2(-Forward.x, -Forward.y) * 70;
        for (int i = 0; i < smoke.Length; i++)
        {
            var s = smoke[i];
            if (s.w <= 0 || s.z >= s.w) continue;
            s.z += dt;
            s.x += drift.x * dt;
            s.y += drift.y * dt;
            smoke[i] = s;
            float a = s.z / s.w;
            int r = 1 + Mathf.RoundToInt(a * 3);
            var col = Color32.Lerp(new Color32(70, 58, 64, 255), new Color32(150, 126, 122, 255), a);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r && (1 - a) * .9f > Threshold((int)s.x + x, (int)s.y + y))
                        Put((int)s.x + x, (int)s.y + y, col);
        }
    }

    // ---- film drawing ----

    private void Clear(Color32 c)
    {
        for (int i = 0; i < px.Length; i++) px[i] = c;
    }

    private void Stars(float drift)
    {
        foreach (var s in stars)
        {
            int x = Mathf.FloorToInt(Mathf.Repeat(s.x + drift, W)), y = Mathf.FloorToInt(Mathf.Repeat(s.y - drift * .5f, H));
            byte v = (byte)(s.z == 2 ? 240 : s.z == 1 ? 170 : 110);
            if (s.z == 2 && Mathf.Repeat(Time.unscaledTime * .7f + s.x, 3) < .15f) v = 120;
            Put(x, y, new Color32(v, v, (byte)Mathf.Min(255, v + 15), 255));
        }
    }

    // A lit, dithered planet with a hazy atmosphere; spin scrolls the surface, heat makes the air glow.
    private void Sphere(Vector2 c, float radius, float spinU, float spinV, Vector3 light, float atmosphere, float heat)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - radius - atmosphere)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(c.x + radius + atmosphere));
        int y0 = Mathf.Max(Bar, Mathf.FloorToInt(c.y - radius - atmosphere)), y1 = Mathf.Min(H - Bar - 1, Mathf.CeilToInt(c.y + radius + atmosphere));
        var glow = Color32.Lerp(Haze, Streak, heat * .5f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + .5f - c.x) / radius, dy = (c.y - (y + .5f)) / radius, r2 = dx * dx + dy * dy;
                if (r2 < 1)
                {
                    float nz = Mathf.Sqrt(1 - r2);
                    float lit = Mathf.Clamp01(dx * light.x + dy * light.y + nz * light.z);
                    float lon = Mathf.Atan2(dx, nz);
                    float h = .65f * Mathf.PerlinNoise(lon * 1.8f + spinU + 11, dy * 1.8f + spinV + 5)
                              + .35f * Mathf.PerlinNoise(lon * 6f + spinU * 3 + 3, dy * 6f + spinV * 3 + 9);
                    float v = lit * (.4f + .8f * h);
                    if (r2 > .86f) v += (r2 - .86f) * 1.6f * lit; // haze brightens the limb
                    px[(H - 1 - y) * W + x] = lit < .05f ? Night : Pick(Planet, v, x, y);
                }
                else if (atmosphere > 0)
                {
                    float d = (Mathf.Sqrt(r2) - 1) * radius;
                    if (d >= atmosphere) continue;
                    float side = Mathf.Clamp01((dx * light.x + dy * light.y) / Mathf.Sqrt(r2) * .7f + .35f);
                    float a = (1 - d / atmosphere) * (1 - d / atmosphere) * side * (1 + heat);
                    if (a > Threshold(x, y)) Blend(x, y, glow, Mathf.Clamp01(.35f + .4f * a));
                }
            }
    }

    private void Line(Vector2 a, Vector2 b, Color32 c, float alpha)
    {
        int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
            if (alpha * (1 - i / (float)(steps + 1)) > Threshold((int)p.x, (int)p.y)) Blend((int)p.x, (int)p.y, c, .7f);
        }
    }

    private static Color32 Pick(Color32[] palette, float v, int x, int y)
    {
        float q = Mathf.Clamp01(v) * (palette.Length - 1) + (Bayer4[(x & 3) + (y & 3) * 4] / 16f - .47f) * .7f;
        return palette[Mathf.Clamp(Mathf.RoundToInt(q), 0, palette.Length - 1)];
    }

    private static float Threshold(int x, int y) => (Bayer4[(x & 3) + (y & 3) * 4] + .5f) / 16f;

    private void Put(int x, int y, Color32 c)
    {
        if ((uint)x < W && (uint)y < H) px[(H - 1 - y) * W + x] = c;
    }

    private void Blend(int x, int y, Color32 c, float a)
    {
        if ((uint)x >= W || (uint)y >= H) return;
        int i = (H - 1 - y) * W + x;
        px[i] = Color32.Lerp(px[i], c, a);
    }

    // ---- captions and sound ----

    private void Caption(float heat)
    {
        bool blink = Mathf.Repeat(Time.unscaledTime, .5f) < .3f;
        if (t < OrbitEnd)
        {
            caption.Text = "MARS ORBIT - DAY 212";
            caption.Color = Cream;
            readout.Text = "ENTRY INTERFACE T-" + Mathf.Max(0, Mathf.CeilToInt(OrbitEnd - t)).ToString(CultureInfo.InvariantCulture);
        }
        else if (t < EntryEnd)
        {
            float b = t - OrbitEnd;
            caption.Text = "!! ATMOSPHERIC ENTRY !!";
            caption.Color = blink ? (Color)Cream : (Color)Coral;
            readout.Text = "ALT " + Mathf.Lerp(125, 14, b / (EntryEnd - OrbitEnd)).ToString("0", CultureInfo.InvariantCulture)
                           + " KM   HULL " + Mathf.RoundToInt(400 + 1600 * heat) + " C";
        }
        else
        {
            float k = t - EntryEnd;
            caption.Text = k < .5f ? "PLASMA BLACKOUT ENDED" : "DROGUE CHUTE FAILED";
            caption.Color = k >= .5f && blink ? (Color)Coral : (Color)Cream;
            readout.Text = "ALT " + Mathf.Lerp(12, 1.2f, k / (End - EntryEnd)).ToString("0.0", CultureInfo.InvariantCulture) + " KM";
            if (k >= .5f && (beepClock -= Time.unscaledDeltaTime) <= 0)
            {
                beepClock = .32f;
                sfx.PlayOneShot(beep, .5f);
            }
        }
    }

    private static float wLow;
    private static float Whoosh(float s)
    {
        wLow += (Random.value * 2 - 1 - wLow) * Mathf.Lerp(.02f, .12f, s / 2.4f);
        return wLow * 3f * Mathf.Clamp01(s / 1.4f) * Mathf.Clamp01((2.4f - s) / .2f);
    }

    private static float rLow, rLower;
    private static float Roar(float s)
    {
        rLow += (Random.value * 2 - 1 - rLow) * Mathf.Lerp(.08f, .32f, Mathf.Clamp01(s / 1.6f));
        rLower += (rLow - rLower) * .3f;
        float envelope = Mathf.Clamp01(s / .25f) * Mathf.Clamp01((3.4f - s) / .6f);
        return rLower * 4.2f * envelope * (.75f + .25f * Mathf.Sin(2 * Mathf.PI * 11 * s));
    }

    private static float gLow;
    private static float Wind(float s)
    {
        gLow += (Random.value * 2 - 1 - gLow) * .18f;
        return gLow * 2.2f * (.7f + .3f * Mathf.Sin(2 * Mathf.PI * .5f * s)); // loops on a whole cycle
    }

    private static float bLow;
    private static float Boom(float s)
    {
        bLow += (Random.value * 2 - 1 - bLow) * .2f;
        float thump = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(60, 28, s / 1.2f) * s);
        return (bLow * 2.5f + thump) * Mathf.Exp(-s * 3.2f) * Mathf.Clamp01(s / .01f);
    }
}
