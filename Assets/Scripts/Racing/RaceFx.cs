using UnityEngine;

namespace FrcSim
{
    // 賽車粒子特效(模組 P)。沿用 Fx 的做法:幾個常駐 ParticleSystem(World 空間、關閉自動發射),只用 Emit()。
    // 由 RaceJuice 訂閱 RaceKart 事件 / 每幀觸發;Init(parent) 要在 Root 建好之後呼叫(RaceJuice.Init 內會自動呼叫)。
    public static class RaceFx
    {
        static ParticleSystem spark, flame, puff, star, chip;
        static Material addMat, alphaMat, sparkMat; static Texture2D soft; static bool ready;
        const float TAU = 6.2831853f;

        public static bool Ready { get { return ready && spark != null; } }

        /// 建立常駐粒子系統。parent 銷毀(RaceSession.End)後,下次 Init 會重建。
        public static void Init(Transform parent)
        {
            if (parent == null) return;
            if (ready && spark != null && spark.transform.parent == parent) return;
            Shutdown();
            if (soft == null) soft = MakeSoft();
            addMat = new Material(Look.Sh("FrcParticle", "Sprites/Default")) { mainTexture = soft };
            if (addMat.HasProperty("_Boost")) addMat.SetFloat("_Boost", 3f);
            alphaMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = soft };
            // 原創粒子貼圖(tools/gen_textures.py):煙霧 / 星芒;找不到就保留舊的柔邊圓
            var tSmoke = RaceShaders.Tex("particle_smoke"); if (tSmoke != null) alphaMat.mainTexture = tSmoke;
            sparkMat = new Material(addMat);
            var tSpark = RaceShaders.Tex("particle_spark"); if (tSpark != null) sparkMat.mainTexture = tSpark;
            spark = Make("RFxSpark", sparkMat, 0.8f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f), parent, ParticleSystemRenderMode.Billboard);
            flame = Make("RFxFlame", addMat, 0f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f), parent, ParticleSystemRenderMode.Stretch);
            puff = Make("RFxPuff", alphaMat, -0.03f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f), parent, ParticleSystemRenderMode.Billboard);
            star = Make("RFxStar", sparkMat, 1.2f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f), parent, ParticleSystemRenderMode.Billboard);
            chip = Make("RFxChip", sparkMat, 1.6f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f), parent, ParticleSystemRenderMode.Billboard);
            var fr = flame.GetComponent<ParticleSystemRenderer>(); fr.velocityScale = 0.06f; fr.lengthScale = 2.2f;
            ready = true;
        }

        public static void Shutdown()
        {
            ready = false;
            foreach (var ps in new[] { spark, flame, puff, star, chip }) if (ps != null) Object.Destroy(ps.gameObject);
            spark = flame = puff = star = chip = null;
            if (addMat != null) Object.Destroy(addMat);
            if (alphaMat != null) Object.Destroy(alphaMat);
            if (sparkMat != null) Object.Destroy(sparkMat);
            sparkMat = null;
            addMat = alphaMat = null;
        }

        static Texture2D MakeSoft()
        {
            var t = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a = a * a * (3f - 2f * a);
                px[y * 32 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            t.SetPixels32(px); t.Apply(false, true); return t;
        }

        static ParticleSystem Make(string name, Material m, float gravity, AnimationCurve size, Transform parent, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1200; main.gravityModifier = gravity; main.startSpeed = 0f; main.startLifetime = 1f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var so = ps.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1f, size);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.renderMode = mode;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void E(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            var ep = new ParticleSystem.EmitParams();
            ep.position = pos; ep.velocity = vel; ep.startSize = size; ep.startLifetime = life; ep.startColor = c;
            ps.Emit(ep, 1);
        }
        static int Cnt(int n) { return JuiceSettings.ReduceFlash ? Mathf.Max(1, n / 2) : n; }

        // ---------------------------------------------------------------- 對外
        /// 漂移火花:p = 後輪接地點,v = 車速(世界),lv 0..3(0 = 還沒充能,小白火星;1 藍 2 橘 3 紫)。
        /// 呼叫端自己控制發射量;一次呼叫發 1 顆。side = 該輪在車子的哪一側(-1 左 / +1 右,決定往外噴),back = 車尾方向。
        public static void DriftSpark(Vector3 p, Vector3 v, int lv)
        {
            if (!Ready) return;
            Color c = lv <= 0 ? new Color(1f, 0.95f, 0.8f, 1f) : RacePalette.Drift[Mathf.Clamp(lv, 1, 3) - 1];
            Vector3 flat = v; flat.y = 0f;
            Vector3 back = flat.sqrMagnitude > 0.5f ? -flat.normalized : Vector3.back;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            Vector3 vel = back * Random.Range(1f, 2.5f) + side * Random.Range(-1.8f, 1.8f) + Vector3.up * Random.Range(1f, 2.2f) + flat * 0.25f;
            E(spark, p + Random.insideUnitSphere * 0.08f, vel, lv >= 3 ? Random.Range(0.32f, 0.5f) : (lv >= 1 ? Random.Range(0.26f, 0.42f) : Random.Range(0.16f, 0.26f)), Random.Range(0.3f, 0.5f), c);
        }

        /// 輪胎煙(漂移 / 急轉),路面灰、草地綠褐。
        public static void Smoke(Vector3 p, Vector3 v, bool grass)
        {
            if (!Ready) return;
            Color c = grass ? new Color(0.5f, 0.58f, 0.3f, 0.5f) : new Color(0.88f, 0.86f, 0.82f, 0.38f);
            E(puff, p, Random.insideUnitSphere * 0.3f + Vector3.up * 0.4f + v * 0.1f, Random.Range(0.8f, 1.3f), Random.Range(0.7f, 1.1f), c);
        }

        /// 加速噴焰:ex = 排氣口(forward 朝後),k 0..1 強度。carVel = 車速(讓火焰不被甩在原地)。
        public static void Flame(Transform ex, float k) { Flame(ex, Vector3.zero, k); }
        public static void Flame(Transform ex, Vector3 carVel, float k)
        {
            if (!Ready || ex == null) return;
            Vector3 dir = ex.forward;
            Color hot = Color.Lerp(new Color(1f, 1f, 0.85f, 1f), new Color(1f, 0.55f, 0.12f, 1f), Random.value);
            E(flame, ex.position, dir * (6f * Mathf.Lerp(0.6f, 1f, k)) + carVel + Random.insideUnitSphere * 0.4f, Random.Range(0.3f, 0.5f) * Mathf.Lerp(0.7f, 1.2f, k), Random.Range(0.12f, 0.2f), hot);
        }

        /// 撞擊:黃星 + puff。k 0..1。
        public static void Impact(Vector3 p, float k)
        {
            if (!Ready) return;
            k = Mathf.Clamp01(k);
            int n = Cnt(6 + Mathf.RoundToInt(k * 8f));
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.2f;
                E(star, p, d * Random.Range(2.5f, 5.5f + k * 3f), Random.Range(0.12f, 0.2f), Random.Range(0.3f, 0.5f), new Color(1f, 0.85f, 0.25f, 1f));
            }
            for (int i = 0; i < 4; i++) E(puff, p, Random.insideUnitSphere * 0.8f, 0.3f, 0.7f, new Color(0.9f, 0.9f, 0.9f, 0.35f));
            if (!JuiceSettings.ReduceFlash) E(star, p, Vector3.zero, 0.9f + k * 0.6f, 0.12f, new Color(1f, 1f, 1f, 0.8f));
        }

        /// 使用道具爆發:車身周圍一圈彩色光環(水平外擴)+ 往上的亮點 + 中心閃光。c = 道具代表色,k 0..1 = 強度。
        public static void UseBurst(Vector3 p, Color c, float k)
        {
            if (!Ready) return;
            k = Mathf.Clamp01(k);
            Color hot = Color.Lerp(c, Color.white, 0.35f); hot.a = 1f; c.a = 1f;
            int n = Cnt(18 + Mathf.RoundToInt(k * 8f));
            Vector3 ring = p + Vector3.up * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * TAU;
                E(star, ring, new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a)) * Random.Range(3.5f, 4.5f), Random.Range(0.14f, 0.22f), Random.Range(0.35f, 0.5f), c);
            }
            int m = Cnt(8);
            for (int i = 0; i < m; i++)
                E(spark, ring + Random.insideUnitSphere * 0.4f, new Vector3(Random.Range(-1f, 1f), Random.Range(2f, 4.5f), Random.Range(-1f, 1f)), Random.Range(0.1f, 0.18f), Random.Range(0.4f, 0.65f), hot);
            if (!JuiceSettings.ReduceFlash) E(star, ring, Vector3.zero, 1.6f + k * 0.6f, 0.18f, new Color(hot.r, hot.g, hot.b, 0.7f));
        }

        /// 落地 / 跳躍灰塵圈。p = 輪胎底高度的車位置。
        public static void Dust(Vector3 p, float k)
        {
            if (!Ready) return;
            k = Mathf.Clamp01(k);
            int n = Cnt(6 + Mathf.RoundToInt(k * 8f));
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * TAU + Random.value * 0.4f;
                E(puff, p + Vector3.up * 0.1f, new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * Random.Range(1.5f, 3f), Random.Range(0.9f, 1.4f), Random.Range(0.55f, 0.85f), new Color(0.82f, 0.78f, 0.68f, 0.5f));
            }
        }

        /// 道具箱破裂(03 呼叫):彩色碎片。
        public static void ItemBox(Vector3 p)
        {
            if (!Ready) return;
            for (int i = 0; i < Cnt(24); i++)
                E(chip, p, Random.onUnitSphere * Random.Range(2f, 6f) + Vector3.up * 3f, Random.Range(0.1f, 0.2f), Random.Range(0.5f, 0.9f), Color.HSVToRGB(Random.value, 0.65f, 1f));
            E(star, p, Vector3.zero, 1.2f, 0.15f, new Color(1f, 1f, 1f, 0.8f));
        }

        /// 起步/過線慶祝彩帶(可選)。
        public static void Confetti(Vector3 p)
        {
            if (!Ready) return;
            for (int i = 0; i < Cnt(60); i++)
                E(chip, p + Vector3.up * 2f, new Vector3(Random.Range(-6f, 6f), Random.Range(5f, 9f), Random.Range(-6f, 6f)), Random.Range(0.1f, 0.18f), Random.Range(1.4f, 2.2f), Color.HSVToRGB(Random.value, 0.7f, 1f));
        }
    }
}
