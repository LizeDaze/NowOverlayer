using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NowOverlayer
{
    // Shows a web page. Web\NowOverlayer.Web.exe hosts Edge WebView2 off-screen and streams PNG frames
    // over stdout: int32 length, then int32 width, int32 height and raw BGRA rows bottom-up (decoded in the helper,
    // so the game only uploads bytes; unchanged pages send nothing). Commands go over stdin.
    public class WebView : ElementView
    {
        private readonly RawImage _image;
        private readonly TextMeshProUGUI _status;
        private Texture2D _tex;

        private Process _proc;
        private Thread _reader;
        private readonly object _lock = new object();
        private byte[] _frame;
        private int _frameW, _frameH;
        private bool _gotFrame, _failed, _paused;
        private string _error;
        private string _url;
        private float _w, _h, _fps;
        private bool _muted;

        public WebView(ElementConfig cfg, Transform parent, Overlay o)
        {
            Cfg = cfg;
            Root = NewRect("Web", parent);

            RectTransform img = NewRect("Image", Root);
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
            _status = NewText(st, o, 20f);
            _status.alignment = TextAlignmentOptions.Center;
            _status.textWrappingMode = TextWrappingModes.Normal;
            _status.color = new Color(1f, 1f, 1f, 0.7f);
            _status.text = "웹페이지 불러오는 중…";
        }

        // ImageConversion is looked up at runtime: its module targets netstandard 2.1, which net48 can't reference.
        private static readonly System.Reflection.MethodInfo LoadImageMethod =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        public static bool LoadImageBytes(Texture2D tex, byte[] data) =>
            LoadImageMethod != null && (bool)LoadImageMethod.Invoke(null, new object[] { tex, data, false });

        private static string ExePath => Path.Combine(Path.Combine(Main.Mod.Path, "Web"), "NowOverlayer.Web.exe");

        protected override void ApplyStyle()
        {
            Root.sizeDelta = new Vector2(Cfg.Width, Cfg.Height);
            if (_proc == null || _proc.HasExited) return;
            if (Cfg.Url != _url)
            {
                _url = Cfg.Url;
                _gotFrame = false;
                Send("url " + _url);
            }
            if (Cfg.Width != _w || Cfg.Height != _h)
            {
                _w = Cfg.Width;
                _h = Cfg.Height;
                Send("size " + (int)_w + " " + (int)_h);
            }
            if (Cfg.Fps != _fps)
            {
                _fps = Cfg.Fps;
                Send("fps " + _fps.ToString(CultureInfo.InvariantCulture));
            }
            if (Cfg.Muted != _muted)
            {
                _muted = Cfg.Muted;
                Send("mute " + (_muted ? 1 : 0));
            }
        }

        public void Reload()
        {
            _failed = false;
            _error = null;
            if (_proc != null && !_proc.HasExited) Send("reload");
        }

        public override void Render(Snapshot s) { }

        public override void Tick(float dt)
        {
            bool want = Cfg.Visible && Overlay.Instance && Overlay.Instance.IsVisible && !string.IsNullOrEmpty(Cfg.Url);
            if (want && !_failed && (_proc == null || _proc.HasExited)) Start();
            if (_proc != null && !_proc.HasExited && want == _paused)
            {
                _paused = !want;
                Send(_paused ? "pause" : "resume");
            }

            byte[] frame = null;
            int fw = 0, fh = 0;
            lock (_lock)
            {
                if (_frame != null)
                {
                    frame = _frame;
                    fw = _frameW;
                    fh = _frameH;
                    _frame = null;
                }
            }
            if (frame != null)
            {
                if (!_tex || _tex.width != fw || _tex.height != fh)
                {
                    if (_tex) UnityEngine.Object.Destroy(_tex);
                    _tex = new Texture2D(fw, fh, TextureFormat.BGRA32, false) { hideFlags = HideFlags.HideAndDontSave };
                }
                _tex.LoadRawTextureData(frame);
                _tex.Apply(false, false);
                _image.texture = _tex;
                _image.color = Color.white;
                _gotFrame = true;
            }

            string status = _failed ? "웹페이지를 불러오지 못했어요\n" + _error
                : !_gotFrame ? "웹페이지 불러오는 중…" : "";
            if (_status.text != status) _status.text = status;
            if (!_gotFrame) _image.color = new Color(1f, 1f, 1f, 0f);
        }

        private void Start()
        {
            Dispose();
            try
            {
                if (!File.Exists(ExePath)) throw new FileNotFoundException("Web\\NowOverlayer.Web.exe 없음");
                _url = Cfg.Url;
                _w = Cfg.Width;
                _h = Cfg.Height;
                _fps = Cfg.Fps;
                _muted = Cfg.Muted;
                var psi = new ProcessStartInfo
                {
                    FileName = ExePath,
                    Arguments = Quote(_url) + " " + (int)_w + " " + (int)_h + " " +
                                _fps.ToString(CultureInfo.InvariantCulture) + " " + (_muted ? 1 : 0),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = Path.GetDirectoryName(ExePath)
                };
                _proc = Process.Start(psi);
                _paused = false;
                Process p = _proc;
                _reader = new Thread(() => ReadLoop(p)) { IsBackground = true, Name = "NowOverlayer.Web" };
                _reader.Start();
            }
            catch (Exception e)
            {
                _failed = true;
                _error = e.Message;
                Main.LogError("Web helper start failed", e);
            }
        }

        private void ReadLoop(Process p)
        {
            try
            {
                Stream s = p.StandardOutput.BaseStream;
                var head = new byte[4];
                while (true)
                {
                    if (!ReadFull(s, head, 4)) break;
                    int len = BitConverter.ToInt32(head, 0);
                    if (len <= 0 || len > 64 * 1024 * 1024) break;
                    if (len < 8 || !ReadFull(s, head, 4)) break;
                    int w = BitConverter.ToInt32(head, 0);
                    if (!ReadFull(s, head, 4)) break;
                    int h = BitConverter.ToInt32(head, 0);
                    var data = new byte[len - 8];
                    if (!ReadFull(s, data, data.Length)) break;
                    if (w <= 0 || h <= 0 || data.Length != w * h * 4) continue;
                    lock (_lock)
                    {
                        _frame = data;
                        _frameW = w;
                        _frameH = h;
                    }
                }
            }
            catch (Exception)
            {
                // process ended
            }
            if (p == _proc && !_gotFrame)
            {
                _failed = true;
                _error = "Edge WebView2 런타임을 확인해 주세요";
            }
        }

        private static bool ReadFull(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        private void Send(string line)
        {
            try
            {
                _proc.StandardInput.WriteLine(line);
                _proc.StandardInput.Flush();
            }
            catch (Exception)
            {
                // helper gone; Tick restarts it
            }
        }

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "%22") + "\"";

        public override void Dispose()
        {
            Process p = _proc;
            _proc = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited)
                {
                    p.StandardInput.WriteLine("quit");
                    p.StandardInput.Flush();
                    if (!p.WaitForExit(800)) p.Kill();
                }
            }
            catch (Exception)
            {
                // already exiting
            }
        }
    }
}
