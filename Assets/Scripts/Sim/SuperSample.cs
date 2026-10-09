using UnityEngine;

namespace FrcSim
{
    // 超取樣(提高畫質):主相機先畫到「螢幕解析度 × 比例」的貼圖(含 MSAA),再縮放貼到螢幕,邊緣和細節明顯更乾淨。
    // 比例在「設定」裡調(100% / 150% / 200%);1080p 螢幕 150% = 內部 2880×1620。
    public class SuperSample : MonoBehaviour
    {
        public static float Scale = 1f;   // 目前實際使用的比例(給 HUD 換算螢幕座標用)
        Camera cam;
        RenderTexture rt;
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
            if (avg < 28f && SettingsStore.RenderScaleIndex > 0)
            {
                SettingsStore.RenderScaleIndex = SettingsStore.RenderScaleIndex - 1;
                Debug.Log($"[SuperSample] avg FPS {avg:0} < 28 → render scale lowered to {SettingsStore.RenderScale}");
                warm = Time.unscaledTime + 8f;
            }
        }

        void LateUpdate()
        {
            if (!MenuSystem.Blocking) AutoQuality();
            float s = SettingsStore.RenderScale;
            if (rt == null && s <= 1.01f && curScale == s && w == Screen.width && h == Screen.height) return;
            if (curScale == s && w == Screen.width && h == Screen.height) return;
            Rebuild(s);
        }

        void Rebuild(float s)
        {
            if (rt != null) { cam.targetTexture = null; rt.Release(); Destroy(rt); rt = null; }
            w = Screen.width; h = Screen.height; curScale = s;
            if (s <= 1.01f || w < 16 || h < 16) { Scale = 1f; return; }
            rt = new RenderTexture(Mathf.RoundToInt(w * s), Mathf.RoundToInt(h * s), 24, RenderTextureFormat.ARGB32)
            { antiAliasing = 4, filterMode = FilterMode.Bilinear, useMipMap = false, name = "SuperSampleRT" };
            rt.Create();
            cam.targetTexture = rt;
            Scale = s;
        }

        void OnGUI()
        {
            if (rt == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = 1000;   // 最先畫(在 HUD 後面)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rt, ScaleMode.StretchToFill, false);
        }

        void OnDestroy() { if (rt != null) { cam.targetTexture = null; rt.Release(); } Scale = 1f; }
    }
}
