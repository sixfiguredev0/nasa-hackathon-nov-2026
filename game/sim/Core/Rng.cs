namespace RedFrontier.Core
{
    /// <summary>
    /// Small seeded generator (xorshift32). Its only state is SimState.rngState, so the sequence is
    /// identical in tests, the Editor and WebAssembly, and it is copied with the state.
    /// </summary>
    public static class Rng
    {
        public static uint SeedState(int seed)
        {
            // Murmur3 finaliser spreads nearby seeds; xorshift must never hold 0.
            uint x = unchecked((uint)seed + 0x9E3779B9u);
            x ^= x >> 16; x = unchecked(x * 0x85EBCA6Bu);
            x ^= x >> 13; x = unchecked(x * 0xC2B2AE35u);
            x ^= x >> 16;
            return x == 0 ? 0x6D2B79F5u : x;
        }

        public static uint Next(SimState s)
        {
            uint x = s.rngState;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            s.rngState = x;
            return x;
        }

        /// <summary>Uniform-enough integer in [0, maxExclusive). maxExclusive must be &gt;= 1.</summary>
        public static int Range(SimState s, int maxExclusive)
        {
            return (int)(Next(s) % (uint)maxExclusive);
        }
    }
}
