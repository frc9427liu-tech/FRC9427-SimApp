using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽道產生器(模組 W,M7):(種子, 主題, 長度目標, 難度, 缺口數) -> TrackDef。純資料 + 數學,不用 Unity 物件,-batchmode -nographics 可以直接跑。
    //
    // 作法(「多格骨牌外框」):
    //   1) 在 c x c 公尺的方格上長出一個沒有洞、沒有對角相碰的連通多格骨牌(隨機生長,偏好往同一方向延伸,所以會有長直線);
    //   2) 它的外框就是一條一定不自交的封閉折線(平行的兩段至少相隔 c 公尺 = 路寬 12 m 的好幾倍);
    //   3) 頂點加抖動、整體隨機旋轉,每個角用半徑 r(受相鄰邊長限制)的圓弧倒角 -> 彎道半徑 >= 約 30 m,長邊留下真正的直線;
    //   4) 圓弧 / 直線轉成 TrackNode(弧上每 ~8 m、直線上每 ~24 m 一個,RaceTrack 用封閉 Catmull-Rom 取樣);
    //   5) 最長的直線當起跑直道(旋轉節點讓 s = 0 落在那裡);其他長直線依序放「飛越缺口」(助跑 + 滑翔跳台 + 缺口 + 落地區);
    //   6) 加速板 / 跳台 / 地標 放在檢查點門、重置點、起跑格、缺口都避開的位置。
    // 檢查全部在產生時做(長度、最小半徑、自我間距、缺口深坑離其他路段 >= 52 m、地標在陸地上…);沒過就換下一個嘗試編號(Attempt),
    // 結果只由 (Seed, Attempt, 參數) 決定,同一組參數永遠得到同一條賽道;DefaultTracks 把通過的 Attempt 記下來,所以平常只會產生一次。

    /// 產生器參數。
    public sealed class TrackGenParams
    {
        public int Seed = 1;                 // 同時當地形種子(TrackDef.seed)
        public int Theme = 3;                // TrackTheme 編號
        public float Length = 3000f;         // 目標長度 m(容許 -10% ~ +12%)
        public int Difficulty = 1;           // 0 簡單(大半徑、缺口短)1 普通 2 困難(小半徑、缺口長)
        public int Gaps = 1;                 // 飛越缺口數(0..2)
        public float Cell = 90f;             // 方格邊長 m(也是平行路段的最小間距)
        public float Blocky = 0.15f;         // 長出 2 x 2 區塊(合併路段)的機率:越大彎越少、越像寬廣的圈
        public float Sweep = 0.5f;           // 0..1:大半徑彎(長邊旁邊的角)的比例
        public int Laps = 2;
        public string Name = "Track", NameZh = "";
        public int Attempt = 0;              // 從第幾次嘗試開始找
        // 地貌(給 RaceTerrain / PropScatter)
        public float Relief = 1.4f, HillAmp = 26f, WallStart = 22f, WallEnd = 100f, TreeDensity = 1f;
        public int Shortcut;                 // 保留(0 = 沒有);捷徑的資料格式支援,產生器目前不放
    }

    public static class TrackGen
    {
        public static int LastAttempt { get; private set; }
        public static string LastError { get; private set; }

        // 縱向直線需求(公尺):助跑 + 跳台(14)+ 缺口 + 落地區
        public const float Approach = 64f, Landing = 100f, RunMargin = 20f;
        public const float NodeStraight = 24f, NodeArc = 8f;
        public const float RegionNode = 0.76f;     // 節點離島心的「方圓」距離(m 值,島嶼遮罩 0.80 才開始降)
        public const float RegionLand = 0.70f;     // 地標底座
        public const float MinClear = 50f;         // 弧長 > 100 m 的兩點,中心線至少相隔這麼遠
        public const float PitClear = 62f;         // 深坑(含邊坡)離其他路段的橫向距離下限

        static float M(float x, float z)
        {
            float a = x / 432f, b = z / 432f;
            return Mathf.Sqrt(Mathf.Sqrt(a * a * a * a + b * b * b * b));
        }

        /// 依參數產生一條賽道。失敗(找不到合格的)丟 InvalidOperationException;DefaultTracks 會接住。
        public static TrackDef Generate(TrackGenParams p)
        {
            string err = "";
            for (int a = p.Attempt; a < p.Attempt + 600; a++)
            {
                string e;
                TrackDef d = TryGenerate(p, a, out e);
                if (d != null) { LastAttempt = a; LastError = null; return d; }
                err = e;
            }
            LastError = err;
            throw new InvalidOperationException("TrackGen: no valid layout for seed " + p.Seed + " (last: " + err + ")");
        }

        /// 只試一個嘗試編號;成功回傳 TrackDef,失敗回傳 null 並說明原因。
        public static TrackDef TryGenerate(TrackGenParams p, int attempt, out string err)
        {
            err = null;
            var rng = new System.Random(unchecked(p.Seed * 7919 + attempt * 104729 + 13));
            float c = p.Cell;

            // 1) 骨牌外框
            List<Vector2> poly = Polyomino(rng, p, out err);
            if (poly == null) return null;

            // 2) 抖動 + 旋轉 + 平移到島心
            Distort(poly, rng, c);

            // 3) 倒角 -> 節點
            List<Vector2> pts = Fillet(poly, rng, p, out err);
            if (pts == null) return null;
            for (int i = 0; i < pts.Count; i++)
                if (M(pts[i].x, pts[i].y) > RegionNode) { err = "node outside the island region (m=" + M(pts[i].x, pts[i].y).ToString("0.00") + ")"; return null; }

            TrackDef def = NewDef(p);
            for (int i = 0; i < pts.Count; i++) def.nodes.Add(new TrackNode(pts[i].x, pts[i].y, 12f));
            RaceTrack t0;
            try { t0 = RaceTrack.FromDef(def); } catch (Exception e) { err = "FromDef: " + e.Message; return null; }
            if (t0.Length < p.Length * 0.90f || t0.Length > p.Length * 1.12f) { err = "length " + t0.Length.ToString("0") + " vs target " + p.Length.ToString("0"); return null; }
            if (t0.MinRadius < 26f) { err = "min radius " + t0.MinRadius.ToString("0.0"); return null; }
            float clr = SelfClearance(t0);
            if (clr < MinClear) { err = "self clearance " + clr.ToString("0.0"); return null; }

            // 4) 最長直線當起跑直道:旋轉節點讓 s = 0 落在它中間
            List<Run> r0 = Runs(t0);
            if (r0.Count == 0) { err = "no straight run"; return null; }
            Run start = r0[0];
            float need0 = 190f;
            if (start.Len < need0) { err = "longest straight only " + start.Len.ToString("0"); return null; }
            RotateStart(def, t0, start);
            RaceTrack t1;
            try { t1 = RaceTrack.FromDef(def); } catch (Exception e) { err = "FromDef(rot): " + e.Message; return null; }

            // 5) 缺口:s = 0 附近的直線之外,依長度順序挑
            List<Run> runs = Runs(t1);
            var gaps = new List<TrackGap>();
            var used = new List<Run>();
            float L = t1.Length;
            for (int gi = 0; gi < p.Gaps; gi++)
            {
                float g = GapLength(rng, p.Difficulty);
                float need = Approach + GapInfo.RampLength + g + Landing + 2f * RunMargin;
                TrackGap placed = null;
                for (int k = 0; k < runs.Count && placed == null; k++)
                {
                    Run r = runs[k];
                    if (r.Len < need) break;                       // 已依長度由長到短排好
                    if (Contains(r, 0f, L) || DistToStart(r, L) < 150f) continue;   // 起跑直線不放缺口
                    if (used.Contains(r)) continue;
                    float slack = r.Len - need;
                    float s0 = r.A + RunMargin + Approach + GapInfo.RampLength + slack * (0.3f + 0.4f * (float)rng.NextDouble());
                    var cand = new TrackGap(s0, s0 + g, 40f, 22f);
                    if (!PitClearOk(t1, cand, out err)) continue;
                    placed = cand; used.Add(r);
                }
                if (placed == null) { err = "no straight long enough for gap " + gi + " (" + (err ?? "") + ")"; return null; }
                gaps.Add(placed);
            }
            gaps.Sort((x, y) => x.s0.CompareTo(y.s0));
            for (int i = 0; i < gaps.Count; i++) def.gaps.Add(gaps[i]);
            RaceTrack t2;
            try { t2 = RaceTrack.FromDef(def); } catch (Exception e) { err = "FromDef(gaps): " + e.Message; return null; }

            // 6) 特徵(缺口的跳台、加速板、跳台)與地標
            AddFeatures(def, t2, rng, p);
            if (!AddLandmarks(def, t2, rng, p, out err)) return null;

            string v = def.Validate();
            if (v != null) { err = "Validate: " + v; return null; }
            try { RaceTrack.FromDef(def); } catch (Exception e) { err = "FromDef(final): " + e.Message; return null; }
            return def;
        }

        static TrackDef NewDef(TrackGenParams p)
        {
            return new TrackDef
            {
                name = p.Name, nameZh = p.NameZh, seed = p.Seed, laps = p.Laps, closed = true, checkpointSpacing = 62f,
                theme = p.Theme, relief = p.Relief, hillAmp = p.HillAmp, wallStart = p.WallStart, wallEnd = p.WallEnd, cliff = 0f, treeDensity = p.TreeDensity, autoBank = true,
            };
        }

        static float GapLength(System.Random rng, int difficulty)
        {
            // M8:70~110 m 的大峽谷(滑翔 4~5 s,見 RaceKart.GlideLaunchSpeed)
            float lo = difficulty <= 0 ? 72f : difficulty == 1 ? 84f : 96f;
            return lo + 14f * (float)rng.NextDouble();
        }

        // ================================================================== 1) 多格骨牌

        static readonly int[] Dx = { 1, 0, -1, 0 }, Dy = { 0, 1, 0, -1 };

        static List<Vector2> Polyomino(System.Random rng, TrackGenParams p, out string err)
        {
            err = null;
            float c = p.Cell;
            int n = Mathf.Clamp(Mathf.FloorToInt(560f / c), 4, 8);   // 方格 n x n(外圍留一圈空格,陣列大小 n + 2)
            int sz = n + 2;
            var cell = new bool[sz, sz];
            int target = Mathf.Clamp(Mathf.RoundToInt((p.Length / (0.80f * c) - 2f) * 0.5f), 6, 40);
            int ci = 1 + n / 2, cj = 1 + n / 2;
            cell[ci, cj] = true;
            int count = 1;
            var cand = new List<int>(); var wt = new List<float>();
            int guard = 0;
            while (count < target && guard++ < 4000)
            {
                cand.Clear(); wt.Clear();
                float tot = 0f;
                for (int j = 1; j <= n; j++)
                    for (int i = 1; i <= n; i++)
                    {
                        if (cell[i, j]) continue;
                        int nb = 0, lastDir = -1;
                        for (int d = 0; d < 4; d++) if (cell[i + Dx[d], j + Dy[d]]) { nb++; lastDir = d; }
                        if (nb == 0) continue;
                        float w = 1f;
                        if (nb == 1)
                        {
                            // 沿同一方向延伸(對面的格子也在):權重 x3 -> 長直線
                            int ox = i + 2 * Dx[lastDir], oy = j + 2 * Dy[lastDir];
                            if (ox >= 0 && ox < sz && oy >= 0 && oy < sz && cell[ox, oy]) w = 3f;
                        }
                        else
                        {
                            if (rng.NextDouble() >= p.Blocky) continue;   // 會形成 2 x 2 區塊或環:機率很低
                            w = 0.5f;
                        }
                        if (Pinches(cell, i, j) || MakesHole(cell, sz, i, j)) continue;
                        cand.Add(j * sz + i); wt.Add(w); tot += w;
                    }
                if (cand.Count == 0) break;
                float pick = (float)rng.NextDouble() * tot;
                int sel = cand.Count - 1;
                for (int k = 0; k < cand.Count; k++) { pick -= wt[k]; if (pick <= 0f) { sel = k; break; } }
                cell[cand[sel] % sz, cand[sel] / sz] = true;
                count++;
            }
            if (count < Mathf.Max(5, target - 3)) { err = "polyomino stalled at " + count + "/" + target + " cells"; return null; }

            // 2) 外框:逆時針,內部在左邊。格點 (x, y),格子 (i, j) 佔 [i, i+1] x [j, j+1]
            var next = new Dictionary<int, int>();
            int V = sz + 1;
            for (int j = 1; j <= n; j++)
                for (int i = 1; i <= n; i++)
                {
                    if (!cell[i, j]) continue;
                    if (!cell[i, j - 1]) next[j * V + i] = j * V + (i + 1);                 // 底邊 向右
                    if (!cell[i + 1, j]) next[j * V + (i + 1)] = (j + 1) * V + (i + 1);     // 右邊 向上
                    if (!cell[i, j + 1]) next[(j + 1) * V + (i + 1)] = (j + 1) * V + i;     // 上邊 向左
                    if (!cell[i - 1, j]) next[(j + 1) * V + i] = j * V + i;                 // 左邊 向下
                }
            if (next.Count == 0) { err = "empty outline"; return null; }
            int startV = -1; foreach (var kv in next) { startV = kv.Key; break; }
            var loop = new List<int>();
            int cur = startV;
            for (int k = 0; k <= next.Count + 2; k++)
            {
                loop.Add(cur);
                int nx;
                if (!next.TryGetValue(cur, out nx)) { err = "broken outline"; return null; }
                cur = nx;
                if (cur == startV) break;
            }
            if (cur != startV || loop.Count != next.Count) { err = "outline is not a single loop"; return null; }

            // 只留轉角(共線的點拿掉)
            var corners = new List<Vector2>();
            int m = loop.Count;
            for (int k = 0; k < m; k++)
            {
                int a = loop[(k - 1 + m) % m], b = loop[k], d = loop[(k + 1) % m];
                int ax = a % V, ay = a / V, bx = b % V, by = b / V, dx = d % V, dy = d / V;
                if ((bx - ax) * (dy - by) - (by - ay) * (dx - bx) == 0) continue;
                corners.Add(new Vector2(bx, by));
            }
            if (corners.Count < 4) { err = "too few corners"; return null; }
            // 置中、縮放成公尺
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var q in corners) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y); }
            Vector2 mid = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            for (int k = 0; k < corners.Count; k++) corners[k] = (corners[k] - mid) * c;
            if (rng.NextDouble() < 0.5) corners.Reverse();   // 順時針 / 逆時針
            return corners;
        }

        // 新格子 (i, j) 會不會和對角的格子「只有角相碰」(兩個共用的邊鄰格都是空的)
        static bool Pinches(bool[,] cell, int i, int j)
        {
            for (int s = -1; s <= 1; s += 2)
                for (int t = -1; t <= 1; t += 2)
                    if (cell[i + s, j + t] && !cell[i + s, j] && !cell[i, j + t]) return true;
            return false;
        }

        // 加入 (i, j) 之後有沒有出現被圍住的空格(洞):從外圍洪水填滿,有空格填不到就是洞
        static bool MakesHole(bool[,] cell, int sz, int i, int j)
        {
            cell[i, j] = true;
            var seen = new bool[sz, sz];
            var stack = new Stack<int>();
            stack.Push(0); seen[0, 0] = true;
            while (stack.Count > 0)
            {
                int q = stack.Pop(); int x = q % sz, y = q / sz;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (nx < 0 || ny < 0 || nx >= sz || ny >= sz || seen[nx, ny] || cell[nx, ny]) continue;
                    seen[nx, ny] = true; stack.Push(ny * sz + nx);
                }
            }
            bool hole = false;
            for (int y = 0; y < sz && !hole; y++)
                for (int x = 0; x < sz; x++)
                    if (!cell[x, y] && !seen[x, y]) { hole = true; break; }
            cell[i, j] = false;
            return hole;
        }

        // ================================================================== 2) 抖動 / 旋轉

        static void Distort(List<Vector2> poly, System.Random rng, float c)
        {
            // 整行 / 整列的偏移(保持邊還是直的)+ 每個頂點的小抖動
            var colOff = new Dictionary<int, float>(); var rowOff = new Dictionary<int, float>();
            for (int i = 0; i < poly.Count; i++)
            {
                int kx = Mathf.RoundToInt(poly[i].x / c * 2f), ky = Mathf.RoundToInt(poly[i].y / c * 2f);
                float ox, oy;
                if (!colOff.TryGetValue(kx, out ox)) { ox = ((float)rng.NextDouble() * 2f - 1f) * 0.06f * c; colOff[kx] = ox; }
                if (!rowOff.TryGetValue(ky, out oy)) { oy = ((float)rng.NextDouble() * 2f - 1f) * 0.06f * c; rowOff[ky] = oy; }
                float jx = ((float)rng.NextDouble() * 2f - 1f) * 0.08f * c, jy = ((float)rng.NextDouble() * 2f - 1f) * 0.08f * c;
                poly[i] = new Vector2(poly[i].x + ox + jx, poly[i].y + oy + jy);
            }
            float ang = ((float)rng.NextDouble() * 2f - 1f) * 0.35f;
            float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            for (int i = 0; i < poly.Count; i++) poly[i] = new Vector2(poly[i].x * cs - poly[i].y * sn, poly[i].x * sn + poly[i].y * cs);
        }

        // ================================================================== 3) 倒角

        static List<Vector2> Fillet(List<Vector2> P, System.Random rng, TrackGenParams p, out string err)
        {
            err = null;
            int n = P.Count;
            var din = new Vector2[n]; var dout = new Vector2[n];
            var len = new float[n];       // 邊 i:P[i] -> P[i+1]
            for (int i = 0; i < n; i++)
            {
                Vector2 e = P[(i + 1) % n] - P[i];
                len[i] = e.magnitude;
                if (len[i] < 20f) { err = "edge shorter than 20 m"; return null; }
            }
            for (int i = 0; i < n; i++) { dout[i] = (P[(i + 1) % n] - P[i]) / len[i]; din[i] = (P[i] - P[(i - 1 + n) % n]) / len[(i - 1 + n) % n]; }
            var tau = new float[n]; var sgn = new float[n]; var r = new float[n]; var tt = new float[n];
            float rMin = p.Difficulty <= 0 ? 46f : p.Difficulty == 1 ? 40f : 34f;
            float rMax = p.Difficulty <= 0 ? 130f : p.Difficulty == 1 ? 115f : 100f;
            for (int i = 0; i < n; i++)
            {
                float cross = din[i].x * dout[i].y - din[i].y * dout[i].x;
                float dot = din[i].x * dout[i].x + din[i].y * dout[i].y;
                tau[i] = Mathf.Atan2(Mathf.Abs(cross), dot);
                sgn[i] = cross >= 0f ? 1f : -1f;
                float u = (float)rng.NextDouble();
                float want = rMin + (rMax - rMin) * (rng.NextDouble() < p.Sweep ? u : u * u * 0.3f);
                // 切線長 t = r * tan(tau / 2) 不得超過相鄰兩條邊中較短者的 48%:這樣每條邊兩端的切線加起來 <= 96% 邊長,不必再互相協調
                float cap = tau[i] < 0.02f ? want : 0.48f * Mathf.Min(len[i], len[(i - 1 + n) % n]) / Mathf.Tan(tau[i] * 0.5f);
                r[i] = Mathf.Min(want, cap);
            }
            float rLow = float.MaxValue;
            for (int i = 0; i < n; i++) if (tau[i] >= 0.02f) rLow = Mathf.Min(rLow, r[i]);
            if (rLow < 28f) { err = "corner radius shrank to " + rLow.ToString("0.0") + " m"; return null; }

            var pts = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ti = tau[i] < 0.02f ? 0f : r[i] * Mathf.Tan(tau[i] * 0.5f);
                float tj = tau[j] < 0.02f ? 0f : r[j] * Mathf.Tan(tau[j] * 0.5f);
                // 角 i 的圓弧
                if (ti <= 0f) AddPt(pts, P[i]);
                else
                {
                    Vector2 A = P[i] - din[i] * ti;
                    Vector2 left = new Vector2(-din[i].y, din[i].x);
                    Vector2 C = A + left * (sgn[i] * r[i]);
                    float phi0 = Mathf.Atan2(A.y - C.y, A.x - C.x);
                    int steps = Mathf.Max(2, Mathf.CeilToInt(r[i] * tau[i] / NodeArc));
                    for (int k = 0; k <= steps; k++)
                    {
                        float phi = phi0 + sgn[i] * tau[i] * k / steps;
                        AddPt(pts, new Vector2(C.x + r[i] * Mathf.Cos(phi), C.y + r[i] * Mathf.Sin(phi)));
                    }
                }
                // 到下一個角之間的直線
                Vector2 B = P[i] + dout[i] * ti;
                Vector2 A2 = P[j] - din[j] * tj;
                float ll = (A2 - B).magnitude;
                int seg = Mathf.CeilToInt(ll / NodeStraight);
                for (int k = 1; k < seg; k++) AddPt(pts, B + (A2 - B) * (k / (float)seg));
            }
            // 去掉頭尾重複
            if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).magnitude < 0.5f) pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        static void AddPt(List<Vector2> pts, Vector2 q)
        {
            if (pts.Count > 0 && (pts[pts.Count - 1] - q).magnitude < 0.5f) return;
            pts.Add(q);
        }

        // ================================================================== 檢查 / 直線分析

        static float SelfClearance(RaceTrack t)
        {
            int n = t.Samples.Count; float L = t.Length, step = t.SampleStep;
            float best = float.MaxValue;
            for (int i = 0; i < n; i += 2)
            {
                Vector3 a = t.Samples[i].Pos;
                for (int j = i + 1; j < n; j += 2)
                {
                    float arc = (j - i) * step; arc = Mathf.Min(arc, L - arc);
                    if (arc < 100f) continue;
                    float dx = a.x - t.Samples[j].Pos.x, dz = a.z - t.Samples[j].Pos.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < best) best = d2;
                }
            }
            return Mathf.Sqrt(best);
        }

        /// 一段直線:弧長 [A, B](A 可以是負的,跨過 s = 0 時 B > L)。
        public sealed class Run
        {
            public float A, B;
            public float Len { get { return B - A; } }
        }

        const float StraightK = 1f / 1000f;

        /// 找出所有中心線曲率 <= 1/1000 的連續區段,依長度由長到短排序。
        public static List<Run> Runs(RaceTrack t)
        {
            int n = t.Samples.Count; float step = t.SampleStep;
            var res = new List<Run>();
            var ok = new bool[n];
            int bad = -1;
            for (int i = 0; i < n; i++) { ok[i] = Mathf.Abs(t.Samples[i].Curvature) <= StraightK; if (!ok[i]) bad = i; }
            if (bad < 0) return res;                       // 整圈都是直的?不可能(封閉)
            // 從一個「不直」的取樣點開始繞一圈,收集連續的直線
            int i0 = (bad + 1) % n;
            int runStart = -1;
            for (int k = 0; k <= n; k++)
            {
                int i = (i0 + k) % n;
                bool good = k < n && ok[i];
                if (good && runStart < 0) runStart = k;
                if (!good && runStart >= 0)
                {
                    int first = (i0 + runStart) % n;
                    float a = first * step;
                    float len = (k - runStart) * step;
                    // 弧長從 first 開始;可能跨過 L
                    res.Add(new Run { A = a, B = a + len });
                    runStart = -1;
                }
            }
            // 兩端各縮 2 個取樣(曲率估計用到相鄰點)
            foreach (var r in res) { r.A += 2f * step; r.B -= 2f * step; }
            res.RemoveAll(r => r.B - r.A < 30f);
            res.Sort((x, y) => y.Len.CompareTo(x.Len));
            return res;
        }

        static bool Contains(Run r, float s, float L)
        {
            float x = s; while (x < r.A) x += L; while (x > r.B + L) x -= L;
            return (x >= r.A && x <= r.B) || (x - L >= r.A && x - L <= r.B);
        }

        // 這段直線離 s = 0 有多遠(取較近的一端,環狀)
        static float DistToStart(Run r, float L)
        {
            float best = float.MaxValue;
            for (float s = r.A; s <= r.B; s += 10f)
            {
                float w = s % L; if (w < 0f) w += L;
                best = Mathf.Min(best, Mathf.Min(w, L - w));
            }
            return best;
        }

        static void RotateStart(TrackDef def, RaceTrack t, Run start)
        {
            // 起跑線放在最長直線偏後的位置(線後有 34 m 起跑格,線前還有足夠的直路給拱門)
            float s = start.A + Mathf.Min(start.Len * 0.4f, 90f);
            Vector3 p = t.PointAt(s);
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < def.nodes.Count; i++)
            {
                float dx = def.nodes[i].x - p.x, dz = def.nodes[i].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            if (best == 0) return;
            var rot = new List<TrackNode>(def.nodes.Count);
            for (int i = 0; i < def.nodes.Count; i++) rot.Add(def.nodes[(best + i) % def.nodes.Count]);
            def.nodes = rot;
        }

        // 缺口深坑(沿缺口 + 橫向 R = 半寬 + 邊坡)離其他路段夠遠:弧長離缺口 > 30 m 的取樣點不能進到「矩形 + PitClear 邊距」裡
        static bool PitClearOk(RaceTrack t, TrackGap g, out string err)
        {
            err = null;
            Vector3 o = t.PointAt(g.s0);
            Vector3 d = t.PointAt(g.s1) - o; d.y = 0f; d.Normalize();
            Vector3 r = new Vector3(d.z, 0f, -d.x);
            float len = g.s1 - g.s0, L = t.Length;
            for (int i = 0; i < t.Samples.Count; i++)
            {
                float s = t.Samples[i].S;
                float ds = Mathf.Abs(s - (g.s0 + g.s1) * 0.5f); ds = Mathf.Min(ds, L - ds);
                Vector3 q = t.Samples[i].Pos - o;
                float u = q.x * d.x + q.z * d.z, v = q.x * r.x + q.z * r.z;
                if (ds > len * 0.5f + GapInfo.RampLength + 30f && u > -6f && u < len + 6f && Mathf.Abs(v) < PitClear)
                { err = "pit too close to another road section"; return false; }
                if (ds <= len * 0.5f + GapInfo.RampLength + 30f && Mathf.Abs(v) > 1.6f && u > -GapInfo.RampLength - 30f && u < len + 40f)
                { err = "road not straight beside the gap"; return false; }
            }
            // 起飛段、落地段要真的直:曲率
            if (Mathf.Abs(t.MaxCurvature(g.s0 - GapInfo.RampLength - Approach, g.s1 + Landing)) > StraightK * 1.5f) { err = "gap section not straight"; return false; }
            return true;
        }

        // ================================================================== 6) 特徵 / 地標

        static float Diff(float a, float b, float L)
        {
            float x = (a - b) % L;
            if (x > L * 0.5f) x -= L; else if (x < -L * 0.5f) x += L;
            return x;
        }

        // s(半長 half)離清單裡每一個位置(中心, 半長)都要 >= margin 公尺的空隙
        static bool Clear(List<Vector2> spots, float s, float half, float margin, float L)
        {
            for (int i = 0; i < spots.Count; i++)
                if (Mathf.Abs(Diff(s, spots[i].x, L)) < half + spots[i].y + margin) return false;
            return true;
        }

        static void AddFeatures(TrackDef def, RaceTrack t, System.Random rng, TrackGenParams p)
        {
            float L = t.Length;
            var hard = new List<Vector2>();      // 絕對不能放東西的地方(中心, 半長):起跑格 / 終點線 / 拱門、每個門線與重置點、缺口整段
            var ramps = new List<Vector2>();     // 跳台(含滑翔跳台)
            var pads = new List<Vector2>();      // 加速板
            hard.Add(new Vector2(Wrap(-17f, L), 30f));                    // 線後 34 m 到線前 13 m
            for (int i = 0; i < t.CheckpointCount; i++)
            {
                Checkpoint cp = t.Checkpoints[i];
                hard.Add(new Vector2(cp.S, 0.5f));                        // 門線
                hard.Add(new Vector2(Wrap(cp.S - 6f, L), 0.5f));          // 重置點
            }
            var feats = def.features;

            // 缺口:起飛跳台(鎖定)+ 助跑加速板 + 落地加速板
            foreach (var g in t.Gaps)
            {
                hard.Add(new Vector2(Wrap(g.RampBase - 70f, L), 0.5f));       // 缺口中央門的重置點(RaceTrack.RespawnS)
                float lo = g.RampBase - 56f, hi = g.S1 + 28f;
                hard.Add(new Vector2((lo + hi) * 0.5f, (hi - lo) * 0.5f));   // 缺口範圍內不放別的東西
                var ramp = new TrackFeature(TrackFeatureKind.GliderRamp, g.S0 - GapInfo.RampLength * 0.5f, DefaultTracks.GliderLength, DefaultTracks.GliderWidth, 0f, DefaultTracks.GliderPitch);
                ramp.locked = true;
                feats.Add(ramp);
                ramps.Add(new Vector2(ramp.s, ramp.length * 0.5f));
                float[] ps = { g.RampBase - 14f, g.RampBase - 44f, g.S1 + 20f };   // 助跑兩塊(最近的一塊加速 1 秒,剛好帶著衝上跳台)、落地一塊
                for (int k = 0; k < ps.Length; k++)
                {
                    bool bad = false;
                    for (int c = 0; c < t.CheckpointCount && !bad; c++)
                    {
                        float cs = t.Checkpoints[c].S;
                        if (Mathf.Abs(Diff(ps[k], cs, L)) < 4.6f || Mathf.Abs(Diff(ps[k], Wrap(cs - 6f, L), L)) < 4.6f) bad = true;   // 加速板半長 3 + 容許 1.1 + 0.5
                    }
                    if (bad) continue;
                    feats.Add(new TrackFeature(TrackFeatureKind.Boost, ps[k], 6f, 5f));
                    pads.Add(new Vector2(ps[k], 3f));
                }
            }

            // 普通跳台:長直線上,前後有平路。每 ~1100 m 一個(至少 2 個)
            int wantRamps = Mathf.Max(2, Mathf.RoundToInt(L / 1100f));
            int placedRamps = 0;
            for (int tries = 0; tries < 600 && placedRamps < wantRamps; tries++)
            {
                float s = (float)rng.NextDouble() * L;
                if (!Clear(hard, s, 4f, 0.5f, L) || !Clear(ramps, s, 4f, 40f, L) || !Clear(pads, s, 4f, 14f, L)) continue;
                if (t.InGap(s, 150f, 90f)) continue;
                if (Mathf.Abs(t.MaxCurvature(s - 10f, s + 40f)) > 1f / 150f) continue;
                if (RampBlocksGate(t, s, 4f, L)) continue;
                feats.Add(new TrackFeature(TrackFeatureKind.Ramp, s, 8f, 6f, 0f, 18f));
                ramps.Add(new Vector2(s, 4f));
                placedRamps++;
            }
            // 加速板:直線上,彼此相隔 >= 60 m
            int wantPads = Mathf.Max(6, Mathf.RoundToInt(L / 260f));
            int placedPads = 0;
            for (int tries = 0; tries < 1200 && placedPads < wantPads; tries++)
            {
                float s = (float)rng.NextDouble() * L;
                if (!Clear(hard, s, 3f, 0.5f, L) || !Clear(pads, s, 3f, 60f, L) || !Clear(ramps, s, 3f, 14f, L)) continue;
                if (t.InGap(s, 100f, 40f)) continue;
                if (Mathf.Abs(t.MaxCurvature(s - 8f, s + 8f)) > 1f / 120f) continue;
                feats.Add(new TrackFeature(TrackFeatureKind.Boost, s, 6f, 5f));
                pads.Add(new Vector2(s, 3f));
                placedPads++;
            }
            feats.Sort((a, b) => a.s.CompareTo(b.s));
        }

        // 跳台(半長 half + 2.5 m 外擴)會不會擋住任何一個門線 / 重置點
        static bool RampBlocksGate(RaceTrack t, float s, float half, float L)
        {
            for (int c = 0; c < t.CheckpointCount; c++)
            {
                float cs = t.Checkpoints[c].S;
                if (Mathf.Abs(Diff(cs, s, L)) <= half + 4.5f) return true;
                if (Mathf.Abs(Diff(Wrap(cs - 6f, L), s, L)) <= half + 4.5f) return true;
            }
            return false;
        }

        static float Wrap(float s, float L) { s %= L; if (s < 0f) s += L; return s; }

        // 地標:起跑直線後方的大拱門 + 依主題挑的路邊景物
        static bool AddLandmarks(TrackDef def, RaceTrack t, System.Random rng, TrackGenParams p, out string err)
        {
            err = null;
            float L = t.Length;
            bool rocky = p.Theme == 4 || p.Theme == 7 || p.Theme == 1;
            def.landmarks.Add(new TrackLandmark(rocky ? TrackLandmarkKind.RockArch : TrackLandmarkKind.BigArch, 65f, 0f, 1f));

            TrackLandmarkKind[] kinds;
            switch (p.Theme)
            {
                case 3: kinds = new[] { TrackLandmarkKind.Windmill, TrackLandmarkKind.Windmill, TrackLandmarkKind.Windmill, TrackLandmarkKind.Lighthouse }; break;
                case 4: kinds = new[] { TrackLandmarkKind.Spire, TrackLandmarkKind.Spire, TrackLandmarkKind.Lighthouse, TrackLandmarkKind.Spire, TrackLandmarkKind.Spire }; break;
                case 5: kinds = new[] { TrackLandmarkKind.Spire, TrackLandmarkKind.Lighthouse, TrackLandmarkKind.Spire, TrackLandmarkKind.Windmill }; break;
                case 6: kinds = new[] { TrackLandmarkKind.Windmill, TrackLandmarkKind.Spire, TrackLandmarkKind.Windmill, TrackLandmarkKind.Lighthouse }; break;
                default: kinds = new[] { TrackLandmarkKind.Spire, TrackLandmarkKind.Spire, TrackLandmarkKind.Spire, TrackLandmarkKind.Spire, TrackLandmarkKind.Spire }; break;
            }
            var placed = new List<Vector3>();
            int want = kinds.Length, got = 0;
            for (int tries = 0; tries < 600 && got < want; tries++)
            {
                float s = 140f + (float)rng.NextDouble() * (L - 280f);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float lat = side * (50f + 8f * (float)rng.NextDouble());
                if (t.InGap(s, 40f, 40f)) continue;
                TrackSample sm = t.SampleAt(s);
                Vector3 b = sm.Pos + sm.Right * lat;
                if (M(b.x, b.z) > RegionLand) continue;
                // 離所有中心線 >= 40 m(含路寬 6 m,地標離路緣 >= 8 m 的測試有餘裕),離別的地標 >= 70 m,不在缺口的深坑範圍內
                bool ok = true;
                for (int i = 0; i < t.Samples.Count && ok; i += 3)
                {
                    float dx = t.Samples[i].Pos.x - b.x, dz = t.Samples[i].Pos.z - b.z;
                    if (dx * dx + dz * dz < 40f * 40f) ok = false;
                }
                for (int i = 0; i < placed.Count && ok; i++) if ((placed[i] - b).sqrMagnitude < 70f * 70f) ok = false;
                foreach (var g in t.Gaps)
                {
                    Vector3 q = b - g.Origin;
                    float u = q.x * g.Dir.x + q.z * g.Dir.z, v = q.x * g.Right.x + q.z * g.Right.z;
                    if (u > -20f && u < g.Length + 20f && Mathf.Abs(v) < g.HalfWidth + 30f) ok = false;
                }
                if (!ok) continue;
                placed.Add(b);
                def.landmarks.Add(new TrackLandmark(kinds[got], s, lat, 1f));
                got++;
            }
            if (got < 3) { err = "only " + got + " landmarks fit"; return false; }
            return true;
        }
    }
}
