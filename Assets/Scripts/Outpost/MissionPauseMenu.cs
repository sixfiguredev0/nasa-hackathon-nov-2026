using RedFrontier.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RedFrontier.Outpost
{
    [DefaultExecutionOrder(-100)]
    internal sealed class MissionPauseMenu : MonoBehaviour
    {
        const string TitleScene = "Assets/Scenes/MainMenu.unity";
        const int VolumeSteps = 10;
        static readonly Vector2 PanelSize = new Vector2(368, 320);
        const float Inset = 24, ContentWidth = 320, ActionHeight = 44;
        static MissionPauseMenu active;
        static int releasedFrame = -1;
        static bool leaving; // Return to title pressed; the title scene is loading

        internal static bool BlocksGameplayInput => active != null || leaving || releasedFrame == Time.frameCount || MissionDebrief.IsOpen;

        RectTransform canvas, panel, main, settings, confirmation;
        EventSystem events;
        bool ownsEvents;
        GameObject priorSelection;
        PixelButton resume, settingsButton, titleButton, motion, keepPlaying, leave;
        PixelSlider volume;
        PixelLabel volumeValue;
        AudioMixRow mix;
        BuilderMode builder;
        UpgradeBubbles upgrades;
        float previousTimeScale;
        bool loading;

        void Awake()
        {
            builder = GetComponent<BuilderMode>();
            upgrades = GetComponent<UpgradeBubbles>();
            leaving = false;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
        }

        void HandleEscape()
        {
            if (loading || MissionDebrief.IsOpen) return;
            if (active == this)
            {
                if (settings.gameObject.activeSelf) BackFromSettings();
                else if (confirmation.gameObject.activeSelf) BackFromConfirmation();
                else Resume();
                return;
            }

            if (upgrades != null && upgrades.ConfirmingSalvage && upgrades.CancelForEscape()) return;
            if ((builder != null && builder.CancelForEscape()) ||
                (upgrades != null && upgrades.CancelForEscape())) return;
            Pause();
        }

        void Pause()
        {
            if (canvas == null) CreateMenu();
            priorSelection = events.currentSelectedGameObject;
            previousTimeScale = Time.timeScale;
            active = this;
            Time.timeScale = 0f;
            GameAudio.SetPaused(true);
            GameAudio.Play(Sfx.UiPanelOpen);
            canvas.gameObject.SetActive(true);
            Show(main, resume);
        }

        void Resume()
        {
            if (active != this) return;
            active = null;
            releasedFrame = Time.frameCount;
            Time.timeScale = previousTimeScale;
            GameAudio.SetPaused(false);
            if (!loading) GameAudio.Play(Sfx.UiPanelClose);
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (events != null) events.SetSelectedGameObject(priorSelection);
        }

        void OnDisable()
        {
            Resume();
        }

        void OnDestroy()
        {
            leaving = false;
            if (canvas != null) Destroy(canvas.gameObject);
            if (ownsEvents && events != null) Destroy(events.gameObject);
        }

        void CreateMenu()
        {
            var existingEvents = EventSystem.current;
            canvas = FrontierUi.CreateCanvas(out events, opaqueBackdrop: false);
            ownsEvents = existingEvents == null;
            canvas.name = "Mission Pause Canvas";
            canvas.GetComponent<Canvas>().sortingOrder = 50;
            FrontierUi.Fill("Dim outpost", canvas, new Color32(20, 17, 25, 172)).raycastTarget = true;

            var layout = FrontierUi.FitLayout(canvas);
            var reference = new Vector2(960, 720);
            FrontierUi.Resize(canvas, reference, layout);
            panel = FrontierUi.Box("Pause panel", layout);
            FrontierUi.Place(panel, reference, (reference.x - PanelSize.x) / 2, (reference.y - PanelSize.y) / 2,
                PanelSize.x, PanelSize.y);

            main = Page("Mission paused");
            resume = Action(main, "Resume mission", 88, true);
            resume.onClick.AddListener(Resume);
            settingsButton = Action(main, "Settings", 144);
            settingsButton.onClick.AddListener(OpenSettings);
            titleButton = Action(main, "Return to title", 200);
            titleButton.onClick.AddListener(OpenConfirmation);
            FrontierUi.Chain(resume, settingsButton, titleButton);
            var keycap = PixelArt.Sliced("Esc key", main, PixelArt.Frame(FrontierUi.PanelDark,
                FrontierUi.PanelLight, FrontierUi.PanelDark, FrontierUi.PanelDark));
            FrontierUi.Place(keycap.rectTransform, PanelSize, Inset, 272, 48, 24);
            PixelLabel.Create("Key", keycap.transform, "Esc", 2, FrontierUi.Cream, .5f, false);
            Label(main, "Resume", 84, 272, 100, 24, 2).Color = FrontierUi.CreamDark;

            settings = Page("Settings");
            Label(settings, "Master volume", Inset, 76, 240, 20, 2);
            volumeValue = Label(settings, "", 272, 76, 72, 20, 2, 1f);
            volumeValue.Color = FrontierUi.CreamDark;
            volume = FrontierUi.Slider("Master volume", settings, VolumeSteps);
            FrontierUi.Place((RectTransform)volume.transform, PanelSize, Inset, 100, ContentWidth, 28);
            volume.onValueChanged.AddListener(value =>
            {
                FrontierUi.MasterVolume = value / VolumeSteps;
                UpdateSettings();
            });
            mix = new AudioMixRow(settings, stackedCaptions: true);
            FrontierUi.Place(mix.Root, PanelSize, Inset, 132, ContentWidth, AudioMixRow.StackedHeight);
            motion = Action(settings, "", 208);
            motion.onClick.AddListener(() =>
            {
                FrontierUi.ReduceMotion = !FrontierUi.ReduceMotion;
                UpdateSettings();
            });
            var back = Action(settings, "Back", 264);
            back.onClick.AddListener(BackFromSettings);
            FrontierUi.Chain(volume, mix.Sliders[0], mix.Sliders[1], mix.Sliders[2], motion, back);

            confirmation = Page("Return to title?");
            Label(confirmation, "Your mission will end", Inset, 88, ContentWidth, 32, 2)
                .Color = FrontierUi.CreamDark;
            keepPlaying = Action(confirmation, "Keep playing", 144, true);
            keepPlaying.onClick.AddListener(BackFromConfirmation);
            leave = Action(confirmation, "Return to title", 200);
            leave.interactable = Application.CanStreamedLevelBeLoaded(TitleScene);
            leave.onClick.AddListener(ReturnToTitle);
            FrontierUi.Chain(keepPlaying, leave);
        }

        RectTransform Page(string title)
        {
            var face = PixelArt.Sliced(title, panel, PixelArt.Frame(FrontierUi.Background,
                FrontierUi.PanelLight, FrontierUi.PanelLight, FrontierUi.PanelDark));
            face.raycastTarget = true;
            var page = face.rectTransform;
            Label(page, title, Inset, 24, ContentWidth, 32, 3);
            var line = FrontierUi.Fill("Header rule", page, FrontierUi.PanelLight);
            FrontierUi.Place(line.rectTransform, PanelSize, Inset, 64, ContentWidth, 1);
            return page;
        }

        static PixelLabel Label(Transform page, string caption, float x, float y, float width, float height,
            int maxScale, float alignment = 0f)
        {
            var label = PixelLabel.Create("Text", page, caption, maxScale, FrontierUi.Cream, alignment);
            FrontierUi.Place(label.Rect, PanelSize, x, y, width, height);
            return label;
        }

        static PixelButton Action(Transform page, string caption, float y, bool primary = false)
        {
            var button = FrontierUi.Button(caption, page, primary, titleAction: !primary);
            if (primary)
            {
                button.label.Rect.offsetMin = new Vector2(16, 0);
                button.label.Rect.offsetMax = new Vector2(-16, 0);
            }
            FrontierUi.Place((RectTransform)button.transform, PanelSize, Inset, y, ContentWidth, ActionHeight);
            return button;
        }

        void Show(RectTransform page, UnityEngine.UI.Selectable selection)
        {
            main.gameObject.SetActive(page == main);
            settings.gameObject.SetActive(page == settings);
            confirmation.gameObject.SetActive(page == confirmation);
            selection.Select();
        }

        void OpenSettings()
        {
            GameAudio.Play(Sfx.UiPanelOpen);
            volume.SetValueWithoutNotify(Mathf.Round(FrontierUi.MasterVolume * VolumeSteps));
            mix.Refresh();
            UpdateSettings();
            Show(settings, volume);
        }

        void OpenConfirmation()
        {
            GameAudio.Play(Sfx.UiPanelOpen);
            Show(confirmation, keepPlaying);
        }

        void BackFromSettings()
        {
            GameAudio.Play(Sfx.UiBack);
            Show(main, settingsButton);
        }

        void BackFromConfirmation()
        {
            GameAudio.Play(Sfx.UiBack);
            Show(main, titleButton);
        }

        void UpdateSettings()
        {
            volumeValue.Text = Mathf.RoundToInt(volume.value * 100 / VolumeSteps) + "%";
            motion.label.Text = FrontierUi.ReduceMotion ? "Reduce motion: On" : "Reduce motion: Off";
        }

        void ReturnToTitle()
        {
            if (loading || !leave.interactable) return;
            loading = true;
            leave.interactable = false;
            // Leaving while paused: undo the pause state (static flag, time scale, audio) so the title scene starts clean.
            if (active == this) active = null;
            leaving = true; // still block gameplay input while the title scene loads
            Time.timeScale = 1f;
            GameAudio.SetPaused(false);
            SceneManager.LoadSceneAsync(TitleScene);
        }
    }
}
