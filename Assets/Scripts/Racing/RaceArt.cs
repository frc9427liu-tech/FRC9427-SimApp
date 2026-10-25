using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽車美術工廠(模組 P)。材質一律經 Mat/Glow 快取;車體用基本幾何組合;不用外部素材。
    // 契約:docs/racing/00-contracts.md §3.11。
    public static class RaceArt
    {
        // ------------------------------------------------------------ 材質快取
        static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();
        static readonly Dictionary<long, Material> glows = new Dictionary<long, Material>();

        static long Key(Color c, float k)
        {
            Color32 a = c;
            return ((long)a.r << 40) ^ ((long)a.g << 32) ^ ((long)a.b << 24) ^ ((long)a.a << 16) ^ (long)Mathf.RoundToInt(Mathf.Clamp(k, 0f, 100f) * 100f);
        }

        /// 霧面標準材質(快取)。smooth 0..1;不要 Destroy 回傳的材質。
        public static Material Mat(Color c, float smooth = 0.1f)
        {
            long k = Key(c, smooth);
            if (mats.TryGetValue(k, out var m) && m != null) return m;
            m = RaceShaders.Toon(c, smooth, 0f);   // 卡通光照;shader 缺失時內部回退 Standard
            mats[k] = m;
            return m;
        }

        /// 發光材質(加速板、終點門、霓虹),快取。
        public static Material Glow(Color c, float intensity)
        {
            long k = Key(c, intensity);
            if (glows.TryGetValue(k, out var m) && m != null) return m;
            m = Look.GlowMaterial(c, intensity);
            m.name = "RaceGlow";
            glows[k] = m;
            return m;
        }

        /// 清掉全部快取材質(App 結束或要整批換品質時才需要;一般比賽之間保留以免重建)。
        public static void ClearCache()
        {
            foreach (var m in mats.Values) if (m != null) Object.Destroy(m);
            foreach (var m in glows.Values) if (m != null) Object.Destroy(m);
            mats.Clear(); glows.Clear();
        }

        // ------------------------------------------------------------ 網格工具
        /// 把共用頂點拆開再重算法線 = 平面著色(稜角分明)。回傳新網格(不改原本的)。
        public static Mesh Flat(Mesh src)
        {
            var v = src.vertices; var uv = src.uv; var t = src.triangles;
            bool hasUv = uv != null && uv.Length == v.Length;
            var nv = new Vector3[t.Length]; var nuv = hasUv ? new Vector2[t.Length] : null; var nt = new int[t.Length];
            for (int i = 0; i < t.Length; i++) { nv[i] = v[t[i]]; if (hasUv) nuv[i] = uv[t[i]]; nt[i] = i; }
            var m = new Mesh { name = src.name + "_flat" };
            if (nv.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = nv; if (hasUv) m.uv = nuv; m.triangles = nt;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// 底面在 y=0、高 1、底半徑 0.5 的 n 邊圓錐(平面著色)。
        public static Mesh Cone(int sides)
        {
            sides = Mathf.Max(3, sides);
            var v = new List<Vector3>(); var t = new List<int>();
            v.Add(new Vector3(0f, 1f, 0f)); v.Add(Vector3.zero);
            for (int i = 0; i < sides; i++) { float a = i * Mathf.PI * 2f / sides; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f)); }
            for (int i = 0; i < sides; i++)
            {
                int a = 2 + i, b = 2 + (i + 1) % sides;
                t.Add(0); t.Add(b); t.Add(a);   // 側面
                t.Add(1); t.Add(a); t.Add(b);   // 底面
            }
            var m = new Mesh { name = "RaceCone" }; m.SetVertices(v); m.SetTriangles(t, 0);
            var f = Flat(m); Object.Destroy(m); return f;
        }

        /// 半徑 0.5 的 icosphere(sub 0..3),平面著色。
        public static Mesh IcoSphere(int sub)
        {
            float p = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> {
                new Vector3(-1,p,0), new Vector3(1,p,0), new Vector3(-1,-p,0), new Vector3(1,-p,0),
                new Vector3(0,-1,p), new Vector3(0,1,p), new Vector3(0,-1,-p), new Vector3(0,1,-p),
                new Vector3(p,0,-1), new Vector3(p,0,1), new Vector3(-p,0,-1), new Vector3(-p,0,1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var t = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < Mathf.Clamp(sub, 0, 3); s++)
            {
                var cache = new Dictionary<long, int>(); var nt = new List<int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int r)) return r;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized); return cache[key] = v.Count - 1;
                }
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                t = nt;
            }
            for (int i = 0; i < v.Count; i++) v[i] *= 0.5f;
            var m = new Mesh { name = "RaceIco" }; m.SetVertices(v); m.SetTriangles(t, 0);
            var f = Flat(m); Object.Destroy(m); return f;
        }

        // ------------------------------------------------------------ 樹 / 雲(W 在 M2 使用;無碰撞體)
        static GameObject Prim(PrimitiveType pt, Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(pt); g.name = name;
            var col = g.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }
        static GameObject MeshObj(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Material m)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }
        static Mesh coneMesh6, icoMesh;

        public static GameObject Tree(TreeKind kind, System.Random r)
        {
            var root = new GameObject("Tree_" + kind).transform;
            float s = 0.85f + (float)r.NextDouble() * 0.5f;
            var trunk = Mat(RacePalette.Trunk, 0.1f);
            var leaf = Mat(RacePalette.Leaves[r.Next(RacePalette.Leaves.Length)], 0.1f);
            if (coneMesh6 == null) coneMesh6 = Cone(6);
            if (icoMesh == null) icoMesh = IcoSphere(1);
            switch (kind)
            {
                case TreeKind.Pine:
                    Prim(PrimitiveType.Cylinder, root, "Trunk", new Vector3(0, 1f, 0), new Vector3(0.45f, 1f, 0.45f), trunk);
                    for (int i = 0; i < 3; i++) MeshObj(root, "Leaf" + i, coneMesh6, new Vector3(0, 1.6f + i * 1.3f, 0), new Vector3(3.4f - i * 0.9f, 2.2f, 3.4f - i * 0.9f), leaf);
                    break;
                case TreeKind.Round:
                    Prim(PrimitiveType.Cylinder, root, "Trunk", new Vector3(0, 1.1f, 0), new Vector3(0.5f, 1.1f, 0.5f), trunk);
                    MeshObj(root, "Leaf", icoMesh, new Vector3(0, 3.4f, 0), new Vector3(4.2f, 3.8f, 4.2f), leaf);
                    break;
                default:
                    var t = Prim(PrimitiveType.Cylinder, root, "Trunk", new Vector3(0.4f, 2f, 0), new Vector3(0.35f, 2.2f, 0.35f), trunk);
                    t.transform.localRotation = Quaternion.Euler(0, 0, -10f);
                    for (int i = 0; i < 5; i++)
                    {
                        var l = Prim(PrimitiveType.Cube, root, "Frond" + i, new Vector3(0.8f, 4.2f, 0), new Vector3(2.6f, 0.12f, 0.7f), leaf);
                        l.transform.localRotation = Quaternion.Euler(0, i * 72f, 0) * Quaternion.Euler(0, 0, -18f);
                        l.transform.localPosition = new Vector3(0.8f, 4.2f, 0) + Quaternion.Euler(0, i * 72f, 0) * new Vector3(1.1f, 0, 0);
                    }
                    break;
            }
            root.localScale = Vector3.one * s;
            root.localRotation = Quaternion.Euler(0, (float)r.NextDouble() * 360f, 0);
            return root.gameObject;
        }

        public static GameObject Cloud(System.Random r)
        {
            var root = new GameObject("Cloud").transform;
            var white = Mat(Color.white, 0f);
            if (icoMesh == null) icoMesh = IcoSphere(1);
            int n = 3 + r.Next(4);
            for (int i = 0; i < n; i++)
            {
                float sz = 10f + (float)r.NextDouble() * 10f;
                var g = MeshObj(root, "Puff" + i, icoMesh, new Vector3(i * 8f - n * 4f, (float)r.NextDouble() * 3f, (float)r.NextDouble() * 6f - 3f), new Vector3(sz, sz * 0.6f, sz), white);
                g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root.gameObject;
        }

        // ------------------------------------------------------------ 車體
        public const float WheelRadius = 0.28f;

        // 楔形(三角稜柱)網格:側面輪廓 (z,y) = (-0.5,0) (0.5,0) (-0.5,1),X 方向厚度 1(置中)。給尾鰭 / 前翼用。
        static Mesh wedgeMesh;
        static Mesh Wedge()
        {
            if (wedgeMesh != null) return wedgeMesh;
            var p = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(-0.5f, 1f, -0.5f),    // x=-0.5 面:A B C
                            new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 1f, -0.5f) };       // x=+0.5 面
            var cen = new Vector3(0f, 0.33f, -0.17f);
            var v = new List<Vector3>(); var t = new List<int>();
            void Tri(int a, int b, int c)
            {
                Vector3 n = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
                Vector3 mid = (p[a] + p[b] + p[c]) / 3f;
                if (Vector3.Dot(n, mid - cen) < 0f) { int s = b; b = c; c = s; }
                t.Add(a); t.Add(b); t.Add(c);
            }
            for (int i = 0; i < 6; i++) v.Add(p[i]);
            Tri(0, 1, 2); Tri(3, 4, 5);                       // 兩側三角
            Tri(0, 1, 4); Tri(0, 4, 3);                       // 底
            Tri(0, 2, 5); Tri(0, 5, 3);                       // 後(垂直)面
            Tri(1, 2, 5); Tri(1, 5, 4);                       // 斜面
            var m = new Mesh { name = "RaceWedge" }; m.SetVertices(v); m.SetTriangles(t, 0);
            wedgeMesh = Flat(m); Object.Destroy(m); return wedgeMesh;
        }

        // 七段顯示器數字(隊號 9427 色塊):在 parent 底下、中心 c、面朝 dir(+1 = +z,-1 = -z)。
        static readonly string[] Seg7 = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
        static void Digits(Transform parent, string text, Vector3 c, float dir, Material mat)
        {
            const float cw = 0.13f, chh = 0.24f, th = 0.032f, step = 0.19f;
            for (int d = 0; d < text.Length; d++)
            {
                float cx = (d - (text.Length - 1) * 0.5f) * step * -dir;   // 從車外看,文字左→右
                var segs = Seg7[text[d] - '0'];
                foreach (char s in segs)
                {
                    float x = 0f, y = 0f, sx = th, sy = th;
                    switch (s)
                    {
                        case 'a': y = chh * 0.5f; sx = cw; break;
                        case 'd': y = -chh * 0.5f; sx = cw; break;
                        case 'g': sx = cw; break;
                        case 'b': x = cw * 0.5f; y = chh * 0.25f; sy = chh * 0.5f; break;
                        case 'c': x = cw * 0.5f; y = -chh * 0.25f; sy = chh * 0.5f; break;
                        case 'f': x = -cw * 0.5f; y = chh * 0.25f; sy = chh * 0.5f; break;
                        case 'e': x = -cw * 0.5f; y = -chh * 0.25f; sy = chh * 0.5f; break;
                    }
                    Prim(PrimitiveType.Cube, parent, "D" + text[d] + s, c + new Vector3(cx + x * -dir, y, 0f), new Vector3(sx, sy, 0.02f), mat);
                }
            }
        }

        /// 造一台車的外觀,回傳 "Visual" 根物件(沒有父物件、沒有碰撞體);呼叫者把它掛到 Kart_<id> 底下。
        /// 子物件:Body、WheelFL/FR/RL/RR(樞紐,KartVisual 轉它;裡面有圓柱)、ExhaustL/R、Driver。腳底 y=0(輪胎底)。
        public static GameObject BuildKart(KartStyle style, Color color)
        {
            try { return KartModels.Build(style, color); }   // 圓滑造型(KartModels.cs);出任何錯就退回下面的舊方塊版,賽車不會因此開不了
            catch (System.Exception e) { Debug.LogError("[RaceArt] KartModels.Build failed, using legacy kart: " + e); return BuildKartLegacy(style, color); }
        }

        /// 舊版方塊車(KartModels 失敗時的後備)。
        public static GameObject BuildKartLegacy(KartStyle style, Color color)
        {
            var root = new GameObject("Visual").transform;
            var body = Mat(color, 0.6f);
            var dark = Mat(Color.Lerp(color, Color.black, 0.45f), 0.5f);
            var light = Mat(Color.Lerp(color, Color.white, 0.55f), 0.6f);
            var tire = Mat(new Color(0.10f, 0.10f, 0.12f), 0.25f);
            var hub = Mat(new Color(0.75f, 0.78f, 0.85f), 0.5f);
            var steel = Mat(new Color(0.55f, 0.58f, 0.64f), 0.7f);
            var white = Mat(new Color(0.96f, 0.96f, 0.98f), 0.4f);
            var headGlow = Glow(new Color(1f, 0.95f, 0.65f), 2f);
            var tailGlow = Glow(new Color(1f, 0.15f, 0.12f), 2f);
            float wr = WheelRadius, ww = 0.26f, wx = 0.68f, wzF = 0.62f, wzR = -0.62f, bodyY = 0.42f;
            float frontZ = 0.9f, rearZ = -0.9f, hullW = 1.2f, driverDy = 0.55f;
            var bodyObj = new GameObject("Body"); bodyObj.transform.SetParent(root, false);
            var bt = bodyObj.transform;
            bool fenders = true;

            switch (style)
            {
                case KartStyle.Bullet:
                    wr = 0.22f; bodyY = 0.38f; wx = 0.62f; wzF = 0.72f; wzR = -0.7f; ww = 0.24f; frontZ = 1.75f; rearZ = -0.95f; hullW = 0.9f; driverDy = 0.40f; fenders = false;
                    var cap = Prim(PrimitiveType.Capsule, bt, "Hull", new Vector3(0, bodyY, 0), new Vector3(0.9f, 0.95f, 0.9f), body);   // 躺平膠囊,長 1.9
                    cap.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                    MeshObj(bt, "Nose", Cone(8), new Vector3(0, bodyY, 0.85f), new Vector3(0.62f, 1.0f, 0.62f), light).transform.localRotation = Quaternion.Euler(90f, 0, 0);
                    Prim(PrimitiveType.Sphere, bt, "NoseTip", new Vector3(0, bodyY, 1.85f), new Vector3(0.14f, 0.14f, 0.14f), white);
                    Prim(PrimitiveType.Sphere, bt, "Canopy", new Vector3(0, bodyY + 0.3f, -0.1f), new Vector3(0.5f, 0.34f, 0.8f), Mat(new Color(0.1f, 0.14f, 0.22f), 0.9f));
                    Prim(PrimitiveType.Cube, bt, "Stripe", new Vector3(0, bodyY + 0.44f, -0.55f), new Vector3(0.1f, 0.03f, 0.9f), white);
                    foreach (float s in new[] { -1f, 1f })
                    {   // 後尾鰭(楔形)+ 前小翼 + 輪軸護罩
                        MeshObj(bt, "Fin", Wedge(), new Vector3(0.38f * s, bodyY + 0.2f, -0.78f), new Vector3(0.06f, 0.6f, 0.75f), dark).transform.localRotation = Quaternion.Euler(0, 0, 8f * s);
                        MeshObj(bt, "Canard", Wedge(), new Vector3(0.5f * s, bodyY - 0.05f, 0.62f), new Vector3(0.04f, 0.2f, 0.5f), dark).transform.localRotation = Quaternion.Euler(0, 0, 70f * s);
                    }
                    MeshObj(bt, "TailFin", Wedge(), new Vector3(0, bodyY + 0.42f, -0.8f), new Vector3(0.05f, 0.5f, 0.8f), light);
                    break;
                case KartStyle.Chonk:
                    wr = 0.45f; ww = 0.34f; bodyY = 0.72f; wx = 0.82f; wzF = 0.7f; wzR = -0.7f; frontZ = 1.15f; rearZ = -1.05f; hullW = 1.4f; driverDy = 1.0f;
                    Prim(PrimitiveType.Cube, bt, "Hull", new Vector3(0, bodyY, 0), new Vector3(1.4f, 0.6f, 2.0f), body);
                    Prim(PrimitiveType.Cube, bt, "Hood", new Vector3(0, bodyY + 0.34f, 0.55f), new Vector3(1.1f, 0.12f, 0.8f), dark);
                    Prim(PrimitiveType.Cube, bt, "Cab", new Vector3(0, bodyY + 0.55f, -0.25f), new Vector3(0.95f, 0.5f, 0.8f), dark);
                    foreach (float x in new[] { -0.55f, 0.55f })
                    {
                        Prim(PrimitiveType.Cube, bt, "Roll", new Vector3(x, bodyY + 1.0f, -0.2f), new Vector3(0.08f, 0.5f, 0.08f), steel);
                        Prim(PrimitiveType.Cylinder, bt, "Stack", new Vector3(x * 0.85f, bodyY + 0.95f, -0.78f), new Vector3(0.12f, 0.45f, 0.12f), steel);   // 排氣管
                        Prim(PrimitiveType.Cylinder, bt, "StackCap", new Vector3(x * 0.85f, bodyY + 1.42f, -0.78f), new Vector3(0.15f, 0.03f, 0.15f), tire);
                    }
                    Prim(PrimitiveType.Cube, bt, "RollTop", new Vector3(0, bodyY + 1.25f, -0.2f), new Vector3(1.2f, 0.08f, 0.08f), steel);
                    Prim(PrimitiveType.Cube, bt, "BullBar", new Vector3(0, bodyY - 0.08f, 1.12f), new Vector3(1.5f, 0.22f, 0.14f), steel);      // 大保險桿
                    Prim(PrimitiveType.Cube, bt, "BullBarTop", new Vector3(0, bodyY + 0.2f, 1.12f), new Vector3(1.2f, 0.06f, 0.1f), steel);
                    Prim(PrimitiveType.Cube, bt, "BumperB", new Vector3(0, bodyY - 0.1f, -1.08f), new Vector3(1.5f, 0.2f, 0.1f), dark);
                    Prim(PrimitiveType.Cube, bt, "StripeL", new Vector3(-0.2f, bodyY + 0.31f, 0.55f), new Vector3(0.12f, 0.02f, 1.7f), light);
                    Prim(PrimitiveType.Cube, bt, "StripeR", new Vector3(0.2f, bodyY + 0.31f, 0.55f), new Vector3(0.12f, 0.02f, 1.7f), light);
                    break;
                case KartStyle.Robot9427:
                    wx = 0.62f; wzF = 0.62f; wzR = -0.62f; wr = 0.28f; frontZ = 1.0f; rearZ = -1.0f; hullW = 1.0f; driverDy = 0.62f; fenders = false;
                    var blue = Mat(RacePalette.Kart[1], 0.5f);
                    var navy = Mat(new Color(0.08f, 0.16f, 0.42f), 0.5f);
                    var orange = Mat(RacePalette.Kart[5], 0.5f);
                    Prim(PrimitiveType.Cube, bt, "Hull", new Vector3(0, bodyY, 0), new Vector3(1.0f, 0.3f, 1.6f), body);
                    // 4 邊保險桿(FRC 風格:低矮、藍色)+ 倒角角塊
                    Prim(PrimitiveType.Cube, bt, "BumperF", new Vector3(0, bodyY - 0.05f, 0.95f), new Vector3(1.7f, 0.22f, 0.1f), blue);
                    Prim(PrimitiveType.Cube, bt, "BumperB", new Vector3(0, bodyY - 0.05f, -0.95f), new Vector3(1.7f, 0.22f, 0.1f), blue);
                    Prim(PrimitiveType.Cube, bt, "BumperL", new Vector3(-0.8f, bodyY - 0.05f, 0), new Vector3(0.1f, 0.22f, 1.9f), blue);
                    Prim(PrimitiveType.Cube, bt, "BumperR", new Vector3(0.8f, bodyY - 0.05f, 0), new Vector3(0.1f, 0.22f, 1.9f), blue);
                    foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                    {
                        Prim(PrimitiveType.Cube, bt, "Corner", new Vector3(0.8f * sx, bodyY - 0.05f, 0.95f * sz), new Vector3(0.14f, 0.24f, 0.14f), navy);
                        // swerve 模組:輪上方的金屬圓筒 + 馬達小方塊
                        Prim(PrimitiveType.Cylinder, bt, "SwerveCyl", new Vector3(0.62f * sx, 0.62f, 0.62f * sz), new Vector3(0.2f, 0.06f, 0.2f), steel);
                        Prim(PrimitiveType.Cube, bt, "SwerveMotor", new Vector3(0.52f * sx, 0.5f, 0.62f * sz), new Vector3(0.14f, 0.18f, 0.14f), tire);
                    }
                    // 隊號 9427:白色七段字,貼在前後保險桿(白字 on 藍桿);側邊橘色色塊
                    Digits(bt, "9427", new Vector3(0, bodyY - 0.05f, 1.0f + 0.011f), 1f, white);
                    Digits(bt, "9427", new Vector3(0, bodyY - 0.05f, -1.0f - 0.011f), -1f, white);
                    Prim(PrimitiveType.Cube, bt, "SideTagL", new Vector3(-0.855f, bodyY - 0.05f, 0), new Vector3(0.012f, 0.14f, 0.7f), orange);
                    Prim(PrimitiveType.Cube, bt, "SideTagR", new Vector3(0.855f, bodyY - 0.05f, 0), new Vector3(0.012f, 0.14f, 0.7f), orange);
                    // 電子元件:電池(黑)+ roboRIO(銀)+ 天線 + 旗桿
                    Prim(PrimitiveType.Cube, bt, "Battery", new Vector3(0.28f, bodyY + 0.24f, -0.45f), new Vector3(0.3f, 0.18f, 0.2f), tire);
                    Prim(PrimitiveType.Cube, bt, "BatteryLabel", new Vector3(0.28f, bodyY + 0.335f, -0.45f), new Vector3(0.22f, 0.01f, 0.12f), Mat(RacePalette.CurbRed, 0.4f));
                    Prim(PrimitiveType.Cube, bt, "RoboRio", new Vector3(-0.2f, bodyY + 0.2f, 0.3f), new Vector3(0.36f, 0.1f, 0.26f), steel);
                    Prim(PrimitiveType.Cube, bt, "RioLed", new Vector3(-0.1f, bodyY + 0.255f, 0.36f), new Vector3(0.05f, 0.015f, 0.05f), Glow(RacePalette.Go, 2.5f));
                    Prim(PrimitiveType.Cylinder, bt, "Antenna", new Vector3(0.4f, bodyY + 0.45f, 0.45f), new Vector3(0.025f, 0.28f, 0.025f), tire);
                    Prim(PrimitiveType.Cylinder, bt, "Pole", new Vector3(-0.5f, bodyY + 0.95f, -0.78f), new Vector3(0.035f, 0.95f, 0.035f), hub);
                    Prim(PrimitiveType.Cube, bt, "Flag", new Vector3(-0.5f, bodyY + 1.65f, -0.52f), new Vector3(0.03f, 0.36f, 0.5f), blue);
                    Digits(bt, "9427", new Vector3(-0.5f - 0.02f, bodyY + 1.65f, -0.52f), -1f, white);   // 旗面隊號
                    Digits(bt, "9427", new Vector3(-0.5f + 0.02f, bodyY + 1.65f, -0.52f), 1f, white);
                    break;
                default:   // Brick
                    Prim(PrimitiveType.Cube, bt, "Hull", new Vector3(0, bodyY, 0), new Vector3(1.2f, 0.35f, 1.8f), body);
                    Prim(PrimitiveType.Cube, bt, "Cabin", new Vector3(0, bodyY + 0.3f, -0.15f), new Vector3(0.7f, 0.3f, 0.7f), dark);
                    Prim(PrimitiveType.Cube, bt, "Hood", new Vector3(0, bodyY + 0.19f, 0.5f), new Vector3(0.9f, 0.04f, 0.6f), light);
                    Prim(PrimitiveType.Cube, bt, "Wing", new Vector3(0, bodyY + 0.5f, -0.95f), new Vector3(1.2f, 0.06f, 0.3f), dark);
                    Prim(PrimitiveType.Cube, bt, "WingL", new Vector3(-0.4f, bodyY + 0.3f, -0.95f), new Vector3(0.06f, 0.35f, 0.1f), dark);
                    Prim(PrimitiveType.Cube, bt, "WingR", new Vector3(0.4f, bodyY + 0.3f, -0.95f), new Vector3(0.06f, 0.35f, 0.1f), dark);
                    Prim(PrimitiveType.Cube, bt, "WingEndL", new Vector3(-0.6f, bodyY + 0.5f, -0.95f), new Vector3(0.04f, 0.14f, 0.34f), light);
                    Prim(PrimitiveType.Cube, bt, "WingEndR", new Vector3(0.6f, bodyY + 0.5f, -0.95f), new Vector3(0.04f, 0.14f, 0.34f), light);
                    Prim(PrimitiveType.Cube, bt, "Stripe", new Vector3(0, bodyY + 0.185f, 0.1f), new Vector3(0.14f, 0.02f, 1.6f), white);
                    Prim(PrimitiveType.Cube, bt, "BumperF", new Vector3(0, bodyY - 0.1f, 0.93f), new Vector3(1.3f, 0.12f, 0.1f), dark);
                    break;
            }

            // 頭燈 / 尾燈(發光色塊)
            foreach (float s in new[] { -1f, 1f })
            {
                float hx = hullW * 0.32f * s;
                Prim(PrimitiveType.Cube, bt, "Head", new Vector3(hx, bodyY + (style == KartStyle.Chonk ? 0.12f : 0.02f), frontZ - 0.02f), new Vector3(0.16f, 0.09f, 0.04f), headGlow);
                Prim(PrimitiveType.Cube, bt, "Tail", new Vector3(hx, bodyY + 0.02f, rearZ + 0.02f), new Vector3(0.16f, 0.07f, 0.04f), tailGlow);
            }

            // 輪子:樞紐(KartVisual 轉)→ 圓柱(軸向 X)
            void Wheel(string n, float x, float z)
            {
                var p = new GameObject(n).transform; p.SetParent(root, false); p.localPosition = new Vector3(x, wr, z);
                var cyl = Prim(PrimitiveType.Cylinder, p, "Tire", Vector3.zero, new Vector3(wr * 2f, ww * 0.5f, wr * 2f), tire);
                cyl.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                var cap = Prim(PrimitiveType.Cylinder, p, "Hub", new Vector3(Mathf.Sign(x) * (ww * 0.5f + 0.005f), 0, 0), new Vector3(wr * 1.1f, 0.01f, wr * 1.1f), hub);
                cap.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                // 輪拱:輪子上方的深色拱板(固定在車身,不跟輪子轉)
                if (fenders)
                {
                    Prim(PrimitiveType.Cube, bt, "Arch", new Vector3(x, wr * 2f + 0.04f, z), new Vector3(ww + 0.1f, 0.08f, wr * 2f + 0.14f), dark);
                    Prim(PrimitiveType.Cube, bt, "ArchLip", new Vector3(x + Mathf.Sign(x) * (ww * 0.5f + 0.04f), wr * 1.6f, z), new Vector3(0.05f, wr * 0.9f, wr * 2f + 0.1f), dark);
                }
            }
            Wheel("WheelFL", -wx, wzF); Wheel("WheelFR", wx, wzF); Wheel("WheelRL", -wx, wzR); Wheel("WheelRR", wx, wzR);

            // 排氣口(空物件,RaceFx 噴焰位置)+ 排氣管外觀
            foreach (var s in new[] { -1f, 1f })
            {
                var e = new GameObject(s < 0 ? "ExhaustL" : "ExhaustR").transform; e.SetParent(root, false);
                e.localPosition = new Vector3(0.35f * s, bodyY, wzR - 0.3f); e.localRotation = Quaternion.Euler(0, 180f, 0);
                if (style != KartStyle.Chonk)
                {
                    var pipe = Prim(PrimitiveType.Cylinder, e, "Pipe", new Vector3(0, 0, 0.12f), new Vector3(0.12f, 0.12f, 0.12f), steel);
                    pipe.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                }
            }

            // 駕駛:膠囊身體 + 車色安全帽(白條)+ 黑色面罩 + 手臂與方向盤
            var drv = new GameObject("Driver").transform; drv.SetParent(root, false);
            float dy = bodyY + driverDy;
            float dz = style == KartStyle.Bullet ? -0.1f : -0.15f;
            Prim(PrimitiveType.Capsule, drv, "Torso", new Vector3(0, dy, dz), new Vector3(0.38f, 0.28f, 0.38f), Mat(new Color(0.95f, 0.95f, 0.98f), 0.3f));
            Prim(PrimitiveType.Sphere, drv, "Helmet", new Vector3(0, dy + 0.38f, dz + 0.03f), new Vector3(0.42f, 0.42f, 0.42f), body);
            Prim(PrimitiveType.Cube, drv, "HelmetStripe", new Vector3(0, dy + 0.585f, dz + 0.03f), new Vector3(0.07f, 0.02f, 0.3f), white);
            Prim(PrimitiveType.Sphere, drv, "Visor", new Vector3(0, dy + 0.38f, dz + 0.19f), new Vector3(0.32f, 0.16f, 0.2f), Mat(new Color(0.04f, 0.04f, 0.06f), 0.8f));
            var wheelObj = Prim(PrimitiveType.Cylinder, drv, "SteeringWheel", new Vector3(0, dy + 0.02f, dz + 0.42f), new Vector3(0.3f, 0.02f, 0.3f), tire);
            wheelObj.transform.localRotation = Quaternion.Euler(70f, 0, 0);
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = Prim(PrimitiveType.Capsule, drv, "Arm", new Vector3(0.2f * s, dy + 0.03f, dz + 0.22f), new Vector3(0.1f, 0.2f, 0.1f), Mat(new Color(0.95f, 0.95f, 0.98f), 0.3f));
                arm.transform.localRotation = Quaternion.Euler(75f, 0, 0);
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.receiveShadows = true; }
            return root.gameObject;
        }

        /// AI 車款:AI 輪流用不同款式(玩家款式不重複),讓賽道上一眼分得出對手。
        public static KartStyle AiStyle(int aiIndex, KartStyle player)
        {
            var all = (KartStyle[])System.Enum.GetValues(typeof(KartStyle));
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
                foreach (var s in all)
                {
                    if (s == player) continue;
                    if (n++ == aiIndex % 3) return s;
                }
            return player;
        }

        // ------------------------------------------------------------ 環境(還原由 RaceSession 的 Env.Restore 負責)
        /// 戶外光:地平線色天空 + 線性霧 + 三色環境光 + 暖色太陽 + 80 m 陰影。M1 用純色天空;天穹/雲 M2 補。
        public static void ApplyOutdoor(Camera cam)
        {
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.Lerp(RacePalette.SkyTop, RacePalette.Horizon, 0.25f); cam.farClipPlane = 800f; }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = RacePalette.Horizon;
            RenderSettings.fogStartDistance = 150f; RenderSettings.fogEndDistance = 600f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.62f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.32f, 0.25f);
            QualitySettings.shadowDistance = 80f;
            var sg = GameObject.Find("Sun"); var sun = sg != null ? sg.GetComponent<Light>() : null;
            if (sun != null) { sun.color = new Color(1f, 0.95f, 0.85f); sun.intensity = 0.8f; }
            RenderSettings.ambientIntensity = 0.7f;
            if (PostFX.Instance != null) PostFX.Instance.Exposure = 0.8f;   // 舊場館的曝光是為暗場調的,戶外明亮場景要降低(RaceSession.Env 負責還原)
        }

        /// 相容用:真正的還原在 RaceSession.End → Env.Restore(),這裡不做事。
        public static void RestoreIndoor() { }
    }
}
