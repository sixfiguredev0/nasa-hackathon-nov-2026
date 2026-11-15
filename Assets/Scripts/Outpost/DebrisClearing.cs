using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>One piece of crash wreckage lying around the split rocket, spawned by <see cref="OpeningCutscene"/>.</summary>
    internal sealed class DebrisPiece
    {
        public SpriteRenderer sprite, shadow;
        public Vector3 at;          // where it lies on the ground
        public bool required;       // counts toward the "clear the debris" objective (the big plates and fins)
        public bool cleared, queued;
        public Action onClear;      // stops the fire or smoke it was feeding

        internal void Clear()
        {
            if (cleared) return;
            cleared = true;
            if (sprite != null) UnityEngine.Object.Destroy(sprite.gameObject);
            if (shadow != null) UnityEngine.Object.Destroy(shadow.gameObject);
            if (onClear != null) onClear();
            DustBit.Burst(at);
        }
    }

    /// <summary>
    /// The first tutorial mission: click wreckage and the crew sweeps it and everything near it away.
    /// <see cref="MissionObjective"/> waits on <see cref="Remaining"/>.
    /// </summary>
    internal sealed class DebrisClearing : MonoBehaviour
    {
        const float SweepRadius = 4.5f, ClickPadding = .45f;
        static readonly List<DebrisPiece> Pieces = new List<DebrisPiece>();

        /// <summary>Wreckage the objective still needs gone.</summary>
        internal static int Remaining
        {
            get
            {
                int n = 0;
                foreach (var p in Pieces)
                    if (p.required && !p.cleared) n++;
                return n;
            }
        }

        internal static void Reset() => Pieces.Clear(); // statics survive scene loads when domain reload is off
        internal static void Register(DebrisPiece piece) => Pieces.Add(piece);

        /// <summary>Removes all wreckage at once (skip paths and self tests).</summary>
        internal static void ClearAllNow()
        {
            foreach (var p in Pieces) p.Clear();
        }

        Colony colony;
        MissionObjective objective;
        Camera cam;

        void Start()
        {
            colony = GetComponent<Colony>();
            objective = GetComponent<MissionObjective>();
            cam = Camera.main;
        }

        void Update()
        {
            if (!AnyLeft()) return;
            // Remaining pieces pulse while they are the goal, so the player can see what to click; queued ones go green,
            // and the one under the cursor lights up.
            bool pulse = objective != null && objective.Stage == MissionObjective.Step.Clear;
            float k = pulse ? .5f + .5f * Mathf.Sin(Time.time * 5f) : 0f;
            var idle = Color.Lerp(Color.white, new Color(1f, .93f, .45f), k);
            var hover = cam != null && InputAllowed() ? PieceAt(Input.mousePosition) : null;
            foreach (var p in Pieces)
                if (!p.cleared && p.sprite != null)
                    p.sprite.color = p.queued ? new Color(.62f, 1f, .72f) : p == hover ? new Color(1f, 1f, .8f) : idle;

            if (Input.GetMouseButtonDown(0) && hover != null) Sweep(hover);
        }

        // Clicks only count in the open world: not over a panel, a blueprint being placed, a pause or a cutscene.
        bool InputAllowed()
        {
            if (colony == null || !colony.Ready || Time.timeScale <= 0f || MissionPauseMenu.BlocksGameplayInput || StoryArc.Cinematic) return false;
            var gui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return !(MissionGuide.PointerOverUI(gui) || BuilderMode.PointerOverUI(gui) || BuilderMode.Placing);
        }

        /// <summary>The nearest piece whose (padded) sprite contains the screen point, or null.</summary>
        internal DebrisPiece PieceAt(Vector3 screen)
        {
            Vector3 world = cam.ScreenToWorldPoint(screen);
            DebrisPiece hit = null;
            float best = float.MaxValue;
            foreach (var p in Pieces)
            {
                if (p.cleared || p.sprite == null) continue;
                var b = p.sprite.bounds;
                b.Expand(ClickPadding * 2f);
                var flat = new Vector3(world.x, world.y, b.center.z);
                float d = (flat - b.center).sqrMagnitude;
                if (b.Contains(flat) && d < best) { hit = p; best = d; }
            }
            return hit;
        }

        static bool AnyLeft()
        {
            foreach (var p in Pieces)
                if (!p.cleared) return true;
            return false;
        }

        /// <summary>Queues the clicked piece and everything within <see cref="SweepRadius"/> for the crew.</summary>
        void Sweep(DebrisPiece centre)
        {
            var batch = new List<DebrisPiece>();
            foreach (var p in Pieces)
                if (!p.cleared && !p.queued && Vector3.Distance(p.at, centre.at) <= SweepRadius) batch.Add(p);
            if (batch.Count == 0) return;
            var crew = Crew.Instance;
            if (crew == null || !crew.RequestClear(batch))
                foreach (var p in batch) p.Clear(); // nobody to send: just tidy up
        }
    }

    /// <summary>A speck of dust kicked up when wreckage is swept away.</summary>
    internal sealed class DustBit : MonoBehaviour
    {
        static Sprite pixel;
        SpriteRenderer sr;
        Vector3 velocity;
        float age, life;

        internal static void Burst(Vector3 at)
        {
            if (pixel == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Dust pixel" };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply(false, true);
                pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 16f);
            }
            for (int i = 0; i < 9; i++)
            {
                var go = new GameObject("Dust");
                go.transform.position = at + new Vector3(UnityEngine.Random.Range(-.4f, .4f), UnityEngine.Random.Range(0f, .3f), 0f);
                var bit = go.AddComponent<DustBit>();
                bit.sr = go.AddComponent<SpriteRenderer>();
                bit.sr.sprite = pixel;
                bit.sr.sortingOrder = 20000;
                bit.velocity = new Vector3(UnityEngine.Random.Range(-1.4f, 1.4f), UnityEngine.Random.Range(.4f, 1.6f), 0f);
                bit.life = UnityEngine.Random.Range(.35f, .7f);
                go.transform.localScale = Vector3.one * UnityEngine.Random.Range(3f, 5f);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            transform.position += velocity * Time.deltaTime;
            velocity.y -= 2f * Time.deltaTime;
            float k = age / life;
            sr.color = Color.Lerp(new Color32(232, 178, 128, 235), new Color32(214, 150, 108, 0), k);
        }
    }
}
