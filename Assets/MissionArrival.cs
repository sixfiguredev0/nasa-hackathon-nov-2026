using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class MissionArrival : MonoBehaviour
{
    internal static void Load(string destination)
    {
        var root = new GameObject("Mission arrival", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        DontDestroyOnLoad(root);
        var cover = FrontierUi.Fill("Scene cover", root.transform, FrontierUi.Background);
        root.AddComponent<MissionArrival>().StartCoroutine(Reveal());

        IEnumerator Reveal()
        {
            yield return SceneManager.LoadSceneAsync(destination);
            // Wait for the new scene's first rendered frame before revealing it.
            yield return new WaitForEndOfFrame();
            const float seconds = .22f;
            Color color = cover.color;
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                color.a = 1f - Mathf.SmoothStep(0f, 1f, elapsed / seconds);
                cover.color = color;
                yield return null;
            }
            Destroy(root);
        }
    }
}
