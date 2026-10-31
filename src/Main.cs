using System;
using HarmonyLib;
using UnityModManagerNet;

namespace NowOverlayer
{
    public static class Main
    {
        public static UnityModManager.ModEntry Mod;
        public static Settings Settings;
        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry mod)
        {
            Mod = mod;
            Settings = Settings.Load(mod);
            mod.OnToggle = OnToggle;
            mod.OnGUI = m => Settings.Draw();
            mod.OnSaveGUI = m => Settings.Save(m);
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry mod, bool on)
        {
            if (on)
            {
                PlayCount.Load();
                Stats.Load();
                Overlay.Create();
                _harmony = new Harmony(mod.Info.Id);
                Patches.Apply(_harmony);
            }
            else
            {
                _harmony?.UnpatchAll(mod.Info.Id);
                _harmony = null;
                Obs.Disconnect();
                Discord.Close();
                Overlay.DestroyInstance();
                PlayCount.Save();
            }
            return true;
        }

        public static void Log(string msg) => Mod?.Logger.Log(msg);

        public static void LogError(string msg, Exception e) => Mod?.Logger.Error(msg + "\n" + e);
    }
}
