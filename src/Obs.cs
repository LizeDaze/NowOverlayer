using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace NowOverlayer
{
    // What to do in OBS on a game event.
    public class ObsAction
    {
        public string Scene = "";
        public bool SaveReplay;
        public int Record; // 0 leave as is, 1 start, 2 stop
        public float Delay;

        public bool IsEmpty => string.IsNullOrEmpty(Scene) && !SaveReplay && Record == 0;
    }

    // obs-websocket v5 client (built into OBS 28+). Switches scenes / saves the replay buffer on play, clear and death.
    public static class Obs
    {
        public static string Status = "연결 안 됨";
        public static List<string> Scenes = new List<string>();
        public static bool Connected => _identified && _ws != null && _ws.State == WebSocketState.Open;

        private static ClientWebSocket _ws;
        private static CancellationTokenSource _cts;
        private static volatile bool _identified;
        private static int _reqId;
        private static readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        private static TaskCompletionSource<bool> _ready;
        private static readonly object ChainLock = new object();
        private static Task _chain = Task.CompletedTask;
        private static CancellationTokenSource _skipDelay;
        private static volatile bool _recording; // a recording this mod started is running
        private static readonly Dictionary<string, TaskCompletionSource<JObject>> Waiting = new Dictionary<string, TaskCompletionSource<JObject>>();

        public static void Connect()
        {
            Disconnect();
            Settings s = Main.Settings;
            _cts = new CancellationTokenSource();
            _ready = new TaskCompletionSource<bool>();
            Status = "연결 중…";
            CancellationToken token = _cts.Token;
            Task.Run(() => Run(s.ObsHost, s.ObsPort, (s.ObsPassword ?? "").Trim(), token));
        }

        public static void Disconnect()
        {
            _identified = false;
            try
            {
                _cts?.Cancel();
                _ws?.Abort();
            }
            catch (Exception)
            {
                // already closed
            }
            _ws = null;
            _cts = null;
            Status = "연결 안 됨";
        }

        // Actions run one after another, so a quick retry's "start" never overtakes the death's delayed "stop".
        // A newer action cuts the previous one's delay short.
        public static void Fire(ObsAction a)
        {
            if (!Main.Settings.ObsEnabled || a == null) return;
            if (a.IsEmpty) return;
            var skip = new CancellationTokenSource();
            Interlocked.Exchange(ref _skipDelay, skip)?.Cancel();
            lock (ChainLock)
            {
                _chain = _chain.ContinueWith(_ => RunAction(a, skip.Token)).Unwrap();
            }
        }

        // The attempt ended without a death or clear (restart, quit): close its recording too.
        public static void EndAttempt()
        {
            if (_recording) Fire(new ObsAction { Record = 2 });
        }

        private static async Task RunAction(ObsAction a, CancellationToken skip)
        {
            try
            {
                ClientWebSocket ws = _ws;
                bool pending = ws != null && (ws.State == WebSocketState.Connecting || ws.State == WebSocketState.Open);
                if (!Connected && !pending) Connect();
                Task ready = _ready?.Task ?? Task.CompletedTask;
                if (await Task.WhenAny(ready, Task.Delay(3000)) != ready || !Connected) return;
                if (a.Delay > 0f)
                {
                    try
                    {
                        await Task.Delay((int)(a.Delay * 1000f), skip);
                    }
                    catch (OperationCanceledException)
                    {
                        // a newer action is waiting
                    }
                }
                if (!string.IsNullOrEmpty(a.Scene)) await Request("SetCurrentProgramScene", new JObject { ["sceneName"] = a.Scene });
                if (a.Record == 1)
                {
                    // right after a stop, OBS is still writing the file and answers 500 OutputRunning for a moment
                    for (int i = 0; i < 10; i++)
                    {
                        JObject st = await RequestWait("StartRecord");
                        int code = (int?)st?["code"] ?? 0;
                        if (st == null || (bool)st["result"])
                        {
                            _recording = st != null;
                            Status = st != null ? "연결됨 · 녹화 중" : "OBS가 녹화 요청에 답하지 않아요";
                            break;
                        }
                        if (code != 500) break;
                        await Task.Delay(400);
                    }
                }
                else if (a.Record == 2)
                {
                    JObject st = await RequestWait("StopRecord");
                    if (st != null && (bool)st["result"]) Status = "연결됨 · 녹화 저장됨";
                    _recording = false;
                }
                if (a.SaveReplay) await Request("SaveReplayBuffer", null);
            }
            catch (Exception e)
            {
                Status = "오류: " + e.Message;
            }
        }

        // Sends a request and waits (up to 3 s) for its requestStatus; null when there was no answer.
        private static async Task<JObject> RequestWait(string type)
        {
            string id = "n" + Interlocked.Increment(ref _reqId);
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously); // keep the receive loop free
            lock (Waiting) Waiting[id] = tcs;
            try
            {
                await Send(new JObject { ["op"] = 6, ["d"] = new JObject { ["requestType"] = type, ["requestId"] = id } });
                return await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task ? tcs.Task.Result : null;
            }
            finally
            {
                lock (Waiting) Waiting.Remove(id);
            }
        }

        public static void RefreshScenes()
        {
            if (Connected) _ = Request("GetSceneList", null);
        }

        private static async Task Run(string host, int port, string password, CancellationToken token)
        {
            var ws = new ClientWebSocket();
            _ws = ws;
            try
            {
                await ws.ConnectAsync(new Uri("ws://" + host + ":" + port), token);
                Status = "인증 중…";
                var buffer = new byte[64 * 1024];
                var sb = new StringBuilder();
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    sb.Length = 0;
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            try
                            {
                                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            }
                            catch (Exception)
                            {
                                // OBS may already be gone
                            }
                            throw new Exception(CloseReason((int?)r.CloseStatus ?? 0, r.CloseStatusDescription));
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    } while (!r.EndOfMessage);
                    await Handle(JObject.Parse(sb.ToString()), password);
                }
            }
            catch (Exception e)
            {
                if (_ws == ws)
                {
                    _identified = false;
                    Status = "연결 실패: " + e.Message;
                    _ready?.TrySetResult(false);
                }
            }
        }

        // obs-websocket v5 close codes (WebSocketCloseCode) in plain words.
        private static string CloseReason(int code, string desc)
        {
            switch (code)
            {
                case 4009: return "비밀번호가 틀려요. OBS → 도구 → WebSocket 서버 설정 → 연결 정보 보기에서 비밀번호를 복사해 붙여넣어 주세요";
                case 4010: return "OBS WebSocket 버전이 맞지 않아요 (OBS 28 이상 필요)";
                case 4011: return "OBS가 연결을 끊었어요 (다른 곳에서 세션을 종료)";
                case 1001: return "OBS가 종료됐어요";
                default: return "OBS가 연결을 닫았어요 (코드 " + code + (string.IsNullOrEmpty(desc) ? "" : ", " + desc) + ")";
            }
        }

        private static async Task Handle(JObject msg, string password)
        {
            int op = (int)msg["op"];
            var d = (JObject)msg["d"];
            switch (op)
            {
                case 0: // Hello
                {
                    var identify = new JObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
                    if (d["authentication"] is JObject auth)
                    {
                        string secret = Sha256B64(password + (string)auth["salt"]);
                        identify["authentication"] = Sha256B64(secret + (string)auth["challenge"]);
                    }
                    await Send(new JObject { ["op"] = 1, ["d"] = identify });
                    break;
                }
                case 2: // Identified
                    _identified = true;
                    Status = "연결됨";
                    _ready?.TrySetResult(true);
                    await Request("GetSceneList", null);
                    if (UsesReplay()) await Request("StartReplayBuffer", null); // saving needs it running
                    break;
                case 7: // RequestResponse
                {
                    var st = (JObject)d["requestStatus"];
                    TaskCompletionSource<JObject> waiter;
                    lock (Waiting)
                    {
                        Waiting.TryGetValue((string)d["requestId"] ?? "", out waiter);
                    }
                    if (waiter != null)
                    {
                        waiter.TrySetResult(st);
                        break;
                    }
                    if (st != null && !(bool)st["result"])
                    {
                        string type = (string)d["requestType"];
                        int code = (int?)st["code"] ?? 0;
                        // 500 OutputRunning / 501 OutputNotRunning: already recording, or nothing to stop
                        if ((code == 500 || code == 501) && type != "SaveReplayBuffer") break;
                        Status = code == 501 ? "OBS 요청 실패: 리플레이 버퍼가 꺼져 있어요 (OBS 설정 → 출력 → 리플레이 버퍼)"
                            : "OBS 요청 실패 (" + type + "): " + st["comment"];
                        break;
                    }
                    if ((string)d["requestType"] == "GetSceneList" && d["responseData"]?["scenes"] is JArray scenes)
                    {
                        var names = new List<string>();
                        foreach (JToken sc in scenes) names.Add((string)sc["sceneName"]);
                        names.Reverse(); // OBS lists bottom-up
                        Scenes = names;
                    }
                    break;
                }
            }
        }

        private static bool UsesReplay()
        {
            Settings s = Main.Settings;
            return s.ObsOnStart.SaveReplay || s.ObsOnClear.SaveReplay || s.ObsOnDeath.SaveReplay;
        }

        private static Task Request(string type, JObject data)
        {
            var d = new JObject { ["requestType"] = type, ["requestId"] = "n" + Interlocked.Increment(ref _reqId) };
            if (data != null) d["requestData"] = data;
            return Send(new JObject { ["op"] = 6, ["d"] = d });
        }

        private static async Task Send(JObject msg)
        {
            ClientWebSocket ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(msg.ToString(Newtonsoft.Json.Formatting.None));
            await SendLock.WaitAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                SendLock.Release();
            }
        }

        private static string Sha256B64(string s)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
            }
        }
    }
}
