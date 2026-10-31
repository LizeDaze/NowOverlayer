using System;
using UnityEngine;

namespace NowOverlayer
{
    // IMGUI kit, "studio console" look: near-black page, hairline-bordered cards, a numbered side rail,
    // red accent with a soft glow, filled slider tracks and value chips.
    // Text uses the skin's default font: in Unity 6 IMGUI a font from CreateDynamicFontFromOSFont drew no text
    // (every label repeated the last string UMM drew, "Options").
    public static class Ui
    {
        public static readonly Color PageColor = Hex("0C0C11");
        public static readonly Color CardColor = Hex("15151C");
        public static readonly Color CardStroke = Hex("24242E");
        public static readonly Color TextColor = Hex("EDEDF2");
        public static readonly Color SubColor = Hex("8A8A99");
        public static readonly Color FaintColor = Hex("55556A");
        public static readonly Color Accent = Hex("FF4757");
        public static readonly Color AccentDark = Hex("E0303F");
        public static readonly Color AccentSoft = Hex("2C151A");
        public static readonly Color AccentText = Hex("FF8792");
        public static readonly Color LineColor = Hex("272731");
        public static readonly Color GhostColor = Hex("1C1C24");
        public static readonly Color Good = Hex("3DDC84");

        public static GUIStyle Page, Window, Card, Title, H2, Body, Sub, Primary, Secondary, Ghost, Danger, Field, Area,
            Chip, Row, RowOn, Track, Thumb, Pill, Knob, Swatch, Outline,
            ThumbBox, ThumbOn, AccentBox, ChipText, ThumbLabel, ThumbLabelOn, Preview, Tab, TabOn, Fab, GuideText;

        public static readonly Color StageColor = Hex("08080B");

        public static GUIStyle SegBox, SegOn, SegOff, Caption, KeyChip,
            Rail, RailItem, RailOn, Brand, PageTitle, PageSub, ValueChip, StatusBar, StatusText, TrackBg, TrackFill, Glow;

        // Equal-width segmented control.
        public static bool Segmented(ref int current, params string[] names)
        {
            int before = current;
            GUILayout.BeginHorizontal(SegBox);
            for (int i = 0; i < names.Length; i++)
            {
                if (GUILayout.Button(names[i], i == current ? SegOn : SegOff, GUILayout.ExpandWidth(true), GUILayout.Height(32))) current = i;
            }
            GUILayout.EndHorizontal();
            return before != current;
        }

        // Wrapping row: items go on the next line instead of pushing the panel wider than the screen.
        // FlowWidth is the room inside a card; the panel that draws sets it.
        public static float FlowWidth = 560f;
        private static float _flowX;

        public static void FlowBegin()
        {
            _flowX = 0f;
            GUILayout.BeginHorizontal();
        }

        public static void FlowItem(float width)
        {
            if (_flowX > 0f && _flowX + width > FlowWidth)
            {
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                _flowX = 0f;
            }
            _flowX += width;
        }

        public static void FlowEnd()
        {
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // Caption with a red tick above a hairline card; close with GUILayout.EndVertical().
        public static void Section(string caption)
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            Rect tick = GUILayoutUtility.GetRect(3, 12, GUILayout.Width(3), GUILayout.Height(12));
            tick.y += 4;
            DrawRect(tick, Accent);
            GUILayout.Space(8);
            GUILayout.Label(caption, Caption);
            GUILayout.EndHorizontal();
            GUILayout.BeginVertical(Card);
        }

        // Side-rail navigation: numbered rows, the open one lit with a red bar and glow.
        public static bool RailNav(ref int current, string[] names)
        {
            int before = current;
            for (int i = 0; i < names.Length; i++)
            {
                bool on = i == current;
                string num = (i + 1).ToString("00");
                string text = "<size=11><color=#" + (on ? "FF4757" : "4A4A5C") + ">" + num + "</color></size>    " + names[i];
                if (GUILayout.Button(text, on ? RailOn : RailItem, GUILayout.Height(40))) current = i;
                if (on && Event.current.type == EventType.Repaint)
                {
                    Rect r = GUILayoutUtility.GetLastRect();
                    DrawRect(new Rect(r.x, r.y + 9, 3, r.height - 18), Accent);
                }
            }
            return before != current;
        }

        // Colored dot + label for the status bar.
        public static void StatusDot(string label, Color c)
        {
            Rect d = GUILayoutUtility.GetRect(8, 8, GUILayout.Width(8), GUILayout.Height(18));
            d.y += 5;
            d.height = 8;
            GUI.color = c;
            GUI.DrawTexture(d, Dot);
            GUI.color = Color.white;
            GUILayout.Space(6);
            GUILayout.Label(label, StatusText);
            GUILayout.Space(14);
        }

        public static bool Tabs(ref int current, params string[] names)
        {
            int before = current;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++)
            {
                if (GUILayout.Button(names[i], i == current ? TabOn : Tab, GUILayout.Height(36))) current = i;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return before != current;
        }

        public static Texture2D White, Dot;

        public static void Init()
        {
            if (Page != null && Page.normal.background) return;

            White = Solid(Color.white);
            Dot = Circle(Color.white, 16, Color.white, false);

            Page = Stroke(PageColor, CardStroke, 18, new RectOffset(16, 16, 16, 16));
            Window = Stroke(PageColor, CardStroke, 18, new RectOffset(0, 0, 0, 0));
            Card = Stroke(CardColor, CardStroke, 14, new RectOffset(18, 18, 14, 14));
            Card.margin = new RectOffset(0, 0, 0, 8);

            Title = Text(20, TextColor, FontStyle.Bold);
            H2 = Text(15, TextColor, FontStyle.Bold);
            H2.margin = new RectOffset(0, 0, 0, 8);
            H2.wordWrap = true; // long card titles pushed the editor panel wider than its area
            Body = Text(14, TextColor, FontStyle.Normal);
            Sub = Text(12, SubColor, FontStyle.Normal);
            Sub.wordWrap = true;

            Primary = Gradient(Hex("FF5A69"), Hex("E8344A"), Hex("FF6E7B"), Color.white, 14, 10);
            Secondary = Button(AccentSoft, Hex("3A1A21"), AccentText, 14, 10, Hex("4A2029"));
            Ghost = Button(GhostColor, Hex("24242E"), Hex("C9C9D3"), 13, 10, LineColor);
            Ghost.clipping = TextClipping.Clip;
            Danger = Button(Hex("1E1416"), Hex("2A171B"), Accent, 13, 10, Hex("3A1F24"));
            Chip = Button(GhostColor, Hex("24242E"), Hex("C9C9D3"), 12, 8, LineColor);
            Chip.padding = new RectOffset(10, 10, 6, 6);
            Chip.margin = new RectOffset(0, 6, 0, 6);

            Field = Stroke(Hex("101016"), LineColor, 9, new RectOffset(12, 12, 9, 9));
            Field.fontSize = 14;
            Field.normal.textColor = Field.focused.textColor = Field.hover.textColor = TextColor;
            Field.focused.background = RoundStroke(Hex("15121A"), Accent, 9);
            Field.hover.background = RoundStroke(Hex("101016"), Hex("34343F"), 9);
            Area = new GUIStyle(Field) { wordWrap = true };

            Row = Box(new Color(0, 0, 0, 0), 10, new RectOffset(10, 10, 9, 9));
            Row.fontSize = 14;
            Row.richText = true;
            Row.alignment = TextAnchor.MiddleLeft;
            Row.clipping = TextClipping.Clip; // long file / device names are cut, not widening the panel
            Row.normal.textColor = TextColor;
            Row.hover.background = Round(Hex("1D1D26"), 10);
            Row.hover.textColor = TextColor;
            RowOn = new GUIStyle(Row);
            RowOn.normal.background = RowOn.hover.background = RoundStroke(AccentSoft, Hex("47202A"), 10);
            RowOn.normal.textColor = RowOn.hover.textColor = AccentText;
            RowOn.fontStyle = FontStyle.Bold;

            // slider: the track is drawn by hand (background + red fill), the IMGUI slider only moves the knob
            Track = new GUIStyle { fixedHeight = 22, margin = new RectOffset(0, 0, 4, 8) };
            TrackBg = Box(Hex("262630"), 3, new RectOffset(0, 0, 0, 0));
            TrackFill = Gradient(Hex("FF6B78"), Accent, Accent, Color.white, 12, 3);
            Thumb = new GUIStyle { fixedWidth = 18, fixedHeight = 18 };
            Thumb.normal.background = Thumb.hover.background = Thumb.active.background = Circle(Color.white, 36, Accent, true);
            ValueChip = Box(GhostColor, 6, new RectOffset(8, 8, 3, 3));
            ValueChip.fontSize = 12;
            ValueChip.fontStyle = FontStyle.Bold;
            ValueChip.normal.textColor = AccentText;
            ValueChip.alignment = TextAnchor.MiddleCenter;
            ValueChip.margin = new RectOffset(0, 0, 2, 2);

            Pill = Box(Color.white, 12, new RectOffset(0, 0, 0, 0)); // tinted with GUI.color
            Knob = new GUIStyle();
            Knob.normal.background = Circle(Color.white, 36, Color.white, false);
            Glow = new GUIStyle();
            Glow.normal.background = Halo(64);
            Swatch = new GUIStyle { fixedWidth = 26, fixedHeight = 26, margin = new RectOffset(0, 6, 0, 6) };
            Outline = new GUIStyle();

            // animation thumbs / gallery cards
            ThumbBox = Stroke(GhostColor, LineColor, 12, new RectOffset(0, 0, 0, 0));
            ThumbBox.margin = new RectOffset(0, 8, 0, 8);
            ThumbOn = Stroke(AccentSoft, Accent, 12, new RectOffset(0, 0, 0, 0));
            ThumbOn.margin = ThumbBox.margin;
            AccentBox = Box(Accent, 8, new RectOffset(0, 0, 0, 0));
            ChipText = Text(14, Color.white, FontStyle.Bold);
            ChipText.alignment = TextAnchor.MiddleCenter;
            ThumbLabel = Text(12, Hex("C9C9D3"), FontStyle.Normal);
            ThumbLabel.alignment = TextAnchor.MiddleCenter;
            ThumbLabelOn = Text(12, AccentText, FontStyle.Bold);
            ThumbLabelOn.alignment = TextAnchor.MiddleCenter;
            Preview = Text(17, Color.white, FontStyle.Bold);
            Preview.alignment = TextAnchor.MiddleCenter;
            Preview.wordWrap = false;

            Tab = Button(GhostColor, Hex("24242E"), SubColor, 13, 10, LineColor);
            Tab.margin = new RectOffset(0, 6, 0, 0);
            TabOn = Button(AccentSoft, AccentSoft, AccentText, 13, 10, Accent);
            TabOn.margin = Tab.margin;

            GuideText = Text(11, Hex("111116"), FontStyle.Bold);
            GuideText.alignment = TextAnchor.MiddleCenter;

            InitLayout();

            Fab = Gradient(Hex("FF5A69"), Hex("E8344A"), Hex("FF6E7B"), Color.white, 30, 28);
            Fab.padding = new RectOffset(0, 0, 0, 4);
        }

        private static void InitLayout()
        {
            SegBox = Stroke(Hex("101016"), LineColor, 10, new RectOffset(3, 3, 3, 3));
            SegBox.margin = new RectOffset(0, 0, 0, 6);
            SegOn = Button(AccentSoft, AccentSoft, AccentText, 13, 8, Hex("4A2029"));
            SegOn.margin = new RectOffset(0, 0, 0, 0);
            SegOn.padding = new RectOffset(6, 6, 7, 7);
            SegOff = Button(new Color(0, 0, 0, 0), Hex("1D1D26"), SubColor, 13, 8, new Color(0, 0, 0, 0));
            SegOff.margin = SegOn.margin;
            SegOff.padding = SegOn.padding;
            SegOff.fontStyle = FontStyle.Normal;
            SegOff.hover.textColor = TextColor;
            Caption = Text(12, Hex("A6A6B5"), FontStyle.Bold);
            Caption.margin = new RectOffset(0, 0, 0, 6);
            KeyChip = Stroke(Hex("1F1F28"), Hex("34343F"), 7, new RectOffset(10, 10, 5, 6));
            KeyChip.fontSize = 13;
            KeyChip.fontStyle = FontStyle.Bold;
            KeyChip.alignment = TextAnchor.MiddleCenter;
            KeyChip.normal.textColor = TextColor;
            KeyChip.margin = new RectOffset(0, 6, 2, 2);

            Rail = new GUIStyle { padding = new RectOffset(12, 12, 18, 18) };
            Rail.normal.background = RoundLeft(Hex("0F0F15"), 18);
            Rail.border = new RectOffset(18, 2, 18, 18);
            RailItem = Box(new Color(0, 0, 0, 0), 9, new RectOffset(16, 10, 0, 0));
            RailItem.fontSize = 14;
            RailItem.richText = true;
            RailItem.alignment = TextAnchor.MiddleLeft;
            RailItem.normal.textColor = SubColor;
            RailItem.hover.background = Round(Hex("191920"), 9);
            RailItem.hover.textColor = TextColor;
            RailItem.margin = new RectOffset(0, 0, 0, 2);
            RailOn = new GUIStyle(RailItem);
            RailOn.normal.background = RailOn.hover.background = Round(Hex("1E1418"), 9);
            RailOn.normal.textColor = RailOn.hover.textColor = Color.white;
            RailOn.fontStyle = FontStyle.Bold;
            Brand = Text(17, TextColor, FontStyle.Bold);
            Brand.margin = new RectOffset(6, 0, 0, 0);
            PageTitle = Text(24, TextColor, FontStyle.Bold);
            PageTitle.margin = new RectOffset(0, 0, 0, 2);
            PageSub = Text(13, SubColor, FontStyle.Normal);
            PageSub.wordWrap = true;
            PageSub.margin = new RectOffset(0, 0, 0, 8);
            StatusBar = new GUIStyle { padding = new RectOffset(18, 18, 9, 10) };
            StatusText = Text(12, SubColor, FontStyle.Normal);
            StatusText.margin = new RectOffset(0, 0, 1, 0);
        }

        // ---- widgets ----

        public static bool Switch(string label, bool value, string sub = null)
        {
            GUILayout.BeginHorizontal(Row);
            GUILayout.BeginVertical();
            GUILayout.Label(label, Body);
            if (!string.IsNullOrEmpty(sub)) GUILayout.Label(sub, Sub);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            Rect r = GUILayoutUtility.GetRect(42, 24, GUILayout.Width(42), GUILayout.Height(24));
            GUILayout.EndHorizontal();
            Rect row = GUILayoutUtility.GetLastRect();

            if (Event.current.type == EventType.Repaint)
            {
                if (value)
                {
                    GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
                    GUI.Box(new Rect(r.x - 8, r.y - 8, r.width + 16, r.height + 16), GUIContent.none, Glow);
                }
                GUI.color = value ? Accent : Hex("2C2C37");
                GUI.Box(r, GUIContent.none, Pill);
                GUI.color = Color.white;
                float kx = value ? r.xMax - 21 : r.x + 3;
                GUI.Box(new Rect(kx, r.y + 3, 18, 18), GUIContent.none, Knob);
            }

            Event e = Event.current;
            if (e.type == EventType.MouseUp && e.button == 0 && row.Contains(e.mousePosition))
            {
                value = !value;
                e.Use();
            }
            return value;
        }

        public static float Slider(string label, float value, float min, float max, string format = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Body);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value.ToString(format), ValueChip, GUILayout.MinWidth(46));
            GUILayout.EndHorizontal();
            Rect r = GUILayoutUtility.GetRect(10, 22, Track, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                float t = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
                var bar = new Rect(r.x, r.y + 9, r.width, 5);
                GUI.Box(bar, GUIContent.none, TrackBg);
                if (t > 0f) GUI.Box(new Rect(bar.x, bar.y, Mathf.Max(6f, bar.width * t), bar.height), GUIContent.none, TrackFill);
            }
            return GUI.HorizontalSlider(r, value, min, max, GUIStyle.none, Thumb);
        }

        public static float NumberField(string label, float value, float width = 90)
        {
            GUILayout.Label(label, Sub, GUILayout.Width(18));
            string s = GUILayout.TextField(value.ToString("0.#"), Field, GUILayout.Width(width));
            return float.TryParse(s, out float v) ? v : value;
        }

        public static bool ColorSwatch(Color c, bool selected)
        {
            Rect r = GUILayoutUtility.GetRect(26, 26, Swatch);
            if (selected)
            {
                DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Accent);
                DrawRect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), CardColor);
            }
            DrawRect(r, c);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static void Divider()
        {
            Rect r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            DrawRect(new Rect(r.x, r.y + 6, r.width, 1), LineColor);
            GUILayout.Space(12);
        }

        public static void DrawRect(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        public static void DrawFrame(Rect r, Color c, float w)
        {
            DrawRect(new Rect(r.x, r.y, r.width, w), c);
            DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            DrawRect(new Rect(r.x, r.y, w, r.height), c);
            DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        // ---- style + texture builders ----

        private static GUIStyle Text(int size, Color color, FontStyle style)
        {
            var s = new GUIStyle { fontSize = size, fontStyle = style, richText = true };
            s.normal.textColor = color;
            s.margin = new RectOffset(0, 0, 2, 2);
            return s;
        }

        private static GUIStyle Box(Color c, int radius, RectOffset padding)
        {
            var s = new GUIStyle { padding = padding, border = new RectOffset(radius, radius, radius, radius) };
            s.normal.background = Round(c, radius);
            return s;
        }

        // Rounded box with a 1px hairline border.
        private static GUIStyle Stroke(Color fill, Color stroke, int radius, RectOffset padding)
        {
            var s = new GUIStyle { padding = padding, border = new RectOffset(radius, radius, radius, radius) };
            s.normal.background = RoundStroke(fill, stroke, radius);
            return s;
        }

        private static GUIStyle Button(Color bg, Color hover, Color text, int size, int radius, Color stroke)
        {
            var s = new GUIStyle
            {
                padding = new RectOffset(16, 16, 10, 10),
                border = new RectOffset(radius, radius, radius, radius),
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                margin = new RectOffset(0, 0, 4, 4)
            };
            s.normal.background = RoundStroke(bg, stroke, radius);
            s.hover.background = s.active.background = RoundStroke(hover, stroke.a > 0f ? Color.Lerp(stroke, Color.white, 0.12f) : stroke, radius);
            s.normal.textColor = s.hover.textColor = s.active.textColor = text;
            return s;
        }

        // Vertical gradient button (top → bottom) with a lighter hover.
        private static GUIStyle Gradient(Color top, Color bottom, Color hoverTop, Color text, int size, int radius)
        {
            var s = new GUIStyle
            {
                padding = new RectOffset(16, 16, 10, 10),
                border = new RectOffset(radius, radius, radius, radius),
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                margin = new RectOffset(0, 0, 4, 4)
            };
            s.normal.background = RoundGradient(top, bottom, radius);
            s.hover.background = s.active.background = RoundGradient(hoverTop, top, radius);
            s.normal.textColor = s.hover.textColor = s.active.textColor = text;
            return s;
        }

        private static Texture2D New(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return t;
        }

        private static Texture2D Solid(Color c)
        {
            Texture2D t = New(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // Distance from a pixel to the inside of a rounded square of side `size` (negative = inside).
        private static float RoundDist(int x, int y, int size, int r)
        {
            float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
            return Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - r;
        }

        // Rounded square, meant to be 9-sliced with border = radius.
        public static Texture2D Round(Color c, int r)
        {
            int size = r * 2 + 2;
            Texture2D t = New(size, size);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = Mathf.Clamp01(0.5f - RoundDist(x, y, size, r));
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * a));
            }
            t.Apply();
            return t;
        }

        // Rounded square with a 1px border; edge pixels (straight sides) carry the stroke when 9-sliced.
        private static Texture2D RoundStroke(Color fill, Color stroke, int r)
        {
            int size = r * 2 + 2;
            Texture2D t = New(size, size);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundDist(x, y, size, r);
                float a = Mathf.Clamp01(0.5f - d);
                float edge = Mathf.Clamp01(d + 1.5f); // 1 at the rim, 0 a pixel inside
                Color c = Color.Lerp(fill, stroke, edge * stroke.a);
                c.a = Mathf.Max(fill.a, stroke.a * edge) * a;
                t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        // Rounded square with a vertical gradient; tall enough that 9-slicing keeps the fade.
        private static Texture2D RoundGradient(Color top, Color bottom, int r)
        {
            int w = r * 2 + 2, h = 64;
            Texture2D t = New(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = Mathf.Clamp(y + 0.5f, r, h - r);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - r;
                Color c = Color.Lerp(bottom, top, y / (float)(h - 1));
                c.a *= Mathf.Clamp01(0.5f - d);
                t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        // Only the left corners rounded (side rail against the content).
        private static Texture2D RoundLeft(Color c, int r)
        {
            int size = r * 2 + 2;
            Texture2D t = New(size, size);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = x < size / 2 ? RoundDist(x, y, size, r) : -1f;
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(0.5f - d)));
            }
            t.Apply();
            return t;
        }

        // Soft radial glow (for the lit switch).
        private static Texture2D Halo(int size)
        {
            Texture2D t = New(size, size);
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r)) / r;
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            t.Apply();
            return t;
        }

        // Circle; `ring` = colored rim of a tenth of the size (slider knob).
        private static Texture2D Circle(Color fill, int size, Color rim, bool ring)
        {
            Texture2D t = New(size, size);
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                float a = Mathf.Clamp01(r - d + 0.5f);
                Color c = ring && d > r - size * 0.14f ? rim : fill;
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
            t.Apply();
            return t;
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }

        public static string ToHex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
