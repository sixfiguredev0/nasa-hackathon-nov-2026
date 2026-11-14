using System.Collections;
using System.Collections.Generic;
using RedFrontier.Mars;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>Spawns every catalog building in its outpost zone, runs the economy and draws the resource bar.</summary>
    public class Colony : MonoBehaviour
    {
        public MarsMapGenerator map;
        [Tooltip("Starting stock in Res order: Energy, Water, Food, Oxygen, Metal")]
        public float[] startStock = { 30, 20, 20, 20, 40 };

        public readonly ColonyEconomy economy = new ColonyEconomy();
        public readonly List<Building> buildings = new List<Building>();
        public static Colony Instance { get; private set; }

        readonly ResourceTopBar resourceTopBar = new ResourceTopBar();
        bool ready;

        void Awake() => Instance = this;

        IEnumerator Start()
        {
            if (map == null) map = FindObjectOfType<MarsMapGenerator>();
            if (map == null) { Debug.LogError("Colony: no MarsMapGenerator in scene"); yield break; }
            while (map.Terrain == null) yield return null; // the map generates itself in its own Start

            foreach (var def in BuildingCatalog.Defs)
            {
                int zi = map.placement.zones.FindIndex(z => z.name == def.zone);
                if (zi < 0) { Debug.LogWarning("Colony: no outpost zone named " + def.zone); continue; }
                var cells = map.placement.zones[zi].cells;
                var go = new GameObject(def.name);
                go.transform.SetParent(transform, false);
                go.transform.position = map.LatticeToWorld(cells.center.x, cells.center.y);
                var state = new BuildingState(def);
                economy.buildings.Add(state);
                var b = go.AddComponent<Building>();
                b.Init(state, cells.size);
                buildings.Add(b);
            }
            economy.RecomputeCaps();
            for (int i = 0; i < ColonyEconomy.ResCount && i < startStock.Length; i++)
                economy.stock[i] = Mathf.Min(startStock[i], economy.cap[i]);
            ready = true;
        }

        void Update()
        {
            if (ready) economy.Tick(Time.deltaTime);
        }

        /// <summary>Front-most building whose sprite covers this world point.</summary>
        public Building BuildingAt(Vector3 world)
        {
            Building best = null;
            foreach (var b in buildings)
            {
                var bounds = b.Renderer.bounds;
                if (!bounds.Contains(new Vector3(world.x, world.y, bounds.center.z))) continue;
                if (best == null || b.Renderer.sortingOrder > best.Renderer.sortingOrder) best = b;
            }
            return best;
        }

        void OnGUI()
        {
            if (!ready) return;
            resourceTopBar.Draw(economy);
            float s = OutpostGUI.Scale, pad = 6 * s;
            GUI.Label(new Rect(pad, Screen.height - 24 * s, 600 * s, 20 * s), "Right-click a building to see its upgrade path",
                OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f)));
        }
    }
}
