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

        const float TrainingLinger = 6f; // keep the exercise result on screen before the story moves on

        Colony colony;
        HistoricalTraining training;
        StoryArc story;
        Canvas canvas;
        CanvasGroup banner, tracker;
        RectTransform trackerRect;
        PixelLabel goal, bannerGoal;
        Image check;
        float started, completedAt = -1f, trainingDoneAt = -1f;
        internal enum Step
        {
            Power, Water, Food, SupportPower, Oxygen, Training,
            Salvage, StorePower, RepairRadio, Signal, RepairHull, Fuel, Decide, Sandbox
        }
        internal Step Stage { get; private set; }
        internal string Caption => Captions[(int)Stage];
        static readonly string[] Captions =
        {
            "Restore power", "Drill for water", "Grow food", "Power life support",
            "Supply oxygen", "Historical exercise", "Salvage metal", "Store power", "Repair the radio",
            "Wait for the orbiter", "Repair the ship", "Make rocket fuel", "Make your choice", "Keep exploring"
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
                    case Step.RepairHull: return "Right-click the wrecked ship and buy Repaired Ship. Then I can take you home.";
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
            // The story runs the solar particle exercise as its own step; MissionDirector's scheduled burst then skips it.
            if (Stage == Step.Training && training != null && !training.Triggered && Time.time - started >= 4f) training.Trigger();
            while ((Stage == Step.Power && Has(BuildingKind.Solar)) ||
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
            bannerGoal.Text = goal.Text = Caption;
            RefreshLiveGoal();
            if (Stage != Step.Sandbox) return;
            completedAt = Time.time;
            goal.Color = Complete;
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
            if (story == null) return;
            if (Stage == Step.Signal) goal.Text = "Orbiter in " + Mathf.CeilToInt(story.OrbiterIn) + "s";
            else if (Stage == Step.Fuel) goal.Text = "Rocket fuel " + Mathf.FloorToInt(story.Fuel * 100f) + "%";
        }

        void LateUpdate()
        {
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput && !StoryArc.Cinematic;
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
