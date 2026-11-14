using System.IO;
using System.Linq;
using RedFrontier.Mars;
using RedFrontier.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RedFrontier.Integration
{
    /// <summary>Batch-mode helpers for the integration scene. Never touches Assets/Scenes/MarsOutpost.unity except to copy it.</summary>
    public static class IntegrationTools
    {
        public const string Scene = "Assets/RedFrontier/Scenes/Integration_MapV1.unity";
        const string Source = "Assets/Scenes/MarsOutpost.unity";

        /// <summary>Creates Integration_MapV1 once (copy => new GUID) and wires the simulation into it.</summary>
        public static void CreateScene()
        {
            if (!File.Exists(Scene) && !AssetDatabase.CopyAsset(Source, Scene)) throw new System.Exception("copy failed");
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var gen = Object.FindObjectOfType<MarsMapGenerator>();
            gen.seed = 1337;
            gen.useRandomSeed = false;
            EditorUtility.SetDirty(gen);
            var map = Object.FindObjectOfType<MarsEnvironmentController>();
            if (Object.FindObjectOfType<SimulationHost>(true) == null)
            {
                var go = new GameObject("Simulation");
                go.SetActive(false);
                go.AddComponent<SimulationHost>();
                var hud = go.AddComponent<DebugHud>();
                hud.PanelMode = true;
                var bridge = go.AddComponent<MapPanelBridge>();
                bridge.panel = hud;
                bridge.map = map;
                go.SetActive(true);
            }
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true), new EditorBuildSettingsScene("Assets/RedFrontier/Scenes/Dev1_Debug.unity", true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Integration] scene guid " + AssetDatabase.AssetPathToGUID(Scene) + " source guid " + AssetDatabase.AssetPathToGUID(Source)
                + " cameras " + Object.FindObjectsOfType<Camera>(true).Length + " listeners " + Object.FindObjectsOfType<AudioListener>(true).Length
                + " hosts " + Object.FindObjectsOfType<SimulationHost>(true).Length);
        }

        /// <summary>Clean Development WebGL build of the integration scene, compression disabled. Output: -buildPath argument.</summary>
        public static void BuildWebGL()
        {
            var args = System.Environment.GetCommandLineArgs();
            string path = args[System.Array.IndexOf(args, "-buildPath") + 1];
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = BuildTarget.WebGL,
                options = BuildOptions.Development | BuildOptions.CleanBuildCache
            });
            Debug.Log("[Integration] build " + r.summary.result + " size " + r.summary.totalSize + " time " + r.summary.totalTime);
            if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
