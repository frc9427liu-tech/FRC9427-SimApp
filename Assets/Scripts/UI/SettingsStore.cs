using UnityEngine;

namespace FrcSim
{
    public static class SettingsStore
    {
        // 幀率改用「垂直同步的整數分頻」(144Hz 螢幕:144/72/48/36),不再用 targetFrameRate 睡眠限速——後者與螢幕刷新不同步,會每幀快慢不一(看起來卡)
        public static int RefreshHz { get { int h = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value); return h < 30 ? 60 : h; } }
        public static int FpsIndex   // 0..3 = vsync 間隔 1..4(新鍵 pace,預設 1 = 刷新率的一半)
        {
            get => Mathf.Clamp(Prefs.GetInt("pace", 1), 0, 3);
            set { Prefs.SetInt("pace", value); PlayerPrefs.Save(); }
        }
        public static int VSyncInterval(bool menu) { int k = FpsIndex + 1; if (menu && RefreshHz >= 120) k = Mathf.Max(k, 2); return k; }
        public static int EffectiveFps => RefreshHz / VSyncInterval(MenuSystem.Blocking);
        public static string PaceLabel(int i) => (RefreshHz / (i + 1)) + " Hz";
        public static void ApplyPacing()
        {
            int k = VSyncInterval(MenuSystem.Blocking);
            if (QualitySettings.vSyncCount != k) QualitySettings.vSyncCount = k;
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
        }        public static readonly float[] RenderScales = { 1.0f, 1.25f, 1.5f, 2.0f };   // 超取樣比例(畫質)
        public static int RenderScaleIndex
        {
            get => Mathf.Clamp(Prefs.GetInt("rscale", 1), 0, RenderScales.Length - 1);
            set { Prefs.SetInt("rscale", value); PlayerPrefs.Save(); }
        }
        public static float RenderScale => RenderScales[RenderScaleIndex];

        // 開始前選的「最高車速」(內建模式直接限速;LEO 真實程式模式換算成底盤齒比)
        public static readonly float[] SpeedOptions = { 2.5f, 3.5f, 4.0f, 5.0f };
        public static int SpeedIndex
        {
            get => Mathf.Clamp(Prefs.GetInt("speedIdx", 2), 0, SpeedOptions.Length - 1);
            set { Prefs.SetInt("speedIdx", value); PlayerPrefs.Save(); }
        }
        public static float MaxSpeedChoice => SpeedOptions[SpeedIndex];

        // 加速度(慣性):數字越小,起步/煞車越「肉」,看得出底盤慣性
        public static readonly float[] AccelOptions = { 5f, 8f, 12f, 22f };
        public static int AccelIndex
        {
            get => Mathf.Clamp(Prefs.GetInt("accel2", 2), 0, AccelOptions.Length - 1);
            set { Prefs.SetInt("accel2", value); PlayerPrefs.Save(); }
        }
        public static float AccelChoice => AccelOptions[AccelIndex];

        // 陰影開關(關掉最省效能)
        public static bool Shadows
        {
            get => Prefs.GetInt("shadows", 1) == 1;
            set { Prefs.SetInt("shadows", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool VSync
        {
            get => Prefs.GetInt("vsync", 0) == 1;
            set { Prefs.SetInt("vsync", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static bool Fullscreen
        {
            get => Prefs.GetInt("full", 0) == 1;
            set { Prefs.SetInt("full", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // 目前該用的幀率上限:選單/暫停時固定 30(待機不燒機),遊戲中用設定值

        static bool firstRunChecked;
        // 第一次啟動(沒有存過任何畫質設定)時依顯卡自動選預設:內顯/小顯存 → 低(超取樣 100%、關陰影、MSAA 2x);其餘維持預設(125%、陰影開、4x)
        static void FirstRunDefaults()
        {
            if (firstRunChecked) return; firstRunChecked = true;
            if (FrcSim.SuperSample.IsTestRun()) return;   // 自動測試/截圖不要寫入使用者的畫質設定
            if (PlayerPrefs.HasKey("rscale") || PlayerPrefs.HasKey("shadows") || PlayerPrefs.HasKey("msaa")) return;
            string gpu = SystemInfo.graphicsDeviceName ?? "";
            bool weak = gpu.IndexOf("Intel", System.StringComparison.OrdinalIgnoreCase) >= 0 || gpu.IndexOf("UHD", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || gpu.IndexOf("Iris", System.StringComparison.OrdinalIgnoreCase) >= 0 || (SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 2000);
            Debug.Log($"[Settings] first run, GPU=\"{gpu}\" VRAM={SystemInfo.graphicsMemorySize}MB → {(weak ? "low" : "default")} quality preset");
            if (weak) { Prefs.SetInt("rscale", 0); Prefs.SetInt("shadows", 0); Prefs.SetInt("msaa", 2); PlayerPrefs.Save(); }
        }
        public static int Msaa => Prefs.GetInt("msaa", 4);

        public static void Apply()
        {
            FirstRunDefaults();
            ApplyPacing();
            // 畫質:4x 抗鋸齒、高解析柔和陰影、各向異性過濾
            QualitySettings.antiAliasing = Msaa;
            Look.ApplyShadowQuality(Shadows);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.pixelLightCount = 4;
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }
}
