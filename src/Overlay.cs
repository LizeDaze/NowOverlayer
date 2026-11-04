using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace NowOverlayer
{
    // Layout numbers are for a 1920x1080 reference canvas (scaled to the real screen by CanvasScaler).
    public class Overlay : MonoBehaviour
    {
        public static Overlay Instance;

        private const float RefreshInterval = 0.1f;

        public TMP_FontAsset Font;
        public Material ShadowMaterial;
        public Canvas Canvas;
        public RectTransform CanvasRect;
        public readonly List<ElementView> Views = new List<ElementView>();

        private readonly Snapshot _snap = new Snapshot();
        private readonly Snapshot _sample = Snapshot.Sample();
        private float _refreshTimer;
        private bool _dirty;

        private bool _shown;
        private byte[] _hash;
        private float _startProgress = -1f, _lastSavedStart = -1f, _multiplier, _storedBest;
        private bool _autoOnce, _isDeath, _songPlaying;
        private float _mapTotal = -1f;
        private int _combo;
        private double _timingSum;
        private int _timingCount;
        private float _lastTiming;

        private int _fpsFrames;
        private float _fpsTime;
        private int _fps;

        public static void Create()
        {
            if (Instance) return;
            var go = new GameObject("NowOverlayer");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<Overlay>();
            go.AddComponent<OverlayEditor>();
            go.AddComponent<UpdateNotice>();
            Instance.Build();
        }

        public static void DestroyInstance()
        {
            if (!Instance) return;
            OverlayEditor.Close();
            Instance.Hide();
            Destroy(Instance.gameObject);
            Instance = null;
        }

        private void Build()
        {
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasRect = (RectTransform)transform;

            BuildBackdrop();
            _elements = ElementView.NewRect("Elements", transform);
            _elements.anchorMin = Vector2.zero;
            _elements.anchorMax = Vector2.one;
            _elements.pivot = Vector2.zero; // the editor's fitted view scales it from the bottom-left corner
            _elements.offsetMin = _elements.offsetMax = Vector2.zero;

            LoadFont();
            Rebuild();
            BuildChrome();
            Canvas.enabled = false;
        }

        private RectTransform _elements;
        private Toast _toast;
        private ResultCard _result;

        // Result card and toast sit above the elements; rebuilt when the font changes.
        private void BuildChrome()
        {
            if (_toast != null) Destroy(transform.Find("Toast").gameObject);
            if (_result != null) Destroy(transform.Find("Result").gameObject);
            _result = new ResultCard(transform, this);
            _toast = new Toast(transform, this);
        }

        // ---- editor "fit" view: like a Photoshop document between the panels ----
        // A dark backdrop covers the live game, a snapshot of the game screen sits in the board rect, and the
        // elements are scaled into that same rect, so every item (even at the screen edges) is reachable.

        private RectTransform _backdrop, _shotRt;
        private RawImage _shot;
        public float FitScale { get; private set; } = 1f;

        private void BuildBackdrop()
        {
            _backdrop = ElementView.NewRect("EditorBackdrop", transform);
            _backdrop.anchorMin = Vector2.zero;
            _backdrop.anchorMax = Vector2.one;
            _backdrop.offsetMin = _backdrop.offsetMax = Vector2.zero;
            Image dark = _backdrop.gameObject.AddComponent<Image>();
            dark.color = new Color(0.075f, 0.075f, 0.09f, 1f);
            dark.raycastTarget = false;
            _shotRt = ElementView.NewRect("Snapshot", _backdrop);
            _shotRt.anchorMin = _shotRt.anchorMax = _shotRt.pivot = Vector2.zero;
            _shot = _shotRt.gameObject.AddComponent<RawImage>();
            _shot.raycastTarget = false;
            _backdrop.gameObject.SetActive(false);
        }

        // board = GUI rect (top-left origin, screen px); null = back to the normal full-screen overlay.
        public void SetFit(Rect? board, Texture snapshot)
        {
            float k = Canvas.scaleFactor;
            if (board == null && snapshot)
            {
                // full-screen editing: the still covers the live game edge to edge
                board = new Rect(0f, 0f, Screen.width, Screen.height);
            }
            if (board == null)
            {
                FitScale = 1f;
                _elements.localScale = Vector3.one;
                _elements.anchoredPosition = Vector2.zero;
                _backdrop.gameObject.SetActive(false);
                return;
            }
            Rect b = board.Value;
            FitScale = b.width / Mathf.Max(1f, Screen.width);
            var pos = new Vector2(b.x / k, (Screen.height - b.yMax) / k);
            _elements.localScale = new Vector3(FitScale, FitScale, 1f);
            _elements.anchoredPosition = pos;
            _backdrop.gameObject.SetActive(true);
            _shotRt.anchoredPosition = pos;
            _shotRt.sizeDelta = new Vector2(b.width / k, b.height / k);
            _shot.texture = snapshot;
            _shot.color = snapshot ? Color.white : new Color(0.12f, 0.12f, 0.15f, 1f);
        }

        // Hides the overlay for one frame and grabs the game screen (for the editor's board background).
        public static bool HidingForSnapshot { get; private set; }

        public System.Collections.IEnumerator GrabScreen(Action<Texture2D> done)
        {
            // Update keeps the canvas off while this is set; wait a whole frame so nothing of ours is on screen
            HidingForSnapshot = true;
            Canvas.enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try
            {
                tex = SnapshotMethod?.Invoke(null, null) as Texture2D;
            }
            catch (Exception e)
            {
                Main.LogError("Editor snapshot failed", e);
            }
            HidingForSnapshot = false;
            done(tex);
        }

        private static readonly System.Reflection.MethodInfo SnapshotMethod =
            Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")?.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes);

        public void ShowToast(string msg) => _toast?.Show(msg);

        public void CloseResult() => _result?.Hide();

        // Keeps the overlay drawn for a moment with sample values (font / effect previews).
        public void ShowPreview(float seconds)
        {
            _previewUntil = Mathf.Max(_previewUntil, Time.unscaledTime + seconds);
            _dirty = true;
        }

        // Recreates every element from Settings.Elements.
        public void Rebuild()
        {
            foreach (ElementView v in Views)
            {
                v.Dispose();
                Destroy(v.Root.gameObject);
            }
            Views.Clear();
            foreach (ElementConfig c in Main.Settings.Elements) Views.Add(CreateView(c));
            ApplySettings();
        }

        private ElementView CreateView(ElementConfig c)
        {
            switch (c.Type)
            {
                case ElementType.ProgressBar: return new BarView(c, _elements);
                case ElementType.Combo: return new ComboView(c, _elements, this);
                case ElementType.Web: return new WebView(c, _elements, this);
                case ElementType.Image: return new ImageView(c, _elements, this);
                case ElementType.Webcam: return new WebcamView(c, _elements);
                default: return new TextView(c, _elements, this);
            }
        }

        public ElementView AddElement(ElementConfig c)
        {
            Main.Settings.Elements.Add(c);
            ElementView v = CreateView(c);
            Views.Add(v);
            v.ApplyLayout(Main.Settings.Size);
            _dirty = true;
            return v;
        }

        public void RemoveElement(ElementView v)
        {
            Main.Settings.Elements.Remove(v.Cfg);
            Views.Remove(v);
            v.Dispose();
            Destroy(v.Root.gameObject);
        }

        private void OnDestroy()
        {
            foreach (ElementView v in Views) v.Dispose();
        }

        private void OnApplicationQuit() => OnDestroy();

        public bool IsVisible => Canvas && Canvas.enabled;
        public bool Playing => _shown; // a level is being played (between Show and Hide)

        // ---- entry animation ----

        private float _introStart = -1f;
        private float _previewUntil = -1f;

        public void PlayIntro()
        {
            _introStart = Time.unscaledTime;
            _dirty = true;
        }

        // Plays the entry animation with sample values even when no level is running.
        public void PreviewIntro()
        {
            Settings s = Main.Settings;
            _previewUntil = Time.unscaledTime + s.AnimDuration + s.AnimStagger * Views.Count + 1.2f;
            PlayIntro();
        }

        private void TickIntro()
        {
            if (_introStart < 0f) return;
            Settings s = Main.Settings;
            float elapsed = Time.unscaledTime - _introStart;
            float dur = Mathf.Max(s.AnimDuration, 0.05f);
            bool done = true;
            int order = 0;
            for (int i = 0; i < Views.Count; i++)
            {
                ElementView v = Views[i];
                if (!v.Cfg.Visible) continue;
                var type = v.Cfg.Anim >= 0 ? (AnimType)v.Cfg.Anim : s.Anim;
                float t = (elapsed - s.AnimStagger * order++) / dur;
                if (t < 1f && type != AnimType.None) done = false;
                Pose p = type == AnimType.None
                    ? Pose.Identity
                    : Anim.Eval(type, Mathf.Max(t, 0f), i + 1, Anim.EdgeDir(v.Cfg.AnchorX, v.Cfg.AnchorY));
                if (t < 0f) p.Alpha = 0f;
                v.ApplyPose(p);
            }
            if (done) _introStart = -1f;
        }

        public void ApplySettings()
        {
            foreach (ElementView v in Views) v.ApplyLayout(Main.Settings.Size);
            _dirty = true;
        }

        public void MarkDirty() => _dirty = true;

        public static string BuiltInFont => Path.Combine(Main.Mod.Path, "Font", "Maplestory Bold.ttf");

        private static TMP_FontAsset CreateFont(string path)
        {
            // a bare file name means the mod's Font folder (layouts shared by exe export use that)
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path)) path = Path.Combine(Path.Combine(Main.Mod.Path, "Font"), path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                return TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            }
            catch (Exception e)
            {
                Main.LogError("Font load failed: " + path, e);
                return null;
            }
        }

        // Switches every overlay text to the font file at path ("" = built-in Maplestory). Returns false if it can't load.
        public bool SetFont(string path)
        {
            TMP_FontAsset f = CreateFont(string.IsNullOrEmpty(path) ? BuiltInFont : path);
            if (!f) return false;
            Main.Settings.FontPath = path ?? "";
            Font = f;
            SetupFont();
            Rebuild();
            BuildChrome();
            return true;
        }

        private void LoadFont()
        {
            Font = CreateFont(Main.Settings.FontPath) ?? CreateFont(BuiltInFont);
            if (!Font)
            {
                Main.Log("Font missing, using TMP default: " + BuiltInFont);
                Font = TMP_Settings.defaultFontAsset;
                return;
            }
            SetupFont();
        }

        private void SetupFont()
        {
            AddFallbacks(Font);
            ShadowMaterial = MakeShadow(Font);
        }

        // ---- per-item fonts (ElementConfig.Font; "" = the overlay font) ----

        private readonly Dictionary<string, KeyValuePair<TMP_FontAsset, Material>> _itemFonts =
            new Dictionary<string, KeyValuePair<TMP_FontAsset, Material>>(StringComparer.OrdinalIgnoreCase);

        public TMP_FontAsset FontFor(string path) => ItemFont(path).Key;

        public Material MaterialFor(string path) => ItemFont(path).Value;

        // Each font file is loaded once and shared by every item that uses it; a file that fails falls back to the overlay font.
        private KeyValuePair<TMP_FontAsset, Material> ItemFont(string path)
        {
            if (string.IsNullOrEmpty(path)) return new KeyValuePair<TMP_FontAsset, Material>(Font, ShadowMaterial);
            if (!_itemFonts.TryGetValue(path, out var entry))
            {
                TMP_FontAsset f = CreateFont(path);
                if (f)
                {
                    AddFallbacks(f);
                    entry = new KeyValuePair<TMP_FontAsset, Material>(f, MakeShadow(f));
                }
                else
                {
                    entry = new KeyValuePair<TMP_FontAsset, Material>(null, null);
                }
                _itemFonts[path] = entry;
            }
            return entry.Key ? entry : new KeyValuePair<TMP_FontAsset, Material>(Font, ShadowMaterial);
        }

        // False when the file at path could not be loaded (the item then shows the overlay font).
        public bool FontLoads(string path) => string.IsNullOrEmpty(path) || (ItemFont(path).Key && _itemFonts[path].Key);

        private static void AddFallbacks(TMP_FontAsset font)
        {
            try
            {
                font.fallbackFontAssetTable = new List<TMP_FontAsset>
                {
                    RDConstants.data.latinFontTMPro,
                    RDConstants.data.koreanFontTMPro,
                    RDConstants.data.japaneseFontTMPro,
                    RDConstants.data.chineseFontTMPro
                };
            }
            catch (Exception e)
            {
                Main.LogError("Fallback fonts failed", e);
            }
        }

        // Black outline + soft drop shadow, the same look on every font.
        private static Material MakeShadow(TMP_FontAsset font)
        {
            Material ShadowMaterial;
            try
            {
                ShadowMaterial = new Material(font.material);
                var mobileSdf = typeof(ShaderUtilities).GetProperty("ShaderRef_MobileSDF")?.GetValue(null) as Shader;
                if (mobileSdf) ShadowMaterial.shader = mobileSdf;
                ShadowMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                ShadowMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                ShadowMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.01f);
                ShadowMaterial.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                ShadowMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.5f));
                ShadowMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 1f);
                ShadowMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -1f);
                ShadowMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0f);
                ShadowMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
            }
            catch (Exception e)
            {
                Main.LogError("Shadow material failed", e);
                ShadowMaterial = null;
            }
            return ShadowMaterial;
        }

        // ---- lifecycle (same rules as JipperResourcePack's Overlay) ----

        public void Show(int floor)
        {
            try
            {
                SaveBest(Progress());
                scrController ctrl = scrController.instance;
                scrConductor cond = scrConductor.instance;

                byte[] hash = null;
                try
                {
                    hash = PlayCount.GetMapHash();
                }
                catch (Exception e)
                {
                    Main.LogError("Map hash failed", e);
                }
                if (hash == null || _hash == null || !SameBytes(hash, _hash))
                {
                    _mapTotal = -1f;
                }
                _hash = hash;
                _hashKey = hash != null ? PlayCount.Hex(hash) : null;
                var level = ADOBase.customLevel ? ADOBase.customLevel.levelData : null;
                _title = level != null ? Strip(level.song) : "";
                _artist = level != null ? Strip(level.artist) : "";

                _autoOnce = RDC.auto || ctrl.noFail;
                _startProgress = _lastSavedStart = floor / (float)ADOBase.lm.listFloors.Count;
                _multiplier = (float)(cond.song.pitch * ctrl.playerOne.planetarySystem.speed);
                PlayCount.PlayData data = _hash != null ? PlayCount.GetData(_hash) : null;
                _storedBest = data != null && data.Best.TryGetValue((_startProgress, _multiplier), out float b) ? b : 0f;
                if (!_autoOnce && _hash != null)
                {
                    PlayCount.AddAttempt(_hash, _startProgress, _multiplier);
                }

                _isDeath = false;
                _songPlaying = false;
                _combo = 0;
                _maxCombo = 0;
                _playStart = Time.unscaledTime;
                _fullRun = _startProgress == 0f && scrController.checkpointsUsed == 0 && !GCS.practiceMode;
                _result.Hide();
                Obs.Fire(Main.Settings.ObsOnStart);
                Discord.MarkPlayStart();
                _timingSum = 0;
                _timingCount = 0;
                _shown = true;
                _dirty = true;
                foreach (ElementView v in Views) v.OnLevelStart();
                PlayIntro();
            }
            catch (Exception e)
            {
                Main.LogError("Show failed", e);
            }
        }

        public void Hide()
        {
            if (!_shown) return;
            _shown = false;
            _result?.Hide();
            Obs.EndAttempt();
            try
            {
                float p = Progress();
                SaveBest(p);
                if (_hash != null && !_autoOnce && _startProgress == p)
                {
                    PlayCount.RemoveAttempt(_hash, _startProgress, _multiplier);
                }
            }
            catch (Exception e)
            {
                Main.LogError("Hide failed", e);
            }
            _startProgress = -1f;
        }

        public void Death()
        {
            if (!_shown || _isDeath) return;
            _isDeath = true;
            ResultCard.Data d = ResultData(false, Progress());
            SaveBest(Progress());
            if (Main.Settings.ResultOnDeath && d != null) _result.Show(d, Main.Settings.ResultSeconds);
            if (d != null) Capture.Schedule(false, d.NewBestProgress, d.Title);
            Obs.Fire(Main.Settings.ObsOnDeath);
        }

        // The clear card stands in for the game's result text (see Patches.GameResultText / GameCongrats).
        public bool ReplacesGameResult => _shown && Main.Settings.ResultOnClear && Main.Settings.ReplaceGameResult;

        public void Clear()
        {
            if (!_shown) return;
            ResultCard.Data d = ResultData(true, 1f);
            SaveBest(1f);
            if (d != null && _fullRun && !_autoOnce)
            {
                Stats.ClearResult r = Stats.RecordClear(_hash, d.XAcc, d.Acc);
                d.FirstClear = r.FirstClear;
                d.NewBestXAcc = r.NewBestXAcc;
                d.MapBestXAcc = Stats.Get(_hash)?.BestXAcc ?? -1f;
            }
            if (Main.Settings.ResultOnClear && d != null) _result.Show(d, Main.Settings.ReplaceGameResult ? 0f : Main.Settings.ResultSeconds);
            if (d != null) Capture.Schedule(true, d.FirstClear || d.NewBestXAcc || d.NewBestProgress, d.Title);
            Obs.Fire(Main.Settings.ObsOnClear);
        }

        private float _discordGather;
        private string _hashKey, _title = "", _artist = "";
        private int _maxCombo;
        private float _playStart;
        private bool _fullRun;

        private ResultCard.Data ResultData(bool cleared, float progress)
        {
            if (!Gather()) return null;
            Snapshot s = _snap;
            return new ResultCard.Data
            {
                Cleared = cleared,
                Title = (s.Artist.Length > 0 ? s.Artist + " - " : "") + s.Title,
                Progress = progress,
                XAcc = s.XAcc,
                Acc = s.Acc,
                Best = Mathf.Max(_storedBest, progress),
                NewBestProgress = !_autoOnce && progress > _storedBest && _storedBest > 0f,
                Time = Time.unscaledTime - _playStart,
                MaxCombo = _maxCombo,
                Hits = (int[])s.Hits.Clone(),
                FullRun = _fullRun && !_autoOnce,
                MapBestXAcc = s.MapBestXAcc
            };
        }

        public void PreviewResult(bool cleared)
        {
            Snapshot s = _sample;
            ShowPreview(Main.Settings.ResultSeconds > 0f ? Main.Settings.ResultSeconds : 5f);
            _result.Show(new ResultCard.Data
            {
                Cleared = cleared, Title = s.Artist + " - " + s.Title, Progress = cleared ? 1f : 0.4211f,
                XAcc = 0.9934f, Acc = 1.0012f, Best = 0.4211f, NewBestProgress = !cleared, NewBestXAcc = cleared,
                FirstClear = cleared, FullRun = cleared, MapBestXAcc = 0.9934f, Time = 127f, MaxCombo = 412,
                Hits = (int[])s.Hits.Clone()
            }, Main.Settings.ResultSeconds > 0f ? Main.Settings.ResultSeconds : 5f);
        }

        public void PreviewMilestone()
        {
            ShowPreview(2f);
            foreach (ElementView v in Views)
            {
                if (v is ComboView cv) cv.PlayMilestone(100);
            }
        }

        private void SaveBest(float progress)
        {
            if (_lastSavedStart == -1f) return;
            if (!_autoOnce && _hash != null)
            {
                PlayCount.SetBest(_hash, _lastSavedStart, progress, _multiplier);
            }
            _lastSavedStart = -1f;
        }

        public void OnHit(HitMargin hit)
        {
            if (!_shown) return;
            int m = (int)hit;
            if (Judge.IsNeutral(m)) return;
            if (Judge.IsPerfect(m))
            {
                _combo++;
                if (_combo > _maxCombo) _maxCombo = _combo;
            }
            else
            {
                _combo = 0;
            }
            _dirty = true;
        }

        public void OnTiming(float ms)
        {
            if (!_shown) return;
            _lastTiming = ms;
            _timingSum += ms;
            _timingCount++;
            _dirty = true;
        }

        private static float Progress()
        {
            scrController c = scrController.instance;
            return c ? c.percentComplete : 0f;
        }

        // ---- per-frame: refresh on hits and every 100ms ----

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fpsFrames++;
            _fpsTime += dt;
            if (_fpsTime >= 0.5f)
            {
                _fps = Mathf.RoundToInt(_fpsFrames / _fpsTime);
                _fpsFrames = 0;
                _fpsTime = 0f;
            }

            Hotkeys.Check();
            Capture.Tick();
            if (Time.unscaledTime >= _songCheckAt)
            {
                _songCheckAt = Time.unscaledTime + 0.5f; // the editor adds / hides items without ApplySettings
                RefreshShowsSong();
            }
            if (_shown && Main.Settings.DiscordEnabled && Time.unscaledTime >= _discordGather)
            {
                _discordGather = Time.unscaledTime + 1f;
                Gather();
            }
            Discord.Tick(_shown ? _snap : null, Time.unscaledTime);
            _toast?.Tick();
            _result?.Tick();

            // the pause menu (Esc) sets scrController.paused; in the level editor it is also set while editing,
            // but the overlay is not shown there anyway (_shown is false until Play)
            bool paused = _shown && scrController.instance && scrController.instance.paused;
            bool content = (_shown && !paused && !Main.Settings.OverlayHidden) || OverlayEditor.Active || Time.unscaledTime < _previewUntil;
            bool visible = (content || (_toast != null && _toast.Active)) && !HidingForSnapshot;
            if (Canvas.enabled != visible) Canvas.enabled = visible;
            if (_elements.gameObject.activeSelf != content) _elements.gameObject.SetActive(content);
            if (!content) return;

            _refreshTimer -= dt;
            if (_dirty || _refreshTimer <= 0f)
            {
                _refreshTimer = RefreshInterval;
                _dirty = false;
                Snapshot s;
                if (_shown && Gather())
                {
                    s = _snap;
                }
                else
                {
                    s = _sample;
                    s.Fps = _fps;
                }
                foreach (ElementView v in Views)
                {
                    if (v.Cfg.Visible) v.Render(s);
                }
            }
            foreach (ElementView v in Views) v.Tick(dt);
            TickIntro();
        }

        // True while the overlay itself shows the song, so the game's own big "artist - song" caption is hidden
        // (Patches.GameTitle). Recomputed by ApplySettings.
        public static bool ShowsSong;
        private float _songCheckAt;

        private static void RefreshShowsSong()
        {
            Settings s = Main.Settings;
            ShowsSong = s.HideGameTitle && !s.OverlayHidden && s.Elements.Exists(c => c.Visible &&
                (c.Type == ElementType.SongInfo || (c.Type == ElementType.CustomText && c.Template != null &&
                 (c.Template.IndexOf("{Title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  c.Template.IndexOf("{Artist", StringComparison.OrdinalIgnoreCase) >= 0))));
        }

        private bool Gather()
        {
            scrController ctrl = scrController.instance;
            scrConductor cond = scrConductor.instance;
            if (!ctrl || !cond) return false;
            Snapshot s = _snap;
            try
            {
                if (RDC.auto) _autoOnce = true;

                s.Progress = ctrl.percentComplete;
                var mm = scrPlayerManager.instance.mistakesManager;
                s.XAcc = float.IsNaN(mm.percentXAcc) ? 1f : mm.percentXAcc;
                s.Acc = float.IsNaN(mm.percentAcc) ? 1f : mm.percentAcc;
                s.Best = _autoOnce ? _storedBest : Mathf.Max(_storedBest, s.Progress);

                if (!_isDeath) GatherTime(ctrl, cond, s);

                scrFloor floor = ctrl.currFloor ?? ctrl.firstFloor;
                if (floor)
                {
                    s.Margin = floor.marginScale;
                    float pitch = cond.song ? cond.song.pitch : 1f;
                    s.TBPM = (float)(cond.bpm * pitch * ctrl.playerOne.planetarySystem.speed);
                    float cbpm = floor.nextfloor
                        ? (float)(60.0 / (floor.nextfloor.entryTime - floor.entryTime) * pitch)
                        : s.TBPM;
                    s.CBPM = float.IsInfinity(cbpm) || float.IsNaN(cbpm) ? s.TBPM : cbpm;
                }

                int[] h = scrMistakesManager.marginTrackers[0].hitMarginsCount;
                Array.Copy(h, s.Hits, Math.Min(h.Length, s.Hits.Length));

                s.Combo = _combo;
                PlayCount.PlayData d = _hash != null ? PlayCount.GetData(_hash, _hashKey) : null;
                s.Attempt = d?.GetAttempts(_startProgress, _multiplier) ?? 0;
                s.FullAttempt = d?.GetAllAttempts() ?? 0;
                s.HasTiming = _timingCount > 0;
                s.Timing = _lastTiming;
                s.AvgTiming = _timingCount > 0 ? (float)(_timingSum / _timingCount) : 0f;
                s.Fps = _fps;
                s.Tile = ctrl.currentSeqID;
                s.TotalTiles = ADOBase.lm.listFloors.Count - 1;
                s.Checkpoints = scrController.checkpointsUsed;
                s.MaxCombo = _maxCombo;
                Stats.MapStat ms = Stats.Get(_hashKey);
                s.MapBestXAcc = ms?.BestXAcc ?? -1f;
                s.MapBestAcc = ms?.BestAcc ?? -1f;
                s.FirstClear = ms?.FirstClear;
                s.Clears = ms?.Clears ?? 0;
                s.Title = _title;
                s.Artist = _artist;
                return true;
            }
            catch (Exception e)
            {
                Main.LogError("Gather failed", e);
                return false;
            }
        }

        private void GatherTime(scrController ctrl, scrConductor cond, Snapshot s)
        {
            AudioSource song = cond.song;
            if (song && song.clip)
            {
                float now = song.time;
                s.Total = song.clip.length;
                if (now == 0f && _songPlaying) now = s.Total;
                else if (now > 0f) _songPlaying = true;
                s.Now = now;
                return;
            }
            if (_mapTotal < 0f)
            {
                var floors = ADOBase.lm.listFloors;
                _mapTotal = (float)floors[floors.Count - 1].entryTime;
            }
            s.Total = _mapTotal;
            float t = ctrl.state == States.Start ? 0f : (float)(cond.addoffset + cond.songposition_minusi);
            s.Now = Mathf.Clamp(t, 0f, _mapTotal);
        }

        private static string Strip(string s) =>
            string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "");

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
