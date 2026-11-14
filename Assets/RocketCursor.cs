using UnityEngine;

internal sealed class RocketCursor : MonoBehaviour
{
    private static readonly Vector2 Hotspot = new Vector2(2, 2);

    private Texture2D idleTexture;
    private Texture2D pressedTexture;
    private bool pressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        var idle = Resources.Load<Texture2D>("RocketCursor");
        var click = Resources.Load<Texture2D>("RocketCursorPressed");
        if (idle == null || click == null)
            return;

        var instance = new GameObject("Rocket Cursor").AddComponent<RocketCursor>();
        instance.idleTexture = idle;
        instance.pressedTexture = click;
        DontDestroyOnLoad(instance.gameObject);
        instance.UpdateCursor(false);
    }

    private void Update()
    {
        bool held = Application.isFocused && Input.GetMouseButton(0);
        if (held != pressed)
            UpdateCursor(held);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (idleTexture != null)
            UpdateCursor(focused && Input.GetMouseButton(0));
    }

    private void UpdateCursor(bool held)
    {
        pressed = held;
        Cursor.SetCursor(held ? pressedTexture : idleTexture, Hotspot, CursorMode.Auto);
    }
}
