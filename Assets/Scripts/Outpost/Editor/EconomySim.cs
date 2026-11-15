using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Headless mission simulator. It runs the real <see cref="ColonyEconomy"/> and <see cref="MissionPlan"/> with three
    /// simple players over many seeds, to check that the mission is punishing but winnable:
    /// idle and naive players should die, a competent player should usually survive.
    /// The bots are crude stand-ins for people (fixed build order, no site choice, one crew job at a time), so the numbers
    /// show whether the rules are in a sane range, not how any real player will do.
    /// Editor: Red Frontier > Outpost > Simulate Economy (writes Logs/economy-sim.txt).
    /// </summary>
    public static class EconomySim
    {
        public enum Bot { Smart, Guided, Naive, Idle }

        public sealed class Result
        {
            public bool won;
            public int crewLeft, crewStart;
            public float endTime;
            public int deathSol;
            public Res firstCause;
            public bool hadLoss;
        }

        const float Dt = 0.5f, JobSeconds = 11f; // 8 s walking out and back + 3 s building, scaled by crew efficiency

        enum Kind { Place, Upgrade }
        struct Order
        {
            public Kind kind; public BuildingKind building; public string node;
            public Order(Kind k, BuildingKind b, string n = null) { kind = k; building = b; node = n; }
        }

        static Order P(BuildingKind b) => new Order(Kind.Place, b);
        static Order U(BuildingKind b, string node) => new Order(Kind.Upgrade, b, node);

        static List<Order> Plan(Bot bot)
        {
            switch (bot)
            {
                case Bot.Smart: // chosen on the fly by NextSmart
                    return new List<Order>();
                case Bot.Guided: // follows Marlonis's tutorial order exactly, then adds a Gym and batteries
                    return new List<Order>
                    {
                        P(BuildingKind.Solar), P(BuildingKind.Water), P(BuildingKind.Greenhouse), P(BuildingKind.Solar),
                        P(BuildingKind.Habitat), P(BuildingKind.Gym), P(BuildingKind.Battery), P(BuildingKind.Solar),
                        P(BuildingKind.Workshop), P(BuildingKind.Battery),
                        U(BuildingKind.Rocket, "rocket2"), U(BuildingKind.Greenhouse, "green2"), U(BuildingKind.Water, "water2"),
                        U(BuildingKind.Solar, "solar2"), U(BuildingKind.Battery, "bat2")
                    };
                case Bot.Naive: // builds what looks fun first, forgets the Gym and batteries
                    return new List<Order>
                    {
                        P(BuildingKind.Greenhouse), P(BuildingKind.Workshop), P(BuildingKind.Solar), P(BuildingKind.Water),
                        P(BuildingKind.Habitat), P(BuildingKind.Solar)
                    };
                default:
                    return new List<Order>();
            }
        }


        static readonly (BuildingKind kind, string node)[] SmartUpgrades =
        {
            (BuildingKind.Rocket, "rocket2"), (BuildingKind.Greenhouse, "green2"), (BuildingKind.Water, "water2"),
            (BuildingKind.Solar, "solar2"), (BuildingKind.Battery, "bat2"), (BuildingKind.Habitat, "hab2"), (BuildingKind.Workshop, "shop2")
        };

        /// <summary>
        /// A competent player's next build: life support first, then enough solar and battery to carry the night,
        /// then a Gym, then upgrades. It reads the power budget the way the HUD lets a person read it.
        /// </summary>
        static Order NextSmart(ColonyEconomy eco, ref int upgrade)
        {
            bool Has(BuildingKind k) => eco.buildings.Exists(b => b.def.kind == k);
            float demand = 0f, supply = 0f;
            foreach (var b in eco.buildings)
                foreach (var r in b.Node.rates)
                {
                    if (r.res != Res.Energy) continue;
                    if (r.value < 0f) demand -= r.value;
                    else if (b.def.kind == BuildingKind.Solar) supply += r.value * 0.95f * eco.Efficiency;
                }
            if (!Has(BuildingKind.Solar)) return P(BuildingKind.Solar);
            if (!Has(BuildingKind.Water)) return P(BuildingKind.Water);
            if (!Has(BuildingKind.Habitat)) return P(BuildingKind.Habitat);
            if (!Has(BuildingKind.Greenhouse)) return P(BuildingKind.Greenhouse);
            if (supply < 1.5f * demand) return P(BuildingKind.Solar);
            if (!Has(BuildingKind.Gym)) return P(BuildingKind.Gym);
            if (eco.cap[(int)Res.Energy] < demand * 45f) return P(BuildingKind.Battery);
            if (!Has(BuildingKind.Workshop)) return P(BuildingKind.Workshop);
            while (upgrade < SmartUpgrades.Length)
            {
                var u = SmartUpgrades[upgrade];
                var target = eco.buildings.Find(x => x.def.kind == u.kind);
                if (target == null || eco.Status(target, u.node) == NodeStatus.Owned) { upgrade++; continue; }
                return U(u.kind, u.node);
            }
            return P(BuildingKind.Solar);
        }

        public static Result Play(int seed, Bot bot, float[] startStock, Action<float, ColonyEconomy> trace = null)
        {
            var plan = new MissionPlan(seed);
            var eco = new ColonyEconomy { crew = ColonyEconomy.DefaultCrew };
            var rocket = new BuildingState(BuildingCatalog.Get(BuildingKind.Rocket));
            eco.buildings.Add(rocket);
            eco.RecomputeCaps();
            for (int i = 0; i < ColonyEconomy.ResCount; i++) eco.stock[i] = Mathf.Min(startStock[i], eco.cap[i]);

            var res = new Result { crewStart = eco.crew };
            eco.CrewLost += r =>
            {
                if (!res.hadLoss) { res.hadLoss = true; res.firstCause = r; res.deathSol = MissionPlan.SolAt(res.endTime); }
            };

            var orders = Plan(bot);
            int next = 0, upgradeIdx = 0;
            BuildingState gym = null;
            bool jobActive = false; float jobDone = 0f; Order job = default; Amount[] jobCost = null;
            var pick = new System.Random(seed + 99);
            var brokenUntil = new Dictionary<BuildingState, float>();
            var fired = new bool[plan.events.Count];
            float shelterUntil = -1f, dipRestoreAt = -1f, removedFitness = 0f;
            bool trained = false;

            for (float t = 0f; t < plan.Length && eco.crew > 0; t += Dt)
            {
                res.endTime = t;
                bool sheltering = t < shelterUntil;

                // events, exactly as MissionDirector applies them
                for (int i = 0; i < plan.events.Count; i++)
                {
                    var e = plan.events[i];
                    if (fired[i] || t < e.at) continue;
                    fired[i] = true;
                    if (e.kind == MissionEventKind.SolarParticle)
                    {
                        bool shelter = bot == Bot.Smart || bot == Bot.Guided; // players who read the warning shelter; the others keep working
                        if (shelter) shelterUntil = t + MissionPlan.SepShelterSeconds;
                        else
                        {
                            removedFitness = Mathf.Min(MissionPlan.SepFitnessDip, eco.fitness);
                            eco.fitness -= removedFitness;
                            dipRestoreAt = t + MissionPlan.SepShelterSeconds;
                        }
                    }
                    else if (e.kind == MissionEventKind.EquipmentFailure)
                    {
                        var cands = eco.buildings.FindAll(b => b.def.kind != BuildingKind.Rocket && !b.broken);
                        if (cands.Count > 0)
                        {
                            var hit = cands[pick.Next(cands.Count)];
                            hit.broken = true;
                            brokenUntil[hit] = t + e.duration;
                        }
                    }
                }
                if (dipRestoreAt >= 0f && t >= dipRestoreAt) { eco.fitness = Mathf.Clamp01(eco.fitness + removedFitness); removedFitness = 0f; dipRestoreAt = -1f; }
                if (brokenUntil.Count > 0)
                {
                    var done = new List<BuildingState>();
                    foreach (var kv in brokenUntil) if (t >= kv.Value) done.Add(kv.Key);
                    foreach (var b in done) { b.broken = false; brokenUntil.Remove(b); }
                }
                eco.solarFactor = MissionPlan.DaylightFactor(t) * plan.DustFactor(t);
                eco.resupplyFactor = plan.ResupplyBlocked(t) ? 0f : 1f;

                // the crew: one job at a time, training in the Gym when idle
                if (!jobActive && !sheltering && bot != Bot.Idle && (bot == Bot.Smart || next < orders.Count))
                {
                    var o = bot == Bot.Smart ? NextSmart(eco, ref upgradeIdx) : orders[next];
                    var def = BuildingCatalog.Get(o.building);
                    if (o.kind == Kind.Place)
                    {
                        int owned = eco.buildings.FindAll(b => b.def.kind == o.building).Count;
                        var cost = ColonyEconomy.PlaceCost(def, owned);
                        if (eco.CanAfford(cost)) { eco.Pay(cost); jobActive = true; job = o; jobCost = cost; jobDone = 0f; next++; }
                    }
                    else
                    {
                        var target = eco.buildings.Find(b => b.def.kind == o.building);
                        if (target == null) next++;
                        else if (eco.Status(target, o.node) == NodeStatus.Available && eco.TryUpgrade(target, o.node)) { jobActive = true; job = o; jobDone = 0f; next++; }
                    }
                }
                if (jobActive && !sheltering)
                {
                    jobDone += Dt * eco.Efficiency;
                    if (jobDone >= JobSeconds)
                    {
                        jobActive = false;
                        if (job.kind == Kind.Place)
                        {
                            var s = new BuildingState(BuildingCatalog.Get(job.building));
                            eco.buildings.Add(s);
                            eco.RecomputeCaps();
                            if (job.building == BuildingKind.Gym && gym == null) gym = s;
                        }
                    }
                }
                bool wantTrain = gym != null && !jobActive && !sheltering && eco.fitness < (trained ? 0.995f : 0.85f);
                trained = wantTrain;
                eco.training = wantTrain ? gym : null;

                eco.Tick(Dt);
                res.endTime = t;
                if (trace != null && Mathf.Abs(t % 10f) < Dt * .5f) trace(t, eco);
            }

            res.crewLeft = eco.crew;
            res.won = eco.crew > 0 && res.endTime >= plan.Length - Dt * 1.5f;
            return res;
        }

        public static string Report(int seeds, float[] startStock, string label)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== " + label + " | start stock E/W/F/O/M = " + string.Join("/", startStock) + " | upkeep x" + ColonyEconomy.UpkeepScale +
                " | crew " + ColonyEconomy.DefaultCrew + " | " + seeds + " seeds x " + MissionPlan.DefaultSols + " sols (" + MissionPlan.SolSeconds + " s each) ==");
            foreach (Bot bot in Enum.GetValues(typeof(Bot)))
            {
                int won = 0, both = 0, one = 0, none = 0, o2 = 0, water = 0, food = 0;
                var deathSols = new List<int>();
                for (int s = 1; s <= seeds; s++)
                {
                    var r = Play(s * 7919, bot, startStock);
                    if (r.won) won++;
                    if (r.crewLeft >= r.crewStart) both++; else if (r.crewLeft > 0) one++; else none++;
                    if (r.hadLoss)
                    {
                        if (r.firstCause == Res.Oxygen) o2++; else if (r.firstCause == Res.Water) water++; else food++;
                        if (r.crewLeft <= 0) deathSols.Add(r.deathSol);
                    }
                }
                deathSols.Sort();
                string med = deathSols.Count > 0 ? deathSols[deathSols.Count / 2].ToString() : "-";
                sb.AppendLine(string.Format("{0,-6} survive {1,5:0.0}% | all crew {2,5:0.0}% | lost one {3,5:0.0}% | wiped out {4,5:0.0}% | first loss from O2/water/food {5}/{6}/{7} | median wipe-out sol {8}",
                    bot, 100f * won / seeds, 100f * both / seeds, 100f * one / seeds, 100f * none / seeds, o2, water, food, med));
            }
            return sb.ToString();
        }

        /// <summary>Acceptance check used by the editor menu and the console harness.</summary>
        public static string Verdict(int seeds, float[] startStock)
        {
            float Rate(Bot b, Func<Result, bool> f) { int n = 0; for (int s = 1; s <= seeds; s++) if (f(Play(s * 7919, b, startStock))) n++; return 100f * n / seeds; }
            float idleLose = Rate(Bot.Idle, r => !r.won), naiveLose = Rate(Bot.Naive, r => !r.won), smartWin = Rate(Bot.Smart, r => r.won);
            float smartAll = Rate(Bot.Smart, r => r.crewLeft >= r.crewStart);
            bool ok = idleLose >= 99f && naiveLose >= 90f && smartWin >= 70f && smartWin <= 95f;
            return string.Format("{0}: idle loses {1:0}% (need >=99), naive loses {2:0}% (need >=90), smart wins {3:0}% (need 70-95), smart keeps full crew {4:0}%",
                ok ? "PASS" : "FAIL", idleLose, naiveLose, smartWin, smartAll);
        }

#if UNITY_EDITOR
        [MenuItem("Red Frontier/Outpost/Simulate Economy")]
        public static void RunMenu()
        {
            var start = ColonyEconomy.MissionStart;
            string text = Report(200, start, "mission rules") + Verdict(200, start) + "\n";
            Debug.Log(text);
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/economy-sim.txt", text);
        }
#endif
    }
}
