using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace NowOverlayer
{
    // In-game layout editor, laid out like Photoshop (see OverlayEditorChrome.cs for the bars, toolbar, rulers and
    // docked panels): click or box-select, drag to move, 8-handle transform box, crop tool for the webcam, undo.
    public partial class OverlayEditor : MonoBehaviour
    {
        public static bool Active { get; private set; }

        private const float Handle = 9f;

        private static readonly string[] Swatches =
            { "FFFFFF", "FFDA45", "FF9C9C", "FF6D6D", "E44343", "60FF4E", "3182F6", "D958FF", "8B95A1", "000000" };

        private ElementView _sel;
        private bool _dragging, _resizing;
        private Vector2 _mouseStart;
        private readonly List<Magnet.Guide> _guides = new List<Magnet.Guide>();
        private Vector2 _scroll;
        private bool _confirmReset;

        public static void Open()
        {
            if (!Overlay.Instance || Active || _opening) return;
            try
            {
                UnityModManager.UI.Instance?.ToggleWindow(false);
            }
            catch (Exception e)
            {
                Main.LogError("Close UMM window failed", e);
            }
            // grab the game screen first (mod window closed, overlay hidden) for the fitted board's background
            _opening = true;
            Overlay.Instance.StartCoroutine(Overlay.Instance.GrabScreen(tex =>
            {
                _opening = false;
                SetShot(tex);
                Active = true;
                Overlay.Instance.MarkDirty();
                foreach (ElementView v in Overlay.Instance.Views) (v as TextView)?.FitSize();
            }));
        }

        public static void Close()
        {
            if (!Active) return;
            Active = false;
            Board = new Rect(0f, 0f, Screen.width, Screen.height);
            _boardSet = null;
            Overlay.Instance?.SetFit(null, null);
            SetShot(null);
            Main.Settings.Save(Main.Mod);
        }

        private static bool _opening;
        private static Texture2D _shot;

        private static void SetShot(Texture2D tex)
        {
            if (_shot) Destroy(_shot);
            _shot = tex;
        }

        // Where the game screen is drawn right now (GUI px, top-left origin): the fitted board, or the whole window.
        public static Rect Board { get; private set; } = new Rect(0f, 0f, 1920f, 1080f);
        private static Rect? _boardSet;

        // Reference px -> GUI px for item coordinates (canvas scale × fitted board scale).
        private static float K => Overlay.Instance.Canvas.scaleFactor * Overlay.Instance.FitScale;

        // Fit the game screen into the free area between the bars and panels (same aspect, centered); off = full screen.
        private static void UpdateBoard(Rect? area)
        {
            Rect b;
            if (area == null)
            {
                b = new Rect(0f, 0f, Screen.width, Screen.height);
            }
            else
            {
                Rect a = area.Value;
                float s = Mathf.Min(1f, Mathf.Min(a.width / Screen.width, a.height / Screen.height));
                float bw = Mathf.Round(Screen.width * s), bh = Mathf.Round(Screen.height * s);
                b = new Rect(Mathf.Round(a.center.x - bw / 2f), Mathf.Round(a.center.y - bh / 2f), bw, bh);
            }
            Board = b;
            if (_boardSet.HasValue && _boardSet.Value == b) return;
            _boardSet = b;
            Overlay.Instance.SetFit(area == null ? (Rect?)null : b, _shot);
        }

        // Retake the background (game screen changed): hide the editor for a frame and shoot again.
        private void Reshoot()
        {
            if (_opening) return;
            _opening = true;
            Active = false;
            Overlay.Instance.SetFit(null, null);
            _boardSet = null;
            Overlay.Instance.StartCoroutine(Overlay.Instance.GrabScreen(tex =>
            {
                _opening = false;
                SetShot(tex);
                Active = true;
            }));
        }

        // ---- "+" gallery: every item type as a card with a live preview ----

        private bool _gallery;
        private Vector2 _galleryScroll;
        private Rect _galleryRect;
        private static readonly Snapshot GallerySample = Snapshot.Sample();

        private void DrawGallery(Event e)
        {
            float w = Mathf.Min(860f, Screen.width - 120f), h = Mathf.Min(680f, Screen.height - 120f);
            _galleryRect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _gallery = false;
                e.Use();
            }
            if (e.type == EventType.MouseDown && !_galleryRect.Contains(e.mousePosition))
            {
                _gallery = false;
                e.Use();
            }
        }

        private void DrawGalleryWindow()
        {
            Ui.DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));
            GUILayout.BeginArea(_galleryRect);
            GUILayout.BeginVertical(Ui.Page, GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("항목 추가", Ui.Title);
            GUILayout.Label("눌러서 화면 가운데에 추가해요. 추가한 뒤 드래그로 옮기면 돼요.", Ui.Sub);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("닫기", Ui.Ghost, GUILayout.Width(80))) _gallery = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            const int cols = 3;
            float cardW = (_galleryRect.width - 32f - 8f * (cols - 1)) / cols - 1f;
            _galleryScroll = GUILayout.BeginScrollView(_galleryScroll, GUIStyle.none, GUIStyle.none);
            ElementType[] types = (ElementType[])Enum.GetValues(typeof(ElementType));
            for (int i = 0; i < types.Length; i++)
            {
                if (i % cols == 0) GUILayout.BeginHorizontal();
                if (GalleryCard(types[i], cardW))
                {
                    Checkpoint();
                    Select(Overlay.Instance.AddElement(ElementConfig.Create(types[i])));
                    _gallery = false;
                }
                if (i % cols == cols - 1 || i == types.Length - 1)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static readonly System.Text.RegularExpressions.Regex Invisible =
            new System.Text.RegularExpressions.Regex("<color=#0000>[^<]*</color>");

        private static bool GalleryCard(ElementType t, float width)
        {
            Rect r = GUILayoutUtility.GetRect(width, 176f, GUILayout.Width(width), GUILayout.Height(176f));
            bool hover = r.Contains(Event.current.mousePosition);
            GUI.Box(r, GUIContent.none, hover ? Ui.ThumbOn : Ui.ThumbBox);
            var stage = new Rect(r.x + 10f, r.y + 10f, r.width - 20f, 96f);
            Ui.DrawRect(stage, Ui.StageColor);

            ElementConfig c = ElementConfig.Create(t);
            Snapshot s = GallerySample;
            switch (t)
            {
                case ElementType.ProgressBar:
                {
                    var bar = new Rect(stage.x + 16f, stage.center.y - 4f, stage.width - 32f, 8f);
                    Ui.DrawRect(bar, new Color(0.45f, 0.12f, 0.18f, 1f));
                    Ui.DrawRect(new Rect(bar.x, bar.y, bar.width * 0.35f, bar.height), Ui.Hex("E44343"));
                    Ui.DrawRect(new Rect(bar.x + bar.width * 0.62f, bar.y + 1f, 2f, 6f), Color.white);
                    break;
                }
                case ElementType.Combo:
                {
                    GUI.Label(new Rect(stage.x, stage.y + 6f, stage.width, 24f), "Combo", Ui.Preview);
                    var big = new GUIStyle(Ui.Preview) { fontSize = 44 };
                    big.normal.textColor = Ui.Hex("E95252");
                    GUI.Label(new Rect(stage.x, stage.y + 28f, stage.width, 60f), "257", big);
                    break;
                }
                case ElementType.Webcam:
                {
                    float d = stage.height - 24f;
                    var cam = new Rect(stage.center.x - d / 2f, stage.y + 12f, d, d);
                    GUI.DrawTexture(cam, Sprites.Circle.texture, ScaleMode.StretchToFill, true, 0f, Ui.Hex("F04452"), 0f, 0f);
                    var inner = new Rect(cam.x + 3f, cam.y + 3f, cam.width - 6f, cam.height - 6f);
                    GUI.DrawTexture(inner, Sprites.Circle.texture, ScaleMode.StretchToFill, true, 0f, Ui.Hex("2A2A33"), 0f, 0f);
                    GUI.Label(inner, "CAM", Ui.Preview);
                    break;
                }
                case ElementType.Image:
                {
                    var pic = new Rect(stage.center.x - 46f, stage.y + 14f, 92f, stage.height - 28f);
                    Ui.DrawRect(pic, Ui.Hex("2A2A33"));
                    Ui.DrawRect(new Rect(pic.x + 14f, pic.y + 12f, 14f, 14f), Ui.Hex("FFD54A"));
                    Ui.DrawRect(new Rect(pic.x + 10f, pic.yMax - 22f, 72f, 12f), Ui.Hex("F04452"));
                    GUI.Label(new Rect(stage.x, stage.yMax - 22f, stage.width, 20f), "PNG · JPG · GIF", Ui.ThumbLabel);
                    break;
                }
                case ElementType.Web:
                {
                    var frame = new Rect(stage.x + 30f, stage.y + 12f, stage.width - 60f, stage.height - 24f);
                    Ui.DrawRect(frame, Ui.Hex("2A2A33"));
                    Ui.DrawRect(new Rect(frame.x, frame.y, frame.width, 14f), Ui.Hex("3A3A44"));
                    for (int k = 0; k < 3; k++) Ui.DrawRect(new Rect(frame.x + 6f + k * 9f, frame.y + 4f, 6f, 6f), Ui.Hex("8E8E9A"));
                    GUI.Label(new Rect(frame.x, frame.y + 14f, frame.width, frame.height - 14f), "웹페이지", Ui.Preview);
                    break;
                }
                default:
                {
                    string text = Invisible.Replace(s.Text(c), "");
                    var st = new GUIStyle(Ui.Preview);
                    st.normal.textColor = Ui.Hex(c.Color);
                    if (t == ElementType.Judgement || t == ElementType.Bpm) st.fontSize = 15;
                    GUI.Label(stage, text, st);
                    break;
                }
            }

            GUI.Label(new Rect(r.x + 14f, r.y + 112f, r.width - 28f, 22f), ElementConfig.DisplayName(t), Ui.H2);
            GUI.Label(new Rect(r.x + 14f, r.y + 136f, r.width - 28f, 36f), ElementConfig.Description(t), Ui.Sub);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ---- canvas: selection, marquee, move, group scale ----
        // Click selects one item; Shift/Ctrl+click toggles; dragging on empty space draws a box that selects
        // everything it touches. Moving or scaling applies to the whole selection.

        private readonly List<ElementView> _multi = new List<ElementView>();
        private bool _marquee, _marqueeAdd;
        private Vector2 _marqueeStart;
        private Rect _marqueeRect;
        private readonly Dictionary<ElementView, Vector2> _startPos = new Dictionary<ElementView, Vector2>();
        private readonly Dictionary<ElementView, float> _startScale = new Dictionary<ElementView, float>();
        private readonly Dictionary<ElementView, Vector2> _startPivot = new Dictionary<ElementView, Vector2>();
        private Vector2 _groupAnchor;

        private static readonly Vector3[] Corners = new Vector3[4]; // reused: this runs for every item on every GUI event

        private static Rect ScreenRect(RectTransform rt)
        {
            Vector3[] c = Corners;
            rt.GetWorldCorners(c);
            float xMin = Mathf.Min(c[0].x, c[2].x), xMax = Mathf.Max(c[0].x, c[2].x);
            float yMin = Mathf.Min(c[0].y, c[2].y), yMax = Mathf.Max(c[0].y, c[2].y);
            return new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
        }

        private bool IsSel(ElementView v) => _multi.Contains(v);

        private List<ElementView> Selected()
        {
            _multi.RemoveAll(v => !Overlay.Instance.Views.Contains(v));
            return _multi;
        }

        private Rect GroupRect()
        {
            bool any = false;
            Rect u = default;
            foreach (ElementView v in Selected())
            {
                if (!v.Cfg.Visible) continue;
                Rect r = ScreenRect(v.Root);
                u = any ? Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin), Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax)) : r;
                any = true;
            }
            return u;
        }

        // Repaint only: thin outlines on every item, the transform box (8 handles) on the selection, or the crop frame.
        private void DrawBoxes()
        {
            Color idle = new Color(1f, 1f, 1f, 0.28f);
            foreach (ElementView v in Overlay.Instance.Views)
            {
                if (!v.Cfg.Visible || IsSel(v)) continue;
                Ui.DrawFrame(Expand(ScreenRect(v.Root), 3f), idle, 1f);
            }

            List<ElementView> s = Selected();
            if (s.Count > 0)
            {
                Rect box = TransformBox();
                foreach (ElementView v in s)
                {
                    if (s.Count > 1 && v.Cfg.Visible) Ui.DrawFrame(Expand(ScreenRect(v.Root), 3f), new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.55f), 1f);
                }
                if (CropMode)
                {
                    DrawCropFrame(box);
                }
                else
                {
                    Ui.DrawFrame(box, Ui.Accent, 1f);
                    for (int i = 0; i < 8; i++)
                    {
                        Rect h = HandleAt(box, i);
                        Ui.DrawRect(h, Color.white);
                        Ui.DrawFrame(h, new Color(0.1f, 0.1f, 0.12f, 1f), 1f);
                    }
                }
                // size readout under the box while transforming (like Photoshop's info tooltip)
                if (_resizing || _dragging)
                {
                    float k = K;
                    Rect g = GroupRect();
                    string info = _resizing
                        ? (CropMode ? CropInfo() : "W " + Mathf.Round(g.width / k) + "  H " + Mathf.Round(g.height / k) + (s.Count == 1 && !HasBox(s[0].Cfg.Type) ? "   ×" + s[0].Cfg.Scale.ToString("0.00") : ""))
                        : "X " + Mathf.Round((g.x - Board.x) / k) + "  Y " + Mathf.Round((g.y - Board.y) / k);
                    Tip(new Vector2(Event.current.mousePosition.x + 18f, Event.current.mousePosition.y + 20f), info);
                }
            }

            if (_marquee)
            {
                Ui.DrawRect(_marqueeRect, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.10f));
                Ui.DrawFrame(_marqueeRect, Ui.Accent, 1f);
            }
        }

        // Photoshop crop look: thick corner brackets, edge bars and a rule-of-thirds grid.
        private static void DrawCropFrame(Rect r)
        {
            Color c = Color.white;
            Ui.DrawFrame(r, new Color(1f, 1f, 1f, 0.6f), 1f);
            for (int i = 1; i < 3; i++)
            {
                Ui.DrawRect(new Rect(r.x + r.width * i / 3f, r.y, 1f, r.height), new Color(1f, 1f, 1f, 0.22f));
                Ui.DrawRect(new Rect(r.x, r.y + r.height * i / 3f, r.width, 1f), new Color(1f, 1f, 1f, 0.22f));
            }
            const float L = 18f, T = 4f;
            Ui.DrawRect(new Rect(r.x - T, r.y - T, L, T), c);
            Ui.DrawRect(new Rect(r.x - T, r.y - T, T, L), c);
            Ui.DrawRect(new Rect(r.xMax + T - L, r.y - T, L, T), c);
            Ui.DrawRect(new Rect(r.xMax, r.y - T, T, L), c);
            Ui.DrawRect(new Rect(r.x - T, r.yMax, L, T), c);
            Ui.DrawRect(new Rect(r.x - T, r.yMax + T - L, T, L), c);
            Ui.DrawRect(new Rect(r.xMax + T - L, r.yMax, L, T), c);
            Ui.DrawRect(new Rect(r.xMax, r.yMax + T - L, T, L), c);
            Ui.DrawRect(new Rect(r.center.x - L / 2f, r.y - T, L, T), c);
            Ui.DrawRect(new Rect(r.center.x - L / 2f, r.yMax, L, T), c);
            Ui.DrawRect(new Rect(r.x - T, r.center.y - L / 2f, T, L), c);
            Ui.DrawRect(new Rect(r.xMax, r.center.y - L / 2f, T, L), c);
        }

        private string CropInfo()
        {
            ElementConfig c = _sel.Cfg;
            return "자르기  ←" + Mathf.Round(c.CropL * 100f) + "%  →" + Mathf.Round(c.CropR * 100f) + "%  ↑" + Mathf.Round(c.CropT * 100f) + "%  ↓" + Mathf.Round(c.CropB * 100f) + "%";
        }

        private void DrawGuides()
        {
            if (_dragging) Magnet.Draw(_guides);
        }

        private static Rect Expand(Rect r, float p) => new Rect(r.x - p, r.y - p, r.width + p * 2, r.height + p * 2);

        // Handles 0..7: TL, T, TR, R, BR, B, BL, L. (hx, hy) = 0 min side, 1 middle, 2 max side.
        private static readonly int[] HX = { 0, 1, 2, 2, 2, 1, 0, 0 }, HY = { 0, 0, 0, 1, 2, 2, 2, 1 };

        private static Rect HandleAt(Rect box, int i)
        {
            float x = HX[i] == 0 ? box.xMin : HX[i] == 1 ? box.center.x : box.xMax;
            float y = HY[i] == 0 ? box.yMin : HY[i] == 1 ? box.center.y : box.yMax;
            return new Rect(x - Handle / 2f, y - Handle / 2f, Handle, Handle);
        }

        private Rect TransformBox()
        {
            List<ElementView> s = Selected();
            return Expand(GroupRect(), s.Count > 1 ? 6f : 3f);
        }

        private int HandleUnder(Vector2 m)
        {
            if (Selected().Count == 0) return -1;
            Rect box = TransformBox();
            for (int i = 0; i < 8; i++)
            {
                if (Expand(HandleAt(box, i), CropMode ? 8f : 5f).Contains(m)) return i;
            }
            return -1;
        }

        // Items whose box is set by Width / Height (free resize); text items only scale.
        private static bool HasBox(ElementType t) =>
            t == ElementType.Web || t == ElementType.Image || t == ElementType.Webcam || t == ElementType.ProgressBar;

        private bool CropMode => _tool == Tool.Crop && _sel != null && Selected().Count == 1 && _sel.Cfg.Type == ElementType.Webcam;

        private ElementView HitTest(Vector2 mouse)
        {
            List<ElementView> views = Overlay.Instance.Views;
            for (int i = views.Count - 1; i >= 0; i--)
            {
                if (views[i].Cfg.Visible && Expand(ScreenRect(views[i].Root), 4f).Contains(mouse)) return views[i];
            }
            return null;
        }

        // Remember where every selected item started, for group drags and scaling.
        private void RecordStart()
        {
            _startPos.Clear();
            _startScale.Clear();
            _startPivot.Clear();
            _startSize.Clear();
            foreach (ElementView v in Selected())
            {
                _startPos[v] = new Vector2(v.Cfg.X, v.Cfg.Y);
                _startScale[v] = v.Cfg.Scale;
                _startSize[v] = new Vector2(v.Cfg.Width, v.Cfg.Height);
                Rect r = ScreenRect(v.Root);
                _startPivot[v] = new Vector2(r.x + v.Cfg.PivotX * r.width, r.y + (1f - v.Cfg.PivotY) * r.height);
            }
        }

        private readonly Dictionary<ElementView, Vector2> _startSize = new Dictionary<ElementView, Vector2>();
        private int _handle = -1;
        private Rect _boxStart;
        private Vector4 _cropStart;

        private void HandleCanvasInput(Event e)
        {
            float k = K;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                {
                    bool additive = e.shift || e.control;
                    int handle = HandleUnder(e.mousePosition);
                    if (handle >= 0)
                    {
                        Checkpoint();
                        _resizing = true;
                        _handle = handle;
                        _mouseStart = e.mousePosition;
                        _boxStart = GroupRect();
                        ElementConfig c = _sel.Cfg;
                        _cropStart = new Vector4(c.CropL, c.CropR, c.CropT, c.CropB);
                        RecordStart();
                        e.Use();
                        return;
                    }
                    if (_tool == Tool.Text)
                    {
                        AddTextAt(e.mousePosition, k);
                        e.Use();
                        return;
                    }
                    ElementView hit = _tool == Tool.Marquee ? null : HitTest(e.mousePosition);
                    if (hit == null)
                    {
                        _marquee = true;
                        _marqueeAdd = additive;
                        _marqueeStart = e.mousePosition;
                        _marqueeRect = new Rect(e.mousePosition, Vector2.zero);
                        if (!additive) Select(null);
                        e.Use();
                        return;
                    }
                    if (additive)
                    {
                        if (IsSel(hit))
                        {
                            _multi.Remove(hit);
                            if (_sel == hit) _sel = _multi.Count > 0 ? _multi[_multi.Count - 1] : null;
                            e.Use();
                            return;
                        }
                        _multi.Add(hit);
                        _sel = hit;
                    }
                    else if (!IsSel(hit))
                    {
                        Select(hit);
                    }
                    Checkpoint();
                    _dragging = true;
                    _mouseStart = e.mousePosition;
                    RecordStart();
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when _marquee:
                {
                    Vector2 a = _marqueeStart, b = e.mousePosition;
                    _marqueeRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when _dragging:
                {
                    Vector2 d = (e.mousePosition - _mouseStart) / k;
                    foreach (ElementView v in Selected())
                    {
                        Vector2 p0 = _startPos[v];
                        v.Cfg.X = Mathf.Round(p0.x + d.x);
                        v.Cfg.Y = Mathf.Round(p0.y - d.y);
                        v.Root.anchoredPosition = new Vector2(v.Cfg.X, v.Cfg.Y);
                    }
                    Snap(e.alt, k);
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when _resizing:
                {
                    if (CropMode) DragCrop(e.mousePosition);
                    else DragHandle(e.mousePosition, e.shift, e.alt, k);
                    e.Use();
                    break;
                }
                case EventType.MouseUp when e.button == 0 && (_dragging || _resizing || _marquee):
                {
                    if (_marquee) FinishMarquee();
                    if (_dragging || _resizing)
                    {
                        foreach (ElementView v in Selected()) Reanchor(v.Cfg);
                    }
                    _handle = -1;
                    _dragging = _resizing = _marquee = false;
                    _guides.Clear();
                    e.Use();
                    break;
                }
                case EventType.ScrollWheel when Selected().Count > 0 && Expand(GroupRect(), 8f).Contains(e.mousePosition):
                {
                    Checkpoint();
                    RecordStart();
                    ScaleFromStart(1f - e.delta.y * 0.02f, GroupRect().center, k);
                    e.Use();
                    break;
                }
            }
        }

        // Drag one of the 8 handles. The opposite side (or the center with Alt) stays put.
        // Corner = both axes, edge = one axis. Text items (and a group) scale evenly; a box item (web, image, webcam,
        // bar) changes its width / height freely, and Shift keeps its shape on a corner.
        private void DragHandle(Vector2 m, bool keepShape, bool fromCenter, float k)
        {
            Rect s0 = _boxStart;
            int hx = HX[_handle], hy = HY[_handle];
            float ax = fromCenter ? s0.center.x : hx == 0 ? s0.xMax : s0.xMin;
            float ay = fromCenter ? s0.center.y : hy == 0 ? s0.yMax : s0.yMin;
            float ex = hx == 0 ? s0.xMin : s0.xMax, ey = hy == 0 ? s0.yMin : s0.yMax; // the dragged edge at the start
            // grab offset: the handle sits a few px outside the edge, so nothing jumps on the first move
            if (hx != 1) m.x -= _mouseStart.x - ex;
            if (hy != 1) m.y -= _mouseStart.y - ey;
            float fx = hx == 1 ? 1f : Mathf.Max(0.05f, (m.x - ax) / Mathf.Max(1f, Mathf.Abs(ex - ax)) * Mathf.Sign(ex - ax));
            float fy = hy == 1 ? 1f : Mathf.Max(0.05f, (m.y - ay) / Mathf.Max(1f, Mathf.Abs(ey - ay)) * Mathf.Sign(ey - ay));
            if (fromCenter)
            {
                if (hx != 1) fx = Mathf.Max(0.05f, (m.x - ax) / Mathf.Max(1f, s0.width / 2f) * (hx == 0 ? -1f : 1f));
                if (hy != 1) fy = Mathf.Max(0.05f, (m.y - ay) / Mathf.Max(1f, s0.height / 2f) * (hy == 0 ? -1f : 1f));
            }

            List<ElementView> s = Selected();
            bool box = s.Count == 1 && HasBox(s[0].Cfg.Type);
            if (!box)
            {
                float f = hx == 1 ? fy : hy == 1 ? fx : Mathf.Max(fx, fy);
                ScaleFromStart(f, new Vector2(ax, ay), k);
                return;
            }

            if (keepShape && hx != 1 && hy != 1) fx = fy = Mathf.Max(fx, fy);
            ElementView v = s[0];
            ElementConfig c = v.Cfg;
            Vector2 size0 = _startSize[v];
            c.Width = Mathf.Round(Mathf.Clamp(size0.x * fx, 8f, 3840f));
            c.Height = Mathf.Round(Mathf.Clamp(size0.y * fy, c.Type == ElementType.ProgressBar ? 1f : 8f, 2160f));
            if (c.Type == ElementType.Webcam && c.Shape == 2)
            {
                // a circle stays round
                float d = hx == 1 ? c.Height : hy == 1 ? c.Width : Mathf.Max(c.Width, c.Height);
                c.Width = c.Height = d;
            }
            // where the box lands on screen, then move the item's pivot to match
            float w = s0.width * c.Width / Mathf.Max(1f, size0.x), h = s0.height * c.Height / Mathf.Max(1f, size0.y);
            float xMin = fromCenter || hx == 1 ? s0.center.x - w / 2f : hx == 0 ? s0.xMax - w : s0.xMin;
            float yMin = fromCenter || hy == 1 ? s0.center.y - h / 2f : hy == 0 ? s0.yMax - h : s0.yMin;
            var pivot = new Vector2(xMin + c.PivotX * w, yMin + (1f - c.PivotY) * h);
            Vector2 d0 = pivot - _startPivot[v];
            c.X = Mathf.Round(_startPos[v].x + d0.x / k);
            c.Y = Mathf.Round(_startPos[v].y - d0.y / k);
            v.ApplyLayout(Main.Settings.Size);
        }

        // Crop tool on a webcam: pulling an edge inward cuts that side of the picture; the box keeps its size.
        private void DragCrop(Vector2 m)
        {
            ElementConfig c = _sel.Cfg;
            Rect s0 = _boxStart;
            float dx = (m.x - _mouseStart.x) / Mathf.Max(1f, s0.width), dy = (m.y - _mouseStart.y) / Mathf.Max(1f, s0.height);
            float keepW = 1f - _cropStart.x - _cropStart.y, keepH = 1f - _cropStart.z - _cropStart.w;
            int hx = HX[_handle], hy = HY[_handle];
            if (hx == 0) c.CropL = Mathf.Clamp(_cropStart.x + dx * keepW, 0f, Mathf.Min(0.45f, 0.9f - c.CropR));
            if (hx == 2) c.CropR = Mathf.Clamp(_cropStart.y - dx * keepW, 0f, Mathf.Min(0.45f, 0.9f - c.CropL));
            if (hy == 0) c.CropT = Mathf.Clamp(_cropStart.z + dy * keepH, 0f, Mathf.Min(0.45f, 0.9f - c.CropB));
            if (hy == 2) c.CropB = Mathf.Clamp(_cropStart.w - dy * keepH, 0f, Mathf.Min(0.45f, 0.9f - c.CropT));
            _sel.ApplyLayout(Main.Settings.Size);
        }

        // Text tool: a click on the canvas drops a new text item there.
        private void AddTextAt(Vector2 m, float k)
        {
            Checkpoint();
            ElementConfig c = ElementConfig.Create(ElementType.CustomText);
            c.Template = "텍스트";
            c.AnchorX = c.AnchorY = 0f;
            c.PivotX = 0f;
            c.PivotY = 1f;
            c.X = Mathf.Round((m.x - Board.x) / k);
            c.Y = Mathf.Round((Board.yMax - m.y) / k);
            ElementView v = Overlay.Instance.AddElement(c);
            Reanchor(c);
            Select(v);
            _tool = Tool.Move;
        }

        private void FinishMarquee()
        {
            if (!_marqueeAdd) _multi.Clear();
            if (_marqueeRect.width < 3f && _marqueeRect.height < 3f) return; // plain click on empty space
            foreach (ElementView v in Overlay.Instance.Views)
            {
                if (v.Cfg.Visible && !IsSel(v) && _marqueeRect.Overlaps(ScreenRect(v.Root))) _multi.Add(v);
            }
            _sel = _multi.Count > 0 ? _multi[_multi.Count - 1] : null;
            if (_sel != null) _ptab = 0;
        }

        // Scale every selected item by f around a screen point, keeping their layout relative to each other.
        private void ScaleFromStart(float f, Vector2 anchor, float k)
        {
            foreach (ElementView v in Selected())
            {
                if (!_startScale.ContainsKey(v)) continue;
                float s0 = _startScale[v];
                float sc = Mathf.Round(Mathf.Clamp(s0 * f, 0.2f, 5f) * 100f) / 100f;
                float real = sc / s0;
                Vector2 p0 = _startPivot[v];
                Vector2 delta = (anchor + (p0 - anchor) * real) - p0;
                v.Cfg.Scale = sc;
                v.Cfg.X = Mathf.Round(_startPos[v].x + delta.x / k);
                v.Cfg.Y = Mathf.Round(_startPos[v].y - delta.y / k);
                float ss = sc * Main.Settings.Size;
                v.Root.localScale = new Vector3(ss, ss, 1f);
                v.Root.anchoredPosition = new Vector2(v.Cfg.X, v.Cfg.Y);
            }
        }

        // Magnet on the whole selection's box (hold Alt to skip).
        private void Snap(bool off, float k)
        {
            _guides.Clear();
            if (off || !Main.Settings.Magnet) return;
            var others = new List<Rect>();
            foreach (ElementView v in Overlay.Instance.Views)
            {
                if (!IsSel(v) && v.Cfg.Visible && v.Root.gameObject.activeInHierarchy) others.Add(ScreenRect(v.Root));
            }
            Vector2 shift = Magnet.Snap(GroupRect(), others, k, Main.Settings, _guides);
            if (shift == Vector2.zero) return;
            foreach (ElementView v in Selected())
            {
                v.Cfg.X += shift.x / k;
                v.Cfg.Y -= shift.y / k;
                v.Root.anchoredPosition = new Vector2(v.Cfg.X, v.Cfg.Y);
            }
        }

        // After a move, anchor to the nearest screen third so the layout holds on other aspect ratios.
        private static void Reanchor(ElementConfig c)
        {
            Vector2 size = Overlay.Instance.CanvasRect.rect.size;
            float px = c.AnchorX * size.x + c.X, py = c.AnchorY * size.y + c.Y;
            float ax = px < size.x / 3f ? 0f : px > size.x * 2f / 3f ? 1f : 0.5f;
            float ay = py < size.y / 3f ? 0f : py > size.y * 2f / 3f ? 1f : 0.5f;
            c.AnchorX = ax;
            c.AnchorY = ay;
            c.X = Mathf.Round(px - ax * size.x);
            c.Y = Mathf.Round(py - ay * size.y);
            Apply(c);
        }

        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
            if (e.keyCode == KeyCode.Escape && Selected().Count > 0)
            {
                Select(null);
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return)
            {
                Close();
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.A && e.control)
            {
                _multi.Clear();
                foreach (ElementView v in Overlay.Instance.Views) if (v.Cfg.Visible) _multi.Add(v);
                _sel = _multi.Count > 0 ? _multi[0] : null;
                e.Use();
                return;
            }
            // Photoshop shortcuts
            if (e.control && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
            {
                if (e.keyCode == KeyCode.Y || e.shift) Redo();
                else Undo();
                e.Use();
                return;
            }
            if (!e.control && !e.alt)
            {
                Tool? pick = e.keyCode == KeyCode.V ? Tool.Move : e.keyCode == KeyCode.M ? Tool.Marquee
                    : e.keyCode == KeyCode.T ? Tool.Text : e.keyCode == KeyCode.C ? Tool.Crop : (Tool?)null;
                if (pick.HasValue)
                {
                    _tool = pick.Value;
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.F)
                {
                    _fit = !_fit;
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.Tab)
                {
                    // like Photoshop: Tab hides every bar and panel, Shift+Tab only the right-hand panels
                    if (e.shift) _panelsHidden = !_panelsHidden;
                    else _chromeHidden = !_chromeHidden;
                    e.Use();
                    return;
                }
            }
            List<ElementView> s = Selected();
            if (s.Count == 0) return;
            if (e.keyCode != KeyCode.None) Checkpoint();
            float step = e.shift ? 10f : 1f;
            Vector2 move = Vector2.zero;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: move.x = -step; break;
                case KeyCode.RightArrow: move.x = step; break;
                case KeyCode.UpArrow: move.y = step; break;
                case KeyCode.DownArrow: move.y = -step; break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelected();
                    e.Use();
                    return;
                case KeyCode.D when e.control:
                    DuplicateSelected();
                    e.Use();
                    return;
                default:
                    return;
            }
            foreach (ElementView v in s)
            {
                v.Cfg.X += move.x;
                v.Cfg.Y += move.y;
                v.Root.anchoredPosition = new Vector2(v.Cfg.X, v.Cfg.Y);
            }
            e.Use();
        }

        private void Select(ElementView v)
        {
            _multi.Clear();
            if (v != null) _multi.Add(v);
            _sel = v;
            if (v != null) _ptab = 0;
            _confirmReset = false;
            GUIUtility.keyboardControl = 0;
        }

        private void Delete(ElementView v)
        {
            Overlay.Instance.RemoveElement(v);
            _multi.Remove(v);
            if (_sel == v) _sel = _multi.Count > 0 ? _multi[0] : null;
        }

        private void DeleteSelected()
        {
            foreach (ElementView v in new List<ElementView>(Selected())) Overlay.Instance.RemoveElement(v);
            Select(null);
        }

        private void Duplicate(ElementView v)
        {
            ElementConfig c = v.Cfg.Clone();
            c.X += 24f;
            c.Y -= 24f;
            Select(Overlay.Instance.AddElement(c));
        }

        private void DuplicateSelected()
        {
            var copies = new List<ElementView>();
            foreach (ElementView v in new List<ElementView>(Selected()))
            {
                ElementConfig c = v.Cfg.Clone();
                c.X += 24f;
                c.Y -= 24f;
                copies.Add(Overlay.Instance.AddElement(c));
            }
            _multi.Clear();
            _multi.AddRange(copies);
            _sel = copies.Count > 0 ? copies[copies.Count - 1] : null;
        }

        private static void Apply(ElementConfig c)
        {
            foreach (ElementView v in Overlay.Instance.Views)
            {
                if (v.Cfg == c) v.ApplyLayout(Main.Settings.Size);
            }
            Overlay.Instance.MarkDirty();
        }

        // ---- multi-selection panel: align, distribute, scale, visibility ----

        private void DrawMulti()
        {
            List<ElementView> s = Selected();
            float k = K;

            GUILayout.Label("선택한 항목", Ui.Caption);
            GUILayout.BeginVertical(Ui.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label(s.Count + "개 선택", Ui.H2);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("복제", Ui.Chip)) DuplicateSelected();
            if (GUILayout.Button("삭제", Ui.Danger, GUILayout.Height(28)))
            {
                DeleteSelected();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();
            var names = new List<string>();
            foreach (ElementView v in s) names.Add(ElementConfig.DisplayName(v.Cfg.Type));
            GUILayout.Label(string.Join(" · ", names.ToArray()), Ui.Sub);
            GUILayout.EndVertical();

            Ui.Section("정렬");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("왼쪽", Ui.Ghost)) AlignX(0, k);
            if (GUILayout.Button("가운데", Ui.Ghost)) AlignX(1, k);
            if (GUILayout.Button("오른쪽", Ui.Ghost)) AlignX(2, k);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("위", Ui.Ghost)) AlignY(0, k);
            if (GUILayout.Button("가운데", Ui.Ghost)) AlignY(1, k);
            if (GUILayout.Button("아래", Ui.Ghost)) AlignY(2, k);
            GUILayout.EndHorizontal();
            if (s.Count >= 3)
            {
                GUILayout.Label("간격 똑같이", Ui.Body);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("가로", Ui.Secondary)) Distribute(true, k);
                if (GUILayout.Button("세로", Ui.Secondary)) Distribute(false, k);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            Ui.Section("크기 · 표시");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−  작게", Ui.Ghost)) GroupScale(1f / 1.1f, k);
            if (GUILayout.Button("+  크게", Ui.Ghost)) GroupScale(1.1f, k);
            GUILayout.EndHorizontal();
            GUILayout.Label("모서리 점을 끌거나 마우스 휠로도 한꺼번에 바꿀 수 있어요.", Ui.Sub);
            Ui.Divider();
            float op = Ui.Slider("투명도", s[0].Cfg.Opacity, 0.1f, 1f);
            if (Mathf.Abs(op - s[0].Cfg.Opacity) > 0.001f)
            {
                foreach (ElementView v in s)
                {
                    v.Cfg.Opacity = Mathf.Round(op * 100f) / 100f;
                    Apply(v.Cfg);
                }
            }
            if (GUILayout.Button("모두 숨기기", Ui.Ghost))
            {
                foreach (ElementView v in s)
                {
                    v.Cfg.Visible = false;
                    Apply(v.Cfg);
                }
                Select(null);
            }
            GUILayout.EndVertical();
        }

        private void GroupScale(float f, float k)
        {
            RecordStart();
            ScaleFromStart(f, GroupRect().center, k);
            foreach (ElementView v in Selected()) Reanchor(v.Cfg);
        }

        // Move an item by a screen-pixel offset.
        private static void Nudge(ElementView v, float dx, float dy, float k)
        {
            v.Cfg.X += dx / k;
            v.Cfg.Y -= dy / k;
            v.Root.anchoredPosition = new Vector2(v.Cfg.X, v.Cfg.Y);
        }

        private void AlignX(int mode, float k)
        {
            Rect g = GroupRect();
            foreach (ElementView v in Selected())
            {
                Rect r = ScreenRect(v.Root);
                float dx = mode == 0 ? g.xMin - r.xMin : mode == 1 ? g.center.x - r.center.x : g.xMax - r.xMax;
                Nudge(v, dx, 0f, k);
                Reanchor(v.Cfg);
            }
        }

        private void AlignY(int mode, float k)
        {
            Rect g = GroupRect();
            foreach (ElementView v in Selected())
            {
                Rect r = ScreenRect(v.Root);
                float dy = mode == 0 ? g.yMin - r.yMin : mode == 1 ? g.center.y - r.center.y : g.yMax - r.yMax;
                Nudge(v, 0f, dy, k);
                Reanchor(v.Cfg);
            }
        }

        // Equal gaps between items, keeping the first and last where they are.
        private void Distribute(bool horizontal, float k)
        {
            var items = new List<ElementView>(Selected());
            items.Sort((a, b) =>
            {
                Rect ra = ScreenRect(a.Root), rb = ScreenRect(b.Root);
                return horizontal ? ra.center.x.CompareTo(rb.center.x) : ra.center.y.CompareTo(rb.center.y);
            });
            Rect first = ScreenRect(items[0].Root), last = ScreenRect(items[items.Count - 1].Root);
            float span = horizontal ? last.xMax - first.xMin : last.yMax - first.yMin;
            float sizes = 0f;
            foreach (ElementView v in items)
            {
                Rect r = ScreenRect(v.Root);
                sizes += horizontal ? r.width : r.height;
            }
            float gap = (span - sizes) / (items.Count - 1);
            float cursor = horizontal ? first.xMin : first.yMin;
            foreach (ElementView v in items)
            {
                Rect r = ScreenRect(v.Root);
                if (horizontal)
                {
                    Nudge(v, cursor - r.xMin, 0f, k);
                    cursor += r.width + gap;
                }
                else
                {
                    Nudge(v, 0f, cursor - r.yMin, k);
                    cursor += r.height + gap;
                }
                Reanchor(v.Cfg);
            }
        }

        private int _ptab; // dock tabs: 0 속성, 1 애니메이션, 2 자석

        private void DrawEmpty()
        {
            Ui.Section("선택한 항목 없음");
            GUILayout.Label("화면에서 항목을 클릭하면 여기서 위치·크기·색·애니메이션을 바꿀 수 있어요.", Ui.Sub);
            GUILayout.Space(6);
            GUILayout.Label("· 도구: V 이동 · M 선택 윤곽 · T 텍스트 · C 자르기(웹캠)\n· 상자의 모서리 4개 · 가장자리 4개 점으로 크기 (Shift 비율 유지, Alt 가운데 기준)\n· 빈 곳을 끌면 여러 개 선택, Shift / Ctrl + 클릭으로 추가·빼기, Ctrl+A 전체\n· 방향키 1px, Shift+방향키 10px 이동\n· Ctrl+Z 실행 취소, Ctrl+Shift+Z 다시, Ctrl+D 복제, Delete 삭제\n· Tab 패널 숨기기 · Esc 선택 해제, 한 번 더 누르면 편집 끝", Ui.Sub);
            GUILayout.EndVertical();
            Ui.Section("참고");
            GUILayout.Label("편집 중에도 게임 입력이 같이 들어갈 수 있어요. 레벨 선택 화면이나 일시정지 상태에서 편집하는 걸 추천해요.", Ui.Sub);
            GUILayout.EndVertical();
        }

        private void DrawProperties(ElementView v)
        {
            ElementConfig c = v.Cfg;
            bool changed = false;

            // header: name, visibility, duplicate / delete
            GUILayout.Label("선택한 항목", Ui.Caption);
            GUILayout.BeginVertical(Ui.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label(ElementConfig.DisplayName(c.Type), Ui.H2);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("복제", Ui.Chip)) Duplicate(v);
            if (GUILayout.Button("삭제", Ui.Danger, GUILayout.Height(28))) Delete(v);
            GUILayout.EndHorizontal();
            bool vis = Ui.Switch("화면에 표시", c.Visible);
            if (vis != c.Visible)
            {
                c.Visible = vis;
                changed = true;
            }
            GUILayout.EndVertical();
            if (_sel != v) return; // deleted

            Ui.Section("위치 · 크기");
            GUILayout.BeginHorizontal();
            float x = Ui.NumberField("X", c.X);
            GUILayout.Space(8);
            float y = Ui.NumberField("Y", c.Y);
            GUILayout.EndHorizontal();
            if (x != c.X || y != c.Y)
            {
                c.X = x;
                c.Y = y;
                changed = true;
            }
            float scale = Ui.Slider("크기", c.Scale, 0.2f, 4f);
            if (Mathf.Abs(scale - c.Scale) > 0.001f)
            {
                c.Scale = Mathf.Round(scale * 100f) / 100f;
                changed = true;
            }
            if (ElementConfig.IsText(c.Type)) changed |= DrawAlign(c, v);
            if (c.Type == ElementType.ProgressBar || c.Type == ElementType.Web || c.Type == ElementType.Image || c.Type == ElementType.Webcam)
            {
                float maxH = c.Type == ElementType.ProgressBar ? 40f : 1080f;
                float w = Ui.Slider("너비", c.Width, 100f, 1920f, "0");
                float h = Ui.Slider(c.Type == ElementType.ProgressBar ? "두께" : "높이", c.Height, 2f, maxH, "0");
                if (Mathf.Abs(w - c.Width) > 0.5f || Mathf.Abs(h - c.Height) > 0.1f)
                {
                    c.Width = Mathf.Round(w);
                    c.Height = Mathf.Round(h);
                    changed = true;
                }
            }
            GUILayout.EndVertical();

            if (c.Type != ElementType.ProgressBar && c.Type != ElementType.Web)
            {
                Ui.Section(c.Type == ElementType.Combo ? "제목 색" : c.Type == ElementType.Image ? "색 입히기" : c.Type == ElementType.Webcam ? "테두리 색" : "글자");
                if (ElementConfig.IsText(c.Type) || c.Type == ElementType.Combo) changed |= DrawItemFont(c);
                if (ElementConfig.IsText(c.Type))
                {
                    float fs = Ui.Slider("글자 크기", c.FontSize, 10f, 120f, "0");
                    if (Mathf.Abs(fs - c.FontSize) > 0.01f)
                    {
                        c.FontSize = Mathf.Round(fs);
                        changed = true;
                    }
                }
                Ui.FlowBegin();
                foreach (string hex in Swatches)
                {
                    Ui.FlowItem(32f);
                    if (Ui.ColorSwatch(Ui.Hex(hex), string.Equals(hex, c.Color, StringComparison.OrdinalIgnoreCase)))
                    {
                        c.Color = hex;
                        changed = true;
                    }
                }
                Ui.FlowEnd();
                GUILayout.BeginHorizontal();
                GUILayout.Label("#", Ui.Sub, GUILayout.Width(12));
                string hexIn = GUILayout.TextField(c.Color, 6, Ui.Field, GUILayout.Width(110));
                GUILayout.EndHorizontal();
                if (hexIn != c.Color && hexIn.Length == 6 && ColorUtility.TryParseHtmlString("#" + hexIn, out _))
                {
                    c.Color = hexIn.ToUpperInvariant();
                    changed = true;
                }
                GUILayout.EndVertical();
            }

            if (c.Type == ElementType.CustomText)
            {
                Ui.Section("내용");
                string t = GUILayout.TextArea(c.Template ?? "", Ui.Area, GUILayout.MinHeight(60));
                if (t != c.Template)
                {
                    c.Template = t;
                    changed = true;
                }
                GUILayout.Label("눌러서 넣기", Ui.Sub);
                Ui.FlowBegin();
                foreach (string token in Snapshot.Tokens)
                {
                    var tc = new GUIContent("{" + token + "}");
                    Ui.FlowItem(Ui.Chip.CalcSize(tc).x + 6f);
                    if (GUILayout.Button(tc, Ui.Chip))
                    {
                        c.Template += "{" + token + "}";
                        changed = true;
                    }
                }
                Ui.FlowEnd();
                GUILayout.EndVertical();
            }

            if (c.Type == ElementType.Webcam)
            {
                Ui.Section("웹캠");
                changed |= DrawWebcam(c);
                GUILayout.EndVertical();
            }

            if (c.Type == ElementType.Image)
            {
                Ui.Section("이미지");
                changed |= DrawImage(c, v);
                GUILayout.EndVertical();
            }

            if (c.Type == ElementType.Web)
            {
                Ui.Section("웹페이지");
                changed |= DrawWeb(c, v);
                GUILayout.EndVertical();
            }

            Ui.Section("효과");
            float op = Ui.Slider("투명도", c.Opacity, 0.1f, 1f);
            if (Mathf.Abs(op - c.Opacity) > 0.001f)
            {
                c.Opacity = Mathf.Round(op * 100f) / 100f;
                changed = true;
            }
            changed |= DrawElementAnim(c);
            GUILayout.EndVertical();

            if (changed) Apply(c);
        }
        private bool _animOpen;
        private string _urlFor, _urlDraft = "";
        private bool _fontOpen;
        private string _fontFind = "", _fontFail, _fontPathDraft = "";

        // This item's own font: "전체 글꼴" follows the 글꼴 tab; otherwise a file from the mod Font folder or Windows.
        private bool DrawItemFont(ElementConfig c)
        {
            bool changed = false;
            string name = string.IsNullOrEmpty(c.Font) ? "전체 글꼴 따르기" : Path.GetFileNameWithoutExtension(c.Font);
            GUILayout.BeginHorizontal();
            GUILayout.Label("글꼴", Ui.Body, GUILayout.Width(40));
            if (GUILayout.Button(name + (_fontOpen ? "   ▲" : "   ▼"), Ui.Ghost, GUILayout.MinWidth(0f))) _fontOpen = !_fontOpen;
            GUILayout.EndHorizontal();
            if (!_fontOpen) return false;

            if (GUILayout.Button("전체 글꼴 따르기", string.IsNullOrEmpty(c.Font) ? Ui.RowOn : Ui.Row, GUILayout.MinWidth(0f)) && !string.IsNullOrEmpty(c.Font))
            {
                c.Font = "";
                _fontFail = null;
                changed = true;
            }

            // any ttf / otf on disk, e.g. one that was never installed to Windows
            GUILayout.Label("파일 경로로 불러오기 (ttf · otf)", Ui.Sub);
            GUILayout.BeginHorizontal();
            _fontPathDraft = GUILayout.TextField(_fontPathDraft, Ui.Field, GUILayout.MinWidth(0f));
            if (GUILayout.Button("붙여넣기", Ui.Chip)) _fontPathDraft = (GUIUtility.systemCopyBuffer ?? "").Trim();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("이 경로로 불러오기", Ui.Ghost))
            {
                string p;
                string err = Settings.CheckFontPath(_fontPathDraft, out p);
                if (err == null && !(Overlay.Instance && Overlay.Instance.FontLoads(p))) err = "글꼴로 읽을 수 없는 파일이에요: " + Path.GetFileName(p);
                if (err == null)
                {
                    c.Font = p;
                    _fontFail = null;
                    changed = true;
                }
                else
                {
                    _fontFail = err;
                }
            }

            GUILayout.Label("목록에서 찾기", Ui.Sub);
            _fontFind = GUILayout.TextField(_fontFind, Ui.Field, GUILayout.MinWidth(0f));
            int shown = 0;
            foreach (string f in Settings.AllFonts())
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (_fontFind.Length > 0 && n.IndexOf(_fontFind, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 12) break;
                // fonts in the mod folder are stored by file name so a shared layout still finds them
                string value = f.StartsWith(Settings.FontFolder, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(f) : f;
                bool on = string.Equals(value, c.Font, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(n, on ? Ui.RowOn : Ui.Row, GUILayout.MinWidth(0f)) && !on)
                {
                    if (Overlay.Instance && Overlay.Instance.FontLoads(value))
                    {
                        c.Font = value;
                        _fontFail = null;
                        changed = true;
                    }
                    else
                    {
                        _fontFail = "불러올 수 없는 글꼴이에요: " + n;
                    }
                }
            }
            if (shown > 12) GUILayout.Label("12개까지 보여요. 이름으로 찾아 주세요.", Ui.Sub);
            if (_fontFail != null) GUILayout.Label("<color=#FF6B6B>" + _fontFail + "</color>", Ui.Sub);
            Ui.Divider();
            return changed;
        }

        private bool DrawElementAnim(ElementConfig c)
        {
            bool changed = false;
            Ui.Divider();
            GUILayout.BeginHorizontal();
            GUILayout.Label("등장 애니메이션", Ui.Body);
            GUILayout.FlexibleSpace();
            string cur = c.Anim < 0 ? "전체 설정 (" + Anim.Name(Main.Settings.Anim) + ")" : Anim.Name((AnimType)c.Anim);
            if (GUILayout.Button(cur + (_animOpen ? "  ▲" : "  ▼"), Ui.Chip)) _animOpen = !_animOpen;
            GUILayout.EndHorizontal();
            if (!_animOpen) return false;

            if (GUILayout.Button("전체 설정 따르기", c.Anim < 0 ? Ui.TabOn : Ui.Tab))
            {
                c.Anim = -1;
                changed = true;
            }
            GUILayout.Space(4);
            AnimType? picked = Anim.Grid(c.Anim < 0 ? (AnimType)(-99) : (AnimType)c.Anim, Main.Settings.AnimDuration, 2, 136f);
            if (picked.HasValue)
            {
                c.Anim = (int)picked.Value;
                changed = true;
            }
            if (GUILayout.Button("▶  미리보기", Ui.Secondary)) Overlay.Instance.PreviewIntro();
            if (changed) Overlay.Instance.PreviewIntro();
            return changed;
        }

        private bool DrawWebcam(ElementConfig c)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("카메라", Ui.Body);
            GUILayout.FlexibleSpace();
            bool rescan = GUILayout.Button("↻ 다시 찾기", Ui.Chip);
            GUILayout.EndHorizontal();
            string[] names = WebcamView.DeviceNames(rescan);
            if (names.Length == 0) GUILayout.Label("연결된 카메라를 찾지 못했어요.", Ui.Sub);
            for (int i = 0; i < names.Length; i++)
            {
                bool on = c.Url == names[i] || (string.IsNullOrEmpty(c.Url) && i == 0);
                if (GUILayout.Button(names[i], on ? Ui.RowOn : Ui.Row, GUILayout.MinWidth(0f)) && !on)
                {
                    c.Url = names[i];
                    changed = true;
                }
            }
            Ui.Divider();
            GUILayout.Label("모양", Ui.Body);
            int shape = c.Shape;
            Ui.Segmented(ref shape, "사각형", "둥근 모서리", "원");
            if (shape != c.Shape)
            {
                c.Shape = shape;
                if (shape == 2) c.Height = c.Width; // a circle wants a square box
                changed = true;
            }
            GUILayout.Label("방향", Ui.Body);
            int rot = ((c.Rotation % 360) + 360) % 360 / 90;
            if (Ui.Segmented(ref rot, "0°", "90°", "180°", "270°"))
            {
                c.Rotation = rot * 90;
                changed = true;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↺  왼쪽으로", Ui.Ghost))
            {
                c.Rotation = (c.Rotation + 270) % 360;
                changed = true;
            }
            if (GUILayout.Button("↻  오른쪽으로", Ui.Ghost))
            {
                c.Rotation = (c.Rotation + 90) % 360;
                changed = true;
            }
            GUILayout.EndHorizontal();
            bool flipV = Ui.Switch("상하 반전", c.FlipV);
            if (flipV != c.FlipV)
            {
                c.FlipV = flipV;
                changed = true;
            }
            bool mirror = Ui.Switch("거울 모드 (좌우 반전)", c.Mirror);
            float zoom = Ui.Slider("확대", c.Zoom, 1f, 3f);
            float border = Ui.Slider("테두리 두께", c.Border, 0f, 16f, "0");
            if (mirror != c.Mirror || Mathf.Abs(zoom - c.Zoom) > 0.004f || Mathf.Abs(border - c.Border) > 0.4f)
            {
                c.Mirror = mirror;
                c.Zoom = Mathf.Round(zoom * 100f) / 100f;
                c.Border = Mathf.Round(border);
                changed = true;
            }
            Ui.Divider();
            GUILayout.BeginHorizontal();
            GUILayout.Label("자르기", Ui.Body);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_tool == Tool.Crop ? "자르기 도구 끄기" : "화면에서 자르기 (C)", _tool == Tool.Crop ? Ui.Secondary : Ui.Chip)) _tool = _tool == Tool.Crop ? Tool.Move : Tool.Crop;
            GUILayout.EndHorizontal();
            float cl = Ui.Slider("왼쪽", c.CropL * 100f, 0f, 45f, "0'%'") / 100f;
            float cr = Ui.Slider("오른쪽", c.CropR * 100f, 0f, 45f, "0'%'") / 100f;
            float ct = Ui.Slider("위", c.CropT * 100f, 0f, 45f, "0'%'") / 100f;
            float cb = Ui.Slider("아래", c.CropB * 100f, 0f, 45f, "0'%'") / 100f;
            if (Mathf.Abs(cl - c.CropL) + Mathf.Abs(cr - c.CropR) + Mathf.Abs(ct - c.CropT) + Mathf.Abs(cb - c.CropB) > 0.001f)
            {
                c.CropL = Mathf.Round(cl * 200f) / 200f;
                c.CropR = Mathf.Round(cr * 200f) / 200f;
                c.CropT = Mathf.Round(ct * 200f) / 200f;
                c.CropB = Mathf.Round(cb * 200f) / 200f;
                changed = true;
            }
            if (c.CropL + c.CropR + c.CropT + c.CropB > 0f && GUILayout.Button("자르기 없애기", Ui.Ghost))
            {
                c.CropL = c.CropR = c.CropT = c.CropB = 0f;
                changed = true;
            }
            GUILayout.Label("자른 만큼 남은 부분이 상자를 꽉 채워요. 자르기 도구로는 상자 가장자리를 끌어서 잘라요.", Ui.Sub);
            GUILayout.Label("캠은 화면에 보일 때만 켜지고, 오버레이가 5초 넘게 꺼져 있으면 꺼져요.", Ui.Sub);
            return changed;
        }

        private string _imgFor, _imgDraft = "";

        private bool DrawImage(ElementConfig c, ElementView v)
        {
            bool changed = false;
            var iv = v as ImageView;
            if (_imgFor != c.Id)
            {
                _imgFor = c.Id;
                _imgDraft = c.Url ?? "";
            }
            GUILayout.Label("파일 이름, 파일 경로, 또는 https:// 주소", Ui.Sub);
            _imgDraft = GUILayout.TextField(_imgDraft, Ui.Field, GUILayout.MinWidth(0f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("불러오기", Ui.Primary))
            {
                c.Url = _imgDraft.Trim().Trim('"');
                c.Width = c.Height = 0f; // size to the new picture
                iv?.Reload();
                changed = true;
            }
            if (GUILayout.Button("원본 크기로", Ui.Ghost)) iv?.FitNative();
            GUILayout.EndHorizontal();

            GUILayout.Label("Images 폴더", Ui.Body);
            string folder = ImageView.Folder;
            string[] files = new string[0];
            try
            {
                Directory.CreateDirectory(folder);
                files = Directory.GetFiles(folder);
            }
            catch (Exception)
            {
                // unreadable folder: just show none
            }
            int shown = 0;
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif") continue;
                if (++shown > 30) break;
                string name = Path.GetFileName(f);
                if (GUILayout.Button(name, name == c.Url ? Ui.RowOn : Ui.Row, GUILayout.MinWidth(0f)))
                {
                    _imgDraft = c.Url = name;
                    c.Width = c.Height = 0f;
                    iv?.Reload();
                    changed = true;
                }
            }
            if (shown == 0) GUILayout.Label("폴더에 PNG · JPG · GIF를 넣으면 여기 나와요.", Ui.Sub);
            if (GUILayout.Button("Images 폴더 열기", Ui.Ghost)) Application.OpenURL("file:///" + folder.Replace('\\', '/'));
            return changed;
        }

        private bool DrawWeb(ElementConfig c, ElementView v)
        {
            bool changed = false;
            GUILayout.Label("주소", Ui.Body);
            if (_urlFor != c.Id)
            {
                _urlFor = c.Id;
                _urlDraft = c.Url ?? "";
            }
            _urlDraft = GUILayout.TextField(_urlDraft, Ui.Field, GUILayout.MinWidth(0f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("불러오기", Ui.Primary))
            {
                string u = _urlDraft.Trim();
                if (u.Length > 0 && !u.Contains("://")) u = "https://" + u;
                _urlDraft = u;
                c.Url = u;
                (v as WebView)?.Reload();
                changed = true;
            }
            if (GUILayout.Button("새로고침", Ui.Ghost)) (v as WebView)?.Reload();
            GUILayout.EndHorizontal();
            GUILayout.Label("채팅창·후원 알림 같은 방송 위젯 주소를 넣으면 돼요. 화면에 보이기만 하고 클릭은 안 돼요.", Ui.Sub);

            float fps = Ui.Slider("갱신 횟수 (초당)", c.Fps, 1f, 30f, "0");
            if (Mathf.Abs(fps - c.Fps) > 0.5f)
            {
                c.Fps = Mathf.Round(fps);
                changed = true;
            }
            bool muted = Ui.Switch("소리 끄기", c.Muted);
            if (muted != c.Muted)
            {
                c.Muted = muted;
                changed = true;
            }
            return changed;
        }

        // Changing the pivot keeps the element where it is on screen.
        private static bool DrawAlign(ElementConfig c, ElementView v)
        {
            GUILayout.Label("정렬", Ui.Body);
            GUILayout.BeginHorizontal();
            float[] pivots = { 0f, 0.5f, 1f };
            string[] names = { "왼쪽", "가운데", "오른쪽" };
            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                bool on = Mathf.Approximately(c.PivotX, pivots[i]);
                if (GUILayout.Button(names[i], on ? Ui.Secondary : Ui.Ghost) && !on)
                {
                    float w = v.Root.rect.width * c.Scale * Main.Settings.Size;
                    c.X += (pivots[i] - c.PivotX) * w;
                    c.PivotX = pivots[i];
                    changed = true;
                }
            }
            GUILayout.EndHorizontal();
            return changed;
        }

        private void DrawMagnet()
        {
            Settings s = Main.Settings;
            Ui.Section("자석");
            bool changed = false;
            bool on = Ui.Switch("자석 켜기", s.Magnet, "드래그 중 Alt를 누르면 잠깐 꺼져요");
            changed |= on != s.Magnet;
            s.Magnet = on;
            if (s.Magnet)
            {
                bool a = Ui.Switch("화면 가장자리 · 가운데", s.MagnetScreen, "분홍 선");
                bool b = Ui.Switch("다른 항목 가장자리 · 가운데", s.MagnetElements, "청록 선");
                bool g = Ui.Switch("같은 간격 맞추기", s.MagnetSpacing, "노란 선 · 두 항목 사이 간격을 그대로 반복");
                changed |= a != s.MagnetScreen || b != s.MagnetElements || g != s.MagnetSpacing;
                s.MagnetScreen = a;
                s.MagnetElements = b;
                s.MagnetSpacing = g;
                float range = Ui.Slider("끌어당기는 거리 (px)", s.MagnetRange, 2f, 24f, "0");
                float margin = Ui.Slider("화면 가장자리 여백", s.MagnetMargin, 0f, 64f, "0");
                if (Mathf.Abs(range - s.MagnetRange) > 0.4f || Mathf.Abs(margin - s.MagnetMargin) > 0.4f)
                {
                    s.MagnetRange = Mathf.Round(range);
                    s.MagnetMargin = Mathf.Round(margin);
                    changed = true;
                }
            }
            GUILayout.EndVertical();
            if (changed) s.Save(Main.Mod);
        }

        // Later items draw on top; also the entry-animation order.
        private static void MoveLayer(int from, int to)
        {
            List<ElementView> views = Overlay.Instance.Views;
            List<ElementConfig> cfgs = Main.Settings.Elements;
            ElementView v = views[from];
            views.RemoveAt(from);
            views.Insert(to, v);
            cfgs.Remove(v.Cfg);
            cfgs.Insert(Mathf.Min(to, cfgs.Count), v.Cfg);
            v.Root.SetSiblingIndex(to);
        }

        private static string Short(string s) => s.Length > 14 ? s.Substring(0, 14) + "…" : s;
    }
}
