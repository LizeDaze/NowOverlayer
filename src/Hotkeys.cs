using System.Collections.Generic;
using UnityEngine;

namespace NowOverlayer
{
    public class Hotkey
    {
        public KeyCode Key = KeyCode.None;
        public bool Ctrl, Shift, Alt;

        public Hotkey() { }

        public Hotkey(KeyCode key, bool ctrl, bool shift, bool alt = false)
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        public string Label()
        {
            if (Key == KeyCode.None) return "없음";
            string s = "";
            if (Ctrl) s += "Ctrl + ";
            if (Shift) s += "Shift + ";
            if (Alt) s += "Alt + ";
            return s + KeyName(Key);
        }

        public static string KeyName(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            switch (k)
            {
                case KeyCode.LeftArrow: return "←";
                case KeyCode.RightArrow: return "→";
                case KeyCode.UpArrow: return "↑";
                case KeyCode.DownArrow: return "↓";
            }
            return k.ToString();
        }

        public static bool Mods(bool ctrl, bool shift, bool alt) =>
            ctrl == (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            shift == (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
            alt == (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));

        public bool Down() => Key != KeyCode.None && Input.GetKeyDown(Key) && Mods(Ctrl, Shift, Alt);
    }

    public static class Hotkeys
    {
        public static bool Capturing; // the hotkey tab is waiting for a key; don't fire actions

        public struct Entry
        {
            public string Name, Description;
            public System.Func<Settings, Hotkey> Get;
            public System.Action<Settings, Hotkey> Set;
        }

        public static readonly Entry[] All =
        {
            new Entry { Name = "오버레이 보이기 / 숨기기", Description = "플레이 중 오버레이 전체를 잠깐 숨겨요",
                Get = s => s.HkToggle, Set = (s, h) => s.HkToggle = h },
            new Entry { Name = "화면 편집 열기 / 닫기", Description = "모드 창 없이 바로 편집 화면으로",
                Get = s => s.HkEditor, Set = (s, h) => s.HkEditor = h },
            new Entry { Name = "다음 프리셋", Description = "저장한 프리셋을 차례로 바꿔요",
                Get = s => s.HkNextPreset, Set = (s, h) => s.HkNextPreset = h },
            new Entry { Name = "이전 프리셋", Description = "",
                Get = s => s.HkPrevPreset, Set = (s, h) => s.HkPrevPreset = h },
            new Entry { Name = "결과 카드 닫기", Description = "떠 있는 결과 카드를 바로 닫아요",
                Get = s => s.HkCloseResult, Set = (s, h) => s.HkCloseResult = h },
            new Entry { Name = "등장 애니메이션 다시 보기", Description = "",
                Get = s => s.HkReplayIntro, Set = (s, h) => s.HkReplayIntro = h }
        };

        public static void Check()
        {
            // skip while typing in the mod window's text fields
            bool typing = GUIUtility.keyboardControl != 0 && UnityModManagerNet.UnityModManager.UI.Instance != null && UnityModManagerNet.UnityModManager.UI.Instance.Opened;
            if (Capturing || typing) return;
            Settings s = Main.Settings;
            Overlay o = Overlay.Instance;
            if (!o) return;

            if (s.HkToggle.Down())
            {
                s.OverlayHidden = !s.OverlayHidden;
                o.ShowToast(s.OverlayHidden ? "오버레이 숨김" : "오버레이 보임");
            }
            if (s.HkEditor.Down())
            {
                if (OverlayEditor.Active) OverlayEditor.Close();
                else OverlayEditor.Open();
            }
            if (s.HkCloseResult.Down()) o.CloseResult();
            if (s.HkReplayIntro.Down()) o.PlayIntro();
            if (s.HkNextPreset.Down()) StepPreset(1);
            if (s.HkPrevPreset.Down()) StepPreset(-1);

            List<Preset> list = s.Presets;
            if (list.Count == 0) return;
            if (!Hotkey.Mods(s.PresetCtrl, s.PresetShift, s.PresetAlt)) return;
            for (int i = 0; i < list.Count && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Presets.Apply(list[i]);
                    return;
                }
            }
        }

        private static void StepPreset(int dir)
        {
            List<Preset> list = Main.Settings.Presets;
            if (list.Count == 0) return;
            int i = list.FindIndex(p => p.Name == Main.Settings.ActivePreset);
            i = ((i < 0 ? (dir > 0 ? -1 : 0) : i) + dir + list.Count) % list.Count;
            Presets.Apply(list[i]);
        }

        public static string PresetLabel(int index)
        {
            Settings s = Main.Settings;
            if (index >= 9) return "";
            string m = (s.PresetCtrl ? "Ctrl + " : "") + (s.PresetShift ? "Shift + " : "") + (s.PresetAlt ? "Alt + " : "");
            return m + (index + 1);
        }
    }
}
