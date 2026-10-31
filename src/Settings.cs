using System.Collections.Generic;
using UnityEngine;
using UnityModManagerNet;

namespace NowOverlayer
{
    public partial class Settings : UnityModManager.ModSettings
    {
        // Left empty here: XmlSerializer appends to an existing list, so defaults are filled in after Load.
        public List<ElementConfig> Elements = new List<ElementConfig>();
        public float Size = 0.8f;
        public AnimType Anim = AnimType.DropDown;
        public float AnimDuration = 1.13f;
        public float AnimStagger = 0f;

        public bool Magnet = true;
        public bool MagnetScreen = true;
        public bool MagnetElements = true;
        public bool MagnetSpacing = true;
        public float MagnetRange = 8f;   // screen px
        public float MagnetMargin = 16f; // reference px from the screen edge

        public bool MilestoneEnabled;
        public string Milestones = "100,500,1000";
        public int MilestoneEvery = 1000;
        public string MilestoneColor = "FFD54A";

        public bool ResultOnClear = true;
        public bool ResultOnDeath = true;
        public float ResultSeconds = 6f; // 0 = until the next attempt
        public float ResultScale = 1f;

        public string FontPath = "";

        public List<Preset> Presets = new List<Preset>();
        public string ActivePreset = "";

        public bool CaptureOnClear = true;
        public bool CaptureOnDeath;
        public bool CaptureOnlyRecords;
        public float CaptureDelay = 0.8f;
        public string CaptureFolder = "";

        public bool DiscordEnabled;
        public string DiscordAppId = "";
        public string DiscordDetails = "{Artist} - {Title}";
        public string DiscordState = "{Progress} · XAcc {XAcc}";
        public string DiscordIdle = "메뉴에서 쉬는 중";
        public string DiscordImage = "";

        public bool ObsEnabled;
        public string ObsHost = "127.0.0.1";
        public int ObsPort = 4455;
        public string ObsPassword = "";
        public ObsAction ObsOnStart = new ObsAction { Record = 1 };
        public ObsAction ObsOnClear = new ObsAction(); // keeps recording; leaving the level (Esc) stops it
        public ObsAction ObsOnDeath = new ObsAction { Record = 2, Delay = 1f };

        public Hotkey HkToggle = new Hotkey(KeyCode.O, true, true);
        public Hotkey HkEditor = new Hotkey(KeyCode.N, true, true);
        public Hotkey HkNextPreset = new Hotkey(KeyCode.RightArrow, true, true);
        public Hotkey HkPrevPreset = new Hotkey(KeyCode.LeftArrow, true, true);
        public Hotkey HkCloseResult = new Hotkey();
        public Hotkey HkReplayIntro = new Hotkey();
        public bool PresetCtrl = true, PresetShift = true, PresetAlt;
        public bool OverlayHidden;
        public bool AutoUpdate = true;        // fetch new releases from GitHub at startup (applied on the next start)
        public bool HideGameTitle = true;    // the game's own big level caption in custom levels
        public bool DiscordReplaceGame = true; // stop the game's own Discord presence while ours is on

        public int LayoutVersion;
        private const int CurrentLayout = 4;

        private static int _tab;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public static Settings Load(UnityModManager.ModEntry mod)
        {
            var s = Load<Settings>(mod);
            if (s.Elements == null || s.Elements.Count == 0) s.Elements = ElementConfig.Defaults();
            if (s.LayoutVersion < 2) s.TightenStatusLines();
            if (s.LayoutVersion < 3) s.RecordByDefault();
            if (s.LayoutVersion < 4 && s.ObsOnClear.Record == 2) s.ObsOnClear.Record = 0; // 1.9.5: keep the clear on tape until Esc
            s.LayoutVersion = CurrentLayout;
            return s;
        }

        // Layout 3: recording actions were added in 1.8.2 but saved as "leave as is", so nothing was recorded.
        private void RecordByDefault()
        {
            if (ObsOnStart.Record != 0 || ObsOnClear.Record != 0 || ObsOnDeath.Record != 0) return;
            ObsOnStart.Record = 1;
            ObsOnStart.SaveReplay = false; // a replay saved at the start holds the previous attempt
            ObsOnDeath.Record = 2;
            if (ObsOnDeath.Delay == 0f) ObsOnDeath.Delay = 1f;
        }

        // Layout 2: the top-left status lines (Progress, XAccuracy, music time, TimingScale) step 28 instead of 35,
        // stacked under the first one and aligned to its X.
        private void TightenStatusLines()
        {
            var lines = Elements.FindAll(c =>
                (c.Type == ElementType.Progress || c.Type == ElementType.XAccuracy ||
                 c.Type == ElementType.MusicTime || c.Type == ElementType.TimingScale) &&
                c.AnchorX == 0f && c.AnchorY == 1f);
            if (lines.Count < 2) return;
            lines.Sort((a, b) => b.Y.CompareTo(a.Y));
            float x = lines[0].X, y = lines[0].Y;
            foreach (ElementConfig c in lines)
            {
                c.X = x;
                c.Y = y;
                y -= ElementConfig.LineStep;
            }
        }

        // UMM window (Ctrl+F10).
        public void Draw()
        {
            Ui.Init();
            bool changed = false;
            // narrower than 800 on small screens so the UMM window never runs off the edge
            float winW = Mathf.Clamp(Screen.width - 120f, 600f, 800f), contentW = winW - 178f;
            Ui.FlowWidth = contentW - 50f - 40f; // content and card padding
            GUILayout.BeginVertical(Ui.Window, GUILayout.Width(winW));
            GUILayout.BeginHorizontal();

            // side rail: wordmark, numbered pages, edit button at the bottom
            GUILayout.BeginVertical(Ui.Rail, GUILayout.Width(178), GUILayout.ExpandHeight(true));
            GUILayout.Label("<color=#FF4757>NOW</color>OVERLAYER", Ui.Brand);
            GUILayout.Label("v" + Main.Mod.Info.Version, _verStyle ?? (_verStyle = new GUIStyle(Ui.Sub) { margin = new RectOffset(6, 0, 0, 0) }));
            GUILayout.Space(18);
            Ui.RailNav(ref _tab, TabNames);
            if (_tab != 5)
            {
                _capture = -1;
                Hotkeys.Capturing = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Space(16);
            if (GUILayout.Button("화면 편집", Ui.Primary, GUILayout.Height(42))) OverlayEditor.Open();
            GUILayout.Label(HkEditor.Label(), _hintStyle ?? (_hintStyle = new GUIStyle(Ui.Sub) { alignment = TextAnchor.MiddleCenter }));
            GUILayout.EndVertical();

            // content: big page title + one-line description, then the page
            GUILayout.BeginVertical(_contentStyle ?? (_contentStyle = new GUIStyle { padding = new RectOffset(26, 24, 22, 18) }), GUILayout.Width(contentW), GUILayout.MaxWidth(contentW));
            GUILayout.Label(TabNames[_tab], Ui.PageTitle);
            GUILayout.Label(TabSubs[_tab], Ui.PageSub);

            switch (_tab)
            {
                case 0: changed |= DrawGeneral(); break;
                case 1: changed |= DrawAnimation(Mathf.Clamp((int)((Ui.FlowWidth + 8f) / 176f), 1, 3), 168f); break;
                case 2: changed |= DrawEffects(); break;
                case 3: DrawFonts(); break;
                case 4: DrawPresets(); break;
                case 5: changed |= DrawHotkeys(); break;
                case 6: changed |= DrawObs(); break;
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            DrawStatusBar();
            GUILayout.EndVertical();

            if (changed) Overlay.Instance?.ApplySettings();
        }

        private static readonly string[] TabNames = { "일반", "애니메이션", "효과", "글꼴", "프리셋", "단축키", "연동" };

        private static readonly string[] TabSubs =
        {
            "보이는 항목, 전체 배율, 게임 기본 제목 숨기기",
            "플레이를 시작할 때 오버레이가 나타나는 모습",
            "콤보 마일스톤 · 결과 카드 · 자동 캡처",
            "오버레이 글자에 쓸 글꼴",
            "배치를 저장해 두고 단축키로 바로 바꾸기",
            "키 조합을 눌러서 바꾸기",
            "디스코드 상태 · OBS 장면 전환과 녹화"
        };

        private static GUIStyle _verStyle, _hintStyle, _contentStyle;

        // Bottom strip under a hairline: what is live right now.
        private void DrawStatusBar()
        {
            Rect line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            Ui.DrawRect(line, Ui.CardStroke);
            GUILayout.BeginHorizontal(Ui.StatusBar);
            Ui.StatusDot(OverlayHidden ? "오버레이 숨김" : "오버레이 켜짐", OverlayHidden ? Ui.FaintColor : Ui.Good);
            if (!string.IsNullOrEmpty(ActivePreset)) Ui.StatusDot("프리셋 " + ActivePreset, Ui.AccentText);
            if (ObsEnabled) Ui.StatusDot("OBS " + (Obs.Connected ? Obs.Status.Contains("녹화 중") ? "녹화 중" : "연결됨" : "끊김"), Obs.Connected ? Obs.Status.Contains("녹화 중") ? Ui.Accent : Ui.Good : Ui.FaintColor);
            if (DiscordEnabled) Ui.StatusDot("디스코드 " + (Discord.Status.StartsWith("연결됨") ? "연결됨" : "대기"), Discord.Status.StartsWith("연결됨") ? Ui.Good : Ui.FaintColor);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Ctrl+F10 으로 닫기", Ui.StatusText);
            GUILayout.EndHorizontal();
        }

        private bool DrawGeneral()
        {
            bool changed = false;
            Ui.Section("표시 항목");
            GUILayout.Label("위치·색·크기는 위의 [화면 편집]에서 바꿔요.", Ui.Sub);
            GUILayout.Space(4);
            foreach (ElementConfig c in Elements)
            {
                string sub = c.Type == ElementType.CustomText ? c.Template : c.Type == ElementType.Web ? c.Url : null;
                bool on = Ui.Switch(ElementConfig.DisplayName(c.Type), c.Visible, sub);
                if (on != c.Visible)
                {
                    c.Visible = on;
                    changed = true;
                }
            }
            GUILayout.EndVertical();

            Ui.Section("화면");
            bool hide = Ui.Switch("플레이 중 오버레이 숨기기", OverlayHidden, "단축키 " + HkToggle.Label());
            if (hide != OverlayHidden)
            {
                OverlayHidden = hide;
                changed = true;
            }
            bool title = Ui.Switch("게임 기본 곡 제목 숨기기", HideGameTitle, "곡 정보 항목(또는 {Title}·{Artist}가 든 사용자 텍스트)이 켜져 있으면, 게임이 크게 띄우는 \"작곡가 - 곡\"을 숨겨요");
            if (title != HideGameTitle)
            {
                HideGameTitle = title;
                changed = true;
            }
            Ui.Divider();
            float size = Ui.Slider("모든 항목 배율", Size, 0.5f, 2f);
            if (Mathf.Abs(size - Size) > 0.001f)
            {
                Size = Mathf.Round(size * 100f) / 100f;
                changed = true;
            }
            GUILayout.EndVertical();

            Ui.Section("기록");
            GUILayout.Label("시도 횟수 · " + PlayCount.Status, Ui.Sub);
            GUILayout.EndVertical();

            Ui.Section("업데이트");
            bool auto = Ui.Switch("자동 업데이트", AutoUpdate, "게임을 켤 때 새 버전을 받아 두고, 다음에 켤 때 적용돼요");
            if (auto != AutoUpdate)
            {
                AutoUpdate = auto;
                Save(Main.Mod);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Updater.Status, Ui.Sub);
            GUILayout.FlexibleSpace();
            GUI.enabled = !Updater.Busy;
            if (GUILayout.Button("지금 확인", Ui.Ghost, GUILayout.Width(90))) Updater.Check(true);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return changed;
        }

        // Shared by the UMM tab and the in-game editor panel.
        public bool DrawAnimation(int columns, float thumbWidth)
        {
            bool changed = false;
            Ui.Section("등장 방식");
            GUILayout.Label("플레이를 시작할 때 오버레이가 나타나는 모습이에요. 항목별로 다르게 하려면 화면 편집 → 속성에서 바꿔요.", Ui.Sub);
            GUILayout.Space(8);
            AnimType? picked = NowOverlayer.Anim.Grid(Anim, AnimDuration, columns, thumbWidth);
            if (picked.HasValue && picked.Value != Anim)
            {
                Anim = picked.Value;
                changed = true;
                Overlay.Instance?.PreviewIntro();
            }
            GUILayout.EndVertical();

            Ui.Section("타이밍");
            float dur = Ui.Slider("길이 (초)", AnimDuration, 0.15f, 2f);
            if (Mathf.Abs(dur - AnimDuration) > 0.001f)
            {
                AnimDuration = Mathf.Round(dur * 100f) / 100f;
                changed = true;
            }
            float stagger = Ui.Slider("항목 사이 간격 (초)", AnimStagger, 0f, 0.3f);
            if (Mathf.Abs(stagger - AnimStagger) > 0.001f)
            {
                AnimStagger = Mathf.Round(stagger * 100f) / 100f;
                changed = true;
            }
            GUILayout.Space(6);
            if (GUILayout.Button("▶  게임 화면에서 미리보기", Ui.Secondary, GUILayout.Height(42)))
            {
                Overlay.Instance?.PreviewIntro();
            }
            GUILayout.EndVertical();
            return changed;
        }
    }
}
