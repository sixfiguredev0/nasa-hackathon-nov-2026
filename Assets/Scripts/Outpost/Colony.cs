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
        Texture2D crewFitnessArt, crewFitnessSegments;
        static readonly int[] CrewSegmentEdges = { 10, 19, 26, 32, 40, 48 };
        GUIStyle crewFitnessLabel;

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
            gameObject.AddComponent<MissionPauseMenu>();
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
            DrawCrewFitness();
            float s = OutpostGUI.Scale, pad = 6 * s;
            GUI.Label(new Rect(pad, Screen.height - 24 * s, 600 * s, 20 * s), "B: Builder Mode    Right-click a building: upgrades",
                OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f)));
        }

        void DrawCrewFitness()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (crewFitnessLabel == null)
            {
                crewFitnessArt = Resources.Load<Texture2D>("OutpostHud/Crew Fitness");
                crewFitnessSegments = Resources.Load<Texture2D>("OutpostHud/Crew Fitness Segments");
                crewFitnessLabel = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(),
                    richText = true
                };
                crewFitnessLabel.normal.textColor = new Color32(248, 232, 215, 255);
            }

            const float width = 180f, height = 58f;
            float scale = Mathf.Min(Mathf.Clamp(Screen.height / 720f, 1f, 1.5f), (Screen.width - 24f) / width);
            Rect resourceBar = resourceTopBar.Bounds;
            float panelWidth = Mathf.Round(width * scale);
            // Keep the gauge level with the resource bar whenever both fit on the same row.
            float x = Mathf.Round(16f * scale);
            float gap = Mathf.Round(8f * scale);
            float y = x + panelWidth + gap <= resourceBar.xMin ? resourceBar.yMin : resourceBar.yMax + gap;
            var panel = new Rect(x, y, panelWidth, Mathf.Round(height * scale));
            GUI.DrawTexture(panel, crewFitnessArt);

            float fitness = economy.fitness;
            Color originalColor = GUI.color;
            int segmentCount = CrewSegmentEdges.Length - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                // Drain from the bottom. The current segment fades through its 20% range.
                float lit = Mathf.Clamp01(fitness * segmentCount - i);
                if (lit <= 0f) continue;
                float top = CrewSegmentEdges[i];
                float segmentHeight = CrewSegmentEdges[i + 1] - top;
                var segment = new Rect(panel.x, panel.y + top * scale, panel.width, segmentHeight * scale);
                var uv = new Rect(0f, 1f - (top + segmentHeight) / height, 1f, segmentHeight / height);
                GUI.color = new Color(originalColor.r, originalColor.g, originalColor.b, originalColor.a * lit);
                GUI.DrawTextureWithTexCoords(segment, crewFitnessSegments, uv);
            }
            GUI.color = originalColor;

            const float labelX = 68f, labelWidth = 102f;
            crewFitnessLabel.fontSize = Mathf.Max(1, Mathf.RoundToInt(12f * scale));
            GUI.Label(new Rect(panel.x + labelX * scale, panel.y + 7f * scale, labelWidth * scale, 15f * scale),
                $"<b>Fitness {fitness * 100f:0}%</b>", crewFitnessLabel);
            crewFitnessLabel.fontSize = Mathf.Max(1, Mathf.RoundToInt(11f * scale));
            GUI.Label(new Rect(panel.x + labelX * scale, panel.y + 22f * scale, labelWidth * scale, 13f * scale),
                $"<color=#cfb9b8>Work speed {economy.Efficiency * 100f:0}%</color>", crewFitnessLabel);

            string status = economy.lifeSupportShort ? $"<color=#ff978e>Out of {economy.shortOf.ToString().ToLowerInvariant()}</color>"
                : economy.training != null && !economy.training.stalled ? "<color=#a0e6b7>Training</color>" : string.Empty;
            GUI.Label(new Rect(panel.x + labelX * scale, panel.y + 35f * scale, labelWidth * scale, 13f * scale),
                status, crewFitnessLabel);
        }
    }
}
