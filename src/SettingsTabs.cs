using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NowOverlayer
{
    // UMM window tabs: effects, fonts, presets, OBS.
    public partial class Settings
    {
        private static readonly string[] MilestoneSwatches = { "FFD54A", "FFFFFF", "FF6D6D", "60FF4E", "4FC3FF", "D958FF" };
        private static bool _showObsPw;
        private static readonly string[] ObsRecordLabels = { "그대로", "녹화 시작", "녹화 중지" };


        private bool DrawEffects()
        {
            bool changed = false;

            Ui.Section("콤보 마일스톤 효과");
            GUILayout.Label("정해 둔 콤보에 닿으면 숫자가 크게 튀고, 금빛 링이 퍼지고, 제목이 \"100 COMBO!\"로 바뀌어요.", Ui.Sub);
            bool on = Ui.Switch("사용", MilestoneEnabled);
            changed |= on != MilestoneEnabled;
            MilestoneEnabled = on;

            GUILayout.Label("효과를 낼 콤보 (쉼표로 구분)", Ui.Body);
            string ms = GUILayout.TextField(Milestones ?? "", Ui.Field);
            if (ms != Milestones)
            {
                Milestones = ms;
                changed = true;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("그 뒤로는 매", Ui.Body, GUILayout.Width(90));
            string every = GUILayout.TextField(MilestoneEvery.ToString(), Ui.Field, GUILayout.Width(90));
            GUILayout.Label("콤보마다 (0 = 안 함)", Ui.Body);
            GUILayout.EndHorizontal();
            if (int.TryParse(every, out int ev) && ev != MilestoneEvery && ev >= 0)
            {
                MilestoneEvery = ev;
                changed = true;
            }
            GUILayout.Label("효과 색", Ui.Body);
            GUILayout.BeginHorizontal();
            foreach (string hex in MilestoneSwatches)
            {
                if (Ui.ColorSwatch(Ui.Hex(hex), hex == MilestoneColor))
                {
                    MilestoneColor = hex;
                    changed = true;
                }
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("▶  미리보기", Ui.Secondary)) Overlay.Instance?.PreviewMilestone();
            GUILayout.EndVertical();

            Ui.Section("결과 카드");
            GUILayout.Label("클리어하거나 죽으면 화면 가운데에 진행도, 정확도, 최대 콤보, 플레이 시간, 판정 수를 보여 줘요. 처음부터 깬 판이면 맵 최고 기록과 첫 클리어도 표시해요.", Ui.Sub);
            bool c1 = Ui.Switch("클리어했을 때", ResultOnClear);
            bool c2 = Ui.Switch("죽었을 때", ResultOnDeath);
            changed |= c1 != ResultOnClear || c2 != ResultOnDeath;
            ResultOnClear = c1;
            ResultOnDeath = c2;
            float sec = Ui.Slider(ResultSeconds <= 0.05f ? "보여 주는 시간 · 다시 시작할 때까지" : "보여 주는 시간 (초)", ResultSeconds, 0f, 15f, "0");
            float sc = Ui.Slider("크기", ResultScale, 0.5f, 1.5f);
            if (Mathf.Abs(sec - ResultSeconds) > 0.4f || Mathf.Abs(sc - ResultScale) > 0.004f)
            {
                ResultSeconds = Mathf.Round(sec);
                ResultScale = Mathf.Round(sc * 100f) / 100f;
                changed = true;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("▶  클리어 미리보기", Ui.Secondary)) Overlay.Instance?.PreviewResult(true);
            if (GUILayout.Button("▶  실패 미리보기", Ui.Ghost)) Overlay.Instance?.PreviewResult(false);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            changed |= DrawCapture();
            return changed;
        }

        private bool DrawCapture()
        {
            bool changed = false;
            Ui.Section("자동 캡처");
            GUILayout.Label("결과 카드가 뜬 뒤 화면을 PNG로 저장해요. 파일 이름에 날짜·곡 제목·CLEAR/FAIL이 붙어요.", Ui.Sub);
            bool a = Ui.Switch("클리어했을 때", CaptureOnClear);
            bool b = Ui.Switch("죽었을 때", CaptureOnDeath);
            bool r = Ui.Switch("기록을 깼을 때만", CaptureOnlyRecords, "첫 클리어 · 최고 정확도 · 최고 진행도");
            changed |= a != CaptureOnClear || b != CaptureOnDeath || r != CaptureOnlyRecords;
            CaptureOnClear = a;
            CaptureOnDeath = b;
            CaptureOnlyRecords = r;
            float delay = Ui.Slider("결과 카드가 뜨고 나서 (초)", CaptureDelay, 0.2f, 3f, "0.0");
            if (Mathf.Abs(delay - CaptureDelay) > 0.04f)
            {
                CaptureDelay = Mathf.Round(delay * 10f) / 10f;
                changed = true;
            }
            Ui.Divider();
            GUILayout.Label("저장 폴더 (비워 두면 기본 폴더)", Ui.Body);
            string folder = GUILayout.TextField(CaptureFolder ?? "", Ui.Field);
            if (folder != CaptureFolder)
            {
                CaptureFolder = folder.Trim();
                changed = true;
            }
            GUILayout.Label(Capture.Folder, Ui.Sub);
            if (GUILayout.Button("캡처 폴더 열기", Ui.Ghost))
            {
                Directory.CreateDirectory(Capture.Folder);
                Application.OpenURL("file:///" + Capture.Folder.Replace('\\', '/'));
            }
            GUILayout.EndVertical();
            return changed;
        }

        private bool DrawDiscord()
        {
            bool changed = false;
            Ui.Section("디스코드 상태 표시");
            GUILayout.Label("디스코드 프로필에 \"플레이 중\"으로 곡 이름과 진행도를 보여 줘요.", Ui.Sub);
            GUILayout.Label("1. discord.com/developers/applications 에서 [New Application] (이름이 \"플레이 중\" 옆에 보여요)\n2. 만든 앱의 Application ID를 아래에 붙여 넣기", Ui.Sub);
            bool on = Ui.Switch("사용", DiscordEnabled);
            if (on != DiscordEnabled)
            {
                DiscordEnabled = on;
                changed = true;
                if (!on) Discord.Close();
                Patches.RefreshGamePresence();
            }
            bool replace = Ui.Switch("게임 기본 디스코드 상태 끄기", DiscordReplaceGame, "얼불춤이 직접 띄우는 \"커스텀 레벨 플레이 중\"이 내 상태를 가리지 않게 해요");
            if (replace != DiscordReplaceGame)
            {
                DiscordReplaceGame = replace;
                changed = true;
                Patches.RefreshGamePresence();
            }
            GUILayout.Label("Application ID", Ui.Body);
            string id = GUILayout.TextField(DiscordAppId ?? "", Ui.Field);
            if (id != DiscordAppId)
            {
                DiscordAppId = id.Trim();
                changed = true;
            }
            GUILayout.Label("첫 줄 (플레이 중)", Ui.Body);
            string det = GUILayout.TextField(DiscordDetails ?? "", Ui.Field);
            GUILayout.Label("둘째 줄 (플레이 중)", Ui.Body);
            string st = GUILayout.TextField(DiscordState ?? "", Ui.Field);
            GUILayout.Label("메뉴에 있을 때", Ui.Body);
            string idle = GUILayout.TextField(DiscordIdle ?? "", Ui.Field);
            GUILayout.Label("큰 이미지 키 (앱의 Rich Presence → Art Assets에 올린 이름, 비워도 돼요)", Ui.Body);
            string img = GUILayout.TextField(DiscordImage ?? "", Ui.Field);
            if (det != DiscordDetails || st != DiscordState || idle != DiscordIdle || img != DiscordImage)
            {
                DiscordDetails = det;
                DiscordState = st;
                DiscordIdle = idle;
                DiscordImage = img.Trim();
                changed = true;
            }
            GUILayout.Label("{Title} {Artist} {Progress} {XAcc} {Combo} {Attempt} 같은 토큰을 쓸 수 있어요. 5초마다 갱신돼요.", Ui.Sub);
            GUILayout.EndVertical();

            Ui.Section("디스코드 연결 상태");
            bool ok = Discord.Status.StartsWith("연결됨");
            bool bad = Discord.Status.Contains("오류") || Discord.Status.Contains("실패") || Discord.Status.Contains("끊");
            string color = ok ? "3DDC84" : bad ? "FF6B6B" : "8A8A99";
            GUILayout.Label("<color=#" + color + ">●</color>  " + Discord.Status, Ui.H2);
            string ping = Discord.LastCheckAt == default(DateTime) ? "아직 확인 안 함"
                : Discord.PingMs >= 0 ? "응답 " + Discord.PingMs + "ms · " + Ago(Discord.LastCheckAt)
                : "응답 기다리는 중… · " + Ago(Discord.LastCheckAt);
            GUILayout.Label("확인: " + ping, Ui.Sub);
            GUILayout.Label("마지막으로 보낸 상태: " + (Discord.LastSentAt == default(DateTime) ? "없음"
                : Ago(Discord.LastSentAt) + " · " + Discord.LastActivity), Ui.Sub);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↻  새로고침", Ui.Primary, GUILayout.Height(38))) Discord.Refresh();
            if (GUILayout.Button("상태 확인", Ui.Ghost, GUILayout.Height(38))) Discord.Check();
            GUILayout.EndHorizontal();
            GUILayout.Label("새로고침은 연결을 새로 맺고 상태를 바로 다시 보내요. 상태 확인은 디스코드에 신호를 보내 살아 있는지 봐요.", Ui.Sub);
            GUILayout.EndVertical();
            return changed;
        }

        private static string Ago(DateTime t)
        {
            double s = (DateTime.Now - t).TotalSeconds;
            return s < 5 ? "방금" : s < 60 ? (int)s + "초 전" : (int)(s / 60) + "분 전";
        }

        // ---- color theme · combo layout ----

        private static readonly string[] ThemeSwatches =
            { "FF4757", "FF6D6D", "FFDA45", "FF9F43", "60FF4E", "4FC3FF", "3182F6", "D958FF", "FF7EB6", "FFFFFF" };

        private static readonly string[] ComboLayoutNames = { "제목 위 · 숫자 아래", "숫자 위 · 제목 아래", "한 줄 (Combo | 257)" };

        private static GUIStyle _comboPreview;

        private bool DrawTheme()
        {
            bool changed = false;
            Ui.Section("색 테마");
            GUILayout.Label("고른 색을 오버레이에 한 번에 입혀요. 어디에 입힐지 먼저 골라 주세요.", Ui.Sub);
            int mode = ThemeMode;
            if (Ui.Segmented(ref mode, Theme.ModeNames))
            {
                ThemeMode = mode;
                changed = true;
            }
            GUILayout.Label(Theme.ModeSubs[ThemeMode], Ui.Sub);
            if (ThemeMode != Theme.Off)
            {
                Ui.Divider();
                GUILayout.Label("테마 색", Ui.Body);
                Ui.FlowBegin();
                foreach (string hex in ThemeSwatches)
                {
                    Ui.FlowItem(32f);
                    if (Ui.ColorSwatch(Ui.Hex(hex), string.Equals(hex, ThemeColor, StringComparison.OrdinalIgnoreCase)))
                    {
                        ThemeColor = hex;
                        changed = true;
                    }
                }
                Ui.FlowEnd();
                GUILayout.BeginHorizontal();
                GUILayout.Label("직접 입력  #", Ui.Sub, GUILayout.Width(80));
                string hexIn = GUILayout.TextField(ThemeColor ?? "", 6, Ui.Field, GUILayout.Width(110));
                GUILayout.EndHorizontal();
                if (hexIn != ThemeColor && hexIn.Length == 6 && ColorUtility.TryParseHtmlString("#" + hexIn, out _))
                {
                    ThemeColor = hexIn.ToUpperInvariant();
                    changed = true;
                }
            }
            Ui.Divider();
            GUILayout.Label("미리보기", Ui.Sub);
            Snapshot sample = Snapshot.Sample();
            foreach (ElementType t in new[] { ElementType.Progress, ElementType.XAccuracy, ElementType.TileInfo })
            {
                var cfg = new ElementConfig { Type = t };
                GUILayout.Label(Theme.Apply(sample.Text(cfg), cfg.Color), Ui.H2);
            }
            GUILayout.Label("판정 개수는 판정마다 정해진 색 그대로 보여요.", Ui.Sub);
            GUILayout.EndVertical();

            Ui.Section("콤보 모양");
            GUILayout.Label("콤보 글자와 숫자를 어떻게 놓을지 골라요. 화면에 있는 모든 콤보에 적용돼요. 하나만 바꾸려면 화면 편집에서 콤보를 눌러요.", Ui.Sub);
            ElementConfig combo = Elements.Find(c => c.Type == ElementType.Combo);
            if (combo == null)
            {
                GUILayout.Label("콤보 항목이 없어요. [화면 편집] → + 에서 추가할 수 있어요.", Ui.Sub);
            }
            else
            {
                int layout = combo.ComboLayout;
                if (Ui.Segmented(ref layout, ComboLayoutNames))
                {
                    foreach (ElementConfig c in Elements)
                    {
                        if (c.Type == ElementType.Combo) c.ComboLayout = layout;
                    }
                    changed = true;
                }
                GUILayout.Label(ComboLook(combo, layout), _comboPreview ?? (_comboPreview = new GUIStyle(Ui.H2) { alignment = TextAnchor.MiddleCenter, richText = true }));
                if (layout != 2)
                {
                    float gap = Ui.Slider("제목과 숫자 간격", combo.ComboGap, -40f, 40f, "0");
                    if (Mathf.Abs(gap - combo.ComboGap) > 0.5f)
                    {
                        foreach (ElementConfig c in Elements)
                        {
                            if (c.Type == ElementType.Combo) c.ComboGap = Mathf.Round(gap);
                        }
                        changed = true;
                    }
                    GUILayout.Label("왼쪽으로 끌면 Combo 글자와 숫자가 더 붙어요 · 0이 기본", Ui.Sub);
                }
                Ui.Divider();
                GUILayout.Label("콤보 숫자 색", Ui.Body);
                string nc = NumberColorPicker(combo.NumberColor);
                if (nc != combo.NumberColor)
                {
                    foreach (ElementConfig c in Elements)
                    {
                        if (c.Type == ElementType.Combo) c.NumberColor = nc;
                    }
                    changed = true;
                }
                GUILayout.Label("Combo 글자 색은 [화면 편집]에서 콤보를 누르면 바꿀 수 있어요.", Ui.Sub);
            }
            GUILayout.EndVertical();

            if (changed)
            {
                Save(Main.Mod);
                Overlay.Instance?.ShowPreview(4f);
            }
            return changed;
        }

        private static readonly string[] NumberSwatches =
            { "FF6D6D", "E44343", "FFFFFF", "FFDA45", "FF9F43", "60FF4E", "4FC3FF", "3182F6", "D958FF", "FF7EB6" };

        // "" = 기본 (red gradient / theme), else RRGGBB. Shared by the 색 테마 tab and the editor panel.
        internal static string NumberColorPicker(string current)
        {
            string result = current ?? "";
            bool auto = result.Length == 0;
            if (GUILayout.Button(auto ? "● 기본 · 콤보가 쌓일수록 진해지는 빨강" : "기본 색으로 되돌리기", auto ? Ui.RowOn : Ui.Row))
                result = "";
            Ui.FlowBegin();
            foreach (string hex in NumberSwatches)
            {
                Ui.FlowItem(32f);
                if (Ui.ColorSwatch(Ui.Hex(hex), string.Equals(hex, result, StringComparison.OrdinalIgnoreCase))) result = hex;
            }
            Ui.FlowEnd();
            GUILayout.BeginHorizontal();
            GUILayout.Label("직접 입력  #", Ui.Sub, GUILayout.Width(80));
            string hexIn = GUILayout.TextField(result, 6, Ui.Field, GUILayout.Width(110));
            GUILayout.EndHorizontal();
            if (hexIn != result && hexIn.Length == 6 && ColorUtility.TryParseHtmlString("#" + hexIn, out _)) result = hexIn.ToUpperInvariant();
            return result;
        }

        // small rich-text picture of a combo layout for the pickers
        internal static string ComboLook(ElementConfig c, int layout)
        {
            string title = "<color=#" + (Theme.ColorsWords ? Main.Settings.ThemeColor : c.Color) + ">Combo</color>";
            string numHex = !string.IsNullOrEmpty(c.NumberColor) ? c.NumberColor : Theme.ColorsNumbers ? Main.Settings.ThemeColor : "FF6D6D";
            string num = "<color=#" + numHex + "><size=30>257</size></color>";
            return layout == 0 ? title + "\n" + num : layout == 1 ? num + "\n" + title : title + "  |  <color=#" + numHex + ">257</color>";
        }

        // ---- fonts ----

        private static string _fontSearch = "";
        private static List<string> _osFonts;
        private static string _fontError;

        internal static string FontFolder => Path.Combine(Main.Mod.Path, "Font");

        // Mod Font folder first, then the OS fonts (listed once).
        private static List<string> _allFonts;
        private static float _allFontsAt = -100f;

        internal static List<string> AllFonts()
        {
            // OnGUI runs several times a frame; reading the folder each time cost frames
            if (_allFonts != null && Time.unscaledTime - _allFontsAt < 3f) return _allFonts;
            _allFontsAt = Time.unscaledTime;
            var list = _allFonts = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(FontFolder))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".otf") list.Add(f);
                }
            }
            catch (Exception)
            {
                // no folder
            }
            if (_osFonts == null)
            {
                _osFonts = new List<string>();
                try
                {
                    foreach (string f in Font.GetPathsToOSFonts())
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".ttf" || ext == ".otf") _osFonts.Add(f);
                    }
                    _osFonts.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception e)
                {
                    Main.LogError("OS font list failed", e);
                }
            }
            list.AddRange(_osFonts);
            return list;
        }

        // Typed / pasted path: strip quotes, then null when it points at a real ttf / otf, else why not.
        internal static string CheckFontPath(string raw, out string path)
        {
            path = (raw ?? "").Trim().Trim('"').Trim();
            if (path.Length == 0) return "글꼴 파일 경로를 넣어 주세요.";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".ttf" && ext != ".otf") return "ttf 나 otf 파일만 쓸 수 있어요.";
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return "경로 모양이 올바르지 않아요.";
            }
            return File.Exists(path) ? null : "파일이 없어요: " + path;
        }

        private static string _fontPathInput = "";

        private bool DrawFonts()
        {
            bool changed = false;
            Ui.Section("전체 글꼴");
            string current = string.IsNullOrEmpty(FontPath) ? "기본 · Maplestory Bold" : Path.GetFileNameWithoutExtension(FontPath);
            GUILayout.Label("지금: " + current, Ui.H2);
            GUILayout.Label("아래 목록에서 고르면 게임 화면에 바로 적용돼서 미리 볼 수 있어요. 글꼴에 없는 글자는 게임 기본 글꼴로 나와요.", Ui.Sub);
            bool all = Ui.Switch("고를 때 모든 항목에 적용", FontAll, "화면 편집에서 글꼴을 따로 정한 항목까지 이 글꼴로 바꿔요");
            if (all != FontAll)
            {
                FontAll = all;
                Save(Main.Mod);
            }
            var own = new List<string>();
            foreach (ElementConfig c in Elements)
            {
                if (!string.IsNullOrEmpty(c.Font)) own.Add(ElementConfig.DisplayName(c.Type) + " (" + Path.GetFileNameWithoutExtension(c.Font) + ")");
            }
            if (own.Count > 0)
            {
                GUILayout.Label("따로 정한 글꼴을 쓰는 항목: " + string.Join(", ", own.ToArray()), Ui.Sub);
                if (GUILayout.Button("이 항목들도 전체 글꼴로 맞추기", Ui.Secondary))
                {
                    foreach (ElementConfig c in Elements) c.Font = "";
                    changed = true;
                    Save(Main.Mod);
                    Overlay.Instance?.ShowPreview(4f);
                    Overlay.Instance?.ShowToast("모든 항목 · " + current);
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("기본으로", Ui.Ghost)) PickFont("");
            if (GUILayout.Button("Font 폴더 열기", Ui.Ghost))
            {
                Directory.CreateDirectory(FontFolder);
                Application.OpenURL("file:///" + FontFolder.Replace('\\', '/'));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("ttf / otf 파일을 Font 폴더에 넣으면 목록 맨 위에 나와요.", Ui.Sub);
            if (!string.IsNullOrEmpty(_fontError)) GUILayout.Label("<color=#FF6B6B>" + _fontError + "</color>", Ui.Sub);
            GUILayout.EndVertical();

            Ui.Section("파일 경로로 불러오기");
            GUILayout.Label("설치하지 않은 글꼴도 파일 경로만 있으면 돼요. 예: D:\\Fonts\\MyFont.ttf  (탐색기에서 Shift+우클릭 → 경로로 복사)", Ui.Sub);
            GUILayout.BeginHorizontal();
            _fontPathInput = GUILayout.TextField(_fontPathInput, Ui.Field, GUILayout.MinWidth(0f));
            if (GUILayout.Button("붙여넣기", Ui.Ghost, GUILayout.Width(80))) _fontPathInput = (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (GUILayout.Button("불러오기", Ui.Primary, GUILayout.Width(90)))
            {
                string p;
                string err = CheckFontPath(_fontPathInput, out p);
                if (err == null) PickFont(p);
                else _fontError = err;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Ui.Card);
            GUILayout.Label("검색", Ui.Body);
            _fontSearch = GUILayout.TextField(_fontSearch, Ui.Field, GUILayout.MinWidth(0f));
            int shown = 0;
            foreach (string f in AllFonts())
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (_fontSearch.Length > 0 && name.IndexOf(_fontSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 40) break;
                bool on = string.Equals(f, FontPath, StringComparison.OrdinalIgnoreCase);
                string tag = f.StartsWith(FontFolder, StringComparison.OrdinalIgnoreCase) ? "  <color=#8E8E9A>모드 폴더</color>" : "";
                if (GUILayout.Button(name + tag, on ? Ui.RowOn : Ui.Row, GUILayout.MinWidth(0f))) PickFont(f);
            }
            if (shown > 40) GUILayout.Label("40개까지만 보여요. 검색어로 좁혀 주세요.", Ui.Sub);
            if (shown == 0) GUILayout.Label("찾는 글꼴이 없어요.", Ui.Sub);
            GUILayout.EndVertical();
            return changed;
        }

        private void PickFont(string path)
        {
            Overlay o = Overlay.Instance;
            if (!o) return;
            if (FontAll)
            {
                foreach (ElementConfig c in Elements) c.Font = "";
            }
            if (o.SetFont(path))
            {
                _fontError = null;
                Save(Main.Mod);
                o.ShowPreview(4f);
                o.ShowToast("글꼴 · " + (string.IsNullOrEmpty(path) ? "Maplestory Bold" : Path.GetFileNameWithoutExtension(path)));
            }
            else
            {
                _fontError = "이 글꼴은 불러올 수 없어요: " + Path.GetFileName(path);
            }
        }

        // ---- presets ----

        private static string _presetName = "";
        private static int _confirmDelete = -1;

        private void DrawPresets()
        {
            Ui.Section("지금 배치를 프리셋으로 저장");
            GUILayout.Label("항목 배치, 크기, 등장 애니메이션, 글꼴을 함께 저장해요. 방송용 / 연습용처럼 나눠 두고 단축키로 바꿀 수 있어요.", Ui.Sub);
            GUILayout.BeginHorizontal();
            _presetName = GUILayout.TextField(_presetName, Ui.Field);
            if (GUILayout.Button("저장", Ui.Primary, GUILayout.Width(90)))
            {
                string n = _presetName.Trim();
                if (n.Length == 0) n = "프리셋 " + (Presets.Count + 1);
                Presets.Add(NowOverlayer.Presets.Capture(n));
                ActivePreset = n;
                _presetName = "";
                Save(Main.Mod);
                Overlay.Instance?.ShowToast("프리셋 저장 · " + n);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            Ui.Section("프리셋");
            if (Presets.Count == 0) GUILayout.Label("아직 저장한 프리셋이 없어요.", Ui.Sub);
            for (int i = 0; i < Presets.Count; i++)
            {
                Preset p = Presets[i];
                GUILayout.BeginHorizontal(Ui.Row);
                GUILayout.BeginVertical();
                GUILayout.Label(p.Name == ActivePreset ? "<color=#FF8A93>● </color>" + p.Name : p.Name, Ui.Body);
                GUILayout.Label(Hotkeys.PresetLabel(i) + " · 항목 " + p.Elements.Count + "개", Ui.Sub);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("적용", Ui.Primary, GUILayout.Width(70))) NowOverlayer.Presets.Apply(p);
                if (GUILayout.Button("덮어쓰기", Ui.Ghost, GUILayout.Width(90)))
                {
                    NowOverlayer.Presets.Overwrite(p);
                    Overlay.Instance?.ShowToast("프리셋 덮어씀 · " + p.Name);
                }
                if (GUILayout.Button(_confirmDelete == i ? "정말?" : "삭제", Ui.Danger, GUILayout.Width(70)))
                {
                    if (_confirmDelete == i)
                    {
                        Presets.RemoveAt(i);
                        _confirmDelete = -1;
                        Save(Main.Mod);
                        GUILayout.EndHorizontal();
                        break;
                    }
                    _confirmDelete = i;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        // ---- hotkeys ----

        private static int _capture = -1;

        private bool DrawHotkeys()
        {
            bool changed = false;
            Event e = Event.current;

            // key capture: the next key press (with modifiers) becomes the binding; Esc cancels
            Hotkeys.Capturing = _capture >= 0;
            if (_capture >= 0 && e.type == EventType.KeyDown && e.keyCode != KeyCode.None && !IsModifier(e.keyCode))
            {
                if (e.keyCode != KeyCode.Escape)
                {
                    Hotkeys.All[_capture].Set(this, new Hotkey(e.keyCode, e.control, e.shift, e.alt));
                    changed = true;
                }
                _capture = -1;
                Hotkeys.Capturing = false;
                e.Use();
            }

            Ui.Section("단축키");
            GUILayout.Label("[변경]을 누른 뒤 원하는 키를 Ctrl·Shift·Alt와 함께 눌러요. Esc는 취소예요.", Ui.Sub);
            GUILayout.Space(4);
            for (int i = 0; i < Hotkeys.All.Length; i++)
            {
                Hotkeys.Entry h = Hotkeys.All[i];
                if (i > 0) Ui.Divider();
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(h.Name, Ui.Body);
                if (h.Description.Length > 0) GUILayout.Label(h.Description, Ui.Sub);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                string label = _capture == i ? "키를 누르세요…" : h.Get(this).Label();
                GUILayout.Label(label, Ui.KeyChip, GUILayout.MinWidth(120));
                if (GUILayout.Button(_capture == i ? "취소" : "변경", Ui.Ghost, GUILayout.Width(64)))
                {
                    _capture = _capture == i ? -1 : i;
                }
                if (GUILayout.Button("지우기", Ui.Ghost, GUILayout.Width(70)))
                {
                    h.Set(this, new Hotkey());
                    _capture = -1;
                    changed = true;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            Ui.Section("프리셋 바로 바꾸기");
            GUILayout.Label("아래 키와 숫자 1~9를 같이 누르면 프리셋 목록 순서대로 바뀌어요.", Ui.Sub);
            bool c = Ui.Switch("Ctrl", PresetCtrl);
            bool s = Ui.Switch("Shift", PresetShift);
            bool a = Ui.Switch("Alt", PresetAlt);
            if (c != PresetCtrl || s != PresetShift || a != PresetAlt)
            {
                PresetCtrl = c;
                PresetShift = s;
                PresetAlt = a;
                changed = true;
            }
            GUILayout.Label("예: " + Hotkeys.PresetLabel(0) + (PresetCtrl || PresetShift || PresetAlt ? "" : "  (조합키 없이 숫자만 누르면 게임 입력과 겹칠 수 있어요)"), Ui.Sub);
            GUILayout.EndVertical();

            if (changed) Save(Main.Mod);
            return changed;
        }

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;

        // ---- OBS ----

        private bool DrawObs()
        {
            bool changed = DrawDiscord();
            Ui.Section("OBS 연동");
            GUILayout.Label("OBS → 도구 → WebSocket 서버 설정에서 \"WebSocket 서버 사용\"을 켜 주세요. 서버 비밀번호를 켰다면 아래에 같은 비밀번호를 넣어요.", Ui.Sub);
            bool on = Ui.Switch("사용", ObsEnabled);
            if (on != ObsEnabled)
            {
                ObsEnabled = on;
                changed = true;
                if (on) Obs.Connect();
                else Obs.Disconnect();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("주소", Ui.Sub, GUILayout.Width(36));
            string host = GUILayout.TextField(ObsHost, Ui.Field, GUILayout.Width(160));
            GUILayout.Label("포트", Ui.Sub, GUILayout.Width(36));
            string port = GUILayout.TextField(ObsPort.ToString(), Ui.Field, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("비밀번호", Ui.Sub, GUILayout.Width(60));
            string pw = _showObsPw
                ? GUILayout.TextField(ObsPassword ?? "", Ui.Field, GUILayout.Width(220))
                : GUILayout.PasswordField(ObsPassword ?? "", '•', Ui.Field, GUILayout.Width(220));
            if (GUILayout.Button(_showObsPw ? "숨기기" : "보기", Ui.Ghost, GUILayout.Width(64))) _showObsPw = !_showObsPw;
            if (GUILayout.Button("붙여넣기", Ui.Ghost, GUILayout.Width(80))) pw = (GUIUtility.systemCopyBuffer ?? "").Trim();
            GUILayout.EndHorizontal();
            if (host != ObsHost || pw != ObsPassword || (int.TryParse(port, out int pt) && pt != ObsPort))
            {
                ObsHost = host.Trim();
                ObsPassword = pw;
                if (int.TryParse(port, out pt)) ObsPort = pt;
                changed = true;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Obs.Connected ? "다시 연결" : "연결", Ui.Primary, GUILayout.Width(120))) Obs.Connect();
            if (GUILayout.Button("장면 목록 새로고침", Ui.Ghost)) Obs.RefreshScenes();
            GUILayout.EndHorizontal();
            string color = Obs.Connected ? "60FF4E" : Obs.Status.StartsWith("연결 실패") || Obs.Status.StartsWith("OBS 요청") ? "FF6B6B" : "8E8E9A";
            GUILayout.Label("상태: <color=#" + color + ">" + Obs.Status + "</color>", Ui.Body);
            GUILayout.Label("비밀번호는 모드 폴더의 Settings.xml에 그대로 저장돼요.", Ui.Sub);
            GUILayout.EndVertical();

            changed |= DrawObsAction("플레이를 시작할 때", ObsOnStart);
            changed |= DrawObsAction("클리어했을 때", ObsOnClear);
            changed |= DrawObsAction("죽었을 때", ObsOnDeath);
            return changed;
        }

        private static bool DrawObsAction(string title, ObsAction a)
        {
            bool changed = false;
            GUILayout.BeginVertical(Ui.Card);
            GUILayout.Label(title, Ui.H2);
            GUILayout.Label("장면 바꾸기", Ui.Body);
            List<string> scenes = Obs.Scenes;
            GUILayout.BeginHorizontal();
            int col = 0;
            if (GUILayout.Button("바꾸지 않음", string.IsNullOrEmpty(a.Scene) ? Ui.TabOn : Ui.Chip))
            {
                a.Scene = "";
                changed = true;
            }
            foreach (string sc in scenes)
            {
                if (++col % 4 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                if (GUILayout.Button(sc, sc == a.Scene ? Ui.TabOn : Ui.Chip))
                {
                    a.Scene = sc;
                    changed = true;
                }
            }
            GUILayout.EndHorizontal();
            if (scenes.Count == 0)
            {
                GUILayout.Label("OBS에 연결하면 장면 목록이 나와요. 직접 적어도 돼요.", Ui.Sub);
                string s = GUILayout.TextField(a.Scene ?? "", Ui.Field);
                if (s != a.Scene)
                {
                    a.Scene = s;
                    changed = true;
                }
            }
            GUILayout.Label("녹화", Ui.Body);
            changed |= Ui.Segmented(ref a.Record, ObsRecordLabels);
            if (a.Record == 0) GUILayout.Label("모드가 시작한 녹화는 Esc로 레벨에서 나가거나 재시작할 때 멈춰요.", Ui.Sub);
            bool rep = Ui.Switch("리플레이 버퍼 저장", a.SaveReplay, "연결할 때 리플레이 버퍼를 자동으로 켜요");
            float delay = Ui.Slider("기다렸다가 실행 (초)", a.Delay, 0f, 5f, "0.0");
            if (rep != a.SaveReplay || Mathf.Abs(delay - a.Delay) > 0.04f)
            {
                a.SaveReplay = rep;
                a.Delay = Mathf.Round(delay * 10f) / 10f;
                changed = true;
            }
            GUILayout.EndVertical();
            return changed;
        }
    }
}
