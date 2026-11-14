using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    internal sealed class ResourceTopBar
    {
        private static readonly Color32 Shell = new Color32(28, 25, 34, 250);
        private static readonly Color32 Bevel = new Color32(122, 96, 115, 255);
        private static readonly Color32 Well = new Color32(44, 37, 49, 255);
        private static readonly Color32 Inset = new Color32(23, 20, 29, 255);
        private static readonly Color32 Cream = new Color32(248, 229, 200, 255);
        private static readonly Color32 Muted = new Color32(201, 180, 197, 255);
        private static readonly Color32 Gain = new Color32(160, 224, 163, 255);
        private static readonly Color32 Loss = new Color32(255, 148, 135, 255);
        private static readonly string[] Names = System.Enum.GetNames(typeof(Res));

        private Texture2D[] icons;
        private GUIStyle text;

        public void Draw(ColonyEconomy economy)
        {
            if (icons == null)
            {
                icons = new Texture2D[Names.Length];
                for (int i = 0; i < icons.Length; i++)
                    icons[i] = Resources.Load<Texture2D>("ResourceIcons/" + Names[i]);
            }

            text ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset()
            };

            float scale = Mathf.Clamp(Screen.height / 720f, 1f, 1.5f);
            float width = Mathf.Min(Screen.width - 16f, 850f * scale);
            bool compact = width / Names.Length < 152f * scale;
            float height = (compact ? 81f : 56f) * scale;
            var bar = new Rect(Mathf.Floor((Screen.width - width) / 2), 8, width, height);

            OutpostGUI.Panel(new Rect(bar.x, bar.y + 3, bar.width, bar.height), new Color32(14, 12, 20, 215));
            OutpostGUI.Panel(bar, Shell);
            OutpostGUI.Panel(new Rect(bar.x, bar.y, bar.width, 1), Bevel);
            OutpostGUI.Panel(new Rect(bar.x, bar.yMax - 1, bar.width, 1), Bevel);

            float cellWidth = (bar.width - 8) / Names.Length;
            for (int i = 0; i < Names.Length; i++)
            {
                var cell = new Rect(bar.x + 4 + Mathf.Round(i * cellWidth), bar.y + 4,
                    Mathf.Round((i + 1) * cellWidth) - Mathf.Round(i * cellWidth) - 2, bar.height - 8);
                DrawCell(cell, i, economy, scale, compact);
            }
        }

        private void DrawCell(Rect cell, int index, ColonyEconomy economy, float scale, bool compact)
        {
            OutpostGUI.Panel(cell, Well);
            float iconSize = compact ? 16f : 32f;
            float iconX = compact ? cell.center.x - iconSize / 2 : cell.x + 7;
            float iconY = compact ? cell.y + 2 * scale : cell.center.y - iconSize / 2;
            if (!compact)
                OutpostGUI.Panel(new Rect(iconX - 2, iconY - 2, iconSize + 4, iconSize + 4), Inset);
            if (icons[index] != null)
                GUI.DrawTexture(new Rect(Mathf.Round(iconX), Mathf.Round(iconY), iconSize, iconSize), icons[index]);

            float left = compact ? cell.x + 1 : cell.x + 44;
            float width = cell.xMax - left - (compact ? 1 : 6);
            float nameY = cell.y + (compact ? 19 : 2) * scale;
            float stockY = cell.y + (compact ? 32 : 16) * scale;
            var alignment = compact ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            float rate = economy.net[index];
            float stock = economy.stock[index];
            float capacity = economy.cap[index];
            Label(new Rect(left, nameY, width, 14 * scale), Names[index], 12 * scale, Muted, alignment);
            text.fontStyle = FontStyle.Bold;
            Label(new Rect(left, stockY, width, 20 * scale), $"{stock:0}/{capacity:0}",
                (compact ? 12 : 16) * scale, Cream, alignment);
            text.fontStyle = FontStyle.Normal;

            if (Mathf.Abs(rate) >= 0.05f)
            {
                float rateY = compact ? cell.y + 49 * scale : nameY;
                var rateAlignment = compact ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight;
                string rateText = rate.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "/s";
                Label(new Rect(left, rateY, width, 14 * scale), rateText, 11 * scale, rate > 0 ? Gain : Loss, rateAlignment);
            }

            float fill = capacity > 0 ? Mathf.Clamp01(stock / capacity) : 0;
            float trackLeft = compact ? left + 4 : left;
            float trackWidth = compact ? width - 8 : width;
            var track = new Rect(trackLeft, cell.yMax - 9 * scale, trackWidth, 4 * scale);
            OutpostGUI.Panel(track, Inset);
            const int segments = 8;
            float segmentWidth = track.width / segments;
            for (int segment = 0; segment < segments; segment++)
            {
                float segmentFill = Mathf.Clamp01(fill * segments - segment);
                if (segmentFill <= 0) break;
                var segmentRect = new Rect(Mathf.Round(track.x + segment * segmentWidth), track.y,
                    Mathf.Round((segmentWidth - 2) * segmentFill), track.height);
                OutpostGUI.Panel(segmentRect, OutpostGUI.ResColor((Res)index));
            }
        }

        private void Label(Rect rect, string value, float size, Color color, TextAnchor alignment)
        {
            text.alignment = alignment;
            text.fontSize = Mathf.RoundToInt(size);
            text.normal.textColor = color;
            GUI.Label(rect, value, text);
        }
    }
}
