using System;
using System.Diagnostics;
using UnityEngine;

namespace NowOverlayer
{
    // Small update card at the right edge, sliding in like a Toss notification: "업데이트 발견" while the new version
    // downloads, then "준비 완료" with a restart button (the dll is only loaded at game start).
    // Releases are looked up every few minutes. During a level it shows for a few seconds and slides away, and comes
    // back when the level is over; never over the editor. × hides it until the next new version.
    public class UpdateNotice : MonoBehaviour
    {
        private const float CheckEvery = 5f * 60f;
        private const float InPlaySeconds = 5f;
        private const float W = 360f, H = 92f, Margin = 28f;

        private float _nextCheck = CheckEvery;
        private string _dismissed, _seen;
        private float _seenAt = -100f;
        private float _a;     // 0 hidden .. 1 in place
        private bool _target;
        private float _moveT = 1f;
        private float _from;
        private GUIStyle _title, _sub, _go, _close, _icon;

        private static string Tag => Updater.Ready ?? Updater.Found;

        private void Update()
        {
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + CheckEvery;
                Updater.Check(false);
            }

            string state = Tag == null ? null : (Updater.Ready != null ? "ready " : "found ") + Tag;
            if (state != _seen)
            {
                _seen = state;
                _seenAt = Time.unscaledTime;
            }
            bool playing = Overlay.Instance && Overlay.Instance.Playing;
            bool want = Tag != null && _dismissed != Tag && !OverlayEditor.Active &&
                        (!playing || Time.unscaledTime - _seenAt < InPlaySeconds);
            if (want != _target)
            {
                _target = want;
                _from = _a;
                _moveT = 0f;
            }
            if (_moveT < 1f)
            {
                _moveT = Mathf.Min(1f, _moveT + Time.unscaledDeltaTime / (_target ? 0.55f : 0.3f));
                _a = _target ? Mathf.LerpUnclamped(_from, 1f, BackOut(_moveT)) : Mathf.Lerp(_from, 0f, _moveT * _moveT);
            }
        }

        // ease-out with a small overshoot: the card slides past its spot and settles back
        private static float BackOut(float t)
        {
            const float c = 1.5f;
            t -= 1f;
            return 1f + (c + 1f) * t * t * t + c * t * t;
        }

        private void OnGUI()
        {
            if (_a <= 0.001f || Tag == null) return;
            Ui.Init();
            InitStyles();
            GUI.depth = -2000;

            // laid out at 1080p, scaled to the window
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2.5f);
            float sw = Screen.width / s, sh = Screen.height / s;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            Color tint = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_a * 1.6f));

            // bottom right, sliding in from past the right edge
            float x = sw - Margin - W + (1f - _a) * (W + Margin + 20f);
            var card = new Rect(x, sh * 0.72f - H / 2f, W, H);
            GUI.Box(card, GUIContent.none, Ui.Card);
            bool ready = Updater.Ready != null;
            if (Event.current.type == EventType.Repaint)
            {
                Ui.DrawRect(new Rect(card.x, card.y + 16f, 3f, H - 32f), Ui.Accent); // red side tick
            }

            // icon: a red circle with an arrow, pulsing while it downloads
            var icon = new Rect(card.x + 20f, card.y + (H - 40f) / 2f, 40f, 40f);
            float pulse = ready ? 1f : 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
            Color ic = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, ic.a * pulse);
            GUI.Label(icon, "↓", _icon);
            GUI.color = ic;

            float tx = icon.xMax + 14f, tw = card.xMax - tx - (ready ? 112f : 44f);
            GUI.Label(new Rect(tx, card.y + 20f, tw, 26f), ready ? "업데이트 준비 완료" : "업데이트 발견", _title);
            GUI.Label(new Rect(tx, card.y + 48f, tw, 22f), ready ? "v" + Tag + " · 재시작하면 적용돼요" : "v" + Tag + " 받는 중…", _sub);

            if (ready && GUI.Button(new Rect(card.xMax - 116f, card.y + (H - 38f) / 2f, 76f, 38f), "재시작", _go)) Restart();
            if (GUI.Button(new Rect(card.xMax - 36f, card.y + 8f, 28f, 28f), "×", _close)) _dismissed = Tag;

            GUI.color = tint;
            GUI.matrix = old;
        }

        private void InitStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(Ui.Body) { fontSize = 18, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            _title.normal.textColor = Ui.TextColor;
            _sub = new GUIStyle(Ui.Sub) { fontSize = 14, clipping = TextClipping.Clip };
            _go = new GUIStyle(Ui.Primary) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _close = new GUIStyle(Ui.Ghost) { fontSize = 16, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 2) };
            _icon = new GUIStyle(Ui.AccentBox) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _icon.normal.textColor = Color.white;
        }

        // Save, start a hidden helper that waits for this process to end and launches the game again, then quit.
        // A Steam install goes through Steam (steam://rungameid) so the game starts the way it normally does.
        private static void Restart()
        {
            try
            {
                Main.Settings.Save(Main.Mod);
                PlayCount.Save();
            }
            catch (Exception e)
            {
                Main.LogError("Save before restart failed", e);
            }
            try
            {
                Process me = Process.GetCurrentProcess();
                string exe = me.MainModule.FileName;
                string target = exe.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "steam://rungameid/977950"
                    : exe;
                string cmd = "Wait-Process -Id " + me.Id + " -ErrorAction SilentlyContinue; Start-Process '" + target.Replace("'", "''") + "'";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -WindowStyle Hidden -Command \"" + cmd + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception e)
            {
                Main.LogError("Restart helper failed", e);
                Overlay.Instance?.ShowToast("자동 재시작 실패 · 게임을 직접 다시 켜 주세요");
                return;
            }
            Application.Quit();
        }
    }
}
