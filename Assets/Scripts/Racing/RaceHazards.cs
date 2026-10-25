using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// 賽道移動障礙(原創設計,依主題換造型):沿著直線段橫越路面,位置只由「賽道 + 比賽時鐘」決定(沒有亂數、沒有物理),
    /// 所以重播 / 幽靈 / 測試完全可重現。命中 = RaceKart.Stun(0.8 s,自動掉 2 枚金幣);星星 / 巨大化 / 子彈 / 無敵 / 暈眩中 / 羽毛跳躍中 / 空中都不會被打。
    /// 模型(HazardSpot、StateAt)是純資料,-batchmode -nographics 可直接測;外觀(RaceToonVC 頂點色網格,不新增材質)只在 Init(visuals: true) 時建。
    public enum HazardKind { Barrel, Boulder, Crab, HayBale, Frog, Tumbleweed, Cactus, IceBlock, Snowball, Log, Fireball }

    public enum HazardMotion { Roll, Slide, Hop, Drop, Bounce }

    public sealed class HazardSpot
    {
        public int Index;
        public HazardKind Kind;
        public HazardMotion Motion;
        public float S;                 // 主線弧長
        public float HalfWidth;         // 該處半路寬
        public float Extent;            // 橫向行程單邊長度(路緣外 2.5 m)
        public float Radius;            // 碰撞 / 外觀半徑
        public float Speed;             // 橫向速度 m/s
        public float Period;            // 一個循環秒數(行進時間 + 休息)
        public float Phase;             // 時鐘偏移
        public int Dir;                 // +1 = 由左往右、-1 = 由右往左
        public float HopAmp, HopRate;   // 跳躍高度 / 每秒跳躍次數(Hop / Bounce)
        public float FallH, FallT;      // 落下高度 / 秒數(Drop)
        public float HitLift;           // 離地高度低於這個才打得到
        public float TravelTime { get { return FallT + 2f * Extent / Speed; } }
        public Vector3 Pos, Right, Tangent;   // 中心線取樣
        public float Bank;
        public bool Relaxed;            // 找不到夠直的位置時用較寬鬆的曲率門檻(峽谷)
    }

    public static class RaceHazards
    {
        // ---- 擺放規則(測試也用這些常數)
        public const float StartClear = 130f;      // 終點線之後這段距離內不放(起跑 / 第一個彎前)
        public const float EndClear = 80f;         // 終點線之前(起跑格在線後方 ~34 m)
        public const float GapBefore = 120f, GapAfter = 80f;
        public const float ShortcutClear = 60f;    // 捷徑入口 / 出口前後
        public const float FeatureClear = 28f;     // 跳台 / 加速板前後(主線)
        public const float MinSpacing = 120f;      // 障礙之間
        public const float StraightCurv = 1f / 70f, CurvWindow = 18f, RelaxCurv = 1f / 45f, RelaxWindow = 12f;   // 半徑 >= 70 m 的緩彎 / 直線(賽道很彎,太嚴格會放不下)
        public const float StunSeconds = 0.8f;

        public static bool Enabled = true;
        public static bool DbgLog = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hazdbg") >= 0;
        public static bool Active { get { return active; } }
        public static float Clock { get { return clock; } }
        public static IReadOnlyList<HazardSpot> Spots { get { return spots; } }
        public static int HitCount { get; private set; }
        public static event System.Action<RaceKart, HazardSpot> Hit;
        /// 目前視覺三角形總數(測試用預算)。
        public static int VisualTris { get; private set; }

        static readonly List<HazardSpot> spots = new List<HazardSpot>();
        static readonly List<RaceHazard> merged = new List<RaceHazard>();
        static float clock;
        static bool active;
        static GameObject visRoot;
        static readonly List<Object> owned = new List<Object>();

        // 主題 → 兩種障礙(第 1、3 個位置用 A,第 2 個用 B)
        static readonly HazardKind[,] ThemeKinds =
        {
            { HazardKind.Barrel, HazardKind.Crab },          // 0 Island
            { HazardKind.Boulder, HazardKind.Cactus },       // 1 Canyon
            { HazardKind.Crab, HazardKind.Barrel },          // 2 Coast
            { HazardKind.HayBale, HazardKind.Frog },         // 3 Meadow
            { HazardKind.Tumbleweed, HazardKind.Cactus },    // 4 Desert
            { HazardKind.IceBlock, HazardKind.Snowball },    // 5 Snow
            { HazardKind.Log, HazardKind.Boulder },          // 6 Forest
            { HazardKind.Fireball, HazardKind.Fireball },    // 7 Volcano
        };

        public static HazardKind KindFor(int theme, int slot)
        {
            theme = Mathf.Clamp(theme, 0, ThemeKinds.GetLength(0) - 1);
            return ThemeKinds[theme, (slot & 1) == 0 ? 0 : 1];
        }

        // ------------------------------------------------------------------ 建立 / 清除

        /// 依賽道建模型。visuals = true 才建外觀(需要 parent)。Enabled = false 或賽道為 null = 不建。
        public static void Init(RaceTrack tr, Transform parent, bool visuals = true)
        {
            Shutdown();
            if (!Enabled || tr == null) return;
            Place(tr);
            clock = 0f; HitCount = 0;
            active = spots.Count > 0;
            if (active && visuals && parent != null) BuildVisuals(parent);
        }

        public static void Shutdown()
        {
            active = false;
            spots.Clear(); merged.Clear();
            clock = 0f; HitCount = 0; VisualTris = 0;
            if (visRoot != null) { if (Application.isPlaying) Object.Destroy(visRoot); else Object.DestroyImmediate(visRoot); }
            visRoot = null;
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) { if (Application.isPlaying) Object.Destroy(owned[i]); else Object.DestroyImmediate(owned[i]); }
            owned.Clear();
        }

        /// 比賽時鐘歸零(RaceSession 在 GO / 重開時呼叫)。
        public static void ResetClock() { clock = 0f; }
        public static void SetClock(float t) { clock = t; }

        // ------------------------------------------------------------------ 擺放

        static bool ValidS(RaceTrack tr, float s, bool relax)
        {
            float L = tr.Length;
            if (s < StartClear || s > L - EndClear) return false;
            if (tr.InGap(s, GapBefore, GapAfter)) return false;
            if (Mathf.Abs(tr.MaxCurvature(s - (relax ? RelaxWindow : CurvWindow), s + (relax ? RelaxWindow : CurvWindow))) > (relax ? RelaxCurv : StraightCurv)) return false;
            var sc = tr.Shortcuts;
            for (int i = 0; i < sc.Count; i++)
            {
                if (Mathf.Abs(s - sc[i].FromS) < ShortcutClear || Mathf.Abs(s - sc[i].ToS) < ShortcutClear) return false;
            }
            var ft = tr.Features;
            for (int i = 0; i < ft.Count; i++)
            {
                TrackFeature f = ft[i];
                if (f.path >= 0) continue;
                if (Mathf.Abs(s - f.s) < f.length * 0.5f + FeatureClear) return false;
            }
            return true;
        }

        static bool FarFromOthers(RaceTrack tr, float s)
        {
            float L = tr.Length;
            for (int j = 0; j < spots.Count; j++) { float dd = Mathf.Abs(spots[j].S - s); dd = Mathf.Min(dd, L - dd); if (dd < MinSpacing) return false; }
            return true;
        }

        static void Place(RaceTrack tr)
        {
            float L = tr.Length;
            float[] frac = { 0.22f, 0.50f, 0.76f };
            for (int pass = 0; pass < 2; pass++)
            {
                bool relax = pass == 1;
                if (relax && spots.Count >= 2) break;
                for (int t = 0; t < frac.Length && (!relax || spots.Count < 3); t++)
                {
                    float target = L * frac[t];
                    for (float d = 0f; d < (relax ? L : 300f); d += 6f)
                    {
                        float s = target + d;
                        if (s > L) s -= L;
                        if (!ValidS(tr, s, relax) || !FarFromOthers(tr, s)) continue;
                        HazardSpot h = Make(tr, spots.Count, s);
                        h.Relaxed = relax;
                        spots.Add(h);
                        break;
                    }
                }
            }
            // 依弧長排序後重新編號(種類 / 方向 / 相位由編號決定,同賽道永遠相同)
            spots.Sort((x, y) => x.S.CompareTo(y.S));
            var sorted = new List<HazardSpot>(spots);
            spots.Clear();
            for (int i = 0; i < sorted.Count; i++) { HazardSpot h = Make(tr, i, sorted[i].S); h.Relaxed = sorted[i].Relaxed; spots.Add(h); }
        }

        static HazardSpot Make(RaceTrack tr, int idx, float s)
        {
            TrackSample sm = tr.SampleAt(s);
            var h = new HazardSpot { Index = idx, S = sm.S, Dir = ((idx + tr.Theme) & 1) == 0 ? 1 : -1 };
            h.Kind = KindFor(tr.Theme, idx);
            h.HalfWidth = sm.Width * 0.5f; h.Extent = h.HalfWidth + 2.5f;
            h.Pos = sm.Pos; h.Right = sm.Right; h.Tangent = sm.Tangent; h.Bank = sm.Bank;
            h.Phase = idx * 2.3f + (tr.Theme * 0.7f);
            h.HitLift = 1.0f;
            switch (h.Kind)
            {
                case HazardKind.Barrel: h.Motion = HazardMotion.Roll; h.Radius = 0.8f; h.Speed = 7.5f; h.Period = 7.5f; break;
                case HazardKind.HayBale: h.Motion = HazardMotion.Roll; h.Radius = 0.95f; h.Speed = 6.5f; h.Period = 8f; break;
                case HazardKind.Boulder: h.Motion = HazardMotion.Roll; h.Radius = 1.25f; h.Speed = 8.5f; h.Period = 8f; break;
                case HazardKind.Tumbleweed: h.Motion = HazardMotion.Roll; h.Radius = 1.1f; h.Speed = 9f; h.Period = 7f; break;
                case HazardKind.Snowball: h.Motion = HazardMotion.Roll; h.Radius = 1.2f; h.Speed = 8f; h.Period = 8f; break;
                case HazardKind.IceBlock: h.Motion = HazardMotion.Slide; h.Radius = 1.35f; h.Speed = 5.5f; h.Period = 9f; break;
                case HazardKind.Log: h.Motion = HazardMotion.Drop; h.Radius = 0.85f; h.Speed = 7.5f; h.Period = 9f; h.FallH = 14f; h.FallT = 1.1f; break;
                case HazardKind.Fireball: h.Motion = HazardMotion.Bounce; h.Radius = 0.85f; h.Speed = 9f; h.Period = 6.5f; h.HopAmp = 2.4f; h.HopRate = 1.1f; h.HitLift = 1.1f; break;
                case HazardKind.Frog: h.Motion = HazardMotion.Hop; h.Radius = 0.8f; h.Speed = 5f; h.Period = 7f; h.HopAmp = 1.5f; h.HopRate = 0.9f; h.HitLift = 0.8f; break;
                case HazardKind.Crab: h.Motion = HazardMotion.Hop; h.Radius = 0.9f; h.Speed = 5.5f; h.Period = 7f; h.HopAmp = 0.9f; h.HopRate = 1.3f; h.HitLift = 0.7f; break;
                default: h.Motion = HazardMotion.Hop; h.Radius = 0.9f; h.Speed = 5f; h.Period = 7.5f; h.HopAmp = 1.3f; h.HopRate = 0.8f; h.HitLift = 0.8f; break;   // Cactus
            }
            h.Period = Mathf.Max(h.Period, h.TravelTime + 2.5f);
            return h;
        }

        // ------------------------------------------------------------------ 純函式:時間 → 狀態

        /// 在時鐘 t 時的橫向位置(右正)、離地高度、是否在路上行進。純函式,同樣的 t 永遠同樣結果。
        public static void StateAt(HazardSpot h, float t, out float lat, out float lift, out bool moving)
        {
            float tt = Mathf.Repeat(t + h.Phase, h.Period);
            moving = tt < h.TravelTime;
            lift = 0f;
            if (!moving) { lat = h.Dir * h.Extent; return; }
            float move = tt;
            switch (h.Motion)
            {
                case HazardMotion.Drop:
                    if (tt < h.FallT) { float u = tt / h.FallT; lift = h.FallH * (1f - u * u); move = 0f; }
                    else move = tt - h.FallT;
                    break;
                case HazardMotion.Hop:
                case HazardMotion.Bounce:
                    lift = h.HopAmp * Mathf.Abs(Mathf.Sin(Mathf.PI * tt * h.HopRate));
                    break;
            }
            lat = h.Dir * (-h.Extent + h.Speed * move);
        }

        /// 世界座標的中心(球心)與「現在打不打得到」。
        public static Vector3 WorldPos(HazardSpot h, float lat, float lift)
        {
            float bank = h.Bank != 0f ? -lat * Mathf.Tan(h.Bank * Mathf.Deg2Rad) : 0f;
            return h.Pos + h.Right * lat + Vector3.up * (bank + lift + h.Radius * 0.9f);
        }

        public static bool Hittable(HazardSpot h, float lat, float lift, bool moving)
        {
            return moving && Mathf.Abs(lat) < h.HalfWidth + h.Radius && lift < h.HitLift;
        }

        // ------------------------------------------------------------------ 每個物理步

        /// RaceSession.FixedStep 呼叫(在 RaceItems.Tick 之後):推進時鐘、命中判定、更新 AI 避障清單。
        public static void Tick(float dt)
        {
            if (!active) return;
            clock += dt;
            merged.Clear();
            for (int i = 0; i < spots.Count; i++)
            {
                HazardSpot h = spots[i];
                float lat, lift; bool mv;
                StateAt(h, clock, out lat, out lift, out mv);
                if (!Hittable(h, lat, lift, mv)) continue;
                Vector3 p = WorldPos(h, lat, lift);
                if (DbgLog && RaceSession.Player != null && Vector3.Distance(RaceSession.Player.Position, p) < 40f) Debug.Log("[HazDbg] race=" + RaceSession.RaceTime.ToString("0.0") + " hazard#" + i + " " + h.Kind + " dist=" + Vector3.Distance(RaceSession.Player.Position, p).ToString("0") + " lat=" + lat.ToString("0.0"));
                merged.Add(new RaceHazard { Pos = p, Vel = h.Right * (h.Dir * h.Speed), Radius = h.Radius + 0.5f, Kind = ItemKind.Bumper, OwnerKartId = -1 });
                var all = RaceKart.All;
                for (int k = 0; k < all.Count; k++)
                {
                    RaceKart kart = all[k];
                    if (kart == null) continue;
                    TryHit(kart, h, p);
                }
            }
        }

        static void TryHit(RaceKart kart, HazardSpot h, Vector3 center)
        {
            Vector3 d = kart.Position - center;
            float hr = h.Radius * 0.85f + 0.95f;
            if (d.x * d.x + d.z * d.z > hr * hr) return;
            float groundY = center.y - h.Radius * 0.9f;                 // 障礙底部
            float ky = kart.Position.y;
            if (ky > groundY + h.Radius * 2f + 0.6f || ky < groundY - 1.3f) return;   // 騰空飛過 / 在坑底
            if (kart.Invincible || kart.Stunned || ItemEffects.Hopping(kart)) return;
            if (kart.Stun(StunSeconds))
            {
                kart.SetInvincible(StunSeconds + RaceItems.PostStunInvincible);
                HitCount++;
                if (Hit != null) Hit(kart, h);
            }
        }

        /// 給 RaceAI.HazardSource:道具障礙 + 目前在路上的移動障礙(Tick 更新)。
        public static IReadOnlyList<RaceHazard> MergedWith(IReadOnlyList<RaceHazard> items)
        {
            if (!active || merged.Count == 0) return items;
            var list = scratch;
            list.Clear();
            if (items != null) for (int i = 0; i < items.Count; i++) list.Add(items[i]);
            for (int i = 0; i < merged.Count; i++) list.Add(merged[i]);
            return list;
        }
        static readonly List<RaceHazard> scratch = new List<RaceHazard>();
        public static IReadOnlyList<RaceHazard> Current { get { return merged; } }

        // ------------------------------------------------------------------ 外觀

        sealed class HazardView
        {
            public HazardSpot Spot;
            public Transform Root, Body;
            public bool Lying;      // 圓柱橫躺(軸向 = 行進方向),滾動時繞軸旋轉
            public bool Spin;       // 滾動類:依位移旋轉
        }

        sealed class Runner : MonoBehaviour
        {
            public readonly List<HazardView> Views = new List<HazardView>();
            void Update()
            {
                float t = clock + Mathf.Clamp(Time.time - Time.fixedTime, 0f, Time.fixedDeltaTime);
                for (int i = 0; i < Views.Count; i++)
                {
                    HazardView v = Views[i];
                    HazardSpot h = v.Spot;
                    float lat, lift; bool mv;
                    StateAt(h, t, out lat, out lift, out mv);
                    bool show = mv && Mathf.Abs(lat) < h.Extent + 0.5f;
                    if (v.Root.gameObject.activeSelf != show) v.Root.gameObject.SetActive(show);
                    if (!show) continue;
                    v.Root.position = WorldPos(h, lat, lift);
                    Quaternion baseRot = Quaternion.LookRotation(h.Tangent, Vector3.up);
                    if (v.Spin)
                    {
                        float ang = -h.Dir * (lat / Mathf.Max(0.3f, h.Radius)) * Mathf.Rad2Deg;
                        if (h.Motion == HazardMotion.Drop) ang += (1f - Mathf.Clamp01((Mathf.Repeat(t + h.Phase, h.Period)) / h.FallT)) * 540f;
                        v.Root.rotation = Quaternion.AngleAxis(ang, h.Tangent) * baseRot;
                    }
                    else
                    {
                        // 面向行進方向;跳躍類依高度微微前傾
                        Vector3 dir = h.Right * h.Dir;
                        v.Root.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    }
                }
            }
        }

        static void BuildVisuals(Transform parent)
        {
            visRoot = new GameObject("RaceHazards");
            visRoot.transform.SetParent(parent, false);
            var runner = visRoot.AddComponent<Runner>();
            Material mat = RaceShaders.ToonVC();
            VisualTris = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                HazardSpot h = spots[i];
                var go = new GameObject("Hazard_" + h.Kind);
                go.transform.SetParent(visRoot.transform, false);
                var body = new GameObject("Body");
                body.transform.SetParent(go.transform, false);
                RaceProto proto = BuildProto(h.Kind);
                var acc = new RaceColorAcc();
                acc.Add(proto, Vector3.zero, 0f, Vector3.one, new Color32(255, 255, 255, 255));
                Mesh m = acc.ToMesh("Hazard_" + h.Kind);
                owned.Add(m);
                VisualTris += acc.TriCount;
                body.AddComponent<MeshFilter>().sharedMesh = m;
                var r = body.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat != null ? mat : RaceArt.Mat(Color.white, 0.1f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                bool lying = h.Kind == HazardKind.Barrel || h.Kind == HazardKind.HayBale || h.Kind == HazardKind.Log;
                if (lying) body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 圓柱軸向 y → 行進方向(本地 z)
                bool spin = h.Motion == HazardMotion.Roll || h.Motion == HazardMotion.Drop;
                go.SetActive(false);
                runner.Views.Add(new HazardView { Spot = h, Root = go.transform, Body = body.transform, Lying = lying, Spin = spin });
            }
        }

        static Color Hc(string hex) { Color c; ColorUtility.TryParseHtmlString(hex, out c); return c; }

        static RaceProto BuildProto(HazardKind kind)
        {
            var b = new RaceProtoBuilder();
            switch (kind)
            {
                case HazardKind.Barrel:
                    b.Lathe(Vector3.zero, new[] { 0.52f, 0.68f, 0.78f, 0.78f, 0.68f, 0.52f }, new[] { -0.8f, -0.5f, -0.2f, 0.2f, 0.5f, 0.8f },
                        new[] { Hc("#5B3A1E"), Hc("#A8693A"), Hc("#B8793F"), Hc("#B8793F"), Hc("#A8693A"), Hc("#5B3A1E") }, 8, true, true);
                    b.Lathe(Vector3.zero, new[] { 0.80f, 0.80f }, new[] { -0.32f, -0.2f }, new[] { Hc("#3A3A44"), Hc("#3A3A44") }, 8, false, false);
                    b.Lathe(Vector3.zero, new[] { 0.80f, 0.80f }, new[] { 0.2f, 0.32f }, new[] { Hc("#3A3A44"), Hc("#3A3A44") }, 8, false, false);
                    break;
                case HazardKind.HayBale:
                    b.Lathe(Vector3.zero, new[] { 0.9f, 0.95f, 0.95f, 0.9f }, new[] { -0.75f, -0.6f, 0.6f, 0.75f },
                        new[] { Hc("#C79B34"), Hc("#E8C45A"), Hc("#F0D070"), Hc("#C79B34") }, 9, true, true);
                    b.Lathe(Vector3.zero, new[] { 0.97f, 0.97f }, new[] { -0.2f, -0.1f }, new[] { Hc("#B4472F"), Hc("#B4472F") }, 9, false, false);
                    b.Lathe(Vector3.zero, new[] { 0.97f, 0.97f }, new[] { 0.1f, 0.2f }, new[] { Hc("#B4472F"), Hc("#B4472F") }, 9, false, false);
                    break;
                case HazardKind.Log:
                    b.Lathe(Vector3.zero, new[] { 0.78f, 0.85f, 0.85f, 0.78f }, new[] { -1.3f, -1.15f, 1.15f, 1.3f },
                        new[] { Hc("#E2BE86"), Hc("#7A4B27"), Hc("#6B4021"), Hc("#E2BE86") }, 8, true, true);
                    b.Lathe(Vector3.zero, new[] { 0.9f, 0.9f }, new[] { -0.15f, 0.15f }, new[] { Hc("#8A5A30"), Hc("#8A5A30") }, 8, false, false);
                    break;
                case HazardKind.Boulder:
                    b.Rock(Vector3.zero, new Vector3(1.25f, 1.15f, 1.25f), 31, Hc("#8E5F47"), Hc("#C98E62"));
                    break;
                case HazardKind.Tumbleweed:
                    b.Rock(Vector3.zero, new Vector3(1.1f, 1.1f, 1.1f), 77, Hc("#8A6234"), Hc("#C99B58"));
                    b.Rock(Vector3.zero, new Vector3(0.8f, 0.8f, 1.2f), 78, Hc("#A67C45"), Hc("#D9AE6A"));
                    break;
                case HazardKind.Snowball:
                    b.Blob(Vector3.zero, new Vector3(1.2f, 1.2f, 1.2f), 1, Hc("#B9D2EA"), Hc("#F6FBFF"));
                    break;
                case HazardKind.IceBlock:
                    b.Rock(Vector3.zero, new Vector3(1.4f, 0.95f, 1.4f), 5, Hc("#6FB6E6"), Hc("#E4F7FF"));
                    b.Rock(new Vector3(0.5f, 0.55f, 0.2f), new Vector3(0.6f, 0.45f, 0.6f), 6, Hc("#8CCBF2"), Hc("#FFFFFF"));
                    break;
                case HazardKind.Fireball:
                    b.Blob(Vector3.zero, new Vector3(0.85f, 0.85f, 0.85f), 0, new Color(1f, 0.30f, 0.02f, 0.12f), new Color(1f, 0.82f, 0.2f, 0.12f));
                    b.Blob(new Vector3(0f, 0.15f, 0.7f), new Vector3(0.5f, 0.5f, 0.6f), 0, new Color(1f, 0.25f, 0.02f, 0.2f), new Color(1f, 0.55f, 0.1f, 0.2f));
                    b.Blob(new Vector3(0f, 0.25f, 1.25f), new Vector3(0.3f, 0.3f, 0.45f), 0, new Color(0.9f, 0.2f, 0.02f, 0.3f), new Color(1f, 0.4f, 0.05f, 0.3f));
                    break;
                case HazardKind.Frog:
                    b.Blob(new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.42f, 0.75f), 0, Hc("#3E9E3A"), Hc("#7EE063"));
                    b.Blob(new Vector3(-0.28f, 0.88f, 0.4f), new Vector3(0.16f, 0.18f, 0.16f), 0, Color.white, Color.white);
                    b.Blob(new Vector3(0.28f, 0.88f, 0.4f), new Vector3(0.16f, 0.18f, 0.16f), 0, Color.white, Color.white);
                    b.Blob(new Vector3(-0.28f, 0.9f, 0.52f), new Vector3(0.07f, 0.09f, 0.06f), 0, Hc("#202020"), Hc("#202020"));
                    b.Blob(new Vector3(0.28f, 0.9f, 0.52f), new Vector3(0.07f, 0.09f, 0.06f), 0, Hc("#202020"), Hc("#202020"));
                    b.Blob(new Vector3(-0.5f, 0.2f, -0.3f), new Vector3(0.16f, 0.2f, 0.4f), 0, Hc("#3E9E3A"), Hc("#5CC24A"));
                    b.Blob(new Vector3(0.5f, 0.2f, -0.3f), new Vector3(0.16f, 0.2f, 0.4f), 0, Hc("#3E9E3A"), Hc("#5CC24A"));
                    break;
                case HazardKind.Crab:
                    b.Blob(new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.36f, 0.55f), 0, Hc("#C23A2B"), Hc("#FF6A4D"));
                    b.Blob(new Vector3(-0.85f, 0.55f, 0.25f), new Vector3(0.3f, 0.22f, 0.3f), 0, Hc("#C23A2B"), Hc("#FF7A5A"));
                    b.Blob(new Vector3(0.85f, 0.55f, 0.25f), new Vector3(0.3f, 0.22f, 0.3f), 0, Hc("#C23A2B"), Hc("#FF7A5A"));
                    b.Blob(new Vector3(-0.25f, 0.8f, 0.3f), new Vector3(0.1f, 0.12f, 0.1f), 0, Color.white, Color.white);
                    b.Blob(new Vector3(0.25f, 0.8f, 0.3f), new Vector3(0.1f, 0.12f, 0.1f), 0, Color.white, Color.white);
                    break;
                default:   // Cactus
                    b.Lathe(Vector3.zero, new[] { 0.5f, 0.52f, 0.46f, 0.1f }, new[] { 0f, 0.4f, 1.2f, 1.5f }, new[] { Hc("#2E7D32"), Hc("#3FA34D"), Hc("#4CB85A"), Hc("#4CB85A") }, 8, true, false);
                    b.Tube(new Vector3(-0.4f, 0.8f, 0f), new Vector3(-0.85f, 0.85f, 0f), 0.16f, 0.15f, 6, Hc("#3FA34D"), Hc("#3FA34D"));
                    b.Tube(new Vector3(-0.85f, 0.85f, 0f), new Vector3(-0.85f, 1.3f, 0f), 0.15f, 0.12f, 6, Hc("#3FA34D"), Hc("#4CB85A"));
                    b.Blob(new Vector3(0f, 1.55f, 0f), new Vector3(0.14f, 0.1f, 0.14f), 0, Hc("#FF6FA8"), Hc("#FFB3D0"));
                    break;
            }
            return b.Build();
        }
    }
}
