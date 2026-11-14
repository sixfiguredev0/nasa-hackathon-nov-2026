using System.Collections;
using System.Collections.Generic;
using RedFrontier.Mars;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Lands the Rocket on the Landing Pad zone, owns every placed building, runs the economy and draws the resource bar.
    /// Other buildings are placed by the player in <see cref="BuilderMode"/> anywhere inside <see cref="BuildArea"/>.
    /// </summary>
    public class Colony : MonoBehaviour
    {
        public MarsMapGenerator map;
        [Tooltip("Starting stock in Res order: Energy, Water, Food, Oxygen, Metal")]
        public float[] startStock = { 30, 40, 50, 50, 60 };

        public readonly ColonyEconomy economy = new ColonyEconomy();
        public readonly List<Building> buildings = new List<Building>();
        public static Colony Instance { get; private set; }

        /// <summary>Flat, rock-free lattice cells the player may build on: the outpost zones' bounds plus their clearance ring.</summary>
        public RectInt BuildArea { get; private set; }
        public bool Ready { get; private set; }
        internal readonly ResourceTopBar resourceTopBar = new ResourceTopBar();

        void Awake() => Instance = this;

        IEnumerator Start()
        {
            if (map == null) map = FindObjectOfType<MarsMapGenerator>();
            if (map == null) { Debug.LogError("Colony: no MarsMapGenerator in scene"); yield break; }
            while (map.Terrain == null) yield return null; // the map generates itself in its own Start

            var ob = map.placement.OutpostBounds;
            int pad = map.placement.outpostClearance;
            BuildArea = new RectInt(ob.xMin - pad, ob.yMin - pad, ob.width + 2 * pad, ob.height + 2 * pad);

            var rocketDef = BuildingCatalog.Get(BuildingKind.Rocket);
            int zi = map.placement.zones.FindIndex(z => z.name == "Landing Pad");
            var c = zi >= 0 ? map.placement.zones[zi].cells.center : BuildArea.center;
            var fp = rocketDef.footprint;
            var rocket = Spawn(rocketDef, new RectInt(Mathf.RoundToInt(c.x - fp.x / 2f), Mathf.RoundToInt(c.y - fp.y / 2f), fp.x, fp.y), false);
            gameObject.AddComponent<Crew>().Init(rocket);
            economy.crew = Crew.Picked.Length;
            if (GetComponent<BuilderMode>() == null) gameObject.AddComponent<BuilderMode>();

            economy.RecomputeCaps();
            for (int i = 0; i < ColonyEconomy.ResCount && i < startStock.Length; i++)
                economy.stock[i] = Mathf.Min(startStock[i], economy.cap[i]);
            Ready = true;
        }

        void Update()
        {
            if (Ready) economy.Tick(Time.deltaTime);
        }

        Building Spawn(BuildingDef def, RectInt cells, bool construct)
        {
            var go = new GameObject(def.name);
            go.transform.SetParent(transform, false);
            go.transform.position = map.LatticeToWorld(cells.center.x, cells.center.y);
            var state = new BuildingState(def);
            var b = go.AddComponent<Building>();
            b.Init(state, cells, construct);
            buildings.Add(b);
            if (!construct) economy.buildings.Add(state);
            return b;
        }

        public int CountOf(BuildingKind k) => buildings.FindAll(b => b.state.def.kind == k).Count;

        public Amount[] PlaceCost(BuildingDef def) => ColonyEconomy.PlaceCost(def, CountOf(def.kind));

        public bool Fits(RectInt cells)
        {
            var taken = new List<RectInt>();
            foreach (var b in buildings) taken.Add(b.cells);
            return Fits(BuildArea, cells, taken);
        }

        /// <summary>Inside the area and not overlapping any taken rect (touching edges is fine).</summary>
        public static bool Fits(RectInt area, RectInt cells, IEnumerable<RectInt> taken)
        {
            if (cells.xMin < area.xMin || cells.yMin < area.yMin || cells.xMax > area.xMax || cells.yMax > area.yMax) return false;
            foreach (var t in taken) if (t.Overlaps(cells)) return false;
            return true;
        }

        /// <summary>Pays and drops a blueprint; the crew walks over and builds it, then it joins the economy.</summary>
        public Building TryPlace(BuildingDef def, RectInt cells)
        {
            var cost = PlaceCost(def);
            if (!Ready || def.kind == BuildingKind.Rocket || !Fits(cells) || !economy.CanAfford(cost)) return null;
            economy.Pay(cost);
            return Spawn(def, cells, true);
        }

        /// <summary>Called when the crew finishes a placed building.</summary>
        public void Commission(BuildingState s)
        {
            if (economy.buildings.Contains(s)) return;
            economy.buildings.Add(s);
            economy.RecomputeCaps();
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
            if (!Ready) return;
            resourceTopBar.Draw(economy);
            float s = OutpostGUI.Scale, w = 150 * s, h = 22 * s, pad = 6 * s;

            // crew fitness bar under the resources
            var fr = new Rect(pad, resourceTopBar.Bottom + pad, w * 1.6f, h * 0.85f);
            OutpostGUI.Panel(fr, new Color(0f, 0f, 0f, 0.6f));
            var fc = Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.4f), economy.fitness);
            OutpostGUI.Panel(new Rect(fr.x, fr.yMax - 3 * s, fr.width * economy.fitness, 3 * s), fc);
            string doing = economy.training != null ? "  <color=#8f8>training</color>" : "";
            GUI.Label(new Rect(fr.x + 6 * s, fr.y, fr.width, fr.height - 2 * s),
                string.Format("Crew fitness {0:0}%  (work speed {1:0}%){2}", economy.fitness * 100, economy.Efficiency * 100, doing),
                OutpostGUI.Text(10 * s, TextAnchor.MiddleLeft, Color.white));
            if (economy.lifeSupportShort)
                GUI.Label(new Rect(fr.xMax + pad, fr.y, 500 * s, fr.height), "<b>OUT OF " + economy.shortOf.ToString().ToUpper() + "!</b> The crew is getting weak fast.",
                    OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, new Color(1f, 0.4f, 0.35f)));

            GUI.Label(new Rect(pad, Screen.height - 24 * s, 600 * s, 20 * s), "B: Builder Mode    Right-click a building: upgrades",
                OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f)));
        }
    }
}
