using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Right-click a building (a click, not a camera drag) to show its whole upgrade tree as bubbles.
    /// Left-click a yellow bubble to buy it. Right-click again, left-click empty space or Esc closes.
    /// </summary>
    public class UpgradeBubbles : MonoBehaviour
    {
        public Colony colony;
        public Camera cam;
        [Tooltip("Max mouse travel (px) for a right-click; more is a camera drag")]
        public float clickSlop = 6f;

        Building open;
        Vector2 downPos;
        readonly Dictionary<string, Vector2> cell = new Dictionary<string, Vector2>(); // x = depth, y = row
        readonly Dictionary<string, Vector2> pos = new Dictionary<string, Vector2>();

        void Update()
        {
            if (colony == null) colony = Colony.Instance;
            if (cam == null) cam = Camera.main;
            if (colony == null || cam == null) return;
            if (Input.GetMouseButtonDown(1)) downPos = Input.mousePosition;
            if (Input.GetMouseButtonUp(1) && ((Vector2)Input.mousePosition - downPos).magnitude <= clickSlop)
            {
                var b = colony.BuildingAt(cam.ScreenToWorldPoint(Input.mousePosition));
                open = b == open ? null : b;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) open = null;
        }

        void OnGUI()
        {
            if (colony == null || cam == null) return;
            if (open == null) { HoverHint(); return; }

            float s = OutpostGUI.Scale, r = 26 * s, colGap = 74 * s, rowGap = 62 * s;
            var st = open.state;
            var def = st.def;
            var eco = colony.economy;

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

            var mouse = Event.current.mousePosition;
            string hovered = null;
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

            if (hovered != null) Tooltip(def.nodes[hovered], eco.Status(st, hovered), mouse, s);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (hovered != null) eco.TryUpgrade(st, hovered);
                else open = null;
                e.Use();
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

        string StatusLine(UpgradeNode n, NodeStatus s)
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

        void Tooltip(UpgradeNode n, NodeStatus status, Vector2 mouse, float s)
        {
            var lines = new List<string> { "<b>" + n.name + "</b>" + (n.level > 0 ? "  (level " + n.level + ")" : ""), n.blurb };
            var makes = new List<Amount>(); var uses = new List<Amount>();
            foreach (var a in n.rates) (a.value >= 0 ? makes : uses).Add(a);
            if (n.cost.Length > 0 && status != NodeStatus.Owned && status != NodeStatus.Current) lines.Add("Cost: " + OutpostGUI.List(n.cost));
            if (makes.Count > 0) lines.Add("<color=#8f8>Makes: " + OutpostGUI.List(makes.ToArray(), "/s") + "</color>");
            if (uses.Count > 0) lines.Add("<color=#f99>Uses: " + OutpostGUI.List(uses.ToArray(), "/s") + "</color>");
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
            if (st.Node.isPlot) text += "\nRight-click to build";
            else if (st.stalled) text += "\n<color=#f88>Stopped: needs " + st.missing + "</color>";
            var style = OutpostGUI.Text(11 * s, TextAnchor.UpperLeft, Color.white);
            var size = style.CalcSize(new GUIContent(text));
            var m = Event.current.mousePosition;
            var rect = new Rect(m.x + 14 * s, m.y + 14 * s, size.x + 12 * s, size.y + 8 * s);
            OutpostGUI.Panel(rect, new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(rect.x + 6 * s, rect.y + 4 * s, size.x, size.y), text, style);
        }
    }
}
