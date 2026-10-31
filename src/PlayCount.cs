using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ADOFAI;

namespace NowOverlayer
{
    // Same file format and map hash as JipperResourcePack's Plays.dat, so its counts carry over.
    // Ints and floats are big-endian (JALib StreamTool).
    public static class PlayCount
    {
        public class PlayData
        {
            public int TotalAttempts;
            public readonly Dictionary<(float, float), int> Attempts = new Dictionary<(float, float), int>();
            public readonly Dictionary<(float, float), float> Best = new Dictionary<(float, float), float>();

            public int GetAttempts(float progress, float multiplier) =>
                Attempts.TryGetValue((progress, multiplier), out int v) ? v : 0;

            private int _all = -1;

            // cached: the overlay reads this many times a second
            public int GetAllAttempts() => _all >= 0 ? _all : (_all = Attempts.Values.Sum());

            public void Changed() => _all = -1;
        }

        private static Dictionary<string, PlayData> _datas = new Dictionary<string, PlayData>();
        private static Dictionary<string, byte[]> _rawKeys = new Dictionary<string, byte[]>();

        public static string Status = "-";

        private static string FilePath => Path.Combine(Main.Mod.Path, "Plays.dat");

        private static string JipperPath =>
            Path.Combine(Path.GetDirectoryName(Main.Mod.Path.TrimEnd('\\', '/')), "JipperResourcePack", "Plays.dat");

        public static void Load()
        {
            _datas = new Dictionary<string, PlayData>();
            _rawKeys = new Dictionary<string, byte[]>();
            foreach (string path in new[] { FilePath, FilePath + ".bak", JipperPath, JipperPath + ".bak" })
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                try
                {
                    LoadFile(path);
                    Status = path == FilePath ? "NowOverlayer\\Plays.dat (" + _datas.Count + "개 맵)"
                        : path + " 에서 가져옴 (" + _datas.Count + "개 맵)";
                    Main.Log("Plays loaded from " + path + ", maps " + _datas.Count);
                    return;
                }
                catch (Exception e)
                {
                    Main.LogError("Plays load failed: " + path, e);
                    _datas.Clear();
                    _rawKeys.Clear();
                }
            }
            Status = "새 파일";
        }

        private static void LoadFile(string path)
        {
            using (var s = new MemoryStream(File.ReadAllBytes(path)))
            {
                int version = s.ReadByte();
                int maps = ReadInt(s);
                for (int i = 0; i < maps; i++)
                {
                    byte[] key = ReadBytes(s, 16);
                    var data = new PlayData { TotalAttempts = ReadInt(s) };
                    int n = ReadInt(s);
                    for (int j = 0; j < n; j++)
                    {
                        if (version == 0) s.ReadByte();
                        float a = ReadFloat(s), b = ReadFloat(s);
                        data.Attempts[(a, b)] = ReadInt(s);
                    }
                    n = ReadInt(s);
                    for (int j = 0; j < n; j++)
                    {
                        if (version == 0) s.ReadByte();
                        float a = ReadFloat(s), b = ReadFloat(s);
                        data.Best[(a, b)] = ReadFloat(s);
                    }
                    string hex = Hex(key);
                    _datas[hex] = data;
                    _rawKeys[hex] = key;
                }
            }
        }

        public static void Save()
        {
            try
            {
                var ms = new MemoryStream();
                ms.WriteByte(1);
                WriteInt(ms, _datas.Count);
                foreach (var kv in _datas)
                {
                    ms.Write(_rawKeys[kv.Key], 0, 16);
                    PlayData d = kv.Value;
                    WriteInt(ms, d.TotalAttempts);
                    WriteInt(ms, d.Attempts.Count);
                    foreach (var a in d.Attempts)
                    {
                        WriteFloat(ms, a.Key.Item1);
                        WriteFloat(ms, a.Key.Item2);
                        WriteInt(ms, a.Value);
                    }
                    WriteInt(ms, d.Best.Count);
                    foreach (var b in d.Best)
                    {
                        WriteFloat(ms, b.Key.Item1);
                        WriteFloat(ms, b.Key.Item2);
                        WriteFloat(ms, b.Value);
                    }
                }
                string path = FilePath;
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Main.LogError("Plays save failed", e);
            }
        }

        public static PlayData GetData(byte[] hash) => GetData(hash, Hex(hash));

        public static PlayData GetData(byte[] hash, string hex)
        {
            if (!_datas.TryGetValue(hex, out PlayData d))
            {
                d = new PlayData();
                _datas[hex] = d;
                _rawKeys[hex] = hash;
            }
            return d;
        }

        public static void AddAttempt(byte[] hash, float progress, float multiplier)
        {
            PlayData d = GetData(hash);
            d.Attempts.TryGetValue((progress, multiplier), out int v);
            d.Attempts[(progress, multiplier)] = v + 1;
            d.Changed();
            d.TotalAttempts++;
            Save();
        }

        public static void RemoveAttempt(byte[] hash, float progress, float multiplier)
        {
            PlayData d = GetData(hash);
            if (!d.Attempts.TryGetValue((progress, multiplier), out int v))
            {
                return;
            }
            if (v == 1) d.Attempts.Remove((progress, multiplier));
            else d.Attempts[(progress, multiplier)] = v - 1;
            d.Changed();
            d.TotalAttempts--;
            Save();
        }

        public static void SetBest(byte[] hash, float start, float cur, float multiplier)
        {
            PlayData d = GetData(hash);
            if (!d.Best.TryGetValue((start, multiplier), out float best))
            {
                d.Best[(start, multiplier)] = cur;
            }
            else if (best < cur)
            {
                d.Best[(start, multiplier)] = cur;
                Save();
            }
        }

        public static byte[] GetMapHash()
        {
            using (MD5 md5 = MD5.Create())
            {
                return md5.ComputeHash(ADOBase.isOfficialLevel ? Encoding.UTF8.GetBytes(ADOBase.currentLevel) : HashSource());
            }
        }

        private static byte[] HashSource()
        {
            var ms = new MemoryStream();
            scrLevelMaker lm = ADOBase.lm;
            if (lm.isOldLevel)
            {
                WriteUtf(ms, lm.leveldata);
            }
            else if (lm.floorAngles == null)
            {
                WriteInt(ms, -1);
            }
            else
            {
                WriteInt(ms, lm.floorAngles.Length);
                foreach (float f in lm.floorAngles) WriteFloat(ms, f);
            }
            foreach (LevelEvent e in ADOBase.customLevel.events)
            {
                switch (e.eventType)
                {
                    case LevelEventType.SetSpeed:
                        var speedType = (SpeedType)e["speedType"];
                        WriteInt(ms, e.floor);
                        ms.WriteByte(0);
                        ms.WriteByte((byte)speedType);
                        WriteFloat(ms, (float)e[speedType == SpeedType.Bpm ? "beatsPerMinute" : "bpmMultiplier"]);
                        break;
                    case LevelEventType.Twirl:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(1);
                        break;
                    case LevelEventType.Hold:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(2);
                        WriteInt(ms, (int)e["duration"]);
                        break;
                    case LevelEventType.MultiPlanet:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(3);
                        ms.WriteByte((byte)(PlanetCount)e["planets"]);
                        break;
                    case LevelEventType.Pause:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(4);
                        WriteFloat(ms, (float)e["duration"]);
                        break;
                    case LevelEventType.AutoPlayTiles:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(5);
                        ms.WriteByte((bool)e["enabled"] ? (byte)1 : (byte)0);
                        break;
                    case LevelEventType.ScaleMargin:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(6);
                        WriteFloat(ms, (float)e["scale"]);
                        break;
                    case LevelEventType.Multitap:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(7);
                        WriteFloat(ms, (float)e["taps"]);
                        break;
                    case LevelEventType.KillPlayer:
                        WriteInt(ms, e.floor);
                        ms.WriteByte(8);
                        break;
                }
            }
            return ms.ToArray();
        }

        public static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "");

        private static void WriteInt(Stream s, int v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static void WriteFloat(Stream s, float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            Array.Reverse(b);
            s.Write(b, 0, 4);
        }

        private static void WriteUtf(Stream s, string v)
        {
            byte[] b = Encoding.UTF8.GetBytes(v);
            WriteInt(s, b.Length);
            s.Write(b, 0, b.Length);
        }

        private static byte[] ReadBytes(Stream s, int n)
        {
            byte[] b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) throw new EndOfStreamException();
                got += r;
            }
            return b;
        }

        private static int ReadInt(Stream s)
        {
            byte[] b = ReadBytes(s, 4);
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }

        private static float ReadFloat(Stream s)
        {
            byte[] b = ReadBytes(s, 4);
            Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }
    }
}
