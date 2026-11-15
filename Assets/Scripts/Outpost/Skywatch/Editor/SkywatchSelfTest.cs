using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RedFrontier.Outpost.EditorTools
{
    /// <summary>Checks the Skywatch clock, weather schedule, NASA data parsing and economy hooks without a scene.</summary>
    public static class SkywatchSelfTest
    {
        [MenuItem("Red Frontier/Skywatch/Run Self Test")]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) fails++; sb.AppendLine((ok ? "PASS " : "FAIL ") + what); }

            // Clock
            var clock = new SolClock();
            Check(clock.Sol == 1 && Mathf.Approximately(clock.Hour, 8f), "clock starts at sol 1, 08:00");
            clock.Tick(SolClock.RealSolSeconds);
            Check(clock.Sol == 2 && Mathf.Abs(clock.Hour - 8f) < .01f, "one sol is " + SolClock.RealSolSeconds + " real seconds");
            var dusk = new SolClock { elapsed = SolClock.Sunset / 24f * SolClock.SolSeconds };
            for (int i = 0; i < 600; i++) dusk.Tick(1f / 60f); // 10 real seconds in frames
            Check(Mathf.Abs(dusk.Hour - (SolClock.Sunset + 10f * SolClock.NightSpeed / SolClock.SolSeconds * 24f)) < .01f,
                "night runs " + SolClock.NightSpeed + "x faster");
            Check(SolClock.Format(13.5f) == "13:30", "time format");
            Check(SolClock.Daylight(0f) == 0f && SolClock.Daylight(12f) == 1f, "panels dark at midnight, full at noon");
            Check(Mathf.Abs(SolClock.Temperature(5.5f, -97f, -16f, 0f) + 97f) < .01f &&
                  Mathf.Abs(SolClock.Temperature(14f, -97f, -16f, 0f) + 16f) < .01f, "temperature hits the InSight low at 05:30 and high at 14:00");
            float stormNight = SolClock.Temperature(5.5f, -97f, -16f, .5f), stormDay = SolClock.Temperature(14f, -97f, -16f, .5f);
            Check(stormNight > -97f && stormDay < -16f, "dust makes nights warmer and days colder");
            Check(SolClock.Coldness(-97f) == 1f && SolClock.Coldness(-16f) == 0f, "heaters work hardest on the coldest night");

            // Data
            var data = MarsWeatherData.LoadFromResources();
            Check(data.error == null, "NASA archives load (" + (data.error ?? "ok") + ")");
            Check(data.sols.Count == 7, "7 InSight sols, got " + data.sols.Count);
            Check(data.archivedArrivals.Count == 29 && data.archivedFlares.Count == 32,
                $"DONKI archive: {data.archivedArrivals.Count} Mars arrivals, {data.archivedFlares.Count} flares");
            const string live = "[{\"simulationID\":\"WSA-ENLIL/1/1\",\"link\":\"https://ccmc.gsfc.nasa.gov/DONKI/view/WSA-ENLIL/1/-1\",\"impactList\":[" +
                "{\"location\":\"Mars\",\"arrivalTime\":\"2026-11-13T12:00Z\",\"isGlancingBlow\":true,\"isMinorImpact\":false}," +
                "{\"location\":\"Earth\",\"arrivalTime\":\"2026-11-12T12:00Z\",\"isGlancingBlow\":false,\"isMinorImpact\":false}]}," +
                "{\"simulationID\":\"WSA-ENLIL/2/1\",\"link\":\"https://ccmc.gsfc.nasa.gov/DONKI/view/WSA-ENLIL/2/-1\",\"impactList\":[" +
                "{\"location\":\"Mars\",\"arrivalTime\":\"2026-11-13T18:00Z\",\"isGlancingBlow\":false,\"isMinorImpact\":false}]}," +
                "{\"simulationID\":\"WSA-ENLIL/3/1\",\"link\":\"https://evil.example.com/x\",\"impactList\":[" +
                "{\"location\":\"Mars\",\"arrivalTime\":\"2026-11-01T00:00Z\",\"isGlancingBlow\":false,\"isMinorImpact\":false}]}]";
            var arrivals = MarsWeatherData.ParseLiveArrivals(live);
            Check(arrivals.Count == 1 && arrivals[0].live && arrivals[0].isGlancingBlow,
                "live parse keeps Mars only, one per day, rejects non-NASA links (" + arrivals.Count + ")");
            Check(MarsWeatherData.ParseLiveArrivals("{\"error\":\"rate limit\"}").Count == 0 && MarsWeatherData.ParseLiveArrivals("").Count == 0,
                "error answers parse to nothing");
            var flares = MarsWeatherData.ParseLiveFlares("[{\"flrID\":\"a\",\"classType\":\"C2.0\",\"peakTime\":\"2026-11-10T00:00Z\",\"link\":\"https://ccmc.gsfc.nasa.gov/x\"}," +
                "{\"flrID\":\"b\",\"classType\":\"X1.1\",\"peakTime\":\"2026-11-11T00:00Z\",\"link\":\"https://ccmc.gsfc.nasa.gov/y\"}]");
            Check(flares.Count == 1 && flares[0].Big, "live flares keep M/X only");
            data.SetLive(arrivals, flares, System.DateTime.UtcNow);
            Check(data.LiveOk && data.donkiStatus.StartsWith("LIVE") && data.FlareBefore(arrivals[0]) == flares[0], "live status and flare-before lookup");
            data.SetLive(new List<MarsWeatherData.Arrival>(), flares, System.DateTime.UtcNow);
            Check(!data.LiveOk && data.donkiStatus.StartsWith("archived"), "no live Mars storm falls back to archived, honestly labelled");

            // Weather schedule over many seeds
            bool fixedOk = true, overlapOk = true, oneAtATime = true;
            int solarSeen = 0, rngEvents = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                var d = new WeatherDirector(data.sols, seed) { PickArrival = f => data.archivedArrivals[0] };
                var started = new Dictionary<int, WeatherKind>();
                int running = 0;
                d.Started += e => { running++; if (running > 1) oneAtATime = false; started[e.sol] = e.kind; if (e.kind == WeatherKind.SolarStorm) solarSeen++; };
                d.Ended += e => running--;
                WeatherEvent last = null;
                d.Started += e => { if (last != null && e.startAbs < last.endAbs) overlapOk = false; last = e; };
                d.Begin(8f);
                for (float t = 8f; t < 24f * 14f; t += .05f) d.Tick(t);
                foreach (var kv in new[] { (2, WeatherKind.DustDevil), (4, WeatherKind.LocalStorm), (6, WeatherKind.SolarStorm) })
                    if (!started.TryGetValue(kv.Item1, out var k) || k != kv.Item2) fixedOk = false;
                foreach (var kv in started) if (!WeatherDirector.FixedSols.ContainsKey(kv.Key)) rngEvents++;
            }
            Check(fixedOk, "fixed intro events on sols 2, 4 and 6 in every run");
            Check(oneAtATime && overlapOk, "never two weather events at once");
            Check(solarSeen >= 40, "solar storms run on real DONKI arrivals (" + solarSeen + ")");
            Check(rngEvents > 40, "random events happen between fixed sols (" + rngEvents + " in 40 runs)");

            var blocked = new WeatherDirector(data.sols, 3) { PickArrival = f => data.archivedArrivals[0], SolarStormAllowed = () => false };
            bool solarStarted = false;
            blocked.Started += e => { if (e.kind == WeatherKind.SolarStorm) solarStarted = true; };
            blocked.Begin(8f);
            for (float t = 8f; t < 24f * 6.9f; t += .05f) blocked.Tick(t);
            Check(!solarStarted && blocked.Scheduled != null && blocked.Scheduled.kind == WeatherKind.SolarStorm,
                "solar storm waits while the radiation exercise runs");

            var nodata = new WeatherDirector(data.sols, 3);
            bool anySolar = false;
            nodata.Started += e => { if (e.kind == WeatherKind.SolarStorm) anySolar = true; };
            nodata.Begin(8f);
            for (float t = 8f; t < 24f * 8f; t += .05f) nodata.Tick(t);
            Check(!anySolar, "no DONKI data: no made-up solar storm");

            // Economy hooks
            var econ = new ColonyEconomy();
            var solar = new BuildingState(BuildingCatalog.Get(BuildingKind.Solar));
            var rocket = new BuildingState(BuildingCatalog.Get(BuildingKind.Rocket));
            econ.buildings.Add(rocket); econ.buildings.Add(solar);
            econ.RecomputeCaps();
            econ.solarFactor = 0f;
            econ.Tick(1f);
            Check(econ[Res.Energy] == 0f, "solar panels make nothing at night");
            econ.solarFactor = .5f;
            econ.Tick(1f);
            Check(Mathf.Abs(econ[Res.Energy] - 1f) < .001f, "solar output scales with sunlight (" + econ[Res.Energy] + ")");

            var hab = new BuildingState(BuildingCatalog.Get(BuildingKind.Habitat));
            var green = new BuildingState(BuildingCatalog.Get(BuildingKind.Greenhouse));
            econ.buildings.Add(hab); econ.buildings.Add(green);
            econ.RecomputeCaps();
            econ.solarFactor = 0f;
            econ[Res.Energy] = 0f; econ[Res.Water] = 50f;
            econ.heatPerBuilding = .25f;
            econ.Tick(1f);
            Check(econ.cold, "no power at night: heaters stop (cold)");
            econ[Res.Energy] = 10f;
            econ.Tick(1f);
            Check(!econ.cold && Mathf.Abs(econ.net[(int)Res.Energy] + .5f + 1f + 1f) < .001f, "heaters are paid first: 2 x 0.25 Energy/s, net " + econ.net[(int)Res.Energy]);
            float food = econ[Res.Food];
            econ.greenhouseFrozen = true;
            econ[Res.Energy] = 50f;
            econ.Tick(1f);
            Check(econ[Res.Food] == food, "frozen greenhouse grows nothing");
            econ.greenhouseFrozen = false;
            econ.greenhouseDimmed = true;
            econ.heatPerBuilding = 0f;
            float e0 = econ[Res.Energy], f0 = econ[Res.Food];
            econ.Tick(1f);
            Check(Mathf.Abs(econ[Res.Food] - f0 - ColonyEconomy.DimmedGrowth) < .001f && Mathf.Abs(e0 - econ[Res.Energy] - 1f) < .001f,
                "dimmed greenhouse: no power, 40% growth (habitat still uses 1 Energy/s)");

            sb.Insert(0, fails == 0 ? "SKYWATCH SELF TEST: ALL PASS\n" : $"SKYWATCH SELF TEST: {fails} FAIL\n");
            return sb.ToString();
        }
    }
}
