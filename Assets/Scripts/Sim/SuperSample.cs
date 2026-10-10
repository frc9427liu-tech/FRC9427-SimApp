using UnityEngine;

namespace FrcSim
{
    // 超取樣(提高畫質):主相機先畫到「螢幕解析度 × 比例」的貼圖(含 MSAA),再由第二台「貼圖相機」縮放貼到螢幕,
    // 邊緣和細節明顯更乾淨。UI 選單(Overlay Canvas)與 HUD(IMGUI)都在這之後畫,所以不會被蓋住。
    // 比例在「設定」裡調(100% / 150% / 200%);1080p 螢幕 150% = 內部 2880×1620。
    public class SuperSample : MonoBehaviour
    {
        public static float Scale = 1f;   // 目前實際使用的比例(給 HUD 換算螢幕座標用)
        Camera cam;
        RenderTexture rt, ldr;
        bool post;
        GameObject blitGo;
        Material blitMat;
        int w, h;
        float curScale = -1f;

        void Awake() { cam = GetComponent<Camera>(); }

        // 自動降級:載入完成後(前 12 秒不算)連續 6 秒平均 FPS < 28 就把畫質降一級並存起來,避免慢電腦被超取樣拖垮
        float warm = 12f, acc, accT;
        static readonly bool testRun = IsTestRun();
        public static bool IsTestRun()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                switch (a) { case "-batchmode": case "-autostart": case "-shot": case "-menu": case "-padtest": case "-fieldtest": case "-walltest": case "-balltest": case "-soaktest": case "-leotest": case "-leoauto": case "-realtest": case "-projtest": case "-lang": case "-cmttest": case "-model": case "-installtest": case "-halsimtest": case "-selftest": case "-settletest": case "-aitest": case "-bindtest": case "-drilltest": case "-aitestsec": case "-clockstart": case "-vsai": return true; }
            return false;
        }   // 自動測試不要去改使用者存的畫質設定
        void AutoQuality()
        {
            if (testRun || Time.unscaledTime < warm || Time.timeScale == 0f) return;
            acc += 1f; accT += Time.unscaledDeltaTime;
            if (accT < 6f) return;
            float avg = acc / accT; acc = 0f; accT = 0f;
            if (avg < SettingsStore.EffectiveFps * 0.85f && SettingsStore.RenderScaleIndex == 0 && SettingsStore.Shadows)
            {
                SettingsStore.Shadows = false; SettingsStore.Apply();   // 畫質已降到最低還是卡:關陰影
                Debug.Log($"[SuperSample] avg FPS {avg:0} < 28 at min scale → shadows off");
                warm = Time.unscaledTime + 8f;
            }
            else if (avg < SettingsStore.EffectiveFps * 0.85f && SettingsStore.RenderScaleIndex > 0)
            {
                SettingsStore.RenderScaleIndex = SettingsStore.RenderScaleIndex - 1;
                Debug.Log($"[SuperSample] avg FPS {avg:0} < 28 → render scale lowered to {SettingsStore.RenderScale}");
                warm = Time.unscaledTime + 8f;
            }
        }

        void LateUpdate()
        {
            SettingsStore.ApplyPacing();   // 垂直同步整數分頻(選單與遊戲都不再用 targetFrameRate 睡眠限速)
            if (!MenuSystem.Blocking) AutoQuality();
            float s = SettingsStore.RenderScale;
            if (curScale == s && w == Screen.width && h == Screen.height && post == PostFX.Active) return;
            Rebuild(s);
        }

        void Teardown()
        {
            if (cam != null) cam.targetTexture = null;
            if (blitGo != null) Destroy(blitGo);
            blitGo = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            if (ldr != null) { ldr.Release(); Destroy(ldr); ldr = null; }
        }

        void Rebuild(float s)
        {
            Teardown();
            w = Screen.width; h = Screen.height; curScale = s;
            post = PostFX.Active;
            if ((s <= 1.01f && !post) || w < 16 || h < 16) { Scale = 1f; return; }

            rt = new RenderTexture(Mathf.RoundToInt(w * s), Mathf.RoundToInt(h * s), 24, post ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32)
            { antiAliasing = 4, filterMode = FilterMode.Bilinear, useMipMap = false, name = "SuperSampleRT" };
            rt.Create();
            cam.targetTexture = rt;
            Scale = s;

            // 貼圖相機:正交、只看第 31 層的一張鋪滿螢幕的方形,畫在主相機之後(螢幕上)
            blitGo = new GameObject("SSBlit");
            var bc = blitGo.AddComponent<Camera>();
            bc.orthographic = true; bc.orthographicSize = 0.5f; bc.nearClipPlane = 0.1f; bc.farClipPlane = 10f;
            bc.cullingMask = 1 << 31; bc.clearFlags = CameraClearFlags.SolidColor; bc.backgroundColor = Color.black;
            bc.depth = cam.depth + 10f; bc.allowMSAA = false;
            blitGo.transform.position = new Vector3(0f, 1000f, -5f);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.layer = 31;
            q.transform.SetParent(blitGo.transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, 5f);
            q.transform.localScale = new Vector3((float)w / h, 1f, 1f);
            blitMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = rt };
            if (post)   // 後製:相機畫進 HDR RT,OnPostRender 做色調映射/調色/泛光後存進 LDR,貼圖四邊形顯示 LDR
            {
                ldr = new RenderTexture(rt.width, rt.height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear, useMipMap = false, name = "SuperSampleLDR" };
                ldr.Create();
                blitMat.mainTexture = ldr;
            }
            q.GetComponent<Renderer>().sharedMaterial = blitMat;
            q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.GetComponent<Renderer>().receiveShadows = false;
        }

        void OnPostRender()
        {
            if (post && rt != null && ldr != null && PostFX.Instance != null)
            {                PostFX.Instance.Process(rt, ldr);
                if (GlassBackdrop.I != null) GlassBackdrop.I.CaptureFrom(ldr);   // 玻璃的模糊底圖取「後製後」的畫面,顏色才跟看到的一致
            }
        }

        void OnDestroy() { Teardown(); Scale = 1f; }
    }
}
