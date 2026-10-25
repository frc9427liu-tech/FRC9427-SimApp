using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 程式生成的「動物森友會 x 瑪利歐賽車」風格車與車手(模組 P)。沒有外部素材。
    // 階層(KartVisual 依名稱找,不可改):Visual → Body / WheelFL/FR/RL/RR / ExhaustL/R / Driver(→ Torso → Head、SteeringWheel)。
    // 每台車:Body 一個合併網格(多個子網格 = 多材質)、4 個輪子各一個網格(胎+輪框)、車手 3 個小網格。
    // 全部頂點共用 → 法線平滑 → 描邊(RaceShaders.Toon outline)不會裂;平面件(數字、擋板)放在不描邊的材質槽。

    /// <summary>可累積多個基本形狀的網格建構器(頂點共用、可分子網格)。純資料,不依賴場景。</summary>
    public sealed class KartMeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<int>[] T;
        public KartMeshBuilder(int slots) { T = new List<int>[slots]; for (int i = 0; i < slots; i++) T[i] = new List<int>(); }
        public int Tris { get { int n = 0; foreach (var t in T) n += t.Count; return n / 3; } }

        // ---- 旋轉(不用 Quaternion.Euler,免走 native;順序同 Unity:Z、X、Y)
        static Quaternion Ax(float x, float y, float z, float deg)
        {
            float h = deg * Mathf.Deg2Rad * 0.5f, s = Mathf.Sin(h);
            return new Quaternion(x * s, y * s, z * s, Mathf.Cos(h));
        }
        public static Quaternion Rot(float x, float y, float z) { return Ax(0, 1, 0, y) * Ax(1, 0, 0, x) * Ax(0, 0, 1, z); }

        static void AddTri(List<int> l, int a, int b, int c) { if (a == b || b == c || a == c) return; l.Add(a); l.Add(b); l.Add(c); }

        /// 封閉形狀:算帶號體積,朝內就整個翻面(保證朝外)。
        void Commit(List<int> tl, int sub, bool closed)
        {
            if (closed)
            {
                double vol = 0;
                for (int i = 0; i + 2 < tl.Count; i += 3) vol += Vector3.Dot(V[tl[i]], Vector3.Cross(V[tl[i + 1]], V[tl[i + 2]]));
                if (vol < 0) for (int i = 0; i + 2 < tl.Count; i += 3) { int t = tl[i + 1]; tl[i + 1] = tl[i + 2]; tl[i + 2] = t; }
            }
            T[sub].AddRange(tl);
        }

        /// 四邊形(a,b,c,d 順著邊走),依期望法線 n 決定繞向。
        void QuadN(List<int> tl, int a, int b, int c, int d, Vector3 n)
        {
            Vector3 g = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (g.sqrMagnitude < 1e-12f) g = Vector3.Cross(V[c] - V[a], V[d] - V[a]);
            if (Vector3.Dot(g, n) >= 0f) { AddTri(tl, a, b, c); AddTri(tl, a, c, d); }
            else { AddTri(tl, a, c, b); AddTri(tl, a, d, c); }
        }

        int Pt(Vector3 p) { V.Add(p); return V.Count - 1; }

        // ---------------------------------------------------------------- 平面件
        /// 銳利方塊(24 頂點、12 三角形)。用於不描邊的細件(擋板、電子元件、數字底)。
        public void Box(Vector3 c, Vector3 half, int sub, Quaternion rot)
        {
            var tl = new List<int>();
            for (int k = 0; k < 3; k++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int u = (k + 1) % 3, v = (k + 2) % 3;
                    Vector3 n = Vector3.zero; n[k] = s;
                    Vector3 eu = Vector3.zero; eu[u] = half[u];
                    Vector3 ev = Vector3.zero; ev[v] = half[v];
                    Vector3 f = n; f[k] = half[k] * s;
                    int a = Pt(c + rot * (f - eu - ev)), b = Pt(c + rot * (f + eu - ev)), cc = Pt(c + rot * (f + eu + ev)), d = Pt(c + rot * (f - eu + ev));
                    QuadN(tl, a, b, cc, d, rot * n);
                }
            Commit(tl, sub, false);
        }

        /// 單面平板(2 三角形),面朝 n,up 為紙面上方。貼紙/數字用。
        public void Quad(Vector3 c, Vector3 n, Vector3 up, float w, float h, int sub)
        {
            Vector3 r = Vector3.Cross(n, up).normalized; Vector3 u = Vector3.Cross(r, n).normalized;
            var tl = new List<int>();
            int a = Pt(c - r * (w * 0.5f) - u * (h * 0.5f)), b = Pt(c + r * (w * 0.5f) - u * (h * 0.5f)), cc = Pt(c + r * (w * 0.5f) + u * (h * 0.5f)), d = Pt(c - r * (w * 0.5f) + u * (h * 0.5f));
            QuadN(tl, a, b, cc, d, n);
            Commit(tl, sub, false);
        }

        static readonly string[] Seg7 = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
        /// 七段數字(單面平板拼成,每段 2 三角形)。從 n 的外側看文字由左到右。
        public void Digits(string text, Vector3 c, Vector3 n, Vector3 up, float charH, int sub)
        {
            Vector3 r = Vector3.Cross(n, up).normalized; Vector3 u = Vector3.Cross(r, n).normalized;
            float cw = charH * 0.52f, th = charH * 0.2f, step = cw * 1.6f;
            for (int d = 0; d < text.Length; d++)
            {
                int dig = text[d] - '0'; if (dig < 0 || dig > 9) continue;
                float cx = (d - (text.Length - 1) * 0.5f) * step;
                foreach (char s in Seg7[dig])
                {
                    float x = 0, y = 0, sx = th, sy = th;
                    switch (s)
                    {
                        case 'a': y = charH * 0.5f - th * 0.5f; sx = cw; break;
                        case 'd': y = -charH * 0.5f + th * 0.5f; sx = cw; break;
                        case 'g': sx = cw; break;
                        case 'b': x = cw * 0.5f - th * 0.5f; y = charH * 0.25f; sy = charH * 0.5f; break;
                        case 'c': x = cw * 0.5f - th * 0.5f; y = -charH * 0.25f; sy = charH * 0.5f; break;
                        case 'f': x = -cw * 0.5f + th * 0.5f; y = charH * 0.25f; sy = charH * 0.5f; break;
                        case 'e': x = -cw * 0.5f + th * 0.5f; y = -charH * 0.25f; sy = charH * 0.5f; break;
                    }
                    Quad(c + r * (cx + x) + u * y, n, up, sx, sy, sub);
                }
            }
        }

        // ---------------------------------------------------------------- 圓角方塊
        static float[] Samples(float h, float r, int mid)
        {
            float f = h - r;
            if (f <= 1e-4f) return new[] { -h, 0f, h };
            var l = new List<float> { -h, -f };
            for (int i = 1; i <= mid; i++) l.Add(-f + 2f * f * i / (mid + 1));
            l.Add(f); l.Add(h);
            return l.ToArray();
        }

        /// 圓角方塊:半尺寸 h、圓角半徑 r;mz = z 軸中間多加的取樣數(長件才需要)。deform 在區域座標上變形(收鼻、錐形)。
        public void RBox(Vector3 c, Vector3 h, float r, int sub, Quaternion rot, int mz = 0, Func<Vector3, Vector3> deform = null)
        {
            r = Mathf.Max(0.004f, Mathf.Min(r, Mathf.Min(h.x, Mathf.Min(h.y, h.z))));
            var A = new[] { Samples(h.x, r, 0), Samples(h.y, r, 0), Samples(h.z, r, mz) };
            var idx = new Dictionary<int, int>();
            int[] n = { A[0].Length, A[1].Length, A[2].Length };
            int Get(int ix, int iy, int iz)
            {
                int key = ix + 32 * (iy + 32 * iz);
                if (idx.TryGetValue(key, out int id)) return id;
                var q = new Vector3(A[0][ix], A[1][iy], A[2][iz]);
                var inner = new Vector3(Mathf.Clamp(q.x, -(h.x - r), h.x - r), Mathf.Clamp(q.y, -(h.y - r), h.y - r), Mathf.Clamp(q.z, -(h.z - r), h.z - r));
                var d = q - inner;
                Vector3 nn = d.sqrMagnitude > 1e-10f ? d.normalized : new Vector3(0, 0, 1);
                Vector3 p = inner + nn * r;
                if (deform != null) p = deform(p);
                id = Pt(c + rot * p); idx[key] = id; return id;
            }
            var tl = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                int u = (k + 1) % 3, v = (k + 2) % 3;
                for (int s = 0; s < 2; s++)
                {
                    Vector3 nrm = Vector3.zero; nrm[k] = s == 0 ? -1f : 1f; nrm = rot * nrm;
                    for (int i = 0; i < n[u] - 1; i++)
                        for (int j = 0; j < n[v] - 1; j++)
                        {
                            int[] i00 = new int[3], i10 = new int[3], i11 = new int[3], i01 = new int[3];
                            i00[k] = i10[k] = i11[k] = i01[k] = s == 0 ? 0 : n[k] - 1;
                            i00[u] = i; i00[v] = j; i10[u] = i + 1; i10[v] = j; i11[u] = i + 1; i11[v] = j + 1; i01[u] = i; i01[v] = j + 1;
                            QuadN(tl, Get(i00[0], i00[1], i00[2]), Get(i10[0], i10[1], i10[2]), Get(i11[0], i11[1], i11[2]), Get(i01[0], i01[1], i01[2]), nrm);
                        }
                }
            }
            Commit(tl, sub, false);
        }

        // ---------------------------------------------------------------- 旋轉體(輪胎、圓柱、橢球、安全帽)
        /// 繞區域 Z 軸旋轉:prof.x = 軸向位置、prof.y = 半徑;半徑 0 的點共用一個極點。loop = 輪廓首尾相連(環面)。ex/ey 為橢圓係數。
        public void Lathe(Vector3 c, Quaternion rot, Vector2[] prof, int seg, int sub, float ex = 1f, float ey = 1f, bool loop = false, Func<int, int, float> bump = null)
        {
            int n = prof.Length;
            var id = new int[n, seg];
            for (int i = 0; i < n; i++)
            {
                bool pole = prof[i].y < 1e-5f;
                for (int s = 0; s < seg; s++)
                {
                    if (pole && s > 0) { id[i, s] = id[i, 0]; continue; }
                    float ang = s * Mathf.PI * 2f / seg;
                    float rad = prof[i].y * (bump != null ? bump(i, s) : 1f);
                    id[i, s] = Pt(c + rot * new Vector3(Mathf.Cos(ang) * rad * ex, Mathf.Sin(ang) * rad * ey, prof[i].x));
                }
            }
            var tl = new List<int>();
            int bands = loop ? n : n - 1;
            for (int i = 0; i < bands; i++)
            {
                int i2 = (i + 1) % n;
                for (int s = 0; s < seg; s++)
                {
                    int s2 = (s + 1) % seg;
                    int a = id[i, s], b = id[i, s2], cc = id[i2, s2], d = id[i2, s];
                    AddTri(tl, a, d, cc); AddTri(tl, a, cc, b);
                }
            }
            Commit(tl, sub, true);
        }

        /// 橢球(極點朝 +Y)。
        public void Ellipsoid(Vector3 c, Vector3 radii, int seg, int bands, int sub, Quaternion? rot = null)
        {
            var p = new Vector2[bands + 1];
            for (int i = 0; i <= bands; i++)
            {
                float phi = (-90f + 180f * i / bands) * Mathf.Deg2Rad;
                float rad = Mathf.Cos(phi); if (i == 0 || i == bands) rad = 0f;
                p[i] = new Vector2(radii.y * Mathf.Sin(phi), rad);
            }
            Lathe(c, (rot ?? Quaternion.identity) * Rot(-90f, 0, 0), p, seg, sub, radii.x, radii.z);
        }

        /// 半球殼(安全帽):從緯度 cutLat 到頂,底面封平。極點朝 +Y。
        public void Dome(Vector3 c, Vector3 radii, float cutLatDeg, int seg, int bands, int sub, Quaternion? rot = null)
        {
            var p = new List<Vector2> { new Vector2(radii.y * Mathf.Sin(cutLatDeg * Mathf.Deg2Rad), 0f) };
            for (int i = 0; i <= bands; i++)
            {
                float phi = (cutLatDeg + (90f - cutLatDeg) * i / bands) * Mathf.Deg2Rad;
                float rad = i == bands ? 0f : Mathf.Cos(phi);
                p.Add(new Vector2(radii.y * Mathf.Sin(phi), rad));
            }
            Lathe(c, (rot ?? Quaternion.identity) * Rot(-90f, 0, 0), p.ToArray(), seg, sub, radii.x, radii.z);
        }

        /// 圓柱(兩端封平),軸向 = 區域 Y 再套 rot;halfLen 為半長。
        public void Cyl(Vector3 c, float radius, float halfLen, int seg, int sub, Quaternion? rot = null)
        {
            var p = new[] { new Vector2(-halfLen, 0f), new Vector2(-halfLen, radius), new Vector2(halfLen, radius), new Vector2(halfLen, 0f) };
            Lathe(c, (rot ?? Quaternion.identity) * Rot(-90f, 0, 0), p, seg, sub);
        }

        // ---------------------------------------------------------------- 輸出
        public Mesh ToMesh(string name, out int[] usedSlots)
        {
            var used = new List<int>();
            for (int i = 0; i < T.Length; i++) if (T[i].Count > 0) used.Add(i);
            usedSlots = used.ToArray();
            var m = new Mesh { name = name };
            m.SetVertices(V);
            m.subMeshCount = Mathf.Max(1, used.Count);
            for (int i = 0; i < used.Count; i++) m.SetTriangles(T[used[i]], i, false);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>車與車手的完整幾何資料(純資料,方便離線檢查)。</summary>
    public sealed class KartParts
    {
        public KartStyle Style;
        public float Wr, Ww, Wx, WzF, WzR;
        public Vector3 ExhaustL, ExhaustR;
        public Vector3 Hip, SteerPos, SteerDir;
        public KartMeshBuilder Body, WheelL, WheelR, Torso, Head, Steer;
        public Color Main, Rim;
        public int Number;
    }

    public static class KartModels
    {
        // 材質槽(所有網格共用同一組編號)
        public const int S_BODY = 0, S_DARK = 1, S_WHITE = 2, S_BLACK = 3, S_STEEL = 4, S_ACC = 5, S_SKIN = 6, S_HEAD = 7, S_TAIL = 8, S_GLASS = 9, S_RIM = 10, NSlots = 11;

        static readonly Color[] Palette6 = {
            new Color(1f, 0.29f, 0.24f), new Color(0.25f, 0.65f, 1f), new Color(1f, 0.79f, 0.24f),
            new Color(0.24f, 0.86f, 0.52f), new Color(0.65f, 0.42f, 1f), new Color(1f, 0.54f, 0.16f) };

        static readonly Dictionary<string, KartParts> partsCache = new Dictionary<string, KartParts>();
        static readonly Dictionary<KartMeshBuilder, KeyValuePair<Mesh, int[]>> meshCache = new Dictionary<KartMeshBuilder, KeyValuePair<Mesh, int[]>>();

        public static void ClearCache()
        {
            foreach (var kv in meshCache) if (kv.Value.Key != null) UnityEngine.Object.Destroy(kv.Value.Key);
            meshCache.Clear(); partsCache.Clear();
        }

        static int NumberFor(Color c)
        {
            int best = 0; float bd = 9f;
            for (int i = 0; i < Palette6.Length; i++)
            {
                float d = Mathf.Abs(Palette6[i].r - c.r) + Mathf.Abs(Palette6[i].g - c.g) + Mathf.Abs(Palette6[i].b - c.b);
                if (d < bd) { bd = d; best = i; }
            }
            return best + 1;
        }

        // ================================================================= 幾何
        /// 產生一款車的全部幾何(純資料,不碰 GameObject)。number<=0 時由車色推導。
        public static KartParts BuildParts(KartStyle style, Color color, int number)
        {
            if (number <= 0) number = NumberFor(color);
            var P = new KartParts { Style = style, Main = color, Number = number };
            P.Body = new KartMeshBuilder(NSlots); P.Torso = new KartMeshBuilder(NSlots); P.Head = new KartMeshBuilder(NSlots); P.Steer = new KartMeshBuilder(NSlots);
            switch (style)
            {
                case KartStyle.Bullet: Bullet(P); break;
                case KartStyle.Chonk: Chonk(P); break;
                case KartStyle.Robot9427: Robot(P); break;
                default: Brick(P); break;
            }
            return P;
        }

        // ---- 輪子:輪胎(squircle 環面,外胎面交錯 = 胎紋)+ 輪框(碟形,邊緣有 5 瓣星形)
        static KartMeshBuilder Wheel(float wr, float ww, bool left, float rimR, int tireProfile, int seg)
        {
            var b = new KartMeshBuilder(NSlots);
            float w = ww * 0.5f, rin = wr * 0.5f, rc = (wr + rin) * 0.5f, hr = (wr - rin) * 0.5f;
            var prof = new Vector2[tireProfile];
            for (int k = 0; k < tireProfile; k++)
            {
                float ph = k * Mathf.PI * 2f / tireProfile; float cs = Mathf.Cos(ph), sn = Mathf.Sin(ph);
                prof[k] = new Vector2(w * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.5f), rc + hr * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.5f));
                if (Mathf.Abs(sn) < 1e-4f) prof[k].x = 0f;
            }
            var rot = KartMeshBuilder.Rot(0, left ? -90f : 90f, 0);
            b.Lathe(Vector3.zero, rot, prof, seg, S_BLACK, 1f, 1f, true, (pi, s) => (prof[pi].y > rc + hr * 0.55f && (s & 1) == 0) ? 0.90f : 1f);
            float rr = rin * 1.12f;
            var rim = new[] { new Vector2(-0.4f * w, 0f), new Vector2(-0.4f * w, rr), new Vector2(0.8f * w, rr * 0.92f), new Vector2(0.8f * w, 0f) };
            b.Lathe(Vector3.zero, rot, rim, 10, S_RIM, 1f, 1f, false, (pi, s) => (pi == 2 && (s & 1) == 1) ? 0.62f : 1f);
            return b;
        }

        // ---- 數字貼紙:深色圓牌 + 白色七段數字
        static void Sticker(KartMeshBuilder b, string txt, Vector3 c, Vector3 n, Vector3 up, float size)
        {
            b.Quad(c + n * 0.004f, n, up, size * 1.25f, size * 1.4f, S_DARK);
            b.Digits(txt, c + n * 0.009f, n, up, size, S_WHITE);
        }

        // ---- 車手(共用):大頭、安全帽(車色)+護目鏡、小身體、手握方向盤
        static void Driver(KartParts P, Vector3 hip, Vector3 steerPos, Vector3 steerDir, float headScale, bool antenna)
        {
            P.Hip = hip; P.SteerPos = steerPos; P.SteerDir = steerDir;
            Func<float, float, float, Quaternion> Q = KartMeshBuilder.Rot;
            // 身體(以髖部為原點)
            P.Torso.Ellipsoid(new Vector3(0, 0.17f, 0), new Vector3(0.19f, 0.21f, 0.17f), 8, 4, S_WHITE);
            foreach (float s in new[] { -1f, 1f })
                P.Torso.Ellipsoid(new Vector3(0.2f * s, 0.27f, 0.13f), new Vector3(0.06f, 0.06f, 0.17f), 6, 3, S_WHITE, Q(-12f, 0, 0));
            // 頭(以脖子為原點,大頭)
            float hs = headScale;
            var hc = new Vector3(0, 0.25f * hs, 0);
            var H = P.Head;
            H.Ellipsoid(hc, new Vector3(0.27f, 0.25f, 0.26f) * hs, 10, 6, S_SKIN);
            H.Dome(hc + new Vector3(0, 0.02f, -0.02f) * hs, new Vector3(0.315f, 0.30f, 0.31f) * hs, 8f, 10, 4, S_BODY, Q(-18f, 0, 0));
            H.Ellipsoid(hc + new Vector3(0, 0.03f, 0) * hs, new Vector3(0.275f, 0.05f, 0.265f) * hs, 10, 3, S_DARK);                 // 護目鏡帶
            H.Ellipsoid(hc + new Vector3(0, 0.03f, 0.2f) * hs, new Vector3(0.19f, 0.075f, 0.07f) * hs, 8, 3, S_GLASS);               // 鏡片
            foreach (float s in new[] { -1f, 1f })
                H.Ellipsoid(hc + new Vector3(0.2f * s, 0.25f, -0.02f) * hs, new Vector3(0.08f, 0.08f, 0.05f) * hs, 8, 3, S_BODY);   // 圓耳朵
            if (antenna)
            {
                H.Cyl(hc + new Vector3(0, 0.66f * hs, 0), 0.012f, 0.1f * hs, 6, S_DARK);
                H.Ellipsoid(hc + new Vector3(0, 0.78f * hs, 0), new Vector3(0.04f, 0.04f, 0.04f), 6, 3, S_HEAD);
            }
            // 方向盤(以盤心為原點,盤面朝區域 +Z):環 + 十字橫桿 + 兩隻小手
            var ring = new[] { new Vector2(0f, 0.15f + 0.03f), new Vector2(0.03f, 0.15f), new Vector2(0f, 0.15f - 0.03f), new Vector2(-0.03f, 0.15f) };
            P.Steer.Lathe(Vector3.zero, Quaternion.identity, ring, 10, S_BLACK, 1f, 1f, true);
            P.Steer.Box(Vector3.zero, new Vector3(0.15f, 0.018f, 0.018f), S_BLACK, Quaternion.identity);
            foreach (float s in new[] { -1f, 1f })
                P.Steer.Ellipsoid(new Vector3(0.15f * s, 0.01f, 0.02f), new Vector3(0.065f, 0.06f, 0.065f), 6, 3, S_SKIN);
        }

        // ---------------------------------------------------------------- Brick:標準款,圓角車身、翅膀、胖輪
        static void Brick(KartParts P)
        {
            P.Wr = 0.30f; P.Ww = 0.24f; P.Wx = 0.54f; P.WzF = 0.6f; P.WzR = -0.6f;
            P.Rim = new Color(0.78f, 0.80f, 0.88f);
            var b = P.Body; Func<float, float, float, Quaternion> Q = KartMeshBuilder.Rot;
            float by = 0.46f;
            Func<Vector3, Vector3> taper = p => { float t = Mathf.Clamp01((p.z - 0.2f) / 0.68f); p.x *= 1f - 0.18f * t; return p; };
            b.RBox(new Vector3(0, by, 0), new Vector3(0.5f, 0.2f, 0.88f), 0.16f, S_BODY, Quaternion.identity, 1, taper);              // 車身
            b.RBox(new Vector3(0, 0.72f, -0.55f), new Vector3(0.34f, 0.2f, 0.2f), 0.16f, S_DARK, Quaternion.identity);               // 座椅靠背
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })                                              // 圓潤輪拱
                    b.RBox(new Vector3(P.Wx * sx, 0.58f, (sz > 0 ? P.WzF : P.WzR)), new Vector3(0.15f, 0.09f, 0.34f), 0.09f, S_BODY, Quaternion.identity);
            b.RBox(new Vector3(0, 0.36f, 0.95f), new Vector3(0.5f, 0.07f, 0.07f), 0.07f, S_DARK, Quaternion.identity);                // 前保險桿
            b.RBox(new Vector3(0, 0.98f, -0.92f), new Vector3(0.55f, 0.03f, 0.14f), 0.03f, S_DARK, Quaternion.identity);              // 尾翼
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(0.3f * sx, 0.78f, -0.88f), new Vector3(0.035f, 0.17f, 0.04f), S_DARK, Quaternion.identity);
                b.Box(new Vector3(0.57f * sx, 0.98f, -0.92f), new Vector3(0.02f, 0.09f, 0.16f), S_ACC, Quaternion.identity);
                b.Ellipsoid(new Vector3(0.26f * sx, 0.5f, 0.87f), new Vector3(0.09f, 0.065f, 0.05f), 8, 3, S_HEAD);                    // 頭燈
                b.Box(new Vector3(0.3f * sx, 0.5f, -0.885f), new Vector3(0.08f, 0.04f, 0.015f), S_TAIL, Quaternion.identity);         // 尾燈
                b.Cyl(new Vector3(0.3f * sx, 0.42f, -0.98f), 0.05f, 0.08f, 8, S_STEEL, Q(90f, 0, 0));                                  // 排氣管
                Sticker(b, P.Number.ToString(), new Vector3(0.5f * sx, by, 0.12f), new Vector3(sx, 0, 0), Vector3.up, 0.15f);
            }
            b.Quad(new Vector3(0, by + 0.2f + 0.004f, 0.05f), Vector3.up, Vector3.forward, 0.12f, 1.2f, S_WHITE);                        // 賽車條紋
            P.ExhaustL = new Vector3(-0.3f, 0.42f, -1.0f); P.ExhaustR = new Vector3(0.3f, 0.42f, -1.0f);
            Driver(P, new Vector3(0, 0.62f, -0.12f), new Vector3(0, 0.93f, 0.25f), new Vector3(0, 0.55f, -0.83f), 1f, false);
        }

        // ---------------------------------------------------------------- Bullet:流線膠囊、尖鼻、小尾翼、細輪
        static void Bullet(KartParts P)
        {
            P.Wr = 0.24f; P.Ww = 0.2f; P.Wx = 0.46f; P.WzF = 0.8f; P.WzR = -0.62f;
            P.Rim = new Color(0.96f, 0.96f, 0.98f);
            var b = P.Body; Func<float, float, float, Quaternion> Q = KartMeshBuilder.Rot;
            float by = 0.42f;
            var prof = new[] { new Vector2(-0.95f, 0f), new Vector2(-0.88f, 0.17f), new Vector2(-0.5f, 0.33f), new Vector2(0.1f, 0.37f), new Vector2(0.65f, 0.29f), new Vector2(1.05f, 0.16f), new Vector2(1.3f, 0.06f), new Vector2(1.37f, 0f) };
            for (int i = 0; i < prof.Length; i++) prof[i].x *= 0.9f;   // 縮短到車長 <= 2.4 m
            b.Lathe(new Vector3(0, by, 0), Quaternion.identity, prof, 12, S_BODY, 1f, 0.78f);                                           // 流線本體
            b.Ellipsoid(new Vector3(0, by, 1.23f), new Vector3(0.07f, 0.07f, 0.09f), 8, 4, S_WHITE);                                      // 白鼻尖
            b.Ellipsoid(new Vector3(0, 0.70f, -0.15f), new Vector3(0.26f, 0.07f, 0.3f), 10, 3, S_DARK);                                   // 座艙邊
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(0.28f * sx, 0.56f, -0.82f), new Vector3(0.2f, 0.012f, 0.14f), S_DARK, Q(0, 0, 8f * sx));              // 小尾翼
                b.Box(new Vector3(0.42f * sx, 0.36f, 0.95f), new Vector3(0.14f, 0.012f, 0.1f), S_ACC, Quaternion.identity);             // 前小翼
                b.Ellipsoid(new Vector3(0.17f * sx, by + 0.02f, 1.02f), new Vector3(0.06f, 0.05f, 0.04f), 6, 3, S_HEAD);               // 頭燈
                b.Box(new Vector3(0.14f * sx, by + 0.02f, -0.9f), new Vector3(0.05f, 0.03f, 0.012f), S_TAIL, Quaternion.identity);
                b.Cyl(new Vector3(0.14f * sx, by - 0.02f, -0.9f), 0.045f, 0.07f, 8, S_STEEL, Q(90f, 0, 0));
                Sticker(b, P.Number.ToString(), new Vector3(0.352f * sx, by, 0.12f), new Vector3(sx, 0, 0), Vector3.up, 0.13f);
            }
            b.Box(new Vector3(0, 0.80f, -0.8f), new Vector3(0.02f, 0.2f, 0.18f), S_DARK, Q(-14f, 0, 0));                                  // 垂直尾鰭
            P.ExhaustL = new Vector3(-0.14f, by - 0.02f, -0.95f); P.ExhaustR = new Vector3(0.14f, by - 0.02f, -0.95f);
            Driver(P, new Vector3(0, 0.6f, -0.15f), new Vector3(0, 0.86f, 0.2f), new Vector3(0, 0.45f, -0.9f), 1f, false);
        }

        // ---------------------------------------------------------------- Chonk:寬胖、大輪、大保險桿、冒煙排氣管
        static void Chonk(KartParts P)
        {
            P.Wr = 0.42f; P.Ww = 0.32f; P.Wx = 0.57f; P.WzF = 0.62f; P.WzR = -0.62f;
            P.Rim = new Color(1f, 0.8f, 0.3f);
            var b = P.Body; Func<float, float, float, Quaternion> Q = KartMeshBuilder.Rot;
            float by = 0.62f;
            b.RBox(new Vector3(0, by, 0), new Vector3(0.45f, 0.28f, 0.95f), 0.2f, S_BODY, Quaternion.identity, 1);                        // 胖車身
            b.RBox(new Vector3(0, by + 0.3f, 0.55f), new Vector3(0.38f, 0.08f, 0.38f), 0.08f, S_DARK, Quaternion.identity);               // 引擎蓋
            b.RBox(new Vector3(0, 1.0f, -0.62f), new Vector3(0.3f, 0.18f, 0.2f), 0.15f, S_DARK, Quaternion.identity);                      // 座椅
            b.RBox(new Vector3(0, 0.52f, 1.12f), new Vector3(0.5f, 0.1f, 0.07f), 0.07f, S_STEEL, Quaternion.identity);                     // 大前保險桿
            b.RBox(new Vector3(0, 0.5f, -1.02f), new Vector3(0.5f, 0.09f, 0.06f), 0.06f, S_DARK, Quaternion.identity);                     // 後保險桿
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Cyl(new Vector3(0.4f * sx, 1.32f, -0.35f), 0.035f, 0.42f, 6, S_STEEL);                                                    // 防滾架立柱
                b.Cyl(new Vector3(0.34f * sx, 1.22f, -0.86f), 0.07f, 0.33f, 8, S_STEEL);                                                    // 排氣管
                b.Ellipsoid(new Vector3(0.34f * sx, 1.7f, -0.86f), new Vector3(0.13f, 0.1f, 0.13f), 8, 3, S_WHITE);                         // 煙
                b.Ellipsoid(new Vector3(0.34f * sx, 1.93f, -0.9f), new Vector3(0.17f, 0.12f, 0.17f), 8, 3, S_WHITE);
                b.Ellipsoid(new Vector3(0.3f * sx, 0.72f, 0.95f), new Vector3(0.1f, 0.08f, 0.05f), 8, 3, S_HEAD);                           // 頭燈
                b.Box(new Vector3(0.3f * sx, 0.7f, -0.955f), new Vector3(0.09f, 0.05f, 0.015f), S_TAIL, Quaternion.identity);
                b.Quad(new Vector3(0.14f * sx, by + 0.38f + 0.004f, 0.55f), Vector3.up, Vector3.forward, 0.09f, 0.72f, S_ACC);               // 引擎蓋條紋
                Sticker(b, P.Number.ToString(), new Vector3(0.45f * sx, by, 0.05f), new Vector3(sx, 0, 0), Vector3.up, 0.2f);
            }
            b.Cyl(new Vector3(0, 1.74f, -0.35f), 0.035f, 0.4f, 6, S_STEEL, Q(0, 0, 90f));                                                    // 防滾架頂橫桿
            P.ExhaustL = new Vector3(-0.3f, 0.5f, -1.08f); P.ExhaustR = new Vector3(0.3f, 0.5f, -1.08f);
            Driver(P, new Vector3(0, 0.9f, -0.1f), new Vector3(0, 1.22f, 0.33f), new Vector3(0, 0.55f, -0.83f), 1.05f, false);
        }

        // ---------------------------------------------------------------- Robot9427:FRC 隊車(隊色保險桿、黑框架、swerve、電子板、天線、9427)
        static void Robot(KartParts P)
        {
            P.Wr = 0.26f; P.Ww = 0.2f; P.Wx = 0.5f; P.WzF = 0.6f; P.WzR = -0.6f;
            P.Rim = new Color(1f, 0.54f, 0.16f);
            var b = P.Body; Func<float, float, float, Quaternion> Q = KartMeshBuilder.Rot;
            float by = 0.34f;
            // 保險桿四條(隊色,圓角低矮)
            b.RBox(new Vector3(0, by, 0.88f), new Vector3(0.7f, 0.1f, 0.05f), 0.05f, S_BODY, Quaternion.identity);
            b.RBox(new Vector3(0, by, -0.88f), new Vector3(0.7f, 0.1f, 0.05f), 0.05f, S_BODY, Quaternion.identity);
            b.RBox(new Vector3(0.65f, by, 0), new Vector3(0.05f, 0.1f, 0.83f), 0.05f, S_BODY, Quaternion.identity);
            b.RBox(new Vector3(-0.65f, by, 0), new Vector3(0.05f, 0.1f, 0.83f), 0.05f, S_BODY, Quaternion.identity);
            // 9427 貼在保險桿四面
            b.Digits("9427", new Vector3(0, by, 0.936f), Vector3.forward, Vector3.up, 0.09f, S_WHITE);
            b.Digits("9427", new Vector3(0, by, -0.936f), Vector3.back, Vector3.up, 0.09f, S_WHITE);
            b.Digits("9427", new Vector3(0.706f, by, 0), Vector3.right, Vector3.up, 0.09f, S_WHITE);
            b.Digits("9427", new Vector3(-0.706f, by, 0), Vector3.left, Vector3.up, 0.09f, S_WHITE);
            // 黑色框架 + 電子板
            b.Box(new Vector3(0.45f, 0.27f, 0), new Vector3(0.04f, 0.04f, 0.8f), S_BLACK, Quaternion.identity);
            b.Box(new Vector3(-0.45f, 0.27f, 0), new Vector3(0.04f, 0.04f, 0.8f), S_BLACK, Quaternion.identity);
            b.Box(new Vector3(0, 0.27f, 0.7f), new Vector3(0.45f, 0.04f, 0.04f), S_BLACK, Quaternion.identity);
            b.Box(new Vector3(0, 0.27f, -0.7f), new Vector3(0.45f, 0.04f, 0.04f), S_BLACK, Quaternion.identity);
            b.Box(new Vector3(0, 0.325f, 0.05f), new Vector3(0.38f, 0.012f, 0.52f), S_WHITE, Quaternion.identity);                       // 電子面板
            b.Box(new Vector3(-0.17f, 0.37f, 0.3f), new Vector3(0.14f, 0.035f, 0.1f), S_STEEL, Quaternion.identity);                      // roboRIO
            b.Box(new Vector3(0.2f, 0.38f, -0.1f), new Vector3(0.1f, 0.05f, 0.08f), S_BLACK, Quaternion.identity);                       // 電池
            b.Box(new Vector3(0.2f, 0.37f, 0.3f), new Vector3(0.09f, 0.03f, 0.07f), S_ACC, Quaternion.identity);                          // 無線電
            b.Ellipsoid(new Vector3(-0.1f, 0.41f, 0.3f), new Vector3(0.025f, 0.02f, 0.025f), 6, 3, S_HEAD);                              // 狀態燈
            b.Cyl(new Vector3(0.42f, 0.55f, 0.55f), 0.014f, 0.24f, 6, S_BLACK);                                                          // 天線
            b.Ellipsoid(new Vector3(0.42f, 0.8f, 0.55f), new Vector3(0.03f, 0.03f, 0.03f), 6, 3, S_ACC);
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                {
                    float wz = sz > 0 ? P.WzF : P.WzR;
                    b.Box(new Vector3(sx * (P.Wx - P.Ww * 0.5f - 0.03f), 0.3f, wz), new Vector3(0.02f, 0.24f, 0.17f), S_STEEL, Quaternion.identity);   // swerve 側板
                    b.Box(new Vector3(sx * (P.Wx - 0.06f), 0.55f, wz), new Vector3(0.1f, 0.04f, 0.13f), S_STEEL, Quaternion.identity);                  // 馬達座
                    b.Cyl(new Vector3(sx * P.Wx, 0.62f, wz), 0.07f, 0.04f, 8, S_BLACK);                                                                   // 轉向齒輪
                }
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(0.3f * sx, 0.32f, 0.84f), new Vector3(0.09f, 0.03f, 0.015f), S_HEAD, Quaternion.identity);
                b.Box(new Vector3(0.3f * sx, 0.32f, -0.84f), new Vector3(0.09f, 0.03f, 0.015f), S_TAIL, Quaternion.identity);
            }
            P.ExhaustL = new Vector3(-0.3f, 0.34f, -1.0f); P.ExhaustR = new Vector3(0.3f, 0.34f, -1.0f);
            Driver(P, new Vector3(0, 0.38f, -0.25f), new Vector3(0, 0.66f, 0.18f), new Vector3(0, 0.5f, -0.86f), 1f, true);
        }

        // ================================================================= 組裝成 GameObject
        static Material SlotMat(int slot, KartParts P)
        {
            Color c = P.Main;
            switch (slot)
            {
                case S_BODY: return RaceShaders.Toon(c, 0.6f, 0.006f);
                case S_DARK: return RaceArt.Mat(Color.Lerp(c, Color.black, 0.45f), 0.5f);
                case S_WHITE: return RaceArt.Mat(new Color(0.96f, 0.96f, 0.98f), 0.4f);
                case S_BLACK: return RaceArt.Mat(new Color(0.10f, 0.10f, 0.12f), 0.25f);
                case S_STEEL: return RaceArt.Mat(new Color(0.55f, 0.58f, 0.64f), 0.7f);
                case S_ACC: return RaceArt.Mat(Color.Lerp(c, Color.white, 0.55f), 0.6f);
                case S_SKIN: return RaceArt.Mat(new Color(1f, 0.82f, 0.66f), 0.3f);
                case S_HEAD: return RaceArt.Glow(new Color(1f, 0.95f, 0.65f), 2f);
                case S_TAIL: return RaceArt.Glow(new Color(1f, 0.15f, 0.12f), 2f);
                case S_GLASS: return RaceArt.Mat(new Color(0.08f, 0.14f, 0.32f), 0.9f);
                default: return RaceArt.Mat(P.Rim, 0.5f);
            }
        }

        static GameObject Part(Transform parent, string name, KartMeshBuilder mb, KartParts P, Vector3 pos)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            if (mb == null) return go;
            if (!meshCache.TryGetValue(mb, out var kv) || kv.Key == null)
            {
                var m = mb.ToMesh("Kart" + P.Style + "_" + name, out int[] used);
                kv = new KeyValuePair<Mesh, int[]>(m, used); meshCache[mb] = kv;
            }
            go.AddComponent<MeshFilter>().sharedMesh = kv.Key;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[kv.Value.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = SlotMat(kv.Value[i], P);
            mr.sharedMaterials = mats; mr.receiveShadows = true;
            return go;
        }

        /// 造一台車(含車手)。回傳 "Visual" 根物件;腳底(輪胎底)y=0。number<=0 時由車色推導車號。
        public static GameObject Build(KartStyle style, Color color, int number = -1)
        {
            string key = style + "|" + ((Color32)color).r + "," + ((Color32)color).g + "," + ((Color32)color).b + "|" + number;
            if (!partsCache.TryGetValue(key, out var P))
            {
                P = BuildParts(style, color, number);
                P.WheelL = Wheel(P.Wr, P.Ww, true, 0f, style == KartStyle.Chonk ? 8 : 6, 12);
                P.WheelR = Wheel(P.Wr, P.Ww, false, 0f, style == KartStyle.Chonk ? 8 : 6, 12);
                partsCache[key] = P;
            }
            var root = new GameObject("Visual").transform;
            Part(root, "Body", P.Body, P, Vector3.zero);
            Part(root, "WheelFL", P.WheelL, P, new Vector3(-P.Wx, P.Wr, P.WzF));
            Part(root, "WheelFR", P.WheelR, P, new Vector3(P.Wx, P.Wr, P.WzF));
            Part(root, "WheelRL", P.WheelL, P, new Vector3(-P.Wx, P.Wr, P.WzR));
            Part(root, "WheelRR", P.WheelR, P, new Vector3(P.Wx, P.Wr, P.WzR));
            var eL = new GameObject("ExhaustL").transform; eL.SetParent(root, false); eL.localPosition = P.ExhaustL; eL.localRotation = Quaternion.Euler(0, 180f, 0);
            var eR = new GameObject("ExhaustR").transform; eR.SetParent(root, false); eR.localPosition = P.ExhaustR; eR.localRotation = Quaternion.Euler(0, 180f, 0);
            var drv = new GameObject("Driver").transform; drv.SetParent(root, false);
            var torso = Part(drv, "Torso", P.Torso, P, P.Hip);
            Part(torso.transform, "Head", P.Head, P, new Vector3(0, 0.36f, 0.02f));
            var sw = Part(drv, "SteeringWheel", P.Steer, P, P.SteerPos);
            sw.transform.localRotation = Quaternion.LookRotation(P.SteerDir.normalized, Vector3.up);
            return root.gameObject;
        }
    }

    // ===================================================================== 測試:-racekartmodeltest
    public class KartModelTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-racekartmodeltest") < 0) return;
            var g = new GameObject("KartModelTest");
            DontDestroyOnLoad(g);
            g.AddComponent<KartModelTest>();
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;
        void Report(string name, bool ok, string detail) { sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail); if (!ok) fails++; }

        IEnumerator Start()
        {
            yield return null; yield return null;
            var styles = (KartStyle[])Enum.GetValues(typeof(KartStyle));
            foreach (var st in styles)
                for (int ci = 0; ci < 2; ci++)
                {
                    string tag = "[" + st + " c" + ci + "] ";
                    GameObject go = null;
                    try { go = KartModels.Build(st, RacePalette.Kart[ci == 0 ? 0 : 4]); }
                    catch (Exception e) { Report(tag + "BUILD", false, "exception: " + e); continue; }
                    try { Check(tag, st, go.transform); }
                    catch (Exception e) { Report(tag + "CHECK", false, "exception: " + e); }
                    Destroy(go);
                }
            // AI 輪替保留
            bool ai = true;
            foreach (var pl in styles) for (int i = 0; i < 3; i++) if (RaceArt.AiStyle(i, pl) == pl) ai = false;
            Report("AISTYLE", ai, "AI style rotation never equals player style");
            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "racekartmodeltest.txt"), sb.ToString()); } catch (Exception e) { Debug.LogError(e); }
            Debug.Log(sb.ToString());
            Application.Quit(fails == 0 ? 0 : 1);
        }

        void Check(string tag, KartStyle st, Transform root)
        {
            foreach (var n in new[] { "Body", "WheelFL", "WheelFR", "WheelRL", "WheelRR", "ExhaustL", "ExhaustR", "Driver", "Driver/Torso/Head", "Driver/SteeringWheel" })
                Report(tag + "HIER " + n, root.Find(n) != null, "exists");

            int tris = 0, draws = 0; bool nan = false, outward = true;
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>();
                for (int i = 0; i < m.subMeshCount; i++) tris += (int)(m.GetIndexCount(i) / 3);
                draws += mr != null ? mr.sharedMaterials.Length : 0;
                var vs = m.vertices; var ns = m.normals;
                foreach (var v in vs) if (float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z)) nan = true;
                foreach (var v in ns) if (float.IsNaN(v.x + v.y + v.z)) nan = true;
                // 法線朝外比例(用重算法線 = 繞向的間接檢查;小件/極點附近不要求)
                if (mf.name == "Body" || mf.name.StartsWith("Wheel"))
                {
                    int pos = 0; Vector3 ctr = m.bounds.center;
                    for (int i = 0; i < vs.Length; i++) if (Vector3.Dot(ns[i], vs[i] - ctr) > 0f) pos++;
                    if (pos < vs.Length * 0.55f) outward = false;
                }
                var b = m.bounds;
                for (int cx = -1; cx <= 1; cx += 2) for (int cy = -1; cy <= 1; cy += 2) for (int cz = -1; cz <= 1; cz += 2)
                        {
                            Vector3 w = root.InverseTransformPoint(mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(cx, cy, cz))));
                            mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
                        }
            }
            Report(tag + "TRIS", tris <= 2500, tris + " triangles (limit 2500)");
            Report(tag + "NAN", !nan, "no NaN/Inf in vertices or normals");
            Report(tag + "NORMALS", outward, "normals point outward (Body/Wheels)");
            float len = mx.z - mn.z, wid = mx.x - mn.x;
            Report(tag + "LENGTH", len >= 1.6f && len <= 2.4f, len.ToString("F2") + " m (1.6..2.4)");
            Report(tag + "WIDTH", wid >= 1.0f && wid <= 1.5f, wid.ToString("F2") + " m (1.0..1.5)");
            foreach (var wn in new[] { "WheelFL", "WheelFR", "WheelRL", "WheelRR" })
            {
                var wt = root.Find(wn); var mesh = wt.GetComponent<MeshFilter>().sharedMesh;
                float bottom = wt.localPosition.y + mesh.bounds.min.y;
                Report(tag + "GROUND " + wn, Mathf.Abs(bottom) <= 0.03f, "tire bottom y=" + bottom.ToString("F3"));
            }
            Report(tag + "MINY", mn.y >= -0.03f && mn.y <= 0.03f, "lowest point y=" + mn.y.ToString("F3"));
            Report(tag + "DRAWS", draws <= 32, draws + " submesh draws per kart (info budget 32)");
            sb.AppendLine("INFO " + tag + "tris=" + tris + " draws=" + draws + " size=" + len.ToString("F2") + "x" + wid.ToString("F2") + " h=" + (mx.y - mn.y).ToString("F2"));
        }
    }
}
