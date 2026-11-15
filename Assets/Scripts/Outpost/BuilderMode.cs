using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// RTS-style Builder Mode. B or the bottom-right button opens a building list on the right.
    /// Pick one and a ghost follows the mouse anywhere on the map, snapped to the lattice: green = can place, red = blocked or too expensive.
    /// A grid draped over the ground follows the cursor; its dots show how well this building would do on each cell
    /// (green = more output, red = less, see <see cref="SiteSurvey"/>), and the label lists why.
    /// Left-click places (hold Shift to keep placing), right-click or Esc cancels, Esc / B again closes the menu.
    /// Placing pays up front; the crew then walks over and builds it. Upgrades work like any building (right-click it).
    /// </summary>
    public class BuilderMode : MonoBehaviour
    {
        public static bool Placing => current != null && current.selected != null;
        static BuilderMode current;

        Colony colony;
        Camera cam;
        bool open;
        BuildingDef selected;
        SpriteRenderer ghost;
        RectInt cells;
        bool fits, affordable;
        float yield = 1f;
        readonly List<SiteEffect> why = new List<SiteEffect>();
        const int GridRadius = 8;
        Vector2Int heatCenter = new Vector2Int(int.MinValue, 0);
        BuildingDef heatDef;
        readonly float[] heat = new float[(2 * GridRadius + 1) * (2 * GridRadius + 1)];
        Rect buttonRect, panelRect;
        Texture2D panelArt, buttonArt;
        GUIStyle buttonStyle, slotLabel;
        const int Columns = 3;
        const float SlotSize = 68f, SlotStride = 76f, PanelPadding = 12f, PanelSize = 244f;
        Vector2 rightDown;
        string flash;
        float flashUntil;

        static readonly List<BuildingDef> Placeable = BuildingCatalog.Defs.FindAll(d => d.kind != BuildingKind.Rocket);

        void Awake() => current = this;

        void OnDestroy()
        {
            if (current == this) current = null;
            if (ghost != null) Destroy(ghost.gameObject);
        }

        void Toggle()
        {
            open = !open;
            if (!open) selected = null;
        }

        static Vector2 GuiMouse => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        internal static bool PointerOverUI(Vector2 guiPos) => current != null && current.OverUI(guiPos);
        bool OverUI(Vector2 guiPos) => buttonRect.Contains(guiPos) || (open && panelRect.Contains(guiPos)) || MissionGuide.PointerOverUI(guiPos);

        internal bool CancelForEscape()
        {
            if (selected == null && !open) return false;
            if (selected != null)
            {
                selected = null;
                if (ghost != null) ghost.enabled = false;
            }
            else open = false;
            return true;
        }

        void Update()
        {
            if (MissionPauseMenu.BlocksGameplayInput) return;
            if (colony == null) colony = Colony.Instance;
            if (cam == null) cam = Camera.main;
            if (colony == null || cam == null || !colony.Ready) return;

            if (Input.GetKeyDown(KeyCode.B)) Toggle();
            if (Input.GetMouseButtonDown(1)) rightDown = Input.mousePosition;
            if (Input.GetMouseButtonUp(1) && ((Vector2)Input.mousePosition - rightDown).magnitude <= 6f) selected = null; // a click, not a camera drag

            if (ghost == null)
            {
                var go = new GameObject("Build Ghost");
                go.transform.SetParent(transform, false);
                ghost = go.AddComponent<SpriteRenderer>();
                ghost.sortingOrder = 32000;
            }
            ghost.enabled = selected != null && !OverUI(GuiMouse);
            if (selected == null) return;

            var l = colony.map.WorldToLattice(cam.ScreenToWorldPoint(Input.mousePosition));
            var fp = selected.footprint;
            cells = new RectInt(Mathf.RoundToInt(l.x - fp.x / 2f), Mathf.RoundToInt(l.y - fp.y / 2f), fp.x, fp.y);
            fits = colony.Fits(cells);
            affordable = colony.economy.CanAfford(colony.PlaceCost(selected));
            var site = colony.SiteAt(cells);
            why.Clear();
            yield = SiteSurvey.Yield(selected.kind, site, why);
            var frames = BuildingArt.Get(selected, selected.nodes[selected.root], fp);
            ghost.sprite = frames[frames.Length - 1];
            ghost.transform.position = colony.FootprintWorld(cells, site);

            var hc = new Vector2Int(Mathf.FloorToInt(l.x), Mathf.FloorToInt(l.y));
            if (hc != heatCenter || heatDef != selected) RefreshHeat(hc);
            ghost.color = fits && affordable ? new Color(0.55f, 1f, 0.55f, 0.75f) : new Color(1f, 0.35f, 0.3f, 0.6f);
        }

        void OnGUI()
        {
            if (MissionPauseMenu.BlocksGameplayInput && Event.current.type != EventType.Repaint) return;
            if (colony == null || cam == null || !colony.Ready) return;
            float s = OutpostGUI.Scale;
            var e = Event.current;
            var mouse = e.mousePosition;

            float scale = Mathf.Min(Mathf.Clamp(Screen.height / 720f, 1f, 1.5f),
                Screen.width / 320f, Screen.height / 460f);
            panelRect = new Rect(Screen.width - (PanelSize + 12f) * scale,
                Screen.height - (PanelSize + 76f) * scale, PanelSize * scale, PanelSize * scale);
            buttonRect = new Rect(panelRect.xMax - 108f * scale, panelRect.yMax + 10f * scale, 108f * scale, 44f * scale);

            if (buttonStyle == null)
            {
                panelArt = Resources.Load<Texture2D>("OutpostHud/Build Panel");
                buttonArt = Resources.Load<Texture2D>("OutpostHud/Build Button");
                buttonStyle = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                buttonStyle.normal.textColor = new Color32(248, 232, 215, 255);
                buttonStyle.hover.textColor = Color.white;
                buttonStyle.active.textColor = buttonStyle.normal.textColor;
                slotLabel = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Normal, wordWrap = true };
            }
            buttonStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(17f * scale));
            slotLabel.fontSize = Mathf.Max(1, Mathf.RoundToInt(11f * scale));
            int priorDepth = GUI.depth;
            GUI.depth = -1;

            if (selected != null && !OverUI(mouse)) DrawPlacement(s, mouse);
            BuildingDef hovered = open ? DrawPanel(scale, mouse) : null;
            if (e.type == EventType.Repaint)
            {
                Color priorColor = GUI.color;
                if (!open && !buttonRect.Contains(mouse)) GUI.color *= new Color(0.86f, 0.86f, 0.86f);
                GUI.DrawTexture(buttonRect, buttonArt);
                GUI.color = priorColor;
            }
            if (GUI.Button(buttonRect, "BUILD", buttonStyle)) Toggle();
            if (hovered != null)
            {
                var cost = colony.PlaceCost(hovered);
                var status = colony.economy.CanAfford(cost) ? NodeStatus.Available : NodeStatus.TooExpensive;
                UpgradeBubbles.Tooltip(hovered.nodes[hovered.root], status, new Vector2(Mathf.Max(8 * s, panelRect.x - 260 * s), mouse.y - 16 * s), s, cost);
            }
            if (Time.unscaledTime < flashUntil) Label(mouse + new Vector2(16 * s, -30 * s), "<color=#f88>" + flash + "</color>", s);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (MissionGuide.PointerOverUI(mouse)) e.Use();
                else if (open && panelRect.Contains(mouse))
                {
                    if (hovered != null) selected = selected == hovered ? null : hovered;
                    e.Use();
                }
                else if (selected != null && !OverUI(mouse)) { Place(e.shift); e.Use(); }
            }
            GUI.depth = priorDepth;
        }

        /// <summary>Per-cell yield of the selected building around the cursor (1x1 survey: terrain only, not the footprint's slope).</summary>
        void RefreshHeat(Vector2Int c)
        {
            heatCenter = c; heatDef = selected;
            int n = 2 * GridRadius + 1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var site = colony.SiteAt(new RectInt(c.x - GridRadius + x, c.y - GridRadius + y, 1, 1));
                    site.uneven = 0f;
                    heat[y * n + x] = SiteSurvey.Yield(selected.kind, site);
                }
        }

        void Place(bool keepGoing)
        {
            if (colony.TryPlace(selected, cells) == null) { flash = Problem(); flashUntil = Time.unscaledTime + 1.5f; return; }
            if (!keepGoing) selected = null;
        }

        string Problem()
        {
            if (!Colony.Fits(colony.BuildArea, cells, new RectInt[0])) return "Off the edge of the map";
            if (!fits) return "Overlaps another building";
            var cost = colony.PlaceCost(selected);
            var need = new List<Amount>();
            foreach (var a in cost)
            {
                float lack = a.value - colony.economy[a.res];
                if (lack > 0) need.Add(new Amount(a.res, Mathf.Ceil(lack)));
            }
            return need.Count > 0 ? "Need " + OutpostGUI.List(need.ToArray()) + " more" : "Can't build here";
        }

        // grid points sit on the terrain surface so the grid drapes over hills, mesas and craters
        Vector2 G(float i, float j) => OutpostGUI.WorldToGui(cam, colony.map.SurfaceToWorld(i, j));

        void Outline(RectInt r, float width, Color c)
        {
            Vector2 a = G(r.xMin, r.yMin), b = G(r.xMax, r.yMin), cc = G(r.xMax, r.yMax), d = G(r.xMin, r.yMax);
            OutpostGUI.Line(a, b, width, c); OutpostGUI.Line(b, cc, width, c);
            OutpostGUI.Line(cc, d, width, c); OutpostGUI.Line(d, a, width, c);
        }

        void DrawPlacement(float s, Vector2 mouse)
        {
            if (Event.current.type == EventType.Repaint) DrawGrid(s);
            foreach (var b in colony.buildings) Outline(b.cells, 1.5f * s, new Color(1f, 0.4f, 0.3f, 0.5f));
            bool ok = fits && affordable;
            Outline(cells, 2 * s, ok ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f));
            if (Time.unscaledTime >= flashUntil)
            {
                string head = ok ? "<b>" + selected.name + "</b>  " + OutpostGUI.List(colony.PlaceCost(selected)) : "<color=#f88>" + Problem() + "</color>";
                Label(mouse + new Vector2(16 * s, 12 * s), head + "\n" + SiteSurvey.Describe(yield, why), s);
            }
        }

        /// <summary>Grid cells around the cursor, fading out with distance, each with a dot coloured by its yield.</summary>
        void DrawGrid(float s)
        {
            var area = colony.BuildArea;
            var c = heatCenter;
            int n = 2 * GridRadius + 1;
            float dot = 4 * s;
            for (int y = c.y - GridRadius; y <= c.y + GridRadius; y++)
                for (int x = c.x - GridRadius; x <= c.x + GridRadius; x++)
                {
                    if (x < area.xMin || y < area.yMin || x >= area.xMax || y >= area.yMax) continue;
                    float fade = 1f - Mathf.Sqrt((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y)) / (GridRadius + 0.5f);
                    if (fade <= 0f) continue;
                    var line = new Color(1f, 1f, 1f, 0.22f * fade);
                    Vector2 a = G(x, y), b = G(x + 1, y), d = G(x, y + 1);
                    OutpostGUI.Line(a, b, s, line);
                    OutpostGUI.Line(a, d, s, line);
                    float h = heat[(y - c.y + GridRadius) * n + (x - c.x + GridRadius)];
                    var col = h >= 1f ? Color.Lerp(Color.white, new Color(0.3f, 1f, 0.35f), (h - 1f) / 0.4f)
                                      : Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f), (1f - h) / 0.4f);
                    col.a = 0.85f * fade;
                    var p = G(x + 0.5f, y + 0.5f);
                    GUI.color = col;
                    GUI.DrawTexture(new Rect(p.x - dot / 2, p.y - dot / 2, dot, dot), OutpostGUI.Circle);
                    GUI.color = Color.white;
                }
        }

        BuildingDef DrawPanel(float scale, Vector2 mouse)
        {
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) GUI.DrawTexture(panelRect, panelArt);

            BuildingDef hovered = null;
            for (int i = 0; i < Placeable.Count; i++)
            {
                var def = Placeable[i];
                float x = panelRect.x + (PanelPadding + i % Columns * SlotStride) * scale;
                float y = panelRect.y + (PanelPadding + i / Columns * SlotStride) * scale;
                var slot = new Rect(x, y, SlotSize * scale, SlotSize * scale);
                bool hot = slot.Contains(mouse);
                if (hot) hovered = def;
                if (!repaint) continue;

                if (hot || def == selected)
                {
                    var border = new Rect(x + 2f * scale, y + 2f * scale, slot.width - 4f * scale, slot.height - 4f * scale);
                    Color tint = def == selected ? new Color32(255, 193, 94, 255) : new Color32(173, 125, 159, 255);
                    GUI.DrawTexture(border, OutpostGUI.White, ScaleMode.StretchToFill, true, 0f, tint, scale, 4f * scale);
                }

                var frames = BuildingArt.Get(def, def.nodes[def.root], def.footprint);
                Sprite icon = frames[frames.Length - 1];
                Rect source = icon.textureRect;
                Texture2D texture = icon.texture;
                var uv = new Rect(source.x / texture.width, source.y / texture.height,
                    source.width / texture.width, source.height / texture.height);
                float fit = 34f * scale / Mathf.Max(source.width, source.height);
                float width = source.width * fit;
                float height = source.height * fit;
                GUI.DrawTextureWithTexCoords(new Rect(slot.center.x - width / 2f, y + 21f * scale - height / 2f, width, height), texture, uv);
                GUI.Label(new Rect(x + 3f * scale, y + 38f * scale, slot.width - 6f * scale, 28f * scale),
                    def.nodes[def.root].name, slotLabel);
            }
            return hovered;
        }

        static void Label(Vector2 at, string text, float s)
        {
            var style = OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, Color.white);
            var size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(at.x, at.y, size.x + 12 * s, size.y + 6 * s);
            if (rect.xMax > Screen.width) rect.x = at.x - rect.width - 32 * s;
            OutpostGUI.Panel(rect, new Color(0f, 0f, 0f, 0.75f));
            GUI.Label(new Rect(rect.x + 6 * s, rect.y + 3 * s, size.x, size.y), text, style);
        }
    }
}
