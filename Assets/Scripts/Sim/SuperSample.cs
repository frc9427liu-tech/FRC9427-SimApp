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
        RenderTexture rt;
        GameObject blitGo;
        Material blitMat;
        int w, h;
        float curScale = -1f;

        void Awake() { cam = GetComponent<Camera>(); }

        // 自動降級:載入完成後(前 12 秒不算)連續 6 秒平均 FPS < 28 就把畫質降一級並存起來,避免慢電腦被超取樣拖垮
        float warm = 12f, acc, accT;
        static readonly bool testRun = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autostart") >= 0 || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-shot") >= 0;   // 自動測試不要去改使用者存的畫質設定
        void AutoQuality()
        {
            if (testRun || Time.unscaledTime < warm || Time.timeScale == 0f) return;
            acc += 1f; accT += Time.unscaledDeltaTime;
            if (accT < 6f) return;
            float avg = acc / accT; acc = 0f; accT = 0f;
            if (avg < 28f && SettingsStore.RenderScaleIndex == 0 && SettingsStore.Shadows)
            {
                SettingsStore.Shadows = false; SettingsStore.Apply();   // 畫質已降到最低還是卡:關陰影
                Debug.Log($"[SuperSample] avg FPS {avg:0} < 28 at min scale → shadows off");
                warm = Time.unscaledTime + 8f;
            }
            else if (avg < 28f && SettingsStore.RenderScaleIndex > 0)
            {
                SettingsStore.RenderScaleIndex = SettingsStore.RenderScaleIndex - 1;
                Debug.Log($"[SuperSample] avg FPS {avg:0} < 28 → render scale lowered to {SettingsStore.RenderScale}");
                warm = Time.unscaledTime + 8f;
            }
        }

        void LateUpdate()
        {
            int wantFps = SettingsStore.EffectiveFps;
            if (Application.targetFrameRate != wantFps) Application.targetFrameRate = wantFps;   // 選單時 30、遊戲中用設定值(預設 60)
            if (!MenuSystem.Blocking) AutoQuality();
            float s = SettingsStore.RenderScale;
            if (curScale == s && w == Screen.width && h == Screen.height) return;
            Rebuild(s);
        }

        void Teardown()
        {
            if (cam != null) cam.targetTexture = null;
            if (blitGo != null) Destroy(blitGo);
            blitGo = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        }

        void Rebuild(float s)
        {
            Teardown();
            w = Screen.width; h = Screen.height; curScale = s;
            if (s <= 1.01f || w < 16 || h < 16) { Scale = 1f; return; }

            rt = new RenderTexture(Mathf.RoundToInt(w * s), Mathf.RoundToInt(h * s), 24, RenderTextureFormat.ARGB32)
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
            q.GetComponent<Renderer>().sharedMaterial = blitMat;
            q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.GetComponent<Renderer>().receiveShadows = false;
        }

        void OnDestroy() { Teardown(); Scale = 1f; }
    }
}
