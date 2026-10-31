using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;

namespace NowOverlayer
{
    // Discord Rich Presence over the local IPC pipe (\\.\pipe\discord-ipc-N).
    // Frames: int32 opcode + int32 length (little-endian) + UTF-8 JSON. Needs the user's own Application ID.
    // Plain kernel32 calls on a synchronous handle: the game's Mono FileStream does not handle overlapped pipe
    // handles (reads came back empty, which looked like Discord hanging up). The reader polls PeekNamedPipe so it
    // only reads what is already there and never holds the handle while a write is waiting.
    public static class Discord
    {
        public static string Status = "꺼짐";

        private static SafeFileHandle _pipe;
        private static readonly object Lock = new object();
        private static string _lastSent;
        private static float _nextUpdate;
        private static long _startUnix;
        private static string _connectedId;
        private static int _failures;
        private static DateTime _retryAt;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle h, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle h, byte[] buffer, uint toWrite, out uint written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekNamedPipe(SafeFileHandle h, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);

        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3;

        private static readonly int Pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        public static void Connect()
        {
            Close();
            string id = (Main.Settings.DiscordAppId ?? "").Trim();
            if (id.Length == 0)
            {
                Status = "Application ID를 넣어 주세요";
                return;
            }
            SafeFileHandle pipe = null;
            for (int i = 0; i < 10; i++)
            {
                SafeFileHandle h = CreateFile(@"\\.\pipe\discord-ipc-" + i, GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (h.IsInvalid) continue;
                pipe = h;
                break;
            }
            if (pipe == null)
            {
                Status = "디스코드가 켜져 있지 않아요";
                return;
            }
            lock (Lock) _pipe = pipe;
            try
            {
                Write(0, new JObject { ["v"] = 1, ["client_id"] = id });
                new Thread(() => ReadLoop(pipe)) { IsBackground = true, Name = "NowOverlayer.Discord" }.Start();
                _connectedId = id;
                _lastSent = null;
                Status = "연결 중…";
            }
            catch (Exception e)
            {
                Status = "연결 실패: " + e.Message;
                Close();
            }
        }

        // Shown under the status line in the UMM window.
        public static string LastActivity = "";
        public static DateTime LastSentAt, LastCheckAt;
        public static int PingMs = -1; // -1 = no answer yet
        private static DateTime _pingSentAt;

        // "새로고침": drop the pipe, reconnect right away (no backoff) and resend the status.
        public static void Refresh()
        {
            Close();
            _failures = 0;
            _retryAt = DateTime.MinValue;
            PingMs = -1;
            if (!Main.Settings.DiscordEnabled)
            {
                Status = "꺼짐 · 위의 [사용]을 켜 주세요";
                return;
            }
            Connect();
            _nextUpdate = 0f; // Tick sends the activity on the next frame
            if (_pipe != null) Check();
        }

        // "상태 확인": PING the open pipe; Discord's PONG proves the connection is alive.
        public static void Check()
        {
            LastCheckAt = DateTime.Now;
            if (_pipe == null)
            {
                Refresh();
                return;
            }
            PingMs = -1;
            _pingSentAt = DateTime.UtcNow;
            var ping = new JObject { ["nonce"] = Guid.NewGuid().ToString() };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Write(3, ping);
                }
                catch (Exception e)
                {
                    Status = "확인 실패: " + e.Message;
                }
            });
        }

        public static void Close()
        {
            lock (Lock)
            {
                try
                {
                    _pipe?.Dispose();
                }
                catch (Exception)
                {
                    // already closed
                }
                _pipe = null;
            }
            _connectedId = null;
            Status = "꺼짐";
        }

        // Discord's READY / ERROR / PONG replies; keeps the pipe drained.
        private static void ReadLoop(SafeFileHandle pipe)
        {
            string why = "디스코드가 연결을 끊었어요";
            var pending = new List<byte>();
            var chunk = new byte[4096];
            try
            {
                while (true)
                {
                    uint avail, got = 0;
                    lock (Lock)
                    {
                        if (pipe != _pipe) return; // replaced or closed on purpose
                        if (!PeekNamedPipe(pipe, IntPtr.Zero, 0, IntPtr.Zero, out avail, IntPtr.Zero)) break; // broken pipe
                        if (avail > 0 && !ReadFile(pipe, chunk, (uint)Math.Min(avail, chunk.Length), out got, IntPtr.Zero)) break;
                    }
                    if (got == 0)
                    {
                        Thread.Sleep(40);
                        continue;
                    }
                    for (int i = 0; i < got; i++) pending.Add(chunk[i]);
                    while (pending.Count >= 8)
                    {
                        byte[] buf = pending.ToArray();
                        int op = BitConverter.ToInt32(buf, 0), len = BitConverter.ToInt32(buf, 4);
                        if (pending.Count < 8 + len) break;
                        string text = Encoding.UTF8.GetString(buf, 8, len);
                        pending.RemoveRange(0, 8 + len);
                        if (!Handle(op, JObject.Parse(text), ref why)) throw new OperationCanceledException();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Discord sent CLOSE; `why` has its message
            }
            catch (Exception e)
            {
                why = "연결 끊김: " + e.Message;
            }
            lock (Lock)
            {
                if (pipe != _pipe) return;
                try
                {
                    _pipe.Dispose();
                }
                catch (Exception)
                {
                    // already closed
                }
                _pipe = null;
            }
            // Tick reconnects; back off 5 s → 10 s → … 60 s so a closed Discord isn't hammered
            _failures++;
            _retryAt = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * _failures));
            Status = why + " · 다시 연결할게요";
        }

        // false = Discord closed the connection (op 2).
        private static bool Handle(int op, JObject msg, ref string why)
        {
            string evt = (string)msg["evt"];
            if (evt == "READY")
            {
                Status = "연결됨 · " + msg["data"]?["user"]?["username"];
                _failures = 0;
            }
            else if (op == 2)
            {
                why = "디스코드 오류: " + (msg["message"] ?? msg["data"]?["message"]);
                return false;
            }
            else if (evt == "ERROR")
            {
                Status = "디스코드 오류: " + msg["data"]?["message"];
                _lastSent = null;
            }
            else if (op == 3)
            {
                ThreadPool.QueueUserWorkItem(_ => Write(4, msg)); // PING → PONG
            }
            else if (op == 4)
            {
                PingMs = Math.Max(0, (int)(DateTime.UtcNow - _pingSentAt).TotalMilliseconds); // our PING answered
            }
            else if ((string)msg["cmd"] == "SET_ACTIVITY")
            {
                LastSentAt = DateTime.Now;
            }
            return true;
        }

        // One WriteFile per frame so Discord never sees half a message.
        private static void Write(int op, JObject payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
            var frame = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            lock (Lock)
            {
                if (_pipe == null) return;
                if (!WriteFile(_pipe, frame, (uint)frame.Length, out uint written, IntPtr.Zero) || written != frame.Length)
                {
                    throw new Exception("보내기 실패 (" + Marshal.GetLastWin32Error() + ")");
                }
            }
        }

        public static void MarkPlayStart() => _startUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Called every frame; sends at most every 5 s (Discord allows 5 updates per 20 s) and only on change.
        public static void Tick(Snapshot playing, float now)
        {
            Settings s = Main.Settings;
            if (!s.DiscordEnabled)
            {
                if (_pipe != null) Close();
                return;
            }
            if (now < _nextUpdate) return;
            _nextUpdate = now + 5f;
            if (_pipe == null || _connectedId != (s.DiscordAppId ?? "").Trim())
            {
                if (_pipe == null && DateTime.UtcNow < _retryAt) return;
                Connect();
                if (_pipe == null)
                {
                    _failures++;
                    _retryAt = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * _failures));
                }
                if (_pipe == null) return;
            }

            var activity = new JObject();
            if (playing != null)
            {
                activity["details"] = Clip(playing.Expand(s.DiscordDetails));
                activity["state"] = Clip(playing.Expand(s.DiscordState));
                if (_startUnix > 0) activity["timestamps"] = new JObject { ["start"] = _startUnix };
            }
            else
            {
                activity["details"] = Clip(s.DiscordIdle);
            }
            if (!string.IsNullOrEmpty(s.DiscordImage))
            {
                activity["assets"] = new JObject { ["large_image"] = s.DiscordImage, ["large_text"] = "A Dance of Fire and Ice" };
            }

            string key = activity.ToString(Newtonsoft.Json.Formatting.None);
            if (key == _lastSent) return;
            _lastSent = key;
            LastActivity = (string)activity["details"] + ((string)activity["state"] is string line2 ? "  ·  " + line2 : "");
            var msg = new JObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JObject { ["pid"] = Pid, ["activity"] = activity },
                ["nonce"] = Guid.NewGuid().ToString()
            };
            // off the main thread so a slow pipe never stalls the game
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Write(1, msg);
                }
                catch (Exception e)
                {
                    Status = "보내기 실패: " + e.Message;
                    _lastSent = null;
                }
            });
        }

        // Discord rejects fields shorter than 2 or longer than 128 characters.
        private static string Clip(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length < 2) s = (s + "  ").Substring(0, 2);
            return s.Length > 128 ? s.Substring(0, 128) : s;
        }
    }
}
