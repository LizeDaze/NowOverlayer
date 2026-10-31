using UnityEngine;
using UnityEngine.UI;

namespace NowOverlayer
{
    // Webcam item (the WebcamOverlay mod's job, inside the overlay). The camera texture is drawn directly by the GPU
    // (no per-pixel CPU work), cropped to fill the box, with mirror / zoom / rounded or circle mask and a border.
    // Cfg.Url = device name ("" = first camera); Cfg.Color = border color.
    public class WebcamView : ElementView
    {
        private readonly Image _border, _mask;
        private readonly RawImage _image;
        private readonly Mask _maskComp;
        private WebCamTexture _cam;
        private string _device;
        private float _hiddenSince = -1f;

        public WebcamView(ElementConfig cfg, Transform parent)
        {
            Cfg = cfg;
            Root = NewRect("Webcam", parent);

            RectTransform b = NewRect("Border", Root);
            Stretch(b);
            _border = b.gameObject.AddComponent<Image>();
            _border.raycastTarget = false;

            RectTransform m = NewRect("Mask", Root);
            Stretch(m);
            _mask = m.gameObject.AddComponent<Image>();
            _mask.raycastTarget = false;
            _maskComp = m.gameObject.AddComponent<Mask>();
            _maskComp.showMaskGraphic = true;

            RectTransform img = NewRect("Camera", m);
            img.anchorMin = img.anchorMax = img.pivot = new Vector2(0.5f, 0.5f); // sized and rotated in UpdateUv
            _image = img.gameObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        protected override void ApplyStyle()
        {
            Root.sizeDelta = new Vector2(Mathf.Max(Cfg.Width, 32f), Mathf.Max(Cfg.Height, 32f));
            Sprite shape = Cfg.Shape == 2 ? Sprites.Circle : Cfg.Shape == 1 ? Sprites.Round : null;
            foreach (Image i in new[] { _border, _mask })
            {
                i.sprite = shape;
                i.type = Cfg.Shape == 1 ? Image.Type.Sliced : Image.Type.Simple;
                i.pixelsPerUnitMultiplier = 1f;
            }
            _mask.color = new Color(0.08f, 0.08f, 0.1f, 1f); // dark backdrop until the camera starts
            float bw = Mathf.Max(0f, Cfg.Border);
            _mask.rectTransform.offsetMin = new Vector2(bw, bw);
            _mask.rectTransform.offsetMax = new Vector2(-bw, -bw);
            _border.enabled = bw > 0f;
            _border.color = Snapshot.Hex(string.IsNullOrEmpty(Cfg.Color) ? "FFFFFF" : Cfg.Color);
            UpdateUv();
        }

        public override void Render(Snapshot s) { }

        public override void Tick(float dt)
        {
            bool want = Cfg.Visible && Overlay.Instance && Overlay.Instance.IsVisible;
            if (want)
            {
                _hiddenSince = -1f;
                if (_cam == null || _device != (Cfg.Url ?? "")) StartCamera();
            }
            else if (_cam != null)
            {
                // keep the camera warm through short hides (restart, menus); release it after a while
                if (_hiddenSince < 0f) _hiddenSince = Time.unscaledTime;
                else if (Time.unscaledTime - _hiddenSince > 5f) StopCamera();
            }
            if (_cam != null && _cam.didUpdateThisFrame) UpdateUv();
        }

        private void StartCamera()
        {
            StopCamera();
            _device = Cfg.Url ?? "";
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0) return;
            string name = devices[0].name;
            foreach (WebCamDevice d in devices)
            {
                if (d.name == _device) name = d.name;
            }
            _cam = new WebCamTexture(name, 1280, 720, 30);
            _cam.Play();
            _image.texture = _cam;
        }

        private void StopCamera()
        {
            if (_cam == null) return;
            _cam.Stop();
            Object.Destroy(_cam);
            _cam = null;
            _image.texture = null;
        }

        // Rotate the picture, crop it to fill the box (like "cover"), then apply zoom and mirror / flip.
        private void UpdateUv()
        {
            int rot = ((Cfg.Rotation % 360) + 360) % 360;
            bool side = rot == 90 || rot == 270;
            Rect box = _mask.rectTransform.rect;
            // a sideways picture is laid out in a box with width and height swapped, then turned into place
            RectTransform rt = _image.rectTransform;
            rt.sizeDelta = side ? new Vector2(box.height, box.width) : new Vector2(box.width, box.height);
            rt.localRotation = Quaternion.Euler(0f, 0f, -rot);
            if (_cam == null || _cam.width <= 16) return;
            bool vflip = _cam.videoVerticallyMirrored != Cfg.FlipV;
            Crop(rot, Cfg.Mirror, vflip, out float cl, out float cr, out float ct, out float cb);
            float rw = Mathf.Max(0.05f, 1f - cl - cr), rh = Mathf.Max(0.05f, 1f - ct - cb);
            float camAspect = _cam.width * rw / (_cam.height * rh);
            float bw = side ? box.height : box.width, bh = side ? box.width : box.height;
            float boxAspect = bh > 0f ? bw / bh : camAspect;
            float uw = 1f, uh = 1f;
            if (camAspect > boxAspect) uw = boxAspect / camAspect;
            else uh = camAspect / boxAspect;
            float z = Mathf.Max(1f, Cfg.Zoom);
            uw /= z;
            uh /= z;
            // centered inside the kept (uncropped) part of the camera picture
            float ux = cl + (1f - uw) / 2f * rw, uy = cb + (1f - uh) / 2f * rh;
            uw *= rw;
            uh *= rh;
            if (Cfg.Mirror)
            {
                ux += uw;
                uw = -uw;
            }
            if (vflip)
            {
                uy += uh;
                uh = -uh;
            }
            _image.uvRect = new Rect(ux, uy, uw, uh);
            _image.color = Color.white;
        }

        // The crop is set as seen on screen (left / right / top / bottom of the box); turn it into camera-texture
        // sides by undoing the clockwise rotation, then the mirror and the vertical flip (texture v=0 is the bottom).
        private void Crop(int rot, bool mirror, bool vflip, out float l, out float r, out float t, out float b)
        {
            float sl = Mathf.Clamp(Cfg.CropL, 0f, 0.45f), sr = Mathf.Clamp(Cfg.CropR, 0f, 0.45f);
            float st = Mathf.Clamp(Cfg.CropT, 0f, 0.45f), sb = Mathf.Clamp(Cfg.CropB, 0f, 0.45f);
            switch (rot)
            {
                case 90: l = st; t = sr; r = sb; b = sl; break;
                case 180: l = sr; r = sl; t = sb; b = st; break;
                case 270: l = sb; b = sr; r = st; t = sl; break;
                default: l = sl; r = sr; t = st; b = sb; break;
            }
            if (mirror)
            {
                float x = l;
                l = r;
                r = x;
            }
            if (vflip)
            {
                float x = t;
                t = b;
                b = x;
            }
        }

        private static string[] _names;
        private static float _namesAt = -100f;

        // WebCamTexture.devices asks Windows for every camera (slow); the editor draws several times a frame,
        // which cost ~10 FPS while the webcam item was selected. Cached, refreshed every 10 s or on demand.
        public static string[] DeviceNames(bool refresh = false)
        {
            if (_names != null && !refresh && Time.unscaledTime - _namesAt < 10f) return _names;
            WebCamDevice[] d = WebCamTexture.devices;
            var names = new string[d.Length];
            for (int i = 0; i < d.Length; i++) names[i] = d[i].name;
            _names = names;
            _namesAt = Time.unscaledTime;
            return names;
        }

        public override void Dispose() => StopCamera();
    }
}
