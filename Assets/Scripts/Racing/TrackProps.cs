using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 模組 W / 環境細節與造景:全部程式生成、低多邊形、圓潤可愛(動物森友會 + 瑪利歐賽車的味道),沒有外部素材。
    /// 內容(依 theme 不同):
    ///   通用:路面磨損 / 裂縫 / 油漬 / 輪胎痕(貼地面片)、起跑區旗幟與格子看板、彎前箭頭警示牌(chevron)、里程牌、觀眾席(低多邊形人形)、氣球拱門、橫幅。
    ///   島嶼:花叢、石牆、池塘木橋、六角小屋、旗幟串、看板。
    ///   峽谷:3 種仙人掌、枯樹、木鷹架、木棧橋、石柱群、礦車軌道、帳篷、看板。
    ///   海岸:多變化棕櫚、海灘傘、碼頭、小船、浮標、岩礁(含浪花)、海鷗(整群繞圈)、海灘小屋。
    /// 效能:全部依顏色合併成一個網格(每種顏色一個 draw call,共約 15 個),沒有碰撞體;三角形硬上限 TriCap(約 2.9 萬)。
    /// 規則:所有地面物件的圓形足跡(Result.Foot)離路面邊緣 >= 6.5 m、不擺在彎道內側(擋視線)、不在跳台 / 加速板上;氣球拱門的拱在路面上方 >= 11 m。
    /// 材質一律 RaceArt.Mat(顏色快取);新增顏色只有:金、粉、天藍、奶油、磨損路、輪胎痕(其餘是樹 / 地標 / 路緣已在用的顏色)。
    internal static class TrackProps
    {
        public sealed class Result
        {
            public int Items, Tris, Draws, Colliders, Decals, Chevrons, ChevronsInside, Stands, Arches, Spectators, Birds, Cacti, Palms, Flowers;
            public float MinRoadDist = 999f;      // 所有足跡圓離路面邊緣的最短距離(已扣半徑)
            public float MinOverhead = 999f;      // 氣球拱門在路面範圍內的最低高度(離路面)
            public float MinDecalMargin = 999f;   // 路面貼花離路面邊緣的最短距離
            public readonly List<Vector4> Foot = new List<Vector4>();   // xyz = 位置,w = 半徑
            public readonly Dictionary<string, int> Kinds = new Dictionary<string, int>();
            public void Count(string k) { int n; Kinds.TryGetValue(k, out n); Kinds[k] = n + 1; Items++; }
        }

        public const int TriCap = 28500;

        static Color HC(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
        static readonly Color Red = RacePalette.CurbRed, White = RacePalette.CurbWhite, Wood = RacePalette.Trunk;
        static readonly Color Orange = new Color(1f, 0.54f, 0.16f);   // 與地標 / 加速板同色 → 共用快取材質
        static readonly Color Gold = RacePalette.Gold, Pink = HC("#FF8FB8"), Blue = HC("#4DB3FF"), Cream = HC("#FFF0D2");
        static readonly Color Dark = new Color(0.10f, 0.10f, 0.12f);   // 與 TrackMeshBuilder.DarkLine 同色
        static readonly Color[] Fan = { Red, Gold, Blue, Pink, White, Orange };

        // ================================================================== 入口

        /// terrain 取自 RaceWorld.Terrain(建圖時已設好)。landmarks = 地標底座(避開);density 0.5 = 低品質。
        public static Result Build(int theme, RaceTrack tr, System.Random rng, Transform parent, List<Object> owned, IList<Vector3> landmarks, float density, RaceColorAcc extraAnim = null)
        {
            var res = new Result();
            RaceTerrain terr = RaceWorld.Terrain;
            if (terr == null || tr == null) return res;
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(theme);
            var root = new GameObject("TrackProps");
            if (parent != null) root.transform.SetParent(parent, false);

            var c = new Ctx { tr = tr, t = terr, rng = rng, th = th, res = res, b = new Batch(), L = tr.Length, density = density };
            if (landmarks != null) for (int i = 0; i < landmarks.Count; i++) c.AddBig(landmarks[i].x, landmarks[i].z, 13f);
            for (int i = 0; i < tr.Landmarks.Count; i++)
            {
                var k = (TrackLandmarkKind)tr.Landmarks[i].kind;
                if (k == TrackLandmarkKind.BigArch || k == TrackLandmarkKind.RockArch) c.archS.Add(tr.Landmarks[i].s);
            }
            c.corners = Corners(tr);

            RoadDetails(c);
            StartArea(c);
            Chevrons(c);
            Markers(c);
            Arches(c);
            Stands(c);
            Banners(c);
            TireStacks(c);
            switch (theme)
            {
                case 1: Canyon(c); break;
                case 2: Coast(c, root.transform, owned); break;
                case 0: Island(c); break;
                default: Scenery(c); break;   // 新主題(0/1/2 以外):依 RaceLook 風格挑景物組
            }
            AnimScenery(c, theme);

            // 合併網格:每種顏色一個
            for (int i = 0; i < c.b.Count; i++)
            {
                RaceMeshAcc acc = c.b.AccAt(i);
                if (acc.TriCount == 0) continue;
                Mesh m = acc.ToMesh("TrackProps_" + i);
                owned.Add(m);
                var go = new GameObject("TrackProps_" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = RaceArt.Mat(c.b.ColorAt(i), 0.1f);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                res.Tris += acc.TriCount; res.Draws++;
            }
            if (extraAnim != null) c.wave.Append(extraAnim);
            if (c.wave.TriCount > 0)
            {
                int wt = 0, wd = 0;
                c.wave.Emit(root.transform, "TrackAnim", owned, null, ref wt, ref wd);
                res.Tris += wt; res.Draws += wd;
            }
            res.Tris += c.extraTris; res.Draws += c.extraDraws;
            return res;
        }

        // ================================================================== 合併用的小工具

        sealed class Batch
        {
            readonly List<Color> cols = new List<Color>();
            readonly List<RaceMeshAcc> accs = new List<RaceMeshAcc>();
            public int Count { get { return accs.Count; } }
            public RaceMeshAcc AccAt(int i) { return accs[i]; }
            public Color ColorAt(int i) { return cols[i]; }
            public RaceMeshAcc A(Color c)
            {
                for (int i = 0; i < cols.Count; i++)
                {
                    Color o = cols[i];
                    if (Mathf.Abs(o.r - c.r) + Mathf.Abs(o.g - c.g) + Mathf.Abs(o.b - c.b) < 0.004f) return accs[i];
                }
                cols.Add(c);
                var a = new RaceMeshAcc();
                accs.Add(a);
                return a;
            }
            public int Tris { get { int n = 0; for (int i = 0; i < accs.Count; i++) n += accs[i].TriCount; return n; } }
        }

        /// 物件座標系:x = 右、y = 上、z = 前(面向賽道),k = 縮放。
        struct Fr
        {
            public Vector3 o, r, u, f; public float k;
            public static Fr Face(Vector3 o, Vector3 fwd, float k)
            {
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                fwd.Normalize();
                return new Fr { o = o, u = Vector3.up, f = fwd, r = Vector3.Cross(Vector3.up, fwd).normalized, k = k };
            }
            public Vector3 P(float x, float y, float z) { return o + (r * x + u * y + f * z) * k; }
            public Vector3 Dir(Vector3 n) { return (r * n.x + u * n.y + f * n.z).normalized; }
            public Fr At(float x, float y, float z) { Fr c = this; c.o = P(x, y, z); return c; }
            public Fr Yaw(float deg) { float a = deg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a); Fr c = this; c.f = f * cs + r * sn; c.r = r * cs - f * sn; return c; }
            public Fr Pitch(float deg) { float a = deg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a); Fr c = this; c.f = f * cs + u * sn; c.u = u * cs - f * sn; return c; }
        }

        static void AddTri(RaceMeshAcc a, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 n0, Vector3 n1, Vector3 n2)
        {
            Vector3 cr = Vector3.Cross(p1 - p0, p2 - p0);
            if (cr.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(cr, n0 + n1 + n2) < 0f) { Vector3 tp = p1; p1 = p2; p2 = tp; Vector3 tn = n1; n1 = n2; n2 = tn; }
            int i = a.V.Count;
            a.V.Add(p0); a.V.Add(p1); a.V.Add(p2);
            a.N.Add(n0); a.N.Add(n1); a.N.Add(n2);
            a.T.Add(i); a.T.Add(i + 1); a.T.Add(i + 2);
        }

        static Vector3[] ico0, ico1; static int[] icoF0, icoF1;

        static void InitIco()
        {
            if (ico0 != null) return;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            icoF0 = new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            ico0 = v.ToArray();
            var v1 = new List<Vector3>(v);
            var f1 = new List<int>();
            for (int i = 0; i < icoF0.Length; i += 3)
            {
                int a = icoF0[i], b = icoF0[i + 1], c = icoF0[i + 2];
                int ab = v1.Count; v1.Add(((v[a] + v[b]) * 0.5f).normalized);
                int bc = v1.Count; v1.Add(((v[b] + v[c]) * 0.5f).normalized);
                int ca = v1.Count; v1.Add(((v[c] + v[a]) * 0.5f).normalized);
                f1.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            ico1 = v1.ToArray(); icoF1 = f1.ToArray();
        }

        /// 圓潤的橢球(平滑法線)。hi = 80 個三角形,否則 20 個。
        static void Ball(Batch b, Color col, Fr fr, float cx, float cy, float cz, float rx, float ry, float rz, bool hi = false)
        {
            InitIco();
            RaceMeshAcc a = b.A(col);
            Vector3[] V = hi ? ico1 : ico0; int[] F = hi ? icoF1 : icoF0;
            var p = new Vector3[3]; var n = new Vector3[3];
            for (int i = 0; i < F.Length; i += 3)
            {
                for (int j = 0; j < 3; j++)
                {
                    Vector3 u = V[F[i + j]];
                    p[j] = fr.P(cx + u.x * rx, cy + u.y * ry, cz + u.z * rz);
                    n[j] = fr.Dir(new Vector3(u.x / Mathf.Max(0.01f, rx), u.y / Mathf.Max(0.01f, ry), u.z / Mathf.Max(0.01f, rz)));
                }
                AddTri(a, p[0], p[1], p[2], n[0], n[1], n[2]);
            }
        }

        static void Bx(Batch b, Color col, Fr fr, float cx, float cy, float cz, float hx, float hy, float hz)
        {
            b.A(col).Box(fr.P(cx, cy, cz), fr.r, fr.u, fr.f, new Vector3(hx, hy, hz) * fr.k);
        }

        /// p0 到 p1 的圓柱 / 圓錐(半徑已含縮放),平滑法線。
        static void Tube(Batch b, Color col, Vector3 p0, Vector3 p1, float r0, float r1, int sides, bool caps)
        {
            RaceMeshAcc a = b.A(col);
            Vector3 ax = p1 - p0; float len = ax.magnitude;
            if (len < 1e-4f) return;
            ax /= len;
            Vector3 rf = Mathf.Abs(ax.y) < 0.95f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(ax, rf).normalized, w = Vector3.Cross(ax, u);
            float slope = (r0 - r1) / len;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                Vector3 n0 = (d0 + ax * slope).normalized, n1 = (d1 + ax * slope).normalized;
                Vector3 b0 = p0 + d0 * r0, b1 = p0 + d1 * r0, t0 = p1 + d0 * r1, t1 = p1 + d1 * r1;
                AddTri(a, b0, b1, t1, n0, n1, n1);
                if (r1 > 0.005f) AddTri(a, b0, t1, t0, n0, n1, n0);
                if (caps)
                {
                    if (r1 > 0.01f) a.Tri(p1, t0, t1, ax);
                    if (r0 > 0.01f) a.Tri(p0, b1, b0, -ax);
                }
            }
        }

        static void Cyl(Batch b, Color col, Fr fr, float cx, float cy, float cz, float r0, float r1, float h, int sides, bool caps = true)
        {
            Tube(b, col, fr.P(cx, cy, cz), fr.P(cx, cy + h, cz), r0 * fr.k, r1 * fr.k, sides, caps);
        }

        /// 兩點之間的長方條。thick 沿 nref 方向(貼在面上的線條要把 nref 設成面法線)。
        static void Beam(Batch b, Color col, Vector3 p0, Vector3 p1, float width, float thick, Vector3 nref)
        {
            Vector3 d = p1 - p0; float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 fwd = d / len;
            Vector3 up = nref - fwd * Vector3.Dot(nref, fwd);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.up - fwd * Vector3.Dot(Vector3.up, fwd);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.right - fwd * Vector3.Dot(Vector3.right, fwd);
            up.Normalize();
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            b.A(col).Box((p0 + p1) * 0.5f, right, up, fwd, new Vector3(width * 0.5f, thick * 0.5f, len * 0.5f));
        }

        /// 雙面三角形(旗、布)。
        static void Dbl(Batch b, Color col, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            RaceMeshAcc a = b.A(col);
            a.Tri(p0, p1, p2, n); a.Tri(p0, p1, p2, -n);
        }

        static void DblQuad(Batch b, Color col, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { Dbl(b, col, p0, p1, p2); Dbl(b, col, p0, p2, p3); }

        /// 隨風擺動的雙面三角形(RaceToonVC 頂點動畫類型 1);w0..w2 = 各頂點擺幅權重(0 = 固定在桿 / 繩上)。
        static void ClothTri(Ctx c, Color col, Vector3 p0, Vector3 p1, Vector3 p2, float w0, float w1, float w2, float phase)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            c.wave.TriAnim(p0, p1, p2, n, col, RaceColorAcc.AnimCloth, w0, w1, w2, phase, 1f);
            c.wave.TriAnim(p0, p1, p2, -n, col, RaceColorAcc.AnimCloth, w0, w1, w2, phase, -1f);
        }

        static void ClothQuad(Ctx c, Color col, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float w0, float w1, float w2, float w3, float phase)
        {
            ClothTri(c, col, p0, p1, p2, w0, w1, w2, phase);
            ClothTri(c, col, p0, p2, p3, w0, w2, w3, phase);
        }

        /// 圓錐傘 / 帳篷:每一片交替兩種顏色。
        static void Canopy(Batch b, Color cA, Color cB, Fr fr, float cx, float cy, float cz, float r, float h, int sides)
        {
            Vector3 tip = fr.P(cx, cy + h, cz);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides, am = (a0 + a1) * 0.5f;
                Vector3 b0 = fr.P(cx + Mathf.Cos(a0) * r, cy, cz + Mathf.Sin(a0) * r), b1 = fr.P(cx + Mathf.Cos(a1) * r, cy, cz + Mathf.Sin(a1) * r);
                Vector3 n = fr.Dir(new Vector3(Mathf.Cos(am) * h, r, Mathf.Sin(am) * h));
                b.A((i & 1) == 0 ? cA : cB).Tri(b0, b1, tip, n);
            }
        }

        // ================================================================== 擺放環境

        struct Cand { public Vector3 pos, toRoad; public float s, lat, dist; public int side; public TrackSample sm; }

        struct Corner { public float s0, s1, sgn, kmax; }

        sealed class Ctx
        {
            public RaceTrack tr; public RaceTerrain t; public System.Random rng; public TrackTheme th; public Result res; public Batch b;
            public float L, density; public int extraTris, extraDraws;
            public readonly RaceColorAcc wave = new RaceColorAcc { A = new List<Vector4>(), P = new List<Vector4>() };   // 動畫網格(旗 / 觀眾 / 氣球 / 火炬):RaceToonVC 頂點動畫,整條賽道 1 個 draw call
            public readonly List<float> archS = new List<float>();
            public List<Corner> corners;
            readonly List<Vector4> big = new List<Vector4>();
            readonly Dictionary<int, List<Vector4>> grid = new Dictionary<int, List<Vector4>>();
            const float GC = 8f;

            public float F() { return (float)rng.NextDouble(); }
            public float F(float a, float b2) { return a + (float)rng.NextDouble() * (b2 - a); }
            public bool Room() { return b.Tris < TriCap; }
            public int N(int baseCount) { return Mathf.Max(1, Mathf.RoundToInt(baseCount * density)); }
            public float G(float x, float z) { return Mathf.Max(0f, t.Raw(x, z)); }

            static int Key(int cx, int cz) { return (cx + 512) * 1024 + (cz + 512); }

            public void AddBig(float x, float z, float r) { big.Add(new Vector4(x, 0f, z, r)); }

            /// 登記一個足跡圓(road = true 會納入「離路面 >= 6.5 m」的統計與測試)。
            public void Occupy(float x, float z, float r)
            {
                res.Foot.Add(new Vector4(x, G(x, z), z, r));
                int cx = Mathf.FloorToInt(x / GC), cz = Mathf.FloorToInt(z / GC);
                int k = Key(cx, cz);
                List<Vector4> l;
                if (!grid.TryGetValue(k, out l)) { l = new List<Vector4>(); grid[k] = l; }
                l.Add(new Vector4(x, 0f, z, r));
            }

            public bool Free(float x, float z, float r)
            {
                for (int i = 0; i < big.Count; i++) { float dx = big[i].x - x, dz = big[i].z - z, rr = big[i].w + r; if (dx * dx + dz * dz < rr * rr) return false; }
                int cx = Mathf.FloorToInt(x / GC), cz = Mathf.FloorToInt(z / GC);
                int span = Mathf.CeilToInt((r + 8f) / GC);
                for (int dz = -span; dz <= span; dz++)
                    for (int dx = -span; dx <= span; dx++)
                    {
                        List<Vector4> l;
                        if (!grid.TryGetValue(Key(cx + dx, cz + dz), out l)) continue;
                        for (int i = 0; i < l.Count; i++) { float ex = l[i].x - x, ez = l[i].z - z, rr = l[i].w + r + 0.4f; if (ex * ex + ez * ez < rr * rr) return false; }
                    }
                return true;
            }

            public bool NearArch(float s, float clear)
            {
                for (int i = 0; i < archS.Count; i++) { float d = Mathf.Abs(s - archS[i]); d = Mathf.Min(d, L - d); if (d < clear) return true; }
                float d0 = Mathf.Min(s, L - s);
                return d0 < 8f;   // 起終點拱門
            }

            /// 地形可站:在地圖內、不是海 / 海灘(minH 以下)、不陡;r > 1.5 時再量四周高度差。
            public bool Land(float x, float z, float r, float maxDelta, float minH)
            {
                float lim = RaceTerrain.Half - 26f;
                if (x < -lim || x > lim || z < -lim || z > lim) return false;
                float h0 = t.Raw(x, z);
                if (h0 < minH) return false;
                if (t.Normal(x, z).y < 0.88f) return false;
                if (r > 1.5f)
                {
                    float q = r * 0.7f;
                    for (int i = 0; i < 4; i++)
                    {
                        float px = x + ((i & 1) == 0 ? q : -q), pz = z + (i < 2 ? q : -q);
                        float h = t.Raw(px, pz);
                        if (h < minH - 0.3f || Mathf.Abs(h - h0) > maxDelta) return false;
                    }
                }
                return true;
            }

            /// 離路面邊緣(扣掉半徑)>= gap,而且離捷徑夠遠。
            public bool Road(float x, float z, float r, float gap, out float dist, out float s, out float lat)
            {
                s = tr.Project(new Vector3(x, 0f, z), -1f, out lat);
                dist = Mathf.Abs(lat) - tr.HalfWidthAt(s) - r;
                if (dist < gap) return false;
                if (t.ScEdgeAt(x, z) < gap - 1f + r) return false;
                return true;
            }

            /// 隨機挑一個候選點:mode 0 = 陸地、1 = 海灘(raw 0.35~1.6)、2 = 水(raw <= -1)。回傳 false = 這次沒挑到。
            public bool Pick(int mode, float r, float maxOff, bool sight, int tries, out Cand c)
            {
                c = default(Cand);
                for (int n = 0; n < tries; n++)
                {
                    float s = F() * L;
                    TrackSample sm = tr.SampleAt(s);
                    int side = F() < 0.5f ? -1 : 1;
                    float hw = sm.Width * 0.5f;
                    float off = (mode == 2 ? 8f : 0f) + Mathf.Pow(F(), mode == 0 ? 1.5f : 1f) * maxOff;
                    float lat = hw + 6.5f + r + 0.3f + off;
                    Vector3 p = sm.Pos + sm.Right * (side * lat) + sm.Tangent * ((F() - 0.5f) * 6f);
                    float raw = t.Raw(p.x, p.z);
                    float lim = RaceTerrain.Half - 26f;
                    if (p.x < -lim || p.x > lim || p.z < -lim || p.z > lim) continue;
                    if (mode == 0 && !Land(p.x, p.z, r, 1.4f, 0.8f)) continue;
                    if (mode == 1 && (raw < 0.35f || raw > 1.6f || t.Normal(p.x, p.z).y < 0.85f)) continue;
                    if (mode == 2 && raw > -1.0f) continue;
                    float dist, s2, lat2;
                    if (!Road(p.x, p.z, r, 6.5f, out dist, out s2, out lat2)) continue;
                    if (NearArch(s2, 18f) || NearArch(s, 18f)) continue;
                    if (sight && dist < 45f)
                    {
                        float k = tr.MaxCurvature(s2 - 30f, s2 + 30f);
                        if (Mathf.Abs(k) > 1f / 110f && lat2 * k > 0f) continue;   // 彎的內側:擋視線
                    }
                    if (!Free(p.x, p.z, r)) continue;
                    p.y = mode == 2 ? 0f : G(p.x, p.z);
                    c = new Cand { pos = p, toRoad = sm.Right * (-side), s = s, lat = lat, dist = dist, side = side, sm = sm };
                    return true;
                }
                return false;
            }
        }

        static List<Corner> Corners(RaceTrack tr)
        {
            var list = new List<Corner>();
            var S = tr.Samples; const float thr = 1f / 100f;
            bool inC = false; Corner cur = default(Corner);
            for (int i = 0; i < S.Count; i++)
            {
                float k = S[i].Curvature, a = Mathf.Abs(k);
                if (!inC)
                {
                    if (a > thr) { inC = true; cur = new Corner { s0 = S[i].S, sgn = Mathf.Sign(k), kmax = a }; }
                }
                else if (a < thr * 0.7f || Mathf.Sign(k) != cur.sgn)
                {
                    cur.s1 = S[i].S; inC = false;
                    if (cur.s1 - cur.s0 >= 20f) list.Add(cur);
                    if (a > thr) { inC = true; cur = new Corner { s0 = S[i].S, sgn = Mathf.Sign(k), kmax = a }; }
                }
                else cur.kmax = Mathf.Max(cur.kmax, a);
            }
            if (inC) { cur.s1 = S[S.Count - 1].S; if (cur.s1 - cur.s0 >= 20f) list.Add(cur); }
            return list;
        }

        /// 賽道旁某弧長 s、側(-1 左 / +1 右)、離中心線 lat 公尺的地面點。
        static Vector3 RoadPt(Ctx c, float s, int side, float lat)
        {
            TrackSample sm = c.tr.SampleAt(c.tr.Wrap(s));
            Vector3 p = sm.Pos + sm.Right * (side * lat);
            p.y = c.G(p.x, p.z);
            return p;
        }

        /// 路邊物件的位置檢查(小物件):離路 >= 6.5 + r、地形平、沒被佔用。
        static bool SideOk(Ctx c, Vector3 p, float r, float minH)
        {
            float d, s, lat;
            if (!c.Land(p.x, p.z, r, 1.0f, minH)) return false;
            if (!c.Road(p.x, p.z, r, 6.5f, out d, out s, out lat)) return false;
            if (c.NearArch(s, 14f)) return false;
            return c.Free(p.x, p.z, r);
        }

        static bool OnFeat(Vector3 p)
        {
            FeatureSet f = RaceWorld.Features;
            if (f == null) return false;
            float off; Vector3 tilt; Surface sf;
            return f.RampOffset(p.x, p.z, out off, out tilt) || f.TrySurface(p.x, p.z, out sf);
        }

        // ================================================================== 賽道本身的細節(貼地面片)

        static Vector3 RP(Ctx c, float s, float lat, float y)
        {
            TrackSample sm = c.tr.SampleAt(c.tr.Wrap(s));
            return sm.Pos + sm.Right * lat + Vector3.up * y;
        }

        static void Stroke(RaceMeshAcc a, Vector3 p0, float w0, Vector3 p1, float w1)
        {
            Vector3 d = p1 - p0; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector3 n = Vector3.Cross(Vector3.up, d.normalized);
            a.Quad(p0 - n * (w0 * 0.5f), p0 + n * (w0 * 0.5f), p1 + n * (w1 * 0.5f), p1 - n * (w1 * 0.5f), Vector3.up);
        }

        static void RoadDetails(Ctx c)
        {
            RaceTrack tr = c.tr; float L = c.L;
            Color roadBase = Color.Lerp(c.th.Asphalt, new Color(0.95f, 0.92f, 0.88f), 0.62f);   // 與 TrackMeshBuilder 的路面色一致(補丁/輪痕才不會變成一塊塊深色方塊)
            Color wear = Color.Lerp(roadBase, Color.white, 0.10f);
            Color skid = Color.Lerp(roadBase, Dark, 0.45f);
            RaceMeshAcc aw = c.b.A(wear), ak = c.b.A(skid);
            const float yW = 0.085f, yC = 0.087f, yS = 0.088f;   // 都低於標線 0.09,所以線畫在上面
            int before = aw.TriCount + ak.TriCount;

            // 磨損 / 補丁
            int nW = c.N(Mathf.RoundToInt(L / 16f));
            for (int i = 0; i < nW; i++)
            {
                float s = c.F() * L, len = c.F(3f, 9f), wid = c.F(1.2f, 3f);
                float d0 = Mathf.Min(s, L - s);
                if (d0 < 14f) continue;
                float hw = tr.HalfWidthAt(s);
                float room = hw - 1.3f - wid * 0.5f;
                if (room < 0.5f) continue;
                float lat = c.F(-1f, 1f) * room;
                Vector3 m0 = RP(c, s, lat, 0f), m1 = RP(c, s + len, lat, 0f);
                if (OnFeat(m0) || OnFeat(m1) || OnFeat((m0 + m1) * 0.5f)) continue;
                RaceMeshAcc a = (i % 3 == 0) ? ak : aw;
                a.Quad(RP(c, s, lat - wid * 0.5f, yW), RP(c, s, lat + wid * 0.5f, yW), RP(c, s + len, lat + wid * 0.5f + c.F(-0.3f, 0.3f), yW), RP(c, s + len, lat - wid * 0.5f, yW), Vector3.up);
                c.res.MinDecalMargin = Mathf.Min(c.res.MinDecalMargin, hw - (Mathf.Abs(lat) + wid * 0.5f + 0.3f));
            }

            // 裂縫
            int nC = c.N(Mathf.RoundToInt(L / 20f));
            for (int i = 0; i < nC; i++)
            {
                float s = c.F() * L;
                if (Mathf.Min(s, L - s) < 14f) continue;
                float hw = tr.HalfWidthAt(s), lim = hw - 1.4f;
                if (lim < 0.5f) continue;
                float lat = c.F(-1f, 1f) * lim;
                Vector3 prev = RP(c, s, lat, yC);
                if (OnFeat(prev)) continue;
                int segs = 4 + (int)(c.F() * 3f);
                for (int j = 0; j < segs; j++)
                {
                    s += c.F(0.8f, 1.6f); lat = Mathf.Clamp(lat + c.F(-0.5f, 0.5f), -lim, lim);
                    Vector3 cur = RP(c, s, lat, yC);
                    Stroke(ak, prev, 0.07f - j * 0.005f, cur, 0.065f - j * 0.005f);
                    if (c.F() < 0.3f)
                    {
                        float bl = Mathf.Clamp(lat + c.F(-1f, 1f), -lim, lim);
                        Stroke(ak, cur, 0.05f, RP(c, s + c.F(-0.5f, 0.5f), bl, yC), 0.03f);
                    }
                    prev = cur;
                }
                c.res.MinDecalMargin = Mathf.Min(c.res.MinDecalMargin, hw - Mathf.Abs(lat) - 0.2f);
            }

            // 油漬
            int nO = c.N(14);
            for (int i = 0; i < nO; i++)
            {
                float s = c.F() * L;
                if (Mathf.Min(s, L - s) < 16f) continue;
                float hw = tr.HalfWidthAt(s), rad = c.F(0.5f, 1.1f);
                float room = hw - 1.3f - rad;
                if (room < 0.4f) continue;
                float lat = c.F(-1f, 1f) * room;
                Vector3 ctr = RP(c, s, lat, yS);
                if (OnFeat(ctr)) continue;
                TrackSample sm = tr.SampleAt(tr.Wrap(s));
                Vector3 prev = Vector3.zero, first = Vector3.zero;
                const int K = 9;
                for (int j = 0; j <= K; j++)
                {
                    float ang = j * Mathf.PI * 2f / K, rr = rad * c.F(0.7f, 1.2f);
                    Vector3 p = ctr + sm.Right * (Mathf.Cos(ang) * rr) + sm.Tangent * (Mathf.Sin(ang) * rr * 1.4f);
                    if (j == 0) first = p;
                    else ak.Tri(ctr, prev, j == K ? first : p, Vector3.up);
                    prev = j == K ? first : p;
                }
                c.res.MinDecalMargin = Mathf.Min(c.res.MinDecalMargin, hw - (Mathf.Abs(lat) + rad * 1.2f));
            }

            // 輪胎痕:彎道上從外側劃向內側,彎前一段直線煞車痕
            for (int ci = 0; ci < c.corners.Count; ci++)
            {
                Corner cn = c.corners[ci];
                int marks = 3;
                for (int m = 0; m < marks; m++)
                {
                    bool brake = m == 0;
                    float len = brake ? 13f : c.F(10f, 18f);
                    float s0 = brake ? cn.s0 - len - 3f : cn.s0 + c.F(-4f, 0.45f * (cn.s1 - cn.s0));
                    if (s0 < 18f || s0 + len > c.L - 18f) continue;
                    float hw = tr.HalfWidthAt(s0 + len * 0.5f);
                    float outLat = -cn.sgn * hw * (brake ? 0.3f : 0.5f), inLat = brake ? outLat : cn.sgn * hw * 0.45f;
                    for (int wl = -1; wl <= 1; wl += 2)
                    {
                        bool ok = true;
                        Vector3 prev = Vector3.zero; bool havePrev = false;
                        int steps = Mathf.Max(3, Mathf.RoundToInt(len / 2f));
                        for (int j = 0; j <= steps; j++)
                        {
                            float t = j / (float)steps, e = t * t * (3f - 2f * t);
                            float lat = Mathf.Lerp(outLat, inLat, e) + wl * 0.6f;
                            Vector3 p = RP(c, s0 + t * len, lat, yS);
                            if (OnFeat(p)) { ok = false; break; }
                            float w = 0.22f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) + 0.06f;
                            if (havePrev) Stroke(ak, prev, Mathf.Max(0.06f, w * 0.9f), p, w);
                            prev = p; havePrev = true;
                            c.res.MinDecalMargin = Mathf.Min(c.res.MinDecalMargin, hw - Mathf.Abs(lat) - 0.15f);
                        }
                        if (!ok) break;
                    }
                }
            }
            c.res.Decals = (aw.TriCount + ak.TriCount) - before;
        }

        // ================================================================== 起跑區:旗、格子看板

        static Fr FaceDriver(Vector3 pos, TrackSample sm, int side, float k, float deg)
        {
            Vector3 toRoad = sm.Right * (-side);
            float a = deg * Mathf.Deg2Rad;
            return Fr.Face(pos, toRoad * Mathf.Cos(a) - sm.Tangent * Mathf.Sin(a), k);
        }

        static void StartArea(Ctx c)
        {
            RaceTrack tr = c.tr; Batch b = c.b;
            float[] offs = { -34f, -22f, -10f, 12f, 24f, 36f };
            int ci = 0;
            for (int i = 0; i < offs.Length; i++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float s = tr.Wrap(offs[i]);
                    TrackSample sm = tr.SampleAt(s);
                    Vector3 p = RoadPt(c, s, side, sm.Width * 0.5f + 7.2f);
                    if (!SideOk(c, p, 0.5f, 0.3f)) continue;
                    Fr fr = Fr.Face(p, sm.Right * (-side), 1f);
                    Cyl(b, White, fr, 0, 0, 0, 0.1f, 0.08f, 5.6f, 6, false);
                    Ball(b, Gold, fr, 0, 5.75f, 0, 0.2f, 0.2f, 0.2f);
                    Color fc = Fan[(ci++) % Fan.Length];
                    Vector3 p0 = fr.P(0.05f, 5.5f, 0), p1 = fr.P(0.05f, 4.5f, 0), tip = fr.P(0.05f, 5.0f, 0) + sm.Tangent * 1.4f + Vector3.up * 0.12f;
                    Dbl(b, fc, p0, p1, tip);
                    c.Occupy(p.x, p.z, 0.5f); c.res.Count("startFlag");
                }

            // 格子看板(起跑線前 8 m,兩側各一塊,斜向迎著來車)
            for (int side = -1; side <= 1; side += 2)
            {
                float s = tr.Wrap(-9f);
                TrackSample sm = tr.SampleAt(s);
                float lat = sm.Width * 0.5f + 8.2f;
                Vector3 p = RoadPt(c, s, side, lat);
                if (!SideOk(c, p, 1.5f, 0.3f)) continue;
                Fr fr = FaceDriver(p, sm, side, 1f, 22f);
                Bx(b, Cream, fr, 0, 2.75f, -0.1f, 3.1f, 0.7f, 0.05f);
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 12; col++)
                    {
                        Color cc = ((row + col) & 1) == 0 ? Dark : White;
                        float x0 = -3f + col * 0.5f, y0 = 2.2f + row * 0.5f;
                        b.A(cc).Quad(fr.P(x0, y0 + 0.5f, 0.06f), fr.P(x0 + 0.5f, y0 + 0.5f, 0.06f), fr.P(x0 + 0.5f, y0, 0.06f), fr.P(x0, y0, 0.06f), fr.f);
                    }
                Cyl(b, Wood, fr, -2.6f, 0, -0.1f, 0.12f, 0.12f, 2.2f, 6, false);
                Cyl(b, Wood, fr, 2.6f, 0, -0.1f, 0.12f, 0.12f, 2.2f, 6, false);
                Ball(b, Pink, fr, -3.1f, 3.5f, -0.1f, 0.25f, 0.25f, 0.25f);
                Ball(b, Pink, fr, 3.1f, 3.5f, -0.1f, 0.25f, 0.25f, 0.25f);
                c.Occupy(p.x, p.z, 0.8f);
                Vector3 pl = fr.P(-2.6f, 0, 0), pr = fr.P(2.6f, 0, 0);
                c.Occupy(pl.x, pl.z, 0.5f); c.Occupy(pr.x, pr.z, 0.5f);
                c.res.Count("startBoard");
            }
        }

        // ================================================================== 彎前箭頭警示牌 / 里程牌

        static void Chevrons(Ctx c)
        {
            Batch b = c.b; RaceTrack tr = c.tr; int cap = c.N(32);
            for (int ci = 0; ci < c.corners.Count && c.res.Chevrons < cap; ci++)
            {
                Corner cn = c.corners[ci];
                int count = Mathf.Clamp((int)((cn.s1 - cn.s0) / 24f), 2, 4);
                for (int j = 0; j < count && c.res.Chevrons < cap; j++)
                {
                    if (!c.Room()) return;
                    float s = Mathf.Lerp(cn.s0 + 6f, cn.s1 - 6f, (j + 0.5f) / count);
                    if (s < 10f || s > c.L - 10f) continue;
                    TrackSample sm = tr.SampleAt(s);
                    int outSide = cn.sgn > 0f ? -1 : 1;   // 右彎 → 外側在左
                    Vector3 p = RoadPt(c, s, outSide, sm.Width * 0.5f + 7.8f);
                    if (!SideOk(c, p, 1.0f, 0.5f)) continue;
                    Fr fr = FaceDriver(p, sm, outSide, 1.1f, 32f);
                    float d = Mathf.Sign(Vector3.Dot(sm.Right * cn.sgn, fr.r));
                    if (d == 0f) d = 1f;
                    Cyl(b, White, fr, -0.45f, 0, 0, 0.07f, 0.07f, 1.7f, 6, false);
                    Cyl(b, White, fr, 0.45f, 0, 0, 0.07f, 0.07f, 1.7f, 6, false);
                    Bx(b, Gold, fr, 0, 2.3f, 0, 0.85f, 0.8f, 0.05f);
                    Bx(b, White, fr, 0, 2.3f, -0.03f, 0.95f, 0.9f, 0.04f);
                    Vector3 tip = fr.P(d * 0.35f, 2.3f, 0.08f), up = fr.P(-d * 0.3f, 2.78f, 0.08f), dn = fr.P(-d * 0.3f, 1.82f, 0.08f);
                    Beam(b, Dark, up, tip, 0.2f * fr.k, 0.04f, fr.f);
                    Beam(b, Dark, dn, tip, 0.2f * fr.k, 0.04f, fr.f);
                    c.Occupy(p.x, p.z, 1.0f);
                    c.res.Chevrons++; c.res.Count("chevron");
                    // 內側檢查(測試用):牌子必須在彎的外側
                    if (outSide * cn.sgn > 0f) c.res.ChevronsInside++;
                }
            }
        }

        static void Markers(Ctx c)
        {
            Batch b = c.b; int idx = 0;
            for (float s = 110f; s < c.L - 40f; s += 140f, idx++)
            {
                if (!c.Room()) return;
                int side = (idx & 1) == 0 ? 1 : -1;
                TrackSample sm = c.tr.SampleAt(s);
                Vector3 p = RoadPt(c, s, side, sm.Width * 0.5f + 7.0f);
                if (!SideOk(c, p, 0.5f, 0.5f)) continue;
                Fr fr = Fr.Face(p, sm.Right * (-side), 1f);
                Cyl(b, Cream, fr, 0, 0, 0, 0.26f, 0.2f, 1.3f, 6);
                Ball(b, Red, fr, 0, 1.45f, 0, 0.3f, 0.3f, 0.3f);
                Bx(b, Pink, fr, 0, 0.8f, 0.2f, 0.14f, 0.2f, 0.03f);
                if ((idx % 3) == 2) Ball(b, Gold, fr, 0, 1.85f, 0, 0.14f, 0.14f, 0.14f);
                c.Occupy(p.x, p.z, 0.5f); c.res.Count("marker");
            }
        }

        // ================================================================== 氣球拱門 / 橫幅 / 觀眾席

        static void Arches(Ctx c)
        {
            RaceTrack tr = c.tr;
            float[] cand = { c.L - 60f, c.L * 0.5f, c.L * 0.27f, c.L * 0.78f };
            int made = 0;
            // 兩輪:先要求近乎直線,找不到再放寬(仍然不擺在彎內側以外的規則 — 拱門橫跨路面,兩根柱都在路外 >= 6.5 m)
            for (int pass = 0; pass < 2 && made == 0; pass++)
            {
                float kmax = pass == 0 ? 1f / 220f : 1f / 110f;
                for (int ci = 0; ci < cand.Length && made < 3; ci++)
                {
                    if (!c.Room()) return;
                    for (float dsn = 0f; dsn < 200f; dsn += 8f)
                    {
                        float s = tr.Wrap(cand[ci] + dsn);
                        if (c.NearArch(s, 26f)) continue;
                        if (Mathf.Abs(tr.MaxCurvature(s - 12f, s + 12f)) > kmax) continue;
                        TrackSample sm = tr.SampleAt(s);
                        float R = sm.Width * 0.5f + 8.3f;   // 柱中心離路面邊緣 8.3 m,扣掉足跡半徑 1.5 m 仍 >= 6.5 m
                        Vector3 pl = RoadPt(c, s, -1, R), pr = RoadPt(c, s, 1, R);
                        if (!SideOk(c, pl, 1.5f, 0.3f) || !SideOk(c, pr, 1.5f, 0.3f)) continue;
                        if (OnFeat(sm.Pos)) continue;
                        BalloonArch(c, sm, R, pl, pr);
                        made++;
                        break;
                    }
                }
            }
        }

        static void BalloonArch(Ctx c, TrackSample sm, float R, Vector3 pl, Vector3 pr)
        {
            Batch b = c.b;
            Fr fl = Fr.Face(pl, sm.Tangent, 1f), fr2 = Fr.Face(pr, sm.Tangent, 1f);
            Tube(b, White, new Vector3(pl.x, Mathf.Min(pl.y, sm.Pos.y) - 0.6f, pl.z), sm.Pos + sm.Right * (-R) + Vector3.up * 1.4f, 0.16f, 0.12f, 6, false);
            Tube(b, White, new Vector3(pr.x, Mathf.Min(pr.y, sm.Pos.y) - 0.6f, pr.z), sm.Pos + sm.Right * R + Vector3.up * 1.4f, 0.16f, 0.12f, 6, false);
            const int N = 24;
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < N; i++)
                {
                    float th = Mathf.Lerp(0.1f, Mathf.PI - 0.1f, (i + row * 0.5f) / (N - 0.5f));
                    float rr = R + (row == 0 ? 0f : 0.35f);
                    Vector3 p = sm.Pos + sm.Right * (Mathf.Cos(th) * rr) + Vector3.up * (Mathf.Sin(th) * rr + 0.3f) + sm.Tangent * (row == 0 ? -0.25f : 0.3f);
                    Color col = Fan[(i * 2 + row * 3) % Fan.Length];
                    float br = 0.62f - row * 0.08f;
                    Ball(b, col, Fr.Face(p, sm.Tangent, 1f), 0, 0, 0, br, br * 1.1f, br);
                    if (Mathf.Abs(Mathf.Cos(th) * rr) < sm.Width * 0.5f + 1f)
                        c.res.MinOverhead = Mathf.Min(c.res.MinOverhead, p.y - br - sm.Pos.y);
                }
            // 地面的氣球簇
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 bp = side < 0 ? pl : pr;
                for (int i = 0; i < 4; i++)
                    Ball(b, Fan[(i + (side < 0 ? 0 : 2)) % Fan.Length], Fr.Face(bp, sm.Tangent, 1f), c.F(-0.5f, 0.5f), 0.7f + i * 0.35f, c.F(-0.5f, 0.5f), 0.5f, 0.55f, 0.5f);
                c.Occupy(bp.x, bp.z, 1.5f);
            }
            c.res.Arches++; c.res.Count("balloonArch");
        }

        static void Banner(Ctx c, float s0, float len, int side, Color c1, Color c2)
        {
            Batch b = c.b; RaceTrack tr = c.tr;
            float hw = tr.HalfWidthAt(s0), lat = hw + 7.0f;
            const int segs = 6;
            var pts = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                pts[i] = RoadPt(c, s0 + len * i / segs, side, lat);
                if (!SideOk(c, pts[i], 0.5f, 0.3f)) return;
            }
            float y0 = pts[0].y;
            for (int i = 0; i <= segs; i += segs)
                Tube(b, Wood, pts[i] + Vector3.down * 0.3f, pts[i] + Vector3.up * 4.1f, 0.1f, 0.08f, 6, false);
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = pts[i], bb = pts[i + 1];
                TrackSample sm = tr.SampleAt(tr.Wrap(s0 + len * i / segs));
                Vector3 w0 = sm.Right * (Mathf.Sin(i * 1.3f) * 0.18f), w1 = sm.Right * (Mathf.Sin((i + 1) * 1.3f) * 0.18f);
                Vector3 h0 = Vector3.up * 3.9f, h1 = Vector3.up * 2.9f, h2 = Vector3.up * 1.9f;
                float ph = s0 * 0.37f + i * 0.9f;
                float e0 = 0.25f + 0.75f * Mathf.Sin(Mathf.PI * (i + 0.0f) / segs), e1 = 0.25f + 0.75f * Mathf.Sin(Mathf.PI * (i + 1.0f) / segs);
                ClothQuad(c, c1, a + h0 + w0, bb + h0 + w1, bb + h1 + w1, a + h1 + w0, 0.25f * e0, 0.25f * e1, 0.8f * e1, 0.8f * e0, ph);
                ClothQuad(c, c2, a + h1 + w0, bb + h1 + w1, bb + h2 + w1, a + h2 + w0, 0.8f * e0, 0.8f * e1, 1.0f * e1, 1.0f * e0, ph);
            }
            Ball(b, Gold, Fr.Face(pts[0], Vector3.forward, 1f), 0, 4.15f, 0, 0.18f, 0.18f, 0.18f);
            Ball(b, Gold, Fr.Face(pts[segs], Vector3.forward, 1f), 0, 4.15f, 0, 0.18f, 0.18f, 0.18f);
            for (int i = 0; i <= segs; i += 2) c.Occupy(pts[i].x, pts[i].z, 0.6f);
            c.res.Count("banner");
        }

        static void Banners(Ctx c)
        {
            Color[,] cols = { { Pink, White }, { Blue, Gold }, { Red, White }, { Gold, Pink } };
            Banner(c, c.tr.Wrap(15f), 24f, 1, Pink, White);
            Banner(c, c.tr.Wrap(15f), 24f, -1, Blue, Gold);
            int made = 0;
            for (int tries = 0; tries < 80 && made < c.N(5) && c.Room(); tries++)
            {
                float s = c.F(60f, c.L - 80f);
                int side = c.F() < 0.5f ? -1 : 1;
                if (c.NearArch(s, 30f) || Mathf.Abs(c.tr.MaxCurvature(s - 5f, s + 30f)) > 1f / 200f) continue;
                int k = made % 4;
                int before = c.res.Items;
                Banner(c, s, 24f, side, cols[k, 0], cols[k, 1]);
                if (c.res.Items > before) made++;
            }
        }

        static void Stands(Ctx c)
        {
            RaceTrack tr = c.tr; int made = 0, want = c.N(3);
            float[] sc = { 58f + 10f, c.L - 85f, c.L * 0.5f, c.L * 0.3f, c.L * 0.7f };
            int[] sides = { 1, -1, 1, -1, 1 };
            for (int ci = 0; ci < sc.Length && made < want; ci++)
            {
                for (float dsn = 0f; dsn < 160f && made < want; dsn += 10f)
                {
                    if (!c.Room()) return;
                    float s = tr.Wrap(sc[ci] + dsn);
                    int side = sides[ci];
                    if (c.NearArch(s, 24f) || Mathf.Min(s, c.L - s) < 18f) continue;
                    if (Mathf.Abs(tr.MaxCurvature(s - 24f, s + 24f)) > 1f / 260f) continue;
                    TrackSample sm = tr.SampleAt(s);
                    float lat = sm.Width * 0.5f + 8.6f;
                    Vector3 p = RoadPt(c, s, side, lat);
                    Fr fr = Fr.Face(p, sm.Right * (-side), 1f);
                    if (!StandSpotOk(c, fr)) continue;
                    BuildStand(c, fr, made % 3);
                    made++;
                    break;
                }
            }
            // 地形陡的賽道(Canyon)嚴格條件擺不出來:沿整條賽道、兩側、放寬坡度/高差再找(至少 1 座)
            if (made == 0)
            {
                for (float s0 = 40f; s0 < c.L - 40f && made < 1; s0 += 8f)
                    for (int sd = -1; sd <= 1 && made < 1; sd += 2)
                    {
                        if (!c.Room()) return;
                        float s = tr.Wrap(s0);
                        if (c.NearArch(s, 24f) || Mathf.Abs(tr.MaxCurvature(s - 24f, s + 24f)) > 1f / 200f) continue;
                        TrackSample sm = tr.SampleAt(s);
                        Vector3 p = RoadPt(c, s, sd, sm.Width * 0.5f + 8.6f);
                        Fr fr = Fr.Face(p, sm.Right * (-sd), 1f);
                        if (!StandSpotOk(c, fr, true)) continue;
                        BuildStand(c, fr, 0);
                        made++;
                    }
            }
        }

        static bool StandSpotOk(Ctx c, Fr fr, bool relax = false)
        {
            float lo = 999f, hi = -999f;
            for (int zi = 0; zi < 3; zi++)
                for (int xi = -1; xi <= 1; xi++)
                {
                    Vector3 q = fr.P(xi * 7.5f, 0, 0.8f - zi * 3.2f);
                    float h = c.t.Raw(q.x, q.z);
                    if (h < 0.8f || c.t.Normal(q.x, q.z).y < (relax ? 0.78f : 0.86f)) return false;
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
            if (hi - lo > (relax ? 3.0f : 1.7f)) return false;
            for (int zi = 0; zi < 3; zi++)
                for (int xi = -2; xi <= 2; xi++)
                {
                    Vector3 q = fr.P(xi * 3.2f, 0, -zi * 2.3f);
                    float d, s, lat;
                    if (!c.Road(q.x, q.z, 1.8f, 6.5f, out d, out s, out lat)) return false;
                    if (!c.Free(q.x, q.z, 1.8f)) return false;
                }
            return true;
        }

        static void BuildStand(Ctx c, Fr fr, int variant)
        {
            Batch b = c.b;
            Color tierA = variant == 0 ? Cream : (variant == 1 ? Cream : Wood);
            Color panel = variant == 0 ? Pink : (variant == 1 ? Blue : Red);
            float baseY = -1.8f;
            for (int t = 0; t < 3; t++)
            {
                float top = 0.9f * (t + 1), zc = -0.45f - t * 1.9f;
                Bx(b, (t & 1) == 0 ? tierA : White, fr, 0, (top + baseY) * 0.5f, zc, 8f, (top - baseY) * 0.5f, 0.95f);
                for (int i = 0; i < 7; i++)
                {
                    float x = -7f + i * (14f / 6f) + c.F(-0.2f, 0.2f);
                    Spectator(c, fr, x, top, zc + c.F(-0.2f, 0.2f));
                }
            }
            Bx(b, panel, fr, 0, 0.0f, 0.52f, 8.05f, 0.5f, 0.04f);
            for (int i = 0; i < 8; i++) Ball(b, Fan[i % Fan.Length], fr, -7f + i * 2f, 0.55f, 0.6f, 0.34f, 0.34f, 0.18f);
            // 屋頂
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = 0; sz < 2; sz++)
                    Cyl(b, Wood, fr, sx * 7.7f, 0, sz == 0 ? 0.3f : -5.4f, 0.14f, 0.14f, 5.6f, 6, false);
            for (int i = 0; i < 4; i++)
                Bx(b, (i & 1) == 0 ? panel : White, fr, -6f + i * 4f, 5.75f, -2.6f, 2f, 0.12f, 3.4f);
            Bx(b, Gold, fr, 0, 5.35f, 0.85f, 8.1f, 0.22f, 0.05f);
            for (int zi = 0; zi < 3; zi++)
                for (int xi = -2; xi <= 2; xi++)
                {
                    Vector3 q = fr.P(xi * 3.2f, 0, -zi * 2.3f);
                    c.Occupy(q.x, q.z, 1.8f);
                }
            c.res.Stands++; c.res.Count("stand");
        }

        // ================================================================== 動畫造景(頂點動畫,零 C# 逐幀成本)

        static RaceProto balloonProto, flameProto, bubbleProto, flameCoreProto;

        /// 所有賽道:路邊旗桿(尖旗隨風擺);雪地:漂浮氣球串;火山:閃爍火炬與熔岩氣泡。
        static void AnimScenery(Ctx c, int theme)
        {
            Cand k;
            // 旗桿
            for (int i = 0, n = c.N(7), made = 0; made < n && i < 100 && c.Room(); i++)
            {
                if (!c.Pick(0, 0.5f, 6f, true, 40, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                Tube(c.b, White, fr.P(0, -0.2f, 0), fr.P(0, 5.6f, 0), 0.09f, 0.06f, 6, false);
                Ball(c.b, Gold, fr, 0, 5.72f, 0, 0.14f, 0.14f, 0.14f);
                Color fc = Fan[(made * 2 + 1) % Fan.Length];
                float ph = made * 1.9f;
                Vector3 a0 = fr.P(0, 5.5f, 0), a1 = fr.P(0, 4.5f, 0), tip = fr.P(0.1f, 5.0f, 2.1f);
                ClothTri(c, fc, a0, a1, tip, 0f, 0f, 1f, ph);
                Vector3 b0 = fr.P(0, 4.5f, 0), b1 = fr.P(0, 3.8f, 0), tip2 = fr.P(0.1f, 4.2f, 1.5f);
                ClothTri(c, Fan[(made * 2 + 3) % Fan.Length], b0, b1, tip2, 0f, 0f, 1f, ph + 0.8f);
                c.Occupy(k.pos.x, k.pos.z, 0.5f); c.res.Count("flagPole"); made++;
            }
            LookKind lk = RaceLook.For(c.th).Kind;
            if (balloonProto == null)
            {
                var bb = new RaceProtoBuilder();
                bb.Blob(Vector3.zero, new Vector3(0.5f, 0.62f, 0.5f), 0, new Color(0.8f, 0.8f, 0.8f), Color.white);
                balloonProto = bb.Build();
                var fb = new RaceProtoBuilder();
                fb.Blob(new Vector3(0f, 0.38f, 0f), new Vector3(0.24f, 0.46f, 0.24f), 0, new Color(1f, 0.3f, 0f, 0.1f), new Color(1f, 0.75f, 0.1f, 0.1f));
                flameProto = fb.Build();
                var cb = new RaceProtoBuilder();
                cb.Blob(new Vector3(0f, 0.28f, 0f), new Vector3(0.12f, 0.26f, 0.12f), 0, new Color(1f, 0.9f, 0.4f, 0.05f), new Color(1f, 1f, 0.7f, 0.05f));
                flameCoreProto = cb.Build();
                var ub = new RaceProtoBuilder();
                ub.Blob(Vector3.zero, new Vector3(0.7f, 0.55f, 0.7f), 0, new Color(1f, 0.35f, 0.05f, 0.12f), new Color(1f, 0.8f, 0.2f, 0.12f));
                bubbleProto = ub.Build();
            }
            if (lk == LookKind.Snow || lk == LookKind.Meadow || lk == LookKind.Coast)
            {
                // 漂浮氣球串:一根固定的小樁 + 3~5 顆氣球(類型 3 漂浮),雪地多一點
                int want = c.N(lk == LookKind.Snow ? 9 : 4);
                for (int i = 0, made = 0; made < want && i < 60 && c.Room(); i++)
                {
                    if (!c.Pick(0, 0.9f, 22f, true, 40, out k)) break;
                    Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                    Cyl(c.b, Wood, fr, 0, -0.1f, 0, 0.12f, 0.09f, 0.7f, 6, true);
                    int nb = 3 + (made % 3);
                    for (int j = 0; j < nb; j++)
                    {
                        float ox = c.F(-0.8f, 0.8f), oz = c.F(-0.8f, 0.8f), oy = 2.6f + c.F() * 1.6f;
                        Color bc = Fan[(made + j) % Fan.Length];
                        Tube(c.b, White, fr.P(0, 0.55f, 0), fr.P(ox, oy, oz), 0.012f, 0.012f, 3, false);
                        c.wave.AddAnim(balloonProto, fr.P(ox, oy + 0.5f, oz), 0f, Vector3.one, bc, RaceColorAcc.AnimBob, 1f, c.F() * 6.28f);
                    }
                    c.Occupy(k.pos.x, k.pos.z, 0.9f); c.res.Count("balloons"); made++;
                }
            }
            if (lk == LookKind.Volcano)
            {
                // 火炬:石柱 + 閃爍的發光火焰(頂點 alpha < 1 = 自發光;類型 4 閃爍)
                for (int i = 0, n = c.N(12), made = 0; made < n && i < 100 && c.Room(); i++)
                {
                    if (!c.Pick(0, 0.5f, 7f, true, 40, out k)) break;
                    Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                    Color rock = Color.Lerp(c.th.Rock, Dark, 0.55f);
                    Tube(c.b, rock, fr.P(0, -0.2f, 0), fr.P(0, 2.5f, 0), 0.22f, 0.15f, 6, true);
                    Cyl(c.b, Dark, fr, 0, 2.5f, 0, 0.32f, 0.3f, 0.18f, 6, true);
                    Vector3 fp = fr.P(0, 2.66f, 0);
                    float ph = c.F() * 6.28f;
                    c.wave.AddAnim(flameProto, fp, 0f, Vector3.one, Color.white, RaceColorAcc.AnimFlicker, 1f, ph);
                    c.wave.AddAnim(flameCoreProto, fp, 0f, Vector3.one, Color.white, RaceColorAcc.AnimFlicker, 1f, ph + 1.3f);
                    c.Occupy(k.pos.x, k.pos.z, 0.5f); c.res.Count("torch"); made++;
                }
                // 熔岩氣泡池:暗色岩石圈 + 起伏脈動的發光氣泡(類型 5)
                for (int i = 0, n = c.N(6), made = 0; made < n && i < 60 && c.Room(); i++)
                {
                    if (!c.Pick(0, 2.4f, 40f, true, 50, out k)) break;
                    Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                    Vector3 pc = fr.P(0, 0.12f, 0);
                    // 池底:發光圓盤(用 Ball 壓扁 = 20 面)
                    c.wave.AddAnim(bubbleProto, pc + Vector3.up * 0.1f, 0f, new Vector3(1.9f, 0.35f, 1.9f), Color.white, RaceColorAcc.AnimPulse, 0.35f, c.F() * 6.28f);
                    for (int bI = 0; bI < 3; bI++)
                        c.wave.AddAnim(bubbleProto, pc + new Vector3(c.F(-1f, 1f), 0.2f, c.F(-1f, 1f)), 0f, Vector3.one * c.F(0.35f, 0.6f), Color.white, RaceColorAcc.AnimPulse, 1f, c.F() * 6.28f);
                    for (int q = 0; q < 5; q++)
                    {
                        float a = q * 1.2566f;
                        Ball(c.b, Color.Lerp(Dark, c.th.Rock, 0.35f), fr, Mathf.Cos(a) * 1.9f, 0.25f, Mathf.Sin(a) * 1.9f, 0.9f, 0.55f, 0.9f);
                    }
                    c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("lavaPool"); made++;
                }
            }
        }

        static RaceProto specBody, specHead;

        static void Spectator(Ctx c, Fr fr, float x, float y, float z)
        {
            // 觀眾:頂點動畫跳動(類型 2),相位隨機;不需要每個物件跑 C#
            Color shirt = Fan[(int)(c.F() * Fan.Length) % Fan.Length];
            Color skin = c.F() < 0.7f ? Cream : Orange;
            float ph = c.F() * 6.28f;
            Vector3 p = fr.P(x, y, z);
            float yaw = Mathf.Atan2(fr.f.x, fr.f.z) * Mathf.Rad2Deg;
            if (specBody == null)
            {
                var pb = new RaceProtoBuilder();
                pb.Lathe(Vector3.zero, new[] { 0.3f, 0.22f }, new[] { 0f, 0.75f }, new[] { Color.white, Color.white }, 5, true, true);
                specBody = pb.Build();
                var hb = new RaceProtoBuilder();
                hb.Blob(new Vector3(0f, 0.95f, 0f), new Vector3(0.21f, 0.21f, 0.21f), 0, Color.white, Color.white);
                specHead = hb.Build();
            }
            c.wave.AddAnim(specBody, p, yaw, Vector3.one * fr.k, shirt, RaceColorAcc.AnimHop, 1f, ph);
            c.wave.AddAnim(specHead, p, yaw, Vector3.one * fr.k, skin, RaceColorAcc.AnimHop, 1f, ph);
            c.res.Spectators++;
        }

        // ================================================================== 輪胎堆(彎道外側的緩衝區)

        /// 緊彎的外側、護欄後面放幾堆紅白輪胎(像瑪利歐賽車的彎道緩衝),每堆 3 個輪胎(底下兩個 + 上面一個)。純外觀,沒有碰撞體;離路面邊緣 >= 6.5 m。
        static void TireStacks(Ctx c)
        {
            int cap = c.N(14), made = 0;
            for (int ci = 0; ci < c.corners.Count && made < cap; ci++)
            {
                Corner cn = c.corners[ci];
                if (cn.kmax < 1f / 75f) continue;   // 只有緊彎
                int outSide = cn.sgn > 0f ? -1 : 1;
                float mid = (cn.s0 + cn.s1) * 0.5f;
                for (int j = -1; j <= 1 && made < cap; j++)
                {
                    if (!c.Room()) return;
                    float s = c.tr.Wrap(mid + j * 7f);
                    if (c.NearArch(s, 16f)) continue;
                    TrackSample sm = c.tr.SampleAt(s);
                    Vector3 p = RoadPt(c, s, outSide, sm.Width * 0.5f + 8.0f);
                    if (!SideOk(c, p, 1.0f, 0.4f)) continue;
                    Fr fr = Fr.Face(p, sm.Right * (-outSide), 1f);
                    Color band = ((made + j + 8) & 1) == 0 ? Red : White;
                    for (int t = 0; t < 3; t++)
                    {
                        float x = t == 0 ? -0.62f : (t == 1 ? 0.62f : 0f), y = t == 2 ? 0.42f : 0f;
                        Cyl(c.b, t == 2 ? band : Dark, fr, x, y, 0, 0.62f, 0.56f, 0.42f, 8, t == 2);   // 最上面那個帶色環 + 頂蓋
                    }
                    c.Occupy(p.x, p.z, 1.3f); c.res.Count("tireStack"); made++;
                }
            }
        }

        // ================================================================== 風格景物組(新主題用,位置規則同島嶼 / 峽谷)

        static void Scenery(Ctx c)
        {
            switch (RaceLook.For(c.th).Kind)
            {
                case LookKind.Desert: Canyon(c); break;
                case LookKind.Snow: SnowScene(c); break;
                case LookKind.Forest: ForestScene(c); break;
                case LookKind.Volcano: VolcanoScene(c); break;
                default: Island(c); break;
            }
        }

        static void CommonSigns(Ctx c, int cottages, Color roof)
        {
            Cand k;
            for (int i = 0, n = c.N(cottages); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.8f, 60f, true, 120, out k)) break;
                Cottage(c, FaceDriver(k.pos, k.sm, k.side, 1f, 15f), i % 2 == 0 ? roof : Red);
                c.Occupy(k.pos.x, k.pos.z, 3.8f); c.res.Count("cottage");
            }
            for (int i = 0, n = c.N(4), made = 0; made < n && i < 80 && c.Room(); i++)
            {
                float s = c.F() * (c.L - 60f) + 30f;
                int side = c.F() < 0.5f ? -1 : 1;
                if (Bunting(c, s, side)) made++;
            }
            for (int i = 0, n = c.N(5); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.6f, 18f, true, 80, out k)) break;
                Billboard(c, FaceDriver(k.pos, k.sm, k.side, 1f, 25f), i % 3);
                c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("billboard");
            }
        }

        // ---- 雪地:雪人、冰屋、冰晶、藍屋頂小屋
        static void SnowScene(Ctx c)
        {
            Batch b = c.b; Cand k;
            Color snow = new Color(0.97f, 0.99f, 1f), ice = Color.Lerp(Blue, White, 0.45f);
            for (int i = 0, n = c.N(12); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.2f, 40f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.3f));
                Ball(b, snow, fr, 0, 0.55f, 0, 0.7f, 0.55f, 0.7f);
                Ball(b, snow, fr, 0, 1.3f, 0, 0.52f, 0.46f, 0.52f);
                Ball(b, snow, fr, 0, 1.95f, 0, 0.4f, 0.38f, 0.4f);
                Tube(b, Orange, fr.P(0, 1.93f, 0.3f), fr.P(0, 1.88f, 0.85f), 0.07f * fr.k, 0.0f, 5, false);   // 胡蘿蔔鼻
                Ball(b, Dark, fr, -0.14f, 2.05f, 0.3f, 0.05f, 0.05f, 0.05f);
                Ball(b, Dark, fr, 0.14f, 2.05f, 0.3f, 0.05f, 0.05f, 0.05f);
                Cyl(b, Red, fr, 0, 1.55f, 0, 0.42f, 0.42f, 0.14f, 8, false);   // 圍巾
                Bx(b, Red, fr, 0.25f, 1.25f, 0.3f, 0.07f, 0.28f, 0.04f);
                Cyl(b, Dark, fr, 0, 2.25f, 0, 0.3f, 0.3f, 0.34f, 6, true);     // 帽子
                c.Occupy(k.pos.x, k.pos.z, 1.2f * fr.k); c.res.Count("snowman");
            }
            for (int i = 0, n = c.N(3); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.2f, 60f, true, 100, out k)) break;
                Fr fr = FaceDriver(k.pos, k.sm, k.side, 1f, 15f);
                Ball(b, snow, fr, 0, 0.1f, 0, 2.6f, 2.0f, 2.6f, true);   // 冰屋圓頂
                Bx(b, Dark, fr, 0, 0.7f, 2.2f, 0.7f, 0.7f, 0.5f);
                Ball(b, ice, fr, 0, 0.1f, 2.0f, 1.0f, 1.0f, 0.9f);
                c.Occupy(k.pos.x, k.pos.z, 3.2f); c.res.Count("igloo");
            }
            for (int i = 0, n = c.N(12); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.6f, 45f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.8f, 1.4f));
                int sp = 3 + (int)(c.F() * 3f);
                for (int q = 0; q < sp; q++)
                {
                    float a = q * 2.1f + c.F(), d = q == 0 ? 0f : c.F(0.5f, 1.1f);
                    Cyl(b, q % 2 == 0 ? ice : Blue, fr, Mathf.Cos(a) * d, -0.05f, Mathf.Sin(a) * d, 0.38f, 0f, c.F(1.2f, 2.4f), 5, false);
                }
                c.Occupy(k.pos.x, k.pos.z, 1.6f * fr.k); c.res.Count("iceCrystal");
            }
            CommonSigns(c, 3, Blue);
        }

        // ---- 森林:巨大蘑菇、原木堆、花叢、小屋
        static void ForestScene(Ctx c)
        {
            Batch b = c.b; Cand k;
            for (int i = 0, n = c.N(16); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.8f, 45f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.6f));
                Color cap = (i % 3 == 0) ? Red : ((i % 3 == 1) ? Orange : Pink);
                Cyl(b, Cream, fr, 0, 0, 0, 0.5f, 0.38f, 1.5f, 6, false);
                Canopy(b, cap, Color.Lerp(cap, White, 0.35f), fr, 0, 1.4f, 0, 1.7f, 0.9f, 8);
                Ball(b, White, fr, 0.6f, 2.1f, 0.3f, 0.2f, 0.12f, 0.2f);
                Ball(b, White, fr, -0.4f, 2.0f, -0.5f, 0.17f, 0.1f, 0.17f);
                c.Occupy(k.pos.x, k.pos.z, 1.8f * fr.k); c.res.Count("giantMushroom");
            }
            for (int i = 0, n = c.N(8); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.2f, 45f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                for (int q = 0; q < 3; q++)
                {
                    float lx = q == 1 ? 0f : (q == 0 ? -0.42f : 0.42f), ly = q == 1 ? 0.7f : 0f;
                    Tube(b, Wood, fr.P(lx, ly + 0.4f, -1.4f), fr.P(lx, ly + 0.4f, 1.4f), 0.4f * fr.k, 0.4f * fr.k, 6, true);
                }
                c.Occupy(k.pos.x, k.pos.z, 2.0f); c.res.Count("logPile");
            }
            for (int i = 0, n = c.N(18); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.3f, 40f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.3f));
                FlowerPatch(c, fr);
                c.Occupy(k.pos.x, k.pos.z, 1.4f * fr.k); c.res.Flowers++; c.res.Count("flowers");
            }
            CommonSigns(c, 4, Red);
        }

        // ---- 火山:熔岩池、焦岩尖柱、營火
        static void VolcanoScene(Ctx c)
        {
            Batch b = c.b; Cand k;
            Color lava = new Color(1f, 0.45f, 0.12f), lava2 = new Color(1f, 0.7f, 0.2f), ash = Color.Lerp(c.th.Rock, Dark, 0.55f);
            for (int i = 0, n = c.N(5); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.6f, 70f, true, 120, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.3f));
                const int S = 10;
                RaceMeshAcc la = b.A(lava), lb = b.A(lava2);
                for (int q = 0; q < S; q++)
                {
                    float a0 = q * Mathf.PI * 2f / S, a1 = (q + 1) * Mathf.PI * 2f / S;
                    la.Tri(fr.P(0, 0.08f, 0), fr.P(Mathf.Cos(a0) * 3.4f, 0.08f, Mathf.Sin(a0) * 2.6f), fr.P(Mathf.Cos(a1) * 3.4f, 0.08f, Mathf.Sin(a1) * 2.6f), Vector3.up);
                    lb.Tri(fr.P(0, 0.1f, 0), fr.P(Mathf.Cos(a0 + 0.3f) * 1.5f, 0.1f, Mathf.Sin(a0 + 0.3f) * 1.1f), fr.P(Mathf.Cos(a1 + 0.3f) * 1.5f, 0.1f, Mathf.Sin(a1 + 0.3f) * 1.1f), Vector3.up);
                    Ball(b, ash, fr, Mathf.Cos(a0) * 3.7f, 0.25f, Mathf.Sin(a0) * 2.9f, 0.55f, 0.4f, 0.55f);
                }
                c.Occupy(k.pos.x, k.pos.z, 3.6f); c.res.Count("lavaPool");
            }
            for (int i = 0, n = c.N(22); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.4f, 50f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.7f));
                int sp = 2 + (int)(c.F() * 2f);
                for (int q = 0; q < sp; q++)
                {
                    float a = q * 2.3f + c.F(), d = q == 0 ? 0f : c.F(0.5f, 1.0f), h = q == 0 ? c.F(2.2f, 3.4f) : c.F(1.0f, 1.9f);
                    Cyl(b, ash, fr, Mathf.Cos(a) * d, -0.05f, Mathf.Sin(a) * d, 0.55f, 0.1f, h, 5, false);
                    Cyl(b, lava, fr, Mathf.Cos(a) * d, h * 0.72f, Mathf.Sin(a) * d, 0.2f, 0.0f, h * 0.3f, 5, false);   // 尖端發亮
                }
                c.Occupy(k.pos.x, k.pos.z, 1.4f * fr.k); c.res.Count("ashSpire");
            }
            for (int i = 0, n = c.N(4); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.4f, 45f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                for (int q = 0; q < 4; q++) Cyl(b, Wood, fr, Mathf.Cos(q * 1.57f) * 0.5f, 0.1f, Mathf.Sin(q * 1.57f) * 0.5f, 0.1f, 0.06f, 0.9f, 4, false);
                Cyl(b, lava, fr, 0, 0.2f, 0, 0.45f, 0.0f, 1.1f, 6, false);
                Cyl(b, lava2, fr, 0, 0.3f, 0, 0.26f, 0.0f, 0.85f, 6, false);
                c.Occupy(k.pos.x, k.pos.z, 1.4f); c.res.Count("campfire");
            }
            CommonSigns(c, 2, Orange);
        }

        // ================================================================== 島嶼

        static void Island(Ctx c)
        {
            Batch b = c.b; Cand k;
            // 花叢
            for (int i = 0, n = c.N(30); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.3f, 40f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.3f));
                FlowerPatch(c, fr);
                c.Occupy(k.pos.x, k.pos.z, 1.4f * fr.k); c.res.Flowers++; c.res.Count("flowers");
            }
            // 石牆
            for (int i = 0, n = c.N(8); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.9f, 25f, true, 80, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                StoneWall(c, fr);
                for (int q = -1; q <= 1; q++) { Vector3 qp = fr.P(q * 3.6f, 0, 0); c.Occupy(qp.x, qp.z, 1.9f); }
                c.res.Count("stoneWall");
            }
            // 池塘 + 木橋
            for (int i = 0, n = c.N(3); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 5.0f, 70f, true, 120, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                PondBridge(c, fr);
                c.Occupy(k.pos.x, k.pos.z, 5.0f); c.res.Count("pondBridge");
            }
            // 小屋
            for (int i = 0, n = c.N(4); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.8f, 60f, true, 120, out k)) break;
                Fr fr = FaceDriver(k.pos, k.sm, k.side, 1f, 15f);
                Cottage(c, fr, i % 2 == 0 ? Red : Blue);
                c.Occupy(k.pos.x, k.pos.z, 3.8f); c.res.Count("cottage");
            }
            // 旗幟串
            for (int i = 0, n = c.N(5), made = 0; made < n && i < 80 && c.Room(); i++)
            {
                float s = c.F() * (c.L - 60f) + 30f;
                int side = c.F() < 0.5f ? -1 : 1;
                if (Bunting(c, s, side)) made++;
            }
            // 看板
            for (int i = 0, n = c.N(6); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.6f, 18f, true, 80, out k)) break;
                Fr fr = FaceDriver(k.pos, k.sm, k.side, 1f, 25f);
                Billboard(c, fr, i % 3);
                c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("billboard");
            }
        }

        static void FlowerPatch(Ctx c, Fr fr)
        {
            Batch b = c.b;
            Ball(b, c.th.Leaves[1 % c.th.Leaves.Length], fr, 0, 0.12f, 0, 1.1f, 0.38f, 1.1f);
            Color[] fl = { Pink, Gold, White, Blue, Red };
            Color main = fl[(int)(c.F() * fl.Length) % fl.Length];
            int n = 4 + (int)(c.F() * 3f);
            for (int i = 0; i < n; i++)
            {
                float a = c.F() * Mathf.PI * 2f, r = c.F(0.1f, 0.85f);
                Color col = c.F() < 0.6f ? main : fl[(int)(c.F() * fl.Length) % fl.Length];
                Ball(b, col, fr, Mathf.Cos(a) * r, 0.5f + c.F() * 0.1f, Mathf.Sin(a) * r, 0.27f, 0.22f, 0.27f);
            }
        }

        static void StoneWall(Ctx c, Fr fr)
        {
            Batch b = c.b;
            for (int i = 0; i < 5; i++)
            {
                float x = -3.6f + i * 1.8f;
                Ball(b, c.th.Rock, fr, x, 0.55f, c.F(-0.15f, 0.15f), 1.0f, 0.75f, 0.7f);
                if (i < 4) Ball(b, c.th.Rock, fr, x + 0.9f, 1.25f, 0, 0.75f, 0.55f, 0.55f);
            }
            for (int i = 0; i < 4; i++) Ball(b, c.th.Leaves[i % c.th.Leaves.Length], fr, -2.6f + i * 1.8f + c.F(-0.3f, 0.3f), 1.7f, 0, 0.42f, 0.32f, 0.42f);
            Ball(b, Pink, fr, c.F(-2f, 2f), 1.95f, 0.1f, 0.2f, 0.16f, 0.2f);
        }

        static void PondBridge(Ctx c, Fr fr)
        {
            Batch b = c.b;
            // 池塘 = 一片貼地圓盤 + 一圈圓石
            const int S = 10;
            RaceMeshAcc wa = b.A(c.th.Water);
            for (int i = 0; i < S; i++)
            {
                float a0 = i * Mathf.PI * 2f / S, a1 = (i + 1) * Mathf.PI * 2f / S;
                wa.Tri(fr.P(0, 0.07f, 0), fr.P(Mathf.Cos(a0) * 4.6f, 0.07f, Mathf.Sin(a0) * 3.4f), fr.P(Mathf.Cos(a1) * 4.6f, 0.07f, Mathf.Sin(a1) * 3.4f), Vector3.up);
                Ball(b, c.th.Rock, fr, Mathf.Cos(a0) * 4.9f, 0.2f, Mathf.Sin(a0) * 3.7f, 0.5f, 0.35f, 0.5f);
            }
            for (int i = 0; i < 3; i++) Ball(b, c.th.Leaves[0], fr, c.F(-2.5f, 2.5f), 0.1f, c.F(-1.8f, 1.8f), 0.36f, 0.05f, 0.36f);
            Ball(b, Pink, fr, 1.5f, 0.2f, 1.0f, 0.2f, 0.15f, 0.2f);
            // 拱橋
            const int P = 6;
            Vector3 prev = fr.P(-4.2f, 0.3f, 0);
            for (int i = 1; i <= P; i++)
            {
                float t = i / (float)P;
                Vector3 cur = fr.P(-4.2f + 8.4f * t, 0.3f + Mathf.Sin(t * Mathf.PI) * 0.9f, 0);
                Beam(b, Wood, prev, cur, 1.6f * fr.k, 0.18f, Vector3.up);
                Vector3 rl0 = prev + fr.r * 0.78f + Vector3.up * 0.7f, rl1 = cur + fr.r * 0.78f + Vector3.up * 0.7f;
                Beam(b, Cream, rl0, rl1, 0.1f, 0.1f, Vector3.up);
                Beam(b, Cream, rl0 - fr.r * 1.56f, rl1 - fr.r * 1.56f, 0.1f, 0.1f, Vector3.up);
                Tube(b, Cream, prev + fr.r * 0.78f, prev + fr.r * 0.78f + Vector3.up * 0.7f, 0.06f, 0.06f, 5, false);
                Tube(b, Cream, prev - fr.r * 0.78f, prev - fr.r * 0.78f + Vector3.up * 0.7f, 0.06f, 0.06f, 5, false);
                prev = cur;
            }
        }

        static void Cottage(Ctx c, Fr fr, Color roof)
        {
            Batch b = c.b;
            Cyl(b, Cream, fr, 0, 0, 0, 2.2f, 2.2f, 2.4f, 6);
            Cyl(b, roof, fr, 0, 2.4f, 0, 3.0f, 0.15f, 2.3f, 6);
            Ball(b, Gold, fr, 0, 4.75f, 0, 0.22f, 0.22f, 0.22f);
            Bx(b, Wood, fr, 0, 0.9f, 1.93f, 0.45f, 0.9f, 0.07f);
            Ball(b, Gold, fr, 0.3f, 0.9f, 2.02f, 0.07f, 0.07f, 0.07f);
            Fr fw = fr.Yaw(60f);
            Bx(b, Blue, fw, 0, 1.4f, 1.93f, 0.4f, 0.4f, 0.07f);
            Fr fw2 = fr.Yaw(-60f);
            Bx(b, Blue, fw2, 0, 1.4f, 1.93f, 0.4f, 0.4f, 0.07f);
            Bx(b, c.th.Rock, fr, 1.0f, 3.9f, -0.6f, 0.3f, 0.55f, 0.3f);
            Ball(b, c.th.Leaves[1 % c.th.Leaves.Length], fr, -1.6f, 0.4f, 2.3f, 0.7f, 0.55f, 0.7f);
            Ball(b, c.th.Leaves[0], fr, 1.7f, 0.35f, 2.4f, 0.55f, 0.45f, 0.55f);
            Ball(b, Pink, fr, -1.5f, 0.85f, 2.55f, 0.18f, 0.15f, 0.18f);
        }

        static bool Bunting(Ctx c, float s, int side)
        {
            Batch b = c.b; const int P = 4; const float gap = 7f;
            var pts = new Vector3[P];
            for (int i = 0; i < P; i++)
            {
                float ss = c.tr.Wrap(s + i * gap);
                TrackSample sm = c.tr.SampleAt(ss);
                if (c.NearArch(ss, 14f)) return false;
                pts[i] = RoadPt(c, ss, side, sm.Width * 0.5f + 7.0f);
                if (!SideOk(c, pts[i], 0.5f, 0.5f)) return false;
            }
            for (int i = 0; i < P; i++)
            {
                Tube(b, Wood, pts[i] + Vector3.down * 0.2f, pts[i] + Vector3.up * 4.2f, 0.11f, 0.08f, 6, false);
                Ball(b, Gold, Fr.Face(pts[i], Vector3.forward, 1f), 0, 4.3f, 0, 0.17f, 0.17f, 0.17f);
                c.Occupy(pts[i].x, pts[i].z, 0.5f);
            }
            int ci = (int)(c.F() * 5f);
            Color[] cols = { Red, Gold, Blue, Pink, White };
            for (int i = 0; i < P - 1; i++)
            {
                Vector3 a = pts[i] + Vector3.up * 4.1f, e = pts[i + 1] + Vector3.up * 4.1f;
                Vector3 prev = a;
                for (int j = 1; j <= 6; j++)
                {
                    float t = j / 6f;
                    Vector3 cur = Vector3.Lerp(a, e, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.7f);
                    Beam(b, White, prev, cur, 0.05f, 0.05f, Vector3.up);
                    if (j < 6)
                    {
                        Vector3 along = (e - a).normalized;
                        ClothTri(c, cols[(ci++) % cols.Length], cur - along * 0.28f, cur + along * 0.28f, cur + Vector3.down * 0.7f, 0f, 0f, 1f, s * 0.21f + i * 1.7f + j * 0.8f);
                    }
                    prev = cur;
                }
            }
            c.res.Count("bunting");
            return true;
        }

        static void Billboard(Ctx c, Fr fr, int variant)
        {
            Batch b = c.b;
            Cyl(b, Wood, fr, -1.8f, 0, 0, 0.12f, 0.12f, 2.2f, 6, false);
            Cyl(b, Wood, fr, 1.8f, 0, 0, 0.12f, 0.12f, 2.2f, 6, false);
            Bx(b, White, fr, 0, 3.0f, 0, 2.7f, 1.15f, 0.07f);
            Color inner = variant == 0 ? Pink : (variant == 1 ? Blue : Gold);
            Bx(b, inner, fr, 0, 3.0f, 0.04f, 2.5f, 0.95f, 0.07f);
            if (variant == 0) { Ball(b, White, fr, -1.2f, 3.0f, 0.14f, 0.6f, 0.6f, 0.07f); Ball(b, Gold, fr, 1.2f, 3.0f, 0.14f, 0.45f, 0.45f, 0.07f); }
            else if (variant == 1) { Bx(b, Gold, fr, 0, 3.0f, 0.13f, 0.9f, 0.18f, 0.03f); Bx(b, Gold, fr, 0, 3.0f, 0.13f, 0.18f, 0.7f, 0.03f); Ball(b, White, fr, -1.8f, 3.0f, 0.14f, 0.3f, 0.3f, 0.05f); Ball(b, White, fr, 1.8f, 3.0f, 0.14f, 0.3f, 0.3f, 0.05f); }
            else { Bx(b, Red, fr, 0, 3.3f, 0.13f, 2.2f, 0.18f, 0.03f); Bx(b, Red, fr, 0, 2.7f, 0.13f, 2.2f, 0.18f, 0.03f); Ball(b, White, fr, 0, 3.0f, 0.14f, 0.45f, 0.45f, 0.05f); }
            Ball(b, Gold, fr, -2.75f, 4.2f, 0, 0.18f, 0.18f, 0.18f);
            Ball(b, Gold, fr, 2.75f, 4.2f, 0, 0.18f, 0.18f, 0.18f);
        }

        // ================================================================== 峽谷

        static void Canyon(Ctx c)
        {
            Batch b = c.b; Cand k;
            // 仙人掌 3 種
            for (int i = 0, n = c.N(42); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.2f, 32f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.85f, 1.35f));
                float t = c.F();
                if (t < 0.4f) Saguaro(c, fr);
                else if (t < 0.75f) Barrel(c, fr);
                else Pear(c, fr);
                c.Occupy(k.pos.x, k.pos.z, 1.2f * fr.k); c.res.Cacti++; c.res.Count("cactus");
            }
            // 枯樹
            for (int i = 0, n = c.N(10); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.4f, 34f, true, 60, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.9f, 1.4f));
                DeadTree(c, fr);
                c.Occupy(k.pos.x, k.pos.z, 1.4f * fr.k); c.res.Count("deadTree");
            }
            // 石柱群
            for (int i = 0, n = c.N(8); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.5f, 40f, true, 80, out k)) break;
                Fr fr = Fr.Face(k.pos, k.toRoad, 1f);
                Pillars(c, fr);
                c.Occupy(k.pos.x, k.pos.z, 3.5f); c.res.Count("pillars");
            }
            // 鷹架
            for (int i = 0, n = c.N(3); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.6f, 40f, true, 80, out k)) break;
                Scaffold(c, Fr.Face(k.pos, k.toRoad, 1f));
                c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("scaffold");
            }
            // 木棧橋
            for (int i = 0, n = c.N(2); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.2f, 45f, true, 100, out k)) break;
                Fr tf = Fr.Face(k.pos, k.toRoad, 1f);
                Trestle(c, tf);
                for (int q = -1; q <= 1; q++) { Vector3 qp = tf.P(q * 3.0f, 0, 0); c.Occupy(qp.x, qp.z, 2.2f); } c.res.Count("trestle");
            }
            // 礦車軌道
            for (int i = 0, n = c.N(3); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.0f, 40f, true, 100, out k)) break;
                if (!MineTrack(c, k)) continue;
                c.res.Count("mineTrack");
            }
            // 帳篷
            for (int i = 0, n = c.N(6); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.2f, 40f, true, 80, out k)) break;
                Tent(c, FaceDriver(k.pos, k.sm, k.side, 1f, 20f), i % 2 == 0 ? Red : Blue);
                c.Occupy(k.pos.x, k.pos.z, 3.2f); c.res.Count("tent");
            }
            // 看板
            for (int i = 0, n = c.N(4); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.6f, 18f, true, 80, out k)) break;
                Billboard(c, FaceDriver(k.pos, k.sm, k.side, 1f, 25f), 1 + (i & 1));
                c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("billboard");
            }
        }

        static Color Cact(Ctx c, int i) { return c.th.Leaves[i % c.th.Leaves.Length]; }

        static void Saguaro(Ctx c, Fr fr)
        {
            Batch b = c.b; Color g = Cact(c, 0);
            Tube(b, g, fr.P(0, 0, 0), fr.P(0, 3.5f, 0), 0.46f * fr.k, 0.4f * fr.k, 6, false);
            Ball(b, g, fr, 0, 3.5f, 0, 0.4f, 0.45f, 0.4f);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float y = 1.5f + (sd > 0 ? 0.5f : 0f) + c.F(-0.2f, 0.2f);
                Vector3 a = fr.P(sd * 0.3f, y, 0), e = fr.P(sd * 1.0f, y, 0);
                Tube(b, g, a, e, 0.22f * fr.k, 0.22f * fr.k, 6, false);
                Vector3 top = fr.P(sd * 1.0f, y + 0.95f + c.F() * 0.3f, 0);
                Tube(b, g, e, top, 0.21f * fr.k, 0.19f * fr.k, 6, false);
                Ball(b, g, fr, sd * 1.0f, (top.y - fr.o.y) / fr.k, 0, 0.2f, 0.24f, 0.2f);
                Ball(b, g, fr, sd * 1.0f, y, 0, 0.22f, 0.22f, 0.22f);
            }
            Ball(b, Pink, fr, 0.1f, 3.95f, 0.1f, 0.2f, 0.14f, 0.2f);
        }

        static void Barrel(Ctx c, Fr fr)
        {
            Batch b = c.b;
            Ball(b, Cact(c, 1), fr, 0, 0.6f, 0, 0.85f, 0.7f, 0.85f);
            Ball(b, Cact(c, 0), fr, 0, 0.35f, 0, 0.95f, 0.3f, 0.95f);
            Ball(b, Pink, fr, 0, 1.3f, 0, 0.28f, 0.2f, 0.28f);
            Ball(b, Gold, fr, 0, 1.45f, 0, 0.1f, 0.08f, 0.1f);
        }

        static void Pear(Ctx c, Fr fr)
        {
            Batch b = c.b;
            Ball(b, Cact(c, 1), fr, 0, 0.7f, 0, 0.75f, 0.8f, 0.22f);
            Ball(b, Cact(c, 0), fr, -0.55f, 1.55f, 0.05f, 0.55f, 0.65f, 0.2f);
            Ball(b, Cact(c, 0), fr, 0.5f, 1.65f, -0.05f, 0.5f, 0.6f, 0.2f);
            Ball(b, Pink, fr, -0.5f, 2.2f, 0.1f, 0.17f, 0.17f, 0.17f);
            Ball(b, Pink, fr, 0.5f, 2.2f, 0.1f, 0.17f, 0.17f, 0.17f);
            Ball(b, Gold, fr, 0.0f, 1.45f, 0.2f, 0.15f, 0.15f, 0.15f);
        }

        static void DeadTree(Ctx c, Fr fr)
        {
            Batch b = c.b;
            Color w = Color.Lerp(Wood, c.th.Rock, 0.2f);
            Tube(b, Wood, fr.P(0, 0, 0), fr.P(0.15f, 2.6f, 0), 0.34f * fr.k, 0.18f * fr.k, 6, false);
            for (int i = 0; i < 3; i++)
            {
                float a = (i - 1) * 0.9f + c.F(-0.2f, 0.2f), y = 1.6f + i * 0.5f;
                Vector3 s0 = fr.P(0.1f, y, 0), s1 = fr.P(0.1f + Mathf.Sin(a) * 1.2f, y + 0.9f + c.F() * 0.4f, Mathf.Cos(a) * 0.5f);
                Tube(b, Wood, s0, s1, 0.12f * fr.k, 0.07f * fr.k, 5, false);
                Ball(b, Wood, Fr.Face(s1, Vector3.forward, 1f), 0, 0, 0, 0.09f * fr.k, 0.09f * fr.k, 0.09f * fr.k);
            }
            Ball(b, Wood, fr, 0.15f, 2.65f, 0, 0.2f, 0.2f, 0.2f);
            Ball(b, Wood, fr, 0, 0.1f, 0, 0.6f, 0.2f, 0.6f);
        }

        static void Pillars(Ctx c, Fr fr)
        {
            Batch b = c.b;
            int n = 3 + (int)(c.F() * 2f);
            for (int i = 0; i < n; i++)
            {
                float x = -2.4f + i * 1.7f + c.F(-0.4f, 0.4f), z = c.F(-1.6f, 1.6f), h = c.F(2.6f, 7.5f), r = c.F(0.9f, 1.4f);
                Cyl(b, c.th.Rock, fr, x, -0.4f, z, r * 1.15f, r, h, 6);
                Cyl(b, Orange, fr, x, h * 0.45f, z, r * 1.0f, r * 1.0f, 0.5f, 6, false);
                Ball(b, c.th.Rock, fr, x, h - 0.4f, z, r * 1.05f, r * 0.8f, r * 1.05f);
            }
            Ball(b, c.th.Rock, fr, 1.5f, 0.3f, 1.8f, 1.1f, 0.6f, 0.9f);
        }

        static void Scaffold(Ctx c, Fr fr)
        {
            Batch b = c.b;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cyl(b, Wood, fr, sx * 1.5f, -0.3f, sz * 1.5f, 0.14f, 0.12f, 5.2f, 6, false);
            for (int lv = 0; lv < 2; lv++)
            {
                float y = 1.8f + lv * 2.4f;
                Bx(b, Wood, fr, 0, y, 0, 1.7f, 0.1f, 1.7f);
                Bx(b, Cream, fr, 0, y + 0.1f, 0, 1.3f, 0.04f, 1.3f);
                for (int sx = -1; sx <= 1; sx += 2) { Bx(b, Wood, fr, sx * 1.5f, y, 0, 0.07f, 0.07f, 1.6f); Bx(b, Wood, fr, 0, y, sx * 1.5f, 1.6f, 0.07f, 0.07f); }
            }
            Beam(b, Wood, fr.P(-1.5f, 0.3f, 1.5f), fr.P(1.5f, 4.2f, 1.5f), 0.1f * fr.k, 0.1f * fr.k, Vector3.up);
            Beam(b, Wood, fr.P(1.5f, 0.3f, 1.5f), fr.P(-1.5f, 4.2f, 1.5f), 0.1f * fr.k, 0.1f * fr.k, Vector3.up);
            Ball(b, Red, fr, 0, 4.7f, 0, 0.4f, 0.4f, 0.4f);
            Bx(b, Gold, fr, 0, 6.0f, 0, 0.9f, 0.5f, 0.06f);
            Bx(b, Wood, fr, 0, 5.2f, 0, 0.06f, 0.5f, 0.06f);
        }

        static void Trestle(Ctx c, Fr fr)
        {
            Batch b = c.b;
            const int P = 6;
            Vector3 prev = fr.P(-4.5f, 0.5f, 0);
            for (int i = 1; i <= P; i++)
            {
                float t = i / (float)P;
                Vector3 cur = fr.P(-4.5f + 9f * t, 0.5f + Mathf.Sin(t * Mathf.PI) * 0.7f, 0);
                Beam(b, Wood, prev, cur, 1.8f * fr.k, 0.2f, Vector3.up);
                Beam(b, Cream, prev + fr.r * 0.8f + Vector3.up * 0.75f, cur + fr.r * 0.8f + Vector3.up * 0.75f, 0.1f, 0.1f, Vector3.up);
                Beam(b, Cream, prev - fr.r * 0.8f + Vector3.up * 0.75f, cur - fr.r * 0.8f + Vector3.up * 0.75f, 0.1f, 0.1f, Vector3.up);
                prev = cur;
            }
            for (int i = 0; i < 4; i++)
            {
                float x = -3.4f + i * 2.3f;
                Cyl(b, Wood, fr, x, -1.4f, 0.8f, 0.14f, 0.14f, 1.9f, 6, false);
                Cyl(b, Wood, fr, x, -1.4f, -0.8f, 0.14f, 0.14f, 1.9f, 6, false);
                Beam(b, Wood, fr.P(x, 0.2f, 0.8f), fr.P(x, 0.2f, -0.8f), 0.14f * fr.k, 0.14f * fr.k, Vector3.up);
            }
            Ball(b, Red, fr, -4.6f, 1.3f, 0.8f, 0.2f, 0.2f, 0.2f);
            Ball(b, Red, fr, 4.6f, 1.3f, 0.8f, 0.2f, 0.2f, 0.2f);
        }

        static bool MineTrack(Ctx c, Cand k)
        {
            Batch b = c.b;
            Fr fr = Fr.Face(k.pos, k.toRoad, 1f);   // x 沿賽道方向
            const int segs = 8; const float step = 2.2f;
            var pts = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                Vector3 p = fr.P((i - segs * 0.5f) * step, 0, 0);
                if (!SideOk(c, p, 1.4f, 0.8f)) return false;
                p.y = c.G(p.x, p.z) + 0.12f;
                pts[i] = p;
            }
            for (int i = 0; i <= segs; i++)
            {
                Beam(b, Wood, pts[i] + fr.f * 0.75f, pts[i] - fr.f * 0.75f, 0.2f, 0.1f, Vector3.up);
                if (i < segs)
                    for (int rl = -1; rl <= 1; rl += 2)
                        Beam(b, Dark, pts[i] + fr.f * (0.45f * rl) + Vector3.up * 0.1f, pts[i + 1] + fr.f * (0.45f * rl) + Vector3.up * 0.1f, 0.09f, 0.09f, Vector3.up);
            }
            // 礦車
            Vector3 cp = pts[segs / 2 + 1];
            Fr cf = new Fr { o = cp, r = fr.r, u = Vector3.up, f = fr.f, k = 1f };
            Bx(b, Wood, cf, 0, 0.75f, 0, 0.8f, 0.4f, 0.6f);
            Bx(b, Orange, cf, 0, 1.05f, 0, 0.86f, 0.1f, 0.66f);
            for (int wx = -1; wx <= 1; wx += 2) for (int wz = -1; wz <= 1; wz += 2) Ball(b, Dark, cf, wx * 0.6f, 0.3f, wz * 0.45f, 0.22f, 0.22f, 0.12f);
            for (int i = 0; i < 4; i++) Ball(b, i % 2 == 0 ? Gold : c.th.Rock, cf, c.F(-0.5f, 0.5f), 1.2f + c.F() * 0.1f, c.F(-0.3f, 0.3f), 0.3f, 0.26f, 0.3f);
            for (int i = 0; i <= segs; i += 2) c.Occupy(pts[i].x, pts[i].z, 1.4f);
            return true;
        }

        static void Tent(Ctx c, Fr fr, Color accent)
        {
            Batch b = c.b;
            Canopy(b, accent, White, fr, 0, 0, 0, 2.6f, 2.8f, 8);
            Cyl(b, Cream, fr, 0, 0, 0, 2.55f, 2.55f, 0.01f, 8, false);
            Ball(b, Gold, fr, 0, 2.95f, 0, 0.2f, 0.2f, 0.2f);
            Tube(b, Wood, fr.P(0, 2.7f, 0), fr.P(0, 3.4f, 0), 0.05f * fr.k, 0.05f * fr.k, 5, false);
            Dbl(b, accent, fr.P(0, 3.4f, 0), fr.P(0, 3.0f, 0), fr.P(0.7f, 3.2f, 0.0f));
            Bx(b, Dark, fr, 0, 0.75f, 2.35f, 0.55f, 0.75f, 0.03f);
            Ball(b, Pink, fr, 2.9f, 0.3f, 0.5f, 0.35f, 0.3f, 0.35f);
            Bx(b, Wood, fr, -2.9f, 0.3f, 0.8f, 0.35f, 0.3f, 0.35f);
        }

        // ================================================================== 海岸

        static void Coast(Ctx c, Transform root, List<Object> owned)
        {
            Batch b = c.b; Cand k;
            // 棕櫚(3 種變化)
            for (int i = 0, n = c.N(22); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 1.4f, 45f, true, 70, out k)) { if (!c.Pick(1, 1.4f, 45f, true, 70, out k)) break; }
                Fr fr = Fr.Face(k.pos, k.toRoad, c.F(0.85f, 1.3f)).Yaw(c.F(-180f, 180f));
                Palm(c, fr, i % 3);
                c.Occupy(k.pos.x, k.pos.z, 1.4f * fr.k); c.res.Palms++; c.res.Count("palm");
            }
            // 海灘傘
            for (int i = 0, n = c.N(12); i < n && c.Room(); i++)
            {
                if (!c.Pick(1, 1.8f, 70f, false, 100, out k)) break;
                Umbrella(c, Fr.Face(k.pos, k.toRoad, 1f).Yaw(c.F(-40f, 40f)), i);
                c.Occupy(k.pos.x, k.pos.z, 1.9f); c.res.Count("umbrella");
            }
            // 沙灘小屋
            for (int i = 0, n = c.N(3); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 3.8f, 60f, true, 100, out k)) break;
                Cottage(c, FaceDriver(k.pos, k.sm, k.side, 1f, 15f), i % 2 == 0 ? Blue : Pink);
                c.Occupy(k.pos.x, k.pos.z, 3.8f); c.res.Count("beachHut");
            }
            // 碼頭
            for (int i = 0, n = c.N(2); i < n && c.Room(); i++)
            {
                if (!c.Pick(1, 2.0f, 90f, false, 200, out k)) break;
                Pier(c, k);
            }
            // 小船
            for (int i = 0, n = c.N(8); i < n && c.Room(); i++)
            {
                if (!c.Pick(2, 2.6f, 80f, false, 120, out k)) break;
                Boat(c, Fr.Face(k.pos, Quaternion.Euler(0, c.F(0, 360f), 0) * Vector3.forward, 1f), i);
                c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("boat");
            }
            // 浮標鏈
            for (int i = 0, n = c.N(3); i < n && c.Room(); i++)
            {
                if (!c.Pick(2, 1.0f, 60f, false, 120, out k)) break;
                Vector3 dir = k.sm.Tangent;
                for (int j = 0; j < 4; j++)
                {
                    Vector3 p = k.pos + dir * (j * 14f);
                    if (c.t.Raw(p.x, p.z) > -0.8f || !c.Free(p.x, p.z, 1.0f)) continue;
                    float d, s, lat;
                    if (!c.Road(p.x, p.z, 1.0f, 8f, out d, out s, out lat)) continue;
                    p.y = 0f;
                    Buoy(c, p, j);
                    c.Occupy(p.x, p.z, 1.0f); c.res.Count("buoy");
                }
            }
            // 岩礁
            for (int i = 0, n = c.N(8); i < n && c.Room(); i++)
            {
                if (!c.Pick(2, 3.2f, 90f, false, 120, out k)) break;
                Reef(c, Fr.Face(k.pos, k.toRoad, 1f).Yaw(c.F(-180f, 180f)));
                c.Occupy(k.pos.x, k.pos.z, 3.2f); c.res.Count("reef");
            }
            // 看板
            for (int i = 0, n = c.N(4); i < n && c.Room(); i++)
            {
                if (!c.Pick(0, 2.6f, 18f, true, 80, out k)) break;
                Billboard(c, FaceDriver(k.pos, k.sm, k.side, 1f, 25f), i % 3);
                c.Occupy(k.pos.x, k.pos.z, 2.6f); c.res.Count("billboard");
            }
            // 海鷗(整群繞圈,高空,不佔地面)
            for (int i = 0, n = c.N(2); i < n; i++) Gulls(c, root, owned, i);
        }

        static void Palm(Ctx c, Fr fr, int variant)
        {
            Batch b = c.b;
            float h = variant == 0 ? 4.6f : (variant == 1 ? 6.2f : 3.6f);
            float lean = variant == 1 ? 1.4f : (variant == 2 ? 0.9f : 0.5f);
            Vector3 p0 = fr.P(0, 0, 0), p1 = fr.P(lean * 0.35f, h * 0.5f, 0), p2 = fr.P(lean, h, 0);
            Tube(b, Wood, p0, p1, 0.3f * fr.k, 0.24f * fr.k, 6, false);
            Tube(b, Wood, p1, p2, 0.24f * fr.k, 0.18f * fr.k, 6, false);
            Fr crown = fr.At(lean, h, 0);
            int fronds = variant == 2 ? 5 : 7;
            for (int i = 0; i < fronds; i++)
            {
                Fr ff = crown.Yaw(i * 360f / fronds + variant * 17f).Pitch(c.F(8f, 28f));
                Color lc = c.th.Leaves[(i + variant) % c.th.Leaves.Length];
                Ball(b, lc, ff, 0, 0, 1.0f, 0.38f, 0.07f, 1.2f);
                Ball(b, lc, ff.At(0, 0, 1.9f).Pitch(-35f), 0, 0, 0.7f, 0.3f, 0.06f, 0.85f);
            }
            Ball(b, Wood, crown, 0.2f, -0.15f, 0.1f, 0.2f, 0.2f, 0.2f);
            Ball(b, Wood, crown, -0.15f, -0.2f, -0.12f, 0.2f, 0.2f, 0.2f);
            if (variant == 1) Ball(b, Gold, crown, 0, -0.3f, 0.25f, 0.18f, 0.18f, 0.18f);
        }

        static void Umbrella(Ctx c, Fr fr, int idx)
        {
            Batch b = c.b;
            Color[,] sets = { { Red, White }, { Blue, White }, { Pink, White }, { Gold, White } };
            int s = idx % 4;
            Tube(b, White, fr.P(0, 0, 0), fr.P(0.1f, 2.5f, 0), 0.05f * fr.k, 0.05f * fr.k, 5, false);
            Canopy(b, sets[s, 0], sets[s, 1], fr.At(0.1f, 0, 0).Pitch(0f), 0, 2.2f, 0, 1.6f, 0.65f, 8);
            Ball(b, Gold, fr, 0.1f, 2.9f, 0, 0.1f, 0.1f, 0.1f);
            Bx(b, sets[(s + 1) % 4, 0], fr, 0.2f, 0.06f, 1.4f, 0.55f, 0.04f, 0.95f);
            Ball(b, White, fr, 1.0f, 0.2f, -0.8f, 0.2f, 0.2f, 0.2f);
        }

        static void Pier(Ctx c, Cand k)
        {
            Batch b = c.b;
            // 從這個海灘點往海的方向找:8 個方向裡找 raw 下降最多的
            Vector3 best = Vector3.zero; float bestH = 999f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float h = c.t.Raw(k.pos.x + d.x * 22f, k.pos.z + d.z * 22f);
                if (h < bestH) { bestH = h; best = d; }
            }
            if (bestH > -1.2f) return;
            Fr fr = Fr.Face(k.pos, best, 1f);   // z = 往海
            const int planks = 16; const float L = 18f;
            float y = Mathf.Max(1.0f, c.t.Raw(k.pos.x, k.pos.z) + 0.4f);
            fr.o.y = 0f;
            for (int i = 0; i < planks; i++)
            {
                float z = -2f + i * (L / planks);
                Vector3 pw = fr.P(0, 0, z);
                if (c.t.Raw(pw.x, pw.z) > 3f) return;
            }
            for (int i = 0; i <= 5; i++)
            {
                Vector3 q = fr.P(0, 0, -2f + i * 4f);
                float d, s, lat;
                if (!c.Road(q.x, q.z, 1.6f, 6.5f, out d, out s, out lat) || !c.Free(q.x, q.z, 1.6f)) return;
            }
            for (int i = 0; i < planks; i++)
            {
                float z = -2f + i * (L / planks) + 0.5f * (L / planks);
                Bx(b, (i & 1) == 0 ? Wood : Cream, fr, 0, y, z, 1.4f, 0.1f, 0.5f * (L / planks));
            }
            for (int i = 0; i <= 4; i++)
            {
                float z = -2f + i * (L / 4f);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Cyl(b, Wood, fr, sx * 1.3f, -2.2f, z, 0.17f, 0.17f, y + 2.2f + 0.7f, 6, false);
                    Ball(b, Cream, fr, sx * 1.3f, y + 0.8f, z, 0.17f, 0.12f, 0.17f);
                }
            }
            Tube(b, Wood, fr.P(0, y + 0.1f, L - 2f), fr.P(0, y + 2.1f, L - 2f), 0.07f * fr.k, 0.07f * fr.k, 5, false);
            Ball(b, Gold, fr, 0, y + 2.25f, L - 2f, 0.28f, 0.28f, 0.28f);
            for (int i = 0; i <= 5; i++) { Vector3 q = fr.P(0, 0, -2f + i * 4f); c.Occupy(q.x, q.z, 1.6f); }
            Fr bf = Fr.Face(fr.P(2.8f, 0, L - 5f), fr.r, 1f);
            float bd, bs, bl;
            if (c.t.Raw(bf.o.x, bf.o.z) < -0.6f && c.Road(bf.o.x, bf.o.z, 2.2f, 6.5f, out bd, out bs, out bl) && c.Free(bf.o.x, bf.o.z, 2.2f))
            {
                Boat(c, bf, 1);
                c.Occupy(bf.o.x, bf.o.z, 2.2f);
            }
            c.res.Count("pier");
        }

        static void Boat(Ctx c, Fr fr, int idx)
        {
            Batch b = c.b;
            Color hull = (idx & 1) == 0 ? Red : Blue;
            fr.o.y = 0.05f;
            Ball(b, hull, fr, 0, 0.1f, 0, 0.8f, 0.5f, 1.8f, true);
            Ball(b, White, fr, 0, 0.45f, -0.1f, 0.7f, 0.2f, 1.6f);
            Ball(b, Cream, fr, 0, 0.95f, -0.5f, 0.5f, 0.45f, 0.55f);
            Ball(b, Blue, fr, 0, 1.0f, -0.05f, 0.42f, 0.2f, 0.08f);
            Tube(b, Wood, fr.P(0, 0.6f, 0.7f), fr.P(0, 2.6f, 0.7f), 0.05f * fr.k, 0.04f * fr.k, 5, false);
            Dbl(b, (idx & 1) == 0 ? Gold : Pink, fr.P(0, 2.55f, 0.7f), fr.P(0, 1.2f, 0.7f), fr.P(0, 1.2f, 2.0f));
            Ball(b, Gold, fr, 0, 2.65f, 0.7f, 0.1f, 0.1f, 0.1f);
        }

        static void Buoy(Ctx c, Vector3 p, int idx)
        {
            Batch b = c.b;
            Fr fr = Fr.Face(p + Vector3.up * 0.1f, Vector3.forward, 1f);
            Ball(b, Red, fr, 0, 0.25f, 0, 0.55f, 0.45f, 0.55f);
            Ball(b, White, fr, 0, 0.7f, 0, 0.45f, 0.2f, 0.45f);
            Tube(b, White, fr.P(0, 0.8f, 0), fr.P(0, 1.5f, 0), 0.07f, 0.04f, 5, false);
            Ball(b, (idx & 1) == 0 ? Gold : Pink, fr, 0, 1.6f, 0, 0.17f, 0.17f, 0.17f);
        }

        static void Reef(Ctx c, Fr fr)
        {
            Batch b = c.b;
            fr.o.y = 0f;
            int n = 3 + (int)(c.F() * 3f);
            for (int i = 0; i < n; i++)
            {
                float x = c.F(-1.8f, 1.8f), z = c.F(-1.5f, 1.5f), r = c.F(0.7f, 1.4f);
                Ball(b, c.th.Rock, fr, x, r * 0.2f, z, r, r * 0.9f, r);
            }
            RaceMeshAcc wa = b.A(White);
            const int S = 10;
            for (int i = 0; i < S; i++)
            {
                float a0 = i * Mathf.PI * 2f / S, a1 = (i + 1) * Mathf.PI * 2f / S;
                wa.Quad(fr.P(Mathf.Cos(a0) * 1.9f, 0.06f, Mathf.Sin(a0) * 1.9f), fr.P(Mathf.Cos(a0) * 3.0f, 0.06f, Mathf.Sin(a0) * 3.0f),
                        fr.P(Mathf.Cos(a1) * 3.0f, 0.06f, Mathf.Sin(a1) * 3.0f), fr.P(Mathf.Cos(a1) * 1.9f, 0.06f, Mathf.Sin(a1) * 1.9f), Vector3.up);
            }
        }

        static void Gulls(Ctx c, Transform root, List<Object> owned, int idx)
        {
            RaceTrack tr = c.tr;
            TrackSample sm = tr.SampleAt(c.L * (0.2f + 0.45f * idx));
            Vector3 center = sm.Pos + sm.Right * (idx == 0 ? 55f : -50f);
            center.y = 26f + idx * 6f;
            var acc = new RaceMeshAcc();
            int n = 8;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + c.F(-0.2f, 0.2f), rad = c.F(10f, 17f), y = c.F(-2f, 2f);
                Vector3 pos = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                Vector3 fwd = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 rt = Vector3.Cross(Vector3.up, fwd);
                float sc = c.F(0.9f, 1.3f);
                Vector3 nose = pos + fwd * 0.9f * sc, tail = pos - fwd * 0.5f * sc;
                Vector3 wl = pos - rt * 1.5f * sc + Vector3.up * 0.35f * sc - fwd * 0.2f * sc, wr = pos + rt * 1.5f * sc + Vector3.up * 0.35f * sc - fwd * 0.2f * sc;
                Vector3 n1 = Vector3.Cross(wl - nose, tail - nose).normalized;
                acc.Tri(nose, wl, tail, n1); acc.Tri(nose, wl, tail, -n1);
                Vector3 n2 = Vector3.Cross(wr - nose, tail - nose).normalized;
                acc.Tri(nose, wr, tail, n2); acc.Tri(nose, wr, tail, -n2);
                c.res.Birds++;
            }
            Mesh m = acc.ToMesh("Gulls_" + idx);
            owned.Add(m);
            var go = new GameObject("Gulls_" + idx);
            go.transform.SetParent(root, false);
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceArt.Mat(White, 0.1f);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            var fl = go.AddComponent<GullFlock>();
            fl.baseY = center.y; fl.speed = (idx == 0 ? 9f : -7f); fl.phase = idx * 1.7f;
            c.extraTris += acc.TriCount; c.extraDraws++;
            c.res.Count("gulls");
        }

        /// 海鷗群:整群繞自己的中心慢慢轉,上下輕輕晃。每幀只改一個 Transform。
        public sealed class GullFlock : MonoBehaviour
        {
            public float baseY, speed = 9f, phase;
            void Update()
            {
                float t = Time.time;
                var p = transform.position;
                p.y = baseY + Mathf.Sin(t * 0.6f + phase) * 1.2f;
                transform.position = p;
                transform.rotation = Quaternion.Euler(0f, t * speed, 0f);
            }
        }
    }
}
