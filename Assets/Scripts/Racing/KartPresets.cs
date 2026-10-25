using UnityEngine;

namespace FrcSim
{
    /// 各車款 + 排氣量(cc)的 KartStats 預設(模組 K,01 §2)。Brick = 150cc 中量級基準值,與 KartStats 欄位預設相同。
    /// 排氣量:50 / 100 / 150 / 200cc 的極速 = 0.55 / 0.75 / 1.0 / 1.3 倍(KartMath.CcSpeedMul);200cc 加速與轉向略高但抓地較低(更滑)。
    public static class KartPresets
    {
        public const float BaseTopSpeed = 24f;   // 150cc 基準極速 m/s

        /// 極速隨 cc:24 m/s x KartMath.CcSpeedMul(cc)。50 / 100 / 150 / 200cc = 13.2 / 18 / 24 / 31.2 m/s。
        public static float TopSpeedForCc(int cc)
        {
            return BaseTopSpeed * KartMath.CcSpeedMul(cc);
        }

        /// 把「150cc 基準」的 KartStats 就地換成指定排氣量(只能對 150cc 的數值呼叫一次,不是冪等)。
        /// 極速 x m;加速度 x m^0.6;轉向率 x m^0.25;煞車 / 滑行阻力 x m^0.5;200cc 抓地與漂移抓地最多降 10%(更滑)。
        public static void ApplyCC(KartStats st, int cc)
        {
            if (st == null) return;
            float m = KartMath.CcSpeedMul(cc);
            if (Mathf.Approximately(m, 1f)) return;
            float accelK = Mathf.Pow(m, 0.6f), turnK = Mathf.Pow(m, 0.25f), brakeK = Mathf.Sqrt(m);
            st.TopSpeed *= m;
            st.Accel *= accelK;
            st.TurnRateLow *= turnK; st.TurnRateHigh *= turnK;
            st.BrakeDecel *= brakeK; st.CoastDrag *= brakeK;
            if (m > 1f)
            {
                float slide = 1f - 0.1f * Mathf.Clamp01((m - 1f) / 0.3f);
                st.Grip *= slide; st.DriftGrip *= slide;
            }
        }

        /// 舊簽名:車款 + 排氣量(行為與之前相同,150cc 的數值完全沒變)。
        public static KartStats For(KartStyle s, int cc = 150)
        {
            var k = new KartStats();
            float top = BaseTopSpeed;
            switch (s)
            {
                case KartStyle.Bullet:    // 輕、加速快、轉得靈、極速略低
                    k.Mass = 125f; k.TopSpeed = top * 0.98f; k.Accel = 18.5f; k.AccelTau = 0.9f;
                    k.TurnRateLow = 130f; k.TurnRateHigh = 80f; k.Grip = 13f; k.BrakeDecel = 30f;
                    break;
                case KartStyle.Chonk:     // 重、極速高、加速慢、轉得鈍
                    k.Mass = 200f; k.TopSpeed = top * 1.06f; k.Accel = 13.5f; k.AccelTau = 1.15f;
                    k.TurnRateLow = 105f; k.TurnRateHigh = 65f; k.Grip = 11f; k.BrakeDecel = 26f;
                    break;
                case KartStyle.Robot9427: // 均衡偏靈活
                    k.Mass = 160f; k.TopSpeed = top * 1.02f; k.Accel = 16.5f; k.AccelTau = 1.0f;
                    k.TurnRateLow = 125f; k.TurnRateHigh = 78f; k.Grip = 12.5f;
                    break;
                default:                  // Brick:01 的預設中量級
                    k.TopSpeed = top;
                    break;
            }
            ApplyCC(k, cc);
            return k;
        }

        /// 四件套組合(角色 / 車身 / 輪胎 / 滑翔翼)+ 排氣量 -> KartStats。細節與六項屬性條見 KartLoadout.Combine。
        public static KartStats For(int driverId, int bodyId, int tireId, int gliderId, int cc = 150)
        {
            return KartLoadout.Combine(driverId, bodyId, tireId, gliderId, cc).Stats;
        }

        /// 直接吃 RaceConfig(DriverId / BodyId / TireId / GliderId / CC)。
        public static KartStats For(RaceConfig cfg)
        {
            if (cfg == null) return For(KartStyle.Brick, 150);
            return KartLoadout.Combine(cfg.DriverId, cfg.BodyId, cfg.TireId, cfg.GliderId, cfg.CC).Stats;
        }
    }
}
