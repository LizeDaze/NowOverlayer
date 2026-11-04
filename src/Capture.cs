using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace NowOverlayer
{
    // Saves a screenshot a moment after the result card pops up (so the card is in the picture).
    public static class Capture
    {
        // ScreenCapture lives in a netstandard 2.1 module the net48 build can't reference directly.
        private static readonly MethodInfo CaptureMethod =
            Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")
                ?.GetMethod("CaptureScreenshot", new[] { typeof(string) });

        private static float _at = -1f;
        private static string _pending;
        private static string _toast;
        private static float _toastAt;

        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NowOverlayer");

        public static string Folder => string.IsNullOrEmpty(Main.Settings.CaptureFolder) ? DefaultFolder : Main.Settings.CaptureFolder;

        public static void Schedule(bool cleared, bool newRecord, string title, float extraDelay = 0f)
        {
            Settings s = Main.Settings;
            if (cleared ? !s.CaptureOnClear : !s.CaptureOnDeath) return;
            if (s.CaptureOnlyRecords && !newRecord) return;
            string name = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Safe(title) + (cleared ? "_CLEAR" : "_FAIL") + ".png";
            _pending = Path.Combine(Folder, name);
            _at = Time.unscaledTime + s.CaptureDelay + extraDelay;
        }

        public static void Tick()
        {
            // toast a little later so it isn't in the screenshot itself
            if (_toast != null && Time.unscaledTime >= _toastAt)
            {
                Overlay.Instance?.ShowToast(_toast);
                _toast = null;
            }
            if (_pending == null || Time.unscaledTime < _at) return;
            string path = _pending;
            _pending = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (CaptureMethod == null) throw new Exception("ScreenCapture 없음");
                CaptureMethod.Invoke(null, new object[] { path });
                _toast = "캡처 저장 · " + Path.GetFileName(path);
                _toastAt = Time.unscaledTime + 0.3f;
            }
            catch (Exception e)
            {
                Main.LogError("Capture failed", e);
                Overlay.Instance?.ShowToast("캡처 실패");
            }
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "level";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Trim();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }
    }
}
