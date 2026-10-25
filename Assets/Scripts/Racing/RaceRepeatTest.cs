using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數:-racerepeattest [-racerepeatsecs N]
    // 「第二場 AI 亂跑」回歸測試:同一個行程裡連跑多場,每場 AI 的行為都要和第一場(乾淨狀態)一致。
    //   R1 第一場(基準):賽道 0,3 台 AI。
    //   R2 End → Begin 同一條賽道。          R3 「再來一場」Restart(從結果畫面重來,Root 整個重建)。
    //   R4 End → Begin 另一條賽道(id 1)。    R5 賽道 0 鏡像 + 反向。   R6 回到賽道 0 原版(確認變體沒有改到共用的 TrackDef)。
    // 每場量:倒數時 AI 在起跑格且不動;跑 N 秒(預設 30 秒模擬時間)期間每 10 秒的前進距離、離開路面的比例、卡住倒車次數、瞬移次數、進度單調遞增。
    // R2 / R3 / R6 還要和 R1 比 N 秒的總前進距離(同種子同賽道 → 應該幾乎一樣)。結果寫 racerepeattest.txt,exit code = 失敗數。
    public sealed class RaceRepeatTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-racerepeattest") < 0) return;
            var g = new GameObject("RaceRepeatTest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceRepeatTest>();
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;
        const int FrameCap = 6000;          // 單場等待上限(50 fps 下 120 秒),只是防呆
        float runSecs = 30f;

        // 門檻(寬鬆:抓的是「亂跑」這種數量級的錯,不是物理的小差異)
        const float MinPer10s = 80f;        // 任何 10 秒窗內 AI 至少前進這麼多公尺
        const float OffTrackMax = 0.15f;    // 離開路面(|橫向偏移| > 半寬 + 2 m)的幀比例上限
        const int RecoverMax = 2;           // 倒車脫困次數上限
        const float TeleportJump = 15f;     // 單幀位移超過這個算瞬移(重生)
        const float DistTol = 0.15f;        // 和基準場比總前進距離的相對誤差

        sealed class KartStat
        {
            public int Id; public string Name;
            public float StartTotal, LastTotal, MinWindow = float.MaxValue, WindowBase, SampleBase;
            public int Frames, OffFrames, Teleports; public bool Backwards;
            public Vector3 LastPos; public bool HaveLast;
            public float StartGridDist, StartSpeed;
            public int Recovers;
            public float Dist { get { return LastTotal - StartTotal; } }
        }
        sealed class RunStat { public string Name; public float TrackLen; public int Nodes; public List<KartStat> Karts = new List<KartStat>(); public bool Reached; }

        void R(string name, bool ok, string detail) { sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail); if (!ok) fails++; }

        IEnumerator Start()
        {
            yield return null; yield return null;
            { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-racerepeatsecs"); if (i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], out float v)) runSecs = Mathf.Clamp(v, 10f, 120f); }
            float oldFixed = Time.fixedDeltaTime; int oldCap = Time.captureFramerate;
            Time.fixedDeltaTime = 0.02f; Time.captureFramerate = 50;
            RaceKart.IgnoreMenuBlock = true;
            Prefs.Isolated = true;
            RaceSession.TestFast = true;
            RaceSession.AutoPilot = true;
            RaceGhost.DirOverride = Path.Combine(Path.GetTempPath(), "frc_repeattest_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            RunStat r1 = null, r2 = null, r3 = null, r4 = null, r5 = null, r6 = null;
            bool ok = true;
            try
            {
                // R1
                yield return Run("R1 first race (baseline)", Cfg(0, false, false), false, s => r1 = s);
                // R2:End → Begin 同賽道
                RaceSession.End(null);
                yield return null;
                yield return Run("R2 End->Begin same track", Cfg(0, false, false), false, s => r2 = s);
                // R3:Restart(不 End)
                yield return Run("R3 Restart (rematch)", null, true, s => r3 = s);
                RaceSession.End(null);
                yield return null;
                // R4:不同賽道
                yield return Run("R4 different track (id 1)", Cfg(1, false, false), false, s => r4 = s);
                RaceSession.End(null);
                yield return null;
                // R5:鏡像 + 反向
                yield return Run("R5 track 0 mirror+reverse", Cfg(0, true, true), false, s => r5 = s);
                RaceSession.End(null);
                yield return null;
                // R6:原版
                yield return Run("R6 track 0 plain again", Cfg(0, false, false), false, s => r6 = s);
                RaceSession.End(null);
                yield return null;
                // R7:比賽金幣走真實 RaceSession.FixedStep -> RaceWorld.StepPickups 路徑(賽道 3,玩家交給 AutoPilot 跑中心線)
                yield return CoinRun();
            }
            finally { }

            // ---- 和基準比
            if (r1 != null)
            {
                Compare("C2 R2 vs R1", r1, r2);
                Compare("C3 R3 vs R1", r1, r3);
                Compare("C6 R6 vs R1", r1, r6);
                if (r6 != null) R("C7 shared TrackDef not mutated by variants", Mathf.Abs(r6.TrackLen - r1.TrackLen) < 0.5f && r6.Nodes == r1.Nodes, "len " + r1.TrackLen.ToString("0.0") + " -> " + r6.TrackLen.ToString("0.0") + " nodes " + r1.Nodes + " -> " + r6.Nodes);
            }
            try { if (RaceSession.Active) RaceSession.End(null); } catch (Exception e) { Debug.LogWarning("[RaceRepeatTest] End: " + e.Message); }
            RaceSession.AutoPilot = false;
            try { if (Directory.Exists(RaceGhost.DirOverride)) Directory.Delete(RaceGhost.DirOverride, true); } catch (Exception) { }
            RaceKart.IgnoreMenuBlock = false;
            Time.captureFramerate = oldCap; Time.fixedDeltaTime = oldFixed;

            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "racerepeattest.txt"), sb.ToString()); } catch (Exception e) { Debug.LogError("[RaceRepeatTest] write failed: " + e); }
            Debug.Log("[RaceRepeatTest]\n" + sb);
            Application.Quit(fails == 0 && ok ? 0 : 1);
        }

        // R7:賽道 3、沒有 AI、沒有道具箱,玩家 AutoPilot 開 secs 秒:要撿到金幣(事件 + 車上金幣數),且最早撿的那枚要在 5 秒後重生(重生用 Time.time,不能卡住)。
        IEnumerator CoinRun()
        {
            const string name = "R7 coins via RaceSession.FixedStep (track 3, AutoPilot)";
            RaceSession.Pending = new RaceConfig { TrackId = 3, Laps = 3, AiCount = 0, CC = 100, AiLevel = 2, Kart = KartStyle.Brick, ColorIndex = 1, Items = false };
            RaceSession.Begin(null);
            yield return null;
            if (!RaceSession.Active || RaceSession.Player == null) { R(name + " begin", false, "active=" + RaceSession.Active); yield break; }
            int frames = 0;
            while (RaceSession.Phase != RacePhase.Racing && frames++ < FrameCap) yield return null;
            if (RaceSession.Phase != RacePhase.Racing) { R(name + " reaches Racing", false, "phase=" + RaceSession.Phase); yield break; }

            var kart = RaceSession.Player;
            int pid = kart.KartId, picks = 0, gained = 0, firstIdx = -1, total = RaceWorld.RaceCoins.Count;
            float firstAt = -1f, tLast = 0f;
            Action<int, int, Vector3> onPick = (kid, idx, pos) => { if (kid == pid) { picks++; if (firstIdx < 0) { firstIdx = idx; firstAt = Time.time; } } };
            Action<RaceKart, int, int> onCoins = (kk, n, d) => { if (kk == kart && d > 0) gained++; };
            RaceWorld.RaceCoinPicked += onPick; RaceKart.CoinsChanged += onCoins;
            float t = 0f; int maxCoins = 0;
            frames = 0;
            while (t < 40f && frames++ < FrameCap && RaceSession.Phase == RacePhase.Racing)
            {
                yield return null;
                t += Time.fixedDeltaTime;
                maxCoins = Mathf.Max(maxCoins, kart.Coins);
                tLast = Time.time;
            }
            RaceWorld.RaceCoinPicked -= onPick; RaceKart.CoinsChanged -= onCoins;
            bool respawned = firstIdx >= 0 && tLast - firstAt > 6f && firstIdx < RaceWorld.RaceCoins.Count && !RaceWorld.RaceCoins[firstIdx].Taken;
            bool reCollected = firstIdx >= 0 && RaceWorld.RaceCoins[firstIdx].Taken && RaceWorld.RaceCoins[firstIdx].RespawnAt > tLast - 0.05f;   // 又被撿走了(圈數夠多時)也算重生過
            R(name + " kart picks up coins", picks >= 3 && gained >= 1 && maxCoins >= 1, "raceCoins=" + total + " picks=" + picks + " gainedEvents=" + gained + " maxCoins=" + maxCoins + " t=" + t.ToString("0.0"));
            R(name + " first coin respawns after 5 s (Time.time clock)", respawned || reCollected, "firstIdx=" + firstIdx + " ageSincePick=" + (tLast - firstAt).ToString("0.0") + " respawned=" + respawned + " reCollected=" + reCollected);
        }

        static RaceConfig Cfg(int track, bool mirror, bool reverse)
        {
            return new RaceConfig { TrackId = track, Laps = 3, AiCount = 3, CC = 100, AiLevel = 1, Kart = KartStyle.Brick, ColorIndex = 1, Mirror = mirror, Reverse = reverse, Items = true };
        }

        // 跑一場:cfg != null → Begin(Pending = cfg);restart == true → 對正在進行的場次呼叫 Restart(先讓上一場跑了一陣子,AI 離開起跑格)。
        IEnumerator Run(string name, RaceConfig cfg, bool restart, Action<RunStat> done)
        {
            var rs = new RunStat { Name = name };
            if (restart)
            {
                RaceSession.Restart();
            }
            else
            {
                RaceSession.Pending = cfg;
                RaceSession.Begin(null);
            }
            yield return null;
            if (!RaceSession.Active || RaceSession.Track == null) { R(name + " begin", false, "active=" + RaceSession.Active); done(rs); yield break; }
            rs.TrackLen = RaceSession.Track.Length; rs.Nodes = RaceSession.Track.CheckpointCount;

            int frames = 0;
            while (RaceSession.Phase != RacePhase.Countdown && RaceSession.Phase != RacePhase.Racing && frames++ < FrameCap) yield return null;

            // 倒數期:AI 應該在起跑格、沒在動
            var ais = new List<RaceKart>();
            foreach (var k in RaceSession.Karts) if (k != null && !k.IsPlayer && k.KartId >= 1) ais.Add(k);
            var stats = new List<KartStat>();
            for (int i = 0; i < ais.Count; i++)
            {
                var k = ais[i];
                Vector3 g = RaceSession.Track.Grid(k.KartId).position, p = k.Position;
                var st = new KartStat { Id = k.KartId, Name = k.DisplayName, StartGridDist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(g.x, g.z)), StartSpeed = Mathf.Abs(k.Speed) };
                stats.Add(st);
            }
            bool atGrid = ais.Count == 3;
            float maxGrid = 0f, maxSpd = 0f;
            foreach (var st in stats) { if (st.StartGridDist > 3f || st.StartSpeed > 1.5f) atGrid = false; maxGrid = Mathf.Max(maxGrid, st.StartGridDist); maxSpd = Mathf.Max(maxSpd, st.StartSpeed); }
            R(name + " AI at grid on start", atGrid, "ai=" + ais.Count + " maxGridDist=" + maxGrid.ToString("0.0") + " maxSpeed=" + maxSpd.ToString("0.0") + " phase=" + RaceSession.Phase);

            frames = 0;
            while (RaceSession.Phase != RacePhase.Racing && frames++ < FrameCap) yield return null;
            if (RaceSession.Phase != RacePhase.Racing) { R(name + " reaches Racing", false, "phase=" + RaceSession.Phase); done(rs); yield break; }
            yield return null;

            // 起跑點
            for (int i = 0; i < ais.Count; i++)
            {
                var st = stats[i]; var k = ais[i];
                st.StartTotal = k.Progress != null ? k.Progress.Total : 0f; st.LastTotal = st.StartTotal; st.WindowBase = st.StartTotal; st.SampleBase = st.StartTotal;
                st.LastPos = k.Position; st.HaveLast = true;
            }
            float t = 0f, nextWin = 10f, nextSample = 2f;
            bool mono = true; string monoWhy = "";
            frames = 0;
            while (t < runSecs && frames++ < FrameCap && RaceSession.Phase == RacePhase.Racing)
            {
                yield return null;
                t += Time.fixedDeltaTime;
                for (int i = 0; i < ais.Count; i++)
                {
                    var k = ais[i]; var st = stats[i];
                    if (k == null || k.Progress == null) continue;
                    st.Frames++;
                    st.LastTotal = k.Progress.Total;
                    Vector3 p = k.Position;
                    if (st.HaveLast && (p - st.LastPos).magnitude > TeleportJump) st.Teleports++;
                    st.LastPos = p;
                    float lat; float s = RaceSession.Track.Project(p, -1f, out lat);
                    if (Mathf.Abs(lat) > RaceSession.Track.HalfWidthAt(s) + 2f) st.OffFrames++;
                    var ai = k.GetComponent<RaceAI>(); if (ai != null) st.Recovers = ai.RecoverCount;
                }
                if (t >= nextSample)
                {
                    nextSample += 2f;
                    foreach (var st in stats) { if (st.LastTotal <= st.SampleBase + 0.01f) { mono = false; monoWhy = st.Name + " no progress in 2s at t=" + t.ToString("0.0"); } st.SampleBase = st.LastTotal; }
                }
                if (t >= nextWin)
                {
                    nextWin += 10f;
                    foreach (var st in stats) { st.MinWindow = Mathf.Min(st.MinWindow, st.LastTotal - st.WindowBase); st.WindowBase = st.LastTotal; }
                }
            }
            rs.Reached = t >= runSecs - 0.1f;
            foreach (var st in stats) { if (st.MinWindow == float.MaxValue) st.MinWindow = st.LastTotal - st.WindowBase; rs.Karts.Add(st); }

            R(name + " ran full duration", rs.Reached, "t=" + t.ToString("0.0") + "/" + runSecs + " phase=" + RaceSession.Phase);
            bool prog = true, off = true, rec = true, tele = true; var d = new StringBuilder();
            foreach (var st in stats)
            {
                float offR = st.Frames > 0 ? (float)st.OffFrames / st.Frames : 1f;
                if (st.MinWindow < MinPer10s * Mathf.Min(1f, runSecs / 10f)) prog = false;
                if (offR > OffTrackMax) off = false;
                if (st.Recovers > RecoverMax) rec = false;
                if (st.Teleports > 0) tele = false;
                d.Append(st.Name + "[dist=" + st.Dist.ToString("0") + " min10=" + st.MinWindow.ToString("0") + " off=" + (offR * 100f).ToString("0.0") + "% rec=" + st.Recovers + " tp=" + st.Teleports + "] ");
            }
            R(name + " AI forward progress", prog, d.ToString());
            R(name + " AI progress strictly increasing", mono, mono ? "ok" : monoWhy);
            R(name + " AI stays on track", off, "limit " + (OffTrackMax * 100f) + "%");
            R(name + " AI no stuck-recover spam", rec, "limit " + RecoverMax);
            R(name + " AI no teleport/reset", tele, "jump>" + TeleportJump + " m");
            done(rs);
        }

        void Compare(string name, RunStat a, RunStat b)
        {
            if (b == null || b.Karts.Count != a.Karts.Count || a.Karts.Count == 0) { R(name + " comparable", false, "missing data"); return; }
            bool ok = true; var d = new StringBuilder();
            for (int i = 0; i < a.Karts.Count; i++)
            {
                float da = a.Karts[i].Dist, db = b.Karts[i].Dist;
                float rel = Mathf.Abs(db - da) / Mathf.Max(1f, da);
                if (rel > DistTol) ok = false;
                d.Append(a.Karts[i].Name + " " + da.ToString("0") + "->" + db.ToString("0") + " (" + (rel * 100f).ToString("0.0") + "%) ");
            }
            R(name + " distance within " + (DistTol * 100f) + "%", ok, d.ToString());
        }
    }
}
