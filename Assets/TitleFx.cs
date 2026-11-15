using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using static FrontierUi;

// Title-screen atmosphere around the flying rocket: parallax star streams (streaking at warp),
// pixel exhaust puffs, a mission telemetry readout and synthesized engine audio.
// All positions are design pixels from the top left, like FrontierUi.Place.
internal sealed class TitleFx
{
    private const int Rate = 44100, PuffCount = 90;
    private static readonly float[] LayerSpeed = { 7, 20, 52 }, LayerSize = { 2, 2, 4 }, LayerAlpha = { .35f, .6f, .9f };
    private static readonly int[] LayerCount = { 46, 26, 12 };
    private static readonly Color Flame = new Color32(255, 170, 60, 255);

    private struct Star { public Image image; public Vector2 pos; public int layer; }
    private struct Puff { public Image image; public Vector2 pos, vel; public float age, life, size; }

    private readonly Star[] stars;
    private readonly Puff[] puffs = new Puff[PuffCount];
    private readonly RectTransform hud;
    private readonly PixelLabel mission, range, speed;
    private readonly AudioSource engine, sfx;
    private readonly AudioClip roar;
    private float spawn, readoutClock, distance = 1204330f;
    private int next;

    public TitleFx(GameObject host, Transform starLayer, Transform exhaustLayer, Transform hudLayer)
    {
        int total = 0;
        foreach (int count in LayerCount) total += count;
        stars = new Star[total];
        for (int layer = 0, i = 0; layer < LayerCount.Length; layer++)
            for (int k = 0; k < LayerCount[layer]; k++, i++)
            {
                var image = Fill("Star", starLayer, new Color(Cream.r / 255f, Cream.g / 255f, Cream.b / 255f, LayerAlpha[layer]));
                stars[i] = new Star { image = image, layer = layer,
                    pos = new Vector2(Random.value * 1280, Random.value * 720) };
            }

        for (int i = 0; i < PuffCount; i++)
        {
            puffs[i].image = Fill("Puff", exhaustLayer, Color.clear);
            puffs[i].image.gameObject.SetActive(false);
        }

        hud = Box("Readout", hudLayer);
        mission = PixelLabel.Create("Mission", hud, "MARS TRANSIT - DAY 212", 3, Coral, 1, true);
        range = PixelLabel.Create("Range", hud, "", 3, Cream, 1, true);
        speed = PixelLabel.Create("Speed", hud, "", 3, Cream, 1, true);

        engine = host.AddComponent<AudioSource>();
        engine.clip = Rumble();
        engine.loop = true;
        engine.volume = 0;
        engine.Play();
        sfx = host.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        roar = Synth("Ignition", 1.8f, Ignition);
    }

    public void Ignite() => sfx.PlayOneShot(roar, .8f);

    // throttle: 0.6 idle, 1 hovered, ~2 at ignition. warp: 0..1 star streaking. scale: rocket size / menu size.
    public void Tick(Vector2 size, float dt, bool reduced, bool showHud, Vector2 nozzle, Vector2 back,
        float throttle, float warp, float scale, float fade)
    {
        float motion = reduced ? 0 : dt;
        for (int i = 0; i < stars.Length; i++)
        {
            ref var s = ref stars[i];
            float baseSpeed = LayerSpeed[s.layer], height = LayerSize[s.layer];
            float length = Mathf.Round(height + warp * baseSpeed * 1.4f);
            s.pos.x -= baseSpeed * (1 + warp * 30) * motion;
            if (s.pos.x < -length || s.pos.y > size.y)
                s.pos = new Vector2(size.x + Random.value * 40, Random.value * size.y);
            Place(s.image.rectTransform, size, Mathf.Round(s.pos.x), Mathf.Round(s.pos.y), length, height);
        }

        // Exhaust: puffs leave the nozzle backwards, cool from cream through coral to mauve smoke.
        if (!reduced)
        {
            spawn += dt * 75 * throttle;
            Vector2 side = new Vector2(-back.y, back.x);
            for (; spawn >= 1; spawn--)
            {
                ref var p = ref puffs[next];
                next = (next + 1) % PuffCount;
                p.pos = nozzle + side * Random.Range(-6f, 6f) * scale;
                p.vel = back * Random.Range(130f, 210f) * Mathf.Lerp(.7f, 1.3f, throttle - .5f) * scale
                    + side * Random.Range(-28f, 28f) * scale + new Vector2(-40 * (1 + warp * 6), 0);
                p.age = 0;
                p.life = Random.Range(.4f, .75f);
                p.size = scale;
                p.image.gameObject.SetActive(true);
            }
        }
        for (int i = 0; i < PuffCount; i++)
        {
            ref var p = ref puffs[i];
            if (!p.image.gameObject.activeSelf) continue;
            p.age += dt;
            float t = p.age / p.life;
            if (t >= 1 || reduced) { p.image.gameObject.SetActive(false); continue; }
            p.pos += p.vel * dt;
            p.vel *= 1 - 2.2f * dt;
            float px = 2 * Mathf.Round(Mathf.Lerp(3, 9, t) * p.size);
            Color c = t < .15f ? (Color)Cream : t < .35f ? Flame : t < .6f ? (Color)Coral : (Color)Mauve;
            c.a = t < .6f ? 1 : (1 - t) * 1.6f;
            p.image.color = c;
            Place(p.image.rectTransform, size, Mathf.Round(p.pos.x - px / 2), Mathf.Round(p.pos.y - px / 2), px, px);
        }

        hud.gameObject.SetActive(showHud);
        if (showHud)
        {
            Place(mission.Rect, size, size.x - 520, size.y - 112, 480, 24);
            Place(range.Rect, size, size.x - 520, size.y - 82, 480, 24);
            Place(speed.Rect, size, size.x - 520, size.y - 52, 480, 24);
        }
        float velocity = 24.1f + warp * 900f;
        distance = Mathf.Max(0, distance - velocity * dt - warp * 900000f * dt);
        readoutClock -= dt;
        if (readoutClock <= 0)
        {
            readoutClock = .2f;
            range.Text = "RANGE " + distance.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ') + " KM";
            speed.Text = "VEL " + velocity.ToString("0.0", CultureInfo.InvariantCulture) + " KM/S";
        }

        engine.volume = (.05f + .07f * throttle + .35f * warp) * (1 - fade);
        engine.pitch = .85f + .12f * throttle + .4f * warp;
    }

    // Two-second low rumble whose tail is crossfaded into its head so the loop has no click.
    private static AudioClip Rumble()
    {
        int length = 2 * Rate, blend = Rate / 10;
        var raw = new float[length + blend];
        float low = 0, lower = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            float s = i / (float)Rate;
            low += (Random.value * 2 - 1 - low) * .05f;
            lower += (low - lower) * .1f;
            // 55 Hz and 3 Hz both complete whole cycles in 2 s.
            raw[i] = lower * 5f + .18f * Mathf.Sin(2 * Mathf.PI * 55 * s) * (.7f + .3f * Mathf.Sin(2 * Mathf.PI * 3 * s));
        }
        var data = new float[length];
        for (int i = 0; i < length; i++)
            data[i] = Mathf.Clamp(i < blend ? Mathf.Lerp(raw[length + i], raw[i], i / (float)blend) : raw[i], -1, 1);
        var clip = AudioClip.Create("Engine rumble", length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float roarLow, roarLower;

    private static float Ignition(float s)
    {
        roarLow += (Random.value * 2 - 1 - roarLow) * .25f;
        roarLower += (roarLow - roarLower) * .2f;
        float envelope = Mathf.Clamp01(s / .04f) * Mathf.Exp(-s * 1.6f);
        float thump = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(70, 35, s / 1.8f) * s) * Mathf.Exp(-s * 4f);
        return (roarLower * 3.5f + thump * .8f) * envelope;
    }

    private static float burnLow, burnLower;

    // Rising roar of air tearing past the hull, with a low buffeting wobble.
    internal static float EntryBurn(float s)
    {
        float bright = Mathf.Lerp(.06f, .3f, Mathf.Clamp01(s / 1.4f));
        burnLow += (Random.value * 2 - 1 - burnLow) * bright;
        burnLower += (burnLow - burnLower) * .3f;
        float envelope = Mathf.Clamp01(s / .5f) * Mathf.Clamp01((2.2f - s) / .4f);
        float buffet = .75f + .25f * Mathf.Sin(2 * Mathf.PI * 9 * s);
        return burnLower * 4f * envelope * buffet;
    }

    internal static AudioClip Synth(string name, float seconds, System.Func<float, float> wave)
    {
        var data = new float[Mathf.CeilToInt(seconds * Rate)];
        for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
