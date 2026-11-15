using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FrontierUi;

internal sealed class MissionArrival : MonoBehaviour
{
    internal static void Load(string destination) => Load(destination, null, new Rect(0, 0, 1, 1));

    internal static void Load(string destination, Texture rocket, Rect rocketFrame)
    {
        var root = new GameObject("Mission arrival", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        DontDestroyOnLoad(root);
        // No card: the entry cinematic ends on a hard cut to black, so the load stays black and only a thin line
        // along the bottom edge shows progress.
        var cover = Fill("Scene cover", root.transform, Color.black);
        cover.raycastTarget = true;
        var track = Fill("Progress track", root.transform, new Color(1f, 1f, 1f, .08f));
        track.rectTransform.anchorMin = Vector2.zero;
        track.rectTransform.anchorMax = new Vector2(1f, 0f);
        track.rectTransform.offsetMin = Vector2.zero;
        track.rectTransform.offsetMax = new Vector2(0f, 3f);
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
