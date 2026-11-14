using RedFrontier.Mars;
using RedFrontier.Runtime;
using UnityEngine;

namespace RedFrontier.Integration
{
    /// <summary>
    /// Integration adapter in the default assembly (RedFrontier.Runtime cannot reference the map's Assembly-CSharp).
    /// Blocks map panning while the Functions panel is open and restores the previous allowPan when it closes or this is disabled.
    /// </summary>
    public sealed class MapPanelBridge : MonoBehaviour
    {
        public DebugHud panel;
        public MarsEnvironmentController map;

        bool blocking, savedAllowPan;

        void Update() { Apply(panel != null && panel.BlocksMapInput); }
        void OnDisable() { Apply(false); }

        void Apply(bool block)
        {
            if (map == null || block == blocking) return;
            if (block) { savedAllowPan = map.allowPan; map.allowPan = false; }
            else map.allowPan = savedAllowPan;
            blocking = block;
        }
    }
}
