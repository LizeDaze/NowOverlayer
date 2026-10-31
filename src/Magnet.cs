using System.Collections.Generic;
using UnityEngine;

namespace NowOverlayer
{
    // Snapping for the editor. Works in GUI screen pixels (y down).
    // Candidates: screen edges (with margin) and center, other items' edges and centers,
    // and "same gap" positions that repeat the spacing between two other items.
    public static class Magnet
    {
        public enum Kind { Screen, Element, Spacing }

        public struct Guide
        {
            public Rect Line;
            public Kind Kind;
            public string Label;
        }

        private struct Cand
        {
            public float Pos;
            public Kind Kind;
            public Rect Other;     // element the line comes from (Element kind)
            public Rect GapA, GapB; // the two items whose gap is repeated (Spacing kind)
            public float Gap;
        }

        public static readonly Color ScreenColor = new Color(1f, 0.3f, 0.6f, 0.95f);
        public static readonly Color ElementColor = new Color(0.2f, 0.9f, 0.85f, 0.95f);
        public static readonly Color SpacingColor = new Color(1f, 0.82f, 0.25f, 0.95f);

        // The game screen as drawn now (GUI px): the whole window, or the fitted canvas while the editor shrinks it.
        public static Rect Area => OverlayEditor.Board;

        // Returns the screen-pixel shift to apply to r and fills guides.
        public static Vector2 Snap(Rect r, List<Rect> others, float scale, Settings s, List<Guide> guides)
        {
            guides.Clear();
            var xs = new List<Cand>();
            var ys = new List<Cand>();
            Rect a = Area;

            if (s.MagnetScreen)
            {
                float m = s.MagnetMargin * scale;
                foreach (float x in new[] { a.xMin, a.xMin + m, a.center.x, a.xMax - m, a.xMax }) xs.Add(new Cand { Pos = x, Kind = Kind.Screen });
                foreach (float y in new[] { a.yMin, a.yMin + m, a.center.y, a.yMax - m, a.yMax }) ys.Add(new Cand { Pos = y, Kind = Kind.Screen });
            }
            if (s.MagnetElements)
            {
                foreach (Rect o in others)
                {
                    foreach (float x in new[] { o.xMin, o.center.x, o.xMax }) xs.Add(new Cand { Pos = x, Kind = Kind.Element, Other = o });
                    foreach (float y in new[] { o.yMin, o.center.y, o.yMax }) ys.Add(new Cand { Pos = y, Kind = Kind.Element, Other = o });
                }
            }
            if (s.MagnetSpacing) AddSpacing(r, others, xs, ys);

            float th = s.MagnetRange;
            Vector2 shift = Vector2.zero;
            if (Best(r.xMin, r.center.x, r.xMax, xs, th, out Cand cx, out float dx)) shift.x = dx;
            if (Best(r.yMin, r.center.y, r.yMax, ys, th, out Cand cy, out float dy)) shift.y = dy;

            Rect moved = new Rect(r.x + shift.x, r.y + shift.y, r.width, r.height);
            if (shift.x != 0f || HasExact(moved, xs, true, out cx)) AddGuide(guides, cx, moved, true, scale);
            if (shift.y != 0f || HasExact(moved, ys, false, out cy)) AddGuide(guides, cy, moved, false, scale);
            return shift;
        }

        private static void AddSpacing(Rect r, List<Rect> others, List<Cand> xs, List<Cand> ys)
        {
            for (int i = 0; i < others.Count; i++)
            for (int j = 0; j < others.Count; j++)
            {
                if (i == j) continue;
                Rect a = others[i], b = others[j];
                // a above b, overlapping horizontally: repeat the gap below b / above a
                if (b.yMin >= a.yMax && Overlap(a.xMin, a.xMax, b.xMin, b.xMax) && Overlap(r.xMin, r.xMax, b.xMin, b.xMax))
                {
                    float g = b.yMin - a.yMax;
                    if (g < 400f)
                    {
                        ys.Add(new Cand { Pos = b.yMax + g, Kind = Kind.Spacing, GapA = a, GapB = b, Gap = g });
                    }
                }
                // a left of b, overlapping vertically: repeat the gap right of b
                if (b.xMin >= a.xMax && Overlap(a.yMin, a.yMax, b.yMin, b.yMax) && Overlap(r.yMin, r.yMax, b.yMin, b.yMax))
                {
                    float g = b.xMin - a.xMax;
                    if (g < 600f)
                    {
                        xs.Add(new Cand { Pos = b.xMax + g, Kind = Kind.Spacing, GapA = a, GapB = b, Gap = g });
                    }
                }
            }
        }

        private static bool Overlap(float a0, float a1, float b0, float b1) => a0 < b1 && b0 < a1;

        // Spacing candidates only match the leading edge (top / left); the rest match min, center or max.
        private static bool Best(float min, float mid, float max, List<Cand> cands, float th, out Cand best, out float delta)
        {
            best = default;
            delta = 0f;
            float bestAbs = th + 0.001f;
            bool found = false;
            foreach (Cand c in cands)
            {
                foreach (float v in c.Kind == Kind.Spacing ? new[] { min } : new[] { min, mid, max })
                {
                    float d = c.Pos - v;
                    float ad = Mathf.Abs(d);
                    // prefer item alignment over screen lines at equal distance
                    if (ad < bestAbs || (Mathf.Approximately(ad, bestAbs) && c.Kind != Kind.Screen))
                    {
                        bestAbs = ad;
                        best = c;
                        delta = d;
                        found = true;
                    }
                }
            }
            return found;
        }

        private static bool HasExact(Rect r, List<Cand> cands, bool xAxis, out Cand hit)
        {
            foreach (Cand c in cands)
            {
                float[] vals = xAxis ? new[] { r.xMin, r.center.x, r.xMax } : new[] { r.yMin, r.center.y, r.yMax };
                foreach (float v in vals)
                {
                    if (Mathf.Abs(c.Pos - v) < 0.5f)
                    {
                        hit = c;
                        return true;
                    }
                }
            }
            hit = default;
            return false;
        }

        private static void AddGuide(List<Guide> guides, Cand c, Rect r, bool vertical, float scale)
        {
            switch (c.Kind)
            {
                case Kind.Screen:
                    guides.Add(new Guide
                    {
                        Kind = Kind.Screen,
                        Line = vertical ? new Rect(c.Pos, Area.y, 1, Area.height) : new Rect(Area.x, c.Pos, Area.width, 1)
                    });
                    break;
                case Kind.Element:
                    if (vertical)
                    {
                        float y0 = Mathf.Min(r.yMin, c.Other.yMin), y1 = Mathf.Max(r.yMax, c.Other.yMax);
                        guides.Add(new Guide { Kind = Kind.Element, Line = new Rect(c.Pos, y0, 1, y1 - y0) });
                    }
                    else
                    {
                        float x0 = Mathf.Min(r.xMin, c.Other.xMin), x1 = Mathf.Max(r.xMax, c.Other.xMax);
                        guides.Add(new Guide { Kind = Kind.Element, Line = new Rect(x0, c.Pos, x1 - x0, 1) });
                    }
                    break;
                case Kind.Spacing:
                    string label = Mathf.RoundToInt(c.Gap / scale).ToString();
                    if (vertical)
                    {
                        float y = r.center.y;
                        guides.Add(new Guide { Kind = Kind.Spacing, Line = new Rect(c.GapA.xMax, y, c.GapB.xMin - c.GapA.xMax, 2), Label = label });
                        guides.Add(new Guide { Kind = Kind.Spacing, Line = new Rect(c.GapB.xMax, y, r.xMin - c.GapB.xMax, 2), Label = label });
                    }
                    else
                    {
                        float x = r.center.x;
                        guides.Add(new Guide { Kind = Kind.Spacing, Line = new Rect(x, c.GapA.yMax, 2, c.GapB.yMin - c.GapA.yMax), Label = label });
                        guides.Add(new Guide { Kind = Kind.Spacing, Line = new Rect(x, c.GapB.yMax, 2, r.yMin - c.GapB.yMax), Label = label });
                    }
                    break;
            }
        }

        public static void Draw(List<Guide> guides)
        {
            foreach (Guide g in guides)
            {
                Color c = g.Kind == Kind.Screen ? ScreenColor : g.Kind == Kind.Element ? ElementColor : SpacingColor;
                Ui.DrawRect(g.Line, c);
                if (g.Kind == Kind.Element)
                {
                    // small end caps like design tools
                    bool vertical = g.Line.width <= 1f;
                    Ui.DrawRect(vertical ? new Rect(g.Line.x - 3, g.Line.y, 7, 1) : new Rect(g.Line.x, g.Line.y - 3, 1, 7), c);
                    Ui.DrawRect(vertical ? new Rect(g.Line.x - 3, g.Line.yMax - 1, 7, 1) : new Rect(g.Line.xMax - 1, g.Line.y - 3, 1, 7), c);
                }
                if (!string.IsNullOrEmpty(g.Label) && (g.Line.width > 14f || g.Line.height > 14f))
                {
                    var box = new Rect(g.Line.center.x - 18f, g.Line.center.y - 10f, 36f, 20f);
                    Ui.DrawRect(box, SpacingColor);
                    GUI.Label(box, g.Label, Ui.GuideText);
                }
            }
        }
    }
}
