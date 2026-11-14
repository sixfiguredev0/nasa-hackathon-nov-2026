using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace RedFrontier.Audio.Editor
{
    /// <summary>
    /// PlayFromMainMenu points Play mode at the title scene, which stops the Test Runner's own init scene
    /// from loading (command-line PlayMode runs hang). Only when Unity was started with -runTests,
    /// clear the start scene as Play mode begins. Interactive play and the team's preference are untouched.
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRunStartScene
    {
        static TestRunStartScene()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-runTests") < 0) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) EditorSceneManager.playModeStartScene = null;
            };
        }
    }
}
