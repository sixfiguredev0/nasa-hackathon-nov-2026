using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RedFrontier.Audio;
using RedFrontier.Outpost;
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
    [SerializeField] private Texture2D titleStarArt;
    [SerializeField] private Texture2D mars;
    [SerializeField] private Texture2D satelliteArt;
    [SerializeField] private Texture2D sky;
    [SerializeField] private Texture2D blackHoleArt;
    [SerializeField, Tooltip("Scene name or full Assets/... .unity path from the active Build Profile scene list.")]
    private string gameplayScene = "SampleScene";

    private RectTransform canvas, layout, overlay;
    private RawImage title, titleStar, planet, satellite, stars, blackHole, rocket;
    private RectTransform flame;
    private Vector2 rocketPos;
    private float rocketSize;
    private Image fade, dim;
    private PixelButton start, options;
    private PixelLabel launchArrow, launchError;
    private EventSystem events;
    private Vector2 size, center, logoSize;
    private float orbitWidth, motionTime;
    private bool narrow, reduced, launching;

    private RectTransform optionsPanel, volumeRow, controlsSection;
    private PixelLabel optionsHeading, volumeValue;
    private PixelButton motion, fullscreen, resolution, optionsBack;
    private PixelSlider volume;
    private AudioMixRow mix;
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private readonly List<Texture2D> createdTextures = new List<Texture2D>();
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
        if (titleStarArt != null)
            titleStar = Art("Title star", title.transform, titleStarArt);

        BuildRocket();
        start = FrontierUi.Button("Start", layout, true, titleAction: true);
        options = FrontierUi.Button("Options", layout, false, titleAction: true, leftAligned: true);
        launchArrow = PixelLabel.Create("Launch arrow", start.face, ">", 4, Background, 1);
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
        GameAudio.EnterTitle();
    }

    // The landed frame of the real rocket art, flying nose-up with an animated pixel flame.
    private void BuildRocket()
    {
        var art = Resources.Load<Texture2D>("Buildings/Rocket");
        rocket = Art("Rocket", layout, art);
        if (art != null)
        {
            int frames = Mathf.Max(1, art.width / Mathf.Max(1, art.height));
            rocket.uvRect = new Rect((frames - 1f) / frames, 0, 1f / frames, 1);
        }
        else
        {
            // Keep the menu working without the rocket art: an invisible placeholder still anchors the flame and launch.
            Debug.LogWarning("MainMenu: Resources/Buildings/Rocket texture is missing; the rocket art is skipped.");
            rocket.color = Color.clear;
        }

        var tex = new Texture2D(7, 14, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        createdTextures.Add(tex);
        for (int y = 0; y < 14; y++)
            for (int x = 0; x < 7; x++)
            {
                float reach = 3.5f * y / 13f, d = Mathf.Abs(x - 3);
                tex.SetPixel(x, y, d > reach ? Color.clear : d < reach * .45f ? (Color)Cream
                    : d < reach * .8f ? (Color)new Color32(255, 170, 60, 255) : (Color)Coral);
            }
        tex.Apply(false, true);
        var image = Art("Flame", rocket.transform, tex);
        flame = image.rectTransform;
        flame.anchorMin = new Vector2(.38f, -.12f);
        flame.anchorMax = new Vector2(.62f, .2f);
        flame.pivot = new Vector2(.5f, 1);
        flame.gameObject.SetActive(false);
    }

    private void PlaceRocket(Vector2 at, float frame, float angle)
    {
        Place(rocket.rectTransform, size, Mathf.Round(at.x - frame / 2), Mathf.Round(at.y - frame / 2), frame, frame);
        rocket.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        flame.localScale = new Vector3(1, 1 + (reduced ? 0 : .18f * Mathf.Sin(Time.unscaledTime * 30f)), 1);
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
        mix = new AudioMixRow(optionsPanel);

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
        Chain(motion, volume, mix.Sliders[0], mix.Sliders[1], mix.Sliders[2], fullscreen, resolution, optionsBack);
    }

    private void Update()
    {
        if (launching) return;
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

        // Align both actions with the visible left edge of the wordmark.
        float left = narrow ? 50 : 88, width = narrow ? 400 : 452, top = narrow ? 32 : 128;
        float logoWidth = width / (LogoRight - LogoLeft);
        float logoHeight = logoWidth * logo.height / logo.width;
        Place(title.rectTransform, size, left - LogoLeft * logoWidth, top - LogoTop * logoHeight, logoWidth, logoHeight);
        logoSize = new Vector2(logoWidth, logoHeight);

        float logoEnd = top + (LogoBottom - LogoTop) * logoHeight;
        float launchWidth = narrow ? 288 : 312, optionsWidth = narrow ? 196 : 212;
        const float launchHeight = 56, secondaryHeight = 44;
        const float gap = 12;
        float startY = narrow ? size.y - 48 - launchHeight - gap - secondaryHeight : logoEnd + 36;
        float secondaryY = startY + launchHeight + gap;
        rocketSize = narrow ? 144 : 200;
        rocketPos = narrow ? new Vector2(104, (logoEnd + startY) / 2) : new Vector2(560, startY + 76);
        float actionLeft = narrow ? (size.x - launchWidth) / 2 : left;
        Place((RectTransform)start.transform, size, actionLeft, startY, launchWidth, launchHeight);
        Place((RectTransform)options.transform, size, actionLeft, secondaryY, optionsWidth, secondaryHeight);
        Place(launchError.Rect, size, actionLeft, secondaryY + secondaryHeight + 4, width, 42);

        float panelWidth = narrow ? 450 : 560;
        LayoutPanel(optionsPanel, optionsHeading, panelWidth,
            (motion.transform, RowHeight), (volumeRow, 58f), (mix.Root, AudioMixRow.Height), (fullscreen.transform, RowHeight),
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

        if (!launching)
            PlaceRocket(rocketPos, rocketSize, 0);

        if (titleStar != null)
        {
            // Crop bounds in the original 1774 x 887 wordmark artwork.
            Place(titleStar.rectTransform, logoSize, logoSize.x * (1140f / 1774f),
                logoSize.y * (20f / 887f), logoSize.x * (252f / 1774f), logoSize.y * (290f / 887f));
        }
    }

    private void OpenOptions()
    {
        if (launching)
            return;
        SetMenuInteractable(false);
        mix.Refresh();
        SetOptionsVisible(true);
        motion.Select();
        GameAudio.Play(Sfx.UiPanelOpen);
    }

    private void CloseOptions()
    {
        GameAudio.Play(Sfx.UiBack);
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
            GameAudio.Play(Sfx.UiDenied);
            return;
        }

        launchError.gameObject.SetActive(false);
        Crew.Picked = new[] { Astronaut.Male };
        launching = true;
        GameAudio.Launch(reduced); // whoosh follows the 1.45 s flight; Reduce Motion skips it, arrival music plays either way
        start.HoldPressed = true;
        SetMenuInteractable(false);
        fade.raycastTarget = true;
        if (reduced)
            MissionArrival.Load(destination, rocket.texture, rocket.uvRect);
        else
            StartCoroutine(FlyToMars(destination));
    }

    private IEnumerator FlyToMars(string destination)
    {
        const float approachSeconds = 1.45f, turnSeconds = .4f;
        Vector2 entry = center + new Vector2(-size.x * .065f, -size.y * .105f);

        var menuArt = title.gameObject.AddComponent<CanvasGroup>();
        var navigation = new[] { start.group, options.group };
        launchArrow.gameObject.SetActive(false);
        flame.gameObject.SetActive(true);
        float planetSize = planet.rectTransform.rect.width / layout.rect.width * size.x;
        Vector2 viewportCenter = size / 2f;
        Vector2 rocketStart = rocketPos;

        for (float elapsed = 0f; elapsed < approachSeconds; elapsed += Time.unscaledDeltaTime)
        {
            float approach = Mathf.Clamp01((elapsed - .26f) / (approachSeconds - .26f));
            float zoom = approach * approach;
            float scale = Mathf.Lerp(1f, 4.2f, zoom);
            Vector2 focus = Vector2.Lerp(entry, viewportCenter, Mathf.SmoothStep(0f, 1f, approach));
            Vector2 imageCenter = focus + (center - entry) * scale;
            float diameter = planetSize * scale;
            Place(planet.rectTransform, size, imageCenter.x - diameter / 2f, imageCenter.y - diameter / 2f,
                diameter, diameter);

            // The camera follows the rocket: it flips nose-down, then stays on the focus point as the planet swells.
            float turn = Mathf.SmoothStep(0f, 1f, elapsed / turnSeconds);
            Vector2 at = Vector2.Lerp(rocketStart, focus, Mathf.SmoothStep(0f, 1f, elapsed / .55f));
            PlaceRocket(at, Mathf.Lerp(rocketSize, rocketSize * .4f, zoom), 180f * turn);

            float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, .83f, elapsed));
            menuArt.alpha = opacity;
            foreach (var button in navigation) button.alpha = opacity;
            satellite.color = new Color(1f, 1f, 1f, opacity);
            blackHole.color = new Color(1f, 1f, 1f, .6f * opacity);
            fade.color = new Color(Background.r / 255f, Background.g / 255f, Background.b / 255f,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.14f, approachSeconds, elapsed)));
            yield return null;
        }

        fade.color = Background;
        MissionArrival.Load(destination, rocket.texture, rocket.uvRect);
    }

    private void OnDestroy()
    {
        // Textures generated at runtime (crisp copies, flame) are not owned by any asset; free them with the menu.
        foreach (var texture in createdTextures)
            if (texture != null)
                Destroy(texture);
        createdTextures.Clear();
    }

    // Binary alpha so the scaled-down relay has no soft fringe over the sky or Mars.
    // Falls back to the source texture (soft alpha) when it is missing or not CPU-readable.
    private Texture2D Crisp(Texture2D source)
    {
        if (source == null || !source.isReadable)
        {
            if (source != null)
                Debug.LogWarning($"MainMenu: '{source.name}' is not readable; using it without crisp alpha.");
            return source;
        }

        Color32[] pixels;
        try
        {
            pixels = source.GetPixels32();
        }
        catch (UnityException error)
        {
            Debug.LogWarning($"MainMenu: could not read '{source.name}' ({error.Message}); using it without crisp alpha.");
            return source;
        }
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].a = (byte)(pixels[i].a < 128 ? 0 : 255);
        var crisp = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        crisp.SetPixels32(pixels);
        crisp.Apply(false, true);
        createdTextures.Add(crisp);
        return crisp;
    }
}
