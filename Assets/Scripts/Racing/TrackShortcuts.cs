using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 一條捷徑(模組 W,M5;見 02 §2.4)。從主線第 fromNode 個控制點分出去、接回第 toNode 個控制點的開放樣條,每 ~2 m 一個取樣點。
    ///
    /// 檢查點規則(RaceCheckpoints 實作):
    ///   - 主線上落在 (FromS, ToS) 之間的門 = 被跳過的門(Checkpoint.ShortcutId = Id,FirstSkipped..LastSkipped)。
    ///   - 車子下一個要過的門是 FirstSkipped 時,可以改穿過 Entry(入口門,在捷徑上離起點 entryDist m),
    ///     之後必須穿過 Exit(出口門,離終點 exitDist m),才把 FirstSkipped..LastSkipped 一次算過,NextCp 變成 LastSkipped + 1。
    ///     沒穿過入口門就直接切過草地 = 拿不到被跳過的門(下一個門過不去),所以抄草地近路不算數、也不會卡死(走主線就好)。
    ///   - 走捷徑中,名次用的弧長 s 以「捷徑走了幾成 = 主線 FromS..ToS 走了幾成」線性換算,Total 不會倒退。
    ///   - Entry / Exit 的 Index = -1(不屬於主線門的編號)。
    public sealed class ShortcutPath
    {
        public int Id;
        public float FromS, ToS;            // 在主線上的分岔 / 匯入位置(弧長)
        public float Width;                 // 路寬(中段)
        public float Length;                // 捷徑弧長
        public float Step;                  // 取樣間距(~2 m)
        public int FirstSkipped = -1, LastSkipped = -1;   // 被跳過的主線門索引(含);沒有 = -1
        public Checkpoint Entry, Exit;
        public float EntryDist, ExitDist;   // 入口門離起點、出口門離終點的弧長(入口要離主線夠遠,主線的車不會誤觸:Island GP 用 28 m)

        internal Vector3[] Pos;             // y 由地形生成後補上(RaceTrack.ApplyShortcutHeights)
        internal Vector3[] Tan, Right;
        internal float[] W;
        internal float[] Nominal;           // 地形刻路用的目標高度(RaceTerrain 設)

        public int Count { get { return Pos.Length; } }
        public float MainSpan { get { return ToS - FromS; } }

        static float CR(float p0, float p1, float p2, float p3, float t)
        {
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
        }

        internal static ShortcutPath Build(TrackDef def, TrackShortcut sd, int id, float fromS, float toS)
        {
            int n = def.nodes.Count;
            var ctrl = new List<TrackNode>();
            ctrl.Add(def.nodes[(sd.fromNode - 1 + n) % n]);
            ctrl.Add(def.nodes[sd.fromNode]);
            for (int i = 0; i < sd.nodes.Count; i++) ctrl.Add(sd.nodes[i]);
            ctrl.Add(def.nodes[sd.toNode]);
            ctrl.Add(def.nodes[(sd.toNode + 1) % n]);

            const int Sub = 24;
            int segs = ctrl.Count - 3;
            var px = new List<float>(); var pz = new List<float>();
            for (int i = 1; i <= segs; i++)
            {
                TrackNode p0 = ctrl[i - 1], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[i + 2];
                for (int k = 0; k < Sub; k++)
                {
                    float u = k / (float)Sub;
                    px.Add(CR(p0.x, p1.x, p2.x, p3.x, u)); pz.Add(CR(p0.z, p1.z, p2.z, p3.z, u));
                }
            }
            TrackNode last = ctrl[ctrl.Count - 2];
            px.Add(last.x); pz.Add(last.z);
            int m = px.Count;
            var cum = new float[m];
            for (int i = 1; i < m; i++) cum[i] = cum[i - 1] + Mathf.Sqrt((px[i] - px[i - 1]) * (px[i] - px[i - 1]) + (pz[i] - pz[i - 1]) * (pz[i] - pz[i - 1]));
            float len = cum[m - 1];

            var sc = new ShortcutPath { Id = id, FromS = fromS, ToS = toS, Width = sd.width, Length = len, EntryDist = sd.entryDist, ExitDist = sd.exitDist };
            int cnt = Mathf.Max(8, Mathf.RoundToInt(len / 2f) + 1);
            sc.Step = len / (cnt - 1);
            sc.Pos = new Vector3[cnt]; sc.Tan = new Vector3[cnt]; sc.Right = new Vector3[cnt]; sc.W = new float[cnt]; sc.Nominal = new float[cnt];
            float wFrom = def.nodes[sd.fromNode].width, wTo = def.nodes[sd.toNode].width;
            int j = 0;
            for (int i = 0; i < cnt; i++)
            {
                float d = i * sc.Step;
                while (j < m - 2 && cum[j + 1] <= d) j++;
                float seg = cum[j + 1] - cum[j];
                float f = seg > 1e-6f ? Mathf.Clamp01((d - cum[j]) / seg) : 0f;
                sc.Pos[i] = new Vector3(Mathf.Lerp(px[j], px[j + 1], f), 0f, Mathf.Lerp(pz[j], pz[j + 1], f));
                float a = Mathf.Clamp01(d / 30f), b = Mathf.Clamp01((len - d) / 30f);
                a = a * a * (3f - 2f * a); b = b * b * (3f - 2f * b);
                sc.W[i] = d < len * 0.5f ? Mathf.Lerp(wFrom, sd.width, a) : Mathf.Lerp(wTo, sd.width, b);
            }
            for (int i = 0; i < cnt; i++)
            {
                Vector3 a = sc.Pos[Mathf.Max(0, i - 1)], b = sc.Pos[Mathf.Min(cnt - 1, i + 1)];
                Vector3 t = b - a; t.y = 0f;
                if (t.sqrMagnitude < 1e-12f) t = Vector3.forward;
                t.Normalize();
                sc.Tan[i] = t; sc.Right[i] = new Vector3(t.z, 0f, -t.x);
            }
            sc.MakeGates();
            return sc;
        }

        /// (重新)建入口 / 出口門;地形高度更新後也要呼叫。
        internal void MakeGates()
        {
            Entry = MakeGate(EntryDist, -1);
            Exit = MakeGate(Length - ExitDist, -1);
            Entry.ShortcutId = Id; Exit.ShortcutId = Id;
            Entry.IsFinish = false; Exit.IsFinish = false;
        }

        Checkpoint MakeGate(float d, int index)
        {
            TrackSample sm = SampleAt(d), back = SampleAt(Mathf.Max(0f, d - 6f));
            return new Checkpoint
            {
                Index = index,
                S = d,
                Center = sm.Pos,
                Forward = sm.Tangent,
                HalfWidth = Width * 0.5f + 1.5f,
                Respawn = new Pose(back.Pos + Vector3.up * 0.3f, Quaternion.LookRotation(back.Tangent, Vector3.up)),
            };
        }

        internal void ApplyHeights(float[] ys)
        {
            for (int i = 0; i < Pos.Length; i++) Pos[i].y = ys[i];
            MakeGates();
        }

        /// 捷徑上弧長 d(0..Length)處的取樣。
        public TrackSample SampleAt(float d)
        {
            d = Mathf.Clamp(d, 0f, Length);
            float f = d / Step;
            int i = Mathf.Min((int)f, Pos.Length - 2);
            float u = Mathf.Clamp01(f - i);
            var r = new TrackSample();
            r.Pos = Vector3.Lerp(Pos[i], Pos[i + 1], u);
            r.Tangent = Vector3.Lerp(Tan[i], Tan[i + 1], u).normalized;
            r.Right = Vector3.Lerp(Right[i], Right[i + 1], u).normalized;
            r.Width = Mathf.Lerp(W[i], W[i + 1], u);
            r.S = d;
            return r;
        }

        /// 投影到捷徑中心線:回傳弧長 d,lateral 往右為正。
        public float Project(Vector3 p, out float lateral)
        {
            int ia; float u, hw;
            Nearest(p.x, p.z, out ia, out u, out hw);
            float d = (ia + u) * Step;
            Vector3 c = Vector3.Lerp(Pos[ia], Pos[ia + 1], u);
            Vector3 r = Vector3.Lerp(Right[ia], Right[ia + 1], u);
            lateral = (p.x - c.x) * r.x + (p.z - c.z) * r.z;
            return Mathf.Clamp(d, 0f, Length);
        }

        /// 離中心線(折線)最近的點:回傳水平距離;ia = 線段起點索引,u = 線段內比例,hw = 該處半路寬。不配置記憶體。
        internal float Nearest(float x, float z, out int ia, out float u, out float hw)
        {
            int n = Pos.Length;
            float bd = float.MaxValue; int bi = 0; float bu = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float ax = Pos[i].x, az = Pos[i].z;
                float abx = Pos[i + 1].x - ax, abz = Pos[i + 1].z - az;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 1e-9f ? Mathf.Clamp01(((x - ax) * abx + (z - az) * abz) / len2) : 0f;
                float ex = ax + abx * t - x, ez = az + abz * t - z;
                float d2 = ex * ex + ez * ez;
                if (d2 < bd) { bd = d2; bi = i; bu = t; }
            }
            ia = bi; u = bu;
            hw = Mathf.Lerp(W[bi], W[bi + 1], bu) * 0.5f;
            return Mathf.Sqrt(bd);
        }
    }

    /// 捷徑的路面網格(土路色、邊線、入口 / 出口門的標示柱)。不加碰撞體(地形碰撞體就是路面)。材質全部走 RaceArt.Mat 快取。
    internal static class TrackShortcutBuilder
    {
        const float YRoad = TrackMeshBuilder.YRoad, YLine = TrackMeshBuilder.YLine, YCurb = TrackMeshBuilder.YCurb;

        static Vector3 Pt(ShortcutPath sc, int i, float lateral, float y)
        {
            return new Vector3(sc.Pos[i].x + sc.Right[i].x * lateral, sc.Pos[i].y + y, sc.Pos[i].z + sc.Right[i].z * lateral);
        }

        /// 捷徑專用顏色(和主線柏油 / 一般土路都不同:橘褐色土路 + 黃色箭頭 + 深色告示牌),一眼看得出是另一條路。
        public static readonly Color SurfaceTint = new Color(0.86f, 0.52f, 0.20f);
        public static readonly Color ArrowYellow = new Color(1f, 0.82f, 0.10f);
        public static readonly Color SignBoard = new Color(0.10f, 0.11f, 0.16f);

        public static void Build(RaceTrack track, Transform parent, List<Mesh> meshes)
        {
            if (track.Shortcuts.Count == 0) return;
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
            var dirt = new RaceMeshAcc(); var arrow = new RaceMeshAcc(); var red = new RaceMeshAcc(); var cw = new RaceMeshAcc(); var board = new RaceMeshAcc();
            for (int k = 0; k < track.Shortcuts.Count; k++)
            {
                ShortcutPath sc = track.Shortcuts[k];
                int n = sc.Count;
                for (int i = 0; i + 1 < n; i++)
                {
                    float h0 = sc.W[i] * 0.5f, h1 = sc.W[i + 1] * 0.5f;
                    dirt.Quad(Pt(sc, i, -h0, YRoad), Pt(sc, i, h0, YRoad), Pt(sc, i + 1, h1, YRoad), Pt(sc, i + 1, -h1, YRoad), Vector3.up);
                    // 兩側路緣(每 4 m 紅白交替,細一點)
                    var curb = ((i / 2) % 2 == 0) ? red : cw;
                    curb.Quad(Pt(sc, i, h0, YCurb), Pt(sc, i, h0 + 0.7f, YCurb), Pt(sc, i + 1, h1 + 0.7f, YCurb), Pt(sc, i + 1, h1, YCurb), Vector3.up);
                    curb.Quad(Pt(sc, i, -h0 - 0.7f, YCurb), Pt(sc, i, -h0, YCurb), Pt(sc, i + 1, -h1, YCurb), Pt(sc, i + 1, -h1 - 0.7f, YCurb), Vector3.up);
                }
                // 路面上每 9 m 一個黃色人字箭頭(指向行進方向)
                for (float d = 12f; d <= sc.Length - 12f; d += 9f) Chevron(arrow, sc, d);
                // 入口 / 出口門:兩側各一根紅白柱 + 橫樑(只有外觀)
                BuildGate(sc.Entry, red, cw);
                BuildGate(sc.Exit, red, cw);
                // 主線上的告示牌:黃色大箭頭指向捷徑那一側
                BuildSign(track, sc, board, arrow);
            }
            var root = new GameObject("Shortcuts");
            root.transform.SetParent(parent, false);
            Add(root.transform, meshes, "ShortcutRoad", dirt, Color.Lerp(th.Dirt, SurfaceTint, 0.45f), 0.05f);
            Add(root.transform, meshes, "ShortcutArrows", arrow, ArrowYellow, 0.1f);
            Add(root.transform, meshes, "ShortcutSign", board, SignBoard, 0.1f);
            Add(root.transform, meshes, "ShortcutRed", red, RacePalette.CurbRed, 0.1f);
            Add(root.transform, meshes, "ShortcutWhite", cw, RacePalette.CurbWhite, 0.1f);
        }

        // 人字箭頭(兩片斜條,共 4 個三角形);尖端朝行進方向
        static void Chevron(RaceMeshAcc acc, ShortcutPath sc, float d)
        {
            TrackSample sm = sc.SampleAt(d);
            Vector3 t = sm.Tangent, r = sm.Right;
            float hw = Mathf.Min(1.7f, sm.Width * 0.3f);
            Vector3 c = sm.Pos + Vector3.up * (YLine + 0.01f);
            Vector3 tip = c + t * 1.4f, wl = c - t * 0.8f - r * hw, wr = c - t * 0.8f + r * hw;
            Vector3 b = -t * 0.85f;
            acc.Quad(wl, tip, tip + b, wl + b, Vector3.up);
            acc.Quad(tip, wr, wr + b, tip + b, Vector3.up);
        }

        // 告示牌:立在主線分岔點前 20 m、捷徑那一側的路肩外;板面朝來車,黃色大箭頭指向捷徑
        static void BuildSign(RaceTrack track, ShortcutPath sc, RaceMeshAcc board, RaceMeshAcc arrow)
        {
            TrackSample sm = track.SampleAt(sc.FromS - 20f);
            TrackSample far = sc.SampleAt(Mathf.Min(sc.Length, 30f));
            Vector3 off = far.Pos - track.SampleAt(sc.FromS).Pos;
            float side = Vector3.Dot(off, sm.Right) >= 0f ? 1f : -1f;
            Vector3 right = sm.Right, fwd = sm.Tangent, up = Vector3.up;
            Vector3 pos = sm.Pos + right * (side * (sm.Width * 0.5f + 4.5f));
            pos.y = sm.Pos.y;
            for (int i = -1; i <= 1; i += 2)
                board.Box(pos + right * (i * 1.5f) + up * 1.6f, right, up, fwd, new Vector3(0.12f, 1.6f, 0.12f));
            board.Box(pos + up * 4.0f, right, up, fwd, new Vector3(2.2f, 1.2f, 0.1f));
            // 箭頭畫在板面朝來車那一側(-fwd):座標 (u 沿 right * side 方向,v 向上)
            Vector3 face = pos + up * 4.0f - fwd * 0.13f;
            Vector3 n = -fwd, ru = right * side;
            Vector3 a0 = face - ru * 1.7f - up * 0.3f, a1 = face + ru * 0.4f - up * 0.3f, a2 = face + ru * 0.4f + up * 0.3f, a3 = face - ru * 1.7f + up * 0.3f;
            arrow.Quad(a0, a1, a2, a3, n);
            arrow.Tri(face + ru * 0.2f - up * 0.9f, face + ru * 1.8f, face + ru * 0.2f + up * 0.9f, n);
        }

        static void BuildGate(Checkpoint g, RaceMeshAcc red, RaceMeshAcc white)
        {
            Vector3 fwd = g.Forward, right = new Vector3(fwd.z, 0f, -fwd.x), up = Vector3.up;
            float lat = g.HalfWidth - 0.3f;
            for (int side = -1; side <= 1; side += 2)
                red.Box(g.Center + right * (side * lat) + up * 2f, right, up, fwd, new Vector3(0.25f, 2f, 0.25f));
            white.Box(g.Center + up * 4.1f, right, up, fwd, new Vector3(lat + 0.3f, 0.35f, 0.25f));
        }

        static void Add(Transform parent, List<Mesh> meshes, string name, RaceMeshAcc acc, Color color, float smooth)
        {
            if (acc.TriCount == 0) return;
            var gobj = new GameObject(name);
            gobj.transform.SetParent(parent, false);
            Mesh m = acc.ToMesh(name);
            meshes.Add(m);
            gobj.AddComponent<MeshFilter>().sharedMesh = m;
            var r = gobj.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceArt.Mat(color, smooth);
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
