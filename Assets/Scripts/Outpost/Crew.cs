using System.Collections;
using System.Collections.Generic;
using RedFrontier.Audio;
using RedFrontier.Mars;
using UnityEngine;

namespace RedFrontier.Outpost
{
    public enum Astronaut { Female, Male }

    /// <summary>
    /// The astronauts. They wait inside the rocket; after an upgrade is bought they come out of the door,
    /// climb down the ladder, walk to the building, build it, then walk back and go inside again.
    /// Jobs bought while the crew is out are queued and done one after another.
    /// With nothing to build and fitness below <see cref="trainBelow"/>, they go work out in a Gym until fit again.
    /// Walk and build speed scale with fitness (<see cref="ColonyEconomy.Efficiency"/>).
    /// </summary>
    public class Crew : MonoBehaviour
    {
        /// <summary>Which astronauts are in the crew. The crew-pick screen sets this before the outpost starts.</summary>
        public static Astronaut[] Picked = { Astronaut.Female, Astronaut.Male };

        public static Crew Instance { get; private set; }

        public float walkSpeed = 2.5f;   // world units per second
        public float buildSeconds = 3f;
        [Tooltip("Rebuilding the crashed ship into the first rocket model (endgame) takes this long")]
        public float rebuildSeconds = 12f;
        [Tooltip("Idle crew head to a Gym when fitness drops below this")]
        [Range(0f, 1f)] public float trainBelow = 0.85f;

        const float Ppu = 16f;
        // rocket landmarks in pixels from its pivot (footprint centre), measured on the landed frame of Rocket.png
        static readonly Vector2 DoorPx = new Vector2(-12, 13), LadderFootPx = new Vector2(-31, -18);
        // sheet frame for each facing, in order S, SE, E, NE, N, NW, W, SW
        static readonly int[] FemaleDirs = { 0, 1, 2, 3, 4, 5, 6, 7 }, MaleDirs = { 1, 2, 3, 4, 5, 6, 7, 8 };
        const int South = 0, North = 4;

        class Member
        {
            public SpriteRenderer sr;
            public Sprite[] dirs;
            public Sprite[] hammer; // build animation (male only); null = plain bounce
            public Sprite[] walkSide, walkDown; // walk cycles (male only): side faces east, flipped for west; null = 1 px bob
            public int facing;
            public bool nearRocket; // on the door / ladder: draw in front of the rocket
        }

        Building rocket;
        ColonyEconomy eco;
        readonly List<Member> members = new List<Member>();
        readonly List<Building> jobs = new List<Building>();
        readonly List<DebrisPiece> debris = new List<DebrisPiece>();
        const float ClearSeconds = 1.1f;
        float jobProgress;
        internal bool Sheltering { get; private set; }
        internal bool Sheltered { get; private set; }

        // Interrupt movement, not the job queue or its accumulated build time.
        internal void SetSheltering(bool value)
        {
            if (Sheltering == value) return;
            Sheltering = value;
            Sheltered = false;
            StopAllCoroutines();
            // The interrupted coroutine can no longer stop its own work loops.
            GameAudio.StopLoop(Sfx.ConstructLoop, this);
            GameAudio.StopLoop(Sfx.GymLoop, this);
            eco.training = null;
            if (isActiveAndEnabled) StartCoroutine(value ? Shelter() : Work());
        }

        IEnumerator Shelter()
        {
            yield return All((m, i) => Enter(m, i * 0.3f, RocketPoint(rocket.Wrecked ? rocket.WreckDoorPx : DoorPx),
                RocketPoint(rocket.Wrecked ? rocket.WreckFootPx : LadderFootPx)));
            Sheltered = true;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>An astronaut died: remove their sprite and let the rest carry on with the saved job queue.</summary>
        internal void LoseOne()
        {
            // eco.crew is already decremented. If a sprite sheet was missing there are fewer sprites than astronauts,
            // so only remove a sprite once there are more sprites than survivors (normally exactly one per death).
            if (members.Count == 0 || members.Count <= eco.crew) return;
            StopAllCoroutines();
            GameAudio.StopLoop(Sfx.ConstructLoop, this);
            GameAudio.StopLoop(Sfx.GymLoop, this);
            eco.training = null;
            var lost = members[members.Count - 1];
            members.RemoveAt(members.Count - 1);
            if (lost.sr != null) Destroy(lost.sr.gameObject);
            // Coroutines were cut mid-fade / mid-walk: survivors must not stay half transparent or drawn as if on the ladder.
            foreach (var m in members)
            {
                m.nearRocket = false;
                if (m.sr != null) m.sr.color = Color.white;
            }
            if (members.Count > 0 && isActiveAndEnabled) StartCoroutine(Sheltering ? Shelter() : Work());
        }

        public void Init(Building rocketBuilding)
        {
            rocket = rocketBuilding;
            eco = Colony.Instance != null ? Colony.Instance.economy : new ColonyEconomy();
            foreach (var who in Picked)
            {
                var dirs = Facings(who);
                if (dirs == null) { Debug.LogWarning("Crew: missing sprite sheet for " + who); continue; }
                var go = new GameObject("Astronaut " + who);
                go.transform.SetParent(transform, false);
                go.SetActive(false); // inside the rocket
                bool male = who == Astronaut.Male;
                members.Add(new Member
                {
                    sr = go.AddComponent<SpriteRenderer>(), dirs = dirs, hammer = male ? Hammer() : null,
                    walkSide = male ? BuildingArt.Sheet("Crew/", "AstronautWalkSide", new Vector2(16, 2)) : null,
                    walkDown = male ? BuildingArt.Sheet("Crew/", "AstronautWalkDown", new Vector2(16, 2)) : null,
                });
            }
            StartCoroutine(Work());
        }

        /// <summary>One sprite per facing (S, SE, E, NE, N, NW, W, SW), or null when the sheet is missing.</summary>
        internal static Sprite[] Facings(Astronaut who)
        {
            var sheet = BuildingArt.Sheet("Crew/", who == Astronaut.Female ? "AstronautFemale" : "AstronautMale", new Vector2(16, 2));
            if (sheet == null) return null;
            var map = who == Astronaut.Female ? FemaleDirs : MaleDirs;
            var dirs = new Sprite[8];
            for (int d = 0; d < 8; d++) dirs[d] = sheet[Mathf.Min(map[d], sheet.Length - 1)];
            return dirs;
        }

        /// <summary>Stands the first astronaut outside, where the opening left them; the work loop walks them back in.</summary>
        public void StartOutside(Vector3 at)
        {
            if (members.Count == 0) return;
            members[0].sr.gameObject.SetActive(true);
            Place(members[0], at);
        }

        /// <summary>Queue a build. False means nobody can come, so the building should just update.</summary>
        public bool RequestBuild(Building b)
        {
            if (rocket == null || members.Count == 0) return false;
            if (!jobs.Contains(b)) jobs.Add(b);
            return true;
        }

        /// <summary>Queue wreckage for sweeping. False means nobody can come.</summary>
        internal bool RequestClear(List<DebrisPiece> pieces)
        {
            if (members.Count == 0) return false;
            foreach (var p in pieces)
            {
                p.queued = true;
                debris.Add(p);
            }
            return true;
        }

        /// <summary>True while a job on this building is queued or under way.</summary>
        internal bool Busy(Building b) => b != null && jobs.Contains(b);

        IEnumerator Work()
        {
            while (true)
            {
                while (rocket.Animating) yield return null;
                Vector3 door = RocketPoint(rocket.Wrecked ? rocket.WreckDoorPx : DoorPx),
                    foot = RocketPoint(rocket.Wrecked ? rocket.WreckFootPx : LadderFootPx);

                if (jobs.Count > 0)
                {
                    var job = jobs[0];
                    yield return All((m, i) => Exit(m, i * 0.4f, door, foot, StandPoint(job, i)));
                    // unfit crew build slower: progress runs at Efficiency (50..100%)
                    GameAudio.StartLoop(Sfx.ConstructLoop, this);
                    float seconds = job.Rebuilding ? rebuildSeconds : buildSeconds;
                    while (jobProgress < seconds)
                    {
                        Build(job, jobProgress);
                        if (job.Rebuilding) job.ShowRebuild(jobProgress / seconds);
                        yield return null;
                        jobProgress += Time.deltaTime * eco.Efficiency;
                    }
                    GameAudio.StopLoop(Sfx.ConstructLoop, this);
                    jobs.RemoveAt(0);
                    jobProgress = 0f;
                    job.FinishBuild();
                    GameAudio.Play(Sfx.BuildComplete); // only here: the crew actually finished work, never on scene setup
                    continue; // straight on to the next queued job
                }

                if (debris.Count > 0)
                {
                    // Sweep the wreckage nearest the crew first, hammering it apart before it is gone.
                    var here = members.Count > 0 ? members[0].sr.transform.position : door;
                    debris.Sort((a, b) => (a.at - here).sqrMagnitude.CompareTo((b.at - here).sqrMagnitude));
                    var piece = debris[0];
                    yield return All((m, i) => Exit(m, i * 0.4f, door, foot, DebrisStand(piece, i)));
                    GameAudio.StartLoop(Sfx.ConstructLoop, this);
                    for (float w = 0f; w < ClearSeconds && jobs.Count == 0; w += Time.deltaTime * eco.Efficiency)
                    {
                        for (int i = 0; i < members.Count; i++) Strike(members[i], i, DebrisStand(piece, i), w);
                        yield return null;
                    }
                    GameAudio.StopLoop(Sfx.ConstructLoop, this);
                    if (jobs.Count == 0)
                    {
                        debris.Remove(piece);
                        piece.Clear();
                    }
                    continue; // buildings come first; otherwise on to the next piece
                }

                var gym = GymToTrainAt();
                if (gym != null)
                {
                    yield return All((m, i) => Exit(m, i * 0.4f, door, foot, StandPoint(gym, i)));
                    eco.training = gym.state;
                    GameAudio.StartLoop(Sfx.GymLoop, this);
                    for (float t = 0f; jobs.Count == 0 && eco.fitness < 0.995f && !gym.state.stalled; t += Time.deltaTime)
                    {
                        Hop(gym, t, 2.5f); // slower bounce: jumping jacks
                        yield return null;
                    }
                    GameAudio.StopLoop(Sfx.GymLoop, this);
                    eco.training = null;
                    continue;
                }

                if (members.Exists(m => m.sr.gameObject.activeSelf)) yield return All((m, i) => Enter(m, i * 0.3f, door, foot));
                yield return null;
            }
        }

        Building GymToTrainAt()
        {
            if (eco.fitness >= trainBelow || Colony.Instance == null) return null;
            return Colony.Instance.buildings.Find(b => b.state.def.kind == BuildingKind.Gym && !b.UnderConstruction && !b.state.stalled);
        }

        // Sheet "Crew/AstronautHammer" (32 px frames, 100 ms each): 0-3 turn side-on, 4-7 swing the hammer, 8 rest.
        static readonly int[] Swing = { 4, 5, 6, 7, 7, 6, 5 };
        const float HammerFrame = .1f;

        static Sprite[] Hammer() => BuildingArt.Sheet("Crew/", "AstronautHammer", new Vector2(16, 2));

        /// <summary>Crew stand in front of the building and work on it.</summary>
        void Build(Building b, float t)
        {
            for (int i = 0; i < members.Count; i++) Strike(members[i], i, StandPoint(b, i), t);
        }

        /// <summary>Hammer crew turn side-on and swing at <paramref name="stand"/>; the rest face away and bounce.</summary>
        void Strike(Member m, int i, Vector3 stand, float t)
        {
            m.facing = North;
            if (m.hammer == null || m.hammer.Length < 8)
            {
                bool hop = Mathf.Repeat(t * 4f + i * 0.5f, 1f) < 0.5f;
                Place(m, stand + (hop ? Vector3.up / Ppu : Vector3.zero));
                return;
            }
            Place(m, stand);
            float at = Mathf.Max(0f, t - i * .15f) / HammerFrame; // crew a little out of step
            m.sr.sprite = m.hammer[at < 4f ? (int)at : Swing[(int)(at - 4f) % Swing.Length]];
        }

        /// <summary>Beside a piece of wreckage, on its left so the swing lands on it.</summary>
        static Vector3 DebrisStand(DebrisPiece p, int i) => p.at + new Vector3(-1.1f - i * 1f, -.15f, 0f);

        /// <summary>Crew face the building and bounce in front of it (building or training).</summary>
        void Hop(Building b, float t, float speed)
        {
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                m.facing = North; // working on the building behind them
                bool hop = Mathf.Repeat(t * speed + i * 0.5f, 1f) < 0.5f;
                Place(m, StandPoint(b, i) + (hop ? Vector3.up / Ppu : Vector3.zero));
            }
        }

        IEnumerator Exit(Member m, float delay, Vector3 door, Vector3 foot, Vector3 stand)
        {
            if (m.sr.gameObject.activeSelf) { yield return Walk(m, stand); yield break; } // already outside (queued job)
            yield return new WaitForSeconds(delay);
            m.nearRocket = true;
            m.facing = South;
            m.sr.gameObject.SetActive(true);
            GameAudio.Play(Sfx.Hatch); // cooldown keeps it to one hatch per trip
            yield return Fade(m, door, 0f, 1f);
            yield return Walk(m, foot);
            m.nearRocket = false;
            yield return Walk(m, stand);
        }

        IEnumerator Enter(Member m, float delay, Vector3 door, Vector3 foot)
        {
            if (!m.sr.gameObject.activeSelf) yield break; // already inside
            yield return new WaitForSeconds(delay);
            yield return Walk(m, foot);
            m.nearRocket = true;
            yield return Walk(m, door);
            m.facing = North;
            GameAudio.Play(Sfx.Hatch);
            yield return Fade(m, door, 1f, 0f);
            m.sr.gameObject.SetActive(false);
            m.nearRocket = false;
        }

        IEnumerator Fade(Member m, Vector3 at, float from, float to)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                m.sr.color = new Color(1f, 1f, 1f, Mathf.Lerp(from, to, t));
                Place(m, at);
                yield return null;
            }
            m.sr.color = new Color(1f, 1f, 1f, to);
        }

        IEnumerator Walk(Member m, Vector3 to)
        {
            Vector3 p = m.sr.transform.position;
            p.z = 0f;
            float t = 0f;
            bool wasBob = false;
            int steps = 0, lastFrame = -1;
            while ((to - p).sqrMagnitude > 1e-6f)
            {
                var step = Vector3.MoveTowards(p, to, walkSpeed * eco.Efficiency * Time.deltaTime);
                m.facing = Facing(step - p, m.facing);
                p = step;
                t += Time.deltaTime;
                var cycle = WalkCycle(m, out bool flip);
                if (cycle != null)
                {
                    int f = (int)(t / WalkFrame) % cycle.Length;
                    if (f != lastFrame && (f == 0 || f == cycle.Length / 2)) GameAudio.Play(Sfx.Footstep); // two steps per cycle
                    lastFrame = f;
                    Place(m, p);
                    m.sr.sprite = cycle[f];
                    m.sr.flipX = flip;
                }
                else
                {
                    bool bob = Mathf.Repeat(t * 6f, 1f) < 0.5f; // no walk cycle for this facing, so a 1 px bob sells the steps
                    if (bob && !wasBob && (steps++ & 1) == 0) GameAudio.Play(Sfx.Footstep);
                    wasBob = bob;
                    Place(m, p + (bob ? Vector3.up / Ppu : Vector3.zero));
                }
                yield return null;
            }
            Place(m, to);
        }

        const float WalkFrame = .1f; // the walk sheets use 100 ms frames

        /// <summary>Walk frames for the current facing: front sheet heading south, side sheet east or west, none heading north.</summary>
        static Sprite[] WalkCycle(Member m, out bool flip)
        {
            flip = m.facing > North;
            if (m.facing == South) return m.walkDown;
            return m.facing == North ? null : m.walkSide;
        }

        /// <summary>Runs one coroutine per member and waits for all of them.</summary>
        IEnumerator All(System.Func<Member, int, IEnumerator> routine)
        {
            int running = members.Count;
            for (int i = 0; i < members.Count; i++) StartCoroutine(Track(routine(members[i], i), () => running--));
            while (running > 0) yield return null;
        }

        IEnumerator Track(IEnumerator r, System.Action done)
        {
            yield return r;
            done();
        }

        void Place(Member m, Vector3 p)
        {
            p = new Vector3(Mathf.Round(p.x * Ppu) / Ppu, Mathf.Round(p.y * Ppu) / Ppu, 0f); // stay on the pixel grid
            m.sr.transform.position = p;
            m.sr.sprite = m.dirs[m.facing];
            m.sr.flipX = false;
            int order = MarsMapGenerator.SortingOrderFor(p.y);
            m.sr.sortingOrder = m.nearRocket ? Mathf.Max(order, rocket.Renderer.sortingOrder + 1) : order;
        }

        Vector3 RocketPoint(Vector2 px) => rocket.transform.position + (Vector3)(px / Ppu);

        /// <summary>Just in front of the building's footprint, crew side by side.</summary>
        Vector3 StandPoint(Building b, int i)
        {
            float frontY = (b.footprint.y * 12f / 2f + 6f) / Ppu; // footprint is fp.y * 12 px tall, centred on the transform
            float x = (i - (members.Count - 1) / 2f) * 1.25f;
            return b.transform.position + new Vector3(x, -frontY, 0f);
        }

        /// <summary>8-way facing index (S, SE, E, NE, N, NW, W, SW) for a move.</summary>
        static int Facing(Vector3 v, int current)
        {
            if (v.sqrMagnitude < 1e-10f) return current;
            int sector = Mathf.RoundToInt(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg / 45f); // 0 = E, counter-clockwise
            return ((sector + 2) % 8 + 8) % 8;
        }
    }
}
