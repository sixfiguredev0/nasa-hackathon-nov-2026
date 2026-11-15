using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Mars local time for the outpost. One sol (24 h 39 min on Mars) plays in <see cref="SolSeconds"/> real seconds,
    /// split into 24 "Mars hours" (local solar time, the way rover teams count a sol). Plain C# so it can be tested.
    /// </summary>
    public class SolClock
    {
        public const float SolSeconds = 240f;          // 4 real minutes per sol (design decision, 2026-11-15)
        public const float Sunrise = 6f, Sunset = 18f;  // near the equator day and night are about equal
        public const float StartHour = 8f;              // the crew steps out in the morning of sol 1
        const float Coldest = 5.5f, Warmest = 14f;      // Mars air is coldest just before dawn, warmest early afternoon

        /// <summary>Real seconds since 00:00 on sol 1.</summary>
        public float elapsed = StartHour / 24f * SolSeconds;

        public int Sol => 1 + Mathf.FloorToInt(elapsed / SolSeconds);
        public float Hour => elapsed % SolSeconds / SolSeconds * 24f;
        /// <summary>Hours since 00:00 on sol 1 (for event start/end times).</summary>
        public float AbsHour => elapsed / SolSeconds * 24f;

        public void Tick(float dt) { if (dt > 0f) elapsed += dt; }

        public static string Format(float hour)
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 60f);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        /// <summary>0 at sunrise, 1 at sunset; below 0 or above 1 at night.</summary>
        public static float DayFraction(float hour) => (hour - Sunrise) / (Sunset - Sunrise);

        /// <summary>-1..1. Above 0 the Sun is up; 1 at noon, -1 at midnight.</summary>
        public static float SunHeight(float hour) => Mathf.Sin(DayFraction(hour) * Mathf.PI);

        /// <summary>0..1 sunlight on flat panels: full for most of the day, ramping at dawn and dusk.</summary>
        public static float Daylight(float hour) => Mathf.Clamp01(SunHeight(hour) * 2f);

        /// <summary>
        /// Air temperature (°C) on a smooth daily curve through a real sol's low and high (InSight data).
        /// Dust in the air flattens the curve: days colder, nights warmer (seen by Curiosity in the 2018 storm).
        /// </summary>
        public static float Temperature(float hour, float low, float high, float dust01)
        {
            float k;
            if (hour >= Coldest && hour <= Warmest)
                k = (1f - Mathf.Cos(Mathf.PI * (hour - Coldest) / (Warmest - Coldest))) * 0.5f;
            else
            {
                float h = hour < Coldest ? hour + 24f : hour;
                k = (1f + Mathf.Cos(Mathf.PI * (h - Warmest) / (24f - Warmest + Coldest))) * 0.5f;
            }
            float mid = (low + high) * 0.5f, amp = (high - low) * 0.5f * (1f - Mathf.Clamp01(dust01));
            return mid + amp * (2f * k - 1f);
        }

        /// <summary>0 at -20 °C or warmer, 1 at -95 °C: how hard the heaters work.</summary>
        public static float Coldness(float celsius) => Mathf.Clamp01((-20f - celsius) / 75f);
    }
}
