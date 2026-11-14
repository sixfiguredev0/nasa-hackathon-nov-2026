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
            Check(e.TryUpgrade(solar, "solar1") && Near(e[Res.Metal], 80), "build solar1 pays 20 Metal");
            Check(e.Status(solar, "solar2") == NodeStatus.NeedsRocket, "solar2 gated by rocket tier");
            Check(!e.TryUpgrade(solar, "solar3a"), "cannot skip steps");
            Check(e.Status(water, "water1") == NodeStatus.Available && e.Status(water, "water3a") == NodeStatus.Future, "plot statuses");
            Check(e.TryUpgrade(water, "water1"), "build water1");

            solar.nodeId = "solar0"; e[Res.Energy] = 0; e[Res.Water] = 0;
            e.Tick(1f);
            Check(water.stalled && water.missing == Res.Energy && Near(e[Res.Water], 0), "drill stalls without energy");
            solar.nodeId = "solar1";
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

            sb.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
            return sb.ToString();
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-3f;
    }
}
