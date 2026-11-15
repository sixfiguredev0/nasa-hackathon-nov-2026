using System.Collections.Generic;
using RedFrontier.Mars;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>What the ground under a footprint is like. Shares are 0..1 of the sampled points.</summary>
    public struct Site
    {
        public float height;    // mean ground height, ground px (sprites are lifted by this)
        public float elevation; // mean height in mesa steps (12 ground px); + is high ground, - is a basin or crater floor
        public float uneven;    // highest minus lowest point, ground px
        public float sand, rock, ejecta, crater;
    }

    public struct SiteEffect
    {
        public float delta;
        public string why;
    }

    /// <summary>
    /// Scales a building's output by where it stands. Rules follow real Mars science, exaggerated for play:
    /// - Solar: dust in the air and on the panels is what cuts Mars solar power most. Low ground has more dusty air above it,
    ///   dark sand areas are where wind moves grit, crater walls shade the panels for part of the sol.
    /// - Ice drill: crater floors are cold traps; fresh impacts dig up buried ice (seen by HiRISE); low ground has denser air,
    ///   so ice lasts nearer the surface; boulders mean hard rock to drill.
    /// - Habitat (MOXIE): oxygen output follows air density, and air is densest on low ground.
    /// - Workshop: dark dune sand is basalt with iron minerals (olivine, pyroxene, magnetite); boulders and crater ejecta are fresh rock.
    /// - Greenhouse: needs light (dust and crater shade hurt) and warmth (low, dense-air ground helps a little).
    /// - Battery: cold air pools in crater floors and on high ground at night, and cold batteries hold less charge.
    /// - All: uneven ground needs a levelled foundation.
    /// </summary>
    public static class SiteSurvey
    {
        public const float MinYield = 0.25f, MaxYield = 1.75f;
        const float StepPx = 12f;

        public static Site Survey(MarsTerrain t, RectInt cells)
        {
            var s = new Site();
            if (t == null) return s;
            float lo = float.MaxValue, hi = float.MinValue;
            int n = 0;
            for (float j = cells.yMin + 0.25f; j < cells.yMax; j += 0.5f)
                for (float i = cells.xMin + 0.25f; i < cells.xMax; i += 0.5f)
                {
                    t.Sample(i, j, out float h, out float sd, out float ej, out bool rk, out bool cr);
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                    s.elevation += h; s.sand += sd; s.ejecta += Mathf.Clamp01(ej * 9f);
                    if (rk) s.rock++;
                    if (cr) s.crater++;
                    n++;
                }
            if (n == 0) return s;
            s.height = s.elevation / n;
            s.elevation /= n * StepPx; s.sand /= n; s.ejecta /= n; s.rock /= n; s.crater /= n;
            s.uneven = hi - lo;
            return s;
        }

        /// <summary>Output multiplier for this kind on this site, clamped to MinYield..MaxYield. Fills <paramref name="why"/> when given.</summary>
        public static float Yield(BuildingKind kind, Site s, List<SiteEffect> why = null)
        {
            float y = 1f;
            void Add(float d, string text)
            {
                if (Mathf.Abs(d) < 0.01f) return;
                y += d;
                why?.Add(new SiteEffect { delta = d, why = text });
            }
            float elev = Mathf.Clamp(s.elevation, -1.5f, 2f);
            bool high = elev > 0f;

            if (kind != BuildingKind.Rocket)
                Add(-Mathf.Min(0.5f, s.uneven * 0.02f), "Uneven ground: needs a levelled foundation");

            switch (kind)
            {
                case BuildingKind.Solar:
                    Add(0.12f * elev, high ? "High ground: less dusty air above" : "Low ground: more dusty air above");
                    Add(-0.3f * s.sand, "Blowing sand coats the panels");
                    Add(-0.3f * s.crater, "Crater walls shade the panels");
                    break;
                case BuildingKind.Greenhouse:
                    Add(-0.08f * elev, high ? "High ground: colder, thinner air" : "Low ground: denser air keeps the dome warm");
                    Add(-0.25f * s.sand, "Dust settles on the dome and dims the light");
                    Add(-0.2f * s.crater, "Crater walls block sunlight");
                    break;
                case BuildingKind.Water:
                    Add(-0.15f * elev, high ? "High ground: thin air, the ice dried out deep down" : "Low ground: ice lies close to the surface");
                    Add(0.35f * s.crater, "Crater floor: a cold trap where ice survives");
                    Add(0.4f * s.ejecta, "Fresh crater: the impact dug up buried ice");
                    Add(-0.2f * s.rock, "Boulders: hard rock to drill through");
                    break;
                case BuildingKind.Habitat:
                    Add(-0.12f * elev, high ? "High ground: thin air, MOXIE pulls in less CO2" : "Low ground: dense air, MOXIE pulls in more CO2");
                    break;
                case BuildingKind.Workshop:
                    Add(0.35f * s.sand, "Dark basalt sand: iron-rich minerals");
                    Add(0.35f * s.rock, "Boulders: fresh rock to crush");
                    Add(0.3f * s.ejecta, "Crater ejecta: rock dug up from deep down");
                    break;
                case BuildingKind.Battery:
                    Add(-0.2f * s.crater, "Crater floor: cold night air pools here");
                    if (high) Add(-0.06f * elev, "High ground: colder nights");
                    break;
            }
            return Mathf.Clamp(y, MinYield, MaxYield);
        }

        /// <summary>Rich-text block for the Builder Mode label: yield line plus each effect.</summary>
        public static string Describe(float yield, List<SiteEffect> why)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Site output <b>").Append(Color(yield - 1f)).Append((yield * 100f).ToString("0")).Append("%</color></b>");
            foreach (var e in why)
                sb.Append('\n').Append(Color(e.delta)).Append(e.delta > 0 ? "+" : "").Append((e.delta * 100f).ToString("0")).Append("%</color> ").Append(e.why);
            return sb.ToString();
        }

        static string Color(float d) => d > 0.005f ? "<color=#8f8>" : d < -0.005f ? "<color=#f88>" : "<color=#ddd>";
    }
}
