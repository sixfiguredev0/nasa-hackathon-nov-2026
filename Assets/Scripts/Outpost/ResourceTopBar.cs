using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    internal sealed class ResourceTopBar
    {
        private static readonly Color32 Shell = new Color32(28, 25, 34, 250);
        private static readonly Color32 Bevel = new Color32(108, 84, 105, 255);
        private static readonly Color32 Rail = new Color32(197, 126, 117, 255);
        private static readonly Color32 Well = new Color32(49, 40, 54, 255);
        private static readonly Color32 Inset = new Color32(23, 20, 29, 255);
        private static readonly Color32 Cream = new Color32(248, 229, 200, 255);
        private static readonly Color32 Muted = new Color32(201, 180, 197, 255);
        private static readonly Color32 Gain = new Color32(160, 224, 163, 255);
        private static readonly Color32 Loss = new Color32(255, 148, 135, 255);
        private static readonly string[] Names = System.Enum.GetNames(typeof(Res));

        private Texture2D[] icons;
        private GUIStyle text;
        private readonly float[] displayedStock = new float[Names.Length];
        private readonly float[] displayedFill = new float[Names.Length];
        private int lastFrame = -1;

        public void Draw(ColonyEconomy economy)
        {
            if (Event.current.type != EventType.Repaint) return;
            UpdateReadouts(economy);

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
            float height = (compact ? 86f : 56f) * scale;
            var bar = new Rect(Mathf.Floor((Screen.width - width) / 2), 8, width, height);

            float rim = compact ? 8 : 18;
            DrawFrame(bar, rim, compact);

            float cellWidth = (bar.width - rim * 2) / Names.Length;
            for (int i = 0; i < Names.Length; i++)
            {
                var cell = new Rect(bar.x + rim + Mathf.Round(i * cellWidth), bar.y + 5,
                    Mathf.Round((i + 1) * cellWidth) - Mathf.Round(i * cellWidth), bar.height - 12);
                if (i > 0)
                    OutpostGUI.Panel(new Rect(cell.x, cell.y + 2, 1, cell.height - 4), Bevel);
                DrawCell(cell, i, economy, scale, compact);
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

        private static void DrawFrame(Rect bar, float rim, bool compact)
        {
            // Stepped ends and a recessed lower lip give the shared tray a pixel-art silhouette.
            OutpostGUI.Panel(new Rect(bar.x + 3, bar.y + 4, bar.width - 6, bar.height - 1), Inset);
            OutpostGUI.Panel(new Rect(bar.x, bar.y + 5, bar.width, bar.height - 11), Shell);
            OutpostGUI.Panel(new Rect(bar.x + 3, bar.y + 2, bar.width - 6, bar.height - 5), Shell);
            OutpostGUI.Panel(new Rect(bar.x + 7, bar.y, bar.width - 14, bar.height - 5), Shell);
            OutpostGUI.Panel(new Rect(bar.x + rim, bar.y + 2, bar.width - rim * 2, 1), Rail);
            OutpostGUI.Panel(new Rect(bar.x + rim, bar.y + 5, bar.width - rim * 2, bar.height - 12), Well);
            OutpostGUI.Panel(new Rect(bar.x + rim, bar.yMax - 5, bar.width - rim * 2, 2), Bevel);
            if (compact) return;
            OutpostGUI.Panel(new Rect(bar.x + 6, bar.center.y - 2, 6, 4), Inset);
            OutpostGUI.Panel(new Rect(bar.xMax - 12, bar.center.y - 2, 6, 4), Inset);
            OutpostGUI.Panel(new Rect(bar.x + 8, bar.center.y - 1, 2, 1), Rail);
            OutpostGUI.Panel(new Rect(bar.xMax - 10, bar.center.y - 1, 2, 1), Rail);
        }

        private void DrawCell(Rect cell, int index, ColonyEconomy economy, float scale, bool compact)
        {
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
            float stock = displayedStock[index];
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

            float trackLeft = compact ? left + 4 : left;
            float trackWidth = compact ? width - 8 : width;
            var track = new Rect(trackLeft, cell.yMax - 9 * scale, trackWidth, 4 * scale);
            OutpostGUI.Panel(track, Inset);
            OutpostGUI.Panel(new Rect(track.x, track.y, track.width * displayedFill[index], track.height),
                OutpostGUI.ResColor((Res)index));
            for (int tick = 1; tick < 4; tick++)
                OutpostGUI.Panel(new Rect(Mathf.Round(track.x + track.width * tick / 4), track.y, 1, track.height),
                    new Color32(102, 88, 108, 180));
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
