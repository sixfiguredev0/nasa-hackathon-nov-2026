using System.Collections.Generic;
using System.Globalization;
using RedFrontier.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Runs the mission: the sol clock and day/night cycle, the seeded event schedule (<see cref="MissionPlan"/>),
    /// the HUD that shows them, crew deaths and the end of the run. Time here is game time, so Pause freezes it.
    /// </summary>
    internal sealed class MissionDirector : MonoBehaviour
    {
        internal struct Entry
        {
            public float time;
            public string text;
            public bool bad;
        }

        internal sealed class Outcome
        {
            public bool survived;
            public int sols, crewAtStart, crewAlive;
            public float time;
        }

        static readonly Vector2 HudSize = new Vector2(250, 46);
        const float BannerSeconds = 7f;

        internal static MissionDirector Instance { get; private set; }
        internal MissionPlan Plan { get; private set; }
        internal float Elapsed { get; private set; }
        internal bool Ended { get; private set; }
        internal readonly List<Entry> Log = new List<Entry>();
        /// <summary>First crew loss: what ran out, when, and what else was going on.</summary>
        internal string FirstLossCause { get; private set; }
        internal Res FirstLossResource { get; private set; }
        internal bool HadLoss { get; private set; }

        Colony colony;
        HistoricalTraining training;
        Crew crew;
        int crewAtStart;
        bool[] warned, started;
        readonly Dictionary<BuildingState, float> brokenUntil = new Dictionary<BuildingState, float>();
        System.Random pick;
        HistoricalTraining.Phase lastPhase;

        Canvas canvas;
        RectTransform hudRect, bannerRect;
        Text hudTop, hudBottom, bannerText;
        readonly List<(string text, float until, bool bad)> banners = new List<(string, float, bool)>();

        void Awake() => Instance = this;

        internal void ForgetBuilding(BuildingState building) => brokenUntil.Remove(building);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (colony != null) colony.economy.CrewLost -= OnCrewLost;
            if (canvas != null) Destroy(canvas.gameObject);
        }

        void Start()
        {
            colony = GetComponent<Colony>();
            training = GetComponent<HistoricalTraining>();
            crew = GetComponent<Crew>();
            int seed = colony.map != null ? colony.map.LastSeed : 1;
            Plan = new MissionPlan(seed);
            warned = new bool[Plan.events.Count];
            started = new bool[Plan.events.Count];
            pick = new System.Random(seed + 99);
            crewAtStart = colony.economy.crew;
            colony.economy.CrewLost += OnCrewLost;
            lastPhase = training != null ? training.State : HistoricalTraining.Phase.Waiting;
            BuildHud();
            Record("Mission start: survive " + Plan.sols + " sols. Seed " + seed + ".", false);
        }

        void Update()
        {
            if (Ended || colony == null || !colony.Ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Elapsed += dt;
            var eco = colony.economy;
            eco.solarFactor = MissionPlan.DaylightFactor(Elapsed) * Plan.DustFactor(Elapsed);
            eco.resupplyFactor = Plan.ResupplyBlocked(Elapsed) ? 0f : 1f;

            for (int i = 0; i < Plan.events.Count; i++)
            {
                var e = Plan.events[i];
                if (!warned[i] && e.warning > 0f && Elapsed >= e.at - e.warning)
                {
                    warned[i] = true;
                    Announce(WarningText(e), true);
                }
                if (!started[i] && Elapsed >= e.at)
                {
                    started[i] = true;
                    Begin(e);
                }
            }

            if (brokenUntil.Count > 0)
            {
                List<BuildingState> fixedNow = null;
                foreach (var kv in brokenUntil)
                    if (Elapsed >= kv.Value) (fixedNow ?? (fixedNow = new List<BuildingState>())).Add(kv.Key);
                if (fixedNow != null)
                    foreach (var b in fixedNow)
                    {
                        b.broken = false;
                        brokenUntil.Remove(b);
                        Announce(b.Node.name + " is back online.", false);
                    }
            }

            WatchExercise();

            if (eco.crew <= 0) End(false);
            else if (Elapsed >= Plan.Length) End(true);
        }

        string WarningText(MissionEvent e)
        {
            switch (e.kind)
            {
                case MissionEventKind.DustStorm: return "Dust storm approaching: charge your batteries!";
                case MissionEventKind.ResupplyDelay: return "Earth reports a launch delay: no Metal deliveries soon.";
                default: return MissionPlan.Name(e.kind) + " soon";
            }
        }

        void Begin(MissionEvent e)
        {
            string sol = "Sol " + MissionPlan.SolAt(Elapsed);
            switch (e.kind)
            {
                case MissionEventKind.SolarParticle:
                    if (training != null) training.Trigger();
                    Announce("Solar particle burst! Open Marlonis' message and choose.", true);
                    Record(sol + ": solar particle burst (NASA-based event).", true);
                    break;
                case MissionEventKind.DustStorm:
                    Announce("Dust storm! Sunlight down to " + Mathf.RoundToInt(e.severity * 100f) + "% for " + Mathf.RoundToInt(e.duration) + " s.", true);
                    Record(sol + ": dust storm cut sunlight to " + Mathf.RoundToInt(e.severity * 100f) + "% for " + Mathf.RoundToInt(e.duration) + " s.", true);
                    break;
                case MissionEventKind.EquipmentFailure:
                    var candidates = colony.economy.buildings.FindAll(b => b.def.kind != BuildingKind.Rocket && !b.broken);
                    if (candidates.Count == 0) { Record(sol + ": equipment failure (nothing built yet to break).", false); break; }
                    var hit = candidates[pick.Next(candidates.Count)];
                    hit.broken = true;
                    brokenUntil[hit] = Elapsed + e.duration;
                    Announce("Equipment failure: " + hit.Node.name + " is offline for " + Mathf.RoundToInt(e.duration) + " s.", true);
                    Record(sol + ": " + hit.Node.name + " broke down for " + Mathf.RoundToInt(e.duration) + " s.", true);
                    break;
                case MissionEventKind.ResupplyDelay:
                    Announce("Resupply delayed: the lander brings no Metal for one sol.", true);
                    Record(sol + ": Earth's resupply was delayed for one sol.", true);
                    break;
            }
        }

        // Writes the player's answer to the solar particle burst into the log, once.
        void WatchExercise()
        {
            if (training == null || training.State == lastPhase) return;
            lastPhase = training.State;
            string sol = "Sol " + MissionPlan.SolAt(Elapsed);
            if (lastPhase == HistoricalTraining.Phase.Sheltering) Record(sol + ": you sheltered the crew. Building paused " + Mathf.RoundToInt(HistoricalTraining.ExerciseSeconds) + " s.", false);
            else if (lastPhase == HistoricalTraining.Phase.Working)
                Record(sol + (training.Hesitated ? ": you hesitated, so the crew kept working. Fitness fell " : ": you kept working. Fitness fell ") +
                    Mathf.RoundToInt(HistoricalTraining.FitnessDip * 100f) + " points.", true);
        }

        void OnCrewLost(Res cause)
        {
            string sol = "Sol " + MissionPlan.SolAt(Elapsed);
            string during = ActiveEventName();
            string what = cause.ToString().ToLowerInvariant();
            string text = sol + ": an astronaut died because " + what + " ran out" + (during.Length > 0 ? " during the " + during : "") + ".";
            if (!HadLoss)
            {
                HadLoss = true;
                FirstLossResource = cause;
                FirstLossCause = text;
            }
            Record(text, true);
            Announce("An astronaut has died: out of " + what + ".", true);
            GameAudio.Play(Sfx.AlertShortage);
            if (crew != null) crew.LoseOne();
        }

        string ActiveEventName()
        {
            foreach (var e in Plan.events)
                if (e.kind != MissionEventKind.SolarParticle && Elapsed >= e.at && Elapsed < e.End)
                    return MissionPlan.Name(e.kind).ToLowerInvariant();
            return "";
        }

        void Record(string text, bool bad) => Log.Add(new Entry { time = Elapsed, text = text, bad = bad });

        internal void Announce(string text, bool bad)
        {
            banners.Add((text, Elapsed + BannerSeconds, bad));
            if (banners.Count > 3) banners.RemoveAt(0);
            if (bad) GameAudio.Play(Sfx.AlertFitness);
        }

        void End(bool survived)
        {
            if (Ended) return;
            Ended = true;
            colony.economy.solarFactor = 1f;
            colony.economy.resupplyFactor = 1f;
            Record(survived ? "Mission complete after " + Plan.sols + " sols." : "The last astronaut was lost on sol " + MissionPlan.SolAt(Elapsed) + ".", !survived);
            MissionDebrief.Show(this, new Outcome
            {
                survived = survived,
                sols = survived ? Plan.sols : MissionPlan.SolAt(Elapsed),
                crewAtStart = crewAtStart,
                crewAlive = colony.economy.crew,
                time = Elapsed
            });
        }

        // ---- HUD ----

        void BuildHud()
        {
            var root = new GameObject("Mission Director Canvas", typeof(RectTransform), typeof(Canvas));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 23;

            hudRect = FrontierUi.Box("Sol clock", root.transform);
            hudRect.anchorMin = hudRect.anchorMax = hudRect.pivot = new Vector2(.5f, 1f);
            hudRect.sizeDelta = HudSize;
            hudTop = HudText("Sol", hudRect, 18, 2, 4, HudSize.x - 4, 22, TextAnchor.MiddleCenter);
            hudBottom = HudText("Day", hudRect, 13, 2, 25, HudSize.x - 4, 18, TextAnchor.MiddleCenter);

            var box = FrontierUi.Box("Event banner", root.transform);
            bannerRect = box;
            bannerRect.anchorMin = bannerRect.anchorMax = bannerRect.pivot = new Vector2(.5f, 1f);
            bannerRect.sizeDelta = new Vector2(620, 80);
            bannerText = box.gameObject.AddComponent<Text>();
            bannerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bannerText.fontSize = 17;
            bannerText.alignment = TextAnchor.UpperCenter;
            bannerText.supportRichText = true;
            bannerText.raycastTarget = false;
            bannerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var shadow = box.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color32(20, 17, 25, 255);
            shadow.effectDistance = new Vector2(1, -1);
        }

        static Text HudText(string name, RectTransform parent, int size, float x, float y, float w, float h, TextAnchor anchor)
        {
            var box = FrontierUi.Box(name, parent);
            var t = box.gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = anchor;
            t.color = FrontierUi.Cream;
            t.raycastTarget = false;
            t.supportRichText = true;
            FrontierUi.Place(box, HudSize, x, y, w, h);
            return t;
        }

        void LateUpdate()
        {
            if (canvas == null || colony == null || !colony.Ready) return;
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput;

            int sol = Mathf.Min(MissionPlan.SolAt(Elapsed), Plan.sols);
            float phase = MissionPlan.Phase(Elapsed);
            bool day = MissionPlan.IsDay(Elapsed);
            hudTop.text = "SOL " + sol + " / " + Plan.sols;
            float sun = colony.economy.solarFactor;
            if (day)
            {
                string dust = Plan.DustFactor(Elapsed) < 1f ? "  <color=#ff9f8c>DUST</color>" : "";
                hudBottom.text = "Day  -  solar " + Mathf.RoundToInt(sun * 100f) + "%" + dust;
            }
            else
            {
                float toDawn = (1f - phase) * MissionPlan.SolSeconds;
                hudBottom.text = "<color=#83d5eb>Night</color>  -  sunrise in " + Mathf.CeilToInt(toDawn) + " s";
            }

            float scale = Mathf.Clamp(Screen.height / 720f, 1f, 1.5f);
            Rect bar = colony.resourceTopBar.Bounds;
            float w = HudSize.x * scale, left = (Screen.width - w) / 2f;
            bool overlapsBar = bar.width > 0f && bar.xMax > left - 8f && bar.xMin < left + w + 8f;
            float top = overlapsBar ? bar.yMax + 8f * scale : 12f * scale;
            hudRect.localScale = Vector3.one * scale;
            hudRect.anchoredPosition = new Vector2(0f, -top);

            banners.RemoveAll(b => Elapsed >= b.until);
            var sb = new System.Text.StringBuilder();
            foreach (var b in banners)
                sb.Append(b.bad ? "<color=#ff9f8c>" : "<color=#f8e5c8>").Append(b.text).Append("</color>\n");
            bannerText.text = sb.ToString().TrimEnd('\n');
            bannerRect.localScale = Vector3.one * scale;
            bannerRect.anchoredPosition = new Vector2(0f, -(top + (HudSize.y + 8f) * scale));
        }

        internal static string Seconds(float s) => Mathf.RoundToInt(s).ToString(CultureInfo.InvariantCulture) + " s";
    }
}
