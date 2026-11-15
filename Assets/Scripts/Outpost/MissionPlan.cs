using System;
using System.Collections.Generic;

namespace RedFrontier.Outpost
{
    public enum MissionEventKind { SolarParticle, DustStorm, EquipmentFailure, ResupplyDelay }

    public sealed class MissionEvent
    {
        public MissionEventKind kind;
        /// <summary>Mission seconds when it strikes.</summary>
        public float at;
        /// <summary>How long it lasts (the solar-particle choice runs its own exercise clock).</summary>
        public float duration;
        /// <summary>Dust storm: share of normal sunlight that still reaches the panels.</summary>
        public float severity = 1f;
        /// <summary>Seconds of warning before it strikes.</summary>
        public float warning;

        public float End => at + duration;
    }

    /// <summary>
    /// The whole mission as data: how many sols, which events happen and when, and the day/night cycle.
    /// Plain C# and fully determined by the seed, so the same seed replays the same mission and the
    /// editor simulation can run it without a scene.
    /// </summary>
    public sealed class MissionPlan
    {
        public const int DefaultSols = 7;
        public const float SolSeconds = 100f;   // game time; a real sol is 24 h 39 min
        public const float DayFraction = 0.6f;  // share of the sol with sunlight
        public const float NoonBoost = 1.5f;

        // Solar particle event rules (game rules, not NASA measurements). Shared with HistoricalTraining and the simulator.
        public const float SepShelterSeconds = 25f, SepFitnessDip = .15f, SepChoiceSeconds = 25f;    // peak sun, so a sol averages about 0.95 of "full" output

        public readonly int seed, sols;
        public readonly List<MissionEvent> events = new List<MissionEvent>();

        public float Length => sols * SolSeconds;

        public MissionPlan(int seed, int sols = DefaultSols)
        {
            this.seed = seed;
            this.sols = Math.Max(4, sols);
            var rnd = new Random(seed ^ 0x2F6E2B1);

            // Events fall on distinct sols after the first one (a calm start to learn in) and before the last.
            var slots = new List<int>();
            for (int s = 1; s <= this.sols - 2; s++) slots.Add(s);
            Shuffle(slots, rnd);
            int count = Math.Min(slots.Count, 3 + rnd.Next(2));
            slots = slots.GetRange(0, count);
            slots.Sort();

            // One real-data solar particle event and one dust storm every run; the rest come from the pool.
            var kinds = new List<MissionEventKind> { MissionEventKind.SolarParticle, MissionEventKind.DustStorm };
            var extras = new List<MissionEventKind> { MissionEventKind.EquipmentFailure, MissionEventKind.ResupplyDelay };
            Shuffle(extras, rnd);
            for (int i = 0; kinds.Count < count; i++) kinds.Add(extras[i % extras.Count]);
            Shuffle(kinds, rnd);

            for (int i = 0; i < count; i++)
            {
                var e = new MissionEvent { kind = kinds[i], at = slots[i] * SolSeconds + 15f + rnd.Next(0, 50) };
                switch (e.kind)
                {
                    case MissionEventKind.DustStorm:
                        e.duration = 35f + rnd.Next(0, 21);
                        e.severity = 0.15f + rnd.Next(0, 21) / 100f;
                        e.warning = 10f;
                        break;
                    case MissionEventKind.EquipmentFailure:
                        e.duration = 40f;
                        break;
                    case MissionEventKind.ResupplyDelay:
                        e.duration = SolSeconds;
                        e.warning = 10f;
                        break;
                    case MissionEventKind.SolarParticle:
                        e.duration = 0f;
                        break;
                }
                events.Add(e);
            }
        }

        static void Shuffle<T>(IList<T> list, Random rnd)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        /// <summary>1-based sol number at mission time <paramref name="t"/>.</summary>
        public static int SolAt(float t) => (int)(t / SolSeconds) + 1;

        /// <summary>0..1 position in the current sol (0 = sunrise).</summary>
        public static float Phase(float t) => (t % SolSeconds) / SolSeconds;

        public static bool IsDay(float t) => Phase(t) < DayFraction;

        /// <summary>Sunlight on the panels from the day/night cycle alone: 0 at night, peaking at noon.</summary>
        public static float DaylightFactor(float t)
        {
            float ph = Phase(t);
            return ph >= DayFraction ? 0f : (float)Math.Sin(Math.PI * ph / DayFraction) * NoonBoost;
        }

        /// <summary>Share of sunlight that dust lets through at mission time <paramref name="t"/> (1 = clear).</summary>
        public float DustFactor(float t)
        {
            float f = 1f;
            foreach (var e in events)
                if (e.kind == MissionEventKind.DustStorm && t >= e.at && t < e.End) f = Math.Min(f, e.severity);
            return f;
        }

        public bool ResupplyBlocked(float t)
        {
            foreach (var e in events)
                if (e.kind == MissionEventKind.ResupplyDelay && t >= e.at && t < e.End) return true;
            return false;
        }

        public static string Name(MissionEventKind kind)
        {
            switch (kind)
            {
                case MissionEventKind.SolarParticle: return "Solar particle event";
                case MissionEventKind.DustStorm: return "Dust storm";
                case MissionEventKind.EquipmentFailure: return "Equipment failure";
                default: return "Resupply delay";
            }
        }

        /// <summary>One true sentence per event for the debrief, in plain language.</summary>
        public static string Lesson(MissionEventKind kind)
        {
            switch (kind)
            {
                case MissionEventKind.SolarParticle:
                    return "The Sun sometimes throws out fast particles. Without Earth's magnetic field, crews on Mars need shelter, and NASA watches the Sun to warn them.";
                case MissionEventKind.DustStorm:
                    return "Mars dust storms can block most sunlight for weeks. Bases need batteries and spare power, because panels can't make electricity in the dark.";
                case MissionEventKind.EquipmentFailure:
                    return "Machines break, and spare parts are millions of kilometres away. Having a backup building matters.";
                default:
                    return "Messages and cargo from Earth take minutes to months, so a Mars base must make most of what it needs itself.";
            }
        }
    }
}
