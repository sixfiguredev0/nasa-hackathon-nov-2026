using RedFrontier.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    internal sealed class MissionGuide : MonoBehaviour
    {
        static MissionGuide active;
        static readonly Vector2 PanelSize = new Vector2(414, 87);
        static readonly Vector2 EventSize = new Vector2(550, 178), SourceSize = new Vector2(550, 270);
        static readonly Vector2 Reference = new Vector2(960, 540);

        static readonly Color32 BlueFace = new Color32(34, 40, 55, 255);
        static readonly Color32 BlueEdge = new Color32(103, 152, 196, 255);
        static readonly Color32 Cyan = new Color32(131, 213, 235, 255);

        static readonly Vector2 NotificationSize = new Vector2(42, 38);

        RectTransform panel, notificationRect, textBox, portraitRect, speakerBox;
        Colony colony;
        MissionObjective objective;
        HistoricalTraining training;
        Canvas canvas;
        GameObject unread;
        EventSystem events;
        Text speaker, dialogue;
        PixelButton notification, next, shelter, work, source;
        MissionObjective.Step shownStep;
        HistoricalTraining.Phase shownPhase;
        bool showingSource;
        static int releasedFrame = -1;

        internal static bool PointerOverUI(Vector2 guiPoint)
        {
            if (active == null || active.canvas == null || MissionPauseMenu.BlocksGameplayInput) return false;
            if (releasedFrame == Time.frameCount) return true;
            var screenPoint = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)active.notification.transform, screenPoint) ||
                (active.panel.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(active.panel, screenPoint));
        }

        void Start()
        {
            var root = FrontierUi.CreateCanvas(out events, opaqueBackdrop: false);
            root.name = "Mission Guide Canvas";
            canvas = root.GetComponent<Canvas>();
            canvas.sortingOrder = 25;
            colony = GetComponent<Colony>();
            objective = GetComponent<MissionObjective>();
            training = GetComponent<HistoricalTraining>();
            var layout = FrontierUi.FitLayout(root);
            FrontierUi.Resize(root, Reference, layout);

            var portraitArt = Resources.Load<Texture2D>("OutpostHud/Mission Guide Portrait");
            notification = BlueButton("", root);
            notification.name = "Marlonis notification";
            notificationRect = (RectTransform)notification.transform;
            notificationRect.anchorMin = notificationRect.anchorMax = notificationRect.pivot = new Vector2(0f, 1f);
            notificationRect.sizeDelta = NotificationSize;
            notificationRect.anchoredPosition = new Vector2(12, -60);
            notification.onClick.AddListener(ToggleDialogue);

            FrontierUi.Art("Notification frame", notification.face,
                Resources.Load<Texture2D>("OutpostHud/Marlonis Notification"));
            notification.face.GetComponent<Image>().enabled = false;
            var avatar = FrontierUi.Art("Marlonis", notification.face, portraitArt);
            FrontierUi.Place(avatar.rectTransform, NotificationSize, 5, 1, 32, 32);
            notification.focus.shine = null;
            var focus = (Image)notification.focus.ring;
            focus.pixelsPerUnitMultiplier = 1f;
            focus.rectTransform.offsetMin = new Vector2(2, 2);
            focus.rectTransform.offsetMax = new Vector2(-2, -2);

            var badge = PixelArt.Sliced("Unread message", notificationRect,
                PixelArt.Frame(Cyan, BlueFace, Cyan, BlueEdge));
            badge.pixelsPerUnitMultiplier = 1f;
            FrontierUi.Place(badge.rectTransform, NotificationSize, 27, -4, 16, 16);
            PixelLabel.Create("Notification mark", badge.transform, "!", 1, BlueFace);
            unread = badge.gameObject;

            panel = FrontierUi.Box("Marlonis dialogue", layout);
            FrontierUi.Place(panel, Reference, (Reference.x - PanelSize.x) / 2,
                Reference.y - PanelSize.y - 24, PanelSize.x, PanelSize.y);

            FrontierUi.Art("Blue frame", panel, Resources.Load<Texture2D>("OutpostHud/Mission Guide Frame"));
            var portrait = FrontierUi.Art("Hologram", panel, portraitArt);
            portraitRect = portrait.rectTransform;
            FrontierUi.Place(portraitRect, PanelSize, 9, 0, 77, 78);

            var bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            speakerBox = FrontierUi.Box("Speaker", panel);
            speaker = speakerBox.gameObject.AddComponent<Text>();
            speaker.font = bodyFont;
            speaker.fontSize = 13;
            speaker.color = Cyan;
            speaker.alignment = TextAnchor.MiddleLeft;
            speaker.raycastTarget = false;
            speaker.text = "<b>Marlonis</b> <color=#9eb9cb>|| Mission Quest</color>";
            FrontierUi.Place(speakerBox, PanelSize, 103, 7, 294, 16);

            textBox = FrontierUi.Box("Dialogue", panel);
            dialogue = textBox.gameObject.AddComponent<Text>();
            dialogue.font = bodyFont;
            dialogue.fontSize = 15;
            dialogue.color = FrontierUi.Cream;
            dialogue.alignment = TextAnchor.MiddleLeft;
            dialogue.raycastTarget = false;
            dialogue.supportRichText = false;
            dialogue.text = objective.Guidance;
            FrontierUi.Place(textBox, PanelSize, 103, 23, 295, 37);

            next = BlueButton("Hide", panel);
            FrontierUi.Place((RectTransform)next.transform, PanelSize, 328, 60, 72, 21);
            next.onClick.AddListener(() =>
            {
                if (MissionPauseMenu.BlocksGameplayInput) return;
                if (showingSource) { showingSource = false; RefreshDialogue(); GameAudio.Play(Sfx.GuideNext); }
                else CloseDialogue();
            });
            shelter = BlueButton("Shelter crew", panel);
            work = BlueButton("Continue working", panel);
            shelter.onClick.AddListener(() => Choose(true));
            work.onClick.AddListener(() => Choose(false));
            source = BlueButton("NASA source", panel);
            source.onClick.AddListener(() =>
            {
                if (MissionPauseMenu.BlocksGameplayInput) return;
                showingSource = true;
                RefreshDialogue();
                GameAudio.Play(Sfx.GuideNext);
            });
            RefreshDialogue();
            panel.gameObject.SetActive(false);
            active = this;
            GameAudio.PlayDelayed(Sfx.GuideNotify, 1.5f); // unread message badge appears after touchdown
        }

        static PixelButton BlueButton(string caption, Transform parent)
        {
            var button = FrontierUi.Button(caption, parent, false, titleAction: true);
            button.face.GetComponent<UnityEngine.UI.Image>().sprite = PixelArt.Frame(BlueFace, BlueEdge,
                new Color32(70, 101, 130, 255), new Color32(41, 57, 81, 255));
            var focus = (Image)button.focus.ring;
            focus.sprite = PixelArt.Ring(Cyan);
            focus.rectTransform.offsetMin = focus.rectTransform.offsetMax = Vector2.zero;
            button.focus.shineAlpha = .06f;
            return button;
        }

        void LateUpdate()
        {
            if (canvas == null) return;
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput;
            if (shownStep != objective.Stage || shownPhase != training.State)
            {
                bool announce = shownPhase != training.State &&
                    (training.State == HistoricalTraining.Phase.Choice || training.State == HistoricalTraining.Phase.Resolved ||
                     training.State == HistoricalTraining.Phase.Unavailable);
                shownStep = objective.Stage;
                shownPhase = training.State;
                showingSource = false;
                unread.SetActive(true);
                RefreshDialogue();
                if (announce) panel.gameObject.SetActive(true);
            }
            if (training.State == HistoricalTraining.Phase.Sheltering || training.State == HistoricalTraining.Phase.Working)
                RefreshDialogue();
            Rect fitness = colony.CrewFitnessBounds;
            if (fitness.width <= 0f) return;
            notificationRect.anchoredPosition = new Vector2(fitness.xMin, -fitness.yMax - 6f) / canvas.scaleFactor;
        }

        void ToggleDialogue()
        {
            if (MissionPauseMenu.BlocksGameplayInput) return;
            if (panel.gameObject.activeSelf)
            {
                CloseDialogue();
                return;
            }

            RefreshDialogue();
            unread.SetActive(false);
            panel.gameObject.SetActive(true);
            GameAudio.Play(Sfx.UiPanelOpen);
            next.Select();
        }

        void Choose(bool protect)
        {
            if (MissionPauseMenu.BlocksGameplayInput || !panel.gameObject.activeSelf) return;
            releasedFrame = Time.frameCount;
            training.Choose(protect);
            RefreshDialogue();
            GameAudio.Play(Sfx.GuideNext);
        }

        void RefreshDialogue()
        {
            bool exercise = training.State != HistoricalTraining.Phase.Waiting;
            bool choosing = training.State == HistoricalTraining.Phase.Choice && !showingSource;
            var size = showingSource ? SourceSize : exercise ? EventSize : PanelSize;
            float textLeft = exercise ? 140 : 103; // clear the portrait divider in the wider original frame
            FrontierUi.Place(panel, Reference, (Reference.x - size.x) / 2,
                Reference.y - size.y - 24, size.x, size.y);
            FrontierUi.Place(portraitRect, size, 9, 0, 77, 78);
            FrontierUi.Place(speakerBox, size, textLeft, 7, size.x - textLeft - 16, 16);
            FrontierUi.Place(textBox, size, textLeft, 23, size.x - textLeft - 16, showingSource ? 205 : exercise ? 67 : 37);
            dialogue.fontSize = showingSource ? 12 : 15;
            FrontierUi.Place((RectTransform)next.transform, size, size.x - 88, size.y - 27, 72, 21);
            FrontierUi.Place((RectTransform)shelter.transform, size, 16, 99, 250, 32);
            FrontierUi.Place((RectTransform)work.transform, size, 282, 99, 250, 32);
            FrontierUi.Place((RectTransform)source.transform, size, 16, size.y - 27, 100, 21);
            shelter.gameObject.SetActive(choosing);
            work.gameObject.SetActive(choosing);
            source.gameObject.SetActive(exercise && !showingSource && training.Observation != null);
            next.label.Text = showingSource ? "Back" : "Hide";
            if (showingSource) { dialogue.text = training.SourceDetails; return; }
            switch (training.State)
            {
                case HistoricalTraining.Phase.Choice:
                    dialogue.text = "Archived training: NASA recorded a solar-particle event on " + training.ObservationDate +
                        ". Shelter pauses construction safely; working brings a brief fitness dip.";
                    break;
                case HistoricalTraining.Phase.Sheltering:
                    dialogue.text = Crew.Instance != null && !Crew.Instance.Sheltered ?
                        "The crew are returning to the lander. Queued construction and its progress are saved." :
                        "The crew are protected in this exercise. Construction resumes in " + Mathf.CeilToInt(training.Remaining) + " seconds.";
                    break;
                case HistoricalTraining.Phase.Working:
                    dialogue.text = "Work continues with an 8-point fitness dip. That dip recovers in " + Mathf.CeilToInt(training.Remaining) + " seconds; normal fitness changes continue.";
                    break;
                case HistoricalTraining.Phase.Resolved:
                    dialogue.text = training.ChoseShelter ?
                        "Exercise complete: shelter prevented the extra fitness dip and delayed construction. Saved jobs now resume; keep building freely." :
                        "Exercise complete: construction kept moving and the extra fitness dip has recovered. Keep building freely.";
                    break;
                case HistoricalTraining.Phase.Unavailable:
                    dialogue.text = training.DataError;
                    break;
                default: dialogue.text = objective.Guidance; break;
            }
        }

        void CloseDialogue()
        {
            GameAudio.Play(Sfx.UiPanelClose);
            releasedFrame = Time.frameCount;
            panel.gameObject.SetActive(false);
            if (events != null) notification.Select();
        }

        void OnDestroy()
        {
            if (active == this) active = null;
            if (canvas != null) Destroy(canvas.gameObject);
        }
    }
}
