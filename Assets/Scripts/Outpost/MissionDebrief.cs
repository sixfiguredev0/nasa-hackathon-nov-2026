using System.Collections.Generic;
using System.Text;
using RedFrontier.Mars;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// The end-of-mission review, shown on success and on failure. It explains the chain of choices and events
    /// instead of just saying "game over", and offers the same seed again, a new mission, or the title.
    /// </summary>
    internal sealed class MissionDebrief : MonoBehaviour
    {
        const string TitleScene = "Assets/Scenes/MainMenu.unity";
        static readonly Vector2 Reference = new Vector2(960, 540), PanelSize = new Vector2(760, 470);
        static MissionDebrief active;

        internal static bool IsOpen => active != null;

        RectTransform canvasRect;
        EventSystem events;
        bool ownsEvents, loading;
        int seed;

        internal static void Show(MissionDirector director, MissionDirector.Outcome outcome)
        {
            if (active != null) return;
            var go = new GameObject("Mission Debrief");
            var debrief = go.AddComponent<MissionDebrief>();
            debrief.Build(director, outcome);
        }

        void Build(MissionDirector director, MissionDirector.Outcome outcome)
        {
            active = this;
            seed = director.Plan.seed;
            Time.timeScale = 0f;

            var existing = EventSystem.current;
            canvasRect = FrontierUi.CreateCanvas(out events, opaqueBackdrop: false);
            ownsEvents = existing == null;
            canvasRect.name = "Mission Debrief Canvas";
            canvasRect.GetComponent<Canvas>().sortingOrder = 60;
            FrontierUi.Fill("Dim outpost", canvasRect, new Color32(20, 17, 25, 200)).raycastTarget = true;
            var layout = FrontierUi.FitLayout(canvasRect);
            FrontierUi.Resize(canvasRect, Reference, layout);

            var panel = FrontierUi.PanelFrame("Debrief panel", layout);
            FrontierUi.Place(panel, Reference, (Reference.x - PanelSize.x) / 2, (Reference.y - PanelSize.y) / 2, PanelSize.x, PanelSize.y);

            bool all = outcome.survived && outcome.crewAlive >= outcome.crewAtStart;
            string title = outcome.survived ? "MISSION COMPLETE" : "MISSION FAILED";
            var heading = PixelLabel.Create("Title", panel, title, 4, outcome.survived ? (Color)FrontierUi.Cream : FrontierUi.CoralLight, 0f);
            FrontierUi.Place(heading.Rect, PanelSize, 24, 18, 460, 34);

            string summary = all ? "All " + outcome.crewAtStart + " astronauts survived " + outcome.sols + " sols."
                : outcome.survived ? outcome.crewAlive + " of " + outcome.crewAtStart + " astronauts survived " + outcome.sols + " sols."
                : "The crew did not survive Sol " + outcome.sols + " of " + director.Plan.sols + ".";
            var sub = PixelLabel.Create("Summary", panel, summary, 2, FrontierUi.CreamDark, 0f);
            FrontierUi.Place(sub.Rect, PanelSize, 24, 58, PanelSize.x - 48, 18);
            var rule = FrontierUi.Fill("Rule", panel, FrontierUi.PanelLight);
            FrontierUi.Place(rule.rectTransform, PanelSize, 24, 84, PanelSize.x - 48, 1);

            var body = FrontierUi.Box("Review", panel);
            var text = body.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 13;
            text.color = FrontierUi.Cream;
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = Review(director, outcome);
            FrontierUi.Place(body, PanelSize, 24, 94, PanelSize.x - 48, 312);

            var retry = Action(panel, "Try again (same seed)", 24, true);
            retry.onClick.AddListener(() => Reload(seed));
            var fresh = Action(panel, "New mission", 266);
            fresh.onClick.AddListener(() => Reload(new System.Random().Next()));
            var title2 = Action(panel, "Title", 508);
            title2.interactable = Application.CanStreamedLevelBeLoaded(TitleScene);
            title2.onClick.AddListener(ToTitle);
            FrontierUi.Chain(retry, fresh, title2);
            retry.Select();
        }

        static PixelButton Action(Transform panel, string caption, float x, bool primary = false)
        {
            var b = FrontierUi.Button(caption, panel, primary, titleAction: !primary);
            if (primary)
            {
                b.label.Rect.offsetMin = new Vector2(8, 0);
                b.label.Rect.offsetMax = new Vector2(-8, 0);
            }
            FrontierUi.Place((RectTransform)b.transform, PanelSize, x, PanelSize.y - 56, primary ? 232 : (x < 400 ? 232 : 228), 40);
            return b;
        }

        static string Review(MissionDirector d, MissionDirector.Outcome o)
        {
            var sb = new StringBuilder();
            sb.Append("<b><color=#83d5eb>What happened</color></b>\n");
            // skip the "Mission start" line; keep the latest six so the ending is always visible
            int first = Mathf.Max(1, d.Log.Count - 6);
            for (int i = first; i < d.Log.Count; i++)
            {
                var e = d.Log[i];
                sb.Append(e.bad ? "<color=#ff9f8c>- " : "- ").Append(e.text).Append(e.bad ? "</color>" : "").Append('\n');
            }

            if (d.HadLoss)
            {
                sb.Append("\n<b><color=#83d5eb>Why it went wrong</color></b>\n");
                sb.Append(d.FirstLossCause).Append('\n').Append(Advice(d.FirstLossResource)).Append('\n');
            }

            sb.Append("\n<b><color=#83d5eb>What this teaches</color></b>\n");
            var done = new HashSet<MissionEventKind>();
            foreach (var e in d.Plan.events)
                if (e.at <= o.time && done.Count < 3 && done.Add(e.kind))
                {
                    string lesson = MissionPlan.Lesson(e.kind);
                    int dot = lesson.IndexOf(". ");
                    sb.Append("- ").Append(dot > 0 ? lesson.Substring(0, dot + 1) : lesson).Append('\n');
                }
            if (done.Count == 0) sb.Append("- Every choice on Mars has a cost: Metal, power, time or crew fitness.\n");

            sb.Append("\n<color=#d6ba9c>Seed ").Append(d.Plan.seed)
              .Append(" (same seed, same map and events). Solar burst: modeled on an archived NASA DONKI record, not live data.</color>");
            return sb.ToString();
        }

        static string Advice(Res r)
        {
            switch (r)
            {
                case Res.Oxygen: return "Habitats and Greenhouses make air, but they need power. Check batteries before night and storms.";
                case Res.Water: return "Ice Drills need power to run. A second Solar Array and a Battery keep water flowing at night.";
                case Res.Food: return "Greenhouses need water and power. Build one early, and keep crew fit in the Gym so they work at full speed.";
                default: return "Keep life support supplied.";
            }
        }

        void Reload(int newSeed)
        {
            if (loading) return;
            loading = true;
            MarsMapGenerator.SeedOverride = newSeed;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path);
        }

        void ToTitle()
        {
            if (loading) return;
            loading = true;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(TitleScene);
        }

        void OnDestroy()
        {
            if (active == this) active = null;
            if (canvasRect != null) Destroy(canvasRect.gameObject);
            if (ownsEvents && events != null) Destroy(events.gameObject);
        }
    }
}
