using System;
using System.Text;
using UnityEngine;

namespace NowOverlayer
{
    // Values every element reads from. Filled from the game while playing, or with sample values in the editor.
    public class Snapshot
    {
        public float Progress, XAcc = 1f, Acc = 1f, Best;
        public float Now, Total;
        public double Margin = 1;
        public float TBPM, CBPM;
        public readonly int[] Hits = new int[16];
        public int Combo, Attempt, FullAttempt, Fps, Tile, TotalTiles, Checkpoints;
        public bool HasTiming;
        public float Timing, AvgTiming;
        public string Title = "", Artist = "";
        public float MapBestXAcc = -1f, MapBestAcc = -1f;
        public string FirstClear;
        public int Clears, MaxCombo;

        public static Snapshot Sample()
        {
            var s = new Snapshot
            {
                Progress = 0.0649f, XAcc = 1f, Acc = 1.0257f, Best = 0.4211f,
                Now = 28f, Total = 207f, Margin = 1, TBPM = 1202.91f, CBPM = 902.18f,
                Combo = 257, Attempt = 63, FullAttempt = 1173, Fps = 240, Tile = 257, TotalTiles = 3960, Checkpoints = 0,
                HasTiming = true, Timing = 3.2f, AvgTiming = -1.4f, Title = "Song Title", Artist = "Artist",
                MapBestXAcc = 0.9912f, MapBestAcc = 1.0034f, FirstClear = "2026-10-21 23:48", Clears = 3, MaxCombo = 412
            };
            Judge.Set(s.Hits, Judge.HasXPerfect ? Judge.XPerfect : Judge.Perfect, 257);
            return s;
        }

        public int Perfect => Judge.Perfects(Hits);

        // ---- per-element text ----

        public string Text(ElementConfig c)
        {
            switch (c.Type)
            {
                case ElementType.Progress:
                    return "Progress | " + Col(Lerp("FF9C9C", "E44343", Progress), Pct(Progress));
                case ElementType.XAccuracy:
                    return "XAccuracy | " + Col(XAcc >= 1f ? "FFDA45" : Lerp("FF8787", "FFFFFF", Mathf.InverseLerp(0.98f, 1f, XAcc)), Pct(XAcc));
                case ElementType.Accuracy:
                    return "Accuracy | " + Col(Acc >= 1f ? "FFDA45" : "FF8787", Pct(Acc));
                case ElementType.MusicTime:
                    return "음악 시간 | " + TimeStr(Now, Total >= 3600f) + "~" + TimeStr(Total, Total >= 3600f);
                case ElementType.TimingScale:
                    return "TimingScale : " + Math.Round(Margin * 100.0, 2) + "%";
                case ElementType.Bpm:
                    return "TBPM | " + Col(Lerp("FF7F7F", "B11818", TBPM / 8000f), R2(TBPM)) + "\n" +
                           "CBPM | " + Col(Lerp("FF8080", "AA3131", CBPM / 8000f), R2(CBPM)) + "\n" +
                           "KPS | " + Col(Lerp("FF8282", "BE2525", CBPM / 8000f), R2(CBPM / 60f));
                case ElementType.Judgement:
                    return JudgementText();
                case ElementType.Attempt:
                    return "Attempt " + Attempt + "\nFull Attempt " + FullAttempt;
                case ElementType.Best:
                    return "Best | " + Col(Lerp("FF9C9C", "E44343", Best), Pct(Best));
                case ElementType.Timing:
                    return HasTiming
                        ? "Timing | " + Col(TimingColor(Timing), Ms(Timing)) + " (" + Col(TimingColor(AvgTiming), Ms(AvgTiming)) + ")"
                        : "Timing | -";
                case ElementType.Fps:
                    return "FPS | " + Fps;
                case ElementType.Clock:
                    return DateTime.Now.ToString("HH:mm:ss");
                case ElementType.SongInfo:
                    return Artist.Length > 0 ? Artist + " - " + Title : Title;
                case ElementType.TileInfo:
                    return "Tile | " + Tile + " / " + TotalTiles;
                case ElementType.Checkpoint:
                    return "CheckPoint | " + Checkpoints;
                case ElementType.CustomText:
                    return Expand(c.Template ?? "");
                case ElementType.MapBest:
                    return "Best XAcc | " + (MapBestXAcc < 0f ? "-" : Col("FFDA45", Pct(MapBestXAcc)));
                case ElementType.FirstClear:
                    return FirstClear == null ? "아직 클리어 기록 없음" : "첫 클리어 " + FirstClear + " · " + Clears + "회";
            }
            return "";
        }

        public static readonly string[] Tokens =
        {
            "Progress", "XAcc", "Acc", "Best", "Time", "TotalTime", "TimingScale", "TBPM", "CBPM", "KPS", "Combo",
            "Attempt", "FullAttempt", "Timing", "AvgTiming", "FPS", "Clock", "Title", "Artist", "Tile", "TotalTiles",
            "Checkpoint", "MapBestXAcc", "MapBestAcc", "FirstClear", "Clears", "MaxCombo", "Overload", "TooEarly", "Early", "Perfect", "Late", "TooLate", "Miss"
        };

        public string Token(string name)
        {
            int[] h = Hits;
            switch (name)
            {
                case "Progress": return Pct(Progress);
                case "XAcc": return Pct(XAcc);
                case "Acc": return Pct(Acc);
                case "Best": return Pct(Best);
                case "Time": return TimeStr(Now, Total >= 3600f);
                case "TotalTime": return TimeStr(Total, Total >= 3600f);
                case "TimingScale": return Math.Round(Margin * 100.0, 2) + "%";
                case "TBPM": return R2(TBPM);
                case "CBPM": return R2(CBPM);
                case "KPS": return R2(CBPM / 60f);
                case "Combo": return Combo.ToString();
                case "Attempt": return Attempt.ToString();
                case "FullAttempt": return FullAttempt.ToString();
                case "Timing": return HasTiming ? Ms(Timing) : "-";
                case "AvgTiming": return HasTiming ? Ms(AvgTiming) : "-";
                case "FPS": return Fps.ToString();
                case "Clock": return DateTime.Now.ToString("HH:mm:ss");
                case "Title": return Title;
                case "Artist": return Artist;
                case "Tile": return Tile.ToString();
                case "TotalTiles": return TotalTiles.ToString();
                case "Checkpoint": return Checkpoints.ToString();
                case "MapBestXAcc": return MapBestXAcc < 0f ? "-" : Pct(MapBestXAcc);
                case "MapBestAcc": return MapBestAcc < 0f ? "-" : Pct(MapBestAcc);
                case "FirstClear": return FirstClear ?? "-";
                case "Clears": return Clears.ToString();
                case "MaxCombo": return MaxCombo.ToString();
                case "Overload": return Judge.Get(h, Judge.FailOverload).ToString();
                case "TooEarly": return Judge.Get(h, Judge.TooEarly).ToString();
                case "Early": return (Judge.Get(h, Judge.VeryEarly) + Judge.Get(h, Judge.EarlyPerfect)).ToString();
                case "Perfect": return Perfect.ToString();
                case "Late": return (Judge.Get(h, Judge.LatePerfect) + Judge.Get(h, Judge.VeryLate)).ToString();
                case "TooLate": return Judge.Get(h, Judge.TooLate).ToString();
                case "Miss": return Judge.Get(h, Judge.FailMiss).ToString();
            }
            return null;
        }

        public string Expand(string template)
        {
            var sb = new StringBuilder(template.Length + 16);
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                int close = template.IndexOf('}', open + 1);
                if (close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                sb.Append(template, i, open - i);
                string v = Token(template.Substring(open + 1, close - open - 1));
                sb.Append(v ?? template.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }

        // 과부화 / 너무빠름 / 주황빠름 / 노랑빠름 / 정확 / 노랑느림 / 주황느림 / 너무느림 / 놓침
        private string JudgementText()
        {
            int[] h = Hits;
            int overload = Judge.Get(h, Judge.FailOverload), tooEarly = Judge.Get(h, Judge.TooEarly);
            int veryEarly = Judge.Get(h, Judge.VeryEarly), earlyP = Judge.Get(h, Judge.EarlyPerfect);
            int lateP = Judge.Get(h, Judge.LatePerfect), veryLate = Judge.Get(h, Judge.VeryLate);
            int tooLate = Judge.Get(h, Judge.TooLate), miss = Judge.Get(h, Judge.FailMiss);

            // Pad the shorter side with invisible zeros so the perfect count stays centered.
            int balance = Digits(overload) + Digits(tooEarly) + Digits(veryEarly) + Digits(earlyP)
                          - Digits(lateP) - Digits(veryLate) - Digits(tooLate) - Digits(miss);
            var sb = new StringBuilder(160);
            if (balance < 0) sb.Append("<color=#0000>").Append('0', -balance).Append("</color>");
            sb.Append(overload)
              .Append(" <color=#FF0000>").Append(tooEarly)
              .Append(" <color=#FF6F4E>").Append(veryEarly)
              .Append(" <color=#A0FF4E>").Append(earlyP)
              .Append(" <color=#60FF4E>").Append(Perfect)
              .Append("</color> ").Append(lateP)
              .Append("</color> ").Append(veryLate)
              .Append("</color> ").Append(tooLate)
              .Append("</color> ").Append(miss);
            if (balance > 0) sb.Append("<color=#0000>").Append('0', balance).Append("</color>");
            return sb.ToString();
        }

        // ---- formatting ----

        private static string Pct(float v) => Math.Round(v * 100f, 2) + "%";

        private static string R2(float v) => Math.Round(v, 2).ToString();

        private static string Ms(float v) => (v > 0 ? "+" : "") + Math.Round(v, 1) + "ms";

        private static string Col(string hex, string text) => "<color=#" + hex + ">" + text + "</color>";

        private static string TimingColor(float ms)
        {
            float a = Mathf.Abs(ms);
            return a < 30f ? "60FF4E" : a < 60f ? "A0FF4E" : "FF6F4E";
        }

        public static string TimeStr(float time, bool hour)
        {
            int n = (int)time;
            return hour
                ? n / 3600 + ":" + (n % 3600 / 60).ToString("00") + ":" + (n % 60).ToString("00")
                : n / 60 + ":" + (n % 60).ToString("00");
        }

        private static int Digits(int v)
        {
            int d = 1;
            while (v >= 10) { v /= 10; d++; }
            return d;
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }

        public static string Lerp(string a, string b, float t) =>
            ColorUtility.ToHtmlStringRGB(Color.Lerp(Hex(a), Hex(b), Mathf.Clamp01(t)));
    }
}
