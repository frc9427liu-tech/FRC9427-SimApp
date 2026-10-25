using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -racerefereetest:驗證裁判(RaceCheckpoints 裁判規則 + RaceReferee + 捷徑資料)。
    //   FrcSim.exe -batchmode -nographics -racerefereetest   → 結果寫 racerefereetest.txt(專案/exe 同層),每項 PASS/FAIL,全 PASS 才印 "RESULT: PASS"
    //   R1 正常跑一圈 = 沒有重置、算一圈          R2 跳過門(離路切過 2 個門再回到路上)= SkippedGate 重置到最後通過的檢查點、車頭朝賽道方向
    //   R3 合法捷徑(入口門 -> 捷徑 -> 出口門)= 不重置、算過被跳過的門     R4 離開走廊 > 4 s = OffCourse(約 4.1 s);離走廊邊緣 > 45 m = 立刻(< 1.5 s)
    //   R5 逆向 > 3 s = WrongWay                R6 滑翔 / 缺口上空 / 軌道滑行 / 特技(合法離路)= 不重置;對照組(不合法)會重置
    //   R7 真車(RaceKart)快速模式:重置後在重置點、朝賽道方向、無敵 1.5 s       R8 真車動畫模式:被裁判吊著(Frozen、Held)約 3 s 後放回、解凍、無敵
    //   R9 捷徑資料:Dune Dash / Frost Peak 各 1 條(鏡像 + 反向也在)、Pine Hollow 沒有;入口 / 出口門、離主線距離、土路、加速板、坡度    R10 裁判造型三角形 <= 350
    public sealed class RaceRefereeTest : MonoBehaviour
    {
        const float Dt = 0.02f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-racerefereetest") < 0) return;
            var g = new GameObject("RaceRefereeTest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceRefereeTest>();
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;
        string tag = "";

        void Report(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS " : "FAIL ") + tag + name + " - " + detail);
            if (!ok) fails++;
        }

        void Run(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Report(name, false, "exception: " + e); }
        }

        // ------------------------------------------------------------------ 幽靈車

        sealed class Ghost : IRacerBody, IRacerAirState
        {
            public Vector3 Pos, Vel, Fwd = Vector3.forward;
            public bool Legit, Glide;
            public int Respawns;
            public Pose LastPose;
            public int RacerId { get { return 0; } }
            public Vector3 Position { get { return Pos; } }
            public Vector3 Velocity { get { return Vel; } }
            public Vector3 Forward { get { return Fwd; } }
            public bool ThrottleHeld { get { return true; } }
            public RacerProgress Progress { get; set; }
            public bool LegitOffRoad { get { return Legit; } }
            public bool Gliding { get { return Glide; } }
            public void Respawn(Pose p) { Pos = p.position; Vel = Vector3.zero; Fwd = p.rotation * Vector3.forward; Respawns++; LastPose = p; }
        }

        struct Cmd { public float Lat, Y; public bool Legit, Glide; }

        sealed class Scn
        {
            public RaceTrack Tr; public RaceCheckpoints Cps; public Ghost G;
            public float T; public bool Reset; public RefereeCause Cause; public float ResetAt; public int ResetCount;
            public Pose Before;      // 重置前最後通過的檢查點重置點
            public int LastPassedBefore;
        }

        static Scn NewScn(RaceTrack tr, int laps = 2)
        {
            var sc = new Scn { Tr = tr, G = new Ghost() };
            sc.G.Pos = tr.Grid(0).position; sc.G.Fwd = tr.Grid(0).rotation * Vector3.forward;
            sc.Cps = new RaceCheckpoints(tr, laps, false);
            sc.Cps.RefereeRules = true;
            sc.Cps.Register(sc.G, true);
            sc.Cps.StartRace(0f);
            sc.Cps.Reset += (id, why) =>
            {
                if (!sc.Reset) { sc.Reset = true; sc.Cause = sc.Cps.LastCause(id); sc.ResetAt = sc.T; sc.Before = sc.G.LastPose; }
                sc.ResetCount++;
            };
            return sc;
        }

        static void Move(Scn sc, Vector3 pos, Vector3 fwd, Vector3 vel, bool legit = false, bool glide = false)
        {
            sc.G.Pos = pos; sc.G.Fwd = fwd; sc.G.Vel = vel; sc.G.Legit = legit; sc.G.Glide = glide;
            sc.T += Dt;
            sc.Cps.Step(Dt, sc.T);
        }

        /// 沿主線中心線從 s0 走到 s1(速度 speed),每步套用 fn(s) 的橫向位移 / 高度 / 合法旗標。stop = 第一次重置就停。
        static void DriveMain(Scn sc, float s0, float s1, float speed, Func<float, Cmd> fn, bool stop = true)
        {
            RaceTrack tr = sc.Tr;
            float s = s0, ds = speed * Dt;
            while (s < s1 && !(stop && sc.Reset))
            {
                TrackSample sm = tr.SampleAt(s);
                Cmd c = fn != null ? fn(s) : default(Cmd);
                Vector3 p = sm.Pos + sm.Right * c.Lat + Vector3.up * c.Y;
                Move(sc, p, sm.Tangent, sm.Tangent * speed, c.Legit, c.Glide);
                s += ds;
            }
        }

        static void DriveShortcut(Scn sc, ShortcutPath p, float speed)
        {
            float d = 0f, dd = speed * Dt;
            while (d <= p.Length)
            {
                TrackSample sm = p.SampleAt(d);
                Move(sc, sm.Pos, sm.Tangent, sm.Tangent * speed);
                d += dd;
            }
        }

        static float Smooth(float a, float b, float x) { float u = Mathf.Clamp01((x - a) / (b - a)); return u * u * (3f - 2f * u); }

        // ------------------------------------------------------------------ 流程

        IEnumerator Start()
        {
            yield return null; yield return null;
            float oldFixed = Time.fixedDeltaTime; int oldCap = Time.captureFramerate;
            Time.fixedDeltaTime = Dt; Time.captureFramerate = 50;
            RaceKart.IgnoreMenuBlock = true;
            RaceReferee.Fast = false;

            foreach (int id in new[] { 4, 5 })
            {
                tag = "[" + id + " " + DefaultTracks.NameOf(id, true) + "] ";
                var cfg = new RaceConfig { TrackId = id, Laps = 2, Seed = 9427 };
                RaceWorld.Build(cfg, null, RaceQuality.Low);
                RaceTrack tr = RaceWorld.Track;
                yield return new WaitForFixedUpdate();
                Run("R1 legit lap", () => R1(tr));
                Run("R2 skipped gates", () => R2(tr));
                Run("R3 valid shortcut", () => R3(tr));
                Run("R4 off corridor", () => R4(tr));
                Run("R5 wrong way", () => R5(tr));
                Run("R6 legit off-road", () => R6(tr));
                Run("R9 shortcut data", () => R9(tr, id));
                if (id == 4)
                {
                    RaceKart.Ground = RaceWorld.SampleGround;
                    yield return StartCoroutine(RealKarts(tr, cfg));
                    RaceKart.Ground = null;
                }
                RaceWorld.Destroy();
                yield return null;
            }

            tag = "[3 Pine Hollow] ";
            Run("R9b no shortcut", () =>
            {
                RaceTrack t6 = RaceTrack.FromDef(DefaultTracks.Get(6));
                Report("R9b Pine Hollow has no shortcut", t6.Shortcuts.Count == 0, "shortcuts=" + t6.Shortcuts.Count);
            });
            tag = "";
            Run("R9c variants", R9c);
            Run("R10 referee mesh", () =>
            {
                int tris = RaceReferee.CloudTriangles;
                Report("R10 referee mesh triangles", tris > 100 && tris <= 350, "triangles=" + tris + " (want 100..350)");
            });

            RaceKart.IgnoreMenuBlock = false;
            Time.captureFramerate = oldCap; Time.fixedDeltaTime = oldFixed;
            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "racerefereetest.txt"), sb.ToString()); }
            catch (Exception e) { Debug.LogError("[RaceRefereeTest] write failed: " + e); }
            Debug.Log("[RaceRefereeTest]\n" + sb);
            Application.Quit(fails == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ 幽靈車測試

        void R1(RaceTrack tr)
        {
            Scn sc = NewScn(tr, 2);
            DriveMain(sc, -8f, tr.Length + 20f, 25f, null, false);
            RacerProgress pr = sc.G.Progress;
            Report("R1 legit lap", sc.ResetCount == 0 && pr.Lap == 1, "resets=" + sc.ResetCount + " lap=" + pr.Lap + " nextCp=" + pr.NextCp);
        }

        void R2(RaceTrack tr)
        {
            int k = 8;
            // M8:缺口變長(70~110 m),固定用第 8 個門可能剛好落在缺口裡;往後找第一組「前後 120 m 都沒有缺口」的門
            for (int kk = 8; kk + 3 < tr.CheckpointCount; kk++)
            {
                bool nearGap = false;
                for (float q = tr.Checkpoints[kk - 1].S - 120f; q <= tr.Checkpoints[kk + 2].S + 120f && !nearGap; q += 20f) nearGap = tr.InGap(q, 0f, 0f);
                if (!nearGap) { k = kk; break; }
            }
            float g0 = tr.Checkpoints[k].S, g2 = tr.Checkpoints[k + 2].S;
            Scn sc = NewScn(tr, 2);
            // 在門 k 前離開道路(橫向 30 m),經過門 k、k+1,在門 k+2 前回到路上 -> 穿過門 k+2 時沒過 k、k+1
            DriveMain(sc, -8f, g2 + 40f, 45f, s => new Cmd { Lat = 30f * (Smooth(g0 - 42f, g0 - 22f, s) - Smooth(g2 - 45f, g2 - 25f, s)) });
            bool pose = false; float dot = 0f; float dist = 0f;
            if (sc.Reset)
            {
                Checkpoint last = tr.Checkpoints[k - 1];
                dist = Vector3.Distance(sc.Before.position, last.Respawn.position);
                dot = Vector3.Dot(sc.Before.rotation * Vector3.forward, last.Forward);
                pose = dist < 0.01f && dot > 0.9f;
            }
            Report("R2 skipped gates -> reset", sc.Reset && sc.Cause == RefereeCause.SkippedGate && sc.ResetCount == 1 && pose,
                "reset=" + sc.Reset + " cause=" + sc.Cause + " count=" + sc.ResetCount + " at=" + sc.ResetAt.ToString("0.0") + "s poseDist=" + dist.ToString("0.00") + " fwdDot=" + dot.ToString("0.00"));
        }

        void R3(RaceTrack tr)
        {
            ShortcutPath p = tr.Shortcuts[0];
            Scn sc = NewScn(tr, 2);
            DriveMain(sc, -8f, p.FromS - 1f, 25f, null, false);
            bool onSc = false;
            RacerProgress pr = sc.G.Progress;
            float d = 0f;
            while (d <= p.Length)
            {
                TrackSample sm = p.SampleAt(d);
                Move(sc, sm.Pos, sm.Tangent, sm.Tangent * 25f);
                if (pr.OnShortcut) onSc = true;
                d += 25f * Dt;
            }
            int nextAfter = pr.NextCp;
            DriveMain(sc, p.ToS + 1f, tr.Length + 20f, 25f, null, false);
            Report("R3 valid shortcut -> no reset", sc.ResetCount == 0 && onSc && nextAfter == p.LastSkipped + 1 && pr.Lap == 1,
                "resets=" + sc.ResetCount + " sawOnShortcut=" + onSc + " nextCpAfterExit=" + nextAfter + " (want " + (p.LastSkipped + 1) + ") lap=" + pr.Lap);
        }

        void R4(RaceTrack tr)
        {
            // 離走廊 > 4 s:橫向 25 m(路半寬 6 + 路肩 8 = 14,超出 11 m)平行慢慢開
            Scn sc = NewScn(tr, 2);
            DriveMain(sc, -8f, 100f, 25f, null);
            float t0 = sc.T;
            DriveMain(sc, 100f, 100f + 10f * 7f, 10f, s => new Cmd { Lat = 25f });
            float dtR = sc.ResetAt - t0;
            bool a = sc.Reset && sc.Cause == RefereeCause.OffCourse && dtR > 3.8f && dtR < 4.7f;
            Report("R4a off corridor > 4 s -> reset", a, "reset=" + sc.Reset + " cause=" + sc.Cause + " after=" + dtR.ToString("0.00") + "s (want ~4.1)");

            // 3 s 後回到路上 = 不重置
            Scn s2 = NewScn(tr, 2);
            DriveMain(s2, -8f, 70f, 25f, null);                                   // 門之間(間距 62 m)離路 3 s,不漏門
            DriveMain(s2, 70f, 100f, 10f, s => new Cmd { Lat = 25f }, true);
            DriveMain(s2, 100f, 200f, 10f, null, true);
            Report("R4b off corridor 3 s then back -> no reset", !s2.Reset, "reset=" + s2.Reset + " cause=" + s2.Cause);

            // 離走廊 > 45 m:找一個橫向 80 m 仍是陸地的位置
            float sFound = -1f, latFound = 0f;
            for (float s = 150f; s < tr.Length - 150f && sFound < 0f; s += 40f)
                foreach (float lat in new[] { 80f, -80f })
                {
                    TrackSample sm = tr.SampleAt(s);
                    Vector3 p = sm.Pos + sm.Right * lat;
                    Surface sf = RaceWorld.SurfaceAt(p.x, p.z);
                    if (sf != Surface.Water && sf != Surface.OutOfBounds && RaceWorld.HeightAt(p.x, p.z) > 1f && DistToShortcut(tr, p) > 60f && GlobalRoadDist(tr, p) > 70f) { sFound = s; latFound = lat; break; }
                }
            if (sFound < 0f) { sb.AppendLine("SKIP " + tag + "R4c hard distance - no land 80 m from the road"); return; }
            Scn s3 = NewScn(tr, 2);
            DriveMain(s3, -8f, sFound, 40f, null);
            float t3 = s3.T;
            DriveMain(s3, sFound, sFound + 10f * 4f, 10f, s => new Cmd { Lat = latFound });
            Report("R4c far from corridor (> 45 m) -> fast reset", s3.Reset && s3.Cause == RefereeCause.OffCourse && s3.ResetAt - t3 < 1.5f,
                "reset=" + s3.Reset + " cause=" + s3.Cause + " after=" + (s3.ResetAt - t3).ToString("0.00") + "s lat=" + latFound + " s=" + sFound);
        }

        static float GlobalRoadDist(RaceTrack tr, Vector3 p) { float lat; tr.Project(p, -1f, out lat); return Mathf.Abs(lat); }

        static float DistToShortcut(RaceTrack tr, Vector3 p)
        {
            float best = 1e9f;
            foreach (var sc in tr.Shortcuts) { int ia; float u, hw; best = Mathf.Min(best, sc.Nearest(p.x, p.z, out ia, out u, out hw)); }
            return best;
        }

        void R5(RaceTrack tr)
        {
            Scn sc = NewScn(tr, 2);
            float s = 300f;
            for (int i = 0; i < 300 && !sc.Reset; i++)
            {
                TrackSample sm = tr.SampleAt(s);
                Move(sc, sm.Pos, -sm.Tangent, -sm.Tangent * 20f);
                s -= 20f * Dt;
            }
            Report("R5 wrong way > 3 s -> reset", sc.Reset && sc.Cause == RefereeCause.WrongWay && sc.ResetAt > 2.9f && sc.ResetAt < 3.6f,
                "reset=" + sc.Reset + " cause=" + sc.Cause + " at=" + sc.ResetAt.ToString("0.00") + "s (want ~3.1)");
            // 慢速倒退(< 6 m/s)不算
            Scn s2 = NewScn(tr, 2);
            s = 300f;
            for (int i = 0; i < 400 && !s2.Reset; i++)
            {
                TrackSample sm = tr.SampleAt(s);
                Move(s2, sm.Pos, -sm.Tangent, -sm.Tangent * 4f);
                s -= 4f * Dt;
            }
            Report("R5b slow reverse (4 m/s) -> no reset", !s2.Reset, "reset=" + s2.Reset);
        }

        void R6(RaceTrack tr)
        {
            // 對照組 + 合法離路
            Scn bad = NewScn(tr, 2);
            DriveMain(bad, -8f, 100f, 25f, null);
            DriveMain(bad, 100f, 160f, 10f, s => new Cmd { Lat = 25f });
            Scn ok = NewScn(tr, 2);
            DriveMain(ok, -8f, 100f, 25f, null);
            DriveMain(ok, 100f, 160f, 10f, s => new Cmd { Lat = 25f, Legit = true });
            Report("R6a legit off-road (glide/rail/wall/trick) 6 s -> no reset", bad.Reset && !ok.Reset, "control reset=" + bad.Reset + " legit reset=" + ok.Reset);

            // 缺口:滑翔飛越(高度 +6,Gliding)整段不重置、算過缺口中央的門
            if (tr.Gaps.Count == 0) { sb.AppendLine("SKIP " + tag + "R6b gap glide - no gap"); return; }
            GapInfo g = tr.Gaps[0];
            Scn sc = NewScn(tr, 2);
            DriveMain(sc, -8f, g.S0 - 40f, 40f, null, false);
            DriveMain(sc, g.S0 - 40f, g.S1 + 40f, 40f, s => new Cmd { Y = (s > g.S0 - 14f && s < g.S1 + 2f) ? 6f : 0f, Glide = s > g.S0 - 14f && s < g.S1 + 2f, Legit = s > g.S0 - 14f && s < g.S1 + 2f }, false);
            int idxMid = tr.NextCheckpoint(g.Mid);
            Report("R6b gap glide flight -> no reset", sc.ResetCount == 0 && sc.G.Progress.NextCp != 0, "resets=" + sc.ResetCount + " cause=" + sc.Cause + " nextCp=" + sc.G.Progress.NextCp);

            // 滑翔斜切:Gliding 時在缺口前把門 k、k+1 都甩在路外(橫向 30 m),回到路上穿過 k+2 -> 補記而非重置
            int k = 8;
            float g0 = tr.Checkpoints[k].S, g2 = tr.Checkpoints[k + 2].S;
            Scn gl = NewScn(tr, 2);
            DriveMain(gl, -8f, g2 + 40f, 45f, s => { float l = 30f * (Smooth(g0 - 42f, g0 - 22f, s) - Smooth(g2 - 45f, g2 - 25f, s)); bool fly = s > g0 - 42f && s < g2 + 6f; return new Cmd { Lat = l, Y = fly ? 5f : 0f, Glide = fly, Legit = fly }; });
            Report("R6c gliding past gates -> credited, not reset", !gl.Reset && gl.G.Progress.NextCp == k + 3, "reset=" + gl.Reset + " nextCp=" + gl.G.Progress.NextCp + " (want " + (k + 3) + ")");
        }

        // ------------------------------------------------------------------ 捷徑資料

        void R9(RaceTrack tr, int id)
        {
            var errs = new List<string>();
            if (tr.Shortcuts.Count != 1) { Report("R9 shortcut data", false, "shortcuts=" + tr.Shortcuts.Count + " (want 1)"); return; }
            ShortcutPath sc = tr.Shortcuts[0];
            int skipped = sc.FirstSkipped >= 0 ? sc.LastSkipped - sc.FirstSkipped + 1 : 0;
            if (sc.FirstSkipped < 1 || skipped < 2) errs.Add("skipped gates " + sc.FirstSkipped + ".." + sc.LastSkipped);
            if (sc.Entry == null || sc.Exit == null || sc.Entry.S >= sc.Exit.S) errs.Add("entry/exit gates");
            float ratio = sc.Length / sc.MainSpan;
            if (ratio < 0.28f || ratio > 0.70f) errs.Add("length ratio " + ratio.ToString("0.00"));
            float maxSlope = 0f, minSep = 1e9f; int surfBad = 0, surfN = 0;
            for (int i = 0; i + 1 < sc.Count; i++)
            {
                Vector3 p = sc.Pos[i], q = sc.Pos[i + 1];
                maxSlope = Mathf.Max(maxSlope, Mathf.Atan2(Mathf.Abs(q.y - p.y), Mathf.Max(0.01f, new Vector2(q.x - p.x, q.z - p.z).magnitude)) * Mathf.Rad2Deg);
                float d = i * sc.Step;
                if (d >= 40f && d <= sc.Length - 40f) { float lat; tr.Project(p, -1f, out lat); minSep = Mathf.Min(minSep, Mathf.Abs(lat)); }
                if (d >= 30f && d <= sc.Length - 30f) { surfN++; Surface sf = RaceWorld.SurfaceAt(p.x, p.z); if (sf != Surface.Dirt && sf != Surface.BoostPad) surfBad++; }
            }
            if (maxSlope > 15f) errs.Add("slope " + maxSlope.ToString("0.0") + " deg");
            if (minSep < 22f) errs.Add("comes within " + minSep.ToString("0.0") + " m of the main road");
            if (surfBad > 0) errs.Add(surfBad + "/" + surfN + " shortcut points are not Dirt/BoostPad");
            int pads = 0;
            for (int i = 0; i < tr.Features.Count; i++) if (tr.Features[i].path == 0 && tr.Features[i].kind == (int)TrackFeatureKind.Boost) pads++;
            if (pads < 2) errs.Add("pads=" + pads);
            float lat0; tr.Project(sc.Entry.Center, -1f, out lat0); float lat1; tr.Project(sc.Exit.Center, -1f, out lat1);
            if (Mathf.Abs(lat0) < 12f || Mathf.Abs(lat1) < 12f) errs.Add("gate too close to main road " + Mathf.Abs(lat0).ToString("0.0") + "/" + Mathf.Abs(lat1).ToString("0.0"));
            for (int i = 0; i < tr.Gaps.Count; i++) if (sc.ToS > tr.Gaps[i].S0 - 100f && sc.FromS < tr.Gaps[i].S1 + 100f) errs.Add("shortcut near gap " + i);
            Report("R9 shortcut data", errs.Count == 0,
                "from=" + sc.FromS.ToString("0") + " to=" + sc.ToS.ToString("0") + " span=" + sc.MainSpan.ToString("0") + " len=" + sc.Length.ToString("0") + " ratio=" + ratio.ToString("0.00") + " skipped=" + sc.FirstSkipped + ".." + sc.LastSkipped
                + " pads=" + pads + " maxSlope=" + maxSlope.ToString("0.0") + " minSep=" + minSep.ToString("0.0") + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        void R9c()
        {
            foreach (int id in new[] { 4, 5 })
                for (int v = 1; v < 4; v++)
                {
                    bool m = (v & 1) != 0, r = (v & 2) != 0;
                    TrackDef d = TrackVariants.Apply(DefaultTracks.Get(id), m, r);
                    RaceTrack t = RaceTrack.FromDef(d);
                    bool ok = t.Shortcuts.Count == 1 && t.Shortcuts[0].FirstSkipped >= 1 && t.Shortcuts[0].LastSkipped > t.Shortcuts[0].FirstSkipped;
                    Report("R9c " + DefaultTracks.NameOf(id, true) + (m ? " mirror" : "") + (r ? " reverse" : "") + " keeps its shortcut", ok, "shortcuts=" + t.Shortcuts.Count);
                }
        }

        // ------------------------------------------------------------------ 真車

        IEnumerator RealKarts(RaceTrack tr, RaceConfig cfg)
        {
            tag = "[4 Dune Dash] ";
            var root = new GameObject("RaceRefereeTestRoot");
            var cps = new RaceCheckpoints(tr, 2, false);
            var kart = RaceKart.Create(root.transform, tr.Grid(0), KartPresets.For(KartStyle.Brick), 0, "T");
            kart.IsPlayer = true;
            kart.Frozen = true;
            cps.Register(kart, true);
            RaceReferee.Attach(cps, root.transform);
            RaceKart.FellOut += k => cps.RequestReset(k.KartId, ResetReason.Fell);
            yield return new WaitForFixedUpdate();
            kart.Frozen = false;
            cps.StartRace(0f);
            float t = 0f;

            // R7 快速模式
            RaceReferee.Fast = true;
            Pose want = cps.GetRespawnPose(0);
            int hooks0 = RaceReferee.HookCalls;
            kart.Teleport(tr.SampleAt(500f).Pos + tr.SampleAt(500f).Right * 40f + Vector3.up * 2f, Quaternion.identity);
            cps.RequestReset(0, ResetReason.OffTrackTooLong, RefereeCause.SkippedGate);
            Vector3 fw = kart.Forward; fw.y = 0f;
            float dist = Vector3.Distance(kart.Position, want.position + Vector3.up * RaceKart.RideDist);
            bool inv = kart.Invincible;
            bool fwdOk = Vector3.Dot(fw.normalized, want.rotation * Vector3.forward) > 0.9f;
            Report("R7 fast reset: at checkpoint, facing forward, invincible", RaceReferee.HookCalls == hooks0 + 1 && dist < 1.5f && fwdOk && inv && !cps.IsHeld(0) && !string.IsNullOrEmpty(RaceReferee.LastMessage),
                "dist=" + dist.ToString("0.00") + " fwdOk=" + fwdOk + " invincible=" + inv + " msg=\"" + RaceReferee.LastMessage + "\"");
            for (int i = 0; i < 95; i++) { yield return new WaitForFixedUpdate(); t += Dt; cps.Step(Dt, t); }
            Report("R7b invincibility ends after ~1.5 s", !kart.Invincible, "invincible=" + kart.Invincible);

            // R8 動畫模式
            RaceReferee.Fast = false;
            RaceSession.TestFast = false;
            kart.Teleport(tr.SampleAt(900f).Pos + tr.SampleAt(900f).Right * 40f + Vector3.up * 2f, Quaternion.identity);
            yield return new WaitForFixedUpdate();
            int anim0 = RaceReferee.Animated;
            want = cps.GetRespawnPose(0);
            cps.RequestReset(0, ResetReason.OffTrackTooLong, RefereeCause.OffCourse);
            bool heldNow = cps.IsHeld(0), frozenNow = kart.Frozen;
            float sawHeldFor = 0f;
            for (int i = 0; i < 300 && cps.IsHeld(0); i++) { yield return new WaitForFixedUpdate(); t += Dt; cps.Step(Dt, t); sawHeldFor += Dt; }
            yield return new WaitForFixedUpdate();
            dist = Vector3.Distance(kart.Position, want.position + Vector3.up * RaceKart.RideDist);
            Report("R8 animated referee: held ~2-4 s, then released at checkpoint, unfrozen, invincible",
                RaceReferee.Animated == anim0 + 1 && heldNow && frozenNow && sawHeldFor > 1.8f && sawHeldFor < 4.5f && !cps.IsHeld(0) && !kart.Frozen && dist < 1.5f && kart.Invincible,
                "animated=" + (RaceReferee.Animated - anim0) + " heldNow=" + heldNow + " frozen=" + frozenNow + " heldFor=" + sawHeldFor.ToString("0.00") + "s dist=" + dist.ToString("0.00") + " unfrozen=" + !kart.Frozen + " invincible=" + kart.Invincible);

            RaceReferee.Fast = false;
            if (RaceReferee.Active != null) Destroy(RaceReferee.Active.gameObject);
            Destroy(root);
            yield return null;
        }
    }
}
