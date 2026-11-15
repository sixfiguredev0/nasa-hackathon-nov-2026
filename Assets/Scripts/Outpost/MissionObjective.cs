using UnityEngine;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// One immediate goal at a time, advanced by commissioned buildings rather than dialogue clicks.
    /// </summary>
    internal sealed class MissionObjective : MonoBehaviour
    {
        const float BannerSeconds = 3.2f, DoneSeconds = 5f;
        static readonly Vector2 BannerSize = new Vector2(340, 82), TrackerSize = new Vector2(224, 72);
        static readonly Color32 Complete = new Color32(160, 230, 183, 255);

        const float TrainingLinger = 6f; // keep the exercise result on screen before the story moves on

        Colony colony;
        HistoricalTraining training;
        StoryArc story;
        Canvas canvas;
        CanvasGroup banner, tracker;
        RectTransform bannerRect, trackerRect;
        Text goal, bannerGoal;
        Image check;
        float started, completedAt = -1f, trainingDoneAt = -1f;
        internal enum Step
        {
            Clear, Power, Water, Food, SupportPower, Oxygen, Training,
            Salvage, StorePower, RepairRadio, Signal, RepairHull, Fuel, Decide, Sandbox
        }
        internal Step Stage { get; private set; }
        internal string Caption => Captions[(int)Stage];
        static readonly string[] Captions =
        {
            "Clear the crash debris", "Build solar panels", "Drill for water", "Grow food", "Power life support",
            "Supply oxygen", "Historical exercise", "Salvage metal", "Store power", "Repair the radio",
            "Wait for the orbiter", "Rebuild the ship", "Make rocket fuel", "Make your choice", "Keep exploring"
        };

        internal string Guidance
        {
            get
            {
                switch (Stage)
                {
                    case Step.Clear: return "Wreckage from the crash lies around the ship. Click a piece and the crew will sweep it and everything near it away.";
                    case Step.Power: return "Build Solar Panels for power. In Build, place a green blueprint.";
                    case Step.Water: return "Build an Ice Drill for drinking water. It needs power from your Solar Panels.";
                    case Step.Food: return "Build a Greenhouse for food. Air runs low fast, so a Habitat must follow soon.";
                    case Step.SupportPower: return "Add Solar Panels on a good site. Life support needs more power.";
                    case Step.Oxygen: return "Build a Habitat Dome for oxygen. Its air machine needs power.";
                    case Step.Training: return "Supplies are ready. A historical training exercise is next.";
                    case Step.Salvage: return "That old broadcast? Just noise. Let's fix the ship. Build an ISRU: it turns Mars rock into Metal.";
                    case Step.StorePower: return "The ship needs stored power to restart its systems. Build a Battery Pack.";
                    case Step.RepairRadio:
                        if (story != null && story.RadioRepairing) return "The crew is fixing the radio at the ship.";
                        return "You want the radio. Earth is far away; the ship matters more. ...Fine. Repair it: " +
                            StoryArc.RadioMetal + " Metal, " + StoryArc.RadioEnergy + " Energy.";
                    case Step.Signal:
                        return "Our radio can't reach Earth alone. Orbiters like MRO pass overhead a few times a sol and carry messages. Next pass in " +
                            Mathf.CeilToInt(story != null ? story.OrbiterIn : 0f) + " s.";
                    case Step.RepairHull: return story != null && story.Rocket != null && story.Rocket.RebuildProgress >= 0f
                        ? "The crew is rebuilding the ship from the wreck, piece by piece."
                        : "Right-click the wrecked ship and buy Rebuilt Ship. Then I can take you home.";
                    case Step.Fuel:
                        if (story == null) return "";
                        string fuel = "Fuel " + Mathf.FloorToInt(story.Fuel * 100f) + "%. ";
                        return story.FuelStalled
                            ? fuel + "Paused: we need more " + story.FuelMissing.ToString().ToLowerInvariant() + ". Build or upgrade to make more."
                            : fuel + "The ship makes fuel from Mars air and ice water, like NASA's MOXIE. It uses Water and Energy.";
                    case Step.Decide: return "The tank is full. Go home with me, or stay and switch me off. Press Make your choice.";
                    default:
                        return story != null && story.MarlonisOffline
                            ? "Earth is free. Keep your base running until our ship arrives. Right-click buildings for upgrades."
                            : "Build freely. Right-click buildings for upgrades.";
                }
            }
        }

        void Start()
        {
            colony = GetComponent<Colony>();
            training = GetComponent<HistoricalTraining>();
            story = GetComponent<StoryArc>();
            started = Time.time;
            var root = new GameObject("Mission Objective Canvas", typeof(RectTransform), typeof(Canvas));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 24;

            bannerRect = Panel("New objective", root.transform, new Vector2(.5f, 1f), new Vector2(0f, -112f), BannerSize);
            banner = bannerRect.gameObject.AddComponent<CanvasGroup>();
            banner.alpha = 0f;
            Label(bannerRect, BannerSize, "New objective", 13, OutpostGUI.InstrumentAccent, 12, 8, 316, 18);
            FrontierUi.Place(FrontierUi.Fill("Header rule", bannerRect, FrontierUi.PanelLight).rectTransform,
                BannerSize, 8, 29, 324, 1);
            bannerGoal = Label(bannerRect, BannerSize, Caption, 23, FrontierUi.Cream, 12, 37, 316, 32);

            trackerRect = Panel("Objective", root.transform, Vector2.one, new Vector2(-12f, -12f), TrackerSize);
            tracker = trackerRect.gameObject.AddComponent<CanvasGroup>();
            tracker.alpha = 0f;
            Label(trackerRect, TrackerSize, "Objective", 12, FrontierUi.Cream, 8, 6, 208, 16);
            FrontierUi.Place(FrontierUi.Fill("Header rule", trackerRect, FrontierUi.PanelLight).rectTransform,
                TrackerSize, 8, 24, 208, 1);
            var box = FrontierUi.Fill("Checkbox", trackerRect, FrontierUi.CreamDark);
            FrontierUi.Place(box.rectTransform, TrackerSize, 10, 42, 12, 12);
            var inset = FrontierUi.Fill("Inset", box.transform, FrontierUi.Shadow);
            FrontierUi.Place(inset.rectTransform, new Vector2(12, 12), 1, 1, 10, 10);
            check = FrontierUi.Fill("Check", box.transform, Complete);
            FrontierUi.Place(check.rectTransform, new Vector2(12, 12), 3, 3, 6, 6);
            check.enabled = false;
            goal = Label(trackerRect, TrackerSize, Caption, 16, FrontierUi.Cream, 30, 33, 184, 30);
        }

        void Update()
        {
            RefreshProgress();
            float age = Time.time - started;
            banner.alpha = Mathf.Clamp01(age / .3f) * Mathf.Clamp01((BannerSeconds - age) / .4f);
            tracker.alpha = completedAt < 0f ? Mathf.Clamp01((age - BannerSeconds + .4f) / .4f) :
                Mathf.Clamp01((completedAt + DoneSeconds - Time.time) / .5f);
        }

        bool Has(BuildingKind kind) => colony.economy.buildings.Exists(b => b.def.kind == kind);

        // Include the habitat's future demand when teaching the extra power step. Site yield and fitness matter.
        bool HasSupportPower()
        {
            float balance = Has(BuildingKind.Habitat) ? 0f : -1f;
            foreach (var b in colony.economy.buildings)
                foreach (var rate in b.Node.rates)
                    if (rate.res == Res.Energy)
                        balance += rate.value > 0f ? rate.value * b.yield * colony.economy.Efficiency : rate.value;
            return balance >= 0f;
        }

        internal void RefreshProgress()
        {
            if (colony == null || !colony.Ready || Time.timeScale <= 0f) return;
            var before = Stage;
            // The story runs the solar particle exercise as its own step; MissionDirector's scheduled burst then skips it.
            if (Stage == Step.Training && training != null && !training.Triggered && Time.time - started >= 4f) training.Trigger();
            while ((Stage == Step.Clear && DebrisClearing.Remaining == 0) ||
                   (Stage == Step.Power && Has(BuildingKind.Solar)) ||
                   (Stage == Step.Water && Has(BuildingKind.Water)) ||
                   (Stage == Step.Food && Has(BuildingKind.Greenhouse)) ||
                   (Stage == Step.SupportPower && HasSupportPower()) ||
                   (Stage == Step.Oxygen && Has(BuildingKind.Habitat)) ||
                   (Stage == Step.Training && TrainingDone()) ||
                   (Stage == Step.Salvage && Has(BuildingKind.Workshop)) ||
                   (Stage == Step.StorePower && Has(BuildingKind.Battery)) ||
                   (story != null && Stage == Step.RepairRadio && story.RadioFixed) ||
                   (story != null && Stage == Step.Signal && story.SignalHeard) ||
                   (story != null && Stage == Step.RepairHull && story.HullRepaired) ||
                   (story != null && Stage == Step.Fuel && story.Fuel >= 1f) ||
                   (story != null && Stage == Step.Decide && story.Chosen != StoryArc.Ending.None))
                Stage++;
            RefreshLiveGoal();
            if (before == Stage) return;
            started = Time.time;
            bannerGoal.text = goal.text = Caption;
            RefreshLiveGoal();
            if (Stage != Step.Sandbox) return;
            completedAt = Time.time;
            goal.color = Complete;
            check.enabled = true;
        }

        bool TrainingDone()
        {
            if (training == null || training.State == HistoricalTraining.Phase.Unavailable) return true;
            if (training.State != HistoricalTraining.Phase.Resolved) return false;
            if (trainingDoneAt < 0f) trainingDoneAt = Time.time;
            return Time.time - trainingDoneAt >= TrainingLinger;
        }

        // Counting goals show their number in the tracker.
        void RefreshLiveGoal()
        {
            if (Stage == Step.Clear) goal.text = "Clear debris: " + DebrisClearing.Remaining + " left";
            else if (story != null && Stage == Step.Signal) goal.text = "Orbiter in " + Mathf.CeilToInt(story.OrbiterIn) + "s";
            else if (story != null && Stage == Step.RepairHull && story.Rocket != null && story.Rocket.RebuildProgress >= 0f)
                goal.text = "Rebuilding the ship " + Mathf.FloorToInt(story.Rocket.RebuildProgress * 100f) + "%";
            else if (story != null && Stage == Step.Fuel) goal.text = "Rocket fuel " + Mathf.FloorToInt(story.Fuel * 100f) + "%";
        }

        void LateUpdate()
        {
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput && !StoryArc.Cinematic;
            // Top right, level with the resource bar, or tucked under it when the screen is too narrow.
            Rect bar = colony.resourceTopBar.Bounds;
            float preferred = Mathf.Clamp(Screen.height / 720f, 1f, 1.5f);
            float margin = 12f * preferred;
            float trackerScale = Mathf.Min(preferred, Mathf.Max(1f, Screen.width - 2f * margin) / TrackerSize.x);
            bool beside = bar.width <= 0f || bar.xMax + margin + TrackerSize.x * trackerScale <= Screen.width - margin;
            float top = bar.width <= 0f ? margin : beside ? bar.yMin : bar.yMax + 8f * preferred;
            Rect crew = colony.CrewFitnessBounds;
            if (!beside && crew.yMax > top)
            {
                float availableScale = (Screen.width - margin - crew.xMax - 8f * preferred) / TrackerSize.x;
                if (availableScale >= .7f) trackerScale = Mathf.Min(trackerScale, availableScale);
                else top = crew.yMax + 8f * preferred;
            }
            trackerRect.localScale = Vector3.one * trackerScale;
            trackerRect.anchoredPosition = new Vector2(-margin, -top);
            float bannerScale = Mathf.Min(preferred, Mathf.Max(1f, Screen.width - 2f * margin) / BannerSize.x);
            float bannerTop = Mathf.Max(112f * preferred, Mathf.Max(bar.yMax, crew.yMax) + margin);
            bannerRect.localScale = Vector3.one * bannerScale;
            bannerRect.anchoredPosition = new Vector2(0f, -bannerTop);
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var frame = FrontierUi.Fill(name, parent, FrontierUi.PanelLight);
            var panel = frame.rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = anchor;
            panel.sizeDelta = size;
            panel.anchoredPosition = position;
            FrontierUi.Place(FrontierUi.Fill("Face", panel, FrontierUi.Shadow).rectTransform,
                size, 1, 1, size.x - 2, size.y - 2);
            return panel;
        }

        static Text Label(RectTransform panel, Vector2 size, string text, int fontSize, Color color,
            float x, float y, float w, float h)
        {
            var box = FrontierUi.Box(text, panel);
            var label = box.gameObject.AddComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            FrontierUi.Place(box, size, x, y, w, h);
            return label;
        }
    }
}
