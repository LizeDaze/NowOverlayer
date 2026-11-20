using System;
using System.Reflection;
using UnityEngine;

namespace NowOverlayer
{
    // Optional bridge to TUFReplay (https://github.com/KGH1113/TUFReplay), with no reference to TUFReplay.dll: its
    // ReplayApi and NativeInputPause are found by name, and TUFReplay finds the Provider's methods by name.
    //  - Each run TUFReplay records saves a RunState (attempts, records, start time) and clock anchors.
    //  - While TUFReplay replays a run, in the game or rendered to video, ReplayRun holds that run and the replay clock.
    //  - While the overlay editor takes the keyboard, TUFReplay does not record keys as the run's input.
    // Without TUFReplay, or with a TUFReplay older than ReplayApi 2, nothing here does anything.
    public static class TUFReplayApi
    {
        private const string Id = "NowOverlayer";
        private const int RequiredApiVersion = 2;
        private const string PauseOwner = "NowOverlayer.Editor";

        private static Func<string, object, bool> _register;
        private static Action<string> _unregister;
        private static Func<object, bool> _addEvent;
        private static Func<bool> _isRecording;
        private static Func<int> _renderFps;
        private static Action<string> _pauseInput, _resumeInput;
        private static bool _registered, _unsupported, _inputPaused;

        // On enable, and after each mod loads (Patches.ModLoaded), since TUFReplay may load after this mod.
        public static void TryRegister()
        {
            if (_registered || _unsupported) return;
            try
            {
                if (!Resolve()) return;
                // TUFReplay writes why to its log when it refuses the provider
                _registered = _register(Id, new Provider());
                if (_registered) Main.Log("TUFReplay ReplayApi connected");
            }
            catch (Exception e)
            {
                _unsupported = true; // don't retry on every mod load
                Main.LogError("TUFReplay ReplayApi failed to connect", e);
            }
        }

        public static void Shutdown()
        {
            PauseNativeInput(false);
            try
            {
                if (_registered) _unregister(Id);
            }
            catch (Exception e)
            {
                Main.LogError("TUFReplay ReplayApi unregister failed", e);
            }
            _registered = false;
            ReplayRun.End();
        }

        // True while TUFReplay records a run, which is when AddEvent keeps an event.
        public static bool IsRecording() => _isRecording != null && _isRecording();

        public static void AddEvent(object e) => _addEvent?.Invoke(e);

        // The overlay editor takes the keyboard away from the game (EditorScene), so TUFReplay must not record those
        // keys as the run's input: a replay would press them again.
        public static void PauseNativeInput(bool pause)
        {
            if (pause == _inputPaused) return;
            if (_pauseInput == null) ResolveInputPause();
            try
            {
                (pause ? _pauseInput : _resumeInput)?.Invoke(PauseOwner);
                _inputPaused = pause && _pauseInput != null;
            }
            catch (Exception e)
            {
                Main.LogError("TUFReplay NativeInputPause failed", e);
            }
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "TUFReplay") return assembly.GetType(name, false);
            }
            return null;
        }

        private static bool Resolve()
        {
            if (_register != null) return true;
            Type api = FindType("TUFReplay.ReplayApi");
            if (api?.GetField("ApiVersion")?.GetValue(null) is not int version) return false;
            if (version < RequiredApiVersion)
            {
                _unsupported = true;
                Main.Log("TUFReplay ReplayApi " + version + " is older than " + RequiredApiVersion + ", so replays show today's attempts and records. Update TUFReplay to connect.");
                return false;
            }
            const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
            object store = api.GetMethod("GetEventStore", Static).Invoke(null, new object[] { Id });
            _addEvent = (Func<object, bool>)Delegate.CreateDelegate(typeof(Func<object, bool>), store,
                store.GetType().GetMethod("AddEvent", BindingFlags.Public | BindingFlags.Instance));
            _isRecording = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), api.GetProperty("IsRecording", Static).GetGetMethod());
            _renderFps = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), api.GetProperty("RenderFps", Static).GetGetMethod());
            _unregister = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), api.GetMethod("Unregister", Static));
            _register = (Func<string, object, bool>)Delegate.CreateDelegate(typeof(Func<string, object, bool>), api.GetMethod("Register", Static));
            return true;
        }

        private static void ResolveInputPause()
        {
            Type pause = FindType("TUFReplay.NativeInputPause");
            MethodInfo request = pause?.GetMethod("Request", new[] { typeof(string) });
            MethodInfo release = pause?.GetMethod("Release", new[] { typeof(string) });
            if (request == null || release == null) return;
            _pauseInput = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), request);
            _resumeInput = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), release);
        }

        // TUFReplay looks these methods up by name. They run on Unity's main thread.
        private sealed class Provider
        {
            // As a recorded run starts (countdown or checkpoint): saved with the run.
            public RunState CaptureRunState() => Overlay.Instance ? Overlay.Instance.CaptureRunState() : null;

            // Before the replayed level starts, in the game and in renders.
            public void OnReplayBegin(bool render)
            {
                ReplayRun.Begin(render);
                ReplayRun.RenderFps = render ? _renderFps() : 0;
                OverlayEditor.Close(); // it freezes the game and blocks its input, which the replay needs
            }

            // The run's RunState (null when it has none): right after OnReplayBegin, and again after a timeline seek.
            public void OnReplayState(RunState state, long ticks) => ReplayRun.SetState(state);

            public void OnReplayEvent(RunClockAnchor anchor, long ticks) => ReplayRun.SetClock(anchor);

            public void OnReplayClock(long ticks) => ReplayRun.Ticks = ticks;

            public void OnReplayEnd() => ReplayRun.End();

            // Renders only: the overlay goes in the video.
            public Canvas GetReplayCanvases()
            {
                ReplayRun.RenderFps = _renderFps(); // final from here on
                return Overlay.Instance ? Overlay.Instance.Canvas : null;
            }
        }
    }
}
