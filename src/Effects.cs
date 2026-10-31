using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NowOverlayer
{
    public static class Sprites
    {
        private static Sprite _round, _ring, _circle;

        // Filled anti-aliased circle (webcam circle mask).
        public static Sprite Circle
        {
            get
            {
                if (_circle) return _circle;
                const int size = 256;
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                float r = size / 2f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
                }
                t.Apply();
                _circle = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _circle;
            }
        }

        // White rounded rect, 9-sliced; tint with Image.color.
        public static Sprite Round
        {
            get
            {
                if (_round) return _round;
                Texture2D t = Ui.Round(Color.white, 24);
                _round = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
                return _round;
            }
        }

        public static Sprite Ring
        {
            get
            {
                if (_ring) return _ring;
                const int size = 128;
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                float r = size / 2f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - (r - 8f)) / 5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                t.Apply();
                _ring = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _ring;
            }
        }
    }

    // Short message at the top center (preset switched, OBS status...).
    public class Toast
    {
        private readonly RectTransform _root;
        private readonly TextMeshProUGUI _text;
        private readonly CanvasGroup _group;
        private float _until = -1f;

        public Toast(Transform parent, Overlay o)
        {
            _root = ElementView.NewRect("Toast", parent);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -40f);
            _root.sizeDelta = new Vector2(420f, 44f);
            var bg = _root.gameObject.AddComponent<Image>();
            bg.sprite = Sprites.Round;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.85f);
            bg.raycastTarget = false;
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            RectTransform t = ElementView.NewRect("Text", _root);
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = t.offsetMax = Vector2.zero;
            _text = ElementView.NewText(t, o, 20f);
            _text.alignment = TextAlignmentOptions.Center;
            _root.gameObject.SetActive(false);
        }

        public void Show(string msg, float seconds = 1.6f)
        {
            _text.text = msg;
            _root.sizeDelta = new Vector2(Mathf.Max(220f, _text.GetPreferredValues(msg).x + 48f), 44f);
            _until = Time.unscaledTime + seconds;
            _root.gameObject.SetActive(true);
        }

        public bool Active => _until > Time.unscaledTime;

        public void Tick()
        {
            if (!_root.gameObject.activeSelf) return;
            float left = _until - Time.unscaledTime;
            _group.alpha = Mathf.Clamp01(left / 0.3f);
            if (left <= 0f) _root.gameObject.SetActive(false);
        }
    }
}
