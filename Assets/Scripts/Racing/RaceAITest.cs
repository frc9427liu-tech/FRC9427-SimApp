using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -raceaitest:驗證模組 I 的 AI(M3,不含道具)。
    //   FrcSim.exe -batchmode -nographics -raceaitest   → 結果寫 raceaitest.txt(專案/exe 同層),每項 PASS/FAIL,全 PASS 才印 "RESULT: PASS"
    // 建賽道 → 生成 7 台 AI(同車款、Level 2)→ 跑 3 圈 → 輸出名次與完賽時間。整場跑兩次(同 seed)比較可重現性。
    // 用 Time.captureFramerate = 50 讓每個渲染幀剛好 0.02 s、一個 FixedUpdate,AI 的 Update 取樣才確定。
    //   A1 不卡牆:每台速度 ≥ 4 m/s 的時間佔比 ≥ 88%,且卡住重置 ≤ 2 次
    //   A2 全部完賽(模擬時間上限 900 s)
    //   A3 名次分布:完賽時間皆不同(1..7 各一),首尾差 < 25%,贏家時間在合理範圍
    //   A4 可重現:同 seed 跑兩次,名次相同且完賽時間差 ≤ 0.2 s
    //   A5 橡皮筋純函式:方向、死區、最後 25% 關閉、視野上限
    //   M7:新賽道(3..7,有飛越缺口)每條另外跑 1 圈(4 台 AI,Level 2;可用 -raceaitrack N 只跑第 N 條,跳過上面的 7 車 3 圈):
    //   B1 全部完賽  B2 不卡住(慢速比例、重置次數)  B3 缺口:每個缺口至少有車「乾淨」飛過去(沒掉下去),掉下去的重置次數有上限  B4 完賽時間合理
    public class RaceAITest : MonoBehaviour
    {
        const float Dt = 0.02f;
        const int Karts = 7;
        const float SlowSpeed = 4f, SlowLimit = 0.12f, SimCap = 900f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-raceaitest") < 0) return;
            var g = new GameObject("RaceAITest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceAITest>();
        }

        sealed class Result
        {
            public float[] Finish = new float[Karts];
            public float[] SlowFrac = new float[Karts];
            public int[] Resets = new int[Karts];
            public int[] Recovers = new int[Karts];
            public int[] Order = new int[Karts];       // Order[place-1] = kart id
            public bool AllFinished;
            public float SimTime, TrackLength;
            public string Err;
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;

        void Report(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail);
            if (!ok) fails++;
        }

        // ------------------------------------------------------------------ 流程

        IEnumerator Start()
        {
            yield return null;
            yield return null;
            float oldFixed = Time.fixedDeltaTime;
            int oldCapture = Time.captureFramerate;
            Time.fixedDeltaTime = Dt;
            Time.captureFramerate = 50;
            RaceKart.IgnoreMenuBlock = true;
            bool oldDrift = RaceAI.EnableDrift;
            RaceAI.EnableDrift = false;

            int only = ArgInt("-raceaitrack", -1);
            var r1 = new Result();
            var r2 = new Result();
            if (only < 0)
            {
                yield return RunRace(9427, r1);
                yield return RunRace(9427, r2);
            }
            var newRes = new List<TrackRes>();
            for (int id = DefaultTracks.Handmade; id < DefaultTracks.Count; id++)
            {
                if (only >= 0 && id != only) continue;
                var tres = new TrackRes { Id = id };
                yield return RunTrack(id, tres);
                newRes.Add(tres);
            }

            RaceKart.IgnoreMenuBlock = false;
            RaceAI.EnableDrift = oldDrift;
            Time.captureFramerate = oldCapture;
            Time.fixedDeltaTime = oldFixed;

            if (only < 0)
            {
                try { Evaluate(r1, r2); }
                catch (Exception e) { Report("evaluate", false, "exception: " + e); }
                TestRubberband();
            }
            foreach (var tr in newRes)
            {
                try { EvaluateTrack(tr); }
                catch (Exception e) { Report("B track " + tr.Id, false, "exception: " + e); }
            }

            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "raceaitest.txt"), sb.ToString()); }
            catch (Exception e) { Debug.LogError("[RaceAITest] write failed: " + e); }
            Debug.Log("[RaceAITest]\n" + sb);
            Application.Quit(fails == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ 一場比賽

        GameObject root;
        RaceTrack track;
        RaceCheckpoints cps;
        readonly List<RaceKart> karts = new List<RaceKart>();
        readonly List<RaceAI> ais = new List<RaceAI>();
        Result cur;
        float simT, raceT0;
        bool started;
        int[] slowSteps, totSteps;
        Action<RaceKart> onRespawnPressed, onFell;
        Action<int, ResetReason> onReset;

        IEnumerator RunRace(int seed, Result res)
        {
            cur = res; simT = 0f; started = false;
            bool ok = Setup(seed);
            if (ok) yield return new WaitForFixedUpdate();   // 讓碰撞體進入物理世界
            float wall0 = Time.realtimeSinceStartup;
            while (ok)
            {
                yield return new WaitForFixedUpdate();
                if (!StepOnce()) break;
                if (Time.realtimeSinceStartup - wall0 > 420f) { res.Err = "wall-clock timeout"; break; }
            }
            Finish(res);
            yield return null;
            yield return null;
        }

        bool Setup(int seed)
        {
            try
            {
                var cfg = new RaceConfig { TrackId = 0, Laps = 3, Seed = seed, AiCount = Karts, AiLevel = 2 };
                root = new GameObject("RaceAITestRoot");
                track = RaceWorld.Build(cfg, root.transform, RaceQuality.Low);
                RaceKart.Ground = RaceWorld.SampleGround;
                cur.TrackLength = track.Length;

                cps = new RaceCheckpoints(track, cfg.Laps, false);
                karts.Clear(); ais.Clear();
                for (int i = 0; i < Karts; i++)
                {
                    var k = RaceKart.Create(root.transform, track.Grid(i), KartPresets.For(KartStyle.Brick), i, "AI" + i);
                    k.Frozen = true;
                    karts.Add(k);
                    cps.Register(k, false);
                }
                for (int i = 0; i < Karts; i++)
                    ais.Add(RaceAI.Spawn(karts[i], track, cfg, i, i == 0 ? null : karts[0]));   // 車 0 當「玩家」,其餘 6 台以它為橡皮筋參考

                slowSteps = new int[Karts]; totSteps = new int[Karts];
                onRespawnPressed = k => cps.RequestReset(k.KartId, ResetReason.Manual);
                onFell = k => cps.RequestReset(k.KartId, ResetReason.Fell);
                onReset = (id, why) => { if (id >= 0 && id < Karts) cur.Resets[id]++; };
                RaceKart.RespawnPressed += onRespawnPressed;
                RaceKart.FellOut += onFell;
                cps.Reset += onReset;
                return true;
            }
            catch (Exception e)
            {
                cur.Err = "setup exception: " + e;
                return false;
            }
        }

        // 回傳 false = 比賽結束(全部完賽或超時)
        bool StepOnce()
        {
            try
            {
                simT += Dt;
                if (!started && simT >= 1f)
                {
                    started = true; raceT0 = simT;
                    for (int i = 0; i < karts.Count; i++) karts[i].Frozen = false;
                    cps.StartRace(0f);
                }
                if (!started) return true;
                float raceT = simT - raceT0;
                cps.Step(Dt, raceT);

                int done = 0;
                for (int i = 0; i < karts.Count; i++)
                {
                    RacerProgress p = karts[i].Progress;
                    if (p != null && p.Finished) { done++; continue; }
                    if (raceT > 2f)
                    {
                        totSteps[i]++;
                        Vector3 v = karts[i].Velocity; v.y = 0f;
                        if (v.magnitude < SlowSpeed) slowSteps[i]++;
                    }
                }
                if (done >= karts.Count) { cur.AllFinished = true; cur.SimTime = raceT; return false; }
                if (raceT > SimCap) { cur.SimTime = raceT; return false; }
                return true;
            }
            catch (Exception e)
            {
                cur.Err = "step exception: " + e;
                return false;
            }
        }

        void Finish(Result res)
        {
            try
            {
                for (int i = 0; i < Karts && i < karts.Count; i++)
                {
                    RacerProgress p = karts[i].Progress;
                    res.Finish[i] = (p != null && p.Finished) ? p.FinishTime : float.PositiveInfinity;
                    res.SlowFrac[i] = totSteps != null && totSteps[i] > 0 ? (float)slowSteps[i] / totSteps[i] : 0f;
                    res.Recovers[i] = ais[i].RecoverCount;
                }
                var ids = new List<int>();
                for (int i = 0; i < Karts; i++) ids.Add(i);
                ids.Sort((a, b) => res.Finish[a] != res.Finish[b] ? res.Finish[a].CompareTo(res.Finish[b]) : a.CompareTo(b));
                for (int i = 0; i < Karts; i++) res.Order[i] = ids[i];
            }
            catch (Exception e) { res.Err = (res.Err ?? "") + " finish exception: " + e; }

            RaceKart.RespawnPressed -= onRespawnPressed;
            RaceKart.FellOut -= onFell;
            if (cps != null && onReset != null) cps.Reset -= onReset;
            for (int i = 0; i < karts.Count; i++) if (karts[i] != null) karts[i].Source = null;
            if (root != null) Destroy(root);
            try { RaceWorld.Destroy(); } catch (Exception e) { Debug.LogWarning("[RaceAITest] RaceWorld.Destroy: " + e.Message); }
            karts.Clear(); ais.Clear();
            RaceKart.Ground = null;
        }

        static int ArgInt(string flag, int def)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, flag), v;
            return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out v) ? v : def;
        }

        // ------------------------------------------------------------------ M7:新賽道(3..7)各跑 1 圈,4 台 AI

        const int NewKarts = 4;

        sealed class TrackRes
        {
            public int Id;
            public string Name = "";
            public float Length, SimTime;
            public int Gaps;
            public bool AllFinished;
            public string Err;
            public float[] Finish = new float[NewKarts];
            public float[] SlowFrac = new float[NewKarts];
            public int[] Resets = new int[NewKarts];
            public int[] GapFalls = new int[NewKarts];
            public int CleanCross;     // (車, 缺口)配對中第一次就乾淨飛過去的數量
            public int Crossed;        // (車, 缺口)配對中最後有飛過去的數量(含掉下去重來的)
            public float GlideTMin = 1e9f, GlideTMax, LandMin = 1e9f, LandMax = -1e9f;   // M8:缺口飛行的滑翔秒數 / 落地點離落地邊緣的距離(正 = 過了邊緣)
            public int Glides;
        }

        GameObject tRoot;
        RaceTrack tTrack;
        RaceCheckpoints tCps;
        readonly List<RaceKart> tKarts = new List<RaceKart>();
        readonly List<RaceAI> tAis = new List<RaceAI>();
        TrackRes tRes;
        float tSim, tRaceT0, tSimCap;
        bool tStarted;
        float[] tPrevS; bool[] tWasGlide; float[] tGT0; int[] tGGap;
        int[] tSlow, tTot;
        int[,] tState;           // 0 = 還沒進助跑段,1 = 助跑 / 飛行中,2 = 已飛過
        bool[,] tFell;
        Action<RaceKart> tOnResp, tOnFell;
        Action<int, ResetReason> tOnReset;

        IEnumerator RunTrack(int id, TrackRes res)
        {
            tRes = res; tSim = 0f; tStarted = false;
            bool ok = SetupTrack(id);
            if (ok) yield return new WaitForFixedUpdate();
            float wall0 = Time.realtimeSinceStartup;
            while (ok)
            {
                yield return new WaitForFixedUpdate();
                if (!StepTrack()) break;
                if (Time.realtimeSinceStartup - wall0 > 420f) { res.Err = "wall-clock timeout"; break; }
            }
            FinishTrack(res);
            yield return null;
            yield return null;
        }

        bool SetupTrack(int id)
        {
            try
            {
                var cfg = new RaceConfig { TrackId = id, Laps = 1, Seed = 9427, AiCount = NewKarts, AiLevel = 2 };
                tRoot = new GameObject("RaceAITestRoot");
                tTrack = RaceWorld.Build(cfg, tRoot.transform, RaceQuality.Low);
                RaceKart.Ground = RaceWorld.SampleGround;
                tRes.Length = tTrack.Length; tRes.Name = RaceWorld.TrackName(id); tRes.Gaps = tTrack.Gaps.Count;
                tSimCap = tTrack.Length / 9f + 60f;
                tCps = new RaceCheckpoints(tTrack, cfg.Laps, false);
                tKarts.Clear(); tAis.Clear();
                for (int i = 0; i < NewKarts; i++)
                {
                    var k = RaceKart.Create(tRoot.transform, tTrack.Grid(i), KartPresets.For(KartStyle.Brick), i, "AI" + i);
                    k.Frozen = true;
                    tKarts.Add(k);
                    tCps.Register(k, false);
                }
                for (int i = 0; i < NewKarts; i++) tAis.Add(RaceAI.Spawn(tKarts[i], tTrack, cfg, i, i == 0 ? null : tKarts[0]));
                tWasGlide = new bool[NewKarts]; tGT0 = new float[NewKarts]; tGGap = new int[NewKarts]; tPrevS = new float[NewKarts]; tSlow = new int[NewKarts]; tTot = new int[NewKarts];
                int ng = Mathf.Max(1, tTrack.Gaps.Count);
                tState = new int[NewKarts, ng]; tFell = new bool[NewKarts, ng];
                tOnResp = k => tCps.RequestReset(k.KartId, ResetReason.Manual);
                tOnFell = k => tCps.RequestReset(k.KartId, ResetReason.Fell);
                tOnReset = (rid, why) =>
                {
                    if (rid < 0 || rid >= NewKarts) return;
                    tRes.Resets[rid]++;
                    for (int g = 0; g < tTrack.Gaps.Count; g++)
                    {
                        GapInfo gp = tTrack.Gaps[g];
                        float d = tTrack.Wrap(tPrevS[rid] - gp.RampBase);
                        if (d > tTrack.Length * 0.5f) d -= tTrack.Length;
                        if (why == ResetReason.Fell && d >= -12f && d <= gp.Length + GapInfo.RampLength + 12f) { tRes.GapFalls[rid]++; tFell[rid, g] = true; tState[rid, g] = 0; }
                    }
                };
                RaceKart.RespawnPressed += tOnResp;
                RaceKart.FellOut += tOnFell;
                tCps.Reset += tOnReset;
                return true;
            }
            catch (Exception e)
            {
                tRes.Err = "setup exception: " + e;
                return false;
            }
        }

        bool StepTrack()
        {
            try
            {
                tSim += Dt;
                if (!tStarted && tSim >= 1f)
                {
                    tStarted = true; tRaceT0 = tSim;
                    for (int i = 0; i < tKarts.Count; i++) tKarts[i].Frozen = false;
                    tCps.StartRace(0f);
                }
                if (!tStarted) return true;
                float raceT = tSim - tRaceT0;
                for (int i = 0; i < tKarts.Count; i++) if (tKarts[i].Progress != null) tPrevS[i] = tKarts[i].Progress.S;
                tCps.Step(Dt, raceT);

                int done = 0;
                for (int i = 0; i < tKarts.Count; i++)
                {
                    RacerProgress p = tKarts[i].Progress;
                    if (p == null) continue;
                    if (p.Finished) { done++; continue; }
                    if (raceT > 2f)
                    {
                        tTot[i]++;
                        Vector3 v = tKarts[i].Velocity; v.y = 0f;
                        if (v.magnitude < SlowSpeed) tSlow[i]++;
                    }
                    {
                        bool gl = tKarts[i].Gliding;
                        if (gl && !tWasGlide[i])
                        {
                            tGGap[i] = -1;
                            for (int g = 0; g < tTrack.Gaps.Count; g++) { float d0 = tTrack.Wrap(p.S - tTrack.Gaps[g].S0); if (d0 > tTrack.Length * 0.5f) d0 -= tTrack.Length; if (d0 > -25f && d0 < 14f) tGGap[i] = g; }
                            tGT0[i] = tSim;
                        }
                        if (!gl && tWasGlide[i] && tGGap[i] >= 0)
                        {
                            float d1 = tTrack.Wrap(p.S - tTrack.Gaps[tGGap[i]].S1); if (d1 > tTrack.Length * 0.5f) d1 -= tTrack.Length;
                            float gt = tSim - tGT0[i];
                            tRes.Glides++; tRes.GlideTMin = Mathf.Min(tRes.GlideTMin, gt); tRes.GlideTMax = Mathf.Max(tRes.GlideTMax, gt);
                            tRes.LandMin = Mathf.Min(tRes.LandMin, d1); tRes.LandMax = Mathf.Max(tRes.LandMax, d1);
                        }
                        tWasGlide[i] = gl;
                    }
                    for (int g = 0; g < tTrack.Gaps.Count; g++)
                    {
                        GapInfo gp = tTrack.Gaps[g];
                        float s = p.S;
                        if (tState[i, g] == 0 && s >= gp.RampBase - 60f && s <= gp.S0) tState[i, g] = 1;
                        else if (tState[i, g] == 1 && s >= gp.S1 + 15f && s <= gp.S1 + 90f)
                        {
                            tState[i, g] = 2; tRes.Crossed++;
                            if (!tFell[i, g]) tRes.CleanCross++;
                        }
                    }
                }
                if (done >= tKarts.Count) { tRes.AllFinished = true; tRes.SimTime = raceT; return false; }
                if (raceT > tSimCap) { tRes.SimTime = raceT; return false; }
                return true;
            }
            catch (Exception e)
            {
                tRes.Err = "step exception: " + e;
                return false;
            }
        }

        void FinishTrack(TrackRes res)
        {
            try
            {
                for (int i = 0; i < NewKarts && i < tKarts.Count; i++)
                {
                    RacerProgress p = tKarts[i].Progress;
                    res.Finish[i] = (p != null && p.Finished) ? p.FinishTime : float.PositiveInfinity;
                    res.SlowFrac[i] = tTot[i] > 0 ? (float)tSlow[i] / tTot[i] : 0f;
                }
            }
            catch (Exception e) { res.Err = (res.Err ?? "") + " finish exception: " + e; }
            RaceKart.RespawnPressed -= tOnResp;
            RaceKart.FellOut -= tOnFell;
            if (tCps != null && tOnReset != null) tCps.Reset -= tOnReset;
            for (int i = 0; i < tKarts.Count; i++) if (tKarts[i] != null) tKarts[i].Source = null;
            if (tRoot != null) Destroy(tRoot);
            try { RaceWorld.Destroy(); } catch (Exception e) { Debug.LogWarning("[RaceAITest] RaceWorld.Destroy: " + e.Message); }
            tKarts.Clear(); tAis.Clear(); tCps = null; tTrack = null; tRoot = null;
            RaceKart.Ground = null;
        }

        void EvaluateTrack(TrackRes r)
        {
            string tag = "[" + r.Id + " " + r.Name + "] ";
            sb.AppendLine(tag + "length " + r.Length.ToString("F0") + " m, 1 lap, karts " + NewKarts + ", gaps " + r.Gaps + ", sim time " + r.SimTime.ToString("F1") + " s" + (r.Err != null ? "  ERROR: " + r.Err : ""));
            float first = float.MaxValue; int fin = 0;
            for (int i = 0; i < NewKarts; i++)
            {
                sb.AppendLine("   kart " + i + "  finish " + (float.IsInfinity(r.Finish[i]) ? "DNF" : r.Finish[i].ToString("F1") + " s") + "  slow " + (r.SlowFrac[i] * 100f).ToString("F1") + "%  resets " + r.Resets[i] + "  gapFalls " + r.GapFalls[i]);
                if (!float.IsInfinity(r.Finish[i])) { fin++; first = Mathf.Min(first, r.Finish[i]); }
            }
            Report(tag + "B1 all-finish", r.Err == null && r.AllFinished, fin + "/" + NewKarts + " finished" + (r.AllFinished ? "" : " within " + tSimCap.ToString("F0") + " s"));
            float worst = 0f; int maxReset = 0;
            for (int i = 0; i < NewKarts; i++) { worst = Mathf.Max(worst, r.SlowFrac[i]); maxReset = Mathf.Max(maxReset, r.Resets[i]); }
            Report(tag + "B2 no-stall", r.Err == null && worst <= 0.25f && maxReset <= 3, "worst slow fraction " + (worst * 100f).ToString("F1") + "% (limit 25%), max resets " + maxReset + " (limit 3)");
            int pairs = NewKarts * r.Gaps, falls = 0;
            for (int i = 0; i < NewKarts; i++) falls += r.GapFalls[i];
            int needClean = Mathf.Max(r.Gaps, (pairs + 1) / 2);
            Report(tag + "B3 gap crossing", r.Err == null && r.Gaps > 0 && r.CleanCross >= needClean && r.Crossed >= pairs,
                   "clean crossings " + r.CleanCross + "/" + pairs + " (need >= " + needClean + "), crossed incl. retries " + r.Crossed + "/" + pairs + ", falls " + falls + (r.Glides > 0 ? ", gap glides " + r.Glides + ": " + r.GlideTMin.ToString("0.0") + ".." + r.GlideTMax.ToString("0.0") + " s, landing " + r.LandMin.ToString("0") + ".." + r.LandMax.ToString("0") + " m past the far edge" : ""));
            float ideal = r.Length / 24f;
            Report(tag + "B4 lap time", fin > 0 && first >= ideal * 0.9f && first <= ideal * 3f, "winner " + (fin > 0 ? first.ToString("F1") : "-") + " s (expected " + (ideal * 0.9f).ToString("F0") + ".." + (ideal * 3f).ToString("F0") + ")");
        }

        // ------------------------------------------------------------------ 判定

        void Evaluate(Result a, Result b)
        {
            sb.AppendLine("track length " + a.TrackLength.ToString("F1") + " m, laps 3, karts " + Karts + ", level 2, sim time " + a.SimTime.ToString("F1") + " s");
            if (a.Err != null) sb.AppendLine("run1 error: " + a.Err);
            if (b.Err != null) sb.AppendLine("run2 error: " + b.Err);
            sb.AppendLine("place  kart  finish(s)  slow%  resets  recovers   [run 1]");
            for (int pl = 0; pl < Karts; pl++)
            {
                int id = a.Order[pl];
                sb.AppendLine((pl + 1).ToString().PadLeft(5) + "  " + id.ToString().PadLeft(4) + "  "
                              + (float.IsInfinity(a.Finish[id]) ? "DNF".PadLeft(9) : a.Finish[id].ToString("F2").PadLeft(9))
                              + "  " + (a.SlowFrac[id] * 100f).ToString("F1").PadLeft(5) + "  " + a.Resets[id].ToString().PadLeft(6) + "  " + a.Recovers[id].ToString().PadLeft(8));
            }

            // A1 不卡牆
            {
                float worst = 0f; int worstId = -1, maxReset = 0;
                for (int i = 0; i < Karts; i++)
                {
                    if (a.SlowFrac[i] > worst) { worst = a.SlowFrac[i]; worstId = i; }
                    if (a.Resets[i] > maxReset) maxReset = a.Resets[i];
                }
                Report("A1 no-stall", a.Err == null && worst <= SlowLimit && maxReset <= 2,
                       "worst slow fraction " + (worst * 100f).ToString("F1") + "% (kart " + worstId + ", limit " + (SlowLimit * 100f) + "%), max resets " + maxReset + " (limit 2)");
            }

            // A2 全部完賽
            Report("A2 all-finish", a.Err == null && a.AllFinished, a.AllFinished ? "all " + Karts + " finished" : "not all finished within " + SimCap + " s");

            // A3 名次分布
            {
                bool allFin = true; float first = float.MaxValue, last = 0f;
                for (int i = 0; i < Karts; i++)
                {
                    float t = a.Finish[i];
                    if (float.IsInfinity(t)) { allFin = false; continue; }
                    if (t < first) first = t;
                    if (t > last) last = t;
                }
                bool distinct = true;
                for (int i = 1; i < Karts; i++)
                    if (!float.IsInfinity(a.Finish[a.Order[i]]) && Mathf.Approximately(a.Finish[a.Order[i]], a.Finish[a.Order[i - 1]])) distinct = false;
                float spread = allFin && first > 0f ? (last - first) / first : 999f;
                float ideal = a.TrackLength * 3f / 24f;   // 以極速 24 m/s 估的下限時間
                bool range = allFin && first >= ideal * 0.9f && first <= ideal * 3.0f;
                Report("A3 rank-distribution", allFin && distinct && spread < 0.25f && range,
                       "distinct=" + distinct + ", spread " + (spread * 100f).ToString("F1") + "% (limit 25%), winner " + first.ToString("F1") + " s (expected " + (ideal * 0.9f).ToString("F0") + ".." + (ideal * 3f).ToString("F0") + ")");
            }

            // A4 可重現
            {
                bool sameOrder = true; float maxDiff = 0f;
                for (int i = 0; i < Karts; i++)
                {
                    if (a.Order[i] != b.Order[i]) sameOrder = false;
                    float d = Mathf.Abs(a.Finish[i] - b.Finish[i]);
                    if (float.IsInfinity(a.Finish[i]) != float.IsInfinity(b.Finish[i])) d = 999f;
                    else if (float.IsInfinity(a.Finish[i])) d = 0f;
                    if (d > maxDiff) maxDiff = d;
                }
                sb.AppendLine("run 2 order: " + string.Join(",", Array.ConvertAll(b.Order, x => x.ToString())) + "  (run 1: " + string.Join(",", Array.ConvertAll(a.Order, x => x.ToString())) + ")");
                Report("A4 reproducible", a.Err == null && b.Err == null && sameOrder && maxDiff <= 0.2f,
                       "same order=" + sameOrder + ", max finish-time diff " + maxDiff.ToString("F3") + " s (limit 0.2)");
            }
        }

        void TestRubberband()
        {
            try
            {
                bool ok = true; string why = "";
                Action<bool, string> chk = (c, m) => { if (!c) { ok = false; why += m + "; "; } };
                chk(Mathf.Approximately(Rubberband.Mul(0f, 1, false, false), 1f), "zero delta");
                chk(Mathf.Approximately(Rubberband.Mul(14f, 0, false, false), 1f) && Mathf.Approximately(Rubberband.Mul(-14f, 0, false, false), 1f), "dead zone");
                chk(Rubberband.Mul(200f, 0, false, false) < 1f, "ahead slows");
                chk(Rubberband.Mul(-200f, 0, false, false) > 1f, "behind speeds up");
                chk(Mathf.Approximately(Rubberband.Mul(200f, 0, false, false), 0.90f), "ahead max L0 = 0.90");
                chk(Mathf.Approximately(Rubberband.Mul(-200f, 0, false, false), 1.12f), "behind max L0 = 1.12");
                chk(Mathf.Approximately(Rubberband.Mul(-200f, 0, true, false), 1f), "last 25% off");
                chk(Rubberband.Mul(-200f, 0, false, true) <= 1.06f + 1e-5f, "visible cap 1.06");
                chk(Rubberband.Mul(-200f, 3, false, false) < Rubberband.Mul(-200f, 0, false, false), "strength falls with level");
                Report("A5 rubberband", ok, ok ? "direction, dead zone, last-25% off, visible cap, per-level strength OK" : why);
            }
            catch (Exception e) { Report("A5 rubberband", false, "exception: " + e); }
        }
    }
}
