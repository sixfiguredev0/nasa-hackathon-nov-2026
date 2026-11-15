using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FrontierUi;

internal sealed class MissionArrival : MonoBehaviour
{
    // Used after the entry cinematic: show the menu's flight rocket (last frame) on the loading card.
    internal static void Load(string destination)
    {
        var art = Resources.Load<Texture2D>("Menu/RocketFlight");
        if (art == null) art = Resources.Load<Texture2D>("Buildings/Rocket");
        int frames = art != null ? Mathf.Max(1, art.width / Mathf.Max(1, art.height)) : 1;
        Load(destination, art, new Rect((frames - 1f) / frames, 0, 1f / frames, 1));
    }

    internal static void Load(string destination, Texture rocket, Rect rocketFrame)
    {
        var root = new GameObject("Mission arrival", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        DontDestroyOnLoad(root);
        var cover = Fill("Scene cover", root.transform, Background);
        cover.raycastTarget = true;
        var layout = FitLayout(root.transform);
        var reference = new Vector2(640, 480);
        Resize((RectTransform)root.transform, reference, layout);

        var cardSize = new Vector2(320, 112);
        var card = Box("Arrival status", layout);
        Place(card, reference, 160, 184, cardSize.x, cardSize.y);
        var destinationLabel = PixelLabel.Create("Destination", card, "Mars outpost", 3, Cream, 0);
        Place(destinationLabel.Rect, cardSize, 0, 12, 244, 36);
        var status = PixelLabel.Create("Load status", card, "Loading mission", 2, CreamDark, 0);
        Place(status.Rect, cardSize, 0, 52, 244, 24);
        var ship = Art("Mission rocket", card, rocket);
        ship.uvRect = rocketFrame;
        if (rocket == null) ship.enabled = false; // no art: skip it rather than draw a white box
        Place(ship.rectTransform, cardSize, 252, 8, 68, 68);
        var track = Fill("Progress track", card, PanelLight);
        Place(track.rectTransform, cardSize, 0, 96, 320, 4);
        var progress = Fill("Load progress", track.transform, CoralLight);
        progress.rectTransform.anchorMax = new Vector2(0, 1);

        var group = root.GetComponent<CanvasGroup>();
        bool reduced = ReduceMotion;
        root.AddComponent<MissionArrival>().StartCoroutine(Reveal());

        IEnumerator Reveal()
        {
            yield return null; // Give the loading card a frame before scene initialization starts.
            AsyncOperation load = null;
            try
            {
                load = SceneManager.LoadSceneAsync(destination);
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
            }
            if (load == null)
            {
                // The persistent cover would otherwise block all input forever.
                Debug.LogError($"MissionArrival: could not start loading scene '{destination}'; removing the loading cover.");
                Destroy(root);
                yield break;
            }
            while (!load.isDone)
            {
                progress.rectTransform.anchorMax = new Vector2(load.progress, 1);
                yield return null;
            }
            progress.rectTransform.anchorMax = Vector2.one;

            // Wait for the new scene's first rendered frame before revealing it.
            yield return new WaitForEndOfFrame();
            if (!reduced)
            {
                const float seconds = .22f;
                for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
                {
                    group.alpha = 1f - Mathf.SmoothStep(0f, 1f, elapsed / seconds);
                    yield return null;
                }
            }
            Destroy(root);
        }
    }
}
