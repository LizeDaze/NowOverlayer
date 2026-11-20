using System;
using UnityEngine;

namespace NowOverlayer
{
    // What the overlay showed as a run started. TUFReplay saves it with each run it records (TUFReplayApi) and hands
    // it back when that run is replayed or rendered to video, so the replay shows the run's own attempts and records,
    // not today's. TUFReplay finds saved runs by these names: keep the class and its field names once runs use them.
    public sealed class RunState
    {
        public int Attempt, FullAttempt;
        public float Best;           // best progress from this start position before the run
        public bool FullRun;         // from the first tile, no checkpoints
        public int Checkpoints;      // checkpoints used so far (scrController.checkpointsUsed)
        public bool NoRecords;       // auto or no-fail: nothing was counted
        public Stats.MapStat Map;    // the map's records before the run, or null
        public DateTime StartedAt;   // when the level started (UTC)
        public RunClockAnchor Clock; // the wall clock as the run started recording
    }

    // The wall clock at a song position. While a run records, a new anchor is added whenever the wall clock and the
    // song drift apart (a pause, the overlay editor), so a replay's clock moves on the same way.
    public sealed class RunClockAnchor
    {
        public DateTime At;      // UTC
        public double SongTime;  // scrConductor.songposition_minusi

        // songposition_minusi runs at the song's pitch
        public DateTime TimeAt(double songTime, float pitch) => At.AddSeconds((songTime - SongTime) / pitch);

        public static float Pitch(scrConductor cond) => cond.song && cond.song.pitch > 0f ? cond.song.pitch : 1f;
    }

    // A TUFReplay replay of a recorded run, in the game or rendered to video, as the overlay sees it.
    // TUFReplayApi fills it in; the overlay reads it.
    public static class ReplayRun
    {
        public static bool Active { get; private set; }     // between OnReplayBegin and OnReplayEnd
        public static bool Rendering { get; private set; }  // rendered to video by OrbitRender
        public static RunState State { get; private set; }  // null: the run was saved without one
        public static long Ticks { get; set; }              // the replay clock, 100 ns, video time in a render
        public static int RenderFps { get; set; }

        // Counts replays, so the overlay can tell a replay's level start from a timeline seek, which restarts the level.
        public static int Session { get; private set; }

        private static RunClockAnchor _clock;
        private static double _clockSong = double.NaN;
        private static DateTime _clockWall;
        private static long _clockTicks;

        public static void Begin(bool render)
        {
            Active = true;
            Rendering = render;
            Session++;
            State = null;
            Ticks = 0;
            SetClock(null);
        }

        // The run's start value: right after Begin, and again after each timeline seek, before the events up to there.
        public static void SetState(RunState state)
        {
            State = state;
            SetClock(state?.Clock);
        }

        public static void SetClock(RunClockAnchor anchor)
        {
            _clock = anchor;
            _clockSong = double.NaN;
        }

        public static void End()
        {
            Active = false;
            Rendering = false;
            State = null;
            RenderFps = 0;
            SetClock(null);
        }

        // The wall clock of the run at this moment of the replay, or null when the run saved none. It follows the song,
        // so it stays right after a seek; while the song stands still (the fail screen) the replay clock carries it on.
        public static DateTime? WallClock()
        {
            scrConductor cond = scrConductor.instance;
            if (_clock == null || !cond) return null;
            double song = cond.songposition_minusi;
            if (song != _clockSong)
            {
                _clockSong = song;
                _clockWall = _clock.TimeAt(song, RunClockAnchor.Pitch(cond));
                _clockTicks = Ticks;
            }
            return _clockWall.AddTicks(Math.Max(0L, Ticks - _clockTicks));
        }
    }

    // The clock the overlay's animations follow: real time, or the replay's clock while TUFReplay replays a run. A render
    // runs the game slower than the video (and takes extra frames for capture), so real time would play them too fast.
    public static class PlayClock
    {
        private static int _frame = -1;
        private static long _lastTicks = -1;
        private static double _now;
        private static float _delta;

        public static float Now
        {
            get
            {
                Advance();
                return (float)_now;
            }
        }

        public static float Delta
        {
            get
            {
                Advance();
                return _delta;
            }
        }

        private static void Advance()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            if (ReplayRun.Active)
            {
                long t = ReplayRun.Ticks;
                _delta = _lastTicks < 0 || t < _lastTicks ? 0f : (t - _lastTicks) / (float)TimeSpan.TicksPerSecond;
                _lastTicks = t;
            }
            else
            {
                _lastTicks = -1;
                _delta = Time.unscaledDeltaTime;
            }
            _now += _delta;
        }
    }

    // While TUFReplay records a run: adds a clock anchor when the wall clock and the song drift apart by a second or
    // more, which happens when the game is paused (the song stops, the wall clock goes on).
    public static class RunClockRecorder
    {
        private const double MaxDrift = 1.0;
        private static RunClockAnchor _anchor;
        private static double _song;

        public static void Start(RunClockAnchor anchor)
        {
            _anchor = anchor;
            _song = anchor.SongTime;
        }

        public static void Stop() => _anchor = null;

        public static void Tick(scrConductor cond)
        {
            if (_anchor == null || !cond || !TUFReplayApi.IsRecording()) return;
            double song = cond.songposition_minusi;
            if (song == _song) return; // the song stands still: nothing to compare yet
            _song = song;
            DateTime now = DateTime.UtcNow;
            if (Math.Abs((now - _anchor.TimeAt(song, RunClockAnchor.Pitch(cond))).TotalSeconds) < MaxDrift) return;
            _anchor = new RunClockAnchor { At = now, SongTime = song };
            TUFReplayApi.AddEvent(_anchor);
        }
    }
}
