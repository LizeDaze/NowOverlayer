using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace NowOverlayer
{
    // The editor's Photoshop-like shell: options bar on top, tool strip on the left, rulers, a docked
    // properties panel over a layers panel on the right (Tab hides them), a status bar, and undo / redo.
    // Everything that only paints runs on Repaint; styles are built once.
    public partial class OverlayEditor
    {
        private enum Tool
        {
            Move,
            Marquee,
            Text,
            Crop
        }

        private Tool _tool = Tool.Move;
        private bool _panelsHidden, _chromeHidden;
        private bool _fit = true; // 화면 맞춤: shrink the game screen into the free area

        private const float TopH = 40f, ToolW = 46f, StatusH = 24f, DockW = 352f, RulerT = 18f;

        private static readonly Color BarColor = new Color32(0x1C, 0x1C, 0x21, 0xFF);
        private static readonly Color BarLine = new Color32(0x30, 0x30, 0x38, 0xFF);
        private static readonly Color DockColor = new Color32(0x15, 0x15, 0x19, 0xFA);
        private static readonly Color HeadColor = new Color32(0x22, 0x22, 0x28, 0xFF);
        private static readonly Color RulerColor = new Color32(0x1A, 0x1A, 0x1F, 0xE6);
        private static readonly Color TickColor = new Color32(0x6A, 0x6A, 0x78, 0xFF);

        private static GUIStyle _tbBtn, _tbOn, _barLabel, _barBtn, _rulerText, _status, _statusRight, _tip, _headTab, _headTabOn, _layerName, _layerSub, _eye;

        private static void InitChrome()
        {
            if (_tbBtn != null) return;
            _tbBtn = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            _tbBtn.normal.textColor = new Color(0.78f, 0.78f, 0.84f);
            _tbBtn.hover.background = Ui.Round(new Color32(0x2C, 0x2C, 0x34, 0xFF), 6);
            _tbBtn.hover.textColor = Color.white;
            _tbBtn.border = new RectOffset(6, 6, 6, 6);
            _tbOn = new GUIStyle(_tbBtn);
            _tbOn.normal.background = _tbOn.hover.background = Ui.Round(new Color32(0x3A, 0x1C, 0x22, 0xFF), 6);
            _tbOn.normal.textColor = _tbOn.hover.textColor = Ui.AccentText;

            _barLabel = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft, richText = true, clipping = TextClipping.Clip };
            _barLabel.normal.textColor = new Color(0.62f, 0.62f, 0.7f);
            _barBtn = new GUIStyle(Ui.Chip) { margin = new RectOffset(0, 6, 8, 0), fixedHeight = 24, padding = new RectOffset(10, 10, 3, 4) };
            _rulerText = new GUIStyle { fontSize = 9, alignment = TextAnchor.UpperLeft };
            _rulerText.normal.textColor = new Color(0.62f, 0.62f, 0.7f);
            _status = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleLeft, richText = true, padding = new RectOffset(10, 10, 0, 0) };
            _status.normal.textColor = new Color(0.62f, 0.62f, 0.7f);
            _statusRight = new GUIStyle(_status) { alignment = TextAnchor.MiddleRight };
            _tip = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 3, 4) };
            _tip.normal.textColor = Color.white;
            _tip.normal.background = Ui.Round(new Color32(0x10, 0x10, 0x14, 0xF0), 5);
            _tip.border = new RectOffset(5, 5, 5, 5);
            _headTab = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 0, 0) };
            _headTab.normal.textColor = new Color(0.6f, 0.6f, 0.68f);
            _headTab.hover.textColor = Color.white;
            _headTabOn = new GUIStyle(_headTab) { fontStyle = FontStyle.Bold };
            _headTabOn.normal.textColor = _headTabOn.hover.textColor = Color.white;
            _headTabOn.normal.background = _headTabOn.hover.background = Ui.Round(DockColor, 2);
            _layerName = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleLeft, richText = true, clipping = TextClipping.Clip };
            _layerName.normal.textColor = new Color(0.88f, 0.88f, 0.92f);
            _layerSub = new GUIStyle(_layerName) { fontSize = 10 };
            _layerSub.normal.textColor = new Color(0.55f, 0.55f, 0.63f);
            _eye = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _eye.normal.textColor = new Color(0.8f, 0.8f, 0.86f);
        }

        // ---- frame ----

        private void OnGUI()
        {
            if (!Active || !Overlay.Instance) return;
            Ui.Init();
            InitChrome();
            GUI.depth = -1000;
            Event e = Event.current;
            bool paint = e.type == EventType.Repaint;
            float w = Screen.width, h = Screen.height;
            // Tab: everything hidden except the status bar, so items under the bars can be reached
            var none = new Rect(-1f, -1f, 0f, 0f);
            bool chrome = !_chromeHidden;
            var top = chrome ? new Rect(0f, 0f, w, TopH) : none;
            var tools = chrome ? new Rect(0f, TopH, ToolW, h - TopH - StatusH) : none;
            var status = new Rect(0f, h - StatusH, w, StatusH);
            var dock = _panelsHidden || !chrome ? none : new Rect(w - DockW, TopH, DockW, h - TopH - StatusH);
            float right = dock.width > 0f ? dock.x : w;

            // fitted board: the game screen shrunk into the space the bars leave free, so nothing hides under them
            const float gap = 14f;
            UpdateBoard(chrome && _fit
                ? new Rect(ToolW + RulerT + gap, TopH + RulerT + gap, right - ToolW - RulerT - gap * 2f, h - TopH - RulerT - StatusH - gap * 2f)
                : (Rect?)null);

            if (paint)
            {
                Ui.DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.16f)); // light dim so boxes read on any background
                if (chrome && _fit)
                {
                    Rect b = Board;
                    Ui.DrawFrame(new Rect(b.x - 1f, b.y - 1f, b.width + 2f, b.height + 2f), new Color(1f, 1f, 1f, 0.18f), 1f);
                }
                if (chrome) DrawRulers(new Rect(ToolW, TopH, right - ToolW, h - TopH - StatusH));
                DrawGuides();
                DrawBoxes();
            }

            if (_gallery)
            {
                DrawGallery(e);
            }
            else
            {
                bool overUi = top.Contains(e.mousePosition) || tools.Contains(e.mousePosition) || status.Contains(e.mousePosition) || dock.Contains(e.mousePosition);
                // a panel click is about to change something: remember the layout first (undo)
                if (e.type == EventType.MouseDown && dock.Contains(e.mousePosition)) Checkpoint();
                if (!overUi || _dragging || _resizing || _marquee) HandleCanvasInput(e);
                HandleKeys(e);
            }

            if (chrome)
            {
                DrawTopBar(top);
                DrawToolStrip(tools);
            }
            DrawStatus(status);
            if (dock.width > 0f) DrawDock(dock);
            if (_gallery) DrawGalleryWindow();
        }

        private static void Tip(Vector2 at, string text)
        {
            var c = new GUIContent(text);
            Vector2 size = _tip.CalcSize(c);
            var r = new Rect(at.x, at.y, size.x, size.y);
            if (r.xMax > Screen.width - 4f) r.x = Screen.width - 4f - r.width;
            GUI.Label(r, c, _tip);
        }

        // ---- options bar ----

        private static readonly string[] ToolNames = { "이동 도구", "선택 윤곽 도구", "텍스트 도구", "자르기 도구" };
        private static readonly string[] ToolHints =
        {
            "클릭 선택 · 드래그 이동 · 빈 곳 드래그로 여러 개 · 모서리/가장자리 점으로 크기",
            "드래그한 사각형 안의 항목을 모두 선택해요 (항목 위에서 시작해도 선택만)",
            "화면을 클릭한 자리에 새 텍스트를 넣어요",
            "웹캠을 고르고 가장자리를 안쪽으로 끌면 그만큼 잘라요"
        };

        private void DrawTopBar(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Ui.DrawRect(r, BarColor);
                Ui.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), BarLine);
            }
            GUILayout.BeginArea(new Rect(r.x + 12f, r.y, r.width - 24f, r.height));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><color=#FF4757>NOW</color><color=#EDEDF2>OVERLAYER</color></b>", _barLabel, GUILayout.Height(TopH), GUILayout.Width(118));
            Sep();
            GUILayout.Label("<b><color=#EDEDF2>" + ToolNames[(int)_tool] + "</color></b>", _barLabel, GUILayout.Height(TopH), GUILayout.Width(92));
            Settings s = Main.Settings;
            if (_tool == Tool.Move || _tool == Tool.Marquee)
            {
                bool mag = GUILayout.Toggle(s.Magnet, s.Magnet ? "자석 켬" : "자석 끔", s.Magnet ? Ui.TabOn : _barBtn, GUILayout.Height(24));
                if (mag != s.Magnet) s.Magnet = mag;
                List<ElementView> sel = Selected();
                if (sel.Count > 1)
                {
                    float k = K;
                    if (GUILayout.Button("왼쪽", _barBtn)) { Checkpoint(); AlignX(0, k); }
                    if (GUILayout.Button("가운데", _barBtn)) { Checkpoint(); AlignX(1, k); }
                    if (GUILayout.Button("오른쪽", _barBtn)) { Checkpoint(); AlignX(2, k); }
                    if (GUILayout.Button("위", _barBtn)) { Checkpoint(); AlignY(0, k); }
                    if (GUILayout.Button("중앙", _barBtn)) { Checkpoint(); AlignY(1, k); }
                    if (GUILayout.Button("아래", _barBtn)) { Checkpoint(); AlignY(2, k); }
                }
            }
            // the hint gives way first (clipped) so the buttons on the right always fit on screen
            GUILayout.Label(ToolHints[(int)_tool], _barLabel, GUILayout.Height(TopH), GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            bool narrow = r.width < 1500f;
            GUI.enabled = _undo.Count > 0;
            if (GUILayout.Button("실행 취소", _barBtn)) Undo();
            GUI.enabled = _redo.Count > 0;
            if (GUILayout.Button("다시", _barBtn)) Redo();
            GUI.enabled = true;
            string fitLabel = narrow ? "맞춤 (F)" : _fit ? "화면 맞춤 켬 (F)" : "화면 맞춤 끔 (F)";
            if (GUILayout.Button(fitLabel, _fit ? Ui.TabOn : _barBtn, GUILayout.Height(24))) _fit = !_fit;
            if (_fit && GUILayout.Button(narrow ? "다시 찍기" : "배경 다시 찍기", _barBtn)) Reshoot();
            string panelLabel = narrow ? "패널" : _panelsHidden ? "패널 보기 (Shift+Tab)" : "패널 숨기기 (Shift+Tab)";
            if (GUILayout.Button(panelLabel, _panelsHidden ? Ui.TabOn : _barBtn, GUILayout.Height(24))) _panelsHidden = !_panelsHidden;
            GUILayout.BeginVertical();
            GUILayout.Space(5);
            if (GUILayout.Button("완료", Ui.Primary, GUILayout.Width(88), GUILayout.Height(30))) Close();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static void Sep()
        {
            Rect r = GUILayoutUtility.GetRect(13f, TopH, GUILayout.Width(13f));
            if (Event.current.type == EventType.Repaint) Ui.DrawRect(new Rect(r.x + 6f, r.y + 10f, 1f, r.height - 20f), BarLine);
        }

        // ---- tool strip (icons drawn with rects, no glyph fonts needed) ----

        private void DrawToolStrip(Rect r)
        {
            bool paint = Event.current.type == EventType.Repaint;
            if (paint)
            {
                Ui.DrawRect(r, BarColor);
                Ui.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), BarLine);
            }
            for (int i = 0; i < 4; i++)
            {
                var b = new Rect(r.x + 6f, r.y + 10f + i * 40f, 34f, 34f);
                bool on = (int)_tool == i;
                if (GUI.Button(b, i == 2 ? "T" : "", on ? _tbOn : _tbBtn)) _tool = (Tool)i;
                if (paint) ToolIcon(i, b, on ? Ui.AccentText : new Color(0.78f, 0.78f, 0.84f));
                if (b.Contains(Event.current.mousePosition) && paint)
                {
                    string key = i == 0 ? "V" : i == 1 ? "M" : i == 2 ? "T" : "C";
                    Tip(new Vector2(b.xMax + 8f, b.y + 7f), ToolNames[i] + "  (" + key + ")");
                }
            }
            var add = new Rect(r.x + 6f, r.y + 10f + 4 * 40f + 10f, 34f, 34f);
            if (paint) Ui.DrawRect(new Rect(r.x + 10f, add.y - 6f, 26f, 1f), BarLine);
            if (GUI.Button(add, "+", _tbBtn)) _gallery = true;
            if (add.Contains(Event.current.mousePosition) && paint) Tip(new Vector2(add.xMax + 8f, add.y + 7f), "항목 추가");
        }

        private static void ToolIcon(int i, Rect b, Color c)
        {
            float cx = b.center.x, cy = b.center.y;
            switch (i)
            {
                case 0: // move: cross with arrow heads
                    Ui.DrawRect(new Rect(cx - 8f, cy - 0.75f, 16f, 1.5f), c);
                    Ui.DrawRect(new Rect(cx - 0.75f, cy - 8f, 1.5f, 16f), c);
                    Ui.DrawRect(new Rect(cx - 2.5f, cy - 9f, 5f, 2f), c);
                    Ui.DrawRect(new Rect(cx - 2.5f, cy + 7f, 5f, 2f), c);
                    Ui.DrawRect(new Rect(cx - 9f, cy - 2.5f, 2f, 5f), c);
                    Ui.DrawRect(new Rect(cx + 7f, cy - 2.5f, 2f, 5f), c);
                    break;
                case 1: // marquee: dashed square
                    for (int k = 0; k < 4; k++)
                    {
                        Ui.DrawRect(new Rect(cx - 8f + k * 4.5f, cy - 8f, 2.5f, 1.5f), c);
                        Ui.DrawRect(new Rect(cx - 8f + k * 4.5f, cy + 6.5f, 2.5f, 1.5f), c);
                        Ui.DrawRect(new Rect(cx - 8f, cy - 8f + k * 4.5f, 1.5f, 2.5f), c);
                        Ui.DrawRect(new Rect(cx + 6.5f, cy - 8f + k * 4.5f, 1.5f, 2.5f), c);
                    }
                    break;
                case 3: // crop: two crossing L shapes
                    Ui.DrawRect(new Rect(cx - 5f, cy - 9f, 1.5f, 14f), c);
                    Ui.DrawRect(new Rect(cx - 5f, cy + 3.5f, 14f, 1.5f), c);
                    Ui.DrawRect(new Rect(cx - 9f, cy - 5f, 14f, 1.5f), c);
                    Ui.DrawRect(new Rect(cx + 3.5f, cy - 5f, 1.5f, 14f), c);
                    break;
            }
        }

        // ---- rulers (reference px, top-left origin) ----

        private void DrawRulers(Rect area)
        {
            float k = K;
            var top = new Rect(area.x, area.y, area.width, RulerT);
            var left = new Rect(area.x, area.y + RulerT, RulerT, area.height - RulerT);
            Ui.DrawRect(top, RulerColor);
            Ui.DrawRect(left, RulerColor);
            Ui.DrawRect(new Rect(top.x, top.yMax - 1f, top.width, 1f), BarLine);
            Ui.DrawRect(new Rect(left.xMax - 1f, left.y, 1f, left.height), BarLine);
            // 50 px ticks, labels every 100 (sparser when the screen is small)
            // 0 = the game screen's top-left (the board's corner when fitted)
            float step = 50f * k < 18f ? 100f : 50f;
            Rect bd = Board;
            for (float u = Mathf.Ceil((top.x - bd.x) / k / step) * step; bd.x + u * k < top.xMax; u += step)
            {
                float x = bd.x + u * k;
                bool major = Mathf.Approximately(u % (step * 2f), 0f);
                Ui.DrawRect(new Rect(x, top.yMax - (major ? 10f : 5f), 1f, major ? 10f : 5f), TickColor);
                if (major) GUI.Label(new Rect(x + 2f, top.y + 1f, 40f, 12f), ((int)u).ToString(), _rulerText);
            }
            for (float u = Mathf.Ceil((left.y - bd.y) / k / step) * step; bd.y + u * k < left.yMax; u += step)
            {
                float y = bd.y + u * k;
                bool major = Mathf.Approximately(u % (step * 2f), 0f);
                Ui.DrawRect(new Rect(left.xMax - (major ? 10f : 5f), y, major ? 10f : 5f, 1f), TickColor);
                if (major) GUI.Label(new Rect(left.x + 1f, y + 1f, 20f, 12f), ((int)u).ToString(), _rulerText);
            }
            // mouse position markers
            Vector2 m = Event.current.mousePosition;
            if (m.x > top.x && m.x < top.xMax) Ui.DrawRect(new Rect(m.x, top.y, 1f, RulerT), Ui.Accent);
            if (m.y > left.y && m.y < left.yMax) Ui.DrawRect(new Rect(left.x, m.y, RulerT, 1f), Ui.Accent);
            // the selection's extent on both rulers
            if (Selected().Count > 0)
            {
                Rect g = GroupRect();
                Color sc = new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.28f);
                Ui.DrawRect(new Rect(Mathf.Max(g.x, top.x), top.y, Mathf.Max(0f, Mathf.Min(g.xMax, top.xMax) - Mathf.Max(g.x, top.x)), RulerT - 1f), sc);
                Ui.DrawRect(new Rect(left.x, Mathf.Max(g.y, left.y), RulerT - 1f, Mathf.Max(0f, Mathf.Min(g.yMax, left.yMax) - Mathf.Max(g.y, left.y))), sc);
            }
        }

        // ---- status bar ----

        private void DrawStatus(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ui.DrawRect(r, BarColor);
            Ui.DrawRect(new Rect(r.x, r.y, r.width, 1f), BarLine);
            float k = K;
            Vector2 m = Event.current.mousePosition;
            string text = "X " + Mathf.Round((m.x - Board.x) / k) + "   Y " + Mathf.Round((m.y - Board.y) / k);
            List<ElementView> s = Selected();
            if (s.Count > 0)
            {
                Rect g = GroupRect();
                text += "      <color=#EDEDF2>" + (s.Count == 1 ? ElementConfig.DisplayName(s[0].Cfg.Type) : s.Count + "개 선택") + "</color>"
                        + "   W " + Mathf.Round(g.width / k) + "  H " + Mathf.Round(g.height / k);
            }
            text += "      화면 " + Screen.width + "×" + Screen.height + "  ·  배율 " + Main.Settings.Size.ToString("0.00");
            GUI.Label(r, text, _status);
            GUI.Label(r, (_chromeHidden ? "<color=#FF8792>Tab 도구 다시 보기</color> · " : "Tab 모두 숨기기 · ") + "Ctrl+Z 실행 취소 · Ctrl+C/V 복사·붙여넣기 · Del 삭제 · Shift 비율 유지 · Alt 가운데 기준 · Esc 끝", _statusRight);
        }

        // ---- docked panels: properties (tabs) over layers ----

        private Vector2 _layerScroll;

        private void DrawDock(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Ui.DrawRect(r, DockColor);
                Ui.DrawRect(new Rect(r.x, r.y, 1f, r.height), BarLine);
            }
            float layersH = Mathf.Clamp(r.height * 0.36f, 200f, 360f);
            var props = new Rect(r.x + 1f, r.y, r.width - 1f, r.height - layersH);
            var layers = new Rect(r.x + 1f, props.yMax, r.width - 1f, layersH);

            // properties panel
            PanelHead(new Rect(props.x, props.y, props.width, 30f), ref _ptab, "속성", "애니메이션", "자석");
            float inner = props.width - 20f;
            GUILayout.BeginArea(new Rect(props.x + 10f, props.y + 36f, inner, props.height - 40f));
            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);
            // pinned width: one long row can no longer widen the whole panel past the screen edge
            float flowBefore = Ui.FlowWidth;
            Ui.FlowWidth = inner - 40f; // card padding
            GUILayout.BeginVertical(GUILayout.Width(inner), GUILayout.MaxWidth(inner));
            switch (_ptab)
            {
                case 0:
                    if (Selected().Count > 1) DrawMulti();
                    else if (_sel != null) DrawProperties(_sel);
                    else DrawEmpty();
                    break;
                case 1:
                    if (Main.Settings.DrawAnimation(2, 140f)) Overlay.Instance.ApplySettings();
                    break;
                case 2:
                    DrawMagnet();
                    break;
            }
            GUILayout.EndVertical();
            Ui.FlowWidth = flowBefore;
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // layers panel
            int dummy = 0;
            PanelHead(new Rect(layers.x, layers.y, layers.width, 30f), ref dummy, "레이어");
            DrawLayerList(new Rect(layers.x, layers.y + 30f, layers.width, layers.height - 30f - 34f));
            DrawLayerBar(new Rect(layers.x, layers.yMax - 34f, layers.width, 34f));
        }

        private static void PanelHead(Rect r, ref int tab, params string[] names)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Ui.DrawRect(r, HeadColor);
                Ui.DrawRect(new Rect(r.x, r.y, r.width, 1f), BarLine);
            }
            float x = r.x + 4f;
            for (int i = 0; i < names.Length; i++)
            {
                float tw = _headTab.CalcSize(new GUIContent(names[i])).x;
                var b = new Rect(x, r.y + 4f, tw, r.height - 4f);
                if (GUI.Button(b, names[i], i == tab ? _headTabOn : _headTab)) tab = i;
                x += tw + 2f;
            }
        }

        // Photoshop order: the top layer (drawn last) is listed first. Eye toggles, click selects, ▲▼ restack.
        private void DrawLayerList(Rect r)
        {
            List<ElementView> views = Overlay.Instance.Views;
            const float rowH = 34f;
            var content = new Rect(0f, 0f, r.width - 12f, views.Count * rowH);
            _layerScroll = GUI.BeginScrollView(r, _layerScroll, content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            bool paint = Event.current.type == EventType.Repaint;
            int first = Mathf.Max(0, (int)(_layerScroll.y / rowH)), last = Mathf.Min(views.Count, first + (int)(r.height / rowH) + 2);
            for (int row = first; row < last; row++)
            {
                int i = views.Count - 1 - row;
                ElementView v = views[i];
                var line = new Rect(0f, row * rowH, content.width, rowH);
                bool sel = IsSel(v);
                if (paint)
                {
                    if (sel) Ui.DrawRect(line, new Color32(0x45, 0x1E, 0x26, 0xFF));
                    else if (line.Contains(Event.current.mousePosition)) Ui.DrawRect(line, new Color32(0x22, 0x22, 0x29, 0xFF));
                    Ui.DrawRect(new Rect(line.x, line.yMax - 1f, line.width, 1f), new Color32(0x24, 0x24, 0x2B, 0xFF));
                    // eye box + type swatch
                    Ui.DrawRect(new Rect(line.x + 30f, line.y, 1f, rowH), new Color32(0x24, 0x24, 0x2B, 0xFF));
                    var sw = new Rect(line.x + 40f, line.y + 7f, 20f, 20f);
                    Ui.DrawRect(sw, new Color32(0x2A, 0x2A, 0x32, 0xFF));
                    Ui.DrawRect(new Rect(sw.x + 5f, sw.y + 5f, 10f, 10f), LayerColor(v.Cfg));
                }
                var eye = new Rect(line.x + 2f, line.y, 28f, rowH);
                if (GUI.Button(eye, v.Cfg.Visible ? "●" : "○", _eye))
                {
                    Checkpoint();
                    v.Cfg.Visible = !v.Cfg.Visible;
                    Apply(v.Cfg);
                }
                string name = ElementConfig.DisplayName(v.Cfg.Type);
                string sub = v.Cfg.Type == ElementType.CustomText && !string.IsNullOrEmpty(v.Cfg.Template) ? Short(v.Cfg.Template)
                    : !string.IsNullOrEmpty(v.Cfg.Font) ? Path.GetFileNameWithoutExtension(v.Cfg.Font) : "";
                var nameRect = new Rect(line.x + 68f, line.y, line.width - 68f - (sel ? 52f : 6f), rowH);
                if (sub.Length > 0)
                {
                    GUI.Label(new Rect(nameRect.x, nameRect.y + 3f, nameRect.width, 18f), v.Cfg.Visible ? name : "<color=#6A6A78>" + name + "</color>", _layerName);
                    GUI.Label(new Rect(nameRect.x, nameRect.y + 18f, nameRect.width, 14f), sub, _layerSub);
                }
                else
                {
                    GUI.Label(nameRect, v.Cfg.Visible ? name : "<color=#6A6A78>" + name + "</color>", _layerName);
                }
                if (GUI.Button(nameRect, GUIContent.none, GUIStyle.none))
                {
                    Event ev = Event.current;
                    if (ev.shift || ev.control)
                    {
                        if (sel) _multi.Remove(v);
                        else _multi.Add(v);
                        _sel = _multi.Count > 0 ? _multi[_multi.Count - 1] : null;
                    }
                    else Select(v);
                }
                if (sel)
                {
                    if (GUI.Button(new Rect(line.xMax - 50f, line.y + 7f, 22f, 20f), "▲", _tbBtn) && i < views.Count - 1) MoveLayer(i, i + 1);
                    if (GUI.Button(new Rect(line.xMax - 26f, line.y + 7f, 22f, 20f), "▼", _tbBtn) && i > 0) MoveLayer(i, i - 1);
                }
            }
            GUI.EndScrollView();
        }

        private static Color LayerColor(ElementConfig c)
        {
            switch (c.Type)
            {
                case ElementType.Web: return new Color32(0x4F, 0xC3, 0xFF, 0xFF);
                case ElementType.Image: return new Color32(0xFF, 0xD5, 0x4A, 0xFF);
                case ElementType.Webcam: return new Color32(0xFF, 0x47, 0x57, 0xFF);
                case ElementType.ProgressBar: return new Color32(0xE4, 0x43, 0x43, 0xFF);
                default: return Snapshot.Hex(string.IsNullOrEmpty(c.Color) ? "FFFFFF" : c.Color);
            }
        }

        private void DrawLayerBar(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Ui.DrawRect(r, HeadColor);
                Ui.DrawRect(new Rect(r.x, r.y, r.width, 1f), BarLine);
            }
            GUILayout.BeginArea(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, r.height - 4f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 추가", _barBtn)) _gallery = true;
            bool any = Selected().Count > 0;
            GUI.enabled = any;
            if (GUILayout.Button("복제", _barBtn)) { Checkpoint(); DuplicateSelected(); }
            if (GUILayout.Button("삭제", _barBtn)) { Checkpoint(); DeleteSelected(); }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_confirmReset ? "한 번 더: 초기화" : "기본 배치", _confirmReset ? Ui.Danger : _barBtn))
            {
                if (_confirmReset)
                {
                    Checkpoint();
                    Main.Settings.Elements = ElementConfig.Defaults();
                    Overlay.Instance.Rebuild();
                    Select(null);
                }
                else
                {
                    _confirmReset = true;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---- undo / redo: whole-layout snapshots (a couple of KB each), taken before each change ----

        private static readonly XmlSerializer LayoutXml = new XmlSerializer(typeof(List<ElementConfig>));
        private readonly LinkedList<string> _undo = new LinkedList<string>();
        private readonly Stack<string> _redo = new Stack<string>();
        private const int UndoLimit = 60;

        private static string LayoutState()
        {
            using (var w = new StringWriter())
            {
                LayoutXml.Serialize(w, Main.Settings.Elements);
                return w.ToString();
            }
        }

        // Call before changing the layout; identical states are not stacked twice.
        private void Checkpoint()
        {
            string now = LayoutState();
            if (_undo.Count > 0 && _undo.Last.Value == now) return;
            _undo.AddLast(now);
            if (_undo.Count > UndoLimit) _undo.RemoveFirst();
            _redo.Clear();
        }

        private void Undo()
        {
            string now = LayoutState();
            while (_undo.Count > 0 && _undo.Last.Value == now) _undo.RemoveLast(); // a checkpoint with no change after it
            if (_undo.Count == 0) return;
            _redo.Push(now);
            string prev = _undo.Last.Value;
            _undo.RemoveLast();
            Restore(prev);
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.AddLast(LayoutState());
            Restore(_redo.Pop());
        }

        private void Restore(string xml)
        {
            using (var r = new StringReader(xml))
            {
                Main.Settings.Elements = (List<ElementConfig>)LayoutXml.Deserialize(r);
            }
            Overlay.Instance.Rebuild();
            Select(null);
        }
    }
}
