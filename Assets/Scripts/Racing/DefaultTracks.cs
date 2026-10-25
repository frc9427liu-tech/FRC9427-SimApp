using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 寫死在程式裡的預設賽道(找不到 Resources/Racing/Tracks/track<id>.json 時使用,所以不會因缺檔就不能玩)。M5:3 條賽道,世界 864 x 864 m。
    // 節點座標由設計工具(圓角多邊形 + Catmull-Rom 取樣)算好貼在這裡;跳台 / 加速板的弧長位置由同一個工具驗證過:
    // 避開檢查點門與重置點、道具箱列、起跑格;跳台前後是夠長的直路(落點區 Road);兩個跳台至少隔 70 m。RaceWorldTest T10 會在遊戲裡再驗一次。
    //
    //  0 Island GP   約 1444 m,丘陵島,逆時針:南邊長直線起跑 → 東南大彎 → 東側連續彎 → 北側缺口(捷徑穿過缺口,土路,兩塊加速板)→ 西端髮夾彎 → 回程 S 形彎。
    //                地標:大拱門、3 座風車。滑翔跳台 1 個(s=1380,南邊長直線接近終點線前,落點在終點線附近的直路)。
    //  1 Canyon Run  約 1197 m,沙漠風峽谷(丘陵權重 12~42 m 就長滿 = 兩側陡壁),連續彎、3 個跳台 + 2 個滑翔跳台(s=662 出彎後、s=1135 起跑前長直道)、岩石拱門、石柱。
    //  2 Coast Loop  約 1597 m,海岸:長直道 + 大半徑彎,懸崖在路邊 25 m 外(掉下去 = 海 = 重置),燈塔、大拱門、風車。滑翔跳台 1 個(s=845,海岸直道;路本身就是備選路線,沒有挖斷的海灣)。
    //
    //  滑翔跳台(GliderRamp,寬 10 / 長 14 / 坡 22 度,高 5.7 m)位置由 Python 工具照 RaceTrack 的取樣與路面剖面算過:起飛段夠直、前緣後 50~90 m 的路中心線離直飛射線 <= 8/15/26 m,
    //  避開檢查點門 / 重置點 / 起跑格 / 道具箱列,前方 95 m 內沒有別的跳台(所以拿掉了 Island 的 s=22、s=1320 與 Canyon 的 s=658、s=1140 四個普通跳台)。
    //
    //  M7:3..7 由 TrackGen 產生(種子 + 參數,見下面的 GenTable;產生一次後快取,所以每次 Get 都是同一條賽道),長度 2.6~3.9 km,每條 1~2 個「飛越缺口」(路被切斷 28~48 m,
    //  只能靠滑翔跳台飛過去,掉下去 = 重置到缺口前的檢查點)。    3 Meadow Run 草原  4 Dune Dash 沙漠  5 Frost Peak 雪地  6 Pine Hollow 森林  7 Magma Rush 火山(熔岩河)。
    public static class DefaultTracks
    {
        public const int Count = 9;
        public const int Handmade = 3;   // 0..2 手工設計,3.. 產生器

        static readonly string[] NameEn = { "Island GP", "Canyon Run", "Coast Loop", "Meadow Run", "Dune Dash", "Frost Peak", "Pine Hollow", "Magma Rush", "Dusk Pass" };
        static readonly string[] NameZh = { "島嶼大賽", "峽谷疾馳", "海岸環線", "翠綠草原", "沙丘狂飆", "冰雪山巔", "松林幽谷", "熔岩疾走", "黃昏山路" };

        /// 賽道名稱(不必產生賽道;選單一次會問很多次)。
        public static string NameOf(int id, bool en)
        {
            if (id < 0) id = 0;
            id %= Count;
            return en ? NameEn[id] : NameZh[id];
        }

        // 產生器參數(3..7)。Attempt = 通過檢查的嘗試編號(由 TrackGen 找出來後記在這裡,避免每次啟動重新搜尋;改參數後要重新找:RaceWorldTest 的 TGEN 會印出實際用到的編號)
        internal static TrackGenParams GenParams(int id)
        {
            switch (id)
            {
                case 3: return new TrackGenParams { Seed = 3301, Theme = 3, Length = 2700f, Difficulty = 0, Gaps = 1, Cell = 90f, Blocky = 0.20f, Sweep = 0.75f, Laps = 2, Name = NameEn[3], NameZh = NameZh[3], Attempt = 28, Relief = 1.3f, HillAmp = 22f, WallStart = 24f, WallEnd = 110f, TreeDensity = 0.8f };
                case 4: return new TrackGenParams { Seed = 4412, Theme = 4, Length = 3100f, Difficulty = 1, Gaps = 1, Cell = 90f, Blocky = 0.12f, Sweep = 0.6f, Laps = 2, Name = NameEn[4], NameZh = NameZh[4], Attempt = 85, Relief = 1.2f, HillAmp = 30f, WallStart = 22f, WallEnd = 90f, TreeDensity = 0.3f };
                case 5: return new TrackGenParams { Seed = 5523, Theme = 5, Length = 3400f, Difficulty = 1, Gaps = 2, Cell = 88f, Blocky = 0.10f, Sweep = 0.5f, Laps = 2, Name = NameEn[5], NameZh = NameZh[5], Attempt = 266, Relief = 1.8f, HillAmp = 34f, WallStart = 20f, WallEnd = 90f, TreeDensity = 0.8f };
                case 6: return new TrackGenParams { Seed = 6634, Theme = 6, Length = 3700f, Difficulty = 2, Gaps = 1, Cell = 88f, Blocky = 0.05f, Sweep = 0.4f, Laps = 2, Name = NameEn[6], NameZh = NameZh[6], Attempt = 4, Relief = 1.5f, HillAmp = 28f, WallStart = 24f, WallEnd = 100f, TreeDensity = 0.75f };
                case 8: return new TrackGenParams { Seed = 8856, Theme = 8, Length = 3500f, Difficulty = 1, Gaps = 1, Cell = 88f, Blocky = 0.08f, Sweep = 0.55f, Laps = 2, Name = NameEn[8], NameZh = NameZh[8], Attempt = 3, Relief = 1.7f, HillAmp = 32f, WallStart = 20f, WallEnd = 90f, TreeDensity = 0.32f };
                default: return new TrackGenParams { Seed = 7745, Theme = 7, Length = 3900f, Difficulty = 2, Gaps = 2, Cell = 88f, Blocky = 0.08f, Sweep = 0.45f, Laps = 2, Name = NameEn[7], NameZh = NameZh[7], Attempt = 555, Relief = 1.6f, HillAmp = 40f, WallStart = 16f, WallEnd = 70f, TreeDensity = 0.15f };
            }
        }

        static readonly TrackDef[] generated = new TrackDef[Count];

        /// 產生器賽道(3..7)的快取版本(第一次呼叫才產生,約數十毫秒)。回傳的是共用物件,呼叫者不要改;要改用 Get(id)。
        static TrackDef Generated(int id)
        {
            TrackDef d = generated[id];
            if (d != null) return d;
            try { d = TrackGen.Generate(GenParams(id)); AddPinnedRails(id, d); AddPinnedShortcuts(id, d); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DefaultTracks] TrackGen failed for track " + id + ": " + e.Message + " - using Island GP instead");
                d = Island(); d.name = NameEn[id]; d.nameZh = NameZh[id];
            }
            generated[id] = d;
            return d;
        }

        // 軌道滑行(模組 K):只加在 Meadow Run(3)與 Magma Rush(7),在產生器結果之後加(不動 GenParams.Attempt,不影響決定性)。
        // 位置 = 從 startRatio x 全長起往後掃第一段「直(|k| < 1/220)、離缺口 / 跳台 / 加速板 / 捷徑 / 起跑線都夠遠」的主線,是 s 的純函式,所以每次結果相同。
        static void AddPinnedRails(int id, TrackDef d)
        {
            try
            {
                if (id == 3) AddRail(d, 0.30f, 56f);
                else if (id == 7) AddRail(d, 0.55f, 64f);
            }
            catch (System.Exception e) { Debug.LogWarning("[DefaultTracks] rail placement failed for track " + id + ": " + e.Message); }
        }

        // 掃描順序:由 startRatio 位置往兩側擴張;條件分 6 關由嚴到鬆(曲率上限 / 跳台加速板緩衝 / 缺口緩衝),都找不到再縮短軌道。
        // 最鬆那關仍然保證不壓缺口(前 64 m / 後 24 m)、跳台與滑翔跳台(>= 7 m)、捷徑、起跑線,所以幾乎一定找得到;全是 s 的純函式,決定性。
        static readonly float[] RailK = { 1f / 220f, 1f / 120f, 1f / 80f, 1f / 55f, 1f / 40f, 1f / 30f };
        static readonly float[] RailFeat = { 30f, 18f, 12f, 8f, 7f, 7f };
        static readonly float[] RailGapB = { 130f, 100f, 80f, 70f, 66f, 64f }, RailGapA = { 70f, 66f, 64f, 64f, 64f, 64f };   // M8:反向賽道時落地邊緣之後變成跳台之前,測試要求 >= 60 m

        static void AddRail(TrackDef d, float startRatio, float len)
        {
            RaceTrack t = RaceTrack.FromDef(d);
            float L = t.Length;
            for (int tryLen = 0; tryLen < 4; tryLen++)
            {
                float rl = tryLen == 0 ? len : tryLen == 1 ? 40f : tryLen == 2 ? 28f : 20f;
                float lo = 150f, hi = L - 130f - rl - 20f;
                if (hi <= lo) continue;
                float from = Mathf.Clamp(L * startRatio, lo, hi);
                for (int pass = 0; pass < RailK.Length; pass++)
                    for (float off = 0f; from - off >= lo || from + off <= hi; off += 6f)
                    {
                        float a = from + off, b = from - off;
                        if (a <= hi && RailSpotOk(t, d, a, rl, pass)) { d.rails.Add(new TrackRail(a, a + rl, 3.5f, 2.2f)); return; }
                        if (off > 0f && b >= lo && RailSpotOk(t, d, b, rl, pass)) { d.rails.Add(new TrackRail(b, b + rl, 3.5f, 2.2f)); return; }
                    }
            }
        }

        static bool RailSpotOk(RaceTrack t, TrackDef d, float s0, float len, int pass)
        {
            for (float s = s0 - 20f; s <= s0 + len + 20f; s += 4f)
            {
                if (Mathf.Abs(t.CurvatureAt(s)) > RailK[pass]) return false;
                if (t.InGap(s, RailGapB[pass], RailGapA[pass])) return false;
            }
            float fm = RailFeat[pass];
            foreach (var f in d.features)
            {
                if (f.path >= 0) continue;
                if (pass >= 3 && f.kind == (int)TrackFeatureKind.Boost) continue;   // 後幾關:加速板在軌道下面沒關係(軌道在 2 m 高)
                if (f.s > s0 - fm - f.length * 0.5f && f.s < s0 + len + fm + f.length * 0.5f) return false;
            }
            foreach (var sc in t.Shortcuts) if (sc.ToS > s0 - 30f && sc.FromS < s0 + len + 30f) return false;
            return true;
        }

        // ------------------------------------------------------------------ 捷徑(裁判系統配套)
        // 只加在 Dune Dash(4)與 Frost Peak(5);Pine Hollow 面數已接近上限,不加。跟軌道一樣在產生器結果之後才加(不動 GenParams.Attempt,不影響決定性)。
        // 位置 = 全是主線幾何的純函式:從 prefer x 全長附近挑「切過一個大彎 / U 形缺口」的弦(捷徑長度 = 被跳過的主線 36~68%),
        // 弦兩端要順著主線切線(夾角 <= 60 度)、中段離主線其他部分 >= 30 m、避開缺口 / 軌道 / 地標 / 起跑線,最後用真正建出來的 ShortcutPath 再驗一次
        // (彎道半徑、被跳過的門離捷徑 >= 10 m、入口 / 出口門離主線 >= 16 m)。捷徑上放 2 塊加速板(土路比較慢,靠加速板補回來)。
        static void AddPinnedShortcuts(int id, TrackDef d)
        {
            try
            {
                bool ok = true;
                if (id == 4) ok = AddShortcut(d, 0.30f);
                else if (id == 5) ok = AddShortcut(d, 0.55f);
                if (!ok) Debug.LogWarning("[DefaultTracks] no valid shortcut spot for track " + id);
            }
            catch (System.Exception e)
            {
                d.shortcuts.Clear(); d.features.RemoveAll(f => f.path >= 0);
                Debug.LogWarning("[DefaultTracks] shortcut placement failed for track " + id + ": " + e.Message);
            }
        }

        const float ScMargin = 170f;       // 離起跑線(s = 0 與 s = L)的最小距離
        sealed class ScCand { public int A, B; public float Score; public Vector3 Pa, Pb; }

        internal static bool AddShortcut(TrackDef d, float prefer)
        {
            RaceTrack t = RaceTrack.FromDef(d);
            float L = t.Length; int n = d.nodes.Count;
            var ns = new float[n];
            for (int i = 1; i < n; i++) { float lat; ns[i] = t.Project(new Vector3(d.nodes[i].x, 0f, d.nodes[i].z), -1f, out lat); }
            var cands = new List<ScCand>();
            for (int a = 2; a < n - 4; a++)
            {
                if (ns[a] < ScMargin || ns[a] < ns[a - 1]) continue;
                if (ns[a] > L - ScMargin - 190f) break;
                for (int b = a + 3; b <= n - 3; b++)
                {
                    float span = ns[b] - ns[a];
                    if (ns[b] < ns[b - 1] || span > 460f || ns[b] > L - ScMargin) break;
                    if (span < 170f) continue;
                    var pa = new Vector3(d.nodes[a].x, 0f, d.nodes[a].z); var pb = new Vector3(d.nodes[b].x, 0f, d.nodes[b].z);
                    Vector3 c = pb - pa; float cl = c.magnitude;
                    float ratio = cl / span;
                    if (ratio < 0.28f || ratio > 0.64f) continue;
                    Vector3 cd = c / cl;
                    if (Vector3.Dot(t.TangentAt(ns[a]), cd) < 0.5f || Vector3.Dot(t.TangentAt(ns[b]), cd) < 0.5f) continue;
                    if (!ScSpotOk(d, t, ns[a], ns[b], pa, pb, cl)) continue;
                    float score = Mathf.Abs((ns[a] + ns[b]) * 0.5f - prefer * L) / 200f + Mathf.Abs(ratio - 0.46f) * 2f;
                    cands.Add(new ScCand { A = a, B = b, Score = score, Pa = pa, Pb = pb });
                }
            }
            cands.Sort((x, y) => x.Score.CompareTo(y.Score));
            for (int k = 0; k < cands.Count && k < 80; k++)
                if (TryBuildShortcut(d, cands[k])) return true;
            d.shortcuts.Clear();
            return false;
        }

        // 弦(pa -> pb)的便宜檢查:缺口 / 軌道 / 地標 / 地圖邊界 / 離主線的距離
        static bool ScSpotOk(TrackDef d, RaceTrack t, float sA, float sB, Vector3 pa, Vector3 pb, float cl)
        {
            for (float s = sA - 30f; s <= sB + 30f; s += 10f) if (t.InGap(s, 150f, 110f)) return false;
            foreach (var r in d.rails) if (r.s1 > sA - 40f && r.s0 < sB + 40f) return false;
            Vector3 cd = (pb - pa) / cl;
            for (float dd = 0f; dd <= cl; dd += 6f)
            {
                Vector3 p = pa + cd * dd;
                if (Mathf.Abs(p.x) > 380f || Mathf.Abs(p.z) > 380f) return false;
                if (dd < 45f || dd > cl - 45f) continue;
                float lat; t.Project(p, -1f, out lat);
                if (Mathf.Abs(lat) < 30f) return false;
                for (int g = 0; g < t.Gaps.Count; g++)
                {
                    GapInfo gi = t.Gaps[g];
                    if (DistToSeg(p, gi.Origin, gi.Origin + gi.Dir * gi.Length) < gi.HalfWidth + 40f) return false;
                }
                for (int m = 0; m < d.landmarks.Count; m++)
                {
                    TrackLandmark lm = d.landmarks[m];
                    TrackSample sm = t.SampleAt(lm.s);
                    if (DistToSeg(sm.Pos + sm.Right * lm.lateral, p, p) < 30f) return false;
                }
            }
            return true;
        }

        static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z, l2 = abx * abx + abz * abz;
            float u = l2 > 1e-9f ? Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / l2) : 0f;
            float ex = a.x + abx * u - p.x, ez = a.z + abz * u - p.z;
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        static bool TryBuildShortcut(TrackDef d, ScCand c)
        {
            var sd = new TrackShortcut { id = 0, fromNode = c.A, toNode = c.B, width = 7f, entryDist = 24f, exitDist = 24f };
            sd.nodes.Add(new TrackNode(Mathf.Lerp(c.Pa.x, c.Pb.x, 0.34f), Mathf.Lerp(c.Pa.z, c.Pb.z, 0.34f), 7f));
            sd.nodes.Add(new TrackNode(Mathf.Lerp(c.Pa.x, c.Pb.x, 0.66f), Mathf.Lerp(c.Pa.z, c.Pb.z, 0.66f), 7f));
            d.shortcuts.Clear(); d.shortcuts.Add(sd);
            RaceTrack t = RaceTrack.FromDef(d);
            ShortcutPath sc = t.Shortcuts[0];
            float ratio = sc.Length / sc.MainSpan;
            if (ratio < 0.28f || ratio > 0.70f || sc.FirstSkipped < 1 || sc.LastSkipped - sc.FirstSkipped < 1) { d.shortcuts.Clear(); return false; }
            // 彎道半徑 >= 22 m(每 4 個取樣點的轉角)
            for (int i = 0; i + 4 < sc.Count; i++)
            {
                float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(sc.Tan[i], sc.Tan[i + 4]), -1f, 1f));
                if (ang / (4f * sc.Step) > 1f / 22f) { d.shortcuts.Clear(); return false; }
            }
            float minSep = 1e9f, minGate = 1e9f;
            for (int i = 0; i < sc.Count; i++)
            {
                float dd = i * sc.Step;
                Vector3 p = sc.Pos[i];
                if (dd >= 40f && dd <= sc.Length - 40f) { float lat; t.Project(p, -1f, out lat); minSep = Mathf.Min(minSep, Mathf.Abs(lat)); }
                if (dd < 8f || dd > sc.Length - 8f) continue;
                for (int g = sc.FirstSkipped; g <= sc.LastSkipped; g++)
                {
                    Checkpoint cp = t.Checkpoints[g];
                    Vector3 right = new Vector3(cp.Forward.z, 0f, -cp.Forward.x);
                    minGate = Mathf.Min(minGate, DistToSeg(p, cp.Center - right * cp.HalfWidth, cp.Center + right * cp.HalfWidth));
                }
            }
            if (minSep < 26f || minGate < 10f) { d.shortcuts.Clear(); return false; }
            // 入口 / 出口門:離主線中心線 >= 16 m 的最近位置(主線上的車不會誤觸)
            float ed = -1f, xd = -1f;
            for (float dist = 14f; dist < sc.Length * 0.4f && ed < 0f; dist += 1f) { float lat; t.Project(sc.SampleAt(dist).Pos, -1f, out lat); if (Mathf.Abs(lat) >= 16f) ed = dist; }
            for (float dist = 14f; dist < sc.Length * 0.4f && xd < 0f; dist += 1f) { float lat; t.Project(sc.SampleAt(sc.Length - dist).Pos, -1f, out lat); if (Mathf.Abs(lat) >= 16f) xd = dist; }
            if (ed < 0f || xd < 0f) { d.shortcuts.Clear(); return false; }
            sd.entryDist = ed; sd.exitDist = xd;
            foreach (float u in new[] { 0.43f, 0.57f })
            {
                var f = new TrackFeature(TrackFeatureKind.Boost, sc.Length * u, 6f, 4f);
                f.path = 0;
                d.features.Add(f);
            }
            return true;
        }

        static readonly float[] IslandGp =
        {
            17.8f, -138.7f, 44.1f, -138.7f, 70.4f, -138.7f, 96.7f, -138.7f, 123.0f, -138.7f, 135.2f, -137.2f,
            146.6f, -132.9f, 156.7f, -126.0f, 164.9f, -116.9f, 176.8f, -100.0f, 188.6f, -83.0f, 193.2f, -74.5f,
            195.6f, -65.0f, 195.6f, -55.3f, 193.3f, -45.9f, 183.4f, -19.5f, 173.5f, 7.0f, 171.6f, 14.2f,
            171.2f, 21.7f, 172.4f, 29.1f, 175.0f, 36.1f, 187.0f, 60.1f, 199.0f, 84.2f, 201.4f, 90.8f,
            202.1f, 97.8f, 201.1f, 104.8f, 198.5f, 111.3f, 194.4f, 117.0f, 174.9f, 138.1f, 169.4f, 142.9f,
            162.9f, 146.3f, 155.9f, 148.3f, 148.6f, 148.6f, 133.8f, 147.6f, 127.4f, 146.4f, 121.5f, 143.6f,
            116.5f, 139.4f, 112.7f, 134.2f, 110.3f, 128.1f, 109.5f, 121.7f, 109.5f, 99.0f, 108.6f, 92.3f,
            106.0f, 86.0f, 101.9f, 80.6f, 96.5f, 76.5f, 90.2f, 73.9f, 83.5f, 73.0f, 58.8f, 73.0f,
            52.1f, 73.9f, 45.9f, 76.5f, 40.5f, 80.6f, 36.3f, 86.0f, 33.7f, 92.3f, 32.8f, 99.0f,
            32.9f, 121.9f, 32.0f, 128.3f, 29.7f, 134.3f, 25.9f, 139.6f, 21.0f, 143.7f, 15.2f, 146.5f,
            8.8f, 147.8f, -15.8f, 149.7f, -40.4f, 151.6f, -43.8f, 151.6f, -47.2f, 151.3f, -72.1f, 147.4f,
            -97.1f, 143.5f, -122.1f, 139.6f, -127.6f, 138.3f, -132.8f, 136.2f, -153.4f, 125.9f, -174.0f, 115.6f,
            -178.1f, 112.9f, -181.5f, 109.5f, -184.0f, 105.3f, -185.6f, 100.7f, -186.2f, 95.9f, -186.2f, 84.0f,
            -185.4f, 78.4f, -183.2f, 73.0f, -179.7f, 68.5f, -175.2f, 65.0f, -169.8f, 62.8f, -164.2f, 62.0f,
            -133.7f, 62.0f, -103.3f, 62.0f, -96.9f, 61.3f, -90.7f, 59.2f, -85.2f, 55.8f, -80.6f, 51.2f,
            -77.1f, 45.8f, -74.9f, 39.7f, -69.6f, 17.5f, -64.3f, -4.7f, -63.4f, -13.3f, -64.7f, -21.7f,
            -68.2f, -29.5f, -73.6f, -36.2f, -80.5f, -41.2f, -104.2f, -54.2f, -127.9f, -67.1f, -132.7f, -70.4f,
            -136.7f, -74.6f, -139.9f, -79.5f, -142.0f, -84.9f, -145.4f, -97.1f, -146.6f, -104.5f, -146.0f, -112.0f,
            -143.7f, -119.2f, -139.9f, -125.7f, -134.7f, -131.2f, -128.3f, -135.3f, -121.2f, -137.8f, -113.7f, -138.7f,
            -87.4f, -138.7f, -61.1f, -138.7f, -34.8f, -138.7f, -8.5f, -138.7f,
        };

        static readonly float[] CanyonRun =
        {
            -19.4f, -114.8f, 6.8f, -115.6f, 32.9f, -116.3f, 59.0f, -117.0f, 85.2f, -117.8f, 111.3f, -118.5f,
            121.6f, -117.6f, 131.4f, -114.4f, 140.3f, -109.1f, 161.0f, -93.0f, 166.8f, -87.1f, 170.9f, -79.9f,
            173.0f, -71.9f, 173.0f, -63.6f, 170.9f, -55.5f, 166.8f, -48.3f, 160.9f, -42.4f, 153.8f, -38.3f,
            146.6f, -35.2f, 140.4f, -31.6f, 135.1f, -26.6f, 131.2f, -20.5f, 128.9f, -13.6f, 128.4f, -6.3f,
            129.5f, 0.8f, 132.4f, 7.5f, 136.8f, 13.3f, 142.4f, 17.8f, 158.2f, 27.7f, 165.1f, 33.4f,
            170.4f, 40.6f, 173.6f, 49.0f, 174.6f, 57.9f, 173.4f, 66.8f, 169.9f, 75.0f, 164.4f, 82.1f,
            143.4f, 103.1f, 137.3f, 107.8f, 130.2f, 110.8f, 122.6f, 111.9f, 115.0f, 111.0f, 107.8f, 108.3f,
            80.8f, 93.5f, 53.7f, 78.7f, 46.5f, 76.0f, 38.9f, 75.1f, 31.3f, 76.2f, 24.2f, 79.2f,
            18.1f, 83.9f, 1.2f, 100.8f, -15.7f, 117.7f, -20.7f, 121.7f, -26.6f, 124.6f, -32.9f, 126.2f,
            -39.4f, 126.3f, -64.8f, 124.2f, -90.2f, 122.1f, -115.7f, 120.0f, -123.2f, 118.6f, -130.4f, 115.8f,
            -136.9f, 111.7f, -166.5f, 88.7f, -172.0f, 83.2f, -176.0f, 76.5f, -178.3f, 69.0f, -178.7f, 61.2f,
            -177.2f, 53.6f, -173.9f, 46.5f, -169.0f, 40.4f, -162.8f, 35.7f, -147.5f, 26.9f, -141.5f, 22.4f,
            -136.9f, 16.5f, -133.8f, 9.7f, -132.5f, 2.4f, -133.0f, -5.0f, -135.4f, -12.1f, -139.5f, -18.4f,
            -144.9f, -23.5f, -151.5f, -27.0f, -170.7f, -34.6f, -178.7f, -39.0f, -185.2f, -45.2f, -189.9f, -53.0f,
            -192.5f, -61.7f, -192.8f, -70.7f, -190.7f, -79.5f, -186.4f, -87.5f, -181.5f, -94.3f, -175.4f, -101.2f,
            -167.8f, -106.4f, -159.2f, -109.7f, -150.1f, -111.1f, -124.0f, -111.8f, -97.8f, -112.6f, -71.7f, -113.3f,
            -45.5f, -114.1f,
        };

        static readonly float[] CoastLoop =
        {
            -26.2f, -332.9f, -0.5f, -333.8f, 25.2f, -334.8f, 50.9f, -335.8f, 76.6f, -336.7f, 102.3f, -337.7f,
            121.3f, -336.3f, 139.6f, -330.9f, 156.3f, -321.7f, 178.3f, -306.2f, 200.2f, -290.7f, 222.2f, -275.3f,
            244.1f, -259.8f, 266.1f, -244.3f, 288.1f, -228.8f, 304.1f, -214.7f, 316.6f, -197.3f, 325.0f, -177.6f,
            328.9f, -156.6f, 330.6f, -133.0f, 332.2f, -109.3f, 333.9f, -85.7f, 335.6f, -62.0f, 334.5f, -41.8f,
            328.2f, -22.6f, 317.3f, -5.5f, 302.5f, 8.2f, 278.3f, 25.3f, 254.2f, 42.4f, 230.1f, 59.5f,
            216.4f, 66.8f, 201.4f, 70.5f, 185.9f, 70.3f, 171.0f, 66.3f, 147.7f, 56.7f, 124.4f, 47.1f,
            101.1f, 37.5f, 77.9f, 27.9f, 54.6f, 18.3f, 51.1f, 16.7f, 28.7f, 5.6f, 6.3f, -5.6f,
            -16.0f, -16.8f, -38.4f, -28.0f, -60.7f, -39.2f, -83.1f, -50.4f, -88.1f, -52.5f, -93.3f, -54.2f,
            -116.5f, -60.3f, -139.8f, -66.4f, -163.0f, -72.5f, -186.2f, -78.7f, -204.2f, -85.8f, -219.8f, -97.1f,
            -232.3f, -111.8f, -240.8f, -129.1f, -244.9f, -148.0f, -244.4f, -167.3f, -240.8f, -190.7f, -237.2f, -214.1f,
            -233.5f, -237.5f, -229.9f, -260.9f, -225.1f, -278.5f, -216.5f, -294.5f, -204.4f, -308.0f, -189.5f, -318.5f,
            -172.6f, -325.3f, -154.6f, -328.1f, -128.9f, -329.0f, -103.2f, -330.0f, -77.6f, -330.9f, -51.9f, -331.9f,
        };

        /// 取得賽道定義(每次回傳新物件,呼叫者可以隨便改)。id 超出範圍取餘數。
        public static TrackDef Get(int id)
        {
            if (id < 0) id = 0;
            id %= Count;
            var res = Resources.Load<TextAsset>("Racing/Tracks/track" + id);
            if (res != null)
            {
                var d = TrackDef.FromJson(res.text);
                if (d != null && d.Validate() == null) return d;
                Debug.LogWarning("[DefaultTracks] track" + id + ".json invalid, using built-in");
            }
            switch (id)
            {
                case 1: return Canyon();
                case 2: return Coast();
                case 0: return Island();
                default: return TrackVariants.Clone(Generated(id));
            }
        }

        /// 滑翔跳台:寬 10 m、長 14 m、坡度 22 度(前緣高約 5.7 m)。s = 跳台中心的弧長。
        public static TrackFeature Glider(float s)
        {
            return new TrackFeature(TrackFeatureKind.GliderRamp, s, GliderLength, GliderWidth, 0f, GliderPitch);
        }
        public const float GliderLength = 14f, GliderWidth = 10f, GliderPitch = 22f;

        static void AddNodes(TrackDef d, float[] xz)
        {
            for (int i = 0; i + 1 < xz.Length; i += 2) d.nodes.Add(new TrackNode(xz[i], xz[i + 1], 12f));
        }

        static TrackDef Island()
        {
            var d = new TrackDef
            {
                name = "Island GP", nameZh = "島嶼大賽", seed = 9427, laps = 3, closed = true, checkpointSpacing = 45f,
                theme = 0, relief = 1.6f, hillAmp = 26f, wallStart = 24f, wallEnd = 120f, cliff = 0f, treeDensity = 1f,
            };
            AddNodes(d, IslandGp);
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 242.0f, 8f, 6f, 0f, 18f));
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 777.0f, 8f, 6f, 0f, 18f));
            d.features.Add(Glider(1380.0f));
            foreach (float ps in new float[] { 195.0f, 252.7f, 745.0f, 977.6f, 1250.0f, 1397.9f }) d.features.Add(new TrackFeature(TrackFeatureKind.Boost, ps, 6f, 5f));
            // 捷徑:從北側長直線分出去,穿過缺口接回(主線要下去缺口再繞上來)。土路(Dirt,比較慢)+ 兩塊加速板補回來。
            var sc = new TrackShortcut { id = 0, fromNode = 36, toNode = 60, width = 7f, entryDist = 24.0f, exitDist = 21.5f };
            sc.nodes.AddRange(new[] { new TrackNode(91.9f, 129.3f, 7f), new TrackNode(62.7f, 143.6f, 7f), new TrackNode(33.4f, 157.9f, 7f) });
            d.shortcuts.Add(sc);
            foreach (float ps in new float[] { 32.0f, 69.0f }) { var f = new TrackFeature(TrackFeatureKind.Boost, ps, 6f, 4f); f.path = 0; d.features.Add(f); }
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.BigArch, 65.0f, 0.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Windmill, 380.0f, -48.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Windmill, 980.0f, 52.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Windmill, 1200.0f, -50.0f, 1.0f));
            return d;
        }

        static TrackDef Canyon()
        {
            var d = new TrackDef
            {
                name = "Canyon Run", nameZh = "峽谷疾馳", seed = 4711, laps = 3, closed = true, checkpointSpacing = 40f,
                theme = 1, relief = 1.5f, hillAmp = 44f, wallStart = 12f, wallEnd = 42f, cliff = 0f, treeDensity = 0.35f,
            };
            AddNodes(d, CanyonRun);
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 65.0f, 8f, 6f, 0f, 18f));
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 487.0f, 8f, 6f, 0f, 18f));
            d.features.Add(Glider(662.0f));
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 1085.0f, 8f, 6f, 0f, 18f));
            d.features.Add(Glider(1135.0f));
            foreach (float ps in new float[] { 29.0f, 130.6f, 428.0f, 536.6f, 623.0f, 939.6f, 1076.0f, 1106.0f }) d.features.Add(new TrackFeature(TrackFeatureKind.Boost, ps, 6f, 5f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.RockArch, 65.0f, 0.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.RockArch, 1100.0f, 0.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 100.0f, 40.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 260.0f, -45.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 420.0f, 42.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 580.0f, -40.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 760.0f, 44.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 920.0f, -42.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Spire, 1080.0f, 40.0f, 1.0f));
            return d;
        }

        static TrackDef Coast()
        {
            var d = new TrackDef
            {
                name = "Coast Loop", nameZh = "海岸環線", seed = 2468, laps = 2, closed = true, checkpointSpacing = 60f,
                theme = 2, relief = 0.8f, hillAmp = 20f, wallStart = 30f, wallEnd = 120f, cliff = 7f, treeDensity = 0.6f,
            };
            AddNodes(d, CoastLoop);
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 199.0f, 8f, 6f, 0f, 18f));
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 619.0f, 8f, 6f, 0f, 18f));
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 991.0f, 8f, 6f, 0f, 18f));
            d.features.Add(new TrackFeature(TrackFeatureKind.Ramp, 1485.0f, 8f, 6f, 0f, 18f));
            d.features.Add(Glider(845.0f));
            foreach (float ps in new float[] { 107.7f, 166.0f, 430.8f, 590.0f, 750.0f, 959.0f, 1069.1f, 1388.2f, 1453.0f }) d.features.Add(new TrackFeature(TrackFeatureKind.Boost, ps, 6f, 5f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.BigArch, 275.0f, 0.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Lighthouse, 300.0f, 26.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Lighthouse, 1050.0f, -26.0f, 1.0f));
            d.landmarks.Add(new TrackLandmark(TrackLandmarkKind.Windmill, 700.0f, 50.0f, 1.0f));
            return d;
        }
    }
}
