using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    internal sealed class ResourceTopBar
    {
        private static readonly Color32 Shell = new Color32(32, 23, 43, 248);
        private static readonly Color32 Outline = new Color32(105, 76, 108, 255);
        private static readonly Color32 Track = new Color32(65, 44, 69, 255);
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

        private readonly Texture2D[] icons = new Texture2D[Names.Length];
        private readonly float[] displayedStock = new float[Names.Length];
        private readonly float[] displayedFill = new float[Names.Length];
        private GUIStyle text;
        private GUIStyle buildButton;
        private int lastFrame = -1;

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
                for (int i = 0; i < icons.Length; i++)
                    icons[i] = Resources.Load<Texture2D>("ResourceIcons/" + Names[i]);
            }

            UpdateReadouts(economy);
            const float width = 800f;
            float scale = Mathf.Min(Mathf.Clamp(Screen.height / 720f, 1f, 1.5f), (Screen.width - 16f) / width);
            bool compact = scale < 0.82f;
            if (compact) scale = Mathf.Clamp(Screen.height / 720f, 1f, 1.5f);
            float panelWidth = Mathf.Min(Screen.width - 16f, width * scale);
            var panel = new Rect(Mathf.Round((Screen.width - panelWidth) / 2f), Mathf.Round(12f * scale),
                panelWidth, Mathf.Round((compact ? 74f : 46f) * scale));

            DrawPill(panel, 12f * scale, Shell, Outline);
            float inset = (compact ? 9f : 14f) * scale;
            float cellWidth = (panel.width - 2f * inset) / (compact ? Names.Length : 5.35f);
            for (int i = 0; i < Names.Length; i++)
            {
                float left = panel.x + inset + i * cellWidth;
                float widthForResource = !compact && i == Names.Length - 1 ? cellWidth * 1.35f : cellWidth;
                DrawCell(new Rect(left, panel.y, widthForResource, panel.height), i, economy, scale, compact);
            }
        }

        public void DrawBuildPreview()
        {
            float scale = Mathf.Clamp(Screen.height / 720f, 1f, 1.5f);
            var rect = new Rect(Screen.width - 130f * scale, Screen.height - 66f * scale, 108f * scale, 44f * scale);
            if (buildButton == null)
            {
                buildButton = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                buildButton.normal.textColor = Cream;
                buildButton.hover.textColor = Color.white;
                buildButton.active.textColor = Cream;
            }
            buildButton.fontSize = Mathf.RoundToInt(17f * scale);

            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = rect.Contains(Event.current.mousePosition);
                DrawPill(rect, 11f * scale, hovered ? new Color32(57, 38, 65, 255) : Shell,
                    hovered ? new Color32(173, 125, 159, 255) : Outline);
            }
            // The preview shows the button styling before the build menu is wired up.
            GUI.Button(rect, "Build", buildButton);
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

        private static void DrawPill(Rect rect, float radius, Color background, Color border)
        {
            GUI.DrawTexture(rect, OutpostGUI.White, ScaleMode.StretchToFill, true, 0f, border, 0f, radius);
            var inset = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            GUI.DrawTexture(inset, OutpostGUI.White, ScaleMode.StretchToFill, true, 0f, background, 0f, radius - 1f);
        }

        private void DrawCell(Rect cell, int index, ColonyEconomy economy, float scale, bool compact)
        {
            float iconSize = compact ? 16f : 24f;
            float iconX = compact ? cell.center.x - iconSize / 2f : cell.x + 7f * scale;
            float iconY = cell.y + (compact ? 5f : 7f) * scale;
            if (icons[index] != null)
                GUI.DrawTexture(new Rect(Mathf.Round(iconX), Mathf.Round(iconY), iconSize, iconSize), icons[index]);

            Color tint = StockColors[index];
            Color capTint = Color.Lerp(tint, Shell, 0.19f);
            string count = ColorText($"<b>{displayedStock[index]:0}</b>", compact ? 14f : 19f, tint, scale);
            string cap = ColorText($"/{economy.cap[index]:0}", compact ? 10f : 13f, capTint, scale);
            float rate = economy.net[index];
            string rateLabel = Mathf.Abs(rate) >= 0.05f
                ? ColorText(rate.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "/s", 11f, rate > 0 ? Gain : Loss, scale)
                : string.Empty;

            if (compact)
            {
                Label(new Rect(cell.x, cell.y + 22f * scale, cell.width, 14f * scale), Names[index], scale, TextAnchor.MiddleCenter);
                Label(new Rect(cell.x, cell.y + 36f * scale, cell.width, 16f * scale), count + cap, scale, TextAnchor.MiddleCenter);
                Label(new Rect(cell.x, cell.y + 51f * scale, cell.width, 14f * scale), rateLabel, scale, TextAnchor.MiddleCenter);
            }
            else
            {
                float left = cell.x + 34f * scale;
                float available = cell.xMax - left - 3f * scale;
                string readout = Names[index] + "  " + count + cap;
                string inlineRate = rateLabel.Length == 0 ? readout : readout + "  " + rateLabel;
                text.fontSize = Mathf.RoundToInt(13f * scale);
                bool rateFits = rateLabel.Length == 0 || text.CalcSize(new GUIContent(inlineRate)).x <= available;
                Label(new Rect(left, cell.y + (rateFits ? 5f : 1f) * scale, available, 27f * scale),
                    rateFits ? inlineRate : readout, scale, TextAnchor.MiddleLeft);
                if (!rateFits)
                    Label(new Rect(left, cell.y + 21f * scale, available, 13f * scale), rateLabel, scale, TextAnchor.MiddleLeft);
            }

            float margin = (compact ? 5f : 8f) * scale;
            var track = new Rect(cell.x + margin, Mathf.Round(cell.yMax - 10f * scale),
                cell.width - 2f * margin, Mathf.Max(2f, Mathf.Round(3f * scale)));
            OutpostGUI.Panel(track, Track);
            OutpostGUI.Panel(new Rect(track.x, track.y, Mathf.Round(track.width * displayedFill[index]), track.height), tint);
        }

        private static string ColorText(string value, float size, Color color, float scale)
        {
            return $"<size={Mathf.RoundToInt(size * scale)}><color=#{ColorUtility.ToHtmlStringRGB(color)}>{value}</color></size>";
        }

        private void Label(Rect rect, string value, float scale, TextAnchor alignment)
        {
            text.alignment = alignment;
            text.fontSize = Mathf.RoundToInt(13f * scale);
            text.normal.textColor = Cream;
            GUI.Label(rect, value, text);
        }
    }
}
