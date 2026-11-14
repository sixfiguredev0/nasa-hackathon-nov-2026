using UnityEngine;

namespace RedFrontier.Mars
{
    public sealed class SeededRandom
    {
        uint s;

        public SeededRandom(int seed)
        {
            s = (uint)seed * 2654435761u + 0x9E3779B9u;
            Next();
            Next();
        }

        public uint Next()
        {
            s += 0x9E3779B9u;
            uint z = s;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            return z ^ (z >> 16);
        }

        public float Value() => (Next() >> 8) * (1f / 16777216f);
        public float Range(float a, float b) => a + (b - a) * Value();
        public int Range(int a, int b) => a + (int)(Next() % (uint)(b - a));
        public bool Chance(float p) => Value() < p;
    }

    public static class MarsMath
    {
        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    public sealed class ValueNoise
    {
        readonly int seed;

        public ValueNoise(int seed) { this.seed = seed; }

        float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) * (1f / 16777216f);
        }

        public float Sample(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Mathf.Lerp(Hash(x0, y0), Hash(x0 + 1, y0), fx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1), Hash(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        public float Fbm(float x, float y, int octaves = 3)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x, y) * amp;
                norm += amp;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f + 9.7f;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
