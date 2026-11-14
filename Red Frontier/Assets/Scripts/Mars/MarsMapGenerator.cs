using System;
using UnityEngine;

namespace RedFrontier.Mars
{
    [Serializable]
    public class MarsMapSettings
    {
        [Range(24, 128)] public int width = 88;
        [Range(24, 128)] public int height = 64;
        [Tooltip("Gentle rolling hills")]
        [Range(0f, 1f)] public float terrainVariation = 0.6f;
        [Tooltip("Layered mesas and plateaus with cliff faces")]
        [Range(0f, 1f)] public float mesaAmount = 0.65f;
        [Tooltip("Dark basaltic sand ripples in basins and crater floors")]
        [Range(0f, 1f)] public float duneAmount = 0.7f;
        [Tooltip("Craters per 100 cells")]
        public float craterDensity = 3.5f;
        [Range(1f, 9f)] public float maxCraterRadius = 7f;
        [Tooltip("Boulders per 100 cells (crater ejecta and talus come on top)")]
        public float rockDensity = 4f;
        [Tooltip("Pebbles per 100 cells")]
        public float pebbleDensity = 14f;
    }

    /// <summary>
    /// Procedural Mars surface. Lattice cell (i, j) is a 24x12 px oblique parallelogram (artist tile footprint).
    /// The ground is baked into one sprite; outpost zones are flattened to height 0 so buildings sit on LatticeToWorld.
    /// </summary>
    [ExecuteAlways]
    public class MarsMapGenerator : MonoBehaviour
    {
        public const float Ppu = 16f;
        public const float TileWidthPx = MarsTerrain.CellW;
        public const float TileRisePx = MarsTerrain.CellH;

        [Header("Seed")]
        public int seed = 1337;
        public bool useRandomSeed = true;
        public bool generateOnStart = true;

        [Header("Map")]
        public MarsMapSettings settings = new MarsMapSettings();
        public MapPlacementRules placement = new MapPlacementRules();

        [Header("Scene references")]
        public SpriteRenderer ground;
        public MarsEnvironmentController environment;
        public MarsSky sky;
        [Tooltip("Artist ground grain (ground8 + scatter variants), 24x12 per variant, Read/Write enabled")]
        public Texture2D grainTexture;

        public int LastSeed { get; private set; }
        public string MapHash { get; private set; }
        public MarsTerrain Terrain { get; private set; }
        public Rect MapWorldRect { get; private set; }

        Vector3 groundOrigin; // world position of ground pixel (0, 0)

        void Start()
        {
            if (Application.isPlaying && generateOnStart) GenerateNew();
        }

        void OnEnable()
        {
            if (!Application.isPlaying && ground != null && ground.sprite == null) Generate(seed);
        }

        [ContextMenu("Generate (current seed)")]
        public void GenerateCurrent() => Generate(seed);

        [ContextMenu("Generate (new random seed)")]
        public void GenerateNew()
        {
            if (useRandomSeed) seed = Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            Generate(seed);
        }

        public void Generate(int newSeed)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            seed = LastSeed = newSeed;
            if (!placement.ZonesValid(settings.width, settings.height, out string err))
                Debug.LogWarning("MarsMapGenerator: outpost zones invalid: " + err);

            Terrain = new MarsTerrain(settings, placement, newSeed, grainTexture);
            var tex = Terrain.Bake();

            // put the outpost centre at world origin, snapped to whole pixels
            var b = placement.OutpostBounds;
            Vector2 c = placement.zones.Count > 0 ? Terrain.LatticeToGround(b.center.x, b.center.y) : new Vector2(Terrain.TexW, Terrain.TexH) * 0.5f;
            groundOrigin = new Vector3(-Mathf.Round(c.x), -Mathf.Round(c.y), 0f) / Ppu;

            if (ground.sprite != null)
            {
                var old = ground.sprite;
                ground.sprite = null;
                if (old.texture != null && old.texture.name == "MarsGround") DestroyAsset(old.texture);
                DestroyAsset(old);
            }
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, Ppu, 0, SpriteMeshType.FullRect);
            sp.name = "MarsGround";
            sp.hideFlags = HideFlags.DontSave;
            ground.sprite = sp;
            ground.transform.position = groundOrigin;
            ground.sortingOrder = -1000;

            MapWorldRect = new Rect(groundOrigin.x, groundOrigin.y, tex.width / Ppu, tex.height / Ppu);
            MapHash = Terrain.Hash();
            var view = MapWorldRect;
            if (sky != null)
            {
                sky.Build(newSeed, MapWorldRect);
                view.yMax = sky.SpaceTopY;
            }
            if (environment != null) environment.SetBounds(view);
            Debug.Log("Mars map generated: seed=" + newSeed + " hash=" + MapHash + " craters=" + Terrain.Craters.Count + " rocks=" + Terrain.Rocks.Count + " ms=" + clock.ElapsedMilliseconds);
        }

        static void DestroyAsset(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>World position of lattice point (i, j) on the ground plane (height 0). Cell centres are (i + 0.5, j + 0.5).</summary>
        public Vector3 LatticeToWorld(float i, float j)
        {
            if (Terrain == null) return Vector3.zero;
            var g = Terrain.LatticeToGround(i, j);
            return groundOrigin + new Vector3(g.x, g.y, 0f) / Ppu;
        }

        /// <summary>Same as LatticeToWorld but lifted by the terrain height (for props outside the flattened outpost).</summary>
        public Vector3 SurfaceToWorld(float i, float j) => LatticeToWorld(i, j) + Vector3.up * (Terrain != null ? Mathf.Round(Terrain.HeightAtLattice(i, j)) / Ppu : 0f);

        public static int SortingOrderFor(float worldY) => 5000 - Mathf.RoundToInt(worldY * Ppu);

        void OnDrawGizmosSelected()
        {
            if (placement == null || Terrain == null) return;
            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.9f);
            foreach (var z in placement.zones)
            {
                var r = z.cells;
                var a = LatticeToWorld(r.xMin, r.yMin); var b = LatticeToWorld(r.xMax, r.yMin);
                var c = LatticeToWorld(r.xMax, r.yMax); var d = LatticeToWorld(r.xMin, r.yMax);
                Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
            }
        }
    }
}
