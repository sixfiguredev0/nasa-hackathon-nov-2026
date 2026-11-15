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
            float rocketMetal = rocketDef.nodes[rocketDef.root].rates[0].value; // Lander cargo per second at full efficiency
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
            Check(Near(ce[Res.Oxygen], 10f - 0.15f * ColonyEconomy.UpkeepScale * 2f) && !ce.lifeSupportShort && Near(ce.fitness, 1f - ColonyEconomy.FitnessLoss), "crew breathe scaled O2 and lose fitness");
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
            Check(Near(ce[Res.Metal] - metalBefore, 0.5f * rocketMetal), "unfit crew halves output");

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
            Check(Near(ye[Res.Water], 1.5f) && Near(ye[Res.Energy], 9f) && Near(ye[Res.Metal] - metal0, rocketMetal), "yield scales output, not use");

            // hard mode: solar factor, deprivation deaths, resupply delay
            var he = new ColonyEconomy { crew = 2 };
            var hs = new BuildingState(BuildingCatalog.Get(BuildingKind.Solar));
            he.buildings.Add(new BuildingState(rocketDef)); he.buildings.Add(hs);
            he.RecomputeCaps(); he[Res.Energy] = 0; he[Res.Oxygen] = 50; he[Res.Food] = 50; he[Res.Water] = 50;
            he.solarFactor = 0.25f; he.Tick(1f);
            Check(Near(he[Res.Energy], 0.5f), "solar factor scales solar energy only");
            he.solarFactor = 1f; he.resupplyFactor = 0f; float m0 = he[Res.Metal]; he.Tick(1f);
            Check(Near(he[Res.Metal], m0), "resupply delay stops lander metal");
            he.resupplyFactor = 1f;
            int lost = 0; Res lostRes = Res.Metal; he.CrewLost += r => { lost++; lostRes = r; };
            he[Res.Oxygen] = 0;
            for (int s = 0; s < 24; s++) he.Tick(1f);
            Check(lost == 0 && he.crew == 2, "no death before the oxygen limit");
            he.Tick(2f);
            Check(lost == 1 && lostRes == Res.Oxygen && he.crew == 1 && he.crewLost == 1, "oxygen out for 25 s costs one astronaut");
            he[Res.Oxygen] = 50; he.Tick(1f);
            Check(he.deprived[(int)Res.Oxygen] == 0f, "supply resets the death timer");

            // mission plan: deterministic per seed, varied across seeds, always playable
            var plan1 = new MissionPlan(42); var plan2 = new MissionPlan(42);
            bool same = plan1.events.Count == plan2.events.Count;
            for (int i = 0; same && i < plan1.events.Count; i++)
                same = plan1.events[i].kind == plan2.events[i].kind && Near(plan1.events[i].at, plan2.events[i].at) && Near(plan1.events[i].duration, plan2.events[i].duration);
            Check(same, "same seed gives the same mission");
            var signatures = new System.Collections.Generic.HashSet<string>();
            bool sane = true;
            for (int seed = 1; seed <= 100; seed++)
            {
                var mp = new MissionPlan(seed);
                var sig = new StringBuilder();
                bool sep = false, dust = false; float lastEnd = -1f;
                foreach (var ev in mp.events)
                {
                    sig.Append(ev.kind).Append((int)ev.at).Append(';');
                    sep |= ev.kind == MissionEventKind.SolarParticle; dust |= ev.kind == MissionEventKind.DustStorm;
                    sane &= ev.at >= MissionPlan.SolSeconds && ev.End <= mp.Length - 20f && ev.at - ev.warning >= lastEnd - MissionPlan.SolSeconds;
                    lastEnd = ev.at;
                }
                sane &= sep && dust && mp.events.Count >= 3 && mp.events.Count <= 4;
                signatures.Add(sig.ToString());
            }
            Check(sane, "every mission has the NASA event and a dust storm, 3-4 events, none in the first sol or the final seconds");
            Check(signatures.Count >= 90, "missions differ between seeds (" + signatures.Count + " of 100 distinct)");
            Check(Near(MissionPlan.DaylightFactor(MissionPlan.SolSeconds * 0.8f), 0f) && MissionPlan.DaylightFactor(MissionPlan.SolSeconds * 0.3f) > 1f, "no sun at night, peak sun at noon");

            sb.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
            return sb.ToString();
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-3f;
    }
}
