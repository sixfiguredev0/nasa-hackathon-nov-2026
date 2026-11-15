using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    public enum BuildingKind { Rocket, Solar, Battery, Water, Greenhouse, Habitat, Workshop, Gym }

    /// <summary>One step on a building's upgrade path. A node fully defines the building's stats while it is on that step.</summary>
    public class UpgradeNode
    {
        static readonly Amount[] None = new Amount[0];

        public string id, name, blurb;
        public int rocketTier;       // Rocket level needed to buy this step
        public int level;            // set by BuildingDef: plot = 0, first building = 1
        public Amount[] cost = None;
        public Amount[] rates = None;    // per second, negative = uses
        public Amount[] storage = None;  // adds to resource caps
        public float train;              // crew fitness gained per second while the crew trains here (Gym)
        public string[] next = new string[0];
    }

    public class BuildingDef
    {
        public readonly BuildingKind kind;
        public readonly string name, root;
        public readonly Vector2Int footprint; // lattice cells it covers when placed
        public readonly Dictionary<string, UpgradeNode> nodes = new Dictionary<string, UpgradeNode>();
        readonly Dictionary<string, string> parent = new Dictionary<string, string>();

        /// <summary>First node is the root: what Builder Mode places, at level 1.</summary>
        public BuildingDef(BuildingKind kind, string name, Vector2Int footprint, params UpgradeNode[] list)
        {
            this.kind = kind; this.name = name; this.footprint = footprint;
            root = list[0].id;
            foreach (var n in list) nodes[n.id] = n;
            foreach (var n in list)
                foreach (var c in n.next) parent[c] = n.id;
            SetLevel(root, 1);
        }

        public string Parent(string id) => parent.TryGetValue(id, out var p) ? p : null;

        void SetLevel(string id, int lv)
        {
            if (!nodes.TryGetValue(id, out var n)) { Debug.LogError(name + ": unknown upgrade id " + id); return; }
            n.level = lv;
            foreach (var c in n.next) SetLevel(c, lv + 1);
        }
    }

    /// <summary>
    /// All buildings and their upgrade paths. The Rocket starts on the Landing Pad zone; the rest are placed in Builder Mode.
    /// Balance numbers are first-pass guesses: tune here.
    /// Real art: drop a sprite named after the node id into Assets/Resources/Buildings/ (e.g. solar3a.png), pivot at the footprint centre.
    /// </summary>
    public static class BuildingCatalog
    {
        const Res E = Res.Energy, W = Res.Water, F = Res.Food, O = Res.Oxygen, M = Res.Metal;

        static Amount[] A(params (Res r, float v)[] list)
        {
            var a = new Amount[list.Length];
            for (int i = 0; i < list.Length; i++) a[i] = new Amount(list[i].r, list[i].v);
            return a;
        }

        static Amount[] All(float v) => A((E, v), (W, v), (F, v), (O, v), (M, v));


        public static BuildingDef Get(BuildingKind k) => Defs.Find(d => d.kind == k);

        public static readonly List<BuildingDef> Defs = new List<BuildingDef>
        {
            new BuildingDef(BuildingKind.Rocket, "Rocket", new Vector2Int(4, 4),
                new UpgradeNode { id = "rocket1", name = "Lander", blurb = "Your crew's ride from Earth and the heart of the base. It stores supplies and gets cargo (Metal) from Earth.",
                    rates = A((M, 0.4f)), storage = All(100), next = new[] { "rocket2" } },
                new UpgradeNode { id = "rocket2", name = "Supply Rocket", blurb = "A bigger rocket: more cargo and more storage. Unlocks level 2 upgrades.",
                    cost = A((M, 60), (F, 20)), rates = A((M, 1f)), storage = All(200), next = new[] { "rocket3" } },
                new UpgradeNode { id = "rocket3", name = "Starship Hub", blurb = "A giant reusable ship lands here now. Unlocks level 3 upgrades.",
                    cost = A((M, 150), (E, 80), (F, 40), (O, 40)), rates = A((M, 2f)), storage = All(400) }),

            new BuildingDef(BuildingKind.Solar, "Solar Arrays", new Vector2Int(4, 2),
                new UpgradeNode { id = "solar1", name = "Solar Panels", blurb = "Panels turn sunlight into electricity. Mars gets about half the sunlight Earth does.",
                    cost = A((M, 20)), rates = A((E, 2f)), next = new[] { "solar2" } },
                new UpgradeNode { id = "solar2", name = "Solar Field", rocketTier = 2, blurb = "More panels, more power.",
                    cost = A((M, 45)), rates = A((E, 4.5f)), next = new[] { "solar3a", "solar3b" } },
                new UpgradeNode { id = "solar3a", name = "Sun Trackers", rocketTier = 3, blurb = "Panels turn to face the Sun all day, like sunflowers.",
                    cost = A((M, 100), (E, 30)), rates = A((E, 8f)) },
                new UpgradeNode { id = "solar3b", name = "Dust Wipers", rocketTier = 3, blurb = "Mars dust covers panels and blocks light. Little brushes keep them clean.",
                    cost = A((M, 70), (W, 20)), rates = A((E, 7f)) }),

            new BuildingDef(BuildingKind.Battery, "Battery Storage", new Vector2Int(2, 2),
                new UpgradeNode { id = "bat1", name = "Battery Pack", blurb = "Saves power for the night. A Mars day (a sol) lasts 24 hours 39 minutes.",
                    cost = A((M, 15)), storage = A((E, 100)), next = new[] { "bat2" } },
                new UpgradeNode { id = "bat2", name = "Battery Bank", rocketTier = 2, blurb = "Lots of batteries wired together.",
                    cost = A((M, 40)), storage = A((E, 250)), next = new[] { "bat3a", "bat3b" } },
                new UpgradeNode { id = "bat3a", name = "Mega Battery", rocketTier = 3, blurb = "Stores enough power for long dust storms.",
                    cost = A((M, 90)), storage = A((E, 600)) },
                new UpgradeNode { id = "bat3b", name = "Fuel Cell", rocketTier = 3, blurb = "Uses stored oxygen to make extra power, day or night.",
                    cost = A((M, 70), (O, 30)), storage = A((E, 250)), rates = A((E, 2.5f), (O, -0.4f)) }),

            new BuildingDef(BuildingKind.Water, "Water Processing", new Vector2Int(3, 2),
                new UpgradeNode { id = "water1", name = "Ice Drill", blurb = "There is frozen water under the Martian ground. Drill it and melt it!",
                    cost = A((M, 25)), rates = A((E, -1f), (W, 1f)), next = new[] { "water2" } },
                new UpgradeNode { id = "water2", name = "Ice Melter", rocketTier = 2, blurb = "Melts more ice at once.",
                    cost = A((M, 50)), rates = A((E, -2f), (W, 2.5f)), next = new[] { "water3a", "water3b" } },
                new UpgradeNode { id = "water3a", name = "Deep Ice Drill", rocketTier = 3, blurb = "Reaches big ice layers deep underground.",
                    cost = A((M, 110)), rates = A((E, -3f), (W, 5f)) },
                new UpgradeNode { id = "water3b", name = "Water Recycler", rocketTier = 3, blurb = "Cleans used water so none is wasted, like on the Space Station.",
                    cost = A((M, 80)), rates = A((E, -1.5f), (W, 3.5f)) }),

            new BuildingDef(BuildingKind.Greenhouse, "Greenhouse", new Vector2Int(4, 3),
                new UpgradeNode { id = "green1", name = "Greenhouse", blurb = "Plants grow food and make oxygen. The clear dome keeps them warm.",
                    cost = A((M, 30), (W, 10)), rates = A((E, -1f), (W, -0.5f), (F, 1f), (O, 0.3f)), next = new[] { "green2" } },
                new UpgradeNode { id = "green2", name = "Hydroponics", rocketTier = 2, blurb = "Plants grow in water instead of soil.",
                    cost = A((M, 60), (W, 20)), rates = A((E, -1.5f), (W, -1f), (F, 2.5f), (O, 0.6f)), next = new[] { "green3a", "green3b" } },
                new UpgradeNode { id = "green3a", name = "Potato Farm", rocketTier = 3, blurb = "Potatoes are tough and filling: great Mars food.",
                    cost = A((M, 110), (W, 30)), rates = A((E, -2f), (W, -1.5f), (F, 5f), (O, 0.8f)) },
                new UpgradeNode { id = "green3b", name = "Algae Tanks", rocketTier = 3, blurb = "Tiny green algae make lots of oxygen, fast.",
                    cost = A((M, 100), (W, 40)), rates = A((E, -2f), (W, -1.5f), (F, 2f), (O, 3f)) }),

            new BuildingDef(BuildingKind.Habitat, "Main Habitat", new Vector2Int(4, 3),
                new UpgradeNode { id = "hab1", name = "Habitat Dome", blurb = "Home for the crew. Its MOXIE machine makes oxygen from Martian air, like the one on the Perseverance rover.",
                    cost = A((M, 40), (F, 10)), rates = A((E, -1f), (O, 0.8f)), storage = A((W, 100), (F, 100), (O, 100)), next = new[] { "hab2" } },
                new UpgradeNode { id = "hab2", name = "Big Habitat", rocketTier = 2, blurb = "Room for more crew and more supplies.",
                    cost = A((M, 80), (F, 30), (O, 20)), rates = A((E, -2f), (O, 2f)), storage = A((W, 250), (F, 250), (O, 250)), next = new[] { "hab3a", "hab3b" } },
                new UpgradeNode { id = "hab3a", name = "Lava Tube Base", rocketTier = 3, blurb = "Living inside a cave shields the crew from space radiation.",
                    cost = A((M, 150), (F, 50)), rates = A((E, -2f), (O, 2f)), storage = A((W, 600), (F, 600), (O, 600)) },
                new UpgradeNode { id = "hab3b", name = "Oxygen Factory", rocketTier = 3, blurb = "Big MOXIE machines make lots of oxygen.",
                    cost = A((M, 130), (W, 40)), rates = A((E, -4f), (O, 5f)), storage = A((W, 250), (F, 250), (O, 250)) }),

            new BuildingDef(BuildingKind.Workshop, "ISRU", new Vector2Int(3, 3),
                new UpgradeNode { id = "shop1", name = "ISRU", blurb = "Turns Martian rock (regolith) into Metal for building.",
                    cost = A((M, 30)), rates = A((E, -1f), (M, 0.8f)), next = new[] { "shop2" } },
                new UpgradeNode { id = "shop2", name = "Smelter", rocketTier = 2, blurb = "Melts rock to get much more Metal.",
                    cost = A((M, 60), (E, 30)), rates = A((E, -2f), (M, 2f)), next = new[] { "shop3a", "shop3b" } },
                new UpgradeNode { id = "shop3a", name = "3D Print Lab", rocketTier = 3, blurb = "Prints building parts straight from Mars dust.",
                    cost = A((M, 120), (E, 60)), rates = A((E, -3f), (M, 4.5f)) },
                new UpgradeNode { id = "shop3b", name = "Rover Miners", rocketTier = 3, blurb = "Robot rovers dig up rocks and find a little ice too.",
                    cost = A((M, 100), (W, 20)), rates = A((E, -2.5f), (M, 3.5f), (W, 0.5f)) }),

            new BuildingDef(BuildingKind.Gym, "Gym", new Vector2Int(3, 2),
                new UpgradeNode { id = "gym1", name = "Exercise Room", blurb = "Mars gravity is only 38% of Earth's, so muscles and bones slowly get weaker. Idle crew come here to train and stay fit.",
                    cost = A((M, 35), (E, 10)), rates = A((E, -1f)), train = 1f / 40f, next = new[] { "gym2" } },
                new UpgradeNode { id = "gym2", name = "Treadmill & Bike", rocketTier = 2, blurb = "Space Station astronauts strap into a treadmill and bike for about 2 hours every day.",
                    cost = A((M, 60), (E, 20)), rates = A((E, -1.5f)), train = 1f / 25f, next = new[] { "gym3a", "gym3b" } },
                new UpgradeNode { id = "gym3a", name = "Resistance Rig", rocketTier = 3, blurb = "Vacuum pistons let the crew lift heavy weights, like the ARED machine on the Space Station.",
                    cost = A((M, 110), (E, 40)), rates = A((E, -2.5f)), train = 1f / 14f },
                new UpgradeNode { id = "gym3b", name = "Swim Dome", rocketTier = 3, blurb = "Swimming is gentle on bones. Costs water instead of lots of power.",
                    cost = A((M, 90), (W, 40)), rates = A((E, -1f), (W, -0.4f)), train = 1f / 18f }),
        };
    }
}
