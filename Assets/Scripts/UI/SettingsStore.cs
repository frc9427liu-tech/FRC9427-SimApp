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

        public static void Apply()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            // 畫質:4x 抗鋸齒、高解析柔和陰影、各向異性過濾
            QualitySettings.antiAliasing = 4;
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
