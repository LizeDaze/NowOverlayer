using System;
using System.Collections.Generic;

namespace NowOverlayer
{
    public enum ElementType
    {
        Progress,
        XAccuracy,
        MusicTime,
        TimingScale,
        ProgressBar,
        Combo,
        Bpm,
        Judgement,
        Attempt,
        Accuracy,
        Best,
        Timing,
        Fps,
        Clock,
        SongInfo,
        TileInfo,
        Checkpoint,
        CustomText,
        Web,
        MapBest,
        FirstClear,
        Image,
        Webcam
    }

    // One overlay item. Position is an anchoredPosition on the 1920x1080 reference canvas.
    public class ElementConfig
    {
        public string Id;
        public ElementType Type;
        public bool Visible = true;
        public float AnchorX, AnchorY;
        public float PivotX, PivotY;
        public float X, Y;
        public float Scale = 1f;
        public float FontSize = 25f;
        public string Color = "FFFFFF";
        public string Template = "";
        public float Width = 526f, Height = 8f;
        public float Opacity = 1f;
        public int Anim = -1; // AnimType, or -1 = use the global entry animation
        public string Url = "";
        public float Fps = 10f;
        public bool Muted = true;
        public bool Mirror = true;  // webcam
        public float Zoom = 1f;     // webcam
        public int Shape = 1;       // webcam: 0 rectangle, 1 rounded, 2 circle
        public float Border = 3f;   // webcam border width
        public string Font = "";    // text items: own font file ("" = the overlay font from the 글꼴 tab)
        public int Rotation;        // webcam: 0, 90, 180, 270 (clockwise)
        public float CropL, CropR, CropT, CropB; // webcam: share of the picture cut from each side (0..0.45), as seen on screen
        public bool FlipV;          // webcam: upside-down flip
        public int ComboLayout;     // combo: 0 title over number, 1 number over title, 2 one line "Combo | 257"
        public float ComboGap;      // combo: px added between the title and the number (negative = closer)
        public string NumberColor = ""; // combo number: "" = red gradient that deepens with the combo (or the theme)

        public ElementConfig Clone()
        {
            var c = (ElementConfig)MemberwiseClone();
            c.Id = NewId();
            return c;
        }

        public const float LineStep = 28f; // top-left status lines (was 35)

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        public static string DisplayName(ElementType t)
        {
            switch (t)
            {
                case ElementType.Progress: return "맵 진행도";
                case ElementType.XAccuracy: return "절대정확도";
                case ElementType.MusicTime: return "음악 시간";
                case ElementType.TimingScale: return "판정범위";
                case ElementType.ProgressBar: return "진행 바";
                case ElementType.Combo: return "콤보";
                case ElementType.Bpm: return "BPM · KPS";
                case ElementType.Judgement: return "판정 개수";
                case ElementType.Attempt: return "시도 횟수";
                case ElementType.Accuracy: return "정확도";
                case ElementType.Best: return "최고 기록";
                case ElementType.Timing: return "타이밍 오차";
                case ElementType.Fps: return "FPS";
                case ElementType.Clock: return "현재 시각";
                case ElementType.SongInfo: return "곡 정보";
                case ElementType.TileInfo: return "타일 번호";
                case ElementType.Checkpoint: return "체크포인트";
                case ElementType.CustomText: return "사용자 텍스트";
                case ElementType.Web: return "웹페이지";
                case ElementType.MapBest: return "맵 최고 정확도";
                case ElementType.FirstClear: return "첫 클리어";
                case ElementType.Image: return "이미지 · GIF";
                case ElementType.Webcam: return "웹캠";
            }
            return t.ToString();
        }

        public static string Description(ElementType t)
        {
            switch (t)
            {
                case ElementType.Accuracy: return "일반 정확도 %";
                case ElementType.Best: return "이 시작 위치에서의 최고 진행도";
                case ElementType.Timing: return "마지막 입력 오차(ms)와 평균";
                case ElementType.Fps: return "화면 프레임";
                case ElementType.Clock: return "실제 시각 (방송용)";
                case ElementType.SongInfo: return "작곡가 - 곡 제목";
                case ElementType.TileInfo: return "현재 타일 / 전체 타일";
                case ElementType.Checkpoint: return "체크포인트 사용 횟수";
                case ElementType.CustomText: return "{토큰}을 넣어 직접 만드는 문구";
                case ElementType.Web: return "채팅·알림 위젯 같은 웹 주소를 화면에 표시";
                case ElementType.MapBest: return "처음부터 깬 판 중 가장 높은 절대정확도";
                case ElementType.FirstClear: return "이 맵을 처음 깬 날짜와 클리어 횟수";
                case ElementType.Image: return "PNG·JPG·GIF를 화면에 표시 (움직이는 GIF도 돼요)";
                case ElementType.Webcam: return "캠 화면을 띄워요 (모양·테두리·거울 모드·확대)";
            }
            return DisplayName(t);
        }

        public static bool IsText(ElementType t) => t != ElementType.ProgressBar && t != ElementType.Combo && t != ElementType.Web && t != ElementType.Image && t != ElementType.Webcam;

        public static ElementConfig Create(ElementType type)
        {
            var c = new ElementConfig { Id = NewId(), Type = type, AnchorX = 0.5f, AnchorY = 0.5f, PivotX = 0.5f, PivotY = 0.5f };
            switch (type)
            {
                case ElementType.ProgressBar:
                    c.AnchorY = c.PivotY = 1f;
                    c.Y = -13f;
                    break;
                case ElementType.Combo:
                    c.AnchorY = 1f;
                    c.Y = -188f;
                    break;
                case ElementType.Judgement:
                    c.FontSize = 30f;
                    c.Color = "D958FF";
                    break;
                case ElementType.CustomText:
                    c.Template = "Combo {Combo} · {KPS} KPS";
                    break;
                case ElementType.Webcam:
                    c.Width = 400f;
                    c.Height = 225f;
                    c.Url = "";
                    c.Shape = 0;   // plain rectangle, no border
                    c.Border = 0f;
                    break;
                case ElementType.Image:
                    c.Width = 0f;
                    c.Height = 0f;
                    break;
                case ElementType.Web:
                    c.Url = "https://www.google.com";
                    c.Width = 480f;
                    c.Height = 270f;
                    break;
            }
            return c;
        }

        // The author's own layout (1.14): status lines top-left, song title over a thicker progress bar,
        // judgement counts and attempts at the top, BPM top-right. Webcam / web page items are left out (per PC).
        public static List<ElementConfig> Defaults()
        {
            ElementConfig Line(ElementType t, float y, float scale = 1f) => new ElementConfig
            {
                Id = NewId(), Type = t, AnchorX = 0f, AnchorY = 1f, PivotX = 0f, PivotY = 0.5f, X = 16f, Y = y, Scale = scale
            };

            var list = new List<ElementConfig>
            {
                Line(ElementType.Progress, -31f),
                Line(ElementType.XAccuracy, -31f - LineStep),
                Line(ElementType.MusicTime, -31f - LineStep * 2),
                Line(ElementType.TimingScale, -31f - LineStep * 3),
                new ElementConfig
                {
                    Id = NewId(), Type = ElementType.ProgressBar, AnchorX = 0.5f, AnchorY = 1f, PivotX = 0.5f, PivotY = 1f, Y = -5f, Scale = 1.35f
                },
                new ElementConfig
                {
                    Id = NewId(), Type = ElementType.Combo, AnchorX = 0.5f, AnchorY = 1f, PivotX = 0.5f, PivotY = 0.5f, Y = -185f, Scale = 1.2f
                },
                new ElementConfig
                {
                    Id = NewId(), Type = ElementType.Bpm, AnchorX = 1f, AnchorY = 1f, PivotX = 1f, PivotY = 1f, X = -16f, Y = -16f
                },
                new ElementConfig
                {
                    Id = NewId(), Type = ElementType.Judgement, AnchorX = 0.5f, AnchorY = 0f, PivotX = 0.5f, PivotY = 0f,
                    Y = 81f, FontSize = 30f, Color = "D958FF", Font = BundledFont
                },
                new ElementConfig
                {
                    Id = NewId(), Type = ElementType.Attempt, AnchorX = 0.5f, AnchorY = 0f, PivotX = 0f, PivotY = 0f, X = 119f, Y = 32f
                },
                Line(ElementType.Fps, -144f, 1.05f),
                new ElementConfig
                {
                    Id = NewId(), Type = ElementType.SongInfo, AnchorX = 0.5f, AnchorY = 1f, PivotX = 0.5f, PivotY = 0.5f,
                    Y = -54f, Scale = 1.94f, Font = BundledFont
                }
            };
            return list;
        }

        // 고도 마음체, shipped in the mod Font folder (stored by file name, like any Font-folder pick)
        public const string BundledFont = "godoMaum.ttf";
    }
}
