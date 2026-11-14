using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    internal sealed class ResourceTopBar
    {
        private static readonly Color32 Shell = new Color32(32, 23, 43, 248);
        private static readonly Color32 Outline = new Color32(105, 76, 108, 255);
        private static readonly Color32 BuildPanel = new Color32(32, 23, 43, 166);
        private static readonly Color32 SlotBackground = new Color32(44, 32, 54, 135);
        private static readonly Color32 SlotOutline = new Color32(83, 60, 87, 255);
        private static readonly Color32 HoverOutline = new Color32(173, 125, 159, 255);
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
        private GUIStyle slotLabel;
        private bool buildOpen;
        private int lastFrame = -1;

        private const int Columns = 3;
        private const float SlotSize = 68f, SlotSpacing = 8f, PanelPadding = 12f;
        private const float PanelSize = Columns * SlotSize + (Columns - 1) * SlotSpacing + 2 * PanelPadding;
        private static float BuildScale => Mathf.Min(Mathf.Clamp(Screen.height / 720f, 1f, 1.5f),
            Screen.width / 320f, Screen.height / 460f);

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

        public bool IsOverBuildUi(Vector2 point)
        {
            GetBuildLayout(BuildScale, out Rect panel, out Rect button);
            return button.Contains(point) || (buildOpen && panel.Contains(point));
        }

        private static void GetBuildLayout(float scale, out Rect panel, out Rect button)
        {
            panel = new Rect(Screen.width - (PanelSize + 22f) * scale,
                Screen.height - (PanelSize + 76f) * scale, PanelSize * scale, PanelSize * scale);
            button = new Rect(panel.center.x - 54f * scale, panel.yMax + 10f * scale, 108f * scale, 44f * scale);
        }

        public void DrawBuildMenu(IReadOnlyList<Building> buildings, UpgradeBubbles upgrades)
        {
            float scale = BuildScale;
            GetBuildLayout(scale, out Rect panel, out Rect button);
            if (buildButton == null)
            {
                buildButton = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                buildButton.normal.textColor = Cream;
                buildButton.hover.textColor = Color.white;
                buildButton.active.textColor = Cream;
                slotLabel = new GUIStyle(buildButton) { fontStyle = FontStyle.Normal, wordWrap = true };
            }
            buildButton.fontSize = Mathf.RoundToInt(17f * scale);
            slotLabel.fontSize = Mathf.RoundToInt(11f * scale);

            var e = Event.current;
            if (buildOpen && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                buildOpen = false;
                e.Use();
            }

            int depth = GUI.depth;
            GUI.depth = -1;
            if (buildOpen)
            {
                if (e.type == EventType.Repaint)
                    DrawBuildSurface(panel, 16f * scale, BuildPanel, Outline);

                for (int i = 0; i < Columns * Columns; i++)
                {
                    float x = panel.x + (PanelPadding + i % Columns * (SlotSize + SlotSpacing)) * scale;
                    float y = panel.y + (PanelPadding + i / Columns * (SlotSize + SlotSpacing)) * scale;
                    var slot = new Rect(x, y, SlotSize * scale, SlotSize * scale);
                    var building = i < buildings.Count ? buildings[i] : null;
                    if (e.type == EventType.Repaint)
                        DrawBuildSurface(slot, 9f * scale, SlotBackground,
                            building != null && slot.Contains(e.mousePosition) ? HoverOutline : SlotOutline);

                    if (building != null && DrawFacility(slot, building, scale) && upgrades != null)
                    {
                        upgrades.ShowBuilding(building);
                        buildOpen = false;
                    }
                }
            }

            if (e.type == EventType.Repaint)
            {
                bool highlighted = buildOpen || button.Contains(e.mousePosition);
                DrawPill(button, 11f * scale, highlighted ? new Color32(57, 38, 65, 255) : Shell,
                    highlighted ? HoverOutline : Outline);
            }
            if (GUI.Button(button, "Build", buildButton))
            {
                buildOpen = !buildOpen;
                if (buildOpen && upgrades != null) upgrades.ShowBuilding(null);
            }
            GUI.depth = depth;
        }

        private bool DrawFacility(Rect slot, Building building, float scale)
        {
            var state = building.state;
            var node = state.Node;
            if (node.isPlot) node = state.def.nodes[node.next[0]];

            if (Event.current.type == EventType.Repaint)
            {
                Sprite icon = BuildingArt.Get(state.def, node, building.footprint);
                Rect source = icon.textureRect;
                Texture2D texture = icon.texture;
                var uv = new Rect(source.x / texture.width, source.y / texture.height,
                    source.width / texture.width, source.height / texture.height);
                float fit = 34f * scale / Mathf.Max(source.width, source.height);
                float width = source.width * fit, height = source.height * fit;
                GUI.DrawTextureWithTexCoords(new Rect(slot.center.x - width / 2f,
                    slot.y + 22f * scale - height / 2f, width, height), texture, uv);
                GUI.Label(new Rect(slot.x + 3f * scale, slot.y + 40f * scale, slot.width - 6f * scale, 24f * scale),
                    node.name, slotLabel);
            }
            return GUI.Button(slot, GUIContent.none, GUIStyle.none);
        }

        private static void DrawBuildSurface(Rect rect, float radius, Color background, Color border)
        {
            GUI.DrawTexture(rect, OutpostGUI.White, ScaleMode.StretchToFill, true, 0f, background, 0f, radius);
            GUI.DrawTexture(rect, OutpostGUI.White, ScaleMode.StretchToFill, true, 0f, border, 1f, radius);
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
