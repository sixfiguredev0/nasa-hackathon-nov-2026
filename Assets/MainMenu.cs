using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
    private RawImage title, titleStar, planet, satellite, stars, blackHole;
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
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private int resolutionIndex;
    private bool fullscreenOn;

    private const float CardHeight = 250, ArtSize = 128;
    private RectTransform crewPanel;
    private PixelLabel crewHeading;
    private PixelButton pickFemale, pickMale, crewBack;

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

        start = FrontierUi.Button("Start", layout, true, titleAction: true);
        options = FrontierUi.Button("Options", layout, false, titleAction: true);
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
        BuildCrewPick();
        fade = Fill("Scene fade", canvas, new Color32(Background.r, Background.g, Background.b, 0));
        SetOptionsVisible(false);
        SetCrewPickVisible(false);

        start.onClick.AddListener(OpenCrewPick);
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

    // Start opens this: pick the astronaut who lands with the rocket, then the outpost loads.
    private void BuildCrewPick()
    {
        crewPanel = PanelFrame("Crew panel", overlay);
        crewHeading = PixelLabel.Create("Heading", crewPanel, "Choose your astronaut", 4, Cream, .5f, true);
        pickFemale = CrewCard("Woman", "AstronautFemale", 0, Astronaut.Female);
        pickMale = CrewCard("Man", "AstronautMale", 1, Astronaut.Male);
        crewBack = FrontierUi.Button("Back", crewPanel, false);
        crewBack.onClick.AddListener(CloseCrewPick);
        Chain(pickFemale, pickMale, crewBack);
    }

    // A tall button showing the astronaut's south-facing frame (frame index from Crew's direction map).
    private PixelButton CrewCard(string caption, string sheet, int southFrame, Astronaut who)
    {
        var card = FrontierUi.Button(caption, crewPanel, false);
        var art = Resources.Load<Texture2D>("Crew/" + sheet);
        if (art != null)
        {
            int frames = Mathf.Max(1, art.width / art.height);
            var image = Art("Astronaut", card.face, art);
            image.uvRect = new Rect((float)southFrame / frames, 0, 1f / frames, 1);
            var r = image.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1);
            r.sizeDelta = new Vector2(ArtSize, ArtSize);
            r.anchoredPosition = new Vector2(0, -36);
        }
        var label = card.label.Rect;
        label.anchorMin = Vector2.zero;
        label.anchorMax = new Vector2(1, 0);
        label.offsetMin = new Vector2(8, 20);
        label.offsetMax = new Vector2(-8, 60);
        card.onClick.AddListener(() => Pick(who));
        return card;
    }

    private void Update()
    {
        if (launching) return;
        if (narrow != IsNarrow)
            ApplyLayout(IsNarrow);
        CoverSky(stars, sky);

        var selected = events.currentSelectedGameObject;
        if (crewPanel.gameObject.activeSelf)
        {
            if (Input.GetButtonDown("Cancel") && !launching)
                CloseCrewPick();
            else if (!launching && (selected == null || !selected.transform.IsChildOf(crewPanel)))
                pickFemale.Select();
        }
        else if (optionsPanel.gameObject.activeSelf)
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
        float launchWidth = narrow ? 260 : 270, optionsWidth = narrow ? 172 : 178;
        float launchHeight = narrow ? 60 : 62, secondaryHeight = 44;
        const float gap = 12;
        float startY = narrow ? size.y - 48 - launchHeight - gap - secondaryHeight : logoEnd + 35;
        float secondaryY = startY + launchHeight + gap;
        float actionLeft = narrow ? (size.x - launchWidth) / 2 : left;
        float optionsLeft = narrow ? (size.x - optionsWidth) / 2 : left;
        Place((RectTransform)start.transform, size, actionLeft, startY, launchWidth, launchHeight);
        Place((RectTransform)options.transform, size, optionsLeft, secondaryY, optionsWidth, secondaryHeight);
        Place(launchError.Rect, size, left, secondaryY + secondaryHeight + 4, width, 42);

        float panelWidth = narrow ? 450 : 560;
        LayoutPanel(optionsPanel, optionsHeading, panelWidth,
            (motion.transform, RowHeight), (volumeRow, 58f), (fullscreen.transform, RowHeight),
            (resolution.transform, RowHeight), (controlsSection, 128f), (optionsBack.transform, RowHeight));
        LayoutCrewPick(panelWidth);

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

    private void LayoutCrewPick(float width)
    {
        float height = PanelPad + HeadingHeight + RowGap + CardHeight + RowGap + RowHeight + PanelPad;
        var panelSize = new Vector2(width, height);
        Place(crewPanel, size, Mathf.Round((size.x - width) / 2), Mathf.Round((size.y - height) / 2), width, height);
        Place(crewHeading.Rect, panelSize, PanelPad, PanelPad, width - 2 * PanelPad, HeadingHeight);
        float inner = width - 2 * PanelPad, cardWidth = Mathf.Floor((inner - RowGap) / 2);
        float y = PanelPad + HeadingHeight + RowGap;
        Place((RectTransform)pickFemale.transform, panelSize, PanelPad, y, cardWidth, CardHeight);
        Place((RectTransform)pickMale.transform, panelSize, width - PanelPad - cardWidth, y, cardWidth, CardHeight);
        Place((RectTransform)crewBack.transform, panelSize, PanelPad, y + CardHeight + RowGap, inner, RowHeight);
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

        if (titleStar != null)
        {
            // Keep the star tethered to the orbit. During each cycle it lifts a few design pixels.
            float lift = reduced ? 0f : Mathf.Round(4f * (1f - Mathf.Cos(motionTime * tau / 5f)));
            // Crop bounds in the original 1774 × 887 wordmark artwork.
            Place(titleStar.rectTransform, logoSize, logoSize.x * (1140f / 1774f),
                logoSize.y * (20f / 887f) - lift, logoSize.x * (252f / 1774f), logoSize.y * (290f / 887f));
        }
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

    private void OpenCrewPick()
    {
        if (launching)
            return;
        SetMenuInteractable(false);
        SetCrewPickVisible(true);
        pickFemale.Select();
    }

    private void CloseCrewPick()
    {
        SetCrewPickVisible(false);
        SetMenuInteractable(true);
        start.Select();
    }

    private void SetCrewPickVisible(bool visible)
    {
        dim.gameObject.SetActive(visible);
        crewPanel.gameObject.SetActive(visible);
    }

    private void Pick(Astronaut who)
    {
        if (launching)
            return;
        Crew.Picked = new[] { who };
        Launch();
        if (!launching)
        {
            CloseCrewPick(); // scene missing: Launch showed the error under the title buttons
            return;
        }
        SetCrewPickVisible(false);
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
            StartCoroutine(FlyToMars(destination));
    }

    private IEnumerator FlyToMars(string destination)
    {
        const float arrowSeconds = .70f, approachSeconds = 1.45f;
        Vector3 arrowWorld = launchArrow.Rect.TransformPoint(
            new Vector3(launchArrow.Rect.rect.xMax - 7f, launchArrow.Rect.rect.center.y));
        Vector3 local = layout.InverseTransformPoint(arrowWorld);
        Vector2 origin = new Vector2((local.x / layout.rect.width + layout.pivot.x) * size.x,
            (1f - local.y / layout.rect.height - layout.pivot.y) * size.y);
        Vector2 entry = center + new Vector2(-size.x * .065f, -size.y * .105f);
        Vector2 bend = new Vector2(Mathf.Lerp(origin.x, entry.x, .55f), Mathf.Min(origin.y, entry.y) - 12f);

        var flyingArrow = PixelLabel.Create("Launch arrow in flight", layout, ">", 9, Cream, .5f, true);
        var menuArt = title.gameObject.AddComponent<CanvasGroup>();
        var navigation = new[] { start.group, options.group };
        launchArrow.gameObject.SetActive(false);
        float planetSize = planet.rectTransform.rect.width / layout.rect.width * size.x;
        Vector2 viewportCenter = size / 2f;

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

            float flight = Mathf.Clamp01(elapsed / arrowSeconds);
            Vector2 arrow = Vector2.Lerp(Vector2.Lerp(origin, bend, flight), Vector2.Lerp(bend, focus, flight), flight);
            float arrowSize = Mathf.Lerp(32f, 86f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flight * 3f)));
            Place(flyingArrow.Rect, size, Mathf.Round(arrow.x - arrowSize / 2f),
                Mathf.Round(arrow.y - arrowSize / 2f), arrowSize, arrowSize);
            flyingArrow.Color = new Color(Cream.r / 255f, Cream.g / 255f, Cream.b / 255f,
                1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.75f, 1f, flight)));

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
        MissionArrival.Load(destination);
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
