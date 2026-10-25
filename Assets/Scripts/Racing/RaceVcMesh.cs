using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    // 頂點色網格工具(模組 W 美術,動森 + 瑪利歐賽車風):
    //   RaceProto         一個可重複擺放的小物件(樹 / 灌木 / 石頭…):共用頂點、平滑法線、每個頂點一個顏色
    //   RaceProtoBuilder  程式生成 RaceProto 的小工具(橢球、旋轉體、圓管、葉片、八面體、稜角石)
    //   RaceColorAcc      把很多 RaceProto 實例(或單獨三角形)累加成一個頂點色網格;一整區只要 1 個 draw call、1 個材質(RaceShaders.ToonVC)
    // 後備:RaceToonVC shader 找不到時,Emit 會依顏色分成最多 4 個桶(葉 / 木 / 亮 / 暗),用 RaceArt.Mat 的固定主題色畫(不會爆材質數)。

    internal sealed class RaceProto
    {
        public Vector3[] V, N;
        public Color32[] C;
        public int[] T;
        public Vector3[] UV;   // 可為 null(沒貼圖);xy = uv、z = RaceTexArray 切片(0 = 無貼圖)
    }

    internal sealed class RaceProtoBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<int> t = new List<int>();
        readonly List<Vector3> uv = new List<Vector3>();
        int slice; float uvK = 1f; bool anyTex;

        /// 之後建立的圓管 / 旋轉體 / 橢球都帶 uv 並取樣貼圖陣列的 s 切片(0 = 不貼);k = 貼圖密度倍率。
        public void UseTex(int s, float k = 1f) { slice = s; uvK = k; if (s > 0) anyTex = true; }

        static Vector3[] ico0, ico1; static int[] icoF0, icoF1;

        static void InitIco()
        {
            if (ico0 != null) return;
            float p = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var u = new List<Vector3>
            {
                new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1)
            };
            for (int i = 0; i < u.Count; i++) u[i] = u[i].normalized;
            icoF0 = new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            ico0 = u.ToArray();
            var u1 = new List<Vector3>(u);
            var f1 = new List<int>();
            var cache = new Dictionary<long, int>();
            for (int i = 0; i < icoF0.Length; i += 3)
            {
                int a = icoF0[i], b = icoF0[i + 1], d = icoF0[i + 2];
                int ab = Mid(u1, cache, a, b), bc = Mid(u1, cache, b, d), ca = Mid(u1, cache, d, a);
                f1.AddRange(new[] { a, ab, ca, b, bc, ab, d, ca, bc, ab, bc, ca });
            }
            ico1 = u1.ToArray(); icoF1 = f1.ToArray();
        }

        static int Mid(List<Vector3> pts, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int r;
            if (cache.TryGetValue(key, out r)) return r;
            pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
            r = pts.Count - 1;
            cache[key] = r;
            return r;
        }

        int Vert(Vector3 p, Vector3 nrm, Color col, float u = 0f, float vv = 0f)
        {
            v.Add(p); n.Add(nrm); c.Add(col); uv.Add(new Vector3(u, vv, slice));
            return v.Count - 1;
        }

        /// 加一個三角形(索引),繞向依「平均法線」自動修正。
        void Face(int a, int b, int d)
        {
            Vector3 cr = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
            if (Vector3.Dot(cr, n[a] + n[b] + n[d]) < 0f) { int tmp = b; b = d; d = tmp; }
            t.Add(a); t.Add(b); t.Add(d);
        }

        static Color Grad3(Color lo, Color mid, Color hi, float y01)
        {
            return y01 < 0.5f ? Color.Lerp(lo, mid, y01 * 2f) : Color.Lerp(mid, hi, (y01 - 0.5f) * 2f);
        }

        /// 橢球(平滑法線),顏色沿 y 由 bottom 漸層到 top。level 0 = 20 面、1 = 80 面。
        public void Blob(Vector3 center, Vector3 radii, int level, Color bottom, Color top, float yawDeg = 0f)
        {
            Blob(center, radii, level, bottom, Color.Lerp(bottom, top, 0.5f), top, yawDeg);
        }

        public void Blob(Vector3 center, Vector3 radii, int level, Color bottom, Color mid, Color top, float yawDeg = 0f)
        {
            InitIco();
            Vector3[] U = level > 0 ? ico1 : ico0; int[] F = level > 0 ? icoF1 : icoF0;
            Quaternion q = Quaternion.Euler(0f, yawDeg, 0f);
            if (slice > 0)
            {
                // 貼圖版:每個三角形各自 3 個頂點(球面 uv,處理接縫與極點),平滑法線
                for (int f = 0; f < F.Length; f += 3)
                {
                    var ids = new int[3]; var us = new float[3]; var vs2 = new float[3]; var vp = new Vector3[3]; var vn = new Vector3[3]; var vc = new Color[3];
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 u = q * U[F[f + k]];
                        vp[k] = center + new Vector3(u.x * radii.x, u.y * radii.y, u.z * radii.z);
                        vn[k] = new Vector3(u.x / Mathf.Max(0.01f, radii.x), u.y / Mathf.Max(0.01f, radii.y), u.z / Mathf.Max(0.01f, radii.z)).normalized;
                        float y01 = u.y * 0.5f + 0.5f;
                        vc[k] = Grad3(bottom, mid, top, y01);
                        us[k] = Mathf.Atan2(u.z, u.x) / (2f * Mathf.PI) + 0.5f;
                        vs2[k] = Mathf.Acos(Mathf.Clamp(u.y, -1f, 1f)) / Mathf.PI;
                    }
                    for (int k = 0; k < 3; k++)
                        if (Mathf.Abs(Mathf.Abs(vs2[k] - 0.5f) - 0.5f) < 0.02f) { us[k] = (us[(k + 1) % 3] + us[(k + 2) % 3]) * 0.5f; }
                    float mx = Mathf.Max(us[0], Mathf.Max(us[1], us[2])), mn = Mathf.Min(us[0], Mathf.Min(us[1], us[2]));
                    if (mx - mn > 0.5f) for (int k = 0; k < 3; k++) if (us[k] < 0.5f) us[k] += 1f;
                    for (int k = 0; k < 3; k++) ids[k] = Vert(vp[k], vn[k], vc[k], us[k] * 1.6f * uvK, vs2[k] * 1.1f * uvK);
                    Face(ids[0], ids[1], ids[2]);
                }
                return;
            }
            int b0 = v.Count;
            for (int i = 0; i < U.Length; i++)
            {
                Vector3 u = q * U[i];
                Vector3 p = center + new Vector3(u.x * radii.x, u.y * radii.y, u.z * radii.z);
                Vector3 nr = new Vector3(u.x / Mathf.Max(0.01f, radii.x), u.y / Mathf.Max(0.01f, radii.y), u.z / Mathf.Max(0.01f, radii.z)).normalized;
                float y01 = u.y * 0.5f + 0.5f;
                Vert(p, nr, Grad3(bottom, mid, top, y01));
            }
            for (int i = 0; i < F.Length; i += 3) Face(b0 + F[i], b0 + F[i + 1], b0 + F[i + 2]);
        }

        /// 旋轉體:rings[k] = (半徑 r, 高度 y, 顏色)。半徑 ~0 的環當尖端。sides 邊。可選底蓋 / 頂蓋。
        public void Lathe(Vector3 baseP, float[] r, float[] y, Color[] col, int sides, bool capBottom, bool capTop, float yawDeg = 0f)
        {
            int R = r.Length;
            bool tex = slice > 0;
            int cols = tex ? sides + 1 : sides;
            int[,] idx = new int[R, cols];
            float yaw = yawDeg * Mathf.Deg2Rad;
            float vAcc = 0f;
            for (int k = 0; k < R; k++)
            {
                if (k > 0) vAcc += Mathf.Sqrt((r[k] - r[k - 1]) * (r[k] - r[k - 1]) + (y[k] - y[k - 1]) * (y[k] - y[k - 1]));
                // 該環的法線斜率:相鄰線段平均
                float dr0 = k > 0 ? r[k] - r[k - 1] : r[k + 1] - r[k], dy0 = k > 0 ? y[k] - y[k - 1] : y[k + 1] - y[k];
                float dr1 = k < R - 1 ? r[k + 1] - r[k] : dr0, dy1 = k < R - 1 ? y[k + 1] - y[k] : dy0;
                float dr = (dr0 + dr1) * 0.5f, dy = (dy0 + dy1) * 0.5f;
                for (int i = 0; i < cols; i++)
                {
                    float a = yaw + i * Mathf.PI * 2f / sides;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 nr = new Vector3(ca * dy, -dr, sa * dy);
                    nr = nr.sqrMagnitude < 1e-8f ? Vector3.up : nr.normalized;
                    idx[k, i] = Vert(baseP + new Vector3(ca * r[k], y[k], sa * r[k]), nr, col[k], (float)i / sides * 3f * uvK, vAcc * 0.35f * uvK);
                }
            }
            for (int k = 0; k < R - 1; k++)
            {
                bool z0 = r[k] < 1e-4f, z1 = r[k + 1] < 1e-4f;
                if (z0 && z1) continue;
                for (int i = 0; i < sides; i++)
                {
                    int j = tex ? i + 1 : (i + 1) % sides;
                    int a = idx[k, i], b = idx[k, j], d = idx[k + 1, j], e = idx[k + 1, i];
                    if (z1) Face(a, b, e);
                    else if (z0) Face(a, d, e);
                    else { Face(a, b, d); Face(a, d, e); }
                }
            }
            if (capBottom) Cap(baseP, r[0], y[0], col[0], sides, yaw, false);
            if (capTop) Cap(baseP, r[R - 1], y[R - 1], col[R - 1], sides, yaw, true);
        }

        void Cap(Vector3 baseP, float rad, float y, Color col, int sides, float yaw, bool up)
        {
            if (rad < 1e-4f) return;
            Vector3 nr = up ? Vector3.up : Vector3.down;
            Color cc = Color.Lerp(col, Color.black, up ? 0f : 0.25f);
            int cen = Vert(baseP + new Vector3(0f, y, 0f), nr, cc);
            int first = v.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = yaw + i * Mathf.PI * 2f / sides;
                Vert(baseP + new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad), nr, cc);
            }
            for (int i = 0; i < sides; i++) Face(cen, first + i, first + (i + 1) % sides);
        }

        /// 任意方向的圓管(錐形),不加蓋。給棕櫚樹幹 / 枯枝用。
        public void Tube(Vector3 p0, Vector3 p1, float r0, float r1, int sides, Color c0, Color c1)
        {
            Vector3 ax = p1 - p0; float len = ax.magnitude;
            if (len < 1e-4f) return;
            ax /= len;
            Vector3 rf = Mathf.Abs(ax.y) < 0.95f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(ax, rf).normalized, w = Vector3.Cross(ax, u).normalized;
            float slope = (r0 - r1) / len;
            int b0 = v.Count;
            int cols = slice > 0 ? sides + 1 : sides;
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 cen = ring == 0 ? p0 : p1; float rr = ring == 0 ? r0 : r1; Color cc = ring == 0 ? c0 : c1;
                for (int i = 0; i < cols; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    Vector3 d = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                    Vert(cen + d * rr, (d + ax * slope).normalized, cc, (float)i / sides * 2f * uvK, ring * len * 0.4f * uvK);
                }
            }
            for (int i = 0; i < sides; i++)
            {
                int j = slice > 0 ? i + 1 : (i + 1) % sides;
                Face(b0 + i, b0 + j, b0 + cols + j);
                Face(b0 + i, b0 + cols + j, b0 + cols + i);
            }
        }

        /// 雙面葉片(棕櫚葉 / 草):從 baseP 經 mid 到 tip,寬度 width(在 sideDir 方向)從中段最寬收到尖端。
        public void Blade(Vector3 baseP, Vector3 mid, Vector3 tip, float width, Vector3 sideDir, Color cBase, Color cTip)
        {
            sideDir = sideDir.normalized;
            Vector3 s = sideDir * (width * 0.5f);
            Color cMid = Color.Lerp(cBase, cTip, 0.5f);
            Vector3 f = (tip - baseP).normalized;
            Vector3 nUp = Vector3.Cross(f, sideDir).normalized;
            if (nUp.y < 0f) nUp = -nUp;
            for (int face = 0; face < 2; face++)
            {
                Vector3 nr = face == 0 ? nUp : -nUp;
                int a = Vert(baseP, nr, cBase), b = Vert(mid - s, nr, cMid), d = Vert(mid + s, nr, cMid), e = Vert(tip, nr, cTip);
                // 正面用 nr 當法線決定繞向
                t.Add(a); t.Add(b); t.Add(d);
                FixLast(nr);
                t.Add(b); t.Add(e); t.Add(d);
                FixLast(nr);
            }
        }

        // 檢查最後一個三角形的繞向是否與 nr 一致,不一致就對調
        void FixLast(Vector3 nr)
        {
            int k = t.Count - 3;
            Vector3 cr = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
            if (Vector3.Dot(cr, nr) < 0f) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
        }

        /// 小八面體(花朵 / 寶石),平面著色,8 面。
        public void Octa(Vector3 center, float rad, float squashY, Color col, Color topCol)
        {
            Vector3[] p = { center + Vector3.right * rad, center - Vector3.right * rad, center + Vector3.forward * rad, center - Vector3.forward * rad,
                            center + Vector3.up * rad * squashY, center - Vector3.up * rad * squashY };
            int[,] f = { { 4, 0, 2 }, { 4, 2, 1 }, { 4, 1, 3 }, { 4, 3, 0 }, { 5, 2, 0 }, { 5, 1, 2 }, { 5, 3, 1 }, { 5, 0, 3 } };
            for (int i = 0; i < 8; i++)
            {
                Vector3 a = p[f[i, 0]], b = p[f[i, 1]], d = p[f[i, 2]];
                Vector3 nr = Vector3.Cross(b - a, d - a).normalized;
                if (Vector3.Dot(nr, (a + b + d) / 3f - center) < 0f) nr = -nr;
                bool top = i < 4;
                int ia = Vert(a, nr, top ? topCol : col), ib = Vert(b, nr, col), id = Vert(d, nr, col);
                Face(ia, ib, id);
            }
        }

        /// 稜角石頭:20 面、頂點被擾動、平面著色;下面深上面淺(top 可以是青苔 / 雪 / 灰燼)。
        public void Rock(Vector3 center, Vector3 radii, int seed, Color bottom, Color top)
        {
            InitIco();
            var rnd = new System.Random(seed);
            var jit = new float[ico0.Length];
            for (int i = 0; i < jit.Length; i++) jit[i] = 0.8f + (float)rnd.NextDouble() * 0.34f;
            for (int i = 0; i < icoF0.Length; i += 3)
            {
                Vector3[] p = new Vector3[3];
                for (int k = 0; k < 3; k++)
                {
                    int vi = icoF0[i + k];
                    Vector3 u = ico0[vi] * jit[vi];
                    p[k] = center + new Vector3(u.x * radii.x, u.y * radii.y, u.z * radii.z);
                }
                Vector3 nr = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
                if (nr.sqrMagnitude < 1e-10f) continue;
                nr.Normalize();
                if (Vector3.Dot(nr, (p[0] + p[1] + p[2]) / 3f - center) < 0f) nr = -nr;
                // 朝上的面染上面色(青苔 / 雪帽),其它面用底色微漸層
                float up = Mathf.Clamp01(nr.y * 1.4f);
                Color cf = Color.Lerp(bottom, top, up);
                float cy = ((p[0].y + p[1].y + p[2].y) / 3f - (center.y - radii.y)) / Mathf.Max(0.01f, radii.y * 2f);
                cf = Color.Lerp(Color.Lerp(cf, Color.black, 0.12f), cf, Mathf.Clamp01(cy * 1.3f));
                int ia = Vert(p[0], nr, cf), ib = Vert(p[1], nr, cf), id = Vert(p[2], nr, cf);
                Face(ia, ib, id);
            }
        }

        public RaceProto Build()
        {
            return new RaceProto { V = v.ToArray(), N = n.ToArray(), C = c.ToArray(), T = t.ToArray(), UV = anyTex ? uv.ToArray() : null };
        }
    }

    /// 頂點色網格累加器。
    internal sealed class RaceColorAcc
    {
        public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        public readonly List<Color32> C = new List<Color32>();
        public readonly List<int> T = new List<int>();

        /// 非 null 時每個頂點帶 uv0 = (u, v, RaceTexArray 切片;0 = 無貼圖)。
        public List<Vector3> UV;
        void PadUV() { if (UV == null) UV = new List<Vector3>(); while (UV.Count < V.Count) UV.Add(Vector3.zero); }

        /// 直接加一塊已索引的網格(世界座標;uvw = u, v, 切片)。
        public void AddRaw(Vector3[] v, Vector3[] n, Vector3[] uvw, Color32[] c, List<int> tris)
        {
            PadUV();
            int b = V.Count;
            V.AddRange(v); N.AddRange(n); C.AddRange(c); UV.AddRange(uvw);
            for (int i = 0; i < tris.Count; i++) T.Add(b + tris[i]);
        }

        /// 貼圖四邊形(法線 nrm 決定朝向);uv 依序對應 a,b,c,d。slice = RaceTexArray 切片。
        public void QuadTex(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, Color32 col, int slice, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            PadUV();
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            N.Add(nrm); N.Add(nrm); N.Add(nrm); N.Add(nrm);
            C.Add(col); C.Add(col); C.Add(col); C.Add(col);
            UV.Add(new Vector3(ua.x, ua.y, slice)); UV.Add(new Vector3(ub.x, ub.y, slice)); UV.Add(new Vector3(uc.x, uc.y, slice)); UV.Add(new Vector3(ud.x, ud.y, slice));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) >= 0f) { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
            else { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
        }

        /// 貼圖三角形(每個頂點 uv;法線 nrm 決定朝向)。
        public void TriTex(Vector3 a, Vector3 b, Vector3 c, Vector3 nrm, Color32 col, int slice, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            PadUV();
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 tp = b; b = c; c = tp; Vector2 tu = ub; ub = uc; uc = tu; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            C.Add(col); C.Add(col); C.Add(col);
            UV.Add(new Vector3(ua.x, ua.y, slice)); UV.Add(new Vector3(ub.x, ub.y, slice)); UV.Add(new Vector3(uc.x, uc.y, slice));
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// 非 null 時每個頂點帶 uv1 動畫參數(RaceToonVC 頂點動畫):x = 類型(0 無、1 布料擺動、2 跳動、3 漂浮、4 火焰閃爍、5 脈動),y = 權重,z = 相位。
        public List<Vector4> A;
        /// 非 null 時每個頂點帶 uv2 = (旋轉中心 xyz, 旋轉軸偏航弧度)(動畫類型 6 = 繞水平軸旋轉,風車葉片用)。
        public List<Vector4> P;
        public static readonly Vector4 NoAnim = Vector4.zero;
        public const float AnimCloth = 1f, AnimHop = 2f, AnimBob = 3f, AnimFlicker = 4f, AnimPulse = 5f, AnimSpin = 6f;

        public int TriCount { get { return T.Count / 3; } }

        void PadA() { if (A != null) while (A.Count < V.Count) A.Add(NoAnim); if (P != null) while (P.Count < V.Count) P.Add(NoAnim); }

        /// 擺一個原型並標記動畫(需要先 A = new List&lt;Vector4&gt;())。
        public void AddAnim(RaceProto p, Vector3 pos, float yawDeg, Vector3 scale, Color32 tint, float type, float weight, float phase)
        {
            PadA();
            Add(p, pos, yawDeg, scale, tint);
            if (A != null) while (A.Count < V.Count) A.Add(new Vector4(type, weight, phase, 0f));
        }

        /// 把另一個動畫網格累加器整個接在後面(兩邊的 A / P 清單都會對齊補零)。
        public void Append(RaceColorAcc o)
        {
            if (o == null || o.V.Count == 0) return;
            PadA(); o.PadA();
            int b = V.Count;
            if (UV != null || o.UV != null) { PadUV(); o.PadUV(); }
            V.AddRange(o.V); N.AddRange(o.N); C.AddRange(o.C);
            if (UV != null) UV.AddRange(o.UV);
            if (A != null) A.AddRange(o.A != null ? (IEnumerable<Vector4>)o.A : new Vector4[o.V.Count]);
            if (P != null) P.AddRange(o.P != null ? (IEnumerable<Vector4>)o.P : new Vector4[o.V.Count]);
            for (int i = 0; i < o.T.Count; i++) T.Add(b + o.T[i]);
        }

        /// 把一個 RaceMeshAcc(世界座標、逐面法線)整個轉進來,染成單色,並標記「繞水平軸旋轉」(風車葉片)。
        /// speedDeg = 度 / 秒(有正負)、phaseDeg = 初始角度、pivot = 旋轉中心(世界座標)、axisYaw = 旋轉軸方向(水平,弧度,軸 = (sin, 0, cos))。需要 A 與 P 都不是 null。
        public void AddSpinning(RaceMeshAcc m, Color32 col, float speedDeg, float phaseDeg, Vector3 pivot, float axisYaw)
        {
            PadA();
            int b = V.Count;
            for (int i = 0; i < m.V.Count; i++)
            {
                V.Add(m.V[i]); N.Add(m.N[i]); C.Add(col);
                A.Add(new Vector4(AnimSpin, speedDeg, phaseDeg, 0f));
                P.Add(new Vector4(pivot.x, pivot.y, pivot.z, axisYaw));
            }
            for (int i = 0; i < m.T.Count; i++) T.Add(b + m.T[i]);
        }

        /// 單獨三角形 + 動畫標記(旗 / 布)。
        public void TriAnim(Vector3 a, Vector3 b, Vector3 d, Vector3 nrm, Color32 col, float type, float w0, float w1, float w2, float phase, float sign = 1f)
        {
            PadA();
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), nrm) < 0f) { Vector3 tmp = b; b = d; d = tmp; float tw = w1; w1 = w2; w2 = tw; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(d);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            C.Add(col); C.Add(col); C.Add(col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
            if (A != null) { A.Add(new Vector4(type, w0, phase, sign)); A.Add(new Vector4(type, w1, phase, sign)); A.Add(new Vector4(type, w2, phase, sign)); }
        }

        static Color32 Mul(Color32 a, Color32 b)
        {
            return new Color32((byte)(a.r * b.r / 255), (byte)(a.g * b.g / 255), (byte)(a.b * b.b / 255), (byte)(a.a * b.a / 255));
        }

        /// 擺一個原型:縮放(非等比時法線用倒數縮放修正)→ 繞 y 轉 → 平移;tint 乘在頂點色上。
        public void Add(RaceProto p, Vector3 pos, float yawDeg, Vector3 scale, Color32 tint)
        {
            if (p.UV != null) { PadUV(); UV.AddRange(p.UV); }
            Quaternion q = Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 inv = new Vector3(1f / Mathf.Max(0.001f, scale.x), 1f / Mathf.Max(0.001f, scale.y), 1f / Mathf.Max(0.001f, scale.z));
            int b = V.Count;
            for (int i = 0; i < p.V.Length; i++)
            {
                V.Add(pos + q * Vector3.Scale(p.V[i], scale));
                N.Add((q * Vector3.Scale(p.N[i], inv)).normalized);
                C.Add(Mul(p.C[i], tint));
            }
            for (int i = 0; i < p.T.Length; i++) T.Add(b + p.T[i]);
        }

        /// 單獨三角形(平面法線 nrm,繞向自動修正)。
        public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 nrm, Color32 col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), nrm) < 0f) { Vector3 tmp = b; b = d; d = tmp; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(d);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            C.Add(col); C.Add(col); C.Add(col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// 單獨三角形(每個頂點各自的法線與顏色,繞向依平均法線修正)。
        public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 na, Vector3 nb, Vector3 nd, Color32 ca, Color32 cb, Color32 cd)
        {
            Vector3 cr = Vector3.Cross(b - a, d - a);
            if (cr.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(cr, na + nb + nd) < 0f) { Vector3 tp = b; b = d; d = tp; Vector3 tn = nb; nb = nd; nd = tn; Color32 tc = cb; cb = cd; cd = tc; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(d);
            N.Add(na); N.Add(nb); N.Add(nd);
            C.Add(ca); C.Add(cb); C.Add(cd);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            List<Color32> cs = C;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                // 頂點色不會被引擎自動轉線性,這裡先轉好(材質色 / 調色盤貼圖會自動轉)
                cs = new List<Color32>(C.Count);
                for (int i = 0; i < C.Count; i++) { Color lc = ((Color)C[i]).linear; lc.a = C[i].a / 255f; cs.Add(lc); }
            }
            m.SetVertices(V); m.SetNormals(N); m.SetColors(cs);
            if (UV != null) { PadUV(); m.SetUVs(0, UV); }
            m.SetTriangles(T, 0);
            if (A != null) { PadA(); m.SetUVs(1, A); }
            if (P != null) { PadA(); m.SetUVs(2, P); }
            m.RecalculateBounds();
            // 動畫網格:頂點會被 shader 位移,邊界放大避免被誤裁切
            if (A != null) { Bounds bd = m.bounds; bd.Expand(P != null ? 24f : 2f); m.bounds = bd; }
            return m;
        }

        // ------------------------------------------------------------------ 輸出成 GameObject

        /// 後備桶:0 葉(綠)、1 木(暖褐 / 橘 / 紅)、2 亮(白 / 粉 / 黃)、3 暗(灰 / 藍 / 黑)。
        static int Bucket(Color32 c)
        {
            float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
            float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
            if (mn > 0.72f) return 2;
            if (g >= r * 1.02f && g >= b * 1.05f && mx - mn > 0.08f) return 0;
            if (r > b * 1.12f && mx - mn > 0.1f) return mx > 0.8f && g > 0.7f ? 2 : 1;
            return 3;
        }

        /// 建出 Renderer(們),回傳新增的 Renderer 數與三角形數經由 ref。parent 底下 GameObject 名稱 name。
        public void Emit(Transform parent, string name, List<Object> owned, List<Renderer> renderers, ref int tris, ref int draws, bool receiveShadows = false)
        {
            if (T.Count == 0) return;
            Material vc = RaceShaders.ToonVC();
            if (vc != null)
            {
                Make(parent, name, this, vc, owned, renderers, receiveShadows);
                tris += TriCount; draws++;
                return;
            }
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
            Color[] bk = { th.Leaves != null && th.Leaves.Length > 1 ? th.Leaves[1] : RacePalette.Leaves[1], RacePalette.Trunk, Color.white, th.Rock };
            for (int k = 0; k < 4; k++)
            {
                RaceColorAcc sub = null;
                for (int i = 0; i + 2 < T.Count; i += 3)
                {
                    int a = T[i], b = T[i + 1], d = T[i + 2];
                    if (Bucket(C[a]) != k) continue;
                    if (sub == null) sub = new RaceColorAcc();
                    sub.Tri(V[a], V[b], V[d], N[a], N[b], N[d], new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
                }
                if (sub == null) continue;
                Make(parent, name + "_" + k, sub, RaceArt.Mat(bk[k], 0.1f), owned, renderers, receiveShadows);
                tris += sub.TriCount; draws++;
            }
        }

        static void Make(Transform parent, string name, RaceColorAcc acc, Material mat, List<Object> owned, List<Renderer> renderers, bool receiveShadows)
        {
            Mesh m = acc.ToMesh(name);
            owned.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = receiveShadows;
            if (renderers != null) renderers.Add(r);
        }
    }
}
