using UnityEngine;

namespace FrcSim
{
    public struct KartProbeResult
    {
        public bool Found;          // 找到可站的地面(坡度小於 40 度)
        public Vector3 Point;       // 地面點(中心射線命中點;沒命中中心就取最近的)
        public Vector3 Normal;      // 平均法線(單位向量)
        public float Dist;          // 車身中心到地面平面的距離(沿 Normal)
        public Surface Surface;
        public bool FromCollider;   // true = 碰撞體射線;false = 只靠 GroundSampler
        public bool GliderFixed;    // M8:該 GliderRamp 是飛越缺口的起飛跳台(FixedLaunch)
        public bool GliderRamp;     // 站的碰撞體(或其父物件)掛了 GliderRamp:飛出去會自動展開滑翔翼
    }

    /// 滑翔跳台標記(模組 K5):掛在跳台碰撞體(或其父物件)上,車子從這個跳台離地時自動展開滑翔翼。
    /// 模組 W 擺放滑翔跳台時 AddComponent 即可;或改在觸發器裡呼叫 RaceKart.BeginGlide()。
    public class GliderRamp : MonoBehaviour { public bool FixedLaunch; }

    /// 可跑牆標記:掛在牆的碰撞體(或其父物件)上,RaceKart 空中(集氣跳 / 特技 / 跳台之後)以淺角度碰到就會吸附跑牆。
    /// 預設由 TrackRailBuilder 標在賽道護欄碰撞體("RailCollider")與峽谷主題的地形碰撞體;測試與其他模組自己 AddComponent 即可。
    public class RideableWall : MonoBehaviour { }

    /// 一條軌道(折線,世界座標;模組 K,軌道滑行)。沒有碰撞體:RaceKart 用幾何查詢直接吸附,所以不影響探地 / 掃掠 / 碰撞數量。
    public sealed class KartRail
    {
        public Vector3[] P;        // 折線頂點(行進方向由 P[0] 到 P[last])
        public float[] Cum;        // 累積弧長
        public float Length;
        public int Id;
        Vector3 bmin, bmax;

        public static KartRail FromPoints(Vector3[] pts, int id = 0)
        {
            var r = new KartRail { P = pts, Id = id, Cum = new float[pts.Length] };
            r.bmin = r.bmax = pts[0];
            for (int i = 1; i < pts.Length; i++)
            {
                r.Cum[i] = r.Cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                r.bmin = Vector3.Min(r.bmin, pts[i]); r.bmax = Vector3.Max(r.bmax, pts[i]);
            }
            r.Length = r.Cum[pts.Length - 1];
            return r;
        }

        int SegAt(float t)
        {
            int lo = 0, hi = P.Length - 2;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (Cum[m] <= t) lo = m; else hi = m - 1; }
            return lo;
        }

        public Vector3 PointAt(float t)
        {
            t = Mathf.Clamp(t, 0f, Length);
            int i = SegAt(t);
            float seg = Cum[i + 1] - Cum[i];
            return Vector3.Lerp(P[i], P[i + 1], seg > 1e-5f ? (t - Cum[i]) / seg : 0f);
        }

        public Vector3 DirAt(float t)
        {
            int i = SegAt(Mathf.Clamp(t, 0f, Length));
            Vector3 d = P[i + 1] - P[i];
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        /// 離 q 最近的點:回傳該點的弧長位置 t,pt = 最近點,dist3 = 3D 距離。
        public float Project(Vector3 q, out Vector3 pt, out float dist3)
        {
            float bestD = float.MaxValue, bestT = 0f; pt = P[0];
            for (int i = 0; i + 1 < P.Length; i++)
            {
                Vector3 a = P[i], ab = P[i + 1] - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(q - a, ab) / l2) : 0f;
                Vector3 c = a + ab * u;
                float d = (q - c).sqrMagnitude;
                if (d < bestD) { bestD = d; pt = c; bestT = Cum[i] + u * Mathf.Sqrt(l2); }
            }
            dist3 = Mathf.Sqrt(bestD);
            return bestT;
        }

        public bool NearBounds(Vector3 q, float m)
        {
            return q.x >= bmin.x - m && q.x <= bmax.x + m && q.y >= bmin.y - m && q.y <= bmax.y + m && q.z >= bmin.z - m && q.z <= bmax.z + m;
        }
    }

    /// 目前世界的軌道登記簿(RaceWorld.Build 填、Destroy 清;測試自己 Add / Clear)。
    public static class KartRails
    {
        public static readonly System.Collections.Generic.List<KartRail> All = new System.Collections.Generic.List<KartRail>();
        public const float CatchH = 0.85f;                  // 水平吸附半徑
        public const float CatchBelow = 0.4f, CatchAbove = 0.6f;   // 車底離軌道的垂直容許(車底在軌道下 0.4 ~ 上 0.6 m)
        public static void Clear() { All.Clear(); }
        public static void Add(KartRail r) { if (r != null) All.Add(r); }

        /// 車底 bottom、速度 v:找得到可吸附的軌道就回傳 true(車必須在下降或接近水平 vy <= 1.5,水平速度不能朝軌道反方向,離尾端 > 1 m)。
        public static bool Find(Vector3 bottom, Vector3 v, out KartRail rail, out float t)
        {
            rail = null; t = 0f;
            if (All.Count == 0 || v.y > 1.5f) return false;
            Vector3 vh = new Vector3(v.x, 0f, v.z);
            for (int i = 0; i < All.Count; i++)
            {
                KartRail r = All[i];
                if (r == null || !r.NearBounds(bottom, 1.2f)) continue;
                Vector3 pt; float d3;
                float tt = r.Project(bottom, out pt, out d3);
                float dy = bottom.y - pt.y;
                float dh = Mathf.Sqrt((bottom.x - pt.x) * (bottom.x - pt.x) + (bottom.z - pt.z) * (bottom.z - pt.z));
                if (dh > CatchH || dy < -CatchBelow || dy > CatchAbove || tt > r.Length - 1f) continue;
                Vector3 dir = r.DirAt(tt); dir.y = 0f;
                if (vh.sqrMagnitude > 1f && Vector3.Dot(vh, dir) < -0.2f * vh.magnitude * dir.magnitude) continue;
                rail = r; t = tt;
                return true;
            }
            return false;
        }
    }

    /// 射線探地(模組 K,01 §2「坡面」):中心 + 4 支角落射線平均出法線;忽略有 Rigidbody 的碰撞體(自己、其他車)與 trigger;
    /// 坡度大於 40 度的面不當地面(視為牆)。碰撞體沒查到時退回 GroundSampler。路面由 RaceSurface 元件決定,沒有就問 GroundSampler。
    public static class KartGroundProbe
    {
        public const float MinNormalY = 0.766f;   // cos 40
        const float Lift = 0.5f;                  // 射線從中心上方 0.5 m 起射,避免起點卡進地面
        static readonly RaycastHit[] hits = new RaycastHit[24];
        static readonly float[] offX = { 0f, 0.4f, -0.4f, 0.4f, -0.4f };
        static readonly float[] offZ = { 0f, 0.6f, 0.6f, -0.6f, -0.6f };

        /// center = 車身中心;up = 車身上方向;fwd = 車頭(已在 up 的垂直面上);reach = 中心以下要找多深。
        public static KartProbeResult Probe(Vector3 center, Vector3 up, Vector3 fwd, float reach, GroundSampler sampler)
        {
            var res = new KartProbeResult { Found = false, Normal = Vector3.up, Surface = Surface.Road };
            Vector3 right = Vector3.Cross(up, fwd);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right; else right.Normalize();
            Vector3 down = -up;
            float len = Lift + reach;

            Vector3 nSum = Vector3.zero; int nCount = 0;
            bool centerHit = false; Vector3 centerPoint = default; Collider centerCol = null;
            float nearD = float.MaxValue; Vector3 nearPoint = default; Collider nearCol = null;

            for (int i = 0; i < offX.Length; i++)
            {
                Vector3 o = center + up * Lift + right * offX[i] + fwd * offZ[i];
                int n = Physics.RaycastNonAlloc(o, down, hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; int bi = -1;
                for (int h = 0; h < n; h++)
                {
                    var hit = hits[h];
                    if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;   // 自己 / 其他車 / 動態物
                    if (hit.normal.y < MinNormalY) continue;                                        // 太陡 = 牆
                    if (hit.distance < best) { best = hit.distance; bi = h; }
                }
                if (bi < 0) continue;
                var bh = hits[bi];
                nSum += bh.normal; nCount++;
                if (i == 0) { centerHit = true; centerPoint = bh.point; centerCol = bh.collider; }
                if (best < nearD) { nearD = best; nearPoint = bh.point; nearCol = bh.collider; }
            }

            if (nCount > 0)
            {
                Vector3 nrm = nSum.normalized;
                Vector3 p = centerHit ? centerPoint : nearPoint;
                Collider col = centerHit ? centerCol : nearCol;
                res.Found = true; res.FromCollider = true;
                res.Normal = nrm; res.Point = p;
                res.Dist = Vector3.Dot(center - p, nrm);
                RaceSurface rs = col != null ? col.GetComponentInParent<RaceSurface>() : null;
                GliderRamp gr = col != null ? col.GetComponentInParent<GliderRamp>() : null;
                res.GliderRamp = gr != null; res.GliderFixed = gr != null && gr.FixedLaunch;
                if (rs != null) res.Surface = rs.Type;
                else if (sampler != null && sampler(center, out GroundHit gh0)) res.Surface = gh0.Surface;
                return res;
            }

            if (sampler != null && sampler(center, out GroundHit gh))
            {
                Vector3 nrm = gh.Normal.sqrMagnitude > 0.01f ? gh.Normal.normalized : Vector3.up;
                if (nrm.y >= MinNormalY)
                {
                    res.Found = true; res.FromCollider = false;
                    res.Normal = nrm; res.Point = new Vector3(center.x, gh.Height, center.z);
                    res.Dist = Vector3.Dot(center - res.Point, nrm);
                    res.Surface = gh.Surface;
                    if (res.Dist > reach + Lift) res.Found = false;   // 太遠 = 在空中
                }
            }
            return res;
        }
    }
}
