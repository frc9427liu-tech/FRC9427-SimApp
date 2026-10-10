using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    public static class Look
    {
        public static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        // ---------- shader access (build-safe: Resources first, Shader.Find fallback) ----------
        static readonly Dictionary<string, Shader> cache = new Dictionary<string, Shader>();
        public static Shader Sh(string file, string findName = null)
        {
            if (cache.TryGetValue(file, out var s) && s != null) return s;
            s = Resources.Load<Shader>("Shaders/" + file);
            if (s == null && findName != null) s = Shader.Find(findName);
            if (s == null || !s.isSupported) { Debug.LogWarning("[Look] shader '" + file + "' missing/unsupported; falling back to Standard"); s = Shader.Find("Standard"); }
            cache[file] = s;
            return s;
        }

        // ---------- lighting ----------
        public static void SetupLighting(Light sun)
        {
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.955f, 0.88f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.5f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.46f, 0.55f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.36f, 0.44f);
            RenderSettings.ambientGroundColor  = new Color(0.14f, 0.15f, 0.18f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 0.55f;

            var fog = new Color(0.045f, 0.06f, 0.10f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = 45f; RenderSettings.fogEndDistance = 95f;
            var cam = Camera.main;
            if (cam != null) { cam.backgroundColor = fog; cam.allowHDR = true; }
        }

        // Call from SettingsStore.Apply() so user-changed quality does not undo these.
        public static void ApplyShadowQuality(bool on)
        {
            QualitySettings.shadows = on ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 32f;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
        }

        // One-shot realtime reflection probe covering the arena (6 camera renders once, then frozen).
        static GameObject probeGo;
        public static void BakeProbe()
        {
            if (probeGo != null) Object.Destroy(probeGo);
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.position = new Vector3(L / 2f, 2.0f, W / 2f);
            var p = probeGo.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            p.resolution = 128;
            p.hdr = true;
            p.boxProjection = true;
            p.size = new Vector3(L + 12f, 20f, W + 12f);
            p.center = new Vector3(0f, 7f, 0f);
            p.clearFlags = ReflectionProbeClearFlags.SolidColor;
            p.backgroundColor = new Color(0.045f, 0.06f, 0.10f);
            p.nearClipPlane = 0.3f; p.farClipPlane = 60f;
            p.intensity = 1f;
            p.RenderProbe();   // renders at end of this frame; later robots/balls are not in it (fine: soft 128px env)
        }

        // ---------- materials ----------
        public static void Classify(Color c, out float smooth, out float metal)
        {
            float mx = Mathf.Max(c.r, c.g, c.b), mn = Mathf.Min(c.r, c.g, c.b);
            if (mx - mn > 0.25f) { smooth = 0.36f; metal = 0f; }          // colored plastic / paint (HUB, bump, bumpers, alliance parts)
            else if (mx < 0.22f) { smooth = 0.22f; metal = 0.03f; }       // black plastic / rubber
            else                 { smooth = 0.30f; metal = 0.22f; }       // aluminium / steel / white parts
        }

        static Texture2D carpetData, carpetNorm;
        public static Material CarpetMaterial(bool useNormal)
        {
            if (carpetData == null) ProcTex.Carpet(out carpetData, out carpetNorm);
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            var m = new Material(Sh("FrcCarpet"));
            m.SetColor("_Color", new Color(0.16f, 0.185f, 0.26f));
            m.SetTexture("_DataTex", carpetData);
            m.SetFloat("_FineAmt", 0.10f); m.SetFloat("_MidAmt", 0.06f); m.SetFloat("_MacroAmt", 0.10f); m.SetFloat("_MidScale", 2.0f); m.SetFloat("_FineScale", 4.5f); m.SetFloat("_NormStrength", 0.3f);   // 地毯質感收斂:去掉迷彩感的大塊斑紋
            m.SetTexture("_NormTex", carpetNorm);
            m.SetVector("_Pool", new Vector4(2f, 1.2f, (L - 4f) / 5f, (W - 2.4f) / 2f));
            m.SetVector("_FieldRect", new Vector4(0f, 0f, L, W));
            if (useNormal) m.EnableKeyword("_NORMALON"); else m.DisableKeyword("_NORMALON");
            return m;
        }

        // Make sure a carpet mesh has UVs + tangents so the normal map is safe. Returns false if it could not (then keep _NORMALON off).
        public static bool EnsureTangents(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var mesh = mf.sharedMesh;
            if (!mesh.isReadable) return false;
            try
            {
                if (mesh.uv.Length != mesh.vertexCount)
                {
                    var vs = mesh.vertices; var uv = new Vector2[vs.Length];
                    for (int i = 0; i < vs.Length; i++) uv[i] = new Vector2(vs[i].x, vs[i].z);   // carpet is horizontal
                    mesh.uv = uv;
                }
                mesh.RecalculateTangents();
                return mesh.tangents.Length == mesh.vertexCount;
            }
            catch { return false; }
        }

        public static Material GlowMaterial(Color c, float intensity)
        {
            var m = new Material(Sh("FrcGlow"));
            m.SetColor("_Color", c); m.SetFloat("_Intensity", intensity);
            return m;
        }

        public static Material FuelMaterial()
        {
            var m = new Material(Sh("FrcFuel"));
            m.SetColor("_Color", new Color(0.98f, 0.80f, 0.04f));
            m.SetFloat("_Glossiness", 0.38f);
            m.SetFloat("_Metallic", 0f);
            m.SetColor("_RimColor", new Color(1f, 0.92f, 0.45f));
            m.SetFloat("_RimPower", 2.5f); m.SetFloat("_RimAmt", 0.22f); m.SetFloat("_Emit", 0.05f);
            m.enableInstancing = true;
            return m;
        }

        public static Material GlassMaterial() => new Material(Sh("FrcGlass"));

        public static Material TapeMaterial(Color c) => FieldBuilder.MakeMat(c, 0.35f, 0f);

        public static Material ArenaMaterial(Color c, float height, float bottom, float top, Color? accent = null, float accentY = 6.5f, float accentI = 0f)
        {
            var m = new Material(Sh("FrcArena"));
            m.SetColor("_Color", c);
            m.SetFloat("_Height", height); m.SetFloat("_Bottom", bottom); m.SetFloat("_Top", top);
            m.SetFloat("_PanelW", height > 2f ? 2.4f : 1000f);       // floors: no seams
            m.SetFloat("_SeamAmt", height > 2f ? 0.12f : 0f);
            if (height <= 2f) { m.SetFloat("_Smooth", 0.04f); m.SetColor("_Color", c * 0.55f); }   // 場外地板:壓暗、不反光,場地才有主體感
            if (accent.HasValue) { m.SetColor("_Accent", accent.Value); m.SetFloat("_AccentY", accentY); m.SetFloat("_AccentI", accentI); }
            return m;
        }

        static Texture2D crowd;
        public static Material CrowdMaterial(float length)
        {
            if (crowd == null) crowd = ProcTex.Crowd();
            var m = FieldBuilder.MakeMat(Color.white, 0.10f, 0f);
            m.mainTexture = crowd;
            m.mainTextureScale = new Vector2(length / 4f, 1f);     // 8 cells x 0.5 m = 4 m per tile
            return m;
        }
    }

    // LED ring on the HUB rim. Placeholder geometry: a square ring at the opening height; align with the real hex opening if wanted.
    public class HubGlow : MonoBehaviour
    {
        Material mat;
        public static HubGlow Create(Transform parent, Vector3 hubCenterXZ, float size, float rimY, Color c)
        {
            var go = new GameObject("HubGlow"); go.transform.SetParent(parent, false);
            var hg = go.AddComponent<HubGlow>();
            hg.mat = Look.GlowMaterial(c, 3f);
            float s = size - 0.1f, t = 0.035f;
            void Strip(string n, Vector3 off, Vector3 scale)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n;
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(go.transform, false);
                g.transform.position = new Vector3(hubCenterXZ.x, rimY + 0.012f, hubCenterXZ.z) + off;
                g.transform.localScale = scale;
                var r = g.GetComponent<Renderer>(); r.sharedMaterial = hg.mat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            Strip("N", new Vector3(0, 0,  s / 2f), new Vector3(s, 0.02f, t));
            Strip("S", new Vector3(0, 0, -s / 2f), new Vector3(s, 0.02f, t));
            Strip("E", new Vector3( s / 2f, 0, 0), new Vector3(t, 0.02f, s));
            Strip("W", new Vector3(-s / 2f, 0, 0), new Vector3(t, 0.02f, s));
            return hg;
        }
        // active match: alliance color; post-match safe: green (Game Manual Table 5-3, partially read; verify the rest)
        public void SetState(Color c, float intensity = 3f) { mat.SetColor("_Color", c); mat.SetFloat("_Intensity", intensity); }
    }
}