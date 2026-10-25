using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 路面上的跳台 / 加速板(模組 W,M6)。把 TrackDef.features 的「弧長 + 橫向位移」解成世界座標的矩形(平面上直的,方向 = 中心點的切線)。
    /// 純資料 + 數學,不配置記憶體,-batchmode -nographics 可以直接查。
    ///   跳台:footprint 內地面 = 地形高度 + tan(pitch) * (du + 半長),實體由 TrackFeatureBuilder 的 MeshCollider(RaceSurface = Ramp)提供。
    ///   加速板:沒有碰撞體(不擋車),SurfaceAt 在矩形內回 BoostPad,K 的探地會用 GroundSampler 的材質(碰撞體上沒有 RaceSurface 時)。
    internal sealed class FeatureSet
    {
        public struct Inst
        {
            public TrackFeatureKind Kind;
            public float S;
            public int Path;           // -1 = 主線;>= 0 = 捷徑編號(S 是該捷徑上的弧長)
            public float Cx, Cz;       // 中心(世界 xz)
            public float Fx, Fz;       // 前進方向(單位向量)
            public float Rx, Rz;       // 右方向(單位向量)
            public float HL, HW;       // 半長、半寬
            public float TanP;         // 跳台斜率 tan(pitch)
            public float PitchDeg;
            public float R2;           // 外接圓半徑平方(快速排除)
            public bool GapRamp;       // M8:飛越缺口的起飛跳台(固定出場速度 + 較輕的滑翔重力;手工賽道的一般滑翔跳台 = false)
            /// 跳台(含滑翔跳台)= 有斜面碰撞體的物件;加速板 = false。
            public bool IsRamp { get { return Kind != TrackFeatureKind.Boost; } }
            public bool IsGlider { get { return Kind == TrackFeatureKind.GliderRamp; } }
        }

        public readonly List<Inst> Items = new List<Inst>();
        public int Count { get { return Items.Count; } }

        public static FeatureSet Resolve(RaceTrack tr)
        {
            var fs = new FeatureSet();
            var list = tr.Features;
            for (int i = 0; i < list.Count; i++)
            {
                TrackFeature f = list[i];
                bool onSc = f.path >= 0 && f.path < tr.Shortcuts.Count;
                TrackSample sm = onSc ? tr.Shortcuts[f.path].SampleAt(f.s) : tr.SampleAt(f.s);
                var it = new Inst();
                it.Kind = (TrackFeatureKind)f.kind;
                it.Path = onSc ? f.path : -1;
                it.S = onSc ? f.s : tr.Wrap(f.s);
                it.Cx = sm.Pos.x + sm.Right.x * f.lateral; it.Cz = sm.Pos.z + sm.Right.z * f.lateral;
                Vector3 t = sm.Tangent; t.y = 0f; t.Normalize();
                it.Fx = t.x; it.Fz = t.z;
                it.Rx = t.z; it.Rz = -t.x;
                it.HL = f.length * 0.5f; it.HW = f.width * 0.5f;
                it.PitchDeg = f.pitch;
                it.TanP = it.Kind != TrackFeatureKind.Boost ? Mathf.Tan(f.pitch * Mathf.Deg2Rad) : 0f;
                it.R2 = it.HL * it.HL + it.HW * it.HW;
                if (it.Kind == TrackFeatureKind.GliderRamp && it.Path < 0)
                    for (int gq = 0; gq < tr.Gaps.Count; gq++)
                    {
                        float dl = it.S + it.HL - tr.Gaps[gq].S0; dl = (dl + tr.Length * 1.5f) % tr.Length - tr.Length * 0.5f;
                        if (Mathf.Abs(dl) < 2f) it.GapRamp = true;
                    }
                fs.Items.Add(it);
            }
            return fs;
        }

        /// 世界座標 → 該物件的本地座標(du 沿前進方向、dv 往右)。回傳是否在矩形內(margin 向外擴張)。
        public static bool Local(in Inst f, float x, float z, float margin, out float du, out float dv)
        {
            float dx = x - f.Cx, dz = z - f.Cz;
            du = dx * f.Fx + dz * f.Fz;
            dv = dx * f.Rx + dz * f.Rz;
            return du >= -f.HL - margin && du <= f.HL + margin && dv >= -f.HW - margin && dv <= f.HW + margin;
        }

        /// 這一點落在哪一個物件內;回傳索引,沒有 = -1。
        public int IndexAt(float x, float z, float margin = 0f)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Inst f = Items[i];
                float dx = x - f.Cx, dz = z - f.Cz;
                float rr = f.R2 + margin * (2f * Mathf.Sqrt(f.R2) + margin);
                if (dx * dx + dz * dz > rr) continue;
                float du, dv;
                if (Local(f, x, z, margin, out du, out dv)) return i;
            }
            return -1;
        }

        /// 地面材質覆寫:在跳台內 = Ramp,在加速板內 = BoostPad。
        public bool TrySurface(float x, float z, out Surface s)
        {
            int i = IndexAt(x, z, 0f);
            if (i < 0) { s = Surface.Road; return false; }
            s = Items[i].Kind != TrackFeatureKind.Boost ? Surface.Ramp : Surface.BoostPad;
            return true;
        }

        /// 跳台在這一點比地形高多少(m);不在任何跳台內回 false。normalTilt = 斜面讓法線往後傾的水平量(= -tan * 前進方向)。
        public bool RampOffset(float x, float z, out float offset, out Vector3 tilt)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Inst f = Items[i];
                if (f.Kind == TrackFeatureKind.Boost) continue;
                float dx = x - f.Cx, dz = z - f.Cz;
                if (dx * dx + dz * dz > f.R2) continue;
                float du, dv;
                if (!Local(f, x, z, 0f, out du, out dv)) continue;
                offset = f.TanP * (du + f.HL);
                tilt = new Vector3(-f.TanP * f.Fx, 0f, -f.TanP * f.Fz);
                return true;
            }
            offset = 0f; tilt = Vector3.zero;
            return false;
        }

        /// 滑翔跳台的前緣(跳台頂端那條邊)資料,給整合者與測試用。Height = 前緣離地形的高度差 m。
        public int GliderCount
        {
            get { int n = 0; for (int i = 0; i < Items.Count; i++) if (Items[i].IsGlider && Items[i].Path < 0) n++; return n; }
        }

        /// 車這一步從 prev 移到 pos:有沒有越過某個滑翔跳台的前緣(從後面進到前面,橫向在跳台寬度內,而且上一步還在跳台上或離前緣 8 m 內)。
        /// 回傳跳台在 Items 的索引,沒有 = -1。不配置記憶體。
        public int GliderLipCrossed(Vector3 prev, Vector3 pos)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Inst f = Items[i];
                if (f.Kind != TrackFeatureKind.GliderRamp) continue;
                float dx = pos.x - f.Cx, dz = pos.z - f.Cz;
                if (dx * dx + dz * dz > (f.HL + 14f) * (f.HL + 14f) + f.HW * f.HW) continue;
                float du0 = (prev.x - f.Cx) * f.Fx + (prev.z - f.Cz) * f.Fz;
                float du1 = dx * f.Fx + dz * f.Fz;
                if (!(du0 < f.HL && du1 >= f.HL) || du0 < f.HL - 8f) continue;
                float t = (f.HL - du0) / (du1 - du0);
                float cx = prev.x + (pos.x - prev.x) * t - f.Cx, cz = prev.z + (pos.z - prev.z) * t - f.Cz;
                float dv = cx * f.Rx + cz * f.Rz;
                if (Mathf.Abs(dv) <= f.HW + 0.6f) return i;
            }
            return -1;
        }
    }

    /// 生成跳台 / 加速板的網格:全部合併成 3 個 Renderer(橘、黑、發光)+ 1 個 MeshCollider(所有跳台,掛 RaceSurface = Ramp)。
    internal static class TrackFeatureBuilder
    {
        const float RailH = 0.5f, RailT = 0.35f, Drop = 0.5f;
        const float VisLift = 0.03f;                 // 跳台外觀比碰撞體高一點,避免和路面 z-fighting
        const float PadLift = 0.145f, ArrowLift = 0.16f;   // 路面網格在地形上方約 10.5 cm(TrackMeshBuilder.YRoad + 2.5 cm);加速板要明顯高過它

        static readonly Color Dark = new Color(0.10f, 0.10f, 0.12f);   // 和 TrackMeshBuilder.DarkLine 同色 → 共用快取材質
        static readonly Color Orange = new Color(1f, 0.54f, 0.16f);

        static Vector3 W(in FeatureSet.Inst f, float du, float dv, float y)
        {
            return new Vector3(f.Cx + f.Fx * du + f.Rx * dv, y, f.Cz + f.Fz * du + f.Rz * dv);
        }

        /// 地形上的點(y = 地形原始高度 + lift)。
        static Vector3 G(RaceTerrain t, in FeatureSet.Inst f, float du, float dv, float lift)
        {
            Vector3 p = W(f, du, dv, 0f);
            p.y = t.Raw(p.x, p.z) + lift;
            return p;
        }

        public static void Build(FeatureSet fs, RaceTerrain terr, Transform parent, List<Mesh> meshes)
        {
            if (fs == null || fs.Count == 0 || terr == null) return;
            var orange = new RaceMeshAcc(); var dark = new RaceMeshAcc(); var glow = new RaceMeshAcc(); var col = new RaceMeshAcc(); var gold = new RaceMeshAcc(); var gcol = new RaceMeshAcc(); var gcolFixed = new RaceMeshAcc();
            for (int i = 0; i < fs.Items.Count; i++)
            {
                FeatureSet.Inst f = fs.Items[i];
                if (f.IsRamp) { BuildRamp(f, terr, orange, dark, f.IsGlider ? (f.GapRamp ? gcolFixed : gcol) : col); if (f.IsGlider) BuildGliderDecor(f, terr, orange, dark, gold); }
                else BuildPad(f, terr, dark, glow);
            }
            var root = new GameObject("Features");
            root.transform.SetParent(parent, false);
            AddPart(root.transform, meshes, "FeatOrange", orange, RaceArt.Mat(Orange, 0.1f));
            AddPart(root.transform, meshes, "FeatDark", dark, RaceArt.Mat(Dark, 0.1f));
            AddPart(root.transform, meshes, "FeatGlow", glow, RaceArt.Glow(RacePalette.Go, 2.2f));
            AddPart(root.transform, meshes, "FeatGlider", gold, RaceArt.Glow(RacePalette.Gold, 2.0f));
            if (col.TriCount > 0)
            {
                var g = new GameObject("RampCollider");
                g.transform.SetParent(root.transform, false);
                Mesh m = col.ToMesh("RampCollider");
                meshes.Add(m);
                g.AddComponent<MeshCollider>().sharedMesh = m;
                g.AddComponent<RaceSurface>().Type = Surface.Ramp;
            }
            if (gcol.TriCount > 0)
            {
                // 滑翔跳台另外一個碰撞體:掛 GliderRamp,車從上面飛出去的那一步 RaceKart 會立刻展開滑翔翼(不必等 AutoGlide 的 0.6 s,飛越缺口的距離才夠)
                var g = new GameObject("GliderRampCollider");
                g.transform.SetParent(root.transform, false);
                Mesh m = gcol.ToMesh("GliderRampCollider");
                meshes.Add(m);
                g.AddComponent<MeshCollider>().sharedMesh = m;
                g.AddComponent<RaceSurface>().Type = Surface.Ramp;
                g.AddComponent<GliderRamp>();
            }
            if (gcolFixed.TriCount > 0)
            {
                // M8:飛越缺口的起飛跳台:固定出場速度(RaceKart.GliderLaunch)
                var g = new GameObject("GliderRampCollider");
                g.transform.SetParent(root.transform, false);
                Mesh m = gcolFixed.ToMesh("GliderRampCollider");
                meshes.Add(m);
                g.AddComponent<MeshCollider>().sharedMesh = m;
                g.AddComponent<RaceSurface>().Type = Surface.Ramp;
                g.AddComponent<GliderRamp>().FixedLaunch = true;
            }
        }

        static void AddPart(Transform parent, List<Mesh> meshes, string name, RaceMeshAcc acc, Material mat)
        {
            if (acc.TriCount == 0) return;
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            Mesh m = acc.ToMesh(name);
            meshes.Add(m);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ 跳台

        static void BuildRamp(FeatureSet.Inst f, RaceTerrain t, RaceMeshAcc orange, RaceMeshAcc dark, RaceMeshAcc col)
        {
            int nu = Mathf.Max(2, Mathf.RoundToInt(f.HL * 2f)), nv = Mathf.Max(2, Mathf.RoundToInt(f.HW * 2f));
            float len = f.HL * 2f, wid = f.HW * 2f;
            Vector3 up = Vector3.up;
            Vector3 F = new Vector3(f.Fx, 0f, f.Fz), R = new Vector3(f.Rx, 0f, f.Rz);
            Vector3 slopeN = (up - F * f.TanP).normalized;

            // 頂面格點(碰撞體 lift = 0;外觀 lift = VisLift)
            var top = new Vector3[nu + 1, nv + 1];
            var baseP = new Vector3[nu + 1, nv + 1];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float du = -f.HL + len * i / nu, dv = -f.HW + wid * j / nv;
                    Vector3 b = G(t, f, du, dv, 0f);
                    baseP[i, j] = b;
                    top[i, j] = b + up * (f.TanP * (du + f.HL));
                }

            // ---- 碰撞體:頂面 + 兩側 + 後面 + 前面(往下延伸 Drop,封閉不漏)
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                    col.Quad(top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1], slopeN);
            for (int i = 0; i < nu; i++)
            {
                col.Quad(top[i, 0], top[i + 1, 0], baseP[i + 1, 0] - up * Drop, baseP[i, 0] - up * Drop, -R);
                col.Quad(top[i, nv], top[i + 1, nv], baseP[i + 1, nv] - up * Drop, baseP[i, nv] - up * Drop, R);
            }
            for (int j = 0; j < nv; j++)
            {
                col.Quad(top[nu, j], top[nu, j + 1], baseP[nu, j + 1] - up * Drop, baseP[nu, j] - up * Drop, F);
                col.Quad(top[0, j], top[0, j + 1], baseP[0, j + 1] - up * Drop, baseP[0, j] - up * Drop, -F);
            }

            // ---- 外觀:橘黑斜紋頂面(2 格寬的階梯斜紋)
            Vector3 lift = up * VisLift;
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    var acc = (((i + j) / 2) % 2 == 0) ? orange : dark;
                    acc.Quad(top[i, j] + lift, top[i + 1, j] + lift, top[i + 1, j + 1] + lift, top[i, j + 1] + lift, slopeN);
                }

            // ---- 側面(黑)與後面(橘黑橫紋)
            for (int i = 0; i < nu; i++)
            {
                dark.Quad(top[i, 0], top[i + 1, 0], baseP[i + 1, 0], baseP[i, 0], -R);
                dark.Quad(top[i, nv], top[i + 1, nv], baseP[i + 1, nv], baseP[i, nv], R);
            }
            int rows = Mathf.Max(1, Mathf.CeilToInt(f.TanP * len));
            for (int j = 0; j < nv; j++)
                for (int k = 0; k < rows; k++)
                {
                    float f0 = k / (float)rows, f1 = (k + 1) / (float)rows;
                    Vector3 a0 = Vector3.Lerp(baseP[nu, j], top[nu, j], f0), a1 = Vector3.Lerp(baseP[nu, j], top[nu, j], f1);
                    Vector3 b0 = Vector3.Lerp(baseP[nu, j + 1], top[nu, j + 1], f0), b1 = Vector3.Lerp(baseP[nu, j + 1], top[nu, j + 1], f1);
                    var acc = ((j + k) % 2 == 0) ? orange : dark;
                    acc.Quad(a0, b0, b1, a1, F);
                }

            // ---- 側邊護欄(橘,只有外觀):沿斜面,高 RailH,厚 RailT
            for (int i = 0; i < nu; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int jo = s < 0 ? 0 : nv;
                    float dvo = s < 0 ? -f.HW : f.HW, dvi = s < 0 ? -f.HW + RailT : f.HW - RailT;
                    float du0 = -f.HL + len * i / nu, du1 = -f.HL + len * (i + 1) / nu;
                    Vector3 o0 = G(t, f, du0, dvo, f.TanP * (du0 + f.HL) + VisLift), o1 = G(t, f, du1, dvo, f.TanP * (du1 + f.HL) + VisLift);
                    Vector3 n0 = G(t, f, du0, dvi, f.TanP * (du0 + f.HL) + VisLift), n1 = G(t, f, du1, dvi, f.TanP * (du1 + f.HL) + VisLift);
                    Vector3 h = up * RailH;
                    orange.Quad(o0, o1, o1 + h, o0 + h, R * s);          // 外面
                    orange.Quad(n0, n1, n1 + h, n0 + h, -R * s);         // 內面
                    dark.Quad(n0 + h, n1 + h, o1 + h, o0 + h, slopeN);   // 頂(黑)
                    if (i == 0) orange.Quad(n0, o0, o0 + h, n0 + h, -F);
                    if (i == nu - 1) orange.Quad(n1, o1, o1 + h, n1 + h, F);
                }
        }

        // ------------------------------------------------------------------ 滑翔跳台的裝飾

        /// 斜面上的點(外觀,離斜面 lift)。
        static Vector3 Top(RaceTerrain t, in FeatureSet.Inst f, float du, float dv, float lift)
        {
            return G(t, f, du, dv, f.TanP * (du + f.HL) + lift);
        }

        /// 滑翔翼貼紙(深色底板 + 發光金色紙飛機造型)、兩排黃色往前的 V 形箭頭、兩側旗幟。全部只有外觀,沒有碰撞體。
        static void BuildGliderDecor(FeatureSet.Inst f, RaceTerrain t, RaceMeshAcc orange, RaceMeshAcc dark, RaceMeshAcc gold)
        {
            Vector3 up = Vector3.up;
            Vector3 slopeN = (up - new Vector3(f.Fx, 0f, f.Fz) * f.TanP).normalized;
            const float L1 = 0.10f, L2 = 0.16f;     // 底板、貼紙離斜面的高度(比斜紋頂面的 VisLift 高)

            // 底板:深色長方形 du -1.8..5.6、dv ±3.9(讓金色貼紙跟橘黑斜紋分開)
            float d0 = -1.8f, d1 = Mathf.Min(f.HL - 1.0f, 5.6f), w = Mathf.Min(f.HW - 1.0f, 3.9f);
            dark.Quad(Top(t, f, d0, -w, L1), Top(t, f, d1, -w, L1), Top(t, f, d1, w, L1), Top(t, f, d0, w, L1), slopeN);

            // 紙飛機:鼻尖 → 右翼尖 → 後缺口 → 左翼尖
            float nose = d1 - 0.5f, tail = d0 + 0.5f, notch = tail + (nose - tail) * 0.28f, wing = w - 0.5f;
            Vector3 pn = Top(t, f, nose, 0f, L2), pr = Top(t, f, tail, wing, L2), pl = Top(t, f, tail, -wing, L2), pc = Top(t, f, notch, 0f, L2);
            gold.Tri(pn, pr, pc, slopeN);
            gold.Tri(pn, pc, pl, slopeN);

            // 往前的 V 形箭頭(跳台下半段,2 個)
            const float armH = 1.3f, armT = 0.55f;
            float aw = Mathf.Min(f.HW - 1.2f, 3.6f);
            for (int k = 0; k < 2; k++)
            {
                float a0 = -f.HL + 0.7f + k * 2.3f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = Top(t, f, a0, s * aw, L2), b = Top(t, f, a0 + armT, s * aw, L2);
                    Vector3 c = Top(t, f, a0 + armH + armT, 0f, L2), d = Top(t, f, a0 + armH, 0f, L2);
                    gold.Quad(a, b, c, d, slopeN);
                }
            }

            // 側邊旗幟:每側 3 根旗桿(深色),旗面是橘色三角形朝跳台中央飄
            float[] pu = { -f.HL + 1.2f, 0f, f.HL - 1.2f };
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < pu.Length; k++)
                {
                    float dv = s * (f.HW + 0.35f);
                    Vector3 b = G(t, f, pu[k], dv, f.TanP * (pu[k] + f.HL));
                    float poleH = 3.4f;
                    dark.Box(b + up * (poleH * 0.5f), new Vector3(f.Rx, 0f, f.Rz), up, new Vector3(f.Fx, 0f, f.Fz), new Vector3(0.07f, poleH * 0.5f, 0.07f));
                    Vector3 top = b + up * poleH, mid = b + up * (poleH - 1.0f);
                    Vector3 tip = top - new Vector3(f.Rx, 0f, f.Rz) * (s * 1.6f) + up * (-0.5f);
                    orange.Tri(top, mid, tip, new Vector3(f.Fx, 0f, f.Fz));
                    orange.Tri(top, tip, mid, -new Vector3(f.Fx, 0f, f.Fz));   // 雙面
                }
        }

        // ------------------------------------------------------------------ 加速板

        static void BuildPad(FeatureSet.Inst f, RaceTerrain t, RaceMeshAcc dark, RaceMeshAcc glow)
        {
            int nu = Mathf.Max(2, Mathf.RoundToInt(f.HL * 2f)), nv = Mathf.Max(2, Mathf.RoundToInt(f.HW * 2f));
            float len = f.HL * 2f, wid = f.HW * 2f;
            Vector3 up = Vector3.up;
            var p = new Vector3[nu + 1, nv + 1];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                    p[i, j] = G(t, f, -f.HL + len * i / nu, -f.HW + wid * j / nv, PadLift);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                    dark.Quad(p[i, j], p[i + 1, j], p[i + 1, j + 1], p[i, j + 1], up);

            // 3 個往前的箭頭(V 形,尖端朝行進方向):每個由左右兩條斜臂組成
            const float armH = 1.25f, armT = 0.55f;
            float aw = Mathf.Max(0.8f, f.HW - 0.7f);
            int count = Mathf.Max(1, Mathf.FloorToInt((len - 0.6f) / (armH + armT + 0.45f)));
            float pitch = (len - 0.6f - (armH + armT)) / Mathf.Max(1, count - 1 + (count == 1 ? 1 : 0));
            float start = -f.HL + 0.3f;
            for (int k = 0; k < count; k++)
            {
                float d0 = count == 1 ? -(armH + armT) * 0.5f : start + pitch * k;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = G(t, f, d0, s * aw, ArrowLift);
                    Vector3 b = G(t, f, d0 + armT, s * aw, ArrowLift);
                    Vector3 c = G(t, f, d0 + armH + armT, 0f, ArrowLift);
                    Vector3 d = G(t, f, d0 + armH, 0f, ArrowLift);
                    glow.Quad(a, b, c, d, up);
                }
            }
        }
    }

    /// 飛越缺口的世界改造(模組 W,M7):把深坑挖進地形(Surface.Water,掉下去 = 重置)、把缺口範圍的路面網格 / 路邊造景拿掉。
    /// 全部在 RaceWorld.Build 裡呼叫,順序:RaceTerrain.Generate -> CarvePits -> TerrainChunks.Build -> TrackMeshBuilder.Build -> CutRoad -> ... -> TrackProps.Build -> CleanDecor。
    /// 不碰 TerrainChunks / TrackMeshBuilder / TrackProps 的程式,只改它們的輸入 / 輸出(高度圖、已建好的網格)。
    internal static class TrackGapBuilder
    {
        public const float Falloff = 14f;     // 深坑橫向邊緣平滑接回自然地形的寬度 m
        const float CutPad = 0.5f;            // 路面網格在前緣 / 落地邊緣各多留 0.5 m 才切(四邊形 2 m 一格,質心判定)
        const float RoadCut = 40f;            // 路面網格橫向切除範圍(路寬 + 路肩 + 護欄都在這之內)

        /// 把每個缺口範圍的高度圖挖到海平面下。地形會在 Falloff 範圍內平滑接回,前後各留 PitInset 公尺不動(跳台與落地區下面維持路面高度)。
        /// 必須在 TerrainChunks.Build 之前呼叫(網格與碰撞體由高度圖產生);鏡像 / 反向 / 一般版共用同一套幾何(座標已經是變體後的)。
        public static void CarvePits(RaceTrack tr, RaceTerrain t)
        {
            if (tr == null || t == null || tr.Gaps.Count == 0) return;
            const int N = RaceTerrain.N;
            for (int gi = 0; gi < tr.Gaps.Count; gi++)
            {
                GapInfo g = tr.Gaps[gi];
                Vector3 o = g.Origin, f = g.Dir, r = g.Right;
                float uA = GapInfo.PitInset, uB = g.Length - GapInfo.PitInset;
                float W = g.HalfWidth, R = W + Falloff;
                float floorH = -g.Depth;
                // 外接矩形(格子範圍)
                float x0 = Mathf.Min(Mathf.Min(o.x + f.x * uA - r.x * R, o.x + f.x * uA + r.x * R), Mathf.Min(o.x + f.x * uB - r.x * R, o.x + f.x * uB + r.x * R));
                float x1 = Mathf.Max(Mathf.Max(o.x + f.x * uA - r.x * R, o.x + f.x * uA + r.x * R), Mathf.Max(o.x + f.x * uB - r.x * R, o.x + f.x * uB + r.x * R));
                float z0 = Mathf.Min(Mathf.Min(o.z + f.z * uA - r.z * R, o.z + f.z * uA + r.z * R), Mathf.Min(o.z + f.z * uB - r.z * R, o.z + f.z * uB + r.z * R));
                float z1 = Mathf.Max(Mathf.Max(o.z + f.z * uA - r.z * R, o.z + f.z * uA + r.z * R), Mathf.Max(o.z + f.z * uB - r.z * R, o.z + f.z * uB + r.z * R));
                int i0 = Mathf.Max(0, Mathf.FloorToInt((x0 + RaceTerrain.Half) / RaceTerrain.Cell)), i1 = Mathf.Min(N - 1, Mathf.CeilToInt((x1 + RaceTerrain.Half) / RaceTerrain.Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((z0 + RaceTerrain.Half) / RaceTerrain.Cell)), j1 = Mathf.Min(N - 1, Mathf.CeilToInt((z1 + RaceTerrain.Half) / RaceTerrain.Cell));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = -RaceTerrain.Half + i * RaceTerrain.Cell, z = -RaceTerrain.Half + j * RaceTerrain.Cell;
                        float dx = x - o.x, dz = z - o.z;
                        float u = dx * f.x + dz * f.z, v = dx * r.x + dz * r.z;
                        if (u < uA || u > uB) continue;
                        float av = Mathf.Abs(v);
                        if (av >= R) continue;
                        float k = 1f;
                        if (av > W) { float q = (av - W) / Falloff; k = 1f - q * q * (3f - 2f * q); }
                        int idx = j * N + i;
                        float h = t.H[idx];
                        t.H[idx] = h + (Mathf.Min(h, floorH) - h) * k;
                    }
                if (floorH < t.MinH) t.MinH = floorH;
            }
        }

        // 三角形質心是否在這個缺口的「路面切除盒」內(沿缺口前緣 + CutPad 到落地邊緣 - CutPad,橫向 RoadCut)。
        static bool InCut(GapInfo g, Vector3 c)
        {
            float dx = c.x - g.Origin.x, dz = c.z - g.Origin.z;
            float u = dx * g.Dir.x + dz * g.Dir.z, v = dx * g.Right.x + dz * g.Right.z;
            return u > CutPad && u < g.Length - CutPad && v > -RoadCut && v < RoadCut;
        }

        // 造景切除盒:整個深坑範圍(含兩端內縮)、橫向到坑邊 + 4 m,而且高度在水面上(漂在空中或貼在坑壁上的東西)。
        static bool InDecorCut(GapInfo g, Vector3 c)
        {
            if (c.y < 0.3f) return false;
            float dx = c.x - g.Origin.x, dz = c.z - g.Origin.z;
            float u = dx * g.Dir.x + dz * g.Dir.z, v = dx * g.Right.x + dz * g.Right.z;
            return u > -2f && u < g.Length + 2f && Mathf.Abs(v) < g.HalfWidth + 4f;
        }

        // ---- M8:缺口的大型標示(純外觀,沒有碰撞體):跳台前 3 座箭頭門架(金色 ^ 形、越近越多)、落地端 4 根大旗 + 兩條落地發光線 + 一座倒 V 落地門架 ----
        static readonly Color GantryDark = new Color(0.16f, 0.17f, 0.22f), FlagOrange = new Color(1f, 0.45f, 0.08f);

        public static int LastMarkerTris;

        public static void BuildMarkers(RaceTrack tr, RaceTerrain terr, Transform worldRoot, List<Mesh> meshes)
        {
            LastMarkerTris = 0;
            if (tr == null || terr == null || worldRoot == null || tr.Gaps.Count == 0) return;
            var dark = new RaceMeshAcc(); var gold = new RaceMeshAcc(); var orange = new RaceMeshAcc();
            for (int gi = 0; gi < tr.Gaps.Count; gi++)
            {
                GapInfo g = tr.Gaps[gi];
                for (int k = 0; k < 3; k++)
                    Gantry(tr, terr, g.RampBase - 30f - 26f * k, 3 - k, true, dark, gold);
                Gantry(tr, terr, g.S1 + 16f, 1, false, dark, gold);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 2; k++)
                    {
                        TrackSample sm = tr.SampleAt(g.S1 + 6f + 44f * k);
                        float hw = tr.HalfWidthAt(g.S1 + 6f + 44f * k);
                        Vector3 b = sm.Pos + sm.Right * (side * (hw + 7f)); b.y = terr.Height(b.x, b.z) - 0.3f;
                        Flag(b, sm.Right * side, 15f, dark, orange, gold);
                    }
                // 落地發光線:路面上兩條橫線(貼著路面,離地 6 cm)
                for (int k = 0; k < 2; k++)
                {
                    float sL = g.S1 + 3f + 7f * k;
                    TrackSample sm = tr.SampleAt(sL), sm2 = tr.SampleAt(sL + 1.2f);
                    float hw = tr.HalfWidthAt(sL) - 0.8f;
                    Vector3 a = sm.Pos + sm.Right * -hw + Vector3.up * 0.07f, b = sm.Pos + sm.Right * hw + Vector3.up * 0.07f;
                    Vector3 c = sm2.Pos + sm2.Right * hw + Vector3.up * 0.07f, d = sm2.Pos + sm2.Right * -hw + Vector3.up * 0.07f;
                    gold.Quad(a, d, c, b, Vector3.up);
                }
            }
            var root = new GameObject("GapMarkers");
            root.transform.SetParent(worldRoot, false);
            LastMarkerTris = dark.TriCount + gold.TriCount + orange.TriCount;
            Part(root.transform, meshes, "GapPosts", dark, RaceArt.Mat(GantryDark, 0.2f));
            Part(root.transform, meshes, "GapFlags", orange, RaceArt.Mat(FlagOrange, 0.2f));
            Part(root.transform, meshes, "GapGlow", gold, RaceArt.Glow(RacePalette.Gold, 2.2f));
        }

        static void Part(Transform parent, List<Mesh> meshes, string name, RaceMeshAcc acc, Material mat)
        {
            if (acc.TriCount == 0) return;
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            Mesh m = acc.ToMesh(name);
            meshes.Add(m);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // 門架:兩根立柱(離路面中心 hw + 6.5)、橫樑(離地 9 m)、樑上 n 個金色 V 形(up = true:^ 形,起飛前的「跳!」;false:v 形,落地端的「收」)
        static void Gantry(RaceTrack tr, RaceTerrain terr, float s, int n, bool up, RaceMeshAcc dark, RaceMeshAcc gold)
        {
            TrackSample sm = tr.SampleAt(s);
            Vector3 fwd = sm.Tangent; fwd.y = 0f; fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            float hw = tr.HalfWidthAt(s) + 6.5f, H = 9f;
            Vector3 c = sm.Pos;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 b = c + right * (side * hw); float gy = terr.Height(b.x, b.z) - 0.3f;
                float top = c.y + H;
                dark.Box(new Vector3(b.x, (gy + top) * 0.5f, b.z), right, Vector3.up, fwd, new Vector3(0.35f, (top - gy) * 0.5f, 0.35f));
            }
            dark.Box(c + Vector3.up * (H - 0.5f), right, Vector3.up, fwd, new Vector3(hw + 0.4f, 0.5f, 0.3f));
            // V 形:兩根斜條,從中央往兩側(^ 向上展開 / v 向下展開)
            float chev = Mathf.Min(hw - 1.5f, 7.5f);
            Vector3 face = -fwd * 0.34f;   // 朝向來車的那一面(稍微凸出樑面)
            for (int i = 0; i < n; i++)
            {
                float y0 = c.y + H - 3.2f - i * 1.7f;
                float dy = (up ? -1f : 1f) * 2.2f;
                Vector3 apex = c + Vector3.up * (y0 - c.y + (up ? 2.2f : 0f)) + face;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 e = apex + right * (side * chev) + Vector3.up * dy;
                    Vector3 w = Vector3.up * 0.55f;
                    gold.Quad(apex, apex + w, e + w, e, face.normalized);
                    gold.Quad(apex, e, e + w, apex + w, -face.normalized);   // 雙面
                }
            }
        }

        // 大旗:立柱 + 橘色三角旗(朝路中央飄)+ 金色頂球(八面體小塊)
        static void Flag(Vector3 b, Vector3 outward, float h, RaceMeshAcc dark, RaceMeshAcc orange, RaceMeshAcc gold)
        {
            Vector3 up = Vector3.up, o = outward; o.y = 0f; o.Normalize();
            Vector3 fw = new Vector3(o.z, 0f, -o.x);
            dark.Box(b + up * (h * 0.5f), o, up, fw, new Vector3(0.22f, h * 0.5f, 0.22f));
            Vector3 top = b + up * h, mid = b + up * (h - 3.4f), tip = top - o * 5.2f + up * (-1.7f);
            orange.Tri(top, mid, tip, fw); orange.Tri(top, tip, mid, -fw);
            gold.Box(top + up * 0.5f, o, up, fw, new Vector3(0.5f, 0.5f, 0.5f));
        }

        /// 把 TrackMeshBuilder 做好的路面 / 路肩 / 標線 / 路緣 / 檢查點小柱 / 護欄碰撞,在缺口範圍內的三角形拿掉。root = "Track" 物件。
        public static void CutRoad(RaceTrack tr, Transform root)
        {
            if (tr == null || root == null || tr.Gaps.Count == 0) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) FilterMesh(tr, mf.sharedMesh, false);
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                Mesh m = mc.sharedMesh;
                if (FilterMesh(tr, m, false)) { mc.sharedMesh = null; mc.sharedMesh = m; }
            }
        }

        /// 把 TrackProps 合併出來的造景網格(root 底下的 "TrackProps")在深坑範圍內漂著的三角形拿掉。
        public static void CleanDecor(RaceTrack tr, Transform worldRoot)
        {
            if (tr == null || worldRoot == null || tr.Gaps.Count == 0) return;
            Transform d = worldRoot.Find("TrackProps");
            if (d == null) return;
            foreach (var mf in d.GetComponentsInChildren<MeshFilter>(true)) FilterMesh(tr, mf.sharedMesh, true);
        }

        // 回傳有沒有拿掉三角形。decor = true 用造景切除盒,否則用路面切除盒。網格是平面著色(每個三角形 3 個獨立頂點),頂點留著不刪,只重寫索引。
        static bool FilterMesh(RaceTrack tr, Mesh m, bool decor)
        {
            if (m == null || m.subMeshCount < 1) return false;
            int[] tris = m.GetTriangles(0);
            if (tris == null || tris.Length < 3) return false;
            Vector3[] vs = m.vertices;
            var keep = new List<int>(tris.Length);
            int removed = 0;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 c = (vs[tris[t]] + vs[tris[t + 1]] + vs[tris[t + 2]]) * (1f / 3f);
                bool cut = false;
                for (int gi = 0; gi < tr.Gaps.Count && !cut; gi++) cut = decor ? InDecorCut(tr.Gaps[gi], c) : InCut(tr.Gaps[gi], c);
                if (cut) { removed++; continue; }
                keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
            }
            if (removed == 0) return false;
            m.SetTriangles(keep, 0);
            m.RecalculateBounds();
            return true;
        }
    }

    /// 軌道滑行的世界物件(模組 K):把 RaceTrack.Rails 變成外觀(細梁 + 發光邊 + 立柱,全部合併成 3 個 Renderer,沒有碰撞體)並登記進 KartRails;
    /// 順便把賽道護欄碰撞體("RailCollider")與峽谷主題的地形碰撞體標上 RideableWall(跑牆資格)。RaceWorld.Build 在 TrackFeatureBuilder 之後呼叫。
    internal static class TrackRailBuilder
    {
        public static int LastTris;   // 最近一次建出的軌道三角形數(測試用)
        static readonly Color Beam = new Color(0.80f, 0.84f, 0.92f);
        static readonly Color Post = new Color(0.22f, 0.24f, 0.30f);

        public static void Build(RaceTrack tr, Transform trackRoot, Transform worldRoot, List<Mesh> meshes)
        {
            KartRails.Clear();
            LastTris = 0;
            if (trackRoot != null)
            {
                Transform rc = trackRoot.Find("RailCollider");
                if (rc != null && rc.GetComponent<RideableWall>() == null) rc.gameObject.AddComponent<RideableWall>();
            }
            if (tr != null && tr.Theme == 1 && worldRoot != null)
            {
                Transform terr = worldRoot.Find("Terrain");
                if (terr != null)
                    foreach (var mc in terr.GetComponentsInChildren<MeshCollider>(true))
                        if (mc.GetComponent<RideableWall>() == null) mc.gameObject.AddComponent<RideableWall>();
            }
            if (tr == null || tr.Rails.Count == 0) return;
            var beam = new RaceMeshAcc(); var post = new RaceMeshAcc(); var glow = new RaceMeshAcc();
            for (int i = 0; i < tr.Rails.Count; i++)
            {
                KartRail kr = tr.BuildRail(i);
                KartRails.Add(kr);
                BuildOne(kr, tr.Rails[i].height, beam, post, glow);
            }
            var root = new GameObject("Rails");
            root.transform.SetParent(trackRoot, false);
            Part(root.transform, meshes, "RailBeam", beam, RaceArt.Mat(Beam, 0.3f));
            Part(root.transform, meshes, "RailPost", post, RaceArt.Mat(Post, 0.1f));
            Part(root.transform, meshes, "RailGlow", glow, RaceArt.Glow(RacePalette.Gold, 2.0f));
            LastTris = beam.TriCount + post.TriCount + glow.TriCount;
        }

        static void Part(Transform parent, List<Mesh> meshes, string name, RaceMeshAcc acc, Material mat)
        {
            if (acc.TriCount == 0) return;
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            Mesh m = acc.ToMesh(name);
            meshes.Add(m);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildOne(KartRail kr, float height, RaceMeshAcc beam, RaceMeshAcc post, RaceMeshAcc glow)
        {
            Vector3[] p = kr.P;
            for (int i = 0; i + 1 < p.Length; i++)
            {
                Vector3 a = p[i], b = p[i + 1], d = b - a;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                Vector3 f = d / len;
                Vector3 r = Vector3.Cross(Vector3.up, f); r = r.sqrMagnitude > 1e-6f ? r.normalized : Vector3.right;
                Vector3 u = Vector3.Cross(f, r).normalized;
                Vector3 mid = (a + b) * 0.5f;
                beam.Box(mid - u * 0.06f, r, u, f, new Vector3(0.08f, 0.06f, len * 0.5f + 0.02f));   // 梁(頂面 = 軌道高度)
                glow.Box(mid + u * 0.07f, r, u, f, new Vector3(0.025f, 0.012f, len * 0.5f));          // 頂面發光細線
            }
            // 立柱:每 ~16 m 一根 + 頭尾(從路面高度到軌道底;立柱在路肩側,不擋路)
            float next = 0f;
            for (int i = 0; i < p.Length; i++)
            {
                bool end = i == 0 || i == p.Length - 1;
                if (!end && kr.Cum[i] < next) continue;
                next = kr.Cum[i] + 16f;
                Vector3 top = p[i] - Vector3.up * 0.12f;
                float h = height - 0.12f;
                post.Box(top - Vector3.up * (h * 0.5f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(end ? 0.14f : 0.09f, h * 0.5f, end ? 0.14f : 0.09f));
            }
        }
    }
}
