using UnityEngine;

namespace FrcSim
{
    /// 橡皮筋(模組 I,03 §2.6)。純函式,只算 AI 的「極速倍率」,絕不動位置。
    ///   deltaS = AI 的總進度 − 玩家(或參考車)的總進度,公尺;正 = AI 在前面。
    ///   |Δs| ≤ 15 m 死區不調;15~80 m 線性內插;> 80 m 滿額。
    ///   AI 在前:× (1 − 0.10 × 強度);AI 在後:× (1 + 0.12 × 強度)。
    ///   最後一圈最後 25% 關閉(回傳 1);玩家看得見的 AI(視野 60 m 內)上限 × 1.06。
    public static class Rubberband
    {
        /// 各難度強度(0 簡單 … 3 超困難)。
        public static readonly float[] Strength = { 1.0f, 0.7f, 0.4f, 0.15f };
        public const float DeadZone = 15f, FullDist = 80f, SlowMax = 0.10f, BoostMax = 0.12f, VisibleCap = 1.06f;
        public const float VisibleRange = 60f;

        public static float Mul(float deltaS, int level, bool lastLapLast25, bool visible)
        {
            if (lastLapLast25 || float.IsNaN(deltaS)) return 1f;
            float str = Strength[Mathf.Clamp(level, 0, Strength.Length - 1)];
            float t = Mathf.Clamp01((Mathf.Abs(deltaS) - DeadZone) / (FullDist - DeadZone));
            float m = deltaS > 0f ? 1f - SlowMax * str * t : 1f + BoostMax * str * t;
            if (visible && m > VisibleCap) m = VisibleCap;
            return m;
        }
    }
}
