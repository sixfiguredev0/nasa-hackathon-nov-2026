using RedFrontier.Mars;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>Visual for one building: swaps sprite when its upgrade node changes, tints red while stalled.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Building : MonoBehaviour
    {
        static readonly Color StalledTint = new Color(1f, 0.55f, 0.55f);

        public BuildingState state;
        public Vector2Int footprint;

        SpriteRenderer sr;
        string shownNode;

        public SpriteRenderer Renderer => sr;

        /// <summary>World point above the sprite's top centre, for UI anchoring.</summary>
        public Vector3 TopCenter => new Vector3(sr.bounds.center.x, sr.bounds.max.y, 0f);

        public void Init(BuildingState s, Vector2Int fp)
        {
            state = s;
            footprint = fp;
            sr = GetComponent<SpriteRenderer>();
            Refresh();
        }

        void Update()
        {
            if (state == null) return;
            if (state.nodeId != shownNode) Refresh();
            sr.color = state.stalled ? StalledTint : Color.white;
        }

        void Refresh()
        {
            shownNode = state.nodeId;
            sr.sprite = BuildingArt.Get(state.def, state.Node, footprint);
            sr.sortingOrder = MarsMapGenerator.SortingOrderFor(transform.position.y);
        }
    }
}
