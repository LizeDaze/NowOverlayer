using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NowOverlayer
{
    // Image / GIF item. Source is a file in Mods\NowOverlayer\Images, any local path, or an http(s) URL.
    // Loading and GIF decoding run on a worker thread; textures are made on the main thread.
    public class ImageView : ElementView
    {
        private readonly RawImage _image;
        private readonly TextMeshProUGUI _status;
        private readonly List<Texture2D> _frames = new List<Texture2D>();
        private readonly List<float> _delays = new List<float>();
        private int _frame;
        private float _frameTime;

        private string _loaded;
        private volatile bool _busy;
        private byte[] _pendingBytes;
        private Gif.Result _pendingGif;
        private string _error;

        public static string Folder => Path.Combine(Main.Mod.Path, "Images");

        public ImageView(ElementConfig cfg, Transform parent, Overlay o)
        {
            Cfg = cfg;
            Root = NewRect("Image", parent);
            RectTransform img = NewRect("Raw", Root);
            img.anchorMin = Vector2.zero;
            img.anchorMax = Vector2.one;
            img.offsetMin = img.offsetMax = Vector2.zero;
            _image = img.gameObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _image.color = new Color(1f, 1f, 1f, 0f);

            RectTransform st = NewRect("Status", Root);
            st.anchorMin = Vector2.zero;
            st.anchorMax = Vector2.one;
            st.offsetMin = st.offsetMax = Vector2.zero;
            _status = NewText(st, o, 18f);
            _status.alignment = TextAlignmentOptions.Center;
            _status.textWrappingMode = TextWrappingModes.Normal;
            _status.color = new Color(1f, 1f, 1f, 0.7f);
        }

        public Vector2 NativeSize => _frames.Count > 0 ? new Vector2(_frames[0].width, _frames[0].height) : Vector2.zero;

        protected override void ApplyStyle()
        {
            Root.sizeDelta = new Vector2(Mathf.Max(Cfg.Width, 16f), Mathf.Max(Cfg.Height, 16f));
            _image.color = _frames.Count > 0 ? Snapshot.Hex(string.IsNullOrEmpty(Cfg.Color) ? "FFFFFF" : Cfg.Color) : new Color(1f, 1f, 1f, 0f);
        }

        public void Reload() => _loaded = null;

        public override void Render(Snapshot s) { }

        public override void Tick(float dt)
        {
            string src = (Cfg.Url ?? "").Trim();
            if (src != _loaded && !_busy) StartLoad(src);

            if (_pendingBytes != null || _pendingGif != null) FinishLoad();

            if (_frames.Count > 1)
            {
                _frameTime += dt;
                while (_frameTime >= _delays[_frame])
                {
                    _frameTime -= _delays[_frame];
                    _frame = (_frame + 1) % _frames.Count;
                    _image.texture = _frames[_frame];
                }
            }

            string status = _busy ? "이미지 불러오는 중…" : _error ?? (src.Length == 0 ? "이미지를 골라 주세요" : "");
            if (_status.text != status) _status.text = status;
        }

        private void StartLoad(string src)
        {
            _loaded = src;
            _error = null;
            Clear();
            if (src.Length == 0) return;
            _busy = true;
            string folder = Folder;
            new Thread(() =>
            {
                try
                {
                    byte[] data;
                    if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var wc = new WebClient()) data = wc.DownloadData(src);
                    }
                    else
                    {
                        string path = Path.IsPathRooted(src) ? src : Path.Combine(folder, src);
                        data = File.ReadAllBytes(path);
                    }
                    if (Gif.IsGif(data))
                    {
                        Gif.Result g = Gif.Decode(data);
                        if (g.Frames.Count == 0) throw new Exception("GIF 프레임이 없어요");
                        _pendingGif = g;
                    }
                    else
                    {
                        _pendingBytes = data;
                    }
                }
                catch (Exception e)
                {
                    _error = "이미지를 불러오지 못했어요\n" + e.Message;
                    _busy = false;
                }
            }) { IsBackground = true, Name = "NowOverlayer.Image" }.Start();
        }

        private void FinishLoad()
        {
            try
            {
                if (_pendingGif != null)
                {
                    Gif.Result g = _pendingGif;
                    _pendingGif = null;
                    foreach (Gif.Frame f in g.Frames)
                    {
                        var t = NewTex(g.Width, g.Height);
                        t.LoadRawTextureData(FlipRows(f.Rgba, g.Width, g.Height));
                        t.Apply(false, true);
                        _frames.Add(t);
                        _delays.Add(f.DelayMs / 1000f);
                    }
                }
                else
                {
                    byte[] bytes = _pendingBytes;
                    _pendingBytes = null;
                    var t = NewTex(2, 2);
                    if (!WebView.LoadImageBytes(t, bytes)) throw new Exception("PNG / JPG / GIF만 돼요");
                    _frames.Add(t);
                    _delays.Add(1f);
                }
                _frame = 0;
                _frameTime = 0f;
                _image.texture = _frames[0];
                if (Cfg.Width <= 0f || Cfg.Height <= 0f) FitNative();
                ApplyStyle();
            }
            catch (Exception e)
            {
                _error = "이미지를 불러오지 못했어요\n" + e.Message;
                Clear();
            }
            _busy = false;
        }

        // Native size, capped so a huge picture doesn't fill the screen.
        public void FitNative()
        {
            Vector2 n = NativeSize;
            if (n == Vector2.zero) return;
            float k = Mathf.Min(1f, 600f / Mathf.Max(n.x, n.y));
            Cfg.Width = Mathf.Round(n.x * k);
            Cfg.Height = Mathf.Round(n.y * k);
            ApplyStyle();
        }

        private static Texture2D NewTex(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };

        // Unity textures start at the bottom row.
        private static byte[] FlipRows(byte[] rgba, int w, int h)
        {
            var o = new byte[rgba.Length];
            int stride = w * 4;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(rgba, y * stride, o, (h - 1 - y) * stride, stride);
            return o;
        }

        private void Clear()
        {
            foreach (Texture2D t in _frames) UnityEngine.Object.Destroy(t);
            _frames.Clear();
            _delays.Clear();
            _image.texture = null;
            _image.color = new Color(1f, 1f, 1f, 0f);
        }

        public override void Dispose() => Clear();
    }
}
