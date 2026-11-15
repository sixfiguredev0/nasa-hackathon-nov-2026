using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    internal sealed class ResourceTopBar
    {
        private enum Gauge { Single, Food, Water }

        private static readonly Color32 Edge = FrontierUi.PanelLight;
        private static readonly Color32 Secondary = OutpostGUI.InstrumentSecondary;
        private static readonly string SecondaryHex = ColorUtility.ToHtmlStringRGB(Secondary);
        private static readonly Color32 Fill = OutpostGUI.InstrumentAccent;
        private static readonly Color32 Warning = OutpostGUI.InstrumentWarning;
        private static readonly Res[] VisibleResources = { Res.Energy, Res.Oxygen, Res.Food, Res.Water, Res.Metal };

        private const float DialWidth = 132f, ResourcesWidth = 232f, CardHeight = 72f, Gap = 8f;
        private const float DialRowWidth = DialWidth * 2f + Gap;
        private const float SuppliesRowWidth = ResourcesWidth + Gap + DialWidth;
        private const float WideWidth = DialRowWidth + Gap + SuppliesRowWidth;
        private const int GaugeSteps = 40, TileSize = 48, AtlasColumns = 8;
        private const int TilesPerGauge = GaugeSteps + 1;
        private const int AtlasRows = (3 * TilesPerGauge + AtlasColumns - 1) / AtlasColumns;

        private readonly Texture2D[] icons = new Texture2D[ColonyEconomy.ResCount];
        private readonly float[] displayedStock = new float[ColonyEconomy.ResCount];
        private readonly float[] displayedFill = new float[ColonyEconomy.ResCount];
        private static Texture2D gaugeAtlas;
        private Texture2D suppliesIcon;
        private GUIStyle text;
        private int lastFrame = -1;

        public Rect Bounds { get; private set; }

        public void Draw(ColonyEconomy economy)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label)
                {
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(),
                    richText = true
                };
                foreach (Res resource in VisibleResources)
                    icons[(int)resource] = Resources.Load<Texture2D>("ResourceIcons/" + resource);
                suppliesIcon = Resources.Load<Texture2D>("OutpostHud/Instrument Supplies");
                if (gaugeAtlas == null) gaugeAtlas = CreateGaugeAtlas();
            }

            UpdateReadouts(economy);
            Bounds = Layout(Screen.width, Screen.height, out bool stacked, out float scale);

            float left = Bounds.x;
            float top = Bounds.y;
            float dialX = stacked ? left + (SuppliesRowWidth - DialRowWidth) * .5f * scale : left;
            DrawDial(new Rect(dialX, top, DialWidth * scale, CardHeight * scale), Res.Energy, economy, scale);
            DrawDial(new Rect(dialX + (DialWidth + Gap) * scale, top, DialWidth * scale, CardHeight * scale),
                Res.Oxygen, economy, scale);

            float resourcesX = stacked ? left : left + (DialRowWidth + Gap) * scale;
            float resourcesY = stacked ? top + (CardHeight + Gap) * scale : top;
            DrawResources(new Rect(resourcesX, resourcesY, ResourcesWidth * scale, CardHeight * scale), economy, scale);
            DrawDial(new Rect(resourcesX + (ResourcesWidth + Gap) * scale, resourcesY, DialWidth * scale, CardHeight * scale),
                Res.Metal, economy, scale);
        }

        internal static Rect Layout(int screenWidth, int screenHeight, out bool stacked, out float scale)
        {
            float preferredScale = Mathf.Clamp(screenHeight / 720f, 1f, 1.5f);
            float available = Mathf.Max(1f, screenWidth - 2f * Gap);
            stacked = available < WideWidth * preferredScale * .9f;
            float width = stacked ? SuppliesRowWidth : WideWidth;
            float height = stacked ? 2f * CardHeight + Gap : CardHeight;
            scale = Mathf.Min(preferredScale, available / width);
            return new Rect(Mathf.Round((screenWidth - width * scale) * .5f), 12f,
                Mathf.Round(width * scale), Mathf.Round(height * scale));
        }

        private void UpdateReadouts(ColonyEconomy economy)
        {
            if (lastFrame == Time.frameCount) return;
            float blend = lastFrame < 0 || FrontierUi.ReduceMotion
                ? 1f
                : 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            lastFrame = Time.frameCount;
            foreach (Res resource in VisibleResources)
            {
                int index = (int)resource;
                float stock = economy.stock[index];
                float capacity = economy.cap[index];
                float fill = capacity > 0 ? Mathf.Clamp01(stock / capacity) : 0f;
                displayedStock[index] = Mathf.Lerp(displayedStock[index], stock, blend);
                displayedFill[index] = Mathf.Lerp(displayedFill[index], fill, blend);
            }
        }

        private void DrawDial(Rect card, Res resource, ColonyEconomy economy, float scale)
        {
            DrawCard(card, scale);
            DrawName(DialRect(card, 8, 6, 116, 16, scale), resource, 16, 12, scale);
            string status = Status(resource, economy);
            var dial = DialRect(card, 10, 28, 40, 40, scale);
            DrawGauge(dial, Gauge.Single, resource, status != null);
            if (status != null)
                Label(dial, status, 9f * scale, Warning, TextAnchor.MiddleCenter);

            DrawStock(DialRect(card, 56, 29, 68, 22, scale), resource, economy, scale, 17f);
            DrawRate(DialRect(card, 56, 51, 68, 16, scale), resource, economy, scale, status != null);
        }

        private void DrawResources(Rect card, ColonyEconomy economy, float scale)
        {
            DrawCard(card, scale);
            if (suppliesIcon != null)
                GUI.DrawTexture(DialRect(card, 8, 6, 16, 16, scale), suppliesIcon);
            Label(DialRect(card, 28, 6, ResourcesWidth - 36, 16, scale),
                "Supplies", 12f * scale, FrontierUi.Cream);

            var dial = DialRect(card, 96, 28, 40, 40, scale);
            DrawSupply(card, dial, 10, Gauge.Food, Res.Food, economy, scale);
            DrawSupply(card, dial, 148, Gauge.Water, Res.Water, economy, scale);
        }

        private void DrawSupply(Rect card, Rect dial, float left, Gauge gauge, Res resource,
            ColonyEconomy economy, float scale)
        {
            string status = Status(resource, economy);
            DrawGauge(dial, gauge, resource, status != null);
            DrawName(DialRect(card, left, 27, 74, 14, scale), resource, 12, 11, scale);
            DrawStock(DialRect(card, left, 41, 74, 15, scale), resource, economy, scale, 15f);
            var rate = DialRect(card, left, 55, 74, 14, scale);
            if (status != null)
                Label(rate, status, 10f * scale, Warning, TextAnchor.MiddleRight);
            DrawRate(rate, resource, economy, scale, status != null);
        }

        private void DrawName(Rect line, Res resource, int iconSize, int fontSize, float scale)
        {
            var icon = icons[(int)resource];
            float size = Mathf.Max(1f, Mathf.Round(iconSize * scale));
            if (icon != null)
                GUI.DrawTexture(new Rect(Mathf.Round(line.x), Mathf.Round(line.center.y - size * .5f), size, size), icon);
            float offset = (iconSize + 4f) * scale;
            line.xMin += offset;
            Label(line, resource.ToString(), fontSize * scale, FrontierUi.Cream);
        }

        private void DrawStock(Rect rect, Res resource, ColonyEconomy economy, float scale, float countSize)
        {
            int index = (int)resource;
            string stock = displayedStock[index].ToString("0", CultureInfo.InvariantCulture);
            string capacity = economy.cap[index].ToString("0", CultureInfo.InvariantCulture);
            int secondarySize = Mathf.Max(1, Mathf.RoundToInt(11f * scale));
            if (stock.Length + capacity.Length >= 8) countSize = 12f;
            string readout = $"<b>{stock}</b> <size={secondarySize}><color=#{SecondaryHex}>/ {capacity}</color></size>";
            Label(rect, readout, countSize * scale, FrontierUi.Cream);
        }

        private void DrawRate(Rect rect, Res resource, ColonyEconomy economy, float scale, bool low)
        {
            float rate = economy.net[(int)resource];
            string readout = (Mathf.Abs(rate) < .05f ? 0f : rate)
                .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "/s";
            Label(rect, readout, 11f * scale, low ? Warning : Secondary);
        }

        private static string Status(Res resource, ColonyEconomy economy)
        {
            int i = (int)resource;
            bool shortage = economy.lifeSupportShort && economy.shortOf == resource;
            bool draining = economy.net[i] <= -.05f && economy.cap[i] > 0f;
            if (!shortage && (!draining || economy.stock[i] > economy.cap[i] * .2f)) return null;
            return economy.stock[i] <= 0f ? "Empty" : "Low";
        }

        private static Rect DialRect(Rect card, float x, float y, float w, float h, float scale) =>
            new Rect(card.x + x * scale, card.y + y * scale, w * scale, h * scale);

        private static void DrawCard(Rect card, float scale)
        {
            OutpostGUI.InstrumentFrame(card, scale);
            var rule = DialRect(card, 8, 24, card.width / scale - 16f, 1f, scale);
            OutpostGUI.Panel(rule, Edge);
        }

        private void DrawGauge(Rect rect, Gauge gauge, Res resource, bool low)
        {
            DrawGaugeTile(rect, gauge, GaugeSteps, Edge);
            int fill = Mathf.RoundToInt(displayedFill[(int)resource] * GaugeSteps);
            if (fill > 0) DrawGaugeTile(rect, gauge, fill, low ? Warning : Fill);
        }

        private void DrawGaugeTile(Rect rect, Gauge gauge, int step, Color tint)
        {
            int tile = (int)gauge * TilesPerGauge + step;
            var uv = new Rect((tile % AtlasColumns) / (float)AtlasColumns, (tile / AtlasColumns) / (float)AtlasRows,
                1f / AtlasColumns, 1f / AtlasRows);
            Color original = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, gaugeAtlas, uv, true);
            GUI.color = original;
        }

        // Each tile is an arc mask. Food fills the left half and Water fills the right,
        // so the combined dial retains both fill levels even when one supply is empty.
        private static Texture2D CreateGaugeAtlas()
        {
            const int width = TileSize * AtlasColumns;
            const int height = TileSize * AtlasRows;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Resource dial arcs",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
            {
                float dx = x + .5f - TileSize * .5f;
                float dy = TileSize * .5f - y - .5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(2.8f - Mathf.Abs(radius - 20f)));
                if (alpha == 0) continue;
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                float clockwise = Mathf.Repeat(angle - 130f, 360f);
                float counterclockwise = Mathf.Repeat(50f - angle, 360f);

                for (int gauge = 0; gauge < 3; gauge++)
                {
                    float offset = gauge == (int)Gauge.Water ? counterclockwise : clockwise;
                    float sweep = gauge == (int)Gauge.Single ? 280f : 134f;
                    int first = Mathf.Max(1, Mathf.CeilToInt(offset / sweep * GaugeSteps));
                    for (int step = first; step <= GaugeSteps; step++)
                    {
                        int tile = gauge * TilesPerGauge + step;
                        int row = tile / AtlasColumns * TileSize + y;
                        int column = tile % AtlasColumns * TileSize + x;
                        pixels[row * width + column] = new Color32(255, 255, 255, alpha);
                    }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void Label(Rect rect, string value, float size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            text.alignment = alignment;
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            text.normal.textColor = color;
            GUI.Label(rect, value, text);
        }
    }
}
