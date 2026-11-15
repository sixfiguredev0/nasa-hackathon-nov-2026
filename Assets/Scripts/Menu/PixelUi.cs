using System.Collections.Generic;
using RedFrontier.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A 5 x 7 bitmap font drawn into point-filtered textures, so labels need no font asset.
internal static class PixelFont
{
    public const int GlyphHeight = 7;
    private const int LineGap = 4;

    // Each entry: the character, then seven rows from top to bottom. Row length is the glyph width.
    private static readonly string[] Source =
    {
        "A|.###.|#...#|#...#|#####|#...#|#...#|#...#",
        "B|####.|#...#|#...#|####.|#...#|#...#|####.",
        "C|.###.|#...#|#....|#....|#....|#...#|.###.",
        "D|####.|#...#|#...#|#...#|#...#|#...#|####.",
        "E|#####|#....|#....|####.|#....|#....|#####",
        "F|#####|#....|#....|####.|#....|#....|#....",
        "G|.###.|#...#|#....|#.###|#...#|#...#|.####",
        "H|#...#|#...#|#...#|#####|#...#|#...#|#...#",
        "I|###|.#.|.#.|.#.|.#.|.#.|###",
        "J|..###|...#.|...#.|...#.|#..#.|#..#.|.##..",
        "K|#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#",
        "L|#....|#....|#....|#....|#....|#....|#####",
        "M|#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#",
        "N|#...#|#...#|##..#|#.#.#|#..##|#...#|#...#",
        "O|.###.|#...#|#...#|#...#|#...#|#...#|.###.",
        "P|####.|#...#|#...#|####.|#....|#....|#....",
        "Q|.###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#",
        "R|####.|#...#|#...#|####.|#.#..|#..#.|#...#",
        "S|.####|#....|#....|.###.|....#|....#|####.",
        "T|#####|..#..|..#..|..#..|..#..|..#..|..#..",
        "U|#...#|#...#|#...#|#...#|#...#|#...#|.###.",
        "V|#...#|#...#|#...#|#...#|#...#|.#.#.|..#..",
        "W|#...#|#...#|#...#|#.#.#|#.#.#|#.#.#|.#.#.",
        "X|#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#",
        "Y|#...#|#...#|.#.#.|..#..|..#..|..#..|..#..",
        "Z|#####|....#|...#.|..#..|.#...|#....|#####",
        "0|.###.|#...#|#..##|#.#.#|##..#|#...#|.###.",
        "1|.#.|##.|.#.|.#.|.#.|.#.|###",
        "2|.###.|#...#|....#|...#.|..#..|.#...|#####",
        "3|#####|...#.|..#..|...#.|....#|#...#|.###.",
        "4|...#.|..##.|.#.#.|#..#.|#####|...#.|...#.",
        "5|#####|#....|####.|....#|....#|#...#|.###.",
        "6|..##.|.#...|#....|####.|#...#|#...#|.###.",
        "7|#####|....#|...#.|..#..|.#...|.#...|.#...",
        "8|.###.|#...#|#...#|.###.|#...#|#...#|.###.",
        "9|.###.|#...#|#...#|.####|....#|...#.|.##..",
        " |...|...|...|...|...|...|...",
        ".|..|..|..|..|..|##|##",
        ",|..|..|..|..|..|.#|#.",
        ":|..|##|##|..|##|##|..",
        "/|....#|....#|...#.|..#..|.#...|#....|#....",
        "(|.#|#.|#.|#.|#.|#.|.#",
        ")|#.|.#|.#|.#|.#|.#|#.",
        "[|##|#.|#.|#.|#.|#.|##",
        "]|##|.#|.#|.#|.#|.#|##",
        "-|...|...|...|###|...|...|...",
        "+|...|...|.#.|###|.#.|...|...",
        "'|#|#|.|.|.|.|.",
        "!|#|#|#|#|#|.|#",
        "?|.###.|#...#|....#|...#.|..#..|.....|..#..",
        "%|##..#|##..#|...#.|..#..|.#...|#..##|#..##",
        ">|...|#..|.#.|..#|.#.|#..|...",
        "<|...|..#|.#.|#..|.#.|..#|...",
    };

    private static Dictionary<char, string[]> glyphs;

    private static string[] Glyph(char c)
    {
        if (glyphs == null)
        {
            glyphs = new Dictionary<char, string[]>();
            foreach (string entry in Source)
            {
                var rows = entry.Substring(2).Split('|');
                glyphs[entry[0]] = rows;
            }
        }
        return glyphs.TryGetValue(c, out var rows2) ? rows2 : glyphs['?'];
    }

    private static int LineWidth(string line)
    {
        int width = 0;
        for (int i = 0; i < line.Length; i++)
            width += Glyph(line[i])[0].Length + (i > 0 ? 1 : 0);
        return width;
    }

    // White glyphs on a clear background, with an optional dark shadow one texel down and right.
    // One clear texel of padding on each side keeps point sampling from clipping the edges.
    public static Texture2D Render(string text, float align, bool shadow)
    {
        var lines = (text ?? "").ToUpperInvariant().Split('\n');
        var widths = new int[lines.Length];
        int textWidth = 1;
        for (int i = 0; i < lines.Length; i++)
            textWidth = Mathf.Max(textWidth, widths[i] = LineWidth(lines[i]));
        int textHeight = lines.Length * GlyphHeight + (lines.Length - 1) * LineGap;
        int extra = shadow ? 1 : 0;
        int w = textWidth + 2 + extra, h = textHeight + 2 + extra;

        var lit = new bool[w * h];
        for (int line = 0; line < lines.Length; line++)
        {
            int x = 1 + Mathf.RoundToInt((textWidth - widths[line]) * align);
            int top = 1 + line * (GlyphHeight + LineGap);
            foreach (char c in lines[line])
            {
                var rows = Glyph(c);
                for (int r = 0; r < GlyphHeight; r++)
                    for (int col = 0; col < rows[r].Length; col++)
                        if (rows[r][col] == '#')
                            lit[(h - 1 - (top + r)) * w + x + col] = true;
                x += rows[0].Length + 1;
            }
        }

        var pixels = new Color32[w * h];
        var white = new Color32(255, 255, 255, 255);
        var dark = new Color32(0, 0, 0, 150);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (lit[i])
                    pixels[i] = white;
                else if (shadow && x > 0 && y < h - 1 && lit[(y + 1) * w + x - 1])
                    pixels[i] = dark;
            }

        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Pixel text" };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}

// A label drawn with PixelFont at the largest whole-number scale (up to maxScale) that fits its box.
internal sealed class PixelLabel : MonoBehaviour
{
    private RawImage glyphs;
    private string text;
    private int maxScale;
    private float align;
    private bool shadow;
    private Vector2 fittedTo;

    public static PixelLabel Create(string name, Transform parent, string text, int maxScale, Color color,
        float align = .5f, bool shadow = false)
    {
        var box = FrontierUi.Box(name, parent);
        var label = box.gameObject.AddComponent<PixelLabel>();
        label.glyphs = FrontierUi.Art("Glyphs", box, null);
        label.glyphs.color = color;
        label.maxScale = maxScale;
        label.align = align;
        label.shadow = shadow;
        label.Text = text;
        return label;
    }

    public RectTransform Rect => (RectTransform)transform;

    public Color Color
    {
        get => glyphs.color;
        set => glyphs.color = value;
    }

    public string Text
    {
        get => text;
        set
        {
            if (value == text && glyphs.texture != null)
                return;
            text = value;
            if (glyphs.texture != null)
                Destroy(glyphs.texture);
            glyphs.texture = PixelFont.Render(text, align, shadow);
            Fit();
        }
    }

    private void LateUpdate()
    {
        if (Rect.rect.size != fittedTo)
            Fit();
    }

    private void Fit()
    {
        var area = Rect.rect.size;
        var texture = glyphs.texture;
        int scale = maxScale;
        if (area.x > 0 && area.y > 0)
            scale = Mathf.Clamp(Mathf.Min(Mathf.FloorToInt(area.x / texture.width),
                Mathf.FloorToInt(area.y / texture.height)), 1, maxScale);
        var rect = glyphs.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(align, .5f);
        rect.sizeDelta = new Vector2(texture.width, texture.height) * scale;
        rect.anchoredPosition = Vector2.zero;
        fittedTo = area;
    }

    private void OnDestroy()
    {
        if (glyphs != null && glyphs.texture != null)
            Destroy(glyphs.texture);
    }
}

// Notched 8 x 8 pixel frames, nine-sliced so the corners stay square at any size.
internal static class PixelArt
{
    public const float Texel = 3;
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Frame(Color32 fill, Color32 border, Color32 light, Color32 dark) =>
        Make($"frame {fill} {border} {light} {dark}", (x, y) =>
        {
            bool edge = x == 0 || x == 7 || y == 0 || y == 7;
            if (edge && (x == 0 || x == 7) && (y == 0 || y == 7))
                return new Color32(0, 0, 0, 0);
            return edge ? border : y == 6 ? light : y == 1 ? dark : fill;
        });

    public static Sprite Solid(Color32 color) => Frame(color, color, color, color);

    public static Sprite Ring(Color32 color) =>
        Make($"ring {color}", (x, y) =>
        {
            bool edge = x == 0 || x == 7 || y == 0 || y == 7;
            bool corner = (x == 0 || x == 7) && (y == 0 || y == 7);
            return edge && !corner ? color : new Color32(0, 0, 0, 0);
        });

    private static Sprite Make(string key, System.Func<int, int, Color32> pixel)
    {
        if (cache.TryGetValue(key, out var sprite) && sprite != null)
            return sprite;
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = key };
        var pixels = new Color32[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[y * 8 + x] = pixel(x, y);
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 100, 0,
            SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
        cache[key] = sprite;
        return sprite;
    }

    public static Image Sliced(string name, Transform parent, Sprite sprite)
    {
        var image = FrontierUi.Fill(name, parent, Color.white);
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1 / Texel;
        return image;
    }
}

// Fades the hover/focus ring in and out over a short time instead of switching it instantly.
internal sealed class FocusRing : MonoBehaviour
{
    public const float Seconds = .12f;
    public Graphic ring, shine;
    public float shineAlpha;
    private float level, target;

    public void Show(bool visible, bool instant)
    {
        target = visible ? 1 : 0;
        if (instant || !Application.isPlaying)
            Apply(target);
    }

    private void Update()
    {
        if (level != target)
            Apply(Mathf.MoveTowards(level, target, Time.unscaledDeltaTime / Seconds));
    }

    private void Apply(float value)
    {
        level = value;
        if (ring != null)
            ring.color = new Color(1, 1, 1, level);
        if (shine != null)
            shine.color = new Color(1, 1, 1, level * shineAlpha);
    }
}

// Menu button: a static lip, a face that drops onto it while pressed, and a fading focus ring.
internal sealed class PixelButton : Button
{
    public const float Lip = 4, Drop = 3;
    public RectTransform face;
    public CanvasGroup group;
    public PixelLabel label;
    public FocusRing focus;
    private bool holdPressed;

    // Keeps the pressed look (at full opacity) while the button is locked, e.g. during a scene load.
    public bool HoldPressed
    {
        get => holdPressed;
        set
        {
            holdPressed = value;
            Refresh();
        }
    }

    public void Refresh() => DoStateTransition(currentSelectionState, true);

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (face == null || focus == null)
            return;

        bool down = holdPressed || state == SelectionState.Pressed;
        face.offsetMin = new Vector2(0, down ? Lip - Drop : Lip);
        face.offsetMax = new Vector2(0, down ? -Drop : 0);
        group.alpha = state == SelectionState.Disabled && !holdPressed ? .4f : 1;
        focus.Show(!holdPressed && (state == SelectionState.Highlighted || state == SelectionState.Selected ||
                                    state == SelectionState.Pressed), instant);
    }

    // Hovering moves keyboard focus too, so only one control ever shows the ring.
    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (IsInteractable() && EventSystem.current != null)
            Select();
    }

    // Generic press sound; GameAudio skips it when the button's action already played its own cue this frame.
    // Hover and focus moves stay silent on purpose.
    public override void OnPointerClick(PointerEventData eventData)
    {
        bool press = eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable();
        base.OnPointerClick(eventData);
        if (press) GameAudio.UiPress();
    }

    public override void OnSubmit(BaseEventData eventData)
    {
        bool press = IsActive() && IsInteractable();
        base.OnSubmit(eventData);
        if (press) GameAudio.UiPress();
    }
}

// Volume slider with the same focus ring and disabled look as the buttons.
internal sealed class PixelSlider : Slider
{
    public CanvasGroup group;
    public FocusRing focus;

    public void Refresh() => DoStateTransition(currentSelectionState, true);

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (group == null || focus == null)
            return;
        group.alpha = state == SelectionState.Disabled ? .4f : 1;
        focus.Show(state == SelectionState.Highlighted || state == SelectionState.Selected ||
                   state == SelectionState.Pressed, instant);
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (IsInteractable() && EventSystem.current != null)
            Select();
    }

    // A soft tick on each user step previews the new level (cooldown in the catalogue stops drag spam).
    protected override void Set(float input, bool sendCallback = true)
    {
        float before = value;
        base.Set(input, sendCallback);
        if (sendCallback && value != before && Application.isPlaying) GameAudio.Play(Sfx.UiSelect);
    }
}
