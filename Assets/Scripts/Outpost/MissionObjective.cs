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

        Colony colony;
        HistoricalTraining training;
        Canvas canvas;
        CanvasGroup banner, tracker;
        RectTransform bannerRect, trackerRect;
        Text goal, bannerGoal;
        Image check;
        float started, completedAt = -1f;
        internal enum Step { Power, Water, Food, SupportPower, Oxygen, Training, Sandbox }
        internal Step Stage { get; private set; }
        internal string Caption => Captions[(int)Stage];
        static readonly string[] Captions =
        {
            "Build solar panels", "Drill for water", "Grow food", "Power life support",
            "Supply oxygen", "Historical exercise", "Survive the mission"
        };

        internal string Guidance
        {
            get
            {
                switch (Stage)
                {
                    case Step.Power: return "Build Solar Panels for power. In Build, place a green blueprint.";
                    case Step.Water: return "Build an Ice Drill for drinking water. It needs power from your Solar Panels.";
                    case Step.Food: return "Build a Greenhouse for food. Air runs low fast, so a Habitat must follow soon.";
                    case Step.SupportPower: return "Add Solar Panels on a good site. Life support needs more power.";
                    case Step.Oxygen: return "Build a Habitat Dome for oxygen. Its air machine needs power.";
                    case Step.Training: return "Supplies are ready. Build a Gym and batteries before trouble finds you.";
                    default: return "Survive " + MissionPlan.DefaultSols + " sols. Solar stops at night, so keep batteries and a Gym. Right-click buildings to upgrade.";
                }
            }
        }

        void Start()
        {
            colony = GetComponent<Colony>();
            training = GetComponent<HistoricalTraining>();
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
            while ((Stage == Step.Power && Has(BuildingKind.Solar)) ||
                   (Stage == Step.Water && Has(BuildingKind.Water)) ||
                   (Stage == Step.Food && Has(BuildingKind.Greenhouse)) ||
                   (Stage == Step.SupportPower && HasSupportPower()) ||
                   (Stage == Step.Oxygen && Has(BuildingKind.Habitat)) ||
                   Stage == Step.Training) // the solar particle event now arrives on the mission schedule, not as a tutorial step
                Stage++;
            if (before == Stage) return;
            started = Time.time;
            bannerGoal.text = goal.text = Caption;
            // The last objective ("Survive the mission") stays on screen, unticked, until the mission director ends the run.
        }

        void LateUpdate()
        {
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput;
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
