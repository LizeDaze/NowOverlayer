using UnityEngine;

namespace NowOverlayer
{
    public enum AnimType
    {
        None,
        Fade,
        SlideIn,
        RiseUp,
        DropDown,
        Pop,
        ZoomOut,
        Bounce,
        Typewriter,
        Glitch,
        Flip,
        Swing
    }

    // Offset is in reference-canvas units (y up). Visible is the shown fraction of characters (typewriter).
    public struct Pose
    {
        public float Alpha, Scale, ScaleY, Rot, Visible;
        public Vector2 Offset;

        public static readonly Pose Identity = new Pose { Alpha = 1f, Scale = 1f, ScaleY = 1f, Visible = 1f };
    }

    public static class Anim
    {
        public static readonly AnimType[] All = (AnimType[])System.Enum.GetValues(typeof(AnimType));

        public static string Name(AnimType t)
        {
            switch (t)
            {
                case AnimType.None: return "없음";
                case AnimType.Fade: return "페이드";
                case AnimType.SlideIn: return "가장자리에서 슬라이드";
                case AnimType.RiseUp: return "아래에서 떠오르기";
                case AnimType.DropDown: return "위에서 내려오기";
                case AnimType.Pop: return "팝";
                case AnimType.ZoomOut: return "줌 아웃";
                case AnimType.Bounce: return "통통 떨어지기";
                case AnimType.Typewriter: return "타자기";
                case AnimType.Glitch: return "글리치";
                case AnimType.Flip: return "펼치기";
                case AnimType.Swing: return "흔들며 등장";
            }
            return t.ToString();
        }

        // Direction an element slides in from: toward the screen edge it is anchored to.
        public static Vector2 EdgeDir(float anchorX, float anchorY)
        {
            float x = anchorX < 0.25f ? -1f : anchorX > 0.75f ? 1f : 0f;
            float y = anchorY > 0.75f ? 1f : anchorY < 0.25f ? -1f : 0f;
            if (x == 0f && y == 0f) y = -1f;
            if (x != 0f && y != 0f) y = 0f;
            return new Vector2(x, y);
        }

        public static Pose Eval(AnimType type, float t, int seed, Vector2 edge)
        {
            t = Mathf.Clamp01(t);
            Pose p = Pose.Identity;
            if (t >= 1f) return p;
            switch (type)
            {
                case AnimType.Fade:
                    p.Alpha = OutCubic(t);
                    break;
                case AnimType.SlideIn:
                    p.Offset = edge * 160f * (1f - OutExpo(t));
                    p.Alpha = Mathf.Clamp01(t * 3f);
                    break;
                case AnimType.RiseUp:
                    p.Offset = new Vector2(0f, -70f * (1f - OutExpo(t)));
                    p.Alpha = OutCubic(t);
                    break;
                case AnimType.DropDown:
                    p.Offset = new Vector2(0f, 70f * (1f - OutExpo(t)));
                    p.Alpha = OutCubic(t);
                    break;
                case AnimType.Pop:
                    p.Scale = Mathf.LerpUnclamped(0.3f, 1f, OutBack(t));
                    p.Alpha = Mathf.Clamp01(t * 4f);
                    break;
                case AnimType.ZoomOut:
                    p.Scale = 1.8f - 0.8f * OutExpo(t);
                    p.Alpha = OutCubic(t);
                    break;
                case AnimType.Bounce:
                    p.Offset = new Vector2(0f, 220f * (1f - OutBounce(t)));
                    p.Alpha = Mathf.Clamp01(t * 5f);
                    break;
                case AnimType.Typewriter:
                    p.Visible = t;
                    p.Alpha = Mathf.Clamp01(t * 8f);
                    break;
                case AnimType.Glitch:
                    if (t < 0.8f)
                    {
                        int step = (int)(t * 28f);
                        float r1 = Hash(seed * 131 + step), r2 = Hash(seed * 977 + step * 7);
                        p.Offset = new Vector2((r1 - 0.5f) * 36f * (1f - t), (r2 - 0.5f) * 10f * (1f - t));
                        p.Alpha = r2 > 0.3f ? 1f : 0.15f;
                        p.ScaleY = r1 > 0.85f ? 0.6f : 1f;
                    }
                    break;
                case AnimType.Flip:
                    p.ScaleY = Mathf.Max(0.02f, OutBack(t));
                    p.Alpha = Mathf.Clamp01(t * 3f);
                    break;
                case AnimType.Swing:
                    p.Rot = 28f * (1f - t) * Mathf.Cos(t * 14f);
                    p.Scale = Mathf.LerpUnclamped(0.6f, 1f, OutBack(t));
                    p.Alpha = Mathf.Clamp01(t * 4f);
                    break;
            }
            return p;
        }

        private static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);

        private static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static float OutBounce(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) return n1 * (t -= 1.5f / d1) * t + 0.75f;
            if (t < 2.5f / d1) return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }

        // ---- IMGUI preview tile: a sample chip playing the animation on a loop ----

        public static bool Thumb(AnimType type, bool selected, float duration, float width = 168f)
        {
            Rect r = GUILayoutUtility.GetRect(width, 104f, GUILayout.Width(width), GUILayout.Height(104f));
            GUI.Box(r, GUIContent.none, selected ? Ui.ThumbOn : Ui.ThumbBox);
            var stage = new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 62f);
            Ui.DrawRect(stage, Ui.StageColor);

            float cycle = duration + 0.9f;
            float t = (Time.realtimeSinceStartup % cycle) / Mathf.Max(duration, 0.05f);
            Pose p = type == AnimType.None ? Pose.Identity : Eval(type, t, 7, new Vector2(-1f, 0f));

            const string sample = "Combo 257";
            string text = type == AnimType.Typewriter
                ? sample.Substring(0, Mathf.Clamp(Mathf.CeilToInt(sample.Length * p.Visible), 0, sample.Length))
                : sample;
            Vector2 center = stage.center + new Vector2(p.Offset.x, -p.Offset.y) * 0.3f;
            var chip = new Rect(center.x - 52f, center.y - 15f, 104f, 30f);

            GUI.BeginClip(stage);
            chip.position -= stage.position;
            Vector2 pivot = chip.center;
            Matrix4x4 oldM = GUI.matrix;
            Color oldC = GUI.color;
            GUIUtility.RotateAroundPivot(-p.Rot, pivot);
            GUIUtility.ScaleAroundPivot(new Vector2(p.Scale, p.Scale * p.ScaleY), pivot);
            GUI.color = new Color(1f, 1f, 1f, p.Alpha);
            GUI.Box(chip, GUIContent.none, Ui.AccentBox);
            GUI.Label(chip, text, Ui.ChipText);
            GUI.matrix = oldM;
            GUI.color = oldC;
            GUI.EndClip();

            GUI.Label(new Rect(r.x, r.yMax - 30f, r.width, 24f), Name(type), selected ? Ui.ThumbLabelOn : Ui.ThumbLabel);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Grid of thumbs; returns the clicked type or null.
        public static AnimType? Grid(AnimType current, float duration, int columns, float width)
        {
            AnimType? picked = null;
            for (int i = 0; i < All.Length; i++)
            {
                if (i % columns == 0) GUILayout.BeginHorizontal();
                if (Thumb(All[i], All[i] == current, duration, width)) picked = All[i];
                if (i % columns == columns - 1 || i == All.Length - 1)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
            return picked;
        }
    }
}
