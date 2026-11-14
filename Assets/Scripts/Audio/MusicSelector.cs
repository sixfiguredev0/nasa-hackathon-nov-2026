using System;

namespace RedFrontier.Audio
{
    /// <summary>
    /// Picks the next gameplay movement and the silence before it. Never repeats the last
    /// <see cref="avoidRecent"/> movements, so long sessions cycle through the whole suite.
    /// Uses its own System.Random so audio never touches gameplay RNG.
    /// </summary>
    public sealed class MusicSelector
    {
        readonly string[] pool;
        readonly int avoidRecent;
        readonly string[] recent;
        int recentCount;
        readonly Random rng;

        public MusicSelector(string[] pool, int seed, int avoidRecent = 2)
        {
            this.pool = pool;
            this.avoidRecent = Math.Max(0, Math.Min(avoidRecent, pool.Length - 1));
            recent = new string[Math.Max(1, this.avoidRecent)];
            rng = new Random(seed);
        }

        public string Next()
        {
            int candidates = 0;
            foreach (var id in pool) if (!Recent(id)) candidates++;
            int pick = rng.Next(candidates);
            foreach (var id in pool)
            {
                if (Recent(id)) continue;
                if (pick-- == 0) { Remember(id); return id; }
            }
            return pool[0];
        }

        /// <summary>Silence between movements: usually short, sometimes a longer breath.</summary>
        public float RestSeconds() => rng.NextDouble() < 0.3 ? Range(25f, 50f) : Range(8f, 20f);

        public float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        bool Recent(string id)
        {
            for (int i = 0; i < recentCount; i++) if (recent[i] == id) return true;
            return false;
        }

        void Remember(string id)
        {
            if (avoidRecent == 0) return;
            for (int i = Math.Min(recentCount, recent.Length - 1); i > 0; i--) recent[i] = recent[i - 1];
            recent[0] = id;
            recentCount = Math.Min(recentCount + 1, recent.Length);
        }
    }
}
