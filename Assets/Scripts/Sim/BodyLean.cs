using UnityEngine;

namespace FrcSim
{
    // 車身慣性的視覺效果:前後加速度越大,外觀前後傾斜越多(加速車頭上揚、煞車點頭),最多約 4°,並帶一點阻尼。
    // 只影響外觀(掛在 ModelPivot 上),不影響物理與碰撞。
    public class BodyLean : MonoBehaviour
    {
        public static float PeakAbs, PeakSigned;   // 測試用:本次執行出現過的最大傾斜(度)
        Vector2 prevVel; bool has;
        float lean, leanVel;

        void LateUpdate()
        {
            var d = GameSession.Drive;
            if (d == null || Time.deltaTime <= 1e-5f) return;
            Vector2 v = d.Velocity;
            if (!has) { prevVel = v; has = true; return; }
            Vector2 a = (v - prevVel) / Time.deltaTime; prevVel = v;
            float h = d.HeadingRad;
            float aFwd = a.x * Mathf.Cos(h) + a.y * Mathf.Sin(h);   // 沿車頭方向的加速度(m/s²)
            float target = Mathf.Clamp(aFwd * 0.30f, -4f, 4f);
            // 彈簧阻尼,讓傾斜有點回彈、不會抖
            float k = 120f, c = 16f;
            leanVel += ((target - lean) * k - leanVel * c) * Time.deltaTime;
            lean += leanVel * Time.deltaTime;
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(lean, -5f, 5f));
            if (Mathf.Abs(lean) > PeakAbs) { PeakAbs = Mathf.Abs(lean); PeakSigned = lean; }
        }
    }
}
