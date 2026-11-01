using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace NowOverlayer
{
    // Auto update from the GitHub releases of LizeDaze/NowOverlayer.
    // UMM loads a cached copy of the dll (NowOverlayer.dll.<n>.cache), so the files in the mod folder can be
    // replaced while the game runs; the new version is used from the next start. Only the mod's own files are
    // written (dll, Info.json, missing fonts): Settings.xml, records and images are never touched.
    public static class Updater
    {
        private const string Api = "https://api.github.com/repos/LizeDaze/NowOverlayer/releases/latest";

        public static string Status = "아직 확인 안 함";
        public static bool Busy;
        public static string Ready; // version downloaded and waiting for a restart (UpdateNotice)
        private static string _staged; // version already downloaded this session

        public static void Check(bool manual)
        {
            if (Busy || !Overlay.Instance) return;
            if (!manual && !Main.Settings.AutoUpdate) return;
            Overlay.Instance.StartCoroutine(Run(manual));
        }

        // leftovers of a swap that could not delete the old file
        public static void CleanUp()
        {
            try
            {
                foreach (string f in Directory.GetFiles(Main.Mod.Path, "*.old", SearchOption.AllDirectories)) File.Delete(f);
            }
            catch (Exception)
            {
                // still in use: next time
            }
        }

        private static IEnumerator Run(bool manual)
        {
            Busy = true;
            Status = "확인 중…";
            JObject rel = null;
            using (UnityWebRequest req = UnityWebRequest.Get(Api))
            {
                req.SetRequestHeader("User-Agent", "NowOverlayer");
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (string.IsNullOrEmpty(req.error))
                {
                    try
                    {
                        rel = JObject.Parse(req.downloadHandler.text);
                    }
                    catch (Exception)
                    {
                        // handled below
                    }
                }
                if (rel == null)
                {
                    Status = "확인 실패 (" + (req.error ?? "응답을 읽지 못함") + ")";
                    Busy = false;
                    yield break;
                }
            }

            string tag = ((string)rel["tag_name"] ?? "").TrimStart('v', 'V');
            Version latest, current;
            if (!TryVersion(tag, out latest) || !TryVersion(Main.Mod.Info.Version, out current))
            {
                Status = "버전을 알 수 없어요: " + tag;
                Busy = false;
                yield break;
            }
            if (latest <= current || _staged == tag)
            {
                Status = _staged == tag ? "v" + tag + " 받아 둠 · 게임을 다시 켜면 적용돼요" : "최신 버전이에요 (v" + Main.Mod.Info.Version + ")";
                Busy = false;
                yield break;
            }

            Status = "v" + tag + " 받는 중…";
            byte[] dll = null, info = null;
            var fonts = new System.Collections.Generic.List<FontFile>();
            foreach (JToken a in (JArray)rel["assets"] ?? new JArray())
            {
                string name = (string)a["name"] ?? "";
                string label = (string)a["label"];
                string url = (string)a["browser_download_url"];
                string file = string.IsNullOrEmpty(label) ? name : label; // asset names lose spaces; the label keeps them
                string ext = Path.GetExtension(file).ToLowerInvariant();
                bool want = name == "NowOverlayer.dll" || name == "Info.json" ||
                            ((ext == ".ttf" || ext == ".otf") && !File.Exists(Path.Combine(Settings.FontFolder, Path.GetFileName(file))));
                if (!want || string.IsNullOrEmpty(url)) continue;

                byte[] data = null;
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.SetRequestHeader("User-Agent", "NowOverlayer");
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    if (string.IsNullOrEmpty(req.error)) data = req.downloadHandler.data;
                }
                if (data == null || data.Length == 0)
                {
                    Status = "받기 실패: " + file;
                    Busy = false;
                    yield break;
                }
                if (name == "NowOverlayer.dll") dll = data;
                else if (name == "Info.json") info = data;
                else fonts.Add(new FontFile(Path.GetFileName(file), data));
            }

            // a dll must be a PE file; without both files nothing is written
            if (dll == null || info == null || dll.Length < 4096 || dll[0] != 'M' || dll[1] != 'Z')
            {
                Status = "v" + tag + " 릴리스에 업데이트 파일이 없어요";
                Busy = false;
                yield break;
            }
            try
            {
                Directory.CreateDirectory(Settings.FontFolder);
                foreach (FontFile f in fonts) Replace(Path.Combine(Settings.FontFolder, f.Name), f.Data);
                Replace(Path.Combine(Main.Mod.Path, "NowOverlayer.dll"), dll);
                Replace(Path.Combine(Main.Mod.Path, "Info.json"), info); // last: it tells UMM the version
                _staged = tag;
                Ready = tag;
                Status = "v" + tag + " 받아 둠 · 게임을 다시 켜면 적용돼요";
                Overlay.Instance?.ShowToast("NowOverlayer v" + tag + " 업데이트 · 다시 켜면 적용");
                Main.Log("Update v" + tag + " downloaded");
            }
            catch (Exception e)
            {
                Status = "업데이트 쓰기 실패: " + e.Message;
                Main.LogError("Update failed", e);
            }
            Busy = false;
            if (manual) Overlay.Instance?.ShowToast(Status);
        }

        private struct FontFile
        {
            public readonly string Name;
            public readonly byte[] Data;

            public FontFile(string name, byte[] data)
            {
                Name = name;
                Data = data;
            }
        }

        private static bool TryVersion(string s, out Version v)
        {
            v = null;
            try
            {
                v = new Version(s);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // write next to the file, then swap; a file Windows keeps open is moved aside (*.old, removed next start)
        private static void Replace(string path, byte[] data)
        {
            string tmp = path + ".new";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    string old = path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
            }
            File.Move(tmp, path);
        }
    }
}
