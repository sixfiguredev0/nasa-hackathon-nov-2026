using System.Collections.Generic;
using RedFrontier.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Story popups (recorder, comms log, broadcast, Earth, Marlonis), the reopenable Log of found clues and the final choice screen.
    /// Popups type out with a voice blip, never pause the game, and queue one at a time.
    /// </summary>
    internal sealed class StoryUi : MonoBehaviour
    {
        static readonly Vector2 Reference = new Vector2(960, 540), PopupSize = new Vector2(560, 128);
        static readonly Vector2 LogSize = new Vector2(320, 250), ChoiceSize = new Vector2(700, 330), CardSize = new Vector2(320, 196);
        const float CharsPerSecond = 40f;

        static readonly Color32 BlueFace = new Color32(34, 40, 55, 255), BlueEdge = new Color32(103, 152, 196, 255),
            Cyan = new Color32(131, 213, 235, 255), TermFace = new Color32(14, 24, 21, 255), TermEdge = new Color32(96, 200, 140, 255),
            TermText = new Color32(150, 236, 176, 255), EarthFace = new Color32(24, 34, 50, 255), EarthEdge = new Color32(214, 232, 255, 255),
            LedOn = new Color32(255, 72, 64, 255), LedOff = new Color32(90, 30, 34, 255);

        StoryArc story;
        Colony colony;
        Canvas canvas;
        EventSystem events;
        RectTransform root, layout, popup, logButtonRect, logPanel, choice;
        Image frame, deckLight;
        RawImage portrait, earth, noise;
        GameObject deck;
        Text speaker, body, logTitle;
        PixelLabel pageLabel;
        PixelButton next, logButton, logClose, returnButton, stayButton, laterButton;
        readonly List<PixelButton> logRows = new List<PixelButton>();
        readonly Queue<StoryArc.Entry> queue = new Queue<StoryArc.Entry>();
        StoryArc.Entry current;
        int page;
        float typed;
        int shownChars = -1;
        Texture2D noiseTexture;
        Color32[] noisePixels;
        float noiseAt;
        AudioSource voiceSource;
        AudioClip[] playerVoice, marlonisVoice, earthVoice, beep;

        internal bool ChoiceOpen => choice != null && choice.gameObject.activeSelf;
        /// <summary>No popup showing or waiting.</summary>
        internal bool Idle => current == null && queue.Count == 0;

        void Start()
        {
            story = GetComponent<StoryArc>();
            colony = GetComponent<Colony>();
            root = FrontierUi.CreateCanvas(out events, opaqueBackdrop: false);
            root.name = "Story Canvas";
            canvas = root.GetComponent<Canvas>();
            canvas.sortingOrder = 26;
            layout = FrontierUi.FitLayout(root);
            FrontierUi.Resize(root, Reference, layout);
            BuildSounds();
            BuildPopup();
            BuildLog();
            BuildChoice();
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        // ---- popups ----

        internal void Show(StoryArc.Entry entry)
        {
            queue.Enqueue(entry);
            GameAudio.Play(Sfx.GuideNotify);
        }

        void Present(StoryArc.Entry entry)
        {
            current = entry;
            page = 0;
            typed = 0f;
            shownChars = -1;
            bool recorder = entry.voice == StoryArc.Voice.Recorder, log = entry.voice == StoryArc.Voice.CommsLog,
                broadcast = entry.voice == StoryArc.Voice.Broadcast, fromEarth = entry.voice == StoryArc.Voice.Earth;
            frame.sprite = log ? PixelArt.Frame(TermFace, TermEdge, new Color32(40, 70, 56, 255), FrontierUi.Shadow)
                : recorder ? PixelArt.Frame(FrontierUi.Background, FrontierUi.Coral, FrontierUi.PanelLight, FrontierUi.Shadow)
                : broadcast ? PixelArt.Frame(FrontierUi.PanelDark, FrontierUi.Mauve, FrontierUi.PanelLight, FrontierUi.Shadow)
                : fromEarth ? PixelArt.Frame(EarthFace, EarthEdge, new Color32(70, 100, 140, 255), FrontierUi.Shadow)
                : PixelArt.Frame(BlueFace, BlueEdge, new Color32(70, 101, 130, 255), new Color32(41, 57, 81, 255));
            deck.SetActive(recorder || log);
            portrait.gameObject.SetActive(entry.voice == StoryArc.Voice.Marlonis);
            earth.gameObject.SetActive(fromEarth);
            noise.gameObject.SetActive(broadcast);
            speaker.color = log ? (Color)TermEdge : recorder || broadcast ? (Color)FrontierUi.CoralLight : fromEarth ? (Color)EarthEdge : (Color)Cyan;
            speaker.text = log ? "<b>Comms log</b> <color=#8fbf9f>|| Recovered from the ship</color>"
                : recorder ? "<b>Recorder</b> <color=#cfb9b8>|| Your voice, before the crash</color>"
                : broadcast ? "<b>Broadcast</b> <color=#cfb9b8>|| Old signal from Earth</color>"
                : fromEarth ? "<b>Mission Control</b> <color=#b9c9db>|| Earth, via orbiter relay</color>"
                : "<b>Marlonis</b> <color=#9eb9cb>|| Ship computer</color>";
            body.color = log ? (Color)TermText : FrontierUi.Cream;
            body.fontSize = log ? 13 : 15;
            popup.gameObject.SetActive(true);
            next.Select();
        }

        void Advance()
        {
            if (current == null || MissionPauseMenu.BlocksGameplayInput) return;
            string text = current.pages[page];
            if (typed < text.Length)
            {
                typed = text.Length; // first press finishes the line
                return;
            }
            GameAudio.Play(Sfx.GuideNext);
            if (++page < current.pages.Length)
            {
                typed = 0f;
                shownChars = -1;
                return;
            }
            current = null;
            popup.gameObject.SetActive(false);
        }

        void Update()
        {
            if (canvas == null) return;
            bool paused = MissionPauseMenu.BlocksGameplayInput;
            bool cinematic = StoryArc.Cinematic && !ChoiceOpen; // an ending is playing
            canvas.enabled = !paused && !cinematic;
            if (paused) return;
            if (current == null && queue.Count > 0 && !ChoiceOpen) Present(queue.Dequeue());
            logButton.gameObject.SetActive(story.Found.Count > 0 && !ChoiceOpen);
            logButton.label.Text = "Log " + story.Found.Count;
            if (current == null) return;

            string text = current.pages[page];
            typed = Mathf.Min(text.Length, typed + Time.unscaledDeltaTime * CharsPerSecond);
            int count = Mathf.FloorToInt(typed);
            if (count != shownChars)
            {
                if (count > shownChars && count % 2 == 0 && count > 0 && char.IsLetter(text[count - 1]))
                    Blip(current.voice);
                body.text = text.Substring(0, count);
                shownChars = count;
            }
            pageLabel.Text = (page + 1) + "/" + current.pages.Length;
            next.label.Text = count < text.Length ? "Skip" : page + 1 < current.pages.Length ? "Next" : "Close";
            if (current.voice == StoryArc.Voice.Broadcast) RefreshNoise();
            if (deck.activeSelf) deckLight.color = Mathf.Repeat(Time.unscaledTime * 2f, 1f) < .5f ? LedOn : LedOff;
            if (current.voice == StoryArc.Voice.Marlonis)
                portrait.color = new Color(1f, 1f, 1f, Hash(Mathf.FloorToInt(Time.unscaledTime * 9f)) < .08f ? .5f : .95f);
        }

        void LateUpdate()
        {
            if (canvas == null || colony == null) return;
            // Beside the Marlonis notification, under the crew fitness gauge.
            Rect fitness = colony.CrewFitnessBounds;
            if (fitness.width <= 0f) return;
            logButtonRect.anchoredPosition = new Vector2(fitness.xMin + 50f * canvas.scaleFactor, -fitness.yMax - 6f) / canvas.scaleFactor;
        }

        void Blip(StoryArc.Voice voice)
        {
            var set = voice == StoryArc.Voice.Marlonis ? marlonisVoice : voice == StoryArc.Voice.Earth ? earthVoice
                : voice == StoryArc.Voice.CommsLog ? beep : playerVoice;
            voiceSource.PlayOneShot(set[Random.Range(0, set.Length)], .4f);
        }

        internal bool PointerOver(Vector2 guiPoint)
        {
            if (canvas == null || !canvas.enabled) return false;
            if (ChoiceOpen) return true;
            var screen = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
            return (popup.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(popup, screen)) ||
                (logButton.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(logButtonRect, screen)) ||
                (logPanel.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(logPanel, screen));
        }

        void BuildPopup()
        {
            popup = FrontierUi.Box("Story popup", layout);
            FrontierUi.Place(popup, Reference, (Reference.x - PopupSize.x) / 2, 206, PopupSize.x, PopupSize.y); // under the objective banner
            frame = Sharp(PixelArt.Sliced("Frame", popup, PixelArt.Frame(BlueFace, BlueEdge, BlueEdge, BlueFace)));
            frame.raycastTarget = true;

            // Left icon: Marlonis's portrait, Earth, static, or a tape deck for recordings.
            portrait = FrontierUi.Art("Marlonis", popup, Resources.Load<Texture2D>("OutpostHud/Mission Guide Portrait"));
            FrontierUi.Place(portrait.rectTransform, PopupSize, 12, 20, 77, 78);
            earth = FrontierUi.Art("Earth", popup, EarthTexture());
            FrontierUi.Place(earth.rectTransform, PopupSize, 22, 30, 56, 56);
            noiseTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Broadcast static" };
            noisePixels = new Color32[32 * 32];
            noise = FrontierUi.Art("Static", popup, noiseTexture);
            FrontierUi.Place(noise.rectTransform, PopupSize, 18, 28, 64, 56);
            var deckImage = Sharp(PixelArt.Sliced("Tape deck", popup, PixelArt.Frame(FrontierUi.PanelDark, FrontierUi.Mauve, FrontierUi.PanelLight, FrontierUi.Shadow)));
            FrontierUi.Place(deckImage.rectTransform, PopupSize, 18, 36, 62, 44);
            deck = deckImage.gameObject;
            for (int i = 0; i < 2; i++)
            {
                var reel = Sharp(PixelArt.Sliced("Reel", deck.transform, PixelArt.Frame(FrontierUi.CreamDark, FrontierUi.Shadow, FrontierUi.Cream, FrontierUi.Mauve)));
                FrontierUi.Place(reel.rectTransform, new Vector2(62, 44), 9 + i * 28, 10, 16, 16);
            }
            deckLight = FrontierUi.Fill("Playback light", deck.transform, LedOff);
            FrontierUi.Place(deckLight.rectTransform, new Vector2(62, 44), 50, 32, 5, 5);

            speaker = Body("Speaker", popup, PopupSize, 13, Cyan, TextAnchor.MiddleLeft, 104, 8, 440, 16);
            speaker.supportRichText = true;
            body = Body("Dialogue", popup, PopupSize, 15, FrontierUi.Cream, TextAnchor.UpperLeft, 104, 28, 440, 64);
            pageLabel = PixelLabel.Create("Page", popup, "1/1", 1, FrontierUi.CreamDark, 0f);
            FrontierUi.Place(pageLabel.Rect, PopupSize, 104, PopupSize.y - 22, 60, 10);
            next = Button("Next", popup);
            FrontierUi.Place((RectTransform)next.transform, PopupSize, PopupSize.x - 92, PopupSize.y - 30, 76, 22);
            next.onClick.AddListener(Advance);
            popup.gameObject.SetActive(false);

            logButton = Button("Log", root);
            logButton.name = "Story log";
            logButtonRect = (RectTransform)logButton.transform;
            logButtonRect.anchorMin = logButtonRect.anchorMax = logButtonRect.pivot = new Vector2(0f, 1f);
            logButtonRect.sizeDelta = new Vector2(64, 38);
            logButtonRect.anchoredPosition = new Vector2(62, -60);
            logButton.onClick.AddListener(ToggleLog);
            logButton.gameObject.SetActive(false);
        }

        // ---- log of found clues ----

        void BuildLog()
        {
            logPanel = FrontierUi.PanelFrame("Story log", layout);
            FrontierUi.Place(logPanel, Reference, 16, 120, LogSize.x, LogSize.y); // clear of the sol clock
            logTitle = Body("Title", logPanel, LogSize, 15, FrontierUi.CoralLight, TextAnchor.MiddleLeft, 14, 8, 290, 20);
            logTitle.text = "<b>Clues you found</b>";
            logTitle.supportRichText = true;
            for (int i = 0; i < 7; i++)
            {
                var row = Button("", logPanel);
                FrontierUi.Place((RectTransform)row.transform, LogSize, 14, 34 + i * 25, 292, 21);
                int index = i;
                row.onClick.AddListener(() => Replay(index));
                logRows.Add(row);
            }
            logClose = Button("Close", logPanel);
            FrontierUi.Place((RectTransform)logClose.transform, LogSize, LogSize.x - 90, LogSize.y - 30, 76, 22);
            logClose.onClick.AddListener(ToggleLog);
            logPanel.gameObject.SetActive(false);
        }

        void ToggleLog()
        {
            if (MissionPauseMenu.BlocksGameplayInput) return;
            bool open = !logPanel.gameObject.activeSelf;
            logPanel.gameObject.SetActive(open);
            GameAudio.Play(open ? Sfx.UiPanelOpen : Sfx.UiPanelClose);
            if (!open) return;
            for (int i = 0; i < logRows.Count; i++)
            {
                bool has = i < story.Found.Count;
                logRows[i].gameObject.SetActive(has);
                if (has) logRows[i].label.Text = story.Found[i].title;
            }
        }

        void Replay(int index)
        {
            if (index >= story.Found.Count) return;
            logPanel.gameObject.SetActive(false);
            queue.Clear();
            Present(story.Found[index]);
            GameAudio.Play(Sfx.GuideNext);
        }

        // ---- the choice ----

        void BuildChoice()
        {
            choice = FrontierUi.Box("Final choice", root);
            var dim = FrontierUi.Fill("Dim", choice, new Color(0f, 0f, 0f, .72f));
            dim.raycastTarget = true;
            var area = FrontierUi.Box("Layout", choice);
            area.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            area.GetComponent<AspectRatioFitter>().aspectRatio = Reference.x / Reference.y;
            var panel = FrontierUi.PanelFrame("Choice panel", area);
            FrontierUi.Place(panel, Reference, (Reference.x - ChoiceSize.x) / 2, (Reference.y - ChoiceSize.y) / 2, ChoiceSize.x, ChoiceSize.y);
            var title = PixelLabel.Create("Title", panel, "Make your choice", 3, FrontierUi.CoralLight);
            FrontierUi.Place(title.Rect, ChoiceSize, 0, 12, ChoiceSize.x, 24);
            var lead = Body("Lead", panel, ChoiceSize, 14, FrontierUi.CreamDark, TextAnchor.UpperCenter, 30, 42, ChoiceSize.x - 60, 36);
            lead.text = "The ship is repaired and the tank is full. Marlonis's core hums inside it. There is no wrong answer.";
            lead.alignment = TextAnchor.UpperCenter;

            returnButton = Card(panel, 20, "Return home", "Fly home with Marlonis. Earth stays calm and safe, but machines decide everything. Face to face, maybe you can change its mind.");
            stayButton = Card(panel, ChoiceSize.x - 20 - CardSize.x, "Stay on Mars", "Shut the core down. Earth's people get their systems back. Without Marlonis the ship can't fly, so Mars becomes your home.");
            returnButton.onClick.AddListener(() => Decide(StoryArc.Ending.Return));
            stayButton.onClick.AddListener(() => Decide(StoryArc.Ending.Stay));
            laterButton = Button("Not yet", panel);
            FrontierUi.Place((RectTransform)laterButton.transform, ChoiceSize, (ChoiceSize.x - 110) / 2, ChoiceSize.y - 34, 110, 24);
            laterButton.onClick.AddListener(() =>
            {
                choice.gameObject.SetActive(false);
                GameAudio.Play(Sfx.UiBack);
            });
            choice.gameObject.SetActive(false);
        }

        PixelButton Card(RectTransform panel, float x, string caption, string text)
        {
            var card = FrontierUi.Button(caption, panel, true);
            var rect = (RectTransform)card.transform;
            FrontierUi.Place(rect, ChoiceSize, x, 86, CardSize.x, CardSize.y);
            card.label.Rect.anchorMin = new Vector2(0f, 1f - 40f / CardSize.y);
            card.label.Rect.anchorMax = new Vector2(1f, 1f - 8f / CardSize.y);
            var words = Body("Text", card.face, CardSize, 14, FrontierUi.Background, TextAnchor.UpperLeft, 16, 50, CardSize.x - 32, CardSize.y - 60);
            words.text = text;
            return card;
        }

        internal void OpenChoice()
        {
            if (MissionPauseMenu.BlocksGameplayInput || story.Chosen != StoryArc.Ending.None) return;
            logPanel.gameObject.SetActive(false);
            choice.gameObject.SetActive(true);
            GameAudio.Play(Sfx.UiPanelOpen);
            returnButton.Select();
        }

        void Decide(StoryArc.Ending which)
        {
            if (MissionPauseMenu.BlocksGameplayInput) return;
            GameAudio.Play(Sfx.UiConfirm);
            choice.gameObject.SetActive(false);
            popup.gameObject.SetActive(false);
            current = null;
            queue.Clear();
            story.Choose(which);
        }

        // ---- helpers ----

        static PixelButton Button(string caption, Transform parent)
        {
            var button = FrontierUi.Button(caption, parent, false, titleAction: true);
            button.face.GetComponent<Image>().sprite = PixelArt.Frame(BlueFace, BlueEdge, new Color32(70, 101, 130, 255), new Color32(41, 57, 81, 255));
            var focus = (Image)button.focus.ring;
            focus.sprite = PixelArt.Ring(Cyan);
            focus.rectTransform.offsetMin = focus.rectTransform.offsetMax = Vector2.zero;
            button.focus.shineAlpha = .06f;
            return button;
        }

        static Image Sharp(Image image)
        {
            image.pixelsPerUnitMultiplier = 1f;
            return image;
        }

        static Text Body(string name, RectTransform panel, Vector2 size, int fontSize, Color color, TextAnchor anchor, float x, float y, float w, float h)
        {
            var box = FrontierUi.Box(name, panel);
            var text = box.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            FrontierUi.Place(box, size, x, y, w, h);
            return text;
        }

        void RefreshNoise()
        {
            if (Time.unscaledTime < noiseAt) return;
            noiseAt = Time.unscaledTime + .06f;
            for (int i = 0; i < noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(40, 200);
                noisePixels[i] = new Color32(v, (byte)(v * .85f), (byte)(v * .8f), 255);
            }
            noiseTexture.SetPixels32(noisePixels);
            noiseTexture.Apply(false);
        }

        /// <summary>A small pixel Earth: blue ocean, green land, white cloud, dark limb.</summary>
        internal static Texture2D EarthTexture()
        {
            const int n = 28;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2f - 1f, dy = (y + .5f) / n * 2f - 1f, r = dx * dx + dy * dy;
                    if (r > 1f) continue;
                    float land = Mathf.Sin(x * .55f + Mathf.Sin(y * .4f) * 2f) + Mathf.Cos(y * .45f - x * .2f);
                    Color32 c = land > .9f ? new Color32(84, 170, 92, 255) : new Color32(52, 112, 200, 255);
                    if (Mathf.Abs(Mathf.Sin(y * .9f + x * .15f)) > .93f) c = new Color32(236, 242, 250, 255);
                    if (r > .72f || dx + dy > .9f) c = Color32.Lerp(c, new Color32(10, 16, 34, 255), .45f);
                    px[y * n + x] = c;
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Earth" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float Hash(int n) => Mathf.Repeat(Mathf.Sin(n * 12.9898f) * 43758.547f, 1f);

        void BuildSounds()
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            var rng = new System.Random(5);
            float White() => (float)rng.NextDouble() * 2f - 1f;
            playerVoice = new[] { OpeningCutscene.Blip(200f, .1f, White), OpeningCutscene.Blip(235f, .1f, White), OpeningCutscene.Blip(270f, .1f, White) };
            marlonisVoice = new[] { OpeningCutscene.Blip(560f, .02f, White), OpeningCutscene.Blip(640f, .02f, White), OpeningCutscene.Blip(720f, .02f, White) };
            earthVoice = new[] { OpeningCutscene.Blip(380f, .3f, White), OpeningCutscene.Blip(420f, .3f, White), OpeningCutscene.Blip(460f, .3f, White) };
            beep = new[] { OpeningCutscene.Blip(1200f, 0f, White), OpeningCutscene.Blip(1500f, 0f, White) };
        }
    }
}
