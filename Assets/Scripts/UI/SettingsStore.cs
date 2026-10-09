using UnityEngine;

namespace FrcSim
{
    public static class SettingsStore
    {
        public static readonly int[] FpsOptions = { 60, 90, 120, 144, 150, 240, 0 };   // 0 = 不限

        public static int FpsIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("fpsIdx", 0), 0, FpsOptions.Length - 1);
            set { PlayerPrefs.SetInt("fpsIdx", value); PlayerPrefs.Save(); }
        }
        public static readonly float[] RenderScales = { 1.0f, 1.5f, 2.0f };   // 超取樣比例(畫質)
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

        public static void Apply()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            // 畫質:4x 抗鋸齒、高解析柔和陰影、各向異性過濾
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 45f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.pixelLightCount = 4;
            int f = FpsOptions[FpsIndex];
            Application.targetFrameRate = f == 0 ? -1 : f;
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }
}
