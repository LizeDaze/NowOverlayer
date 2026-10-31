using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NowOverlayer
{
    // Per-map records for full runs (from the first tile, no checkpoints, no auto): best accuracy, first clear date.
    // Stored as MapStats.json in the mod folder, keyed by the same map hash as Plays.dat.
    public static class Stats
    {
        public class MapStat
        {
            public float BestXAcc = -1f;
            public float BestAcc = -1f;
            public string FirstClear;
            public int Clears;
        }

        private static Dictionary<string, MapStat> _maps = new Dictionary<string, MapStat>();

        private static string FilePath => Path.Combine(Main.Mod.Path, "MapStats.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    _maps = JsonConvert.DeserializeObject<Dictionary<string, MapStat>>(File.ReadAllText(FilePath))
                            ?? new Dictionary<string, MapStat>();
                }
            }
            catch (Exception e)
            {
                Main.LogError("MapStats load failed", e);
                _maps = new Dictionary<string, MapStat>();
            }
        }

        private static void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_maps, Formatting.Indented));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Main.LogError("MapStats save failed", e);
            }
        }

        public static MapStat Get(byte[] hash) => hash == null ? null : Get(Key(hash));

        public static MapStat Get(string key)
        {
            if (key == null) return null;
            _maps.TryGetValue(key, out MapStat s);
            return s;
        }

        public struct ClearResult
        {
            public bool NewBestXAcc, NewBestAcc, FirstClear;
        }

        public static ClearResult RecordClear(byte[] hash, float xacc, float acc)
        {
            var r = new ClearResult();
            if (hash == null) return r;
            string key = Key(hash);
            if (!_maps.TryGetValue(key, out MapStat s))
            {
                s = new MapStat();
                _maps[key] = s;
            }
            if (s.FirstClear == null)
            {
                s.FirstClear = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                r.FirstClear = true;
            }
            s.Clears++;
            if (xacc > s.BestXAcc)
            {
                r.NewBestXAcc = s.BestXAcc >= 0f;
                s.BestXAcc = xacc;
            }
            if (acc > s.BestAcc)
            {
                r.NewBestAcc = s.BestAcc >= 0f;
                s.BestAcc = acc;
            }
            Save();
            return r;
        }

        private static string Key(byte[] hash) => BitConverter.ToString(hash).Replace("-", "");
    }
}
