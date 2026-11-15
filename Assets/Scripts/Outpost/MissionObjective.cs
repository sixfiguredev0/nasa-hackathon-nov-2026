using UnityEngine;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// The mission objective. Announced when the opening hands over control, then tracked beside the resource bar
    /// until the first Solar Panel is built.
    /// </summary>
    internal sealed class MissionObjective : MonoBehaviour
    {
        const float BannerSeconds = 3.2f, DoneSeconds = 5f;
        static readonly Vector2 BannerSize = new Vector2(340, 82), TrackerSize = new Vector2(220, 48);
        static readonly Color32 Complete = new Color32(160, 230, 183, 255);

        Colony colony;
        Canvas canvas;
        CanvasGroup banner, tracker;
        RectTransform trackerRect;
        PixelLabel goal;
        Image check;
        float started, completedAt = -1f;

        void Start()
        {
            colony = GetComponent<Colony>();
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
            Label(bannerRect, BannerSize, "Restore power", 4, FrontierUi.Cream, 0, 32, BannerSize.x, 40);

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
            float age = Time.time - started;
            banner.alpha = Mathf.Clamp01(age / .3f) * Mathf.Clamp01((BannerSeconds - age) / .4f);
            if (completedAt < 0f)
            {
                tracker.alpha = Mathf.Clamp01((age - BannerSeconds + .4f) / .4f);
                if (!colony.buildings.Exists(b => b.state.def.kind == BuildingKind.Solar && !b.UnderConstruction)) return;
                completedAt = Time.time;
                goal.Text = "Power restored";
                goal.Color = Complete;
                check.enabled = true;
            }
            tracker.alpha = Mathf.Clamp01((completedAt + DoneSeconds - Time.time) / .5f);
            if (Time.time > completedAt + DoneSeconds) Destroy(this);
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
