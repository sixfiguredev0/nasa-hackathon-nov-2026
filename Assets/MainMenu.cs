using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FrontierUi;

public sealed class MainMenu : MonoBehaviour
{
    private const float OrbitSeconds = 18, Tilt = 12 * Mathf.Deg2Rad, Flatten = .18f;
    // Visible bounds of the approved art, as fractions of each texture.
    private const float LogoLeft = .097f, LogoRight = .905f, LogoTop = .034f, LogoBottom = .949f;
    private const float MarsRadius = .448f;
    private const int VolumeSteps = 10;
    private const float PanelPad = 28, RowGap = 14, RowHeight = 52, HeadingHeight = 40;

    // Fullscreen and resolution only make sense in a Windows player, not in the editor or a browser.
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static readonly bool DisplayOptions = true;
#else
    private static readonly bool DisplayOptions = false;
#endif

    [SerializeField] private Texture2D logo;
    [SerializeField] private Texture2D mars;
    [SerializeField] private Texture2D satelliteArt;
    [SerializeField] private Texture2D sky;
    [SerializeField] private Texture2D blackHoleArt;
    [SerializeField, Tooltip("Scene name or full Assets/... .unity path from the active Build Profile scene list.")]
    private string gameplayScene = "SampleScene";

    private RectTransform canvas, layout, overlay;
    private RawImage title, planet, satellite, stars, blackHole;
    private Image star, fade, dim;
    private PixelButton start, options;
    private PixelLabel launchError;
    private EventSystem events;
    private Vector2 size, center;
    private float orbitWidth, motionTime;
    private bool narrow, reduced, launching;

    private RectTransform optionsPanel, volumeRow, controlsSection;
    private PixelLabel optionsHeading, volumeValue;
    private PixelButton motion, fullscreen, resolution, optionsBack;
    private PixelSlider volume;
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private int resolutionIndex;
    private bool fullscreenOn;

    private void Awake()
    {
        reduced = ReduceMotion;
        canvas = CreateCanvas(out events);
        stars = Art("Star field", canvas, sky);
        layout = FitLayout(canvas);
        blackHole = Art("Distant black hole", layout, Crisp(blackHoleArt));
        blackHole.color = new Color(1, 1, 1, .6f);

        // Back to front: relay on its rear pass, Mars, relay on its near pass. No orbit line is drawn.
        satellite = Art("Relay", layout, Crisp(satelliteArt));
        planet = Art("Mars", layout, mars);
        title = Art("Red Frontier", layout, logo);
        star = Fill("Title star glow", title.transform, Color.clear);
        Fill("Star glow center", star.transform, new Color32(255, 230, 191, 255))
            .rectTransform.localScale = new Vector3(.4f, .4f, 1);

        start = FrontierUi.Button("Start Mission", layout, true, titleAction: true);
        options = FrontierUi.Button("Options", layout, false, titleAction: true);
        var launchArrow = PixelLabel.Create("Launch arrow", start.face, ">", 4, Background, 1);
        launchArrow.Rect.offsetMax = new Vector2(-22, 0);
        launchError = PixelLabel.Create("Launch error", layout,
            "Game scene unavailable.\nCheck the Build Profile scene list.", 2, Cream, 0, true);
        launchError.gameObject.SetActive(false);
        Chain(start, options);

        // The dim stays behind Options and blocks clicks to the title menu.
        dim = Fill("Panel dim", canvas, new Color32(Background.r, Background.g, Background.b, 190));
        dim.raycastTarget = true;
        overlay = FitLayout(canvas);
        overlay.name = "Panel layout";
        BuildOptions();
        fade = Fill("Scene fade", canvas, new Color32(Background.r, Background.g, Background.b, 0));
        SetOptionsVisible(false);

        start.onClick.AddListener(Launch);
        options.onClick.AddListener(OpenOptions);
        optionsBack.onClick.AddListener(CloseOptions);
        events.firstSelectedGameObject = start.gameObject;
        start.Select();
        ApplyLayout(IsNarrow);
    }

    private void BuildOptions()
    {
        optionsPanel = PanelFrame("Options panel", overlay);
        optionsHeading = PixelLabel.Create("Heading", optionsPanel, "Options", 4, Cream, .5f, true);

        motion = FrontierUi.Button("Reduce motion", optionsPanel, false);
        motion.onClick.AddListener(ToggleMotion);
        UpdateMotionLabel();

        volumeRow = Box("Master volume", optionsPanel);
        var caption = PixelLabel.Create("Caption", volumeRow, "Master volume", 3, Cream, 0, true);
        volumeValue = PixelLabel.Create("Value", volumeRow, "", 3, Cream, 1, true);
        volume = FrontierUi.Slider("Volume slider", volumeRow, VolumeSteps);
        volume.SetValueWithoutNotify(Mathf.Round(MasterVolume * VolumeSteps));
        volume.onValueChanged.AddListener(value =>
        {
            MasterVolume = value / VolumeSteps;
            UpdateVolumeLabel();
        });
        UpdateVolumeLabel();
        Place(caption.Rect, new Vector2(1, 58), 0, 0, 1, 24);
        Place(volumeValue.Rect, new Vector2(1, 58), 0, 0, 1, 24);
        Place((RectTransform)volume.transform, new Vector2(1, 58), 0, 30, 1, 28);

        fullscreen = FrontierUi.Button("Fullscreen", optionsPanel, false);
        resolution = FrontierUi.Button("Resolution", optionsPanel, false);
        if (DisplayOptions)
        {
            fullscreenOn = Screen.fullScreenMode != FullScreenMode.Windowed;
            foreach (var mode in Screen.resolutions)
            {
                var entry = new Vector2Int(mode.width, mode.height);
                if (!resolutions.Contains(entry))
                    resolutions.Add(entry);
            }
            var current = new Vector2Int(Screen.width, Screen.height);
            resolutionIndex = resolutions.IndexOf(current);
            if (resolutionIndex < 0)
            {
                resolutions.Add(current);
                resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                resolutionIndex = resolutions.IndexOf(current);
            }
            fullscreen.onClick.AddListener(ToggleFullscreen);
            resolution.onClick.AddListener(NextResolution);
            UpdateDisplayLabels();
        }
        else
        {
            fullscreen.gameObject.SetActive(false);
            resolution.gameObject.SetActive(false);
        }

        controlsSection = Box("Controls", optionsPanel);
        var heading = PixelLabel.Create("Controls heading", controlsSection, "Controls", 3, Cream, 0);
        var instructions = PixelLabel.Create("Instructions", controlsSection,
            "Mouse / touch: Click or tap\nArrow keys / WASD: Move focus\nEnter / Space: Select\nEsc: Close options",
            2, Cream, 0, true);
        var controlsSize = new Vector2(1, 128);
        Place(heading.Rect, controlsSize, 0, 0, 1, 28);
        Place(instructions.Rect, controlsSize, 0, 32, 1, 96);

        optionsBack = FrontierUi.Button("Back", optionsPanel, false);
        Chain(motion, volume, fullscreen, resolution, optionsBack);
    }

    private void Update()
    {
        if (narrow != IsNarrow)
            ApplyLayout(IsNarrow);
        CoverSky(stars, sky);

        var selected = events.currentSelectedGameObject;
        if (optionsPanel.gameObject.activeSelf)
        {
            // Focus stays inside the open panel; Esc or the Cancel button closes it.
            if (Input.GetButtonDown("Cancel"))
                CloseOptions();
            else if (selected == null || !selected.transform.IsChildOf(optionsPanel))
                motion.Select();
        }
        else if (!launching && selected == null &&
                 (Input.GetAxisRaw("Horizontal") != 0 || Input.GetAxisRaw("Vertical") != 0))
            start.Select();

        if (!reduced)
        {
            motionTime += Time.unscaledDeltaTime;
            DrawMotion();
        }
    }

    private void ApplyLayout(bool isNarrow)
    {
        narrow = isNarrow;
        size = narrow ? new Vector2(500, 840) : new Vector2(1280, 720);
        Resize(canvas, size, layout, overlay);

        // Align the visible art with the outside edges of the two action rows.
        float left = narrow ? 50 : 88, width = narrow ? 400 : 452, top = narrow ? 32 : 128;
        float logoWidth = width / (LogoRight - LogoLeft);
        float logoHeight = logoWidth * logo.height / logo.width;
        Place(title.rectTransform, size, left - LogoLeft * logoWidth, top - LogoTop * logoHeight, logoWidth, logoHeight);
        Place(star.rectTransform, new Vector2(logoWidth, logoHeight),
            logoWidth * .717f - 7, logoHeight * .175f - 7, 14, 14);

        float logoEnd = top + (LogoBottom - LogoTop) * logoHeight;
        float launchHeight = narrow ? 64 : 72, secondaryHeight = narrow ? 54 : 58;
        const float gap = 16;
        float startY = narrow ? size.y - 48 - launchHeight - gap - secondaryHeight : logoEnd + 40;
        float secondaryY = startY + launchHeight + gap;
        Place((RectTransform)start.transform, size, left, startY, width, launchHeight);
        Place((RectTransform)options.transform, size, left, secondaryY, width, secondaryHeight);
        Place(launchError.Rect, size, left, secondaryY + secondaryHeight + 4, width, 42);

        float panelWidth = narrow ? 450 : 560;
        LayoutPanel(optionsPanel, optionsHeading, panelWidth,
            (motion.transform, RowHeight), (volumeRow, 58f), (fullscreen.transform, RowHeight),
            (resolution.transform, RowHeight), (controlsSection, 128f), (optionsBack.transform, RowHeight));

        // Mars fills the right half; the orbit's ends clear the silhouette by the relay's half-diagonal.
        float radius = narrow ? 174 : 264;
        center = narrow ? new Vector2(250, (logoEnd + startY) / 2) : new Vector2(950, 360);
        float planetSize = radius / MarsRadius;
        Place(planet.rectTransform, size, center.x - planetSize / 2, center.y - planetSize / 2, planetSize, planetSize);
        // Small, static accent in the open sky between the title and Mars.
        float hole = blackHoleArt.width;
        Place(blackHole.rectTransform, size, narrow ? 385 : 610, narrow ? 250 : 54, hole, hole);
        orbitWidth = radius + 32;
        CoverSky(stars, sky);
        DrawMotion();
    }

    // Stacks a heading and rows (skipping hidden ones) inside a panel centered on screen.
    private void LayoutPanel(RectTransform panel, PixelLabel heading, float width,
        params (Transform row, float height)[] rows)
    {
        float height = PanelPad + HeadingHeight + RowGap;
        foreach (var (row, rowHeight) in rows)
            if (row.gameObject.activeSelf)
                height += rowHeight + RowGap;
        height += PanelPad - RowGap;

        var panelSize = new Vector2(width, height);
        Place(panel, size, Mathf.Round((size.x - width) / 2), Mathf.Round((size.y - height) / 2), width, height);
        Place(heading.Rect, panelSize, PanelPad, PanelPad, width - 2 * PanelPad, HeadingHeight);
        float y = PanelPad + HeadingHeight + RowGap;
        foreach (var (row, rowHeight) in rows)
        {
            if (!row.gameObject.activeSelf)
                continue;
            Place((RectTransform)row, panelSize, PanelPad, y, width - 2 * PanelPad, rowHeight);
            y += rowHeight + RowGap;
        }
    }

    private static Vector2 Point(Vector2 center, float width, float angle)
    {
        float x = width * Mathf.Cos(angle), y = -width * Flatten * Mathf.Sin(angle);
        return center + new Vector2(x * Mathf.Cos(Tilt) + y * Mathf.Sin(Tilt),
            -x * Mathf.Sin(Tilt) + y * Mathf.Cos(Tilt));
    }

    private void DrawMotion()
    {
        float tau = 2 * Mathf.PI;
        float angle = Mathf.PI + tau * (reduced ? .17f : motionTime / OrbitSeconds + .02f);
        Vector2 point = Point(center, orbitWidth, angle);
        // Whole design pixels keep the point-filtered relay from shimmering as it moves.
        float w = satellite.texture.width, h = satellite.texture.height;
        Place(satellite.rectTransform, size, Mathf.Round(point.x - w / 2), Mathf.Round(point.y - h / 2), w, h);

        // The near pass is the lower half of the ellipse. Depth switches at the ends, clear of Mars.
        bool near = Mathf.Sin(angle) < 0;
        int relay = satellite.transform.GetSiblingIndex(), behind = planet.transform.GetSiblingIndex();
        if (near && relay < behind)
            satellite.transform.SetSiblingIndex(behind);
        else if (!near && relay > behind)
            satellite.transform.SetSiblingIndex(behind);

        float pulse = reduced ? 0 : .65f * Mathf.Pow(.5f - .5f * Mathf.Cos(motionTime * tau / 6), 4);
        var glow = new Color(1, .88f, .70f, pulse);
        star.color = glow;
        star.transform.GetChild(0).GetComponent<Image>().color = glow;
    }

    private void OpenOptions()
    {
        if (launching)
            return;
        SetMenuInteractable(false);
        SetOptionsVisible(true);
        motion.Select();
    }

    private void CloseOptions()
    {
        SetOptionsVisible(false);
        SetMenuInteractable(true);
        options.Select();
    }

    private void SetOptionsVisible(bool visible)
    {
        dim.gameObject.SetActive(visible);
        optionsPanel.gameObject.SetActive(visible);
    }

    private void SetMenuInteractable(bool value)
    {
        start.interactable = value;
        options.interactable = value;
    }

    private void ToggleMotion()
    {
        reduced = !reduced;
        ReduceMotion = reduced;
        UpdateMotionLabel();
        DrawMotion();
    }

    private void UpdateMotionLabel() => motion.label.Text = reduced ? "Reduce motion: On" : "Reduce motion: Off";

    private void UpdateVolumeLabel() => volumeValue.Text = Mathf.RoundToInt(volume.value * 100 / VolumeSteps) + "%";

    private void ToggleFullscreen()
    {
        fullscreenOn = !fullscreenOn;
        Screen.fullScreenMode = fullscreenOn ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        UpdateDisplayLabels();
    }

    private void NextResolution()
    {
        resolutionIndex = (resolutionIndex + 1) % resolutions.Count;
        var target = resolutions[resolutionIndex];
        Screen.SetResolution(target.x, target.y,
            fullscreenOn ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        UpdateDisplayLabels();
    }

    private void UpdateDisplayLabels()
    {
        fullscreen.label.Text = fullscreenOn ? "Fullscreen: On" : "Fullscreen: Off";
        var current = resolutions[resolutionIndex];
        resolution.label.Text = $"Resolution: {current.x} x {current.y}";
    }

    private void Launch()
    {
        if (launching)
            return;

        var destination = gameplayScene?.Trim();
        if (string.IsNullOrEmpty(destination) || !Application.CanStreamedLevelBeLoaded(destination))
        {
            launchError.gameObject.SetActive(true);
            return;
        }

        launchError.gameObject.SetActive(false);
        launching = true;
        start.HoldPressed = true;
        SetMenuInteractable(false);
        fade.raycastTarget = true;
        if (reduced)
            SceneManager.LoadSceneAsync(destination);
        else
            StartCoroutine(FadeToMission(destination));
    }

    private IEnumerator FadeToMission(string destination)
    {
        for (float elapsed = 0; elapsed < .2f; elapsed += Time.unscaledDeltaTime)
        {
            fade.color = new Color32(Background.r, Background.g, Background.b, (byte)(255 * elapsed / .2f));
            yield return null;
        }
        fade.color = Background;
        SceneManager.LoadSceneAsync(destination);
    }

    // Binary alpha so the scaled-down relay has no soft fringe over the sky or Mars.
    private static Texture2D Crisp(Texture2D source)
    {
        var pixels = source.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].a = (byte)(pixels[i].a < 128 ? 0 : 255);
        var crisp = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        crisp.SetPixels32(pixels);
        crisp.Apply(false, true);
        return crisp;
    }
}
