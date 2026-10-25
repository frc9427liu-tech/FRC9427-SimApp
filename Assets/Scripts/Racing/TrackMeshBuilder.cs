using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 簡單的網格累加器(模組 W 內部使用):每個面獨立頂點 → 平面著色;法線由呼叫者指定,繞向自動修正。
    internal sealed class RaceMeshAcc
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<int> T = new List<int>();

        /// 非 null 時每個頂點帶 uv(路面 shader 用:x = 橫向 -1..1、y = 半路寬)。
        public List<Vector2> UV;

        /// 非 null 時每個頂點帶 uv1(Vector3):路面導引用 = (局部曲率 k, 前方彎道曲率 k, 沿路弧長 s)。
        public List<Vector3> UV2;

        public int TriCount { get { return T.Count / 3; } }

        public void TriUv(Vector3 a, Vector3 b, Vector3 c, Vector3 nrm, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = c; c = t; Vector2 tu = ub; ub = uc; uc = tu; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            UV.Add(ua); UV.Add(ub); UV.Add(uc);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        void TriUvG(Vector3 a, Vector3 b, Vector3 c, Vector3 nrm, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 ga, Vector3 gb, Vector3 gc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = c; c = t; Vector2 tu = ub; ub = uc; uc = tu; Vector3 tg = gb; gb = gc; gc = tg; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            UV.Add(ua); UV.Add(ub); UV.Add(uc);
            UV2.Add(ga); UV2.Add(gb); UV2.Add(gc);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public void QuadUvG(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 ga, Vector3 gb, Vector3 gc, Vector3 gd)
        {
            TriUvG(a, b, c, nrm, ua, ub, uc, ga, gb, gc);
            TriUvG(a, c, d, nrm, ua, uc, ud, ga, gc, gd);
        }

        public void QuadUv(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            TriUv(a, b, c, nrm, ua, ub, uc);
            TriUv(a, c, d, nrm, ua, uc, ud);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 nrm)
        {
            // Unity 左手座標:Cross(b-a, c-a) 與期望法線同向 = 從法線那一側看是正面
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = c; c = t; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            if (UV != null) { UV.Add(Vector2.zero); UV.Add(Vector2.zero); UV.Add(Vector2.zero); }
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// a,b,c,d 依周長順序排列(同一平面)。
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
        {
            Tri(a, b, c, nrm);
            Tri(a, c, d, nrm);
        }

        /// 任意朝向的長方體;right/up/fwd 為單位向量,half 為各軸半長。
        public void Box(Vector3 center, Vector3 right, Vector3 up, Vector3 fwd, Vector3 half)
        {
            Vector3 x = right * half.x, y = up * half.y, z = fwd * half.z;
            Face(center + x, y, z, right);
            Face(center - x, y, z, -right);
            Face(center + y, x, z, up);
            Face(center - y, x, z, -up);
            Face(center + z, x, y, fwd);
            Face(center - z, x, y, -fwd);
        }

        void Face(Vector3 c, Vector3 u, Vector3 v, Vector3 nrm)
        {
            Quad(c - u - v, c + u - v, c + u + v, c - u + v, nrm);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            if (UV != null && UV.Count == V.Count) m.SetUVs(0, UV);
            if (UV2 != null && UV2.Count == V.Count) m.SetUVs(1, UV2);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// 沿樣條生成路面帶、路肩、路緣、標線、轉彎外側護欄、起終點拱門、檢查點小標記。
    /// 全部依「材質」合併成少量網格(約 8 個),低多邊形、平面著色。護欄只有 1 個 MeshCollider,拱門 2 個 BoxCollider。
    public static class TrackMeshBuilder
    {
        // 離地高度(相對中心線取樣點 y;地形比取樣點低約 2.5 cm,疊一點點避免 z-fighting)
        // M5:格距從 2 m 放大到 4.5 m,地形三角形內插誤差變大,再整體抬高 3 cm(RoadLift 給加速板等貼路面的東西參考)
        internal const float YShoulder = 0.07f, YRoad = 0.08f, YLine = 0.09f, YCurb = 0.10f, YCheck = 0.095f;
        const float CurbW = 1.0f, ShoulderW = 2.5f;
        const float MergeK = 1f / 60f;
        const float RailGap = 3.8f, RailThick = 0.4f, RailH = 1.0f;   // 護欄內緣離路緣的距離(從中心線算 hw + RailGap)
        const float RailCurvThr = 1f / 60f;                             // 半徑小於 60 m 的彎才加護欄
        const float RumbleCurvThr = 1f / 95f;                           // 半徑小於 95 m 的彎,外側路緣外加一圈紅白跳動條(rumble strip,純外觀貼面)
        const float RailRoof = 0.16f;                                   // 護欄頂的屋脊高度(外觀圓頂;碰撞體不含)

        static readonly Color DarkLine = new Color(0.10f, 0.10f, 0.12f);

        static Vector3 Pt(TrackSample s, float lateral, float y)
        {
            // M8:側傾(Bank 度,正 = 右彎、外側 = 左側抬高):橫向位置 lateral 的高度 = 中心高 - lateral * tan(Bank)
            float bk = s.Bank != 0f ? -lateral * Mathf.Tan(s.Bank * Mathf.Deg2Rad) : 0f;
            return new Vector3(s.Pos.x + s.Right.x * lateral, s.Pos.y + y + bk, s.Pos.z + s.Right.z * lateral);
        }

        /// 兩個相鄰取樣點之間、橫向範圍 [fa*hw+ea, fb*hw+eb] 的一個四邊形。
        static void StripQuad(RaceMeshAcc acc, TrackSample s0, TrackSample s1, float fa, float ea, float fb, float eb, float y)
        {
            float hw0 = s0.Width * 0.5f, hw1 = s1.Width * 0.5f;
            acc.Quad(Pt(s0, hw0 * fa + ea, y), Pt(s0, hw0 * fb + eb, y), Pt(s1, hw1 * fb + eb, y), Pt(s1, hw1 * fa + ea, y), Vector3.up);
        }

        /// 兩個相鄰取樣點之間、沿路方向 t0..t1(0..1)、橫向 hw*side + [e0, e1](往 side 方向外推)的一個四邊形。
        static void SubQuad(RaceMeshAcc acc, TrackSample s0, TrackSample s1, float t0, float t1, int side, float e0, float e1, float y)
        {
            float hw0 = s0.Width * 0.5f, hw1 = s1.Width * 0.5f;
            Vector3 a0 = Pt(s0, side * (hw0 + e0), y), a1 = Pt(s0, side * (hw0 + e1), y), b0 = Pt(s1, side * (hw1 + e0), y), b1 = Pt(s1, side * (hw1 + e1), y);
            acc.Quad(Vector3.Lerp(a0, b0, t0), Vector3.Lerp(a1, b1, t0), Vector3.Lerp(a1, b1, t1), Vector3.Lerp(a0, b0, t1), Vector3.up);
        }

        /// 生成賽道所有網格,回傳 "Track" 根物件。meshes 會收集所有新建的 Mesh(RaceWorld.Destroy 用來釋放)。
        public static GameObject Build(RaceTrack track, Transform parent, List<Mesh> meshes)
        {
            var root = new GameObject("Track");
            if (parent != null) root.transform.SetParent(parent, false);

            TrackSample[] sm = track.Raw;
            int n = sm.Length;
            var road = new RaceMeshAcc { UV = new List<Vector2>(), UV2 = new List<Vector3>() }; var shoulder = new RaceMeshAcc(); var white = new RaceMeshAcc(); var dark = new RaceMeshAcc();
            var red = new RaceMeshAcc(); var curbWhite = new RaceMeshAcc(); var marks = new RaceMeshAcc(); var railCol = new RaceMeshAcc();

            float[] kLoc, kAhd;
            GuideData(track, sm, out kLoc, out kAhd);
            float stepLen = track.SampleStep;

            for (int i = 0; i < n; i++)
            {
                int ai = i, bi = (i + 1) % n;
                TrackSample a = sm[i], b = sm[(i + 1) % n];
                // M7 三角形預算:長賽道(3~4 km)的路面網格很大。偶數索引開頭、這 2 格的曲率都小於 1/60(半徑 > 60 m,4 m 弦的誤差 < 3.5 cm)時,
                // 用「一個 4 m 四邊形」取代「兩個 2 m 四邊形」(紅白路緣 / 虛線的條紋本來就是每 2 格換一次,視覺完全一樣);直線與大彎省下近一半三角形。
                if ((i & 1) == 0 && i + 2 <= n && Mathf.Abs(a.Curvature) < MergeK && Mathf.Abs(b.Curvature) < MergeK && Mathf.Abs(sm[(i + 2) % n].Curvature) < MergeK)
                {
                    bi = (i + 2) % n;
                    b = sm[bi];
                    i++;   // 吃掉下一格
                }
                // 路面 + 路肩(兩側)
                {
                    float hwa = a.Width * 0.5f, hwb = b.Width * 0.5f;
                    float sA = a.S, sB = a.S + ((bi - ai + n) % n) * stepLen;
                    Vector3 gA = new Vector3(kLoc[ai], kAhd[ai], sA), gB = new Vector3(kLoc[bi], kAhd[bi], sB);
                    road.QuadUvG(Pt(a, -hwa, YRoad), Pt(a, hwa, YRoad), Pt(b, hwb, YRoad), Pt(b, -hwb, YRoad), Vector3.up,
                        new Vector2(-1f, hwa), new Vector2(1f, hwa), new Vector2(1f, hwb), new Vector2(-1f, hwb), gA, gA, gB, gB);
                }
                StripQuad(shoulder, a, b, 1f, CurbW, 1f, CurbW + ShoulderW, YShoulder);
                StripQuad(shoulder, a, b, -1f, -(CurbW + ShoulderW), -1f, -CurbW, YShoulder);
                // 路緣:每 4 m(2 個取樣)紅白交替
                var curb = ((i / 2) % 2 == 0) ? red : curbWhite;
                StripQuad(curb, a, b, 1f, 0f, 1f, CurbW, YCurb);
                StripQuad(curb, a, b, -1f, -CurbW, -1f, 0f, YCurb);
                // 邊線(連續)與中線(虛線:4 m 畫、4 m 空)
                StripQuad(white, a, b, 1f, -0.9f, 1f, -0.6f, YLine);
                StripQuad(white, a, b, -1f, 0.6f, -1f, 0.9f, YLine);
                if ((i / 2) % 2 == 0) StripQuad(white, a, b, 0f, -0.15f, 0f, 0.15f, YLine);
                // 彎道外側的 rumble strip:路緣外一條 1.2 m 寬的紅白短條(每 1 m 換色),把彎道外緣畫得更醒目
                float kv = a.Curvature;
                if (Mathf.Abs(kv) > RumbleCurvThr)
                {
                    int outer = kv > 0f ? -1 : 1;   // 右彎(κ>0)外側在左
                    for (int h = 0; h < 2; h++)
                        SubQuad(((i * 2 + h) % 2 == 0) ? red : curbWhite, a, b, h * 0.5f, (h + 1) * 0.5f, outer, CurbW + 0.05f, CurbW + 1.25f, YCurb + 0.004f);
                }
            }

            BuildRails(track, sm, red, curbWhite, railCol);
            BuildFinish(track, white, dark);
            BuildGridMarks(track, white);
            BuildArch(track, red, curbWhite, dark, root.transform, meshes);
            BuildCheckpointMarks(track, marks);

            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
            Material roadMat = RaceShaders.Road(th.Asphalt, 0.15f, 0.14f);
            if (roadMat != null && roadMat.HasProperty("_Guide")) roadMat.SetColor("_Guide", GuideColor(track.Theme));
            AddPart(root.transform, meshes, "Road", road, th.Asphalt, 0.05f, null, roadMat);
            AddPart(root.transform, meshes, "Shoulder", shoulder, th.Sand, 0.05f, null, RaceShaders.Road(th.Sand, 0f, 0f, 0.09f));
            AddPart(root.transform, meshes, "Lines", white, RacePalette.Line, 0.1f, null);
            AddPart(root.transform, meshes, "FinishDark", dark, DarkLine, 0.1f, null);
            AddPart(root.transform, meshes, "RedParts", red, RacePalette.CurbRed, 0.1f, null);
            AddPart(root.transform, meshes, "WhiteParts", curbWhite, RacePalette.CurbWhite, 0.1f, null);
            AddPart(root.transform, meshes, "CheckpointMarks", marks, th.Rock, 0.1f, null);
            if (railCol.TriCount > 0)
            {
                // 護欄碰撞:只有 1 個 MeshCollider,沒有 Renderer(外觀已經在 RedParts / WhiteParts)
                var g = new GameObject("RailCollider");
                g.transform.SetParent(root.transform, false);
                Mesh m = railCol.ToMesh("RailCollider");
                meshes.Add(m);
                g.AddComponent<MeshCollider>().sharedMesh = m;
            }
            return root;
        }

        // ------------------------------------------------------------------ 路面導引(彎道外側彩色邊線 + 彎前箭頭,shader 畫,零額外三角形)

        static readonly string[] GuideHex = { "#FFC93C", "#FF7A2A", "#37E0D0", "#FF5FA2", "#2FD9B8", "#FF4D6D", "#FFE14D", "#3DE0FF" };

        /// 各主題的導引強調色(都要在深色柏油上夠亮)。
        public static Color GuideColor(int theme)
        {
            Color c;
            ColorUtility.TryParseHtmlString(GuideHex[Mathf.Clamp(theme, 0, GuideHex.Length - 1)], out c);
            return c;
        }

        /// 每個取樣點的導引資料:kLoc = 局部曲率(+-3 格平均,彎道外側邊線用)、kAhd = 前方 8~60 m 內最緊的彎(只有緊彎,且自己不在彎裡,箭頭用)。
        /// 起終點附近與缺口附近清零。
        internal static void GuideData(RaceTrack track, TrackSample[] sm, out float[] kLoc, out float[] kAhd)
        {
            int n = sm.Length;
            kLoc = new float[n]; kAhd = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int d = -3; d <= 3; d++) sum += sm[((i + d) % n + n) % n].Curvature;
                kLoc[i] = sum / 7f;
            }
            float step = track.SampleStep;
            int j0 = Mathf.CeilToInt(8f / step), j1 = Mathf.CeilToInt(60f / step);
            for (int i = 0; i < n; i++)
            {
                float best = 0f;
                for (int j = j0; j <= j1; j++)
                {
                    float k = kLoc[(i + j) % n];
                    if (Mathf.Abs(k) > Mathf.Abs(best)) best = k;
                }
                kAhd[i] = (Mathf.Abs(best) > 1f / 80f && Mathf.Abs(kLoc[i]) < 1f / 200f) ? best : 0f;
                float s = sm[i].S;
                if (s < 30f || s > track.Length - 40f || track.InGap(s, 50f, 40f)) { kAhd[i] = 0f; kLoc[i] = 0f; }
            }
        }

        static void AddPart(Transform parent, List<Mesh> meshes, string name, RaceMeshAcc acc, Color color, float smooth, string unused, Material mat = null)
        {
            if (acc.TriCount == 0) return;
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            Mesh m = acc.ToMesh(name);
            meshes.Add(m);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat != null ? mat : RaceArt.Mat(color, smooth);
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ 護欄

        static void BuildRails(RaceTrack track, TrackSample[] sm, RaceMeshAcc red, RaceMeshAcc white, RaceMeshAcc col)
        {
            int n = sm.Length;
            // side: +1 = 右側有護欄,-1 = 左側,0 = 沒有。右彎(κ>0)的外側是左邊。
            var side = new int[n];
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(sm[i].Curvature) > RailCurvThr) side[i] = sm[i].Curvature > 0f ? -1 : 1;
            var ext = (int[])side.Clone();
            for (int i = 0; i < n; i++)
            {
                if (side[i] == 0) continue;
                for (int k = -4; k <= 4; k++)
                {
                    int j = ((i + k) % n + n) % n;
                    if (side[j] == 0 && ext[j] == 0) ext[j] = side[i];
                }
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (ext[i] == 0 || ext[i] != ext[j]) continue;
                int sd = ext[i];
                if (NearShortcut(track, Pt(sm[i], sd * (sm[i].Width * 0.5f + RailGap + RailThick * 0.5f), 0f), 6f)) continue;   // 捷徑穿過護欄的地方留缺口
                bool startCap = ext[(i - 1 + n) % n] != sd;
                bool endCap = ext[(j + 1) % n] != sd;
                bool redSeg = (i / 2) % 2 == 0;
                var vis = redSeg ? red : white;
                RailSegment(vis, sm[i], sm[j], sd, startCap, endCap, redSeg ? white : red);   // 外觀:屋脊頂(換另一種顏色,像瑪利歐賽車的圓頂護欄)
                RailSegment(col, sm[i], sm[j], sd, startCap, endCap, null);                   // 碰撞:原本的方塊,不變
            }
        }

        /// 點 p(xz)離任何一條捷徑的中心線 < r 公尺。
        static bool NearShortcut(RaceTrack track, Vector3 p, float r)
        {
            var list = track.Shortcuts;
            for (int k = 0; k < list.Count; k++)
            {
                ShortcutPath sc = list[k];
                for (int i = 0; i < sc.Count; i++)
                {
                    float dx = sc.Pos[i].x - p.x, dz = sc.Pos[i].z - p.z;
                    if (dx * dx + dz * dz < r * r) return true;
                }
            }
            return false;
        }

        static void RailSegment(RaceMeshAcc acc, TrackSample a, TrackSample b, int sd, bool startCap, bool endCap, RaceMeshAcc roof)
        {
            float lin0 = sd * (a.Width * 0.5f + RailGap), lout0 = sd * (a.Width * 0.5f + RailGap + RailThick);
            float lin1 = sd * (b.Width * 0.5f + RailGap), lout1 = sd * (b.Width * 0.5f + RailGap + RailThick);
            Vector3 inA0 = Pt(a, lin0, 0f), inA1 = Pt(a, lin0, RailH), outA0 = Pt(a, lout0, 0f), outA1 = Pt(a, lout0, RailH);
            Vector3 inB0 = Pt(b, lin1, 0f), inB1 = Pt(b, lin1, RailH), outB0 = Pt(b, lout1, 0f), outB1 = Pt(b, lout1, RailH);
            Vector3 toRoad = -sd * ((a.Right + b.Right).normalized);   // 內側面朝向賽道
            acc.Quad(inA0, inA1, inB1, inB0, toRoad);
            acc.Quad(outA0, outA1, outB1, outB0, -toRoad);
            if (roof == null) acc.Quad(inA1, outA1, outB1, inB1, Vector3.up);
            else
            {
                // 屋脊:中線往上 RailRoof,兩側斜面(法線微微朝外上方,看起來圓潤)
                Vector3 up = Vector3.up * RailRoof;
                Vector3 midA = (inA1 + outA1) * 0.5f + up, midB = (inB1 + outB1) * 0.5f + up;
                roof.Quad(inA1, midA, midB, inB1, (Vector3.up + toRoad * 0.7f).normalized);
                roof.Quad(midA, outA1, outB1, midB, (Vector3.up - toRoad * 0.7f).normalized);
            }
            if (startCap) acc.Quad(inA0, inA1, outA1, outA0, -a.Tangent);
            if (endCap) acc.Quad(inB0, inB1, outB1, outB0, b.Tangent);
        }

        // ------------------------------------------------------------------ 終點線、起跑格、拱門、檢查點標記

        static void BuildFinish(RaceTrack track, RaceMeshAcc white, RaceMeshAcc dark)
        {
            const int Cols = 12;
            for (int r = 0; r < 2; r++)
            {
                TrackSample s0 = track.SampleAt(r * 1f), s1 = track.SampleAt((r + 1) * 1f);
                float hw0 = s0.Width * 0.5f, hw1 = s1.Width * 0.5f;
                for (int c = 0; c < Cols; c++)
                {
                    float fa = -1f + 2f * c / Cols, fb = -1f + 2f * (c + 1) / Cols;
                    var acc = ((r + c) % 2 == 0) ? white : dark;
                    acc.Quad(Pt(s0, hw0 * fa, YCheck), Pt(s0, hw0 * fb, YCheck), Pt(s1, hw1 * fb, YCheck), Pt(s1, hw1 * fa, YCheck), Vector3.up);
                }
            }
        }

        static void BuildGridMarks(RaceTrack track, RaceMeshAcc white)
        {
            for (int slot = 0; slot < 8; slot++)
            {
                float s = -(6f + slot * 4f) + 2.0f;     // 車頭前方一點的橫線
                float lat = (slot % 2 == 0) ? -2.6f : 2.6f;
                TrackSample a = track.SampleAt(s), b = track.SampleAt(s + 0.25f);
                white.Quad(Pt(a, lat - 1.1f, YLine), Pt(a, lat + 1.1f, YLine), Pt(b, lat + 1.1f, YLine), Pt(b, lat - 1.1f, YLine), Vector3.up);
            }
        }

        static void BuildArch(RaceTrack track, RaceMeshAcc red, RaceMeshAcc white, RaceMeshAcc dark, Transform parent, List<Mesh> meshes)
        {
            TrackSample s = track.SampleAt(0f);
            float hw = s.Width * 0.5f;
            float postLat = hw + 2.0f, postH = 8f;
            Vector3 up = Vector3.up, right = s.Right, fwd = s.Tangent;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 c = s.Pos + right * (side * postLat) + up * (postH * 0.5f);
                // 外觀:紅白相間的糖果柱(8 節)+ 白色底座;碰撞體維持原本 1 x 8 x 1 的方柱
                for (int k = 0; k < 8; k++)
                    ((k & 1) == 0 ? red : white).Box(s.Pos + right * (side * postLat) + up * (k + 0.5f), right, up, fwd, new Vector3(0.5f, 0.5f, 0.5f));
                white.Box(s.Pos + right * (side * postLat) + up * 0.3f, right, up, fwd, new Vector3(0.85f, 0.3f, 0.85f));
                var g = new GameObject(side < 0 ? "ArchPostL" : "ArchPostR");
                g.transform.SetParent(parent, false);
                g.transform.position = c;
                g.transform.rotation = Quaternion.LookRotation(fwd, up);
                g.AddComponent<BoxCollider>().size = new Vector3(1f, postH, 1f);
            }
            float beamHalf = postLat + 0.5f;
            white.Box(s.Pos + up * (postH - 0.3f), right, up, fwd, new Vector3(beamHalf, 0.7f, 0.5f));
            red.Box(s.Pos + up * (postH + 0.6f), right, up, fwd, new Vector3(beamHalf, 0.2f, 0.55f));
            // 橫樑兩面的黑白格(2 列),從前後都看得到「起終點」
            float cell = 0.7f;
            int cols = Mathf.Max(4, Mathf.RoundToInt(beamHalf * 2f / cell));
            float cw = beamHalf * 2f / cols;
            for (int face = -1; face <= 1; face += 2)
                for (int r = 0; r < 2; r++)
                    for (int cI = 0; cI < cols; cI++)
                    {
                        if (((r + cI) & 1) != 0) continue;
                        float x0 = -beamHalf + cI * cw, x1 = x0 + cw;
                        float y0 = postH - 1.0f + r * cell, y1 = y0 + cell;
                        Vector3 zOff = fwd * (face * 0.505f);
                        dark.Quad(s.Pos + right * x0 + up * y0 + zOff, s.Pos + right * x1 + up * y0 + zOff, s.Pos + right * x1 + up * y1 + zOff, s.Pos + right * x0 + up * y1 + zOff, fwd * face);
                    }
        }

        static void BuildCheckpointMarks(RaceTrack track, RaceMeshAcc marks)
        {
            // 每個門(不含終點線)在路外 2.2 m 處各放一根低矮小柱,顏色接近岩石灰,不搶眼
            for (int i = 1; i < track.CheckpointCount; i++)
            {
                Checkpoint cp = track.Checkpoints[i];
                Vector3 right = new Vector3(cp.Forward.z, 0f, -cp.Forward.x);
                float lat = cp.HalfWidth - 4f + 2.2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 c = cp.Center + right * (side * lat) + Vector3.up * 0.5f;
                    marks.Box(c, right, Vector3.up, cp.Forward, new Vector3(0.15f, 0.5f, 0.15f));
                }
            }
        }
    }
}
