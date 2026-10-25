using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// M8 走廊感:在每條產生賽道(theme >= 3)上挑 3 段(~180..260 m),沿路邊 6.5..14 m 外立起連續的斷崖牆(右側 / 左側 / 兩側輪流),
    /// 讓路看起來像在峽谷 / 山谷裡跑,而不是一片開闊平地。牆是純裝飾(沒有碰撞體,離護欄 >= 3 m、車不會碰到),全部合併成 1 個頂點色網格(RaceToonVC)。
    /// 每個斷崖由 3 層傾斜的小平面(底 -> 中 -> 頂,越高越往後倒、每個頂點各自抖動)+ 頂面蓋子組成,顏色依主題:岩壁漸層(底暗頂亮)+ 主題頂色(草 / 雪 / 苔 / 熔岩),
    /// 牆面每 ~55 m 掛一面彩色布條(橫幅,看板圖集的顏色條紋,不是貼圖)。選段避開缺口(前後 160 m)、起跑線、捷徑、地標,並檢查離其他路段的距離。
    /// 全部由 (賽道, 種子) 決定,結果固定。三角形約 1~1.5 千。
    internal static class TrackCorridor
    {
        public static int LastTris, LastSections;
        public static readonly List<Vector2> Sections = new List<Vector2>();   // (s0, s1) 供測試用
        public static readonly List<int> SectionSides = new List<int>();       // -1 左、+1 右、0 兩側

        const float Step = 7f;

        public static void Build(RaceTrack tr, RaceTerrain terr, Transform worldRoot, List<Object> owned, int seed)
        {
            LastTris = 0; LastSections = 0; Sections.Clear(); SectionSides.Clear();
            if (tr == null || terr == null || worldRoot == null || tr.Theme < 3) return;
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(tr.Theme);
            var rng = new System.Random(seed * 31 + tr.Theme * 977 + 5);
            var acc = new RaceColorAcc();
            float L = tr.Length;
            for (int slot = 0; slot < 3; slot++)
            {
                float len = 150f + 70f * (float)rng.NextDouble();
                float s0 = L * (0.2f + 0.3f * slot) + 40f * (float)rng.NextDouble();
                bool placed = false;
                for (int tries = 0; tries < 130 && !placed; tries++, s0 += 20f)
                {
                    float a = Wrap(s0, L);
                    if (!SectionOk(tr, a, len)) continue;
                    int side = slot == 0 ? 1 : slot == 1 ? -1 : 0;
                    if (side == 0 && !SectionOk(tr, a, len)) continue;
                    BuildSection(acc, tr, terr, th, a, len, side, rng, slot);
                    Sections.Add(new Vector2(a, a + len)); SectionSides.Add(side);
                    placed = true;
                }
            }
            LastSections = Sections.Count;
            if (acc.TriCount == 0) return;
            var root = new GameObject("CorridorWalls");
            root.transform.SetParent(worldRoot, false);
            int tris = 0, draws = 0;
            acc.Emit(root.transform, "CorridorWalls", owned, null, ref tris, ref draws);
            LastTris = tris;
        }

        static float Wrap(float s, float L) { s %= L; if (s < 0f) s += L; return s; }

        static float Diff(float a, float b, float L)
        {
            float x = (a - b) % L;
            if (x > L * 0.5f) x -= L; else if (x < -L * 0.5f) x += L;
            return x;
        }

        // [a, a+len] 與 [lo, hi](都可能跨過 L)有沒有交集(hi - lo 小於 L)
        static bool Overlap(float a, float len, float lo, float hi, float L)
        {
            float mid1 = a + len * 0.5f, mid2 = (lo + hi) * 0.5f;
            return Mathf.Abs(Diff(mid1, mid2, L)) < len * 0.5f + (hi - lo) * 0.5f;
        }

        static bool SectionOk(RaceTrack tr, float a, float len)
        {
            float L = tr.Length;
            if (Overlap(a, len, L - 170f, L + 170f, L)) return false;                 // 起跑線
            foreach (var g in tr.Gaps) if (Overlap(a, len, g.RampBase - 170f, g.S1 + 170f, L)) return false;
            foreach (var sc in tr.Shortcuts) if (Overlap(a, len, sc.FromS - 70f, sc.ToS + 70f, L)) return false;
            foreach (var lm in tr.Landmarks) if (Mathf.Abs(Diff(lm.s, a + len * 0.5f, L)) < len * 0.5f + 14f) return false;
            foreach (var r in tr.Rails) if (Overlap(a, len, r.s0 - 20f, r.s1 + 20f, L)) return false;
            // 彎不能太急(牆在內側會壓到路),離其他路段夠遠
            for (float s = a; s <= a + len; s += Step)
            {
                if (Mathf.Abs(tr.SampleAt(s).Curvature) > 1f / 45f) return false;
                TrackSample sm = tr.SampleAt(s);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = sm.Pos + sm.Right * (side * (sm.Width * 0.5f + 17f));
                    for (int i = 0; i < tr.Samples.Count; i += 2)
                    {
                        float d = Mathf.Abs(Diff(tr.Samples[i].S, s, L));
                        if (d < 60f) continue;
                        Vector3 q = tr.Samples[i].Pos - p; q.y = 0f;
                        if (q.sqrMagnitude < 20f * 20f) return false;
                    }
                }
            }
            return true;
        }

        // 牆貼圖(RaceTexArray 切片)與每格重複長度:草原 = 石磚牆、沙漠 = 沙岩層、冰 = 冰磚、松林 = 苔石、火山 = 暗磚
        static int WallSlice(int theme)
        {
            switch (theme)
            {
                case 4: return RaceTexArray.MtnMesa;
                case 5: return RaceTexArray.Ice;
                case 6: return RaceTexArray.Stone;
                case 7: return RaceTexArray.Brick;
                default: return RaceTexArray.Stone;
            }
        }

        static Color32 C32(Color c) { return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255), 255); }

        static readonly Color[] Banner = { new Color(0.95f, 0.30f, 0.20f), new Color(0.98f, 0.82f, 0.18f), new Color(0.22f, 0.55f, 0.95f), new Color(0.96f, 0.96f, 0.98f) };

        // 主題的牆色:底、中、頂(岩壁漸層)、蓋子
        static void Palette(RaceTrack tr, TrackTheme th, out Color lo, out Color mid, out Color hi, out Color cap)
        {
            Color rock = th.MtnRock;
            switch (tr.Theme)
            {
                case 4: lo = new Color(0.62f, 0.34f, 0.17f); mid = new Color(0.82f, 0.50f, 0.25f); hi = new Color(0.92f, 0.68f, 0.38f); cap = new Color(0.88f, 0.72f, 0.46f); break;                 // 沙岩
                case 5: lo = new Color(0.46f, 0.58f, 0.74f); mid = new Color(0.70f, 0.82f, 0.92f); hi = new Color(0.92f, 0.96f, 1.00f); cap = Color.white; break;                               // 冰壁
                case 6: lo = new Color(0.20f, 0.22f, 0.26f); mid = new Color(0.34f, 0.38f, 0.40f); hi = new Color(0.30f, 0.50f, 0.28f); cap = new Color(0.28f, 0.52f, 0.26f); break;               // 暗岩 + 苔
                case 7: lo = new Color(0.12f, 0.08f, 0.08f); mid = new Color(0.26f, 0.15f, 0.12f); hi = new Color(0.62f, 0.22f, 0.10f); cap = new Color(0.20f, 0.12f, 0.10f); break;              // 玄武岩 + 熔岩紅
                default: lo = Color.Lerp(rock, Color.black, 0.35f); mid = rock; hi = Color.Lerp(rock, Color.white, 0.25f); cap = th.GrassLight; break;                                         // 草原:灰岩 + 草頂
            }
        }

        static void BuildSection(RaceColorAcc acc, RaceTrack tr, RaceTerrain terr, TrackTheme th, float a, float len, int sideMode, System.Random rng, int slot)
        {
            Color lo, mid, hi, cap;
            Palette(tr, th, out lo, out mid, out hi, out cap);
            int wallSlice = WallSlice(tr.Theme);
            float keep = tr.Theme == 4 ? 0.0f : (tr.Theme == 7 ? 0.55f : 0.45f);   // 主題色保留多少(其餘用白 = 貼圖本色)
            Color wl = Color.Lerp(Color.white, lo, keep + 0.15f), wm = Color.Lerp(Color.white, mid, keep), wh = Color.Lerp(Color.white, hi, keep * 0.5f);
            if (tr.Theme == 4) { wl = new Color(0.72f, 0.62f, 0.58f); wm = new Color(0.92f, 0.86f, 0.8f); wh = Color.white; }
            if (tr.Theme == 7) { wl = new Color(0.35f, 0.26f, 0.26f); wm = new Color(0.62f, 0.4f, 0.36f); wh = new Color(0.9f, 0.55f, 0.4f); }
            Color32 cLo = C32(wl), cMid = C32(wm), cHi = C32(wh), cCap = C32(cap);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / Step));
            for (int sd = -1; sd <= 1; sd += 2)
            {
                if (sideMode != 0 && sd != sideMode) continue;
                // 每個斷面的 4 個頂點(底、1/3、2/3、頂)與後緣蓋子點;沿路每 Step 一個斷面
                var prof = new Vector3[n + 1][];
                float baseH = 9f + 4f * (float)rng.NextDouble();
                for (int i = 0; i <= n; i++)
                {
                    float s = a + len * i / n;
                    // 兩端漸入:前後 28 m 牆高從 0 長到全高(不會突然一道牆立起來)
                    float u = (float)i / n, edge = Mathf.Min(1f, Mathf.Min(u * len, (1f - u) * len) / 28f);
                    edge = edge * edge * (3f - 2f * edge);
                    TrackSample sm = tr.SampleAt(Wrap(s, tr.Length));
                    float hw = sm.Width * 0.5f;
                    float jit = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float d0 = hw + 6.5f + (4f + 3.5f * Mathf.Sin(s * 0.043f + slot * 2f)) * (0.5f + 0.5f * edge) + 1.2f * jit;   // 離路中心(6.5..14 m 在路緣外)
                    float H = (baseH + 3f * Mathf.Sin(s * 0.021f + slot) + 1.8f * jit) * edge;
                    var col = new Vector3[5];
                    for (int k = 0; k < 5; k++)
                    {
                        float f = k < 4 ? k / 3f : 1f;
                        float lean = k < 4 ? (0.6f * f + 2.4f * f * f) * (1f + 0.25f * (float)(rng.NextDouble() - 0.5)) : 9f;   // 越高越往後倒;k = 4:蓋子後緣
                        float dj = k > 0 && k < 4 ? 0.9f * (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
                        float lat = sd * (d0 + lean + dj);
                        Vector3 p = sm.Pos + sm.Right * lat;
                        float gy = terr.Raw(p.x, p.z);
                        float yb = Mathf.Min(gy, sm.Pos.y) - 2.5f;
                        float top = Mathf.Max(sm.Pos.y + H, gy + 2.5f * edge);
                        float yy = k < 4 ? Mathf.Lerp(yb, top, f) : top;
                        if (k > 0 && k < 4) yy += 0.7f * (float)(rng.NextDouble() - 0.5);
                        col[k] = new Vector3(p.x, yy, p.z);
                    }
                    prof[i] = col;
                }
                for (int i = 0; i < n; i++)
                {
                    Vector3[] A = prof[i], B = prof[i + 1];
                    Vector3 rd = -tr.SampleAt(Wrap(a + len * (i + 0.5f) / n, tr.Length)).Right * sd;
                    for (int k = 0; k < 3; k++)
                    {
                        Color32 c0 = k == 0 ? cLo : k == 1 ? cMid : cHi;
                        // 層與層之間略微變色(底 -> 頂漸層),相鄰斷面隔一個再變暗一點,看起來像岩層
                        if (((i + k) & 1) == 1) c0 = new Color32((byte)(c0.r * 0.92f), (byte)(c0.g * 0.92f), (byte)(c0.b * 0.92f), 255);
                        // 貼圖:u = 沿路距離 / 5 m,v = 高度 / 5 m(沙岩層 v 依整面高度拉伸成 0..1)
                        float u0 = (a + len * i / n) / 5f, u1 = (a + len * (i + 1) / n) / 5f;
                        float v00, v01, v10, v11;
                        if (wallSlice == RaceTexArray.MtnMesa) { float hw = Mathf.Max(3f, A[3].y - A[0].y); v00 = (A[k].y - A[0].y) / hw; v01 = (A[k + 1].y - A[0].y) / hw; v10 = (B[k].y - B[0].y) / hw; v11 = (B[k + 1].y - B[0].y) / hw; }
                        else { v00 = A[k].y / 5f; v01 = A[k + 1].y / 5f; v10 = B[k].y / 5f; v11 = B[k + 1].y / 5f; }
                        QuadT(acc, A[k], A[k + 1], B[k + 1], B[k], c0, wallSlice, new Vector2(u0, v00), new Vector2(u0, v01), new Vector2(u1, v11), new Vector2(u1, v10), rd);
                    }
                    Quad(acc, A[3], A[4], B[4], B[3], cCap, sd);   // 頂面
                }
                // 橫幅:每 ~55 m 一面,貼在中層牆面前 0.12 m,寬 7 m 高 2.4 m,三色橫條
                int bi = 0;
                for (float bs = a + 30f; bs < a + len - 30f; bs += 55f, bi++)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt((bs - a) / len * n), 0, n - 1);
                    Vector3[] A = prof[i], B = prof[Mathf.Min(n, i + 1)];
                    TrackSample sm = tr.SampleAt(Wrap(bs, tr.Length));
                    Vector3 fwd = sm.Tangent; fwd.y = 0f; fwd.Normalize();
                    Vector3 face = A[1]; face.y = Mathf.Lerp(A[1].y, A[2].y, 0.35f);
                    Vector3 toRoad = -sm.Right * sd;
                    Vector3 o = face + toRoad * 0.25f;
                    {
                        // 一面大橫幅(banner_stripes 貼圖,兩邊各一根柱),上下各一條深色鑲邊
                        Color32 bc = C32(Color.Lerp(Color.white, Banner[(bi + slot) % Banner.Length], 0.22f));
                        Vector3 p0 = o - fwd * 4.2f, p1 = o + fwd * 4.2f, p2 = p1 + Vector3.up * 3.2f, p3 = p0 + Vector3.up * 3.2f;
                        acc.QuadTex(p0, p1, p2, p3, toRoad, bc, RaceTexArray.Banner, new Vector2(0f, 0f), new Vector2(2.1f, 0f), new Vector2(2.1f, 1.1f), new Vector2(0f, 1.1f));
                        Color32 trim = C32(new Color(0.18f, 0.16f, 0.2f));
                        Quad(acc, p0 + Vector3.up * -0.12f + toRoad * 0.02f, p1 + Vector3.up * -0.12f + toRoad * 0.02f, p1 + toRoad * 0.02f, p0 + toRoad * 0.02f, trim, 0, toRoad);
                        Quad(acc, p3 + toRoad * 0.02f, p2 + toRoad * 0.02f, p2 + Vector3.up * 0.12f + toRoad * 0.02f, p3 + Vector3.up * 0.12f + toRoad * 0.02f, trim, 0, toRoad);
                    }
                }
            }
        }

        // 貼圖牆面四邊形:只畫朝路的那一面(toward = 由牆往路的方向)
        static void QuadT(RaceColorAcc acc, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, int slice, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 toward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-8f) return;
            n.Normalize();
            if (Vector3.Dot(n, toward) < 0f) n = -n;
            acc.QuadTex(a, b, c, d, n, col, slice, ua, ub, uc, ud);
        }

        // 一個四邊形(兩個三角形),法線朝路(sd = 牆在路的哪一側;nrmOverride != null 時直接用)
        static void Quad(RaceColorAcc acc, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, int sd, Vector3? nrmOverride = null)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-8f) return;
            n.Normalize();
            if (nrmOverride.HasValue) { if (Vector3.Dot(n, nrmOverride.Value) < 0f) n = -n; }
            else if (n.y < 0.7f)
            {
                // 牆面:法線背離牆體(朝路):與「由牆往路」方向同側。由呼叫端的繞向決定不了,用 y 分量和水平方向猜:取朝向最接近 -(牆中心 - 路) 的那一邊
                // 牆面:兩面都畫(法線各朝一邊;Tri 會自動修正繞向)
                acc.Tri(a, b, c, n, col); acc.Tri(a, c, d, n, col);
                acc.Tri(a, b, c, -n, col); acc.Tri(a, c, d, -n, col);
                return;
            }
            acc.Tri(a, b, c, n, col); acc.Tri(a, c, d, n, col);
        }
    }
}
