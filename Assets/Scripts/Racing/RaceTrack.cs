using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// 一個飛越缺口(M7;見 TrackDef.TrackGap)。路面在 [S0, S1] 之間被切斷,滑翔跳台前緣在 S0(跳台中心在 S0 - RampLength/2)。
    /// 純資料(RaceTrack.FromDef 從 TrackDef.gaps 建出來),-batchmode -nographics 可以直接查。
    public sealed class GapInfo
    {
        public const float RampLength = 14f;       // 滑翔跳台長度(DefaultTracks.GliderLength)
        public const float ApproachDist = 90f;     // 跳台底部之前這段距離 = AI 的「缺口模式」(全速、走中線)
        public const float GateFreeBefore = 22f;   // 跳台底部之前這段距離內不放門(助跑加速板在這裡)
        public const float PitInset = 8f;          // 深坑離前緣 / 落地邊緣各內縮這麼遠(地形格距 4.5 m,留一格半不動到跳台底下的地形)
        public int Index;
        public float S0, S1;                       // 前緣 / 落地邊緣的主線弧長
        public float HalfWidth, Depth;            // 深坑橫向半寬、坑底在海平面下的深度
        public Vector3 Origin;                     // 前緣中心(世界座標,y 是路面高度)
        public Vector3 Dir, Right;                 // 從前緣指向落地邊緣的水平單位向量、它的右方
        public float Length { get { return S1 - S0; } }
        public float RampBase { get { return S0 - RampLength; } }
        public float Mid { get { return (S0 + S1) * 0.5f; } }
    }

    // 賽道幾何(模組 W,見 docs/racing/00-contracts.md §3.5)。
    // 從 TrackDef 的控制點做「封閉 Catmull-Rom 樣條」,每 ~2 m 取一個 TrackSample;檢查點每 ~40 m 一個。
    // 純資料 + 數學,不用 Unity 物件(只用 Vector3/Pose),所以 -batchmode -nographics 可以直接測。
    // 約定:s = 沿中心線的弧長,終點線在 s = 0;lateral 右正;曲率 κ 正 = 右彎(從上方看順時針)。
    public sealed class RaceTrack
    {
        public const float RoadWidthMul = 1.2f;    // 路面整體加寬(MK 式寬路,感覺賽道更大)
        public int Id;
        public string Name;
        public int Laps;
        public bool Closed = true;
        public float Length;

        /// 中心線最小轉彎半徑 m(取樣算出來的,給測試與 AI 參考)。
        public float MinRadius { get; private set; }
        /// 取樣間距 m(Length / 取樣數,約 2 m)。
        public float SampleStep { get { return step; } }

        public IReadOnlyList<TrackSample> Samples { get { return samples; } }
        public IReadOnlyList<Checkpoint> Checkpoints { get { return cps; } }
        public int CheckpointCount { get { return cps.Length; } }
        public IReadOnlyList<Vector3> ItemBoxRows { get { return itemRows; } }
        /// 跳台 / 加速板定義(來自 TrackDef.features,沒有時是空清單)。
        public IReadOnlyList<TrackFeature> Features { get { return features; } }
        public Rect MapBounds { get { return mapBounds; } }
        /// 捷徑(M5)。沒有 = 空清單。
        public IReadOnlyList<ShortcutPath> Shortcuts { get { return shortcuts; } }
        /// 路邊造景定義(來自 TrackDef.landmarks)。
        public IReadOnlyList<TrackLandmark> Landmarks { get { return landmarks; } }
        /// 飛越缺口(M7)。沒有 = 空清單。
        public IReadOnlyList<GapInfo> Gaps { get { return gaps; } }
        /// 軌道定義(TrackDef.rails)。沒有 = 空清單。
        public IReadOnlyList<TrackRail> Rails { get { return rails; } }

        // 地貌 / 外觀(來自 TrackDef,RaceTerrain / PropScatter / RaceWorld 讀)
        public int Theme;
        public float Relief = 1f, HillAmp = 24f, WallStart = 24f, WallEnd = 110f, Cliff, TreeDensity = 1f;

        internal TrackSample[] Raw { get { return samples; } }   // 給 TrackMeshBuilder 快速存取

        TrackSample[] samples;
        int N;
        float step;
        Checkpoint[] cps;
        Vector3[] itemRows;
        float cpSpacing = 40f;
        readonly List<TrackFeature> features = new List<TrackFeature>();
        readonly List<TrackLandmark> landmarks = new List<TrackLandmark>();
        readonly List<ShortcutPath> shortcuts = new List<ShortcutPath>();
        readonly List<GapInfo> gaps = new List<GapInfo>();
        readonly List<TrackRail> rails = new List<TrackRail>();
        float[] nodeS;
        Rect mapBounds;

        // 空間格子(加速全圈搜尋 Project):每格 16 m,存落在該格的取樣點索引
        const float GridCell = 16f;
        float gMinX, gMinZ;
        int gW, gH;
        int[] gStart, gItems;

        // ------------------------------------------------------------------ 建立

        public static RaceTrack FromDef(TrackDef def)
        {
            if (def == null) throw new ArgumentNullException("def");
            string err = def.Validate();
            if (err != null) throw new ArgumentException("TrackDef invalid: " + err);
            var t = new RaceTrack { Name = def.name, Laps = def.laps, Closed = true };
            t.cpSpacing = def.checkpointSpacing;
            if (def.features != null) foreach (var f in def.features) if (f != null) t.features.Add(f);
            if (def.landmarks != null) foreach (var l in def.landmarks) if (l != null) t.landmarks.Add(l);
            if (def.rails != null) foreach (var r in def.rails) if (r != null) t.rails.Add(r);
            t.Theme = def.theme; t.Relief = def.relief; t.HillAmp = def.hillAmp; t.WallStart = def.wallStart; t.WallEnd = def.wallEnd; t.Cliff = def.cliff; t.TreeDensity = def.treeDensity;
            t.BuildSamples(def);
            t.BuildGaps(def);
            t.BuildCheckpoints();
            t.BuildGrid();
            t.BuildShortcuts(def);
            return t;
        }

        static float CR(float p0, float p1, float p2, float p3, float t)
        {
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
        }

        void BuildSamples(TrackDef def)
        {
            int n = def.nodes.Count;
            const int Sub = 32;
            int M = n * Sub;
            var dx = new float[M]; var dz = new float[M]; var dw = new float[M]; var db = new float[M];
            var cum = new float[M + 1];
            for (int i = 0; i < n; i++)
            {
                var p0 = def.nodes[(i - 1 + n) % n]; var p1 = def.nodes[i];
                var p2 = def.nodes[(i + 1) % n]; var p3 = def.nodes[(i + 2) % n];
                for (int k = 0; k < Sub; k++)
                {
                    float u = k / (float)Sub;
                    int idx = i * Sub + k;
                    dx[idx] = CR(p0.x, p1.x, p2.x, p3.x, u);
                    dz[idx] = CR(p0.z, p1.z, p2.z, p3.z, u);
                    float su = u * u * (3f - 2f * u);
                    dw[idx] = Mathf.Lerp(p1.width, p2.width, su) * RoadWidthMul;
                    db[idx] = Mathf.Lerp(p1.bank, p2.bank, su);
                }
            }
            float L = 0f;
            for (int i = 0; i < M; i++)
            {
                cum[i] = L;
                int nx = (i + 1) % M;
                float ex = dx[nx] - dx[i], ez = dz[nx] - dz[i];
                L += Mathf.Sqrt(ex * ex + ez * ez);
            }
            cum[M] = L;
            Length = L;
            nodeS = new float[n];
            for (int i = 0; i < n; i++) nodeS[i] = cum[i * Sub];
            N = Mathf.Max(8, Mathf.RoundToInt(L / 2f));
            step = L / N;
            samples = new TrackSample[N];
            int j = 0;
            for (int i = 0; i < N; i++)
            {
                float s = i * step;
                while (j < M - 1 && cum[j + 1] <= s) j++;
                float seg = cum[j + 1] - cum[j];
                float f = seg > 1e-6f ? Mathf.Clamp01((s - cum[j]) / seg) : 0f;
                int j2 = (j + 1) % M;
                samples[i].Pos = new Vector3(Mathf.Lerp(dx[j], dx[j2], f), 0f, Mathf.Lerp(dz[j], dz[j2], f));
                samples[i].Width = Mathf.Lerp(dw[j], dw[j2], f);
                samples[i].Bank = Mathf.Lerp(db[j], db[j2], f);
                samples[i].S = s;
            }
            var yaw = new float[N];
            for (int i = 0; i < N; i++)
            {
                Vector3 a = samples[(i - 1 + N) % N].Pos, b = samples[(i + 1) % N].Pos;
                Vector3 t = b - a; t.y = 0f;
                if (t.sqrMagnitude < 1e-12f) t = Vector3.forward;
                t.Normalize();
                samples[i].Tangent = t;
                samples[i].Right = new Vector3(t.z, 0f, -t.x);
                yaw[i] = Mathf.Atan2(t.x, t.z);
            }
            float maxK = 1e-6f;
            for (int i = 0; i < N; i++)
            {
                float a0 = yaw[(i - 1 + N) % N] * Mathf.Rad2Deg, a1 = yaw[(i + 1) % N] * Mathf.Rad2Deg;
                float k = Mathf.DeltaAngle(a0, a1) * Mathf.Deg2Rad / (2f * step);
                samples[i].Curvature = k;
                if (Mathf.Abs(k) > maxK) maxK = Mathf.Abs(k);
            }
            MinRadius = 1f / maxK;
            if (def.autoBank) ComputeAutoBank();

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < N; i++)
            {
                Vector3 p = samples[i].Pos; float hw = samples[i].Width * 0.5f + 6f;
                minX = Mathf.Min(minX, p.x - hw); maxX = Mathf.Max(maxX, p.x + hw);
                minZ = Mathf.Min(minZ, p.z - hw); maxZ = Mathf.Max(maxZ, p.z + hw);
            }
            mapBounds = new Rect(minX, minZ, maxX - minX, maxZ - minZ);
        }

        /// M2:地形刻好路之後,把中心線每個取樣點的高度設進來(ys.Length 必須等於取樣數),並重算檢查點 / 重置點 / 道具箱位置的高度。
        /// 只改 Pos.y;水平位置、切線、曲率、弧長都不變,所以 Project / 格子索引不受影響。
        internal void ApplyHeights(float[] ys)
        {
            if (ys == null || ys.Length != N) throw new ArgumentException("ys length");
            for (int i = 0; i < N; i++) samples[i].Pos.y = ys[i];
            BuildCheckpoints();
            MarkSkipped();
        }

        /// 捷徑地形刻好之後,把捷徑中心線每個取樣點的高度設進來(ys.Length 必須等於取樣數),並重做入口 / 出口門。
        internal void ApplyShortcutHeights(int id, float[] ys)
        {
            if (id < 0 || id >= shortcuts.Count) throw new ArgumentException("shortcut id");
            if (ys == null || ys.Length != shortcuts[id].Count) throw new ArgumentException("ys length");
            shortcuts[id].ApplyHeights(ys);
        }

        void BuildShortcuts(TrackDef def)
        {
            shortcuts.Clear();
            if (def.shortcuts == null) return;
            for (int k = 0; k < def.shortcuts.Count; k++)
            {
                TrackShortcut sd = def.shortcuts[k];
                float fromS = nodeS[sd.fromNode], toS = nodeS[sd.toNode];
                if (toS <= fromS) throw new ArgumentException("shortcut " + k + ": toNode is not after fromNode along the track");
                shortcuts.Add(ShortcutPath.Build(def, sd, k, fromS, toS));
            }
            MarkSkipped();
        }

        /// 把被捷徑跳過的主線門標上 ShortcutId,並記下每條捷徑跳過的門索引範圍。終點線(第 0 個門)不會被跳過。
        void MarkSkipped()
        {
            for (int k = 0; k < shortcuts.Count; k++)
            {
                ShortcutPath sc = shortcuts[k];
                sc.FirstSkipped = -1; sc.LastSkipped = -1;
                for (int i = 1; i < cps.Length; i++)
                {
                    if (cps[i].S > sc.FromS && cps[i].S < sc.ToS)
                    {
                        cps[i].ShortcutId = k;
                        if (sc.FirstSkipped < 0) sc.FirstSkipped = i;
                        sc.LastSkipped = i;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ 軌道(模組 K)

        /// 軌道 i 的世界折線(每 ~4 m 一點;y = 路面中心線高度 + height,所以要在 ApplyHeights 之後呼叫)。弧長包過終點線時照樣取樣。
        public KartRail BuildRail(int i)
        {
            TrackRail r = rails[i];
            float len = r.s1 - r.s0;
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 4f) + 1);
            var pts = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                TrackSample sm = SampleAt(r.s0 + len * k / (n - 1));
                pts[k] = sm.Pos + sm.Right * r.lateral + Vector3.up * r.height;
            }
            return KartRail.FromPoints(pts, i);
        }

        /// 中心線橫向位移 = 軌道 lateral 的軌道點是否都在檢查點門寬(HalfWidth)內:軌道不會把人帶出門外(測試用)。
        public bool RailWithinGates(int i)
        {
            TrackRail r = rails[i];
            for (int k = 0; k < cps.Length; k++)
                if (Mathf.Abs(r.lateral) > cps[k].HalfWidth) return false;
            return true;
        }

        public const float MaxBankDeg = 8f;

        /// M8:路面側傾由曲率決定:半徑 >= 160 m 的彎不傾斜,<= 50 m 的緊彎 MaxBankDeg 度(外側高),中間線性;帶號值再用 +-24 m 的方框平均,所以進出彎是漸變、S 彎會經過 0。
        void ComputeAutoBank()
        {
            var raw = new float[N];
            for (int i = 0; i < N; i++)
            {
                float k = samples[i].Curvature, a = Mathf.Abs(k);
                float m = Mathf.Clamp01((a - 1f / 160f) / (1f / 50f - 1f / 160f));
                raw[i] = Mathf.Sign(k) * m * MaxBankDeg;
            }
            int r = Mathf.Max(1, Mathf.RoundToInt(24f / step));
            for (int i = 0; i < N; i++)
            {
                float sum = 0f;
                for (int q = -r; q <= r; q++) sum += raw[((i + q) % N + N) % N];
                samples[i].Bank = sum / (2 * r + 1);
            }
        }

        // ------------------------------------------------------------------ 缺口(M7)

        void BuildGaps(TrackDef def)
        {
            gaps.Clear();
            if (def.gaps == null) return;
            for (int k = 0; k < def.gaps.Count; k++)
            {
                TrackGap g = def.gaps[k];
                if (g.s1 >= Length - 40f) throw new ArgumentException("gap " + k + " runs into the finish line (s1=" + g.s1 + ", length " + Length + ")");
                TrackSample a = SampleAt(g.s0), b = SampleAt(g.s1);
                Vector3 d = b.Pos - a.Pos; d.y = 0f;
                if (d.sqrMagnitude < 1e-6f) d = a.Tangent;
                d.Normalize();
                gaps.Add(new GapInfo
                {
                    Index = k, S0 = g.s0, S1 = g.s1, HalfWidth = g.halfWidth, Depth = g.depth,
                    Origin = a.Pos, Dir = d, Right = new Vector3(d.z, 0f, -d.x),
                });
            }
        }

        // 弧長差 a - b 包到 [-L/2, L/2)
        float Diff(float a, float b)
        {
            float x = (a - b) % Length;
            if (x >= Length * 0.5f) x -= Length; else if (x < -Length * 0.5f) x += Length;
            return x;
        }

        /// s 是不是在某個缺口內(前緣前 before 公尺到落地邊緣後 after 公尺)。
        public bool InGap(float s, float before = 0f, float after = 0f)
        {
            return GapNear(s, before, after) != null;
        }

        /// s 所在的缺口(前緣前 before 公尺到落地邊緣後 after 公尺);沒有 = null。
        public GapInfo GapNear(float s, float before, float after)
        {
            for (int i = 0; i < gaps.Count; i++)
            {
                GapInfo g = gaps[i];
                float d = Diff(s, g.S0);
                if (d >= -before && d <= g.Length + after) return g;
            }
            return null;
        }

        /// 世界座標 p 在不在某個缺口的上空(沿著缺口範圍 ± 4 m、橫向在深坑 + 16 m 內)。hintS &lt; 0 = 全圈搜尋。RaceCheckpoints 用它判斷「飛在水面上方」不是掉進水裡。
        public bool OverGap(Vector3 p, float hintS)
        {
            if (gaps.Count == 0) return false;
            float lat;
            float s = Project(p, hintS, out lat);
            GapInfo g = GapNear(s, 4f, 4f);
            return g != null && Mathf.Abs(lat) <= g.HalfWidth + 16f;
        }

        /// 檢查點 s 的重置點弧長:一般 = 門後 6 m;缺口中央的門(或離跳台太近的門)= 跳台底部之前 70 m 的直路(掉進缺口後要重新助跑)。
        float RespawnS(float s)
        {
            for (int i = 0; i < gaps.Count; i++)
            {
                GapInfo g = gaps[i];
                if (s > g.RampBase - 8f && s < g.S1 + 0.5f) return Wrap(g.RampBase - 70f);
            }
            return Wrap(s - 6f);
        }

        void BuildCheckpoints()
        {
            int count = Mathf.Max(4, Mathf.RoundToInt(Length / Mathf.Max(10f, cpSpacing)));
            var sl = new List<float>(count + gaps.Count);
            for (int i = 0; i < count; i++)
            {
                float s = i * Length / count;
                // 缺口附近不放門:助跑加速板 + 跳台 + 缺口 + 落地區前段(門線不能被跳台擋住、重置點不能壓在加速板上;落地後的第一個門離落地邊緣 >= 12 m,重置點才在路面上)
                if (i > 0 && GapNear(s, GapInfo.RampLength + GapInfo.GateFreeBefore, 12f) != null) continue;
                sl.Add(s);
            }
            for (int k = 0; k < gaps.Count; k++) sl.Add(gaps[k].Mid);   // 缺口正中央放一個門:繞過缺口拿不到
            sl.Sort();
            cps = new Checkpoint[sl.Count];
            for (int i = 0; i < sl.Count; i++)
            {
                float s = sl[i];
                TrackSample sm = SampleAt(s);
                TrackSample back = SampleAt(RespawnS(s));
                cps[i] = new Checkpoint
                {
                    Index = i,
                    S = s,
                    Center = sm.Pos,
                    Forward = sm.Tangent,
                    HalfWidth = sm.Width * 0.5f + 4f,
                    Respawn = new Pose(back.Pos + Vector3.up * 0.3f, Quaternion.LookRotation(back.Tangent, Vector3.up)),
                    IsFinish = i == 0,
                };
            }
            int rows = Mathf.Max(1, Mathf.RoundToInt(Length / 150f));
            itemRows = new Vector3[rows];
            for (int k = 0; k < rows; k++)
            {
                float s = (k + 0.5f) * Length / rows;
                GapInfo g = GapNear(s, 30f, 15f);
                if (g != null) s = Wrap(g.RampBase - 40f);   // 道具箱不放在缺口上:移到助跑段
                itemRows[k] = SampleAt(s).Pos;
            }
        }

        void BuildGrid()
        {
            gMinX = mapBounds.xMin - GridCell; gMinZ = mapBounds.yMin - GridCell;
            gW = Mathf.CeilToInt((mapBounds.xMax + GridCell - gMinX) / GridCell) + 1;
            gH = Mathf.CeilToInt((mapBounds.yMax + GridCell - gMinZ) / GridCell) + 1;
            gStart = new int[gW * gH + 1];
            var cell = new int[N];
            for (int i = 0; i < N; i++)
            {
                int c = CellZ(samples[i].Pos.z) * gW + CellX(samples[i].Pos.x);
                cell[i] = c; gStart[c + 1]++;
            }
            for (int c = 0; c < gW * gH; c++) gStart[c + 1] += gStart[c];
            gItems = new int[N];
            var fill = new int[gW * gH];
            for (int i = 0; i < N; i++) { int c = cell[i]; gItems[gStart[c] + fill[c]++] = i; }
        }

        int CellX(float x) { return Mathf.Clamp(Mathf.FloorToInt((x - gMinX) / GridCell), 0, gW - 1); }
        int CellZ(float z) { return Mathf.Clamp(Mathf.FloorToInt((z - gMinZ) / GridCell), 0, gH - 1); }

        // ------------------------------------------------------------------ 查詢

        /// 把 s 包到 [0, Length)。
        public float Wrap(float s)
        {
            s %= Length;
            if (s < 0f) s += Length;
            if (s >= Length) s = 0f;
            return s;
        }

        /// 在相鄰取樣點之間內插(位置、方向、寬度、曲率)。
        public TrackSample SampleAt(float s)
        {
            s = Wrap(s);
            float f = s / step;
            int i = (int)f; if (i >= N) i = N - 1;
            float u = Mathf.Clamp01(f - i);
            int j = (i + 1) % N;
            TrackSample a = samples[i], b = samples[j];
            var r = new TrackSample();
            r.Pos = Vector3.Lerp(a.Pos, b.Pos, u);
            r.Tangent = Vector3.Lerp(a.Tangent, b.Tangent, u).normalized;
            r.Right = Vector3.Lerp(a.Right, b.Right, u).normalized;
            r.Width = Mathf.Lerp(a.Width, b.Width, u);
            r.Bank = Mathf.Lerp(a.Bank, b.Bank, u);
            r.Curvature = Mathf.Lerp(a.Curvature, b.Curvature, u);
            r.S = s;
            return r;
        }

        public Vector3 PointAt(float s) { return SampleAt(s).Pos; }
        public Vector3 TangentAt(float s) { return SampleAt(s).Tangent; }
        public float HalfWidthAt(float s) { return SampleAt(s).Width * 0.5f; }
        public float CurvatureAt(float s) { return SampleAt(s).Curvature; }
        public Vector3 AheadPoint(float s, float distance) { return PointAt(s + distance); }

        /// 區間 [s0, s1](沿行進方向,可跨過終點線)內 |κ| 最大者(保留正負號)。
        public float MaxCurvature(float s0, float s1)
        {
            float span = s1 - s0;
            if (span < 0f) span += Length;
            if (span > Length) span = Length;
            float best = 0f;
            for (float d = 0f; ; d += step)
            {
                bool last = d >= span;
                float k = CurvatureAt(s0 + (last ? span : d));
                if (Mathf.Abs(k) > Mathf.Abs(best)) best = k;
                if (last) break;
            }
            return best;
        }

        /// 把世界座標投影到中心線,回傳 s。lateral = 離中心線的橫向距離(右正)。
        /// hintS &lt; 0:全圈搜尋(用空間格子加速);否則只在 hintS ±60 m 內找(避免賽道靠近時跳到別段)。
        public float Project(Vector3 p, float hintS, out float lateral)
        {
            int idx;
            if (hintS < 0f)
            {
                float bd;
                idx = NearestGrid(p.x, p.z, out bd);
                if (idx < 0 || bd > GridCell * GridCell) idx = NearestBrute(p.x, p.z);
            }
            else
            {
                int c = Mathf.RoundToInt(Wrap(hintS) / step);
                int range = Mathf.CeilToInt(60f / step);
                idx = c % N; float bd = float.MaxValue;
                for (int k = -range; k <= range; k++)
                {
                    int i = ((c + k) % N + N) % N;
                    float ex = samples[i].Pos.x - p.x, ez = samples[i].Pos.z - p.z;
                    float d = ex * ex + ez * ez;
                    if (d < bd) { bd = d; idx = i; }
                }
            }
            return Refine(p, idx, out lateral);
        }

        int NearestBrute(float x, float z)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < N; i++)
            {
                float ex = samples[i].Pos.x - x, ez = samples[i].Pos.z - z;
                float d = ex * ex + ez * ez;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        int NearestGrid(float x, float z, out float bestD)
        {
            int cx = CellX(x), cz = CellZ(z);
            int best = -1; bestD = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            {
                int zz = cz + dz; if (zz < 0 || zz >= gH) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = cx + dx; if (xx < 0 || xx >= gW) continue;
                    int c = zz * gW + xx;
                    for (int k = gStart[c]; k < gStart[c + 1]; k++)
                    {
                        int i = gItems[k];
                        float ex = samples[i].Pos.x - x, ez = samples[i].Pos.z - z;
                        float d = ex * ex + ez * ez;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
            }
            return best;
        }

        float Refine(Vector3 p, int idx, out float lateral)
        {
            float bestD = float.MaxValue, bestS = 0f, bcx = 0f, bcz = 0f, brx = 0f, brz = 1f;
            for (int seg = 0; seg < 2; seg++)
            {
                int ia = seg == 0 ? (idx - 1 + N) % N : idx;
                int ib = seg == 0 ? idx : (idx + 1) % N;
                Vector3 A = samples[ia].Pos, B = samples[ib].Pos;
                float abx = B.x - A.x, abz = B.z - A.z;
                float len2 = abx * abx + abz * abz;
                float u = len2 > 1e-9f ? Mathf.Clamp01(((p.x - A.x) * abx + (p.z - A.z) * abz) / len2) : 0f;
                float cx = A.x + abx * u, cz = A.z + abz * u;
                float d = (p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz);
                if (d < bestD)
                {
                    bestD = d; bcx = cx; bcz = cz;
                    float sa = samples[ia].S, sb = ib == 0 ? Length : samples[ib].S;
                    bestS = sa + (sb - sa) * u;
                    brx = Mathf.Lerp(samples[ia].Right.x, samples[ib].Right.x, u);
                    brz = Mathf.Lerp(samples[ia].Right.z, samples[ib].Right.z, u);
                }
            }
            float mag = Mathf.Sqrt(brx * brx + brz * brz);
            if (mag < 1e-6f) mag = 1f;
            lateral = ((p.x - bcx) * brx + (p.z - bcz) * brz) / mag;
            return Wrap(bestS);
        }

        /// 下一個該過的檢查點索引(第一個 S &gt; s 的門;超過最後一個就回到 0 = 終點線)。
        public int NextCheckpoint(float s)
        {
            s = Wrap(s);
            for (int i = 0; i < cps.Length; i++) if (cps[i].S > s) return i;
            return 0;
        }

        /// 起跑格:slot 0..7,0 = 桿位。2 列(左右交錯)× 4 排,每排間隔 4 m,線後 6 m 起算。
        public Pose Grid(int slot)
        {
            slot = Mathf.Clamp(slot, 0, 7);
            float s = -(6f + slot * 4f);
            float lat = (slot % 2 == 0) ? -2.6f : 2.6f;
            TrackSample sm = SampleAt(s);
            Vector3 pos = sm.Pos + sm.Right * lat + Vector3.up * 0.3f;
            return new Pose(pos, Quaternion.LookRotation(sm.Tangent, Vector3.up));
        }

        public Pose RespawnPose(int checkpoint)
        {
            int i = ((checkpoint % cps.Length) + cps.Length) % cps.Length;
            return cps[i].Respawn;
        }

        /// 粗估進度 0..1(HUD 用)。
        public float Progress(Vector3 p)
        {
            float lat;
            return Project(p, -1f, out lat) / Length;
        }

        /// 車頭方向和中心線切線夾角 &gt; 120 度。速度與持續時間的條件由 RaceCheckpoints 判斷。
        public bool IsWrongWay(Vector3 p, Vector3 fwd, float hintS = -1f)
        {
            float lat;
            float s = Project(p, hintS, out lat);
            Vector3 t = TangentAt(s);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) return false;
            return Vector3.Dot(fwd.normalized, t) < -0.5f;
        }

        /// 世界座標 → 小地圖 0..1(MapBounds 正規化;y 向上 = +z)。
        public Vector2 ToMap(Vector3 p)
        {
            return new Vector2((p.x - mapBounds.xMin) / mapBounds.width, (p.z - mapBounds.yMin) / mapBounds.height);
        }
    }
}
