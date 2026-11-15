using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    public enum Res { Energy, Water, Food, Oxygen, Metal }

    [Serializable]
    public struct Amount
    {
        public Res res;
        public float value;
        public Amount(Res r, float v) { res = r; value = v; }
    }

    public enum NodeStatus { Owned, Current, Available, TooExpensive, NeedsRocket, Future, Closed }

    /// <summary>Runtime state of one building: the upgrade node it is on.</summary>
    public class BuildingState
    {
        public readonly BuildingDef def;
        public string nodeId;
        public bool stalled;
        public Res missing;
        /// <summary>Offline from an equipment failure event (set and cleared by <see cref="MissionDirector"/>).</summary>
        public bool broken;
        /// <summary>Output multiplier from where it stands (<see cref="SiteSurvey"/>). Battery: also scales storage. Gym: training.</summary>
        public float yield = 1f;
        internal readonly float[] paid = new float[ColonyEconomy.ResCount];

        public BuildingState(BuildingDef d) { def = d; nodeId = d.root; }

        public UpgradeNode Node => def.nodes[nodeId];

        internal void RecordCost(Amount[] cost)
        {
            foreach (var amount in cost) paid[(int)amount.res] += amount.value;
        }

        /// <summary>True for the current node and every node before it on the path.</summary>
        public bool Owns(string id)
        {
            for (var n = nodeId; n != null; n = def.Parent(n))
                if (n == id) return true;
            return false;
        }
    }

    /// <summary>Resource stock, caps, production, crew life support and fitness. Plain C# so it can be tested without a scene.</summary>
    public class ColonyEconomy
    {
        public static readonly int ResCount = Enum.GetValues(typeof(Res)).Length;

        /// <summary>What each astronaut breathes, eats and drinks per second.</summary>
        /// <summary>Astronauts in a mission (the art has exactly two).</summary>
        public const int DefaultCrew = 2;
        /// <summary>Stock the mission starts with, in Res order: Energy, Water, Food, Oxygen, Metal. Tuned with the economy simulator.</summary>
        public static readonly float[] MissionStart = { 30f, 40f, 50f, 50f, 60f };
        public const float UpkeepScale = 1.75f;        // hard mode: tuned with Red Frontier > Outpost > Simulate Economy
        public static readonly Amount[] UpkeepPerCrew =
        {
            new Amount(Res.Oxygen, 0.15f * UpkeepScale), new Amount(Res.Food, 0.08f * UpkeepScale), new Amount(Res.Water, 0.08f * UpkeepScale)
        };
        /// <summary>Seconds of continuous shortage after which one astronaut is lost. Air is the quickest, food the slowest.</summary>
        public static float DeathAfter(Res r)
        {
            switch (r)
            {
                case Res.Oxygen: return 25f;
                case Res.Water: return 60f;
                case Res.Food: return 100f;
                default: return 0f;
            }
        }
        public const float FitnessLoss = 1f / 300f;    // per second in low gravity: fit to unfit in 5 minutes
        public const float ShortageLoss = 1f / 25f;    // per second while Oxygen, Food or Water has run out
        public const float RepeatCostStep = 0.35f;     // each extra copy of a building costs 35% more to place

        /// <summary>Astronauts to keep alive. 0 (tests) turns off upkeep and fitness.</summary>
        public int crew;
        /// <summary>0..1. Sets crew walk/build speed and every building's output (50% when unfit).</summary>
        public float fitness = 1f;
        /// <summary>Gym the crew is training in right now (set by Crew), or null.</summary>
        public BuildingState training;
        public bool lifeSupportShort;
        public Res shortOf;
        /// <summary>Day/night and dust: scales Energy made by Solar buildings only (1 = noon on a clear day).</summary>
        public float solarFactor = 1f;
        /// <summary>Scales Metal delivered by the lander (0 while Earth's resupply is delayed).</summary>
        public float resupplyFactor = 1f;
        /// <summary>Seconds each of Oxygen / Water / Food has been out. Reset when it is available again.</summary>
        public readonly float[] deprived = new float[ResCount];
        public int crewLost;
        /// <summary>Raised when an astronaut dies; the argument is the resource that ran out.</summary>
        public event Action<Res> CrewLost;

        public float Efficiency => Mathf.Lerp(0.5f, 1f, fitness);

        public readonly float[] stock = new float[ResCount], cap = new float[ResCount], net = new float[ResCount];
        public readonly List<BuildingState> buildings = new List<BuildingState>();

        public float this[Res r] { get => stock[(int)r]; set => stock[(int)r] = value; }

        public int RocketTier
        {
            get
            {
                foreach (var b in buildings) if (b.def.kind == BuildingKind.Rocket) return b.Node.level;
                return 0;
            }
        }

        public void RecomputeCaps()
        {
            CalculateCaps(cap);
            for (int i = 0; i < ResCount; i++) stock[i] = Mathf.Min(stock[i], cap[i]);
        }

        internal void CalculateCaps(float[] result, BuildingState excluding = null)
        {
            Array.Clear(result, 0, ResCount);
            foreach (var b in buildings)
            {
                if (b == excluding) continue;
                foreach (var a in b.Node.storage)
                    result[(int)a.res] += a.value * (b.def.kind == BuildingKind.Battery ? b.yield : 1f);
            }
        }

        /// <summary>
        /// Producers run first so consumers can use this tick's output. A consumer that can't pay stops (stalled).
        /// Then the crew takes life support; running out of any of it wrecks their fitness fast.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return; // paused: keep rates, shortage state and deprivation timers as they were
            Array.Clear(net, 0, ResCount);
            float eff = Efficiency;
            for (int pass = 0; pass < 2; pass++)
                foreach (var b in buildings)
                    if (Consumes(b.Node) == (pass == 1)) Run(b, dt, eff);
            if (crew <= 0) return;

            lifeSupportShort = false;
            foreach (var a in UpkeepPerCrew)
            {
                if (crew <= 0) break;
                int i = (int)a.res;
                float need = a.value * crew * dt;
                net[i] -= a.value * crew;
                if (stock[i] >= need) { stock[i] -= need; deprived[i] = 0f; }
                else
                {
                    stock[i] = 0f; lifeSupportShort = true; shortOf = a.res;
                    deprived[i] += dt;
                    if (deprived[i] >= DeathAfter(a.res))
                    {
                        deprived[i] = 0f;
                        crew--; crewLost++;
                        CrewLost?.Invoke(a.res);
                    }
                }
            }
            float f = fitness - (lifeSupportShort ? ShortageLoss : FitnessLoss) * dt;
            if (training != null && !training.stalled && !lifeSupportShort && buildings.Contains(training)) f += training.Node.train * training.yield * dt;
            fitness = Mathf.Clamp01(f);
        }

        static bool Consumes(UpgradeNode n)
        {
            foreach (var a in n.rates) if (a.value < 0) return true;
            return false;
        }

        /// <summary>Output is scaled by crew efficiency and the site's yield; what a building uses is not.</summary>
        void Run(BuildingState b, float dt, float eff)
        {
            var rates = b.Node.rates;
            b.stalled = false;
            if (b.broken) { b.stalled = true; return; }
            foreach (var a in rates)
                if (a.value < 0 && stock[(int)a.res] < -a.value * dt) { b.stalled = true; b.missing = a.res; return; }
            foreach (var a in rates)
            {
                int i = (int)a.res;
                float v = a.value > 0 ? a.value * eff * b.yield : a.value;
                if (v > 0 && a.res == Res.Energy && b.def.kind == BuildingKind.Solar) v *= solarFactor;
                if (v > 0 && a.res == Res.Metal && b.def.kind == BuildingKind.Rocket) v *= resupplyFactor;
                stock[i] = Mathf.Min(stock[i] + v * dt, cap[i]);
                net[i] += v;
            }
        }

        public bool CanAfford(Amount[] cost)
        {
            foreach (var a in cost) if (stock[(int)a.res] < a.value) return false;
            return true;
        }

        public void Pay(Amount[] cost)
        {
            foreach (var a in cost) stock[(int)a.res] -= a.value;
        }

        /// <summary>Place cost of a building when the colony already has <paramref name="owned"/> of its kind.</summary>
        public static Amount[] PlaceCost(BuildingDef def, int owned)
        {
            var baseCost = def.nodes[def.root].cost;
            var c = new Amount[baseCost.Length];
            for (int i = 0; i < c.Length; i++) c[i] = new Amount(baseCost[i].res, Mathf.Round(baseCost[i].value * (1f + RepeatCostStep * owned)));
            return c;
        }

        public NodeStatus Status(BuildingState b, string id)
        {
            if (id == b.nodeId) return NodeStatus.Current;
            if (b.Owns(id)) return NodeStatus.Owned;
            var parent = b.def.Parent(id);
            if (parent == b.nodeId)
            {
                var n = b.def.nodes[id];
                if (n.rocketTier > RocketTier) return NodeStatus.NeedsRocket;
                return CanAfford(n.cost) ? NodeStatus.Available : NodeStatus.TooExpensive;
            }
            // first owned ancestor: the current node means "later on this path", anything older means another branch was picked
            var p = parent;
            while (p != null && !b.Owns(p)) p = b.def.Parent(p);
            return p == b.nodeId ? NodeStatus.Future : NodeStatus.Closed;
        }

        public bool TryUpgrade(BuildingState b, string id)
        {
            // blueprints (placed but not yet built by the crew) are not in the economy and can't be upgraded
            if (!buildings.Contains(b) || Status(b, id) != NodeStatus.Available) return false;
            var cost = b.def.nodes[id].cost;
            Pay(cost);
            b.RecordCost(cost);
            b.nodeId = id;
            RecomputeCaps();
            return true;
        }
    }
}
