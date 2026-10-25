using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽車的聲音、粒子與震動回饋(模組 P)。契約:特效/音效一律由這裡訂閱 RaceKart 事件觸發(01/03 不直接呼叫)。
    // RaceSession.Begin 呼叫 Init(內部會建 RaceFx 粒子);RaceSession.JuiceTick 每幀呼叫 Tick;End 呼叫 Shutdown。
    public static class RaceJuice
    {
        sealed class KartFx
        {
            public Transform rl, rr, exl, exr;
            public float sparkAcc, smokeAcc, flameAcc, rumbleT, techAcc;
        }

        static bool inited;
        static int lastCount = -1;
        static float engVol, engPitch = 0.6f, skidVol, lastHitT = -9f;
        static readonly Dictionary<RaceKart, KartFx> fx = new Dictionary<RaceKart, KartFx>();

        public static void Init()
        {
            if (inited) return;
            inited = true; lastCount = -1; engVol = 0f; engPitch = 0.6f; skidVol = 0f; fx.Clear();
            RaceSfx.Init();
            if (RaceSession.Root != null) RaceFx.Init(RaceSession.Root);
            RaceSession.PhaseChanged += OnPhase;
            RaceSession.LapDone += OnLap;
            RaceSession.KartFinished += OnFinish;
            RaceKart.Bumped += OnBumped;
            RaceKart.DriftLevelUp += OnDriftLv;
            RaceKart.BoostStart += OnBoost;
            RaceKart.Hopped += OnHop;
            RaceKart.Landed += OnLanded;
        }

        public static void Shutdown()
        {
            if (!inited) return;
            inited = false;
            RaceSession.PhaseChanged -= OnPhase;
            RaceSession.LapDone -= OnLap;
            RaceSession.KartFinished -= OnFinish;
            RaceKart.Bumped -= OnBumped;
            RaceKart.DriftLevelUp -= OnDriftLv;
            RaceKart.BoostStart -= OnBoost;
            RaceKart.Hopped -= OnHop;
            RaceKart.Landed -= OnLanded;
            fx.Clear();
            if (SfxBus.Enabled)
            {
                SfxBus.SetLoop("kart_engine", RaceSfx.Engine, 0f, 1f);
                SfxBus.SetLoop("kart_skid", RaceSfx.Skid, 0f, 1f);
            }
            RaceFx.Shutdown();
        }

        // ---------------------------------------------------------------- 小工具
        static bool IsPlayer(RaceKart k) { return k != null && k == RaceSession.Player; }
        static void Snd(string n, float v, float pitch = 1f) { if (SfxBus.Enabled) SfxBus.Play(n, v, pitch); }
        static float DistToPlayer(Vector3 p) { var pl = RaceSession.Player; return pl == null ? 9999f : Vector3.Distance(pl.Position, p); }

        static KartFx Get(RaceKart k)
        {
            if (!fx.TryGetValue(k, out var f))
            {
                f = new KartFx();
                var kv = k.GetComponentInChildren<KartVisual>();
                var vis = kv != null ? kv.transform : k.transform;
                f.rl = vis.Find("WheelRL"); f.rr = vis.Find("WheelRR"); f.exl = vis.Find("ExhaustL"); f.exr = vis.Find("ExhaustR");
                fx[k] = f;
            }
            return f;
        }

        // ---------------------------------------------------------------- 使用道具(RaceItems.TryUse 直接呼叫)
        /// 道具代表色(光環 / 火花用)。
        public static Color ItemColor(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Boost: case ItemKind.TripleBoost: return new Color(1f, 0.55f, 0.1f);      // 橘
                case ItemKind.Shield: return new Color(0.3f, 0.85f, 1f);                                 // 青
                case ItemKind.Bomb: case ItemKind.Bumper: case ItemKind.TripleBumper: return new Color(1f, 0.2f, 0.15f);   // 紅
                case ItemKind.Homing: case ItemKind.BlueShell: return new Color(0.3f, 0.5f, 1f);         // 藍
                case ItemKind.InkBlast: return new Color(0.6f, 0.35f, 0.9f);                             // 紫
                case ItemKind.Oil: case ItemKind.TripleBanana: return new Color(1f, 0.85f, 0.15f);      // 香蕉黃
                case ItemKind.Boo: return new Color(0.8f, 0.9f, 1f);
                case ItemKind.Boomerang: return new Color(1f, 0.65f, 0.15f);
                case ItemKind.Star: case ItemKind.Lightning: return new Color(1f, 0.92f, 0.25f);         // 黃
                case ItemKind.Mega: return new Color(0.4f, 1f, 0.4f);                                    // 綠
                case ItemKind.Bullet: return new Color(0.95f, 0.95f, 1f);                                // 白
                case ItemKind.FakeBox: return new Color(1f, 0.6f, 0.85f);                                // 粉
                default: return Color.white;
            }
        }

        /// 使用道具回饋:光環 + 車身閃爍 + 短音效(加速已有專屬音效,不重複播)。
        /// 不依賴 Init(RaceFx 沒準備好就只做閃爍 / 音效),任何 null 都安全。
        public static void ItemUsed(RaceKart k, ItemKind kind, float flickerSec)
        {
            if (k == null) return;
            bool big = kind == ItemKind.Mega || kind == ItemKind.Bullet || kind == ItemKind.Star;
            RaceFx.UseBurst(k.Position, ItemColor(kind), big ? 1f : 0.5f);
            var kv = k.GetComponentInChildren<KartVisual>();
            if (kv != null && kind != ItemKind.Bullet) kv.Flicker(flickerSec);     // 子彈本身會隱藏車身
            if (!SfxBus.Enabled) return;
            float d = DistToPlayer(k.Position);
            if (!IsPlayer(k) && d > 40f) return;
            float vol = IsPlayer(k) ? 0.7f : 0.35f * (1f - d / 40f);
            switch (kind)
            {
                case ItemKind.Boost: case ItemKind.TripleBoost: break;                 // OnBoost 已播
                case ItemKind.Star: Snd(RaceSfx.StarOn, vol); break;
                case ItemKind.Lightning: Snd(RaceSfx.Zap, vol); break;
                case ItemKind.Bomb: Snd(RaceSfx.ItemGet, vol, 0.7f); break;
                case ItemKind.BlueShell: Snd(RaceSfx.ShellWarn, vol * 0.7f); break;
                case ItemKind.Mega: case ItemKind.Bullet: Snd(RaceSfx.JumpLaunch, vol); break;
                default: Snd(RaceSfx.ItemGet, vol, 1.25f); break;
            }
        }

        // ---------------------------------------------------------------- 事件
        static void OnPhase(RacePhase p)
        {
            if (p == RacePhase.Countdown) lastCount = -1;
            if (p == RacePhase.Racing && RaceSession.Active)
            {
                Snd(RaceSfx.Go, 0.9f);
                Rumble.Driver(0.5f, 0f, 0.2f);
            }
        }

        static void OnLap(RaceKart k, int done)
        {
            if (k == null || k != RaceSession.Player) return;
            int total = RaceSession.Config != null ? RaceSession.Config.Laps : 1;
            if (done >= total) return;   // 最後一圈跑完由 OnFinish 處理
            Snd(done == total - 1 ? RaceSfx.FinalLap : RaceSfx.Lap, 0.8f);
            if (done == total - 1) RaceMusic.PlayFinal(RaceSession.Config.TrackId);   // 最後一圈:加速版配樂
            Rumble.Driver(0f, 0.4f, 0.12f);
        }

        static void OnFinish(RaceKart k)
        {
            if (k == null || k != RaceSession.Player) return;
            Snd(RaceSfx.FinalLap, 0.9f);
            Rumble.Driver(0.3f, 0.5f, 0.3f);
            RaceMusic.PlayResult();
            RaceFx.Confetti(k.Position);
        }

        static void OnBumped(RaceKart k, KartBump b)
        {
            if (!RaceSession.Active || k == null) return;
            RaceFx.Impact(b.Point, b.K);
            bool me = IsPlayer(k) || (b.OtherKartId == 0 && RaceSession.Player != null);
            float d = DistToPlayer(b.Point);
            if (Time.unscaledTime - lastHitT > 0.08f && d < 45f)
            {
                lastHitT = Time.unscaledTime;
                float v = Mathf.Lerp(0.25f, 1f, b.K) * (me ? 1f : Mathf.Clamp01(1f - d / 45f) * 0.6f);
                Snd(RaceSfx.Hit, v, Random.Range(0.9f, 1.1f) * Mathf.Lerp(1.15f, 0.85f, b.K));
            }
            if (me && !MenuSystem.Blocking)
            {
                JuiceFX.AddTrauma(0.3f + 0.5f * b.K);
                Rumble.Driver(0.3f + 0.7f * b.K, 0.2f + 0.5f * b.K, 0.12f + 0.2f * b.K);
            }
        }

        static void OnDriftLv(RaceKart k, int lv)
        {
            if (!RaceSession.Active || k == null) return;
            var f = Get(k);
            for (int i = 0; i < 8; i++)
            {   // 升級時兩輪各爆一小撮對應顏色
                if (f.rl != null) RaceFx.DriftSpark(f.rl.position + Vector3.down * 0.2f, k.Velocity, lv);
                if (f.rr != null) RaceFx.DriftSpark(f.rr.position + Vector3.down * 0.2f, k.Velocity, lv);
            }
            if (IsPlayer(k))
            {
                Snd(RaceSfx.DriftLv, 0.6f, Mathf.Pow(1.26f, Mathf.Clamp(lv, 1, 3) - 1));
                Rumble.Driver(0f, 0.5f, 0.08f);
            }
        }

        static void OnBoost(RaceKart k, float sec)
        {
            if (!RaceSession.Active || k == null) return;
            if (IsPlayer(k))
            {
                Snd(RaceSfx.Boost, 0.85f);
                if (SfxBus.Enabled) SfxBus.Duck(0.6f, 0.4f);
                Rumble.Driver(0.6f, 0.3f, 0.3f);
                JuiceFX.AddTrauma(0.1f);
            }
            else
            {
                float d = DistToPlayer(k.Position);
                if (d < 40f) Snd(RaceSfx.Boost, 0.35f * (1f - d / 40f), Random.Range(0.95f, 1.1f));
            }
        }

        static void OnHop(RaceKart k)
        {
            if (!RaceSession.Active || k == null) return;
            if (IsPlayer(k)) Snd(RaceSfx.Hop, 0.4f);
            RaceFx.Dust(k.Position - Vector3.up * RaceKart.RideDist, 0.1f);
        }

        static void OnLanded(RaceKart k)
        {
            if (!RaceSession.Active || k == null) return;
            RaceFx.Dust(k.Position - Vector3.up * RaceKart.RideDist, 0.6f);
            if (IsPlayer(k)) { Snd("thump", 0.4f); JuiceFX.AddTrauma(0.15f); Rumble.Driver(0.25f, 0.1f, 0.08f); }
        }

        // ---------------------------------------------------------------- 每幀
        public static void Tick(float dt)
        {
            if (!inited || !RaceSession.Active) return;
            if (RaceSession.Phase == RacePhase.Countdown && !MenuSystem.Blocking)
            {
                int c = Mathf.CeilToInt(RaceSession.CountdownLeft);
                if (c != lastCount && c >= 1 && c <= 3)
                {
                    lastCount = c;
                    Snd(RaceSfx.Beep, 0.7f);
                    Rumble.Driver(0f, 0.2f, 0.05f);
                }
            }
            if (!MenuSystem.Blocking) TickFx(dt);
            if (!SfxBus.Enabled) return;
            TickAudio(dt);
        }

        static void TickFx(float dt)
        {
            if (!RaceFx.Ready && RaceSession.Root != null) RaceFx.Init(RaceSession.Root);
            if (!RaceFx.Ready) return;
            foreach (var k in RaceSession.Karts)
            {
                if (k == null) continue;
                var f = Get(k);
                Vector3 vel = k.Velocity;
                bool grass = k.Surface == Surface.Grass || k.Surface == Surface.Dirt || k.Surface == Surface.Sand;
                if (k.Charging || k.WallRiding || k.RailGrinding)
                {   // 集氣 / 跑牆 / 軌道:重用漂移火花(集氣依等級變色,跑牆 / 軌道固定橘色)
                    int tl = k.Charging ? Mathf.Max(1, k.ChargeLevel) : 2;
                    f.techAcc += (k.Charging && k.ChargeLevel == 0 ? 12f : 50f) * dt;
                    while (f.techAcc >= 1f)
                    {
                        f.techAcc -= 1f;
                        if (f.rl != null) RaceFx.DriftSpark(f.rl.position + Vector3.down * 0.2f, vel, tl);
                        if (f.rr != null) RaceFx.DriftSpark(f.rr.position + Vector3.down * 0.2f, vel, tl);
                    }
                }
                else f.techAcc = 0f;
                if (k.Drifting && k.Grounded && k.Speed > 3f)
                {
                    int lv = k.DriftLevel;
                    f.sparkAcc += (lv <= 0 ? 14f : 55f) * dt;
                    f.smokeAcc += 8f * dt;
                    while (f.sparkAcc >= 1f)
                    {
                        f.sparkAcc -= 1f;
                        if (f.rl != null) RaceFx.DriftSpark(f.rl.position + Vector3.down * 0.2f, vel, lv);
                        if (f.rr != null) RaceFx.DriftSpark(f.rr.position + Vector3.down * 0.2f, vel, lv);
                    }
                    while (f.smokeAcc >= 1f)
                    {
                        f.smokeAcc -= 1f;
                        if (f.rl != null) RaceFx.Smoke(f.rl.position + Vector3.down * 0.2f, vel, grass);
                        if (f.rr != null) RaceFx.Smoke(f.rr.position + Vector3.down * 0.2f, vel, grass);
                    }
                    if (IsPlayer(k)) { f.rumbleT -= dt; if (f.rumbleT <= 0f) { f.rumbleT = 0.1f; Rumble.Driver(0.15f, 0f, 0.1f); } }
                }
                else { f.sparkAcc = 0f; f.smokeAcc = 0f; }

                if (k.Boosting)
                {
                    float kk = Mathf.Clamp01(k.BoostRemaining / 1.0f);
                    f.flameAcc += 60f * dt;
                    while (f.flameAcc >= 1f)
                    {
                        f.flameAcc -= 1f;
                        if (f.exl != null) RaceFx.Flame(f.exl, vel, kk);
                        if (f.exr != null) RaceFx.Flame(f.exr, vel, kk);
                    }
                }
                else f.flameAcc = 0f;

                // 路面高速行駛:後輪揚起淡淡的灰塵尾跡(速度越快越多)
                if (!grass && !k.Drifting && k.Grounded && k.Speed > 9f && Random.value < Mathf.Clamp01((k.Speed - 9f) / 20f) * 7f * dt)
                {
                    if (f.rl != null) RaceFx.Smoke(f.rl.position + Vector3.down * 0.2f, vel * 0.2f, false);
                    if (f.rr != null) RaceFx.Smoke(f.rr.position + Vector3.down * 0.2f, vel * 0.2f, false);
                }
                // 草地/沙地高速行駛時的碎屑
                if (grass && k.Grounded && k.Speed > 6f && Random.value < 12f * dt)
                    RaceFx.Smoke(k.Position - Vector3.up * RaceKart.RideDist, vel * 0.3f, true);
            }
        }

        static void TickAudio(float dt)
        {
            var k = RaceSession.Player;
            float vol = 0f, pitch = 0.6f, sk = 0f;
            if (k != null && !MenuSystem.Blocking && RaceSession.Phase != RacePhase.Finished)
            {
                float frac = Mathf.Clamp01(Mathf.Abs(k.Speed) / Mathf.Max(1f, k.MaxSpeed));
                bool gas = k.ThrottleHeld;
                // 四段「換檔」:每段內轉速上升,換檔時掉一截,聽起來有轉速感
                float gearF = frac * 4f; int g = Mathf.Min(3, (int)gearF); float within = Mathf.Clamp01(gearF - g);
                pitch = (0.62f + 0.38f * within) * (1f + 0.16f * g);
                if (gas && frac < 0.06f) pitch = Mathf.Max(pitch, 0.85f);   // 原地踩油門空轉
                if (!gas) pitch *= 0.85f;                                   // 放油門降 15%
                if (k.Boosting) pitch *= 1.12f;
                vol = Mathf.Lerp(0.18f, 0.5f, frac) * (gas ? 1f : 0.7f);
                if (k.Drifting && k.Grounded && k.Speed > 3f) sk = Mathf.Lerp(0.18f, 0.35f, Mathf.Clamp01(k.DriftCharge / 3.2f));
                else if (k.Grounded && Mathf.Abs(k.Input.Steer) > 0.8f && k.Speed > 0.75f * k.MaxSpeed) sk = 0.1f;   // 高速急轉輕微胎叫
                if (k.Input.Brake > 0.5f && k.Speed > 10f && k.Grounded) sk = Mathf.Max(sk, 0.22f);
            }
            engVol = Mathf.MoveTowards(engVol, vol, dt * 2f);
            engPitch = Mathf.Lerp(engPitch, pitch, 1f - Mathf.Exp(-9f * dt));
            skidVol = Mathf.MoveTowards(skidVol, sk, dt * 3f);
            SfxBus.SetLoop("kart_engine", RaceSfx.Engine, engVol, engPitch);
            SfxBus.SetLoop("kart_skid", RaceSfx.Skid, skidVol, 1f);
        }
    }
}
