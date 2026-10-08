using UnityEngine;

namespace FrcSim
{
    // 全向(swerve)底盤:輸入是 (vx, vy, omega) 的期望底盤速度,
    // 實際速度受加速度上限限制(對應機器人的 SwerveSetpointGenerator 概念),
    // 碰撞交給 Unity 剛體。
    [RequireComponent(typeof(Rigidbody))]
    public class SwerveDrive : MonoBehaviour
    {
        public bool FieldCentric = true;
        public bool SimDriven;                 // true:速度由真實機器人程式經馬達物理算出(MechSim)
        public float TractionAccel = 14f;      // m/s²:輪胎抓地力(μ≈1.4~1.5 × g)上限,真實程式模式用
        public float TractionAlpha = 45f;      // rad/s²:旋轉抓地力上限
        public Vector2 SimVelField; public float SimOmega;
        public Vector2 StartPos = new Vector2(2.0f, SimConstants.FieldWidth / 2f);
        public float StartHeadingDeg = 0f;

        Rigidbody rb;
        Vector2 vel;            // 場地座標 (x,y) 速度 m/s
        float omega;            // rad/s,逆時針為正
        Vector2 cmdRobot;       // 期望速度(場地座標系)
        float cmdOmega;

        public int YFixes;
        public float BumpPitchDeg;   // 目前車身因 BUMP 斜坡的俯仰角(度,供測試/除錯)
        public string RbInfo => rb == null ? "no rb" : $"yfixes={YFixes} " + $"rbVel=({rb.linearVelocity.x:0.00},{rb.linearVelocity.z:0.00}) kin={rb.isKinematic} sleep={rb.IsSleeping()} pos=({rb.position.x:0.00},{rb.position.y:0.00},{rb.position.z:0.00})";
        public string RbInfoOld => rb == null ? "no rb" : $"rbVel=({rb.linearVelocity.x:0.00},{rb.linearVelocity.z:0.00}) kin={rb.isKinematic} sleep={rb.IsSleeping()} cons={rb.constraints} pos=({rb.position.x:0.00},{rb.position.y:0.00},{rb.position.z:0.00})";
        public Vector2 Pose2d => new Vector2(transform.position.x, transform.position.z);
        public float HeadingRad => -transform.eulerAngles.y * Mathf.Deg2Rad;
        public float Speed => vel.magnitude;
        public Vector2 Velocity => vel;
        public float Omega => omega;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.mass = SimConstants.RobotMass;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;   // 不凍結 Y:凍結會把車釘在建立時的高度(0)
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            ResetPose();
        }

        public void ResetPose()
        {
            rb.position = new Vector3(StartPos.x, SimConstants.BumperHeight / 2f + 0.03f, StartPos.y);
            rb.rotation = Quaternion.Euler(0, -StartHeadingDeg, 0);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            vel = Vector2.zero;
            omega = 0f;
        }

        public void SetPose(Vector2 p, float headingDeg)
        {
            StartPos = p;
            StartHeadingDeg = headingDeg;
            ResetPose();
        }

        // fwd/strafe/rot 皆為 -1..1:fwd 往前、strafe 往左為正、rot 逆時針為正
        public void Drive(float fwd, float strafe, float rot)
        {
            Vector2 v = new Vector2(fwd, strafe);
            if (v.magnitude > 1f) v.Normalize();
            v *= SimConstants.MaxSpeed;
            if (!FieldCentric)
            {
                float h = HeadingRad, c = Mathf.Cos(h), s = Mathf.Sin(h);
                v = new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
            }
            cmdRobot = v;
            cmdOmega = Mathf.Clamp(rot, -1f, 1f) * SimConstants.MaxAngularSpeed;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // 車身高度固定:被擠進地板會產生巨大摩擦把車煞住,所以每步檢查並拉回
            float gy = SimConstants.BumperHeight / 2f + 0.03f + FieldBuilder.BumpHeightAt(rb.position.x, rb.position.z);   // 過 BUMP 時車身隨 15° 斜坡抬升
            if (Mathf.Abs(rb.position.y - gy) > 0.005f) { YFixes++; rb.position = new Vector3(rb.position.x, gy, rb.position.z); }
            // 過 BUMP 時車身沿斜坡方向(場地 x)傾斜;只動世界 Z 軸俯仰,不影響偏航(eulerAngles.y 仍是車頭方向)
            {
                const float e = 0.05f;
                float dhdx = (FieldBuilder.BumpHeightAt(rb.position.x + e, rb.position.z) - FieldBuilder.BumpHeightAt(rb.position.x - e, rb.position.z)) / (2f * e);
                float pitch = Mathf.Atan(dhdx) * Mathf.Rad2Deg;
                BumpPitchDeg = pitch;
                rb.rotation = Quaternion.AngleAxis(pitch, Vector3.forward) * Quaternion.Euler(0f, rb.rotation.eulerAngles.y, 0f);
            }
            if (SimDriven)
            {
                // 輪胎抓地力上限:從「實際剛體速度」朝馬達算出的目標速度靠近,每步最多改 μ·g 的加速度
                // (不會瞬間達速;被撞擋住後保留碰撞結果;與馬達端 Kraken 力矩上限一起決定加速曲線)
                Vector3 lvs = rb.linearVelocity;
                Vector2 cur = new Vector2(lvs.x, lvs.z);
                Vector2 dvs = SimVelField - cur;
                float maxDvs = TractionAccel * dt;
                if (dvs.magnitude > maxDvs) dvs = dvs.normalized * maxDvs;
                cur += dvs;
                float curW = -rb.angularVelocity.y;
                curW += Mathf.Clamp(SimOmega - curW, -TractionAlpha * dt, TractionAlpha * dt);
                rb.linearVelocity = new Vector3(cur.x, 0f, cur.y);
                rb.angularVelocity = new Vector3(0f, -curW, 0f);
                vel = cur; omega = curW;
                return;
            }
            // 取實際剛體速度,碰撞後才會真的被擋住
            Vector3 lv = rb.linearVelocity;
            vel = new Vector2(lv.x, lv.z);

            Vector2 dv = cmdRobot - vel;
            float maxDv = SimConstants.MaxAccel * dt;
            if (dv.magnitude > maxDv) dv = dv.normalized * maxDv;
            vel += dv;
            rb.linearVelocity = new Vector3(vel.x, 0f, vel.y);

            float dw = cmdOmega - omega;
            float maxDw = SimConstants.MaxAngularAccel * dt;
            omega += Mathf.Clamp(dw, -maxDw, maxDw);
            rb.angularVelocity = new Vector3(0f, -omega, 0f);
        }
    }
}
