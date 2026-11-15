using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using RedFrontier.Audio;
using RedFrontier.Mars;
using UnityEngine;
using UnityEngine.Networking;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Skywatch: the sol clock (bottom-left), day/night, cold nights, weather events and the Real NASA Data screen.
    /// Time and weather logic live in <see cref="SolClock"/> and <see cref="WeatherDirector"/>; this component applies
    /// them to the economy, the crew and the sky, and draws the HUD. Design: Docs/CONCEPT_SKYWATCH.md.
    /// </summary>
    internal sealed class Skywatch : MonoBehaviour
    {
        internal const float HeatPeak = .25f;        // Energy/s per Habitat or Greenhouse on the coldest night
        internal const float WorkingDose = .12f;     // fitness lost by working through a whole direct-hit solar storm
        internal const float CleanCost = .05f;       // fitness spent cleaning the panels by hand
        internal const float CleanAbove = .15f;      // panel dust before the Clean button shows
        const float FetchTimeout = 8f;

        internal static Skywatch Instance { get; private set; }

        internal readonly SolClock clock = new SolClock();
        internal WeatherDirector director;
        internal MarsWeatherData data;
        internal float Temperature { get; private set; }
        internal bool CrewSheltered => sheltering;

        Colony colony;
        Crew crew;
        HistoricalTraining training;
        MissionObjective objective;
        MarsSky sky;
        SpriteRenderer overlay;
        Texture2D overlayTex;
        bool sheltering, firstNightShown, frozenNoticed;
        readonly HashSet<string> usedLive = new HashSet<string>();

        class Card
        {
            public string title, body, source;
            public string[] labels;
            public Action[] actions;
            public float closeAt = -1f;
            public WeatherEvent forEvent;
        }
        readonly List<Card> cards = new List<Card>();
        bool dataOpen;
        Rect clockRect, cardRect;
        Vector2 dataScroll;

        static readonly Color Face = new Color32(34, 26, 40, 235), Edge = new Color32(112, 82, 96, 255),
            ButtonFace = new Color32(70, 52, 70, 255), Cream = new Color32(248, 232, 215, 255), Dim = new Color32(207, 185, 184, 255),
            Live = new Color32(160, 230, 183, 255), Archived = new Color32(255, 205, 130, 255), Cold = new Color32(150, 200, 255, 255);

        internal static bool PointerOverUI(Vector2 guiPoint)
        {
            var s = Instance;
            if (s == null || !s.isActiveAndEnabled || s.colony == null || !s.colony.Ready || MissionPauseMenu.BlocksGameplayInput) return false;
            return s.dataOpen || s.clockRect.Contains(guiPoint) || (s.cards.Count > 0 && s.cardRect.Contains(guiPoint));
        }

        void Awake() => Instance = this;

        void Start()
        {
            colony = GetComponent<Colony>();
            crew = GetComponent<Crew>();
            training = GetComponent<HistoricalTraining>();
            objective = GetComponent<MissionObjective>();
            sky = FindObjectOfType<MarsSky>();
            data = MarsWeatherData.LoadFromResources();
            if (data.error != null) Debug.LogWarning("Skywatch: " + data.error);

            director = new WeatherDirector(data.sols)
            {
                PickArrival = PickArrival,
                FlareFor = data.FlareBefore,
                SolarStormAllowed = SolarStormAllowed,
            };
            director.Forecast += OnForecast;
            director.Started += OnStarted;
            director.Ended += OnEnded;
            director.Begin(clock.AbsHour);
            CreateOverlay();
            StartCoroutine(FetchLive());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (overlay != null) Destroy(overlay.gameObject);
            if (overlayTex != null) Destroy(overlayTex);
        }

        void OnDisable()
        {
            if (colony != null)
            {
                var e = colony.economy;
                e.solarFactor = 1f; e.heatPerBuilding = 0f; e.greenhouseDimmed = e.greenhouseFrozen = false;
            }
            if (sheltering && crew != null) crew.SetSheltering(false);
            sheltering = false;
            if (sky != null) sky.sunDayFraction = float.NaN;
            if (overlay != null) overlay.enabled = false;
        }

        // ---------- Simulation ----------

        void Update()
        {
            if (colony == null || !colony.Ready || director == null) return;
            float dt = Time.deltaTime;
            clock.Tick(dt);
            director.Tick(clock.AbsHour);

            float h = clock.Hour;
            var today = director.Today;
            Temperature = today != null
                ? SolClock.Temperature(h, today.airMinC, today.airMaxC, director.AirDust)
                : SolClock.Temperature(h, -95f, -15f, director.AirDust);

            var e = colony.economy;
            e.solarFactor = director.SolarFactor(h);
            e.heatPerBuilding = HeatPeak * SolClock.Coldness(Temperature);

            if (e.cold && !e.greenhouseFrozen && colony.CountOf(BuildingKind.Greenhouse) > 0)
            {
                e.greenhouseFrozen = true;
                if (!frozenNoticed)
                {
                    frozenNoticed = true;
                    Info("The greenhouse froze!", "The batteries ran out, so the heaters stopped. Plants stop growing until the Sun warms them at noon. " +
                        "More batteries or solar panels keep the heaters on all night.", "Game rule. Real data: InSight measured nights near -97 °C.", 14f);
                }
            }
            if (e.greenhouseFrozen && h >= 12f && h < SolClock.Sunset && !e.cold) e.greenhouseFrozen = false;

            var active = director.Active;
            if (active != null && active.kind == WeatherKind.SolarStorm && !sheltering && crew != null)
                e.fitness = Mathf.Clamp01(e.fitness - StormDoseRate(active) * dt);

            if (!firstNightShown && clock.Sol == 1 && h >= 16.5f)
            {
                firstNightShown = true;
                Info("Night is coming", "The Sun sets at 18:00. Solar panels stop until morning, and the air gets colder than -90 °C! " +
                    "Heaters in the Habitat and Greenhouse use extra power. Batteries save daytime power for the night.",
                    "Real data: NASA InSight lander measured -97 °C at night (sol 675, 19 Oct 2020).", 18f);
            }

            for (int i = cards.Count - 1; i >= 0; i--)
                if (cards[i].closeAt > 0f && Time.time >= cards[i].closeAt) cards.RemoveAt(i);
        }

        void LateUpdate()
        {
            if (colony == null || !colony.Ready) return;
            float h = clock.Hour, sun = SolClock.SunHeight(h);
            if (sky != null) sky.sunDayFraction = SolClock.DayFraction(h);
            if (overlay == null) return;

            var cam = Camera.main;
            if (cam == null) return;
            float darkness = Mathf.Clamp01((.15f - sun) / .45f);
            float dust = director.AirDust;
            var night = new Color(.03f, .04f, .16f, .62f * darkness);
            var storm = new Color(.62f, .38f, .2f, .7f * dust);
            float a = Mathf.Max(night.a, storm.a);
            overlay.enabled = a > .005f;
            var c = a > 0f ? Color.Lerp(night, storm, storm.a / (night.a + storm.a + 1e-4f)) : night;
            c.a = a;
            overlay.color = c;
            float height = cam.orthographicSize * 2.2f;
            overlay.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
            overlay.transform.localScale = new Vector3(height * cam.aspect, height, 1f);
        }

        void CreateOverlay()
        {
            overlayTex = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            overlayTex.SetPixel(0, 0, Color.white);
            overlayTex.Apply();
            var go = new GameObject("Skywatch Light");
            overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = Sprite.Create(overlayTex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            overlay.sortingOrder = 32000; // over the ground, buildings and crew; IMGUI and canvases draw above it
            overlay.enabled = false;
        }

        float StormDoseRate(WeatherEvent e)
        {
            float dose = WorkingDose * (e.arrival != null && e.arrival.isGlancingBlow ? .5f : 1f) * (HasLavaTube() ? .5f : 1f);
            return dose / Mathf.Max(1f, e.DurationHours / 24f * SolClock.SolSeconds);
        }

        bool HasLavaTube() => colony.economy.buildings.Exists(b => b.def.kind == BuildingKind.Habitat && b.Owns("hab3a"));

        bool SolarStormAllowed()
        {
            if (crew == null || training == null) return crew != null;
            switch (training.State)
            {
                case HistoricalTraining.Phase.Resolved:
                case HistoricalTraining.Phase.Unavailable: return true;
                case HistoricalTraining.Phase.Waiting: return objective == null || objective.Stage != MissionObjective.Step.Training;
                default: return false;
            }
        }

        MarsWeatherData.Arrival PickArrival(bool fixedSol)
        {
            // Live first: the newest real storm NASA saw reach Mars that this mission has not used yet.
            foreach (var a in data.liveArrivals)
                if (usedLive.Add(a.simulationID)) return a;
            var pool = data.archivedArrivals;
            if (pool.Count == 0) return null;
            if (fixedSol)
            {
                var direct = pool.FindAll(a => !a.isMinorImpact && !a.isGlancingBlow);
                if (direct.Count > 0) pool = direct;
            }
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        IEnumerator FetchLive()
        {
            var end = DateTime.UtcNow.Date;
            string q = "?startDate=" + end.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                       "&endDate=" + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            List<MarsWeatherData.Arrival> arrivals;
            using (var r = UnityWebRequest.Get(MarsWeatherData.DonkiApi + "WSAEnlilSimulations" + q))
            {
                r.timeout = (int)FetchTimeout;
                yield return r.SendWebRequest();
                if (r.result != UnityWebRequest.Result.Success) { data.SetOffline(r.error); yield break; }
                arrivals = MarsWeatherData.ParseLiveArrivals(r.downloadHandler.text);
            }
            var flares = new List<MarsWeatherData.Flare>();
            using (var r = UnityWebRequest.Get(MarsWeatherData.DonkiApi + "FLR" + q))
            {
                r.timeout = (int)FetchTimeout;
                yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success) flares = MarsWeatherData.ParseLiveFlares(r.downloadHandler.text);
            }
            data.SetLive(arrivals, flares, DateTime.UtcNow);
            // A storm already announced from the archive keeps its card; a live one replaces it if it has not started yet.
            var next = director.For(clock.Sol + 1) ?? director.Scheduled;
            if (data.LiveOk && next != null && next.kind == WeatherKind.SolarStorm && next.arrival != null && !next.arrival.live)
            {
                next.arrival = PickArrival(next.fixedSol);
                next.flare = data.FlareBefore(next.arrival);
                cards.RemoveAll(c => c.forEvent == next);
                OnForecast(next);
            }
        }

        // ---------- Events ----------

        static string Tag(bool live) => live ? "LIVE" : "Archived";

        void OnForecast(WeatherEvent e)
        {
            string body, source;
            if (e.kind == WeatherKind.SolarStorm)
            {
                body = (e.flare != null
                        ? $"Sun watchers saw a big flash on the Sun: a class {e.flare.classType} flare ({MarsWeatherData.Day(e.flare.when)}). "
                        : "Sun watchers saw a cloud of gas leave the Sun. ") +
                       "A solar storm is on its way to Mars and reaches the base tomorrow. Plan where the crew will be!";
                source = $"{Tag(e.arrival.live)} NASA DONKI: predicted to reach Mars on {MarsWeatherData.Day(e.arrival.when)} ({e.arrival.Strength} hit)." +
                         (e.flare != null ? $" Flare {e.flare.flrID}." : "");
            }
            else
            {
                string power = e.kind == WeatherKind.LocalStorm ? "half" : e.kind == WeatherKind.RegionalStorm ? "a quarter of" : "a tenth of";
                body = $"Orbiters see a {WeatherDirector.Title(e.kind).ToLowerInvariant()} coming tomorrow. Solar panels will make only {power} their power. " +
                       "Charge your batteries today!";
                source = "Game rule based on the daily Mars weather reports from NASA's Mars Reconnaissance Orbiter.";
            }
            Info("Weather report: " + WeatherDirector.Title(e.kind), body, source, 16f, e);
        }

        void OnStarted(WeatherEvent e)
        {
            cards.RemoveAll(c => c.forEvent == e);
            var w = e.weather;
            switch (e.kind)
            {
                case WeatherKind.DustDevil:
                    Info("Dust devil!", "A spinning whirlwind of dust is crossing the base. It can blow the dust off solar panels!",
                        "Real fact: dust devils cleaned the panels of NASA's Spirit and Opportunity rovers.", 12f, e);
                    break;
                case WeatherKind.LocalStorm:
                case WeatherKind.RegionalStorm:
                case WeatherKind.GlobalStorm:
                    float light = WeatherDirector.StormLight(e.kind) * 100f;
                    Choice(WeatherDirector.Title(e.kind) + "!",
                        $"Dust blocks the sunlight: panels make only {light:0}% power. The dust also keeps nights a bit warmer. " +
                        "Turn the greenhouse lights down to save power? The plants grow slower while they are dim.",
                        e.kind == WeatherKind.GlobalStorm
                            ? "Real fact: a storm like this covered all of Mars in 2018 and ended NASA's Opportunity rover mission."
                            : "Game rule based on dust storms seen by NASA's Curiosity and Opportunity rovers.",
                        e, ("Dim greenhouse", () => colony.economy.greenhouseDimmed = true),
                        ("Keep lights on", () => colony.economy.greenhouseDimmed = false));
                    break;
                case WeatherKind.SolarStorm:
                    Choice("Solar storm reached Mars!",
                        "Fast particles from the Sun are hitting the base. Shelter: the crew stays safe in the lander but stops building. " +
                        "Keep working: building goes on, but the radiation makes the crew tired." +
                        (HasLavaTube() ? " Your Lava Tube Base blocks half the radiation." : ""),
                        $"{Tag(e.arrival.live)} NASA DONKI model run {e.arrival.simulationID}: {e.arrival.Strength} hit on Mars, {MarsWeatherData.Day(e.arrival.when)}.",
                        e, ("Shelter crew", () => SetShelter(true)), ("Keep working", () => SetShelter(false)));
                    break;
                case WeatherKind.Frost:
                    Info("Frosty morning", "A thin layer of frost covers the ground. It will vanish when the Sun warms the ground.",
                        "Real fact: NASA's Viking 2 and Phoenix landers photographed frost on Mars.", 10f, e);
                    break;
                case WeatherKind.Wind:
                    Info("Windy afternoon",
                        $"Gusts up to {w?.windMax ?? 25f:0} m/s from the {w?.windFrom ?? "west"}! Mars air is so thin that this wind would feel like a gentle breeze. " +
                        "It cannot knock over your rocket.",
                        w != null ? $"Real data: NASA InSight lander, sol {w.sol} ({w.earthDate}), archived." : "Real fact: NASA InSight lander wind data.", 12f, e);
                    break;
            }
        }

        void OnEnded(WeatherEvent e)
        {
            cards.RemoveAll(c => c.forEvent == e);
            int dust = Mathf.RoundToInt(director.panelDust * 100f);
            if (e.IsDustStorm)
            {
                colony.economy.greenhouseDimmed = false;
                Info("The storm is over", $"The sky is clearing. Dust settled on the solar panels: they are now {dust}% dusty.",
                    "Game rule.", 10f);
            }
            else if (e.kind == WeatherKind.DustDevil)
                Info("Panels cleaned", $"The dust devil blew dust off your panels. They are now {dust}% dusty.", "Game rule based on Spirit and Opportunity.", 8f);
            else if (e.kind == WeatherKind.SolarStorm)
            {
                SetShelter(false);
                Info("Solar storm over", "The radiation is back to normal. The crew can work outside again.", "Game rule.", 8f);
            }
        }

        void SetShelter(bool value)
        {
            if (crew == null || sheltering == value) return;
            sheltering = value;
            crew.SetSheltering(value);
        }

        void Info(string title, string body, string source, float seconds, WeatherEvent e = null)
        {
            cards.Add(new Card { title = title, body = body, source = source, closeAt = Time.time + seconds, forEvent = e,
                labels = new[] { "OK" }, actions = new Action[] { null } });
            GameAudio.Play(Sfx.GuideNotify);
        }

        void Choice(string title, string body, string source, WeatherEvent e, (string label, Action act) a, (string label, Action act) b)
        {
            cards.Insert(0, new Card { title = title, body = body, source = source, forEvent = e,
                labels = new[] { a.label, b.label }, actions = new[] { a.act, b.act } });
            GameAudio.Play(Sfx.GuideNotify);
        }

        internal void OpenDataScreen()
        {
            dataOpen = true;
            dataScroll = Vector2.zero;
            GameAudio.Play(Sfx.UiPanelOpen);
        }

        // ---------- HUD ----------

        void OnGUI()
        {
            if (colony == null || !colony.Ready || director == null || MissionPauseMenu.BlocksGameplayInput)
            {
                clockRect = cardRect = default(Rect);
                return;
            }
            GUI.depth = dataOpen ? -10 : 0; // the data screen covers the rest of the HUD
            float s = Mathf.Clamp(Screen.height / 720f, 1f, 1.5f);
            DrawClock(s);
            if (cards.Count > 0) DrawCard(cards[0], s); else cardRect = default(Rect);
            if (dataOpen) DrawData(s);
        }

        static Rect R(Rect r, float x, float y, float w, float h, float s) => new Rect(r.x + x * s, r.y + y * s, w * s, h * s);

        static void Label(Rect r, string text, float size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool wrap = false) =>
            GUI.Label(r, text, OutpostGUI.Text(size, a, c, wrap));

        static bool Button(Rect r, string text, float s, float size = 11f)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            OutpostGUI.Frame(r, hover ? Color.Lerp(ButtonFace, Color.white, .15f) : ButtonFace, Edge, s);
            Label(r, text, size * s, Cream, TextAnchor.MiddleCenter);
            if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) return false;
            GameAudio.Play(Sfx.UiConfirm);
            return true;
        }

        void DrawClock(float s)
        {
            const float w = 220f, h = 142f;
            var r = new Rect(Mathf.Round(16f * s), Mathf.Round(Screen.height - 28f * s - h * s), w * s, h * s);
            clockRect = r;
            OutpostGUI.Frame(r, Face, Edge, s);

            float hour = clock.Hour, sunH = SolClock.SunHeight(hour);
            Label(R(r, 10, 4, 100, 18, s), $"<b>SOL {clock.Sol}</b>", 14f * s, Cream);
            Label(R(r, 110, 4, 100, 18, s), $"<b>{SolClock.Format(hour)}</b>", 14f * s, Cream, TextAnchor.MiddleRight);

            // Sky arc: the Sun rides it by day, Phobos by night. Blue glow near the horizon: Mars sunsets are blue.
            var c = new Vector2(r.x + 110f * s, r.y + 62f * s);
            float radius = 38f * s;
            bool day = sunH > 0f;
            var arcColor = day ? new Color(.85f, .68f, .45f, .55f) : new Color(.45f, .5f, .8f, .5f);
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.PI * i / 16f;
                OutpostGUI.Disc(c + new Vector2(-Mathf.Cos(a), -Mathf.Sin(a)) * radius, 1.3f * s, arcColor);
            }
            OutpostGUI.Panel(new Rect(c.x - radius - 14f * s, c.y, (radius + 14f * s) * 2f, Mathf.Max(1f, Mathf.Round(1.5f * s))), new Color32(176, 96, 64, 255));
            if (day)
            {
                float a = Mathf.PI * Mathf.Clamp01(SolClock.DayFraction(hour));
                var p = c + new Vector2(-Mathf.Cos(a), -Mathf.Sin(a)) * radius;
                if (sunH < .3f) OutpostGUI.Disc(p, 11f * s, new Color(.45f, .65f, 1f, .45f * (1f - sunH / .3f)));
                OutpostGUI.Disc(p, 8f * s, new Color(1f, .9f, .5f, .3f));
                OutpostGUI.Disc(p, 5.5f * s, new Color(1f, .93f, .6f, 1f));
            }
            else
            {
                float nightFrac = Mathf.Repeat(hour - SolClock.Sunset, 24f) / (24f - (SolClock.Sunset - SolClock.Sunrise));
                float a = Mathf.PI * Mathf.Clamp01(nightFrac);
                OutpostGUI.Disc(c + new Vector2(-Mathf.Cos(a), -Mathf.Sin(a)) * radius, 3.5f * s, new Color(.75f, .72f, .7f, 1f));
            }
            Label(R(r, 8, 50, 40, 14, s), "rise", 9f * s, Dim);
            Label(R(r, 172, 50, 40, 14, s), "set", 9f * s, Dim, TextAnchor.MiddleRight);

            var active = director.Active;
            string condition = active != null ? WeatherDirector.Title(active.kind) : day ? "Clear sky" : "Night";
            Label(R(r, 10, 68, 90, 18, s), $"<b>{Temperature:0} °C</b>", 13f * s, Temperature < -60f ? Cold : Cream);
            Label(R(r, 92, 68, 118, 18, s), condition, 12f * s, Cream, TextAnchor.MiddleRight);

            string line;
            if (active != null && active.kind == WeatherKind.SolarStorm)
                line = sheltering ? "<color=#a0e6b7>Radiation: crew sheltered</color>" : "<color=#ff978e>Radiation HIGH: crew outside</color>";
            else if (colony.economy.greenhouseFrozen) line = "<color=#96c8ff>Greenhouse frozen until noon</color>";
            else if (colony.economy.cold) line = "<color=#96c8ff>Heaters off: no power!</color>";
            else
            {
                var next = director.For(clock.Sol + 1) ?? director.For(clock.Sol);
                bool tomorrow = next != null && next.sol > clock.Sol;
                line = next != null && next.Forecastable ? $"{(tomorrow ? "Tomorrow" : "Today")}: {WeatherDirector.Title(next.kind)}" : "Forecast: no storms";
            }
            Label(R(r, 10, 86, 200, 16, s), line, 11f * s, Dim);

            float dust = director.panelDust;
            Label(R(r, 10, 103, 140, 16, s), $"Panels {dust * 100f:0}% dusty", 11f * s, dust >= CleanAbove ? Archived : Dim);
            if (dust >= CleanAbove && !sheltering && Button(R(r, 146, 102, 64, 17, s), "Clean", s))
            {
                director.CleanPanels();
                colony.economy.fitness = Mathf.Clamp01(colony.economy.fitness - CleanCost);
                Info("Panels cleaned", "An astronaut brushed the dust off by hand. Hard work in a spacesuit: fitness -5%.",
                    "Real fact: InSight had no brushes, and dust on its panels ended the mission in 2022.", 8f);
            }
            if (Button(R(r, 8, 121, 204, 17, s), "Real NASA data", s)) OpenDataScreen();
        }

        void DrawCard(Card card, float s)
        {
            float w = 300f * s, pad = 10f * s;
            var body = OutpostGUI.Text(12f * s, TextAnchor.UpperLeft, Cream, true);
            float bodyH = body.CalcHeight(new GUIContent(card.body), w - 2f * pad);
            var src = OutpostGUI.Text(10f * s, TextAnchor.UpperLeft, Dim, true);
            float srcH = src.CalcHeight(new GUIContent(card.source), w - 2f * pad);
            float h = pad + 20f * s + bodyH + 6f * s + srcH + 8f * s + 22f * s + pad;
            var r = new Rect(clockRect.x, clockRect.y - 8f * s - h, w, h);
            cardRect = r;
            OutpostGUI.Frame(r, Face, Edge, s);
            float y = r.y + pad;
            Label(new Rect(r.x + pad, y, w - 2f * pad, 20f * s), $"<b>{card.title}</b>", 14f * s, Cream);
            y += 20f * s;
            GUI.Label(new Rect(r.x + pad, y, w - 2f * pad, bodyH), card.body, OutpostGUI.Text(12f * s, TextAnchor.UpperLeft, Cream, true));
            y += bodyH + 6f * s;
            GUI.Label(new Rect(r.x + pad, y, w - 2f * pad, srcH), $"<i>{card.source}</i>",
                OutpostGUI.Text(10f * s, TextAnchor.UpperLeft, card.source.StartsWith("LIVE") ? Live : Dim, true));
            y += srcH + 8f * s;
            int n = card.labels.Length;
            float bw = (w - 2f * pad - (n - 1) * 8f * s) / n;
            for (int i = 0; i < n; i++)
                if (Button(new Rect(r.x + pad + i * (bw + 8f * s), y, bw, 22f * s), card.labels[i], s, 12f))
                {
                    card.actions[i]?.Invoke();
                    cards.Remove(card);
                    break;
                }
        }

        void DrawData(float s)
        {
            OutpostGUI.Panel(new Rect(0, 0, Screen.width, Screen.height), new Color(.08f, .06f, .1f, .6f));
            float w = Mathf.Min(580f * s, Screen.width - 32f * s), h = Mathf.Min(470f * s, Screen.height - 32f * s), pad = 14f * s;
            var r = new Rect(Mathf.Round((Screen.width - w) / 2f), Mathf.Round((Screen.height - h) / 2f), w, h);
            OutpostGUI.Frame(r, Face, Edge, s);
            Label(new Rect(r.x + pad, r.y + pad, w - 2f * pad, 24f * s), "<b>Real NASA data in this mission</b>", 17f * s, Cream);
            Label(new Rect(r.x + pad, r.y + pad + 24f * s, w - 2f * pad, 18f * s),
                "Where the weather and storms in your game come from.", 11f * s, Dim);

            var w0 = director.Today;
            var entries = new[]
            {
                ("InSight Mars Weather", "A NASA lander measured the air on Mars every day: temperature, wind and air pressure.",
                    "Sets how cold each sol gets, how much power the heaters need, and when dust devils and strong winds happen." +
                    (w0 != null ? $" Today's weather is InSight sol {w0.sol} ({w0.earthDate}): {w0.airMinC:0} °C to {w0.airMaxC:0} °C, gusts {w0.windMax:0} m/s." : ""),
                    "Archived: 7 sols from Oct 2020 (the lander stopped in 2022).", false, MarsWeatherData.InsightPage),
                ("DONKI space weather", "Scientists watch the Sun and predict when its storms will reach Earth, Mars and spacecraft.",
                    "Solar storms in your game use real storms NASA predicted would reach Mars, and real flares seen on the Sun.",
                    data.donkiStatus, data.LiveOk, MarsWeatherData.DonkiPage),
                ("Mission facts", "What NASA rovers and landers taught us: Curiosity, Opportunity, Spirit, InSight, Viking and Phoenix.",
                    "Dust storms, dust on panels, dust devils, frost and blue sunsets. These are game rules based on real missions, not measurements.",
                    "Game rules based on real missions.", false, "https://science.nasa.gov/mars/"),
            };

            var view = new Rect(r.x + pad, r.y + pad + 48f * s, w - 2f * pad, h - 2f * pad - 48f * s - 30f * s);
            // Own copy: OutpostGUI.Text hands out one shared style that every Label call reconfigures.
            var bodyStyle = new GUIStyle(OutpostGUI.Text(11f * s, TextAnchor.UpperLeft, Cream, true));
            float cw = view.width - 18f * s, total = 0f;
            var heights = new float[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var (_, what, does, status, _, _) = entries[i];
                heights[i] = 24f * s + bodyStyle.CalcHeight(new GUIContent(what), cw - 16f * s) + bodyStyle.CalcHeight(new GUIContent("In the game: " + does), cw - 16f * s)
                             + 18f * s + 24f * s + 12f * s;
                total += heights[i] + 8f * s;
            }
            dataScroll = GUI.BeginScrollView(view, dataScroll, new Rect(0, 0, cw, total), false, false);
            float y = 0f;
            for (int i = 0; i < entries.Length; i++)
            {
                var (name, what, does, status, live, url) = entries[i];
                var box = new Rect(0, y, cw, heights[i]);
                OutpostGUI.Frame(box, new Color32(48, 37, 54, 255), Edge, s);
                float x = 8f * s, iy = y + 6f * s, iw = cw - 16f * s;
                Label(new Rect(x, iy, iw, 18f * s), $"<b>{name}</b>", 13f * s, Cream);
                iy += 20f * s;
                float hw = bodyStyle.CalcHeight(new GUIContent(what), iw);
                GUI.Label(new Rect(x, iy, iw, hw), what, OutpostGUI.Text(11f * s, TextAnchor.UpperLeft, Cream, true));
                iy += hw;
                string doesText = "<b>In the game:</b> " + does;
                float hd = bodyStyle.CalcHeight(new GUIContent("In the game: " + does), iw);
                GUI.Label(new Rect(x, iy, iw, hd), doesText, OutpostGUI.Text(11f * s, TextAnchor.UpperLeft, Dim, true));
                iy += hd + 2f * s;
                Label(new Rect(x, iy, iw, 16f * s), status, 10f * s, live ? Live : Archived);
                iy += 18f * s;
                if (Button(new Rect(x, iy, 150f * s, 20f * s), "Open NASA page", s)) Application.OpenURL(url);
                y += heights[i] + 8f * s;
            }
            GUI.EndScrollView();

            if (Button(new Rect(r.xMax - pad - 100f * s, r.yMax - pad - 24f * s, 100f * s, 24f * s), "Close", s, 12f))
            {
                dataOpen = false;
                GameAudio.Play(Sfx.UiPanelClose);
            }
        }
    }
}
