using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MonsterLove.StateMachine;

namespace NowOverlayer
{
    // Same hook points as JipperResourcePack (Main.cs / Combo.cs) for r149+.
    public static class Patches
    {
        public static void Apply(Harmony harmony)
        {
            foreach (Type t in typeof(Patches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception e)
                {
                    Main.LogError("Patch failed: " + t.Name, e);
                }
            }
        }

        private static IEnumerable<MethodBase> All(Type type, string name) =>
            AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name);

        [HarmonyPatch(typeof(scnGame), "Play")]
        private static class GameStart
        {
            private static void Postfix(int seqID)
            {
                if (!GCS.practiceMode) Overlay.Instance?.Show(seqID);
            }
        }

        [HarmonyPatch(typeof(scrPressToStart), "ShowText")]
        private static class EditorOrPracticeStart
        {
            private static void Postfix()
            {
                if (GCS.practiceMode || !scnGame.instance)
                {
                    Overlay.Instance?.Show(scrController.instance.currentSeqID);
                }
            }
        }

        [HarmonyPatch(typeof(StateBehaviour), "ChangeState", typeof(Enum))]
        private static class StateChange
        {
            private static void Postfix(Enum newState)
            {
                if (!(newState is States s)) return;
                if (s == States.Fail2) Overlay.Instance?.Death();
                else if (s == States.Won) Overlay.Instance?.Clear();
            }
        }

        [HarmonyPatch]
        private static class GameStop
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                All(typeof(scrUIController), "WipeToBlack")
                    .Concat(All(typeof(scnEditor), "ResetScene"))
                    .Concat(All(typeof(scrController), "StartLoadingScene"));

            private static void Postfix() => Overlay.Instance?.Hide();
        }

        private static bool TimingAvailable()
        {
            if (RDC.auto) return false;
            scrFloor f = scrController.instance ? scrController.instance.currFloor : null;
            return !f || !f.nextfloor || !f.nextfloor.auto;
        }

        [HarmonyPatch(typeof(scrMisc), "GetHitMarginInDeg")]
        private static class TimingDeg
        {
            private static void Postfix(float hitAngle, float refAngle, bool clockwise, float floorBpm, float conductorPitch)
            {
                if (!TimingAvailable()) return;
                float deg = (hitAngle - refAngle) * (clockwise ? 1f : -1f) * 57.29578f;
                Overlay.Instance?.OnTiming(deg / 180f / floorBpm / conductorPitch * 60000f);
            }
        }

        [HarmonyPatch(typeof(scrMisc), "GetHitMarginInSec")]
        private static class TimingSec
        {
            private static void Postfix(double timeDiff)
            {
                if (TimingAvailable()) Overlay.Instance?.OnTiming((float)timeDiff * 1000f);
            }
        }

        // Same as Overlayer's HudVisibilityPatch: the game's level caption is a scrHUDText with isTitle; while an
        // overlay item shows the song (곡 정보, or {Title}/{Artist} in a custom text) keep its graphic off.
        [HarmonyPatch(typeof(scrHUDText), "Update")]
        private static class GameTitle
        {
            private static readonly HashSet<UnityEngine.UI.Graphic> Hidden = new HashSet<UnityEngine.UI.Graphic>();

            private static void Postfix(scrHUDText __instance)
            {
                if (!__instance.isTitle || !__instance.TryGetComponent(out UnityEngine.UI.Graphic g)) return;
                if (Overlay.ShowsSong)
                {
                    g.enabled = false;
                    Hidden.Add(g);
                }
                else if (Hidden.Remove(g) && g)
                {
                    g.enabled = true; // the switch was turned off: give the caption back
                }
            }
        }

        // The game sets its own Rich Presence ("Playing a custom level") from the same process, which hides ours.
        // While ours is on, clear the game's once and skip its updates; turning ours off lets the game set it again.
        [HarmonyPatch(typeof(DiscordController), "UpdatePresence")]
        private static class GamePresence
        {
            private static readonly FieldInfo SdkField = AccessTools.Field(typeof(DiscordController), "discord");
            private static bool _cleared;

            private static bool Prefix(DiscordController __instance)
            {
                Settings s = Main.Settings;
                if (!s.DiscordEnabled || !s.DiscordReplaceGame)
                {
                    _cleared = false;
                    return true;
                }
                if (!_cleared)
                {
                    _cleared = true;
                    try
                    {
                        // the Discord SDK wrapper lives in its own assembly, so go through reflection;
                        // the callback must be a real delegate (the SDK invokes it from native code)
                        object sdk = SdkField?.GetValue(__instance);
                        object am = sdk?.GetType().GetMethod("GetActivityManager")?.Invoke(sdk, null);
                        MethodInfo clear = am?.GetType().GetMethod("ClearActivity");
                        if (clear != null)
                        {
                            Type handler = clear.GetParameters()[0].ParameterType;
                            Type result = handler.GetMethod("Invoke").GetParameters()[0].ParameterType;
                            Delegate noop = System.Linq.Expressions.Expression.Lambda(handler, System.Linq.Expressions.Expression.Empty(),
                                System.Linq.Expressions.Expression.Parameter(result)).Compile();
                            clear.Invoke(am, new object[] { noop });
                        }
                    }
                    catch (Exception e)
                    {
                        Main.LogError("Clearing the game's Discord status failed", e);
                    }
                }
                DiscordController.shouldUpdatePresence = false;
                return false;
            }
        }

        // Called when our Discord setting changes, so the game's presence comes back (or is cleared) right away.
        public static void RefreshGamePresence() => DiscordController.shouldUpdatePresence = true;

        [HarmonyPatch(typeof(scrMarginTracker), "AddHit")]
        private static class Hit
        {
            private static void Postfix(scrMarginTracker __instance, HitMargin hit)
            {
                var trackers = scrMistakesManager.marginTrackers;
                if (trackers != null && trackers.Length > 0 && trackers[0] != __instance) return;
                Overlay.Instance?.OnHit(hit);
            }
        }
    }
}
