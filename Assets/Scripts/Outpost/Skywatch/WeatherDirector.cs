using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    public enum WeatherKind { None, DustDevil, LocalStorm, RegionalStorm, GlobalStorm, SolarStorm, Frost, Wind }

    public class WeatherEvent
    {
        public WeatherKind kind;
        public int sol;
        /// <summary>Hours since 00:00 on sol 1 (<see cref="SolClock.AbsHour"/>).</summary>
        public float startAbs, endAbs;
        public bool fixedSol;
        public MarsWeatherData.InsightSol weather;   // the real InSight sol that day (Wind uses its gusts)
        public MarsWeatherData.Arrival arrival;      // SolarStorm only
        public MarsWeatherData.Flare flare;          // SolarStorm only, may be null

        public bool IsDustStorm => kind == WeatherKind.LocalStorm || kind == WeatherKind.RegionalStorm || kind == WeatherKind.GlobalStorm;
        /// <summary>Storms are announced a sol ahead (like MRO's daily Mars weather report); dust devils, frost and wind are surprises.</summary>
        public bool Forecastable => IsDustStorm || kind == WeatherKind.SolarStorm;
        public float DurationHours => endAbs - startAbs;
    }

    /// <summary>
    /// Picks the weather: fixed events on intro sols, otherwise one random roll per sol, never two events at once.
    /// Each sol also gets one real InSight weather sol (its low/high temperature and wind). Plain C# for tests.
    /// </summary>
    public class WeatherDirector
    {
        /// <summary>Each event type is introduced once, in a safe order (design decision, 2026-11-15).</summary>
        public static readonly Dictionary<int, WeatherKind> FixedSols = new Dictionary<int, WeatherKind>
        {
            { 2, WeatherKind.DustDevil }, { 4, WeatherKind.LocalStorm }, { 6, WeatherKind.SolarStorm }, { 10, WeatherKind.RegionalStorm },
        };

        // Chances per sol (starting point, tune in playtests). Rolled in this order; the rest of the time: clear sky.
        public const float GlobalChance = .01f, RegionalChance = .04f, LocalChance = .12f, SolarChance = .05f,
            DustDevilChance = .35f, WindyDustDevilChance = .45f, FrostChance = .10f, WindChance = .20f;
        public const int GlobalAfterSol = 10;
        public const float WindyGust = 22f, StrongWindGust = 24f; // m/s, InSight wind gusts

        public const float DustPerSol = .03f, MaxDust = .4f, DevilCleans = .6f;

        readonly System.Random rng;
        readonly List<MarsWeatherData.InsightSol> sols;
        MarsWeatherData.InsightSol tomorrow;
        float lastAbs = float.NaN;

        readonly List<WeatherEvent> pending = new List<WeatherEvent>();
        /// <summary>The next event that has not started yet (planned a sol ahead), or null.</summary>
        public WeatherEvent Scheduled => pending.Count > 0 ? pending[0] : null;
        public WeatherEvent Active { get; private set; }
        public MarsWeatherData.InsightSol Today { get; private set; }
        /// <summary>0..<see cref="MaxDust"/>: dust on the solar panels, lost output.</summary>
        public float panelDust;

        /// <summary>Real solar storm for a sol (true = fixed intro sol). Null = no data, the storm is skipped.</summary>
        public Func<bool, MarsWeatherData.Arrival> PickArrival = fixedSol => null;
        public Func<MarsWeatherData.Arrival, MarsWeatherData.Flare> FlareFor = a => null;
        /// <summary>False while another radiation exercise runs; the storm then waits 2 hours.</summary>
        public Func<bool> SolarStormAllowed = () => true;

        public event Action<WeatherEvent> Forecast, Started, Ended;

        public WeatherDirector(List<MarsWeatherData.InsightSol> sols, int? seed = null)
        {
            this.sols = sols ?? new List<MarsWeatherData.InsightSol>();
            rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public void Begin(float abs)
        {
            lastAbs = abs;
            Today = PickSol();
            int sol = 1 + Mathf.FloorToInt(abs / 24f);
            Plan(sol + 1);
        }

        public void Tick(float abs)
        {
            if (float.IsNaN(lastAbs)) Begin(abs);
            // Dawns crossed since the last tick (06:00 of each sol).
            for (int sol = 1 + Mathf.FloorToInt((lastAbs - SolClock.Sunrise) / 24f) + 1; ; sol++)
            {
                float dawn = (sol - 1) * 24f + SolClock.Sunrise;
                if (dawn > abs) break;
                if (dawn > lastAbs) Dawn(sol);
            }

            var next = Scheduled;
            if (Active == null && next != null && abs >= next.startAbs)
            {
                if (next.kind == WeatherKind.SolarStorm && !SolarStormAllowed())
                { next.startAbs += 2f; next.endAbs += 2f; }
                else { Active = next; pending.RemoveAt(0); Started?.Invoke(Active); }
            }
            if (Active != null && abs >= Active.endAbs)
            {
                var e = Active;
                Active = null;
                if (e.kind == WeatherKind.DustDevil) panelDust *= 1f - DevilCleans;
                if (e.IsDustStorm) panelDust = Mathf.Min(MaxDust, panelDust + StormDust(e.kind)); // dust settles when it clears
                Ended?.Invoke(e);
            }
            lastAbs = abs;
        }

        void Dawn(int sol)
        {
            Today = tomorrow ?? PickSol();
            tomorrow = null;
            panelDust = Mathf.Min(MaxDust, panelDust + DustPerSol);
            // Plan tomorrow only if everything already running or planned is over before tomorrow's earliest event (frost, 04:00).
            float busyUntil = Active != null ? Active.endAbs : 0f;
            foreach (var e in pending) busyUntil = Mathf.Max(busyUntil, e.endAbs);
            if (busyUntil <= sol * 24f + 4f) Plan(sol + 1);
        }

        void Plan(int sol)
        {
            tomorrow = PickSol();
            bool isFixed = FixedSols.TryGetValue(sol, out var kind);
            if (!isFixed) kind = Roll(sol, tomorrow);
            if (kind == WeatherKind.None) return;

            var e = new WeatherEvent { kind = kind, sol = sol, fixedSol = isFixed, weather = tomorrow };
            float start, hours;
            switch (kind)
            {
                case WeatherKind.DustDevil: start = Range(12f, 16f); hours = 1.5f; break;   // afternoon whirlwinds
                case WeatherKind.LocalStorm: start = Range(8f, 11f); hours = 20f; break;
                case WeatherKind.RegionalStorm: start = Range(8f, 11f); hours = Range(44f, 68f); break;
                case WeatherKind.GlobalStorm: start = 9f; hours = 96f; break;
                case WeatherKind.SolarStorm:
                    e.arrival = PickArrival(isFixed);
                    if (e.arrival == null) return;
                    e.flare = FlareFor(e.arrival);
                    start = Range(9f, 14f);
                    hours = e.arrival.isMinorImpact ? 6f : e.arrival.isGlancingBlow ? 9f : 12f;
                    break;
                case WeatherKind.Frost: start = 4f; hours = 5f; break;                      // burns off mid-morning
                default: start = Range(11f, 14f); hours = 5f; break;                         // Wind
            }
            e.startAbs = (sol - 1) * 24f + start;
            e.endAbs = e.startAbs + hours;
            if (!isFixed) // a random multi-sol storm must not swallow an intro sol
                foreach (var f in FixedSols.Keys)
                    if (f > sol && (f - 1) * 24f + 4f < e.endAbs) return;
            pending.Add(e);
            if (e.Forecastable) Forecast?.Invoke(e);
        }

        WeatherKind Roll(int sol, MarsWeatherData.InsightSol w)
        {
            bool windy = w != null && w.windMax > WindyGust, strongWind = w != null && w.windMax >= StrongWindGust;
            float r = (float)rng.NextDouble(), c = 0f;
            if (sol > GlobalAfterSol && r < (c += GlobalChance)) return WeatherKind.GlobalStorm;
            if (r < (c += RegionalChance)) return WeatherKind.RegionalStorm;
            if (r < (c += LocalChance)) return WeatherKind.LocalStorm;
            if (r < (c += SolarChance)) return WeatherKind.SolarStorm;
            if (r < (c += windy ? WindyDustDevilChance : DustDevilChance)) return WeatherKind.DustDevil;
            if (r < (c += FrostChance)) return WeatherKind.Frost;
            if (strongWind && r < (c += WindChance)) return WeatherKind.Wind;
            return WeatherKind.None;
        }

        MarsWeatherData.InsightSol PickSol() => sols.Count == 0 ? null : sols[rng.Next(sols.Count)];
        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void CleanPanels() => panelDust = 0f;

        /// <summary>The planned event for a sol, or null.</summary>
        public WeatherEvent For(int sol) => pending.Find(e => e.sol == sol);

        // Storm strength. Light reaching the panels and how much the dust flattens the day/night temperature swing.
        public static float StormLight(WeatherKind k) =>
            k == WeatherKind.LocalStorm ? .5f : k == WeatherKind.RegionalStorm ? .25f : k == WeatherKind.GlobalStorm ? .1f : 1f;
        public static float StormAirDust(WeatherKind k) =>
            k == WeatherKind.LocalStorm ? .35f : k == WeatherKind.RegionalStorm ? .55f : k == WeatherKind.GlobalStorm ? .75f : 0f;
        static float StormDust(WeatherKind k) =>
            k == WeatherKind.LocalStorm ? .08f : k == WeatherKind.RegionalStorm ? .15f : .25f;

        WeatherKind ActiveKind => Active != null ? Active.kind : WeatherKind.None;
        public float AirDust => StormAirDust(ActiveKind);
        /// <summary>0..1 multiplier for Solar Arrays: sunlight now, storm dust in the air, dust on the panels.</summary>
        public float SolarFactor(float hour) => SolClock.Daylight(hour) * StormLight(ActiveKind) * (1f - panelDust);

        public static string Title(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.DustDevil: return "Dust devil";
                case WeatherKind.LocalStorm: return "Dust storm";
                case WeatherKind.RegionalStorm: return "Big dust storm";
                case WeatherKind.GlobalStorm: return "Planet-wide dust storm";
                case WeatherKind.SolarStorm: return "Solar storm";
                case WeatherKind.Frost: return "Frosty morning";
                case WeatherKind.Wind: return "Windy";
                default: return "Clear sky";
            }
        }
    }
}
