using UnityEngine;

namespace FrcSim
{
    public static class SettingsStore
    {
        public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144 };   // 不再提供 240/不限(會讓顯卡、CPU 滿載燒機)

        public static int FpsIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("fps2", 1), 0, FpsOptions.Length - 1);
            set { PlayerPrefs.SetInt("fps2", value); PlayerPrefs.Save(); }
        }
        public static readonly float[] RenderScales = { 1.0f, 1.25f, 1.5f, 2.0f };   // 超取樣比例(畫質)
        public static int RenderScaleIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("rscale", 1), 0, RenderScales.Length - 1);
            set { PlayerPrefs.SetInt("rscale", value); PlayerPrefs.Save(); }
        }
        public static float RenderScale => RenderScales[RenderScaleIndex];

        // 開始前選的「最高車速」(內建模式直接限速;LEO 真實程式模式換算成底盤齒比)
        public static readonly float[] SpeedOptions = { 2.5f, 3.5f, 4.0f, 5.0f };
        public static int SpeedIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("speedIdx", 2), 0, SpeedOptions.Length - 1);
            set { PlayerPrefs.SetInt("speedIdx", value); PlayerPrefs.Save(); }
        }
        public static float MaxSpeedChoice => SpeedOptions[SpeedIndex];

        // 加速度(慣性):數字越小,起步/煞車越「肉」,看得出底盤慣性
        public static readonly float[] AccelOptions = { 5f, 8f, 12f, 22f };
        public static int AccelIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("accel2", 2), 0, AccelOptions.Length - 1);
            set { PlayerPrefs.SetInt("accel2", value); PlayerPrefs.Save(); }
        }
        public static float AccelChoice => AccelOptions[AccelIndex];

        // 陰影開關(關掉最省效能)
        public static bool Shadows
        {
            get => PlayerPrefs.GetInt("shadows", 1) == 1;
            set { PlayerPrefs.SetInt("shadows", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt("vsync", 0) == 1;
            set { PlayerPrefs.SetInt("vsync", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt("full", 0) == 1;
            set { PlayerPrefs.SetInt("full", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // 目前該用的幀率上限:選單/暫停時固定 30(待機不燒機),遊戲中用設定值
        public static int EffectiveFps => MenuSystem.Blocking ? 30 : FpsOptions[FpsIndex];

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
            if (weak) { PlayerPrefs.SetInt("rscale", 0); PlayerPrefs.SetInt("shadows", 0); PlayerPrefs.SetInt("msaa", 2); PlayerPrefs.Save(); }
        }
        public static int Msaa => PlayerPrefs.GetInt("msaa", 4);

        public static void Apply()
        {
            FirstRunDefaults();
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            // 畫質:4x 抗鋸齒、高解析柔和陰影、各向異性過濾
            QualitySettings.antiAliasing = Msaa;
            QualitySettings.shadows = Shadows ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 35f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.pixelLightCount = 4;
            int f = FpsOptions[FpsIndex];
            Application.targetFrameRate = f == 0 ? -1 : f;
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }
}
