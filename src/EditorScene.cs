using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace NowOverlayer
{
    // What the editor does to the game around it: the level in progress is frozen (same steps as the game's own
    // pause, without its menu) and the other mods' HUDs are hidden, so only this overlay is on the board.
    public static class EditorScene
    {
        public static void Enter()
        {
            try
            {
                Freeze();
            }
            catch (Exception e)
            {
                Main.LogError("Pause for editor failed", e);
            }
            try
            {
                HideMods();
            }
            catch (Exception e)
            {
                Main.LogError("Hide other mods failed", e);
            }
        }

        public static void Leave()
        {
            ShowMods();
            try
            {
                Unfreeze();
            }
            catch (Exception e)
            {
                Main.LogError("Resume after editor failed", e);
            }
        }

        // ---- pause ----

        private static bool _frozen;

        // scrController.TogglePauseGame without pauseMenu.Show
        private static void Freeze()
        {
            scrController ctrl = scrController.instance;
            if (_frozen || !ctrl || ctrl.paused || !Overlay.Instance || !Overlay.Instance.Playing) return;
            States s = ctrl.currentState;
            if (s != States.Countdown && s != States.Checkpoint && s != States.PlayerControl) return;
            ctrl.paused = true;
            ctrl.audioPaused = true;
            ctrl.enabled = false;
            Time.timeScale = 0f;
            Video("Pause");
            _frozen = true;
        }

        // Hand the paused level back as the game's normal pause: its menu comes up and Resume undoes everything.
        // The level editor's play mode has no pause menu, so there it is resumed right away.
        private static void Unfreeze()
        {
            if (!_frozen) return;
            _frozen = false;
            scrController ctrl = scrController.instance;
            if (!ctrl || !ctrl.paused) return;
            if ((!ADOBase.isLevelEditor || ADOBase.isScnGame) && ctrl.pauseMenu)
            {
                ctrl.pauseMenu.Show(PauseMenu.Submenu.Main, false);
                return;
            }
            ctrl.paused = false;
            ctrl.audioPaused = false;
            ctrl.enabled = true;
            Time.timeScale = 1f;
            Video("Play");
            AsyncInputUtils.UpdateOffsetTime(1L);
        }

        // the background video (VideoPlayer lives in a module this project does not reference)
        private static void Video(string method)
        {
            scrVfxPlus vfx = scrVfxPlus.instance;
            object player = vfx ? VideoField?.GetValue(vfx) : null;
            if (!(player is Behaviour b) || !b || !b.isActiveAndEnabled) return;
            player.GetType().GetMethod(method, Type.EmptyTypes)?.Invoke(player, null);
        }

        private static readonly FieldInfo VideoField = typeof(scrVfxPlus).GetField("videoBG");

        // ---- other mods ----

        private static readonly List<Canvas> HiddenCanvases = new List<Canvas>();
        private static readonly List<Behaviour> HiddenDrawers = new List<Behaviour>();

        private static void HideMods()
        {
            if (HiddenCanvases.Count > 0 || HiddenDrawers.Count > 0) return;
            var mods = new HashSet<Assembly>();
            foreach (UnityModManager.ModEntry m in UnityModManager.modEntries)
            {
                if (m.Active && m.Assembly != null && m != Main.Mod) mods.Add(m.Assembly);
            }
            mods.Remove(typeof(EditorScene).Assembly);
            if (mods.Count == 0) return;

            Transform ours = Overlay.Instance ? Overlay.Instance.transform.root : null;
            // uGUI: a top canvas whose object tree holds a component of another mod
            foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.enabled || !c.isRootCanvas || c.transform.root == ours) continue;
                foreach (MonoBehaviour mb in c.transform.root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb && mods.Contains(mb.GetType().Assembly))
                    {
                        c.enabled = false;
                        HiddenCanvases.Add(c);
                        break;
                    }
                }
            }
            // IMGUI: another mod's behaviour that draws in OnGUI
            foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (!mb.enabled || !mods.Contains(mb.GetType().Assembly) || !DrawsGui(mb.GetType())) continue;
                if (ours && mb.transform.root == ours) continue;
                mb.enabled = false;
                HiddenDrawers.Add(mb);
            }
            if (HiddenCanvases.Count + HiddenDrawers.Count > 0)
                Main.Log("Editor: hid " + HiddenCanvases.Count + " canvases, " + HiddenDrawers.Count + " GUI drawers of other mods");
        }

        private static void ShowMods()
        {
            foreach (Canvas c in HiddenCanvases)
            {
                try
                {
                    if (c) c.enabled = true;
                }
                catch (Exception e)
                {
                    Main.LogError("Show canvas failed", e);
                }
            }
            foreach (Behaviour b in HiddenDrawers)
            {
                try
                {
                    if (b) b.enabled = true;
                }
                catch (Exception e)
                {
                    Main.LogError("Show GUI drawer failed", e);
                }
            }
            HiddenCanvases.Clear();
            HiddenDrawers.Clear();
        }

        private static readonly Dictionary<Type, bool> GuiTypes = new Dictionary<Type, bool>();

        private static bool DrawsGui(Type t)
        {
            if (GuiTypes.TryGetValue(t, out bool yes)) return yes;
            yes = t.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;
            GuiTypes[t] = yes;
            return yes;
        }
    }
}
