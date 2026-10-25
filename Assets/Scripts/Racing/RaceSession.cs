using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽車模式的生命週期(04 擁有)。GameSession 在 Racing == true 時把 Begin/Rematch/End 分流到這裡。
    // M1:RaceWorld.Build 賽道 → RaceKart 玩家車(RaceArt.BuildKart 外觀)→ RaceCheckpoints 圈數/計時 → RaceHud / RaceJuice。
    // 最佳圈/總時間用 RaceKeys 存毫秒到 Prefs。End 全部還原。
    public static class RaceSession
    {
        public static bool Active;
        public static RaceConfig Pending = new RaceConfig();   // 選單寫這個
        public static RaceConfig Config;                        // 開賽時從 Pending 複製
        public static RacePhase Phase = RacePhase.Inactive;
        public static float RaceTime;                           // 比賽中且沒開選單才累加(unscaled)
        public static readonly List<float> LapTimes = new List<float>();   // 玩家各圈秒數
        public static Transform Root;                           // 所有賽車物件的父物件;End 時整個 Destroy
        public static Transform PlayerBody;                     // = Player.transform
        public static float CountdownLeft;                      // 倒數剩餘秒數(Phase == Countdown 時)
        public const float CountdownLen = 3f;

        public static RaceKart Player;
        public static readonly List<RaceKart> Karts = new List<RaceKart>();
        public static RaceTrack Track;
        public static RaceCheckpoints Progress;

        // 完賽後 HUD 讀(契約外的小擴充)
        public static float FinishTime;     // 玩家完賽總時間(秒)
        public static bool NewRecord;       // 本場刷新了總時間紀錄

        public static event System.Action<RacePhase> PhaseChanged;
        public static event System.Action<RaceKart, int> LapDone;   // (車, 已完成圈數)
        public static event System.Action<RaceKart> KartFinished;

        public static int TrackCount => RaceWorld.TrackCount;
        public static string TrackName(int id) => RaceWorld.TrackName(id);

        public static int Lap(RaceKart k) { return k != null && k.Progress != null ? k.Progress.Lap : 0; }
        public static int Place(RaceKart k) { return k != null && k.Progress != null ? k.Progress.Rank : 1; }

        /// 最佳圈(秒):歷史紀錄與本場取小;0 = 沒有。
        public static float BestLapTime()
        {
            float best = 0f;
            if (Config != null) { int ms = Prefs.GetInt(RaceKeys.BestLap(Config.TrackId), 0); if (ms > 0) best = ms / 1000f; }
            foreach (var t in LapTimes) if (t > 0f && (best <= 0f || t < best)) best = t;
            return best;
        }

        static CameraRig rig;
        static float lapStart;

        // ---- 大獎賽 / 計時賽 / 排氣量整合(模組 G)
        public static bool AutoPilot = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-raceauto") >= 0;       // 測試用:玩家車交給 RaceAI(難度 2);-raceauto 旗標截圖用
        public static bool TestFast;        // 測試用:倒數縮到 0.01 s
        static float postTime;              // 大獎賽:玩家完賽後等其他車的累計秒數(進度系統的時鐘 = RaceTime + postTime)
        static bool coachPhase, coachDonePending, coachFailed;
        static RaceKart coachKart;
        static float coachClock;
        static GhostRecorder coachRec;
        static GhostData coachData;
        static readonly HashSet<string> coachTried = new HashSet<string>();
        const float CoachTimeLimit = 300f;

        /// 排氣量 → AI 難度(50cc 簡單 / 100cc 普通 / 150cc 困難 / 200cc 超困難)。
        public static int AiLevelForCc(int cc) { return cc <= 50 ? 0 : cc <= 100 ? 1 : cc <= 150 ? 2 : 3; }

        static void NormalizeConfig(RaceConfig c)
        {
            c.CC = c.CC <= 75 ? 50 : c.CC <= 125 ? 100 : c.CC <= 175 ? 150 : 200;
            if (c.Mirror) c.Infinite = false;
            if (c.Infinite) c.CC = 200;
            RaceKart.InfiniteRate = c.Infinite ? 0.02f : 0f;   // 每秒 +2% 極速(60 秒 x2.2、3 分鐘 x4.6,上限 x6)
            if (c.TimeTrial) { c.GrandPrix = false; c.AiCount = 0; c.Items = false; c.FreeRoam = false; }
            else if (c.GrandPrix || c.Cup >= 0) c.AiLevel = AiLevelForCc(c.CC);   // VS 自訂維持選單設定的 AiLevel
            if (c.ItemRule == 2) c.Items = false;                                  // 無道具;其餘規則由 RaceItems 讀 cfg.ItemRule
        }

        static KartStats StatsFor(KartStyle style)
        {
            if (RaceSoft.HasApplyCC)
            {
                var st = KartPresets.For(style, 150);
                RaceSoft.ApplyCC(st, Config.CC);   // K5:KartPresets.ApplyCC
                return st;
            }
            return KartPresets.For(style, Config.CC);   // 替身:KartPresets.For 本來就會依 cc 調極速
        }

        // ---------------------------------------------------------------- 生命週期
        public static void Begin(CameraRig r)
        {
            if (Active) return;
            rig = r;
            Config = (Pending ?? new RaceConfig()).Clone();
            { var ca = System.Environment.GetCommandLineArgs(); int ai = System.Array.IndexOf(ca, "-raceai"); if (ai >= 0 && ai + 1 < ca.Length && int.TryParse(ca[ai + 1], out int n)) Config.AiCount = Mathf.Clamp(n, 0, 7); }
            { var cb = System.Environment.GetCommandLineArgs(); int ti = System.Array.IndexOf(cb, "-racetrack"); if (ti >= 0 && ti + 1 < cb.Length && int.TryParse(cb[ti + 1], out int tid)) Config.TrackId = Mathf.Clamp(tid, 0, Mathf.Max(0, RaceWorld.TrackCount - 1)); if (System.Array.IndexOf(cb, "-racefree") >= 0) Config.FreeRoam = true; }   // 測試:-racetrack N / -racefree   // 測試:-raceai N 直接指定 AI 台數
            NormalizeConfig(Config);
            RaceGrandPrix.Begin(Config);   // 大獎賽:把 Config 改成第 1 場的賽道
            coachTried.Clear(); postTime = 0f;
            try
            {
                Active = true; SetPhase(RacePhase.Loading);
                var cam = rig != null ? rig.GetComponent<Camera>() : Camera.main;
                Env.Save(cam);
                Env.HideFrc();
                RaceArt.ApplyOutdoor(cam);
                BuildRace(cam);
                Debug.Log("[RaceSession] begin track=" + Config.TrackId + " laps=" + Config.Laps + " cc=" + Config.CC + (Config.GrandPrix ? " GP cup=" + Config.Cup : "") + (Config.TimeTrial ? " TT" : ""));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[RaceSession] Begin failed: " + e);
                MenuSystem.GoMain();   // → GameSession.End → RaceSession.End,全部還原
            }
        }

        // 建一場比賽(世界 + 車 + 進度 + 道具 + HUD)。不碰環境存檔(Env),所以大獎賽換場可以重複呼叫。
        static void BuildRace(Camera cam)
        {
            Root = new GameObject("RaceRoot").transform;
            Root.gameObject.AddComponent<RaceRunner>();
            Track = RaceWorld.Build(Config, Root, SuperSample.IsTestRun() ? RaceQuality.Low : RaceQuality.High);
            RaceKart.Ground = RaceWorld.SampleGround;

            Karts.Clear(); postTime = 0f;
            Player = SpawnKart(0, Track.Grid(0), true);
            PlayerBody = Player.transform;
            GivePlayerInput();
            for (int i = 0; i < Config.AiCount && i < 7; i++)
            {
                var ak = SpawnKart(1 + i, Track.Grid(1 + i), false);
                RaceAI.Spawn(ak, Track, Config, i, Player, GameSession.Rookie);
            }
            BuildProgress();
            RaceItems.Init(Track, Config, Root);
            RaceHazards.Init(Config.FreeRoam ? null : Track, Root);   // 移動障礙(自由探索沒有)
            RaceAI.HazardSource = () => RaceHazards.MergedWith(RaceItems.Hazards);

            RaceJuice.Init();
            RaceMusic.Play(Config.TrackId);
            RaceShaders.CreateSkyDome(Root);
            if (cam != null) RaceShaders.EnableDepth(cam);
            Root.gameObject.AddComponent<RaceHud>();
            if (Config.TimeTrial) Root.gameObject.AddComponent<GhostOverlay>();
            if (rig != null) { rig.Target = PlayerBody; rig.Orbit = false; rig.Mode = 10; }
            RaceCamera.Reset();
            RaceGrandPrix.OnRaceBuilt(Karts);
            if (Config.TimeTrial)
            {
                RaceGhost.Init(Config, Root);
                if (RaceGhost.NeedCoach && !coachTried.Contains(RaceGhost.CoachPath)) { StartCoachPhase(); return; }
            }
            StartCountdown();
        }

        static void GivePlayerInput()
        {
            if (Player == null) return;
            if (AutoPilot)
            {
                var c2 = Config.Clone(); c2.AiLevel = 2;
                RaceAI.Spawn(Player, Track, c2, 90, null);
                Player.IsPlayer = true;   // RaceAI.Spawn 會把它清掉
            }
            else Player.Source = new KartInputReader(0);
        }

        // 拆掉一場比賽(世界 / 車 / 道具 / HUD),保留環境存檔。End 與大獎賽換場共用。
        static void TearDownRace()
        {
            DropProgress();
            RaceGhost.Shutdown();
            RaceStartBoost.Clear();
            RaceJuice.Shutdown();
            RaceMusic.Stop();
            RaceItems.Shutdown(); RaceHazards.Shutdown(); RaceAI.HazardSource = null;
            Rumble.StopAll();
            try { RaceWorld.Destroy(); } catch (System.Exception e) { Debug.LogWarning("[RaceSession] RaceWorld.Destroy: " + e.Message); }
            RaceKart.Ground = null;
            if (Root != null) { Root.gameObject.SetActive(false); Object.Destroy(Root.gameObject); }   // 先停用:舊車立刻從 RaceKart.All 移除,新一場的 AI 不會把它們當前車
            Root = null; PlayerBody = null; Player = null; Track = null; coachKart = null; coachPhase = false; coachDonePending = false;
            Karts.Clear(); LapTimes.Clear(); RaceTime = 0f; FinishTime = 0f; NewRecord = false; postTime = 0f;
        }

        /// 大獎賽換場(RaceGrandPrix.Continue 呼叫):Config.TrackId / Reverse 已改好;重建世界,車手 / 車款 / 設定不變,AI 回起跑格。
        internal static void GpNextRace()
        {
            if (!Active) return;
            Time.timeScale = 1f;
            try
            {
                var cam = rig != null ? rig.GetComponent<Camera>() : Camera.main;
                TearDownRace();
                SetPhase(RacePhase.Loading);
                BuildRace(cam);
                Debug.Log("[RaceSession] GP next race track=" + Config.TrackId + " rev=" + Config.Reverse);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[RaceSession] GpNextRace failed: " + e);
                MenuSystem.GoMain();
            }
        }

        static RaceKart SpawnKart(int id, Pose pose, bool isPlayer)
        {
            var style = Config.Kart;
            int ci = Mathf.Clamp(Config.ColorIndex, 0, RacePalette.Kart.Length - 1);
            if (!isPlayer) ci = (ci + id) % RacePalette.Kart.Length;   // AI 輪流用不同顏色(避開玩家色:id≥1 一定不同)
            var color = RacePalette.Kart[ci];
            var k = RaceKart.Create(Root, pose, StatsFor(style), id, isPlayer ? (Loc.Lang == "en" ? "You" : "玩家") : "AI" + id);
            k.IsPlayer = isPlayer;
            var vis = RaceArt.BuildKart(style, color);
            vis.transform.SetParent(k.transform, false);
            vis.transform.localPosition = new Vector3(0f, -RaceKart.RideDist, 0f);   // Visual 腳底 y=0;車根在地面上方 RideDist
            var kv = vis.AddComponent<KartVisual>(); kv.Kart = k;
            if (isPlayer && !AutoPilot) k.Source = new KartInputReader(0);
            k.Frozen = true;
            Karts.Add(k);
            return k;
        }

        static void BuildProgress()
        {
            DropProgress();
            Progress = new RaceCheckpoints(Track, Config.Laps, Config.FreeRoam);
            foreach (var k in Karts) Progress.Register(k, k.IsPlayer);
            RaceReferee.Attach(Progress, Root);   // 裁判(迷路 / 抄捷徑 / 逆向 重置)
            Progress.LapCompleted += OnLapCompleted;
            Progress.Finished += OnFinished;
            RaceKart.FellOut += OnFell;
            RaceKart.RespawnPressed += OnRespawnKey;
        }

        static void DropProgress()
        {
            if (Progress != null) { Progress.LapCompleted -= OnLapCompleted; Progress.Finished -= OnFinished; }
            Progress = null;
            RaceKart.FellOut -= OnFell;
            RaceKart.RespawnPressed -= OnRespawnKey;
        }

        static RaceKart KartById(int id) { foreach (var k in Karts) if (k != null && k.KartId == id) return k; return null; }

        static void OnFell(RaceKart k) { if (Progress != null && k != null) Progress.RequestReset(k.KartId, ResetReason.Fell); }
        static void OnRespawnKey(RaceKart k) { if (Progress != null && k != null && Phase == RacePhase.Racing) Progress.RequestReset(k.KartId, ResetReason.Manual); }

        static void OnLapCompleted(int id, int done)
        {
            var k = KartById(id); if (k == null) return;
            if (coachPhase)
            {
                if (k == coachKart && coachRec != null && coachRec.Running) { coachData = coachRec.Finish(); coachDonePending = true; }
                return;   // 教練那圈不通知 HUD
            }
            if (k == Player && Config != null && LapTimes.Count < Config.Laps && Phase != RacePhase.Finished)
            {
                float t = Mathf.Max(0.001f, RaceTime - lapStart); lapStart = RaceTime; LapTimes.Add(t);
                SaveBestLap(t);
            }
            if (k == Player && Config != null && Config.TimeTrial && Phase == RacePhase.Racing && Player != null)
                RaceGhost.LapDone(Player.Position, Player.transform.rotation);   // 存最佳圈幽靈(刷新才覆蓋)
            LapDone?.Invoke(k, done);
        }

        static void OnFinished(int id)
        {
            var k = KartById(id); if (k == null) return;
            if (coachPhase) return;
            if (k == Player && Phase != RacePhase.Finished)
            {
                if (LapTimes.Count < Config.Laps) { float t = Mathf.Max(0.001f, RaceTime - lapStart); lapStart = RaceTime; LapTimes.Add(t); SaveBestLap(t); }
                FinishTime = RaceTime;
                int ms = Mathf.RoundToInt(FinishTime * 1000f); string key = RaceKeys.BestTotal(Config.TrackId, Config.Laps); int old = Prefs.GetInt(key, 0);
                NewRecord = old <= 0 || ms < old;
                if (NewRecord) { Prefs.SetInt(key, ms); Persist(); }
                Player.Source = NullKartInput.I;   // 滑行停下
                SetPhase(RacePhase.Finished);
                if (RaceGrandPrix.Active) RaceGrandPrix.OnPlayerFinished();   // 大獎賽:等其他車 / 15 秒超時 → 積分榜
            }
            KartFinished?.Invoke(k);
        }

        static void SaveBestLap(float t)
        {
            int ms = Mathf.RoundToInt(t * 1000f); string key = RaceKeys.BestLap(Config.TrackId); int old = Prefs.GetInt(key, 0);
            if (old <= 0 || ms < old) { Prefs.SetInt(key, ms); Persist(); }
        }
        static void Persist() { if (!Prefs.Isolated) PlayerPrefs.Save(); }

        static void SetPhase(RacePhase p) { Phase = p; PhaseChanged?.Invoke(p); }

        // 「再來一場」:整場重建(世界 / 車 / AI / 道具 / 進度),Config 不變。
        // 舊版只把玩家車送回起跑格,AI 車留在上一場的終點附近(帶著舊的 AI 狀態 / 道具 / 加速 / 卡住計時),
        // 新的進度系統又在它們目前的位置登記 → AI 在倒數時就亂跑、進度與橡皮筋全錯(使用者回報「第二場對手亂跑」)。
        // 重建成本約數百毫秒,與大獎賽換場(GpNextRace)同一條路徑,已有測試涵蓋。
        public static void Restart()
        {
            if (!Active || Player == null || Track == null) return;
            if (coachPhase) return;
            Time.timeScale = 1f;
            try
            {
                var cam = rig != null ? rig.GetComponent<Camera>() : Camera.main;
                TearDownRace();
                SetPhase(RacePhase.Loading);
                RaceGrandPrix.OnRestart();
                BuildRace(cam);
                Debug.Log("[RaceSession] restart track=" + Config.TrackId);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[RaceSession] Restart failed: " + e);
                MenuSystem.GoMain();
            }
        }

        public static void End(CameraRig r)
        {
            if (!Active && Root == null) return;
            Active = false;
            Time.timeScale = 1f;
            TearDownRace();
            RaceGrandPrix.Reset();
            coachTried.Clear();
            SetPhase(RacePhase.Inactive);
            JuiceFX.BaseFovOverride = float.NaN;
            Env.Restore();
            Env.ShowFrc();
            var cr = r != null ? r : rig;
            if (cr != null) cr.Mode = 1;
            rig = null;
            Debug.Log("[RaceSession] end");
        }

        /// 玩家車回到最後檢查點的重生點(暫停選單「重置位置」、R 鍵)。倒數中回起跑格。
        public static void ResetPlayer()
        {
            if (Player == null) return;
            if (Progress != null && Phase == RacePhase.Racing) Progress.RequestReset(Player.KartId, ResetReason.Manual);
            else if (Track != null) Player.Respawn(Track.Grid(0));
        }

        /// Juice.Tick 在 Racing 時轉呼叫這裡(取代 FRC 的音樂/群眾聲)。
        public static void JuiceTick(float dt)
        {
            if (!SfxBus.Enabled) return;
            SfxBus.SetLoop("intake", "intake_loop", 0f, 1f);
            SfxBus.SetLoop("drive", "drive_loop", 0f, 1f);
            SfxBus.SetLoop("fly", "fly_loop", 0f, 1f);
            SfxBus.SetLoop("crowd", "crowd_loop", 0f, 1f, SfxGroup.Amb);
            SfxBus.SetLoop("music", "music_loop", 0f, 1f, SfxGroup.Music);
            RaceJuice.Tick(dt);
        }

        static void StartCountdown()
        {
            RaceTime = 0f; lapStart = 0f; LapTimes.Clear(); NewRecord = false; FinishTime = 0f; RaceHazards.ResetClock();
            CountdownLeft = (SuperSample.IsTestRun() || TestFast) ? 0.01f : CountdownLen;
            if (Player != null) Player.Frozen = true;
            RaceStartBoost.Begin(Player, Karts, Config, !AutoPilot, Config.Seed + RaceGrandPrix.RaceIndex);
            if (Config.TimeTrial) RaceGhost.ResetRun();
            SetPhase(RacePhase.Countdown);
        }

        // ---------------------------------------------------------------- 計時賽:教練幽靈(RaceAI 難度 2 跑一圈錄下來)
        // 第一次進這條賽道(沒有教練檔)時,在倒數前先加速(timeScale 3)讓教練車跑完一圈,存檔後教練車移除、玩家回起跑格倒數。
        static void StartCoachPhase()
        {
            coachFailed = false; coachDonePending = false; coachData = null; coachClock = 0f;
            coachKart = SpawnKart(1, Track.Grid(1), false);
            var c2 = Config.Clone(); c2.AiLevel = 2;
            RaceAI.Spawn(coachKart, Track, c2, 0, null);
            coachKart.Frozen = false;
            if (Player != null) Player.Frozen = true;
            BuildProgress();
            Progress.StartRace(0f);
            coachRec = new GhostRecorder();
            coachRec.Begin(coachKart.Position, coachKart.transform.rotation);
            coachPhase = true;
            RaceGhost.Status = Loc.Lang == "en" ? "Recording the coach ghost..." : "正在錄製教練幽靈...";
            Time.timeScale = 3f;
            SetPhase(RacePhase.Intro);
            Debug.Log("[RaceSession] coach ghost recording started");
        }

        static void FinishCoachPhase(bool ok)
        {
            if (!coachPhase) return;
            coachPhase = false; coachDonePending = false;
            Time.timeScale = 1f;
            string key = RaceGhost.CoachPath;
            coachTried.Add(key);
            if (ok && coachData != null && !coachFailed) RaceGhost.SaveCoach(coachData);
            else Debug.LogWarning("[RaceSession] coach ghost failed (time limit " + CoachTimeLimit + " s)");
            coachData = null; coachRec = null; RaceGhost.Status = "";
            if (coachKart != null) { Karts.Remove(coachKart); Object.Destroy(coachKart.gameObject); coachKart = null; }
            postTime = 0f;
            BuildProgress();
            if (Player != null && Track != null) Player.Respawn(Track.Grid(0));
            RaceCamera.Reset();
            StartCountdown();
        }

        /// 教練幽靈錄製中的提示文字。
        sealed class GhostOverlay : MonoBehaviour
        {
            GUIStyle st;
            void OnGUI()
            {
                if (string.IsNullOrEmpty(RaceGhost.Status)) return;
                if (st == null) st = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, 50f), RaceGhost.Status, st);
            }
        }

        internal static void Tick(float dt)
        {
            if (!Active) return;
            if (Player != null) RaceCamera.MaxSpeedHint = Mathf.Max(1f, Player.MaxSpeed);
            if (MenuSystem.Blocking && !RaceKart.IgnoreMenuBlock) return;   // 暫停中計時明確停住(測試除外)
            if (coachPhase && coachDonePending) FinishCoachPhase(true);
            if (Phase == RacePhase.Countdown)
            {
                CountdownLeft -= dt;
                RaceStartBoost.Tick(dt, Mathf.Max(0f, CountdownLeft));
                if (CountdownLeft <= 0f)
                {
                    RaceTime = 0f; lapStart = 0f; RaceHazards.ResetClock();
                    foreach (var k in Karts) if (k != null) k.Frozen = false;
                    if (Progress != null) Progress.StartRace(0f);
                    RaceStartBoost.Go();
                    if (Config.TimeTrial && Player != null) RaceGhost.StartLap(Player.Position, Player.transform.rotation);
                    SetPhase(RacePhase.Racing);
                }
            }
            else if (Phase == RacePhase.Racing) RaceTime += dt;
            else if (Phase == RacePhase.Finished && RaceGrandPrix.State == GpState.WaitOthers) postTime += dt;
            if (RaceGrandPrix.Active) RaceGrandPrix.Tick(dt);
            if (Input.GetKeyDown(KeyCode.R)) ResetPlayer();
        }

        internal static void FixedStep(float dt)
        {
            if (!Active || Progress == null || (MenuSystem.Blocking && !RaceKart.IgnoreMenuBlock)) return;
            if (coachPhase)
            {
                coachClock += dt;
                if (coachKart != null && coachRec != null && coachRec.Running) { Progress.Step(dt, coachClock); if (coachRec.Running) coachRec.Step(dt, coachKart.Position, coachKart.transform.rotation); }
                if (coachClock > CoachTimeLimit && !coachDonePending) { coachDonePending = true; coachData = null; coachFailed = true; }
                return;
            }
            if (Phase == RacePhase.Racing) Progress.Step(dt, RaceTime);
            else if (Phase == RacePhase.Finished && RaceGrandPrix.State == GpState.WaitOthers) Progress.Step(dt, RaceTime + postTime);   // 大獎賽:玩家完賽後其他車繼續算進度 / 完賽
            if (Config != null && Config.TimeTrial && Phase == RacePhase.Racing && Player != null) RaceGhost.Step(dt, Player.Position, Player.transform.rotation);
            RaceItems.Tick(dt);
            if (Phase == RacePhase.Racing || Phase == RacePhase.Finished) RaceHazards.Tick(dt);
            if (Config != null && Config.FreeRoam && Player != null) RaceWorld.TryCollect(Player.transform.position);   // 自由探索:撿金幣
            // 比賽金幣:每個物理步驟對每台車撿一次(之前只有測試呼叫 StepPickups,正式比賽根本沒接線 -> 金幣吃不到)
            if (Config != null && !Config.FreeRoam && Phase == RacePhase.Racing)
                for (int ci = 0; ci < Karts.Count; ci++)
                {
                    var ck = Karts[ci]; if (ck == null) continue;
                    PickupResult pr = RaceWorld.StepPickups(ck.KartId, ck.Position, Time.time);
                    for (int n = 0; n < pr.Coins; n++) ck.AddCoin();
                }
        }

        // ---------------------------------------------------------------- 環境存檔/還原(04 §5.1、§6 衝突 4/5)
        static class Env
        {
            static readonly string[] FrcRoots = { "Field", "Arena", "FieldModel", "HubGlows" };
            static readonly List<GameObject> hidden = new List<GameObject>();
            static bool saved;
            static Camera cam;
            static CameraClearFlags clear; static Color bg; static float far, fov;
            static bool fog; static FogMode fogMode; static Color fogColor; static float fogStart, fogEnd;
            static UnityEngine.Rendering.AmbientMode ambMode; static Color ambSky, ambEq, ambGround; static float ambInt;
            static float fixedDt; static Vector3 gravity; static float shadowDist;
            static Light sun; static Quaternion sunRot; static Color sunColor; static float sunInt; static float exposure = 1.12f;

            public static void Save(Camera c)
            {
                cam = c;
                if (cam != null) { clear = cam.clearFlags; bg = cam.backgroundColor; far = cam.farClipPlane; fov = cam.fieldOfView; }
                fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor; fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
                ambMode = RenderSettings.ambientMode; ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor; ambInt = RenderSettings.ambientIntensity;
                fixedDt = Time.fixedDeltaTime; gravity = Physics.gravity; shadowDist = QualitySettings.shadowDistance;
                var sg = GameObject.Find("Sun"); sun = sg != null ? sg.GetComponent<Light>() : null;
                if (sun != null) { sunRot = sun.transform.rotation; sunColor = sun.color; sunInt = sun.intensity; }
                if (PostFX.Instance != null) exposure = PostFX.Instance.Exposure;
                saved = true;
            }

            public static void Restore()
            {
                if (!saved) return; saved = false;
                if (cam != null) { cam.clearFlags = clear; cam.backgroundColor = bg; cam.farClipPlane = far; cam.fieldOfView = fov; }
                RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.ambientMode = ambMode; RenderSettings.ambientSkyColor = ambSky; RenderSettings.ambientEquatorColor = ambEq; RenderSettings.ambientGroundColor = ambGround; RenderSettings.ambientIntensity = ambInt;
                Time.fixedDeltaTime = fixedDt; Physics.gravity = gravity; QualitySettings.shadowDistance = shadowDist;
                if (sun != null) { sun.transform.rotation = sunRot; sun.color = sunColor; sun.intensity = sunInt; }
                if (PostFX.Instance != null) { PostFX.Instance.Exposure = exposure; PostFX.Instance.ClearRaceGrade(); }
                cam = null; sun = null;
            }

            public static void HideFrc()
            {
                foreach (var n in FrcRoots)
                {
                    var g = GameObject.Find(n);   // 只找得到 active 的根物件
                    if (g != null && g.transform.parent == null && !hidden.Contains(g)) { g.SetActive(false); hidden.Add(g); }
                }
            }

            public static void ShowFrc()
            {
                foreach (var g in hidden) if (g != null) g.SetActive(true);
                hidden.Clear();
            }
        }

        // 每幀驅動(掛在 RaceRoot 上,End 時跟著 Destroy)。FixedUpdate 晚於 RaceKart 執行。
        [DefaultExecutionOrder(100)]
        sealed class RaceRunner : MonoBehaviour
        {
            float rehide;
            void Update()
            {
                Tick(Time.unscaledDeltaTime);
                rehide += Time.unscaledDeltaTime;
                if (rehide > 0.5f) { rehide = 0f; Env.HideFrc(); }   // 場地 GLB 是非同步載入,晚到的也要藏
            }
            void FixedUpdate() { FixedStep(Time.fixedDeltaTime); }
        }

        public static string FormatTime(float t)
        {
            int ms = Mathf.Max(0, Mathf.RoundToInt(t * 1000f));
            return (ms / 60000) + ":" + (ms / 1000 % 60).ToString("00") + "." + (ms % 1000).ToString("000");
        }
    }
}
