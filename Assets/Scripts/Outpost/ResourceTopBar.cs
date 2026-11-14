using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    internal sealed class ResourceTopBar
    {
        private static readonly Color32 Capacity = new Color32(207, 185, 184, 255);
        private static readonly Color32 Cream = new Color32(248, 232, 215, 255);
        private static readonly Color32 Gain = new Color32(160, 230, 183, 255);
        private static readonly Color32 Loss = new Color32(255, 151, 142, 255);
        private static readonly Color32[] StockColors =
        {
            new Color32(255, 193, 94, 255),
            new Color32(121, 192, 255, 255),
            new Color32(170, 230, 136, 255),
            new Color32(125, 235, 225, 255),
            new Color32(225, 206, 239, 255)
        };
        private static readonly string[] Names = System.Enum.GetNames(typeof(Res));

        private Texture2D resourceBarArt;
        private readonly Texture2D[] icons = new Texture2D[Names.Length];
        private readonly float[] displayedStock = new float[Names.Length];
        private readonly float[] displayedFill = new float[Names.Length];
        private GUIStyle text;
        private int lastFrame = -1;

        public Rect Bounds { get; private set; }

        public void Draw(ColonyEconomy economy)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (text == null)
            {
                resourceBarArt = Resources.Load<Texture2D>("OutpostHud/Resource Bar");
                text = new GUIStyle(GUI.skin.label)
                {
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(),
                    richText = true
                };
                for (int i = 0; i < icons.Length; i++)
                    icons[i] = Resources.Load<Texture2D>("ResourceIcons/" + Names[i]);
            }

            UpdateReadouts(economy);
            const float width = 640f, height = 44f;
            float scale = Mathf.Min(Mathf.Clamp(Screen.height / 720f, 1f, 1.5f), (Screen.width - 16f) / width);
            bool compact = scale < 0.85f;
            float verticalScale = compact ? 1.7f : scale;
            float panelWidth = Mathf.Round(width * scale);
            var panel = new Rect(Mathf.Round((Screen.width - panelWidth) / 2f), 12f,
                panelWidth, Mathf.Round(height * verticalScale));

            Bounds = panel;
            GUI.DrawTexture(panel, resourceBarArt);
            // These dimensions match the recessed compartments and tracks painted into the sprite.
            for (int i = 0; i < Names.Length; i++)
            {
                float left = panel.x + (15f + i * 121f) * scale;
                float cellWidth = (i == Names.Length - 1 ? 126f : 116f) * scale;
                DrawCell(new Rect(left, panel.y, cellWidth, panel.height), i, economy, scale, verticalScale, compact);
            }
        }

        private void UpdateReadouts(ColonyEconomy economy)
        {
            if (lastFrame == Time.frameCount) return;
            // IMGUI may repaint more than once per frame. Animate from real time once per frame.
            float blend = lastFrame < 0 || FrontierUi.ReduceMotion
                ? 1f
                : 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            lastFrame = Time.frameCount;
            for (int i = 0; i < Names.Length; i++)
            {
                float stock = economy.stock[i];
                float cap = economy.cap[i];
                float fill = cap > 0 ? Mathf.Clamp01(stock / cap) : 0;
                displayedStock[i] = Mathf.Lerp(displayedStock[i], stock, blend);
                displayedFill[i] = Mathf.Lerp(displayedFill[i], fill, blend);
            }
        }

        private void DrawCell(Rect cell, int index, ColonyEconomy economy, float scale, float verticalScale, bool compact)
        {
            float textScale = Mathf.Max(0.8f, scale);
            float iconSize = Mathf.Round(16f * textScale);
            float iconX = compact ? cell.center.x - iconSize / 2f : cell.x + scale;
            float iconY = cell.y + (compact ? 3f : 10f) * verticalScale;
            if (icons[index] != null)
                GUI.DrawTexture(new Rect(Mathf.Round(iconX), Mathf.Round(iconY), iconSize, iconSize), icons[index]);

            Color tint = StockColors[index];
            string count = ColorText($"<b>{displayedStock[index]:0}</b>", 14f, tint, textScale);
            string cap = ColorText($"/{economy.cap[index]:0}", 11f, Capacity, textScale);
            float rate = economy.net[index];
            string rateLabel = Mathf.Abs(rate) >= 0.05f
                ? ColorText(rate.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "/s", 10f,
                    rate > 0 ? Gain : Loss, textScale)
                : string.Empty;

            if (compact)
            {
                Label(new Rect(cell.x, cell.y + 11.5f * verticalScale, cell.width, 12f * textScale),
                    Names[index], textScale, TextAnchor.MiddleCenter);
                Label(new Rect(cell.x, cell.y + 18f * verticalScale, cell.width, 13f * textScale),
                    count + cap, textScale, TextAnchor.MiddleCenter);
                Label(new Rect(cell.x, cell.y + 25f * verticalScale, cell.width, 10f * textScale),
                    rateLabel, textScale, TextAnchor.MiddleCenter);
            }
            else
            {
                float left = cell.x + 21f * scale;
                float available = cell.xMax - left;
                string readout = Names[index] + "  " + count + cap;
                string inlineRate = readout + "  " + rateLabel;
                text.fontSize = Mathf.RoundToInt(11f * textScale);
                bool rateFits = rateLabel.Length == 0 || text.CalcSize(new GUIContent(inlineRate)).x <= available;
                Label(new Rect(left, cell.y + (rateFits ? 8f : 5f) * scale, available, 18f * scale),
                    rateFits ? inlineRate : readout, textScale, TextAnchor.MiddleLeft);
                if (!rateFits)
                    Label(new Rect(left, cell.y + 19f * scale, available, 10f * scale), rateLabel,
                        textScale, TextAnchor.MiddleLeft);
            }

            float fillX = Mathf.Round(cell.x + 2f * scale);
            float fillY = Mathf.Round(cell.y + 31f * verticalScale);
            float fillWidth = Mathf.Round((cell.width - 4f * scale) * displayedFill[index]);
            float fillHeight = Mathf.Max(1f, Mathf.Round(4f * verticalScale));
            OutpostGUI.Panel(new Rect(fillX, fillY, fillWidth, fillHeight), tint);
        }

        private static string ColorText(string value, float size, Color color, float scale)
        {
            return $"<size={Mathf.RoundToInt(size * scale)}><color=#{ColorUtility.ToHtmlStringRGB(color)}>{value}</color></size>";
        }

        private void Label(Rect rect, string value, float scale, TextAnchor alignment)
        {
            text.alignment = alignment;
            text.fontSize = Mathf.RoundToInt(11f * scale);
            text.normal.textColor = Cream;
            GUI.Label(rect, value, text);
        }
    }
}
