using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    internal sealed class MissionGuide : MonoBehaviour
    {
        static readonly string[] Lines =
        {
            "Restore power first. Open Build and choose Solar Panels.",
            "Find a clear spot. Click when the blueprint turns green.",
            "The crew will build it. Right-click a building to see upgrades."
        };

        static MissionGuide active;
        static readonly Vector2 PanelSize = new Vector2(414, 87);
        static readonly Vector2 Reference = new Vector2(960, 540);

        static readonly Color32 BlueFace = new Color32(34, 40, 55, 255);
        static readonly Color32 BlueEdge = new Color32(103, 152, 196, 255);
        static readonly Color32 Cyan = new Color32(131, 213, 235, 255);

        static readonly Vector2 NotificationSize = new Vector2(42, 38);

        RectTransform panel, notificationRect;
        Colony colony;
        Canvas canvas;
        GameObject unread;
        EventSystem events;
        Text speaker, dialogue;
        PixelButton notification, next;
        int line;

        internal static bool PointerOverUI(Vector2 guiPoint)
        {
            if (active == null || active.canvas == null || MissionPauseMenu.BlocksGameplayInput) return false;
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
            FrontierUi.Place(portrait.rectTransform, PanelSize, 9, 0, 77, 78);

            var bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var speakerBox = FrontierUi.Box("Speaker", panel);
            speaker = speakerBox.gameObject.AddComponent<Text>();
            speaker.font = bodyFont;
            speaker.fontSize = 13;
            speaker.color = Cyan;
            speaker.alignment = TextAnchor.MiddleLeft;
            speaker.raycastTarget = false;
            speaker.text = "<b>Marlonis</b> <color=#9eb9cb>|| Mission Quest</color>";
            FrontierUi.Place(speakerBox, PanelSize, 103, 7, 294, 16);

            var textBox = FrontierUi.Box("Dialogue", panel);
            dialogue = textBox.gameObject.AddComponent<Text>();
            dialogue.font = bodyFont;
            dialogue.fontSize = 15;
            dialogue.color = FrontierUi.Cream;
            dialogue.alignment = TextAnchor.MiddleLeft;
            dialogue.raycastTarget = false;
            dialogue.supportRichText = false;
            dialogue.text = Lines[0];
            FrontierUi.Place(textBox, PanelSize, 103, 23, 295, 37);

            next = BlueButton("Next >", panel);
            FrontierUi.Place((RectTransform)next.transform, PanelSize, 328, 60, 72, 21);
            next.onClick.AddListener(Advance);
            panel.gameObject.SetActive(false);
            active = this;
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

            if (line >= Lines.Length)
            {
                line = 0;
                dialogue.text = Lines[0];
                next.label.Text = "Next >";
            }
            unread.SetActive(false);
            panel.gameObject.SetActive(true);
            next.Select();
        }

        void Advance()
        {
            if (MissionPauseMenu.BlocksGameplayInput || !panel.gameObject.activeSelf) return;
            line++;
            if (line >= Lines.Length)
            {
                CloseDialogue();
                return;
            }
            dialogue.text = Lines[line];
            if (line == Lines.Length - 1) next.label.Text = "Done";
        }

        void CloseDialogue()
        {
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
