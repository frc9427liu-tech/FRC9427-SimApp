using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FrcSim
{
    // ------------------------------------------------------------------ spring (critically/under-damped, sub-stepped)
    public struct Spring
    {
        public float x, v, target;
        public Spring(float init) { x = init; v = 0f; target = init; }
        // response = seconds for one natural period, damping 1 = no overshoot. returns true while moving
        public bool Step(float dt, float response = 0.35f, float damping = 0.75f)
        {
            if (Mathf.Abs(x - target) < 0.0005f && Mathf.Abs(v) < 0.0005f) { x = target; v = 0f; return false; }
            float w = 2f * Mathf.PI / Mathf.Max(0.05f, response), k = w * w, c = 2f * damping * w;
            dt = Mathf.Min(dt, 0.05f);
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f)); float h = dt / n;
            for (int i = 0; i < n; i++) { v += (k * (target - x) - c * v) * h; x += v * h; }
            return true;
        }
    }

    // ------------------------------------------------------------------ style presets (units = design px / GUI units)
    public class GlassStyle
    {
        public Color tint = new Color(.055f, .075f, .110f, .44f), selTint = new Color(.25f, .62f, 1f, .62f);
        public float tintAdapt = .22f, refract = 22f, dispersion = .35f, rim = 1f, bevel = 26f, rimWidth = 1.6f;
        public float shadow = .42f, shadowOffset = 18f, pad = 56f, squircle = 2.6f;
        public bool overlay;

        public static GlassStyle Card() => new GlassStyle();
        public static GlassStyle HudPanel() => new GlassStyle { tint = new Color(.055f, .075f, .110f, .36f), refract = 7f, dispersion = .30f, rim = .9f, bevel = 12f, rimWidth = 1.2f, shadow = .30f, shadowOffset = 6f, pad = 20f };
        public static GlassStyle HudCard() => new GlassStyle { tint = new Color(.055f, .075f, .110f, .46f), tintAdapt = .24f, refract = 14f, bevel = 20f, rimWidth = 1.4f, shadow = .45f, shadowOffset = 14f, pad = 44f };
        public static GlassStyle Pill() => new GlassStyle { overlay = true, tint = new Color(1, 1, 1, .07f), selTint = new Color(.25f, .62f, 1f, .62f), refract = 0, dispersion = 0, rim = .6f, bevel = 10f, rimWidth = 1.2f, shadow = 0, shadowOffset = 0, pad = 4f, tintAdapt = 0 };
        public static GlassStyle PillPrimary() { var s = Pill(); s.tint = new Color(.25f, .62f, 1f, .55f); s.selTint = new Color(.32f, .68f, 1f, .85f); s.rim = 1f; return s; }
        public static GlassStyle BarTrack() { var s = Pill(); s.tint = new Color(1, 1, 1, .22f); s.rim = .35f; s.bevel = 6f; s.rimWidth = 1f; s.pad = 2f; return s; }
        public static GlassStyle BarFill(Color c) { var s = Pill(); s.tint = new Color(c.r, c.g, c.b, .95f); s.rim = .6f; s.bevel = 6f; s.rimWidth = 1f; s.pad = 2f; return s; }
    }

    // ------------------------------------------------------------------ shared state + material helper
    public static class UiGlass
    {
        public static bool Ready;                 // blur textures valid and shader usable
        public static bool Disabled = HasArg("-noglass") || PlayerPrefs.GetInt("noGlass", 0) == 1;
        public static bool FlipY = HasArg("-glassflip");     // toggle if backdrop appears upside down on some GPU/API
        public static bool UseOnRenderImage = HasArg("-glassori"); // fallback capture path
        public static bool DebugView = HasArg("-glassdebug");
        public static int Active;                 // enabled uGUI GlassPanels
        public static int HudFrame = -10;         // last frame the HUD drew glass
        static Shader shader;
        static bool tried;
        static readonly Vector2 L = new Vector2(-0.55f, 0.83f).normalized;
        static readonly int idRect = Shader.PropertyToID("_Rect"), idStyle = Shader.PropertyToID("_Style"), idState = Shader.PropertyToID("_State"),
                            idMisc = Shader.PropertyToID("_Misc"), idLight = Shader.PropertyToID("_Light"), idTint = Shader.PropertyToID("_Tint");

        static bool HasArg(string a) { foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true; return false; }

        public static Shader GlassShader
        {
            get
            {
                if (!tried) { tried = true; shader = Resources.Load<Shader>("FrcGlass/LiquidGlass"); if (shader != null && !shader.isSupported) shader = null; }
                return shader;
            }
        }
        public static Material NewMaterial() { var s = GlassShader; return s == null ? null : new Material(s) { hideFlags = HideFlags.HideAndDontSave }; }

        // sizePx = quad size in screen pixels (includes pad), u = pixels per design unit
        public static void Apply(Material m, Vector2 sizePx, float u, GlassStyle s, float radius, Color tint, float glow, float press)
        {
            m.SetVector(idRect, new Vector4(sizePx.x, sizePx.y, s.pad * u, radius * u));
            m.SetVector(idStyle, new Vector4(s.refract * u, s.dispersion, s.rim, s.bevel * u));
            m.SetVector(idState, new Vector4(glow, press, s.shadow, s.shadowOffset * u));
            m.SetVector(idMisc, new Vector4(u, s.squircle, s.rimWidth * u, s.overlay ? 1f : 0f));
            m.SetVector(idLight, new Vector4(L.x, L.y, FlipY ? 1f : 0f, s.tintAdapt));
            m.SetVector(idTint, new Vector4(tint.r, tint.g, tint.b, tint.a));
        }
    }

    // ------------------------------------------------------------------ backdrop capture + dual Kawase blur
    public class GlassBackdrop : MonoBehaviour
    {
        public static GlassBackdrop I;
        public float blurOffset = 1.25f;                  // bigger = blurrier (1.0 - 2.0)
        protected Camera cam;
        Material km;
        readonly RenderTexture[] L = new RenderTexture[4];
        RenderTexture outRT;
        CommandBuffer cb;
        bool attached, primed; int w, h, attachFrame; float builtScale;
        protected virtual bool UseCB => !PostFX.Active;   // 有後製時改由 SuperSample.OnPostRender 餵 LDR 畫面(CaptureFrom)
        protected bool runThisFrame;

        void OnEnable()
        {
            I = this; cam = GetComponent<Camera>();
            var sh = Resources.Load<Shader>("FrcGlass/DualKawase");
            if (sh == null || !sh.isSupported || UiGlass.GlassShader == null) { enabled = false; return; }
            km = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }

        void OnDisable()
        {
            Detach(); Free();
            if (km != null) Destroy(km);
            if (I == this) I = null;
            UiGlass.Ready = false;
        }

        void Update()
        {
            if (outRT == null || Screen.width != w || Screen.height != h || !Mathf.Approximately(builtScale, SuperSample.Scale))
            { Detach(); Alloc(); if (UseCB) BuildCB(); }

            bool need = UiGlass.Active > 0 || Time.frameCount - UiGlass.HudFrame <= 2;
            int interval = (Application.targetFrameRate > 0 && Application.targetFrameRate <= 30) ? 1 : 2;
            runThisFrame = need && (!primed || Time.frameCount % interval == 0);
            if (UseCB)
            {
                if (runThisFrame && !attached) { cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cb); attached = true; attachFrame = Time.frameCount; }
                else if (!runThisFrame && attached) { cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, cb); attached = false; }
            }
            if (runThisFrame && !primed && Time.frameCount > attachFrame + 1) primed = true;
            UiGlass.Ready = primed && !UiGlass.Disabled;
        }

        void Detach() { if (attached && cam != null && cb != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, cb); attached = false; cb?.Release(); cb = null; primed = false; }

        void Free()
        {
            for (int i = 0; i < L.Length; i++) if (L[i] != null) { L[i].Release(); Destroy(L[i]); L[i] = null; }
            if (outRT != null) { outRT.Release(); Destroy(outRT); outRT = null; }
        }

        void Alloc()
        {
            Free();
            w = Screen.width; h = Screen.height; builtScale = SuperSample.Scale;
            int sw = Mathf.Max(64, w), sh = Mathf.Max(64, h);
            L[0] = MakeRT(sw / 2, sh / 2); L[1] = MakeRT(sw / 4, sh / 4); L[2] = MakeRT(sw / 8, sh / 8); L[3] = MakeRT(sw / 16, sh / 16);
            outRT = MakeRT(sw / 4, sh / 4);
            Shader.SetGlobalTexture("_FrcGlassBlur", outRT);
            Shader.SetGlobalTexture("_FrcGlassSoft", L[1]);
        }

        static RenderTexture MakeRT(int rw, int rh)
        {
            var rt = new RenderTexture(Mathf.Max(8, rw), Mathf.Max(8, rh), 0, RenderTextureFormat.ARGB32)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false, name = "FrcGlassRT" };
            rt.Create();
            var a = RenderTexture.active; RenderTexture.active = rt; GL.Clear(false, true, new Color(.25f, .28f, .33f, 1f)); RenderTexture.active = a;
            return rt;
        }

        // source resolution in pixels (the SuperSample RT is Scale x the screen)
        Vector2 SrcSize() { float s = Mathf.Max(1f, SuperSample.Scale); return new Vector2(Screen.width * s, Screen.height * s); }

        void BuildCB()
        {
            cb = new CommandBuffer { name = "FrcGlassBlur" };
            var src = BuiltinRenderTextureType.CurrentActive;
            var ss = SrcSize();
            Step(src, ss.x, ss.y, L[0], 0);
            Step(L[0], L[0].width, L[0].height, L[1], 0);
            Step(L[1], L[1].width, L[1].height, L[2], 0);
            Step(L[2], L[2].width, L[2].height, L[3], 0);
            Step(L[3], L[3].width, L[3].height, L[2], 1);
            Step(L[2], L[2].width, L[2].height, outRT, 1);
        }
        void Step(RenderTargetIdentifier s, float sw, float sh, RenderTexture d, int pass)
        {
            cb.SetGlobalVector("_FrcSrcTexel", new Vector4(1f / sw, 1f / sh, blurOffset, 0));
            cb.Blit(s, d, km, pass);
        }

        public void CaptureFrom(RenderTexture src) { if (runThisFrame && !UseCB) RunChain(src); }

        // used by the OnRenderImage fallback subclass
        protected void RunChain(RenderTexture src)
        {
            if (km == null || outRT == null) return;
            var ss = new Vector2(src.width, src.height);
            Run(src, ss, L[0], 0); Run(L[0], new Vector2(L[0].width, L[0].height), L[1], 0);
            Run(L[1], new Vector2(L[1].width, L[1].height), L[2], 0); Run(L[2], new Vector2(L[2].width, L[2].height), L[3], 0);
            Run(L[3], new Vector2(L[3].width, L[3].height), L[2], 1); Run(L[2], new Vector2(L[2].width, L[2].height), outRT, 1);
        }
        void Run(Texture s, Vector2 sz, RenderTexture d, int pass)
        {
            km.SetVector("_FrcSrcTexel", new Vector4(1f / sz.x, 1f / sz.y, blurOffset, 0));
            Graphics.Blit(s, d, km, pass);
        }

        void OnGUI()
        {
            if (UiGlass.DebugView && outRT != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(Screen.width - 360, 10, 350, 350f * Screen.height / Screen.width), outRT, ScaleMode.StretchToFill, false);
        }
    }

    // Fallback if the CommandBuffer path shows garbage (MSAA/back-buffer read problems): run with  -glassori
    public class GlassBackdropORI : GlassBackdrop
    {
        protected override bool UseCB => false;
        void OnRenderImage(RenderTexture s, RenderTexture d)
        {
            if (runThisFrame) RunChain(s);
            Graphics.Blit(s, d);
        }
    }

    public class GlassBoot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (UiGlass.Disabled || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var g = new GameObject("GlassBoot"); DontDestroyOnLoad(g); g.AddComponent<GlassBoot>();
        }
        void Update()
        {
            if (Time.frameCount % 20 != 0 || GlassBackdrop.I != null || UiGlass.GlassShader == null) return;
            var ss = FindFirstObjectByType<SuperSample>();
            Camera c = ss != null ? ss.GetComponent<Camera>() : Camera.main;
            if (c == null || !c.isActiveAndEnabled) return;
            if (UiGlass.UseOnRenderImage) c.gameObject.AddComponent<GlassBackdropORI>(); else c.gameObject.AddComponent<GlassBackdrop>();
        }
    }

    // ------------------------------------------------------------------ uGUI panel (put on a RawImage; use GlassPanel.Make)
    [RequireComponent(typeof(RawImage))]
    public class GlassPanel : MonoBehaviour
    {
        public GlassStyle Style; public float Radius = 64f;
        public RectTransform Root;                    // the visible rect (children/labels go here); this object is Root + pad
        public float SelectedT, HoverT, PressT;       // targets 0..1
        public float Opacity = 1f;
        public Spring sSel = new Spring(0), sHov = new Spring(0), sPress = new Spring(0);
        RawImage ri; Material mat; RectTransform rt; Canvas cv; Vector2 lastSize; float lastU; bool dirty = true; float lastOpacity = -1f; float enabledAt; bool flat;

        public static GlassPanel Make(Transform parent, string name, float x, float y, float w, float h, float radius, GlassStyle style)
        {
            var root = UiKit.Rect(name, parent);
            UiKit.PlaceTL(root, x, y, w, h);
            var g = UiKit.Rect("Glass", root);
            g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one;
            g.offsetMin = new Vector2(-style.pad, -style.pad); g.offsetMax = new Vector2(style.pad, style.pad);
            var ri = g.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false;
            var gp = g.gameObject.AddComponent<GlassPanel>();
            gp.Style = style; gp.Radius = radius; gp.Root = root;
            gp.mat = UiGlass.NewMaterial();
            if (gp.mat != null) ri.material = gp.mat;
            else ri.color = style.overlay ? style.tint : new Color(.06f, .10f, .17f, .74f);   // fallback = flat fill
            return gp;
        }

        void Awake() { ri = GetComponent<RawImage>(); rt = (RectTransform)transform; }
        void OnEnable() { UiGlass.Active++; dirty = true; enabledAt = Time.unscaledTime; }
        void OnDisable() { UiGlass.Active--; }
        void OnDestroy() { if (mat != null) Destroy(mat); }
        public void MarkDirty() { dirty = true; }

        void LateUpdate()
        {
            if (mat == null) return;
            float dt = Time.unscaledDeltaTime;
            sSel.target = SelectedT; sHov.target = HoverT; sPress.target = PressT;
            bool moving = sSel.Step(dt, .32f, .72f) | sHov.Step(dt, .22f, .80f) | sPress.Step(dt, .18f, .70f);
            if (cv == null) cv = GetComponentInParent<Canvas>();
            float u = cv != null ? cv.scaleFactor : 1f;
            Vector2 sz = rt.rect.size * u;
            if (!(dirty || moving || sz != lastSize || u != lastU)) { if (Opacity != lastOpacity || (ri.color.a > 0f) != UiGlass.Ready) ApplyOpacity(); return; }
            lastSize = sz; lastU = u; dirty = false;
            var s = Style;
            Color t = s.overlay ? Color.Lerp(s.tint, s.selTint, Mathf.Clamp01(sSel.x)) : s.tint;
            if (s.overlay) t.a += 0.06f * sHov.x + 0.14f * sPress.x;
            UiGlass.Apply(mat, sz, u, s, Radius, t, sHov.x + sSel.x * 0.5f, sPress.x);
            if (Root != null && s.overlay) { float sc = 1f + 0.02f * sSel.x - 0.04f * sPress.x; Root.localScale = new Vector3(sc, sc, 1f); }
            ApplyOpacity();
        }
        void ApplyOpacity() { lastOpacity = Opacity; var c = ri.color; c.a = UiGlass.Ready ? Opacity : 0f; ri.color = c; }
    }

    // ------------------------------------------------------------------ IMGUI drawing (HUD)
    public static class GlassGL
    {
        static Material mat;
        // r in GUI units (inside GUI.matrix scale). glow/press 0..1 (hover/press spring values), opacity 0..1
        public static void Draw(Rect r, float radius, GlassStyle s, float glow = 0f, float press = 0f, float opacity = 1f, float selected = 0f)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (mat == null) { mat = UiGlass.NewMaterial(); if (mat == null) return; }
            float u = GUI.matrix.m00;
            float pad = s.pad * u;
            float x = r.x * u - pad, y = r.y * u - pad, w = r.width * u + 2f * pad, h = r.height * u + 2f * pad;
            Color t = s.overlay ? Color.Lerp(s.tint, s.selTint, selected) : s.tint;
            if (s.overlay) t.a += 0.06f * glow + 0.14f * press;
            UiGlass.Apply(mat, new Vector2(w, h), u, s, radius, t, glow, press);
            GL.PushMatrix();
            mat.SetPass(0);
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);      // y down, pixels
            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 1f, 1f, opacity));
            GL.TexCoord2(0, 1); GL.Vertex3(x, y, 0);                    // v = 1 at the top
            GL.TexCoord2(1, 1); GL.Vertex3(x + w, y, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(x + w, y + h, 0);
            GL.TexCoord2(0, 0); GL.Vertex3(x, y + h, 0);
            GL.End();
            GL.PopMatrix();
        }
    }
}
