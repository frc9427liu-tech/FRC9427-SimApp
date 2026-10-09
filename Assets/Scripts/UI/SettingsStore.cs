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
