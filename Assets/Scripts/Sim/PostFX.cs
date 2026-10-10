using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    // Fullscreen post chain (HDR -> bloom-lite -> ACES -> grade -> vignette -> dither).
    // NOT using OnRenderImage: SuperSample renders the camera into an RT, so SuperSample.OnPostRender calls Process(hdrRT, ldrRT).
    // Quality: 0 off, 1 = tonemap/grade/vignette, 2 = +bloom, 3 = +AO (AO needs depth texture; see notes).
    [RequireComponent(typeof(Camera))]
    public class PostFX : MonoBehaviour
    {
        public static PostFX Instance;
        public static int Quality
        {
            get => PlayerPrefs.GetInt("postfx", 2);
            set { PlayerPrefs.SetInt("postfx", Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); if (Instance != null) Instance.Refresh(); }
        }
        public static bool Active => Instance != null && Instance.mat != null && Quality > 0;
        public static float MenuBlur;                       // 0..1 target; MenuSystem sets this

        public Shader shader;                                // optional serialized reference

        [Header("Tonemap / grade")]
        public float Exposure = 1.25f;
        [Range(0, 1)] public float AcesMix = 1f;
        [Range(0, 0.6f)] public float SCurve = 0.22f;
        public float Saturation = 1.10f;
        public Color ShadowTint = new Color(0.93f, 0.99f, 1.07f);
        public Color HighlightTint = new Color(1.05f, 1.00f, 0.93f);

        [Header("Vignette")]
        [Range(0, 1)] public float VigStrength = 0.55f;
        public float VigInner = 0.45f, VigOuter = 1.05f;
        [Range(0, 1)] public float VigRound = 0.5f;
        public Color VigColor = new Color(0.35f, 0.40f, 0.52f);

        [Header("Bloom")]
        public float Threshold = 1.25f;
        [Range(0.01f, 1)] public float Knee = 0.5f;
        public float BloomIntensity = 0.28f;
        [Range(0, 1)] public float Scatter = 0.65f;
        [Range(2, 6)] public int Iterations = 5;

        [Header("AO (quality 3)")]
        public float AORadius = 0.4f, AOIntensity = 0.55f, AORange = 1.0f;

        [Header("Misc")]
        public float Dither = 1f;
        public float MenuBlurMax = 0.55f;

        Camera cam;
        Material mat;
        float blurCur;
        readonly RenderTexture[] rts = new RenderTexture[7];

        void Awake() { cam = GetComponent<Camera>(); Instance = this; }

        void OnEnable()
        {
            if (mat == null)
            {
                var sh = shader != null ? shader : Look.Sh("FrcPost", "Hidden/FrcSim/Post");
                if (sh == null || !sh.isSupported) { Debug.LogWarning("[PostFX] shader missing/unsupported, disabled"); return; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            Refresh();
        }

        void OnDestroy() { if (mat != null) Destroy(mat); if (Instance == this) Instance = null; }

        public void Refresh()
        {
            if (cam == null) cam = GetComponent<Camera>();
            int q = Quality;
            cam.allowHDR = q > 0;
            if (q >= 3) cam.depthTextureMode |= DepthTextureMode.Depth; else cam.depthTextureMode &= ~DepthTextureMode.Depth;
            // SuperSample notices Active changing in LateUpdate and rebuilds its RTs (HDR RT + LDR RT).
        }

        void Update() { blurCur = Mathf.MoveTowards(blurCur, MenuBlur, Time.unscaledDeltaTime * 3f); }

        // src: HDR RT the camera rendered into; dst: LDR RT shown on the screen quad.
        public void Process(RenderTexture src, RenderTexture dst)
        {
            int q = Quality;
            if (mat == null || q <= 0) { Graphics.Blit(src, dst); return; }

            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            bool blurMode = blurCur > 0.01f;
            bool bloom = (q >= 2 && BloomIntensity > 0.001f) || blurMode;

            mat.SetFloat("_IsLinear", lin ? 1f : 0f);
            mat.SetFloat("_Exposure", Exposure);
            mat.SetFloat("_ACESMix", AcesMix);
            mat.SetFloat("_SCurve", SCurve);
            mat.SetFloat("_Saturation", Saturation);
            mat.SetColor("_ShadowTint", ShadowTint);
            mat.SetColor("_HighlightTint", HighlightTint);
            mat.SetFloat("_VigStrength", VigStrength);
            mat.SetFloat("_VigInner", VigInner);
            mat.SetFloat("_VigOuter", VigOuter);
            mat.SetFloat("_VigRound", VigRound);
            mat.SetColor("_VigColor", VigColor);
            mat.SetFloat("_Aspect", (float)src.width / src.height);
            mat.SetFloat("_Dither", Dither);
            mat.SetFloat("_Scatter", Scatter);

            float knee = Threshold * Knee + 1e-4f;
            mat.SetFloat("_Threshold", Threshold);
            mat.SetVector("_Curve", new Vector4(Threshold - knee, knee * 2f, 0.25f / knee, 0f));
            mat.SetFloat("_NoThreshold", blurMode ? 1f : 0f);

            bool ao = q >= 3 && !cam.orthographic;
            if (ao)
            {
                mat.EnableKeyword("_AO_ON");
                mat.SetFloat("_AORadius", AORadius);
                mat.SetFloat("_AOIntensity", AOIntensity);
                mat.SetFloat("_AORange", AORange);
                mat.SetFloat("_AOProj", 1f / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)));
            }
            else mat.DisableKeyword("_AO_ON");

            RenderTexture up = null;
            int n = 0;
            if (bloom)
            {
                var fmt = RenderTextureFormat.DefaultHDR;
                int w = Mathf.Max(8, src.width >> 1), h = Mathf.Max(8, src.height >> 1);
                rts[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
                rts[0].filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rts[0], mat, 0);
                n = 1;
                for (; n < Mathf.Min(Iterations, rts.Length); n++)
                {
                    w >>= 1; h >>= 1;
                    if (w < 8 || h < 8) break;
                    rts[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
                    rts[n].filterMode = FilterMode.Bilinear;
                    Graphics.Blit(rts[n - 1], rts[n], mat, 1);
                }
                up = rts[n - 1];
                for (int i = n - 2; i >= 0; i--)
                {
                    var t = RenderTexture.GetTemporary(rts[i].width, rts[i].height, 0, fmt);
                    t.filterMode = FilterMode.Bilinear;
                    mat.SetTexture("_BloomTex", rts[i]);
                    Graphics.Blit(up, t, mat, 2);
                    if (up != rts[n - 1]) RenderTexture.ReleaseTemporary(up);
                    up = t;
                }
            }

            mat.SetTexture("_BloomTex", bloom ? (Texture)up : Texture2D.blackTexture);
            mat.SetFloat("_BloomIntensity", (bloom && !blurMode) ? BloomIntensity : 0f);
            mat.SetFloat("_MenuBlur", blurCur * MenuBlurMax);
            Graphics.Blit(src, dst, mat, 3);

            if (bloom)
            {
                if (up != rts[n - 1]) RenderTexture.ReleaseTemporary(up);
                for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(rts[i]); rts[i] = null; }
            }
        }
    }
}