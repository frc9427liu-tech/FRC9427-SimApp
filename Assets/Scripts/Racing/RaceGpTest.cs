using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數:
    //   -racegptest     → 2 場迷你盃(玩家交給 RaceAI,AI 全自動):積分累計、場間切換、頒獎、盃賽獎牌。結果寫 racegptest.txt
    //   -raceghosttest  → 幽靈:合成軌跡錄製誤差、存讀檔、破紀錄覆蓋、實車錄一圈再重播(教練幽靈自動生成)。結果寫 raceghosttest.txt
    // 全部 PASS 才印 "RESULT: PASS"。測試用 Prefs.Isolated(不寫登錄檔)與暫存幽靈資料夾(不碰使用者存檔)。
    public sealed class RaceGpTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = Environment.GetCommandLineArgs();
            bool gp = Array.IndexOf(a, "-racegptest") >= 0, gh = Array.IndexOf(a, "-raceghosttest") >= 0;
            if (!gp && !gh) return;
            var g = new GameObject("RaceGpTest");
            DontDestroyOnLoad(g);
            var t = g.AddComponent<RaceGpTest>();
            t.runGp = gp; t.runGhost = gh;
        }

        bool runGp, runGhost;
        readonly StringBuilder sb = new StringBuilder();
        int fails;
        const int FrameCap = 120000;   // 50 fps 下約 40 分鐘模擬時間,只是防呆

        void Report(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail);
            if (!ok) fails++;
        }

        static string OutDir() { return Path.GetDirectoryName(Application.dataPath); }

        IEnumerator Start()
        {
            yield return null; yield return null;
            float oldFixed = Time.fixedDeltaTime; int oldCap = Time.captureFramerate;
            Time.fixedDeltaTime = 0.02f; Time.captureFramerate = 50;
            RaceKart.IgnoreMenuBlock = true;
            Prefs.Isolated = true;
            RaceSession.TestFast = true;
            RaceGhost.DirOverride = Path.Combine(Path.GetTempPath(), "frc_ghosttest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            RaceGrandPrix.ExitOnDone = false;

            if (runGp)
            {
                string gpPath = Path.Combine(OutDir(), "racegptest.txt");
                sb.Length = 0; fails = 0;
                yield return RunGp();
                Finish(gpPath);
            }
            if (runGhost)
            {
                string ghPath = Path.Combine(OutDir(), "raceghosttest.txt");
                sb.Length = 0; fails = 0;
                yield return RunGhost();
                Finish(ghPath);
            }

            try { if (Directory.Exists(RaceGhost.DirOverride)) Directory.Delete(RaceGhost.DirOverride, true); } catch (Exception) { }
            RaceKart.IgnoreMenuBlock = false;
            Time.captureFramerate = oldCap; Time.fixedDeltaTime = oldFixed;
            Application.Quit(totalFails == 0 ? 0 : 1);
        }

        int totalFails;
        void Finish(string path)
        {
            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            totalFails += fails;
            try { File.WriteAllText(path, sb.ToString()); } catch (Exception e) { Debug.LogError("[RaceGpTest] write failed: " + e); }
            Debug.Log("[RaceGpTest]\n" + sb);
        }

        // =================================================================== 大獎賽
        IEnumerator RunGp()
        {
            const int cup = 77;
            RaceSession.AutoPilot = true;
            RaceGrandPrix.RacesOverride = new[] { 0, 1 };
            var cfg = new RaceConfig { GrandPrix = true, Cup = cup, Laps = 1, AiCount = 3, CC = 100, AiLevel = 1, Kart = KartStyle.Bullet, ColorIndex = 2 };
            RaceSession.Pending = cfg;
            Prefs.SetInt(RaceGrandPrix.MedalKey(cup, 100), 0);
            RaceSession.Begin(null);
            yield return null;

            bool began = RaceSession.Active && RaceSession.Config != null && RaceGrandPrix.Active;
            Report("G1 begin", began, "active=" + RaceSession.Active + " gp=" + RaceGrandPrix.State);
            if (!began) { Cleanup(); yield break; }
            Report("G2 cc applied", RaceSession.Config.CC == 100 && RaceSession.Config.AiLevel == RaceSession.AiLevelForCc(100) && RaceSession.AiLevelForCc(50) == 0 && RaceSession.AiLevelForCc(150) == 2 && RaceSession.AiLevelForCc(200) == 3,
                "cc=" + RaceSession.Config.CC + " aiLevel=" + RaceSession.Config.AiLevel);
            Report("G3 race 1 track", RaceSession.Config.TrackId == 0 && RaceGrandPrix.RaceIndex == 0 && RaceGrandPrix.RaceCount == 2, "track=" + RaceSession.Config.TrackId + " idx=" + RaceGrandPrix.RaceIndex + "/" + RaceGrandPrix.RaceCount);

            int frames = 0;
            while (RaceGrandPrix.State != GpState.Standings && frames++ < FrameCap) yield return null;
            bool reached = RaceGrandPrix.State == GpState.Standings;
            Report("G4 race 1 ends in standings", reached, "frames=" + frames + " state=" + RaceGrandPrix.State);
            if (!reached) { Cleanup(); yield break; }

            int[] expect1 = SumFirst(4);
            int total1 = 0; bool places1 = true; var seen = new HashSet<int>();
            foreach (var e in RaceGrandPrix.Entries) { total1 += e.Total; if (e.LastPlace < 1 || e.LastPlace > 4 || !seen.Add(e.LastPlace)) places1 = false; if (e.Total != RaceSoft.Points(e.LastPlace)) places1 = false; }
            Report("G5 race 1 points", RaceGrandPrix.Entries.Count == 4 && total1 == expect1[0] && places1, "entries=" + RaceGrandPrix.Entries.Count + " sum=" + total1 + " expect=" + expect1[0] + " [" + RaceGrandPrix.Summary() + "]");
            Report("G6 standings sorted", Sorted(), "ranks ok");
            int firstTrack = RaceSession.Config.TrackId;

            RaceGrandPrix.Continue();   // 玩家按 A
            yield return null;
            var grid = RaceSession.Track != null ? RaceSession.Track.Grid(0).position : Vector3.zero;
            float gd = RaceSession.Player != null ? Vector2.Distance(new Vector2(RaceSession.Player.Position.x, RaceSession.Player.Position.z), new Vector2(grid.x, grid.z)) : 999f;
            bool switched = RaceGrandPrix.State == GpState.Racing && RaceGrandPrix.RaceIndex == 1 && RaceSession.Config.TrackId == 1 && RaceSession.Config.TrackId != firstTrack;
            Report("G7 switch to race 2", switched, "state=" + RaceGrandPrix.State + " idx=" + RaceGrandPrix.RaceIndex + " track=" + RaceSession.Config.TrackId);
            Report("G8 karts reset to grid", RaceSession.Karts.Count == 4 && gd < 3f && (RaceSession.Phase == RacePhase.Countdown || RaceSession.Phase == RacePhase.Racing) && RaceSession.Config.Kart == KartStyle.Bullet && RaceSession.Config.ColorIndex == 2,
                "karts=" + RaceSession.Karts.Count + " gridDist=" + gd.ToString("0.0") + " phase=" + RaceSession.Phase);

            frames = 0;
            while (RaceGrandPrix.State != GpState.Podium && frames++ < FrameCap) yield return null;
            reached = RaceGrandPrix.State == GpState.Podium;
            Report("G9 race 2 ends in podium", reached, "frames=" + frames + " state=" + RaceGrandPrix.State);
            if (!reached) { Cleanup(); yield break; }

            int total2 = 0; bool sums = true; bool order = Sorted();
            foreach (var e in RaceGrandPrix.Entries)
            {
                total2 += e.Total; int s = 0; foreach (var p in e.Points) s += p;
                if (s != e.Total || e.Places.Count != 2) sums = false;
            }
            Report("G10 totals accumulate", RaceGrandPrix.Entries.Count == 4 && total2 == expect1[0] * 2 && sums, "sum=" + total2 + " expect=" + expect1[0] * 2 + " [" + RaceGrandPrix.Summary() + "]");
            Report("G11 final ranking", order && RaceGrandPrix.PlayerRank >= 1 && RaceGrandPrix.PlayerRank <= 4, "playerRank=" + RaceGrandPrix.PlayerRank);
            int expMedal = RaceGrandPrix.PlayerRank == 1 ? 3 : RaceGrandPrix.PlayerRank == 2 ? 2 : RaceGrandPrix.PlayerRank == 3 ? 1 : 0;
            Report("G12 cup medal saved", RaceGrandPrix.Medal == expMedal && Prefs.GetInt(RaceGrandPrix.MedalKey(cup, 100), -1) == expMedal, "medal=" + RaceGrandPrix.Medal + " pref=" + Prefs.GetInt(RaceGrandPrix.MedalKey(cup, 100), -1));

            RaceGrandPrix.Continue();
            Report("G13 podium continue -> done", RaceGrandPrix.State == GpState.Done, "state=" + RaceGrandPrix.State);

            // 起跑加速替身:好起步 / 打滑的機率表存在且合理
            Report("G14 start boost const", RaceStartBoost.WindowHi > RaceStartBoost.WindowLo && RaceStartBoost.BoostSec > 0f, "window " + RaceStartBoost.WindowLo + ".." + RaceStartBoost.WindowHi);
            Cleanup();
        }

        int[] SumFirst(int n) { int s = 0; for (int i = 1; i <= n; i++) s += RaceSoft.Points(i); return new[] { s }; }

        bool Sorted()
        {
            for (int i = 1; i < RaceGrandPrix.Entries.Count; i++)
                if (RaceGrandPrix.Entries[i - 1].Total < RaceGrandPrix.Entries[i].Total || RaceGrandPrix.Entries[i].Rank != i + 1) return false;
            return RaceGrandPrix.Entries.Count > 0 && RaceGrandPrix.Entries[0].Rank == 1;
        }

        void Cleanup()
        {
            try { if (RaceSession.Active) RaceSession.End(null); } catch (Exception e) { Debug.LogWarning("[RaceGpTest] End: " + e.Message); }
            RaceSession.AutoPilot = false; RaceGrandPrix.RacesOverride = null; RaceGrandPrix.Reset();
        }

        // =================================================================== 幽靈
        static Vector3 Truth(float t) { return new Vector3(20f * Mathf.Cos(t), 0.5f * Mathf.Sin(t * 0.5f), 20f * Mathf.Sin(t)); }
        static Quaternion TruthRot(float t) { return Quaternion.Euler(0f, -t * Mathf.Rad2Deg + 90f, 0f); }

        static GhostData Synth(float dur)
        {
            var rec = new GhostRecorder();
            rec.Begin(Truth(0f), TruthRot(0f));
            const float dt = 0.02f;
            int n = Mathf.RoundToInt(dur / dt);
            for (int i = 1; i <= n; i++) rec.Step(dt, Truth(i * dt), TruthRot(i * dt));
            return rec.Finish();
        }

        IEnumerator RunGhost()
        {
            // ---- H1 合成軌跡:錄製誤差
            var d = Synth(12.34f);
            float maxAt = 0f, maxMid = 0f, maxRot = 0f;
            for (int i = 0; i < d.Count; i++)
            {
                d.Sample(d.T[i], out var p, out var r);
                maxAt = Mathf.Max(maxAt, Vector3.Distance(p, Truth(d.T[i])));
            }
            for (float t = 0.003f; t < d.Duration; t += 0.0137f)
            {
                d.Sample(t, out var p, out var r);
                maxMid = Mathf.Max(maxMid, Vector3.Distance(p, Truth(t)));
                maxRot = Mathf.Max(maxRot, Quaternion.Angle(r, TruthRot(t)));
            }
            Report("H1 record error (synthetic circle)", maxAt < 0.05f && maxMid < 0.05f && Mathf.Abs(d.Duration - 12.34f) < 0.021f,
                "samples=" + d.Count + " dur=" + d.Duration.ToString("0.000") + " maxAtSample=" + maxAt.ToString("0.0000") + " maxInterp=" + maxMid.ToString("0.0000") + " maxRotDeg=" + maxRot.ToString("0.00"));
            Report("H2 sample spacing 0.1 s", Mathf.Abs(d.T[1] - 0.1f) < 1e-4f && Mathf.Abs(d.T[10] - 1.0f) < 1e-3f && d.Count >= 124, "t1=" + d.T[1] + " t10=" + d.T[10] + " n=" + d.Count);

            // ---- H3 存讀檔
            d.Track = 3; d.Cc = 100; d.Mirror = true;
            string path = GhostStore.FileFor("me", 3, true, false, 100);
            bool saved = GhostStore.Save(path, d);
            var l = GhostStore.Load(path);
            float maxRt = 0f;
            if (l != null) for (int i = 0; i < Mathf.Min(d.Count, l.Count); i++) maxRt = Mathf.Max(maxRt, Vector3.Distance(d.Pos[i], l.Pos[i]));
            Report("H3 save/load roundtrip", saved && l != null && l.Count == d.Count && l.Track == 3 && l.Cc == 100 && l.Mirror && Mathf.Abs(l.Duration - d.Duration) < 1e-5f && maxRt < 1e-4f,
                "saved=" + saved + " n=" + (l != null ? l.Count : -1) + " maxDiff=" + maxRt);

            // ---- H4 破紀錄覆蓋
            var slow = Synth(14f); var fast = Synth(11f);
            bool slowWrote = GhostStore.SaveIfBetter(path, slow);
            float afterSlow = GhostStore.Load(path).Duration;
            bool fastWrote = GhostStore.SaveIfBetter(path, fast);
            float afterFast = GhostStore.Load(path).Duration;
            Report("H4 overwrite only when faster", !slowWrote && Mathf.Abs(afterSlow - d.Duration) < 1e-4f && fastWrote && Mathf.Abs(afterFast - fast.Duration) < 1e-4f,
                "slowWrote=" + slowWrote + " fastWrote=" + fastWrote + " dur " + d.Duration.ToString("0.00") + " -> " + afterFast.ToString("0.00"));

            // ---- H5~ 實車:計時賽(沒有教練檔 → 自動生成),錄第 1 圈、第 2 圈重播並可能破紀錄
            try { Directory.Delete(RaceGhost.DirOverride, true); } catch (Exception) { }
            RaceSession.AutoPilot = true;
            RaceSession.Pending = new RaceConfig { TimeTrial = true, Laps = 2, CC = 100, TrackId = 0, Kart = KartStyle.Brick, AiCount = 5 /* 應被忽略 */ };
            var trace = new List<KeyValuePair<float, Vector3>>();
            bool lap1Trace = true;
            RaceGhost.TraceHook = (t, p) => { if (lap1Trace) trace.Add(new KeyValuePair<float, Vector3>(t, p)); };
            RaceSession.Begin(null);
            yield return null;
            bool ok = RaceSession.Active && RaceSession.Config != null && RaceSession.Config.TimeTrial;
            Report("H5 time trial begin", ok && RaceSession.Config.AiCount == 0 && !RaceSession.Config.Items, "aiCount=" + (RaceSession.Config != null ? RaceSession.Config.AiCount : -1) + " items=" + (RaceSession.Config != null && RaceSession.Config.Items));
            if (!ok) { RaceGhost.TraceHook = null; Cleanup(); yield break; }
            bool coachRecording = RaceSession.Phase == RacePhase.Intro;
            string coachPath = RaceGhost.CoachPath;

            int frames = 0;
            while (RaceSession.Phase != RacePhase.Racing && frames++ < FrameCap) yield return null;
            Report("H6 coach ghost generated", coachRecording && RaceSession.Phase == RacePhase.Racing && GhostStore.Exists(coachPath) && RaceGhost.Coach != null && RaceSession.Karts.Count == 1,
                "wasIntro=" + coachRecording + " phase=" + RaceSession.Phase + " file=" + GhostStore.Exists(coachPath) + " coachDur=" + (RaceGhost.Coach != null ? RaceGhost.Coach.Duration.ToString("0.0") : "-") + " karts=" + RaceSession.Karts.Count + " frames=" + frames);

            // 第 1 圈:等到玩家 LapTimes 有 1 筆
            frames = 0;
            while (RaceSession.LapTimes.Count < 1 && RaceSession.Phase == RacePhase.Racing && frames++ < FrameCap) yield return null;
            lap1Trace = false;
            var first = RaceGhost.Me;
            bool haveMe = first != null && GhostStore.Exists(RaceGhost.MePath);
            Report("H7 lap 1 recorded + saved", haveMe, "lapTimes=" + RaceSession.LapTimes.Count + " meDur=" + (first != null ? first.Duration.ToString("0.00") : "-") + " frames=" + frames);
            float firstDur = first != null ? first.Duration : 0f;

            // 錄製 vs 原始軌跡:在 0.1 s 取樣點上比
            float maxRec = 0f; int cmp = 0;
            if (first != null)
                foreach (var kv in trace)
                {
                    float k = kv.Key / GhostData.Dt;
                    if (Mathf.Abs(k - Mathf.Round(k)) > 0.02f || kv.Key > first.Duration) continue;
                    first.Sample(kv.Key, out var p, out var r);
                    maxRec = Mathf.Max(maxRec, Vector3.Distance(p, kv.Value)); cmp++;
                }
            Report("H8 recorded vs raw trace < 0.05 m", first != null && cmp > 20 && maxRec < 0.05f, "compared=" + cmp + " maxErr=" + maxRec.ToString("0.0000"));

            // 第 2 圈:幽靈車位置 vs 錄製資料
            float maxPlay = 0f; int playN = 0; bool shown = false;
            frames = 0;
            while (RaceSession.Phase == RacePhase.Racing && frames++ < FrameCap)
            {
                yield return null;
                var car = RaceGhost.MeCar; var data = RaceGhost.Me;
                if (car != null && car.Go != null && data != null && car.Go.activeSelf && RaceGhost.ReplayClock > 0.3f)
                {
                    shown = true;
                    data.Sample(RaceGhost.ReplayClock, out var p, out var r);
                    maxPlay = Mathf.Max(maxPlay, Vector3.Distance(car.Go.transform.position, p)); playN++;
                }
            }
            Report("H9 replay ghost follows data < 0.05 m", shown && playN > 50 && maxPlay < 0.05f, "frames=" + playN + " maxErr=" + maxPlay.ToString("0.0000"));
            Report("H10 ghost has no collider", !HasCollider(RaceGhost.MeCar) && !HasCollider(RaceGhost.CoachCar), "ok");

            bool finished = RaceSession.Phase == RacePhase.Finished;
            var fileMe = GhostStore.Load(RaceGhost.MePath);
            bool consistent = fileMe != null && RaceGhost.Me != null && Mathf.Abs(fileMe.Duration - RaceGhost.Me.Duration) < 1e-4f && RaceGhost.Me.Duration <= firstDur + 1e-4f;
            Report("H11 best-lap ghost overwrite", finished && consistent,
                "finished=" + finished + " lap1=" + firstDur.ToString("0.00") + " best=" + (RaceGhost.Me != null ? RaceGhost.Me.Duration.ToString("0.00") : "-") + " newBest=" + RaceGhost.NewBest + " file=" + (fileMe != null ? fileMe.Duration.ToString("0.00") : "-"));

            RaceGhost.TraceHook = null;
            Cleanup();
            RaceSession.AutoPilot = false;
        }

        static bool HasCollider(GhostCar c) { return c != null && c.Go != null && c.Go.GetComponentInChildren<Collider>(true) != null; }
    }
}
