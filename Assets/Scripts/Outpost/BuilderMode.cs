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

            buttonRect = ResourceTopBar.BuildButtonRect;
            float top = colony.resourceTopBar.Bottom + 8 * s;
            panelRect = new Rect(Screen.width - 226 * s, top, 216 * s, buttonRect.y - 8 * s - top);

            if (selected != null && !OverUI(mouse)) DrawPlacement(s, mouse);
            BuildingDef hovered = open ? DrawPanel(s, mouse) : null;
            if (colony.resourceTopBar.DrawBuildButton(open)) Toggle(); // uses the click, so the handler below never sees it
            if (hovered != null)
            {
                var cost = colony.PlaceCost(hovered);
                var status = colony.economy.CanAfford(cost) ? NodeStatus.Available : NodeStatus.TooExpensive;
                UpgradeBubbles.Tooltip(hovered.nodes[hovered.root], status, new Vector2(panelRect.x - 260 * s, mouse.y - 16 * s), s, cost);
            }
            if (Time.unscaledTime < flashUntil) Label(mouse + new Vector2(16 * s, -30 * s), "<color=#f88>" + flash + "</color>", s);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (open && panelRect.Contains(mouse))
                {
                    if (hovered != null) selected = selected == hovered ? null : hovered;
                    e.Use();
                }
                else if (selected != null) { Place(e.shift); e.Use(); }
            }
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

        BuildingDef DrawPanel(float s, Vector2 mouse)
        {
            OutpostGUI.Panel(panelRect, new Color(0.05f, 0.04f, 0.08f, 0.88f));
            GUI.Label(new Rect(panelRect.x + 8 * s, panelRect.y + 4 * s, panelRect.width, 20 * s), "<b>BUILD</b>   <color=#aaa>Shift = place many</color>",
                OutpostGUI.Text(12 * s, TextAnchor.MiddleLeft, Color.white));
            BuildingDef hovered = null;
            float h = 44 * s, y = panelRect.y + 28 * s;
            foreach (var def in Placeable)
            {
                var r = new Rect(panelRect.x + 6 * s, y, panelRect.width - 12 * s, h);
                y += h + 4 * s;
                bool hot = r.Contains(mouse);
                if (hot) hovered = def;
                if (def == selected) OutpostGUI.Panel(new Rect(r.x - 2 * s, r.y - 2 * s, r.width + 4 * s, r.height + 4 * s), new Color(0.95f, 0.7f, 0.15f));
                OutpostGUI.Panel(r, hot ? new Color(0.22f, 0.2f, 0.28f) : new Color(0.13f, 0.12f, 0.17f));

                var cost = colony.PlaceCost(def);
                var sb = new System.Text.StringBuilder();
                foreach (var a in cost)
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(colony.economy[a.res] >= a.value ? "<color=#ddd>" : "<color=#f77>").Append(a.value.ToString("0")).Append(' ').Append(a.res).Append("</color>");
                }
                int owned = colony.CountOf(def.kind);
                GUI.Label(new Rect(r.x + 8 * s, r.y + 3 * s, r.width - 16 * s, 18 * s),
                    "<b>" + def.name + "</b>  <color=#999>" + def.footprint.x + "x" + def.footprint.y + (owned > 0 ? "   owned " + owned : "") + "</color>",
                    OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, Color.white));
                GUI.Label(new Rect(r.x + 8 * s, r.y + 22 * s, r.width - 16 * s, 18 * s), sb.ToString(),
                    OutpostGUI.Text(10 * s, TextAnchor.MiddleLeft, Color.white));
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
