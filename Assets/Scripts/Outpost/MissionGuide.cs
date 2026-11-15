using RedFrontier.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    internal sealed class MissionGuide : MonoBehaviour
    {
        static MissionGuide active;
        static readonly Vector2 PanelSize = new Vector2(420, 100);
        static readonly Vector2 EventSize = new Vector2(420, 158), SourceSize = new Vector2(480, 240), StorySize = new Vector2(420, 112);
        const float CellX = 12, CellY = 14, CellSize = 72, TextLeft = 100;
        static readonly Vector2 Reference = new Vector2(960, 540);

        static readonly Color32 BlueFace = new Color32(34, 40, 55, 255);
        static readonly Color32 BlueEdge = new Color32(103, 152, 196, 255);
        static readonly Color32 Cyan = new Color32(131, 213, 235, 255);

        static readonly Vector2 NotificationSize = new Vector2(42, 38);

        RectTransform panel, notificationRect, textBox, portraitRect, portraitCell, speakerBox;
        Colony colony;
        MissionObjective objective;
        HistoricalTraining training;
        StoryArc story;
        Canvas canvas;
        GameObject unread;
        EventSystem events;
        Text speaker, dialogue;
        PixelButton notification, next, shelter, work, source, action;
        MissionObjective.Step shownStep;
        HistoricalTraining.Phase shownPhase;
        bool showingSource, shownOffline;
        static int releasedFrame = -1;

        internal static bool PointerOverUI(Vector2 guiPoint)
        {
            if (Skywatch.PointerOverUI(guiPoint) || StoryArc.PointerOverUI(guiPoint)) return true;
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
            story = GetComponent<StoryArc>();
            var layout = FrontierUi.FitLayout(root);
            FrontierUi.Resize(root, Reference, layout);

            var portraitArt = Resources.Load<Texture2D>("OutpostHud/Mission Guide Portrait");
            notification = FrontierUi.Button("", root, false, titleAction: true);
            notification.name = "Marlonis notification";
            notificationRect = (RectTransform)notification.transform;
            notificationRect.anchorMin = notificationRect.anchorMax = notificationRect.pivot = new Vector2(0f, 1f);
            notificationRect.sizeDelta = NotificationSize;
            notificationRect.anchoredPosition = new Vector2(12, -60);
            notification.onClick.AddListener(ToggleDialogue);

            var notificationFace = notification.face.GetComponent<Image>();
            notificationFace.sprite = PixelArt.Frame(FrontierUi.Shadow, FrontierUi.PanelLight,
                FrontierUi.Shadow, FrontierUi.Shadow);
            notificationFace.pixelsPerUnitMultiplier = 1f;
            var avatar = FrontierUi.Art("Marlonis", notification.face, portraitArt);
            FrontierUi.Place(avatar.rectTransform, NotificationSize, 5, 1, 32, 32);
            notification.focus.shine = null;
            var focus = (Image)notification.focus.ring;
            focus.sprite = PixelArt.Ring(OutpostGUI.InstrumentAccent);
            focus.pixelsPerUnitMultiplier = 1f;
            focus.rectTransform.offsetMin = new Vector2(2, 2);
            focus.rectTransform.offsetMax = new Vector2(-2, -2);

            var badge = FrontierUi.Fill("Unread message", notificationRect, OutpostGUI.InstrumentAccent);
            FrontierUi.Place(badge.rectTransform, NotificationSize, 29, -3, 12, 12);
            PixelLabel.Create("Notification mark", badge.transform, "!", 1, FrontierUi.Shadow);
            unread = badge.gameObject;

            panel = FrontierUi.Box("Marlonis dialogue", layout);
            FrontierUi.Place(panel, Reference, (Reference.x - PanelSize.x) / 2,
                Reference.y - PanelSize.y - 24, PanelSize.x, PanelSize.y);

            // Sliced frame (stays crisp at every panel size) plus a fixed square portrait cell, so the head never stretches or drifts.
            var frame = PixelArt.Sliced("Blue frame", panel, PixelArt.Frame(BlueFace, BlueEdge, BlueEdge, BlueFace));
            frame.pixelsPerUnitMultiplier = 1f;
            frame.raycastTarget = true;
            var cell = PixelArt.Sliced("Portrait cell", panel, PixelArt.Frame(new Color32(14, 20, 32, 255), new Color32(70, 101, 130, 255),
                new Color32(70, 101, 130, 255), new Color32(14, 20, 32, 255)));
            cell.pixelsPerUnitMultiplier = 1f;
            portraitCell = cell.rectTransform;
            FrontierUi.Place(portraitCell, PanelSize, CellX, CellY, CellSize, CellSize);
            var portrait = FrontierUi.Art("Hologram", portraitCell, portraitArt);
            portraitRect = portrait.rectTransform;
            FrontierUi.Place(portraitRect, new Vector2(CellSize, CellSize), 4, 4, CellSize - 8, CellSize - 8);

            var bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            speakerBox = FrontierUi.Box("Speaker", panel);
            speaker = speakerBox.gameObject.AddComponent<Text>();
            speaker.font = bodyFont;
            speaker.fontSize = 13;
            speaker.color = Cyan;
            speaker.alignment = TextAnchor.MiddleLeft;
            speaker.raycastTarget = false;
            speaker.text = "<b>Marlonis</b> <color=#9eb9cb>|| Mission Quest</color>";
            FrontierUi.Place(speakerBox, PanelSize, TextLeft, 7, PanelSize.x - TextLeft - 14, 16);

            textBox = FrontierUi.Box("Dialogue", panel);
            dialogue = textBox.gameObject.AddComponent<Text>();
            dialogue.font = bodyFont;
            dialogue.fontSize = 14;
            dialogue.color = FrontierUi.Cream;
            dialogue.alignment = TextAnchor.UpperLeft;
            dialogue.raycastTarget = false;
            dialogue.supportRichText = false;
            dialogue.text = objective.Guidance;
            FrontierUi.Place(textBox, PanelSize, TextLeft, 26, PanelSize.x - TextLeft - 14, 44);

            next = BlueButton("Hide", panel);
            FrontierUi.Place((RectTransform)next.transform, PanelSize, PanelSize.x - 86, PanelSize.y - 28, 72, 21);
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
            // Story steps: repair the radio, then open the final choice.
            action = BlueButton("Repair radio", panel);
            action.onClick.AddListener(() =>
            {
                if (MissionPauseMenu.BlocksGameplayInput || story == null) return;
                releasedFrame = Time.frameCount;
                if (objective.Stage == MissionObjective.Step.Decide) GetComponent<StoryUi>().OpenChoice();
                else story.TryRepairRadio();
                RefreshDialogue();
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
            canvas.enabled = !MissionPauseMenu.BlocksGameplayInput && !StoryArc.Cinematic;
            bool offline = story != null && story.MarlonisOffline;
            if (shownStep != objective.Stage || shownPhase != training.State || shownOffline != offline)
            {
                shownOffline = offline;
                bool announce = (shownPhase != training.State &&
                    (training.State == HistoricalTraining.Phase.Choice || training.State == HistoricalTraining.Phase.Resolved ||
                     training.State == HistoricalTraining.Phase.Unavailable)) ||
                    (shownStep != objective.Stage && (objective.Stage == MissionObjective.Step.RepairRadio || objective.Stage == MissionObjective.Step.Decide));
                shownStep = objective.Stage;
                shownPhase = training.State;
                showingSource = false;
                unread.SetActive(true);
                RefreshDialogue();
                if (announce) panel.gameObject.SetActive(true);
            }
            if (training.State == HistoricalTraining.Phase.Choice || training.State == HistoricalTraining.Phase.Sheltering ||
                training.State == HistoricalTraining.Phase.Working ||
                objective.Stage == MissionObjective.Step.RepairRadio || objective.Stage == MissionObjective.Step.Signal ||
                objective.Stage == MissionObjective.Step.Fuel)
                RefreshDialogue();
            Rect fitness = colony.CrewFitnessBounds;
            if (fitness.width <= 0f) return;
            float scale = canvas.scaleFactor;
            notificationRect.localScale = Vector3.one * (Mathf.Max(.75f, scale) / scale);
            notificationRect.anchoredPosition = new Vector2(fitness.xMin, -fitness.yMax - 6f) / scale;
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
            // The exercise layout stays until the story moves past it; story steps get a taller box for longer lines.
            bool exercise = training.State != HistoricalTraining.Phase.Waiting && objective.Stage <= MissionObjective.Step.Training;
            bool storyStep = !exercise && objective.Stage > MissionObjective.Step.Training;
            bool choosing = training.State == HistoricalTraining.Phase.Choice && !showingSource && exercise;
            var size = showingSource ? SourceSize : exercise ? EventSize : storyStep ? StorySize : PanelSize;
            float textLeft = TextLeft, textWidth = size.x - textLeft - 14;
            FrontierUi.Place(panel, Reference, (Reference.x - size.x) / 2,
                Reference.y - size.y - 24, size.x, size.y);
            FrontierUi.Place(portraitCell, size, CellX, CellY, CellSize, CellSize);
            FrontierUi.Place(speakerBox, size, textLeft, 7, textWidth, 16);
            FrontierUi.Place(textBox, size, textLeft, 26, textWidth, showingSource ? size.y - 62 : exercise ? 66 : storyStep ? 54 : 44);
            dialogue.fontSize = showingSource ? 12 : 14;
            FrontierUi.Place((RectTransform)next.transform, size, size.x - 86, size.y - 28, 72, 21);
            float half = (size.x - 2 * 12 - 8) / 2;
            FrontierUi.Place((RectTransform)shelter.transform, size, 12, size.y - 58, half, 24);
            FrontierUi.Place((RectTransform)work.transform, size, 20 + half, size.y - 58, half, 24);
            FrontierUi.Place((RectTransform)source.transform, size, textLeft, size.y - 28, 100, 21);
            shelter.gameObject.SetActive(choosing);
            work.gameObject.SetActive(choosing);
            source.gameObject.SetActive(exercise && !showingSource && training.Observation != null);
            bool repair = story != null && story.CanRepairRadio, decide = story != null && objective.Stage == MissionObjective.Step.Decide &&
                story.Chosen == StoryArc.Ending.None;
            action.gameObject.SetActive(storyStep && (repair || decide));
            action.label.Text = decide ? "Make your choice" : "Repair radio";
            action.interactable = decide || (repair && story.RadioAffordable);
            FrontierUi.Place((RectTransform)action.transform, size, textLeft, size.y - 28, 150, 21);
            bool offline = story != null && story.MarlonisOffline;
            speaker.text = offline ? "<b>Mission Control</b> <color=#9eb9cb>|| Earth</color>" : "<b>Marlonis</b> <color=#9eb9cb>|| Mission Quest</color>";
            portraitCell.gameObject.SetActive(!offline);
            next.label.Text = showingSource ? "Back" : "Hide";
            if (showingSource) { dialogue.text = training.SourceDetails; return; }
            if (!exercise) { dialogue.text = objective.Guidance; return; }
            switch (training.State)
            {
                case HistoricalTraining.Phase.Choice:
                    dialogue.text = "Solar particle burst incoming! (Modeled on NASA's archived " + training.ObservationDate +
                        " record.) Shelter: no building for " + Mathf.RoundToInt(HistoricalTraining.ExerciseSeconds) + " s. Keep working: fitness -" +
                        Mathf.RoundToInt(HistoricalTraining.FitnessDip * 100f) + ". Decide in " + Mathf.CeilToInt(training.ChoiceRemaining) + " s.";
                    break;
                case HistoricalTraining.Phase.Sheltering:
                    dialogue.text = Crew.Instance != null && !Crew.Instance.Sheltered ?
                        "The crew are returning to the lander. Queued construction and its progress are saved." :
                        "The crew are protected. Construction resumes in " + Mathf.CeilToInt(training.Remaining) + " seconds.";
                    break;
                case HistoricalTraining.Phase.Working:
                    dialogue.text = "Work continues with a " + Mathf.RoundToInt(HistoricalTraining.FitnessDip * 100f) + "-point fitness dip. It recovers in " + Mathf.CeilToInt(training.Remaining) + " seconds; normal fitness changes continue.";
                    break;
                case HistoricalTraining.Phase.Resolved:
                    dialogue.text = training.ChoseShelter ?
                        "The burst has passed: shelter prevented the fitness dip but cost building time. Saved jobs now resume." :
                        "The burst has passed: construction kept moving and the fitness dip has recovered.";
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
