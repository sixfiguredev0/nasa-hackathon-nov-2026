using System.Collections;
using RedFrontier.Audio;
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
        const float LandingSeconds = 1.8f;

        public BuildingState state;
        public Vector2Int footprint;
        public RectInt cells; // lattice cells it covers

        SpriteRenderer sr;
        string shownNode;
        Sprite[] frames;
        float animStart;
        bool awaitingCrew, landing;

        public SpriteRenderer Renderer => sr;
        public bool UnderConstruction { get; private set; }

        /// <summary>World point above the sprite's top centre, for UI anchoring.</summary>
        public Vector3 TopCenter => new Vector3(sr.bounds.center.x, sr.bounds.max.y, 0f);
        public Vector3 TopRight => new Vector3(sr.bounds.max.x, sr.bounds.max.y, 0f);

        /// <summary>True while a multi-frame sheet is still playing (the rocket landing).</summary>
        public bool Animating => landing || (frames != null && frames.Length > 1 && Time.time - animStart < frames.Length * FrameSeconds);

        public void Init(BuildingState s, RectInt c, bool construct = false)
        {
            state = s;
            cells = c;
            footprint = c.size;
            sr = GetComponent<SpriteRenderer>();
            Refresh();
            if (construct) { UnderConstruction = true; shownNode = null; } // Update asks the crew to come build it
        }

        internal IEnumerator LandFromAbove(Camera camera)
        {
            Vector3 touchdown = transform.position;
            if (!FrontierUi.ReduceMotion && camera != null)
            {
                float depth = camera.WorldToViewportPoint(touchdown).z;
                float viewTop = camera.ViewportToWorldPoint(new Vector3(0.5f, 1f, depth)).y;
                float distance = Mathf.Max(0f, viewTop - sr.bounds.min.y) + 0.5f;
                landing = true;
                GameAudio.Play(Sfx.RocketDescent); // 1.9 s roar, timed to the 1.8 s ease-out
                transform.position = touchdown + Vector3.up * distance;

                for (float elapsed = 0f; elapsed < LandingSeconds; elapsed += Time.deltaTime)
                {
                    float remaining = 1f - elapsed / LandingSeconds;
                    float offset = Mathf.Round(distance * remaining * remaining * MarsMapGenerator.Ppu) / MarsMapGenerator.Ppu;
                    transform.position = touchdown + Vector3.up * offset;
                    yield return null;
                }
            }

            transform.position = touchdown;
            landing = false;
            GameAudio.Play(Sfx.RocketTouchdown); // also the only landing cue when Reduce Motion skips the descent
            animStart = Time.time - (frames.Length + 1) * FrameSeconds;
            sr.sprite = frames[frames.Length - 1];
        }

        void Update()
        {
            if (state == null) return;
            if (state.nodeId != shownNode && !awaitingCrew)
            {
                if (Crew.Instance != null && Crew.Instance.RequestBuild(this)) awaitingCrew = true;
                else FinishBuild();
            }
            // Sheets play once and hold the last frame.
            if (frames.Length > 1)
                sr.sprite = frames[Mathf.Min(frames.Length - 1, (int)((Time.time - animStart) / FrameSeconds))];
            sr.color = UnderConstruction ? BlueprintTint : state.stalled ? StalledTint : Color.white;
        }

        /// <summary>Called by the crew when they finish building: shows the current upgrade node.</summary>
        public void FinishBuild()
        {
            awaitingCrew = false;
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
            sr.sortingOrder = MarsMapGenerator.SortingOrderFor(transform.position.y);
        }
    }
}
