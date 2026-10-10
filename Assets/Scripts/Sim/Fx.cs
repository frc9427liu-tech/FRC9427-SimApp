using UnityEngine;

namespace FrcSim
{
    public static class Fx
    {
        static ParticleSystem spark, puff;
        static Material addMat, alphaMat; static Texture2D soft; static bool ready;
        const float TAU = 6.2831853f;

        public static void Init(Transform parent)
        {
            if (ready) return;
            soft = MakeSoft();
            addMat = new Material(Look.Sh("FrcParticle", "Sprites/Default")) { mainTexture = soft };
            if (addMat.HasProperty("_Boost")) addMat.SetFloat("_Boost", 3f);
            alphaMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = soft };
            spark = Make("FxSpark", addMat, 0.8f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f), parent);
            puff = Make("FxPuff", alphaMat, -0.03f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.7f), parent);
            ready = true;
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

        static ParticleSystem Make(string name, Material m, float gravity, AnimationCurve size, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500; main.gravityModifier = gravity; main.startSpeed = 0f; main.startLifetime = 1f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var so = ps.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1f, size);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();   // loop = true + emission disabled: stays alive forever, we only call Emit()
            return ps;
        }

        static void E(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            var ep = new ParticleSystem.EmitParams();
            ep.position = pos; ep.velocity = vel; ep.startSize = size; ep.startLifetime = life; ep.startColor = c;
            ps.Emit(ep, 1);
        }
        static int Cnt(int n) { return JuiceSettings.ReduceFlash ? Mathf.Max(1, n / 2) : n; }
        static Color Ally(bool blue) { return blue ? new Color(0.35f, 0.6f, 1f, 1f) : new Color(1f, 0.4f, 0.35f, 1f); }

        public static void HubScore(Vector3 pos, bool blue, int streak)
        {
            if (!ready) return;
            Color a = Ally(blue), gold = new Color(1f, 0.85f, 0.3f, 1f);
            int n = Cnt(Mathf.Min(16 + streak * 3, 44));
            for (int i = 0; i < n; i++)
            {
                Vector3 d = new Vector3(Random.Range(-0.6f, 0.6f), 1f, Random.Range(-0.6f, 0.6f)).normalized;
                E(spark, pos, d * Random.Range(2.5f, 6f), Random.Range(0.05f, 0.11f), Random.Range(0.5f, 1f), i % 3 == 0 ? gold : a);
            }
            int ring = Cnt(24);
            for (int i = 0; i < ring; i++) { float an = i / (float)ring * TAU; E(spark, pos, new Vector3(Mathf.Cos(an), 0.15f, Mathf.Sin(an)) * 3.2f, 0.09f, 0.45f, a); }
            if (!JuiceSettings.ReduceFlash) E(spark, pos, Vector3.zero, 1.1f, 0.16f, new Color(1f, 1f, 1f, 0.9f));
        }
        public static void HubFlash(Vector3 pos, bool blue)    // hub became ACTIVE
        {
            if (!ready) return;
            Color a = Ally(blue); int ring = Cnt(36);
            for (int i = 0; i < ring; i++) { float an = i / (float)ring * TAU; E(spark, pos, new Vector3(Mathf.Cos(an), 0.1f, Mathf.Sin(an)) * 4.5f, 0.1f, 0.6f, a); }
            for (int i = 0; i < Cnt(10); i++) E(spark, pos, new Vector3(Random.Range(-0.2f, 0.2f), 1f, Random.Range(-0.2f, 0.2f)) * 6f, 0.07f, 0.8f, a);
        }
        public static void Muzzle(Vector3 pos, Vector3 dir)
        {
            if (!ready) return;
            if (!JuiceSettings.ReduceFlash) E(spark, pos, Vector3.zero, 0.35f, 0.06f, new Color(1f, 0.9f, 0.6f, 1f));
            for (int i = 0; i < Cnt(4); i++) E(spark, pos, (dir + Random.insideUnitSphere * 0.25f) * Random.Range(4f, 8f), 0.04f, 0.25f, new Color(1f, 0.8f, 0.4f, 1f));
            for (int i = 0; i < 2; i++) E(puff, pos, dir * 0.8f + Random.insideUnitSphere * 0.2f, 0.12f, 0.6f, new Color(0.8f, 0.8f, 0.8f, 0.35f));
        }
        public static void Collect(Vector3 pos)
        {
            if (!ready) return;
            for (int i = 0; i < Cnt(5); i++) E(spark, pos, new Vector3(Random.Range(-0.5f, 0.5f), 1f, Random.Range(-0.5f, 0.5f)) * Random.Range(1f, 2.2f), 0.05f, 0.35f, new Color(1f, 0.9f, 0.2f, 1f));
        }
        public static void Dust(Vector3 pos)
        {
            if (!ready) return;
            for (int i = 0; i < 2; i++) E(puff, pos, Random.insideUnitSphere * 0.3f, 0.10f, 0.5f, new Color(0.85f, 0.85f, 0.85f, 0.3f));
        }
        public static void Bump(Vector3 pos, float k)
        {
            if (!ready) return;
            int n = Cnt(6 + Mathf.RoundToInt(k * 10f));
            for (int i = 0; i < n; i++) { Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y); E(spark, pos, d * Random.Range(2f, 5f), 0.05f, 0.4f, new Color(1f, 0.75f, 0.4f, 1f)); }
            for (int i = 0; i < 2; i++) E(puff, pos, Random.insideUnitSphere * 0.4f, 0.18f, 0.7f, new Color(0.8f, 0.8f, 0.8f, 0.3f));
        }
        public static void Confetti(Vector3 pos)
        {
            if (!ready) return;
            for (int i = 0; i < Cnt(70); i++)
                E(spark, pos, new Vector3(Random.Range(-6f, 6f), Random.Range(6f, 10f), Random.Range(-6f, 6f)), 0.08f, 2.2f, Color.HSVToRGB(Random.value, 0.7f, 1f));
        }
    }
}