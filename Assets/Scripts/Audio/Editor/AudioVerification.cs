using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RedFrontier.Audio.Editor
{
    /// <summary>Editor checks for the audio delivery, runnable from the menu or in batch mode (-executeMethod).</summary>
    public static class AudioVerification
    {
        [MenuItem("Red Frontier/Audio/Run Self Test")]
        public static void RunSelfTest() => Debug.Log(SelfTestReport(out _));

        /// <summary>Every catalogue variation resolves to an imported clip with the expected import settings.</summary>
        public static string SelfTestReport(out int fails)
        {
            var sb = new StringBuilder("AUDIO_SELFTEST\n");
            int bad = 0, clips = 0;
            foreach (var def in AudioCatalog.All.OrderBy(d => d.Id))
            {
                for (int i = 0; i < def.Variations; i++)
                {
                    var res = def.ResourcePath(i);
                    var guids = AssetDatabase.FindAssets(Path.GetFileName(res) + " t:AudioClip", new[] { "Assets/Audio" });
                    var path = guids.Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == Path.GetFileName(res));
                    if (path == null) { bad++; sb.AppendLine("FAIL missing " + res); continue; }
                    clips++;
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var problem = AudioImportRules.Check(path, importer);
                    if (problem != null) { bad++; sb.AppendLine($"FAIL {path}: {problem}"); }
                    if (Resources.Load<AudioClip>(res) == null) { bad++; sb.AppendLine("FAIL not loadable from Resources: " + res); }
                }
            }
            fails = bad;
            sb.AppendLine($"{clips} clips checked, {bad} failures");
            sb.AppendLine(bad == 0 ? "AUDIO_SELFTEST PASS" : "AUDIO_SELFTEST FAIL");
            return sb.ToString();
        }

        /// <summary>Batch: Unity -batchmode -projectPath . -executeMethod RedFrontier.Audio.Editor.AudioVerification.BatchSelfTest -quit</summary>
        public static void BatchSelfTest()
        {
            // A fresh checkout imports audio before this assembly compiles; reimport so the rules are applied.
            AssetDatabase.ImportAsset("Assets/Audio", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log(SelfTestReport(out var fails));
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        /// <summary>
        /// Batch: builds the enabled Build Settings scenes for WebGL into Builds/WebGL-audio for local verification.
        /// Uses uncompressed output so a plain static server works; the project's compression setting is restored afterwards.
        /// </summary>
        public static void BuildWebGL()
        {
            var previous = PlayerSettings.WebGL.compressionFormat;
            int code = 1;
            try
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = "Builds/WebGL-audio",
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"AUDIO_WEBGL_BUILD {s.result} size={s.totalSize} errors={s.totalErrors} time={s.totalTime}");
                foreach (var file in report.GetFiles().Where(f => f.path.Contains("Audio") || f.path.EndsWith(".data")))
                    Debug.Log($"AUDIO_WEBGL_FILE {file.path} {file.size}");
                code = s.result == BuildResult.Succeeded ? 0 : 1;
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = previous;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
