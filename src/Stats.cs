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

        // What a clear with these numbers beats, compared with the records before it (null = none yet). Saves nothing:
        // a TUFReplay replay compares with the records its run had.
        public static ClearResult Compare(MapStat before, float xacc, float acc) => new ClearResult
        {
            FirstClear = before?.FirstClear == null,
            NewBestXAcc = before != null && before.BestXAcc >= 0f && xacc > before.BestXAcc,
            NewBestAcc = before != null && before.BestAcc >= 0f && acc > before.BestAcc
        };

        public static ClearResult RecordClear(byte[] hash, float xacc, float acc)
        {
            if (hash == null) return new ClearResult();
            string key = Key(hash);
            if (!_maps.TryGetValue(key, out MapStat s))
            {
                s = new MapStat();
                _maps[key] = s;
            }
            ClearResult r = Compare(s, xacc, acc);
            if (s.FirstClear == null) s.FirstClear = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            s.Clears++;
            if (xacc > s.BestXAcc) s.BestXAcc = xacc;
            if (acc > s.BestAcc) s.BestAcc = acc;
            Save();
            return r;
        }

        private static string Key(byte[] hash) => BitConverter.ToString(hash).Replace("-", "");
    }
}
