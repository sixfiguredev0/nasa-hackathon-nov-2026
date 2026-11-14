using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.U2D;

namespace RedFrontier.Mars.EditorTools
{
    public static class MarsSceneBuilder
    {
        const string GrainPath = "Assets/Art/Mars/Terrain/MarsGrain.png";
        const string ScenePath = "Assets/Scenes/MarsOutpost.unity";
        const string SpaceDir = "Assets/Art/Mars/Space";

        [MenuItem("Red Frontier/Mars/Import Art Settings")]
        public static void ConfigureTextures()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(GrainPath);
            if (ti == null) { Debug.LogWarning("Missing " + GrainPath + " (run Tools/MarsArt/make_mars_art.py)"); return; }
            ti.textureType = TextureImporterType.Default;
            ti.isReadable = true;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SpaceDir }))
            {
                var si = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                si.textureType = TextureImporterType.Sprite;
                si.spriteImportMode = SpriteImportMode.Single;
                si.spritePixelsPerUnit = 16;
                si.filterMode = FilterMode.Point;
                si.mipmapEnabled = false;
                si.textureCompression = TextureImporterCompression.Uncompressed;
                si.alphaIsTransparency = true;
                si.wrapMode = TextureWrapMode.Clamp;
                si.SaveAndReimport();
            }
        }

        static Sprite Space(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(SpaceDir + "/" + n + ".png");

        [MenuItem("Red Frontier/Mars/Build Outpost Scene")]
        public static void Build()
        {
            ConfigureTextures();
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.orthographic = true;
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<PixelPerfectCamera>();

            var envGo = new GameObject("Mars Environment");
            var env = envGo.AddComponent<MarsEnvironmentController>();
            env.cam = cam;
            env.ConfigureCamera();

            var mapGo = new GameObject("Mars Map");
            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(mapGo.transform, false);
            var sr = groundGo.AddComponent<SpriteRenderer>();
            var skyGo = new GameObject("Mars Sky");
            var sky = skyGo.AddComponent<MarsSky>();
            sky.sunFrames = new[] { Space("Space_sun_0"), Space("Space_sun_1"), Space("Space_sun_2") };
            sky.earth = Space("Space_earth_8");
            sky.phobos = Space("Space_phobos");
            sky.deimos = Space("Space_deimos");
            sky.shootingStar = Space("Space_shootingstar");

            var gen = mapGo.AddComponent<MarsMapGenerator>();
            gen.sky = sky;
            gen.ground = sr;
            gen.environment = env;
            gen.grainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainPath);
            gen.useRandomSeed = true;
            gen.Generate(1337);
            RedFrontier.Outpost.EditorTools.OutpostEditor.AddColony(gen, cam);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("MARS_BUILD done: " + ScenePath + " hash=" + gen.MapHash);
        }

        static void SetGameViewSize(int w, int h)
        {
            var asm = typeof(UnityEditor.Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var inst = single.GetProperty("instance").GetValue(null, null);
            var group = sizesType.GetMethod("GetGroup").Invoke(inst, new object[] { (int)GameViewSizeGroupType.Standalone });
            var gType = group.GetType();
            var sizeType = asm.GetType("UnityEditor.GameViewSize");
            var enumType = asm.GetType("UnityEditor.GameViewSizeType");
            string label = "RF " + w + "x" + h;
            var texts = (string[])gType.GetMethod("GetDisplayTexts").Invoke(group, null);
            int idx = System.Array.FindIndex(texts, t => t.Contains(label));
            if (idx < 0)
            {
                var ctor = sizeType.GetConstructor(new[] { enumType, typeof(int), typeof(int), typeof(string) });
                var size = ctor.Invoke(new object[] { System.Enum.ToObject(enumType, 1), w, h, label });
                gType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                texts = (string[])gType.GetMethod("GetDisplayTexts").Invoke(group, null);
                idx = System.Array.FindIndex(texts, t => t.Contains(label));
            }
            var gv = EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"));
            gv.GetType().GetProperty("selectedSizeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).SetValue(gv, idx, null);
            gv.Repaint();
        }

        [MenuItem("Red Frontier/Mars/Game View 1920x1080")]
        public static void GameView1080() => SetGameViewSize(1920, 1080);

        [MenuItem("Red Frontier/Mars/Game View 1366x768")]
        public static void GameView768() => SetGameViewSize(1366, 768);

        [MenuItem("Red Frontier/Mars/Game View 1280x720")]
        public static void GameView720() => SetGameViewSize(1280, 720);

        [MenuItem("Red Frontier/Mars/Build WebGL Test")]
        public static void BuildWebGL()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            Directory.CreateDirectory(root);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(root, "WebGL"),
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            var s = report.summary;
            File.WriteAllText(Path.Combine(root, "webgl_result.txt"),
                s.result + " size=" + s.totalSize + " seconds=" + s.totalTime.TotalSeconds + " errors=" + s.totalErrors + " warnings=" + s.totalWarnings);
        }

        [MenuItem("Red Frontier/Mars/Run Self Test")]
        public static void RunSelfTest() => Debug.Log(SelfTestReport());

        public static string SelfTestReport()
        {
            var gen = Object.FindObjectOfType<MarsMapGenerator>();
            if (gen == null) return "MARS_TEST no generator in scene";
            var sb = new StringBuilder("MARS_TEST\n");
            int[] seeds = { 11, 4242, 987654321 };
            var hashes = new Dictionary<int, string>();
            foreach (int sd in seeds)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                gen.Generate(sd);
                sw.Stop();
                hashes[sd] = gen.MapHash;
                sb.AppendLine(string.Format("seed {0}: hash {1} craters {2} rocks {3} ({4} ms) violations: {5}", sd, gen.MapHash, gen.Terrain.Craters.Count, gen.Terrain.Rocks.Count, sw.ElapsedMilliseconds, Validate(gen)));
            }
            sb.AppendLine("all seeds differ: " + (new HashSet<string>(hashes.Values).Count == seeds.Length));
            bool same = true;
            foreach (int sd in seeds)
            {
                gen.Generate(sd);
                if (gen.MapHash != hashes[sd]) same = false;
            }
            sb.AppendLine("repeat seeds identical: " + same);
            return sb.ToString();
        }

        // Outpost cells must be flat (height 0) and free of rocks and craters.
        static int Validate(MarsMapGenerator g)
        {
            int bad = 0;
            var t = g.Terrain;
            var r = g.placement;
            foreach (var z in r.zones)
                for (int j = z.cells.yMin; j < z.cells.yMax; j++)
                    for (int i = z.cells.xMin; i < z.cells.xMax; i++)
                        for (float fj = 0.1f; fj < 1f; fj += 0.4f)
                            for (float fi = 0.1f; fi < 1f; fi += 0.4f)
                                if (Mathf.Abs(t.HeightAtLattice(i + fi, j + fj)) > 0.5f) bad++;
            foreach (var rk in t.Rocks)
                if (r.InZone(Mathf.FloorToInt(rk.i), Mathf.FloorToInt(rk.j), 0)) bad++;
            foreach (var c in t.Craters)
                if (r.InZone(Mathf.FloorToInt(c.i), Mathf.FloorToInt(c.j), Mathf.CeilToInt(c.r))) bad++;
            return bad;
        }
    }
}
