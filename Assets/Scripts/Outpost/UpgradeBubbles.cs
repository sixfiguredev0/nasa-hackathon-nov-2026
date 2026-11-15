using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Right-click a building (a click, not a camera drag) to show two round buttons beside it:
    /// up arrow opens the upgrade tree as bubbles (left-click a yellow bubble to buy it), "i" shows building info.
    /// Right-click again, left-click empty space or Esc closes.
    /// </summary>
    public class UpgradeBubbles : MonoBehaviour
    {
        public Colony colony;
        public Camera cam;
        [Tooltip("Max mouse travel (px) for a right-click; more is a camera drag")]
        public float clickSlop = 6f;

        enum Mode { Buttons, Tree, Info }

        Building open;
        Mode mode;
        Vector2 downPos;
        readonly Dictionary<string, Vector2> cell = new Dictionary<string, Vector2>(); // x = depth, y = row
        readonly Dictionary<string, Vector2> pos = new Dictionary<string, Vector2>();

        internal bool CancelForEscape()
        {
            if (open == null) return false;
            open = null;
            return true;
        }

        void Update()
        {
            if (MissionPauseMenu.BlocksGameplayInput) return;
            if (colony == null) colony = Colony.Instance;
            if (cam == null) cam = Camera.main;
            if (colony == null || cam == null) return;
            if (Input.GetMouseButtonDown(1)) downPos = Input.mousePosition;
            if (Input.GetMouseButtonUp(1) && ((Vector2)Input.mousePosition - downPos).magnitude <= clickSlop)
            {
                var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (BuilderMode.PointerOverUI(mouse)) return;
                var b = colony.BuildingAt(cam.ScreenToWorldPoint(Input.mousePosition));
                open = b == open ? null : b;
                mode = Mode.Buttons;
            }
        }

        void OnGUI()
        {
            if (MissionPauseMenu.BlocksGameplayInput) return;
            if (colony == null || cam == null) return;
            if (BuilderMode.Placing || BuilderMode.PointerOverUI(Event.current.mousePosition)) return;
            if (open == null) { HoverHint(); return; }

            float s = OutpostGUI.Scale;
            var st = open.state;
            var eco = colony.economy;
            var mouse = Event.current.mousePosition;

            // two buttons right of the building's top corner, info offset down-right like the sketch
            float br = 15 * s;
            var corner = OutpostGUI.WorldToGui(cam, open.TopRight);
            var upPos = new Vector2(Mathf.Min(corner.x + br + 8 * s, Screen.width - 2.6f * br), Mathf.Max(corner.y + br, br + 50 * s));
            var infoPos = upPos + new Vector2(0.6f * br, 2.6f * br);
            bool canUpgrade = false;
            foreach (var c in st.Node.next) canUpgrade |= eco.Status(st, c) == NodeStatus.Available;
            bool upHot = (mouse - upPos).magnitude <= br, infoHot = (mouse - infoPos).magnitude <= br;

            string hoveredNode = null;
            if (mode == Mode.Tree) DrawTree(st, eco, mouse, s, out hoveredNode);

            RoundButton(upPos, br, upHot, mode == Mode.Tree, canUpgrade, s);
            UpArrow(upPos, br);
            RoundButton(infoPos, br, infoHot, mode == Mode.Info, false, s);
            GUI.Label(new Rect(infoPos.x - br, infoPos.y - br, br * 2, br * 2), "<b>i</b>",
                OutpostGUI.Text(br * 1.3f, TextAnchor.MiddleCenter, Color.white));

            if (mode == Mode.Info) Tooltip(st.Node, eco.Status(st, st.nodeId), infoPos + new Vector2(br, -br), s);
            if (hoveredNode != null) Tooltip(st.def.nodes[hoveredNode], eco.Status(st, hoveredNode), mouse, s);
            else if (upHot && mode != Mode.Tree) Hint(mouse, "Upgrades", s);
            else if (infoHot && mode != Mode.Info) Hint(mouse, "Info", s);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (upHot) mode = mode == Mode.Tree ? Mode.Buttons : Mode.Tree;
                else if (infoHot) mode = mode == Mode.Info ? Mode.Buttons : Mode.Info;
                else if (hoveredNode != null) eco.TryUpgrade(st, hoveredNode);
                else open = null;
                e.Use();
            }
        }

        static void RoundButton(Vector2 p, float r, bool hot, bool active, bool pulse, float s)
        {
            float rr = r * (hot ? 1.1f : 1f) * (pulse ? 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f) : 1f);
            OutpostGUI.Disc(p, rr + 2 * s, active ? new Color(0.95f, 0.7f, 0.15f) : Color.white);
            OutpostGUI.Disc(p, rr, pulse ? new Color(0.75f, 0.5f, 0.1f) : new Color(0.1f, 0.1f, 0.13f));
        }

        static void UpArrow(Vector2 p, float r)
        {
            float w = 0.24f * r;
            OutpostGUI.Panel(new Rect(p.x - w / 2, p.y - 0.1f * r, w, 0.6f * r), Color.white);
            var tip = p + new Vector2(0, -0.5f * r);
            OutpostGUI.Line(tip, p + new Vector2(-0.45f * r, -0.02f * r), w, Color.white);
            OutpostGUI.Line(tip, p + new Vector2(0.45f * r, -0.02f * r), w, Color.white);
        }

        static void Hint(Vector2 m, string text, float s)
        {
            var style = OutpostGUI.Text(11 * s, TextAnchor.MiddleLeft, Color.white);
            var size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(m.x + 14 * s, m.y + 14 * s, size.x + 12 * s, size.y + 6 * s);
            OutpostGUI.Panel(rect, new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(rect.x + 6 * s, rect.y + 3 * s, size.x, size.y), text, style);
        }

        void DrawTree(BuildingState st, ColonyEconomy eco, Vector2 mouse, float s, out string hovered)
        {
            float r = 26 * s, colGap = 74 * s, rowGap = 62 * s;
            var def = st.def;

            cell.Clear();
            float leaves = 0;
            Place(def, def.root, 0, ref leaves);
            int maxDepth = 0;
            foreach (var c in cell.Values) maxDepth = Mathf.Max(maxDepth, (int)c.x);

            // tree sits above the building, root on the left, kept on screen
            var anchor = OutpostGUI.WorldToGui(cam, open.TopCenter);
            float treeW = maxDepth * colGap, treeH = (leaves - 1) * rowGap;
            float x0 = Mathf.Clamp(anchor.x - treeW / 2, r + 4 * s, Screen.width - treeW - r - 4 * s);
            float y0 = Mathf.Clamp(anchor.y - r - 16 * s - treeH, r + 50 * s, Screen.height - treeH - r - 30 * s);
            pos.Clear();
            foreach (var kv in cell) pos[kv.Key] = new Vector2(x0 + kv.Value.x * colGap, y0 + kv.Value.y * rowGap);

            GUI.Label(new Rect(x0 - r, y0 - r - 22 * s, 300 * s, 20 * s), "<b>" + def.name + "</b>  upgrade path",
                OutpostGUI.Text(12 * s, TextAnchor.MiddleLeft, Color.white));
            OutpostGUI.Line(anchor, pos[def.root], 2 * s, new Color(1f, 1f, 1f, 0.35f));

            foreach (var n in def.nodes.Values)
                foreach (var c in n.next)
                {
                    var cs = eco.Status(st, c);
                    var col = cs == NodeStatus.Owned || cs == NodeStatus.Current || cs == NodeStatus.Available
                        ? StatusColor(cs) : new Color(0.5f, 0.5f, 0.55f, 0.6f);
                    OutpostGUI.Line(pos[n.id], pos[c], 4 * s, Color.black);
                    OutpostGUI.Line(pos[n.id], pos[c], 2 * s, col);
                }

            hovered = null;
            foreach (var n in def.nodes.Values)
            {
                var p = pos[n.id];
                var status = eco.Status(st, n.id);
                bool hot = (mouse - p).magnitude <= r;
                if (hot) hovered = n.id;
                float rr = r * (status == NodeStatus.Available ? 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f) : 1f) * (hot ? 1.08f : 1f);
                if (status == NodeStatus.Current) OutpostGUI.Disc(p, rr + 5 * s, Color.white);
                OutpostGUI.Disc(p, rr + 2 * s, Color.black);
                OutpostGUI.Disc(p, rr, StatusColor(status));
                var textCol = status == NodeStatus.Closed || status == NodeStatus.Future ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                GUI.Label(new Rect(p.x - rr, p.y - rr, rr * 2, rr * 2), n.name, OutpostGUI.Text(9 * s, TextAnchor.MiddleCenter, textCol, true));
                if (n.rocketTier > 0)
                    GUI.Label(new Rect(p.x - rr, p.y + rr - 2 * s, rr * 2, 12 * s), "Lv " + n.rocketTier,
                        OutpostGUI.Text(8 * s, TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.6f)));
            }
        }

        float Place(BuildingDef def, string id, int depth, ref float leaves)
        {
            var n = def.nodes[id];
            float y;
            if (n.next.Length == 0) y = leaves++;
            else
            {
                float sum = 0;
                foreach (var c in n.next) sum += Place(def, c, depth + 1, ref leaves);
                y = sum / n.next.Length;
            }
            cell[id] = new Vector2(depth, y);
            return y;
        }

        static Color StatusColor(NodeStatus s)
        {
            switch (s)
            {
                case NodeStatus.Current: return new Color(0.2f, 0.7f, 0.35f);
                case NodeStatus.Owned: return new Color(0.15f, 0.45f, 0.28f);
                case NodeStatus.Available: return new Color(0.95f, 0.7f, 0.15f);
                case NodeStatus.TooExpensive: return new Color(0.6f, 0.42f, 0.2f);
                case NodeStatus.NeedsRocket: return new Color(0.38f, 0.4f, 0.55f);
                case NodeStatus.Future: return new Color(0.3f, 0.3f, 0.36f);
                default: return new Color(0.16f, 0.16f, 0.18f);
            }
        }

        static string StatusLine(UpgradeNode n, NodeStatus s)
        {
            switch (s)
            {
                case NodeStatus.Current: return "<color=#8f8>You are here</color>";
                case NodeStatus.Owned: return "<color=#8f8>Done</color>";
                case NodeStatus.Available: return "<color=#fd6>Click to " + (n.level == 1 ? "build" : "upgrade") + "!</color>";
                case NodeStatus.TooExpensive: return "<color=#fa6>Not enough resources yet</color>";
                case NodeStatus.NeedsRocket: return "<color=#aaf>Upgrade the Rocket to level " + n.rocketTier + " first</color>";
                case NodeStatus.Future: return "<color=#aaa>Unlock the step before this first</color>";
                default: return "<color=#888>You picked the other path</color>";
            }
        }

        /// <summary>Node card. <paramref name="cost"/> overrides the node's own cost (Builder Mode's scaled place cost).</summary>
        public static void Tooltip(UpgradeNode n, NodeStatus status, Vector2 mouse, float s, Amount[] cost = null)
        {
            cost = cost ?? n.cost;
            var lines = new List<string> { "<b>" + n.name + "</b>" + (n.level > 0 ? "  (level " + n.level + ")" : ""), n.blurb };
            var makes = new List<Amount>(); var uses = new List<Amount>();
            foreach (var a in n.rates) (a.value >= 0 ? makes : uses).Add(a);
            if (cost.Length > 0 && status != NodeStatus.Owned && status != NodeStatus.Current) lines.Add("Cost: " + OutpostGUI.List(cost));
            if (makes.Count > 0) lines.Add("<color=#8f8>Makes: " + OutpostGUI.List(makes.ToArray(), "/s") + "</color>");
            if (uses.Count > 0) lines.Add("<color=#f99>Uses: " + OutpostGUI.List(uses.ToArray(), "/s") + "</color>");
            if (n.train > 0) lines.Add("<color=#fc8>Trains crew: +" + (n.train * 100).ToString("0.#") + "% fitness/s</color>");
            if (n.storage.Length > 0) lines.Add("<color=#9cf>Stores: +" + OutpostGUI.List(n.storage) + "</color>");
            lines.Add(StatusLine(n, status));

            var style = OutpostGUI.Text(11 * s, TextAnchor.UpperLeft, Color.white, true);
            string text = string.Join("\n", lines);
            float w = 240 * s, h = style.CalcHeight(new GUIContent(text), w - 12 * s) + 10 * s;
            var rect = new Rect(mouse.x + 16 * s, mouse.y + 16 * s, w, h);
            if (rect.xMax > Screen.width) rect.x = mouse.x - w - 16 * s;
            if (rect.yMax > Screen.height) rect.y = mouse.y - h - 16 * s;
            OutpostGUI.Panel(rect, new Color(0.05f, 0.04f, 0.08f, 0.92f));
            GUI.Label(new Rect(rect.x + 6 * s, rect.y + 5 * s, w - 12 * s, h - 10 * s), text, style);
        }

        void HoverHint()
        {
            var b = colony.BuildingAt(cam.ScreenToWorldPoint(Input.mousePosition));
            if (b == null) return;
            float s = OutpostGUI.Scale;
            var st = b.state;
            string text = "<b>" + st.def.name + "</b>: " + st.Node.name;
            if (st.def.kind != BuildingKind.Rocket) text += "\n<color=#aaa>Site output " + (st.yield * 100f).ToString("0") + "%</color>";
            if (b.UnderConstruction) text += "\n<color=#9cf>Blueprint: waiting for the crew to build it</color>";
            else text += "\n<color=#aaa>Right-click for upgrades</color>";
            if (st.stalled) text += "\n<color=#f88>Stopped: needs " + st.missing + "</color>";
            var style = OutpostGUI.Text(11 * s, TextAnchor.UpperLeft, Color.white);
            var size = style.CalcSize(new GUIContent(text));
            var m = Event.current.mousePosition;
            var rect = new Rect(m.x + 14 * s, m.y + 14 * s, size.x + 12 * s, size.y + 8 * s);
            OutpostGUI.Panel(rect, new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(rect.x + 6 * s, rect.y + 4 * s, size.x, size.y), text, style);
        }
    }
}
