using UnityEngine;
using UnityEngine.UI;
using static FrontierUi;

internal sealed class PlasmaSheath
{
    // Atmospheric entry, drawn on the rocket art's own pixel grid: a bow shock capping the nose and a
    // turbulent fire trail streaming back, quantized into heat bands with ordered dithering.
    private const int PlasmaSize = 260;
    private static readonly Color32[] Bands =
    {
        new Color32(255, 250, 236, 255), new Color32(255, 228, 118, 255), new Color32(255, 162, 54, 255),
        new Color32(234, 94, 54, 255), new Color32(170, 50, 54, 235), new Color32(98, 40, 54, 150),
    };
    private static readonly float[] BandFloor = { .86f, .68f, .5f, .34f, .21f, .14f };
    private static readonly float[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    private readonly Texture2D plasmaTexture;
    private readonly Color32[] plasmaPixels = new Color32[PlasmaSize * PlasmaSize];
    private readonly RawImage plasma;
    private float flow;

    public PlasmaSheath(Transform parent)
    {
        plasmaTexture = new Texture2D(PlasmaSize, PlasmaSize, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        plasma = Art("Entry plasma", parent, plasmaTexture);
        plasma.gameObject.SetActive(false);
    }

    // nose: tip in design px; forward: unit flight direction (y down); texel: design px per art pixel; heat 0..1.
    public void Tick(Vector2 size, float dt, Vector2 nose, Vector2 forward, float texel, float heat)
    {
        plasma.gameObject.SetActive(heat > .01f);
        if (heat <= .01f) return;
        flow += dt;

        // One plasma pixel is one art pixel, rounded to whole design pixels; the buffer sits behind the nose.
        float px = Mathf.Max(1, Mathf.Round(texel)), k = px / texel;
        Vector2 origin = nose - forward * px * 95 - Vector2.one * (PlasmaSize * px / 2);
        origin = new Vector2(Mathf.Round(origin.x), Mathf.Round(origin.y));
        Place(plasma.rectTransform, size, origin.x, origin.y, PlasmaSize * px, PlasmaSize * px);

        Vector2 tip = (nose - origin) / px, side = new Vector2(-forward.y, forward.x);
        float standoff = 4 + 5 * heat, trail = 40 + 85 * heat, scroll = flow * 3.2f;
        float gain = Mathf.Lerp(.7f, 1.25f, heat), shockDepth = 4 + 3 * heat;
        for (int y = 0; y < PlasmaSize; y++)
            for (int x = 0; x < PlasmaSize; x++)
            {
                int index = (PlasmaSize - 1 - y) * PlasmaSize + x; // texture rows run bottom-up
                Vector2 d = new Vector2(x + .5f - tip.x, y + .5f - tip.y);
                // u: art texels ahead of the nose tip (negative behind); v: texels to the side.
                float u = Vector2.Dot(d, forward) * k, v = Vector2.Dot(d, side) * k;
                if (u > standoff + 8 || u < -trail * 1.15f || v > 75 || v < -75)
                {
                    plasmaPixels[index] = default(Color32);
                    continue;
                }

                // Bow shock: a parabola wrapping the nose, flaring out past the hull.
                float front = standoff - v * v / 34f - u, intensity = 0;
                if (front > -6)
                {
                    float layer = front >= 0 ? Mathf.Exp(-front / shockDepth) : Mathf.Exp(front / 1.6f) * .7f;
                    intensity = layer * 1.1f * Mathf.Clamp01(1 + (u + 34) / 10);
                }

                // Fire trail: an envelope hugging the hull, widening behind the shoulders, its edges and
                // tail torn into tongues by noise that streams backwards.
                if (u < 0)
                {
                    float back = -u, along = back / trail;
                    float half = 31 + 4.5f * Mathf.Sqrt(Mathf.Max(0, back - 8)); // hull radius is ~23
                    float turb = .65f * Mathf.PerlinNoise(back * .09f - scroll * 2.6f, v * .16f + 7.3f)
                                 + .35f * Mathf.PerlinNoise(back * .23f - scroll * 4.4f, v * .37f + 1.9f);
                    float r = Mathf.Abs(v) / half - (turb - .5f) * (.35f + .5f * along);
                    along += (turb - .5f) * .45f; // ragged tail end
                    if (r < 1 && along < 1.1f)
                    {
                        float body = (1.15f - r * r) * (1 - Mathf.Clamp01(along) * .8f);
                        intensity = Mathf.Max(intensity, body * Mathf.Lerp(.95f, turb * 1.5f, Mathf.Clamp01(along)));
                    }
                }

                // Fade out toward the buffer's border so it never shows as a square.
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(PlasmaSize - 1 - x, PlasmaSize - 1 - y));
                intensity *= Mathf.Clamp01(edge / 30f);
                float q = intensity * gain + (Bayer[(x & 3) + (y & 3) * 4] / 16f - .47f) * .14f;
                var c = default(Color32);
                for (int band = 0; band < Bands.Length; band++)
                    if (q >= BandFloor[band]) { c = Bands[band]; break; }
                plasmaPixels[index] = c;
            }
        plasmaTexture.SetPixels32(plasmaPixels);
        plasmaTexture.Apply(false);
    }
}
