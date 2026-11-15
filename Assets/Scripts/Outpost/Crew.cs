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
            public int facing;
            public bool nearRocket; // on the door / ladder: draw in front of the rocket
        }

        Building rocket;
        ColonyEconomy eco;
        readonly List<Member> members = new List<Member>();
        readonly List<Building> jobs = new List<Building>();
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
            yield return All((m, i) => Enter(m, i * 0.3f, RocketPoint(DoorPx), RocketPoint(LadderFootPx)));
            Sheltered = true;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>An astronaut died: remove their sprite and let the rest carry on with the saved job queue.</summary>
        internal void LoseOne()
        {
            if (members.Count == 0) return;
            StopAllCoroutines();
            GameAudio.StopLoop(Sfx.ConstructLoop, this);
            GameAudio.StopLoop(Sfx.GymLoop, this);
            eco.training = null;
            var lost = members[members.Count - 1];
            members.RemoveAt(members.Count - 1);
            if (lost.sr != null) Destroy(lost.sr.gameObject);
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
                members.Add(new Member { sr = go.AddComponent<SpriteRenderer>(), dirs = dirs });
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

        IEnumerator Work()
        {
            while (true)
            {
                while (rocket.Animating) yield return null;
                Vector3 door = RocketPoint(DoorPx), foot = RocketPoint(LadderFootPx);

                if (jobs.Count > 0)
                {
                    var job = jobs[0];
                    yield return All((m, i) => Exit(m, i * 0.4f, door, foot, StandPoint(job, i)));
                    // unfit crew build slower: progress runs at Efficiency (50..100%)
                    GameAudio.StartLoop(Sfx.ConstructLoop, this);
                    while (jobProgress < buildSeconds)
                    {
                        Hop(job, jobProgress, 4f);
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
            int steps = 0;
            while ((to - p).sqrMagnitude > 1e-6f)
            {
                var step = Vector3.MoveTowards(p, to, walkSpeed * eco.Efficiency * Time.deltaTime);
                m.facing = Facing(step - p, m.facing);
                p = step;
                t += Time.deltaTime;
                bool bob = Mathf.Repeat(t * 6f, 1f) < 0.5f; // the sheets have no walk cycle, so a 1 px bob sells the steps
                if (bob && !wasBob && (steps++ & 1) == 0) GameAudio.Play(Sfx.Footstep);
                wasBob = bob;
                Place(m, p + (bob ? Vector3.up / Ppu : Vector3.zero));
                yield return null;
            }
            Place(m, to);
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
