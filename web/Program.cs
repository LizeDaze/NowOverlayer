using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NowOverlayer.Web
{
    // Usage: NowOverlayer.Web.exe "<url>" <width> <height> <fps> <muted 0|1>
    // stdout: frames as int32 little-endian length + PNG bytes.
    // stdin:  url <u> | size <w> <h> | fps <n> | mute <0|1> | reload | pause | resume | quit  (EOF = quit)
    internal static class Program
    {
        private static Form _form;
        private static WebView2 _web;
        private static System.Windows.Forms.Timer _timer;
        private static Stream _out;
        private static bool _busy, _paused, _ready;
        private static string _url;

        [STAThread]
        private static int Main(string[] args)
        {
            _url = args.Length > 0 ? args[0] : "about:blank";
            int w = args.Length > 1 ? int.Parse(args[1]) : 480;
            int h = args.Length > 2 ? int.Parse(args[2]) : 270;
            float fps = args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 10f;
            bool muted = args.Length <= 4 || args[4] != "0";
            _out = Console.OpenStandardOutput();

            Application.EnableVisualStyles();
            _form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ClientSize = new Size(w, h),
                Text = "NowOverlayer.Web"
            };
            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
            _form.Controls.Add(_web);
            _timer = new System.Windows.Forms.Timer { Interval = Interval(fps) };
            _timer.Tick += async (s, e) =>
            {
                if (_busy || _paused || !_ready) return;
                _busy = true;
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                        ms.Position = 0;
                        byte[] frame = Raw(ms, out int fw, out int fh);
                        // unchanged page (static chat, idle widget): send nothing, the game keeps the last frame
                        uint sum = Checksum(frame);
                        if (sum == _lastSum && fw == _lastW && fh == _lastH) return;
                        _lastSum = sum;
                        _lastW = fw;
                        _lastH = fh;
                        _out.Write(BitConverter.GetBytes(frame.Length + 8), 0, 4);
                        _out.Write(BitConverter.GetBytes(fw), 0, 4);
                        _out.Write(BitConverter.GetBytes(fh), 0, 4);
                        _out.Write(frame, 0, frame.Length);
                        _out.Flush();
                    }
                }
                catch (IOException)
                {
                    Application.Exit(); // the game closed the pipe
                }
                catch (Exception)
                {
                    // page still loading; try next tick
                }
                finally
                {
                    _busy = false;
                }
            };

            _form.Load += async (s, e) =>
            {
                try
                {
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NowOverlayer", "WebView2");
                    var opts = new CoreWebView2EnvironmentOptions(
                        "--disable-features=CalculateNativeWinOcclusion --disable-background-timer-throttling " +
                        "--disable-renderer-backgrounding --disable-backgrounding-occluded-windows --autoplay-policy=no-user-gesture-required");
                    CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, data, opts);
                    await _web.EnsureCoreWebView2Async(env);
                    _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _web.CoreWebView2.IsMuted = muted;
                    await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                        "addEventListener('DOMContentLoaded',()=>{const s=document.createElement('style');" +
                        "s.textContent='::-webkit-scrollbar{display:none}';document.head.appendChild(s);});");
                    Navigate(_url);
                    _ready = true;
                    _timer.Start();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Application.Exit();
                }
            };

            new Thread(ReadCommands) { IsBackground = true }.Start();
            Application.Run(_form);
            return 0;
        }

        private static uint _lastSum;
        private static int _lastW, _lastH;

        // PNG -> raw BGRA, rows bottom-up (Unity texture order), so the game only uploads bytes.
        private static byte[] Raw(Stream png, out int w, out int h)
        {
            using (var bmp = new Bitmap(png))
            {
                w = bmp.Width;
                h = bmp.Height;
                var rect = new Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = w * 4;
                    var buf = new byte[stride * h];
                    for (int y = 0; y < h; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + data.Stride * y, buf, (h - 1 - y) * stride, stride);
                    }
                    return buf;
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
        }

        private static uint Checksum(byte[] b)
        {
            uint hsh = 2166136261;
            for (int i = 0; i < b.Length; i += 4) hsh = (hsh ^ (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24)) * 16777619;
            return hsh;
        }

        private static int Interval(float fps) => Math.Max(15, (int)(1000f / Math.Max(1f, fps)));

        private static void Navigate(string url)
        {
            _url = url;
            try
            {
                _web.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        private static void ReadCommands()
        {
            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    string cmd = line.Trim();
                    if (cmd == "quit") break;
                    _form.BeginInvoke((Action)(() => Handle(cmd)));
                }
            }
            catch (Exception)
            {
                // stdin closed
            }
            try
            {
                _form.BeginInvoke((Action)Application.Exit);
            }
            catch (Exception)
            {
                Environment.Exit(0);
            }
        }

        private static void Handle(string cmd)
        {
            int sp = cmd.IndexOf(' ');
            string name = sp < 0 ? cmd : cmd.Substring(0, sp);
            string arg = sp < 0 ? "" : cmd.Substring(sp + 1).Trim();
            switch (name)
            {
                case "url":
                    if (_ready) Navigate(arg);
                    else _url = arg;
                    break;
                case "reload":
                    if (_ready) _web.CoreWebView2.Reload();
                    break;
                case "size":
                    string[] p = arg.Split(' ');
                    if (p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h))
                    {
                        _form.ClientSize = new Size(Math.Max(16, w), Math.Max(16, h));
                    }
                    break;
                case "fps":
                    if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) _timer.Interval = Interval(f);
                    break;
                case "mute":
                    if (_ready) _web.CoreWebView2.IsMuted = arg != "0";
                    break;
                case "pause":
                    _paused = true;
                    break;
                case "resume":
                    _paused = false;
                    break;
            }
        }
    }
}
