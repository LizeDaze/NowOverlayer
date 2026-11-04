using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace NowOverlayer
{
    // Rhythm-game style result screen. On clear: a card with a rank letter (X / SS / S+ / S / A / B / C / D) stamped onto
    // a slanted color band and the clear type under it (ALL XPERFECT / PURE PERFECT / CLEAR, Korean when the game is in
    // Korean). Right: song, a counting-up X-Accuracy, small stats, a progress bar and a 10-row judgement table where
    // XPerfect has its own row. With Settings.ReplaceGameResult it fills the screen a few seconds after the clear.
    // On death: MiniCard, a small version that flies to the right edge.
    // Numbers use Bahnschrift (a narrow DIN-like face that ships with Windows), text uses Malgun Gothic.
    public class ResultCard
    {
        public class Data
        {
            public bool Cleared;
            public string Title;
            public float Progress, XAcc, Acc, Best, Time;
            public int MaxCombo;
            public int[] Hits;
            public bool FullRun, NewBestProgress, NewBestXAcc, FirstClear;
            public float MapBestXAcc;
        }

        private const float W = 1120f, H = 640f, Pad = 40f, RankW = 340f;
        private const float EmptyRowAlpha = 0.6f, BarH = 4f, TrackH = 6f;
        private static readonly Color Dim = new Color(0.78f, 0.78f, 0.84f);
        private static readonly Color BodyColor = new Color(0.035f, 0.035f, 0.05f);
        private static readonly Color BodyOpaque = new Color(BodyColor.r, BodyColor.g, BodyColor.b, 0.985f);

        private static TMP_FontAsset _num, _bold, _regular;
        private static Material _glowMat;

        private readonly RectTransform _root, _rankBox, _band, _burst;
        private readonly CanvasGroup _group, _rankGroup;
        private readonly Image _topLine, _bandImg, _burstImg, _barFill, _barBest, _chip;
        private readonly TextMeshProUGUI _rank, _rankGhost, _clearType, _chipText, _badges, _title, _artist,
            _heroLabel, _hero, _statLabels, _statValues, _progressLabel;
        private readonly Row[] _rows = new Row[10];
        private readonly Canvas _canvas;
        private readonly Image _dim, _shadow, _border, _body, _flash;
        private readonly MiniCard _mini;
        private bool _full; // clear card taking over from the game's own result text (Settings.ReplaceGameResult)

        private class Row
        {
            public CanvasGroup Group;
            public RectTransform Rt, Bar;
            public TextMeshProUGUI Count;
            public int Target;
            public float Share;
            public float BaseX;
        }

        // label, color; counts are filled in Show from HitMargin
        private static readonly string[] RowNames =
            { "XPERFECT", "PERFECT", "EARLY PERFECT", "LATE PERFECT", "OVERLOAD", "EARLY!", "LATE!", "TOO EARLY", "TOO LATE", "MISS" };

        private static readonly string[] RowColors =
            { "8AF4FF", "60FF4E", "C8F25A", "C8F25A", "D958FF", "FFB54E", "FFB54E", "FF5A5A", "FF5A5A", "FF3B6B" };

        private Data _d;
        private Color _accent;
        private float _heroTarget, _barTarget;
        private float _shownAt = -1f, _until = -1f;

        public ResultCard(Transform parent, Overlay o)
        {
            LoadFonts(o);
            _mini = new MiniCard(parent);
            _root = ElementView.NewRect("Result", parent);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(W, H);
            _canvas = _root.gameObject.AddComponent<Canvas>();
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            _dim = Box(_root, "Dim", Vector2.zero, new Vector2(8000f, 8000f), new Color(0f, 0f, 0f, 0.55f), 1f);
            _shadow = Box(_root, "Shadow", new Vector2(0f, -14f), new Vector2(W + 50f, H + 50f), new Color(0f, 0f, 0f, 0.45f), 0.6f);
            _border = Box(_root, "Border", Vector2.zero, new Vector2(W + 2f, H + 2f), new Color(1f, 1f, 1f, 0.10f), 1.2f);
            _body = Box(_root, "Body", Vector2.zero, new Vector2(W, H), BodyOpaque, 1.2f);
            _topLine = Box(_root, "TopLine", Vector2.zero, new Vector2(W - 60f, 3f), Color.white, 10f);
            Place(_topLine.rectTransform, 0f, H / 2f - 1f, 0.5f, 1f);

            // ---- left: rank area (rounded mask so the slanted band stays inside) ----
            Image rankBg = Box(_root, "RankArea", Vector2.zero, new Vector2(RankW - 16f, H - 16f), new Color(1f, 1f, 1f, 0.03f), 1.4f);
            _rankBox = rankBg.rectTransform;
            Place(_rankBox, -W / 2f + 8f, 0f, 0f, 0.5f);
            _rankBox.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            _bandImg = Box(_rankBox, "Band", Vector2.zero, new Vector2(150f, H * 1.6f), Color.white, 100f);
            _band = _bandImg.rectTransform;
            _band.localRotation = Quaternion.Euler(0f, 0f, -16f);
            Image band2 = Box(_rankBox, "Band2", Vector2.zero, new Vector2(18f, H * 1.6f), Color.white, 100f);
            band2.rectTransform.anchoredPosition = new Vector2(104f, 0f);
            band2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -16f);
            band2.color = new Color(1f, 1f, 1f, 0.05f);

            var rankRoot = ElementView.NewRect("Rank", _rankBox);
            rankRoot.anchoredPosition = new Vector2(0f, 34f);
            rankRoot.sizeDelta = new Vector2(RankW, 220f);
            _rankGroup = rankRoot.gameObject.AddComponent<CanvasGroup>();

            _burstImg = Box(rankRoot, "Burst", Vector2.zero, new Vector2(240f, 240f), Color.white, 1f);
            _burstImg.sprite = Sprites.Ring;
            _burstImg.type = Image.Type.Simple;
            _burst = _burstImg.rectTransform;

            _rankGhost = Text(rankRoot, "Ghost", 230f, _num, TextAlignmentOptions.Center);
            Stretch(_rankGhost.rectTransform);
            _rankGhost.rectTransform.anchoredPosition = new Vector2(8f, -8f);
            _rankGhost.color = new Color(0f, 0f, 0f, 0.55f);
            _rank = Text(rankRoot, "Letter", 230f, _num, TextAlignmentOptions.Center);
            Stretch(_rank.rectTransform);
            if (_glowMat) _rank.fontSharedMaterial = _glowMat;

            _clearType = Text(_rankBox, "ClearType", 28f, _num, TextAlignmentOptions.Center);
            Place(_clearType.rectTransform, 0f, -128f, 0.5f, 1f, RankW - 30f, 38f);
            _clearType.characterSpacing = 3f;
            _clearType.fontStyle = FontStyles.Bold;

            // ---- right: song + score ----
            float left = -W / 2f + RankW + 20f, right = W / 2f - Pad, top = H / 2f - Pad, colW = right - left;

            _chip = Box(_root, "Chip", Vector2.zero, new Vector2(140f, 34f), Color.white, 3f);
            Place(_chip.rectTransform, left, top, 0f, 1f);
            _chipText = Text(_chip.rectTransform, "Text", 18f, _num, TextAlignmentOptions.Center);
            Stretch(_chipText.rectTransform);
            _chipText.characterSpacing = 3f;

            _badges = Text(_root, "Badges", 18f, _bold, TextAlignmentOptions.MidlineRight);
            Place(_badges.rectTransform, right, top, 1f, 1f, 480f, 34f);

            _title = Text(_root, "Title", 38f, _bold, TextAlignmentOptions.TopLeft);
            Place(_title.rectTransform, left, top - 46f, 0f, 1f, colW, 52f);
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _artist = Text(_root, "Artist", 21f, _regular, TextAlignmentOptions.TopLeft);
            Place(_artist.rectTransform, left, top - 98f, 0f, 1f, colW, 32f);
            _artist.color = Dim;
            _artist.overflowMode = TextOverflowModes.Ellipsis;

            _heroLabel = Text(_root, "HeroLabel", 19f, _num, TextAlignmentOptions.TopLeft);
            Place(_heroLabel.rectTransform, left, top - 142f, 0f, 1f, 300f, 26f);
            _heroLabel.characterSpacing = 3f;
            _heroLabel.color = Dim;
            _hero = Text(_root, "Hero", 88f, _num, TextAlignmentOptions.TopLeft);
            Place(_hero.rectTransform, left - 3f, top - 164f, 0f, 1f, 370f, 96f);
            _hero.fontStyle = FontStyles.Bold;

            // labels on the left and values on the right edge, so each column lines up
            _statLabels = Text(_root, "StatLabels", 22f, _num, TextAlignmentOptions.TopLeft);
            Place(_statLabels.rectTransform, right - 330f, top - 144f, 0f, 1f, 200f, 124f);
            _statLabels.lineSpacing = 12f;
            _statLabels.color = Dim;
            _statValues = Text(_root, "StatValues", 22f, _num, TextAlignmentOptions.TopRight);
            Place(_statValues.rectTransform, right, top - 144f, 1f, 1f, 200f, 124f);
            _statValues.lineSpacing = 12f;
            _statValues.fontStyle = FontStyles.Bold;

            Image track = Box(_root, "Track", Vector2.zero, new Vector2(colW, TrackH), new Color(1f, 1f, 1f, 0.12f), 10f);
            Place(track.rectTransform, left, top - 296f, 0f, 1f);
            _barFill = Box(track.rectTransform, "Fill", Vector2.zero, new Vector2(0f, TrackH), Color.white, 10f);
            _barFill.rectTransform.anchorMin = _barFill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            _barBest = Box(track.rectTransform, "Best", Vector2.zero, new Vector2(3f, 18f), new Color(1f, 1f, 1f, 0.9f), 10f);
            _barBest.rectTransform.anchorMin = _barBest.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _progressLabel = Text(_root, "ProgressLabel", 18f, _num, TextAlignmentOptions.TopRight);
            Place(_progressLabel.rectTransform, right, top - 270f, 1f, 1f, 330f, 24f);
            _progressLabel.characterSpacing = 2f;
            _progressLabel.color = Dim;

            // ---- judgement table: 2 columns x 5 rows ----
            float gap = 30f, cw = (colW - gap) / 2f, rh = 49f, ty = top - 314f;
            for (int i = 0; i < 10; i++)
            {
                int col = i / 5, r = i % 5;
                var row = new Row();
                RectTransform rt = ElementView.NewRect("Row" + i, _root);
                Place(rt, left + col * (cw + gap), ty - r * rh, 0f, 1f, cw, rh);
                row.Rt = rt;
                row.BaseX = rt.anchoredPosition.x;
                row.Group = rt.gameObject.AddComponent<CanvasGroup>();
                Color c = Snapshot.Hex(RowColors[i]);

                Image tick = Box(rt, "Tick", Vector2.zero, new Vector2(4f, 24f), c, 10f);
                Place(tick.rectTransform, -cw / 2f, rh / 2f - 7f, 0f, 1f);
                TextMeshProUGUI label = Text(rt, "Label", 20f, _num, TextAlignmentOptions.TopLeft);
                Place(label.rectTransform, -cw / 2f + 14f, rh / 2f - 7f, 0f, 1f, cw - 120f, 28f);
                label.text = RowNames[i];
                label.color = c;
                label.characterSpacing = 1f;
                row.Count = Text(rt, "Count", 29f, _num, TextAlignmentOptions.TopRight);
                Place(row.Count.rectTransform, cw / 2f, rh / 2f - 1f, 1f, 1f, 120f, 36f);
                row.Count.fontStyle = FontStyles.Bold;

                Image barBg = Box(rt, "BarBg", Vector2.zero, new Vector2(cw, BarH), new Color(1f, 1f, 1f, 0.09f), 10f);
                Place(barBg.rectTransform, -cw / 2f, -rh / 2f + 5f, 0f, 0f);
                Image bar = Box(rt, "Bar", Vector2.zero, new Vector2(0f, BarH), c, 10f);
                Place(bar.rectTransform, -cw / 2f, -rh / 2f + 5f, 0f, 0f);
                row.Bar = bar.rectTransform;
                _rows[i] = row;
            }

            _flash = Box(_root, "Flash", Vector2.zero, new Vector2(8000f, 8000f), new Color(1f, 1f, 1f, 0f), 1f);

            _root.gameObject.SetActive(false);
        }

        // ---- fonts ----

        private static void LoadFonts(Overlay o)
        {
            if (_num && _bold && _regular) return;
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            _bold = Create(Path.Combine(fonts, "malgunbd.ttf")) ?? o.Font;
            _regular = Create(Path.Combine(fonts, "malgun.ttf")) ?? _bold;
            _num = Create(Path.Combine(fonts, "bahnschrift.ttf")) ?? _bold;
            if (_num != _bold)
            {
                // Korean in a numbers line (badges etc.) falls back to Malgun
                _num.fallbackFontAssetTable.Insert(0, _bold);
            }
            _glowMat = Glow(_num);
        }

        private static TMP_FontAsset Create(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                TMP_FontAsset f = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                if (f) f.fallbackFontAssetTable = new List<TMP_FontAsset> { RDConstants.data.koreanFontTMPro, RDConstants.data.japaneseFontTMPro, RDConstants.data.chineseFontTMPro };
                return f;
            }
            catch (Exception e)
            {
                Main.LogError("Result font failed: " + path, e);
                return null;
            }
        }

        // Soft underlay glow for the rank letter; its color is set per result.
        private static Material Glow(TMP_FontAsset f)
        {
            try
            {
                var m = new Material(f.material);
                var mobileSdf = typeof(ShaderUtilities).GetProperty("ShaderRef_MobileSDF")?.GetValue(null) as Shader;
                if (mobileSdf) m.shader = mobileSdf;
                m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(1f, 1f, 1f, 0.5f));
                m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
                m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.6f);
                m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.9f);
                return m;
            }
            catch (Exception e)
            {
                Main.LogError("Result glow failed", e);
                return null;
            }
        }

        // ---- building blocks ----

        private static Image Box(RectTransform parent, string name, Vector2 pos, Vector2 size, Color c, float corner = 2.4f)
        {
            RectTransform rt = ElementView.NewRect(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = Sprites.Round;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = corner; // sprite corner is 24px: 2.4 -> ~10px, 1 -> 24px
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, float size, TMP_FontAsset font, TextAlignmentOptions align)
        {
            RectTransform rt = ElementView.NewRect(name, parent);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.richText = true;
            t.color = Color.white;
            return t;
        }

        // Put rt's (pivotX, pivotY) corner at (x, y) in parent coordinates (origin = parent center).
        private static void Place(RectTransform rt, float x, float y, float pivotX, float pivotY, float w = -1f, float h = -1f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(pivotX, pivotY);
            if (w >= 0f) rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // ---- rank ----

        private struct Grade
        {
            public string Letter, Type;
            public Color Top, Bottom;
        }

        private static bool Korean => RDString.language == SystemLanguage.Korean;

        private static Grade Rank(Data d, int[] c)
        {
            bool allX = c[1] + c[2] + c[3] + c[4] + c[5] + c[6] + c[7] + c[8] + c[9] == 0;
            bool fullPerfect = c[4] + c[5] + c[6] + c[7] + c[8] + c[9] == 0;
            string type = allX ? "ALL XPERFECT" : fullPerfect ? (Korean ? "완벽한 플레이" : "PURE PERFECT") : (Korean ? "클리어" : "CLEAR");
            float x = d.XAcc * 100f;
            if (allX) return new Grade { Letter = "X", Type = type, Top = Color.white, Bottom = Snapshot.Hex("6FE9FF") };
            if (x >= 99.5f) return new Grade { Letter = "SS", Type = type, Top = Snapshot.Hex("FFF3B0"), Bottom = Snapshot.Hex("FFB300") };
            if (x >= 99f) return new Grade { Letter = "S+", Type = type, Top = Snapshot.Hex("FFE680"), Bottom = Snapshot.Hex("FF9D00") };
            if (x >= 97f) return new Grade { Letter = "S", Type = type, Top = Snapshot.Hex("FFD966"), Bottom = Snapshot.Hex("F08C00") };
            if (x >= 94f) return new Grade { Letter = "A", Type = type, Top = Snapshot.Hex("B6FF9E"), Bottom = Snapshot.Hex("2FCB4F") };
            if (x >= 90f) return new Grade { Letter = "B", Type = type, Top = Snapshot.Hex("A9E4FF"), Bottom = Snapshot.Hex("2F8FE0") };
            if (x >= 80f) return new Grade { Letter = "C", Type = type, Top = Snapshot.Hex("D9C2FF"), Bottom = Snapshot.Hex("8A5CE6") };
            return new Grade { Letter = "D", Type = type, Top = Snapshot.Hex("D0D0DA"), Bottom = Snapshot.Hex("7A7A8A") };
        }

        // ---- show / animate ----

        private static int[] Counts(int[] h) => new[]
        {
            Judge.Get(h, Judge.XPerfect) + Judge.Get(h, Judge.Auto),
            Judge.Get(h, Judge.PerfectMinus) + Judge.Get(h, Judge.PerfectPlus) + Judge.Get(h, Judge.Perfect), // older game: one Perfect
            Judge.Get(h, Judge.EarlyPerfect),
            Judge.Get(h, Judge.LatePerfect),
            Judge.Get(h, Judge.FailOverload),
            Judge.Get(h, Judge.VeryEarly),
            Judge.Get(h, Judge.VeryLate),
            Judge.Get(h, Judge.TooEarly),
            Judge.Get(h, Judge.TooLate),
            Judge.Get(h, Judge.FailMiss)
        };

        private static string SongTitle(string title)
        {
            title = title ?? "";
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? title.Substring(dash + 3) : (title.Length > 0 ? title : "A Dance of Fire and Ice");
        }

        // delay: seconds before the card starts to appear
        public void Show(Data d, float seconds, float delay = 0f)
        {
            Hide();
            if (!d.Cleared)
            {
                _mini.Show(d, seconds);
                return;
            }
            _d = d;
            _full = Main.Settings.ReplaceGameResult;
            _dim.color = _full ? new Color(BodyColor.r, BodyColor.g, BodyColor.b, Main.Settings.ResultBgAlpha) : new Color(0f, 0f, 0f, 0.55f);
            _body.color = _full ? Color.clear : BodyOpaque;
            _shadow.gameObject.SetActive(!_full);
            _border.gameObject.SetActive(!_full);
            int[] counts = Counts(d.Hits);
            int total = 0, most = 1;
            foreach (int c in counts)
            {
                total += c;
                most = Math.Max(most, c);
            }

            Grade g = Rank(d, counts);
            _accent = g.Bottom;
            _rank.text = g.Letter;
            _rankGhost.text = g.Letter;
            _rank.fontSize = _rankGhost.fontSize = g.Letter.Length > 1 ? 190f : 250f;
            _rank.enableVertexGradient = true;
            _rank.colorGradient = new VertexGradient(g.Top, g.Top, g.Bottom, g.Bottom);
            if (_glowMat) _glowMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(g.Bottom.r, g.Bottom.g, g.Bottom.b, 0.55f));
            _clearType.text = g.Type;
            _clearType.color = Color.Lerp(g.Top, g.Bottom, 0.15f);
            _bandImg.color = new Color(g.Bottom.r, g.Bottom.g, g.Bottom.b, 0.16f);
            _burstImg.color = new Color(g.Top.r, g.Top.g, g.Top.b, 0f);
            _topLine.color = g.Bottom;

            _chip.color = new Color(_accent.r, _accent.g, _accent.b, 0.18f);
            _chipText.color = Color.Lerp(g.Top, g.Bottom, 0.1f);
            _chipText.text = Korean ? "레벨 클리어" : "LEVEL CLEAR";
            _chip.rectTransform.sizeDelta = new Vector2(_chipText.GetPreferredValues(_chipText.text).x + 32f, 34f);

            var badges = new List<string>();
            if (d.FirstClear) badges.Add("FIRST CLEAR");
            if (d.NewBestXAcc) badges.Add("NEW RECORD");
            if (d.NewBestProgress) badges.Add("BEST PROGRESS");
            _badges.text = badges.Count == 0 ? "" : "<color=#FFD54A>" + string.Join("   ", badges.ToArray()) + "</color>";

            string title = d.Title ?? "";
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            _title.text = SongTitle(title);
            _artist.text = dash > 0 ? title.Substring(0, dash) : "";

            _heroLabel.text = "X-ACCURACY";
            _heroTarget = d.XAcc;
            _barTarget = Mathf.Clamp01(d.Progress);
            _barFill.color = _accent;
            float trackW = TrackW;
            _barBest.gameObject.SetActive(d.Best > 0f && d.Best < 1f);
            _barBest.rectTransform.anchoredPosition = new Vector2(trackW * Mathf.Clamp01(d.Best), 0f);
            _progressLabel.text = d.FullRun && d.MapBestXAcc >= 0f ? "MAP BEST  " + Pct(d.MapBestXAcc) : "";

            _statLabels.text = "ACCURACY\nMAX COMBO\nTIME";
            _statValues.text = Pct(d.Acc) + "\n" + d.MaxCombo.ToString("N0") + "\n" + Snapshot.TimeStr(d.Time, d.Time >= 3600f);

            for (int i = 0; i < 10; i++)
            {
                _rows[i].Target = counts[i];
                _rows[i].Share = counts[i] / (float)most;
                _rows[i].Group.alpha = counts[i] == 0 && i >= 4 ? EmptyRowAlpha : 1f; // empty bad rows step back
            }

            _shownAt = UnityEngine.Time.unscaledTime + delay;
            _until = seconds > 0f ? _shownAt + seconds : float.MaxValue;
            _root.gameObject.SetActive(true);
            // full screen goes over the rest of the overlay and the game's own UI (only works on an active canvas)
            _canvas.overrideSorting = _full;
            if (_full) _canvas.sortingOrder = 30000;
            Tick();
        }

        private static float TrackW => W / 2f - Pad - (-W / 2f + RankW + 20f);

        public void Hide()
        {
            _until = -1f;
            _root.gameObject.SetActive(false);
            _mini.Hide();
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(_root.gameObject);
            UnityEngine.Object.Destroy(_mini.Root.gameObject);
        }

        public void Tick()
        {
            _mini.Tick();
            if (!_root.gameObject.activeSelf || _d == null) return;
            float now = UnityEngine.Time.unscaledTime;
            float t = now - _shownAt;
            if (t < 0f)
            {
                _group.alpha = 0f;
                return;
            }

            float k;
            if (_full)
            {
                // jelly entrance: drops in squashed, the width and height spring back out of step; a hit when the rank lands
                k = OutCubic(Mathf.Clamp01(t / 0.2f));
                float fit = FitScale();
                float landed = t - RankLands;
                float hit = landed >= 0f ? Mathf.Exp(-landed * 8f) : 0f;
                _root.localScale = new Vector3(fit * Mathf.LerpUnclamped(1.2f, 1f, Spring(t, 2.1f, 6.5f)), fit * Mathf.LerpUnclamped(0.5f, 1f, Spring(t, 2.5f, 6.5f)), 1f);
                _root.anchoredPosition = new Vector2(Mathf.Sin(landed * 70f) * 9f * hit, Mathf.LerpUnclamped(-140f, 0f, Spring(t, 2.3f, 7f)));
                _flash.color = new Color(1f, 1f, 1f, 0.28f * hit);
            }
            else
            {
                // card: rise + fade + slight scale
                k = OutCubic(Mathf.Clamp01(t / 0.4f));
                float sc = Mathf.Lerp(0.95f, 1f, k) * Main.Settings.ResultScale;
                _root.localScale = new Vector3(sc, sc, 1f);
                _root.anchoredPosition = new Vector2(0f, Mathf.Lerp(-30f, 0f, k));
                _flash.color = Color.clear;
            }
            float fadeOut = Mathf.Clamp01((_until - now) / 0.35f);
            _group.alpha = k * fadeOut;
            // an opaque full-screen card must not cover the pause menu or the screen editor
            if (_full && (OverlayEditor.Active || OverlayEditor.Opening || (scrController.instance && scrController.instance.paused))) _group.alpha = 0f;

            // band sweeps in from the left
            float b = OutCubic(Mathf.Clamp01((t - 0.05f) / 0.5f));
            _band.anchoredPosition = new Vector2(Mathf.Lerp(-260f, 40f, b), 0f);

            // rank stamps in (big -> settle with overshoot) with a ring burst
            float rt = (t - 0.55f) / 0.42f;
            float rk = Mathf.Clamp01(rt);
            float rs = Mathf.LerpUnclamped(2.4f, 1f, OutBack(rk));
            _rankGroup.alpha = Mathf.Clamp01(rt * 3f);
            _rankGroup.transform.localScale = new Vector3(rs, rs, 1f);
            float burst = Mathf.Clamp01((t - 0.8f) / 0.55f);
            float bs = Mathf.Lerp(0.6f, 1.9f, OutCubic(burst));
            _burst.localScale = new Vector3(bs, bs, 1f);
            Color bc = _burstImg.color;
            bc.a = burst > 0f && burst < 1f ? 0.7f * (1f - burst) : 0f;
            _burstImg.color = bc;
            _clearType.alpha = Mathf.Clamp01((t - 0.95f) / 0.3f);

            // hero number counts up, bar fills
            float c = OutExpo(Mathf.Clamp01((t - 0.15f) / 1f));
            _hero.text = Pct(_heroTarget * c);
            _barFill.rectTransform.sizeDelta = new Vector2(TrackW * _barTarget * c, TrackH);

            // judgement rows slide in one after another, counts and bars grow
            for (int i = 0; i < 10; i++)
            {
                Row row = _rows[i];
                int order = (i % 5) * 2 + i / 5;
                float ri = Mathf.Clamp01((t - 0.3f - order * 0.045f) / 0.4f);
                float baseA = row.Target == 0 && i >= 4 ? EmptyRowAlpha : 1f;
                row.Group.alpha = OutCubic(ri) * baseA;
                row.Rt.anchoredPosition = new Vector2(row.BaseX + (1f - OutBack(ri)) * (_full ? 70f : 24f), row.Rt.anchoredPosition.y);
                float n = OutExpo(Mathf.Clamp01((t - 0.45f - order * 0.045f) / 0.9f));
                row.Count.text = Mathf.RoundToInt(row.Target * n).ToString("N0");
                float cw = row.Rt.sizeDelta.x;
                row.Bar.sizeDelta = new Vector2(cw * row.Share * n, BarH);
            }

            if (now >= _until) Hide();
        }

        // as big as the screen allows, keeping the card's shape
        private float FitScale()
        {
            Rect r = ((RectTransform)_root.parent).rect;
            return r.width > 0f && r.height > 0f ? Mathf.Min(r.width / W, r.height / H) : 1f;
        }

        private const float RankLands = 0.78f; // when the rank stamp first reaches its size

        // Damped spring from 0 to 1: passes 1, comes back, settles.
        private static float Spring(float t, float hz, float damping) =>
            1f - Mathf.Exp(-damping * t) * Mathf.Cos(2f * Mathf.PI * hz * t);

        private static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static float OutQuint(float t) => 1f - Mathf.Pow(1f - t, 5f);

        private static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);

        private static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static string Pct(float v) => (Math.Round(v * 100f, 2)).ToString("0.00") + "%";

        // ---- death: small card that flies across the screen and lands at the right edge ----

        private class MiniCard
        {
            private const float MW = 592f, MH = 290f, MarginRight = 24f;
            private const float ScreenY = 0.199f; // card center above the screen center, as a share of the height
            private const float ZeroAlpha = 0.55f;

            private static readonly string[] Labels =
                { "XPERFECT", "PERFECT", "E.PERFECT", "L.PERFECT", "OVERLOAD", "EARLY!", "LATE!", "T.EARLY", "T.LATE", "MISS" };

            public readonly RectTransform Root;
            private readonly CanvasGroup _group;
            private readonly RectTransform _letterRt;
            private readonly TextMeshProUGUI _title, _hero, _statValues;
            private readonly RectTransform _fill, _best;
            private readonly TextMeshProUGUI[] _counts = new TextMeshProUGUI[10];
            private readonly CanvasGroup[] _cells = new CanvasGroup[10];
            private readonly int[] _targets = new int[10];
            private readonly float _trackW;
            private float _progress, _shownAt, _until;

            public MiniCard(Transform parent)
            {
                Root = ElementView.NewRect("ResultMini", parent);
                Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
                Root.sizeDelta = new Vector2(MW, MH);
                _group = Root.gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;

                Color top = Snapshot.Hex("FF6B78"), bottom = Snapshot.Hex("C21F3A");
                float l = -MW / 2f, t = MH / 2f;
                Box(Root, "Border", Vector2.zero, new Vector2(MW + 2f, MH + 2f), new Color(1f, 1f, 1f, 0.12f), 1.6f);
                Box(Root, "Body", Vector2.zero, new Vector2(MW, MH), new Color(BodyColor.r, BodyColor.g, BodyColor.b, 0.96f), 1.6f);
                Image rankBg = Box(Root, "RankArea", Vector2.zero, new Vector2(126f, MH - 12f), new Color(bottom.r, bottom.g, bottom.b, 0.16f), 2f);
                Place(rankBg.rectTransform, l + 6f, 0f, 0f, 0.5f);

                TextMeshProUGUI letter = Text(rankBg.rectTransform, "Letter", 124f, _num, TextAlignmentOptions.Center);
                _letterRt = letter.rectTransform;
                Place(_letterRt, 0f, 26f, 0.5f, 0.5f, 126f, 150f);
                letter.text = "F";
                letter.enableVertexGradient = true;
                letter.colorGradient = new VertexGradient(top, top, bottom, bottom);
                TextMeshProUGUI type = Text(rankBg.rectTransform, "Type", 21f, _num, TextAlignmentOptions.Center);
                Place(type.rectTransform, 0f, -68f, 0.5f, 1f, 126f, 30f);
                type.text = Korean ? "실패" : "FAILED";
                type.color = Color.Lerp(top, bottom, 0.15f);
                type.fontStyle = FontStyles.Bold;

                float x0 = l + 150f, right = -l - 18f, w = right - x0;
                _title = Text(Root, "Title", 24f, _bold, TextAlignmentOptions.TopLeft);
                Place(_title.rectTransform, x0, t - 12f, 0f, 1f, w, 34f);
                _title.overflowMode = TextOverflowModes.Ellipsis;

                TextMeshProUGUI heroLabel = Text(Root, "HeroLabel", 15f, _num, TextAlignmentOptions.TopLeft);
                Place(heroLabel.rectTransform, x0, t - 50f, 0f, 1f, 200f, 20f);
                heroLabel.text = "PROGRESS";
                heroLabel.color = Dim;
                _hero = Text(Root, "Hero", 58f, _num, TextAlignmentOptions.TopLeft);
                Place(_hero.rectTransform, x0 - 2f, t - 66f, 0f, 1f, 230f, 64f);
                _hero.fontStyle = FontStyles.Bold;

                TextMeshProUGUI statLabels = Text(Root, "StatLabels", 18f, _num, TextAlignmentOptions.TopLeft);
                Place(statLabels.rectTransform, right - 180f, t - 54f, 0f, 1f, 90f, 74f);
                statLabels.lineSpacing = 12f;
                statLabels.color = Dim;
                statLabels.text = "BEST\nX-ACC\nCOMBO";
                _statValues = Text(Root, "StatValues", 18f, _num, TextAlignmentOptions.TopRight);
                Place(_statValues.rectTransform, right, t - 54f, 1f, 1f, 110f, 74f);
                _statValues.lineSpacing = 12f;
                _statValues.fontStyle = FontStyles.Bold;

                _trackW = w;
                Image track = Box(Root, "Track", Vector2.zero, new Vector2(w, 5f), new Color(1f, 1f, 1f, 0.12f), 10f);
                Place(track.rectTransform, x0, t - 134f, 0f, 1f);
                Image fill = Box(track.rectTransform, "Fill", Vector2.zero, new Vector2(0f, 5f), bottom, 10f);
                _fill = fill.rectTransform;
                _fill.anchorMin = _fill.anchorMax = new Vector2(0f, 0.5f);
                _fill.pivot = new Vector2(0f, 0.5f);
                Image best = Box(track.rectTransform, "Best", Vector2.zero, new Vector2(3f, 15f), new Color(1f, 1f, 1f, 0.9f), 10f);
                _best = best.rectTransform;
                _best.anchorMin = _best.anchorMax = new Vector2(0f, 0.5f);

                // 5 columns x 2 rows: count over its name
                float cw = w / 5f, rh = 62f, gy = t - 148f;
                for (int i = 0; i < 10; i++)
                {
                    RectTransform cell = ElementView.NewRect("Cell" + i, Root);
                    Place(cell, x0 + i % 5 * cw, gy - i / 5 * rh, 0f, 1f, cw, rh);
                    _cells[i] = cell.gameObject.AddComponent<CanvasGroup>();
                    _counts[i] = Text(cell, "Count", 25f, _num, TextAlignmentOptions.TopLeft);
                    Place(_counts[i].rectTransform, -cw / 2f, rh / 2f - 2f, 0f, 1f, cw, 32f);
                    _counts[i].fontStyle = FontStyles.Bold;
                    TextMeshProUGUI label = Text(cell, "Label", 14f, _num, TextAlignmentOptions.TopLeft);
                    Place(label.rectTransform, -cw / 2f, rh / 2f - 34f, 0f, 1f, cw, 20f);
                    label.text = Labels[i];
                    label.color = Snapshot.Hex(RowColors[i]);
                }
                Root.gameObject.SetActive(false);
            }

            public void Show(Data d, float seconds)
            {
                _title.text = SongTitle(d.Title);
                _statValues.text = Pct(d.Best) + "\n" + Pct(d.XAcc) + "\n" + d.MaxCombo.ToString("N0");
                _progress = Mathf.Clamp01(d.Progress);
                _best.gameObject.SetActive(d.Best > 0f && d.Best < 1f);
                _best.anchoredPosition = new Vector2(_trackW * Mathf.Clamp01(d.Best), 0f);
                int[] counts = Counts(d.Hits);
                for (int i = 0; i < 10; i++)
                {
                    _targets[i] = counts[i];
                    _cells[i].alpha = counts[i] == 0 ? ZeroAlpha : 1f;
                }
                _shownAt = UnityEngine.Time.unscaledTime;
                _until = seconds > 0f ? _shownAt + seconds : float.MaxValue;
                Root.gameObject.SetActive(true);
                Root.SetAsLastSibling();
                Tick();
            }

            public void Hide() => Root.gameObject.SetActive(false);

            public void Tick()
            {
                if (!Root.gameObject.activeSelf) return;
                float now = UnityEngine.Time.unscaledTime, t = now - _shownAt;
                Rect screen = ((RectTransform)Root.parent).rect;
                float sc = Main.Settings.ResultScale;
                float target = screen.width / 2f - MarginRight - MW / 2f * sc;
                float start = -screen.width / 2f - MW / 2f * sc;

                // flies in from the left edge, passes its spot and wobbles back; stretched on the way, squashed on landing
                float p = OutQuint(Mathf.Clamp01(t / 0.38f));
                float settle = Mathf.Max(0f, t - 0.2f);
                float wobble = Mathf.Sin(settle * 15f) * Mathf.Exp(-settle * 6f);
                float fadeOut = Mathf.Clamp01((_until - now) / 0.35f);
                float x = Mathf.Lerp(start, target, p) + wobble * 60f + (1f - fadeOut) * 60f;
                Root.anchoredPosition = new Vector2(x, screen.height * ScreenY);
                float stretch = 1f - p;
                Root.localScale = new Vector3(sc * (1f + 0.35f * stretch - 0.09f * wobble), sc * (1f - 0.22f * stretch + 0.09f * wobble), 1f);
                _group.alpha = Mathf.Clamp01(t / 0.08f) * fadeOut * (OverlayEditor.Active || OverlayEditor.Opening ? 0f : 1f);

                // F stamps in once the card has landed
                float stamp = Mathf.Clamp01((t - 0.3f) / 0.32f);
                float ls = Mathf.LerpUnclamped(2f, 1f, OutBack(stamp));
                _letterRt.localScale = new Vector3(ls, ls, 1f);

                float c = OutExpo(Mathf.Clamp01((t - 0.2f) / 0.9f));
                _hero.text = Pct(_progress * c);
                _fill.sizeDelta = new Vector2(_trackW * _progress * c, 5f);
                for (int i = 0; i < 10; i++) _counts[i].text = Mathf.RoundToInt(_targets[i] * c).ToString("N0");

                if (now >= _until) Hide();
            }
        }
    }
}
