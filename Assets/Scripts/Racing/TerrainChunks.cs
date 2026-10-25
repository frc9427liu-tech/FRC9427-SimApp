using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    // 模組 W / M2:地形(高度圖 + 地面材質 + 分塊網格)。見 docs/racing/02-world-and-track.md §1、§3。
    //   RaceNoise    整數雜湊值雜訊(不依賴引擎,同 seed 一定同一張圖)
    //   RaceTerrain  193 x 193 高度圖(每格 4.5 m)+ 離路距離場 + 查詢(HeightAt / NormalAt / SurfaceAt,不 raycast、不配置記憶體)
    //   TerrainChunks 6 x 6 = 36 塊,每塊 LOD0(32x32 格,4.5 m)/ LOD1(8x8 格,18 m)網格(平滑法線 + 頂點色漸層:草 → 土 → 沙 → 岩,路邊漸層)+ 裙邊 + 一份共用頂點的碰撞網格;一張水面
    // 美術(動森 + 瑪利歐賽車):地形遠離路面處加緩起伏(RaceLook.Roll)與階地(RaceLook.Terrace);顏色全由 RaceLook / TrackTheme 決定。起伏只動 w > 0 的部分(離路 > wallStart),路面與刻路區不變。
    // 約定(M5:世界 864 m,格距 4.5 m):格點 (i, j) 的世界座標 x = -432 + 4.5 i、z = -432 + 4.5 j;每個格子的對角線依 (i+j) 奇偶交錯,查詢與網格用同一套切法,所以 HeightAt 和碰撞體完全一致。

    /// 整數雜湊值雜訊(0..1)。只用整數運算與簡單浮點內插。
    internal static class RaceNoise
    {
        public static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)z * 2246822519u + 3266489917u;
                h = (h ^ (h >> 15)) * 2246822519u;
                h = (h ^ (h >> 13)) * 3266489917u;
                return h ^ (h >> 16);
            }
        }

        static float Rnd(int x, int z, int seed) { return (Hash(x, z, seed) & 0xFFFFFFu) / 16777215f; }

        /// 值雜訊 0..1(五次內插)。
        public static float Value(float x, float z, int seed)
        {
            int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z);
            float fx = x - x0, fz = z - z0;
            fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            fz = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);
            float a = Rnd(x0, z0, seed), b = Rnd(x0 + 1, z0, seed), c = Rnd(x0, z0 + 1, seed), d = Rnd(x0 + 1, z0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        /// 分形疊加,結果正規化到 0..1。
        public static float Fbm(float x, float z, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(x, z, seed + o * 101) * amp;
                norm += amp;
                x = x * 2.03f + 17.3f; z = z * 2.03f - 9.1f; amp *= 0.5f;
            }
            return sum / norm;
        }
    }

    /// 高度圖與地面查詢。純資料 + 數學(不用 Unity 物件),-batchmode -nographics 可以直接測。
    public sealed class RaceTerrain
    {
        public const int N = 193;            // 每邊格點數
        public const float Cell = 4.5f;      // 格距 m(M5:世界放大到 864 x 864 m,格子數不變 = 三角形 / 區塊預算不變)
        public const float Half = 432f;      // 地圖半邊長 m(= 96 格 x Cell)
        public const float Sc = Half / 192f; // 相對舊 384 m 地圖的放大倍率(雜訊波長用)
        public const float WaterY = 0f;      // 海平面
        const float BorderLim = Half - 4f;   // 超過這裡(而且是陸地)= OutOfBounds

        public readonly float[] H = new float[N * N];      // 地形原始高度(可低於 0 = 海底)
        public readonly float[] Edge = new float[N * N];   // 離「路面邊緣」的距離 m(負 = 在路面內);路面邊緣 = 離中心線 hw + 1(含路緣)
        public float[] ScEdge;                              // 離「捷徑邊緣」的距離 m;沒有捷徑 = null
        public int Seed;
        public bool Mirror;                                 // 鏡像賽道:雜訊用 -x 取樣 → 整張高度圖是不鏡像版本的左右鏡射
        float relief = 1f, hillAmp = 24f, wallStart = 24f, wallEnd = 110f, cliff;
        public float MinH, MaxH;
        public float RoadMinY, RoadMaxY, RoadMaxSlopeDeg;   // 路面剖面統計(測試與報告用)
        RaceLook look;                                      // 風格(Generate 時固定下來;緩起伏 / 階地參數)

        // ------------------------------------------------------------------ 查詢(不配置記憶體)

        /// 地形原始高度(三角形內插,與碰撞網格完全一致)。超出範圍夾到邊緣。
        public float Raw(float x, float z)
        {
            float fx = (x + Half) * (1f / Cell), fz = (z + Half) * (1f / Cell);
            if (fx < 0f) fx = 0f; else if (fx > N - 1.0001f) fx = N - 1.0001f;
            if (fz < 0f) fz = 0f; else if (fz > N - 1.0001f) fz = N - 1.0001f;
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            int k = j * N + i;
            float h00 = H[k], h10 = H[k + 1], h01 = H[k + N], h11 = H[k + N + 1];
            if (((i + j) & 1) == 0)
            {
                // 對角線 00-11
                if (u >= v) return h00 + (h10 - h00) * u + (h11 - h10) * v;
                return h00 + (h11 - h01) * u + (h01 - h00) * v;
            }
            // 對角線 10-01
            if (u + v <= 1f) return h00 + (h10 - h00) * u + (h01 - h00) * v;
            return h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
        }

        /// 車子該踩的高度:陸地 = 地形;水面下 = 海平面(車浮在水上,SurfaceAt 會是 Water 觸發重置)。
        public float Height(float x, float z)
        {
            float h = Raw(x, z);
            return h > WaterY ? h : WaterY;
        }

        /// 平滑法線(±1 m 中央差分),水面上是 (0,1,0)。
        public Vector3 Normal(float x, float z)
        {
            const float d = 1f;
            float hl = Height(x - d, z), hr = Height(x + d, z), hd = Height(x, z - d), hu = Height(x, z + d);
            var n = new Vector3(hl - hr, 2f * d, hd - hu);
            return n.normalized;
        }

        public float EdgeAt(float x, float z)
        {
            float fx = (x + Half) * (1f / Cell), fz = (z + Half) * (1f / Cell);
            if (fx < 0f) fx = 0f; else if (fx > N - 1.0001f) fx = N - 1.0001f;
            if (fz < 0f) fz = 0f; else if (fz > N - 1.0001f) fz = N - 1.0001f;
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            int k = j * N + i;
            float a = Edge[k] + (Edge[k + 1] - Edge[k]) * u;
            float b = Edge[k + N] + (Edge[k + N + 1] - Edge[k + N]) * u;
            return a + (b - a) * v;
        }

        /// 離捷徑邊緣的距離(沒有捷徑 = 999)。
        public float ScEdgeAt(float x, float z)
        {
            if (ScEdge == null) return 999f;
            float fx = (x + Half) * (1f / Cell), fz = (z + Half) * (1f / Cell);
            if (fx < 0f) fx = 0f; else if (fx > N - 1.0001f) fx = N - 1.0001f;
            if (fz < 0f) fz = 0f; else if (fz > N - 1.0001f) fz = N - 1.0001f;
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            int k = j * N + i;
            float a = ScEdge[k] + (ScEdge[k + 1] - ScEdge[k]) * u;
            float b = ScEdge[k + N] + (ScEdge[k + N + 1] - ScEdge[k + N]) * u;
            return a + (b - a) * v;
        }

        public Surface SurfaceAt(float x, float z)
        {
            float raw = Raw(x, z);
            if (raw < WaterY) return Surface.Water;                              // 海(含地圖外夾到邊緣的那一圈,邊緣一定是海)
            if (x < -BorderLim || x > BorderLim || z < -BorderLim || z > BorderLim) return Surface.OutOfBounds;
            float e = EdgeAt(x, z);
            if (e <= 0f) return Surface.Road;       // 含路緣
            float es = ScEdgeAt(x, z);
            if (es <= 0f) return Surface.Dirt;      // 捷徑路面 = 土路(比較慢,要靠加速板補回來)
            if (e <= 2.5f || es <= 2.5f) return Surface.Dirt;     // 路肩(沙色)
            if (raw < 0.9f) return Surface.Sand;    // 海灘
            return Surface.Grass;
        }

        // ------------------------------------------------------------------ 生成

        /// 依賽道與種子生成地形,並把算好的路面高度設回 track(track.ApplyHeights;捷徑用 track.ApplyShortcutHeights)。
        public static RaceTerrain Generate(RaceTrack track, int seed, bool mirror = false)
        {
            var t = new RaceTerrain { Seed = seed, Mirror = mirror, look = RaceLook.Current };
            t.relief = track.Relief; t.hillAmp = track.HillAmp; t.wallStart = track.WallStart; t.wallEnd = Mathf.Max(track.WallEnd, track.WallStart + 1f); t.cliff = track.Cliff;
            TrackSample[] sm = track.Raw;
            int n = sm.Length;

            // 1) 路面剖面:取中心線上的「自然高度」,平滑、墊高到海面以上、限制坡度
            float[] ys = t.BuildRoadProfile(sm, track.SampleStep, track.Closed, track);
            track.ApplyHeights(ys);

            // 2) 取樣點放進格子(54 m),加速「最近中心線」查詢
            const float BC = 24f * Sc; const int BN = 16;
            var bStart = new int[BN * BN + 1];
            var bCell = new int[n];
            for (int i = 0; i < n; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt((sm[i].Pos.x + Half) / BC), 0, BN - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((sm[i].Pos.z + Half) / BC), 0, BN - 1);
                bCell[i] = cz * BN + cx; bStart[bCell[i] + 1]++;
            }
            for (int c = 0; c < BN * BN; c++) bStart[c + 1] += bStart[c];
            var bItems = new int[n]; var fill = new int[BN * BN];
            for (int i = 0; i < n; i++) { int c = bCell[i]; bItems[bStart[c] + fill[c]++] = i; }

            // 3) 每個格點:離路距離 → 自然地形 → 刻路
            float wideK = track.Gaps.Count > 0 ? 2f : 0f;   // M8 台地:只在有缺口的賽道(路面比自然地形高很多)把邊坡放寬;手工賽道 0..2 維持原樣
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = -Half + i * Cell, z = -Half + j * Cell;
                    float a, hw, roadH, lat, tanB;
                    NearestRoad(sm, ys, bStart, bItems, BN, BC, x, z, out a, out hw, out roadH, out lat, out tanB);
                    float w = Smooth(t.wallStart, t.wallEnd, a);   // 離路越遠,丘陵越高
                    float nat = t.Natural(x, z, w);
                    float flatR = hw + 5f;                          // 路面 + 路緣 + 路肩 + 護欄都壓平
                    float blendW = 12f + wideK * Mathf.Clamp(Mathf.Abs(roadH - nat) - 3f, 0f, 14f);   // M8 台地:路面比自然地形高很多時邊坡放寬
                    float tt = Mathf.Clamp01((a - flatR) / blendW);   // 再用 12 m 的 smoothstep 接回自然地形
                    float k = 1f - tt * tt * (3f - 2f * tt);
                    float core = 1f - Smooth(hw, hw + 3f, a);
                    float target = roadH - (tanB != 0f ? lat * Mathf.Clamp(tanB, -0.3f, 0.3f) : 0f) - 0.025f * core;           // M8:側傾 // 地形比路面網格低 2.5 cm,路面不會被地形蓋住
                    float h = Mathf.Lerp(nat, target, k);
                    int idx = j * N + i;
                    t.H[idx] = h;
                    t.Edge[idx] = a - (hw + 1f);
                }

            // 3b) 捷徑:另外刻一條路(主線附近維持主線高度)
            if (track.Shortcuts.Count > 0) t.CarveShortcuts(track, ys);

            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < t.H.Length; i++) { float h = t.H[i]; if (h < mn) mn = h; if (h > mx) mx = h; }
            t.MinH = mn; t.MaxH = mx;

            // 4) 取樣點高度改成「實際地形高度 + 2.5 cm」:HeightAt 與取樣點的差恆為 2.5 cm,路面網格緊貼真正的地形
            var ys2 = new float[n];
            for (int i = 0; i < n; i++) ys2[i] = t.Raw(sm[i].Pos.x, sm[i].Pos.z) + 0.025f;
            track.ApplyHeights(ys2);
            for (int k = 0; k < track.Shortcuts.Count; k++)
            {
                ShortcutPath sc = track.Shortcuts[k];
                var yk = new float[sc.Count];
                for (int i = 0; i < yk.Length; i++) yk[i] = t.Raw(sc.Pos[i].x, sc.Pos[i].z) + 0.025f;
                track.ApplyShortcutHeights(k, yk);
            }
            return t;
        }

        /// 捷徑的地形:目標高度 = 兩端主線高度的 smoothstep 內插;靠近主線(離主線路緣 < 9 m)的地方維持主線的高度,避免動到主線路面。
        void CarveShortcuts(RaceTrack track, float[] ys)
        {
            ScEdge = new float[N * N];
            for (int i = 0; i < ScEdge.Length; i++) ScEdge[i] = 999f;
            int nMain = ys.Length;
            float step = track.SampleStep;
            for (int k = 0; k < track.Shortcuts.Count; k++)
            {
                ShortcutPath sc = track.Shortcuts[k];
                float hA = ys[Mathf.RoundToInt(sc.FromS / step) % nMain], hB = ys[Mathf.RoundToInt(sc.ToS / step) % nMain];
                int cnt = sc.Count;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, maxHw = 0f;
                for (int i = 0; i < cnt; i++)
                {
                    float u = i / (float)(cnt - 1);
                    sc.Nominal[i] = Mathf.Lerp(hA, hB, u * u * (3f - 2f * u));
                    Vector3 p = sc.Pos[i];
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x; if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                    if (sc.W[i] * 0.5f > maxHw) maxHw = sc.W[i] * 0.5f;
                }
                float R = maxHw + 5f + 12f + 2f;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((minX - R + Half) / Cell)), i1 = Mathf.Min(N - 1, Mathf.CeilToInt((maxX + R + Half) / Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((minZ - R + Half) / Cell)), j1 = Mathf.Min(N - 1, Mathf.CeilToInt((maxZ + R + Half) / Cell));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = -Half + i * Cell, z = -Half + j * Cell;
                        int ia; float u, hw;
                        float a = sc.Nearest(x, z, out ia, out u, out hw);
                        int idx = j * N + i;
                        float edge = a - (hw + 1f);
                        if (edge < ScEdge[idx]) ScEdge[idx] = edge;
                        float flatR = hw + 5f;
                        if (a > flatR + 12f) continue;
                        float roadH = Mathf.Lerp(sc.Nominal[ia], sc.Nominal[ia + 1], u);
                        float tt = Mathf.Clamp01((a - flatR) / 12f);
                        float kk = 1f - tt * tt * (3f - 2f * tt);
                        float core = 1f - Smooth(hw, hw + 3f, a);
                        float target = roadH - 0.025f * core;
                        kk *= Smooth(1f, 9f, Edge[idx]);
                        H[idx] = Mathf.Lerp(H[idx], target, kk);
                    }
            }
        }

        static float Smooth(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        /// 沒有賽道影響時的地形高度。w = 丘陵權重(0 = 賽道旁,1 = 遠離賽道)。座標單位 m;雜訊波長依 Sc 放大(整張圖比舊版大 Sc 倍,形狀相似)。
        float Natural(float x, float z, float w)
        {
            if (Mirror) x = -x;
            // 島嶼遮罩:圓角方形距離 + 雜訊扭曲;m = 0.8 以內全是陸地,m = 1.0 沉到海底(懸崖模式:0.84~0.90 就陡降)
            float ax = Mathf.Abs(x) * (1f / Half), az = Mathf.Abs(z) * (1f / Half);
            float ax2 = ax * ax, az2 = az * az;
            float m = Mathf.Sqrt(Mathf.Sqrt(ax2 * ax2 + az2 * az2));
            m += 0.04f * (RaceNoise.Fbm(x * (0.012f / Sc) + 31f, z * (0.012f / Sc) + 17f, 2, Seed + 7) * 2f - 1f);
            float f = cliff > 0.5f ? 1f - Smooth(0.84f, 0.90f, m) : 1f - Smooth(0.80f, 1.0f, m);

            float big = RaceNoise.Fbm(x * (1f / (150f * Sc)), z * (1f / (150f * Sc)), 2, Seed + 1) * 2f - 1f;
            float hl = RaceNoise.Fbm(x * (1f / (58f * Sc)) + 7.3f, z * (1f / (58f * Sc)) - 3.1f, 2, Seed + 2);
            float det = RaceNoise.Value(x * (1f / (14f * Sc)), z * (1f / (14f * Sc)), Seed + 3) * 2f - 1f;
            float hh = hl * hl;
            if (look != null && look.Terrace > 0.01f && w > 0f) hh = Mathf.Lerp(hh, Terraced(hh, look.TerraceSteps), look.Terrace);   // 階地化:丘陵一階一階(方山 / 梯田)
            float landBase = 3.4f + cliff + 2.2f * big + w * (hillAmp * hh) + 0.45f * (0.3f + 0.7f * w) * det;
            float land = landBase;
            if (look != null && w > 0f)
            {
                // 緩起伏:只在遠離路面處(w > 0)加,而且不會把原本 >= 1.8 m 的陸地壓到海面附近(不產生內陸水坑)
                float roll = look.Roll * w * (RaceNoise.Fbm(x * (1f / (34f * Sc)) + 71f, z * (1f / (34f * Sc)) - 43f, 2, Seed + 9) * 2f - 1f);
                land = landBase + roll;
                if (roll < 0f) land = Mathf.Max(land, Mathf.Min(landBase, 1.8f));
            }
            float sea = -3.6f - 1.0f * RaceNoise.Fbm(x * (1f / (90f * Sc)), z * (1f / (90f * Sc)), 2, Seed + 4);
            return sea + (land - sea) * f;
        }

        /// 平滑的階梯函數:0..1 切成 steps 階,階面平、階與階之間用 smoothstep 圓角接起來(單調、連續)。
        static float Terraced(float h, int steps)
        {
            float st = Mathf.Max(1, steps);
            float t = Mathf.Clamp01(h) * st;
            float f = Mathf.Floor(t);
            float u = Mathf.Clamp01((t - f - 0.3f) / 0.4f);
            u = u * u * (3f - 2f * u);
            return (f + u) / st;
        }

        public const float GapPlateauRamp = 200f;
        /// 第 gi 個缺口的台地高度 m(9..12,由種子決定)。
        public static float GapPlateauRise(int seed, int gi)
        {
            unchecked { uint h = (uint)(seed * 2654435761u + (uint)gi * 40503u + 17u); h ^= h >> 13; h *= 1274126177u; h ^= h >> 16; return 9f + 3f * ((h & 1023u) / 1023f); }
        }

        float[] BuildRoadProfile(TrackSample[] sm, float step, bool closed, RaceTrack track)
        {
            int n = sm.Length;
            var ys = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = sm[i].Pos.x, z = sm[i].Pos.z;
                float xn = Mirror ? -x : x;
                ys[i] = Natural(x, z, 0f) + relief * 3.6f * (RaceNoise.Fbm(xn * (1f / (60f * Sc)) + 50f, z * (1f / (60f * Sc)) - 20f, 2, Seed + 5) * 2f - 1f);
            }
            // M8:缺口台地。每個缺口前 ~200 m 緩緩爬升 9~12 m、整段缺口 + 落地區維持高台、之後緩降 -> 每條有缺口的賽道至少一次明顯的爬坡 / 下坡,
            // 缺口的深坑兩側是高台懸崖(看得到深度)。坡度由下面 5.5 度的限制器保證。
            if (track != null && track.Gaps.Count > 0)
            {
                float L = track.Length;
                for (int gi = 0; gi < track.Gaps.Count; gi++)
                {
                    GapInfo g = track.Gaps[gi];
                    float pl0 = g.RampBase - 40f, pl1 = g.S1 + 40f, c = (pl0 + pl1) * 0.5f, half = (pl1 - pl0) * 0.5f;
                    float rise = GapPlateauRise(Seed, gi);
                    // 台地高度 = 缺口範圍內自然路面高度的平均 + rise;缺口範圍壓成完全水平(起飛 / 落地同高,飛行距離可預測),兩側 200 m 內平滑接回
                    float sum = 0f; int cnt = 0;
                    for (int i = 0; i < n; i++)
                    {
                        float d = (sm[i].S - c) % L; if (d > L * 0.5f) d -= L; else if (d < -L * 0.5f) d += L;
                        if (Mathf.Abs(d) <= half) { sum += ys[i]; cnt++; }
                    }
                    float target = (cnt > 0 ? sum / cnt : 3f) + rise;
                    for (int i = 0; i < n; i++)
                    {
                        float d = (sm[i].S - c) % L; if (d > L * 0.5f) d -= L; else if (d < -L * 0.5f) d += L;
                        float dist = Mathf.Max(0f, Mathf.Abs(d) - half);
                        if (dist >= GapPlateauRamp) continue;
                        float w = 1f - Smooth(0f, GapPlateauRamp, dist);
                        ys[i] = Mathf.Lerp(ys[i], target, w);
                    }
                }
            }
            var tmp = new float[n];
            for (int pass = 0; pass < 2; pass++)   // 週期性方框平滑,半徑 5 個取樣點(約 10 m)
            {
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int k = -5; k <= 5; k++) sum += ys[((i + k) % n + n) % n];
                    tmp[i] = sum / 11f;
                }
                System.Array.Copy(tmp, ys, n);
            }
            for (int i = 0; i < n; i++) if (ys[i] < 1.4f) ys[i] = 1.4f;   // 離海面至少 1.4 m

            // 坡度限制 ≤ 5.5 度(規格是 12 度,留餘量):來回掃幾遍(封閉賽道,索引環繞)
            float maxD = Mathf.Tan(5.5f * Mathf.Deg2Rad) * step;
            for (int sweep = 0; sweep < 8; sweep++)
            {
                for (int q = 0; q < 2 * n; q++)
                {
                    int i = q % n, j = (i + 1) % n;
                    if (ys[j] - ys[i] > maxD) ys[j] = ys[i] + maxD; else if (ys[i] - ys[j] > maxD) ys[j] = ys[i] - maxD;
                }
                for (int q = 2 * n; q > 0; q--)
                {
                    int i = q % n, j = ((q - 1) % n + n) % n;
                    if (ys[j] - ys[i] > maxD) ys[j] = ys[i] + maxD; else if (ys[i] - ys[j] > maxD) ys[j] = ys[i] - maxD;
                }
            }
            for (int pass = 0; pass < 2; pass++)   // 收尾再平滑(只會讓坡度更小)
            {
                for (int i = 0; i < n; i++) tmp[i] = (ys[(i + n - 1) % n] + 2f * ys[i] + ys[(i + 1) % n]) * 0.25f;
                System.Array.Copy(tmp, ys, n);
            }
            float lo = float.MaxValue, hi = float.MinValue, sl = 0f;
            for (int i = 0; i < n; i++)
            {
                lo = Mathf.Min(lo, ys[i]); hi = Mathf.Max(hi, ys[i]);
                sl = Mathf.Max(sl, Mathf.Abs(ys[(i + 1) % n] - ys[i]) / step);
            }
            RoadMinY = lo; RoadMaxY = hi; RoadMaxSlopeDeg = Mathf.Atan(sl) * Mathf.Rad2Deg;
            return ys;
        }

        /// 離中心線(折線)最近的點:回傳水平距離 a、該處路寬一半 hw、該處剖面高度 roadH。
        static void NearestRoad(TrackSample[] sm, float[] ys, int[] bStart, int[] bItems, int BN, float BC, float x, float z, out float a, out float hw, out float roadH, out float lat, out float tanB)
        {
            int n = sm.Length;
            int cx = Mathf.Clamp(Mathf.FloorToInt((x + Half) / BC), 0, BN - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z + Half) / BC), 0, BN - 1);
            int best = -1; float bd = float.MaxValue;
            for (int r = 1; ; r = r < 4 ? r * 2 : r + 4)
            {
                if (r > BN) r = BN;
                best = -1; bd = float.MaxValue;
                int x0 = Mathf.Max(0, cx - r), x1 = Mathf.Min(BN - 1, cx + r), z0 = Mathf.Max(0, cz - r), z1 = Mathf.Min(BN - 1, cz + r);
                for (int zz = z0; zz <= z1; zz++)
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        int c = zz * BN + xx;
                        for (int k = bStart[c]; k < bStart[c + 1]; k++)
                        {
                            int i = bItems[k];
                            float ex = sm[i].Pos.x - x, ez = sm[i].Pos.z - z;
                            float d = ex * ex + ez * ez;
                            if (d < bd) { bd = d; best = i; }
                        }
                    }
                if (best >= 0 && bd <= (r * BC) * (r * BC)) break;   // 範圍內最近的一定是全域最近
                if (r >= BN) break;
            }
            if (best < 0) { best = 0; }

            float bestD = float.MaxValue; a = 0f; hw = 6f; roadH = ys[best]; lat = 0f; tanB = 0f;
            for (int seg = 0; seg < 2; seg++)
            {
                int ia = seg == 0 ? (best - 1 + n) % n : best;
                int ib = seg == 0 ? best : (best + 1) % n;
                float ax = sm[ia].Pos.x, az = sm[ia].Pos.z;
                float abx = sm[ib].Pos.x - ax, abz = sm[ib].Pos.z - az;
                float len2 = abx * abx + abz * abz;
                float u = len2 > 1e-9f ? Mathf.Clamp01(((x - ax) * abx + (z - az) * abz) / len2) : 0f;
                float px = ax + abx * u - x, pz = az + abz * u - z;
                float d2 = px * px + pz * pz;
                if (d2 < bestD)
                {
                    bestD = d2;
                    hw = Mathf.Lerp(sm[ia].Width, sm[ib].Width, u) * 0.5f;
                    roadH = Mathf.Lerp(ys[ia], ys[ib], u);
                    float bkDeg = Mathf.Lerp(sm[ia].Bank, sm[ib].Bank, u);
                    tanB = bkDeg != 0f ? Mathf.Tan(bkDeg * Mathf.Deg2Rad) : 0f;
                    // 有號橫向位移(右正):最近點 -> 格點 在切線右方向上的分量
                    Vector3 rt = Vector3.Lerp(sm[ia].Right, sm[ib].Right, u);
                    lat = -(px * rt.x + pz * rt.z);
                }
            }
            a = Mathf.Sqrt(bestD);
        }

        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < H.Length; i++) { h ^= (uint)Mathf.RoundToInt(H[i] * 1000f); h *= 1099511628211UL; }
                for (int i = 0; i < Edge.Length; i += 3) { h ^= (uint)Mathf.RoundToInt(Edge[i] * 100f); h *= 1099511628211UL; }
                if (ScEdge != null) for (int i = 0; i < ScEdge.Length; i += 3) { h ^= (uint)Mathf.RoundToInt(Mathf.Clamp(ScEdge[i], -50f, 50f) * 100f); h *= 1099511628211UL; }
            }
            return h;
        }
    }

    /// 把地形切成 6 x 6 塊,每塊產生 LOD0 / LOD1 平面著色網格、碰撞網格,並建水面。
    internal static class TerrainChunks
    {
        public const int ChunkCells = 32;                  // 每塊 32 x 32 格(64 m)
        public const int Chunks = 6;
        public const int Lod1Stride = 4;                   // LOD1:每 4 格取一點 → 8 x 8 格(8 m)
        const float SkirtLod0 = 3f, SkirtLod1 = 6f;        // 裙邊下垂深度
        const float MinChunkTop = -0.5f;                   // 整塊都低於這個高度 = 全在水下,不建

        // 調色盤索引
        const int PGrassLight = 0, PGrassDark = 1, PSand = 2, PRock = 3, PSeabed = 4, PGrassHigh = 5, PWhite = 6, PalSize = 8;

        public sealed class BuildStats { public int Chunks, Colliders, Lod0Tris, Lod1Tris, ColliderTris; }

        public static Texture2D MakePalette()
        {
            var tex = new Texture2D(PalSize, 1, TextureFormat.RGBA32, false);
            tex.name = "RaceTerrainPalette";
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var c = new Color[PalSize];
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
            for (int i = 0; i < PalSize; i++) c[i] = th.GrassLight;
            c[PGrassLight] = th.GrassLight;
            c[PGrassDark] = th.GrassDark;
            c[PSand] = th.Sand;
            c[PRock] = th.Rock;
            c[PSeabed] = th.Seabed;
            c[PGrassHigh] = th.GrassHigh;
            c[PWhite] = Color.white;   // 網格有頂點色時 uv 指向這格(顏色全由頂點色決定)
            tex.SetPixels(c);
            tex.Apply(false, false);
            return tex;
        }

        public static Material MakeMaterial(Texture2D pal)
        {
            var toon = RaceShaders.Terrain(pal); if (toon != null) return toon;   // 自製地形 shader(找不到會回退到下面的 Standard)
            Material m = FieldBuilder.MakeMat(Color.white, 0.05f, 0f);
            m.name = "RaceTerrainMat";
            m.mainTexture = pal;
            return m;
        }

        static int PalIdx(int cx, int cz, float h0, float h1, float h2, float ny)
        {
            float mx = Mathf.Max(h0, Mathf.Max(h1, h2));
            float avg = (h0 + h1 + h2) * (1f / 3f);
            if (mx < RaceTerrain.WaterY) return PSeabed;
            if (avg < 0.9f) return PSand;
            if (ny < 0.72f || avg > 13.5f) return PRock;
            if (avg > 8f) return PGrassHigh;
            return (((cx >> 2) + (cz >> 2)) & 1) == 0 ? PGrassLight : PGrassDark;
        }

        static readonly Vector2[] PalUv = MakePalUv();
        static Vector2[] MakePalUv()
        {
            var a = new Vector2[PalSize];
            for (int i = 0; i < PalSize; i++) a[i] = new Vector2((i + 0.5f) / PalSize, 0.5f);
            return a;
        }

        // 格子角編號 0=(0,0) 1=(1,0) 2=(0,1) 3=(1,1);同一個格子的兩個三角形
        static readonly int[] EvenTris = { 0, 1, 3, 0, 3, 2 };
        static readonly int[] OddTris = { 0, 1, 2, 3, 2, 1 };

        public static GameObject Build(RaceTerrain t, Transform parent, RaceQuality q, List<Object> owned, List<RaceWorldLod.Chunk> chunksOut, out BuildStats stats)
        {
            stats = new BuildStats();
            var root = new GameObject("Terrain");
            if (parent != null) root.transform.SetParent(parent, false);

            Texture2D pal = MakePalette(); owned.Add(pal);
            Material mat = MakeMaterial(pal); owned.Add(mat);
            // 頂點色:只有自製地形 shader 讀得到;後備的 Standard 材質改走調色盤 uv
            bool useVc = mat != null && mat.shader != null && mat.shader.name == "FrcSim/RaceTerrain";
            TrackTheme th0 = TrackTheme.Current ?? TrackTheme.Get(0);
            Color32[] cols = useVc ? BuildColors(t, RaceLook.For(th0), th0) : null;

            for (int cz = 0; cz < Chunks; cz++)
                for (int cx = 0; cx < Chunks; cx++)
                {
                    int i0 = cx * ChunkCells, j0 = cz * ChunkCells;
                    float top = float.MinValue, bot = float.MaxValue;
                    for (int j = j0; j <= j0 + ChunkCells; j++)
                        for (int i = i0; i <= i0 + ChunkCells; i++)
                        {
                            float h = t.H[j * RaceTerrain.N + i];
                            if (h > top) top = h; if (h < bot) bot = h;
                        }
                    if (top < MinChunkTop) continue;   // 全在水下:海面蓋住就好

                    var go = new GameObject("Chunk_" + cx + "_" + cz);
                    go.transform.SetParent(root.transform, false);
                    Mesh m0 = BuildRenderMesh(t, cols, i0, j0, 1, ChunkCells, SkirtLod0, "ChunkLod0_" + cx + "_" + cz, false);
                    Mesh m1 = BuildRenderMesh(t, cols, i0, j0, Lod1Stride, ChunkCells / Lod1Stride, SkirtLod1, "ChunkLod1_" + cx + "_" + cz, true);
                    Mesh mc = BuildColliderMesh(t, i0, j0, "ChunkCol_" + cx + "_" + cz);
                    owned.Add(m0); owned.Add(m1); owned.Add(mc);

                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = true;
                    mf.sharedMesh = q == RaceQuality.Low ? m1 : m0;

                    var col = go.AddComponent<MeshCollider>();
                    col.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
                    col.sharedMesh = mc;
                    // 刻意不掛 RaceSurface:K 的探地會優先採用碰撞體上的標記,那樣路面會被當成草地;沒有標記才會問 GroundSampler(RaceWorld.SampleGround)

                    var b = new Bounds();
                    b.SetMinMax(new Vector3(-RaceTerrain.Half + i0 * RaceTerrain.Cell, bot, -RaceTerrain.Half + j0 * RaceTerrain.Cell),
                                new Vector3(-RaceTerrain.Half + (i0 + ChunkCells) * RaceTerrain.Cell, top, -RaceTerrain.Half + (j0 + ChunkCells) * RaceTerrain.Cell));
                    chunksOut.Add(new RaceWorldLod.Chunk { Filter = mf, Lod0 = m0, Lod1 = m1, Bounds = b, Tris0 = (int)(m0.GetIndexCount(0) / 3), Tris1 = (int)(m1.GetIndexCount(0) / 3), Current = q == RaceQuality.Low ? 1 : 0 });
                    stats.Chunks++; stats.Colliders++;
                    stats.Lod0Tris += (int)(m0.GetIndexCount(0) / 3); stats.Lod1Tris += (int)(m1.GetIndexCount(0) / 3); stats.ColliderTris += (int)(mc.GetIndexCount(0) / 3);
                }

            BuildWater(root.transform, owned);
            return root;
        }

        // ------------------------------------------------------------------ 水面

        static void BuildWater(Transform parent, List<Object> owned)
        {
            const float R = 2600f, Y = -0.03f;
            var m = new Mesh { name = "RaceWater" };
            m.vertices = new[] { new Vector3(-R, Y, -R), new Vector3(-R, Y, R), new Vector3(R, Y, R), new Vector3(R, Y, -R) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            owned.Add(m);
            var g = new GameObject("Water");
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceShaders.Water() ?? RaceArt.Mat((TrackTheme.Current ?? TrackTheme.Get(0)).Water, 0.55f);
            RaceShaders.SetWaterColor((TrackTheme.Current ?? TrackTheme.Get(0)).Water);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ------------------------------------------------------------------ 網格

        // ------------------------------------------------------------------ 頂點色(地面顏色)

        static float Sm(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        /// 整張 193 x 193 格點的地面顏色(LOD0 / LOD1 共用同一份,所以切換 LOD 顏色不會跳)。
        static Color32[] BuildColors(RaceTerrain t, RaceLook lk, TrackTheme th)
        {
            int N = RaceTerrain.N;
            var cols = new Color32[N * N];
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    Color c = GroundColor(t, lk, th, i, j);
                    if (lin) c = c.linear;
                    c.a = Mathf.Clamp01(c.a);   // alpha = 熔岩裂縫遮罩(1 = 無裂縫)
                    cols[j * N + i] = c;
                }
            return cols;
        }

        /// 一個格點的地面色:草(兩種綠的大塊柔和斑塊)→ 高處草 → 緩坡加深 → 沙灘(含濕沙)→ 海床 → 陡坡 / 高處岩石(含橫紋)→ 路邊漸層 / 捷徑土路。
        static Color GroundColor(RaceTerrain t, RaceLook lk, TrackTheme th, int i, int j)
        {
            int N = RaceTerrain.N;
            int idx = j * N + i;
            float h = t.H[idx];
            float x = -RaceTerrain.Half + i * RaceTerrain.Cell, z = -RaceTerrain.Half + j * RaceTerrain.Cell;
            if (t.Mirror) x = -x;   // 鏡像賽道:色塊也是左右鏡射的
            int ia = Mathf.Max(0, i - 1), ib = Mathf.Min(N - 1, i + 1), ja = Mathf.Max(0, j - 1), jb = Mathf.Min(N - 1, j + 1);
            float gx = (t.H[j * N + ib] - t.H[j * N + ia]) / ((ib - ia) * RaceTerrain.Cell);
            float gz = (t.H[jb * N + i] - t.H[ja * N + i]) / ((jb - ja) * RaceTerrain.Cell);
            float slope = Mathf.Sqrt(gx * gx + gz * gz);

            int seed = t.Seed;
            float nA = RaceNoise.Value(x * 0.017f + 11.3f, z * 0.017f - 5.7f, seed + 21);   // 約 60 m 的大斑塊
            float nB = RaceNoise.Value(x * 0.055f - 3.1f, z * 0.055f + 8.9f, seed + 22);    // 約 18 m 的小斑塊
            float nC = RaceNoise.Value(x * 0.006f + 2.2f, z * 0.006f - 9.4f, seed + 23);    // 超大尺度明暗

            float gt = Sm(0.30f, 0.70f, nA) * 0.75f + nB * 0.25f;
            Color ground = Color.Lerp(th.GrassLight, th.GrassDark, gt);
            ground = Color.Lerp(ground, th.GrassHigh, Sm(6f, 15f, h) * 0.8f);
            ground = Color.Lerp(ground, th.GrassDark, Sm(0.18f, 0.5f, slope) * 0.5f);      // 緩坡暗一點、平台亮一點 → 階地看得出層次

            // 沙灘:越靠近海面越沙,海面附近是濕沙
            Color wet = Color.Lerp(th.Sand, th.Dirt, 0.45f);
            Color beach = Color.Lerp(wet, th.Sand, Sm(0f, 0.6f, h));
            ground = Color.Lerp(ground, beach, 1f - Sm(lk.BeachLow, lk.BeachHigh, h));
            ground = Color.Lerp(ground, th.Seabed, Sm(0f, -2.4f, h));                    // 水下 → 海床

            // 岩石:陡坡與高處,帶橫紋
            float rockT = Sm(lk.RockStart, lk.RockStart + 0.35f, slope);
            rockT = Mathf.Max(rockT, Sm(15f, 26f, h) * 0.55f);
            if (rockT > 0.001f && h > -0.5f)
            {
                float strata = Mathf.Sin(h * lk.StrataFreq + nB * 3f);
                Color rc = Color.Lerp(th.Rock, th.MtnRock, 0.35f);
                float k = 1f + lk.Strata * strata;
                // 岩面細節:高頻顆粒(約 4 m)+ 曲率明暗(凹處暗、凸稜亮),讓大塊岩壁不再是光滑色塊
                float grain = RaceNoise.Value(x * 0.26f + 7.7f, z * 0.26f - 2.9f, seed + 26) - 0.5f;
                float lap = (t.H[j * N + ib] + t.H[j * N + ia] + t.H[jb * N + i] + t.H[ja * N + i] - 4f * h) / (RaceTerrain.Cell * RaceTerrain.Cell);
                k *= 1f + 0.30f * grain - Mathf.Clamp(lap * 2.2f, -0.28f, 0.28f);
                rc = new Color(rc.r * k, rc.g * k, rc.b * k, 1f);
                ground = Color.Lerp(ground, rc, rockT);
            }

            // 路邊漸層:路肩沙 / 土色 → 草(寬度與強度依風格)
            float e = t.Edge[idx];
            Color verge = Color.Lerp(th.Sand, th.Dirt, 0.4f);
            ground = Color.Lerp(ground, verge, (1f - Sm(2f, lk.VergeWidth, e)) * lk.VergeAmount);
            if (t.ScEdge != null)
            {
                float es = t.ScEdge[idx];
                if (es < 6f) ground = Color.Lerp(ground, th.Dirt, (1f - Sm(1f, 5f, es)) * 0.9f);
            }

            float e0 = t.Edge[idx];
            // 額外的中尺度變化(約 9 m):亮色小丘 / 暗色三葉草叢,避免一整片平色
            float nD = RaceNoise.Value(x * 0.11f + 4.4f, z * 0.11f - 1.3f, seed + 24);
            if (lk.Kind == LookKind.Snow)
            {
                // 冰面斑塊(偏青藍)+ 路邊髒雪帶
                float ice = Sm(0.60f, 0.78f, RaceNoise.Value(x * 0.03f - 8.8f, z * 0.03f + 3.9f, seed + 25)) * Sm(5f, 12f, e0);
                ground = Color.Lerp(ground, new Color(0.62f, 0.83f, 0.94f), ice * 0.75f);
                ground = Color.Lerp(ground, new Color(0.76f, 0.84f, 0.92f), Sm(0.55f, 0.8f, nD) * 0.35f);
            }
            else if (lk.Kind != LookKind.Volcano && lk.Kind != LookKind.Desert)
            {
                ground = Color.Lerp(ground, th.GrassHigh, Sm(0.62f, 0.85f, nD) * 0.5f * Sm(1.5f, 6f, e0));
                ground = Color.Lerp(ground, th.GrassDark, (1f - Sm(0.18f, 0.40f, nD)) * 0.30f * Sm(1.5f, 6f, e0));
            }
            float crack = 0f;
            if (lk.CrackAmount > 0.01f && h > 0.6f)
            {
                float flat = 1f - Sm(0.22f, 0.5f, slope);
                crack = flat * Sm(7f, 15f, e0) * (0.25f + 0.75f * Sm(0.30f, 0.60f, nB));
                ground = Color.Lerp(ground, new Color(0.30f, 0.20f, 0.18f), crack * 0.55f);   // 裂縫區的焦黑地殼
            }

            // 地面更有層次:草地稍微去螢光、加大明暗起伏(MK 的草是深淺不一的草叢,不是一整片螢光綠)
            if (lk.Kind != LookKind.Snow && lk.Kind != LookKind.Volcano && lk.Kind != LookKind.Desert)
            {
                float g = ground.r * 0.299f + ground.g * 0.587f + ground.b * 0.114f;
                ground = Color.Lerp(ground, new Color(g, g, g), 0.24f);
                ground = new Color(ground.r * 0.88f, ground.g * 0.9f, ground.b * 0.84f, 1f);
                float chk = ((Mathf.FloorToInt(x / 9f) + Mathf.FloorToInt(z / 9f)) & 1) == 0 ? 1.07f : 0.93f;   // 草皮割草格紋(很淡),遠看也有層次
                ground = new Color(ground.r * chk, ground.g * chk, ground.b * chk, 1f);
                ground = Color.Lerp(ground, new Color(ground.r * 0.78f, ground.g * 0.9f, ground.b * 0.7f), Sm(0.35f, 0.8f, nB) * 0.55f);   // 深色草叢
                ground = Color.Lerp(ground, new Color(Mathf.Min(1f, ground.r * 1.12f), Mathf.Min(1f, ground.g * 1.1f), ground.b * 0.92f), Sm(0.55f, 0.9f, nD) * 0.4f);   // 亮色(曬黃)草叢
            }
            float tone = 0.86f + 0.28f * nC;
            return new Color(ground.r * tone, ground.g * tone, ground.b * tone, 1f - 0.96f * crack);
        }

        // ------------------------------------------------------------------ 網格

        /// 平滑法線(heightfield 中央差分,跨度 d 格;用全域格點,所以相鄰區塊的邊界法線一致)。
        static Vector3 SmoothNormal(RaceTerrain t, int fi, int fj, int d)
        {
            int N = RaceTerrain.N;
            int a = Mathf.Max(0, fi - d), b = Mathf.Min(N - 1, fi + d), c = Mathf.Max(0, fj - d), e = Mathf.Min(N - 1, fj + d);
            float dx = (t.H[fj * N + b] - t.H[fj * N + a]) / ((b - a) * RaceTerrain.Cell);
            float dz = (t.H[e * N + fi] - t.H[c * N + fi]) / ((e - c) * RaceTerrain.Cell);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        sealed class MeshAcc
        {
            public readonly List<Vector3> V, N;
            public readonly List<Vector2> UV;
            public readonly List<Color32> C;
            public readonly List<int> T;
            public MeshAcc(int cap)
            {
                V = new List<Vector3>(cap); N = new List<Vector3>(cap); UV = new List<Vector2>(cap); C = new List<Color32>(cap); T = new List<int>(cap);
            }

            /// 一個三角形(獨立頂點);繞向保證朝上。
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color32 ca, Color32 cb, Color32 cc, Vector2 uv)
            {
                if (Vector3.Cross(b - a, c - a).y < 0f)
                {
                    Vector3 tp = b; b = c; c = tp;
                    Vector3 tn = nb; nb = nc; nc = tn;
                    Color32 tc = cb; cb = cc; cc = tc;
                }
                int k = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                N.Add(na); N.Add(nb); N.Add(nc);
                C.Add(ca); C.Add(cb); C.Add(cc);
                UV.Add(uv); UV.Add(uv); UV.Add(uv);
                T.Add(k); T.Add(k + 1); T.Add(k + 2);
            }
        }

        static Color32 Dim(Color32 c)
        {
            return new Color32((byte)(c.r * 0.78f), (byte)(c.g * 0.78f), (byte)(c.b * 0.78f), 255);
        }

        static void AddSkirt(MeshAcc m, Vector3 a, Vector3 b, Vector3 outward, float depth, Color32 ca, Color32 cb, Vector2 uv)
        {
            Vector3 a2 = a + Vector3.down * depth, b2 = b + Vector3.down * depth;
            Vector3 n = outward.normalized;
            Color32 da = Dim(ca), db = Dim(cb);
            Vector3[] p = { a, b, b2, a, b2, a2 };
            Color32[] pc = { ca, cb, db, ca, db, da };
            for (int s = 0; s < 2; s++)
            {
                Vector3 p0 = p[s * 3], p1 = p[s * 3 + 1], p2 = p[s * 3 + 2];
                Color32 c0 = pc[s * 3], c1 = pc[s * 3 + 1], c2 = pc[s * 3 + 2];
                // 兩個三角形,繞向讓法線朝 outward
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) < 0f) { Vector3 tp = p1; p1 = p2; p2 = tp; Color32 tc = c1; c1 = c2; c2 = tc; }
                int k = m.V.Count;
                m.V.Add(p0); m.V.Add(p1); m.V.Add(p2);
                m.N.Add(n); m.N.Add(n); m.N.Add(n);
                m.C.Add(c0); m.C.Add(c1); m.C.Add(c2);
                m.UV.Add(uv); m.UV.Add(uv); m.UV.Add(uv);
                m.T.Add(k); m.T.Add(k + 1); m.T.Add(k + 2);
            }
        }

        /// 渲染網格。stride = 1(LOD0)或 4(LOD1);cells = 這一塊的格數;lowerNearRoad:LOD1 在路附近取視窗最小值,避免粗網格把路面蓋住。
        /// cols != null = 用頂點色(uv 指向調色盤的白格);cols == null = 後備,每個三角形依 PalIdx 取調色盤色。法線一律平滑。
        static Mesh BuildRenderMesh(RaceTerrain t, Color32[] cols, int i0, int j0, int stride, int cells, float skirt, string name, bool lowerNearRoad)
        {
            int vn = cells + 1;
            var hx = new float[vn * vn];
            for (int b = 0; b < vn; b++)
                for (int a = 0; a < vn; a++)
                {
                    int fi = i0 + a * stride, fj = j0 + b * stride;
                    float h = t.H[fj * RaceTerrain.N + fi];
                    if (lowerNearRoad && stride > 1)
                    {
                        // 附近有路:取 (stride+1) x (stride+1) 視窗的最小值
                        bool near = false; float mn = h;
                        int r = stride / 2;
                        for (int dj = -r; dj <= r; dj++)
                            for (int di = -r; di <= r; di++)
                            {
                                int ii = Mathf.Clamp(fi + di, 0, RaceTerrain.N - 1), jj = Mathf.Clamp(fj + dj, 0, RaceTerrain.N - 1);
                                int k = jj * RaceTerrain.N + ii;
                                if (t.Edge[k] < 14f || (t.ScEdge != null && t.ScEdge[k] < 14f)) near = true;
                                if (t.H[k] < mn) mn = t.H[k];
                            }
                        if (near) h = mn;
                    }
                    hx[b * vn + a] = h;
                }

            // 每個格點的平滑法線與顏色
            var vnrm = new Vector3[vn * vn];
            var vcol = new Color32[vn * vn];
            var white = new Color32(255, 255, 255, 255);
            for (int b = 0; b < vn; b++)
                for (int a = 0; a < vn; a++)
                {
                    int fi = i0 + a * stride, fj = j0 + b * stride;
                    vnrm[b * vn + a] = SmoothNormal(t, fi, fj, Mathf.Max(1, stride));
                    vcol[b * vn + a] = cols != null ? cols[fj * RaceTerrain.N + fi] : white;
                }

            var acc = new MeshAcc(cells * cells * 6 + cells * 24);
            float step = stride * RaceTerrain.Cell;
            float wx0 = -RaceTerrain.Half + i0 * RaceTerrain.Cell, wz0 = -RaceTerrain.Half + j0 * RaceTerrain.Cell;
            var cp = new Vector3[4]; var ch = new float[4]; var cn = new Vector3[4]; var cc = new Color32[4];
            Vector2 uvWhite = PalUv[PWhite];
            for (int b = 0; b < cells; b++)
                for (int a = 0; a < cells; a++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        int da = k & 1, db = k >> 1;
                        int vi = (b + db) * vn + a + da;
                        ch[k] = hx[vi];
                        cp[k] = new Vector3(wx0 + (a + da) * step, ch[k], wz0 + (b + db) * step);
                        cn[k] = vnrm[vi];
                        cc[k] = vcol[vi];
                    }
                    int gi = (i0 / stride) + a, gj = (j0 / stride) + b;     // 以本 LOD 的格索引算奇偶
                    int[] ti = (((gi + gj) & 1) == 0) ? EvenTris : OddTris;
                    int cxi = i0 + a * stride, czi = j0 + b * stride;       // 以細格索引算棋盤色(後備用)
                    for (int s = 0; s < 2; s++)
                    {
                        int p0 = ti[s * 3], p1 = ti[s * 3 + 1], p2 = ti[s * 3 + 2];
                        Vector2 uv = uvWhite;
                        if (cols == null)
                        {
                            Vector3 nn = Vector3.Cross(cp[p1] - cp[p0], cp[p2] - cp[p0]);
                            float ny = Mathf.Abs(nn.y) / Mathf.Max(1e-6f, nn.magnitude);
                            uv = PalUv[PalIdx(cxi, czi, ch[p0], ch[p1], ch[p2], ny)];
                        }
                        acc.Tri(cp[p0], cp[p1], cp[p2], cn[p0], cn[p1], cn[p2], cc[p0], cc[p1], cc[p2], uv);
                    }
                }

            // 裙邊:四條邊往下垂,蓋住 LOD 交界的裂縫(顏色 = 邊上格點的顏色,變暗)
            Vector2 uvSkirt = cols != null ? uvWhite : PalUv[PGrassDark];
            for (int e = 0; e < cells; e++)
            {
                Vector3 s0 = new Vector3(wx0 + e * step, hx[e], wz0), s1 = new Vector3(wx0 + (e + 1) * step, hx[e + 1], wz0);
                AddSkirt(acc, s0, s1, Vector3.back, skirt, vcol[e], vcol[e + 1], uvSkirt);
                Vector3 n0 = new Vector3(wx0 + e * step, hx[cells * vn + e], wz0 + cells * step), n1 = new Vector3(wx0 + (e + 1) * step, hx[cells * vn + e + 1], wz0 + cells * step);
                AddSkirt(acc, n0, n1, Vector3.forward, skirt, vcol[cells * vn + e], vcol[cells * vn + e + 1], uvSkirt);
                Vector3 w0 = new Vector3(wx0, hx[e * vn], wz0 + e * step), w1 = new Vector3(wx0, hx[(e + 1) * vn], wz0 + (e + 1) * step);
                AddSkirt(acc, w0, w1, Vector3.left, skirt, vcol[e * vn], vcol[(e + 1) * vn], uvSkirt);
                Vector3 e0 = new Vector3(wx0 + cells * step, hx[e * vn + cells], wz0 + e * step), e1 = new Vector3(wx0 + cells * step, hx[(e + 1) * vn + cells], wz0 + (e + 1) * step);
                AddSkirt(acc, e0, e1, Vector3.right, skirt, vcol[e * vn + cells], vcol[(e + 1) * vn + cells], uvSkirt);
            }

            var m = new Mesh { name = name };
            if (acc.V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(acc.V); m.SetNormals(acc.N); m.SetUVs(0, acc.UV); m.SetColors(acc.C); m.SetTriangles(acc.T, 0);
            m.RecalculateBounds();
            return m;
        }

        /// 碰撞網格:共用頂點的 33 x 33 格點,2048 個三角形,切法與 RaceTerrain.Raw 完全一致。永遠用 LOD0。
        static Mesh BuildColliderMesh(RaceTerrain t, int i0, int j0, string name)
        {
            const int vn = ChunkCells + 1;
            var v = new Vector3[vn * vn];
            for (int b = 0; b < vn; b++)
                for (int a = 0; a < vn; a++)
                    v[b * vn + a] = new Vector3(-RaceTerrain.Half + (i0 + a) * RaceTerrain.Cell, t.H[(j0 + b) * RaceTerrain.N + i0 + a], -RaceTerrain.Half + (j0 + b) * RaceTerrain.Cell);
            var tri = new int[ChunkCells * ChunkCells * 6];
            int n = 0;
            var c = new int[4];
            for (int b = 0; b < ChunkCells; b++)
                for (int a = 0; a < ChunkCells; a++)
                {
                    c[0] = b * vn + a; c[1] = c[0] + 1; c[2] = c[0] + vn; c[3] = c[2] + 1;
                    int[] ti = (((i0 + a + j0 + b) & 1) == 0) ? EvenTris : OddTris;
                    for (int s = 0; s < 2; s++)
                    {
                        int p0 = c[ti[s * 3]], p1 = c[ti[s * 3 + 1]], p2 = c[ti[s * 3 + 2]];
                        if (Vector3.Cross(v[p1] - v[p0], v[p2] - v[p0]).y < 0f) { int tmp = p1; p1 = p2; p2 = tmp; }
                        tri[n++] = p0; tri[n++] = p1; tri[n++] = p2;
                    }
                }
            var m = new Mesh { name = name };
            m.vertices = v; m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }
    }
}
