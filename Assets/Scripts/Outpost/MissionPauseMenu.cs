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
        static readonly Vector2 PanelSize = new Vector2(402, 389);
        static MissionPauseMenu active;
        static int releasedFrame = -1;

        internal static bool BlocksGameplayInput => active != null || releasedFrame == Time.frameCount || MissionDebrief.IsOpen;

        RectTransform canvas, panel, main, settings, confirmation;
        EventSystem events;
        bool ownsEvents;
        GameObject priorSelection;
        PixelButton resume, settingsButton, titleButton, nasaData, motion, keepPlaying, leave;
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
            FrontierUi.Fill("Dim outpost", canvas, new Color32(20, 17, 25, 150)).raycastTarget = true;

            var layout = FrontierUi.FitLayout(canvas);
            var reference = new Vector2(960, 720);
            FrontierUi.Resize(canvas, reference, layout);
            panel = FrontierUi.Box("Pause panel", layout);
            FrontierUi.Place(panel, reference, (reference.x - PanelSize.x) / 2, (reference.y - PanelSize.y) / 2,
                PanelSize.x, PanelSize.y);

            main = Page("Mission paused");
            resume = Action(main, "Resume mission", 91, true);
            resume.onClick.AddListener(Resume);
            settingsButton = Action(main, "Settings", 154);
            settingsButton.onClick.AddListener(OpenSettings);
            titleButton = Action(main, "Return to title", 217);
            titleButton.onClick.AddListener(OpenConfirmation);
            nasaData = Action(main, "Real NASA data", 280);
            nasaData.onClick.AddListener(OpenNasaData);
            FrontierUi.Chain(resume, settingsButton, titleButton, nasaData);
            var keycap = PixelArt.Sliced("Esc key", main, PixelArt.Frame(FrontierUi.PanelDark,
                FrontierUi.Mauve, FrontierUi.CreamDark, FrontierUi.Mauve));
            FrontierUi.Place(keycap.rectTransform, PanelSize, 137, 345, 46, 23);
            PixelLabel.Create("Key", keycap.transform, "Esc", 2, FrontierUi.Cream, .5f, false);
            Label(main, "Resume", 196, 342, 105, 28, 2);

            settings = Page("Settings");
            Label(settings, "Master volume", 29, 78, 240, 26, 2);
            volumeValue = Label(settings, "", 270, 78, 103, 26, 2, 1f);
            volume = FrontierUi.Slider("Master volume", settings, VolumeSteps);
            FrontierUi.Place((RectTransform)volume.transform, PanelSize, 29, 104, 344, 30);
            volume.onValueChanged.AddListener(value =>
            {
                FrontierUi.MasterVolume = value / VolumeSteps;
                UpdateSettings();
            });
            mix = new AudioMixRow(settings);
            FrontierUi.Place(mix.Root, PanelSize, 29, 142, 344, AudioMixRow.Height);
            motion = Action(settings, "", 208, height: 44);
            motion.onClick.AddListener(() =>
            {
                FrontierUi.ReduceMotion = !FrontierUi.ReduceMotion;
                UpdateSettings();
            });
            var back = Action(settings, "Back", 260, height: 44);
            back.onClick.AddListener(BackFromSettings);
            FrontierUi.Chain(volume, mix.Sliders[0], mix.Sliders[1], mix.Sliders[2], motion, back);

            confirmation = Page("Return to title?");
            Label(confirmation, "The current mission\nwill end.", 29, 84, 344, 61, 3, .5f);
            keepPlaying = Action(confirmation, "Keep playing", 163, true);
            keepPlaying.onClick.AddListener(BackFromConfirmation);
            leave = Action(confirmation, "Return to title", 231);
            leave.interactable = Application.CanStreamedLevelBeLoaded(TitleScene);
            leave.onClick.AddListener(ReturnToTitle);
            FrontierUi.Chain(keepPlaying, leave);
        }

        RectTransform Page(string title)
        {
            var face = PixelArt.Sliced(title, panel, PixelArt.Frame(FrontierUi.Background,
                FrontierUi.Mauve, FrontierUi.CoralLight, FrontierUi.Mauve));
            face.raycastTarget = true;
            var page = face.rectTransform;
            var inset = FrontierUi.PanelFrame("Pause symbol", page);
            FrontierUi.Place(inset, PanelSize, 24, 18, 54, 49);
            for (int i = 0; i < 2; i++)
            {
                var bar = FrontierUi.Fill("Pause bar", inset, FrontierUi.Cream);
                FrontierUi.Place(bar.rectTransform, new Vector2(54, 49), 16 + i * 13, 12, 8, 25);
            }
            Label(page, title, 91, 18, 282, 49, 3);
            var line = FrontierUi.Fill("Header rule", page, FrontierUi.Mauve);
            FrontierUi.Place(line.rectTransform, PanelSize, 24, 70, 354, 2);
            return page;
        }

        static PixelLabel Label(Transform page, string caption, float x, float y, float width, float height,
            int maxScale, float alignment = 0f)
        {
            var label = PixelLabel.Create("Text", page, caption, maxScale, FrontierUi.Cream, alignment, true);
            FrontierUi.Place(label.Rect, PanelSize, x, y, width, height);
            return label;
        }

        static PixelButton Action(Transform page, string caption, float y, bool primary = false, float height = 47)
        {
            var button = FrontierUi.Button(caption, page, primary, titleAction: !primary);
            if (primary)
            {
                button.label.Rect.offsetMin = new Vector2(16, 0);
                button.label.Rect.offsetMax = new Vector2(-16, 0);
            }
            FrontierUi.Place((RectTransform)button.transform, PanelSize, 29, y, 344, height);
            return button;
        }

        void Show(RectTransform page, UnityEngine.UI.Selectable selection)
        {
            main.gameObject.SetActive(page == main);
            settings.gameObject.SetActive(page == settings);
            confirmation.gameObject.SetActive(page == confirmation);
            selection.Select();
        }

        void OpenNasaData()
        {
            Resume();
            if (Skywatch.Instance != null) Skywatch.Instance.OpenDataScreen();
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
            SceneManager.LoadSceneAsync(TitleScene);
        }
    }
}
