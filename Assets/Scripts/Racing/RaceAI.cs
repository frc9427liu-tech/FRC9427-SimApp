using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// 賽車 AI 對手(模組 I,03 §2;M3 不含道具)。
    /// 與玩家走同一條路徑:RaceKart.Source = RaceAI,每個 Update 呼叫 Read(dt) 回傳 KartInput。
    /// 行為:追中心線(Pure Pursuit)+ 車道偏移;依曲率提前減速(含「到彎前能煞到」的距離計算);
    /// 超車(換到前車另一側)、避開前方慢車與場上障礙;卡住時倒車、再失敗送出 RespawnDown;
    /// 橡皮筋只調 Kart.SpeedMul(極速),見 Rubberband。所有隨機用 System.Random(Seed),可重現。
    public class RaceAI : MonoBehaviour, IKartInput
    {
        // ------------------------------------------------------------ 難度表(03 §2.5,Level 0..3)
        static readonly float[] SpeedMulT   = { 0.82f, 0.90f, 0.97f, 1.00f };
        static readonly float[] GripBelief  = { 0.70f, 0.80f, 0.90f, 0.97f };
        static readonly float[] ThinkT      = { 0.60f, 0.35f, 0.20f, 0.10f };
        static readonly float[] SteerNoiseT = { 0.12f, 0.07f, 0.03f, 0.01f };
        static readonly float[] MistakeT    = { 0.30f, 0.15f, 0.06f, 0.02f };
        static readonly float[] DriftSkillT = { 0.0f, 0.4f, 0.8f, 1.0f };
        public static readonly float[] ItemSmartT = { 0.2f, 0.5f, 0.8f, 1.0f };   // M4 道具用
        static readonly float[] OvertakeT   = { 0.3f, 0.6f, 0.9f, 1.0f };
        const float RookieSpeedMul = 0.70f;                                         // 新手模式:更慢、不漂移

        /// 漂移尚未在 RaceKart 實作完成(M2)前預設關閉;K 的漂移完工且驗證後,整合者可設 true。
        public static bool EnableDrift = false;
        /// 場上障礙來源(M4 由整合者設:() => RaceItems.Hazards)。null = 不避障。
        public static Func<IReadOnlyList<RaceHazard>> HazardSource;

        // ------------------------------------------------------------ 公開欄位(契約 §3.7)
        public int Level = 1;               // 0..3
        public bool Rookie;
        public float LanePersonality;       // -1..1
        public int Seed;
        public RaceKart Kart;
        public RaceTrack Track;
        public RaceKart Rival;              // 橡皮筋參考(玩家);null = 不用
        public int TotalLaps;               // 0 = 用 Track.Laps
        public float RubberbandMul { get { return rubber; } }
        public string DebugState { get { return state; } }
        public int RecoverCount { get; private set; }

        // ------------------------------------------------------------ 內部狀態
        System.Random rng;
        float noiseSeed;
        string state = "Follow";
        float clock;
        float hintS = -1f;
        bool haveLast; Vector3 lastPos;

        float thinkT;
        float laneCur;                     // 目前車道偏移(公尺,右正),平滑追 laneGoal
        bool laneInit;
        float overtakeT, overtakeLane;
        float hazardT, hazardLane;
        float mistakeLaneT, mistakeLaneSign, lateBrakeT;
        float blockerCap = float.MaxValue;
        bool blocked;
        float rubber = 1f, rubberGoal = 1f;
        float vTCur = 20f;

        float stuckT, recoverT, lastRecoverClock = -99f;
        int recoverStreak;
        bool respawnSent;

        bool driftOn; float driftT, driftCool;

        int L { get { return Mathf.Clamp(Level, 0, 3); } }

        // ------------------------------------------------------------ 工廠(整合者呼叫)
        /// 在已建好的 AI 車上掛 RaceAI 並設為輸入來源。
        ///   index = 這台 AI 的序號(0..AiCount-1),只用來分散車道個性。
        ///   rival = 橡皮筋參考(通常是 RaceSession.Player);null = 不用橡皮筋。
        ///   cfg 提供 AiLevel / Laps / Seed;rookie = GameSession.Rookie。
        public static RaceAI Spawn(RaceKart kart, RaceTrack track, RaceConfig cfg, int index, RaceKart rival, bool rookie = false)
        {
            if (kart == null) throw new ArgumentNullException("kart");
            var ai = kart.GetComponent<RaceAI>();
            if (ai == null) ai = kart.gameObject.AddComponent<RaceAI>();
            ai.Kart = kart; ai.Track = track;
            ai.Level = Mathf.Clamp(cfg != null ? cfg.AiLevel : 1, 0, 3);
            ai.Rookie = rookie;
            ai.Rival = rival;
            ai.TotalLaps = cfg != null ? cfg.Laps : 0;
            int baseSeed = cfg != null ? cfg.Seed : 9427;
            ai.Seed = unchecked(baseSeed * 7919 + kart.KartId * 104729 + 17);
            float g = (index * 0.6180339f) % 1f;                       // 黃金比例分散,確定性
            ai.LanePersonality = g * 2f - 1f;
            ai.ResetState();
            kart.IsPlayer = false;
            kart.Source = ai;
            return ai;
        }

        /// 移除 AI(例如玩家接管或 End)。
        public static void Detach(RaceKart kart)
        {
            if (kart == null) return;
            var ai = kart.GetComponent<RaceAI>();
            if (ai != null) { if (kart.Source == (IKartInput)ai) kart.Source = NullKartInput.I; Destroy(ai); }
        }

        public void ResetState()
        {
            rng = new System.Random(Seed);
            noiseSeed = (Seed & 1023) * 0.37f;
            state = "Follow"; clock = 0f; hintS = -1f; haveLast = false;
            thinkT = (float)rng.NextDouble() * ThinkT[L];
            laneInit = false; overtakeT = hazardT = mistakeLaneT = lateBrakeT = 0f;
            stuckT = recoverT = 0f; lastRecoverClock = -99f; recoverStreak = 0; respawnSent = false;
            rubber = rubberGoal = 1f; blockerCap = float.MaxValue; blocked = false;
            driftOn = false; driftT = driftCool = 0f;
        }

        // ------------------------------------------------------------ 主迴圈
        public KartInput Read(float dt)
        {
            if (Kart != null && RaceItems.AiShouldUse(Kart, out bool itemBack, 1f)) RaceItems.TryUse(Kart, itemBack);   // AI 用道具
            KartInput inp = default;
            if (Kart == null || Track == null) return inp;
            if (rng == null) ResetState();
            dt = Mathf.Clamp(dt, 0.001f, 0.1f);
            clock += dt;

            Vector3 pos = Kart.Position;
            if (haveLast && (pos - lastPos).sqrMagnitude > 100f)       // 被傳送(重生)
            {
                hintS = -1f; laneInit = false; overtakeT = hazardT = 0f; stuckT = 0f; recoverT = 0f; driftOn = false;
            }
            lastPos = pos; haveLast = true;

            float lat;
            float s = Track.Project(pos, hintS, out lat);
            if (hintS >= 0f && Mathf.Abs(lat) > 25f) s = Track.Project(pos, -1f, out lat);
            hintS = s;
            if (!laneInit) { laneCur = lat; laneInit = true; }

            KartStats st = Kart.Stats ?? new KartStats();
            float speed = Kart.Speed;
            float speedMulLevel = Rookie ? RookieSpeedMul : SpeedMulT[L];
            float vMax = Mathf.Max(3f, Kart.MaxSpeed * speedMulLevel);

            // ---- 飛越缺口(M7):跳台底部前 ApproachDist 公尺到落地後 12 m = 缺口模式:全速(忽略難度 / 橡皮筋 / 前車的速度上限,飛得過去才重要)、
            // 走中線(跳台在路中央)、不超車、不閃避;空中(跳台前緣之後)展開滑翔翼,油門煞車都放開(中性滑翔飛最遠),只用轉向對準落地區。
            GapInfo gap = Track.GapNear(s, GapInfo.RampLength + GapInfo.ApproachDist, 12f);
            bool gapMode = gap != null;
            bool gapAir = gapMode && !Kart.Grounded && s > gap.S0 - 8f && s < gap.S1 + 6f;
            if (gapMode && s < gap.S1) vMax = Mathf.Max(vMax, Kart.MaxSpeed);

            // ---- 決策(每 Think 秒一次)
            thinkT -= dt;
            if (thinkT <= 0f)
            {
                float tk = ThinkT[L];
                thinkT += tk * (0.8f + 0.4f * (float)rng.NextDouble());
                Think(s, lat, speed, tk);
            }
            if (overtakeT > 0f) overtakeT -= dt;
            if (hazardT > 0f) hazardT -= dt;
            if (mistakeLaneT > 0f) mistakeLaneT -= dt;
            if (lateBrakeT > 0f) lateBrakeT -= dt;
            if (driftCool > 0f) driftCool -= dt;

            // 橡皮筋:平滑後寫到 Kart.SpeedMul(只調極速)
            rubber += (rubberGoal - rubber) * (1f - Mathf.Exp(-2f * dt));
            Kart.SpeedMul = gapMode ? Mathf.Max(1f, rubber) : rubber;   // 缺口模式不被橡皮筋減速

            // ---- 追點:前瞻 + 車道
            float look = 6f + 0.45f * Mathf.Max(0f, speed);
            float sLook = s + look;
            TrackSample sp = Track.SampleAt(sLook);
            float hwLook = Mathf.Max(2.5f, sp.Width * 0.5f);
            float edge = Mathf.Max(0.5f, hwLook - 1.7f);
            float curveShrink = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(sp.Curvature) / 0.04f));

            float laneGoal = LanePersonality * 0.6f * hwLook * curveShrink;
            if (overtakeT > 0f) laneGoal = overtakeLane;
            if (hazardT > 0f) laneGoal = hazardLane;
            if (mistakeLaneT > 0f) laneGoal = mistakeLaneSign * 0.9f * edge;
            if (gapMode) laneGoal = 0f;
            laneGoal = Mathf.Clamp(laneGoal, -edge, edge);
            laneCur = Mathf.MoveTowards(laneCur, laneGoal, 3.5f * dt);

            Vector3 target = sp.Pos + sp.Right * laneCur;
            Vector3 to = target - pos; to.y = 0f;
            Vector3 fwd = Kart.Forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            float angDeg = to.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fwd, to, Vector3.up) : 0f;   // 右正
            float angRad = angDeg * Mathf.Deg2Rad;

            // ---- 目標速度
            float b = (Rookie ? GripBelief[0] : GripBelief[L]) * 0.8f;
            float vT = vMax;
            float lowR = st.TurnRateLow * Mathf.Deg2Rad, highR = st.TurnRateHigh * Mathf.Deg2Rad;
            float topS = Mathf.Max(1f, st.TopSpeed);
            const float aiDecel = 14f;
            if (lateBrakeT <= 0f)
            {
                for (float d = 0f; d <= 60f; d += 3f)
                {
                    float kap = Mathf.Abs(Track.CurvatureAt(s + d));
                    // 轉向能力:v·κ ≤ b·rate(v),rate(v) = low + (high-low)·v/top  →  解出 v 上限
                    float vi = b * lowR / (kap + b * (lowR - highR) / topS);
                    vi = Mathf.Clamp(vi, 5f, vMax);
                    float allowed = Mathf.Sqrt(vi * vi + 2f * aiDecel * Mathf.Max(0f, d - 3f));
                    if (allowed < vT) vT = allowed;
                }
            }
            // 車頭偏離目標太多(剛撞牆/打滑)先慢下來轉
            vT = Mathf.Min(vT, vMax * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(Mathf.Abs(angDeg) / 60f)));
            if (blocked && !gapMode) vT = Mathf.Min(vT, Mathf.Max(3f, blockerCap));
            vTCur = vT;

            // ---- 卡住 / 復原
            bool stunned = Kart.Stunned;
            if (recoverT <= 0f)
            {
                if (!stunned && Mathf.Abs(speed) < 1.0f) stuckT += dt; else stuckT = Mathf.Max(0f, stuckT - 2f * dt);
                if (stuckT > 1.5f)
                {
                    stuckT = 0f; recoverT = 0.9f; RecoverCount++;
                    recoverStreak = (clock - lastRecoverClock < 10f) ? recoverStreak + 1 : 1;
                    lastRecoverClock = clock;
                }
            }

            if (recoverT > 0f)
            {
                recoverT -= dt;
                state = "Recover";
                inp.Brake = 1f; inp.Throttle = 0f;
                inp.Steer = Mathf.Clamp(-angRad * 2.2f, -1f, 1f);       // 倒車時轉向反向
                if (recoverStreak >= 2 && !respawnSent) { inp.RespawnDown = true; respawnSent = true; recoverStreak = 0; recoverT = 0f; }
                return inp;
            }
            respawnSent = false;

            // ---- 轉向 / 油門 / 煞車
            float noise = (Mathf.PerlinNoise(clock * 0.8f, noiseSeed) - 0.5f) * 2f * (Rookie ? SteerNoiseT[0] : SteerNoiseT[L]);
            inp.Steer = Mathf.Clamp(angRad * 2.2f + noise, -1f, 1f);

            if (gapAir)
            {
                if (!Kart.Gliding) Kart.BeginGlide();   // 跳台的 GliderRamp 碰撞體通常已經展開了;沒有的話在這裡補
                inp.Throttle = 0f; inp.Brake = 0f;
                state = "Glide";
                return inp;
            }

            float err = speed - vT;
            if (err > 1.5f) { inp.Throttle = 0f; inp.Brake = Mathf.Clamp01((err - 1.5f) / 3f); }
            else if (err > -1f) { inp.Throttle = Mathf.Clamp01(0.5f - err * 0.5f); inp.Brake = 0f; }
            else inp.Throttle = 1f;

            // ---- 漂移(預設關閉)
            if (EnableDrift && !Rookie && DriftSkillT[L] > 0f) DriftLogic(ref inp, s, speed, vMax);

            if (hazardT > 0f || (blocked && overtakeT <= 0f)) state = "Avoid";
            else if (overtakeT > 0f) state = "Overtake";
            else state = "Follow";
            return inp;
        }

        // ------------------------------------------------------------ 決策
        void Think(float s, float lat, float speed, float thinkDt)
        {
            int lv = L;
            float hw = Mathf.Max(2.5f, Track.HalfWidthAt(s));
            float edge = Mathf.Max(0.5f, hw - 1.7f);
            Vector3 pos = Kart.Position;
            Vector3 fwd = Kart.Forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // 橡皮筋
            rubberGoal = 1f;
            if (Rival != null && Rival != Kart && Rival.Progress != null && Kart.Progress != null)
            {
                int laps = TotalLaps > 0 ? TotalLaps : Track.Laps;
                bool lastLap25 = laps > 0 && Kart.Progress.Lap >= laps - 1 && Kart.Progress.S > 0.75f * Track.Length;
                bool visible = (Rival.Position - pos).sqrMagnitude < Rubberband.VisibleRange * Rubberband.VisibleRange;
                float delta = Kart.Progress.Total - Rival.Progress.Total;
                rubberGoal = Rubberband.Mul(delta, lv, lastLap25, visible);
            }

            // 前方的車
            blocked = false; blockerCap = float.MaxValue;
            RaceKart near = null; float nearF = 1e9f;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == Kart) continue;
                Vector3 d = o.Position - pos; d.y = 0f;
                float f = Vector3.Dot(d, fwd);
                if (f <= 0.5f || f > 25f) continue;
                float r = Vector3.Dot(d, right);
                if (Mathf.Abs(r) > 2.2f) continue;
                if (f < nearF) { nearF = f; near = o; }
            }
            if (near != null)
            {
                bool slower = near.Speed < vTCur - 1.5f;
                if (nearF < 12f && slower && overtakeT <= 0f && rng.NextDouble() < OvertakeT[Rookie ? 0 : lv])
                {
                    float olat;
                    float hint = (near.Progress != null) ? near.Progress.S : -1f;
                    Track.Project(near.Position, hint, out olat);
                    float roomL = olat + edge, roomR = edge - olat;
                    float side = roomR >= roomL ? 1f : -1f;
                    overtakeLane = Mathf.Clamp(olat + side * 2.6f, -edge, edge);
                    overtakeT = 2.5f;
                }
                // 仍在正前方窄走廊內:不要撞上去,跟車速度
                float r2 = Vector3.Dot(near.Position - pos, right);
                if (overtakeT <= 0f || (nearF < 6f && Mathf.Abs(r2) < 1.6f))
                {
                    blocked = true;
                    blockerCap = near.Speed + 1.5f + 0.35f * nearF;
                }
            }

            // 場上障礙(M4 油漬/彈)
            if (HazardSource != null)
            {
                var hz = HazardSource();
                if (hz != null)
                {
                    for (int i = 0; i < hz.Count; i++)
                    {
                        RaceHazard h = hz[i];
                        if (h.OwnerKartId == Kart.KartId && h.Vel.sqrMagnitude > 1f) continue;
                        Vector3 d = h.Pos - pos; d.y = 0f;
                        float f = Vector3.Dot(d, fwd);
                        if (f < 0f || f > 25f) continue;
                        float r = Vector3.Dot(d, right);
                        if (Mathf.Abs(r) > h.Radius + 1.2f) continue;
                        float olat;
                        Track.Project(h.Pos, -1f, out olat);
                        hazardLane = Mathf.Clamp(olat + (r >= 0f ? -1f : 1f) * (h.Radius + 1.8f), -edge, edge);
                        hazardT = 1.0f;
                        break;
                    }
                }
            }

            // 犯錯(每圈期望次數 MistakeRate)
            float rate = Rookie ? MistakeT[0] : MistakeT[lv];
            float p = rate * Mathf.Max(0f, speed) * thinkDt / Mathf.Max(100f, Track.Length);
            if (rng.NextDouble() < p)
            {
                if (rng.NextDouble() < 0.5) lateBrakeT = 0.6f;
                else { mistakeLaneT = 1.0f; mistakeLaneSign = rng.NextDouble() < 0.5 ? -1f : 1f; }
            }
        }

        void DriftLogic(ref KartInput inp, float s, float speed, float vMax)
        {
            float k1 = Track.CurvatureAt(s + 15f), k2 = Track.CurvatureAt(s + 27f);
            if (!driftOn)
            {
                if (driftCool <= 0f && speed > 0.6f * vMax && Mathf.Abs(k1) > 0.04f && Mathf.Abs(k2) > 0.04f && Math.Sign(k1) == Math.Sign(k2))
                {
                    if (rng.NextDouble() < DriftSkillT[L]) { driftOn = true; driftT = 0f; inp.DriftDown = true; }
                    else driftCool = 3f;
                }
            }
            if (driftOn)
            {
                driftT += Time.deltaTime;
                inp.DriftHeld = true;
                if (Mathf.Abs(Track.CurvatureAt(s + 4f)) < 0.02f || driftT > 6f) { driftOn = false; inp.DriftHeld = false; driftCool = 2f; }
            }
        }
    }
}
