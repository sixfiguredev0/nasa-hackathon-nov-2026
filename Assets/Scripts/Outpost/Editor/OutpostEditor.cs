using System.Text;
using RedFrontier.Mars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RedFrontier.Outpost.EditorTools
{
    public static class OutpostEditor
    {
        [MenuItem("Red Frontier/Outpost/Add Colony To Open Scene")]
        public static void AddToOpenScene()
        {
            if (UnityEngine.Object.FindObjectOfType<Colony>() != null) { Debug.Log("OUTPOST Colony already in scene"); return; }
            var go = AddColony(UnityEngine.Object.FindObjectOfType<MarsMapGenerator>(), Camera.main);
            Undo.RegisterCreatedObjectUndo(go, "Add Colony");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("OUTPOST Colony added");
        }

        public static GameObject AddColony(MarsMapGenerator map, Camera cam)
        {
            var go = new GameObject("Colony");
            var colony = go.AddComponent<Colony>();
            colony.map = map;
            var bubbles = go.AddComponent<UpgradeBubbles>();
            bubbles.colony = colony;
            bubbles.cam = cam;
            return go;
        }

        [MenuItem("Red Frontier/Outpost/Run Self Test")]
        public static void RunSelfTest() => Debug.Log(SelfTestReport());

        public static string SelfTestReport()
        {
            var sb = new StringBuilder("OUTPOST_TEST\n");
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; sb.AppendLine("FAIL " + what); } }

            // catalog: links resolve, every node reachable, costs fit the storage the Rocket gives at that tier
            var rocketDef = BuildingCatalog.Get(BuildingKind.Rocket);
            float RocketCap(int tier)
            {
                foreach (var n in rocketDef.nodes.Values) if (n.level == tier) return n.storage[0].value;
                return 0;
            }
            foreach (var def in BuildingCatalog.Defs)
            {
                int reachable = 0;
                foreach (var n in def.nodes.Values)
                {
                    foreach (var c in n.next) Check(def.nodes.ContainsKey(c), def.name + ": missing node " + c);
                    if (n.id == def.root || def.Parent(n.id) != null) reachable++;
                    int tier = def.kind == BuildingKind.Rocket ? n.level - 1 : Mathf.Max(1, n.rocketTier);
                    if (tier < 1) continue;
                    foreach (var a in n.cost) Check(a.value <= RocketCap(tier), n.id + " costs " + a.value + " " + a.res + " but storage is " + RocketCap(tier));
                }
                Check(reachable == def.nodes.Count, def.name + ": unreachable nodes");
            }

            // economy
            var e = new ColonyEconomy();
            var rocket = new BuildingState(rocketDef);
            var solar = new BuildingState(BuildingCatalog.Get(BuildingKind.Solar));
            var water = new BuildingState(BuildingCatalog.Get(BuildingKind.Water));
            e.buildings.Add(rocket); e.buildings.Add(solar); e.buildings.Add(water);
            e.RecomputeCaps();
            Check(e.cap[(int)Res.Metal] == 100 && e.RocketTier == 1, "rocket1 caps / tier");

            e[Res.Metal] = 100;
            Check(solar.nodeId == "solar1" && e.Status(solar, "solar1") == NodeStatus.Current, "placed buildings start at level 1");
            Check(e.Status(solar, "solar2") == NodeStatus.NeedsRocket, "solar2 gated by rocket tier");
            Check(!e.TryUpgrade(solar, "solar3a"), "cannot skip steps");
            Check(e.Status(water, "water2") == NodeStatus.NeedsRocket && e.Status(water, "water3a") == NodeStatus.Future, "future statuses");

            e.buildings.Remove(solar); e[Res.Energy] = 0; e[Res.Water] = 0;
            e.Tick(1f);
            Check(water.stalled && water.missing == Res.Energy && Near(e[Res.Water], 0), "drill stalls without energy");
            e.buildings.Insert(1, solar);
            e.Tick(1f);
            Check(!water.stalled && Near(e[Res.Water], 1) && Near(e[Res.Energy], 1) && Near(e.net[(int)Res.Energy], 1), "solar powers drill same tick");

            e[Res.Metal] = 99.9f; e.Tick(10f);
            Check(e[Res.Metal] <= 100f, "stock clamped to cap");

            e[Res.Metal] = 100; e[Res.Food] = 100;
            Check(e.TryUpgrade(rocket, "rocket2") && e.RocketTier == 2 && e.cap[(int)Res.Metal] == 200, "rocket2");
            for (int i = 0; i < ColonyEconomy.ResCount; i++) e.stock[i] = e.cap[i];
            Check(e.TryUpgrade(rocket, "rocket3"), "rocket3");
            for (int i = 0; i < ColonyEconomy.ResCount; i++) e.stock[i] = e.cap[i];
            Check(e.TryUpgrade(solar, "solar2") && e.TryUpgrade(solar, "solar3a"), "solar path to 3a");
            Check(e.Status(solar, "solar3b") == NodeStatus.Closed && e.Status(solar, "solar1") == NodeStatus.Owned, "other branch closed");

            // builder mode: place cost grows per copy, footprints may touch but not overlap or leave the area
            var solarDef = BuildingCatalog.Get(BuildingKind.Solar);
            Check(Near(ColonyEconomy.PlaceCost(solarDef, 0)[0].value, 20) && Near(ColonyEconomy.PlaceCost(solarDef, 2)[0].value, 34), "place cost +35% per copy");
            var area = new RectInt(0, 0, 10, 10);
            var taken = new[] { new RectInt(2, 2, 3, 3) };
            Check(Colony.Fits(area, new RectInt(5, 2, 2, 2), taken), "touching edge fits");
            Check(!Colony.Fits(area, new RectInt(4, 4, 2, 2), taken), "overlap blocked");
            Check(!Colony.Fits(area, new RectInt(9, 0, 2, 2), taken), "outside area blocked");
            Check(BuildingCatalog.Get(BuildingKind.Gym) != null && BuildingCatalog.Get(BuildingKind.Gym).nodes["gym1"].train > 0, "gym trains");

            // crew upkeep, fitness, gym
            var ce = new ColonyEconomy { crew = 2 };
            var gym = new BuildingState(BuildingCatalog.Get(BuildingKind.Gym));
            ce.buildings.Add(new BuildingState(rocketDef)); ce.buildings.Add(gym);
            ce.RecomputeCaps();
            ce[Res.Oxygen] = 10; ce[Res.Food] = 10; ce[Res.Water] = 10; ce[Res.Energy] = 0;
            ce.Tick(1f);
            Check(Near(ce[Res.Oxygen], 9.7f) && !ce.lifeSupportShort && Near(ce.fitness, 1f - ColonyEconomy.FitnessLoss), "crew breathe 0.3 O2/s and lose fitness");
            ce.training = gym; ce.fitness = 0.5f;
            ce.Tick(1f);
            Check(gym.stalled && ce.fitness < 0.5f, "stalled gym gives no training");
            ce[Res.Energy] = 50; ce.fitness = 0.5f;
            ce.Tick(1f);
            Check(ce.fitness > 0.5f, "powered gym trains crew");
            ce.training = null; ce[Res.Oxygen] = 0; ce.fitness = 1f;
            ce.Tick(1f);
            Check(ce.lifeSupportShort && ce.shortOf == Res.Oxygen && Near(ce.fitness, 1f - ColonyEconomy.ShortageLoss), "running out of oxygen hurts");
            float metalBefore = ce[Res.Metal]; ce.fitness = 0f; ce[Res.Oxygen] = 50;
            ce.Tick(1f);
            Check(Near(ce[Res.Metal] - metalBefore, 0.2f), "unfit crew halves output");

            // site yield: terrain changes output, clamped, and the economy applies it to output only
            var flat = new Site();
            var mesaTop = new Site { elevation = 1.75f };
            var dunes = new Site { sand = 1f };
            var freshCrater = new Site { elevation = -1f, crater = 1f, ejecta = 0.5f };
            Check(Near(SiteSurvey.Yield(BuildingKind.Solar, flat), 1f), "flat ground is 100%");
            Check(SiteSurvey.Yield(BuildingKind.Solar, mesaTop) > 1f && SiteSurvey.Yield(BuildingKind.Habitat, mesaTop) < 1f, "high ground: solar up, MOXIE down");
            Check(SiteSurvey.Yield(BuildingKind.Workshop, dunes) > 1f && SiteSurvey.Yield(BuildingKind.Solar, dunes) < 1f, "basalt sand: metal up, solar down");
            Check(SiteSurvey.Yield(BuildingKind.Water, freshCrater) > 1.5f && SiteSurvey.Yield(BuildingKind.Solar, freshCrater) < 1f, "fresh crater: ice up, sun down");
            Check(Near(SiteSurvey.Yield(BuildingKind.Solar, new Site { uneven = 1000f, crater = 1f, sand = 1f }), SiteSurvey.MinYield), "yield floor");
            var ye = new ColonyEconomy();
            var ys = new BuildingState(BuildingCatalog.Get(BuildingKind.Water)) { yield = 1.5f };
            ye.buildings.Add(new BuildingState(rocketDef)); ye.buildings.Add(ys);
            ye.RecomputeCaps(); ye[Res.Energy] = 10; ye[Res.Water] = 0;
            float metal0 = ye[Res.Metal];
            ye.Tick(1f);
            Check(Near(ye[Res.Water], 1.5f) && Near(ye[Res.Energy], 9f) && Near(ye[Res.Metal] - metal0, 0.4f), "yield scales output, not use");

            sb.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
            return sb.ToString();
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-3f;
    }
}
