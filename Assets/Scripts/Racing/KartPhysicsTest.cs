using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FrcSim
{
    /// 腳本化輸入:Script(frame) 依「讀了幾次」回傳輸入(決定性測試用);沒有 Script 就回傳 Cur(測試協程隨時改)。
    public sealed class ScriptedInput : IKartInput
    {
        public KartInput Cur;
        public Func<int, KartInput> Script;
        int frame;
        public KartInput Read(float dt) { return Script != null ? Script(frame++) : Cur; }
    }

    /// 依「已讀幾秒」回傳輸入(F(t),t = 這次 Read 之前累計的 dt 總和)。captureFramerate 下 dt 固定,所以 30 / 60 / 144 fps 都可重現。
    public sealed class TimedInput : IKartInput
    {
        public Func<float, KartInput> F;
        float t;
        public KartInput Read(float dt) { KartInput r = F != null ? F(t) : default; t += dt; return r; }
    }

    /// -karttest:賽車物理自動測試(模組 K,01 §6 測試 1~13;9 = 車對車四種情境、13 = 8 台壓力)。
    /// 用法:FRC9427-Sim.exe -karttest -batchmode -nographics。結果寫在專案(或 exe)旁的 karttest.txt:每項一行 PASS/FAIL + 數據,
    /// 全部通過最後一行才是單獨的 PASS,否則是 FAIL。
    /// 測試用 Time.captureFramerate = 60 讓每個畫面剛好跑一個物理步(跑得快、而且可重現)。
    /// 不改 SimBootstrap:自己用 RuntimeInitializeOnLoadMethod 檢查命令列旗標,等一幀再開始。
    public class KartPhysicsTest : MonoBehaviour
    {
        static readonly WaitForFixedUpdate WFF = new WaitForFixedUpdate();
        const float DT = 1f / 60f;

        readonly StringBuilder log = new StringBuilder();
        readonly System.Diagnostics.Stopwatch wall = System.Diagnostics.Stopwatch.StartNew();
        KartTestTrack track;
        int laneIdx = -1, pairLane = -1, kartSeq, fails, total;
        bool done;
        float savedFixed, savedScale; int savedCapture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-karttest") < 0) return;
            var go = new GameObject("KartPhysicsTest");
            DontDestroyOnLoad(go);
            go.AddComponent<KartPhysicsTest>();
        }

        IEnumerator Start()
        {
            yield return null;   // 等一幀,讓其他啟動流程跑完
            savedFixed = Time.fixedDeltaTime; savedScale = Time.timeScale; savedCapture = Time.captureFramerate;
            Time.timeScale = 1f; Time.fixedDeltaTime = DT; Time.captureFramerate = 60;
            RaceKart.IgnoreMenuBlock = true;
            RaceKart.Ground = null;
            log.AppendLine("karttest  fixedDt=" + Time.fixedDeltaTime.ToString("0.0000") + "  captureFramerate=" + Time.captureFramerate
                           + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            track = KartTestTrack.Build(new Vector3(1000f, 50f, 1000f));
            yield return null;

            yield return StartCoroutine(Run("1 accel", T1_Accel()));
            yield return StartCoroutine(Run("2 brake", T2_Brake()));
            yield return StartCoroutine(Run("3 turn", T3_Turn()));
            yield return StartCoroutine(Run("4 drift", T4_Drift()));
            yield return StartCoroutine(Run("5a wall 90deg", WallCase("5a wall 90deg", 0f, false)));
            yield return StartCoroutine(Run("5b wall 45deg", WallCase("5b wall 45deg", 45f, false)));
            yield return StartCoroutine(Run("5c wall 90deg boost", WallCase("5c wall 90deg boost", 0f, true)));
            yield return StartCoroutine(Run("6a slope 25deg", T6_Slope25()));
            yield return StartCoroutine(Run("6b slope 45deg", T6_Slope45()));
            yield return StartCoroutine(Run("7 jump", T7_Jump()));
            yield return StartCoroutine(Run("8 tiles", T8_Tiles()));
            yield return StartCoroutine(T9_KartPairs());
            yield return StartCoroutine(Run("10 api", T10_Api()));
            yield return StartCoroutine(Run("11 determinism", T11_Determinism()));
            yield return StartCoroutine(Run("12 framerate", T12_Framerate()));

            // ---- 模組 K5/K6:滑翔 / 特技 / 金幣 / 起跑 / 排氣量 / 四件套(另開一塊 800 x 800 測試場,不佔用上面的車道)----
            KartTestTrack mainTrack = track;
            track = KartTestTrack.Build(new Vector3(3000f, 50f, 1000f));
            laneIdx = -1;
            yield return null;
            yield return StartCoroutine(Run("K1 glide", K1_Glide()));
            yield return StartCoroutine(Run("K1d fixed launch", K1d_FixedLaunch()));
            yield return StartCoroutine(Run("K2 glide wall", K2_GlideWall()));
            yield return StartCoroutine(Run("K3 auto glide", K3_AutoGlide()));
            yield return StartCoroutine(Run("K4 trick", K4_Trick()));
            yield return StartCoroutine(Run("K5 coins", K5_Coins()));
            yield return StartCoroutine(Run("K6 start boost", K6_StartBoost()));
            yield return StartCoroutine(Run("K7 cc", K7_Cc()));
            yield return StartCoroutine(Run("K8 loadout", K8_Loadout()));

            // ---- 世界式移動技:集氣跳 / 跑牆 / 軌道滑行 / 特技無敵(另開一塊測試場,每個案例結束都清掉牆與軌道)----
            track.Destroy();
            track = KartTestTrack.Build(new Vector3(5000f, 50f, 1000f));
            laneIdx = -1;
            yield return null;
            yield return StartCoroutine(Run("K9a charge jump Lv0", K9_Case("Lv0 (0.3 s): no jump", 18, 0, true)));
            yield return StartCoroutine(Run("K9b charge jump Lv1", K9_Case("Lv1 (0.8 s)", 48, 1, true)));
            yield return StartCoroutine(Run("K9c charge jump Lv3", K9_Case("Lv3 (2.0 s): apex, trick invincible, landing boost", 120, 3, true)));
            yield return StartCoroutine(Run("K9d charge jump off", K9_Case("AllowTech=false: no charge", 120, 3, false)));
            yield return StartCoroutine(Run("K10 steer rule", K10_SteerRule()));
            yield return StartCoroutine(Run("K11 wall ride", K11_WallRide()));
            yield return StartCoroutine(Run("K12 rail", K12_Rail()));
            yield return StartCoroutine(Run("K13 trick invincible toggle", K13_TrickInvToggle()));
            KartRails.Clear();
            track.Destroy();
            track = mainTrack;

            yield return StartCoroutine(Run("13 stress", T13_Stress()));
            Finish();
        }

        void Update()
        {
            if (!done && wall.Elapsed.TotalSeconds > 900.0)
            {
                Report("timeout", false, "watchdog 900 s");
                Finish();
            }
        }

        // ------------------------------------------------------------ 框架
        IEnumerator Run(string name, IEnumerator body)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e)
                {
                    Report(name, false, "exception " + e.GetType().Name + ": " + e.Message);
                    yield break;
                }
                yield return cur;
            }
        }

        void Report(string name, bool ok, string detail)
        {
            total++; if (!ok) fails++;
            string line = "[" + name + "] " + (ok ? "PASS" : "FAIL") + "  " + detail;
            log.AppendLine(line);
            Debug.Log("karttest " + line);
        }

        void Finish()
        {
            if (done) return;
            done = true;
            Time.fixedDeltaTime = savedFixed; Time.timeScale = savedScale; Time.captureFramerate = savedCapture;
            RaceKart.IgnoreMenuBlock = false;
            bool all = fails == 0 && total > 0;
            log.AppendLine(all ? "PASS" : "FAIL (" + fails + " of " + total + ")");
            try
            {
                string dir = Path.GetDirectoryName(Application.dataPath);
                File.WriteAllText(Path.Combine(dir, "karttest.txt"), log.ToString());
            }
            catch (Exception e) { Debug.LogError("karttest write error: " + e.Message); }
            Application.Quit();
        }

        float NextLane() { laneIdx++; return -375f + 50f * laneIdx; }   // 50 m 一道,最多 16 道都在 800 m 地面內

        RaceKart Spawn(float lx, float lz, float yaw, IKartInput src, KartStats st)
        {
            var k = RaceKart.Create(null, track.Start(new Vector3(lx, 0f, lz), yaw), st ?? KartPresets.For(KartStyle.Brick, 150), 100 + kartSeq, "T" + kartSeq);
            kartSeq++;
            k.Source = src;
            return k;
        }

        static void Despawn(RaceKart k) { if (k != null) UnityEngine.Object.Destroy(k.gameObject); }
        static Vector3 H(Vector3 v) { v.y = 0f; return v; }

        static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
        {
            float ab = H(b - a).magnitude, bc = H(c - b).magnitude, ca = H(a - c).magnitude;
            float area2 = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
            if (area2 < 1e-6f) return float.PositiveInfinity;
            return ab * bc * ca / (2f * area2);
        }

        // ------------------------------------------------------------ 測試 1:平地全油門
        IEnumerator T1_Accel()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -300f, 0f, src, st);
            for (int i = 0; i < 12; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
            float peak = 0f, t90 = -1f, t = 0f;
            for (int i = 0; i < 270; i++)   // 4.5 s
            {
                yield return WFF; t += DT;
                float sp = k.Speed;
                if (sp > peak) peak = sp;
                if (t90 < 0f && sp >= 0.9f * st.TopSpeed) t90 = t;
            }
            bool ok = Mathf.Abs(peak - st.TopSpeed) <= 0.03f * st.TopSpeed && t90 >= 2.0f && t90 <= 2.8f && k.Grounded;
            Report("1 accel", ok, "peak=" + peak.ToString("0.00") + " m/s (Vtop " + st.TopSpeed.ToString("0.0") + " +-3%)  t90=" + t90.ToString("0.00")
                                  + " s (2.0~2.8)  grounded=" + k.Grounded);
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 2:最高速煞車
        IEnumerator T2_Brake()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -300f, 0f, src, st);
            src.Cur = new KartInput { Throttle = 1f };
            for (int i = 0; i < 280; i++) yield return WFF;
            Vector3 p0 = k.Position; float v0 = k.Speed;
            src.Cur = new KartInput { Brake = 1f };
            int n = 0;
            while (k.Speed > 0.2f && n < 300) { yield return WFF; n++; }
            float dist = H(k.Position - p0).magnitude;
            bool ok = v0 > 0.95f * st.TopSpeed && dist < 12f && k.Speed <= 0.2f;
            Report("2 brake", ok, "from " + v0.ToString("0.0") + " m/s  stopDist=" + dist.ToString("0.00") + " m (<12)  steps=" + n);
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 3:最高速滿舵繞圈
        IEnumerator T3_Turn()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -300f, 0f, src, st);
            src.Cur = new KartInput { Throttle = 1f };
            for (int i = 0; i < 280; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f, Steer = 1f };
            var pts = new List<Vector3>(); var sps = new List<float>();
            for (int i = 0; i < 300; i++) { yield return WFF; pts.Add(k.Position); sps.Add(k.Speed); }
            float r1 = Circumradius(pts[120], pts[168], pts[216]);
            float r2 = Circumradius(pts[180], pts[228], pts[276]);
            bool ok = r1 >= 14f && r1 <= 22f && r2 >= 14f && r2 <= 22f && sps[216] > 0.9f * st.TopSpeed && k.Grounded;
            Report("3 turn", ok, "radius=" + r1.ToString("0.0") + " / " + r2.ToString("0.0") + " m (14~22)  speed=" + sps[216].ToString("0.0") + " m/s");
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 5:撞牆
        // 90 度:撞後滑行(不踩油門)0.5 s 後速度要 > 3;45 度同樣。穿牆量用「車身最前緣越過牆面」量,< 0.05 m。
        IEnumerator WallCase(string name, float wallYaw, bool boost)
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            GameObject wallGo = track.AddWall("Wall", new Vector3(lx, 0f, 0f), wallYaw, 50f, 8f, 2f);
            RaceKart k = Spawn(lx, -110f, 0f, src, st);
            Vector3 wallCenter = wallGo.transform.position;
            Vector3 nW = -wallGo.transform.forward;
            if (Vector3.Dot(nW, k.Position - wallCenter) < 0f) nW = -nW;
            Vector3 face = wallCenter + nW * 1f;     // 厚 2 m
            bool bumped = false;
            Action<RaceKart, KartBump> onBump = (who, b) => { if (who == k) bumped = true; };
            RaceKart.Bumped += onBump;

            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
            if (boost) k.Boost(6f, 0.45f);
            float minSd = float.MaxValue, maxSpeed = 0f; int bumpStep = -1, step = 0;
            while (step < 700)
            {
                yield return WFF; step++;
                float ext = Mathf.Max(0.45f, 0.35f * Mathf.Abs(Vector3.Dot(k.Forward, nW)) + 0.40f);
                float sd = Vector3.Dot(k.Position - face, nW) - ext;
                if (sd < minSd) minSd = sd;
                if (k.Speed > maxSpeed) maxSpeed = k.Speed;
                if (bumpStep < 0 && bumped) { bumpStep = step; src.Cur = default; }
                if (bumpStep >= 0 && step >= bumpStep + 30) break;
            }
            RaceKart.Bumped -= onBump;
            float pen = Mathf.Max(0f, -minSd);
            float after = H(k.Velocity).magnitude;
            bool ok = bumpStep >= 0 && pen < 0.05f && after > 3f && IsFiniteV(k.Position);
            Report(name, ok, "hit=" + (bumpStep >= 0) + "  approach=" + maxSpeed.ToString("0.0") + " m/s  penetration=" + pen.ToString("0.000")
                             + " m (<0.05)  speed 0.5 s after=" + after.ToString("0.0") + " m/s (>3)");
            Despawn(k); yield return null;
        }

        static bool IsFiniteV(Vector3 v) { return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)); }

        // ------------------------------------------------------------ 測試 6:坡
        IEnumerator T6_Slope25()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            const float z0 = -140f, lh = 20f;
            float rise = lh * Mathf.Tan(25f * Mathf.Deg2Rad);
            track.AddBox("Slope25", new Vector3(lx, rise * 0.5f, z0 + lh * 0.5f), 0f, 25f, new Vector3(20f, 1f, lh / Mathf.Cos(25f * Mathf.Deg2Rad)), Surface.Road);
            track.AddBox("Plateau", new Vector3(lx, rise, z0 + lh + 50f), 0f, 0f, new Vector3(20f, 2f, 100f), Surface.Road);
            RaceKart k = Spawn(lx, -250f, 0f, src, st);
            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
            float minSp = float.MaxValue; bool sawSlope = false;
            for (int i = 0; i < 500; i++)
            {
                yield return WFF;
                float z = k.Position.z - track.Origin.z;
                if (z > z0 + 3f && z < z0 + lh - 1f) { sawSlope = true; if (k.Speed < minSp) minSp = k.Speed; }
            }
            float y = k.Position.y - track.Origin.y;
            bool ok = sawSlope && minSp >= 0.6f * st.TopSpeed && y > rise + 0.3f && k.Grounded;
            Report("6a slope 25deg", ok, "minSpeedOnSlope=" + minSp.ToString("0.0") + " m/s (>=" + (0.6f * st.TopSpeed).ToString("0.0") + ")  endHeight="
                                         + y.ToString("0.00") + " m (plateau " + rise.ToString("0.00") + ")  grounded=" + k.Grounded);
            Despawn(k); yield return null;
        }

        IEnumerator T6_Slope45()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            const float z0 = -140f, lh = 10f;
            track.AddBox("Slope45", new Vector3(lx, lh * 0.5f, z0 + lh * 0.5f), 0f, 45f, new Vector3(20f, 1f, lh * 1.41421356f), Surface.Road);
            track.AddBox("Plateau45", new Vector3(lx, lh, z0 + lh + 30f), 0f, 0f, new Vector3(20f, 2f, 60f), Surface.Road);
            RaceKart k = Spawn(lx, -250f, 0f, src, st);
            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
            float maxY = 0f, maxZ = -1000f;
            for (int i = 0; i < 420; i++)
            {
                yield return WFF;
                maxY = Mathf.Max(maxY, k.Position.y - track.Origin.y);
                maxZ = Mathf.Max(maxZ, k.Position.z - track.Origin.z);
            }
            float gain = maxY - RaceKart.RideDist;
            bool ok = gain < 1.0f && maxZ < z0 + 3f;
            Report("6b slope 45deg", ok, "maxHeightGain=" + gain.ToString("0.00") + " m (<1.0)  maxZ=" + (maxZ - z0).ToString("0.00") + " m past slope base (<3)");
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 4:漂移 + 迷你渦輪
        // 最高速 -> 按漂移鍵並一直往內壓 2.0 s -> 放開。要求:鎖定右漂、充能升級事件依序 1、2(、3)、放開時至少 Lv2、
        // 放開後 BoostStart 的秒數 = 該等級的秒數、放開後 0.5~1.0 s 之間的速度峰值 > 1.2 Vtop。
        IEnumerator T4_Drift()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new TimedInput();
            bool pressed = false;
            src.F = t =>
            {
                var ki = new KartInput { Throttle = 1f };
                if (t >= 4.5f && t < 6.5f)
                {
                    ki.Steer = 1f; ki.DriftHeld = true;
                    if (!pressed) { pressed = true; ki.DriftDown = true; }
                }
                return ki;
            };
            RaceKart k = Spawn(NextLane(), -300f, 0f, src, st);
            var levels = new List<int>(); var boosts = new List<float>();
            Action<RaceKart, int> onLv = (w, l) => { if (w == k) levels.Add(l); };
            Action<RaceKart, float> onBo = (w, sec) => { if (w == k) boosts.Add(sec); };
            RaceKart.DriftLevelUp += onLv; RaceKart.BoostStart += onBo;

            bool sawDrift = false, wasDrift = false; int lvAtRelease = 0, dirSeen = 0;
            float t0 = 0f, releaseT = -1f, maxAfter = 0f, driftSecs = 0f, minDriftSpeed = float.MaxValue;
            for (int i = 0; i < 600; i++)
            {
                yield return WFF; t0 += DT;
                if (k.Drifting)
                {
                    sawDrift = true; wasDrift = true; lvAtRelease = k.DriftLevel; dirSeen = k.DriftDir; driftSecs += DT;
                    if (k.Speed < minDriftSpeed) minDriftSpeed = k.Speed;
                }
                else if (wasDrift && releaseT < 0f) releaseT = t0;
                if (releaseT >= 0f && t0 - releaseT >= 0.5f && t0 - releaseT <= 1.0f && k.Speed > maxAfter) maxAfter = k.Speed;
            }
            RaceKart.DriftLevelUp -= onLv; RaceKart.BoostStart -= onBo;
            bool order = levels.Count >= 2 && levels[0] == 1 && levels[1] == 2 && (levels.Count < 3 || levels[2] == 3);
            bool boostOk = boosts.Count >= 1 && Mathf.Abs(boosts[0] - KartMath.DriftBoostSeconds(lvAtRelease)) < 0.01f;
            bool ok = sawDrift && dirSeen == 1 && lvAtRelease >= 2 && order && boostOk && maxAfter > 1.2f * st.TopSpeed && IsFiniteV(k.Position);
            Report("4 drift", ok, "drift=" + sawDrift + " dir=" + dirSeen + " (want 1)  driftTime=" + driftSecs.ToString("0.00") + " s  minSpeedInDrift=" + minDriftSpeed.ToString("0.0")
                                  + "  levelUps=[" + string.Join(",", levels) + "]  levelAtRelease=" + lvAtRelease + " (>=2)  boost=" + (boosts.Count > 0 ? boosts[0].ToString("0.0") : "none")
                                  + " s  speed 0.5~1.0 s after release=" + maxAfter.ToString("0.0") + " m/s (>" + (1.2f * st.TopSpeed).ToString("0.0") + ")");
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 7:跳台
        // 20 度、水平長 10 m 的跳台(Surface.Ramp)全速衝上去飛出。要求:離地、AirTime 0.4~1.2 s、落地後車身朝上(up·n > 0.95)、
        // 落地後 0.67 s 內垂直彈起 < 0.3 m、有發 Landed 事件。
        IEnumerator T7_Jump()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            const float z0 = -100f, run = 10f, pitch = 20f;
            float rise = run * Mathf.Tan(pitch * Mathf.Deg2Rad);
            track.AddBox("JumpRamp", new Vector3(lx, rise * 0.5f, z0 + run * 0.5f), 0f, pitch, new Vector3(20f, 1f, run / Mathf.Cos(pitch * Mathf.Deg2Rad)), Surface.Ramp);
            RaceKart k = Spawn(lx, -250f, 0f, src, st);
            int landedEvents = 0;
            Action<RaceKart> onLanded = w => { if (w == k) landedEvents++; };
            RaceKart.Landed += onLanded;
            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };

            bool left = false, landedBack = false; float maxAir = 0f, peakY = 0f;
            int landStep = -1, step = 0; float upDot = 0f, bounce = 0f, landVy = 0f;
            while (step < 800)
            {
                yield return WFF; step++;
                float z = k.Position.z - track.Origin.z, y = k.Position.y - track.Origin.y;
                if (!left && !k.Grounded && z > z0 + 1f) left = true;
                if (left && k.AirTime > maxAir) maxAir = k.AirTime;
                if (left && y > peakY) peakY = y;
                if (left && !landedBack && k.Grounded) { landedBack = true; landStep = step; landVy = k.Velocity.y; }
                if (landedBack)
                {
                    int since = step - landStep;
                    if (since == 3) upDot = Vector3.Dot(k.transform.up, k.GroundNormal);
                    if (since > 0 && y - RaceKart.RideDist > bounce) bounce = y - RaceKart.RideDist;
                    if (since >= 40) break;
                }
            }
            RaceKart.Landed -= onLanded;
            bool ok = left && landedBack && maxAir >= 0.4f && maxAir <= 1.2f && upDot > 0.95f && bounce < 0.3f && landedEvents >= 1 && IsFiniteV(k.Position);
            Report("7 jump", ok, "left=" + left + " airTime=" + maxAir.ToString("0.00") + " s (0.4~1.2)  peakHeight=" + peakY.ToString("0.00") + " m  up.n=" + upDot.ToString("0.000")
                                 + " (>0.95)  bounceAfterLanding=" + bounce.ToString("0.000") + " m (<0.3)  landedEvents=" + landedEvents);
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 8:1 mm 錯位地磚
        IEnumerator T8_Tiles()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            const float z0 = -200f; const int n = 20;
            for (int i = 0; i < n; i++)
                track.AddBox("Tile" + i, new Vector3(lx, 0.002f + 0.001f * (i % 2), z0 + 4f * i + 2f), 0f, 0f, new Vector3(20f, 1f, 4f), Surface.Road);
            RaceKart k = Spawn(lx, -300f, 0f, src, st);
            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
            float maxVy = 0f, speedAt = 0f; bool allGrounded = true, reached = false; int steps = 0;
            for (int i = 0; i < 600; i++)
            {
                yield return WFF;
                float z = k.Position.z - track.Origin.z;
                if (z >= z0 - 2f && z <= z0 + 4f * n)
                {
                    steps++;
                    reached = true;
                    float vy = Mathf.Abs(k.Velocity.y);
                    if (vy > maxVy) maxVy = vy;
                    if (!k.Grounded) allGrounded = false;
                    speedAt = k.Speed;
                }
                if (z > z0 + 4f * n + 1f) break;
            }
            bool ok = reached && steps > 60 && maxVy < 1f && allGrounded && speedAt > 0.9f * st.TopSpeed;
            Report("8 tiles", ok, "steps on tiles=" + steps + "  maxVerticalVelocity=" + maxVy.ToString("0.000") + " m/s (<1)  alwaysGrounded=" + allGrounded + "  speed=" + speedAt.ToString("0.0") + " m/s");
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ 測試 9:車對車
        // 四種情境都用「滑行」(沒有油門),這樣動能只會被阻力減少,才能嚴格檢查碰撞不增加能量。
        // 共同條件:有發 Bumped(OtherKartId = 對方)、0.15 s 內同一對只發一次、穿透 < 0.1 m、無 NaN、1 s 後沒有重疊且正在分開、能量不增加(無敵情境除外)。
        float NextPairLane() { pairLane++; return -350f + 60f * pairLane; }   // 另開一區 (z 60~120),不佔用上面的 50 m 車道

        static float Ke(RaceKart k) { Vector3 v = H(k.Velocity); return 0.5f * k.Stats.Mass * v.sqrMagnitude; }

        IEnumerator PairCase(string name, string kind, KartStats stA, KartStats stB, float ax, float az, float yawA, float spA,
                             float bx, float bz, float yawB, float spB, bool aInv)
        {
            RaceKart a = Spawn(ax, az, yawA, new ScriptedInput(), stA);
            RaceKart b = Spawn(bx, bz, yawB, new ScriptedInput(), stB);
            var evSteps = new List<int>();
            int step = 0;
            Action<RaceKart, KartBump> on = (who, bp) => { if (who == a && bp.OtherKartId == b.KartId) evSteps.Add(step); };
            RaceKart.Bumped += on;
            for (int i = 0; i < 6; i++) yield return WFF;
            if (aInv) a.SetInvincible(20f);
            a.ApplyImpulse(a.Forward * spA); b.ApplyImpulse(b.Forward * spB);

            float prevKe = 0.5f * stA.Mass * spA * spA + 0.5f * stB.Mass * spB * spB;
            Vector3 vaPrev = a.Forward * spA, vbPrev = b.Forward * spB, vaBefore = vaPrev, vbBefore = vbPrev, vaAfter = default, vbAfter = default;
            float closingAfterCooldown = 0f, keBefore = 0f, keAfter0 = 0f, keAfter2 = 0f, maxPen = 0f, maxBLateral = 0f;
            bool stunnedSeen = false, finite = true; int first = -1;
            while (step < 300)
            {
                yield return WFF; step++;
                float pen = RaceKart.BodyOverlap(a, b); if (pen > maxPen) maxPen = pen;
                float ke = Ke(a) + Ke(b);
                finite &= IsFiniteV(a.Position) && IsFiniteV(b.Position) && IsFiniteV(a.Velocity) && IsFiniteV(b.Velocity);
                if (first < 0 && evSteps.Count > 0) { first = evSteps[0]; keBefore = prevKe; vaBefore = vaPrev; vbBefore = vbPrev; keAfter0 = ke; }
                if (first >= 0 && step == first + 1 && b.Stunned) stunnedSeen = true;
                if (first >= 0 && step == first + 9) closingAfterCooldown = Vector3.Dot(H(a.Velocity) - H(b.Velocity), H(b.Position - a.Position).normalized);
                if (first >= 0 && step == first + 2) { keAfter2 = ke; vaAfter = H(a.Velocity); vbAfter = H(b.Velocity); }
                if (first >= 0 && step > first && step <= first + 6 && b.Velocity.x > maxBLateral) maxBLateral = b.Velocity.x;
                prevKe = ke; vaPrev = H(a.Velocity); vbPrev = H(b.Velocity);
                if (first >= 0 && step >= first + 60) break;
            }
            RaceKart.Bumped -= on;

            int inWindow = 0; for (int i = 0; i < evSteps.Count; i++) if (first >= 0 && evSteps[i] - first <= 9) inWindow++;   // 0.15 s = 9 步
            Vector3 dAB = H(b.Position - a.Position); Vector3 nAB = dAB.sqrMagnitude > 1e-6f ? dAB.normalized : Vector3.forward;
            float endClosing = Vector3.Dot(H(a.Velocity) - H(b.Velocity), nAB);
            // 無敵情境:自己不減速、被撞的暈眩車依契約以 20 m/s^2 減速,無敵車必然再追上並再撞(週期性),所以 1 s 後的瞬間相對速度沒有意義;
            // 改檢查「彈開冷卻結束時(碰撞後 9 步)」兩車已在分開,其餘情境維持原本的收尾判定。
            if (kind == "star") endClosing = closingAfterCooldown;
            float endOverlap = RaceKart.BodyOverlap(a, b);
            bool common = first >= 0 && finite && maxPen < 0.1f && inWindow == 1 && endOverlap <= 0f && endClosing <= 0.3f;
            bool energy = aInv || (keAfter0 <= keBefore * 1.001f && keAfter2 <= keBefore * 1.001f);
            bool special = true; string sp = "";
            if (kind == "head")
            {
                float pre = vaBefore.magnitude + vbBefore.magnitude, post = vaAfter.magnitude + vbAfter.magnitude;
                float loss = pre > 0.1f ? 1f - post / pre : 1f;
                bool back = Vector3.Dot(vaAfter, a.Forward) < 0f;
                special = loss < 0.5f && back; sp = "  speedLoss=" + (loss * 100f).ToString("0") + "% (<50%)  A bounced back=" + back;
            }
            else if (kind == "rear")
            {
                float fa0 = vaBefore.magnitude, fa1 = vaAfter.magnitude, fb0 = vbBefore.magnitude, fb1 = vbAfter.magnitude;
                special = fb1 > fb0 + 0.5f && fa1 < fa0 && (fa0 - fa1) > (fb1 - fb0);   // 前車(重)被推快、後車(輕)被擋、輕的速度變化較大
                sp = "  A(light) " + fa0.ToString("0.0") + "->" + fa1.ToString("0.0") + "  B(heavy) " + fb0.ToString("0.0") + "->" + fb1.ToString("0.0") + " m/s";
            }
            else if (kind == "side")
            {
                special = maxBLateral > 0.3f; sp = "  B pushed sideways vx=" + maxBLateral.ToString("0.00") + " m/s (>0.3)";
            }
            else if (kind == "star")
            {
                float ra = vaBefore.magnitude > 0.1f ? vaAfter.magnitude / vaBefore.magnitude : 0f;
                special = stunnedSeen && ra > 0.95f && vbAfter.magnitude > vbBefore.magnitude + 0.5f;
                sp = "  victimStunned=" + stunnedSeen + "  invincible A speed ratio=" + ra.ToString("0.00") + " (>0.95)  B " + vbBefore.magnitude.ToString("0.0") + "->" + vbAfter.magnitude.ToString("0.0");
            }
            bool ok = common && energy && special;
            Report(name, ok, "bump=" + (first >= 0) + " events/0.15s=" + inWindow + " (want 1)  maxPen=" + maxPen.ToString("0.000") + " m (<0.1)  endOverlap=" + endOverlap.ToString("0.000")
                             + "  endClosing=" + endClosing.ToString("0.00") + " (<=0.3)  KE " + keBefore.ToString("0") + "->" + keAfter0.ToString("0") + "/" + keAfter2.ToString("0")
                             + (aInv ? " (invincible, energy not checked)" : " (not increasing)") + sp);
            Despawn(a); Despawn(b); yield return null;
        }

        IEnumerator T9_KartPairs()
        {
            var brick = KartPresets.For(KartStyle.Brick, 150);
            float lx = NextPairLane();
            yield return StartCoroutine(Run("9a head-on", PairCase("9a head-on", "head", brick, brick, lx, 70f, 0f, 12f, lx, 86f, 180f, 12f, false)));
            lx = NextPairLane();
            yield return StartCoroutine(Run("9b rear-end", PairCase("9b rear-end", "rear", KartPresets.For(KartStyle.Bullet, 150), KartPresets.For(KartStyle.Chonk, 150),
                                                                       lx, 70f, 0f, 16f, lx, 82f, 0f, 6f, false)));
            lx = NextPairLane();
            yield return StartCoroutine(Run("9c side-swipe", PairCase("9c side-swipe", "side", brick, brick, lx, 70f, 20f, 14f, lx + 2f, 70f, 0f, 10f, false)));
            lx = NextPairLane();
            yield return StartCoroutine(Run("9d invincible", PairCase("9d invincible", "star", brick, brick, lx, 70f, 0f, 16f, lx, 82f, 0f, 6f, true)));
        }

        // ------------------------------------------------------------ 測試 13:壓力(8 台隨機輸入 5 分鐘)
        sealed class RandomKartInput : IKartInput
        {
            readonly System.Random rng; float timer; KartInput cur; bool drift, down;
            public RandomKartInput(int seed) { rng = new System.Random(seed); }
            public KartInput Read(float dt)
            {
                timer -= dt;
                if (timer <= 0f)
                {
                    timer = 0.4f + (float)rng.NextDouble() * 1.1f;
                    cur = new KartInput { Throttle = 0.7f + 0.3f * (float)rng.NextDouble(), Steer = (float)(rng.NextDouble() * 2.0 - 1.0) };
                    if (rng.NextDouble() < 0.10) { cur.Throttle = 0f; cur.Brake = 1f; }
                    bool want = rng.NextDouble() < 0.2;
                    down = want && !drift; drift = want; cur.DriftHeld = want;
                }
                KartInput r = cur; r.DriftDown = down; down = false;
                return r;
            }
        }

        // 120 x 120 m 圍牆場地,8 台車繞圓圈朝中心出發(一開始就會互撞),之後隨機輸入。檢查:無 NaN、沒掉出場地、車對車穿透 < 0.1 m、
        // 每台車每 10 s 至少走 2 m(不卡死)、至少發生過車對車碰撞、平均物理步 < 0.3 ms/台(RaceKart.PerfTicks,含碰撞解算)。
        IEnumerator T13_Stress()
        {
            const float cz = 210f, half = 56f; const int N = 8; const int steps = 5 * 60 * 60;
            track.AddWall("ArenaN", new Vector3(0f, 0f, cz + half + 1f), 0f, 2f * half + 4f, 8f, 2f);
            track.AddWall("ArenaS", new Vector3(0f, 0f, cz - half - 1f), 0f, 2f * half + 4f, 8f, 2f);
            track.AddWall("ArenaE", new Vector3(half + 1f, 0f, cz), 90f, 2f * half + 4f, 8f, 2f);
            track.AddWall("ArenaW", new Vector3(-half - 1f, 0f, cz), 90f, 2f * half + 4f, 8f, 2f);
            var karts = new RaceKart[N];
            KartStyle[] styles = { KartStyle.Brick, KartStyle.Bullet, KartStyle.Chonk, KartStyle.Robot9427 };
            int kartBumps = 0;
            Action<RaceKart, KartBump> on = (who, bp) => { if (bp.OtherKartId >= 0) kartBumps++; };
            RaceKart.Bumped += on;
            for (int i = 0; i < N; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                karts[i] = Spawn(18f * Mathf.Sin(ang), cz + 18f * Mathf.Cos(ang), i * 45f + 180f, new RandomKartInput(9427 + i), KartPresets.For(styles[i % 4], 150));
                karts[i].AllowTech = false;   // 壓力測試只測碰撞/穿透,不含集氣跳/滑牆/軌道(那些有專屬 K9~K12)
            }
            for (int i = 0; i < 6; i++) yield return WFF;
            RaceKart.PerfTicks = 0; RaceKart.PerfCount = 0;
            var last = new Vector3[N]; var path = new float[N]; float minWindow = float.MaxValue;
            for (int i = 0; i < N; i++) last[i] = karts[i].Position;
            bool finite = true, inside = true; float maxPen = 0f, maxSpeed = 0f; int worstA = -1, worstB = -1;
            for (int s = 1; s <= steps; s++)
            {
                yield return WFF;
                for (int i = 0; i < N; i++)
                {
                    RaceKart k = karts[i];
                    Vector3 p = k.Position;
                    if (!IsFiniteV(p) || !IsFiniteV(k.Velocity)) { finite = false; continue; }
                    Vector3 lp = p - track.Origin;
                    if (Mathf.Abs(lp.x) > half + 4f || Mathf.Abs(lp.z - cz) > half + 4f || lp.y < -3f || lp.y > 6f) inside = false;
                    path[i] += H(p - last[i]).magnitude; last[i] = p;
                    float sp = k.Velocity.magnitude; if (sp > maxSpeed) maxSpeed = sp;
                    for (int j = i + 1; j < N; j++)
                    {
                        float pen = RaceKart.BodyOverlap(k, karts[j]);
                        if (pen > maxPen) { maxPen = pen; worstA = i; worstB = j; }
                    }
                }
                if (s % 600 == 0)
                {
                    for (int i = 0; i < N; i++) { if (path[i] < minWindow) minWindow = path[i]; path[i] = 0f; }
                }
            }
            RaceKart.Bumped -= on;
            double ms = RaceKart.PerfCount > 0 ? RaceKart.PerfTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / RaceKart.PerfCount : 0.0;
            bool ok = finite && inside && maxPen < 0.1f && minWindow >= 2f && kartBumps > 0 && ms < 0.3 && maxSpeed < 60f;
            Report("13 stress 8 karts", ok, "5 min  finite=" + finite + " insideArena=" + inside + "  maxKartPenetration=" + maxPen.ToString("0.000") + " m (<0.1; pair " + worstA + "-" + worstB
                                            + ")  minPathPer10s=" + minWindow.ToString("0.0") + " m (>=2)  kartBumps=" + kartBumps + " (>0)  avgStep=" + ms.ToString("0.000") + " ms/kart (<0.3)  maxSpeed=" + maxSpeed.ToString("0.0"));
            for (int i = 0; i < N; i++) Despawn(karts[i]);
            yield return null;
        }

        // ------------------------------------------------------------ 測試 10:API
        IEnumerator T10_Api()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            float lx = NextLane();
            RaceKart a = Spawn(lx, -300f, 0f, new ScriptedInput(), st);
            RaceKart b = Spawn(lx, -200f, 0f, new ScriptedInput(), st);
            for (int i = 0; i < 4; i++) yield return WFF;

            a.SetInvincible(2f);
            bool stunWhileInv = a.Stun(1f);                       // 無敵中:必須回 false
            bool invOk = !stunWhileInv && a.Invincible && !a.Stunned;

            a.Boost(1.0f, 0.25f); a.Boost(0.5f, 0.25f);          // 不疊加:剩餘仍是 1.0
            bool n1 = Mathf.Abs(a.BoostRemaining - 1.0f) < 0.001f;
            a.Boost(2.0f, 0.1f);                                  // 時間取大(2.0)、加成取大(0.25)
            bool n2 = Mathf.Abs(a.BoostRemaining - 2.0f) < 0.001f && Mathf.Abs(a.MaxSpeed - st.TopSpeed * 1.25f) < 0.01f;

            bool s1 = b.Stun(0.5f);                               // 正常暈眩:true
            bool s2 = b.Stun(0.5f);                               // 暈眩中再暈:false
            bool stunOk = s1 && !s2 && b.Stunned;

            bool ok = invOk && n1 && n2 && stunOk;
            Report("10 api", ok, "stunWhileInvincible=" + stunWhileInv + " (want False)  boostNoStack=" + n1 + "/" + n2 + "  maxSpeed=" + a.MaxSpeed.ToString("0.00")
                                 + " (want " + (st.TopSpeed * 1.25f).ToString("0.00") + ")  stun=" + s1 + "/" + s2);
            Despawn(a); Despawn(b); yield return null;
        }

        // ------------------------------------------------------------ 測試 11:決定性
        static KartInput DetScript(int f)
        {
            var k = new KartInput();
            if (f < 10) return k;
            k.Throttle = 1f;
            if (f >= 210 && f < 330) k.Steer = 0.6f;
            else if (f >= 330 && f < 400) k.Steer = -1f;
            if (f >= 400) { k.Throttle = 0f; k.Brake = 1f; }
            return k;
        }

        IEnumerator T11_Determinism()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            float lx = NextLane();
            Vector3 p1 = default, p2 = default; float y1 = 0f, y2 = 0f;
            for (int run = 0; run < 2; run++)
            {
                var src = new ScriptedInput { Script = DetScript };
                RaceKart k = Spawn(lx, -300f, 0f, src, st);
                for (int i = 0; i < 480; i++) yield return WFF;
                if (run == 0) { p1 = k.Position; y1 = k.YawDeg; } else { p2 = k.Position; y2 = k.YawDeg; }
                Despawn(k);
                yield return null; yield return null;
            }
            float d = (p1 - p2).magnitude;
            bool ok = d < 1e-3f && Mathf.Abs(y1 - y2) < 1e-2f && IsFiniteV(p1);
            Report("11 determinism", ok, "endpoint diff=" + d.ToString("0.000000") + " m (<0.001)  yawDiff=" + Mathf.Abs(y1 - y2).ToString("0.0000") + " deg");
        }

        // ------------------------------------------------------------ 測試 12:幀率無關的輸入
        // captureFramerate 30 與 144 各跑一次:在固定時間點按 10 次漂移鍵(每次 DriftDown 只持續一個 Update),必須剛好收到 10 次 Hopped。
        IEnumerator T12_Framerate()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            int[] fps = { 30, 144 };
            int[] counts = new int[2];
            for (int r = 0; r < 2; r++)
            {
                Time.captureFramerate = fps[r];
                yield return null; yield return null;
                var src = new TimedInput();
                int idx = 0; float lastPress = -10f;
                src.F = t =>
                {
                    var ki = new KartInput { Throttle = 0.4f };
                    if (idx < 10 && t >= 0.6f + 0.7f * idx) { ki.DriftDown = true; lastPress = t; idx++; }
                    if (t - lastPress < 0.12f) ki.DriftHeld = true;
                    return ki;
                };
                RaceKart k = Spawn(NextLane(), -300f, 0f, src, st);
                int hops = 0;
                Action<RaceKart> onHop = w => { if (w == k) hops++; };
                RaceKart.Hopped += onHop;
                int steps = Mathf.RoundToInt(8.6f * 60f);
                for (int i = 0; i < steps; i++) yield return WFF;
                RaceKart.Hopped -= onHop;
                counts[r] = hops;
                Despawn(k); yield return null;
            }
            Time.captureFramerate = 60;
            yield return null;
            bool ok = counts[0] == 10 && counts[1] == 10;
            Report("12 framerate", ok, "hops @30fps=" + counts[0] + "  hops @144fps=" + counts[1] + "  (both want 10)");
        }

        // ============================================================ 模組 K5 / K6 測試
        // 20 度、水平長 10 m 的跳台(Surface.Ramp);glider = true 時掛 GliderRamp 標記。回傳跳台末端的 z(相對 Origin)。
        const float RampZ0 = -100f, RampRun = 10f, RampPitch = 20f;
        float AddTestRamp(float lx, bool glider, bool fixedLaunch = false)
        {
            float rise = RampRun * Mathf.Tan(RampPitch * Mathf.Deg2Rad);
            GameObject go = track.AddBox("KRamp", new Vector3(lx, rise * 0.5f, RampZ0 + RampRun * 0.5f), 0f, RampPitch,
                                         new Vector3(20f, 1f, RampRun / Mathf.Cos(RampPitch * Mathf.Deg2Rad)), Surface.Ramp);
            if (glider) go.AddComponent<GliderRamp>().FixedLaunch = fixedLaunch;
            return RampZ0 + RampRun;
        }

        // 一台車的飛行紀錄(離台 -> 落地)。
        sealed class Flight
        {
            public RaceKart K; public ScriptedInput Src;
            public bool Left, Landed, SawGliding, SawTricking;
            public Vector3 LeavePos, LandPos; public float BoostAtLand, MaxProgress, LeaveSpeed; public int MaxChargeLv;
            public int Glided; public List<bool> Ended = new List<bool>(); public List<float> Boosts = new List<float>();
            public float Dist { get { return H(LandPos - LeavePos).magnitude; } }
        }

        void FlightStep(Flight f)
        {
            RaceKart k = f.K;
            float z = k.Position.z - track.Origin.z;
            if (!f.Left && !k.Grounded && z > RampZ0 + 1f) { f.Left = true; f.LeavePos = k.Position; f.LeaveSpeed = k.Speed; }
            if (f.Left && !f.Landed)
            {
                if (k.Gliding) f.SawGliding = true;
                if (k.Tricking) f.SawTricking = true;
                if (k.TrickProgress > f.MaxProgress) f.MaxProgress = k.TrickProgress;
                if (k.GlideChargeLevel > f.MaxChargeLv) f.MaxChargeLv = k.GlideChargeLevel;
                if (k.Grounded) { f.Landed = true; f.LandPos = k.Position; f.BoostAtLand = k.BoostRemaining; }
            }
        }

        // ------------------------------------------------------------ K1:滑翔(GliderRamp 標記)
        // 三台車同時以 22 m/s 衝出 20 度跳台:A = 滑翔跳台、B = 一般跳台(對照)、C = 滑翔跳台 + 空中一直按住漂移鍵(蓄力迷你渦輪)。
        IEnumerator K1_Glide()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            float thr = 22f / st.TopSpeed;
            var fl = new Flight[3];
            bool[] gl = { true, false, true };
            for (int i = 0; i < 3; i++)
            {
                float lx = NextLane();
                AddTestRamp(lx, gl[i]);
                var f = new Flight { Src = new ScriptedInput() };
                f.K = Spawn(lx, -250f, 0f, f.Src, st);
                fl[i] = f;
            }
            Action<RaceKart> onG = w => { for (int i = 0; i < 3; i++) if (fl[i].K == w) fl[i].Glided++; };
            Action<RaceKart, bool> onE = (w, sm) => { for (int i = 0; i < 3; i++) if (fl[i].K == w) fl[i].Ended.Add(sm); };
            Action<RaceKart, float> onB = (w, sec) => { for (int i = 0; i < 3; i++) if (fl[i].K == w) fl[i].Boosts.Add(sec); };
            RaceKart.Glided += onG; RaceKart.GlideEnded += onE; RaceKart.BoostStart += onB;
            for (int i = 0; i < 6; i++) yield return WFF;
            for (int i = 0; i < 3; i++) fl[i].Src.Cur = new KartInput { Throttle = thr, DriftHeld = i == 2 };
            for (int step = 0; step < 900; step++)
            {
                yield return WFF;
                for (int i = 0; i < 3; i++) FlightStep(fl[i]);
                if (fl[0].Landed && fl[1].Landed && fl[2].Landed && step > 30) { for (int w = 0; w < 6; w++) yield return WFF; break; }
            }
            RaceKart.Glided -= onG; RaceKart.GlideEnded -= onE; RaceKart.BoostStart -= onB;
            Flight A = fl[0], B = fl[1], C = fl[2];

            float ratio = B.Dist > 0.5f ? A.Dist / B.Dist : 0f;
            bool glideOk = A.Left && A.Landed && A.SawGliding && A.Glided == 1 && A.Ended.Count == 1 && A.Ended[0] && A.LeaveSpeed > 20f && IsFiniteV(A.LandPos);
            bool ctrlOk = B.Left && B.Landed && !B.SawGliding && B.Glided == 0 && B.Ended.Count == 0;
            bool distOk = ratio > 1.5f;
            Report("K1a glide ramp: unfolds, flies > 1.5x farther than a normal jump", glideOk && ctrlOk && distOk,
                   "glideDist=" + A.Dist.ToString("0.0") + " m  normalDist=" + B.Dist.ToString("0.0") + " m  ratio=" + ratio.ToString("0.00") + " (>1.5)  launchSpeed="
                   + A.LeaveSpeed.ToString("0.0") + "  gliding=" + A.SawGliding + "  Glided events=" + A.Glided + " (want 1)  GlideEnded=[" + string.Join(",", A.Ended) + "] (want True)"
                   + "  control gliding=" + B.SawGliding);
            bool boostOk = A.Boosts.Count == 1 && Mathf.Abs(A.Boosts[0] - RaceKart.GlideLandBoostSeconds) < 0.01f && A.BoostAtLand > 0.5f && A.BoostAtLand <= 0.61f;
            Report("K1b glide smooth landing gives a 0.6 s boost (once)", boostOk && B.Boosts.Count == 0,
                   "boostEvents=[" + string.Join(",", A.Boosts) + "] (want [0.6])  boostRemainingAtLanding=" + A.BoostAtLand.ToString("0.00") + "  control boostEvents=" + B.Boosts.Count);
            float expect = Mathf.Max(RaceKart.GlideLandBoostSeconds, KartMath.DriftBoostSeconds(C.MaxChargeLv));
            bool chargeOk = C.MaxChargeLv >= 2 && C.Boosts.Count == 1 && Mathf.Abs(C.Boosts[0] - expect) < 0.01f && C.Boosts[0] > RaceKart.GlideLandBoostSeconds + 0.1f;
            Report("K1c mini-turbo charged in the air pays out at landing", chargeOk,
                   "chargeLevel=" + C.MaxChargeLv + " (>=2)  boostEvents=[" + string.Join(",", C.Boosts) + "] (want [" + expect.ToString("0.0") + "])  glideDist=" + C.Dist.ToString("0.0") + " m");
            for (int i = 0; i < 3; i++) Despawn(fl[i].K);
            yield return null;
        }

        // ------------------------------------------------------------ K1d:飛越缺口的起飛跳台 = 固定出場速度(M8),50 / 100 / 150cc 飛得一樣遠、都飛 3~6 s
        IEnumerator K1d_FixedLaunch()
        {
            int[] ccs = { 50, 100, 150 };
            var fl = new Flight[3];
            float[] gt = new float[3];
            for (int i = 0; i < 3; i++)
            {
                var st = KartPresets.For(KartStyle.Brick, ccs[i]);
                float lx = NextLane();
                AddTestRamp(lx, true, true);
                var f = new Flight { Src = new ScriptedInput() };
                f.K = Spawn(lx, -250f, 0f, f.Src, st);
                fl[i] = f;
            }
            for (int i = 0; i < 6; i++) yield return WFF;
            for (int i = 0; i < 3; i++) fl[i].Src.Cur = new KartInput { Throttle = 1f };   // 一直按油門(下壓的最差情況)
            for (int step = 0; step < 1500; step++)
            {
                yield return WFF;
                for (int i = 0; i < 3; i++) { FlightStep(fl[i]); if (fl[i].Landed && gt[i] == 0f) gt[i] = fl[i].K.GlideTime; }
                if (fl[0].Landed && fl[1].Landed && fl[2].Landed && step > 30) break;
            }
            bool allLanded = fl[0].Landed && fl[1].Landed && fl[2].Landed;
            float d0 = fl[0].Dist, d1 = fl[1].Dist, d2 = fl[2].Dist;
            float mn = Mathf.Min(d0, Mathf.Min(d1, d2)), mx = Mathf.Max(d0, Mathf.Max(d1, d2));
            bool timeOk = gt[0] >= 3f && gt[0] <= 6f && gt[1] >= 3f && gt[1] <= 6f && gt[2] >= 3f && gt[2] <= 6f;
            Report("K1d fixed launch: 50/100/150cc fly the same >= 100 m in 3..6 s", allLanded && mn >= 100f && mx - mn <= 6f && timeOk,
                   "dist50/100/150=" + d0.ToString("0.0") + "/" + d1.ToString("0.0") + "/" + d2.ToString("0.0") + " m  glideTime=" + gt[0].ToString("0.0") + "/" + gt[1].ToString("0.0") + "/" + gt[2].ToString("0.0") + " s  landed=" + allLanded);
            for (int i = 0; i < 3; i++) Despawn(fl[i].K);
            yield return null;
        }

        // ------------------------------------------------------------ K2:BeginGlide() API + 撞牆失速解除
        IEnumerator K2_GlideWall()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            float endZ = AddTestRamp(lx, false);                    // 一般跳台,靠 BeginGlide() 預約
            track.AddWall("GlideWall", new Vector3(lx, 0f, endZ + 25f), 0f, 50f, 40f, 2f);
            RaceKart k = Spawn(lx, -250f, 0f, src, st);
            int glided = 0, bumps = 0; var ended = new List<bool>(); var boosts = new List<float>();
            Action<RaceKart> onG = w => { if (w == k) glided++; };
            Action<RaceKart, bool> onE = (w, sm) => { if (w == k) ended.Add(sm); };
            Action<RaceKart, float> onB = (w, sec) => { if (w == k) boosts.Add(sec); };
            Action<RaceKart, KartBump> onBump = (w, b) => { if (w == k && b.OtherKartId < 0) bumps++; };
            RaceKart.Glided += onG; RaceKart.GlideEnded += onE; RaceKart.BoostStart += onB; RaceKart.Bumped += onBump;
            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 22f / st.TopSpeed };
            bool armedCalled = false, armedRet = false, glidingBeforeLeave = false, sawGliding = false, wasGliding = false;
            bool glidingAfterBump = false; int bumpStep = -1, step = 0; float vBefore = 0f, vAfter = 0f, maxBoost = 0f, glideAtBump = 0f;
            while (step < 600)
            {
                yield return WFF; step++;
                float z = k.Position.z - track.Origin.z;
                if (!armedCalled && k.Grounded && z > RampZ0 + 3f) { armedCalled = true; armedRet = k.BeginGlide(); glidingBeforeLeave = k.Gliding; }
                if (k.Gliding) { sawGliding = true; wasGliding = true; vBefore = H(k.Velocity).magnitude; glideAtBump = k.GlideTime; }
                if (k.BoostRemaining > maxBoost) maxBoost = k.BoostRemaining;
                if (bumpStep < 0 && bumps > 0) { bumpStep = step; vAfter = H(k.Velocity).magnitude; glidingAfterBump = k.Gliding; }
                if (bumpStep >= 0 && step >= bumpStep + 150) break;
            }
            RaceKart.Glided -= onG; RaceKart.GlideEnded -= onE; RaceKart.BoostStart -= onB; RaceKart.Bumped -= onBump;
            bool ok = armedRet && !glidingBeforeLeave && sawGliding && glided == 1 && bumpStep >= 0 && wasGliding && !glidingAfterBump && ended.Count == 1 && !ended[0]
                      && vAfter < 0.7f * vBefore && maxBoost < 0.01f && IsFiniteV(k.Position);
            Report("K2 BeginGlide() arms on the ramp; wall hit stalls and cancels the glide (no landing boost)", ok,
                   "BeginGlide()=" + armedRet + "  glidingOnRamp=" + glidingBeforeLeave + " (want False)  glided=" + sawGliding + "  Glided events=" + glided + "  bumped=" + (bumpStep >= 0)
                   + "  glideTimeAtBump=" + glideAtBump.ToString("0.00") + "  gliding after bump=" + glidingAfterBump + " (want False)  GlideEnded=[" + string.Join(",", ended) + "] (want [False])"
                   + "  speed " + vBefore.ToString("0.0") + "->" + vAfter.ToString("0.0") + " (<70%)  maxBoost=" + maxBoost.ToString("0.00") + " (want 0)");
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ K3:自動滑翔(高空、仍在上升)+ 最長 8 s
        IEnumerator K3_AutoGlide()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            float lx = NextLane();
            RaceKart k = Spawn(lx, -250f, 0f, src, st);
            for (int i = 0; i < 6; i++) yield return WFF;
            int glided = 0; var ended = new List<bool>();
            Action<RaceKart> onG = w => { if (w == k) glided++; };
            Action<RaceKart, bool> onE = (w, sm) => { if (w == k) ended.Add(sm); };
            RaceKart.Glided += onG; RaceKart.GlideEnded += onE;
            k.Teleport(k.Position + Vector3.up * 10f, k.transform.rotation, false);
            k.ApplyImpulse(k.Forward * 15f + Vector3.up * 16f);
            bool sawGliding = false; float firstGlideAir = -1f, maxGlideTime = 0f; int steps = 0;
            while (steps < 900)
            {
                yield return WFF; steps++;
                if (k.Gliding) { sawGliding = true; if (firstGlideAir < 0f) firstGlideAir = k.AirTime; if (k.GlideTime > maxGlideTime) maxGlideTime = k.GlideTime; }
                if (sawGliding && k.Grounded && steps > 30) break;
            }
            for (int i = 0; i < 5; i++) yield return WFF;
            RaceKart.Glided -= onG; RaceKart.GlideEnded -= onE;
            bool ok = sawGliding && glided == 1 && ended.Count == 1 && !k.Gliding && maxGlideTime <= RaceKart.GlideMaxSeconds + 0.05f && IsFiniteV(k.Position);
            Report("K3 auto glide (airborne > 3 m, still rising)", ok,
                   "gliding=" + sawGliding + "  startedAtAirTime=" + firstGlideAir.ToString("0.00") + " s  glideTime=" + maxGlideTime.ToString("0.00") + " s (<=8)  Glided=" + glided
                   + "  GlideEnded=[" + string.Join(",", ended) + "]  grounded=" + k.Grounded);
            Despawn(k); yield return null;
        }

        // ------------------------------------------------------------ K4:特技
        // T = 離台後按漂移鍵、U = 不按(對照)。T 落地給 0.7 s 加速(只給一次),U 沒有。
        IEnumerator K4_Trick()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var fl = new Flight[2];
            for (int i = 0; i < 2; i++)
            {
                float lx = NextLane();
                AddTestRamp(lx, false);
                var f = new Flight { Src = new ScriptedInput() };
                f.K = Spawn(lx, -250f, 0f, f.Src, st);
                fl[i] = f;
            }
            var boosts = new List<float>[2] { new List<float>(), new List<float>() };
            Action<RaceKart, float> onB = (w, sec) => { for (int i = 0; i < 2; i++) if (fl[i].K == w) boosts[i].Add(sec); };
            RaceKart.BoostStart += onB;
            for (int i = 0; i < 6; i++) yield return WFF;
            for (int i = 0; i < 2; i++) fl[i].Src.Cur = new KartInput { Throttle = 22f / st.TopSpeed };
            bool pressed = false, releasePending = false;
            for (int step = 0; step < 700; step++)
            {
                yield return WFF;
                for (int i = 0; i < 2; i++) FlightStep(fl[i]);
                if (releasePending) { fl[0].Src.Cur = new KartInput { Throttle = 22f / st.TopSpeed }; releasePending = false; }
                if (!pressed && fl[0].Left)
                {
                    pressed = true; releasePending = true;
                    fl[0].Src.Cur = new KartInput { Throttle = 22f / st.TopSpeed, DriftDown = true, DriftHeld = true };
                }
                if (fl[0].Landed && fl[1].Landed && step > 30) { for (int w = 0; w < 4; w++) yield return WFF; break; }
            }
            RaceKart.BoostStart -= onB;
            Flight T = fl[0], U = fl[1];
            bool ok = T.Left && T.Landed && T.SawTricking && T.MaxProgress > 0.5f && !T.K.Tricking && boosts[0].Count == 1 && Mathf.Abs(boosts[0][0] - RaceKart.TrickBoostSeconds) < 0.01f
                      && T.BoostAtLand > 0.6f && T.BoostAtLand <= 0.71f;
            bool ctrl = U.Left && U.Landed && !U.SawTricking && boosts[1].Count == 0 && U.BoostAtLand < 0.01f;
            Report("K4 trick: drift key after the ramp -> flip, 0.7 s boost on landing (once)", ok && ctrl,
                   "tricking=" + T.SawTricking + "  maxProgress=" + T.MaxProgress.ToString("0.00") + " (>0.5)  boostEvents=[" + string.Join(",", boosts[0]) + "] (want [0.7])  boostAtLanding="
                   + T.BoostAtLand.ToString("0.00") + "  control: tricking=" + U.SawTricking + " boostEvents=" + boosts[1].Count + " boostAtLanding=" + U.BoostAtLand.ToString("0.00"));
            Despawn(fl[0].K); Despawn(fl[1].K); yield return null;
        }

        // ------------------------------------------------------------ K5:金幣
        IEnumerator K5_Coins()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            int[] n = { 0, 5, 10 };
            var ks = new RaceKart[3]; var peak = new float[3];
            var evDelta = new List<int>();
            Action<RaceKart, int, int> onC = (w, c, d) => evDelta.Add(d);
            RaceKart.CoinsChanged += onC;
            for (int i = 0; i < 3; i++)
            {
                ks[i] = Spawn(NextLane(), -300f, 0f, new ScriptedInput { Cur = new KartInput { Throttle = 1f } }, st);
                for (int c = 0; c < n[i]; c++) ks[i].AddCoin();
            }
            int addEvents = evDelta.Count;
            for (int s = 0; s < 400; s++)
            {
                yield return WFF;
                for (int i = 0; i < 3; i++) if (ks[i].Speed > peak[i]) peak[i] = ks[i].Speed;
            }
            float r5 = peak[1] / peak[0], r10 = peak[2] / peak[0];
            bool speedOk = Mathf.Abs(r5 - 1.035f) < 0.01f && Mathf.Abs(r10 - 1.07f) < 0.012f && peak[0] > 0.97f * st.TopSpeed;
            Report("K5a coins raise top speed +0.7% each (10 coins = +7%)", speedOk && addEvents == 15,
                   "peak 0/5/10 coins = " + peak[0].ToString("0.00") + " / " + peak[1].ToString("0.00") + " / " + peak[2].ToString("0.00") + " m/s  ratio " + r5.ToString("0.000") + " (1.035) / " + r10.ToString("0.000")
                   + " (1.070)  coinEvents=" + addEvents + " (15)");

            RaceKart k = ks[2];
            bool full = !k.AddCoin();                                           // 滿了:false
            k.SpeedMul = 0.9f;                                                  // 別人(橡皮筋 / 道具)寫的倍率不會被金幣覆蓋
            float mulExpect = st.TopSpeed * 0.9f * 1.07f;
            bool stack = Mathf.Abs(k.MaxSpeed - mulExpect) < 0.01f;
            k.SpeedMul = 1f;
            bool stunned = k.Stun(0.5f);                                        // 被打暈:掉 2 枚
            bool lost2 = stunned && k.Coins == 8;
            int lostA = k.LoseCoins(5), lostB = k.LoseCoins(10);                // 8 -> 3 -> 0
            k.Coins = 99;                                                       // 寫入夾在 0..10
            bool clamp = k.Coins == 10;
            RaceKart.CoinsChanged -= onC;
            Report("K5b coins: cap 10, stack with SpeedMul, lose 2 when stunned", full && stack && lost2 && lostA == 5 && lostB == 3 && clamp,
                   "addWhenFull=" + !full + " (want False)  MaxSpeed(SpeedMul .9)=" + mulExpect.ToString("0.00") + " ok=" + stack + "  coinsAfterStun=" + (lost2 ? 8 : -1) + " (want 8)  LoseCoins(5)=" + lostA + "  LoseCoins(10)=" + lostB + "  clamp=" + clamp);
            for (int i = 0; i < 3; i++) Despawn(ks[i]);
            yield return null;
        }

        // ------------------------------------------------------------ K6:起跑加速(三種情境)+ AI 機率
        // A = 倒數剩 0.68 s 才按住油門(0.4~1.0 窗口)-> GO 加速 0.8 s;B = 剩 1.98 s 就按(太早)-> GO 後空轉 0.8 s;C = 剩 0.18 s 才按(太晚)-> 沒有加成。
        IEnumerator K6_StartBoost()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var ks = new RaceKart[3]; var boosts = new List<float>[3] { new List<float>(), new List<float>(), new List<float>() };
            float[] pressAt = { 0.7f, 2.0f, 0.2f };
            for (int i = 0; i < 3; i++)
            {
                ks[i] = Spawn(NextLane(), -300f, 0f, new ScriptedInput { Cur = new KartInput { Throttle = 1f } }, st);
                ks[i].IsPlayer = false; ks[i].Frozen = true;
            }
            Action<RaceKart, float> onB = (w, sec) => { for (int i = 0; i < 3; i++) if (ks[i] == w) boosts[i].Add(sec); };
            RaceKart.BoostStart += onB;
            for (int i = 0; i < 6; i++) yield return WFF;
            float remain = 3f;
            while (remain > 0f)
            {
                yield return WFF; remain -= DT;
                if (remain > 0f) for (int i = 0; i < 3; i++) ks[i].StartBoostWindow(remain, remain <= pressAt[i] ? 1f : 0f);
            }
            for (int i = 0; i < 3; i++) { ks[i].StartBoostWindow(0f, 1f); ks[i].Frozen = false; }
            var res = new StartBoostResult[3]; for (int i = 0; i < 3; i++) res[i] = ks[i].LastStartResult;
            bool spinningAtGo = ks[1].StartSpinning;
            var sp05 = new float[3]; var sp10 = new float[3]; var sp25 = new float[3];
            for (int s = 1; s <= 150; s++)
            {
                yield return WFF;
                for (int i = 0; i < 3; i++)
                {
                    if (s == 30) sp05[i] = ks[i].Speed;
                    if (s == 60) sp10[i] = ks[i].Speed;
                    if (s == 150) sp25[i] = ks[i].Speed;
                }
            }
            RaceKart.BoostStart -= onB;
            bool a = res[0] == StartBoostResult.Boost && boosts[0].Count == 1 && Mathf.Abs(boosts[0][0] - RaceKart.StartBoostSeconds) < 0.01f && sp10[0] > sp10[2] + 3f;
            bool b = res[1] == StartBoostResult.Spin && spinningAtGo && boosts[1].Count == 0 && sp05[1] < 1f && sp25[1] > 5f;
            bool c = res[2] == StartBoostResult.None && boosts[2].Count == 0 && sp10[2] > 5f;
            Report("K6a start boost: press at 0.7 s left -> 0.8 s boost", a, "result=" + res[0] + "  boostEvents=[" + string.Join(",", boosts[0]) + "] (want [0.8])  speed@1s boosted=" + sp10[0].ToString("0.0") + " vs normal " + sp10[2].ToString("0.0"));
            Report("K6b start boost: press at 2.0 s left (too early) -> 0.8 s wheel-spin", b, "result=" + res[1] + "  spinning=" + spinningAtGo + "  speed@0.5s=" + sp05[1].ToString("0.00") + " (<1)  speed@2.5s=" + sp25[1].ToString("0.0") + " (>5, recovers)");
            Report("K6c start boost: press at 0.2 s left (too late) -> nothing", c, "result=" + res[2] + "  boostEvents=" + boosts[2].Count + "  speed@1s=" + sp10[2].ToString("0.0"));

            // AI 機率:簡單 20%、困難 80%(400 次,容許 +-8%)
            var rng = new System.Random(9427);
            int hit0 = 0, hit3 = 0;
            for (int i = 0; i < 400; i++) { if (ks[0].RollAiStartBoost(0, rng)) hit0++; if (ks[0].RollAiStartBoost(3, rng)) hit3++; }
            float p0 = hit0 / 400f, p3 = hit3 / 400f;
            bool ai = Mathf.Abs(p0 - 0.2f) < 0.08f && Mathf.Abs(p3 - 0.8f) < 0.08f && Mathf.Approximately(KartMath.AiStartBoostChance(0), 0.2f) && Mathf.Approximately(KartMath.AiStartBoostChance(3), 0.8f);
            Report("K6d AI start boost chance easy 20% / hard 80%", ai, "easy=" + (p0 * 100f).ToString("0") + "%  hard=" + (p3 * 100f).ToString("0") + "%");
            for (int i = 0; i < 3; i++) Despawn(ks[i]);
            yield return null;
        }

        // ------------------------------------------------------------ K7:排氣量(四種最高速比 0.55 : 0.75 : 1.0 : 1.3,+-3%)
        IEnumerator K7_Cc()
        {
            int[] ccs = { 50, 100, 150, 200 };
            float[] want = { 0.55f, 0.75f, 1.0f, 1.3f };
            float lx = NextLane();
            var ks = new RaceKart[4]; var peak = new float[4];
            for (int i = 0; i < 4; i++)
                ks[i] = Spawn(lx - 21f + 14f * i, -300f, 0f, new ScriptedInput { Cur = new KartInput { Throttle = 1f } }, KartPresets.For(KartStyle.Brick, ccs[i]));
            for (int s = 0; s < 480; s++)
            {
                yield return WFF;
                for (int i = 0; i < 4; i++) if (ks[i].Speed > peak[i]) peak[i] = ks[i].Speed;
            }
            bool ok = true; var sb = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                float ratio = peak[i] / peak[2];
                if (Mathf.Abs(ratio - want[i]) > 0.03f * want[i]) ok = false;
                sb.Append(ccs[i] + "cc=" + peak[i].ToString("0.0") + " (" + ratio.ToString("0.000") + " want " + want[i].ToString("0.00") + ")  ");
            }
            KartStats s50 = KartPresets.For(KartStyle.Brick, 50), s150 = KartPresets.For(KartStyle.Brick, 150), s200 = KartPresets.For(KartStyle.Brick, 200);
            var manual = new KartStats(); KartPresets.ApplyCC(manual, 200);
            bool api = Mathf.Abs(manual.TopSpeed - s200.TopSpeed) < 0.001f && Mathf.Abs(s150.TopSpeed - 24f) < 0.001f && s200.Accel > s150.Accel && s150.Accel > s50.Accel
                       && s200.Grip < s150.Grip && s200.TurnRateHigh > s150.TurnRateHigh;
            Report("K7 cc: top speed 0.55 : 0.75 : 1.0 : 1.3 (+-3%), ApplyCC", ok && api, sb + " ApplyCC(200)=" + manual.TopSpeed.ToString("0.0") + "  accel 50/150/200=" + s50.Accel.ToString("0.0") + "/" + s150.Accel.ToString("0.0") + "/" + s200.Accel.ToString("0.0")
                   + "  grip 150/200=" + s150.Grip.ToString("0.0") + "/" + s200.Grip.ToString("0.0"));
            for (int i = 0; i < 4; i++) Despawn(ks[i]);
            yield return null;
        }

        // ------------------------------------------------------------ K8:四件套屬性
        IEnumerator K8_Loadout()
        {
            LoadoutResult def = KartLoadout.Combine(0, 0, 0, 0);
            KartStats brick = KartPresets.For(KartStyle.Brick, 150);
            bool defOk = Mathf.Abs(def.Stats.TopSpeed - brick.TopSpeed) < 0.2f && Mathf.Abs(def.Stats.Mass - brick.Mass) < 3f && Mathf.Abs(def.Stats.Accel - brick.Accel) < 0.3f
                         && Mathf.Abs(def.Stats.Grip - brick.Grip) < 0.3f && Mathf.Abs(def.Stats.TurnRateLow - brick.TurnRateLow) < 2f;
            LoadoutResult heavy = KartLoadout.Combine(6, 2, 3, 1), light = KartLoadout.Combine(3, 1, 4, 3);
            bool order = heavy.Stats.Mass > light.Stats.Mass + 40f && heavy.Stats.TopSpeed > light.Stats.TopSpeed + 0.8f && heavy.Stats.Accel < light.Stats.Accel - 1.5f
                         && heavy.Stats.TurnRateLow < light.Stats.TurnRateLow - 10f && heavy.Class == WeightClass.Heavy && light.Class == WeightClass.Light
                         && heavy.Bars[2] > light.Bars[2] && heavy.Bars[0] > light.Bars[0] && heavy.Bars[1] < light.Bars[1];
            bool range = true; string bad = "";
            for (int d = 0; d < KartLoadout.DriverCount; d++)
                for (int b = 0; b < KartLoadout.BodyCount; b++)
                    for (int t = 0; t < KartLoadout.TireCount; t++)
                        for (int g = 0; g < KartLoadout.GliderCount; g++)
                        {
                            LoadoutResult r = KartLoadout.Combine(d, b, t, g);
                            KartStats k = r.Stats;
                            bool good = k.TopSpeed >= 22f && k.TopSpeed <= 26f && k.Mass >= 115f && k.Mass <= 205f && k.Accel >= 12.5f && k.Accel <= 19.5f && k.Grip >= 10f && k.Grip <= 14f
                                        && k.TurnRateLow >= 100f && k.TurnRateLow <= 140f && k.BoostMul >= 1.18f && k.BoostMul <= 1.32f && r.OffRoad >= 0f && r.OffRoad <= 1f && r.GlideLift >= 0.8f && r.GlideLift <= 1.3f;
                            for (int i = 0; i < KartLoadout.StatCount; i++) if (r.Bars[i] < 0f || r.Bars[i] > 1f || Mathf.Abs(r.Stars[i] - r.Bars[i] * 5f) > 1e-4f) good = false;
                            if (!good && range) { range = false; bad = d + "," + b + "," + t + "," + g; }
                        }
            bool names = true;
            for (int i = 0; i < KartLoadout.DriverCount; i++) names &= !string.IsNullOrEmpty(KartLoadout.DriverName(i, "zh")) && !string.IsNullOrEmpty(KartLoadout.DriverName(i, "en"));
            for (int i = 0; i < KartLoadout.BodyCount; i++) names &= !string.IsNullOrEmpty(KartLoadout.BodyName(i, "zh")) && !string.IsNullOrEmpty(KartLoadout.BodyName(i, "en"));
            for (int i = 0; i < KartLoadout.TireCount; i++) names &= !string.IsNullOrEmpty(KartLoadout.TireName(i, "zh")) && !string.IsNullOrEmpty(KartLoadout.TireName(i, "en"));
            for (int i = 0; i < KartLoadout.GliderCount; i++) names &= !string.IsNullOrEmpty(KartLoadout.GliderName(i, "zh")) && !string.IsNullOrEmpty(KartLoadout.GliderName(i, "en"));
            names &= KartLoadout.DriverName(0, "zh") != KartLoadout.DriverName(0, "en");
            LoadoutResult clamp = KartLoadout.Combine(-5, 99, 99, -1);                        // 超出範圍夾住、不丟例外
            LoadoutResult cc200 = KartLoadout.Combine(0, 0, 0, 0, 200);
            bool cc = Mathf.Abs(cc200.Stats.TopSpeed / def.Stats.TopSpeed - 1.3f) < 0.01f;
            bool styles = KartLoadout.BodyStyle(0) == KartStyle.Brick && KartLoadout.BodyStyle(1) == KartStyle.Bullet && KartLoadout.BodyStyle(2) == KartStyle.Chonk && KartLoadout.BodyStyle(3) == KartStyle.Robot9427;
            var cfg = new RaceConfig { DriverId = 6, BodyId = 2, TireId = 3, GliderId = 1, CC = 100 };
            KartStats viaCfg = KartPresets.For(cfg);
            bool cfgOk = Mathf.Abs(viaCfg.TopSpeed - KartLoadout.Combine(6, 2, 3, 1, 100).Stats.TopSpeed) < 0.001f && Mathf.Abs(viaCfg.TopSpeed - KartPresets.For(6, 2, 3, 1, 100).TopSpeed) < 0.001f;
            Report("K8 loadout: default = Brick, heavy vs light ordering, ranges, names, clamp, cc, config", defOk && order && range && names && IsFiniteV(Vector3.one * clamp.Stats.TopSpeed) && cc && styles && cfgOk,
                   "defaultTop=" + def.Stats.TopSpeed.ToString("0.00") + " (Brick " + brick.TopSpeed.ToString("0.00") + ")  heavy top/mass/accel=" + heavy.Stats.TopSpeed.ToString("0.0") + "/" + heavy.Stats.Mass.ToString("0")
                   + "/" + heavy.Stats.Accel.ToString("0.0") + "  light=" + light.Stats.TopSpeed.ToString("0.0") + "/" + light.Stats.Mass.ToString("0") + "/" + light.Stats.Accel.ToString("0.0")
                   + "  order=" + order + "  allRanges=" + range + (range ? "" : " bad=" + bad) + "  names=" + names + "  cc200ratio=" + (cc200.Stats.TopSpeed / def.Stats.TopSpeed).ToString("0.000")
                   + "  styles=" + styles + "  cfg=" + cfgOk);
            yield return null;
        }

        // ============================================================ 世界式移動技(集氣跳 / 跑牆 / 軌道滑行 / 特技無敵)
        // 規則見 RaceKart 檔頭註解(集氣跳:小跳落地後鍵還按著且直行才集氣;跑牆:空中 + 資格 + 夾角 <= 35 度 + 牆掛 RideableWall;軌道:空中車底落進軌道 0.85 m)。

        /// 加速 4 s -> 按漂移鍵(小跳)-> 落地後開始集氣(Charging)-> 再集氣 holdSteps 步 -> 送出「放開」(回傳時下一個物理步就起跳)。tech = false 的車不會集氣(最多等 90 步)。
        IEnumerator PrepJump(RaceKart k, ScriptedInput src, int holdSteps)
        {
            for (int i = 0; i < 6; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
            for (int i = 0; i < 240; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f, DriftDown = true, DriftHeld = true };
            yield return WFF;
            src.Cur = new KartInput { Throttle = 1f, DriftHeld = true };
            int guard = 0;
            while (!k.Charging && guard++ < 90) yield return WFF;
            for (int i = 0; i < holdSteps; i++) yield return WFF;
            src.Cur = new KartInput { Throttle = 1f };
        }

        // ---- K9 集氣跳:Lv0 不跳、Lv1、Lv3(高度、火花事件、落地加速、特技中無敵、落地後可被擊中);AllowTech = false 不集氣
        IEnumerator K9_Case(string name, int holdSteps, int wantLv, bool tech)
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -330f, 0f, src, st);
            k.AllowTech = tech;
            var ups = new List<int>(); var jumps = new List<int>(); var boosts = new List<float>(); int hops = 0;
            Action<RaceKart, int> onUp = (w, l) => { if (w == k) ups.Add(l); };
            Action<RaceKart, int> onJ = (w, l) => { if (w == k) jumps.Add(l); };
            Action<RaceKart, float> onB = (w, s) => { if (w == k) boosts.Add(s); };
            Action<RaceKart> onH = w => { if (w == k) hops++; };
            RaceKart.ChargeLevelUp += onUp; RaceKart.ChargeJumped += onJ; RaceKart.BoostStart += onB; RaceKart.Hopped += onH;
            yield return StartCoroutine(PrepJump(k, src, holdSteps));
            float y0 = k.Position.y, maxY = y0;
            bool sawAir = false, sawJumping = false, sawTricking = false, invAir = false, airHit = true, airChecked = false;
            for (int i = 0; i < 160; i++)
            {
                yield return WFF;
                if (!k.Grounded)
                {
                    sawAir = true; maxY = Mathf.Max(maxY, k.Position.y);
                    if (k.ChargeJumping) sawJumping = true;
                    if (k.Tricking)
                    {
                        sawTricking = true;
                        if (!airChecked) { airChecked = true; invAir = k.Invincible; airHit = k.Stun(1f); }   // 特技中被擊中:無效
                    }
                }
                else if (sawAir && i > 8) break;
                else if (!sawAir && i >= 40) break;
            }
            for (int i = 0; i < 4; i++) yield return WFF;
            bool postInv = k.Invincible, postHit = sawAir ? k.Stun(0.3f) : false;               // 落地後:不再無敵,可被擊中
            RaceKart.ChargeLevelUp -= onUp; RaceKart.ChargeJumped -= onJ; RaceKart.BoostStart -= onB; RaceKart.Hopped -= onH;
            float apex = maxY - y0;
            bool ok;
            string detail = "ups=[" + string.Join(",", ups) + "] jumps=[" + string.Join(",", jumps) + "] apex=" + apex.ToString("0.00") + " m boosts=[" + string.Join(",", boosts) + "] hops=" + hops
                            + " air=" + sawAir + " tricking=" + sawTricking + " invAir=" + invAir + " airHit=" + airHit + " postInv=" + postInv + " postHit=" + postHit + " grounded=" + k.Grounded;
            if (!tech) ok = ups.Count == 0 && jumps.Count == 0 && !sawAir && hops == 1;
            else if (wantLv == 0) ok = ups.Count == 0 && jumps.Count == 0 && !sawAir && boosts.Count == 0 && hops == 1;
            else
            {
                float expect = KartMath.ChargeJumpSpeed(wantLv) * KartMath.ChargeJumpSpeed(wantLv) / (2f * st.AirGravity);
                float landBoost = RaceKart.TrickBoostSeconds + 0.1f * (wantLv - 1);
                ok = ups.Count == wantLv && ups[ups.Count - 1] == wantLv && jumps.Count == 1 && jumps[0] == wantLv && Mathf.Abs(apex - expect) < 0.35f && sawJumping && sawTricking
                     && boosts.Count == 1 && Mathf.Abs(boosts[0] - landBoost) < 0.01f && invAir && !airHit && !postInv && postHit && hops == 1 && k.Grounded;
                detail += "  expectApex=" + expect.ToString("0.00") + " landBoost=" + landBoost.ToString("0.00");
            }
            Report("K9 charge jump " + name, ok, detail);
            Despawn(k); yield return null;
        }

        // ---- K10 規則:按住 + 轉向 = 一般漂移(不集氣);直行集氣中才轉向 = 轉成漂移(集氣作廢)
        IEnumerator K10_SteerRule()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var srcA = new ScriptedInput(); var srcB = new ScriptedInput();
            RaceKart a = Spawn(NextLane(), -330f, 0f, srcA, st), b = Spawn(NextLane(), -330f, 0f, srcB, st);
            int jumps = 0;
            Action<RaceKart, int> onJ = (w, l) => { if (w == a || w == b) jumps++; };
            RaceKart.ChargeJumped += onJ;
            for (int i = 0; i < 6; i++) yield return WFF;
            srcA.Cur = srcB.Cur = new KartInput { Throttle = 1f };
            for (int i = 0; i < 240; i++) yield return WFF;
            srcA.Cur = new KartInput { Throttle = 1f, Steer = 1f, DriftDown = true, DriftHeld = true };   // A:按住同時轉向
            srcB.Cur = new KartInput { Throttle = 1f, DriftDown = true, DriftHeld = true };               // B:按住直行
            yield return WFF;
            srcA.Cur = new KartInput { Throttle = 1f, Steer = 1f, DriftHeld = true };
            srcB.Cur = new KartInput { Throttle = 1f, DriftHeld = true };
            bool aCharged = false, aDrift = false, bCharged = false;
            for (int i = 0; i < 70; i++) { yield return WFF; aCharged |= a.Charging; aDrift |= a.Drifting; bCharged |= b.Charging; }
            srcB.Cur = new KartInput { Throttle = 1f, Steer = 1f, DriftHeld = true };                      // B 開始轉向 -> 漂移
            bool bDrift = false, bStillCharging = false;
            for (int i = 0; i < 12; i++) { yield return WFF; bDrift |= b.Drifting; if (i > 3) bStillCharging |= b.Charging; }
            RaceKart.ChargeJumped -= onJ;
            Report("K10 steering+hold = drift (no charge); straight hold then steer = drift", aDrift && !aCharged && bCharged && bDrift && !bStillCharging && jumps == 0,
                   "A(steer+hold) drift=" + aDrift + " charged=" + aCharged + "  B(straight) charged=" + bCharged + " -> steer: drift=" + bDrift + " stillCharging=" + bStillCharging + "  chargeJumps=" + jumps);
            Despawn(a); Despawn(b); yield return null;
        }

        sealed class WallRes
        {
            public bool Rode, Boosted, InvDuring, Bumped, Finite = true, Grounded; public float MaxT, Climb; public int Begans;
        }

        /// 在車前方(離車 3 m 的垂直距離)放一面與行進方向夾 deg 度、向右前方延伸的牆(高 12 m)。
        GameObject MakeWall(RaceKart k, float deg, bool tag)
        {
            Vector3 F = H(k.Forward).normalized, R = new Vector3(F.z, 0f, -F.x);
            float th = deg * Mathf.Deg2Rad;
            Vector3 a = F * Mathf.Cos(th) - R * Mathf.Sin(th);                // 牆的長軸(在車右邊,往前越來越靠近車的路線:車會迎面逼近牆)
            Vector3 n = -R * Mathf.Cos(th) - F * Mathf.Sin(th);               // 面向車的法線(n·F < 0 = 車在接近牆)
            Vector3 q = k.Position - n * 3f;                                   // 牆面上最近的點
            Vector3 c = q + a * 40f - n * 1f;                                  // 牆中心(厚 2 m)
            float yaw = Mathf.Atan2(-a.z, a.x) * Mathf.Rad2Deg;
            GameObject w = track.AddWall("RideWall" + deg, new Vector3(c.x - track.Origin.x, 0f, c.z - track.Origin.z), yaw, 100f, 12f, 2f);
            if (tag) w.AddComponent<RideableWall>();
            Physics.SyncTransforms();
            return w;
        }

        IEnumerator WallRideCase(WallRes r, float deg, bool tag)
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -330f, 0f, src, st);
            var boosts = new List<float>();
            Action<RaceKart> onBeg = w => { if (w == k) r.Begans++; };
            Action<RaceKart, bool> onEnd = (w, bo) => { if (w == k) r.Boosted |= bo; };
            Action<RaceKart, KartBump> onBump = (w, bp) => { if (w == k) r.Bumped = true; };
            Action<RaceKart, float> onB = (w, s) => { if (w == k) boosts.Add(s); };
            RaceKart.WallRideBegan += onBeg; RaceKart.WallRideEnded += onEnd; RaceKart.Bumped += onBump; RaceKart.BoostStart += onB;
            yield return StartCoroutine(PrepJump(k, src, 120));               // Lv3 集氣跳
            GameObject wall = MakeWall(k, deg, tag);
            float yAttach = float.NaN, maxY = -1e9f; bool invChecked = false;
            for (int i = 0; i < 240; i++)
            {
                yield return WFF;
                if (!IsFiniteV(k.Position)) r.Finite = false;
                if (k.WallRiding)
                {
                    r.Rode = true;
                    if (float.IsNaN(yAttach)) yAttach = k.Position.y;
                    maxY = Mathf.Max(maxY, k.Position.y); r.MaxT = Mathf.Max(r.MaxT, k.WallRideTime);
                    if (!invChecked) { invChecked = true; r.InvDuring = k.Invincible && !k.Stun(1f); }   // 跑牆中算特技:被擊中無效
                }
            }
            RaceKart.WallRideBegan -= onBeg; RaceKart.WallRideEnded -= onEnd; RaceKart.Bumped -= onBump; RaceKart.BoostStart -= onB;
            r.Climb = float.IsNaN(yAttach) ? 0f : maxY - yAttach;
            r.Grounded = k.Grounded;
            if (r.Boosted && !boosts.Contains(RaceKart.WallExitBoostSeconds)) r.Finite = false;   // 有給脫離加速就必須收到 0.8 s 的 BoostStart
            UnityEngine.Object.Destroy(wall);
            Despawn(k); yield return null;
        }

        IEnumerator K11_WallRide()
        {
            var shallow = new WallRes(); var steep = new WallRes(); var head = new WallRes(); var untagged = new WallRes();
            yield return StartCoroutine(WallRideCase(shallow, 12f, true));
            yield return StartCoroutine(WallRideCase(steep, 30f, true));
            yield return StartCoroutine(WallRideCase(head, 60f, true));
            yield return StartCoroutine(WallRideCase(untagged, 12f, false));
            bool a = shallow.Rode && shallow.Begans == 1 && shallow.MaxT >= 1.9f && shallow.MaxT <= 2.1f && shallow.Boosted && shallow.Climb < 1.0f && shallow.InvDuring && shallow.Grounded && shallow.Finite;
            bool b = steep.Rode && steep.MaxT <= 2.1f && steep.Climb > 1.8f && steep.Climb > shallow.Climb + 1f && steep.Boosted && steep.Finite;
            bool c = !head.Rode && head.Bumped && head.Finite;
            bool d = !untagged.Rode && untagged.Finite;
            Report("K11 wall ride: shallow angle attaches + level, steeper climbs higher, 60 deg bumps, untagged wall ignored; ends <= 2 s with exit boost", a && b && c && d,
                   "12deg: rode=" + shallow.Rode + " t=" + shallow.MaxT.ToString("0.00") + " climb=" + shallow.Climb.ToString("0.00") + " boost=" + shallow.Boosted + " inv=" + shallow.InvDuring + " landed=" + shallow.Grounded
                   + " | 30deg: rode=" + steep.Rode + " t=" + steep.MaxT.ToString("0.00") + " climb=" + steep.Climb.ToString("0.00") + " boost=" + steep.Boosted
                   + " | 60deg: rode=" + head.Rode + " bumped=" + head.Bumped + " | untagged 12deg: rode=" + untagged.Rode);
        }

        sealed class RailRes
        {
            public bool Began, Boosted, InvDuring, InvExit, PostStun, Finite = true, Grounded, Boosting; public int Begans, Hops, GrindSteps;
            public float MaxLat, MaxHeightErr, MinSpeed = 1e9f; public List<float> Boosts = new List<float>();
        }

        IEnumerator RailCase(RailRes r, float steer, bool hopPress, bool jump)
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -330f, 0f, src, st);
            Action<RaceKart> onBeg = w => { if (w == k) r.Begans++; };
            Action<RaceKart, bool> onEnd = (w, bo) => { if (w == k) r.Boosted |= bo; };
            Action<RaceKart> onHop = w => { if (w == k) r.Hops++; };
            Action<RaceKart, float> onB = (w, s) => { if (w == k) r.Boosts.Add(s); };
            RaceKart.RailBegan += onBeg; RaceKart.RailEnded += onEnd; RaceKart.RailHopped += onHop; RaceKart.BoostStart += onB;
            if (jump) yield return StartCoroutine(PrepJump(k, src, 120));
            else
            {
                for (int i = 0; i < 6; i++) yield return WFF;
                src.Cur = new KartInput { Throttle = 1f };
                for (int i = 0; i < 246; i++) yield return WFF;
            }
            Vector3 P = k.Position, F = H(k.Forward).normalized, R = new Vector3(F.z, 0f, -F.x);
            float ry = track.Origin.y + 2f;                                    // 軌道高度:離地 2 m
            var pts = new Vector3[12];
            for (int i = 0; i < pts.Length; i++) { Vector3 q = P + F * (6f + 4f * i); q.y = ry; pts[i] = q; }
            KartRails.Add(KartRail.FromPoints(pts, 77));
            src.Cur = new KartInput { Throttle = 1f, Steer = steer };
            bool exitChecked = false, pressNow = false;
            for (int i = 0; i < 260; i++)
            {
                yield return WFF;
                if (pressNow) { src.Cur = new KartInput { Throttle = 1f, Steer = steer }; pressNow = false; }   // 等 Update 讀過一次按下之後才放開
                if (!IsFiniteV(k.Position)) r.Finite = false;
                if (k.RailGrinding)
                {
                    r.Began = true; r.GrindSteps++;
                    r.MaxLat = Mathf.Max(r.MaxLat, Mathf.Abs(Vector3.Dot(k.Position - P, R)));
                    if (r.GrindSteps > 8) { r.MaxHeightErr = Mathf.Max(r.MaxHeightErr, Mathf.Abs(k.Position.y - ry - RaceKart.RailRideH)); r.MinSpeed = Mathf.Min(r.MinSpeed, k.Speed); }
                    if (r.GrindSteps == 6) r.InvDuring = k.Invincible && !k.Stun(1f);
                    if (hopPress && r.GrindSteps == 30) { src.Cur = new KartInput { Throttle = 1f, Steer = steer, DriftDown = true }; pressNow = true; }
                }
                else if (r.Began && !k.Grounded && !exitChecked)
                {
                    exitChecked = true; r.Boosting = k.Boosting;
                    r.InvExit = k.Tricking && k.Invincible && !k.Stun(1f);   // 脫離後的特技(尾端脫離會自動特技)到落地都無敵
                }
                else if (r.Began && k.Grounded && exitChecked && !r.PostStun) { r.PostStun = k.Stun(0.2f); break; }
            }
            r.Grounded = k.Grounded;
            RaceKart.RailBegan -= onBeg; RaceKart.RailEnded -= onEnd; RaceKart.RailHopped -= onHop; RaceKart.BoostStart -= onB;
            KartRails.Clear();
            Despawn(k); yield return null;
        }

        IEnumerator K12_Rail()
        {
            var a = new RailRes(); var b = new RailRes(); var c = new RailRes();
            yield return StartCoroutine(RailCase(a, 0f, false, true));        // 集氣跳上軌道,直行
            yield return StartCoroutine(RailCase(b, 1f, true, true));         // 往右微調 + 跳躍加速
            yield return StartCoroutine(RailCase(c, 0f, false, false));       // 對照:不跳,從軌道下面開過去(軌道在 2 m 高,車底 0.03 m)
            bool okA = a.Began && a.Begans == 1 && a.Boosted && a.GrindSteps >= 40 && a.MaxLat < 0.4f && a.MaxHeightErr < 0.35f && a.MinSpeed > 20f && a.Boosts.Contains(RaceKart.RailExitBoostSeconds)
                       && a.Boosting && a.InvDuring && a.InvExit && a.PostStun && a.Finite;
            bool okB = b.Began && b.Hops == 1 && b.Boosts.Contains(RaceKart.RailHopBoostSeconds) && b.MaxLat > 0.7f && b.MaxLat < 1.2f && b.Boosted && b.Finite;
            bool okC = !c.Began && c.Begans == 0 && c.Finite;
            Report("K12 rail grind: lands on rail, follows it, steers a little, trick-key hop boost, exit boost + trick (invincible), no rail from underneath", okA && okB && okC,
                   "A: grind=" + a.GrindSteps + " steps lat=" + a.MaxLat.ToString("0.00") + " heightErr=" + a.MaxHeightErr.ToString("0.00") + " minSpeed=" + a.MinSpeed.ToString("0.0") + " exitBoost=" + a.Boosted + " boosts=[" + string.Join(",", a.Boosts) + "] invGrind=" + a.InvDuring + " invExit=" + a.InvExit + " postStun=" + a.PostStun
                   + " | B: hops=" + b.Hops + " maxLat=" + b.MaxLat.ToString("0.00") + " boosts=[" + string.Join(",", b.Boosts) + "] | C(control): began=" + c.Began);
        }

        // ---- K13 特技無敵可關閉(TrickInvincible = false):特技中被擊中有效
        IEnumerator K13_TrickInvToggle()
        {
            var st = KartPresets.For(KartStyle.Brick, 150);
            var src = new ScriptedInput();
            RaceKart k = Spawn(NextLane(), -330f, 0f, src, st);
            RaceKart.TrickInvincible = false;
            bool inv = true, hit = false;
            try
            {
                yield return StartCoroutine(PrepJump(k, src, 48));
                for (int i = 0; i < 60 && !hit; i++)
                {
                    yield return WFF;
                    if (!k.Grounded && k.Tricking) { inv = k.Invincible; hit = k.Stun(0.3f); }
                }
            }
            finally { RaceKart.TrickInvincible = true; }
            Report("K13 TrickInvincible=false -> trick no longer protects", !inv && hit, "invincible=" + inv + " hitLanded=" + hit);
            Despawn(k); yield return null;
        }
    }
}
