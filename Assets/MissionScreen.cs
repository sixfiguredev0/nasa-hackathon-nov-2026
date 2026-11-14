using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FrontierUi;

// Placeholder destination for Start Mission until the mission itself is built.
public sealed class MissionScreen : MonoBehaviour
{
    private const string MenuScene = "MainMenu";
    private const float Pad = 28, HeadingHeight = 40, Gap = 28, ButtonHeight = 64;

    [SerializeField] private Texture2D sky;

    private RectTransform canvas, layout, panel;
    private RawImage stars;
    private PixelLabel heading;
    private PixelButton back;
    private EventSystem events;
    private bool narrow, leaving;

    private void Awake()
    {
        canvas = CreateCanvas(out events);
        if (sky != null)
            stars = Art("Star field", canvas, sky);
        Fill("Shade", canvas, new Color32(Background.r, Background.g, Background.b, 150));
        layout = FitLayout(canvas);
        panel = PanelFrame("Mission panel", layout);
        heading = PixelLabel.Create("Heading", panel, "Mission coming soon", 4, Cream, .5f, true);
        back = FrontierUi.Button("Back to menu", panel, true);
        back.onClick.AddListener(Leave);
        events.firstSelectedGameObject = back.gameObject;
        back.Select();
        ApplyLayout(IsNarrow);
    }

    private void Update()
    {
        if (narrow != IsNarrow)
            ApplyLayout(IsNarrow);
        if (stars != null)
            CoverSky(stars, sky);
        if (leaving)
            return;

        if (Input.GetButtonDown("Cancel"))
            Leave();
        else if (events.currentSelectedGameObject != back.gameObject)
            back.Select();
    }

    private void ApplyLayout(bool isNarrow)
    {
        narrow = isNarrow;
        var size = narrow ? new Vector2(500, 840) : new Vector2(1280, 720);
        Resize(canvas, size, layout);
        float width = narrow ? 450 : 640, height = 2 * Pad + HeadingHeight + Gap + ButtonHeight;
        var panelSize = new Vector2(width, height);
        Place(panel, size, Mathf.Round((size.x - width) / 2), Mathf.Round((size.y - height) / 2), width, height);
        Place(heading.Rect, panelSize, Pad, Pad, width - 2 * Pad, HeadingHeight);
        float buttonWidth = Mathf.Min(width - 2 * Pad, 400);
        Place((RectTransform)back.transform, panelSize, (width - buttonWidth) / 2, Pad + HeadingHeight + Gap,
            buttonWidth, ButtonHeight);
        if (stars != null)
            CoverSky(stars, sky);
    }

    // Exactly one scene load, however many times Back is pressed.
    private void Leave()
    {
        if (leaving)
            return;
        leaving = true;
        back.HoldPressed = true;
        back.interactable = false;
        SceneManager.LoadSceneAsync(MenuScene);
    }
}
