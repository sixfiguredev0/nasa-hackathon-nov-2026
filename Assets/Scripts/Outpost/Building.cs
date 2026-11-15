using RedFrontier.Mars;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Visual for one building: swaps sprite when its upgrade node changes, tints red while stalled.
    /// After an upgrade the old look stays until the crew has walked over and built it.
    /// A freshly placed building is a see-through blueprint, and stays out of the economy, until the crew builds it.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Building : MonoBehaviour
    {
        static readonly Color StalledTint = new Color(1f, 0.55f, 0.55f), BlueprintTint = new Color(0.7f, 0.9f, 1f, 0.45f);
        const float FrameSeconds = 0.1f;

        public BuildingState state;
        public Vector2Int footprint;
        public RectInt cells; // lattice cells it covers
        public float groundY; // world y of the footprint centre on the height-0 plane: depth sorting ignores terrain lift

        SpriteRenderer sr;
        string shownNode;
        Sprite[] frames;
        float stallLevel;
        float animStart;
        bool awaitingCrew, inFlight;

        public SpriteRenderer Renderer => sr;
        public bool UnderConstruction { get; private set; }

        /// <summary>World point above the sprite's top centre, for UI anchoring.</summary>
        public Vector3 TopCenter => new Vector3(sr.bounds.center.x, sr.bounds.max.y, 0f);
        public Vector3 TopRight => new Vector3(sr.bounds.max.x, sr.bounds.max.y, 0f);

        /// <summary>True while the rocket is coming down or a multi-frame sheet is still playing.</summary>
        public bool Animating => inFlight || (frames != null && frames.Length > 1 && Time.time - animStart < frames.Length * FrameSeconds);

        public void Init(BuildingState s, RectInt c, bool construct = false)
        {
            state = s;
            cells = c;
            footprint = c.size;
            sr = GetComponent<SpriteRenderer>();
            Refresh();
            if (construct) { UnderConstruction = true; shownNode = null; } // Update asks the crew to come build it
        }

        /// <summary>While set the sheet holds its first frame (the rocket coming down); clearing it shows the landed frame.</summary>
        internal bool InFlight
        {
            get => inFlight;
            set
            {
                inFlight = value;
                if (value) return;
                animStart = Time.time - (frames.Length + 1) * FrameSeconds;
                sr.sprite = frames[frames.Length - 1];
            }
        }

        Sprite wreck;
        string wreckNode;

        /// <summary>True while the crashed first rocket lies in pieces; its next upgrade replaces it.</summary>
        public bool Wrecked => wreck != null;
        /// <summary>Where the crew climb in and out of the wreck, in pixels from the pivot.</summary>
        public Vector2 WreckDoorPx { get; private set; }
        public Vector2 WreckFootPx { get; private set; }

        /// <summary>Shows a wreck sprite in place of the current node's art until the node changes.</summary>
        internal void ShowWreck(Sprite sprite, Vector2 doorPx, Vector2 footPx)
        {
            wreck = sprite;
            wreckNode = state.nodeId;
            WreckDoorPx = doorPx;
            WreckFootPx = footPx;
            sr.sprite = sprite;
        }

        const int RebuildSteps = 24;
        float rebuild = -1f;
        Sprite[] rebuildSprites;
        SpriteRenderer wreckRemains;

        /// <summary>True when the wreck's upgrade is bought: the crew's next job here rebuilds the ship.</summary>
        internal bool Rebuilding => wreck != null && state.nodeId != wreckNode;
        /// <summary>0..1 while the crew rebuilds the wreck, -1 otherwise.</summary>
        internal float RebuildProgress => rebuild;

        /// <summary>Crew rebuilding the wreck: the new rocket rises from its base row by row while the wreckage fades out.</summary>
        internal void ShowRebuild(float progress)
        {
            if (!Rebuilding) return;
            var target = BuildingArt.Get(state.def, state.Node, footprint);
            var full = target[target.Length - 1]; // the landed frame
            if (rebuildSprites == null)
            {
                rebuildSprites = new Sprite[RebuildSteps];
                wreckRemains = new GameObject("Wreck remains").AddComponent<SpriteRenderer>();
                wreckRemains.transform.SetParent(transform, false);
                wreckRemains.sprite = wreck;
            }
            int i = Mathf.Clamp((int)(progress * RebuildSteps), 0, RebuildSteps - 1);
            if (rebuildSprites[i] == null)
            {
                var r = full.rect;
                float h = Mathf.Max(1f, Mathf.Round(r.height * (i + 1f) / RebuildSteps));
                rebuildSprites[i] = Sprite.Create(full.texture, new Rect(r.x, r.y, r.width, h),
                    new Vector2(full.pivot.x / r.width, full.pivot.y / h), full.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            rebuild = Mathf.Clamp01(progress);
            sr.sprite = rebuildSprites[i];
            wreckRemains.color = new Color(1f, 1f, 1f, 1f - rebuild);
            wreckRemains.sortingOrder = sr.sortingOrder - 1;
        }

        void EndRebuild()
        {
            if (rebuildSprites == null) return;
            foreach (var s in rebuildSprites) if (s != null) Destroy(s);
            rebuildSprites = null;
            Destroy(wreckRemains.gameObject);
            rebuild = -1f;
        }

        void Update()
        {
            if (state == null) return;
            if (state.nodeId != shownNode && !awaitingCrew)
            {
                if (Crew.Instance != null && Crew.Instance.RequestBuild(this)) awaitingCrew = true;
                else FinishBuild();
            }
            if (wreck != null && shownNode != wreckNode) wreck = null; // upgraded and built: the new rocket stands
            // Sheets play once and hold the last frame.
            if (rebuild >= 0f) { } // ShowRebuild owns the sprite until the crew finishes
            else if (wreck != null)
                sr.sprite = wreck;
            else if (frames.Length > 1)
                sr.sprite = inFlight ? frames[0] : frames[Mathf.Min(frames.Length - 1, (int)((Time.time - animStart) / FrameSeconds))];
            // Ease the stalled tint: near-empty Energy stalls a building every other tick, which flickered red.
            stallLevel = Mathf.MoveTowards(stallLevel, state.stalled ? 1f : 0f, Time.deltaTime * 1.5f);
            sr.color = UnderConstruction ? BlueprintTint : Color.Lerp(Color.white, StalledTint, stallLevel);
        }

        /// <summary>Called by the crew when they finish building: shows the current upgrade node.</summary>
        public void FinishBuild()
        {
            awaitingCrew = false;
            EndRebuild();
            if (UnderConstruction)
            {
                UnderConstruction = false;
                if (Colony.Instance != null) Colony.Instance.Commission(state);
            }
            Refresh();
        }

        void Refresh()
        {
            shownNode = state.nodeId;
            var next = BuildingArt.Get(state.def, state.Node, footprint);
            if (frames == null || frames[0].texture != next[0].texture) animStart = Time.time; // same sheet (rocket tiers): don't replay
            frames = next;
            sr.sprite = Animating ? frames[0] : frames[frames.Length - 1];
            sr.sortingOrder = MarsMapGenerator.SortingOrderFor(groundY);
        }
    }
}
