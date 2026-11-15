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
        static readonly Vector2 BannerSize = new Vector2(340, 82), TrackerSize = new Vector2(220, 48);
        static readonly Color32 Complete = new Color32(160, 230, 183, 255);

        Colony colony;
        HistoricalTraining training;
        Canvas canvas;
        CanvasGroup banner, tracker;
        RectTransform trackerRect;
        PixelLabel goal, bannerGoal;
        Image check;
        float started, completedAt = -1f;
        internal enum Step { Power, Water, Food, SupportPower, Oxygen, Training, Sandbox }
        internal Step Stage { get; private set; }
        internal string Caption => Captions[(int)Stage];
        static readonly string[] Captions =
        {
            "Restore power", "Drill for water", "Grow food", "Power life support",
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
            var root = new GameObject("Mission Objective Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 24;
            FrontierUi.Resize((RectTransform)root.transform, new Vector2(960, 540));

            var bannerRect = Panel("New objective", root.transform, new Vector2(.5f, 1f), new Vector2(0f, -112f), BannerSize);
            banner = bannerRect.gameObject.AddComponent<CanvasGroup>();
            banner.alpha = 0f;
            Label(bannerRect, BannerSize, "New objective", 2, FrontierUi.CoralLight, 0, 10, BannerSize.x, 22);
            bannerGoal = Label(bannerRect, BannerSize, Caption, 4, FrontierUi.Cream, 0, 32, BannerSize.x, 40);

            trackerRect = Panel("Objective", root.transform, Vector2.one, new Vector2(-12f, -12f), TrackerSize);
            tracker = trackerRect.gameObject.AddComponent<CanvasGroup>();
            tracker.alpha = 0f;
            Label(trackerRect, TrackerSize, "Objective", 1, FrontierUi.CreamDark, 12, 7, 120, 10, 0f);
            var box = PixelArt.Sliced("Checkbox", trackerRect, PixelArt.Frame(FrontierUi.Shadow, FrontierUi.Cream, FrontierUi.PanelDark, FrontierUi.Shadow));
            box.pixelsPerUnitMultiplier = 1f;
            FrontierUi.Place(box.rectTransform, TrackerSize, 12, 21, 16, 16);
            check = FrontierUi.Fill("Check", box.transform, Complete);
            check.rectTransform.offsetMin = new Vector2(4f, 4f);
            check.rectTransform.offsetMax = new Vector2(-4f, -4f);
            check.enabled = false;
            goal = Label(trackerRect, TrackerSize, "Restore power", 2, FrontierUi.Cream, 36, 19, 172, 20, 0f);
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
            bannerGoal.Text = goal.Text = Caption;
            // The last objective ("Survive the mission") stays on screen, unticked, until the mission director ends the run.
        }

        void LateUpdate()
        {
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput;
            // Top right, level with the resource bar, or tucked under it when the screen is too narrow.
            Rect bar = colony.resourceTopBar.Bounds;
            float scale = canvas.scaleFactor, margin = 12f * scale;
            bool beside = bar.width <= 0f || bar.xMax + margin + TrackerSize.x * scale <= Screen.width - margin;
            float top = bar.width <= 0f ? margin : beside ? bar.yMin : bar.yMax + 8f * scale;
            trackerRect.anchoredPosition = new Vector2(-12f, -top / scale);
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var panel = FrontierUi.PanelFrame(name, parent);
            panel.anchorMin = panel.anchorMax = panel.pivot = anchor;
            panel.sizeDelta = size;
            panel.anchoredPosition = position;
            return panel;
        }

        static PixelLabel Label(RectTransform panel, Vector2 size, string text, int scale, Color color,
            float x, float y, float w, float h, float align = .5f)
        {
            var label = PixelLabel.Create(text, panel, text, scale, color, align, true);
            FrontierUi.Place(label.Rect, size, x, y, w, h);
            return label;
        }
    }
}
