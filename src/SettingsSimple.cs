using System;
using System.IO;
using UnityEngine;

namespace NowOverlayer
{
    // 간편 설정: one page with only what a first-time user needs (what to show, one color, size, font, move things).
    // Everything else stays behind [자세한 설정].
    public partial class Settings
    {
        private static readonly string[] SizeNames = { "작게", "보통", "크게" };
        private static readonly float[] SizeValues = { 0.65f, 0.8f, 1f };
        private static readonly string[] SimpleThemeNames = { "숫자에만", "글자까지 전부" };
        private static GUIStyle _stepStyle;

        private bool DrawSimple()
        {
            bool changed = false;

            Step(1, "화면에 띄울 것 고르기", "켠 것만 플레이 중에 보여요.");
            foreach (ElementConfig c in Elements)
            {
                bool on = Ui.Switch(ElementConfig.DisplayName(c.Type), c.Visible);
                if (on != c.Visible)
                {
                    c.Visible = on;
                    changed = true;
                }
            }
            GUILayout.EndVertical();

            Step(2, "색 고르기", "누르면 바로 바뀌어요. 맨 앞 [기본]은 원래 색이에요.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("기본", ThemeMode == Theme.Off ? Ui.RowOn : Ui.Row, GUILayout.Width(64), GUILayout.Height(30)))
            {
                ThemeMode = Theme.Off;
                changed = true;
            }
            GUILayout.Space(6);
            foreach (string hex in ThemeSwatches)
            {
                bool sel = ThemeMode != Theme.Off && string.Equals(hex, ThemeColor, StringComparison.OrdinalIgnoreCase);
                if (Ui.ColorSwatch(Ui.Hex(hex), sel))
                {
                    if (ThemeMode == Theme.Off) ThemeMode = Theme.Numbers;
                    ThemeColor = hex;
                    changed = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (ThemeMode != Theme.Off)
            {
                int pick = ThemeMode == Theme.Numbers ? 0 : 1;
                if (Ui.Segmented(ref pick, SimpleThemeNames))
                {
                    ThemeMode = pick == 0 ? Theme.Numbers : Theme.All;
                    changed = true;
                }
            }
            GUILayout.EndVertical();

            Step(3, "크기", "오버레이 전체 크기예요.");
            int size = NearestSize(Size);
            if (Ui.Segmented(ref size, SizeNames))
            {
                Size = SizeValues[size];
                changed = true;
            }
            GUILayout.EndVertical();

            Step(4, "글꼴", "지금: " + (string.IsNullOrEmpty(FontPath) ? "기본 (Maplestory Bold)" : Path.GetFileNameWithoutExtension(FontPath)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("글꼴 바꾸러 가기", Ui.Secondary, GUILayout.Height(34)))
            {
                SimpleMode = false;
                _tab = 2;
                Save(Main.Mod);
            }
            if (!string.IsNullOrEmpty(FontPath) && GUILayout.Button("기본으로", Ui.Ghost, GUILayout.Height(34))) PickFont("");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            Step(5, "위치 옮기기", "게임이 잠깐 멈추고, 항목을 끌어서 옮기거나 크기를 바꿀 수 있어요. 다 했으면 Esc.");
            if (GUILayout.Button("화면 편집 열기", Ui.Primary, GUILayout.Height(40))) OverlayEditor.Open();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.Label("업데이트 · " + Updater.Status, Ui.Sub);

            if (changed)
            {
                Save(Main.Mod);
                Overlay.Instance?.ShowPreview(4f);
            }
            return changed;
        }

        // A section card whose caption carries a step number.
        private static void Step(int n, string title, string sub)
        {
            Ui.Section(n + ".  " + title);
            GUILayout.Label(sub, _stepStyle ?? (_stepStyle = new GUIStyle(Ui.Sub) { wordWrap = true }));
            GUILayout.Space(4);
        }

        private static int NearestSize(float v)
        {
            int best = 0;
            for (int i = 1; i < SizeValues.Length; i++)
            {
                if (Mathf.Abs(SizeValues[i] - v) < Mathf.Abs(SizeValues[best] - v)) best = i;
            }
            return best;
        }
    }
}
