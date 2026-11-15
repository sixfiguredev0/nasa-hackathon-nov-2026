using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Real NASA data behind Skywatch: InSight weather sols (archived) and DONKI solar storms that reached Mars
    /// (archived list, replaced by a live fetch when one succeeds). Parsing and validation only, no Unity scene needed.
    /// </summary>
    public class MarsWeatherData
    {
        public const string InsightResource = "Nasa/insight-weather-archived";
        public const string DonkiResource = "Nasa/donki-mars-curated";
        public const string DonkiApi = "https://ccmc.gsfc.nasa.gov/DONKI-API/get/";
        public const string InsightPage = "https://api.nasa.gov/";  // hosts the InSight weather feed
        public const string DonkiPage = "https://ccmc.gsfc.nasa.gov/tools/DONKI/";

        [Serializable] public class InsightSol
        {
            public int sol;
            public string earthDate, windFrom;
            public float airMinC, airMaxC, windAvg, windMax, pressurePa;
        }

        [Serializable] class InsightArchive
        {
            public int schemaVersion;
            public string source, dataMode;
            public InsightSol[] sols;
        }

        [Serializable] public class Arrival
        {
            public string simulationID, arrivalTime, link;
            public bool isGlancingBlow, isMinorImpact;
            [NonSerialized] public DateTime when;
            [NonSerialized] public bool live;
            public string Strength => isMinorImpact ? "minor" : isGlancingBlow ? "glancing" : "direct";
        }

        [Serializable] public class Flare
        {
            public string flrID, classType, peakTime, link;
            [NonSerialized] public DateTime when;
            [NonSerialized] public bool live;
            public bool Big => classType.StartsWith("X", StringComparison.Ordinal);
        }

        [Serializable] class DonkiArchive
        {
            public int schemaVersion;
            public string source, dataMode;
            public Arrival[] arrivals;
            public Flare[] flares;
        }

        // DONKI API answers with top-level arrays; JsonUtility needs an object, so the text is wrapped in {"items": ...}.
        [Serializable] class LiveImpact { public string location, arrivalTime; public bool isGlancingBlow, isMinorImpact; }
        [Serializable] class LiveSim { public string simulationID, link; public LiveImpact[] impactList; }
        [Serializable] class LiveSims { public LiveSim[] items; }
        [Serializable] class LiveFlares { public Flare[] items; }

        public readonly List<InsightSol> sols = new List<InsightSol>();
        public readonly List<Arrival> archivedArrivals = new List<Arrival>();
        public readonly List<Flare> archivedFlares = new List<Flare>();
        public readonly List<Arrival> liveArrivals = new List<Arrival>();
        public readonly List<Flare> liveFlares = new List<Flare>();
        public string error;

        /// <summary>"live" / "archived" plus why, for the data screen.</summary>
        public string donkiStatus = "archived (checking NASA for new storms...)";
        public bool LiveOk { get; private set; }

        public static MarsWeatherData LoadFromResources()
        {
            var d = new MarsWeatherData();
            var insight = Resources.Load<TextAsset>(InsightResource);
            var donki = Resources.Load<TextAsset>(DonkiResource);
            if (!d.ReadInsight(insight != null ? insight.text : null)) d.error = "InSight archive missing or invalid.";
            if (!d.ReadDonkiArchive(donki != null ? donki.text : null)) d.error = (d.error + " DONKI archive missing or invalid.").Trim();
            return d;
        }

        public bool ReadInsight(string json)
        {
            sols.Clear();
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var a = JsonUtility.FromJson<InsightArchive>(json);
                if (a == null || a.schemaVersion != 1 || a.dataMode != "archived" || a.sols == null) return false;
                foreach (var s in a.sols)
                    if (s != null && s.airMinC < s.airMaxC && s.airMinC > -150f && s.airMaxC < 40f && s.windMax >= s.windAvg &&
                        DateTime.TryParse(s.earthDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                        sols.Add(s);
            }
            catch (ArgumentException) { return false; }
            return sols.Count > 0;
        }

        public bool ReadDonkiArchive(string json)
        {
            archivedArrivals.Clear();
            archivedFlares.Clear();
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var a = JsonUtility.FromJson<DonkiArchive>(json);
                if (a == null || a.schemaVersion != 1 || a.dataMode != "archived") return false;
                if (a.arrivals != null) foreach (var x in a.arrivals) if (Valid(x, false)) archivedArrivals.Add(x);
                if (a.flares != null) foreach (var f in a.flares) if (Valid(f, false)) archivedFlares.Add(f);
            }
            catch (ArgumentException) { return false; }
            return archivedArrivals.Count > 0;
        }

        /// <summary>Parses a live WSAEnlilSimulations answer; keeps Mars impacts only, one per arrival day.</summary>
        public static List<Arrival> ParseLiveArrivals(string json)
        {
            var list = new List<Arrival>();
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '[') return list;
            try
            {
                var sims = JsonUtility.FromJson<LiveSims>("{\"items\":" + json + "}");
                if (sims?.items == null) return list;
                var days = new HashSet<DateTime>();
                foreach (var s in sims.items)
                {
                    if (s?.impactList == null) continue;
                    foreach (var i in s.impactList)
                    {
                        if (i == null || i.location != "Mars") continue;
                        var a = new Arrival
                        {
                            simulationID = s.simulationID, link = s.link, arrivalTime = i.arrivalTime,
                            isGlancingBlow = i.isGlancingBlow, isMinorImpact = i.isMinorImpact
                        };
                        if (Valid(a, true) && days.Add(a.when.Date)) list.Add(a);
                    }
                }
            }
            catch (ArgumentException) { }
            return list;
        }

        /// <summary>Parses a live FLR answer; keeps M and X class flares.</summary>
        public static List<Flare> ParseLiveFlares(string json)
        {
            var list = new List<Flare>();
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '[') return list;
            try
            {
                var f = JsonUtility.FromJson<LiveFlares>("{\"items\":" + json + "}");
                if (f?.items != null) foreach (var x in f.items) if (Valid(x, true)) list.Add(x);
            }
            catch (ArgumentException) { }
            return list;
        }

        public void SetLive(List<Arrival> arrivals, List<Flare> flares, DateTime fetchedUtc)
        {
            liveArrivals.Clear(); liveArrivals.AddRange(arrivals);
            liveFlares.Clear(); liveFlares.AddRange(flares);
            liveArrivals.Sort((a, b) => b.when.CompareTo(a.when)); // newest first
            LiveOk = liveArrivals.Count > 0;
            donkiStatus = LiveOk
                ? $"LIVE: {liveArrivals.Count} storm(s) reached Mars in the last 30 days (checked {fetchedUtc:d MMM yyyy HH:mm} UTC)"
                : "archived: NASA reports no storm reaching Mars in the last 30 days, so storms come from Aug-Nov 2026";
        }

        public void SetOffline(string why) =>
            donkiStatus = "archived: could not reach NASA (" + why + "), so storms come from Aug-Nov 2026";

        static bool Valid(Arrival a, bool live)
        {
            if (a == null || string.IsNullOrEmpty(a.simulationID) || !Time(a.arrivalTime, out a.when) || !NasaLink(a.link)) return false;
            a.live = live;
            return true;
        }

        static bool Valid(Flare f, bool live)
        {
            if (f == null || string.IsNullOrEmpty(f.classType) || !(f.classType[0] == 'M' || f.classType[0] == 'X') ||
                !Time(f.peakTime, out f.when) || !NasaLink(f.link)) return false;
            f.live = live;
            return true;
        }

        static bool Time(string s, out DateTime t)
        {
            t = default(DateTime);
            if (string.IsNullOrEmpty(s) ||
                !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o)) return false;
            t = o.UtcDateTime;
            return true;
        }

        static bool NasaLink(string link) =>
            Uri.TryCreate(link, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "ccmc.gsfc.nasa.gov";

        /// <summary>The flare shown as the warning for an arrival: the latest M/X flare in the 5 days before it, or null.</summary>
        public Flare FlareBefore(Arrival a)
        {
            var pool = a.live && liveFlares.Count > 0 ? liveFlares : archivedFlares;
            Flare best = null;
            foreach (var f in pool)
                if (f.when <= a.when && (a.when - f.when).TotalDays <= 5 && (best == null || f.when > best.when)) best = f;
            return best;
        }

        public static string Day(DateTime t) => t.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
