using System.Collections.Generic;

namespace RedFrontier.Audio
{
    /// <summary>Per-cue cooldown bookkeeping on unscaled time. Voice limits are checked against the live source pool.</summary>
    public sealed class CueGate
    {
        readonly Dictionary<string, float> lastStart = new Dictionary<string, float>();

        public bool Allow(CueDef def, float now, int playingVoices)
        {
            if (playingVoices >= def.MaxVoices) return false;
            if (lastStart.TryGetValue(def.Id, out var t) && now - t < def.Cooldown) return false;
            lastStart[def.Id] = now;
            return true;
        }

        public void Clear() => lastStart.Clear();
    }
}
