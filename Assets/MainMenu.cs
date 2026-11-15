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
    private Vector2 rocketPos, hullCenter, startSize, optionsSize;
    private float rocketSize, rocketFrame, rocketAngle, throttle = .6f, warp;
    private Vector2 rocketAt;
    private TitleFx fx;
    private const float MenuHeading = -90; // nose points right, at Mars
    private const float ButtonGap = 12;

    // The title buttons are backlit display panels set into the hull: the PixelButton stays for clicks and
    // focus, its frame is hidden. At rest the letters glow cyan behind scanlined glass and a sheen sweeps
    // across now and then; lit, they flare warm white with a halo, the glass warms and chevrons point in.
    private sealed class Lettering
    {
        public PixelButton button;
        public PixelLabel text;
        public PixelLabel[] halo, chevrons;
        public Image warmth;
        public RectTransform sheen, glass;
        public float level, sheenDelay;
    }
    private static readonly Color Glass = new Color32(8, 22, 28, 255), GlassRim = new Color32(2, 12, 16, 255),
        GlassShadow = new Color32(0, 6, 8, 255), HullShine = new Color32(184, 250, 254, 255),
        Backlight = new Color32(120, 234, 255, 255), Flare = new Color32(255, 244, 214, 255);
    private static readonly Vector2Int[] HaloOffsets =
    {
        new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1),
        new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1),
        new Vector2Int(-2, 0), new Vector2Int(2, 0), new Vector2Int(0, -2), new Vector2Int(0, 2),
    };
    private static Texture2D scanlines;
    private Lettering startText, optionsText;
    private bool keyboardNav;
    private Vector3 lastMouse;
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
        var starStreams = Box("Star streams", layout);
        blackHole = Art("Distant black hole", layout, Crisp(blackHoleArt));
        blackHole.color = new Color(1, 1, 1, .6f);

        // Back to front: relay on its rear pass, Mars, relay on its near pass. No orbit line is drawn.
        satellite = Art("Relay", layout, Crisp(satelliteArt));
        planet = Art("Mars", layout, mars);
        title = Art("Red Frontier", layout, logo);
        if (titleStarArt != null)
            titleStar = Art("Title star", title.transform, titleStarArt);

        var exhaust = Box("Exhaust", layout);
        BuildRocket();
        fx = new TitleFx(gameObject, starStreams, exhaust, Box("Telemetry", layout));
        start = FrontierUi.Button("Start", layout, true, titleAction: true);
        options = FrontierUi.Button("Options", layout, false, titleAction: true, leftAligned: true);
        launchArrow = PixelLabel.Create("Launch arrow", start.face, ">", 4, Background, 1);
        launchArrow.Rect.offsetMax = new Vector2(-22, 0);
        launchError = PixelLabel.Create("Launch error", layout,
            "Game scene unavailable.\nCheck the Build Profile scene list.", 2, Cream, 0, true);
        launchError.gameObject.SetActive(false);
        Chain(start, options);
        startText = HullLettering(start, "Start", 6, 0f);
        optionsText = HullLettering(options, "Options", 4, 1.7f);

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
        // Nothing starts selected, so the lettering only lights on hover or once the keyboard is used.
        ApplyLayout(IsNarrow);
        GameAudio.EnterTitle();
    }

    // The landed frame of the real rocket art, lying nose-right with an animated pixel flame at its base.
    private void BuildRocket()
    {
        var art = Resources.Load<Texture2D>("Menu/RocketFlight"); // landed rocket art, boarding stairs removed
        int frames = Mathf.Max(1, art.width / art.height);
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
        flame.anchorMin = new Vector2(.42f, -.12f);
        flame.anchorMax = new Vector2(.63f, .2f);
        flame.pivot = new Vector2(.5f, 1);
        flame.gameObject.SetActive(false);
    }

    private void PlaceRocket(Vector2 at, float frame, float angle)
    {
        rocketAt = at;
        rocketFrame = frame;
        rocketAngle = angle;
        Place(rocket.rectTransform, size, Mathf.Round(at.x - frame / 2), Mathf.Round(at.y - frame / 2), frame, frame);
        rocket.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        float flicker = reduced ? 1 : 1 + .18f * Mathf.Sin(Time.unscaledTime * 30f);
        flame.localScale = new Vector3(Mathf.Min(1.4f, .8f + .3f * throttle), flicker * throttle, 1);
    }

    private static Lettering HullLettering(PixelButton button, string caption, int scale, float sheenDelay)
    {
        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            if (graphic.gameObject != button.gameObject) // the root keeps the clear hit area
                graphic.enabled = false;
        var lettering = new Lettering { button = button, halo = new PixelLabel[HaloOffsets.Length], sheenDelay = sheenDelay };

        // Recessed panel: dark rim, shadowed top lip, lit bottom lip, glass inside.
        var panel = PixelArt.Sliced("Display panel", button.face, PixelArt.Frame(Glass, GlassRim, GlassShadow, HullShine));
        lettering.glass = Box("Glass", panel.transform);
        lettering.glass.offsetMin = lettering.glass.offsetMax = Vector2.zero;
        lettering.glass.offsetMin = new Vector2(2 * PixelArt.Texel, 2 * PixelArt.Texel);
        lettering.glass.offsetMax = new Vector2(-2 * PixelArt.Texel, -2 * PixelArt.Texel);
        lettering.glass.gameObject.AddComponent<RectMask2D>();
        lettering.warmth = Fill("Warm glow", lettering.glass, Color.clear);
        lettering.sheen = Fill("Sheen", lettering.glass, new Color(1f, 1f, 1f, .14f)).rectTransform;
        lettering.sheen.anchorMin = new Vector2(0, 0);
        lettering.sheen.anchorMax = new Vector2(0, 1);
        lettering.sheen.sizeDelta = new Vector2(4 * PixelArt.Texel, 0);

        for (int i = 0; i < HaloOffsets.Length; i++)
        {
            var halo = PixelLabel.Create("Glow", lettering.glass, caption, scale, Color.clear);
            halo.Rect.offsetMin = halo.Rect.offsetMax = (Vector2)HaloOffsets[i] * scale;
            lettering.halo[i] = halo;
        }
        lettering.text = PixelLabel.Create("Lettering", lettering.glass, caption, scale, Backlight);

        // Chevrons either side, shown while lit.
        lettering.chevrons = new PixelLabel[2];
        for (int side = 0; side < 2; side++)
        {
            var chevron = PixelLabel.Create("Chevron", lettering.glass, side == 0 ? ">" : "<", scale - 1, Color.clear, side, true);
            chevron.Rect.anchorMin = new Vector2(side, 0);
            chevron.Rect.anchorMax = new Vector2(side, 1);
            chevron.Rect.pivot = new Vector2(side, .5f);
            chevron.Rect.sizeDelta = new Vector2(6 * scale, 0);
            lettering.chevrons[side] = chevron;
        }

        // Scanlines over everything on the glass.
        if (scanlines == null)
        {
            scanlines = new Texture2D(1, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            scanlines.SetPixels32(new[] { new Color32(0, 0, 0, 70), new Color32(0, 0, 0, 0) });
            scanlines.Apply(false, true);
        }
        Art("Scanlines", lettering.glass, scanlines);
        return lettering;
    }

    private void Glow(Lettering lettering, bool lit)
    {
        float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
        lettering.level = Mathf.MoveTowards(lettering.level, lit ? 1 : 0, dt / .12f);
        float level = lettering.level;
        // A faint, uneven flicker in the backlight keeps the panels alive at rest.
        float flicker = reduced ? 1f : .9f + .1f * Mathf.PerlinNoise(now * 3f, lettering.sheenDelay);
        lettering.text.Color = Color.Lerp(Backlight * flicker, Flare, level);
        float pulse = reduced ? 1f : 1f + .08f * Mathf.Sin(now * 9f);
        for (int i = 0; i < lettering.halo.Length; i++)
        {
            Color rest = new Color(Backlight.r, Backlight.g, Backlight.b, (i < 4 ? .12f : 0f) * flicker);
            Color hot = i < 8 ? new Color(1f, .75f, .35f, .85f * pulse) : new Color(1f, .47f, .2f, .45f * pulse);
            lettering.halo[i].Color = Color.Lerp(rest, hot, level);
        }
        lettering.warmth.color = new Color(1f, .62f, .3f, .16f * level);

        // Chevrons slide in a little and nudge towards the word while lit.
        float nudge = reduced ? 0f : Mathf.Round(Mathf.Repeat(now * 2.5f, 1f)) * PixelArt.Texel;
        for (int side = 0; side < 2; side++)
        {
            var chevron = lettering.chevrons[side];
            chevron.Color = new Color(Flare.r, Flare.g, Flare.b, level);
            float x = Mathf.Round((1f - level) * -8f + nudge + 2 * PixelArt.Texel);
            chevron.Rect.anchoredPosition = new Vector2(side == 0 ? x : -x, 0);
        }

        // The sheen crosses the glass every few seconds.
        var glassWidth = lettering.glass.rect.width;
        float cycle = Mathf.Repeat(now + lettering.sheenDelay, 3.6f) / .7f;
        lettering.sheen.gameObject.SetActive(!reduced && cycle < 1f);
        lettering.sheen.anchoredPosition = new Vector2(Mathf.Round(cycle * (glassWidth + 24) - 12), 0);
        // Scanline rows stay one texel tall at any panel size.
        var lines = lettering.glass.Find("Scanlines").GetComponent<RawImage>();
        lines.uvRect = new Rect(0, 0, 1, lettering.glass.rect.height / (2 * PixelArt.Texel));
    }

    private bool Lit(PixelButton button) => button.interactable &&
        (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, Input.mousePosition, null)
         || keyboardNav && events.currentSelectedGameObject == button.gameObject);

    // Advances stars, exhaust, readout and engine sound from the rocket's current pose.
    private void TickFx()
    {
        float r = rocketAngle * Mathf.Deg2Rad, texel = rocketFrame / 120f;
        // Nozzle: 3 texels right of and 38 below the frame centre in the nose-up art, turned with the rocket.
        Vector2 offset = new Vector2(3 * Mathf.Cos(r) + 38 * Mathf.Sin(r), -(3 * Mathf.Sin(r) - 38 * Mathf.Cos(r)));
        Vector2 back = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        fx.Tick(size, Time.unscaledDeltaTime, reduced, !narrow, rocketAt + offset * texel, back, throttle, warp,
            rocketFrame / rocketSize, fade.color.a);
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
        TickFx();
        if (launching) return;
        // Keyboard focus lights the lettering until the mouse moves again.
        if (Input.GetAxisRaw("Horizontal") != 0 || Input.GetAxisRaw("Vertical") != 0 || Input.GetButtonDown("Submit"))
            keyboardNav = true;
        else if (Input.mousePosition != lastMouse)
            keyboardNav = false;
        lastMouse = Input.mousePosition;
        bool hot = Lit(start);
        Glow(startText, hot);
        Glow(optionsText, Lit(options));
        // The engine throttles up while Start is lit.
        throttle = Mathf.MoveTowards(throttle, hot ? 1f : .6f, Time.unscaledDeltaTime * 2.5f);
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
        else if (!launching && selected == null && keyboardNav)
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
        float left = narrow ? 50 : 88, width = narrow ? 400 : 452, top = narrow ? 32 : 104;
        float logoWidth = width / (LogoRight - LogoLeft);
        float logoHeight = logoWidth * logo.height / logo.width;
        Place(title.rectTransform, size, left - LogoLeft * logoWidth, top - LogoTop * logoHeight, logoWidth, logoHeight);
        logoSize = new Vector2(logoWidth, logoHeight);

        float logoEnd = top + (LogoBottom - LogoTop) * logoHeight;
        // The buttons sit on the rocket's hull. Hull in the 120 px art frame: columns 40-86, rows 32-96,
        // so once turned nose-right its centre is 4 texels left of and 3 below the frame centre.
        float texel = narrow ? 4.2f : 5.4f;
        rocketSize = 120 * texel;
        startSize = new Vector2(264, 68);
        optionsSize = new Vector2(236, 52);
        hullCenter = narrow ? new Vector2(250, size.y - 40 - 31 * texel) : new Vector2(360, logoEnd + 175);
        rocketPos = hullCenter + new Vector2(6, -3) * texel; // cylinder below the nose dome: rows 40-92
        float rocketTop = hullCenter.y - 32 * texel, rocketBottom = hullCenter.y + 31 * texel;
        Place(launchError.Rect, size, left, Mathf.Min(rocketBottom + 4, size.y - 44), width, 42);

        float panelWidth = narrow ? 450 : 560;
        LayoutPanel(optionsPanel, optionsHeading, panelWidth,
            (motion.transform, RowHeight), (volumeRow, 58f), (mix.Root, AudioMixRow.Height), (fullscreen.transform, RowHeight),
            (resolution.transform, RowHeight), (controlsSection, 128f), (optionsBack.transform, RowHeight));

        // Mars fills the right half; the orbit's ends clear the silhouette by the relay's half-diagonal.
        float radius = narrow ? 150 : 264;
        center = narrow ? new Vector2(250, (logoEnd + rocketTop) / 2) : new Vector2(950, 360);
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
        {
            // Rocket and the buttons on its hull bob together.
            var bob = new Vector2(0, reduced ? 0 : Mathf.Round(5f * Mathf.Sin(motionTime * 1.6f)));
            PlaceRocket(rocketPos + bob, rocketSize, MenuHeading);
            float x = Mathf.Round(hullCenter.x - startSize.x / 2);
            float y = Mathf.Round(hullCenter.y + bob.y - (startSize.y + ButtonGap + optionsSize.y) / 2);
            Place((RectTransform)start.transform, size, x, y, startSize.x, startSize.y);
            Place((RectTransform)options.transform, size, x, y + startSize.y + ButtonGap, optionsSize.x, optionsSize.y);
        }

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
        GameAudio.Launch(reduced); // launch whoosh over the ignition; arrival music scores the entry cutscene (Reduce Motion skips the whoosh)
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
        var menuArt = title.gameObject.AddComponent<CanvasGroup>();
        var navigation = new[] { start.group, options.group };
        launchArrow.gameObject.SetActive(false);
        Vector2 rocketStart = rocketAt;

        // Ignition: the engine roars to full burn and the screen shakes while the buttons drop away.
        const float ignitionSeconds = .55f;
        fx.Ignite();
        for (float t = 0f; t < ignitionSeconds; t += Time.unscaledDeltaTime)
        {
            float k = t / ignitionSeconds;
            throttle = Mathf.Lerp(1f, 2.2f, Mathf.SmoothStep(0f, 1f, k));
            Shake(5f * k);
            foreach (var button in navigation)
                button.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / .2f);
            // A slight recoil before the burn carries it forward.
            PlaceRocket(rocketStart + new Vector2(-10f * Mathf.Sin(k * Mathf.PI), 0), rocketSize, MenuHeading);
            yield return null;
        }

        // Departure: dead straight along its axis, accelerating off the right edge towards Mars as the stars
        // stretch into warp; then the entry cutscene takes over.
        const float departSeconds = 1.05f;
        Vector2 offscreen = new Vector2(size.x + rocketSize * .6f, rocketStart.y);
        for (float t = 0f; t < departSeconds; t += Time.unscaledDeltaTime)
        {
            float k = t / departSeconds;
            foreach (var button in navigation) button.alpha = 0; // the buttons reset their group alpha otherwise
            throttle = 2.2f;
            warp = Mathf.SmoothStep(0f, 1f, k / .6f);
            PlaceRocket(Vector2.Lerp(rocketStart, offscreen, k * k * k), rocketSize, MenuHeading);
            Shake(5f * (1f - k));
            float opacity = 1f - Mathf.SmoothStep(0f, 1f, k / .6f);
            menuArt.alpha = opacity;
            satellite.color = new Color(1f, 1f, 1f, opacity);
            blackHole.color = new Color(1f, 1f, 1f, .6f * opacity);
            fade.color = new Color(Background.r / 255f, Background.g / 255f, Background.b / 255f,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.75f, 1f, k)));
            yield return null;
        }

        Shake(0);
        warp = 0;
        fade.color = Background; // also silences the menu engine
        EntryCinematic.Play(destination);
    }

    // Smooth noise shake: fluid rumble instead of a fresh random offset every frame.
    private void Shake(float amount)
    {
        float t = Time.unscaledTime * 14f;
        layout.anchoredPosition = amount <= 0 ? Vector2.zero
            : new Vector2(Mathf.PerlinNoise(t, .3f) - .5f, Mathf.PerlinNoise(.7f, t) - .5f) * 2f * amount;
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
