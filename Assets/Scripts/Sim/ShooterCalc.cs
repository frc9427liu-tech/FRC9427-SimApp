using UnityEngine;

namespace FrcSim
{
    // 移植自機器人 ShooterCalculator.java(查表+邊走邊射 5 次迭代),規格見記憶 code-spec-shooter-calculator。
    public static class ShooterCalc
    {
        public struct Result { public float FieldAngle, HoodDeg, FlywheelRps, Distance; }

        const float PhaseDelay = 0.03f;
        const float DragTau = 0.375f;
        public static readonly Vector2 TurretOffset = new Vector2(0.151136f, 0f);

        static readonly float[] rollK = { 2.017127f, 2.46169f, 3.102784f };
        static readonly float[] rollV = { 36f, 40.5f, 45f };               // 3.102784 後者覆蓋成 45
        static readonly float[] hoodK = { 2.017127f, 2.46169f, 3.102784f, 4.071786f };
        static readonly float[] hoodV = { 3f, 8f, 10f, 20f };
        static readonly float[] tofK = { 1.290171f, 2.058596f, 2.547153f, 3.253292f, 3.710604f, 4.076018f };
        static readonly float[] tofV = { 0.84f, 0.82f, 0.93f, 0.93f, 0.97f, 1.0f };

        // InterpolatingTreeMap:線性內插,超出範圍夾到首/尾
        static float Interp(float[] k, float[] v, float x)
        {
            if (x <= k[0]) return v[0];
            if (x >= k[k.Length - 1]) return v[v.Length - 1];
            for (int i = 0; i < k.Length - 1; i++)
                if (x <= k[i + 1])
                {
                    float t = (x - k[i]) / (k[i + 1] - k[i]);
                    return Mathf.Lerp(v[i], v[i + 1], t);
                }
            return v[v.Length - 1];
        }

        // pos: 機器人位置(場地座標), heading: 弧度, vel: 場地速度, omega: rad/s, target: 目標點
        public static Result Solve(Vector2 pos, float heading, Vector2 vel, float omega, Vector2 target)
        {
            // 1. 延遲補償(簡化成一階外推)
            Vector2 p = pos + vel * PhaseDelay;
            float h = heading + omega * PhaseDelay;
            // 2. 砲塔位置
            float c = Mathf.Cos(h), s = Mathf.Sin(h);
            Vector2 off = new Vector2(TurretOffset.x * c - TurretOffset.y * s, TurretOffset.x * s + TurretOffset.y * c);
            Vector2 turretPos = p + off;
            // 5. 砲塔速度 = 底盤速度 + ω × offset
            Vector2 tv = new Vector2(vel.x - omega * off.y, vel.y + omega * off.x);
            // 6. 迭代 5 次
            float dist = (target - turretPos).magnitude;
            Vector2 look = turretPos;
            for (int i = 0; i < 5; i++)
            {
                float tof = Interp(tofK, tofV, dist);
                float eff = (1f - Mathf.Exp(-tof * DragTau)) / DragTau;
                look = turretPos + tv * eff;
                dist = (target - look).magnitude;
            }
            Vector2 d = target - look;
            return new Result
            {
                FieldAngle = Mathf.Atan2(d.y, d.x),
                HoodDeg = Interp(hoodK, hoodV, dist),
                FlywheelRps = Interp(rollK, rollV, dist),
                Distance = dist
            };
        }
    }
}
