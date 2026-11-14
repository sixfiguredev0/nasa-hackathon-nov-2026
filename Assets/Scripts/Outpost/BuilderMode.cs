using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// RTS-style Builder Mode. B or the bottom-right button opens a building list on the right.
    /// Pick one and a ghost follows the mouse, snapped to the lattice: green = can place, red = blocked or too expensive.
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
        bool OverUI(Vector2 guiPos) => buttonRect.Contains(guiPos) || (open && panelRect.Contains(guiPos));

        void Update()
        {
            if (colony == null) colony = Colony.Instance;
            if (cam == null) cam = Camera.main;
            if (colony == null || cam == null || !colony.Ready) return;

            if (Input.GetKeyDown(KeyCode.B)) Toggle();
            if (Input.GetKeyDown(KeyCode.Escape)) { if (selected != null) selected = null; else open = false; }
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
            var frames = BuildingArt.Get(selected, selected.nodes[selected.root], fp);
            ghost.sprite = frames[frames.Length - 1];
            ghost.transform.position = colony.map.LatticeToWorld(cells.center.x, cells.center.y);
            ghost.color = fits && affordable ? new Color(0.55f, 1f, 0.55f, 0.75f) : new Color(1f, 0.35f, 0.3f, 0.6f);
        }

        void OnGUI()
        {
            if (colony == null || cam == null || !colony.Ready) return;
            float s = OutpostGUI.Scale;
            var e = Event.current;
            var mouse = e.mousePosition;

            float scale = Mathf.Min(Mathf.Clamp(Screen.height / 720f, 1f, 1.5f),
                Screen.width / 320f, Screen.height / 460f);
            panelRect = new Rect(Screen.width - (PanelSize + 22f) * scale,
                Screen.height - (PanelSize + 76f) * scale, PanelSize * scale, PanelSize * scale);
            buttonRect = new Rect(panelRect.center.x - 54f * scale, panelRect.yMax + 10f * scale, 108f * scale, 44f * scale);

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
                if (open && panelRect.Contains(mouse))
                {
                    if (hovered != null) selected = selected == hovered ? null : hovered;
                    e.Use();
                }
                else if (selected != null && !buttonRect.Contains(mouse)) { Place(e.shift); e.Use(); }
            }
            GUI.depth = priorDepth;
        }

        void Place(bool keepGoing)
        {
            if (colony.TryPlace(selected, cells) == null) { flash = Problem(); flashUntil = Time.unscaledTime + 1.5f; return; }
            if (!keepGoing) selected = null;
        }

        string Problem()
        {
            if (!Colony.Fits(colony.BuildArea, cells, new RectInt[0])) return "Outside the build area";
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

        Vector2 G(float i, float j) => OutpostGUI.WorldToGui(cam, colony.map.LatticeToWorld(i, j));

        void Outline(RectInt r, float width, Color c)
        {
            Vector2 a = G(r.xMin, r.yMin), b = G(r.xMax, r.yMin), cc = G(r.xMax, r.yMax), d = G(r.xMin, r.yMax);
            OutpostGUI.Line(a, b, width, c); OutpostGUI.Line(b, cc, width, c);
            OutpostGUI.Line(cc, d, width, c); OutpostGUI.Line(d, a, width, c);
        }

        void DrawPlacement(float s, Vector2 mouse)
        {
            var area = colony.BuildArea;
            var grid = new Color(1f, 1f, 1f, 0.12f);
            for (int x = area.xMin + 1; x < area.xMax; x++) OutpostGUI.Line(G(x, area.yMin), G(x, area.yMax), s, grid);
            for (int y = area.yMin + 1; y < area.yMax; y++) OutpostGUI.Line(G(area.xMin, y), G(area.xMax, y), s, grid);
            Outline(area, 2 * s, new Color(1f, 1f, 1f, 0.45f));
            foreach (var b in colony.buildings) Outline(b.cells, 1.5f * s, new Color(1f, 0.4f, 0.3f, 0.5f));
            bool ok = fits && affordable;
            Outline(cells, 2 * s, ok ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f));
            if (Time.unscaledTime >= flashUntil)
                Label(mouse + new Vector2(16 * s, 12 * s), ok ? "<b>" + selected.name + "</b>  " + OutpostGUI.List(colony.PlaceCost(selected)) : "<color=#f88>" + Problem() + "</color>", s);
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
