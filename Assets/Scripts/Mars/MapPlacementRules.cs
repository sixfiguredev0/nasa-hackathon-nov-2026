using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Mars
{
    [Serializable]
    public struct OutpostZone
    {
        public string name;
        public RectInt cells;
    }

    [Serializable]
    public class MapPlacementRules
    {
        public int edgeMargin = 1;
        public int zoneGap = 1;
        public int outpostClearance = 1;
        public List<OutpostZone> zones = DefaultZones();

        public static List<OutpostZone> DefaultZones() => new List<OutpostZone>
        {
            new OutpostZone { name = "Landing Pad", cells = new RectInt(38, 27, 4, 4) },
            new OutpostZone { name = "ISRU", cells = new RectInt(43, 27, 3, 3) },
            new OutpostZone { name = "Water Processing Facility", cells = new RectInt(47, 27, 3, 2) },
            new OutpostZone { name = "Battery Storage", cells = new RectInt(48, 30, 2, 2) },
            new OutpostZone { name = "Main Habitat", cells = new RectInt(43, 31, 4, 3) },
            new OutpostZone { name = "Greenhouse", cells = new RectInt(38, 32, 4, 3) },
            new OutpostZone { name = "Radiation Shelter", cells = new RectInt(48, 33, 3, 3) },
            new OutpostZone { name = "Solar Arrays", cells = new RectInt(43, 35, 4, 2) },
        };

        public RectInt OutpostBounds
        {
            get
            {
                if (zones.Count == 0) return new RectInt(0, 0, 0, 0);
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                foreach (var z in zones)
                {
                    x0 = Mathf.Min(x0, z.cells.xMin); y0 = Mathf.Min(y0, z.cells.yMin);
                    x1 = Mathf.Max(x1, z.cells.xMax); y1 = Mathf.Max(y1, z.cells.yMax);
                }
                return new RectInt(x0, y0, x1 - x0, y1 - y0);
            }
        }

        public bool InOutpostArea(int x, int y, int pad)
        {
            var b = OutpostBounds;
            return x >= b.xMin - pad && x < b.xMax + pad && y >= b.yMin - pad && y < b.yMax + pad;
        }

        public float DistanceToOutpost(int x, int y)
        {
            var b = OutpostBounds;
            float dx = Mathf.Max(0, Mathf.Max(b.xMin - x, x - (b.xMax - 1)));
            float dy = Mathf.Max(0, Mathf.Max(b.yMin - y, y - (b.yMax - 1)));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public bool InZone(int x, int y, int pad)
        {
            foreach (var z in zones)
            {
                var r = z.cells;
                if (x >= r.xMin - pad && x < r.xMax + pad && y >= r.yMin - pad && y < r.yMax + pad) return true;
            }
            return false;
        }

        public bool InBounds(float x, float y, float rx, float ry, int w, int h)
        {
            return x - rx >= edgeMargin && x + rx <= w - edgeMargin && y - ry >= edgeMargin && y + ry <= h - edgeMargin;
        }

        public bool ZonesValid(int w, int h, out string error)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                var a = zones[i].cells;
                if (a.xMin < 0 || a.yMin < 0 || a.xMax > w || a.yMax > h)
                {
                    error = zones[i].name + " outside map";
                    return false;
                }
                for (int j = i + 1; j < zones.Count; j++)
                {
                    var b = zones[j].cells;
                    bool apart = a.xMax + zoneGap <= b.xMin || b.xMax + zoneGap <= a.xMin ||
                                 a.yMax + zoneGap <= b.yMin || b.yMax + zoneGap <= a.yMin;
                    if (!apart)
                    {
                        error = zones[i].name + " too close to " + zones[j].name;
                        return false;
                    }
                }
            }
            error = null;
            return true;
        }
    }
}
