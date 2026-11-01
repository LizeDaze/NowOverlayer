using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NowOverlayer
{
    // Overlay color theme (UMM 테마 tab): one color laid over the numbers, the words, or everything.
    // Judgement counts keep their judgement colors in every mode.
    public static class Theme
    {
        public const int Off = 0, Numbers = 1, Words = 2, All = 3;

        public static readonly string[] ModeNames = { "끄기", "숫자만", "글자만", "전체" };

        public static readonly string[] ModeSubs =
        {
            "항목마다 원래 색 그대로예요",
            "숫자에만 테마 색을 입혀요 · 예: Progress | <b>42.1%</b>",
            "글자에만 테마 색을 입혀요 · 예: <b>Progress |</b> 42.1%",
            "글자와 숫자 모두, 진행 바도 테마 색이에요"
        };

        public static int Mode => Main.Settings.ThemeMode;

        public static Color Color => Snapshot.Hex(Main.Settings.ThemeColor);

        public static bool ColorsNumbers => Mode == Numbers || Mode == All;

        public static bool ColorsWords => Mode == Words || Mode == All;

        public static bool Skips(ElementConfig c) => c.Type == ElementType.Judgement;

        // Rich text from Snapshot.Text recolored for the theme. baseHex is the item's own color (words without a tag).
        public static string Apply(string rich, string baseHex)
        {
            int mode = Mode;
            if (mode == Off || string.IsNullOrEmpty(rich)) return rich;
            string theme = Main.Settings.ThemeColor;
            var sb = new StringBuilder(rich.Length + 64);
            var stack = new List<string>();
            var run = new StringBuilder();
            string runColor = null;

            void Flush()
            {
                if (run.Length == 0) return;
                sb.Append("<color=#").Append(runColor).Append('>').Append(run).Append("</color>");
                run.Length = 0;
            }

            for (int i = 0; i < rich.Length; i++)
            {
                char ch = rich[i];
                if (ch == '<')
                {
                    int close = rich.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        string tag = rich.Substring(i + 1, close - i - 1);
                        if (tag.StartsWith("color=", System.StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Add(tag.Substring(6).TrimStart('#'));
                            i = close;
                            continue;
                        }
                        if (tag.Equals("/color", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                            i = close;
                            continue;
                        }
                        // other tags (<b>, <size> ...) pass through as they are
                        Flush();
                        sb.Append(rich, i, close - i + 1);
                        i = close;
                        continue;
                    }
                }
                string own = stack.Count > 0 ? stack[stack.Count - 1] : baseHex;
                string color;
                if (ch == '\n' || ch == ' ') color = runColor ?? own;
                else if (IsNumberChar(rich, i)) color = mode == Words ? own : theme;
                else color = mode == Numbers ? own : theme;
                if (color != runColor) Flush();
                runColor = color;
                run.Append(ch);
            }
            Flush();
            return sb.ToString();
        }

        // 42.18%  +3.2  1:05  257 / 3960 : digits, plus the signs and separators stuck to them
        private static bool IsNumberChar(string s, int i)
        {
            char c = s[i];
            if (char.IsDigit(c)) return true;
            bool prevDigit = i > 0 && char.IsDigit(s[i - 1]);
            bool nextDigit = i + 1 < s.Length && char.IsDigit(s[i + 1]);
            switch (c)
            {
                case '.':
                case ',':
                case ':':
                    return prevDigit && nextDigit;
                case '%':
                    return prevDigit;
                case '+':
                case '-':
                    return nextDigit && (i == 0 || !char.IsLetterOrDigit(s[i - 1]));
            }
            return false;
        }
    }
}
