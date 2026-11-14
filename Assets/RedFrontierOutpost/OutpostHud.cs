using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    public sealed class OutpostHud : MonoBehaviour
    {
        public enum Resource { Energy, Water, Food, Oxygen, Metal }

        [SerializeField] private Text[] slots;

        // Call this when the game's existing resource state changes.
        public void SetResource(Resource resource, float amount, float capacity, float? netPerSecond = null)
        {
            string stock = $"<b>{Format(amount)}</b>";
            if (capacity > 0)
                stock += $"<size=12><color=#bfb0a9>/{Format(capacity)}</color></size>";
            if (netPerSecond.HasValue)
            {
                string rate = netPerSecond.Value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
                string color = netPerSecond > 0 ? "#b6dea0" : netPerSecond < 0 ? "#ffae9f" : "#bfb0a9";
                stock += $" <size=12><color={color}>{rate}/s</color></size>";
            }
            slots[(int)resource].text = stock;
        }

        private static string Format(float number) => number.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
