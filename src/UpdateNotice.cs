using System;
using System.Diagnostics;
using UnityEngine;

namespace NowOverlayer
{
    // Big "update ready" card once Updater has downloaded a new version: the dll is only loaded at game start, so it
    // asks for a restart. Never over a level in progress or the editor; "나중에" hides it for this session.
    // Long sessions look for new releases again every 30 minutes.
    public class UpdateNotice : MonoBehaviour
    {
        private const float CheckEvery = 30f * 60f;

        private float _nextCheck = CheckEvery;
        private string _dismissed;
        private float _shownAt = -1f;
        private GUIStyle _chip, _title, _body, _go, _later;

        private bool Showing =>
            Updater.Ready != null && _dismissed != Updater.Ready && !OverlayEditor.Active &&
            Overlay.Instance && !Overlay.Instance.Playing;

        private void Update()
        {
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + CheckEvery;
                if (Updater.Ready == null) Updater.Check(false);
            }
            if (!Showing) _shownAt = -1f;
            else if (_shownAt < 0f) _shownAt = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (!Showing || _shownAt < 0f) return;
            Ui.Init();
            InitStyles();
            GUI.depth = -2000;

            // laid out at 1080p, scaled to the window
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2.5f);
            float w = Screen.width / s, h = Screen.height / s;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            float t = Mathf.Clamp01((Time.unscaledTime - _shownAt) / 0.45f);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            Color tint = GUI.color;

            GUI.color = new Color(1f, 1f, 1f, ease);
            if (Event.current.type == EventType.Repaint) Ui.DrawRect(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.62f));

            const float cw = 720f, ch = 360f;
            var card = new Rect((w - cw) / 2f, (h - ch) / 2f + (1f - ease) * 60f, cw, ch);
            GUI.Box(card, GUIContent.none, Ui.Card);
            if (Event.current.type == EventType.Repaint)
            {
                Ui.DrawRect(new Rect(card.x + 28f, card.y, cw - 56f, 4f), Ui.Accent); // red top line
            }

            float x = card.x + 48f, y = card.y + 48f, iw = cw - 96f;
            GUI.Label(new Rect(x, y, 140f, 32f), "업데이트", _chip);
            y += 50f;
            GUI.Label(new Rect(x, y, iw, 56f), "v" + Updater.Ready + " 업데이트가 준비됐어요", _title);
            y += 64f;
            GUI.Label(new Rect(x, y, iw, 60f), "게임을 다시 시작해야 새 버전이 적용돼요.\n내 배치 · 설정 · 기록은 그대로예요.", _body);

            float by = card.yMax - 48f - 64f;
            if (GUI.Button(new Rect(x, by, iw - 196f, 64f), "지금 재시작", _go)) Restart();
            if (GUI.Button(new Rect(card.xMax - 48f - 180f, by, 180f, 64f), "나중에", _later)) _dismissed = Updater.Ready;

            GUI.color = tint;
            GUI.matrix = old;
        }

        private void InitStyles()
        {
            if (_title != null) return;
            _chip = new GUIStyle(Ui.ValueChip) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            _title = new GUIStyle(Ui.Title) { fontSize = 40, wordWrap = true };
            _body = new GUIStyle(Ui.Body) { fontSize = 20, wordWrap = true };
            _body.normal.textColor = Ui.SubColor;
            _go = new GUIStyle(Ui.Primary) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _later = new GUIStyle(Ui.Ghost) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
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
