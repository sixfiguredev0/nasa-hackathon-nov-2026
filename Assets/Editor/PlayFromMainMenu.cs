using UnityEditor;
using UnityEditor.SceneManagement;

// Pressing Play in the editor starts from the main menu, like the built game,
// whatever scene is open. Toggle with Red Frontier/Play From Main Menu.
[InitializeOnLoad]
internal static class PlayFromMainMenu
{
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const string MenuPath = "Red Frontier/Play From Main Menu";
    private const string Pref = "RedFrontier.PlayFromMainMenu";

    static PlayFromMainMenu() => EditorApplication.delayCall += Apply;

    private static bool Enabled => EditorPrefs.GetBool(Pref, true);

    private static void Apply() =>
        EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) : null;

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        EditorPrefs.SetBool(Pref, !Enabled);
        Apply();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
