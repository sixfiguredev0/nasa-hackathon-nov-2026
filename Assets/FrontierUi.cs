using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class FrontierUi
{
    // Palette taken from the logo and Mars art: charcoal, coral and cream, plus their light and dark steps.
    public static readonly Color32 Background = new Color32(32, 29, 39, 255);
    public static readonly Color32 Panel = new Color32(51, 42, 53, 255);
    public static readonly Color32 PanelLight = new Color32(76, 62, 76, 255);
    public static readonly Color32 PanelDark = new Color32(40, 33, 44, 255);
    public static readonly Color32 Cream = new Color32(248, 229, 200, 255);
    public static readonly Color32 CreamDark = new Color32(214, 186, 156, 255);
    public static readonly Color32 Coral = new Color32(228, 108, 107, 255);
    public static readonly Color32 CoralLight = new Color32(246, 158, 140, 255);
    public static readonly Color32 CoralDark = new Color32(186, 80, 86, 255);
    public static readonly Color32 Maroon = new Color32(120, 46, 58, 255);
    public static readonly Color32 Mauve = new Color32(122, 90, 96, 255);
    public static readonly Color32 Shadow = new Color32(20, 17, 25, 255);

    public const string MotionPreference = "ReduceMotion";
    public const string VolumePreference = "MasterVolume";

    public static bool ReduceMotion
    {
        get => PlayerPrefs.GetInt(MotionPreference) == 1;
        set
        {
            PlayerPrefs.SetInt(MotionPreference, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static float MasterVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreference, 1));
        set
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumePreference, Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedVolume() => AudioListener.volume = MasterVolume;

    public static bool IsNarrow => Screen.width < Screen.height * 1.25f;

    public static RectTransform CreateCanvas(out EventSystem events)
    {
        var root = new GameObject("Frontier Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        events = new GameObject("Event System", typeof(EventSystem),
            typeof(StandaloneInputModule)).GetComponent<EventSystem>();
        Fill("Backdrop", root.transform, Background);
        return root.GetComponent<RectTransform>();
    }

    public static RectTransform FitLayout(Transform canvas)
    {
        var layout = Box("Layout", canvas);
        layout.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        return layout;
    }

    public static void Resize(RectTransform canvas, Vector2 size, params RectTransform[] layouts)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = size;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        foreach (var layout in layouts)
            layout.GetComponent<AspectRatioFitter>().aspectRatio = size.x / size.y;
    }

    // Centered, aspect-preserving crop that covers the whole viewport.
    public static void CoverSky(RawImage stars, Texture sky)
    {
        float view = (float)Screen.width / Screen.height, art = (float)sky.width / sky.height;
        Vector2 crop = view > art ? new Vector2(1, art / view) : new Vector2(view / art, 1);
        stars.uvRect = new Rect((Vector2.one - crop) / 2, crop);
    }

    // Screen coordinates in design pixels, measured from the top left.
    public static void Place(RectTransform rect, Vector2 layoutSize, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(x / layoutSize.x, 1 - (y + height) / layoutSize.y);
        rect.anchorMax = new Vector2((x + width) / layoutSize.x, 1 - y / layoutSize.y);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static RectTransform Box(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    public static Image Fill(string name, Transform parent, Color color)
    {
        var image = Box(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static RawImage Art(string name, Transform parent, Texture texture)
    {
        var image = Box(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    // Charcoal panel with a coral pixel border. It blocks clicks to whatever is behind it.
    public static RectTransform PanelFrame(string name, Transform parent)
    {
        var panel = PixelArt.Sliced(name, parent, PixelArt.Frame(Panel, Coral, PanelLight, PanelDark));
        panel.raycastTarget = true;
        return panel.rectTransform;
    }

    // Every button shares one shape. Primary is a coral face with charcoal text; secondary is charcoal
    // with a coral border and cream text. The hit area is the whole visible button, lip included,
    // so the rocket cursor's tip enters the button exactly at its drawn edge.
    public static PixelButton Button(string caption, Transform parent, bool primary, bool titleAction = false)
    {
        var hit = Fill(caption, parent, Color.clear);
        hit.raycastTarget = true;
        var button = hit.gameObject.AddComponent<PixelButton>();
        button.transition = Selectable.Transition.None;
        var colors = button.colors;
        colors.fadeDuration = .1f; // How long a keyboard press shows the pressed face.
        button.colors = colors;
        button.group = hit.gameObject.AddComponent<CanvasGroup>();

        var ring = PixelArt.Sliced("Focus ring", hit.transform, PixelArt.Ring(Cream));
        float ringGap = (titleAction ? 1 : 2) * PixelArt.Texel;
        ring.rectTransform.offsetMin = new Vector2(-ringGap, -ringGap);
        ring.rectTransform.offsetMax = new Vector2(ringGap, ringGap);
        PixelArt.Sliced("Lip", hit.transform, PixelArt.Solid(primary || !titleAction ? Maroon : Shadow));
        Color32 secondaryBorder = titleAction ? Mauve : Coral;
        var face = primary
            ? PixelArt.Sliced("Face", hit.transform, PixelArt.Frame(Coral, Shadow, CoralLight, CoralDark))
            : PixelArt.Sliced("Face", hit.transform, PixelArt.Frame(Panel, secondaryBorder, PanelLight, PanelDark));
        var shine = PixelArt.Sliced("Shine", face.transform, PixelArt.Solid(Color.white));

        bool leftAligned = titleAction && primary;
        var label = PixelLabel.Create("Label", face.transform, caption, primary ? 4 : 3,
            primary ? (Color)Background : Cream, leftAligned ? 0 : .5f, !primary);
        label.Rect.offsetMin = new Vector2(leftAligned ? 24 : 4 * PixelArt.Texel, 0);
        label.Rect.offsetMax = new Vector2(leftAligned ? -64 : -4 * PixelArt.Texel, 0);

        button.focus = hit.gameObject.AddComponent<FocusRing>();
        button.focus.ring = ring;
        button.focus.shine = shine;
        button.focus.shineAlpha = primary ? .16f : .08f;
        button.face = face.rectTransform;
        button.label = label;
        button.Refresh();
        return button;
    }

    public static PixelSlider Slider(string name, Transform parent, int steps)
    {
        var root = Fill(name, parent, Color.clear);
        root.raycastTarget = true;
        var slider = root.gameObject.AddComponent<PixelSlider>();
        slider.transition = Selectable.Transition.None;
        slider.group = root.gameObject.AddComponent<CanvasGroup>();

        var ring = PixelArt.Sliced("Focus ring", root.transform, PixelArt.Ring(Cream));
        ring.rectTransform.offsetMin = new Vector2(-2 * PixelArt.Texel, -2 * PixelArt.Texel);
        ring.rectTransform.offsetMax = new Vector2(2 * PixelArt.Texel, 2 * PixelArt.Texel);
        var track = PixelArt.Sliced("Track", root.transform, PixelArt.Frame(Shadow, Mauve, Shadow, Shadow));
        track.rectTransform.offsetMin = new Vector2(0, 2 * PixelArt.Texel);
        track.rectTransform.offsetMax = new Vector2(0, -2 * PixelArt.Texel);
        var fillArea = Box("Fill area", track.transform);
        fillArea.offsetMin = new Vector2(PixelArt.Texel, PixelArt.Texel);
        fillArea.offsetMax = new Vector2(-PixelArt.Texel, -PixelArt.Texel);
        var fill = Fill("Fill", fillArea, Coral);
        var handleArea = Box("Handle area", root.transform);
        handleArea.offsetMin = new Vector2(3 * PixelArt.Texel, 0);
        handleArea.offsetMax = new Vector2(-3 * PixelArt.Texel, 0);
        var handle = PixelArt.Sliced("Handle", handleArea, PixelArt.Frame(Cream, Shadow, Color.white, CreamDark));
        handle.rectTransform.sizeDelta = new Vector2(6 * PixelArt.Texel, 0);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = 0;
        slider.maxValue = steps;
        slider.wholeNumbers = true;

        slider.focus = root.gameObject.AddComponent<FocusRing>();
        slider.focus.ring = ring;
        slider.Refresh();
        return slider;
    }

    // Arrow keys and WASD move straight up and down the list; left and right stay free for sliders.
    public static void Chain(params Selectable[] items)
    {
        Selectable previous = null;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null || !items[i].gameObject.activeSelf)
                continue;
            var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = previous };
            items[i].navigation = nav;
            if (previous != null)
            {
                var up = previous.navigation;
                up.selectOnDown = items[i];
                previous.navigation = up;
            }
            previous = items[i];
        }
    }
}
