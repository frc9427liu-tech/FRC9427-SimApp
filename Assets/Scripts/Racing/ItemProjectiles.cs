using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrcSim
{
    /// 場上的投射物 / 障礙(直線彈、追蹤彈、油漬)。內部狀態,外部只讀。
    public sealed class Projectile
    {
        public ItemKind Kind;
        public RaceKart Owner;
        public int OwnerId;
        public GameObject Go;
        public Vector3 Pos, Dir;
        public float Speed, Life, Age, Radius, Hover;
        public float SHint = -1f, TSHint = -1f;
        public int Bounces;
        public RaceKart Target;
        public bool Dead;
        // 模組 I 擴充(炸彈 / 藍殼 / 假箱)
        public float S, Lat, Lift, RetargetT;
        public bool Armed;
        public Vector3 From, To;
        public Transform Spark;
        // 2026-10-24 道具設計 v2
        public bool NoHome;                                 // 追蹤彈往後丟(夾角 > 90 度):不鎖定、不轉向,行為同直線彈
        public float RetT;                                  // 迴力鏢:回程已經飛了多久(轉頭後加速用)
        public bool Returning;                              // 迴力鏢:已經轉頭飛回主人
        public readonly List<RaceKart> HitThisPhase = new List<RaceKart>();   // 迴力鏢:這一段(去 / 回)已經打過的車
        public Transform Spin;                              // 迴力鏢:自轉的子物件
    }

    /// 道具物件的生成 / 移動 / 碰撞(模組 I / M4)。全部程式生成簡單幾何,沒有物理碰撞體:
    /// 命中用「線段 vs 車心距離」判定,牆面用賽道邊界(HalfWidth + 邊距)模擬,所以 headless 也能跑。
    /// 命中結果交給 RaceItems.Resolve(護盾 / 無敵 / 暈眩 / 事件)。
    public static class ItemProjectiles
    {
        public const float BumperSpeed = 40f, BumperLife = 4f, BumperStun = 1.0f, BumperRadius = 0.5f;
        public const float HomingSpeed = 32f, HomingLife = 8f, HomingStun = 1.4f, HomingRadius = 0.55f;
        public const float OilLife = 25f, OilRadius = 1.0f, OilThrowDistance = 8f, OilDropBehind = 2.6f;   // Oil = 香蕉皮(ItemKind.Oil 數值保留)
        public const float BananaSpin = 1.0f;                         // 踩到香蕉皮:打轉 1.0 s(Stun)並掉 1 枚金幣
        public const int BananaCoinLoss = 1;
        public const float BoomSpeed = 34f, BoomOut = 0.7f, BoomLife = 3.2f, BoomStun = 0.9f, BoomRadius = 0.5f, BoomCatch = 1.6f, BoomTurn = 9f;
        public const int MaxBounces = 3;
        public const float WallMargin = 0.8f;      // 路邊「牆」在 HalfWidth 之外多少
        public const float KartHitRadius = 0.8f;
        public const float OwnerGrace = 0.6f, OilOwnerGrace = 1.0f;
        public const float HomingTurnRate = 7f;    // rad/s

        public const float BombLife = 3f, BombRadius = 6f, BombStun = 1.6f, BombTrigger = 1.0f, BombKnock = 14f, BombKnockUp = 5f;
        public const float BombLobDistance = 10f, BombDropBehind = 3f, BombFlight = 0.4f, BombOwnerGrace = 1.0f;
        public const float ShellSpeed = 55f, ShellLife = 30f, ShellBlast = 5f, ShellStun = 2.0f, ShellTrigger = 3.2f, ShellKnock = 14f;
        // 火焰花(2026-10-23):三連火球,直線飛、牆上只反彈 1 次
        public const float FireSpeed = 36f, FireLife = 3.5f, FireStun = 0.9f, FireRadius = 0.45f;
        public const int FireMaxBounces = 1;
        public const float HornRadius = 12f, HornGraceAge = 0.6f;     // 超級喇叭:範圍內的障礙 / 投射物全部消除(自己剛丟出的除外)
        public const float FakeLife = 40f, FakeStun = 1.2f, FakeRadius = 1.0f, FakeThrow = 14f, FakeDropBehind = 2.8f, FakeOwnerGrace = 2.5f;

        static readonly List<Projectile> list = new List<Projectile>();
        static RaceTrack track;
        static Transform root;

        static Mesh icoMesh, coneMesh, discMesh, peelMesh;
        static readonly Color BlastOrange = new Color(1f, 0.55f, 0.1f), BlastBlue = new Color(0.3f, 0.6f, 1f);

        /// 累計反彈次數(測試用)。
        public static int TotalBounces;

        public static IReadOnlyList<Projectile> All { get { return list; } }

        public static void Init(RaceTrack t, Transform parent)
        {
            Shutdown();
            track = t;
            var g = new GameObject("ItemProjectiles");
            if (parent != null) g.transform.SetParent(parent, false);
            root = g.transform;
            TotalBounces = 0;
        }

        public static void Shutdown()
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Go != null) Object.Destroy(list[i].Go);
            list.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null; track = null;
        }

        /// 清掉場上所有投射物 / 障礙(不發事件;測試用)。
        public static void ClearAll()
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Go != null) Object.Destroy(list[i].Go);
            list.Clear();
        }

        public static int Count(ItemKind kind)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (!list[i].Dead && list[i].Kind == kind) n++;
            return n;
        }

        /// 這台車是否被追蹤彈鎖定。
        public static bool IsTargeting(RaceKart k)
        {
            if (k == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                Projectile p = list[i];
                if (!p.Dead && ((p.Kind == ItemKind.Homing && !p.NoHome) || p.Kind == ItemKind.BlueShell) && p.Target == k) return true;
            }
            return false;
        }

        public static void FillHazards(List<RaceHazard> dst)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Projectile p = list[i];
                if (p.Dead) continue;
                dst.Add(new RaceHazard
                {
                    Pos = p.Pos,
                    Vel = (p.Kind == ItemKind.Oil || p.Kind == ItemKind.Bomb || p.Kind == ItemKind.FakeBox) ? Vector3.zero : p.Dir * p.Speed,
                    Radius = p.Radius,
                    Kind = p.Kind,
                    OwnerKartId = p.OwnerId
                });
            }
        }

        // ------------------------------------------------------------------ 生成

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 1e-6f) return Vector3.forward;
            return v.normalized;
        }

        static float GroundY(Vector3 p, float fallback)
        {
            GroundHit gh;
            if (RaceKart.Ground != null && RaceKart.Ground(p, out gh)) return gh.Height;
            return fallback;
        }

        /// 直線彈:aimBack = 往後射。
        public static Projectile FireBumper(RaceKart owner, bool aimBack)
        {
            Vector3 f = Flat(owner.Forward);
            return FireBumperAt(owner, aimBack ? -f : f);
        }

        /// 直線彈:任意水平方向(世界座標單位向量;瞄準系統用)。
        public static Projectile FireBumperAt(RaceKart owner, Vector3 dir)
        {
            dir = Flat(dir);
            return FireBumper(owner, owner.Position + dir * 2.2f, dir);
        }

        /// 火焰花的一發火球:任意方向,牆上只反彈 FireMaxBounces 次。
        public static Projectile FireFlowerAt(RaceKart owner, Vector3 dir)
        {
            dir = Flat(dir);
            return FireFlower(owner, owner.Position + dir * 2.2f, dir);
        }

        /// 火球(指定出發點與方向;測試與特殊用途)。
        public static Projectile FireFlower(RaceKart owner, Vector3 pos, Vector3 dir)
        {
            if (root == null) return null;
            var p = NewProj(ItemKind.FireFlower, owner, pos, Flat(dir), FireSpeed, FireLife, FireRadius, 0.5f);
            BuildFireVisual(p);
            return p;
        }

        /// 迴力鏢花:往 dir 飛出 BoomOut 秒後轉頭飛回主人;去程 / 回程各能打到每台車一次(穿過不消失)。
        public static Projectile FireBoomerangAt(RaceKart owner, Vector3 dir)
        {
            if (root == null) return null;
            dir = Flat(dir);
            var p = NewProj(ItemKind.Boomerang, owner, owner.Position + dir * 2.0f, dir, BoomSpeed, BoomLife, BoomRadius, 0.6f);
            BuildBoomerangVisual(p);
            return p;
        }

        /// 直線彈(指定出發點與方向;測試與特殊用途)。
        public static Projectile FireBumper(RaceKart owner, Vector3 pos, Vector3 dir)
        {
            if (root == null) return null;
            var p = NewProj(ItemKind.Bumper, owner, pos, Flat(dir), BumperSpeed, BumperLife, BumperRadius, 0.5f);
            BuildBumperVisual(p);
            return p;
        }

        /// 追蹤彈:鎖定名次在前一名的車(領先者沒有前車 → 沿賽道飛,途中鎖定前方最近的車)。
        public static Projectile FireHoming(RaceKart owner) { return FireHomingAt(owner, owner.Forward); }

        /// 追蹤彈:發射方向可任意(之後照原本規則轉向目標)。
        public static Projectile FireHomingAt(RaceKart owner, Vector3 dir)
        {
            if (root == null) return null;
            Vector3 f = Flat(dir);
            var p = NewProj(ItemKind.Homing, owner, owner.Position + f * 2.0f, f, HomingSpeed, HomingLife, HomingRadius, 0.6f);
            // 像真的紅龜殼:只有往前丟(與車頭夾角 <= 90 度)才鎖定前車;往後丟 = 直線彈(沿直線飛、撞牆反彈),不鎖定也不亮警告
            p.NoHome = Vector3.Dot(f, Flat(owner.Forward)) < -0.001f;
            p.Target = p.NoHome ? null : RaceItems.PickHomingTarget(owner);
            BuildHomingVisual(p);
            return p;
        }

        /// 油漬:預設丟在車後;aimBack = true(手把下)則往前丟 8 m。
        public static Projectile DropOil(RaceKart owner, bool throwForward)
        {
            Vector3 f = Flat(owner.Forward);
            return DropOilAt(owner, throwForward ? f : -f);
        }

        // 瞄準距離:朝車頭方向丟得遠(behind → ahead),朝後方就落在車後;側向取中間(dir 與車頭的夾角內插)
        static float AimDist(RaceKart owner, Vector3 dir, float behind, float ahead)
        {
            float dot = Vector3.Dot(Flat(dir), Flat(owner.Forward));
            return Mathf.Lerp(behind, ahead, Mathf.Clamp01(0.5f + 0.5f * dot));
        }

        /// 油漬:任意方向(朝後 2.6 m ~ 朝前 8 m,側向約 5.3 m)。
        public static Projectile DropOilAt(RaceKart owner, Vector3 dir)
        {
            if (root == null) return null;
            Vector3 f = Flat(owner.Forward);
            dir = Flat(dir);
            Vector3 pos = owner.Position + dir * AimDist(owner, dir, OilDropBehind, OilThrowDistance);
            var p = NewProj(ItemKind.Oil, owner, pos, f, 0f, OilLife, OilRadius, 0.04f);
            BuildOilVisual(p);
            return p;
        }

        /// 炸彈:預設往前拋 10 m 落地;aimBack(手把下)= 丟在車後 3 m。落地後 3 s 或被碰到就爆炸。
        public static Projectile PlaceBomb(RaceKart owner, bool aimBack)
        {
            Vector3 f = Flat(owner.Forward);
            return PlaceBombAt(owner, aimBack ? -f : f);
        }

        /// 炸彈:任意方向拋(朝後 3 m ~ 朝前 10 m)。
        public static Projectile PlaceBombAt(RaceKart owner, Vector3 dir)
        {
            if (root == null) return null;
            dir = Flat(dir);
            Vector3 to = owner.Position + dir * AimDist(owner, dir, BombDropBehind, BombLobDistance);
            var p = PlaceBomb(owner, to);
            if (p != null) { p.From = owner.Position; p.From.y = p.Pos.y; p.Armed = false; }
            return p;
        }

        /// 炸彈(指定落點,立刻武裝;測試與特殊用途)。
        public static Projectile PlaceBomb(RaceKart owner, Vector3 pos)
        {
            if (root == null) return null;
            var p = NewProj(ItemKind.Bomb, owner, pos, Flat(owner != null ? owner.Forward : Vector3.forward), 0f, BombLife, 1.0f, 0.35f);
            p.To = p.Pos; p.From = p.Pos; p.Armed = true;
            BuildBombVisual(p);
            return p;
        }

        /// 假道具箱:預設丟在車後;aimBack = true(手把下)則往前丟 14 m(同油漬的慣例)。
        public static Projectile PlaceFakeBox(RaceKart owner, bool throwForward)
        {
            Vector3 f = Flat(owner.Forward);
            return PlaceFakeBoxAt(owner, throwForward ? f : -f);
        }

        /// 假道具箱:任意方向(朝後 2.8 m ~ 朝前 14 m)。
        public static Projectile PlaceFakeBoxAt(RaceKart owner, Vector3 dir)
        {
            if (root == null) return null;
            Vector3 f = Flat(owner.Forward);
            dir = Flat(dir);
            Vector3 pos = owner.Position + dir * AimDist(owner, dir, FakeDropBehind, FakeThrow);
            var p = NewProj(ItemKind.FakeBox, owner, pos, f, 0f, FakeLife, FakeRadius, 1.0f);
            var box = ItemBox.CreateFake(root, p.Pos);
            p.Go = box.gameObject;
            return p;
        }

        /// 藍殼:沿賽道樣條高速追第一名,接近時爆炸(範圍傷害)。
        public static Projectile FireBlueShell(RaceKart owner)
        {
            if (root == null) return null;
            Vector3 f = Flat(owner.Forward);
            var p = NewProj(ItemKind.BlueShell, owner, owner.Position + f * 2f, f, ShellSpeed, ShellLife, 0.6f, 0.8f);
            float lat;
            p.S = track.Project(owner.Position, -1f, out lat);
            p.Lat = lat;
            p.Target = RaceItems.PickFirst(owner);
            p.TSHint = -1f;
            BuildShellVisual(p);
            return p;
        }

        static Projectile NewProj(ItemKind kind, RaceKart owner, Vector3 pos, Vector3 dir, float speed, float life, float radius, float hover)
        {
            var p = new Projectile
            {
                Kind = kind, Owner = owner, OwnerId = owner != null ? owner.KartId : -1,
                Dir = dir, Speed = speed, Life = life, Radius = radius, Hover = hover
            };
            float gy = GroundY(pos, owner != null ? owner.Position.y - 0.45f : pos.y - hover);
            pos.y = gy + hover;
            p.Pos = pos;
            list.Add(p);
            return p;
        }

        // ------------------------------------------------------------------ 視覺

        static GameObject Visual(Projectile p, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root, false);
            g.transform.position = p.Pos;
            g.transform.rotation = Quaternion.LookRotation(p.Dir, Vector3.up);
            p.Go = g;
            return g;
        }

        static GameObject MeshChild(Transform parent, string name, Mesh mesh, Vector3 lp, Vector3 scale, Material m)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = lp; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        static void AddTrail(GameObject g, Color c)
        {
            var tr = g.AddComponent<TrailRenderer>();
            tr.time = 0.35f; tr.startWidth = 0.35f; tr.endWidth = 0f;
            tr.sharedMaterial = RaceArt.Glow(c, 1.3f);
            tr.startColor = c; tr.endColor = new Color(c.r, c.g, c.b, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void BuildBumperVisual(Projectile p)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            Color red = new Color(0.95f, 0.2f, 0.18f);
            var g = Visual(p, "Bumper");
            MeshChild(g.transform, "Ball", icoMesh, Vector3.zero, Vector3.one * 1.0f, RaceArt.Mat(red, 0.7f));
            AddTrail(g, red);
        }

        static void BuildFireVisual(Projectile p)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            Color orange = new Color(1f, 0.5f, 0.08f);
            var g = Visual(p, "Fireball");
            MeshChild(g.transform, "Core", icoMesh, Vector3.zero, Vector3.one * 0.8f, RaceArt.Glow(orange, 2f));
            MeshChild(g.transform, "Heart", icoMesh, Vector3.zero, Vector3.one * 0.45f, RaceArt.Glow(new Color(1f, 0.9f, 0.4f), 2.4f));
            AddTrail(g, orange);
        }

        static void BuildHomingVisual(Projectile p)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            if (coneMesh == null) coneMesh = RaceArt.Cone(8);
            Color blue = new Color(0.2f, 0.5f, 1f);
            var g = Visual(p, "Homing");
            MeshChild(g.transform, "Ball", icoMesh, Vector3.zero, Vector3.one * 1.1f, RaceArt.Mat(blue, 0.7f));
            var cone = MeshChild(g.transform, "Nose", coneMesh, new Vector3(0f, 0f, 0.5f), new Vector3(0.6f, 0.8f, 0.6f), RaceArt.Mat(new Color(0.9f, 0.95f, 1f), 0.5f));
            cone.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 圓錐頂尖 +Y → +Z
            AddTrail(g, blue);
        }

        static void BuildOilVisual(Projectile p)
        {
            if (discMesh == null) discMesh = MakeDisc(24);
            var g = Visual(p, "BananaPeel");
            g.transform.rotation = Quaternion.Euler(0f, Mathf.Abs(p.OwnerId * 53 + (int)(p.Pos.x * 7f + p.Pos.z * 13f)) % 360, 0f);
            Material yellow = RaceArt.Mat(new Color(1f, 0.84f, 0.08f), 0.6f);
            MeshChild(g.transform, "Peel", PeelMesh(), Vector3.zero, Vector3.one * 1.5f, yellow);
            if (coneMesh == null) coneMesh = RaceArt.Cone(8);
            MeshChild(g.transform, "Stem", coneMesh, new Vector3(0f, 0.12f, 0f), new Vector3(0.12f, 0.2f, 0.12f), RaceArt.Mat(new Color(0.42f, 0.27f, 0.08f), 0.5f));
        }

        static void BuildBoomerangVisual(Projectile p)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            Color c = new Color(1f, 0.72f, 0.1f);
            var g = Visual(p, "Boomerang");
            var spin = new GameObject("Spin");
            spin.transform.SetParent(g.transform, false);
            Material m = RaceArt.Glow(c, 1.4f);
            var a = MeshChild(spin.transform, "ArmA", icoMesh, new Vector3(0.28f, 0f, 0.12f), new Vector3(0.8f, 0.12f, 0.2f), m);
            a.transform.localRotation = Quaternion.Euler(0f, 28f, 0f);
            var b = MeshChild(spin.transform, "ArmB", icoMesh, new Vector3(-0.28f, 0f, 0.12f), new Vector3(0.8f, 0.12f, 0.2f), m);
            b.transform.localRotation = Quaternion.Euler(0f, -28f, 0f);
            p.Spin = spin.transform;
            AddTrail(g, c);
        }

        /// 香蕉皮網格(原創):中心一小塊 + 4 片往外翻捲的細長果皮,雙面。本地 y 向上,半徑約 0.55。
        public static Mesh PeelMesh()
        {
            if (peelMesh != null) return peelMesh;
            var v = new List<Vector3>(); var t = new List<int>();
            const int flaps = 4, segs = 5;
            for (int f = 0; f < flaps; f++)
            {
                float ang = f * Mathf.PI * 2f / flaps + 0.35f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), perp = new Vector3(-dir.z, 0f, dir.x);
                int b0 = v.Count;
                for (int i = 0; i <= segs; i++)
                {
                    float u = i / (float)segs;
                    float r = 0.05f + 0.50f * u;
                    float w = 0.15f * (1f - 0.6f * u) + 0.02f;
                    float y = 0.03f + 0.32f * u * u + 0.06f * Mathf.Sin(u * Mathf.PI);   // 往上翻捲
                    Vector3 c = dir * r + Vector3.up * y;
                    v.Add(c + perp * w); v.Add(c - perp * w);
                }
                for (int i = 0; i < segs; i++)
                {
                    int a = b0 + i * 2, bq = a + 1, c2 = a + 2, d = a + 3;
                    t.Add(a); t.Add(c2); t.Add(bq); t.Add(bq); t.Add(c2); t.Add(d);
                }
            }
            int front = t.Count;
            int n = v.Count;
            for (int i = 0; i < n; i++) v.Add(v[i]);                        // 背面(獨立頂點,法線才會反過來)
            for (int i = 0; i < front; i += 3) { t.Add(t[i] + n); t.Add(t[i + 2] + n); t.Add(t[i + 1] + n); }
            var m = new Mesh { name = "BananaPeel" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            peelMesh = m;
            return m;
        }

        static void BuildBombVisual(Projectile p)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            if (coneMesh == null) coneMesh = RaceArt.Cone(8);
            var g = Visual(p, "Bomb");
            g.transform.rotation = Quaternion.identity;
            MeshChild(g.transform, "Body", icoMesh, Vector3.zero, Vector3.one * 1.3f, RaceArt.Mat(new Color(0.08f, 0.08f, 0.1f), 0.6f));
            MeshChild(g.transform, "Cap", coneMesh, new Vector3(0f, 0.55f, 0f), new Vector3(0.35f, 0.3f, 0.35f), RaceArt.Mat(new Color(0.6f, 0.6f, 0.65f), 0.5f));
            var spark = MeshChild(g.transform, "Spark", icoMesh, new Vector3(0f, 0.95f, 0f), Vector3.one * 0.3f, RaceArt.Glow(new Color(1f, 0.6f, 0.1f), 2f));
            p.Spark = spark.transform;
        }

        static void BuildShellVisual(Projectile p)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            if (coneMesh == null) coneMesh = RaceArt.Cone(6);
            Color blue = new Color(0.15f, 0.4f, 1f);
            var g = Visual(p, "BlueShell");
            MeshChild(g.transform, "Shell", icoMesh, Vector3.zero, new Vector3(1.4f, 1.0f, 1.5f), RaceArt.Mat(blue, 0.8f));
            Material white = RaceArt.Mat(Color.white, 0.5f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                var sp = MeshChild(g.transform, "Spike" + i, coneMesh, new Vector3(Mathf.Cos(a) * 0.6f, 0.45f, Mathf.Sin(a) * 0.6f), new Vector3(0.3f, 0.5f, 0.3f), white);
                sp.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 35f, 0f, -Mathf.Cos(a) * 35f);
            }
            MeshChild(g.transform, "WingL", icoMesh, new Vector3(-0.85f, 0.1f, 0f), new Vector3(0.15f, 0.6f, 0.9f), white);
            MeshChild(g.transform, "WingR", icoMesh, new Vector3(0.85f, 0.1f, 0f), new Vector3(0.15f, 0.6f, 0.9f), white);
            AddTrail(g, blue);
        }

        // y=0 平面上、半徑 0.5、朝上的圓盤
        static Mesh MakeDisc(int sides)
        {
            var v = new Vector3[sides + 1];
            var t = new int[sides * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
            }
            for (int i = 0; i < sides; i++)
            {
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % sides; t[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = "OilDisc", vertices = v, triangles = t };
            var n = new Vector3[sides + 1];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        /// 超級喇叭:pos 為圓心 radius 內(水平距離,高度差 &lt; 6 m)所有投射物 / 障礙直接消除(不爆炸、不暈眩)。
        /// 使用者自己剛丟出的(Age &lt; HornGraceAge)除外。回傳消除幾個。
        public static int DestroyNear(Vector3 pos, float radius, RaceKart owner)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Projectile p = list[i];
                if (p.Dead) continue;
                if (owner != null && p.Owner == owner && p.Age < HornGraceAge) continue;
                Vector3 d = p.Pos - pos;
                if (Mathf.Abs(d.y) > 6f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                p.Dead = true; n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ 爆炸

        /// 範圍爆炸:半徑內每台車走 RaceItems.Resolve(護盾 / 無敵規則一致);被炸暈的往外拋。
        public static void Explode(Vector3 pos, float radius, RaceKart owner, ItemKind kind, float stun, float knock)
        {
            var karts = RaceKart.All;
            for (int i = 0; i < karts.Count; i++)
            {
                RaceKart k = karts[i];
                if (k == null) continue;
                Vector3 d = k.Position - pos;
                if (Mathf.Abs(d.y) > 3.5f) continue;
                d.y = 0f;
                float m = d.magnitude;
                if (m > radius) continue;
                bool was = k.Stunned;
                if (RaceItems.Resolve(k, stun, owner, kind) && k.Stunned && !was)
                {
                    Vector3 dir = m > 0.8f ? d / m : -Flat(k.Forward);
                    k.ApplyImpulse(dir * (knock * (1f - 0.5f * m / radius)) + Vector3.up * BombKnockUp);
                }
            }
            RaceItems.RaiseExploded(pos, radius, kind);
            ItemEffects.Blast(pos, radius, kind == ItemKind.BlueShell ? BlastBlue : BlastOrange);
        }

        static void Detonate(Projectile p)
        {
            if (p.Dead) return;
            p.Dead = true;
            if (p.Kind == ItemKind.Bomb) Explode(p.Pos, BombRadius, p.Owner, ItemKind.Bomb, BombStun, BombKnock);
            else Explode(p.Pos, ShellBlast, p.Owner, ItemKind.BlueShell, ShellStun, ShellKnock);
        }

        // ------------------------------------------------------------------ 每步更新

        public static void Step(float dt)
        {
            if (track == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Projectile p = list[i];
                if (!p.Dead) StepOne(p, dt);
                if (p.Dead)
                {
                    if (p.Go != null) Object.Destroy(p.Go);
                    list.RemoveAt(i);
                }
            }
        }

        static void StepOne(Projectile p, float dt)
        {
            p.Age += dt;
            p.Life -= dt;
            if (p.Life <= 0f) { if (p.Kind == ItemKind.Bomb) Detonate(p); p.Dead = true; return; }

            Vector3 old = p.Pos;
            if (p.Kind == ItemKind.Bumper || p.Kind == ItemKind.FireFlower) MoveBumper(p, dt);
            else if (p.Kind == ItemKind.Boomerang) MoveBoomerang(p, dt);
            else if (p.Kind == ItemKind.Homing) { if (p.NoHome) MoveBumper(p, dt); else MoveHoming(p, dt); }
            else if (p.Kind == ItemKind.BlueShell) MoveShell(p, dt);
            else if (p.Kind == ItemKind.Bomb) MoveBomb(p, dt);
            if (p.Dead) return;

            if (p.Kind != ItemKind.Oil)
            {
                float gy = GroundY(p.Pos, p.Pos.y - p.Hover);
                p.Pos.y = gy + p.Hover;
            }

            // 碰撞
            float grace = p.Kind == ItemKind.Oil ? OilOwnerGrace : p.Kind == ItemKind.FakeBox ? FakeOwnerGrace : OwnerGrace;
            var karts = RaceKart.All;
            for (int i = 0; i < karts.Count; i++)
            {
                RaceKart k = karts[i];
                if (k == null) continue;
                if (p.Kind == ItemKind.BlueShell) break;
                if (p.Kind == ItemKind.Bomb)
                {
                    if (!p.Armed || (k == p.Owner && p.Age < BombOwnerGrace)) continue;
                    if (ItemEffects.Hopping(k)) continue;          // 羽毛跳起來:不會踩到炸彈觸發圈
                    if (Touch(old, p.Pos, k.Position, BombTrigger + KartHitRadius)) { Detonate(p); break; }
                    continue;
                }
                if (k == p.Owner && (p.Age < grace || p.Kind == ItemKind.Boomerang)) continue;
                if (p.Kind == ItemKind.Boomerang && p.HitThisPhase.Contains(k)) continue;
                if (!Touch(old, p.Pos, k.Position, p.Radius + KartHitRadius)) continue;
                // 三連香蕉皮拖在車尾:從後面來的「殼」(直線彈 / 追蹤彈 / 火球 / 迴力鏢)會先打掉一片香蕉皮
                if ((p.Kind == ItemKind.Bumper || p.Kind == ItemKind.Homing || p.Kind == ItemKind.FireFlower || p.Kind == ItemKind.Boomerang)
                    && RaceItems.TrailBlock(k, p.Pos, p.Kind)) { p.Dead = true; break; }
                float stun = p.Kind == ItemKind.Homing ? HomingStun : p.Kind == ItemKind.FakeBox ? FakeStun : p.Kind == ItemKind.FireFlower ? FireStun
                    : p.Kind == ItemKind.Boomerang ? BoomStun : p.Kind == ItemKind.Oil ? BananaSpin : BumperStun;
                if (RaceItems.Resolve(k, stun, p.Owner, p.Kind))
                {
                    if (p.Kind == ItemKind.Boomerang) p.HitThisPhase.Add(k);     // 穿過去繼續飛,每段每台車只打一次(去程 + 回程 = 最多兩次)
                    else { p.Dead = true; break; }
                }
            }
            if (p.Dead) return;

            if (p.Go != null)
            {
                p.Go.transform.position = p.Pos + Vector3.up * p.Lift;
                if (p.Spin != null) p.Spin.localRotation = Quaternion.Euler(0f, p.Age * 900f, 0f);
                if (p.Spark != null) p.Spark.localScale = Vector3.one * (0.22f + 0.14f * Mathf.Abs(Mathf.Sin(p.Age * (6f + p.Age * 4f))));
                if (p.Kind != ItemKind.Oil && p.Kind != ItemKind.Bomb && p.Kind != ItemKind.FakeBox && p.Dir.sqrMagnitude > 1e-6f) p.Go.transform.rotation = Quaternion.LookRotation(p.Dir, Vector3.up);
            }
        }

        // 線段 a→b(水平投影)到點 c 的距離 ≤ r,且高度差合理
        static bool Touch(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            Vector3 ab = b - a; ab.y = 0f;
            Vector3 ac = c - a; ac.y = 0f;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ac, ab) / len2) : 0f;
            float qx = a.x + ab.x * t, qz = a.z + ab.z * t;
            float dx = c.x - qx, dz = c.z - qz;
            float dy = c.y - Mathf.Lerp(a.y, b.y, t);
            return dx * dx + dz * dz <= r * r && Mathf.Abs(dy) < 2.5f;
        }

        static void MoveBomb(Projectile p, float dt)
        {
            if (p.Armed) return;
            float t = Mathf.Clamp01(p.Age / BombFlight);
            Vector3 np = Vector3.Lerp(p.From, p.To, t);
            p.Pos.x = np.x; p.Pos.z = np.z;
            p.Lift = 4f * t * (1f - t) * 2.5f;
            if (t >= 1f) { p.Armed = true; p.Lift = 0f; }
        }

        static void MoveShell(Projectile p, float dt)
        {
            p.RetargetT -= dt;
            if (p.Target == null || p.RetargetT <= 0f || (p.Target.Progress != null && p.Target.Progress.Finished))
            {
                p.Target = RaceItems.PickFirst(p.Owner);
                p.RetargetT = 0.4f;
            }
            float desiredLat = 0f;
            if (p.Target != null)
            {
                Vector3 to = p.Target.Position - p.Pos; to.y = 0f;
                float dist = to.magnitude;
                if (dist < ShellTrigger) { Detonate(p); return; }
                float tl;
                p.TSHint = track.Project(p.Target.Position, p.TSHint, out tl);
                desiredLat = tl * Mathf.Clamp01((70f - dist) / 40f);
            }
            p.S = track.Wrap(p.S + p.Speed * dt);
            p.Lat = Mathf.MoveTowards(p.Lat, desiredLat, 25f * dt);
            TrackSample sm = track.SampleAt(p.S);
            Vector3 np = sm.Pos + sm.Right * p.Lat;
            Vector3 d = np - p.Pos; d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) p.Dir = d.normalized;
            p.Pos = np;
        }

        static void MoveBumper(Projectile p, float dt)
        {
            Vector3 np = p.Pos + p.Dir * (p.Speed * dt);
            float lat;
            float s = track.Project(np, p.SHint, out lat);
            p.SHint = s;
            float lim = track.HalfWidthAt(s) + WallMargin;
            if (Mathf.Abs(lat) > lim)
            {
                if (p.Kind == ItemKind.Boomerang) { p.Returning = true; p.HitThisPhase.Clear(); p.Pos = np - p.Dir * 0.2f; return; }   // 撞到路邊:直接開始回程
                if (p.Bounces >= (p.Kind == ItemKind.FireFlower ? FireMaxBounces : MaxBounces)) { p.Dead = true; return; }
                Vector3 n = track.SampleAt(s).Right * (lat > 0f ? -1f : 1f);
                n.y = 0f; n.Normalize();
                if (Vector3.Dot(p.Dir, n) < 0f)
                {
                    p.Dir = Flat(Vector3.Reflect(p.Dir, n));
                    p.Bounces++; TotalBounces++;
                }
                np += n * (Mathf.Abs(lat) - lim + 0.05f);
            }
            p.Pos = np;
        }

        // 迴力鏢:去程直線 BoomOut 秒(撞路邊提早回頭),回程轉向飛向主人目前位置;進入 BoomCatch 公尺內 = 接住(消失)
        static void MoveBoomerang(Projectile p, float dt)
        {
            if (!p.Returning)
            {
                MoveBumper(p, dt);
                if (!p.Returning && p.Age >= BoomOut) { p.Returning = true; p.HitThisPhase.Clear(); }
                return;
            }
            if (p.Owner == null) { p.Dead = true; return; }
            Vector3 to = p.Owner.Position - p.Pos; to.y = 0f;
            float d = to.magnitude;
            if (d < BoomCatch) { p.Dead = true; return; }
            p.Dir = to / d;                                              // 回程直接對準主人(沿來路飛回,不繞大圈);轉頭那一下用減速 / 再加速表現
            p.RetT += dt;
            float ramp = Mathf.Clamp01(0.25f + p.RetT / 0.3f);
            p.Pos += p.Dir * (p.Speed * ramp * dt);
        }

        static void MoveHoming(Projectile p, float dt)
        {
            Vector3 pos = p.Pos;
            float lat;
            float s = track.Project(pos, p.SHint, out lat);
            p.SHint = s;

            if (p.Target == null || p.Target.Progress != null && p.Target.Progress.Finished) p.Target = FindAhead(p);

            float lookd = Mathf.Max(7f, p.Speed * 0.3f);
            TrackSample sm = track.SampleAt(s + lookd);
            Vector3 desired;
            if (p.Target != null)
            {
                Vector3 to = p.Target.Position - pos; to.y = 0f;
                float dist = to.magnitude;
                if (dist < 14f && dist > 0.01f)
                {
                    desired = to / dist;
                }
                else
                {
                    float tl;
                    p.TSHint = track.Project(p.Target.Position, p.TSHint, out tl);
                    float blend = Mathf.Clamp01((60f - dist) / 40f);
                    Vector3 wp = sm.Pos + sm.Right * (tl * blend);
                    desired = Flat(wp - pos);
                }
            }
            else
            {
                desired = Flat(sm.Pos - pos);
            }
            p.Dir = Flat(Vector3.RotateTowards(p.Dir, desired, HomingTurnRate * dt, 0f));

            Vector3 np = pos + p.Dir * (p.Speed * dt);
            float lim = track.HalfWidthAt(s) + 0.5f;
            if (Mathf.Abs(lat) > lim)
            {
                Vector3 n = track.SampleAt(s).Right * (lat > 0f ? -1f : 1f);
                n.y = 0f; n.Normalize();
                np += n * (Mathf.Abs(lat) - lim);
            }
            p.Pos = np;
        }

        // 沒目標時:鎖定前方 45 m 內最近的車
        static RaceKart FindAhead(Projectile p)
        {
            RaceKart best = null; float bd = 45f * 45f;
            var karts = RaceKart.All;
            for (int i = 0; i < karts.Count; i++)
            {
                RaceKart k = karts[i];
                if (k == null || k == p.Owner) continue;
                if (k.Progress != null && k.Progress.Finished) continue;
                Vector3 d = k.Position - p.Pos; d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < bd && Vector3.Dot(d, p.Dir) > 0f) { bd = m; best = k; }
            }
            return best;
        }
    }
}
