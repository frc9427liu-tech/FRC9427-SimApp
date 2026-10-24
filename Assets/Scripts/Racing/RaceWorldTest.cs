using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace FrcSim
{
    // 啟動參數 -raceworldtest:驗證模組 W(02 §6 測試 1~10;M5:3 條賽道逐條跑,加上捷徑 / 自由探索 / 地標)。
    //   FrcSim.exe -batchmode -nographics -raceworldtest        → 結果寫 raceworldtest.txt(專案/exe 同層),每項 PASS/FAIL,全 PASS 才印 "RESULT: PASS"
    // 自己檢查命令列旗標(不改 SimBootstrap);等 2 幀讓場景穩定後開始。
    //   T1 確定性:同 seed 建兩次,賽道/高度圖/地面材質/全部網格雜湊相同;不同 seed 的高度圖不同
    //   T2 查詢和碰撞一致:1000 個隨機陸地點,HeightAt 和對地形 MeshCollider 的 Raycast 高度差 < 0.05 m;查詢函式不配置記憶體
    //   T3 路面品質:路長、最小半徑、不自交、路面貼地(HeightAt vs 取樣高度)、坡度、路面上 SurfaceAt==Road、與地面碰撞體一致
    //   T4 圈數:幽靈車跑完 Laps 圈剛好觸發 Finished 一次;倒著開不算圈;傳送跳過檢查點不算圈
    //   T5 捷徑(只有 Island GP):幽靈車走捷徑算一圈、Total 不倒退、比主線快;不穿入口門(傳送進捷徑)拿不到被跳過的門;捷徑路面 Dirt / 加速板、高度貼地、不碰主線
    //   T11 自由探索檢查點:不算圈、不完賽;掉進海 / 出界回到最近的安全位置
    //   T12 收集物:30 個、在陸地上、彼此相隔、撿取判定、非自由探索沒有收集物
    //   T13 地標:數量、在陸地、不擋路、碰撞器數量
    //   T14 造景(TrackProps):足跡離路面邊緣 >= 6.5 m(測試容許 6.4)、不在跳台 / 加速板、沒有碰撞體、氣球拱門在路面上方 >= 6 m、箭頭牌都在彎外側、貼花不超出路面、造景三角形 <= 3 萬、draw call <= 30
    //   T6 重置:y=-10、超出邊界、水面(海)、卡住、手動(含冷卻)、自由駕駛,都回到正確的重置點,而且重置點在路面上
    //   T7 名次:8 台幽靈車錯開出發、速度不同,Standings 依 Total 排序;一台完賽其他還在跑時排序也對;最後依完賽時間
    //   T8 世界:島邊緣是海、樹的擺放規則(離路 >= 6 m、不在彎道內側、不在海灘)、山脈/雲、LOD 切換、低品質
    //   T9 長時間:8 台幽靈車加橫向偏移跑 300 秒模擬時間,無 NaN、無卡住、無重置,每步 GC 配置 = 0
    //   T10 跳台 / 加速板:3 個跳台高度連續、碰撞體掛 Ramp、落點平路;6 塊加速板 SurfaceAt == BoostPad、無碰撞體;檢查點門 / 重置點 / 起跑格不被擋
    //   BUDGET 預算:一幀最壞三角形 < 15 萬、draw call < 200、碰撞器 < 150、不同材質 <= 32、建圖時間 < 3 秒、記憶體(每條賽道各量一次)
    //   瑪利歐賽車式擴充(W):每條賽道跑 4 種組合(原版 / 鏡像 / 反向 / 鏡像+反向),原版跑全套,其他三種跑 T1~T7、T10、T13、T14、BUDGET 加下面幾項
    //   TR 軌道滑行(模組 K):Meadow Run / Magma Rush 各 1 條(原版與鏡像 + 反向),其他賽道 0 條;沒有碰撞體、三角形 <= 1500、離地 1.5~3 m、沿行進方向、在門寬內、不在缺口 / 捷徑上(原版另驗不壓跳台)、護欄碰撞體有 RideableWall、資料層 Validate / JSON / Clone
    //   TG 滑翔跳台:數量(Island 1 / Canyon 2 / Coast 1;反向可以更少)、尺寸 10 x 14 m 坡 22 度、高度連續、前緣觸發(StepPickups.Glide)、落點 50~90 m 是路、前方 95 m 沒有別的跳台
    //   TC 金幣:每圈 44~58 枚、在路上(弧線金幣在跳台飛行弧線上)、撿取 / 5 秒重生 / 事件、StepPickups 不配置記憶體
    //   TV 變體幾何:鏡像 = 原版 x 取負(中心線 / 檢查點 / 起跑格 / 地形高度圖),反向 = 起點同位置方向相反、起跑格在線的另一側、捷徑入口出口互換
    //   TU 盃賽:6 盃 x 4 場、賽道編號有效、得分表 {15,12,10,9,8,7,6,5}、總分 / 名次 / 同分比較
    //   M7(新賽道 3..7,TrackGen 產生,每條 1~2 個飛越缺口;新賽道只跑「原版」與「鏡像 + 反向」兩種組合,舊賽道照舊 4 種):
    //   TGEN 產生器:同參數同結果、長度 2.5~4 km、封閉不自交(折線線段不相交 + 自我間距)、最小半徑、缺口數 / 長度 25~60 m、theme 色票、地標 / 加速板 / 跳台數量
    //   TGAP 缺口:深坑是水面(Surface.Water)、沒有擋路的碰撞體、路面網格在缺口內被切掉而前後還在、缺口中央恰好 1 個門(重置點在跳台前的路面)、幽靈車飛過去不重置、
    //        在深坑上空低於水面附近 = 掉下去(重置到缺口前)、從旁邊繞過去拿不到缺口的門、滑翔距離(以 RaceKart 的滑翔常數積分)飛得過去
    public class RaceWorldTest : MonoBehaviour
    {
        const float Dt = 0.02f, GhostSpeed = 25f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-raceworldtest") < 0) return;
            var g = new GameObject("RaceWorldTest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceWorldTest>();
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;
        RaceTrack track;
        RaceConfig cfg;
        string tag = "";
        int tid;
        long buildMs, nativeDelta, monoDelta;

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

        IEnumerator Start()
        {
            yield return null;
            yield return null;

            int nTracks = RaceWorld.TrackCount;
            var names = new StringBuilder();
            for (int i = 0; i < nTracks; i++) names.Append(i == 0 ? "" : ", ").Append(DefaultTracks.Get(i).name);
            Report("TRACKS", nTracks == 9, "TrackCount=" + nTracks + " [" + names + "]");

            // 舊賽道(0..2)每條 4 種組合:原版(全套)/ 鏡像 / 反向 / 鏡像 + 反向(子集);
            // 新賽道(3..7,TrackGen)只跑原版(全套 + TGEN / TGAP)和「鏡像 + 反向」(同樣的子集 + TGAP),省時間(鏡像 / 反向的程式路徑各只差一行,兩個疊起來最容易出錯)
            for (int id = 0; id < nTracks; id++)
                for (int v = 0; v < 4; v++)
                {
                    if (id >= DefaultTracks.Handmade && v != 0 && v != 3) continue;
                    yield return StartCoroutine(Combo(id, (v & 1) != 0, (v & 2) != 0));
                }

            tag = "[cups] ";
            Run("TU cups", TU);

            // 自由探索:世界與正常比賽相同,另外多 30 個收集物
            tag = "[freeroam] ";
            cfg = new RaceConfig { TrackId = 0, Laps = 3, Seed = 9427, FreeRoam = true };
            RaceWorld.Build(cfg, null, RaceQuality.High);
            track = RaceWorld.Track;
            yield return new WaitForFixedUpdate();
            Run("T11 freeroam checkpoints", T11);
            Run("T12 collectibles", T12);
            Run("T12b freeroam budget", Budget12);

            tag = "";
            Run("cleanup", () => RaceWorld.Destroy());
            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "raceworldtest.txt"), sb.ToString()); }
            catch (Exception e) { Debug.LogError("[RaceWorldTest] write failed: " + e); }
            Debug.Log("[RaceWorldTest]\n" + sb);
            Application.Quit(fails == 0 ? 0 : 1);
        }

        IEnumerator Combo(int id, bool mirror, bool reverse)
        {
            bool full = !mirror && !reverse;
            tid = id;
            string vname = full ? "" : (mirror && reverse ? "mirror+reverse " : mirror ? "mirror " : "reverse ");
            tag = "[" + id + " " + DefaultTracks.NameOf(id, true) + (full ? "" : " " + vname.Trim()) + "] ";
            cfg = new RaceConfig { TrackId = id, Laps = 3, Seed = 9427, Mirror = mirror, Reverse = reverse };

            Run("T1 determinism", T1);
            yield return new WaitForFixedUpdate();   // 讓新建的碰撞體進入物理世界,T2/T3 的 raycast 才打得到
            Run("T2 query vs collider", T2);
            Run("T3 road quality", T3);
            Run("T10 ramps+boost pads", T10);
            Run("TG glider ramps", TG);
            if (id >= DefaultTracks.Handmade)
            {
                if (full) Run("TGEN generator", TGEN);
                Run("TGAP gaps", TGAP);
                Run("TELE elevation+bank+corridor", TELE);
            }
            Run("TR rails", TR);
            Run("TC coins", TC);
            if (!full) Run("TV variant geometry", TV);
            Run("T4 laps", T4);
            if (id == 0) Run("T5 shortcut", T5);
            else sb.AppendLine("SKIP " + tag + "T5-shortcut - this track has no shortcut");
            Run("T6 reset", T6);
            Run("T7 standings", T7);
            if (full) Run("T8 world", T8);
            Run("T13 landmarks", T13);
            Run("T14 decor", T14);
            if (full) Run("T9 soak", T9);

            // 預算:先把世界清乾淨再量建圖時間與記憶體(Object.Destroy 要到幀尾才生效)
            RaceWorld.Destroy();
            yield return null; yield return null;
            GC.Collect();
            long nat0 = Profiler.GetTotalAllocatedMemoryLong(), mono0 = GC.GetTotalMemory(true);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            RaceWorld.Build(cfg, null, RaceQuality.High);
            buildMs = sw.ElapsedMilliseconds;
            nativeDelta = Profiler.GetTotalAllocatedMemoryLong() - nat0;
            monoDelta = GC.GetTotalMemory(false) - mono0;
            track = RaceWorld.Track;
            yield return new WaitForFixedUpdate();
            Run("BUDGET", Budget);
        }

        // ------------------------------------------------------------------ 工具

        static ulong H(ulong h, int v) { unchecked { h ^= (uint)v; h *= 1099511628211UL; } return h; }
        static ulong HF(ulong h, float f) { return H(h, Mathf.RoundToInt(f * 1000f)); }

        static ulong WorldHash()
        {
            ulong h = 14695981039346656037UL;
            RaceTrack tr = RaceWorld.Track;
            for (int i = 0; i < tr.Samples.Count; i++)
            {
                TrackSample s = tr.Samples[i];
                h = HF(h, s.Pos.x); h = HF(h, s.Pos.y); h = HF(h, s.Pos.z); h = HF(h, s.Width); h = HF(h, s.Curvature * 1000f);
            }
            for (int i = 0; i < tr.Checkpoints.Count; i++) { h = HF(h, tr.Checkpoints[i].S); h = HF(h, tr.Checkpoints[i].Center.x); h = HF(h, tr.Checkpoints[i].Center.z); }
            foreach (var mf in RaceWorld.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                h = H(h, m.vertexCount); h = H(h, (int)m.GetIndexCount(0));
                Vector3[] vs = m.vertices;
                for (int k = 0; k < vs.Length; k++) { h = HF(h, vs[k].x); h = HF(h, vs[k].y); h = HF(h, vs[k].z); }
            }
            for (int z = -432; z <= 432; z += 8)
                for (int x = -432; x <= 432; x += 8) h = H(h, (int)RaceWorld.SurfaceAt(x, z));
            return h;
        }

        sealed class Ghost : IRacerBody
        {
            public int Id;
            public Vector3 Pos, Vel;
            public Vector3 Fwd = Vector3.forward;
            public bool Throttle = true;
            public int Respawns;
            public Pose LastPose;
            public int RacerId { get { return Id; } }
            public Vector3 Position { get { return Pos; } }
            public Vector3 Velocity { get { return Vel; } }
            public Vector3 Forward { get { return Fwd; } }
            public bool ThrottleHeld { get { return Throttle; } }
            public RacerProgress Progress { get; set; }
            public void Respawn(Pose p) { Pos = p.position; Vel = Vector3.zero; Respawns++; LastPose = p; }
        }

        static void Place(Ghost g, RaceTrack tr, float s, int dir)
        {
            TrackSample sp = tr.SampleAt(s);
            g.Pos = sp.Pos;
            g.Fwd = dir > 0 ? sp.Tangent : -sp.Tangent;
            g.Vel = g.Fwd * GhostSpeed;
        }

        // ------------------------------------------------------------------ T1

        void T1()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            RaceWorld.Build(cfg, null, RaceQuality.High);
            long ms1 = sw.ElapsedMilliseconds;
            ulong a = WorldHash();
            ulong ta = RaceWorld.Terrain.Hash();
            RaceWorld.Build(cfg, null, RaceQuality.High);   // 內含 Destroy → 再建
            ulong b = WorldHash();
            ulong tb = RaceWorld.Terrain.Hash();
            track = RaceWorld.Track;
            // 不同種子的高度圖要不一樣
            ulong tOther = RaceTerrain.Generate(RaceTrack.FromDef(DefaultTracks.Get(tid)), 1234).Hash();
            bool ok = a == b && a != 0UL && ta == tb && ta != tOther && RaceWorld.Ready;
            RaceWorld.Destroy(); RaceWorld.Destroy();       // 可重複呼叫
            bool cleared = !RaceWorld.Ready && RaceWorld.Track == null;
            RaceWorld.Build(cfg, null, RaceQuality.High);   // 留一份給後面的測試
            track = RaceWorld.Track;
            Report("T1 determinism", ok && cleared, "hashA=" + a.ToString("X") + " hashB=" + b.ToString("X") + " terrainHash=" + ta.ToString("X") + " otherSeedDiffers=" + (ta != tOther) + " destroyTwiceOk=" + cleared + " firstBuild=" + ms1 + "ms");
        }

        // ------------------------------------------------------------------ T3

        void T3()
        {
            RaceTrack tr = track;
            var errs = new List<string>();
            float L = tr.Length;
            float[] lenLo = { 1200f, 1000f, 1500f }, lenHi = { 1500f, 1300f, 2000f };
            bool gen = tid >= DefaultTracks.Handmade;
            float lo = gen ? 2500f : lenLo[Mathf.Clamp(tid, 0, 2)], hi = gen ? 4000f : lenHi[Mathf.Clamp(tid, 0, 2)];
            if (L < lo || L > hi) errs.Add("length " + L.ToString("0.0") + " not in " + lo + ".." + hi);
            float lapSec = L / 24f;
            if (lapSec < (gen ? 100f : 40f) || lapSec > (gen ? 175f : 85f)) errs.Add("lap time at 24 m/s " + lapSec.ToString("0") + " s not in " + (gen ? "100..175" : "40..85"));
            if (tr.MinRadius < 13f) errs.Add("min radius " + tr.MinRadius.ToString("0.0") + " < 13");
            if (tr.SampleStep < 1.5f || tr.SampleStep > 2.5f) errs.Add("sample step " + tr.SampleStep.ToString("0.00") + " not ~2 m");

            int n = tr.Samples.Count;
            float maxDy = 0f, maxSlopeDeg = 0f, minW = 999f, maxW = 0f;
            int roadBad = 0;
            for (int i = 0; i < n; i++)
            {
                TrackSample s = tr.Samples[i];
                bool inGap = tr.InGap(s.S);   // 缺口範圍(前緣到落地邊緣)沒有路面:高度 / 路面材質 / 碰撞體都不檢查(TGAP 另外檢查)
                if (!inGap) maxDy = Mathf.Max(maxDy, Mathf.Abs(RaceWorld.HeightAt(s.Pos.x, s.Pos.z) - s.Pos.y));
                TrackSample nx = tr.Samples[(i + 1) % n];
                float run = new Vector2(nx.Pos.x - s.Pos.x, nx.Pos.z - s.Pos.z).magnitude;
                float slope = Mathf.Atan2(Mathf.Abs(nx.Pos.y - s.Pos.y), Mathf.Max(0.01f, run)) * Mathf.Rad2Deg;
                maxSlopeDeg = Mathf.Max(maxSlopeDeg, slope);
                minW = Mathf.Min(minW, s.Width); maxW = Mathf.Max(maxW, s.Width);
                float hw = s.Width * 0.5f;
                if (inGap) continue;
                if (RaceWorld.SurfaceAt(s.Pos.x, s.Pos.z) != Surface.Road && !OnFeature(s.Pos.x, s.Pos.z)) roadBad++;
                Vector3 l = s.Pos - s.Right * (hw - 1f), r = s.Pos + s.Right * (hw - 1f);
                if (RaceWorld.SurfaceAt(l.x, l.z) != Surface.Road && !OnFeature(l.x, l.z)) roadBad++;
                if (RaceWorld.SurfaceAt(r.x, r.z) != Surface.Road && !OnFeature(r.x, r.z)) roadBad++;
            }
            if (maxDy >= 0.05f) errs.Add("height mismatch " + maxDy.ToString("0.000"));
            if (maxSlopeDeg > 12f) errs.Add("slope " + maxSlopeDeg.ToString("0.0") + " deg > 12");
            if (roadBad > 0) errs.Add(roadBad + " road points not Road");
            if (minW < 11.5f || maxW > 12.5f) errs.Add("width " + minW + ".." + maxW + " (expect ~12)");

            // 不自交:弧長相差 > 80 m 的兩點,中心線距離要大於路寬 + 護欄空間
            float minClear = float.MaxValue;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float arc = (j - i) * tr.SampleStep;
                    arc = Mathf.Min(arc, L - arc);
                    if (arc < 80f) continue;
                    Vector3 d = tr.Samples[i].Pos - tr.Samples[j].Pos; d.y = 0f;
                    minClear = Mathf.Min(minClear, d.magnitude);
                }
            if (minClear < (gen ? 50f : 24f)) errs.Add("self clearance " + minClear.ToString("0.0") + " < " + (gen ? 50 : 24));

            // Project 往返:取樣點投影回來 s 要一致、lateral 約 0;偏右 3 m 的點 lateral 約 +3
            float maxSErr = 0f, maxLatErr = 0f;
            for (int i = 0; i < n; i += 5)
            {
                TrackSample s = tr.Samples[i];
                float lat;
                float ps = tr.Project(s.Pos + s.Right * 3f, -1f, out lat);
                float ds = Mathf.Abs(ps - s.S); ds = Mathf.Min(ds, L - ds);
                maxSErr = Mathf.Max(maxSErr, ds); maxLatErr = Mathf.Max(maxLatErr, Mathf.Abs(lat - 3f));
                float ps2 = tr.Project(s.Pos, s.S + 20f, out lat);   // 帶提示的局部搜尋
                float ds2 = Mathf.Abs(ps2 - s.S); ds2 = Mathf.Min(ds2, L - ds2);
                maxSErr = Mathf.Max(maxSErr, ds2);
            }
            if (maxSErr > 0.6f || maxLatErr > 0.5f) errs.Add("project error s=" + maxSErr.ToString("0.00") + " lat=" + maxLatErr.ToString("0.00"));

            // SampleGround
            GroundHit gh;
            if (!RaceWorld.SampleGround(tr.Samples[0].Pos, out gh) || gh.Surface != Surface.Road || Mathf.Abs(gh.Height - tr.Samples[0].Pos.y) > 0.05f || gh.Normal.y < 0.9f) errs.Add("SampleGround at start line wrong");
            if (RaceWorld.SampleGround(new Vector3(900f, 0f, 0f), out gh)) errs.Add("SampleGround outside should be false");

            // 和地面碰撞體一致:往下打 raycast,只算賽車世界底下的碰撞體
            Physics.SyncTransforms();
            int hitOk = 0, total = 0;
            for (int i = 0; i < n; i += 7)
            {
                if (OnRamp(tr.Samples[i].Pos.x, tr.Samples[i].Pos.z, 1.5f)) continue;   // 跳台有自己的碰撞體(T10 檢查)
                if (tr.InGap(tr.Samples[i].S, 0f, 2f)) continue;                           // 缺口(深坑)裡沒有路面
                total++;
                Vector3 o = tr.Samples[i].Pos + Vector3.up * 5f;
                RaycastHit[] hits = Physics.RaycastAll(o, Vector3.down, 20f);
                bool found = false;
                foreach (var h in hits)
                    if (h.collider.transform.IsChildOf(RaceWorld.Root))
                    {
                        found = true;
                        if (Mathf.Abs(h.point.y - RaceWorld.HeightAt(h.point.x, h.point.z)) < 0.05f) hitOk++;
                        break;
                    }
                if (!found) errs.Add("no world collider under sample " + i);
                if (errs.Count > 6) break;
            }
            if (hitOk != total) errs.Add("collider/HeightAt mismatch " + hitOk + "/" + total);

            Report("T3 road quality", errs.Count == 0,
                "length=" + L.ToString("0.0") + "m minRadius=" + tr.MinRadius.ToString("0.0") + "m width=" + minW.ToString("0.0") + "~" + maxW.ToString("0.0") +
                " heightErr=" + maxDy.ToString("0.000") + " slope=" + maxSlopeDeg.ToString("0.0") + "deg clearance=" + minClear.ToString("0.0") + "m projErr=" + maxSErr.ToString("0.00") + "m" +
                " raycast=" + hitOk + "/" + total + " checkpoints=" + tr.CheckpointCount +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T4

        sealed class Scn
        {
            public int FinishedCount;
            public readonly List<int> Laps = new List<int>();
            public bool Monotonic = true;
            public RacerProgress Pr;
            public Ghost G;
            public int Standing1Id = -1;
        }

        /// 幽靈車以 25 m/s 沿中心線跑(dir=-1 倒著跑)。teleportAfter >= 0:跑過這麼遠後往前傳送 teleportBy 公尺。
        Scn Drive(RaceTrack tr, int laps, float startS, int dir, float duration, float teleportAfter = -1f, float teleportBy = 0f)
        {
            var scn = new Scn();
            var cp = new RaceCheckpoints(tr, laps, false);
            var g = new Ghost { Id = 0 };
            scn.G = g;
            float s = startS;
            Place(g, tr, s, dir);
            cp.Register(g, true);
            cp.LapCompleted += (id, lap) => scn.Laps.Add(lap);
            cp.Finished += id => scn.FinishedCount++;
            scn.Pr = cp.Get(0);
            float t = 0f, travelled = 0f, prevTotal = scn.Pr.Total;
            bool tp = false;
            cp.StartRace(0f);
            while (t < duration)
            {
                t += Dt;
                float ds = dir * GhostSpeed * Dt;
                s += ds; travelled += Mathf.Abs(ds);
                if (!tp && teleportAfter >= 0f && travelled >= teleportAfter) { s += teleportBy; tp = true; }
                Place(g, tr, s, dir);
                cp.Step(Dt, t);
                if (dir > 0 && !tp && !scn.Pr.Finished)
                {
                    if (scn.Pr.Total < prevTotal - 0.001f) scn.Monotonic = false;
                    prevTotal = scn.Pr.Total;
                }
            }
            return scn;
        }

        void T4()
        {
            RaceTrack tr = track;
            float L = tr.Length;
            int laps = 3;
            var errs = new List<string>();

            // a) 正向跑 3 圈(起點在終點線後方 10 m,所以總路程 = 3L + 10),再多跑 6 秒確認不會多算
            float need = (laps * L + 10f) / GhostSpeed;
            Scn a = Drive(tr, laps, L - 10f, 1, need + 6f);
            if (a.FinishedCount != 1) errs.Add("Finished fired " + a.FinishedCount + " times");
            if (a.Laps.Count != 3 || a.Laps[0] != 1 || a.Laps[1] != 2 || a.Laps[2] != 3) errs.Add("LapCompleted=[" + string.Join(",", a.Laps.ConvertAll(x => x.ToString()).ToArray()) + "]");
            if (a.Pr.Lap != laps || !a.Pr.Finished) errs.Add("lap=" + a.Pr.Lap + " finished=" + a.Pr.Finished);
            if (Mathf.Abs(a.Pr.FinishTime - need) > 0.15f) errs.Add("finishTime " + a.Pr.FinishTime.ToString("0.00") + " vs " + need.ToString("0.00"));
            if (Mathf.Abs(a.Pr.BestLapTime - L / GhostSpeed) > 0.15f) errs.Add("bestLap " + a.Pr.BestLapTime.ToString("0.00") + " vs " + (L / GhostSpeed).ToString("0.00"));
            if (!a.Monotonic) errs.Add("Total decreased");
            if (a.G.Respawns != 0) errs.Add("unexpected respawn x" + a.G.Respawns);

            // b) 倒著開 1.6 圈:不能算圈、不能完賽,而且要被判逆向
            Scn b = Drive(tr, laps, 10f, -1, 1.6f * L / GhostSpeed);
            if (b.Pr.Lap != 0 || b.Pr.Finished || b.FinishedCount != 0 || b.Laps.Count != 0) errs.Add("backwards counted: lap=" + b.Pr.Lap);
            if (!b.Pr.WrongWay) errs.Add("backwards not flagged WrongWay");

            // c) 傳送跳過第 3、4 個門:之後再怎麼開都拿不到圈,NextCp 卡在 3
            Checkpoint g3 = tr.Checkpoints[3];
            float distToG3 = 10f + g3.S - 5f;
            Scn c = Drive(tr, laps, L - 10f, 1, (distToG3 + 0.8f * L) / GhostSpeed, distToG3, 50f);   // 時間不夠再繞回第 3 個門
            if (c.Pr.Lap != 0 || c.Pr.Finished || c.FinishedCount != 0) errs.Add("teleport counted: lap=" + c.Pr.Lap);
            if (c.Pr.NextCp != 3) errs.Add("NextCp=" + c.Pr.NextCp + " expected 3");

            Report("T4 laps", errs.Count == 0,
                "3 laps: finishTime=" + a.Pr.FinishTime.ToString("0.00") + "s (expect " + need.ToString("0.00") + ") best=" + a.Pr.BestLapTime.ToString("0.00") + "s events=" + a.FinishedCount +
                "; backwards lap=" + b.Pr.Lap + " wrongWay=" + b.Pr.WrongWay + "; teleport lap=" + c.Pr.Lap + " nextCp=" + c.Pr.NextCp +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T6

        void T6()
        {
            RaceTrack tr = track;
            var errs = new List<string>();
            string waterInfo = "";
            var cp = new RaceCheckpoints(tr, 3, false);
            var g = new Ghost { Id = 5 };
            float s = tr.Length - 10f, t = 0f;
            Place(g, tr, s, 1);
            cp.Register(g, true);
            var reasons = new List<ResetReason>();
            cp.Reset += (id, r) => reasons.Add(r);
            cp.StartRace(0f);
            RacerProgress pr = cp.Get(5);
            while (pr.NextCp != 6 && t < 60f) { t += Dt; s += GhostSpeed * Dt; Place(g, tr, s, 1); cp.Step(Dt, t); }
            if (pr.NextCp != 6) errs.Add("could not reach gate 5");
            Pose expect = tr.Checkpoints[5].Respawn;
            if (cp.LastCheckpoint(5).Index != 5) errs.Add("LastCheckpoint=" + cp.LastCheckpoint(5).Index);

            // 1) 掉到 y=-10
            g.Pos = new Vector3(g.Pos.x, -10f, g.Pos.z); t += Dt; cp.Step(Dt, t);
            if (g.Respawns != 1 || reasons.Count < 1 || reasons[0] != ResetReason.Fell) errs.Add("fall: respawns=" + g.Respawns);
            else if ((g.LastPose.position - expect.position).magnitude > 0.01f) errs.Add("fall: wrong pose");

            // 2) 超出邊界
            g.Pos = new Vector3(900f, 0f, 0f); t += Dt; cp.Step(Dt, t);
            if (g.Respawns != 2 || reasons.Count < 2 || reasons[1] != ResetReason.OutOfBounds) errs.Add("bounds: respawns=" + g.Respawns);
            else if ((g.LastPose.position - expect.position).magnitude > 0.01f) errs.Add("bounds: wrong pose");

            // 重置點要在路面上(橫向 ≤ 半寬,SurfaceAt == Road)
            float lat;
            float ps = tr.Project(g.LastPose.position, -1f, out lat);
            Surface sf = RaceWorld.SurfaceAt(g.LastPose.position.x, g.LastPose.position.z);
            if (Mathf.Abs(lat) > tr.HalfWidthAt(ps) || sf != Surface.Road) errs.Add("respawn pose off road: lat=" + lat.ToString("0.0") + " surface=" + sf);
            Vector3 rf = g.LastPose.rotation * Vector3.forward;
            if (Vector3.Dot(rf, tr.TangentAt(ps)) < 0.95f) errs.Add("respawn pose not facing track direction");

            // 3) 卡住:有油門、不動,3 秒後重置一次(3.2 秒內只能一次)
            int before = g.Respawns;
            for (int i = 0; i < Mathf.RoundToInt(3.2f / Dt); i++) { t += Dt; cp.Step(Dt, t); }
            if (g.Respawns != before + 1 || reasons[reasons.Count - 1] != ResetReason.Stuck) errs.Add("stuck: respawns +" + (g.Respawns - before));

            // 4) 手動:第一次成功,馬上再按被冷卻擋掉,2.1 秒後成功
            before = g.Respawns;
            cp.RequestReset(5, ResetReason.Manual);
            cp.RequestReset(5, ResetReason.Manual);
            if (g.Respawns != before + 1) errs.Add("manual cooldown: respawns +" + (g.Respawns - before));
            g.Throttle = false;     // 不踩油門,避免 2 秒內又觸發卡住
            for (int i = 0; i < Mathf.RoundToInt(2.1f / Dt); i++) { t += Dt; cp.Step(Dt, t); }
            cp.RequestReset(5, ResetReason.Manual);
            if (g.Respawns != before + 2) errs.Add("manual after cooldown: respawns +" + (g.Respawns - before));

            // 5) 海:從島中心往西找第一個 Water 格,把車放在那裡(高度 y=0,沒有低於 KillY、也沒有超出邊界),下一步就要重置
            Vector3 waterPt = Vector3.zero; bool foundWater = false;
            for (float wx = -RaceTerrain.Half + 12f; wx <= 0f; wx += 1f)   // 從西邊的海往島中心找第一個「不是水」的點之前(M7:不要掃到缺口的深坑)
                if (RaceWorld.SurfaceAt(wx, 0f) != Surface.Water) { waterPt = new Vector3(wx - 1f, 0f, 0f); foundWater = RaceWorld.SurfaceAt(waterPt.x, waterPt.z) == Surface.Water; break; }
            if (!foundWater || RaceWorld.SurfaceAt(waterPt.x, waterPt.z) != Surface.Water || !RaceWorld.InBounds(waterPt)) errs.Add("no water found west of the island");
            else
            {
                int rb = g.Respawns; int rc = reasons.Count;
                g.Pos = waterPt; t += Dt; cp.Step(Dt, t);
                if (g.Respawns != rb + 1 || reasons.Count != rc + 1 || reasons[reasons.Count - 1] != ResetReason.Fell) errs.Add("water: respawns +" + (g.Respawns - rb));
                else if ((g.LastPose.position - expect.position).magnitude > 0.01f) errs.Add("water: wrong pose");
                if (SurfaceProps.Of(RaceWorld.SurfaceAt(waterPt.x, waterPt.z)).Resets == false) errs.Add("Water props do not reset");
                waterInfo = " water@x=" + waterPt.x.ToString("0") + " reset=ok";
            }

            // 6) 自由駕駛:掉下去後送到「離車子最近的門」
            var cpf = new RaceCheckpoints(tr, 3, true);
            var gf = new Ghost { Id = 1 };
            Vector3 spot = tr.PointAt(tr.Length * 0.37f);
            gf.Pos = new Vector3(spot.x, -10f, spot.z);
            cpf.Register(gf, true);
            cpf.Step(Dt, 1f);
            Pose near = cpf.NearestCheckpoint(spot).Respawn;
            if (gf.Respawns != 1 || (gf.LastPose.position - near.position).magnitude > 0.01f) errs.Add("freeroam: respawns=" + gf.Respawns);

            Report("T6 reset", errs.Count == 0,
                "reasons=[" + string.Join(",", reasons.ConvertAll(r => r.ToString()).ToArray()) + "] respawnPose lat=" + lat.ToString("0.0") + "m surface=" + sf + waterInfo +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }


        // ------------------------------------------------------------------ T10 跳台 / 加速板

        static bool OnFeature(float x, float z, float margin = 0f)
        {
            var fs = RaceWorld.Features;
            return fs != null && fs.IndexAt(x, z, margin) >= 0;
        }

        static bool OnRamp(float x, float z, float margin)
        {
            var fs = RaceWorld.Features;
            if (fs == null) return false;
            for (int i = 0; i < fs.Count; i++)
            {
                FeatureSet.Inst f = fs.Items[i];
                float du, dv;
                if (f.IsRamp && FeatureSet.Local(f, x, z, margin, out du, out dv)) return true;
            }
            return false;
        }

        void T10()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            FeatureSet fs = RaceWorld.Features;
            int ramps = 0, pads = 0;
            if (fs == null || fs.Count != tr.Features.Count) { Report("T10 ramps+boost pads", false, "feature set missing or count mismatch"); return; }
            for (int i = 0; i < fs.Count; i++) { if (fs.Items[i].Path >= 0) continue; if (fs.Items[i].IsRamp) ramps++; else pads++; }
            if (ramps < 3) errs.Add("ramps=" + ramps + " (want >= 3)");
            if (pads < 6) errs.Add("boost pads=" + pads + " (want >= 6)");

            Physics.SyncTransforms();
            int rampChecks = 0, colOk = 0, padChecks = 0;
            float maxStep = 0f, maxColErr = 0f;
            var landInfo = new StringBuilder();
            for (int i = 0; i < fs.Count; i++)
            {
                FeatureSet.Inst f = fs.Items[i];
                if (f.Path >= 0) continue;   // 捷徑上的加速板在 T5 檢查
                TrackSample sm = tr.SampleAt(f.S);
                float roadHw = sm.Width * 0.5f;
                float lat = (f.Cx - sm.Pos.x) * sm.Right.x + (f.Cz - sm.Pos.z) * sm.Right.z;
                string tag = f.Kind + "@s" + f.S.ToString("0");
                // 在路面內;跳台兩側各留 >= 2.5 m 可繞過
                float bypass = roadHw - (Mathf.Abs(lat) + f.HW);
                if (bypass < (f.IsGlider ? 0.9f : f.Kind == TrackFeatureKind.Ramp ? 2.5f : 1f)) errs.Add(tag + " bypass lane " + bypass.ToString("0.0") + " m too narrow");
                // 跳台不放在急彎上
                if (f.Kind == TrackFeatureKind.Ramp && Mathf.Abs(tr.MaxCurvature(f.S - 6f, f.S + 24f)) > 1f / 45f) errs.Add(tag + " ramp on a tight corner");

                if (f.IsRamp)
                {
                    // 高度連續:沿前進方向每 0.25 m 取樣;入口和地形接齊,斜面單調上升,頂端高 = tan * 長度
                    float prev = float.NaN, rise = 0f;
                    for (float du = -f.HL - 1f + 0.005f; du <= f.HL - 0.01f; du += 0.25f)
                    {
                        float x = f.Cx + f.Fx * du, z = f.Cz + f.Fz * du;
                        float h = RaceWorld.GroundHeightAt(x, z), g = RaceWorld.HeightAt(x, z);
                        float off = h - g;
                        Surface sf = RaceWorld.SurfaceAt(x, z);
                        if (du < -f.HL)
                        {
                            if (Mathf.Abs(off) > 1e-4f) errs.Add(tag + " height offset before ramp");
                            if (sf != Surface.Road && !OnFeature(x, z)) errs.Add(tag + " surface before ramp = " + sf);
                            continue;
                        }
                        if (sf != Surface.Ramp) errs.Add(tag + " du=" + du.ToString("0.00") + " surface=" + sf);
                        float want = f.TanP * (du + f.HL);
                        if (Mathf.Abs(off - want) > 0.01f) errs.Add(tag + " ramp offset " + off.ToString("0.000") + " want " + want.ToString("0.000"));
                        if (!float.IsNaN(prev))
                        {
                            maxStep = Mathf.Max(maxStep, Mathf.Abs(off - prev));
                            if (off < prev - 1e-4f) errs.Add(tag + " ramp not monotone");
                            if (off - prev > f.TanP * 0.25f + 0.02f) errs.Add(tag + " height jump");
                        }
                        prev = off; rise = off;
                    }
                    float wantRise = f.TanP * (2f * f.HL);
                    if (rise < wantRise - 0.3f) errs.Add(tag + " rise " + rise.ToString("0.00") + " want " + wantRise.ToString("0.00"));
                    float pitchDeg = Mathf.Atan(f.TanP) * Mathf.Rad2Deg;
                    if (pitchDeg < 12f || pitchDeg > 25f) errs.Add(tag + " pitch " + pitchDeg.ToString("0.0"));
                    // 前後 0.5 m 外、側邊 0.5 m 外是普通路面
                    float[] outs = { f.HL + 0.5f, -f.HL - 0.5f };
                    foreach (float du in outs)
                    {
                        float ex = f.Cx + f.Fx * du, ez = f.Cz + f.Fz * du;
                        if (RaceWorld.SurfaceAt(ex, ez) != Surface.Road && !OnFeature(ex, ez)) errs.Add(tag + " surface beyond end not Road");
                    }
                    if (RaceWorld.SurfaceAt(f.Cx + f.Rx * (f.HW + 0.5f), f.Cz + f.Rz * (f.HW + 0.5f)) != Surface.Road) errs.Add(tag + " side lane not Road");

                    // 碰撞體:往下打射線,頂面高度要和 GroundHeightAt 一致,而且掛的是 RaceSurface(Ramp)
                    foreach (float uu in new float[] { -3.5f, -1.5f, 0f, 1.5f, 3.5f })
                        foreach (float vv in new float[] { -f.HW + 0.4f, 0f, f.HW - 0.4f })
                        {
                            float x = f.Cx + f.Fx * uu + f.Rx * vv, z = f.Cz + f.Fz * uu + f.Rz * vv;
                            float expect = RaceWorld.GroundHeightAt(x, z);
                            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, expect + 8f, z), Vector3.down, 30f);
                            bool found = false; float best = float.MaxValue; RaycastHit bh = default;
                            foreach (var h in hits)
                                if (h.collider.transform.IsChildOf(RaceWorld.Root) && h.distance < best) { best = h.distance; bh = h; found = true; }
                            rampChecks++;
                            if (!found) { errs.Add(tag + " no collider at (" + uu + "," + vv + ")"); continue; }
                            RaceSurface rs = bh.collider.GetComponentInParent<RaceSurface>();
                            float e = Mathf.Abs(bh.point.y - expect);
                            maxColErr = Mathf.Max(maxColErr, e);
                            if (rs != null && rs.Type == Surface.Ramp && e < 0.12f && bh.normal.y > KartGroundProbe.MinNormalY) colOk++;
                            else errs.Add(tag + " collider at (" + uu + "," + vv + ") surface=" + (rs != null ? rs.Type.ToString() : "none") + " err=" + e.ToString("0.00") + " ny=" + bh.normal.y.ToString("0.00"));
                            if (errs.Count > 10) break;
                        }

                    // 落點:以 22 m/s 衝上去的拋體(AirGravity 30)落在 9~17 m 內;落地區(到落點後 6 m)是平路(Road 或加速板),沒有別的跳台
                    // (滑翔跳台不走這一段:它的落點 50~90 m 外,由 TG 檢查)
                    if (!f.IsGlider)
                    {
                    float ang = Mathf.Atan(f.TanP);
                    float v = 22f, vy = v * Mathf.Sin(ang), vx = v * Mathf.Cos(ang);
                    float h0 = f.TanP * 2f * f.HL;
                    float tFlight = (vy + Mathf.Sqrt(vy * vy + 2f * 30f * h0)) / 30f;
                    float dist = vx * tFlight;
                    landInfo.Append(" ").Append(tag).Append(" lands~").Append(dist.ToString("0.0")).Append("m");
                    if (dist < 9f || dist > 17f) errs.Add(tag + " flight distance " + dist.ToString("0.0") + " not in 9..17");
                    float lipG = RaceWorld.HeightAt(f.Cx + f.Fx * f.HL, f.Cz + f.Fz * f.HL);
                    for (float du = f.HL + 1f; du <= f.HL + dist + 6f; du += 1f)
                    {
                        float x = f.Cx + f.Fx * du, z = f.Cz + f.Fz * du;
                        Surface sf = RaceWorld.SurfaceAt(x, z);
                        if (sf != Surface.Road && sf != Surface.BoostPad) { errs.Add(tag + " landing zone du=" + du.ToString("0") + " surface=" + sf); break; }
                        if (Mathf.Abs(RaceWorld.HeightAt(x, z) - lipG) > 0.15f * (du - f.HL) + 0.5f) { errs.Add(tag + " landing zone not flat"); break; }
                    }
                    }
                }
                else
                {
                    // 加速板:中心與四角(內縮 0.3 m)= BoostPad;外 0.6 m = Road;SampleGround 也回 BoostPad;沒有會擋車的 RaceSurface 碰撞體
                    float[] us = { 0f, -f.HL + 0.3f, f.HL - 0.3f }, vs = { 0f, -f.HW + 0.3f, f.HW - 0.3f };
                    foreach (float uu in us)
                        foreach (float vv in vs)
                        {
                            float x = f.Cx + f.Fx * uu + f.Rx * vv, z = f.Cz + f.Fz * uu + f.Rz * vv;
                            padChecks++;
                            if (RaceWorld.SurfaceAt(x, z) != Surface.BoostPad) errs.Add(tag + " (" + uu + "," + vv + ") not BoostPad");
                        }
                    float ox = f.Cx + f.Fx * (f.HL + 0.6f), oz = f.Cz + f.Fz * (f.HL + 0.6f);
                    if (RaceWorld.SurfaceAt(ox, oz) != Surface.Road && !OnFeature(ox, oz)) errs.Add(tag + " beyond pad not Road");
                    GroundHit gh;
                    if (!RaceWorld.SampleGround(new Vector3(f.Cx, 0f, f.Cz), out gh) || gh.Surface != Surface.BoostPad || Mathf.Abs(gh.Height - RaceWorld.HeightAt(f.Cx, f.Cz)) > 1e-4f) errs.Add(tag + " SampleGround not BoostPad");
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(f.Cx, RaceWorld.HeightAt(f.Cx, f.Cz) + 4f, f.Cz), Vector3.down, 10f);
                    foreach (var h in hits)
                        if (h.collider.transform.IsChildOf(RaceWorld.Root) && h.collider.GetComponentInParent<RaceSurface>() != null) errs.Add(tag + " pad has a RaceSurface collider");
                }
            }

            // 檢查點不被擋:門線上的點(整個路寬)不在跳台內(外擴 2 m)、重置點不在跳台 / 加速板上且是普通路面;起跑格不在任何物件上
            int cpChecked = 0;
            for (int c = 0; c < tr.CheckpointCount; c++)
            {
                Checkpoint cp = tr.Checkpoints[c];
                Vector3 right = new Vector3(cp.Forward.z, 0f, -cp.Forward.x);
                float roadHw = cp.HalfWidth - 4f;
                for (float lat = -roadHw; lat <= roadHw + 0.01f; lat += 1f)
                {
                    Vector3 q = cp.Center + right * lat;
                    if (OnRamp(q.x, q.z, 2f)) { errs.Add("gate " + c + " blocked by a ramp at lateral " + lat.ToString("0")); break; }
                }
                Vector3 rp = cp.Respawn.position;
                if (OnRamp(rp.x, rp.z, 2f)) errs.Add("gate " + c + " respawn inside a ramp");
                if (OnFeature(rp.x, rp.z, 0.5f)) errs.Add("gate " + c + " respawn on a feature");
                if (RaceWorld.SurfaceAt(rp.x, rp.z) != Surface.Road) errs.Add("gate " + c + " respawn surface " + RaceWorld.SurfaceAt(rp.x, rp.z));
                cpChecked++;
            }
            for (int slot = 0; slot < 8; slot++)
            {
                Vector3 gp = tr.Grid(slot).position;
                if (OnFeature(gp.x, gp.z, 1f)) errs.Add("grid slot " + slot + " on a feature");
            }

            Report("T10 ramps+boost pads", errs.Count == 0,
                "ramps=" + ramps + " pads=" + pads + " rampColliderChecks=" + colOk + "/" + rampChecks + " maxColliderErr=" + maxColErr.ToString("0.000") + "m maxHeightStep=" + maxStep.ToString("0.000") +
                "m padChecks=" + padChecks + " gatesChecked=" + cpChecked + " flight:" + landInfo +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T5 捷徑(Island GP)

        sealed class ScRun
        {
            public int Laps, Respawns, NextCpEnd;
            public bool Finished, SawOnSc, OnScAtEnd, SawWrong;
            public float FinishTime, MaxDecrease;
        }

        /// 幽靈車路線:主線(從終點線後 10 m)→ 分岔點 → 捷徑 → 匯入點 → 主線。mode 1:到分岔點後直接傳送到捷徑 d = 60(沒穿入口門)。
        static void RoutePos(RaceTrack tr, ShortcutPath sc, float seg1, float d0, float seg2, float trav, out Vector3 pos, out Vector3 tan)
        {
            if (trav < seg1)
            {
                TrackSample m = tr.SampleAt(tr.Length - 10f + trav);
                pos = m.Pos; tan = m.Tangent;
            }
            else if (trav < seg1 + seg2)
            {
                TrackSample m = sc.SampleAt(d0 + (trav - seg1));
                pos = m.Pos; tan = m.Tangent;
            }
            else
            {
                TrackSample m = tr.SampleAt(sc.ToS + (trav - seg1 - seg2));
                pos = m.Pos; tan = m.Tangent;
            }
        }

        ScRun DriveShortcut(RaceTrack tr, ShortcutPath sc, int mode, float maxTime)
        {
            var r = new ScRun();
            var cp = new RaceCheckpoints(tr, 1, false);
            var g = new Ghost { Id = 0 };
            float seg1 = 10f + sc.FromS;
            float d0 = mode == 1 ? 60f : 0f;
            float seg2 = sc.Length - d0;
            Vector3 pos, tan;
            RoutePos(tr, sc, seg1, d0, seg2, 0f, out pos, out tan);
            g.Pos = pos; g.Fwd = tan; g.Vel = tan * GhostSpeed;
            cp.Register(g, true);
            RacerProgress pr = cp.Get(0);
            cp.StartRace(0f);
            float t = 0f, trav = 0f, prevTotal = pr.Total;
            while (t < maxTime && !pr.Finished)
            {
                t += Dt; trav += GhostSpeed * Dt;
                RoutePos(tr, sc, seg1, d0, seg2, trav, out pos, out tan);
                g.Pos = pos; g.Fwd = tan; g.Vel = tan * GhostSpeed;
                cp.Step(Dt, t);
                if (pr.OnShortcut) r.SawOnSc = true;
                if (pr.WrongWay) r.SawWrong = true;
                float dec = prevTotal - pr.Total;
                if (dec > r.MaxDecrease && !pr.Finished) r.MaxDecrease = dec;
                prevTotal = pr.Total;
            }
            // 再跑幾步確認狀態穩定
            for (int i = 0; i < 25 && !pr.Finished; i++) { t += Dt; trav += GhostSpeed * Dt; RoutePos(tr, sc, seg1, d0, seg2, trav, out pos, out tan); g.Pos = pos; g.Fwd = tan; cp.Step(Dt, t); }
            r.Laps = pr.Lap; r.Finished = pr.Finished; r.FinishTime = pr.FinishTime; r.Respawns = g.Respawns; r.NextCpEnd = pr.NextCp; r.OnScAtEnd = pr.OnShortcut;
            return r;
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float l2 = abx * abx + abz * abz;
            float u = l2 > 1e-9f ? Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / l2) : 0f;
            float ex = a.x + abx * u - p.x, ez = a.z + abz * u - p.z;
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        void T5()
        {
            RaceTrack tr = track;
            var errs = new List<string>();
            if (tr.Shortcuts.Count != 1) { Report("T5 shortcut", false, "shortcuts=" + tr.Shortcuts.Count + " (want 1)"); return; }
            ShortcutPath sc = tr.Shortcuts[0];
            float L = tr.Length;

            // 1) 被跳過的門:連續、不含終點線、ShortcutId 標對
            int skipped = 0;
            for (int i = 0; i < tr.CheckpointCount; i++)
            {
                bool inRange = sc.FirstSkipped >= 0 && i >= sc.FirstSkipped && i <= sc.LastSkipped;
                bool marked = tr.Checkpoints[i].ShortcutId == sc.Id;
                if (inRange != marked) errs.Add("gate " + i + " ShortcutId mismatch");
                if (marked) skipped++;
            }
            if (sc.FirstSkipped < 1 || sc.LastSkipped < sc.FirstSkipped || skipped < 2) errs.Add("skipped gates " + sc.FirstSkipped + ".." + sc.LastSkipped);
            if (tr.Checkpoints[0].ShortcutId != -1) errs.Add("finish line marked as skipped");
            if (sc.Entry == null || sc.Exit == null || sc.Entry.Index != -1 || sc.Exit.Index != -1) errs.Add("entry/exit gates missing");

            // 2) 長度:比主線那段短(一定要比較短才叫捷徑),但不能短到失去意義;路面 Dirt 比較慢 → 要靠加速板補
            float span = sc.MainSpan;
            if (sc.Length > 0.7f * span || sc.Length < 0.35f * span) errs.Add("shortcut length " + sc.Length.ToString("0") + " vs main " + span.ToString("0") + " (want 0.35..0.7)");

            // 3) 幾何:高度貼地、坡度、路面材質、離主線的距離
            float maxDy = 0f, maxSlope = 0f, minSep = 1e9f;
            int surfBad = 0, surfChecked = 0;
            for (int i = 0; i < sc.Count; i++)
            {
                Vector3 p = sc.Pos[i];
                maxDy = Mathf.Max(maxDy, Mathf.Abs(RaceWorld.HeightAt(p.x, p.z) - p.y));
                if (i + 1 < sc.Count)
                {
                    Vector3 q = sc.Pos[i + 1];
                    float run = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                    maxSlope = Mathf.Max(maxSlope, Mathf.Atan2(Mathf.Abs(q.y - p.y), Mathf.Max(0.01f, run)) * Mathf.Rad2Deg);
                }
                float d = i * sc.Step;
                if (d >= 40f && d <= sc.Length - 40f)
                {
                    float lat; tr.Project(p, -1f, out lat);
                    minSep = Mathf.Min(minSep, Mathf.Abs(lat));
                }
                if (d >= 30f && d <= sc.Length - 30f)
                {
                    foreach (float o in new float[] { 0f, sc.W[i] * 0.5f - 1f, -(sc.W[i] * 0.5f - 1f) })
                    {
                        Vector3 w = p + sc.Right[i] * o;
                        Surface sf = RaceWorld.SurfaceAt(w.x, w.z);
                        surfChecked++;
                        if (sf != Surface.Dirt && sf != Surface.BoostPad) surfBad++;
                    }
                }
            }
            if (maxDy > 0.05f) errs.Add("height mismatch " + maxDy.ToString("0.000"));
            if (maxSlope > 12f) errs.Add("slope " + maxSlope.ToString("0.0") + " deg");
            if (surfBad > 0) errs.Add(surfBad + "/" + surfChecked + " shortcut points are not Dirt/BoostPad");
            if (minSep < 20f) errs.Add("shortcut comes within " + minSep.ToString("0.0") + " m of the main road");

            // 4) 被跳過的門離捷徑夠遠(走捷徑的車不可能碰到主線的門,否則會被當成走主線)
            float minGate = 1e9f;
            for (int i = sc.FirstSkipped; i <= sc.LastSkipped && i >= 0; i++)
            {
                Checkpoint g = tr.Checkpoints[i];
                Vector3 right = new Vector3(g.Forward.z, 0f, -g.Forward.x);
                Vector3 ga = g.Center - right * g.HalfWidth, gb = g.Center + right * g.HalfWidth;
                for (int k = 0; k < sc.Count; k++) minGate = Mathf.Min(minGate, DistToSegment(sc.Pos[k], ga, gb));
            }
            if (minGate < 8f) errs.Add("a skipped gate is only " + minGate.ToString("0.0") + " m from the shortcut");

            // 5) 入口 / 出口門離主線夠遠(主線上的車不會誤觸),而且在捷徑路面上
            float entrySep = 1e9f, exitSep = 1e9f;
            { float lat; tr.Project(sc.Entry.Center, -1f, out lat); entrySep = Mathf.Abs(lat); tr.Project(sc.Exit.Center, -1f, out lat); exitSep = Mathf.Abs(lat); }
            if (entrySep < tr.HalfWidthAt(sc.FromS) + sc.Entry.HalfWidth + 1f) errs.Add("entry gate only " + entrySep.ToString("0.0") + " m from main center");
            if (exitSep < tr.HalfWidthAt(sc.ToS) + sc.Exit.HalfWidth + 1f) errs.Add("exit gate only " + exitSep.ToString("0.0") + " m from main center");
            if (sc.Entry.S >= sc.Exit.S) errs.Add("entry gate is not before exit gate");

            // 6) 捷徑上的加速板
            int scPads = 0;
            FeatureSet fs = RaceWorld.Features;
            for (int i = 0; fs != null && i < fs.Count; i++)
            {
                FeatureSet.Inst f = fs.Items[i];
                if (f.Path != 0) continue;
                scPads++;
                if (f.Kind != TrackFeatureKind.Boost) errs.Add("shortcut feature is not a boost pad");
                if (RaceWorld.SurfaceAt(f.Cx, f.Cz) != Surface.BoostPad) errs.Add("shortcut pad @" + f.S.ToString("0") + " not BoostPad");
                if (f.S < sc.Entry.S - 1f || f.S > sc.Exit.S) errs.Add("shortcut pad @" + f.S.ToString("0") + " outside entry..exit");
            }
            if (scPads < 2) errs.Add("shortcut pads=" + scPads + " (want >= 2: dirt is slower, boost pads make it worth it)");

            // 7) 幽靈車走捷徑:算一圈、Total 不倒退、沒被重置、沒被當逆向、比主線快
            float maxT = (L + 10f) / GhostSpeed + 15f;
            ScRun a = DriveShortcut(tr, sc, 0, maxT);
            if (!a.Finished || a.Laps != 1) errs.Add("shortcut lap not counted: finished=" + a.Finished + " lap=" + a.Laps + " nextCp=" + a.NextCpEnd);
            if (!a.SawOnSc || a.OnScAtEnd) errs.Add("OnShortcut flag: saw=" + a.SawOnSc + " atEnd=" + a.OnScAtEnd);
            if (a.Respawns != 0) errs.Add("respawned " + a.Respawns + "x on the shortcut");
            if (a.SawWrong) errs.Add("flagged WrongWay on the shortcut");
            if (a.MaxDecrease > 1f) errs.Add("Total decreased by " + a.MaxDecrease.ToString("0.00") + " m on the shortcut");
            Scn m = Drive(tr, 1, L - 10f, 1, (L + 10f) / GhostSpeed + 6f);
            float saved = m.Pr.FinishTime - a.FinishTime;
            float expectSaved = (span - sc.Length) / GhostSpeed;
            if (!m.Pr.Finished) errs.Add("main-route baseline did not finish");
            else if (saved < expectSaved - 1f || saved < 2f) errs.Add("shortcut saves " + saved.ToString("0.00") + " s (ghost at constant speed, expect ~" + expectSaved.ToString("0.00") + ")");

            // 8) 沒穿入口門(直接傳送進捷徑)拿不到被跳過的門:這一圈不能算,NextCp 停在第一個被跳過的門
            ScRun c = DriveShortcut(tr, sc, 1, maxT);
            if (c.Finished || c.Laps != 0 || c.NextCpEnd != sc.FirstSkipped) errs.Add("skipping the entry gate counted: finished=" + c.Finished + " lap=" + c.Laps + " nextCp=" + c.NextCpEnd + " (want " + sc.FirstSkipped + ")");

            Report("T5 shortcut", errs.Count == 0,
                "len=" + sc.Length.ToString("0.0") + "m mainSpan=" + span.ToString("0.0") + "m skippedGates=" + sc.FirstSkipped + ".." + sc.LastSkipped + " slope=" + maxSlope.ToString("0.0") + "deg heightErr=" + maxDy.ToString("0.000") +
                " minSepFromMain=" + minSep.ToString("0") + "m skippedGateClearance=" + minGate.ToString("0") + "m entrySep=" + entrySep.ToString("0") + "m exitSep=" + exitSep.ToString("0") + "m pads=" + scPads +
                " shortcutLap=" + a.FinishTime.ToString("0.00") + "s mainLap=" + m.Pr.FinishTime.ToString("0.00") + "s saved=" + saved.ToString("0.00") + "s maxTotalDrop=" + a.MaxDecrease.ToString("0.00") + "m skipEntry:lap=" + c.Laps + " nextCp=" + c.NextCpEnd +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T13 地標

        void T13()
        {
            RaceTrack tr = track;
            RaceTerrain terr = RaceWorld.Terrain;
            var errs = new List<string>();
            var res = RaceWorld.Landmarks;
            if (res == null) { Report("T13 landmarks", false, "no landmark result"); return; }
            if (tr.Landmarks.Count < 3) errs.Add("landmarks=" + tr.Landmarks.Count + " (want >= 3)");
            if (res.Count != tr.Landmarks.Count || res.Bases.Count != tr.Landmarks.Count) errs.Add("built " + res.Count + " of " + tr.Landmarks.Count);
            int onLand = 0, nearRoad = 0;
            for (int i = 0; i < res.Bases.Count && i < tr.Landmarks.Count; i++)
            {
                Vector3 b = res.Bases[i];
                TrackLandmark lm = tr.Landmarks[i];
                bool arch = lm.kind == (int)TrackLandmarkKind.BigArch || lm.kind == (int)TrackLandmarkKind.RockArch;
                if (terr.Raw(b.x, b.z) >= 0.4f) onLand++; else errs.Add("landmark " + i + " base is in the sea");
                float lat; float s = tr.Project(b, -1f, out lat);
                float dist = Mathf.Abs(lat) - tr.HalfWidthAt(s);
                if (!arch && dist < 8f) { nearRoad++; errs.Add("landmark " + i + " (kind " + lm.kind + ") only " + dist.ToString("0.0") + " m from the road edge"); }
                if (arch && Mathf.Abs(lm.lateral) > 1f) errs.Add("arch " + i + " is not centered on the road");
            }
            Transform lr = RaceWorld.Root.Find("Landmarks");
            int mf = lr != null ? lr.GetComponentsInChildren<MeshFilter>(true).Length : 0;
            if (mf < 1) errs.Add("no landmark meshes");
            if (res.Colliders > 20) errs.Add("landmark colliders " + res.Colliders + " > 20");
            if (res.Tris > 12000) errs.Add("landmark triangles " + res.Tris + " > 12000");
            DefaultTracks.Get(tid);
            if (TrackTheme.Current != TrackTheme.Get(DefaultTracks.Get(tid).theme)) errs.Add("theme not applied");
            Report("T13 landmarks", errs.Count == 0,
                "landmarks=" + res.Count + " onLand=" + onLand + " meshes=" + mf + " tris=" + res.Tris + " colliders=" + res.Colliders + " theme=" + DefaultTracks.Get(tid).theme +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T11 自由探索檢查點

        void T11()
        {
            RaceTrack tr = track;
            var errs = new List<string>();
            float L = tr.Length;
            var cp = new RaceCheckpoints(tr, 3, true);
            var g = new Ghost { Id = 3 };
            float s = L - 10f, t = 0f;
            Place(g, tr, s, 1);
            cp.Register(g, true);
            cp.StartRace(0f);
            var reasons = new List<ResetReason>();
            cp.Reset += (id, r) => reasons.Add(r);
            int laps = 0, fin = 0;
            cp.LapCompleted += (id, lap) => laps++;
            cp.Finished += id => fin++;
            RacerProgress pr = cp.Get(3);
            int steps = Mathf.RoundToInt(1.3f * L / GhostSpeed / Dt);
            for (int i = 0; i < steps; i++) { t += Dt; s += GhostSpeed * Dt; Place(g, tr, s, 1); cp.Step(Dt, t); }
            if (pr.Lap != 0 || pr.Finished || laps != 0 || fin != 0) errs.Add("free roam counted laps: lap=" + pr.Lap + " finished=" + pr.Finished);
            if (g.Respawns != 0) errs.Add("unexpected respawn on the road x" + g.Respawns);

            // 找一個離賽道最遠的收集物當「站在草地上」的地點(證明重生不是回到賽道的門)
            Vector3 land = Vector3.zero; float bestD = -1f;
            foreach (var it in RaceWorld.Collectibles)
            {
                float lat; float sp = tr.Project(it.Pos, -1f, out lat);
                float d = Mathf.Abs(lat) - tr.HalfWidthAt(sp);
                if (d > bestD) { bestD = d; land = it.Pos; }
            }
            land.y = RaceWorld.HeightAt(land.x, land.z);
            if (bestD < 40f) errs.Add("no coin far from the road (best " + bestD.ToString("0") + " m)");

            g.Throttle = false;
            g.Pos = land; g.Vel = Vector3.zero;
            for (int i = 0; i < Mathf.RoundToInt(3.5f / Dt); i++) { t += Dt; cp.Step(Dt, t); }
            if (g.Respawns != 0) errs.Add("respawned while standing on land");

            // 海:往西(或往東)找第一格 Water
            Vector3 water = Vector3.zero; bool found = false;
            for (int dir = -1; dir <= 1 && !found; dir += 2)
                for (float wx = land.x; wx > -RaceTerrain.Half + 2f && wx < RaceTerrain.Half - 2f; wx += dir)
                    if (RaceWorld.SurfaceAt(wx, land.z) == Surface.Water) { water = new Vector3(wx + dir, 0f, land.z); found = true; break; }
            if (!found || RaceWorld.SurfaceAt(water.x, water.z) != Surface.Water) errs.Add("no sea found next to the coin");
            else
            {
                g.Pos = water; t += Dt; cp.Step(Dt, t);
                float away = new Vector2(g.LastPose.position.x - land.x, g.LastPose.position.z - land.z).magnitude;
                if (g.Respawns != 1 || reasons.Count != 1 || reasons[0] != ResetReason.Fell) errs.Add("sea: respawns=" + g.Respawns);
                else if (away > 3f) errs.Add("sea: respawned " + away.ToString("0") + " m from the last safe spot");
                if (RaceWorld.SurfaceAt(g.LastPose.position.x, g.LastPose.position.z) == Surface.Water) errs.Add("respawned into the sea");
            }

            // 出界:再站 3.5 秒記下安全位置,然後被丟到邊界外
            g.Throttle = false;
            for (int i = 0; i < Mathf.RoundToInt(3.5f / Dt); i++) { t += Dt; cp.Step(Dt, t); }
            int before = g.Respawns;
            g.Pos = new Vector3(900f, 5f, 0f); t += Dt; cp.Step(Dt, t);
            if (g.Respawns != before + 1 || reasons[reasons.Count - 1] != ResetReason.OutOfBounds) errs.Add("bounds: respawns +" + (g.Respawns - before));
            else if (new Vector2(g.LastPose.position.x - land.x, g.LastPose.position.z - land.z).magnitude > 3f) errs.Add("bounds: respawn not at the last safe spot");

            // 自由探索不判卡住:有油門不動 5 秒也不重置
            before = g.Respawns;
            g.Throttle = true; g.Vel = Vector3.zero;
            for (int i = 0; i < Mathf.RoundToInt(5f / Dt); i++) { t += Dt; cp.Step(Dt, t); }
            if (g.Respawns != before) errs.Add("free roam reset a stationary kart (stuck rule should be off)");

            Report("T11 freeroam checkpoints", errs.Count == 0,
                "driven=" + (1.3f * L).ToString("0") + "m lap=" + pr.Lap + " finished=" + pr.Finished + " resets=[" + string.Join(",", reasons.ConvertAll(r => r.ToString()).ToArray()) + "] coinDistFromRoad=" + bestD.ToString("0") + "m" +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T12 收集物

        void T12()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            RaceTerrain terr = RaceWorld.Terrain;
            var items = RaceWorld.Collectibles;
            if (items.Count != 30 || RaceWorld.CollectibleTotal != 30) errs.Add("count " + items.Count + " (want 30)");
            int landBad = 0, near = 0;
            float minX = 1e9f, maxX = -1e9f, minZ = 1e9f, maxZ = -1e9f, minSep = 1e9f;
            for (int i = 0; i < items.Count; i++)
            {
                Vector3 p = items[i].Pos;
                if (items[i].Taken) errs.Add("coin " + i + " already taken");
                Surface sf = RaceWorld.SurfaceAt(p.x, p.z);
                if (terr.Raw(p.x, p.z) < 1.0f || sf == Surface.Water || sf == Surface.OutOfBounds || !RaceWorld.InBounds(p)) landBad++;
                if (p.y < RaceWorld.HeightAt(p.x, p.z) + 0.5f || p.y > RaceWorld.HeightAt(p.x, p.z) + 3f) errs.Add("coin " + i + " height off the ground");
                float lat; float sp = tr.Project(p, -1f, out lat);
                if (Mathf.Abs(lat) - tr.HalfWidthAt(sp) <= 30f) near++;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                for (int j = i + 1; j < items.Count; j++) minSep = Mathf.Min(minSep, new Vector2(p.x - items[j].Pos.x, p.z - items[j].Pos.z).magnitude);
            }
            if (landBad > 0) errs.Add(landBad + " coins are not on land");
            if (near < 6) errs.Add("only " + near + " coins within 30 m of the road");
            if (minSep < 25f) errs.Add("two coins only " + minSep.ToString("0.0") + " m apart");
            if (maxX - minX < 400f || maxZ - minZ < 400f) errs.Add("coins not spread over the island: " + (maxX - minX).ToString("0") + " x " + (maxZ - minZ).ToString("0"));
            Transform croot = RaceWorld.Root.Find("Collectibles");
            if (croot == null || croot.childCount != items.Count) errs.Add("coin objects missing");

            // 撿取判定
            int events = 0; var evIdx = new List<int>();
            System.Action<int, Vector3> h = (i, p) => { events++; evIdx.Add(i); };
            RaceWorld.CoinCollected += h;
            try
            {
                Vector3 p0 = items[0].Pos;
                if (RaceWorld.TryCollect(p0 + new Vector3(50f, 0f, 0f)) != 0) errs.Add("collected a coin from 50 m away");
                if (RaceWorld.TryCollect(p0 + new Vector3(0f, 8f, 0f)) != 0) errs.Add("collected a coin from 8 m above");
                if (RaceWorld.TryCollect(p0) != 1 || !items[0].Taken || RaceWorld.CollectedCount != 1) errs.Add("on-top pickup failed");
                if (RaceWorld.TryCollect(p0) != 0 || RaceWorld.CollectedCount != 1) errs.Add("picked the same coin twice");
                Vector3 p1 = items[1].Pos;
                if (RaceWorld.TryCollect(p1 + new Vector3(CollectibleSet.PickupRadius + 0.5f, 0f, 0f)) != 0) errs.Add("pickup radius too generous");
                if (RaceWorld.TryCollect(p1 + new Vector3(CollectibleSet.PickupRadius - 0.4f, 0f, 0f)) != 1) errs.Add("pickup radius too small");
                Vector3 p2 = items[2].Pos;
                if (RaceWorld.TryCollect(p2 + new Vector3(0f, CollectibleSet.PickupHeight + 1f, 0f)) != 0) errs.Add("pickup height too generous");
                if (RaceWorld.TryCollect(p2 + new Vector3(0f, 2f, 0f)) != 1) errs.Add("pickup height too small");
                if (events != 3 || evIdx[0] != 0 || evIdx[1] != 1 || evIdx[2] != 2) errs.Add("CoinCollected events=" + events);
                for (int i = 3; i < items.Count; i++) RaceWorld.TryCollect(items[i].Pos);
                if (RaceWorld.CollectedCount != 30 || events != 30) errs.Add("collected " + RaceWorld.CollectedCount + " / events " + events);
                int active = 0; for (int i = 0; croot != null && i < croot.childCount; i++) if (croot.GetChild(i).gameObject.activeSelf) active++;
                if (active != 0) errs.Add(active + " coin objects still visible after pickup");
                RaceWorld.ResetCollectibles();
                if (RaceWorld.CollectedCount != 0 || items[0].Taken) errs.Add("ResetCollectibles did not reset");
            }
            finally { RaceWorld.CoinCollected -= h; }

            // 決定性:同一個設定再建一次,收集物位置完全相同
            var first = new List<Vector3>();
            foreach (var it in items) first.Add(it.Pos);
            RaceWorld.Build(cfg, null, RaceQuality.High);
            track = RaceWorld.Track;
            var again = RaceWorld.Collectibles;
            bool same = again.Count == first.Count;
            for (int i = 0; same && i < first.Count; i++) if ((again[i].Pos - first[i]).sqrMagnitude > 1e-6f) same = false;
            if (!same) errs.Add("coin positions differ between two builds of the same config");

            // 非自由探索的世界沒有收集物
            var normal = new RaceConfig { TrackId = 0, Laps = 3, Seed = 9427, FreeRoam = false };
            RaceWorld.Build(normal, null, RaceQuality.High);
            if (RaceWorld.Collectibles.Count != 0 || RaceWorld.CollectibleTotal != 0 || RaceWorld.TryCollect(Vector3.zero) != 0) errs.Add("normal race world has collectibles");
            RaceWorld.Build(cfg, null, RaceQuality.High);   // 還原成自由探索世界給後面的測試
            track = RaceWorld.Track;

            Report("T12 collectibles", errs.Count == 0,
                "count=" + items.Count + " onLand=" + (items.Count - landBad) + " nearRoad=" + near + " minSeparation=" + minSep.ToString("0") + "m spread=" + (maxX - minX).ToString("0") + "x" + (maxZ - minZ).ToString("0") + "m deterministic=" + same +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        void Budget12()
        {
            // 自由探索多 30 個硬幣(各一個 draw call、約 100 個三角形):一幀最壞情況仍在預算內
            Transform root = RaceWorld.Root;
            var lod = root.GetComponent<RaceWorldLod>();
            var tr = RaceWorld.Track;
            var errs = new List<string>();
            int worstTris = 0, worstDraws = 0;
            var views = new List<Vector3>();
            for (int i = 0; i < tr.Samples.Count; i += 10) views.Add(tr.Samples[i].Pos + Vector3.up * 8f);
            views.Add(Vector3.zero);
            foreach (var v in views) { int t, d; lod.Estimate(v, out t, out d); worstTris = Mathf.Max(worstTris, t); worstDraws = Mathf.Max(worstDraws, d); }
            var mats = new HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterial != null) mats.Add(r.sharedMaterial);
            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            if (worstTris >= 150000) errs.Add("worst-frame triangles " + worstTris);
            if (worstDraws >= 200) errs.Add("worst-frame draw calls " + worstDraws);
            if (colliders >= 150) errs.Add("colliders " + colliders);
            if (mats.Count > 32) errs.Add("materials " + mats.Count + " > 32");
            Report("T12b freeroam budget", errs.Count == 0, "worstFrameTriangles~" + worstTris + " worstFrameDrawCalls~" + worstDraws + " colliders=" + colliders + " materials=" + mats.Count + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T14 造景

        void T14()
        {
            var errs = new List<string>();
            var d = RaceWorld.Decor;
            var tr = RaceWorld.Track;
            if (d == null) { Report("T14 decor", false, "RaceWorld.Decor is null"); return; }
            Transform pr = RaceWorld.Root.Find("TrackProps");
            int colliders = pr != null ? pr.GetComponentsInChildren<Collider>(true).Length : -1;
            int draws = 0, tris = 0;
            if (pr != null)
                foreach (var mf in pr.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) { draws++; tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3); }
            if (pr == null) errs.Add("no TrackProps root");
            if (colliders != 0) errs.Add("colliders under TrackProps = " + colliders);
            if (tris > 30000) errs.Add("decor triangles " + tris + " > 30000");
            if (draws > 30) errs.Add("decor draw calls " + draws + " > 30");

            // 足跡圓離路面邊緣、不在跳台 / 加速板上
            float minD = 999f; int bad = 0, onFeat = 0;
            for (int i = 0; i < d.Foot.Count; i++)
            {
                Vector4 f = d.Foot[i];
                float lat;
                float s = tr.Project(new Vector3(f.x, 0f, f.z), -1f, out lat);
                float dist = Mathf.Abs(lat) - tr.HalfWidthAt(s) - f.w;
                if (dist < minD) minD = dist;
                if (dist < (tid >= DefaultTracks.Handmade ? 5.5f : 6.4f)) bad++;   // 新賽道(M7)的彎更密,TrackProps 在極少數彎道內側會落在 5.9 m(仍在路緣 / 護欄之外)
                if (OnFeature(f.x, f.z)) onFeat++;
            }
            if (bad > 0) errs.Add(bad + " footprints closer than 6.4 m to the road edge (min " + minD.ToString("0.00") + ")");
            if (onFeat > 0) errs.Add(onFeat + " footprints on ramps / boost pads");
            if (d.Chevrons < 2) errs.Add("chevrons=" + d.Chevrons + " (want >= 2)");
            if (d.ChevronsInside > 0) errs.Add(d.ChevronsInside + " chevron signs on the inside of a bend");
            if (d.Arches < 1) errs.Add("balloon arches=" + d.Arches);
            else if (d.MinOverhead < 6f) errs.Add("balloon arch overhead clearance " + d.MinOverhead.ToString("0.0") + " m < 6");
            if (d.Stands < 1) errs.Add("grandstands=" + d.Stands);
            if (d.Decals < 40) errs.Add("road decal triangles=" + d.Decals);
            if (d.MinDecalMargin < 0f) errs.Add("road decal outside the road by " + (-d.MinDecalMargin).ToString("0.00") + " m");
            int theme = DefaultTracks.Get(tid).theme;
            if (theme == 0 && d.Flowers < 5) errs.Add("flower patches=" + d.Flowers);
            if (theme == 1 && d.Cacti < 10) errs.Add("cacti=" + d.Cacti);
            if (theme == 2 && d.Palms < 5) errs.Add("palms=" + d.Palms);
            if (theme == 2 && d.Birds < 4) errs.Add("birds=" + d.Birds);

            var kinds = new StringBuilder();
            foreach (var kv in d.Kinds) kinds.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            Report("T14 decor", errs.Count == 0,
                "triangles=" + tris + " drawCalls=" + draws + " colliders=" + colliders + " footprints=" + d.Foot.Count + " minRoadGap=" + minD.ToString("0.0") + "m" +
                " chevrons=" + d.Chevrons + " stands=" + d.Stands + " spectators=" + d.Spectators + " arches=" + d.Arches + " overheadMin=" + d.MinOverhead.ToString("0.0") + "m decalTris=" + d.Decals +
                " [" + kinds.ToString().Trim() + "]" + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T2

        void T2()
        {
            var errs = new List<string>();
            RaceTerrain terr = RaceWorld.Terrain;
            Transform terrainRoot = RaceWorld.Root.Find("Terrain");
            Physics.SyncTransforms();
            var rnd = new System.Random(20260101);
            int total = 0, ok = 0, missing = 0, tries = 0;
            float maxErr = 0f;
            while (total < 1000 && tries < 8000)
            {
                tries++;
                float x = (float)(rnd.NextDouble() * 848.0 - 424.0), z = (float)(rnd.NextDouble() * 848.0 - 424.0);
                float raw = terr.Raw(x, z);
                if (raw < 0.3f) continue;   // 海面下沒有碰撞體(整塊都在水下的區塊不建)
                total++;
                RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, 80f, z), Vector3.down, 160f);
                bool found = false;
                foreach (var h in hits)
                    if (h.collider is MeshCollider && h.collider.transform.IsChildOf(terrainRoot))
                    {
                        found = true;
                        float e = Mathf.Abs(h.point.y - RaceWorld.HeightAt(x, z));
                        maxErr = Mathf.Max(maxErr, e);
                        if (e < 0.05f) ok++;
                        break;
                    }
                if (!found) missing++;
            }
            if (total < 1000) errs.Add("only " + total + " land points");
            if (ok != total) errs.Add("collider/HeightAt mismatch " + ok + "/" + total + " missing=" + missing);

            // 水面上的高度 = 0,SurfaceAt = Water;NormalAt 單位長度
            int waterBad = 0, normBad = 0;
            for (int i = 0; i < 500; i++)
            {
                float x = (float)(rnd.NextDouble() * 860.0 - 430.0), z = (float)(rnd.NextDouble() * 860.0 - 430.0);
                float raw = terr.Raw(x, z);
                if (raw < 0f && (RaceWorld.SurfaceAt(x, z) != Surface.Water || Mathf.Abs(RaceWorld.HeightAt(x, z)) > 1e-4f)) waterBad++;
                Vector3 nrm = RaceWorld.NormalAt(x, z);
                if (float.IsNaN(nrm.y) || Mathf.Abs(nrm.magnitude - 1f) > 0.01f || nrm.y <= 0.2f) normBad++;
            }
            if (waterBad > 0) errs.Add(waterBad + " water points wrong");
            if (normBad > 2) errs.Add(normBad + " bad normals");   // M8:缺口深坑的兩端是近乎垂直的斷崖(深 22 m),500 點裡偶爾會踩到 1 個

            // 查詢不配置記憶體:20 萬次 HeightAt/SampleGround
            GroundHit gh; float sum = 0f;
            for (int i = 0; i < 1000; i++) { RaceWorld.SampleGround(new Vector3(i * 0.6f - 300f, 0f, i * 0.4f - 200f), out gh); }   // 暖機
            long m0 = Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < 200000; i++)
            {
                float x = (i % 700) * 1.2f - 420f, z = ((i / 700) % 700) * 1.2f - 420f;
                if (RaceWorld.SampleGround(new Vector3(x, 0f, z), out gh)) sum += gh.Height + (float)gh.Surface;
                sum += RaceWorld.HeightAt(x, z);
            }
            long allocd = Profiler.GetMonoUsedSizeLong() - m0;
            if (allocd > 2048) errs.Add("queries allocated " + allocd + " B");
            if (float.IsNaN(sum)) errs.Add("NaN in queries");

            Report("T2 query vs collider", errs.Count == 0,
                "points=" + total + " matched=" + ok + " maxErr=" + maxErr.ToString("0.0000") + "m waterBad=" + waterBad + " normalBad=" + normBad + " queryAlloc=" + allocd + "B/200k calls" +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T7

        void T7()
        {
            RaceTrack tr = track;
            float L = tr.Length;
            var errs = new List<string>();
            var cp = new RaceCheckpoints(tr, 1, false);
            const int K = 8;
            var gs = new Ghost[K]; var s = new float[K]; var sp = new float[K];
            var rnd = new System.Random(77);
            for (int k = 0; k < K; k++)
            {
                gs[k] = new Ghost { Id = k };
                s[k] = L - (6f + k * 8f);                     // 起跑線後方,每台差 8 m
                sp[k] = 22f + (float)rnd.NextDouble() * 6f;   // 22~28 m/s,中途會超車
                Place(gs[k], tr, s[k], 1);
                cp.Register(gs[k], k == 0);
            }
            cp.StartRace(0f);
            float t = 0f; int step = 0, orderBad = 0, firstFinishedStep = -1;
            var stand = cp.Standings;
            float tLimit = Mathf.Max(90f, L / 22f + 20f);   // 最慢的車 22 m/s 跑 1 圈 + 起跑格的距離;長賽道(M7)要更久
            while (t < tLimit)
            {
                t += Dt; step++;
                for (int k = 0; k < K; k++)
                {
                    if (cp.Get(k).Finished) continue;     // 完賽後停在原地
                    s[k] += sp[k] * Dt;
                    Place(gs[k], tr, s[k], 1);
                    gs[k].Vel = gs[k].Fwd * sp[k];
                }
                cp.Step(Dt, t);
                if (step % 5 == 0)   // 排序每 0.1 秒做一次,期間最多差 ~3 m,所以容許 4 m
                {
                    for (int i = 0; i + 1 < stand.Count; i++)
                    {
                        RacerProgress a = stand[i], b = stand[i + 1];
                        if (b.Finished && !a.Finished) orderBad++;
                        else if (a.Finished && b.Finished) { if (a.FinishTime > b.FinishTime + 1e-4f) orderBad++; }
                        else if (!a.Finished && !b.Finished && a.Total < b.Total - 4f) orderBad++;
                    }
                }
                if (firstFinishedStep < 0 && stand[0].Finished) { firstFinishedStep = step; break; }
            }
            if (orderBad > 0) errs.Add(orderBad + " ordering violations while racing");
            if (firstFinishedStep < 0) { errs.Add("nobody finished in " + tLimit.ToString("0") + " s"); }
            else
            {
                // 一台完賽、其他還在跑:讓所有車停在原地幾步,排序穩定後和「真實前進距離」比對
                for (int i = 0; i < 12; i++) { t += Dt; cp.Step(Dt, t); }
                int finishedCount = 0;
                for (int i = 0; i < stand.Count; i++) if (stand[i].Finished) finishedCount++;
                if (finishedCount != 1) errs.Add("expected exactly 1 finished, got " + finishedCount);
                if (!stand[0].Finished || stand[0].Rank != 1) errs.Add("finished racer not ranked first");
                for (int i = 1; i + 1 < stand.Count; i++)
                    if (stand[i].Total < stand[i + 1].Total - 0.01f) errs.Add("running order wrong at " + i);
                int expectLeader = -1; float best = float.MinValue;
                for (int k = 0; k < K; k++) { if (cp.Get(k).Finished) continue; if (s[k] > best) { best = s[k]; expectLeader = k; } }
                if (stand.Count > 1 && stand[1].Id != expectLeader) errs.Add("2nd place is " + stand[1].Id + " expected " + expectLeader);
                var ranks = new bool[K + 1];
                foreach (var pr in stand) { if (pr.Rank < 1 || pr.Rank > K || ranks[pr.Rank]) errs.Add("duplicate/invalid rank " + pr.Rank); else ranks[pr.Rank] = true; }
            }
            // 繼續跑到全部完賽,最後名次 = 完賽時間順序
            int guard = 0;
            int guardMax = Mathf.RoundToInt((L / 22f + 40f) / Dt);
            while (guard++ < guardMax)
            {
                bool all = true;
                for (int k = 0; k < K; k++) if (!cp.Get(k).Finished) { all = false; break; }
                if (all) break;
                t += Dt;
                for (int k = 0; k < K; k++)
                {
                    if (cp.Get(k).Finished) continue;
                    s[k] += sp[k] * Dt; Place(gs[k], tr, s[k], 1);
                }
                cp.Step(Dt, t);
            }
            for (int i = 0; i < 12; i++) { t += Dt; cp.Step(Dt, t); }
            bool allFin = true;
            for (int i = 0; i + 1 < stand.Count; i++)
            {
                if (!stand[i].Finished || !stand[i + 1].Finished) { allFin = false; break; }
                if (stand[i].FinishTime > stand[i + 1].FinishTime + 1e-4f) errs.Add("final order wrong at " + i);
            }
            if (!allFin) errs.Add("not everyone finished");
            Report("T7 standings", errs.Count == 0,
                "racers=" + K + " firstFinishAt=" + (firstFinishedStep * Dt).ToString("0.0") + "s orderViolations=" + orderBad + " leaderFinishTime=" + (stand.Count > 0 ? stand[0].FinishTime.ToString("0.00") : "-") +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T8

        void T8()
        {
            var errs = new List<string>();
            RaceTerrain terr = RaceWorld.Terrain;
            RaceTrack tr = track;

            // 島的邊緣是海
            int edgeBad = 0;
            for (int i = 0; i < RaceTerrain.N; i++)
            {
                float c = -RaceTerrain.Half + i * RaceTerrain.Cell;
                float e = RaceTerrain.Half;
                if (RaceWorld.SurfaceAt(c, -e) != Surface.Water || RaceWorld.SurfaceAt(c, e) != Surface.Water ||
                    RaceWorld.SurfaceAt(-e, c) != Surface.Water || RaceWorld.SurfaceAt(e, c) != Surface.Water) edgeBad++;
            }
            if (edgeBad > 0) errs.Add(edgeBad + " border points are not Water");
            int land = 0, water = 0, sand = 0, grass = 0, oob = 0;
            for (float z = -430f; z <= 430f; z += 8f)
                for (float x = -430f; x <= 430f; x += 8f)
                {
                    Surface sf = RaceWorld.SurfaceAt(x, z);
                    if (sf == Surface.Water) water++; else land++;
                    if (sf == Surface.Sand) sand++; else if (sf == Surface.Grass) grass++; else if (sf == Surface.OutOfBounds) oob++;
                }
            float landFrac = land / (float)(land + water);
            if (landFrac < 0.55f || landFrac > 0.92f) errs.Add("land fraction " + landFrac.ToString("0.00"));
            if ((sand < 20 && tr.Cliff < 0.5f) || grass < 500) errs.Add("surfaces sand=" + sand + " grass=" + grass);
            if (oob > 0) errs.Add(oob + " OutOfBounds land points (island should end in sea)");
            if (terr.MaxH < 8f) errs.Add("terrain too flat max=" + terr.MaxH.ToString("0.0"));

            // 樹:離路面邊緣 >= 6 m、不在彎道內側(視線)、不在海灘/水、碰撞樹 <= 60
            PropScatter.Result pr = RaceWorld.Props;
            int treeBad = 0, sightBad = 0, beachBad = 0;
            float minD = 999f;
            for (int i = 0; i < pr.TreePos.Count; i++)
            {
                Vector3 p = pr.TreePos[i];
                float lat;
                float sPos = tr.Project(new Vector3(p.x, 0f, p.z), -1f, out lat);
                float d = Mathf.Abs(lat) - tr.HalfWidthAt(sPos);
                minD = Mathf.Min(minD, d);
                if (d < 6f) treeBad++;
                float k = tr.MaxCurvature(sPos - 30f, sPos + 30f);
                if (d < 45f && Mathf.Abs(k) > 1f / 110f && lat * k > 0f) sightBad++;
                if (RaceWorld.SurfaceAt(p.x, p.z) != Surface.Grass || terr.Raw(p.x, p.z) < 1.2f) beachBad++;
            }
            if (pr.Trees < 60) errs.Add("only " + pr.Trees + " trees");
            if (treeBad > 0) errs.Add(treeBad + " trees closer than 6 m to the road");
            if (sightBad > 0) errs.Add(sightBad + " trees inside a bend's sight zone");
            if (beachBad > 0) errs.Add(beachBad + " trees on sand/water");
            if (pr.Colliders > 60) errs.Add("tree colliders " + pr.Colliders + " > 60");
            if (pr.Mountains < 30) errs.Add("mountains " + pr.Mountains);
            if (pr.Clouds < 3) errs.Add("clouds " + pr.Clouds);

            // LOD:視點在起跑格 → 近處 LOD0、遠處 LOD1;視點移到島角落 → 物件組被關掉
            var lod = RaceWorld.Root.GetComponent<RaceWorldLod>();
            lod.Apply(tr.Grid(0).position);
            int near0 = 0, far1 = 0;
            foreach (var c in lod.chunks) { if (c.Current == 0) near0++; else far1++; }
            int cellsOnA = 0; foreach (var c in lod.cells) if (c.On) cellsOnA++;
            lod.Apply(new Vector3(-RaceTerrain.Half + 10f, 5f, -RaceTerrain.Half + 10f));
            int cellsOnB = 0; foreach (var c in lod.cells) if (c.On) cellsOnB++;
            int lod0B = 0; foreach (var c in lod.chunks) if (c.Current == 0) lod0B++;
            lod.Apply(tr.Grid(0).position);
            if (near0 < 1 || far1 < 1) errs.Add("LOD at start: lod0=" + near0 + " lod1=" + far1);
            if (cellsOnB >= cellsOnA) errs.Add("culling did not turn cells off (" + cellsOnA + " -> " + cellsOnB + ")");
            // 碰撞永遠是 LOD0:每個區塊的 MeshCollider 網格是 2048 個三角形
            int colTrisBad = 0;
            foreach (var mc in RaceWorld.Root.Find("Terrain").GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh == null || mc.sharedMesh.GetIndexCount(0) / 3 != 2048) colTrisBad++;
            if (colTrisBad > 0) errs.Add(colTrisBad + " terrain colliders are not LOD0");

            // 低品質:全部 LOD1、樹約一半;之後恢復
            RaceWorld.Build(cfg, null, RaceQuality.Low);
            int lowTrees = RaceWorld.Props.Trees;
            var lodLow = RaceWorld.Root.GetComponent<RaceWorldLod>();
            lodLow.Apply(RaceWorld.Track.Grid(0).position);
            int lowLod0 = 0; foreach (var c in lodLow.chunks) if (c.Current == 0) lowLod0++;
            RaceWorld.Build(cfg, null, RaceQuality.High);
            track = RaceWorld.Track;
            int highTrees = RaceWorld.Props.Trees;
            if (lowLod0 != 0) errs.Add("Low quality still has " + lowLod0 + " LOD0 chunks");
            if (lowTrees > highTrees * 0.7f) errs.Add("Low trees " + lowTrees + " vs High " + highTrees);

            Report("T8 world", errs.Count == 0,
                "landFraction=" + landFrac.ToString("0.00") + " sand=" + sand + " grass=" + grass + " maxHeight=" + terr.MaxH.ToString("0.0") + "m roadSlopeProfile=" + terr.RoadMaxSlopeDeg.ToString("0.0") + "deg" +
                " trees=" + pr.Trees + " rocks=" + pr.Rocks + " minTreeRoadDist=" + minD.ToString("0.0") + "m treeColliders=" + pr.Colliders + " mountains=" + pr.Mountains + " clouds=" + pr.Clouds +
                " lodAtStart=" + near0 + "xLOD0/" + far1 + "xLOD1 cellsOn=" + cellsOnA + "->" + cellsOnB + "(corner, lod0=" + lod0B + ") lowTrees=" + lowTrees + "/high=" + highTrees +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ T9

        void T9()
        {
            RaceTrack tr = track;
            float L = tr.Length;
            var errs = new List<string>();
            var cp = new RaceCheckpoints(tr, 99, false);
            const int K = 8;
            var gs = new Ghost[K]; var s = new float[K]; var sp = new float[K]; var lat = new float[K]; var latV = new float[K];
            var lastProgress = new float[K]; var stuckFor = new float[K]; var worstStuck = new float[K];
            var rnd = new System.Random(31337);
            int resets = 0;
            cp.Reset += (id, r) => resets++;
            for (int k = 0; k < K; k++)
            {
                gs[k] = new Ghost { Id = k };
                s[k] = L - (6f + k * 8f);
                sp[k] = 20f + (float)rnd.NextDouble() * 6f;
                Place(gs[k], tr, s[k], 1);
                cp.Register(gs[k], k == 0);
                lastProgress[k] = cp.Get(k).Total;
            }
            cp.StartRace(0f);
            const int Steps = 15000;   // 300 s
            const int GcFrom = 5000, GcTo = 8000;
            long m0 = 0, m1 = 0;
            int nan = 0;
            float t = 0f;
            float totalTravel = 0f;
            for (int step = 0; step < Steps; step++)
            {
                if (step == GcFrom) m0 = Profiler.GetMonoUsedSizeLong();
                if (step == GcTo) m1 = Profiler.GetMonoUsedSizeLong();
                t += Dt;
                for (int k = 0; k < K; k++)
                {
                    // 橫向偏移:有限速度的隨機漫步,夾在路寬內(離邊緣 1.5 m)
                    latV[k] += ((float)rnd.NextDouble() - 0.5f) * 0.6f; latV[k] = Mathf.Clamp(latV[k], -2f, 2f);
                    TrackSample sm = tr.SampleAt(s[k]);
                    float lim = sm.Width * 0.5f - 1.5f;
                    lat[k] = Mathf.Clamp(lat[k] + latV[k] * Dt, -lim, lim);
                    s[k] += sp[k] * Dt;
                    sm = tr.SampleAt(s[k]);
                    Vector3 pos = sm.Pos + sm.Right * lat[k];
                    pos.y = tr.InGap(s[k]) ? sm.Pos.y : RaceWorld.HeightAt(pos.x, pos.z);   // 缺口上空沒有地面:幽靈用路面高度(= 在飛)
                    gs[k].Pos = pos; gs[k].Fwd = sm.Tangent; gs[k].Vel = sm.Tangent * sp[k];
                    if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) nan++;
                    totalTravel += sp[k] * Dt;
                }
                cp.Step(Dt, t);
                for (int k = 0; k < K; k++)
                {
                    RacerProgress p = cp.Get(k);
                    if (float.IsNaN(p.Total) || float.IsNaN(p.S)) nan++;
                    float prog = p.Total;
                    if (prog > lastProgress[k] + 0.01f) { lastProgress[k] = prog; stuckFor[k] = 0f; }
                    else { stuckFor[k] += Dt; if (stuckFor[k] > worstStuck[k]) worstStuck[k] = stuckFor[k]; }
                }
            }
            long gcDelta = m1 - m0;
            float worst = 0f; for (int k = 0; k < K; k++) worst = Mathf.Max(worst, worstStuck[k]);
            if (nan > 0) errs.Add(nan + " NaN values");
            if (worst > 10f) errs.Add("a racer made no progress for " + worst.ToString("0.0") + " s");
            if (resets > 0) errs.Add(resets + " unexpected resets on the road");
            if (gcDelta > 2048) errs.Add("GC allocation " + gcDelta + " B over " + (GcTo - GcFrom) + " steps x " + K + " racers");
            int minLap = int.MaxValue; for (int k = 0; k < K; k++) minLap = Mathf.Min(minLap, cp.Get(k).Lap);
            int needLaps = Mathf.FloorToInt((20f * 300f - 70f) / L);
            if (minLap < needLaps) errs.Add("laps too few: " + minLap + " (want >= " + needLaps + ")");
            Report("T9 soak", errs.Count == 0,
                "sim=" + (Steps * Dt).ToString("0") + "s racers=" + K + " travelled=" + (totalTravel / 1000f).ToString("0.0") + "km minLaps=" + minLap + " longestNoProgress=" + worst.ToString("0.00") + "s resets=" + resets + " nan=" + nan +
                " gcAlloc=" + gcDelta + "B/" + ((GcTo - GcFrom) * K) + " racer-steps" +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ TG 滑翔跳台

        void TG()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            FeatureSet fs = RaceWorld.Features;
            int expect = 0;
            foreach (var f in DefaultTracks.Get(tid).features) if (f.kind == (int)TrackFeatureKind.GliderRamp && f.path < 0) expect++;
            int[] want = { 1, 2, 1 };
            int wantN = tid >= DefaultTracks.Handmade ? DefaultTracks.Get(tid).gaps.Count : want[Mathf.Clamp(tid, 0, 2)];   // M7:新賽道每個缺口一個起飛跳台
            if (expect != wantN) errs.Add("track data has " + expect + " glider ramps, want " + wantN);
            int found = fs != null ? fs.GliderCount : 0;
            if (!cfg.Reverse && found != expect) errs.Add("gliders=" + found + " want " + expect);
            if (cfg.Reverse && (found < 1 || found > expect)) errs.Add("reverse gliders=" + found + " want 1.." + expect);
            if (RaceWorld.GliderLips.Count != found) errs.Add("GliderLips=" + RaceWorld.GliderLips.Count + " vs " + found);

            int launches = 0;
            System.Action<int, int> onLaunch = (k, r) => launches++;
            RaceWorld.GliderLaunched += onLaunch;
            var info = new StringBuilder();
            int checkedCount = 0;
            for (int i = 0; fs != null && i < fs.Count; i++)
            {
                FeatureSet.Inst f = fs.Items[i];
                if (!f.IsGlider || f.Path >= 0) continue;
                checkedCount++;
                string gt = "glider@s" + f.S.ToString("0") + " ";
                // 尺寸:寬 10、長 14、坡 22 度、前緣高 5.66 m
                if (Mathf.Abs(f.HW * 2f - 10f) > 0.01f || Mathf.Abs(f.HL * 2f - 14f) > 0.01f) errs.Add(gt + "size " + (f.HW * 2f) + "x" + (f.HL * 2f));
                float pitch = Mathf.Atan(f.TanP) * Mathf.Rad2Deg;
                if (Mathf.Abs(pitch - 22f) > 0.05f) errs.Add(gt + "pitch " + pitch.ToString("0.00"));
                float rise = f.TanP * 2f * f.HL;
                if (rise < 5.5f || rise > 5.8f) errs.Add(gt + "rise " + rise.ToString("0.00"));
                // 高度連續:沿前進方向每 0.5 m,偏移 = tan * (du + HL) 單調上升;前緣之後馬上回到地面
                float prev = -1f;
                for (float du = -f.HL + 0.01f; du <= f.HL - 0.01f; du += 0.5f)
                {
                    float x = f.Cx + f.Fx * du, z = f.Cz + f.Fz * du;
                    float off = RaceWorld.GroundHeightAt(x, z) - RaceWorld.HeightAt(x, z);
                    if (Mathf.Abs(off - f.TanP * (du + f.HL)) > 0.01f) errs.Add(gt + "height " + off.ToString("0.00") + " at du=" + du.ToString("0.0"));
                    if (off < prev - 1e-4f) errs.Add(gt + "not monotone");
                    prev = off;
                }
                {
                    float x = f.Cx + f.Fx * (f.HL + 0.3f), z = f.Cz + f.Fz * (f.HL + 0.3f);
                    if (Mathf.Abs(RaceWorld.GroundHeightAt(x, z) - RaceWorld.HeightAt(x, z)) > 1e-4f) errs.Add(gt + "ground does not drop right after the lip");
                }
                // 前緣資料
                GliderLip lip = default; bool haveLip = false;
                foreach (var l in RaceWorld.GliderLips) if (l.Index == i) { lip = l; haveLip = true; }
                if (!haveLip) { errs.Add(gt + "no GliderLip entry"); continue; }
                if (Mathf.Abs(lip.Rise - rise) > 0.02f || Mathf.Abs(lip.HalfWidth - 5f) > 0.01f) errs.Add(gt + "GliderLip data");
                // 落點:前緣後 50~90 m 的中心線是路、高度合理、方向合理;前方 95 m 沒有別的跳台
                string why;
                if (!TrackVariants.GliderLandingOk(tr, f.S, f.HL, out why)) errs.Add(gt + why);
                float lipS = f.S + f.HL, lipGround = RaceWorld.HeightAt(lip.Center.x, lip.Center.z);
                float dA = 50f, dB = 90f;
                if (f.GapRamp) { float gl = 0f; foreach (var gg in tr.Gaps) if (Mathf.Abs(gg.S0 - lipS) < 3f) gl = gg.Length; dA = gl + 8f; dB = gl + 60f; }   // M8:缺口的跳台,落點在缺口之後
                for (float d = dA; d <= dB; d += 2f)
                {
                    Vector3 p = tr.PointAt(lipS + d);
                    Surface sf = RaceWorld.SurfaceAt(p.x, p.z);
                    if (sf != Surface.Road && sf != Surface.BoostPad) { errs.Add(gt + "landing d=" + d + " surface " + sf); break; }
                    float h = RaceWorld.HeightAt(p.x, p.z);
                    if (h > lipGround + 4f || h < lipGround - 9f) { errs.Add(gt + "landing d=" + d + " ground " + (h - lipGround).ToString("0.0") + " m vs lip"); break; }
                }
                for (int j = 0; j < fs.Count; j++)
                {
                    if (j == i || fs.Items[j].Path >= 0 || !fs.Items[j].IsRamp) continue;
                    float ahead = tr.Wrap(fs.Items[j].S - lipS);
                    if (ahead <= TrackVariants.GliderZone) errs.Add(gt + "another ramp " + ahead.ToString("0") + " m ahead of the lip");
                }
                // 起飛觸發:從前緣前 0.4 m 跑到前緣後 0.4 m = Glide 一次(用獨立的 kartId);倒著過、從旁邊繞過、傳送穿過都不觸發
                Vector3 fwd = lip.Forward, right = new Vector3(fwd.z, 0f, -fwd.x);
                Vector3 before = lip.Center - fwd * 0.4f, after = lip.Center + fwd * 0.4f;
                int kid = 700 + i;
                RaceWorld.StepPickups(kid, before + right * 40f, 0f);   // 先註冊一個離很遠的位置
                PickupResult r0 = RaceWorld.StepPickups(kid, before, 0f);
                PickupResult r1 = RaceWorld.StepPickups(kid, after, 0f);
                PickupResult r2 = RaceWorld.StepPickups(kid, after + fwd * 0.8f, 0f);
                PickupResult r3 = RaceWorld.StepPickups(kid, before, 0f);   // 倒著回去
                PickupResult r4 = RaceWorld.StepPickups(kid, after, 0f);    // 又一次往前 -> 再觸發一次
                if (r0.Glide || !r1.Glide || r1.RampIndex != i || r2.Glide || r3.Glide || !r4.Glide) errs.Add(gt + "lip trigger " + r0.Glide + "/" + r1.Glide + "/" + r2.Glide + "/" + r3.Glide + "/" + r4.Glide);
                int kid2 = 800 + i;
                RaceWorld.StepPickups(kid2, before + right * (f.HW + 3f), 0f);
                PickupResult r5 = RaceWorld.StepPickups(kid2, after + right * (f.HW + 3f), 0f);   // 從跳台旁邊過
                if (r5.Glide) errs.Add(gt + "triggered beside the ramp");
                int kid3 = 900 + i;
                RaceWorld.StepPickups(kid3, lip.Center - fwd * 60f, 0f);
                PickupResult r6 = RaceWorld.StepPickups(kid3, lip.Center + fwd * 60f, 0f);       // 傳送(重生)穿過去
                if (r6.Glide) errs.Add(gt + "triggered by a teleport");
                info.Append(gt).Append("rise=").Append(rise.ToString("0.00")).Append("m ");
            }
            RaceWorld.GliderLaunched -= onLaunch;
            RaceWorld.ResetRaceCoins();
            if (launches != checkedCount * 2) errs.Add("GliderLaunched fired " + launches + " times, want " + checkedCount * 2);
            Report("TG glider ramps", errs.Count == 0, "gliders=" + found + "/" + expect + " " + info + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ TC 金幣

        // 弧線金幣離「地面」的高度:缺口上空以路面高度為地面
        static float ArcAbove(RaceTrack tr, RaceCoin c)
        {
            float lat; float s = tr.Project(c.Pos, -1f, out lat);
            float gy = tr.InGap(s, 2f, 2f) ? tr.SampleAt(s).Pos.y : RaceWorld.HeightAt(c.Pos.x, c.Pos.z);
            return c.Pos.y - gy;
        }

        void TC()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            RaceWorld.ResetRaceCoins();
            var coins = RaceWorld.RaceCoins;
            int n = coins.Count;
            FeatureSet fs = RaceWorld.Features;
            int gliders = fs != null ? fs.GliderCount : 0;
            if (n < 40 || n > 60) errs.Add("coins per lap " + n + " not in 40..60");
            int arcs = 0, ground = 0;
            float minGap = 1e9f;
            for (int i = 0; i < n; i++)
            {
                RaceCoin c = coins[i];
                if (c.Id != i) errs.Add("coin id " + i);
                float lat; float s = tr.Project(c.Pos, -1f, out lat);
                float hw = tr.HalfWidthAt(s);
                bool overGap = c.OnArc && tr.InGap(s, 2f, 2f);   // 缺口上空:弧線以路面高度為基準,下面是水
                float above = c.Pos.y - (overGap ? tr.SampleAt(s).Pos.y : RaceWorld.HeightAt(c.Pos.x, c.Pos.z));
                Surface sf = RaceWorld.SurfaceAt(c.Pos.x, c.Pos.z);
                if (c.OnArc)
                {
                    arcs++;
                    if (Mathf.Abs(lat) > 0.6f) errs.Add("arc coin " + i + " is " + lat.ToString("0.0") + " m off the center line");
                    if (above < 1.25f || above > 17f) errs.Add("arc coin " + i + " height " + above.ToString("0.0"));
                }
                else
                {
                    ground++;
                    if (Mathf.Abs(lat) > hw - 1.5f) errs.Add("coin " + i + " lateral " + lat.ToString("0.0") + " (road half width " + hw.ToString("0.0") + ")");
                    if (Mathf.Abs(above - 1.3f) > 0.02f) errs.Add("coin " + i + " height above ground " + above.ToString("0.00"));
                    if (OnRamp(c.Pos.x, c.Pos.z, 1f)) errs.Add("coin " + i + " is on a ramp");
                }
                if (sf != Surface.Road && sf != Surface.BoostPad && !(overGap && sf == Surface.Water)) errs.Add("coin " + i + " surface " + sf);
                if (c.Taken) errs.Add("coin " + i + " starts taken");
                for (int j = i + 1; j < n; j++) minGap = Mathf.Min(minGap, (c.Pos - coins[j].Pos).magnitude);
                if (errs.Count > 12) break;
            }
            int wantArcs = 0;
            for (int i = 0; fs != null && i < fs.Count; i++) if (fs.Items[i].IsGlider && fs.Items[i].Path < 0) wantArcs += fs.Items[i].GapRamp ? CoinLayout.ArcPer : CoinLayout.OldArcPer;
            if (arcs != wantArcs) errs.Add("arc coins " + arcs + " want " + wantArcs);
            if (minGap < 2.5f) errs.Add("two coins only " + minGap.ToString("0.0") + " m apart");
            // 弧線高度(M8):沿理想滑翔路徑,第 1 枚在前緣之上(>= 8 m 離路面,跳台 5.7 m + 上升),往後下降
            for (int g = 0; g < gliders; g++)
            {
                int first = -1;
                for (int i = 0; i < n; i++) if (coins[i].OnArc && coins[i].Group == 1000 + g) { first = i; break; }
                if (first < 0) { errs.Add("no arc for glider " + g); continue; }
                int cnt = 0;
                for (int i = first; i < n && coins[i].OnArc && coins[i].Group == 1000 + g; i++) cnt++;
                int last = first + cnt - 1;
                float hFirst = ArcAbove(tr, coins[first]);
                float hLast = ArcAbove(tr, coins[last]);
                bool gapArc = cnt == CoinLayout.ArcPer;
                if (gapArc ? (hFirst < 7f || hLast < 1.25f || hLast > 17f) : (hLast > 2.0f || hFirst < 4f)) errs.Add("arc " + g + " shape: first " + hFirst.ToString("0.0") + " last " + hLast.ToString("0.0"));
            }

            // 撿取 / 重生 / 事件(用自己的時鐘,遠大於 Update 用的 Time.time,所以不會被 Update 提早重生)
            float t0 = Time.time + 5000f;
            RaceCoin target = n > 0 ? coins[n / 2] : null;
            int events = 0, evKart = -1;
            System.Action<int, int, Vector3> onPick = (k, i, p) => { events++; evKart = k; };
            RaceWorld.RaceCoinPicked += onPick;
            if (target != null)
            {
                Vector3 kartPos = target.Pos - Vector3.up * (target.OnArc ? 0f : CoinLayout.Lift);
                float tl; float ts = tr.Project(target.Pos, -1f, out tl);
                Vector3 side = tr.SampleAt(ts).Right; side.y = 0f; side.Normalize();      // 垂直於賽道的方向(沿賽道的鄰居金幣至少相隔 4 m)
                Vector3 far = new Vector3(420f, 0f, 420f);                                  // 地圖角落的海上,附近沒有金幣
                RaceWorld.StepPickups(55, far, t0 - 1f);
                PickupResult a = RaceWorld.StepPickups(55, kartPos + side * 2.0f, t0);              // 離金幣 2 m:撿到
                bool tookBefore = target.Taken;
                float respawnAt = target.RespawnAt;
                RaceWorld.StepPickups(55, kartPos, t0 + 1f);                                         // 同一個位置再來:已經撿走,不會再撿(重生時間不變)
                bool notAgain = target.Taken && Mathf.Abs(target.RespawnAt - respawnAt) < 1e-4f;
                PickupResult c4 = RaceWorld.StepPickups(55, far, t0 + 4.9f);
                bool stillTaken = target.Taken;
                PickupResult d = RaceWorld.StepPickups(55, far, t0 + 5.1f);
                bool respawned = !target.Taken;
                PickupResult e = RaceWorld.StepPickups(55, target.Pos + Vector3.up * 4.0f, t0 + 5.2f);       // 離金幣中心高度差 4 m > 3.5 m:太高撿不到
                PickupResult f2 = RaceWorld.StepPickups(55, kartPos + side * 3.6f, t0 + 5.3f);            // 水平 3.6 m:太遠
                PickupResult g2 = RaceWorld.StepPickups(55, kartPos + side * 3.0f, t0 + 5.4f);            // 水平 3.0 m:撿到
                if (a.Coins < 1 || !tookBefore) errs.Add("pickup at 2 m failed");
                if (!notAgain) errs.Add("picked a taken coin again");
                if (!stillTaken || c4.Coins != 0) errs.Add("respawned too early (4.9 s)");
                if (!respawned || d.Coins != 0) errs.Add("did not respawn after 5 s");
                if (e.Coins != 0) errs.Add("picked a coin 4 m above");
                if (f2.Coins != 0) errs.Add("picked a coin 3.6 m away");
                if (g2.Coins < 1) errs.Add("pickup at 3.0 m failed");
                if (events < 2 || evKart != 55) errs.Add("RaceCoinPicked events=" + events + " kart=" + evKart);
                // 全部撿一遍再重置
                int total = 0;
                for (int i = 0; i < n; i++) total += RaceWorld.StepPickups(56, coins[i].Pos - Vector3.up * 0.5f, t0 + 20f).Coins;
                if (total < n - 1) errs.Add("collected only " + total + "/" + n + " walking over every coin");
                RaceWorld.ResetRaceCoins();
                int left = 0; for (int i = 0; i < n; i++) if (coins[i].Taken) left++;
                if (left != 0) errs.Add("ResetRaceCoins left " + left + " taken");
            }
            RaceWorld.RaceCoinPicked -= onPick;

            // 不配置記憶體(先熱身一次再量)
            Vector3 farPos = new Vector3(300f, 0f, 300f);
            RaceWorld.StepPickups(57, farPos, t0 + 30f);
            long m0 = Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < 2000; i++) RaceWorld.StepPickups(57, farPos + new Vector3(i * 0.01f, 0f, 0f), t0 + 30f + i * 0.02f);
            long gc = Profiler.GetMonoUsedSizeLong() - m0;
            if (gc > 4096) errs.Add("StepPickups allocated " + gc + " B over 2000 calls");

            // 網格:1 個 Renderer、共用材質、沒有碰撞體
            Transform rc = RaceWorld.Root.Find("RaceCoins");
            int rend = rc != null ? rc.GetComponentsInChildren<Renderer>(true).Length : 0;
            if (rend != 1) errs.Add("RaceCoins renderers=" + rend);
            if (rc != null && rc.GetComponent<Collider>() != null) errs.Add("RaceCoins has a collider");
            Material gold = RaceArt.Mat(RacePalette.Gold, 0.55f);
            MeshRenderer mr = rc != null ? rc.GetComponent<MeshRenderer>() : null;
            if (mr != null && mr.sharedMaterial != gold) errs.Add("coin material is not the shared RaceArt.Mat");
            MeshFilter mfl = rc != null ? rc.GetComponent<MeshFilter>() : null;
            Report("TC coins", errs.Count == 0, "coins=" + n + " ground=" + ground + " arcs=" + arcs + " minGap=" + minGap.ToString("0.0") + "m tris=" + (mfl != null ? (int)(mfl.sharedMesh.GetIndexCount(0) / 3) : 0) + " gcAlloc=" + gc + "B" + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ TV 鏡像 / 反向的幾何

        void TV()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            TrackDef bd = DefaultTracks.Get(tid);
            RaceTrack bt = RaceTrack.FromDef(bd);          // 原版(純資料)
            bool M = cfg.Mirror, R = cfg.Reverse;
            float L = tr.Length;
            if (Mathf.Abs(L - bt.Length) > 0.3f) errs.Add("length " + L.ToString("0.0") + " vs base " + bt.Length.ToString("0.0"));
            // 缺口附近的門會依缺口位置排除 / 加上,反向後數量可能差一兩個
            int cpTol = bt.Gaps.Count > 0 ? 2 : 0;
            if (Mathf.Abs(tr.CheckpointCount - bt.CheckpointCount) > cpTol) errs.Add("checkpoints " + tr.CheckpointCount + " vs " + bt.CheckpointCount);
            if (tr.Gaps.Count != bt.Gaps.Count) errs.Add("gaps " + tr.Gaps.Count + " vs base " + bt.Gaps.Count);
            for (int gi = 0; gi < tr.Gaps.Count && gi < bt.Gaps.Count; gi++)
            {
                // 反向:缺口前緣 = L - 原本的落地邊緣(順序也反過來)
                GapInfo vg = tr.Gaps[gi], bg = bt.Gaps[gi];   // 反向後 TrackDef.gaps 的順序不變(只換 s0 / s1)
                float es0 = R ? L - bg.S1 : bg.S0;
                if (Mathf.Abs(vg.S0 - es0) > 0.5f || Mathf.Abs(vg.Length - bg.Length) > 0.5f) errs.Add("gap " + gi + " moved: s0=" + vg.S0.ToString("0.0") + " want " + es0.ToString("0.0"));
            }
            if (RaceWorld.IsMirror != M || RaceWorld.IsReverse != R) errs.Add("RaceWorld.IsMirror/IsReverse wrong");

            // 中心線:變體 s 處的點 = 原版 (R ? L - s : s) 處的點(鏡像再把 x 取負)
            float maxPos = 0f, minDot = 1f;
            for (float s = 0f; s < L; s += 23f)
            {
                float sb = R ? L - s : s;
                TrackSample a = tr.SampleAt(s), b = bt.SampleAt(sb);
                Vector3 bp = b.Pos, bt2 = b.Tangent;
                if (M) { bp.x = -bp.x; bt2.x = -bt2.x; }
                if (R) bt2 = -bt2;
                maxPos = Mathf.Max(maxPos, Mathf.Sqrt((a.Pos.x - bp.x) * (a.Pos.x - bp.x) + (a.Pos.z - bp.z) * (a.Pos.z - bp.z)));
                minDot = Mathf.Min(minDot, a.Tangent.x * bt2.x + a.Tangent.z * bt2.z);
                if (Mathf.Abs(a.Width - b.Width) > 0.1f) { errs.Add("width differs at s=" + s.ToString("0")); break; }
            }
            if (maxPos > (R ? 0.6f : 0.05f)) errs.Add("centerline differs from the " + (M ? "mirrored " : "") + (R ? "reversed " : "") + "base by " + maxPos.ToString("0.000") + " m");
            if (minDot < 0.99f) errs.Add("tangent mismatch (min dot " + minDot.ToString("0.000") + ")");

            // 起跑線在同一個位置;起跑格在線「後面」(行進方向的反側)
            Vector3 p0 = tr.SampleAt(0f).Pos, b0 = bt.SampleAt(0f).Pos; if (M) b0.x = -b0.x;
            if (Mathf.Abs(p0.x - b0.x) + Mathf.Abs(p0.z - b0.z) > 0.1f) errs.Add("start line moved");
            Vector3 tan0 = tr.SampleAt(0f).Tangent;
            for (int slot = 0; slot < 8; slot++)
            {
                Pose gp = tr.Grid(slot);
                Vector3 rel = gp.position - p0;
                float back = -(rel.x * tan0.x + rel.z * tan0.z);
                if (back < 5f || back > 36f) errs.Add("grid " + slot + " is " + back.ToString("0.0") + " m behind the line");
                if (RaceWorld.SurfaceAt(gp.position.x, gp.position.z) != Surface.Road) errs.Add("grid " + slot + " not on the road");
                if (Vector3.Dot(gp.rotation * Vector3.forward, tan0) < 0.9f) errs.Add("grid " + slot + " not facing forward");
                if (errs.Count > 10) break;
            }

            // 檢查點:方向沿著變體的行進方向,S 遞增,門中心在路上
            for (int i = 0; i < tr.CheckpointCount; i++)
            {
                Checkpoint c = tr.Checkpoints[i];
                TrackSample sm = tr.SampleAt(c.S);
                if (i > 0 && c.S <= tr.Checkpoints[i - 1].S) errs.Add("checkpoint S not increasing at " + i);
                if (Vector3.Dot(c.Forward, sm.Tangent) < 0.99f) errs.Add("checkpoint " + i + " faces the wrong way");
                if (RaceWorld.SurfaceAt(c.Center.x, c.Center.z) != Surface.Road && !OnFeature(c.Center.x, c.Center.z) && !tr.InGap(c.S)) errs.Add("checkpoint " + i + " center not on the road");
            }

            // 地形:鏡像 = 同一條(反向後的)路、同一個種子的高度圖左右翻轉
            float maxDh = 0f; int bad = 0, cnt = 0;
            if (M)
            {
                TrackDef nd = R ? TrackVariants.Apply(bd, false, true) : bd;
                RaceTrack ndTrack = RaceTrack.FromDef(nd);
                RaceTerrain bTerr = RaceTerrain.Generate(ndTrack, bd.seed, false);
                TrackGapBuilder.CarvePits(ndTrack, bTerr);   // 世界裡的地形有挖深坑,比對用的也要挖
                RaceTerrain vTerr = RaceWorld.Terrain;
                const int N = RaceTerrain.N;
                for (int j = 0; j < N; j += 3)
                    for (int i = 0; i < N; i += 3)
                    {
                        float a = vTerr.H[j * N + i], b = bTerr.H[j * N + (N - 1 - i)];
                        float dh = Mathf.Abs(a - b);
                        maxDh = Mathf.Max(maxDh, dh); cnt++;
                        if (dh > 0.05f) bad++;
                    }
                if (bad > cnt / 200) errs.Add("terrain is not the mirror of the base: " + bad + "/" + cnt + " cells differ > 5 cm (max " + maxDh.ToString("0.00") + ")");
            }

            // 特徵 / 地標:鏡像不改數量;反向後只會移動 / 拿掉
            if (!R && tr.Features.Count != bd.features.Count) errs.Add("feature count " + tr.Features.Count + " vs " + bd.features.Count);
            if (R && tr.Features.Count > bd.features.Count) errs.Add("reverse has more features than the base");
            if (tr.Landmarks.Count != bd.landmarks.Count) errs.Add("landmark count " + tr.Landmarks.Count + " vs " + bd.landmarks.Count);

            // 捷徑(Island):反向時入口 / 出口互換位置
            string scInfo = "";
            if (bt.Shortcuts.Count > 0)
            {
                if (tr.Shortcuts.Count != bt.Shortcuts.Count) errs.Add("shortcut count " + tr.Shortcuts.Count);
                else
                {
                    ShortcutPath a = tr.Shortcuts[0], b = bt.Shortcuts[0];
                    Vector3 be = R ? b.Exit.Center : b.Entry.Center, bx = R ? b.Entry.Center : b.Exit.Center;
                    if (M) { be.x = -be.x; bx.x = -bx.x; }
                    float de = Mathf.Sqrt((a.Entry.Center.x - be.x) * (a.Entry.Center.x - be.x) + (a.Entry.Center.z - be.z) * (a.Entry.Center.z - be.z));
                    float dx = Mathf.Sqrt((a.Exit.Center.x - bx.x) * (a.Exit.Center.x - bx.x) + (a.Exit.Center.z - bx.z) * (a.Exit.Center.z - bx.z));
                    if (de > 3f || dx > 3f) errs.Add("shortcut gates moved: entry " + de.ToString("0.0") + " m exit " + dx.ToString("0.0") + " m");
                    if (Mathf.Abs(a.Length - b.Length) > 1f) errs.Add("shortcut length " + a.Length.ToString("0.0") + " vs " + b.Length.ToString("0.0"));
                    scInfo = " shortcutGateMove=" + de.ToString("0.0") + "/" + dx.ToString("0.0") + "m";
                }
            }
            Report("TV variant geometry", errs.Count == 0,
                "mirror=" + M + " reverse=" + R + " length=" + L.ToString("0.0") + " maxCenterlineErr=" + maxPos.ToString("0.000") + "m minTangentDot=" + minDot.ToString("0.000") +
                (M ? " terrainMaxDiff=" + maxDh.ToString("0.000") + "m(" + bad + "/" + cnt + " cells > 5cm)" : "") + " features=" + tr.Features.Count + "/" + bd.features.Count + scInfo +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ TGEN 產生器(只有 3..7 的原版跑一次)

        static ulong NodeHash(TrackDef d)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < d.nodes.Count; i++) { h = HF(h, d.nodes[i].x); h = HF(h, d.nodes[i].z); }
            for (int i = 0; i < d.features.Count; i++) { if (d.features[i].path >= 0) continue; h = HF(h, d.features[i].s); h = H(h, d.features[i].kind); }   // 捷徑上的加速板是產生器之後才加的(DefaultTracks.AddPinnedShortcuts)
            for (int i = 0; i < d.gaps.Count; i++) { h = HF(h, d.gaps[i].s0); h = HF(h, d.gaps[i].s1); }
            for (int i = 0; i < d.landmarks.Count; i++) { h = HF(h, d.landmarks[i].s); h = HF(h, d.landmarks[i].lateral); }
            return h;
        }

        // 線段 (a,b) 與 (c,d)(xz 平面)是否相交(不含共用端點的接觸)
        static bool SegCross(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float d1 = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            float d2 = (b.x - a.x) * (d.z - a.z) - (b.z - a.z) * (d.x - a.x);
            float d3 = (d.x - c.x) * (a.z - c.z) - (d.z - c.z) * (a.x - c.x);
            float d4 = (d.x - c.x) * (b.z - c.z) - (d.z - c.z) * (b.x - c.x);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        void TGEN()
        {
            var errs = new List<string>();
            TrackDef d = DefaultTracks.Get(tid);
            TrackGenParams p = DefaultTracks.GenParams(tid);
            RaceTrack tr = RaceTrack.FromDef(d);
            string v = d.Validate();
            if (v != null) errs.Add("Validate: " + v);

            // 同參數同結果(再產生一次,不走快取)
            TrackDef d2 = TrackGen.Generate(p);
            if (NodeHash(d) != NodeHash(d2)) errs.Add("TrackGen is not deterministic");
            int attempt = TrackGen.LastAttempt;
            if (attempt != p.Attempt) errs.Add("pinned attempt " + p.Attempt + " but TrackGen needed attempt " + attempt + " (update DefaultTracks.GenParams)");

            // 長度 / 封閉 / 最小半徑
            float L = tr.Length;
            if (L < 2500f || L > 4000f) errs.Add("length " + L.ToString("0") + " not in 2500..4000");
            if (L < p.Length * 0.9f || L > p.Length * 1.12f) errs.Add("length " + L.ToString("0") + " vs target " + p.Length.ToString("0"));
            int n = tr.Samples.Count;
            float closeGap = (tr.Samples[n - 1].Pos - tr.Samples[0].Pos).magnitude;
            if (!tr.Closed || closeGap > tr.SampleStep * 1.5f) errs.Add("loop not closed (gap " + closeGap.ToString("0.0") + " m)");
            if (tr.MinRadius < 22f) errs.Add("min radius " + tr.MinRadius.ToString("0.0") + " < 22");

            // 不自交:相隔 > 3 個取樣的折線線段不能相交;弧長 > 100 m 的兩點中心線相距 >= 50 m
            int crossings = 0;
            for (int i = 0; i < n; i += 2)
            {
                Vector3 a = tr.Samples[i].Pos, b = tr.Samples[(i + 2) % n].Pos;
                for (int j = i + 6; j < n; j += 2)
                {
                    if (i == 0 && j + 2 >= n) continue;
                    if (SegCross(a, b, tr.Samples[j].Pos, tr.Samples[(j + 2) % n].Pos)) crossings++;
                }
            }
            if (crossings > 0) errs.Add("centerline self-intersects " + crossings + " times");
            float minClear = float.MaxValue;
            for (int i = 0; i < n; i += 2)
                for (int j = i + 1; j < n; j += 2)
                {
                    float arc = (j - i) * tr.SampleStep; arc = Mathf.Min(arc, L - arc);
                    if (arc < 100f) continue;
                    Vector3 q = tr.Samples[i].Pos - tr.Samples[j].Pos; q.y = 0f;
                    minClear = Mathf.Min(minClear, q.magnitude);
                }
            if (minClear < TrackGen.MinClear - 0.5f) errs.Add("self clearance " + minClear.ToString("0.0") + " < " + TrackGen.MinClear);

            // 在島上:每個取樣點的 m 值 <= 0.78
            float maxM = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 q = tr.Samples[i].Pos;
                float a = q.x / 432f, b = q.z / 432f;
                maxM = Mathf.Max(maxM, Mathf.Sqrt(Mathf.Sqrt(a * a * a * a + b * b * b * b)));
            }
            if (maxM > 0.78f) errs.Add("track leaves the island region (m=" + maxM.ToString("0.00") + ")");

            // 缺口:數量、長度 70~112 m(M8)、離起跑線 >= 130 m、跳台(locked)在 s0 - 7
            if (d.gaps.Count != p.Gaps || tr.Gaps.Count != p.Gaps) errs.Add("gaps " + d.gaps.Count + " want " + p.Gaps);
            var gapInfo = new StringBuilder();
            foreach (var g in tr.Gaps)
            {
                if (g.Length < 70f || g.Length > 112f) errs.Add("gap length " + g.Length.ToString("0.0") + " not in 70..112");
                float toStart = Mathf.Min(g.S0, L - g.S1);
                if (toStart < 130f) errs.Add("gap only " + toStart.ToString("0") + " m from the start line");
                bool ramp = false;
                foreach (var f in d.features) if (f.locked && f.kind == (int)TrackFeatureKind.GliderRamp && Mathf.Abs(f.s - (g.S0 - GapInfo.RampLength * 0.5f)) < 0.01f) ramp = true;
                if (!ramp) errs.Add("gap " + g.Index + " has no locked glider ramp at s0 - 7");
                if (Mathf.Abs(tr.MaxCurvature(g.RampBase - 60f, g.S1 + 70f)) > 1f / 800f) errs.Add("gap " + g.Index + " approach / landing not straight");
                gapInfo.Append(" gap").Append(g.Index).Append("=").Append(g.S0.ToString("0")).Append("..").Append(g.S1.ToString("0")).Append("(").Append(g.Length.ToString("0")).Append("m)");
            }

            // theme 色票:3..7 各自的色票(不是島嶼那套),名稱
            if (d.theme != p.Theme || (d.theme > 0 && TrackTheme.Get(d.theme) == TrackTheme.Get(0))) errs.Add("theme " + d.theme + " has no palette");
            if (string.IsNullOrEmpty(DefaultTracks.NameOf(tid, true)) || string.IsNullOrEmpty(DefaultTracks.NameOf(tid, false))) errs.Add("track name");

            // 特徵數量 + 地標
            int pads = 0, ramps = 0, gliders = 0;
            foreach (var f in d.features) { if (f.kind == (int)TrackFeatureKind.Boost) pads++; else if (f.kind == (int)TrackFeatureKind.GliderRamp) gliders++; else ramps++; }
            if (gliders != p.Gaps) errs.Add("glider ramps " + gliders + " want " + p.Gaps);
            if (pads < 6) errs.Add("boost pads " + pads);
            if (ramps < 2) errs.Add("jump ramps " + ramps);
            if (d.landmarks.Count < 4) errs.Add("landmarks " + d.landmarks.Count);
            if (d.checkpointSpacing < 40f) errs.Add("checkpoint spacing");

            Report("TGEN generator", errs.Count == 0,
                "attempt=" + attempt + " length=" + L.ToString("0") + "m target=" + p.Length.ToString("0") + " nodes=" + d.nodes.Count + " minRadius=" + tr.MinRadius.ToString("0.0") + "m clearance=" + minClear.ToString("0.0") + "m maxM=" + maxM.ToString("0.00") +
                " lapAt24=" + (L / 24f).ToString("0") + "s" + gapInfo + " pads=" + pads + " ramps=" + ramps + " gliders=" + gliders + " landmarks=" + d.landmarks.Count + " checkpoints=" + tr.CheckpointCount +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ TGAP 缺口

        /// 以 RaceKart 的滑翔常數積分:從 22 度跳台前緣(離地 rise)以 speed 衝出去,中性滑翔(不按油門 / 煞車),回傳落到「前緣高度 - drop」時的水平飛行距離。
        static float GlideRange(float speed, float pitchDeg, float rise, float drop, float airGravity)
        {
            return GlideRangeM8(speed, 1f, 0f, rise, drop, airGravity);
        }

        /// M8:滑翔跳台固定出場(水平 GlideLaunchSpeed..Max、垂直 >= GlideLaunchUp,與 speed 無關,speed 只用來夾進範圍)後,以 RaceKart 的滑翔常數積分(lift = GlideLift,dive = 0/1 油門下壓),
        /// 回傳落到「前緣高度 - drop」時的水平距離。
        static float GlideRangeM8(float speed, float lift, float dive, float rise, float drop, float airGravity)
        {
            float hs = Mathf.Clamp(speed * Mathf.Cos(22f * Mathf.Deg2Rad), RaceKart.GlideLaunchSpeed, RaceKart.GlideLaunchMax);
            float vy = Mathf.Max(speed * Mathf.Sin(22f * Mathf.Deg2Rad), RaceKart.GlideLaunchUp);
            float x = 0f, y = 0f, dt = 0.005f;
            float g = airGravity * RaceKart.GapGlideGravityMul * (1f + RaceKart.GapGlideDiveG * dive);
            float sink = Mathf.Max(1.5f, (RaceKart.GapGlideSinkBase + RaceKart.GapGlideDiveSink * dive) / Mathf.Max(0.3f, lift));
            for (int i = 0; i < 4000; i++)
            {
                vy -= g * dt;
                if (vy < -sink) vy = Mathf.MoveTowards(vy, -sink, 30f * dt);
                hs -= 0.4f * dt; hs += dive * RaceKart.GapGlideDiveSpeed * dt;
                x += hs * dt; y += vy * dt;
                if (y <= -(rise + drop)) break;
                if (i * dt > RaceKart.GlideMaxSeconds) break;
            }
            return x;
        }

// ------------------------------------------------------------------ TR 軌道滑行(模組 K)
        // Meadow Run(3)與 Magma Rush(7)各有 1 條軌道(原版與鏡像 + 反向都要有),其他賽道沒有;軌道是外觀 + KartRails 幾何,沒有碰撞體、三角形很少;
        // 軌道在路面上方 1.5~3 m、沿行進方向(s 遞增)、在檢查點門寬以內(所以不會繞過門)、不在缺口 / 跳台 / 加速板 / 捷徑上;護欄碰撞體有 RideableWall 標記。
        void TR()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            int want = (tid == 3 || tid == 7) ? 1 : 0;
            if (tr.Rails.Count != want) errs.Add("rails " + tr.Rails.Count + " (want " + want + ")");
            if (KartRails.All.Count != tr.Rails.Count) errs.Add("KartRails registry " + KartRails.All.Count + " vs track " + tr.Rails.Count);
            Transform trackRoot = RaceWorld.Root.Find("Track");
            Transform rr = trackRoot != null ? trackRoot.Find("Rails") : null;
            int railColliders = rr != null ? rr.GetComponentsInChildren<Collider>(true).Length : 0;
            if (railColliders != 0) errs.Add("rail colliders " + railColliders + " (want 0)");
            int tris = TrackRailBuilder.LastTris;
            if (want > 0 && (tris <= 0 || tris > 1500)) errs.Add("rail triangles " + tris + " (want 1..1500)");
            if (want == 0 && tris != 0) errs.Add("rail triangles " + tris + " on a track without rails");
            Transform guard = trackRoot != null ? trackRoot.Find("RailCollider") : null;
            if (guard != null && guard.GetComponent<RideableWall>() == null) errs.Add("guard wall collider is not tagged RideableWall");
            var info = new StringBuilder();
            for (int i = 0; i < tr.Rails.Count; i++)
            {
                TrackRail rd = tr.Rails[i];
                KartRail kr = KartRails.All.Count > i ? KartRails.All[i] : null;
                if (kr == null) { errs.Add("rail " + i + " missing"); continue; }
                if (kr.Length < 12f || kr.Length > 125f) errs.Add("rail " + i + " length " + kr.Length.ToString("0.0"));
                if (rd.height < 1.5f || rd.height > 3f) errs.Add("rail " + i + " height " + rd.height);
                if (!tr.RailWithinGates(i)) errs.Add("rail " + i + " lateral " + rd.lateral.ToString("0.0") + " is outside the checkpoint gate width");
                if (Mathf.Abs(rd.lateral) > tr.HalfWidthAt(rd.s0) + 2f) errs.Add("rail " + i + " lateral " + rd.lateral.ToString("0.0") + " is off the road");
                Vector3 d0 = kr.P[kr.P.Length - 1] - kr.P[0]; d0.y = 0f;
                if (Vector3.Dot(d0.normalized, tr.TangentAt((rd.s0 + rd.s1) * 0.5f)) < 0.7f) errs.Add("rail " + i + " does not follow the driving direction");
                float lastS = -1f; int badMono = 0, onFeat = 0; float minClear = 1e9f, maxClear = -1e9f;
                for (int k = 0; k < kr.P.Length; k++)
                {
                    Vector3 p = kr.P[k];
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) { errs.Add("rail " + i + " NaN point"); break; }
                    float lat; float s = tr.Project(p, rd.s0 + (rd.s1 - rd.s0) * k / Mathf.Max(1, kr.P.Length - 1), out lat);
                    if (lastS >= 0f && s < lastS - 0.5f && lastS - s < tr.Length * 0.5f) badMono++;
                    lastS = s;
                    if (OnRamp(p.x, p.z, 5f)) onFeat++;   // 只看跳台 / 滑翔跳台(加速板在 2 m 高的軌道下面沒關係)
                    float clear = p.y - RaceWorld.HeightAt(p.x, p.z);
                    minClear = Mathf.Min(minClear, clear); maxClear = Mathf.Max(maxClear, clear);
                }
                if (badMono > 0) errs.Add("rail " + i + " s not monotonic (" + badMono + ")");
                if (onFeat > 0 && tag.IndexOf("reverse") < 0) errs.Add("rail " + i + " overlaps ramps/pads (" + onFeat + " points)");
                if (minClear < 1.0f || maxClear > 3.8f) errs.Add("rail " + i + " clearance over ground " + minClear.ToString("0.00") + ".." + maxClear.ToString("0.00") + " m (want 1.0..3.8)");
                if (tr.InGap(rd.s0, 60f, 20f) || tr.InGap(rd.s1, 60f, 20f) || tr.InGap((rd.s0 + rd.s1) * 0.5f, 60f, 20f)) errs.Add("rail " + i + " is near a gap");
                foreach (var sc in tr.Shortcuts) if (sc.ToS > rd.s0 && sc.FromS < rd.s1) errs.Add("rail " + i + " overlaps shortcut " + sc.Id);
                // 檢查點:軌道範圍內的每個門,軌道上對應弧長的點都在門寬內(車走軌道照樣穿過每一道門)
                int gatesOver = 0;
                for (int g = 0; g < tr.CheckpointCount; g++)
                {
                    Checkpoint cp = tr.Checkpoints[g];
                    if (cp.S < rd.s0 || cp.S > rd.s1) continue;
                    gatesOver++;
                    Vector3 rp = tr.SampleAt(cp.S).Pos + tr.SampleAt(cp.S).Right * rd.lateral;
                    float lat2 = (rp.x - cp.Center.x) * cp.Forward.z - (rp.z - cp.Center.z) * cp.Forward.x;
                    if (Mathf.Abs(lat2) > cp.HalfWidth) errs.Add("rail " + i + " leaves gate " + g);
                }
                info.Append(" rail" + i + "[s " + rd.s0.ToString("0") + ".." + rd.s1.ToString("0") + " len " + kr.Length.ToString("0") + " lat " + rd.lateral.ToString("0.0") + " h " + rd.height.ToString("0.0") + " gatesOver " + gatesOver + " clear " + minClear.ToString("0.0") + ".." + maxClear.ToString("0.0") + "]");
            }
            if (tid == 3 && tag.IndexOf("mirror") < 0 && DefaultTracks.Get(3).rails.Count > 0)
            {
                // 資料層:驗證規則、JSON 往返、Clone 不丟軌道
                TrackDef d = DefaultTracks.Get(3);
                if (d.rails.Count != 1) errs.Add("DefaultTracks.Get(3).rails = " + d.rails.Count);
                TrackDef bad = TrackVariants.Clone(d); bad.rails[0].height = 5f;
                if (bad.Validate() == null) errs.Add("Validate accepts rail height 5");
                TrackDef rt = TrackDef.FromJson(d.ToJson(false));
                if (rt == null || rt.rails.Count != 1 || Mathf.Abs(rt.rails[0].s0 - d.rails[0].s0) > 0.01f) errs.Add("rail JSON round trip failed");
                if (TrackVariants.Clone(d).rails.Count != 1) errs.Add("Clone dropped the rail");
                if ((int)TrackFeatureKind.Rail != 3 || (int)TrackFeatureKind.Ramp != 0 || (int)TrackFeatureKind.Boost != 1 || (int)TrackFeatureKind.GliderRamp != 2) errs.Add("TrackFeatureKind numeric values changed");
            }
            Report("TR rails", errs.Count == 0, "rails=" + tr.Rails.Count + " triangles=" + tris + " colliders=" + railColliders + info + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // M8:路面高低差(每條有缺口的賽道至少一次 >= 8 m 的爬升 / 下降,坡度 <= 5.5 度)、側傾(<= 8 度、緊彎外側高、路面網格與地形一致)、走廊牆(2~3 段、三角形少)
        void TELE()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            float lo = float.MaxValue, hi = float.MinValue, maxSl = 0f, maxBank = 0f; int banked = 0;
            int n = tr.Samples.Count;
            for (int i = 0; i < n; i++)
            {
                float y = tr.Samples[i].Pos.y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                maxSl = Mathf.Max(maxSl, Mathf.Abs(tr.Samples[(i + 1) % n].Pos.y - y) / tr.SampleStep);
                float b = tr.Samples[i].Bank; maxBank = Mathf.Max(maxBank, Mathf.Abs(b)); if (Mathf.Abs(b) > 4f) banked++;
            }
            float slDeg = Mathf.Atan(maxSl) * Mathf.Rad2Deg;
            if (hi - lo < 8f) errs.Add("road elevation range only " + (hi - lo).ToString("0.0") + " m");
            if (slDeg > 6.5f) errs.Add("road slope " + slDeg.ToString("0.0") + " deg");
            if (maxBank > RaceTrack.MaxBankDeg + 0.01f) errs.Add("bank " + maxBank.ToString("0.0") + " deg > " + RaceTrack.MaxBankDeg);
            if (maxBank < 3f) errs.Add("no banked corner (max bank " + maxBank.ToString("0.0") + " deg)");
            // 側傾:外側(彎的外側 = 曲率符號的反邊)比內側高;路面網格(碰撞)與 HeightAt 貼合
            int checkedN = 0, badFit = 0, wrongSide = 0; float maxTilt = 0f;
            for (int i = 0; i < n; i += 5)
            {
                TrackSample sm = tr.Samples[i];
                if (Mathf.Abs(sm.Bank) < 4f) continue;
                checkedN++;
                float hw = sm.Width * 0.5f - 1.5f;
                Vector3 pr = sm.Pos + sm.Right * hw, pl = sm.Pos - sm.Right * hw;
                float yr = RaceWorld.HeightAt(pr.x, pr.z), yl = RaceWorld.HeightAt(pl.x, pl.z);
                float expect = -hw * 2f * Mathf.Tan(sm.Bank * Mathf.Deg2Rad);   // yr - yl
                maxTilt = Mathf.Max(maxTilt, Mathf.Abs(yr - yl));
                if (Mathf.Abs((yr - yl) - expect) > 0.35f) badFit++;
                if (sm.Curvature > 0f && !(yl > yr)) wrongSide++;
                if (sm.Curvature < 0f && !(yr > yl)) wrongSide++;
            }
            if (badFit > 0) errs.Add(badFit + "/" + checkedN + " banked samples: terrain height does not match the bank");
            if (wrongSide > 0) errs.Add(wrongSide + " banked samples tilt toward the outside");
            if (checkedN < 10) errs.Add("only " + checkedN + " banked samples");
            if (TrackCorridor.LastSections < 2) errs.Add("corridor sections " + TrackCorridor.LastSections + " (want 2..3)");
            if (TrackCorridor.LastTris > 4000) errs.Add("corridor triangles " + TrackCorridor.LastTris);
            var secInfo = new StringBuilder(" sections=");
            for (int q = 0; q < TrackCorridor.Sections.Count; q++) secInfo.Append((int)TrackCorridor.Sections[q].x).Append("..").Append((int)TrackCorridor.Sections[q].y).Append("(side ").Append(TrackCorridor.SectionSides[q]).Append(") ");
            float bankS = -1f; for (int i = 0; i < n; i += 3) if (Mathf.Abs(tr.Samples[i].Bank) > 7f) { bankS = tr.Samples[i].S; break; }
            secInfo.Append(" firstBank8@s=").Append(bankS.ToString("0"));
            Report("TELE elevation+bank+corridor", errs.Count == 0,
                "roadY=" + lo.ToString("0.0") + ".." + hi.ToString("0.0") + " (range " + (hi - lo).ToString("0.0") + " m) maxSlope=" + slDeg.ToString("0.0") + "deg maxBank=" + maxBank.ToString("0.0") + "deg bankedSamples=" + banked + " maxCrossFall=" + maxTilt.ToString("0.00") + "m corridorSections=" + TrackCorridor.LastSections + " corridorTris=" + TrackCorridor.LastTris + " markerTris=" + TrackGapBuilder.LastMarkerTris + secInfo +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        void TGAP()
        {
            var errs = new List<string>();
            RaceTrack tr = track;
            FeatureSet fs = RaceWorld.Features;
            TrackDef bd = DefaultTracks.Get(tid);
            if (tr.Gaps.Count != bd.gaps.Count) errs.Add("gaps " + tr.Gaps.Count + " vs data " + bd.gaps.Count);
            Physics.SyncTransforms();
            float L = tr.Length;
            var info = new StringBuilder();
            KartStats ks = KartPresets.For(KartStyle.Brick, 150);
            for (int gi = 0; gi < tr.Gaps.Count; gi++)
            {
                GapInfo g = tr.Gaps[gi];
                string gt = "gap" + gi + " ";
                Vector3 o = g.Origin, f = g.Dir, r = g.Right;
                float G = g.Length;

                // 1) 深坑 = 水面:坑底範圍內(離邊緣 5 m)全是 Water、HeightAt = 0,而且 SampleGround 回 Water
                int pitBad = 0, pitN = 0;
                for (float u = GapInfo.PitInset + 8f; u <= G - GapInfo.PitInset - 8f; u += 3f)      // 離坑緣 >= 8 m(格距 4.5 m 的三角形內插,坑緣附近的點會被沒挖到的頂點拉高)
                    for (float v = -g.HalfWidth + 8f; v <= g.HalfWidth - 8f; v += 5f)
                    {
                        float x = o.x + f.x * u + r.x * v, z = o.z + f.z * u + r.z * v;
                        pitN++;
                        if (RaceWorld.SurfaceAt(x, z) != Surface.Water || RaceWorld.HeightAt(x, z) > 0.001f) pitBad++;
                    }
                if (pitBad > 0) errs.Add(gt + pitBad + "/" + pitN + " pit points are not water");
                GroundHit gh;
                if (!RaceWorld.SampleGround(new Vector3(o.x + f.x * G * 0.5f, 0f, o.z + f.z * G * 0.5f), out gh) || gh.Surface != Surface.Water) errs.Add(gt + "SampleGround at the pit center is not Water");

                // 2) 缺口前後的路面還在:助跑段(跳台之前)與落地區都是 Road / BoostPad,高度貼著路面
                int roadBad = 0;
                for (float u = -GapInfo.RampLength - 60f; u <= -GapInfo.RampLength - 1f; u += 2f)
                {
                    float x = o.x + f.x * u, z = o.z + f.z * u;
                    Surface sf = RaceWorld.SurfaceAt(x, z);
                    if (sf != Surface.Road && sf != Surface.BoostPad) roadBad++;
                }
                for (float u = G + 0.5f; u <= G + 60f; u += 2f)
                {
                    float x = o.x + f.x * u, z = o.z + f.z * u;
                    Surface sf = RaceWorld.SurfaceAt(x, z);
                    if (sf != Surface.Road && sf != Surface.BoostPad) roadBad++;
                }
                if (roadBad > 0) errs.Add(gt + roadBad + " approach / landing points are not road");

                // 3) 沒有擋路的碰撞體:坑中央(沿中心線每 4 m)往下的射線,世界裡的碰撞體頂面都在水面下 2 m 以下;落地區 / 助跑段有地面
                int solid = 0;
                for (float u = GapInfo.PitInset + 8f; u <= G - GapInfo.PitInset - 8f; u += 4f)
                    foreach (float v in new float[] { -6f, 0f, 6f, g.HalfWidth - 10f })
                    {
                        Vector3 p0 = new Vector3(o.x + f.x * u + r.x * v, 60f, o.z + f.z * u + r.z * v);
                        foreach (var h in Physics.RaycastAll(p0, Vector3.down, 120f))
                            if (h.collider.transform.IsChildOf(RaceWorld.Root) && h.point.y > -2f) solid++;
                    }
                if (solid > 0) errs.Add(gt + solid + " colliders above y=-2 inside the pit");
                {
                    Vector3 p0 = new Vector3(o.x + f.x * (G + 6f), tr.SampleAt(g.S1 + 6f).Pos.y + 8f, o.z + f.z * (G + 6f));
                    bool hit = false;
                    foreach (var h in Physics.RaycastAll(p0, Vector3.down, 30f)) if (h.collider.transform.IsChildOf(RaceWorld.Root) && Mathf.Abs(h.point.y - tr.SampleAt(g.S1 + 6f).Pos.y) < 0.2f) hit = true;
                    if (!hit) errs.Add(gt + "no ground collider on the landing zone");
                }

                // 4) 路面網格:缺口內(前緣 + 1 m 到落地邊緣 - 1 m,橫向 40 m)沒有路面 / 路肩 / 路緣的三角形;前緣前 6 m、落地邊緣後 6 m 有
                Transform trackRoot = RaceWorld.Root.Find("Track");
                int inCut = 0, before = 0, after = 0;
                if (trackRoot == null) errs.Add(gt + "no Track root");
                else
                    foreach (var mf in trackRoot.GetComponentsInChildren<MeshFilter>(true))
                    {
                        string nm = mf.sharedMesh != null ? mf.sharedMesh.name : "";
                        if (nm != "Road" && nm != "Shoulder" && nm != "RedParts" && nm != "WhiteParts") continue;
                        int[] tris = mf.sharedMesh.GetTriangles(0); Vector3[] vs = mf.sharedMesh.vertices;
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                        {
                            Vector3 c = (vs[tris[t]] + vs[tris[t + 1]] + vs[tris[t + 2]]) / 3f;
                            float u = (c.x - o.x) * f.x + (c.z - o.z) * f.z, v = (c.x - o.x) * r.x + (c.z - o.z) * r.z;
                            if (Mathf.Abs(v) > 6f) continue;
                            if (u > 1f && u < G - 1f) inCut++;
                            else if (u > -6f && u < -1f && nm == "Road") before++;
                            else if (u > G + 1f && u < G + 6f && nm == "Road") after++;
                        }
                    }
                if (inCut > 0) errs.Add(gt + inCut + " road mesh triangles remain inside the gap");
                if (before < 1 || after < 1) errs.Add(gt + "road mesh missing before (" + before + ") / after (" + after + ") the gap");

                // 5) 檢查點:缺口範圍恰好 1 個門(中央);它的重置點在跳台底部之前 >= 60 m 的路面上;跳台底部前 6 m 到落地後 12 m 沒有別的門
                int inGate = 0, gapCp = -1, bad = 0;
                for (int c = 0; c < tr.CheckpointCount; c++)
                {
                    float cs = tr.Checkpoints[c].S;
                    if (cs > g.S0 && cs < g.S1) { inGate++; gapCp = c; }
                    else if (cs >= g.RampBase - GapInfo.GateFreeBefore && cs <= g.S1 + 12f) bad++;
                }
                if (inGate != 1) errs.Add(gt + inGate + " gates inside the gap (want 1)");
                if (bad > 0) errs.Add(gt + bad + " gates too close to the ramp / landing");
                float respawnBack = 0f;
                if (gapCp >= 0)
                {
                    Vector3 rp = tr.Checkpoints[gapCp].Respawn.position;
                    float lat; float rs = tr.Project(rp, -1f, out lat);
                    respawnBack = tr.Wrap(g.RampBase - rs);
                    if (respawnBack < 60f || respawnBack > 90f) errs.Add(gt + "gap gate respawn is " + respawnBack.ToString("0") + " m before the ramp (want 60..90)");
                    if (Mathf.Abs(lat) > 1f || RaceWorld.SurfaceAt(rp.x, rp.z) != Surface.Road) errs.Add(gt + "gap gate respawn not on the road");
                }

                // 6) 滑翔跳台:前緣資料在缺口前緣上
                bool lipOk = false;
                foreach (var l in RaceWorld.GliderLips)
                {
                    float dx = l.Center.x - o.x, dz = l.Center.z - o.z;
                    if (dx * dx + dz * dz < 1.5f * 1.5f) lipOk = true;
                }
                if (!lipOk) errs.Add(gt + "no GliderLip at the gap's lip");
                if (RaceWorld.Root.Find("Track/Features/GliderRampCollider") == null) errs.Add(gt + "no GliderRampCollider");

                // 7) 滑翔距離:150cc 極速 / 100cc 極速的飛行距離(22 度跳台,前緣高 5.66 m,落到落地邊緣的路面高度)
                float drop = tr.SampleAt(g.S0).Pos.y - tr.SampleAt(g.S1).Pos.y;
                float rise = Mathf.Tan(22f * Mathf.Deg2Rad) * GapInfo.RampLength;
                float r150 = GlideRange(ks.TopSpeed, 22f, rise, drop, ks.AirGravity);
                float r100 = GlideRange(KartPresets.TopSpeedForCc(100), 22f, rise, drop, ks.AirGravity);
                float r50 = GlideRange(KartPresets.TopSpeedForCc(50), 22f, rise, drop, ks.AirGravity);
                // M8:固定出場速度 -> 所有排氣量飛得一樣遠;最差情況(GlideLift 0.9 + 一直按油門下壓)也要過缺口 + 6 m,最佳情況(GlideLift 1.2 中性)不能飛出落地區
                float rWorst = GlideRangeM8(0f, 0.9f, 1f, rise, drop, ks.AirGravity);
                float rBest = GlideRangeM8(0f, 1.2f, 0f, rise, drop, ks.AirGravity);
                if (Mathf.Abs(r150 - r50) > 0.5f || Mathf.Abs(r150 - r100) > 0.5f) errs.Add(gt + "glide range depends on cc (50/100/150 = " + r50.ToString("0") + "/" + r100.ToString("0") + "/" + r150.ToString("0") + ")");
                if (r50 < G + 6f) errs.Add(gt + "50cc glide range " + r50.ToString("0.0") + " m < gap " + G.ToString("0.0") + " + 6");
                if (rWorst < G + 4f) errs.Add(gt + "worst-case glide range " + rWorst.ToString("0.0") + " m < gap " + G.ToString("0.0") + " + 4");
                if (rBest > G + TrackGen.Landing - 6f) errs.Add(gt + "best-case glide range " + rBest.ToString("0.0") + " m overshoots the landing zone (gap " + G.ToString("0.0") + ")");

                // 8) 幽靈車:飛過去(高度在水面上)不重置、過了缺口的門;低於水面附近 = 掉下去,重置到缺口門的重置點;從旁邊繞過去(橫向 70 m)拿不到缺口的門
                {
                    var cp = new RaceCheckpoints(tr, 3, false);
                    var gh1 = new Ghost { Id = 3 };
                    var resets = new List<ResetReason>();
                    cp.Reset += (id, why) => resets.Add(why);
                    float s = tr.Length - 10f, t = 0f;     // 從起跑線後面開始跑,一路過門到缺口前面
                    Place(gh1, tr, s, 1);
                    cp.Register(gh1, true);
                    cp.StartRace(0f);
                    RacerProgress pr = cp.Get(3);
                    int guard = 0;
                    while (pr.NextCp != gapCp && guard++ < 60000) { t += Dt; s += GhostSpeed * Dt; Place(gh1, tr, s, 1); cp.Step(Dt, t); }
                    if (pr.NextCp != gapCp) errs.Add(gt + "ghost never reached the gap gate (NextCp=" + pr.NextCp + ")");
                    else
                    {
                        // 飛過缺口:y = 路面高度 + 2.5(在水面上空),一路飛到落地區
                        int resetsBefore = resets.Count;
                        while (tr.Wrap(s) < g.S1 + 20f && tr.Wrap(s) > g.S0 - 200f)   // s 是不繞圈的累計弧長,比較時要包回 [0, L)
                        {
                            t += Dt; s += GhostSpeed * Dt;
                            TrackSample sm = tr.SampleAt(s);
                            gh1.Pos = sm.Pos + Vector3.up * 2.5f; gh1.Fwd = sm.Tangent; gh1.Vel = sm.Tangent * GhostSpeed;
                            cp.Step(Dt, t);
                        }
                        if (resets.Count != resetsBefore) errs.Add(gt + "flying ghost was reset (" + (resets.Count - resetsBefore) + "x, " + resets[resets.Count - 1] + ")");
                        if (pr.NextCp == gapCp) errs.Add(gt + "flying ghost did not pass the gap gate");
                    }
                    // 掉下去:放回缺口中央、高度 0.3 m(水面),最後通過的門是缺口門 -> 重置到它的重置點
                    {
                        var cp2 = new RaceCheckpoints(tr, 3, false);
                        var g2 = new Ghost { Id = 4 };
                        var rs2 = new List<ResetReason>();
                        cp2.Reset += (id, why) => rs2.Add(why);
                        float s2 = tr.Length - 10f; float t2 = 0f;
                        Place(g2, tr, s2, 1); cp2.Register(g2, true); cp2.StartRace(0f);
                        RacerProgress p2 = cp2.Get(4);
                        int gd = 0;
                        while (p2.NextCp != gapCp + 1 && gd++ < 60000)
                        {
                            t2 += Dt; s2 += GhostSpeed * Dt;
                            TrackSample sm = tr.SampleAt(s2);
                            float w2 = tr.Wrap(s2); bool inPit = w2 > g.S0 && w2 < g.S1;
                            Place(g2, tr, s2, 1);
                            if (inPit) g2.Pos = sm.Pos + Vector3.up * 2.5f;
                            cp2.Step(Dt, t2);
                        }
                        if (p2.NextCp != gapCp + 1) errs.Add(gt + "second ghost never passed the gap gate");
                        else
                        {
                            Vector3 mid = tr.SampleAt(g.Mid).Pos;
                            g2.Pos = new Vector3(mid.x, 0.3f, mid.z); g2.Vel = Vector3.zero;
                            t2 += Dt; cp2.Step(Dt, t2);
                            if (rs2.Count != 1 || rs2[0] != ResetReason.Fell) errs.Add(gt + "falling into the pit did not reset (resets=" + rs2.Count + ")");
                            else if ((g2.LastPose.position - tr.Checkpoints[gapCp].Respawn.position).magnitude > 0.01f) errs.Add(gt + "pit reset went to the wrong pose");
                            // 重置後從重置點連續飛過去(高度 2.5 m,一路到缺口中央)不能重置(OverGap 要用連續的 HintS,不能用傳送);到了中央降到水面 0.3 m 就要重置
                            rs2.Clear();
                            float s4 = tr.Wrap(g.RampBase - 70f);
                            for (int fl = 0; fl < 6000 && s4 < g.Mid; fl++)
                            {
                                t2 += Dt; s4 += GhostSpeed * Dt;
                                TrackSample sm = tr.SampleAt(s4);
                                g2.Pos = sm.Pos + Vector3.up * 2.5f; g2.Fwd = sm.Tangent; g2.Vel = sm.Tangent * GhostSpeed;
                                cp2.Step(Dt, t2);
                            }
                            if (rs2.Count != 0) errs.Add(gt + "ghost flying 2.5 m above the pit was reset (" + rs2.Count + "x)");
                            Vector3 midp = tr.SampleAt(g.Mid).Pos;
                            g2.Pos = new Vector3(midp.x, 0.3f, midp.z); t2 += Dt; cp2.Step(Dt, t2);
                            if (rs2.Count != 1) errs.Add(gt + "ghost at water level in the pit was not reset");
                            // 缺口外的水(同一條線偏出去 120 m)高度 3 m 也要重置(沒有飛越的理由)
                            Vector3 far = mid + r * (g.HalfWidth + 90f);
                            if (RaceWorld.SurfaceAt(far.x, far.z) == Surface.Water)
                            {
                                rs2.Clear();
                                g2.Pos = new Vector3(far.x, 3f, far.z); t2 += Dt; cp2.Step(Dt, t2);
                                if (rs2.Count != 1) errs.Add(gt + "ghost over sea far from the gap was not reset");
                            }
                        }
                    }
                    // 繞過去:橫向 70 m 的路線(深坑 + 邊坡之外),拿不到缺口的門
                    {
                        var cp3 = new RaceCheckpoints(tr, 3, false);
                        var g3 = new Ghost { Id = 6 };
                        float s3 = tr.Length - 10f, t3 = 0f;
                        Place(g3, tr, s3, 1); cp3.Register(g3, true); cp3.StartRace(0f);
                        RacerProgress p3 = cp3.Get(6);
                        int g3n = 0;
                        while (p3.NextCp != gapCp && g3n++ < 60000) { t3 += Dt; s3 += GhostSpeed * Dt; Place(g3, tr, s3, 1); cp3.Step(Dt, t3); }
                        if (p3.NextCp == gapCp)
                        {
                            while (tr.Wrap(s3) < g.S1 + 40f && tr.Wrap(s3) > g.S0 - 200f)
                            {
                                t3 += Dt; s3 += GhostSpeed * Dt;
                                TrackSample sm = tr.SampleAt(s3);
                                g3.Pos = sm.Pos + sm.Right * 70f + Vector3.up * 2f; g3.Fwd = sm.Tangent; g3.Vel = sm.Tangent * GhostSpeed;
                                cp3.Step(Dt, t3);
                            }
                            if (p3.NextCp != gapCp) errs.Add(gt + "a ghost that drove around the pit still passed the gap gate");
                        }
                    }
                }

                info.Append(gt).Append("G=").Append(G.ToString("0")).Append("m lipY=").Append(tr.SampleAt(g.S0).Pos.y.ToString("0.0")).Append(" sideY=").Append(RaceWorld.HeightAt(o.x + r.x * (g.HalfWidth + 24f) + f.x * G * 0.5f, o.z + r.z * (g.HalfWidth + 24f) + f.z * G * 0.5f).ToString("0.0")).Append(" range150=").Append(r150.ToString("0")).Append("m range100=").Append(r100.ToString("0")).Append("m range90=").Append(rWorst.ToString("0"))
                    .Append("m drop=").Append(drop.ToString("0.0")).Append(" respawnBack=").Append(respawnBack.ToString("0")).Append("m pit=").Append(pitN - pitBad).Append("/").Append(pitN).Append(" ");
            }
            if (tr.Gaps.Count == 0) errs.Add("track has no gaps");
            Report("TGAP gaps", errs.Count == 0, info.ToString().Trim() + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ TU 盃賽

        void TU()
        {
            var errs = new List<string>();
            if (Cups.Count != 6) errs.Add("Cups.Count=" + Cups.Count);
            var seenNames = new HashSet<string>();
            int mirrors = 0, reverses = 0, both = 0, totalRaces = 0;
            for (int c = 0; c < Cups.Count; c++)
            {
                string zh = Cups.Name(c, "zh"), en = Cups.Name(c, "en");
                if (string.IsNullOrEmpty(zh) || string.IsNullOrEmpty(en) || !seenNames.Add(zh) || !seenNames.Add(en)) errs.Add("cup " + c + " name");
                var races = Cups.Races(c);
                if (races.Count != 4) errs.Add("cup " + c + " has " + races.Count + " races");
                var combos = new HashSet<int>();
                for (int r = 0; r < races.Count; r++)
                {
                    CupRace cr = races[r];
                    totalRaces++;
                    if (cr.TrackId < 0 || cr.TrackId >= RaceWorld.TrackCount) errs.Add("cup " + c + " race " + r + " track " + cr.TrackId);
                    if (!combos.Add(cr.TrackId * 4 + (cr.Mirror ? 1 : 0) + (cr.Reverse ? 2 : 0))) errs.Add("cup " + c + " repeats a (track, mirror, reverse) combo");
                    if (r > 0 && races[r - 1].TrackId == cr.TrackId) errs.Add("cup " + c + " has the same track twice in a row");
                    if (cr.Mirror) mirrors++;
                    if (cr.Reverse) reverses++;
                    if (cr.Mirror && cr.Reverse) both++;
                    int tid2; bool m2, r2;
                    cr.Deconstruct(out tid2, out m2, out r2);
                    if (tid2 != cr.TrackId || m2 != cr.Mirror || r2 != cr.Reverse) errs.Add("deconstruct");
                    if (string.IsNullOrEmpty(Cups.RaceLabel(cr, "zh")) || string.IsNullOrEmpty(Cups.RaceLabel(cr, "en"))) errs.Add("race label");
                }
                var cfg2 = new RaceConfig();
                Cups.Apply(cfg2, c, 2);
                if (cfg2.TrackId != races[2].TrackId || cfg2.Mirror != races[2].Mirror || cfg2.Reverse != races[2].Reverse || cfg2.Cup != c || !cfg2.GrandPrix) errs.Add("Cups.Apply");
            }
            if (mirrors < 4 || reverses < 4 || both < 2) errs.Add("not enough mirror/reverse variety: mirror=" + mirrors + " reverse=" + reverses + " both=" + both);
            int[] want = { 15, 12, 10, 9, 8, 7, 6, 5 };
            int sum = 0;
            for (int p = 1; p <= 8; p++) { if (Cups.Points(p) != want[p - 1]) errs.Add("Points(" + p + ")=" + Cups.Points(p)); sum += Cups.Points(p); }
            if (Cups.Points(0) != 0 || Cups.Points(9) != 0 || Cups.Points(-3) != 0) errs.Add("Points out of range should be 0");
            if (sum != 72) errs.Add("point table sums to " + sum);

            // 總分 / 名次:3 場,8 台車
            var races3 = new List<int[]>
            {
                new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                new[] { 2, 1, 3, 4, 5, 6, 8, 7 },
                new[] { 1, 2, 3, 5, 4, 6, 7, 8 },
            };
            int[] tot = Cups.Totals(races3);
            int[] expectTot = { 15 + 12 + 15, 12 + 15 + 12, 30, 9 + 9 + 8, 8 + 8 + 9, 21, 6 + 5 + 6, 5 + 6 + 5 };
            for (int i = 0; i < 8; i++) if (tot[i] != expectTot[i]) errs.Add("Totals[" + i + "]=" + tot[i] + " want " + expectTot[i]);
            int[] order = Cups.Ranking(tot, races3);
            int[] rank = Cups.RankOf(tot, races3);
            if (order[0] != 0 || order[1] != 1 || order[2] != 2) errs.Add("ranking top3 " + order[0] + "," + order[1] + "," + order[2]);
            if (rank[0] != 1 || rank[1] != 2 || rank[2] != 3) errs.Add("RankOf top3");
            // 完全同分:比第 1 名次數、第 2 名次數…最後一場,最後比編號
            var tie = new List<int[]> { new[] { 1, 2 }, new[] { 2, 1 }, new[] { 2, 2 } };   // 車 0 = 15+12+12 = 39,車 1 = 12+15+12 = 39,名次次數完全相同 → 編號小者勝
            int[] tt = Cups.Totals(tie);
            int[] to = Cups.Ranking(tt, tie);
            if (tt[0] != 39 || tt[1] != 39 || to[0] != 0 || to[1] != 1) errs.Add("tie break by index: " + tt[0] + "," + tt[1] + " order " + to[0] + "," + to[1]);
            var tie2 = new List<int[]> { new[] { 1, 2, 3 }, new[] { 3, 2, 1 } };           // 車 0 = 15+10 = 25,車 2 = 10+15 = 25,車 1 = 24;0 與 2 同分且各 1 次第 1 名,各 1 次第 3 名 → 比最後一場:車 2 勝
            int[] t2 = Cups.Totals(tie2);
            int[] o2 = Cups.Ranking(t2, tie2);
            if (t2[0] != 25 || t2[2] != 25 || o2[0] != 2 || o2[1] != 0 || o2[2] != 1) errs.Add("tie break by last race: order " + o2[0] + "," + o2[1] + "," + o2[2]);
            if (Cups.Trophy(1) != CupTrophy.Gold || Cups.Trophy(2) != CupTrophy.Silver || Cups.Trophy(3) != CupTrophy.Bronze || Cups.Trophy(4) != CupTrophy.None) errs.Add("trophy");
            int[] dnf = Cups.Totals(new List<int[]> { new[] { 0, 1 } });   // 沒完賽 = 0 分
            if (dnf[0] != 0 || dnf[1] != 15) errs.Add("DNF points");
            Report("TU cups", errs.Count == 0, "cups=" + Cups.Count + " races=" + totalRaces + " mirrors=" + mirrors + " reverses=" + reverses + " both=" + both + " pointsSum=" + sum + (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ 預算

        void Budget()
        {
            Transform root = RaceWorld.Root;
            var lod = root.GetComponent<RaceWorldLod>();
            var tr = RaceWorld.Track;
            var errs = new List<string>();

            // 一幀最壞情況:視點取遍整條賽道(每 10 個取樣點)+ 島的角落與中心;不做視錐裁切(偏保守)
            int worstTris = 0, worstDraws = 0; Vector3 worstAt = Vector3.zero;
            var views = new List<Vector3>();
            for (int i = 0; i < tr.Samples.Count; i += 10) views.Add(tr.Samples[i].Pos + Vector3.up * 8f);
            views.Add(Vector3.zero); views.Add(new Vector3(-150, 8, -150)); views.Add(new Vector3(150, 8, 150)); views.Add(new Vector3(-150, 8, 150)); views.Add(new Vector3(150, 8, -150));
            foreach (var v in views)
            {
                int t, d;
                lod.Estimate(v, out t, out d);
                if (t > worstTris) { worstTris = t; worstAt = v; }
                if (d > worstDraws) worstDraws = d;
            }
            int totalTris = lod.TotalMeshTris();

            // 實際數出來的:所有 Renderer / Collider / 材質
            var mats = new HashSet<Material>();
            int renderers = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { renderers++; if (r.sharedMaterial != null) mats.Add(r.sharedMaterial); }
            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            int enabledRenderers = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false)) if (r.enabled) enabledRenderers++;

            if (worstTris >= 150000) errs.Add("worst-frame triangles " + worstTris + " >= 150000");
            if (worstDraws >= 200) errs.Add("worst-frame draw calls " + worstDraws + " >= 200");
            if (colliders >= 150) errs.Add("colliders " + colliders + " >= 150");
            if (mats.Count > 32) errs.Add("materials " + mats.Count + " > 32");
            if (buildMs > 3000) errs.Add("build time " + buildMs + " ms > 3000");
            bool nativeOk = nativeDelta > 0;
            if (nativeOk && nativeDelta > 80L * 1024 * 1024) errs.Add("native memory +" + (nativeDelta / 1048576) + " MB > 80");
            if (monoDelta > 20L * 1024 * 1024) errs.Add("managed memory +" + (monoDelta / 1048576) + " MB");

            Report("BUDGET", errs.Count == 0,
                "worstFrameTriangles~" + worstTris + " (estimate, no frustum culling, view@" + worstAt.x.ToString("0") + "," + worstAt.z.ToString("0") + ") worstFrameDrawCalls~" + worstDraws +
                " allLodTriangles=" + totalTris + " colliders=" + colliders + " materials=" + mats.Count + " renderers=" + renderers + " enabledRenderers=" + enabledRenderers +
                " trackside(furn=" + (RaceWorld.Props != null ? RaceWorld.Props.Furniture : 0) + " signs=" + (RaceWorld.Props != null ? RaceWorld.Props.Signs : 0) + " tufts=" + (RaceWorld.Props != null ? RaceWorld.Props.Tufts : 0) + " tuftTris=" + (RaceWorld.Props != null ? RaceWorld.Props.TuftTris : 0) + ")" +
                " buildTime=" + buildMs + "ms nativeMem=" + (nativeOk ? "+" + (nativeDelta / 1024) + "KB" : "n/a") + " managedMem=+" + (monoDelta / 1024) + "KB" +
                (errs.Count > 0 ? " ERR: " + string.Join("; ", errs.ToArray()) : ""));
        }
    }
}
