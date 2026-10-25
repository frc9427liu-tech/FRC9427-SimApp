using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    // 比賽用金幣 + 滑翔觸發(模組 W,瑪利歐賽車式機制,見 docs/racing/05 §4)。
    //
    // 金幣:每圈 44~56 枚,沿中心線串成直線 / S 形 / 弧形(每組 4~6 枚),滑翔跳台前緣之後另有一條 5 枚的飛行弧線(離路面 6 m 左右往下收到路面)。
    //       撿取用水平距離 3.2 m + 垂直 3.5 m 判定(不用 Collider);撿走後 5 秒重生。全部合併成 1 個動態網格 + 1 個共用材質(RaceArt.Mat(Gold)),
    //       每幀由 CPU 重寫頂點讓金幣旋轉 / 上下飄(約 1 萬個頂點,不配置記憶體)。
    //       自由探索的 30 枚收集物(Collectibles.cs)是另一套,維持不變;自由探索模式不生成比賽金幣。
    // 滑翔:RaceWorld.StepPickups 每個物理步驟對每台車呼叫一次,車越過滑翔跳台前緣時回報 Glide = true。

    /// 一枚比賽金幣。
    public sealed class RaceCoin
    {
        public int Id;
        public Vector3 Pos;        // 金幣中心
        public bool Taken;
        public float RespawnAt;    // Taken 時,now >= RespawnAt 就重生
        public bool OnArc;         // 滑翔跳台飛行弧線上的金幣
        public int Group;          // 同一串的編號(弧線 = 跳台序號 + 1000)
    }

    /// RaceWorld.StepPickups 的回傳。
    public struct PickupResult
    {
        public int Coins;          // 這一步新撿到幾枚(整合者:呼叫 kart.AddCoin() 這麼多次)
        public bool Glide;         // 這一步越過了滑翔跳台前緣(整合者:呼叫 kart.BeginGlide())
        public int RampIndex;      // 滑翔跳台在 RaceWorld.GliderLips 的 Index 欄位(沒有 = -1)
    }

    /// 滑翔跳台的前緣資料(給 AI / 小地圖 / 特效用)。
    public struct GliderLip
    {
        public int Index;          // 內部索引(PickupResult.RampIndex)
        public Vector3 Center;     // 前緣中心(含高度)
        public Vector3 Forward;    // 水平的前進方向
        public float HalfWidth;    // 前緣半寬
        public float Rise;         // 前緣離路面的高度 m
    }

    /// M8:滑翔跳台的理想滑翔路徑(中性、GlideLift 1、固定出場速度),以 RaceKart 的滑翔常數積分。純數學。
    internal static class GlidePath
    {
        public const float Rise = 5.66f;       // 前緣離路面的高度(22 度 x 14 m 的跳台)
        static float[] xs, ys;
        const float Dt = 0.02f;

        static void Init()
        {
            if (xs != null) return;
            float g = 30f * RaceKart.GapGlideGravityMul, sink = RaceKart.GapGlideSinkBase;
            float hs = RaceKart.GlideLaunchSpeed, vy = RaceKart.GlideLaunchUp, x = 0f, y = 0f;
            var lx = new List<float>(); var ly = new List<float>();
            for (int i = 0; i < 600; i++)
            {
                lx.Add(x); ly.Add(y);
                vy -= g * Dt;
                if (vy < -sink) vy = Mathf.MoveTowards(vy, -sink, 30f * Dt);
                hs -= 0.4f * Dt;
                x += hs * Dt; y += vy * Dt;
                if (y < -Rise - 3f) break;
            }
            xs = lx.ToArray(); ys = ly.ToArray();
        }

        /// 水平距離 d(離前緣)處,離前緣高度的高度差(前緣 = 0,往下是負)。超過路徑終點就延伸最後的下降斜率。
        public static float RelHeight(float d)
        {
            Init();
            if (d <= 0f) return 0f;
            int n = xs.Length;
            if (d >= xs[n - 1]) return ys[n - 1] - (d - xs[n - 1]) * 0.2f;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (xs[m] <= d) lo = m; else hi = m; }
            float u = (d - xs[lo]) / Mathf.Max(1e-4f, xs[hi] - xs[lo]);
            return Mathf.Lerp(ys[lo], ys[hi], u);
        }

        /// 路徑落到前緣所在路面高度(RelHeight = -Rise)的水平距離 = 中性滑翔的落點。
        public static float Range()
        {
            Init();
            for (int i = 1; i < ys.Length; i++) if (ys[i] <= -Rise) return xs[i];
            return xs[xs.Length - 1];
        }
    }

    /// 金幣位置的產生(純資料 + 數學,不用 Unity 物件;同一條賽道結果固定)。
    internal static class CoinLayout
    {
        public const float Lift = 1.3f;                 // 金幣中心離地
        public static readonly float[] OldArcDist = { 10f, 22f, 34f, 46f, 54f };   // 手工賽道上沒有缺口的滑翔跳台(舊物理):前緣之後的距離
        public const int OldArcPer = 5;
        public const float OldArcApex = 6f, OldArcApexAt = 15f, OldArcEnd = 55f;
        public static float OldArcHeight(float d)
        {
            float a = OldArcApex / ((OldArcEnd - OldArcApexAt) * (OldArcEnd - OldArcApexAt));
            return Mathf.Max(Lift, OldArcApex - a * (d - OldArcApexAt) * (d - OldArcApexAt));
        }
        public const int ArcPer = 9;                    // M8:每個滑翔跳台一串 9 枚,沿「理想滑翔路徑」從前緣之後 8 m 排到缺口落地邊緣之前

        /// 第 k 枚弧線金幣離前緣的水平距離(缺口長 gapLen)。
        public static float ArcDist(int k, float gapLen)
        {
            float a = 8f, b = Mathf.Max(a + 20f, gapLen - 6f);
            return a + (b - a) * k / (ArcPer - 1);
        }

        /// 弧線金幣離「前緣處路面」的高度 m:中性滑翔(GlideLift 1、固定出場速度)在水平距離 d 處、離前緣所在路面的高度;最低 Lift。
        public static float ArcHeight(float d, float riseAtLip)
        {
            return Mathf.Max(Lift, riseAtLip + GlidePath.RelHeight(d));
        }

        static bool InSpan(float L, float s0, float s1, float a, float b)
        {
            // [s0,s1] 與 [a,b](都可以跨過 L)有交集
            float mid = (s0 + s1) * 0.5f, half = (s1 - s0) * 0.5f, mid2 = (a + b) * 0.5f, half2 = (b - a) * 0.5f;
            float d = (mid - mid2) % L;
            if (d > L * 0.5f) d -= L; else if (d < -L * 0.5f) d += L;
            return Mathf.Abs(d) <= half + half2;
        }

        static readonly int[] GroupCounts = { 5, 6, 5, 4 };

        /// 放 groups 串地面金幣(out == null 只數數量)。回傳放了幾枚。
        static int PlaceGroups(List<RaceCoin> outList, RaceTrack tr, RaceTerrain terr, List<Vector2> zones, float L, int groups)
        {
            int placed = 0;
            float gap = L / groups;
            for (int g = 0; g < groups; g++)
            {
                int pat = g % 4, n = GroupCounts[pat];
                float spacing = pat == 2 ? 4.5f : 4.0f;
                float sc = (g + 0.5f) * gap, len = (n - 1) * spacing;
                float s0 = sc - len * 0.5f;
                bool blocked = false;
                for (int z = 0; z < zones.Count && !blocked; z++)
                    if (InSpan(L, s0 - 1f, s0 + len + 1f, zones[z].x, zones[z].y)) blocked = true;
                if (blocked) continue;
                placed += n;
                if (outList == null) continue;
                float sign = (g & 1) == 0 ? 1f : -1f;
                for (int i = 0; i < n; i++)
                {
                    float u = n > 1 ? i / (float)(n - 1) : 0f, lat;
                    if (pat == 1) lat = 2.8f * Mathf.Sin(u * Mathf.PI * 2f);
                    else if (pat == 2) lat = sign * (-1.5f + 3f * Mathf.Sin(u * Mathf.PI));
                    else lat = ((g % 3) - 1) * 2.4f;
                    TrackSample sm = tr.SampleAt(s0 + i * spacing);
                    Vector3 p = sm.Pos + sm.Right * lat;
                    p.y = terr.Height(p.x, p.z) + Lift;
                    outList.Add(new RaceCoin { Pos = p, Group = g });
                }
            }
            return placed;
        }

        public static List<RaceCoin> Generate(RaceTrack tr, RaceTerrain terr, FeatureSet fs)
        {
            var coins = new List<RaceCoin>();
            float L = tr.Length;
            int target = Mathf.Clamp(Mathf.RoundToInt(L / 26f), 44, 52);

            // 禁放區(地面金幣):起跑格與終點線、滑翔跳台的起飛段與飛行弧線下面、普通跳台
            var zones = new List<Vector2>();
            zones.Add(new Vector2(L - 44f, L + 14f));
            int gi = 0;
            if (fs != null)
                for (int i = 0; i < fs.Count; i++)
                {
                    FeatureSet.Inst f = fs.Items[i];
                    if (f.Path >= 0 || !f.IsRamp) continue;
                    if (f.IsGlider)
                    {
                        float lip = f.S + f.HL;
                        float gl = 0f;
                        for (int q = 0; q < tr.Gaps.Count; q++) if (Mathf.Abs(tr.Gaps[q].S0 - lip) < 3f) gl = tr.Gaps[q].Length;
                        if (gl <= 0f)
                        {
                            zones.Add(new Vector2(f.S - f.HL - 14f, lip + 62f));
                            for (int k = 0; k < OldArcPer; k++)
                            {
                                TrackSample sm = tr.SampleAt(lip + OldArcDist[k]);
                                Vector3 p = sm.Pos;
                                p.y = (tr.InGap(lip + OldArcDist[k], 2f, 2f) ? sm.Pos.y : terr.Height(p.x, p.z)) + OldArcHeight(OldArcDist[k]);
                                coins.Add(new RaceCoin { Pos = p, OnArc = true, Group = 1000 + gi });
                            }
                            gi++;
                            continue;
                        }
                        zones.Add(new Vector2(f.S - f.HL - 14f, lip + gl + 40f));
                        float rise0 = tr.SampleAt(lip).Pos.y;
                        for (int k = 0; k < ArcPer; k++)
                        {
                            float ad = ArcDist(k, gl);
                            TrackSample sm = tr.SampleAt(lip + ad);
                            Vector3 p = sm.Pos;
                            // M7:缺口上空沒有地面(深坑 = 水面 0 m),弧線改以路面高度為基準;M8:弧線以前緣處路面為基準(台地上整段一樣高)
                            p.y = (tr.InGap(lip + ad, 2f, 2f) ? rise0 : terr.Height(p.x, p.z)) + ArcHeight(ad, GlidePath.Rise);
                            coins.Add(new RaceCoin { Pos = p, OnArc = true, Group = 1000 + gi });
                        }
                        gi++;
                    }
                    else zones.Add(new Vector2(f.S - f.HL - 3f, f.S + f.HL + 3f));
                }
            int arcCount = coins.Count;

            // 地面串:四種圖案輪流(直線 5 / S 形 6 / 弧形 5 / 直線 4)。串數從「剛好湊到 target」附近挑一個讓總數最接近 target 的(被禁放區擋掉的串不放)
            int g0 = Mathf.Max(1, Mathf.CeilToInt((target - arcCount) / 5f));
            int bestG = g0, bestDiff = int.MaxValue;
            for (int g = Mathf.Max(1, g0 - 2); g <= g0 + 12; g++)
            {
                int tot = arcCount + PlaceGroups(null, tr, terr, zones, L, g);
                int diff = Mathf.Abs(tot - target);
                if (diff < bestDiff) { bestDiff = diff; bestG = g; }
            }
            PlaceGroups(coins, tr, terr, zones, L, bestG);
            for (int i = 0; i < coins.Count; i++) coins[i].Id = i;
            return coins;
        }
    }

    /// 比賽金幣的實體:1 個動態網格,每幀重寫頂點(旋轉 + 飄動 + 重生時彈出)。
    public sealed class RaceCoinSet : MonoBehaviour
    {
        public const float RespawnSeconds = 5f, PickupRadius = 3.2f, PickupHeight = 3.5f;
        const int Sides = 10;
        const float Radius = 0.9f, HalfThick = 0.1f;

        public readonly List<RaceCoin> Items = new List<RaceCoin>();
        public int Count { get { return Items.Count; } }
        public int ArcCount { get; private set; }
        public int TriangleCount { get; private set; }
        /// (kartId, 金幣索引, 位置)
        public event Action<int, int, Vector3> Picked;

        Mesh mesh;
        Vector3[] tplV, tplN;       // 單枚金幣的頂點 / 法線(本地)
        Vector3[] verts, norms;     // 全部金幣
        float[] scale;              // 0..1(重生彈出)
        bool[] shown;
        float t;

        internal static RaceCoinSet Build(RaceTrack tr, RaceTerrain terr, FeatureSet fs, Transform parent, List<UnityEngine.Object> owned)
        {
            var go = new GameObject("RaceCoins");
            go.transform.SetParent(parent, false);
            var set = go.AddComponent<RaceCoinSet>();
            set.Items.AddRange(CoinLayout.Generate(tr, terr, fs));
            foreach (var c in set.Items) if (c.OnArc) set.ArcCount++;
            set.MakeMesh(owned);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = set.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceArt.Mat(RacePalette.Gold, 0.55f);   // 和自由探索收集物共用同一個材質
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return set;
        }

        void MakeMesh(List<UnityEngine.Object> owned)
        {
            var acc = new RaceMeshAcc();
            Vector3 front = new Vector3(0f, 0f, HalfThick), back = new Vector3(0f, 0f, -HalfThick);
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * Radius, Mathf.Sin(a0) * Radius, 0f), p1 = new Vector3(Mathf.Cos(a1) * Radius, Mathf.Sin(a1) * Radius, 0f);
                acc.Tri(front, p0 + front, p1 + front, Vector3.forward);
                acc.Tri(back, p0 + back, p1 + back, Vector3.back);
                float am = (a0 + a1) * 0.5f;
                acc.Quad(p0 + front, p1 + front, p1 + back, p0 + back, new Vector3(Mathf.Cos(am), Mathf.Sin(am), 0f));
                Vector3 q0 = p0 * 0.62f, q1 = p1 * 0.62f;
                acc.Quad(q0 + front * 1.25f, q1 + front * 1.25f, p1 * 0.7f + front, p0 * 0.7f + front, Vector3.forward);   // 中間的凹痕環
            }
            tplV = acc.V.ToArray(); tplN = acc.N.ToArray();
            int vpc = tplV.Length, n = Items.Count;
            verts = new Vector3[vpc * n]; norms = new Vector3[vpc * n];
            scale = new float[n]; shown = new bool[n];
            var tris = new int[acc.T.Count * n];
            for (int c = 0; c < n; c++)
                for (int k = 0; k < acc.T.Count; k++) tris[c * acc.T.Count + k] = acc.T[k] + c * vpc;
            TriangleCount = tris.Length / 3;
            Bounds b = new Bounds(Items.Count > 0 ? Items[0].Pos : Vector3.zero, Vector3.one);
            for (int c = 0; c < n; c++) { scale[c] = 1f; b.Encapsulate(Items[c].Pos); }
            b.Expand(new Vector3(4f, 6f, 4f));
            mesh = new Mesh { name = "RaceCoins" };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.MarkDynamic();
            Write(0f);
            mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
            mesh.bounds = b;
            bounds = b;
            owned.Add(mesh);
        }

        Bounds bounds;

        /// 重寫全部頂點。yaw / 飄動只依 time 與索引,所以建好當下(time = 0)的結果固定。
        void Write(float time)
        {
            int vpc = tplV.Length;
            for (int c = 0; c < Items.Count; c++)
            {
                RaceCoin coin = Items[c];
                int o = c * vpc;
                if (coin.Taken || scale[c] <= 0f)
                {
                    if (shown[c])
                    {
                        // 收成一個點(退化三角形,不會畫出來)
                        for (int v = 0; v < vpc; v++) { verts[o + v] = coin.Pos; norms[o + v] = Vector3.up; }
                        shown[c] = false;
                    }
                    continue;
                }
                float yaw = (time * 180f + c * 37f) * Mathf.Deg2Rad;
                float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
                float sc = scale[c];
                Vector3 p = coin.Pos; p.y += Mathf.Sin(time * 2.2f + c) * 0.18f;
                for (int v = 0; v < vpc; v++)
                {
                    Vector3 l = tplV[v], nn = tplN[v];
                    verts[o + v] = new Vector3(p.x + (cs * l.x + sn * l.z) * sc, p.y + l.y * sc, p.z + (-sn * l.x + cs * l.z) * sc);
                    norms[o + v] = new Vector3(cs * nn.x + sn * nn.z, nn.y, -sn * nn.x + cs * nn.z);
                }
                shown[c] = true;
            }
        }

        void Update()
        {
            if (mesh == null) return;
            float dt = Time.deltaTime;
            t += dt;
            Tick(Time.time);
            for (int c = 0; c < scale.Length; c++)
                if (!Items[c].Taken && scale[c] < 1f) scale[c] = Mathf.Min(1f, scale[c] + dt / 0.3f);
            Write(t);
            mesh.vertices = verts; mesh.normals = norms;
            mesh.bounds = bounds;
        }

        /// 讓時間到了的金幣重生(Update 會用 Time.time 呼叫;測試可以傳自己的時鐘)。
        public void Tick(float now)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                RaceCoin c = Items[i];
                if (c.Taken && now >= c.RespawnAt) { c.Taken = false; if (scale != null) scale[i] = 0f; }
            }
        }

        /// 撿取:水平距離 &lt;= PickupRadius 而且高度差 &lt;= PickupHeight 的金幣全撿走,5 秒後重生。回傳新撿到幾枚(沒撿到時不配置記憶體)。
        public int Collect(int kartId, Vector3 pos, float now)
        {
            Tick(now);
            int got = 0;
            float r2 = PickupRadius * PickupRadius;
            for (int i = 0; i < Items.Count; i++)
            {
                RaceCoin c = Items[i];
                if (c.Taken) continue;
                float dx = c.Pos.x - pos.x, dz = c.Pos.z - pos.z;
                if (dx * dx + dz * dz > r2 || Mathf.Abs(c.Pos.y - pos.y) > PickupHeight) continue;
                c.Taken = true; c.RespawnAt = now + RespawnSeconds; got++;
                if (scale != null) scale[i] = 0f;
                if (Picked != null) Picked(kartId, i, c.Pos);
            }
            return got;
        }

        /// 全部還原(新的一場比賽不必重建世界)。
        public void ResetAll()
        {
            for (int i = 0; i < Items.Count; i++) { Items[i].Taken = false; Items[i].RespawnAt = 0f; if (scale != null) scale[i] = 1f; }
        }
    }
}
