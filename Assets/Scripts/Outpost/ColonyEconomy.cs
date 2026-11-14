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

        public BuildingState(BuildingDef d) { def = d; nodeId = d.root; }

        public UpgradeNode Node => def.nodes[nodeId];

        /// <summary>True for the current node and every node before it on the path.</summary>
        public bool Owns(string id)
        {
            for (var n = nodeId; n != null; n = def.Parent(n))
                if (n == id) return true;
            return false;
        }
    }

    /// <summary>Resource stock, caps and production. Plain C# so it can be tested without a scene.</summary>
    public class ColonyEconomy
    {
        public static readonly int ResCount = Enum.GetValues(typeof(Res)).Length;

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
            Array.Clear(cap, 0, ResCount);
            foreach (var b in buildings)
                foreach (var a in b.Node.storage) cap[(int)a.res] += a.value;
            for (int i = 0; i < ResCount; i++) stock[i] = Mathf.Min(stock[i], cap[i]);
        }

        /// <summary>Producers run first so consumers can use this tick's output. A consumer that can't pay stops (stalled).</summary>
        public void Tick(float dt)
        {
            Array.Clear(net, 0, ResCount);
            for (int pass = 0; pass < 2; pass++)
                foreach (var b in buildings)
                    if (Consumes(b.Node) == (pass == 1)) Run(b, dt);
        }

        static bool Consumes(UpgradeNode n)
        {
            foreach (var a in n.rates) if (a.value < 0) return true;
            return false;
        }

        void Run(BuildingState b, float dt)
        {
            var rates = b.Node.rates;
            b.stalled = false;
            foreach (var a in rates)
                if (a.value < 0 && stock[(int)a.res] < -a.value * dt) { b.stalled = true; b.missing = a.res; return; }
            foreach (var a in rates)
            {
                int i = (int)a.res;
                stock[i] = Mathf.Min(stock[i] + a.value * dt, cap[i]);
                net[i] += a.value;
            }
        }

        public bool CanAfford(Amount[] cost)
        {
            foreach (var a in cost) if (stock[(int)a.res] < a.value) return false;
            return true;
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
            if (Status(b, id) != NodeStatus.Available) return false;
            foreach (var a in b.def.nodes[id].cost) stock[(int)a.res] -= a.value;
            b.nodeId = id;
            RecomputeCaps();
            return true;
        }
    }
}
