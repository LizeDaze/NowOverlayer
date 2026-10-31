using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NowOverlayer
{
    public abstract class ElementView
    {
        public ElementConfig Cfg;
        public RectTransform Root;
        private CanvasGroup _group;
        private float _size = 1f;

        public abstract void Render(Snapshot s);

        public virtual void Tick(float dt) { }

        public virtual void Dispose() { }

        public void ApplyLayout(float globalSize)
        {
            _size = globalSize;
            if (!_group)
            {
                // own canvas: a changing number only rebuilds this item's mesh, not the whole overlay
                Root.gameObject.AddComponent<Canvas>();
                _group = Root.gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            Root.anchorMin = Root.anchorMax = new Vector2(Cfg.AnchorX, Cfg.AnchorY);
            Root.pivot = new Vector2(Cfg.PivotX, Cfg.PivotY);
            Root.gameObject.SetActive(Cfg.Visible);
            ApplyStyle();
            ApplyPose(Pose.Identity);
        }

        // Entry-animation frame on top of the configured position/scale/opacity.
        public void ApplyPose(Pose p)
        {
            Root.anchoredPosition = new Vector2(Cfg.X, Cfg.Y) + p.Offset;
            float sc = Cfg.Scale * _size * p.Scale;
            Root.localScale = new Vector3(sc, sc * p.ScaleY, 1f);
            Root.localEulerAngles = new Vector3(0f, 0f, p.Rot);
            _group.alpha = Mathf.Clamp01(p.Alpha) * Cfg.Opacity;
            SetVisibleFraction(p.Visible);
        }

        protected virtual void SetVisibleFraction(float f) { }

        protected virtual void ApplyStyle() { }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // Puts the item's own font (ElementConfig.Font, "" = overlay font) on t; only touches TMP when it changed.
        public static void ApplyFont(TextMeshProUGUI t, string path)
        {
            Overlay o = Overlay.Instance;
            if (!o || !t) return;
            TMP_FontAsset f = o.FontFor(path);
            Material m = o.MaterialFor(path);
            if (t.font != f) t.font = f;
            if (m && t.fontSharedMaterial != m) t.fontSharedMaterial = m;
        }

        public static TextMeshProUGUI NewText(RectTransform rt, Overlay o, float size, string fontPath = null)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = o.FontFor(fontPath);
            Material mat = o.MaterialFor(fontPath);
            if (mat) t.fontSharedMaterial = mat;
            t.fontSize = size;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            t.color = Color.white;
            return t;
        }
    }

    public class TextView : ElementView
    {
        private readonly TextMeshProUGUI _text;
        private string _last;

        public TextView(ElementConfig cfg, Transform parent, Overlay o)
        {
            Cfg = cfg;
            Root = NewRect(cfg.Type.ToString(), parent);
            RectTransform t = NewRect("Text", Root);
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = t.offsetMax = Vector2.zero;
            _text = NewText(t, o, cfg.FontSize, cfg.Font);
        }

        protected override void ApplyStyle()
        {
            ApplyFont(_text, Cfg.Font);
            _text.fontSize = Cfg.FontSize;
            _text.color = Snapshot.Hex(Cfg.Color);
            _text.lineSpacing = Cfg.Type == ElementType.Bpm ? 30f : 0f;
            int h = Cfg.PivotX < 0.25f ? 0 : Cfg.PivotX > 0.75f ? 2 : 1;
            int v = Cfg.PivotY > 0.75f ? 0 : Cfg.PivotY < 0.25f ? 2 : 1;
            _text.alignment = Aligns[v, h];
            _last = null;
        }

        private static readonly TextAlignmentOptions[,] Aligns =
        {
            { TextAlignmentOptions.TopLeft, TextAlignmentOptions.Top, TextAlignmentOptions.TopRight },
            { TextAlignmentOptions.Left, TextAlignmentOptions.Center, TextAlignmentOptions.Right },
            { TextAlignmentOptions.BottomLeft, TextAlignmentOptions.Bottom, TextAlignmentOptions.BottomRight }
        };

        protected override void SetVisibleFraction(float f)
        {
            _text.maxVisibleCharacters = f >= 1f ? 99999 : Mathf.CeilToInt(_text.textInfo.characterCount * f);
        }

        public override void Render(Snapshot s)
        {
            string text = s.Text(Cfg);
            if (text == _last) return;
            _last = text;
            _text.text = text;
            // The rect only matters for the editor's boxes (text is aligned to the pivot either way),
            // so skip the layout pass while playing.
            if (OverlayEditor.Active || !_sized) FitSize();
        }

        private bool _sized;

        public void FitSize()
        {
            if (_last == null) return;
            Vector2 size = _text.GetPreferredValues(_last);
            Root.sizeDelta = new Vector2(Mathf.Max(size.x, 12f), Mathf.Max(size.y, 12f));
            _sized = true;
        }
    }

    public class BarView : ElementView
    {
        private readonly RectTransform _fill, _best;

        public BarView(ElementConfig cfg, Transform parent)
        {
            Cfg = cfg;
            Root = NewRect("ProgressBar", parent);
            Image bg = Root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.45f, 0.12f, 0.18f, 0.55f);
            bg.raycastTarget = false;

            _fill = NewRect("Fill", Root);
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.sizeDelta = Vector2.zero;
            Image fill = _fill.gameObject.AddComponent<Image>();
            fill.color = Snapshot.Hex("E44343");
            fill.raycastTarget = false;

            // Thin tick at the best progress from this start position.
            _best = NewRect("Best", Root);
            _best.anchorMin = new Vector2(0f, 0f);
            _best.anchorMax = new Vector2(0f, 1f);
            _best.pivot = new Vector2(0.5f, 0.5f);
            _best.sizeDelta = new Vector2(2f, 6f);
            Image best = _best.gameObject.AddComponent<Image>();
            best.color = new Color(1f, 1f, 1f, 0.85f);
            best.raycastTarget = false;
        }

        protected override void ApplyStyle() => Root.sizeDelta = new Vector2(Cfg.Width, Cfg.Height);

        public override void Render(Snapshot s)
        {
            _fill.sizeDelta = new Vector2(Mathf.Clamp01(s.Progress) * Cfg.Width, 0f);
            _best.gameObject.SetActive(s.Best > 0f && s.Best < 1f);
            _best.anchoredPosition = new Vector2(Mathf.Clamp01(s.Best) * Cfg.Width, 0f);
        }
    }

    public class ComboView : ElementView
    {
        private const float BumpTime = 0.15f;
        private const float TitleY = 62f; // was 70; gap to the number tightened
        private readonly TextMeshProUGUI _title, _value;
        private int _last = -1;
        private float _bumpT = BumpTime;

        public ComboView(ElementConfig cfg, Transform parent, Overlay o)
        {
            Cfg = cfg;
            Root = NewRect("Combo", parent);
            Root.sizeDelta = new Vector2(300f, 170f);

            RectTransform title = NewRect("Title", Root);
            title.anchoredPosition = new Vector2(0f, TitleY);
            title.sizeDelta = new Vector2(300f, 40f);
            _title = NewText(title, o, 34f);
            _title.alignment = TextAlignmentOptions.Center;
            _title.text = "Combo";

            RectTransform value = NewRect("Value", Root);
            value.anchoredPosition = new Vector2(0f, -5f);
            value.sizeDelta = new Vector2(300f, 110f);
            _value = NewText(value, o, 108f);
            _value.alignment = TextAlignmentOptions.Center;
        }

        protected override void ApplyStyle()
        {
            ApplyFont(_title, Cfg.Font);
            ApplyFont(_value, Cfg.Font);
            _title.color = Snapshot.Hex(Cfg.Color);
            _last = -1;
        }

        // ---- milestone effect (100 / 500 / 1000 ... combo) ----

        private const float MilestoneTime = 0.9f;
        private Image _ring;
        private float _mileT = MilestoneTime;
        private Color _baseColor = Color.white;

        private Image Ring()
        {
            if (_ring) return _ring;
            RectTransform rt = NewRect("Ring", Root);
            rt.SetAsFirstSibling();
            rt.anchoredPosition = new Vector2(0f, -5f);
            rt.sizeDelta = new Vector2(200f, 200f);
            _ring = rt.gameObject.AddComponent<Image>();
            _ring.sprite = Sprites.Ring;
            _ring.raycastTarget = false;
            _ring.color = new Color(1f, 1f, 1f, 0f);
            return _ring;
        }

        public static bool IsMilestone(int combo, Settings s)
        {
            if (!s.MilestoneEnabled || combo <= 0) return false;
            foreach (string part in (s.Milestones ?? "").Split(','))
            {
                if (int.TryParse(part.Trim(), out int m) && m == combo) return true;
            }
            return s.MilestoneEvery > 0 && combo % s.MilestoneEvery == 0;
        }

        public override void Render(Snapshot s)
        {
            if (s.Combo == _last) return;
            bool up = s.Combo > _last && _last >= 0;
            if (up) _bumpT = 0f;
            _last = s.Combo;
            _value.text = s.Combo.ToString();
            float t = Mathf.Clamp01(s.Combo / 1000f);
            t = t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
            _baseColor = Color.Lerp(Snapshot.Hex("FF6D6D"), Snapshot.Hex("DA3636"), t);
            _value.color = _baseColor;
            if (up && IsMilestone(s.Combo, Main.Settings)) PlayMilestone(s.Combo);
        }

        public void PlayMilestone(int combo)
        {
            _mileT = 0f;
            _title.text = combo + " COMBO!";
            Ring();
        }

        public override void Tick(float dt)
        {
            float sc = 1f;
            if (_bumpT < BumpTime)
            {
                _bumpT += dt;
                float k = 1f - Mathf.Pow(2f, -10f * Mathf.Clamp01(_bumpT / BumpTime));
                sc = 1.15f - 0.15f * k;
            }
            if (_mileT < MilestoneTime)
            {
                _mileT += dt;
                float m = Mathf.Clamp01(_mileT / MilestoneTime);
                // number: big elastic pulse and a gold flash fading back
                float pulse = 1f + 0.45f * Mathf.Pow(2f, -8f * m) * Mathf.Cos(m * 18f);
                sc = Mathf.Max(sc, pulse);
                Color gold = Snapshot.Hex(Main.Settings.MilestoneColor);
                _value.color = Color.Lerp(gold, _baseColor, m * m);
                // ring: expands and fades
                Image ring = Ring();
                float rs = Mathf.Lerp(0.6f, 2.6f, 1f - Mathf.Pow(1f - m, 3f));
                ring.rectTransform.localScale = new Vector3(rs, rs, 1f);
                ring.color = new Color(gold.r, gold.g, gold.b, 0.9f * (1f - m));
                _title.color = Color.Lerp(gold, Snapshot.Hex(Cfg.Color), Mathf.Clamp01((m - 0.6f) / 0.4f));
                if (_mileT >= MilestoneTime)
                {
                    _title.text = "Combo";
                    _title.color = Snapshot.Hex(Cfg.Color);
                    _value.color = _baseColor;
                    ring.color = new Color(1f, 1f, 1f, 0f);
                }
            }
            _value.rectTransform.localScale = new Vector3(sc, sc, 1f);
        }
    }
}
