using System;

namespace NowOverlayer
{
    // HitMargin is not the same in every game version: the Steam "older" branch has a single Perfect and no
    // XPerfect / PerfectMinus / PerfectPlus / Midspin, and the values shift. Compiled-in enum constants would
    // read the wrong counters, so each judgement is looked up by name at startup (-1 = this version lacks it).
    internal static class Judge
    {
        public static readonly int TooEarly = Of("TooEarly"), VeryEarly = Of("VeryEarly"), EarlyPerfect = Of("EarlyPerfect"),
            Perfect = Of("Perfect"), XPerfect = Of("XPerfect"), PerfectMinus = Of("PerfectMinus"), PerfectPlus = Of("PerfectPlus"),
            LatePerfect = Of("LatePerfect"), VeryLate = Of("VeryLate"), TooLate = Of("TooLate"),
            FailMiss = Of("FailMiss"), FailOverload = Of("FailOverload"), Auto = Of("Auto"), Midspin = Of("Midspin");

        // the game has XPerfect (newer versions); without it every perfect is a plain Perfect
        public static bool HasXPerfect => XPerfect >= 0;

        private static int Of(string name)
        {
            try
            {
                return (int)Enum.Parse(typeof(HitMargin), name);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static int Get(int[] hits, int margin) => margin >= 0 && hits != null && margin < hits.Length ? hits[margin] : 0;

        public static void Set(int[] hits, int margin, int value)
        {
            if (margin >= 0 && margin < hits.Length) hits[margin] = value;
        }

        // every judgement that counts as perfect, on any version
        public static int Perfects(int[] h) => Get(h, Perfect) + Get(h, XPerfect) + Get(h, PerfectMinus) + Get(h, PerfectPlus) + Get(h, Auto);

        public static bool IsPerfect(int m) => m >= 0 && (m == Perfect || m == XPerfect || m == PerfectMinus || m == PerfectPlus);

        public static bool IsNeutral(int m) => m >= 0 && (m == Auto || m == Midspin);
    }
}
