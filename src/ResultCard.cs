using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace NowOverlayer
{
    // Rhythm-game style result screen on clear / death.
    // Left: a rank letter (X / SS / S+ / S / A / B / C / D, F on death) stamped onto a slanted color band, with the
    // clear type under it (ALL XPERFECT / PURE PERFECT / CLEAR / FAILED, Korean when the game is in Korean). Right: song, a counting-up X-Accuracy
    // (progress on death), small stats, a progress bar and a 10-row judgement table where XPerfect has its own row.
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

        private static TMP_FontAsset _num, _bold, _regular;
        private static Material _glowMat;

        private readonly RectTransform _root, _rankBox, _band, _burst;
        private readonly CanvasGroup _group, _rankGroup;
        private readonly Image _topLine, _bandImg, _burstImg, _barFill, _barBest, _chip;
        private readonly TextMeshProUGUI _rank, _rankGhost, _clearType, _chipText, _badges, _title, _artist,
            _heroLabel, _hero, _statLabels, _statValues, _progressLabel;
        private readonly Row[] _rows = new Row[10];

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
            _root = ElementView.NewRect("Result", parent);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(W, H);
            _root.gameObject.AddComponent<Canvas>();
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            Box(_root, "Dim", Vector2.zero, new Vector2(8000f, 8000f), new Color(0f, 0f, 0f, 0.55f), 1f);
            Box(_root, "Shadow", new Vector2(0f, -14f), new Vector2(W + 50f, H + 50f), new Color(0f, 0f, 0f, 0.45f), 0.6f);
            Box(_root, "Border", Vector2.zero, new Vector2(W + 2f, H + 2f), new Color(1f, 1f, 1f, 0.10f), 1.2f);
            Box(_root, "Body", Vector2.zero, new Vector2(W, H), new Color(0.035f, 0.035f, 0.05f, 0.985f), 1.2f);
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
            if (!d.Cleared) return new Grade { Letter = "F", Type = Korean ? "실패" : "FAILED", Top = Snapshot.Hex("FF6B78"), Bottom = Snapshot.Hex("C21F3A") };
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

        public void Show(Data d, float seconds)
        {
            _d = d;
            int[] h = d.Hits;
            int[] counts =
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
            _chipText.text = Korean ? (d.Cleared ? "레벨 클리어" : "레벨 실패") : (d.Cleared ? "LEVEL CLEAR" : "LEVEL FAILED");
            _chip.rectTransform.sizeDelta = new Vector2(_chipText.GetPreferredValues(_chipText.text).x + 32f, 34f);

            var badges = new List<string>();
            if (d.FirstClear) badges.Add("FIRST CLEAR");
            if (d.NewBestXAcc) badges.Add("NEW RECORD");
            if (d.NewBestProgress) badges.Add("BEST PROGRESS");
            _badges.text = badges.Count == 0 ? "" : "<color=#FFD54A>" + string.Join("   ", badges.ToArray()) + "</color>";

            string title = d.Title ?? "";
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            _title.text = dash > 0 ? title.Substring(dash + 3) : (title.Length > 0 ? title : "A Dance of Fire and Ice");
            _artist.text = dash > 0 ? title.Substring(0, dash) : "";

            _heroLabel.text = d.Cleared ? "X-ACCURACY" : "PROGRESS";
            _heroTarget = d.Cleared ? d.XAcc : d.Progress;
            _barTarget = Mathf.Clamp01(d.Progress);
            _barFill.color = _accent;
            float trackW = TrackW;
            _barBest.gameObject.SetActive(d.Best > 0f && d.Best < 1f);
            _barBest.rectTransform.anchoredPosition = new Vector2(trackW * Mathf.Clamp01(d.Best), 0f);
            _progressLabel.text = d.Cleared ? (d.FullRun && d.MapBestXAcc >= 0f ? "MAP BEST  " + Pct(d.MapBestXAcc) : "")
                : "BEST  " + Pct(d.Best);

            _statLabels.text = "ACCURACY\nMAX COMBO\nTIME" + (d.Cleared ? "" : "\nX-ACC");
            _statValues.text = Pct(d.Acc) + "\n" + d.MaxCombo.ToString("N0") + "\n" + Snapshot.TimeStr(d.Time, d.Time >= 3600f)
                               + (d.Cleared ? "" : "\n" + Pct(d.XAcc));

            for (int i = 0; i < 10; i++)
            {
                _rows[i].Target = counts[i];
                _rows[i].Share = counts[i] / (float)most;
                _rows[i].Group.alpha = counts[i] == 0 && i >= 4 ? EmptyRowAlpha : 1f; // empty bad rows step back
            }

            _shownAt = UnityEngine.Time.unscaledTime;
            _until = seconds > 0f ? _shownAt + seconds : float.MaxValue;
            _root.gameObject.SetActive(true);
            Tick();
        }

        private static float TrackW => W / 2f - Pad - (-W / 2f + RankW + 20f);

        public void Hide()
        {
            _until = -1f;
            _root.gameObject.SetActive(false);
        }

        public void Tick()
        {
            if (!_root.gameObject.activeSelf || _d == null) return;
            float now = UnityEngine.Time.unscaledTime;
            float t = now - _shownAt;

            // card: rise + fade + slight scale
            float k = OutCubic(Mathf.Clamp01(t / 0.4f));
            float sc = Mathf.Lerp(0.95f, 1f, k) * Main.Settings.ResultScale;
            _root.localScale = new Vector3(sc, sc, 1f);
            _root.anchoredPosition = new Vector2(0f, Mathf.Lerp(-30f, 0f, k));
            float fadeOut = Mathf.Clamp01((_until - now) / 0.35f);
            _group.alpha = k * fadeOut;

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
                float ri = OutCubic(Mathf.Clamp01((t - 0.3f - order * 0.045f) / 0.35f));
                float baseA = row.Target == 0 && i >= 4 ? EmptyRowAlpha : 1f;
                row.Group.alpha = ri * baseA;
                row.Rt.anchoredPosition = new Vector2(row.BaseX + (1f - ri) * 24f, row.Rt.anchoredPosition.y);
                float n = OutExpo(Mathf.Clamp01((t - 0.45f - order * 0.045f) / 0.9f));
                row.Count.text = Mathf.RoundToInt(row.Target * n).ToString("N0");
                float cw = row.Rt.sizeDelta.x;
                row.Bar.sizeDelta = new Vector2(cw * row.Share * n, BarH);
            }

            if (now >= _until) Hide();
        }

        private static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);

        private static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static string Pct(float v) => (Math.Round(v * 100f, 2)).ToString("0.00") + "%";
    }
}
