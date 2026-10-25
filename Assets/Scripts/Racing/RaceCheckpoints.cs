using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 檢查點、圈數、名次、迷路重置(模組 W,見 docs/racing/00-contracts.md §3.6、02 §2)。
    // 只透過 IRacerBody 認識車子(不依賴 RaceKart)。名次與圈數只在這裡算;RaceSession 每個 FixedUpdate 呼叫 Step。
    //
    // 規則:
    //  - 每台車只看「下一個檢查點」:這一步和上一步相對門平面的符號從負變成非負,而且穿過點離門中心 ≤ 半寬,才算通過。
    //  - 一定要照順序過;過了終點線(第 0 個門)而且其他門都過了才算一圈。起跑時車在終點線後方,第一次穿過終點線只是「起跑」,不算一圈。
    //  - 一步移動超過 20 m 視為傳送,不做過門判定(傳送不能跳門)。
    //  - 重置條件(送回最後通過的檢查點的重置點):y < KillY、在水上(缺口上空飛行中除外,見 GapFlyMinY)、超出地圖邊界 +10 m、OutOfBounds 地面連續 > 4 s、
    //    有油門但速度 < 0.5 m/s 連續 > 3 s(卡住)、玩家手動(冷卻 2 s)。
    //  - 自由駕駛(M5):不算圈、不過門、不判卡住,只檢查掉進海 / 掉出地圖 / 邊界外(OutOfBounds 久待);重生點 = 2~3 秒前記下的安全位置
    //    (車子每秒記一次站在陸地上的位置),還沒有記錄時才用離車子最近的門。
    //  - 捷徑(M5,見 ShortcutPath):下一個門是被跳過的第一個門時,可以改穿過捷徑入口門,再穿過出口門,就一次算過被跳過的門。
    //    走捷徑時主線被跳過的門不需要過;但如果車子其實是走主線去穿過了下一個主線門,就當作沒走捷徑(取消捷徑狀態,正常過門)。
    //    重置(掉出去等)一律取消捷徑狀態,送回最後通過的主線門。
    //  - 裁判(瑪利歐賽車式,見 RaceReferee):下面幾種「作弊 / 迷路」也會重置到最後通過的檢查點(RefereeCause):
    //      SkippedGate  穿過第 N+k(k = 1..SkipLookahead)個門卻沒過 N..N+k-1,而且不是走合法捷徑(入口門 -> 出口門)。滑翔 / 缺口上空飛行中算「合法」,
    //                   改成幫忙把中間的門補記(飛越缺口、滑翔跳台可能斜著切過幾個門),不重置。
    //      OffCourse    離開可行駛走廊(路 + 路肩 CorridorShoulder + 捷徑路面 + 缺口上空)連續 > OffCorridorTime 秒,或離走廊邊緣 > OffCorridorHard m 超過 0.5 s。
    //                   滑翔 / 軌道滑行 / 跑牆 / 特技 / 滯空算「合法離路」,計時歸零(IRacerAirState,RaceKart 直接判斷)。
    //      WrongWay     速度 > WrongWayMinSpeed 而且速度方向逆著賽道 > WrongWayResetTime 秒。
    //    重置交給 RefereeHook(裁判動畫);沒有 hook(測試 / 幽靈車)就立刻重置。手動重置(R 鍵)一律立刻重置、不經裁判。
    public sealed class RaceCheckpoints
    {
        sealed class State
        {
            public IRacerBody Body;
            public RacerProgress P;
            public bool IsPlayer;
            public Vector3 LastPos;
            public bool LapStarted;
            public int NextCp;
            public int LastPassed = -1;
            public float LapStartTime;
            public float HintS = -1f;
            public float OffTime, StuckTime, WrongTime;
            // 裁判規則(RefereeCause)的計時
            public float CorClock, CorExcess, OffCorTime, HardTime, RefWrong;
            public bool Held;                          // 裁判正在抓著這台車(不判定、不過門)
            public ResetReason HeldWhy;
            public RefereeCause LastCause;
            public float LastManual = -99f;
            public ShortcutPath OnSc;                 // 正在走的捷徑(穿過入口門之後、出口門之前)
            public float ScD;                          // 在捷徑上的弧長
            // 自由駕駛:最近 3 個安全位置(每秒記一次的環狀緩衝,不配置記憶體)
            public readonly Vector3[] SafePos = new Vector3[3];
            public readonly Vector3[] SafeFwd = new Vector3[3];
            public int SafeCount, SafeHead;
            public float SafeTimer;
        }

        public const float MaxStepForGate = 20f;   // 一步移動超過這個距離 = 傳送
        // 裁判規則門檻(RaceRefereeTest 驗這些數值)
        public const int SkipLookahead = 10;           // 偵測「跳過門」只看前面這麼多個門
        public const float CorridorShoulder = 8f;      // 路緣外這麼寬仍算可行駛(路肩 + 草地邊緣)
        public const float OffCorridorTime = 4f;       // 離開走廊超過這麼久 = 重置
        public const float OffCorridorHard = 45f;      // 離走廊邊緣超過這麼遠(橫向)超過 HardDebounce 秒 = 重置
        public const float HardDebounce = 0.5f;
        public const float WrongWayResetTime = 3f;     // 逆向行駛超過這麼久 = 重置
        public const float WrongWayMinSpeed = 6f;      // m/s
        public const float GapFlyMinY = 0.6f;      // 缺口上空:車的高度(世界 y)高於這個值就還在飛(海平面 = 0)

        readonly RaceTrack track;
        readonly int laps;
        readonly bool freeRoam;
        readonly List<State> states = new List<State>();
        readonly Dictionary<int, State> byId = new Dictionary<int, State>();
        readonly List<RacerProgress> standings = new List<RacerProgress>();
        float raceStart;
        bool racing;
        float sortTimer;
        float lastRaceTime;

        public event Action<int, int> LapCompleted;       // (id, 已完成圈數)
        public event Action<int> Finished;
        public event Action<int, ResetReason> Reset;

        /// 裁判接手重置:回傳 true = 裁判負責(之後一定要呼叫 FinishHold(id) 完成重置);false / null = 立刻重置。
        /// 參數 = (車, 重置點, 原因)。手動重置不經過這裡。
        public Func<IRacerBody, Pose, RefereeCause, bool> RefereeHook;

        /// 裁判規則(跳門 / 離開走廊 / 逆向重置)是否啟用。預設 false = 行為和裁判出現之前完全一樣(舊測試、AI 測試不受影響);
        /// RaceReferee.Attach(RaceSession)會設成 true。
        public bool RefereeRules;

        public RaceCheckpoints(RaceTrack track, int laps, bool freeRoam)
        {
            if (track == null) throw new ArgumentNullException("track");
            this.track = track;
            this.laps = Mathf.Max(1, laps);
            this.freeRoam = freeRoam;
        }

        public IReadOnlyList<RacerProgress> Standings { get { return standings; } }

        // ------------------------------------------------------------------ 註冊

        public void Register(IRacerBody body, bool isPlayer)
        {
            if (body == null) return;
            Unregister(body.RacerId);
            var st = new State { Body = body, IsPlayer = isPlayer, P = new RacerProgress { Id = body.RacerId } };
            st.LastPos = body.Position;
            st.LapStartTime = raceStart;
            st.NextCp = 0;
            body.Progress = st.P;
            states.Add(st);
            byId[body.RacerId] = st;
            standings.Add(st.P);
            UpdateS(st, st.LastPos);
            Sort();
        }

        public void Unregister(int id)
        {
            State st;
            if (!byId.TryGetValue(id, out st)) return;
            states.Remove(st);
            byId.Remove(id);
            standings.Remove(st.P);
        }

        public RacerProgress Get(int id)
        {
            State st;
            return byId.TryGetValue(id, out st) ? st.P : null;
        }

        public Checkpoint NextCheckpoint(int id)
        {
            State st;
            if (!byId.TryGetValue(id, out st)) return null;
            return track.Checkpoints[st.NextCp];
        }

        public Checkpoint LastCheckpoint(int id)
        {
            State st;
            if (!byId.TryGetValue(id, out st)) return null;
            return track.Checkpoints[st.LastPassed < 0 ? 0 : st.LastPassed];
        }

        public Checkpoint NearestCheckpoint(Vector3 p)
        {
            Checkpoint best = null; float bd = float.MaxValue;
            for (int i = 0; i < track.Checkpoints.Count; i++)
            {
                Checkpoint c = track.Checkpoints[i];
                float dx = c.Center.x - p.x, dz = c.Center.z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        public Pose GetRespawnPose(int id)
        {
            State st;
            if (!byId.TryGetValue(id, out st)) return track.RespawnPose(0);
            if (freeRoam)
            {
                if (st.SafeCount > 0)
                {
                    int o = st.SafeCount < 3 ? 0 : st.SafeHead;   // 最舊的一個(2~3 秒前)
                    Vector3 f = st.SafeFwd[o]; f.y = 0f;
                    if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
                    return new Pose(st.SafePos[o] + Vector3.up * 0.3f, Quaternion.LookRotation(f.normalized, Vector3.up));
                }
                return NearestCheckpoint(st.Body.Position).Respawn;
            }
            return track.Checkpoints[st.LastPassed < 0 ? 0 : st.LastPassed].Respawn;
        }

        // ------------------------------------------------------------------ 重置

        public void RequestReset(int id, ResetReason why) { RequestReset(id, why, CauseOf(why)); }

        public static RefereeCause CauseOf(ResetReason why)
        {
            switch (why)
            {
                case ResetReason.Fell: return RefereeCause.Fell;
                case ResetReason.OutOfBounds: return RefereeCause.OutOfBounds;
                case ResetReason.OffTrackTooLong: return RefereeCause.OffCourse;
                case ResetReason.Stuck: return RefereeCause.Stuck;
                default: return RefereeCause.Manual;
            }
        }

        public void RequestReset(int id, ResetReason why, RefereeCause cause)
        {
            State st;
            if (!byId.TryGetValue(id, out st)) return;
            if (st.Held) return;                                  // 裁判已經在處理這台車
            if (why == ResetReason.Manual)
            {
                if (lastRaceTime - st.LastManual < 2f) return;   // 冷卻 2 s
                st.LastManual = lastRaceTime;
            }
            Pose pose = GetRespawnPose(id);
            if (RefereeHook != null && why != ResetReason.Manual)
            {
                st.Held = true; st.HeldWhy = why; st.LastCause = cause;
                if (RefereeHook(st.Body, pose, cause)) return;    // 裁判接手
                st.Held = false;
            }
            DoReset(st, pose, why, cause);
        }

        /// 裁判把車放回重置點之後呼叫:真正執行重置(傳送、清狀態、發 Reset 事件)。沒有被抓著就什麼都不做。
        public void FinishHold(int id)
        {
            State st;
            if (!byId.TryGetValue(id, out st) || !st.Held) return;
            st.Held = false;
            DoReset(st, GetRespawnPose(id), st.HeldWhy, st.LastCause);
        }

        public bool IsHeld(int id) { State st; return byId.TryGetValue(id, out st) && st.Held; }
        public RefereeCause LastCause(int id) { State st; return byId.TryGetValue(id, out st) ? st.LastCause : RefereeCause.None; }

        void DoReset(State st, Pose pose, ResetReason why, RefereeCause cause)
        {
            st.LastCause = cause;
            st.Body.Respawn(pose);
            st.LastPos = pose.position;
            st.HintS = -1f;
            st.OffTime = 0f; st.StuckTime = 0f; st.WrongTime = 0f;
            st.OffCorTime = 0f; st.HardTime = 0f; st.RefWrong = 0f; st.CorExcess = 0f; st.CorClock = 0f;
            st.OnSc = null; st.P.OnShortcut = false;
            st.SafeCount = 0; st.SafeHead = 0; st.SafeTimer = 0f;
            if (Reset != null) Reset(st.P.Id, why);
        }

        // ------------------------------------------------------------------ 每步更新

        public void StartRace(float raceTime)
        {
            raceStart = raceTime;
            lastRaceTime = raceTime;
            racing = true;
            for (int i = 0; i < states.Count; i++)
            {
                states[i].LapStartTime = raceTime;
                states[i].LastPos = states[i].Body.Position;
            }
        }

        public void Step(float dt, float raceTime)
        {
            lastRaceTime = raceTime;
            for (int i = 0; i < states.Count; i++) StepRacer(states[i], dt, raceTime);
            sortTimer += dt;
            if (sortTimer >= 0.1f) { sortTimer = 0f; Sort(); }
        }

        void StepRacer(State st, float dt, float t)
        {
            IRacerBody body = st.Body;
            Vector3 pos = body.Position;
            RacerProgress pr = st.P;

            // 0) 裁判抓著:不判定、不過門(車子被吊著走,位置不代表比賽進度)
            if (st.Held) { st.LastPos = pos; return; }

            // 1) 重置判定
            ResetReason why;
            if (CheckReset(st, pos, dt, out why))
            {
                RequestReset(pr.Id, why);
                pos = body.Position;
                if (st.Held) { st.LastPos = pos; return; }
            }

            // 2) 過門
            bool skipReset = false;
            if (!freeRoam && racing && !pr.Finished)
            {
                Vector3 prev = st.LastPos;
                Vector3 dp = pos - prev;
                if (dp.x * dp.x + dp.z * dp.z < MaxStepForGate * MaxStepForGate)
                {
                    Checkpoint cp = track.Checkpoints[st.NextCp];
                    bool did = false;
                    if (Crosses(cp, prev, pos))
                    {
                        if (st.OnSc != null) { st.OnSc = null; pr.OnShortcut = false; }   // 其實是走主線過了下一個門 = 沒走捷徑
                        PassGate(st, cp, t);
                        did = true;
                    }
                    else if (st.OnSc != null)
                    {
                        if (Crosses(st.OnSc.Exit, prev, pos)) { FinishShortcut(st); did = true; }
                    }
                    else
                    {
                        ShortcutPath sc = ShortcutStartingAt(st.NextCp);
                        if (sc != null && Crosses(sc.Entry, prev, pos)) { st.OnSc = sc; pr.OnShortcut = true; did = true; }
                    }
                    if (!did && RefereeRules)
                    {
                        int sk = SkippedGateCrossed(st, prev, pos);
                        if (sk >= 0)
                        {
                            if (FlightLegit(st, pos)) CreditGatesTo(st, sk, t);   // 滑翔 / 缺口上空斜切過幾個門:幫忙補記
                            else skipReset = true;
                        }
                    }
                }
                else st.HintS = -1f;   // 傳送:重新找位置
            }
            else if (!freeRoam)
            {
                Vector3 dp = pos - st.LastPos;
                if (dp.x * dp.x + dp.z * dp.z >= MaxStepForGate * MaxStepForGate) st.HintS = -1f;
            }
            else RecordSafe(st, pos, dt);
            st.LastPos = pos;
            if (skipReset)
            {
                RequestReset(pr.Id, ResetReason.OffTrackTooLong, RefereeCause.SkippedGate);
                st.LastPos = body.Position;
                if (st.Held) return;
                pos = st.LastPos;
            }

            // 3) 進度與逆向
            UpdateS(st, pos);
            UpdateWrongWay(st, dt);

            // 4) 裁判規則:離開走廊太久 / 逆向太久
            if (RefereeRules && !freeRoam && racing && !pr.Finished)
            {
                RefereeCause cause;
                if (CheckCourse(st, pos, dt, out cause)) RequestReset(pr.Id, ResetReason.OffTrackTooLong, cause);
            }
        }

        /// 這一步有沒有從門的背面穿到正面(而且穿過點在門的半寬內)。
        static bool Crosses(Checkpoint cp, Vector3 prev, Vector3 pos)
        {
            float d0 = Side(cp, prev), d1 = Side(cp, pos);
            if (!(d0 < 0f && d1 >= 0f)) return false;
            float u = d0 / (d0 - d1);
            float cx = prev.x + (pos.x - prev.x) * u, cz = prev.z + (pos.z - prev.z) * u;
            float lat = (cx - cp.Center.x) * cp.Forward.z - (cz - cp.Center.z) * cp.Forward.x;
            return Mathf.Abs(lat) <= cp.HalfWidth;
        }

        /// 下一個要過的門是 nextCp 時,從它分出去的捷徑(沒有 = null)。
        ShortcutPath ShortcutStartingAt(int nextCp)
        {
            var list = track.Shortcuts;
            for (int i = 0; i < list.Count; i++) if (list[i].FirstSkipped == nextCp && list[i].FirstSkipped >= 0) return list[i];
            return null;
        }

        void FinishShortcut(State st)
        {
            ShortcutPath sc = st.OnSc;
            st.OnSc = null; st.P.OnShortcut = false;
            int count = track.Checkpoints.Count;
            st.LastPassed = sc.LastSkipped;
            st.NextCp = (sc.LastSkipped + 1) % count;
            st.P.NextCp = st.NextCp;
        }

        /// 自由駕駛:每秒記一次「站在陸地上」的位置。
        void RecordSafe(State st, Vector3 pos, float dt)
        {
            st.SafeTimer += dt;
            if (st.SafeTimer < 1f) return;
            st.SafeTimer = 0f;
            Surface sf = RaceWorld.SurfaceAt(pos.x, pos.z);
            if (sf == Surface.Water || sf == Surface.OutOfBounds || pos.y < RaceWorld.KillY) return;
            if (RaceWorld.HeightAt(pos.x, pos.z) < 0.8f) return;
            st.SafePos[st.SafeHead] = pos;
            Vector3 f = st.Body.Forward;
            st.SafeFwd[st.SafeHead] = f;
            st.SafeHead = (st.SafeHead + 1) % 3;
            if (st.SafeCount < 3) st.SafeCount++;
        }

        static float Side(Checkpoint cp, Vector3 p)
        {
            return (p.x - cp.Center.x) * cp.Forward.x + (p.z - cp.Center.z) * cp.Forward.z;
        }

        bool CheckReset(State st, Vector3 pos, float dt, out ResetReason why)
        {
            why = ResetReason.Fell;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) return true;
            if (pos.y < RaceWorld.KillY) return true;
            float lim = RaceWorld.Size * 0.5f + 10f;
            if (Mathf.Abs(pos.x) > lim || Mathf.Abs(pos.z) > lim) { why = ResetReason.OutOfBounds; return true; }

            Surface sf = RaceWorld.SurfaceAt(pos.x, pos.z);
            // 水:掉進海 / 深坑就重置。M7 例外:飛越缺口時車在深坑(水面)上空,只有高度降到水面附近(< GapFlyMinY)才算掉下去。
            if (SurfaceProps.Of(sf).Resets && !(pos.y > GapFlyMinY && track.OverGap(pos, st.HintS))) return true;
            if (sf == Surface.OutOfBounds) st.OffTime += dt; else st.OffTime = 0f;
            if (st.OffTime > 4f) { why = ResetReason.OffTrackTooLong; return true; }

            if (!freeRoam && racing && !st.P.Finished)
            {
                Vector3 v = st.Body.Velocity;
                if (st.Body.ThrottleHeld && v.x * v.x + v.z * v.z < 0.25f) st.StuckTime += dt; else st.StuckTime = 0f;
                if (st.StuckTime > 3f) { why = ResetReason.Stuck; return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ 裁判規則

        /// 車子現在的狀態是否讓「離開道路」合法(滑翔 / 軌道滑行 / 跑牆 / 特技 / 滯空)。
        public static bool LegitOffRoad(IRacerBody b)
        {
            var a = b as IRacerAirState;
            if (a != null) return a.LegitOffRoad;
            var k = b as RaceKart;
            return k != null && (k.Gliding || k.RailGrinding || k.WallRiding || k.InTrick || !k.Grounded);
        }

        static bool IsGliding(IRacerBody b)
        {
            var a = b as IRacerAirState;
            if (a != null) return a.Gliding;
            var k = b as RaceKart;
            return k != null && k.Gliding;
        }

        /// 飛行中穿過後面的門算合法:滑翔中,或在缺口上空(高度 > GapFlyMinY)。
        bool FlightLegit(State st, Vector3 pos)
        {
            if (IsGliding(st.Body)) return true;
            return pos.y > GapFlyMinY && track.OverGap(pos, st.HintS);
        }

        /// 這一步穿過了「下一個門之後」的哪個門(沒有 = -1)。走捷徑中,被捷徑跳過的那段門不算(那是主線的門,車子其實在捷徑上)。
        int SkippedGateCrossed(State st, Vector3 prev, Vector3 pos)
        {
            int count = track.Checkpoints.Count;
            int look = Mathf.Min(SkipLookahead, count - 1);
            for (int j = 1; j <= look; j++)
            {
                int idx = (st.NextCp + j) % count;
                if (st.OnSc != null && idx >= st.OnSc.FirstSkipped && idx <= st.OnSc.LastSkipped) continue;
                if (Crosses(track.Checkpoints[idx], prev, pos)) return idx;
            }
            return -1;
        }

        /// 依序把 NextCp .. idx 都記成通過(合法飛行斜切過幾個門時補記)。
        void CreditGatesTo(State st, int idx, float t)
        {
            for (int guard = 0; guard < SkipLookahead + 2; guard++)
            {
                Checkpoint c = track.Checkpoints[st.NextCp];
                bool last = c.Index == idx;
                PassGate(st, c, t);
                if (last) break;
            }
            st.OnSc = null; st.P.OnShortcut = false;
        }

        /// 離開走廊的程度:車子離「最近的可行駛走廊邊緣」的距離(<= 0 = 在走廊內)。走廊 = 主線路面 + 路肩 + 捷徑路面 + 缺口上空。
        float CorridorExcess(State st, Vector3 pos)
        {
            float lat;
            float s = track.Project(pos, -1f, out lat);
            float ex = Mathf.Abs(lat) - (track.HalfWidthAt(s) + CorridorShoulder);
            if (ex <= 0f) return ex;
            if (pos.y > GapFlyMinY && track.OverGap(pos, s)) return 0f;     // 缺口上空 / 滑翔路徑
            var list = track.Shortcuts;
            for (int i = 0; i < list.Count; i++)
            {
                int ia; float u, hw;
                float d = list[i].Nearest(pos.x, pos.z, out ia, out u, out hw);
                ex = Mathf.Min(ex, d - (hw + CorridorShoulder));
            }
            return ex;
        }

        bool CheckCourse(State st, Vector3 pos, float dt, out RefereeCause cause)
        {
            cause = RefereeCause.None;
            IRacerBody body = st.Body;

            // 走廊:每 0.1 s 算一次離走廊的距離(含全圈最近點搜尋),計時每步累加
            st.CorClock += dt;
            if (st.CorClock >= 0.1f) { st.CorClock = 0f; st.CorExcess = CorridorExcess(st, pos); }
            bool legit = LegitOffRoad(body);
            if (st.CorExcess > 0f && !legit) st.OffCorTime += dt; else st.OffCorTime = 0f;
            if (st.CorExcess > OffCorridorHard && !legit) st.HardTime += dt; else st.HardTime = 0f;
            if (st.OffCorTime > OffCorridorTime || st.HardTime > HardDebounce) { cause = RefereeCause.OffCourse; return true; }

            // 逆向:速度方向逆著賽道
            Vector3 v = body.Velocity; v.y = 0f;
            float sp = v.magnitude;
            bool wrong = false;
            if (sp > WrongWayMinSpeed && st.HintS >= 0f && st.CorExcess <= OffCorridorHard)   // 離走廊很遠時 HintS 可能黏到別段路,交給走廊規則
            {
                Vector3 tan = st.OnSc != null ? st.OnSc.SampleAt(st.ScD).Tangent : track.TangentAt(st.HintS);
                wrong = Vector3.Dot(v / sp, tan) < -0.5f;
            }
            st.RefWrong = wrong ? st.RefWrong + dt : 0f;
            if (st.RefWrong > WrongWayResetTime) { cause = RefereeCause.WrongWay; return true; }
            return false;
        }

        void PassGate(State st, Checkpoint cp, float t)
        {
            RacerProgress pr = st.P;
            int count = track.Checkpoints.Count;
            if (cp.Index == 0)
            {
                if (!st.LapStarted)
                {
                    st.LapStarted = true;            // 起跑穿線,不算一圈
                }
                else
                {
                    pr.Lap++;
                    float lt = t - st.LapStartTime;
                    pr.LastLapTime = lt;
                    if (pr.BestLapTime <= 0f || lt < pr.BestLapTime) pr.BestLapTime = lt;
                    st.LapStartTime = t;
                    if (LapCompleted != null) LapCompleted(pr.Id, pr.Lap);
                    if (pr.Lap >= laps)
                    {
                        pr.Finished = true;
                        pr.FinishTime = t - raceStart;
                        if (Finished != null) Finished(pr.Id);
                    }
                }
                st.LastPassed = 0;
                st.NextCp = count > 1 ? 1 : 0;
            }
            else
            {
                st.LastPassed = cp.Index;
                st.NextCp = (cp.Index + 1) % count;
            }
            pr.NextCp = st.NextCp;
        }

        void UpdateS(State st, Vector3 pos)
        {
            float lat;
            float L = track.Length;
            if (st.OnSc != null)
            {
                // 走捷徑中:捷徑走了幾成 = 主線 FromS..ToS 走了幾成(線性換算,名次不會跳)
                ShortcutPath sc = st.OnSc;
                float d = sc.Project(pos, out lat);
                if (Mathf.Abs(lat) > 14f) { st.OnSc = null; st.P.OnShortcut = false; }   // 離開捷徑太遠(回主線 / 切草地):取消捷徑狀態,下一個主線門照常要過
                else
                {
                    st.ScD = d;
                    float ss = sc.FromS + d / sc.Length * (sc.ToS - sc.FromS);
                    st.HintS = ss;
                    st.P.S = ss;
                    st.P.Total = st.P.Lap * L + ss;
                    return;
                }
            }
            float s = track.Project(pos, st.HintS, out lat);
            if (st.HintS >= 0f && Mathf.Abs(lat) > 25f) s = track.Project(pos, -1f, out lat);   // 離線太遠:局部搜尋可能找錯,改全圈
            st.HintS = s;

            float eff = s;
            if (st.NextCp == 0)   // 正在等終點線
            {
                if (!st.LapStarted) { if (s > L * 0.5f) eff = s - L; }   // 起跑線後方
                else if (s < L * 0.5f) eff = s + L;                       // 已越線但這一步還沒登記
            }
            RacerProgress pr = st.P;
            pr.S = s;
            pr.Total = pr.Lap * L + eff;
        }

        void UpdateWrongWay(State st, float dt)
        {
            Vector3 v = st.Body.Velocity; v.y = 0f;
            Vector3 f = st.Body.Forward; f.y = 0f;
            bool wrong = false;
            if (v.sqrMagnitude > 9f && f.sqrMagnitude > 1e-6f && st.HintS >= 0f)
            {
                Vector3 tan = st.OnSc != null ? st.OnSc.SampleAt(st.ScD).Tangent : track.TangentAt(st.HintS);
                wrong = Vector3.Dot(f.normalized, tan) < -0.5f;
            }
            st.WrongTime = wrong ? st.WrongTime + dt : 0f;
            st.P.WrongWay = st.WrongTime > 2f;
        }

        // ------------------------------------------------------------------ 名次(插入排序,不配置記憶體)

        // a 是否應排在 b 前面
        static bool Ahead(RacerProgress a, RacerProgress b)
        {
            if (a.Finished != b.Finished) return a.Finished;
            if (a.Finished) return a.FinishTime < b.FinishTime;
            if (a.Total != b.Total) return a.Total > b.Total;
            return a.Id < b.Id;
        }

        void Sort()
        {
            for (int i = 1; i < standings.Count; i++)
            {
                RacerProgress x = standings[i];
                int j = i - 1;
                while (j >= 0 && Ahead(x, standings[j])) { standings[j + 1] = standings[j]; j--; }
                standings[j + 1] = x;
            }
            for (int i = 0; i < standings.Count; i++) standings[i].Rank = i + 1;
        }
    }
}
