using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrcSim
{
    /// 子彈加速的輸入:沿賽道中心線自動駕駛(玩家輸入被覆蓋)。Blend 0 = 完全自動,1 = 完全還給原本的輸入來源。
    public sealed class BulletInput : IKartInput
    {
        public readonly RaceKart Kart;
        public readonly RaceTrack Track;
        public IKartInput Orig;
        public float Blend;
        float hint = -1f;

        public BulletInput(RaceKart k, RaceTrack t, IKartInput orig) { Kart = k; Track = t; Orig = orig; }

        public KartInput Read(float dt)
        {
            KartInput b = default;
            if (Kart != null && Track != null)
            {
                float lat;
                float s = Track.Project(Kart.Position, hint, out lat);
                if (hint >= 0f && Mathf.Abs(lat) > 25f) s = Track.Project(Kart.Position, -1f, out lat);
                hint = s;
                float speed = Mathf.Max(0f, Kart.Speed);
                float look = 8f + speed * 0.45f;
                Vector3 target = Track.SampleAt(s + look).Pos;
                Vector3 f = Kart.Forward; f.y = 0f;
                Vector3 to = target - Kart.Position; to.y = 0f;
                float ang = f.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(f, to, Vector3.up) : 0f;
                b.Steer = Mathf.Clamp(ang / 22f, -1f, 1f);
                b.Throttle = 1f;
                // 彎前自動收油
                float curv = Mathf.Abs(Track.MaxCurvature(s, s + 10f + speed * 0.9f));
                if (curv > 1e-4f)
                {
                    float vmax = Mathf.Sqrt(22f / curv);
                    if (speed > vmax * 1.08f) { b.Throttle = 0.2f; b.Brake = 0.3f; }
                    else if (speed > vmax) b.Throttle = 0.5f;
                }
            }
            if (Blend <= 0f || Orig == null) return b;
            KartInput o = Orig.Read(dt);
            KartInput r = o;
            r.Steer = Mathf.Lerp(b.Steer, o.Steer, Blend);
            r.Throttle = Mathf.Lerp(b.Throttle, o.Throttle, Blend);
            r.Brake = Mathf.Lerp(b.Brake, o.Brake, Blend);
            return r;
        }
    }

    /// 持續型道具效果(星星、巨大化、閃電縮小、子彈加速、墨水)與三連彈環繞視覺。內部狀態,查詢走 RaceItems。
    public static class ItemEffects
    {
        public const float StarSeconds = 10f, StarSpeed = 1.2f, StarStun = 1.2f, StarHitRadius = 2.0f;
        public const float MegaSeconds = 8f, MegaSpeed = 1.15f, MegaScale = 2.2f, MegaStun = 1.5f, MegaHitRadius = 2.8f;
        public const float ShrinkSeconds = 4f, ShrinkScale = 0.5f, ShrinkSpeed = 0.7f;
        public const float BulletSeconds = 6f, BulletBlend = 1.0f, BulletSpeed = 1.5f;
        public const float FeatherSeconds = 1.5f, FeatherHopSpeed = 9f;     // 羽毛:1.5 s 內地面障礙碰不到;同時給車一個向上的初速(實際跳高由 RaceKart 重力決定)
        public const float InkSeconds = 3f, InkAiGrip = 0.45f, InkAiSpeed = 0.92f;
        public const float BooSeconds = 3f;                                  // 幽靈:3 s 隱形 + 穿透所有道具(含閃電);開始時偷走前車一個道具

        sealed class Fx
        {
            public RaceKart Kart;
            public float Star, Mega, Shrink, Bullet, Ink, Squash, BlendT, Feather, Boo;
            public GameObject TrailVis, BooVis; public Transform[] Peels; public int TrailCount; public bool BooHidden;
            public bool BulletOn;
            public IKartInput Orig; public BulletInput BIn;
            public Vector3 Scale = Vector3.one;
            public Transform Visual; public Vector3 VisualBase = Vector3.one;
            public float BaseMul = 1f, LastMul = 1f; public bool Written;
            public GameObject StarVis, BulletVis, OrbitVis; public Transform[] Orbs; public int OrbCount;
            public bool Hidden;
        }

        sealed class Transient { public GameObject Go; public float Life, Max, Radius; public bool Grow; }

        static readonly List<Fx> list = new List<Fx>();
        static readonly List<Transient> trans = new List<Transient>();
        static RaceTrack track;
        static Mesh icoMesh;

        public static void Init(RaceTrack t) { Shutdown(); track = t; }

        public static void Shutdown()
        {
            for (int i = list.Count - 1; i >= 0; i--) Cleanup(list[i]);
            list.Clear();
            for (int i = 0; i < trans.Count; i++) if (trans[i].Go != null) Object.Destroy(trans[i].Go);
            trans.Clear();
            track = null;
        }

        static Fx Get(RaceKart k, bool create)
        {
            if (k == null) return null;
            for (int i = 0; i < list.Count; i++) if (list[i].Kart == k) return list[i];
            if (!create) return null;
            var f = new Fx { Kart = k };
            Transform v = k.transform.Find("Visual");
            if (v != null) { f.Visual = v; f.VisualBase = v.localScale; }
            list.Add(f);
            return f;
        }

        // ------------------------------------------------------------ 查詢
        public static float Remaining(RaceKart k, ItemKind kind)
        {
            var f = Get(k, false);
            if (f == null) return 0f;
            switch (kind)
            {
                case ItemKind.Star: return f.Star;
                case ItemKind.Mega: return f.Mega;
                case ItemKind.Lightning: return f.Shrink;
                case ItemKind.Bullet: return f.Bullet;
                case ItemKind.InkBlast: return f.Ink;
                case ItemKind.Feather: return f.Feather;
                case ItemKind.Boo: return f.Boo;
            }
            return 0f;
        }
        /// 羽毛跳躍中(地面障礙:油漬 / 假箱 / 直線彈 / 火球 / 炸彈都碰不到)。
        public static bool Hopping(RaceKart k) { var f = Get(k, false); return f != null && f.Feather > 0f; }
        public static bool BulletActive(RaceKart k) { var f = Get(k, false); return f != null && f.BulletOn; }
        public static float ScaleOf(RaceKart k) { var f = Get(k, false); return f != null ? f.Scale.x : 1f; }
        public static float SquashOf(RaceKart k) { var f = Get(k, false); return f != null ? f.Scale.y / Mathf.Max(0.01f, f.Scale.x) : 1f; }

        // ------------------------------------------------------------ 開始
        public static void StartStar(RaceKart k)
        {
            var f = Get(k, true);
            bool fresh = f.Star <= 0f;
            f.Star = StarSeconds;
            k.SetInvincible(StarSeconds);
            if (f.StarVis == null) f.StarVis = BuildStarVis(k);
            if (fresh) RaceItems.RaiseEffect(true, k, ItemKind.Star);
        }

        public static void StartMega(RaceKart k)
        {
            var f = Get(k, true);
            bool fresh = f.Mega <= 0f;
            f.Mega = MegaSeconds;
            k.SetInvincible(MegaSeconds);
            if (fresh) RaceItems.RaiseEffect(true, k, ItemKind.Mega);
        }

        public static void StartFeather(RaceKart k)
        {
            var f = Get(k, true);
            bool fresh = f.Feather <= 0f;
            f.Feather = FeatherSeconds;
            k.ApplyImpulse(Vector3.up * FeatherHopSpeed);
            if (fresh) RaceItems.RaiseEffect(true, k, ItemKind.Feather);
        }

        /// 幽靈:BooSeconds 秒無敵(所有道具穿過去,包含閃電)+ 隱形(AI 車直接隱藏;玩家車保留可見,另加淡藍光環)。偷道具由 RaceItems 處理。
        public static void StartBoo(RaceKart k)
        {
            var f = Get(k, true);
            bool fresh = f.Boo <= 0f;
            f.Boo = BooSeconds;
            k.SetInvincible(BooSeconds);
            if (f.BooVis == null) f.BooVis = BuildBooVis(k);
            if (!k.IsPlayer && f.Visual != null && !f.BooHidden) { f.Visual.gameObject.SetActive(false); f.BooHidden = true; }
            if (fresh) RaceItems.RaiseEffect(true, k, ItemKind.Boo);
        }

        public static void Shrink(RaceKart k, float sec)
        {
            var f = Get(k, true);
            bool fresh = f.Shrink <= 0f;
            f.Shrink = Mathf.Max(f.Shrink, sec);
            if (fresh) RaceItems.RaiseEffect(true, k, ItemKind.Lightning);
        }

        public static void Ink(RaceKart k, float sec)
        {
            var f = Get(k, true);
            bool fresh = f.Ink <= 0f;
            f.Ink = Mathf.Max(f.Ink, sec);
            if (!k.IsPlayer) k.Slip(sec, InkAiGrip, InkAiSpeed);     // AI 沒有畫面可遮:改成降低抓地
            if (fresh) RaceItems.RaiseEffect(true, k, ItemKind.InkBlast);
        }

        public static void Squash(RaceKart k, float sec)
        {
            var f = Get(k, true);
            f.Squash = Mathf.Max(f.Squash, sec);
        }

        public static void StartBullet(RaceKart k, RaceTrack t)
        {
            var f = Get(k, true);
            if (f.BulletOn) { f.Bullet = BulletSeconds; f.BlendT = 0f; return; }
            f.BulletOn = true; f.Bullet = BulletSeconds; f.BlendT = 0f;
            f.Orig = k.Source;
            f.BIn = new BulletInput(k, t ?? track, f.Orig);
            k.Source = f.BIn;
            k.SetInvincible(BulletSeconds + BulletBlend + 0.3f);
            k.Boost(0.6f, 0.25f);
            if (f.Visual != null) { f.Visual.gameObject.SetActive(false); f.Hidden = true; }
            f.BulletVis = BuildBulletVis(k);
            RaceItems.RaiseEffect(true, k, ItemKind.Bullet);
        }

        // ------------------------------------------------------------ 環繞的三連彈
        public static void SyncOrbit(RaceKart k, int count)
        {
            var f = Get(k, count > 0);
            if (f == null) return;
            if (count <= 0)
            {
                if (f.OrbitVis != null) { Object.Destroy(f.OrbitVis); f.OrbitVis = null; f.Orbs = null; }
                f.OrbCount = 0;
                return;
            }
            if (f.OrbitVis == null) BuildOrbit(f);
            f.OrbCount = count;
        }

        // ------------------------------------------------------------ 車尾拖的三連香蕉皮
        public static void SyncTrail(RaceKart k, int count)
        {
            var f = Get(k, count > 0);
            if (f == null) return;
            if (count <= 0)
            {
                if (f.TrailVis != null) { Object.Destroy(f.TrailVis); f.TrailVis = null; f.Peels = null; }
                f.TrailCount = 0;
                return;
            }
            if (f.TrailVis == null) BuildTrail(f);
            f.TrailCount = count;
        }

        static void BuildTrail(Fx f)
        {
            var g = new GameObject("BananaTrail");
            g.transform.SetParent(f.Kart.transform, false);
            f.Peels = new Transform[3];
            Material m = RaceArt.Mat(new Color(1f, 0.84f, 0.08f), 0.6f);
            for (int i = 0; i < 3; i++)
            {
                var o = new GameObject("Peel" + i);
                o.transform.SetParent(g.transform, false);
                o.transform.localPosition = new Vector3(0f, -0.2f, -2.0f - i * 0.95f);
                o.transform.localScale = Vector3.one * 1.1f;
                o.AddComponent<MeshFilter>().sharedMesh = ItemProjectiles.PeelMesh();
                o.AddComponent<MeshRenderer>().sharedMaterial = m;
                f.Peels[i] = o.transform;
            }
            f.TrailVis = g;
        }

        static GameObject BuildBooVis(RaceKart k)
        {
            var g = new GameObject("BooVis");
            g.transform.SetParent(k.transform, false);
            g.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            Color c = new Color(0.75f, 0.85f, 1f, 0.8f);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.loop = true;
            lr.positionCount = 28;
            for (int i = 0; i < 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                float r = 1.5f + 0.18f * Mathf.Sin(a * 5f);          // 波浪邊的幽靈光環
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            lr.widthMultiplier = 0.1f;
            lr.sharedMaterial = RaceArt.Glow(c, 1.3f);
            lr.startColor = lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        static void BuildOrbit(Fx f)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            var g = new GameObject("BumperOrbit");
            g.transform.SetParent(f.Kart.transform, false);
            f.Orbs = new Transform[3];
            Material m = RaceArt.Mat(new Color(0.95f, 0.2f, 0.18f), 0.7f);
            for (int i = 0; i < 3; i++)
            {
                var o = new GameObject("Orb" + i);
                o.transform.SetParent(g.transform, false);
                o.transform.localScale = Vector3.one * 0.55f;
                o.AddComponent<MeshFilter>().sharedMesh = icoMesh;
                o.AddComponent<MeshRenderer>().sharedMaterial = m;
                f.Orbs[i] = o.transform;
            }
            f.OrbitVis = g;
        }

        // ------------------------------------------------------------ 視覺
        static GameObject BuildStarVis(RaceKart k)
        {
            var g = new GameObject("StarVis");
            g.transform.SetParent(k.transform, false);
            g.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            Color gold = new Color(1f, 0.85f, 0.2f);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.loop = true;
            lr.positionCount = 10;
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                float r = (i % 2 == 0) ? 0.6f : 0.25f;
                lr.SetPosition(i, new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0f));
            }
            lr.widthMultiplier = 0.09f;
            lr.sharedMaterial = RaceArt.Glow(gold, 1.6f);
            lr.startColor = lr.endColor = gold;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        static GameObject BuildBulletVis(RaceKart k)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            var g = new GameObject("BulletVis");
            g.transform.SetParent(k.transform, false);
            g.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            Child(g.transform, "Body", new Vector3(0f, 0f, 0f), new Vector3(1.3f, 1.1f, 2.8f), RaceArt.Mat(new Color(0.12f, 0.12f, 0.15f), 0.6f));
            Child(g.transform, "EyeL", new Vector3(-0.3f, 0.2f, 0.9f), new Vector3(0.38f, 0.45f, 0.2f), RaceArt.Mat(Color.white, 0.5f));
            Child(g.transform, "EyeR", new Vector3(0.3f, 0.2f, 0.9f), new Vector3(0.38f, 0.45f, 0.2f), RaceArt.Mat(Color.white, 0.5f));
            Child(g.transform, "Fin", new Vector3(0f, 0.1f, -1.3f), new Vector3(0.9f, 0.9f, 0.5f), RaceArt.Mat(new Color(1f, 0.6f, 0.15f), 0.5f));
            var tr = g.AddComponent<TrailRenderer>();
            tr.time = 0.4f; tr.startWidth = 0.9f; tr.endWidth = 0f;
            Color c = new Color(1f, 0.6f, 0.15f);
            tr.sharedMaterial = RaceArt.Glow(c, 1.4f);
            tr.startColor = c; tr.endColor = new Color(c.r, c.g, c.b, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        static void Child(Transform parent, string name, Vector3 lp, Vector3 scale, Material m)
        {
            var o = new GameObject(name);
            o.transform.SetParent(parent, false);
            o.transform.localPosition = lp; o.transform.localScale = scale;
            o.AddComponent<MeshFilter>().sharedMesh = icoMesh;
            o.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// 閃電落雷閃光(短暫)。
        public static void Bolt(Vector3 pos)
        {
            var g = new GameObject("LightningBolt");
            g.transform.position = pos;
            Color c = new Color(1f, 0.95f, 0.4f);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            const int N = 8;
            lr.positionCount = N;
            var r = new System.Random((int)(pos.x * 31f + pos.z * 17f));
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                Vector3 p = pos + Vector3.up * (28f * (1f - t));
                if (i > 0 && i < N - 1) p += new Vector3((float)r.NextDouble() - 0.5f, 0f, (float)r.NextDouble() - 0.5f) * 3f;
                lr.SetPosition(i, p);
            }
            lr.widthMultiplier = 0.5f;
            lr.sharedMaterial = RaceArt.Glow(c, 2f);
            lr.startColor = lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trans.Add(new Transient { Go = g, Life = 0.35f, Max = 0.35f });
        }

        /// 爆炸光球(擴張後消失)。
        public static void Blast(Vector3 pos, float radius, Color c)
        {
            if (icoMesh == null) icoMesh = RaceArt.IcoSphere(1);
            var g = new GameObject("Blast");
            g.transform.position = pos + Vector3.up * 0.5f;
            g.AddComponent<MeshFilter>().sharedMesh = icoMesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = RaceArt.Glow(c, 1.8f);
            g.transform.localScale = Vector3.one * 0.5f;
            trans.Add(new Transient { Go = g, Life = 0.45f, Max = 0.45f, Radius = radius, Grow = true });
        }

        // ------------------------------------------------------------ 每步
        public static void Tick(float dt)
        {
            for (int i = trans.Count - 1; i >= 0; i--)
            {
                Transient t = trans[i];
                t.Life -= dt;
                if (t.Life <= 0f || t.Go == null) { if (t.Go != null) Object.Destroy(t.Go); trans.RemoveAt(i); continue; }
                if (t.Grow)
                {
                    float u = 1f - t.Life / t.Max;
                    t.Go.transform.localScale = Vector3.one * (t.Radius * 2f * Mathf.Sqrt(u) + 0.3f);
                }
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Fx f = list[i];
                if (f.Kart == null) { Cleanup(f); list.RemoveAt(i); continue; }
                StepFx(f, dt);
                if (Idle(f)) { Cleanup(f); list.RemoveAt(i); }
            }
        }

        static bool Idle(Fx f)
        {
            return f.Star <= 0f && f.Mega <= 0f && f.Shrink <= 0f && f.Feather <= 0f && !f.BulletOn && f.Ink <= 0f && f.Squash <= 0f
                && f.Boo <= 0f && f.TrailCount <= 0 && f.OrbCount <= 0 && Mathf.Abs(f.Scale.x - 1f) < 0.01f && Mathf.Abs(f.Scale.y - 1f) < 0.01f && !f.Written;
        }

        static void StepFx(Fx f, float dt)
        {
            RaceKart k = f.Kart;

            if (f.Star > 0f)
            {
                f.Star -= dt;
                if (f.Star <= 0f)
                {
                    f.Star = 0f;
                    if (f.StarVis != null) { Object.Destroy(f.StarVis); f.StarVis = null; }
                    RaceItems.RaiseEffect(false, k, ItemKind.Star);
                }
                else
                {
                    if (f.StarVis != null) f.StarVis.transform.Rotate(0f, 240f * dt, 0f, Space.Self);
                    Contact(f, StarHitRadius, StarStun, ItemKind.Star);
                }
            }
            if (f.Mega > 0f)
            {
                f.Mega -= dt;
                if (f.Mega <= 0f) { f.Mega = 0f; RaceItems.RaiseEffect(false, k, ItemKind.Mega); }
                else Contact(f, MegaHitRadius, MegaStun, ItemKind.Mega);
            }
            if (f.Shrink > 0f)
            {
                f.Shrink -= dt;
                if (f.Shrink <= 0f) { f.Shrink = 0f; RaceItems.RaiseEffect(false, k, ItemKind.Lightning); }
            }
            if (f.Ink > 0f)
            {
                f.Ink -= dt;
                if (f.Ink <= 0f) { f.Ink = 0f; RaceItems.RaiseEffect(false, k, ItemKind.InkBlast); }
            }
            if (f.Feather > 0f)
            {
                f.Feather -= dt;
                if (f.Feather <= 0f) { f.Feather = 0f; RaceItems.RaiseEffect(false, k, ItemKind.Feather); }
            }
            if (f.Squash > 0f) { f.Squash -= dt; if (f.Squash < 0f) f.Squash = 0f; }
            if (f.Boo > 0f)
            {
                f.Boo -= dt;
                if (f.BooVis != null) f.BooVis.transform.Rotate(0f, 90f * dt, 0f, Space.Self);
                if (f.Boo <= 0f)
                {
                    f.Boo = 0f;
                    if (f.BooVis != null) { Object.Destroy(f.BooVis); f.BooVis = null; }
                    if (f.BooHidden && f.Visual != null && !f.Hidden) f.Visual.gameObject.SetActive(true);
                    f.BooHidden = false;
                    RaceItems.RaiseEffect(false, k, ItemKind.Boo);
                }
            }
            if (f.TrailVis != null && f.Peels != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    bool on = i < f.TrailCount;
                    if (f.Peels[i].gameObject.activeSelf != on) f.Peels[i].gameObject.SetActive(on);
                    float sway = Mathf.Sin(Time.time * 6f + i * 1.3f) * 0.12f;
                    f.Peels[i].localPosition = new Vector3(sway, -0.2f, -2.0f - i * 0.95f);
                }
            }

            // 子彈加速:6 s 自動駕駛,再用 1 s 把輸入與速度平順還回
            float bulletMul = 1f;
            if (f.BulletOn)
            {
                if (f.Bullet > 0f)
                {
                    f.Bullet -= dt;
                    bulletMul = BulletSpeed;
                    if (f.Bullet <= 0f)
                    {
                        f.Bullet = 0f; f.BlendT = BulletBlend;
                        if (f.Hidden && f.Visual != null) f.Visual.gameObject.SetActive(true);
                        f.Hidden = false;
                        if (f.BulletVis != null) { Object.Destroy(f.BulletVis); f.BulletVis = null; }
                    }
                }
                else
                {
                    f.BlendT -= dt;
                    float u = Mathf.Clamp01(1f - f.BlendT / BulletBlend);
                    if (f.BIn != null) f.BIn.Blend = u;
                    bulletMul = Mathf.Lerp(BulletSpeed, 1f, u);
                    if (f.BlendT <= 0f)
                    {
                        if (k.Source == (IKartInput)f.BIn) k.Source = f.Orig;
                        f.BulletOn = false; f.BlendT = 0f; f.BIn = null; f.Orig = null;
                        bulletMul = 1f;
                        RaceItems.RaiseEffect(false, k, ItemKind.Bullet);
                    }
                }
            }

            // 速度倍率(疊在別人如橡皮筋寫的 SpeedMul 上)
            float m = 1f;
            if (f.Star > 0f) m *= StarSpeed;
            if (f.Mega > 0f) m *= MegaSpeed;
            if (f.Shrink > 0f) m *= ShrinkSpeed;
            m *= bulletMul;
            float cur = k.SpeedMul;
            if (!f.Written || !Mathf.Approximately(cur, f.LastMul)) f.BaseMul = cur;
            if (Mathf.Approximately(m, 1f))
            {
                if (f.Written) { k.SpeedMul = f.BaseMul; f.Written = false; }
            }
            else { f.LastMul = f.BaseMul * m; k.SpeedMul = f.LastMul; f.Written = true; }

            // 大小
            float us = f.Mega > 0f ? MegaScale : (f.Shrink > 0f ? ShrinkScale : 1f);
            Vector3 target = new Vector3(us, us * (f.Squash > 0f ? 0.3f : 1f), us);
            f.Scale = Vector3.Lerp(f.Scale, target, 1f - Mathf.Exp(-14f * dt));
            if ((f.Scale - target).sqrMagnitude < 1e-5f) f.Scale = target;
            if (f.Visual != null) f.Visual.localScale = Vector3.Scale(f.VisualBase, f.Scale);

            // 環繞彈
            if (f.OrbitVis != null && f.Orbs != null)
            {
                float a0 = Time.time * 200f * Mathf.Deg2Rad;
                for (int i = 0; i < 3; i++)
                {
                    bool on = i < f.OrbCount;
                    if (f.Orbs[i].gameObject.activeSelf != on) f.Orbs[i].gameObject.SetActive(on);
                    float a = a0 + i * Mathf.PI * 2f / 3f;
                    f.Orbs[i].localPosition = new Vector3(Mathf.Cos(a) * 1.4f, 0.5f, Mathf.Sin(a) * 1.4f);
                }
            }
        }

        // 星星 / 巨大化碰到別車:對方暈眩(走 RaceItems.Resolve:護盾 / 無敵規則一致)
        static void Contact(Fx f, float radius, float stun, ItemKind kind)
        {
            RaceKart me = f.Kart;
            float r = kind == ItemKind.Mega ? radius : radius;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == me) continue;
                Vector3 d = o.Position - me.Position;
                if (Mathf.Abs(d.y) > 2.5f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > r * r) continue;
                if (RaceItems.Resolve(o, stun, me, kind))
                {
                    if (o.Stunned) { if (kind == ItemKind.Mega) Squash(o, stun); }
                    else o.SetInvincible(0.6f);       // 護盾擋下:短暫無敵,避免下一步又被打
                }
            }
        }

        static void Cleanup(Fx f)
        {
            if (f.StarVis != null) Object.Destroy(f.StarVis);
            if (f.BulletVis != null) Object.Destroy(f.BulletVis);
            if (f.OrbitVis != null) Object.Destroy(f.OrbitVis);
            if (f.TrailVis != null) Object.Destroy(f.TrailVis);
            if (f.BooVis != null) Object.Destroy(f.BooVis);
            f.StarVis = f.BulletVis = f.OrbitVis = f.TrailVis = f.BooVis = null;
            f.TrailCount = 0;
            if (f.Kart != null)
            {
                if (f.Written) { f.Kart.SpeedMul = f.BaseMul; f.Written = false; }
                if (f.BulletOn && f.BIn != null && f.Kart.Source == (IKartInput)f.BIn) f.Kart.Source = f.Orig;
            }
            if (f.Visual != null)
            {
                f.Visual.localScale = f.VisualBase;
                if (f.Hidden || f.BooHidden) f.Visual.gameObject.SetActive(true);
            }
            f.Hidden = false; f.BooHidden = false; f.BulletOn = false;
        }
    }

    /// 墨水遮畫面(只畫在玩家車被 InkBlast 命中時)。OnGUI 程式生成的圓點,沒有外部素材。
    public sealed class ItemInkOverlay : MonoBehaviour
    {
        static Texture2D blob;
        float[] bx, by, bs;

        void Awake()
        {
            var r = new System.Random(9427);
            int n = 14;
            bx = new float[n]; by = new float[n]; bs = new float[n];
            for (int i = 0; i < n; i++)
            {
                bx[i] = (float)r.NextDouble(); by[i] = (float)r.NextDouble();
                bs[i] = 0.18f + (float)r.NextDouble() * 0.22f;
            }
        }

        static Texture2D Blob()
        {
            if (blob != null) return blob;
            const int N = 64;
            blob = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    blob.SetPixel(x, y, new Color(0.03f, 0.02f, 0.06f, Mathf.Clamp01((1f - d) * 3f)));
                }
            blob.Apply();
            return blob;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float ink = 0f;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].IsPlayer) ink = Mathf.Max(ink, ItemEffects.Remaining(all[i], ItemKind.InkBlast));
            if (ink <= 0f) return;
            float grow = Mathf.Clamp01((ItemEffects.InkSeconds - ink) / 0.25f);
            float fade = Mathf.Clamp01(ink / 0.6f);
            Texture2D t = Blob();
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            float sw = Screen.width, sh = Screen.height, m = Mathf.Max(sw, sh);
            for (int i = 0; i < bx.Length; i++)
            {
                float s = bs[i] * m * (0.4f + 0.6f * grow);
                GUI.DrawTexture(new Rect(bx[i] * sw - s * 0.5f, by[i] * sh - s * 0.5f, s, s), t);
            }
            GUI.color = old;
        }

        void OnDestroy() { if (blob != null) { Destroy(blob); blob = null; } }
    }
}
