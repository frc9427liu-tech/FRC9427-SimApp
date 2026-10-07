using UnityEngine;

namespace FrcSim
{
    // Intake(齒條手臂+滾輪)、Hopper(球數)、Shooter(飛輪/Hood/砲塔自動瞄準 HUB)。
    // 數值依 code-spec-intake-hopper / code-spec-shooter:
    //  手臂 0..0.25 m(約 0.4 s),滾輪 40 RPS;飛輪加速 100 RPS/s,到位誤差 3 RPS;
    //  Hood 1..45°、1080°/s、到位 2°;發射仰角 = 80° - hood,球速 = 0.5 × 飛輪表面速度(2in 半徑)。
    public class RobotMechanisms : MonoBehaviour
    {
        public SwerveDrive Drive;
        public Collider RobotCollider;
        public Transform TurretVisual;
        public Transform ArmVisual;

        public bool IntakeDown;
        public bool Shooting;
        public int Held = 8;                // 預載 8 顆
        public const int Capacity = 40;
        public float ArmExt;                // m
        public float FlywheelRps;
        public float HoodDeg = 1f;
        public float TurretRad;             // 相對機器人,逆時針為正
        public bool Ready;
        public int ShotsFired;
        public float TargetDistance;

        public const float ArmMax = 0.25f;
        const float ArmSpeed = 0.63f;
        const float FlywheelAccel = 100f;
        const float FlywheelCoast = 25f;
        const float HoodSpeed = 1080f;
        const float TurretSpeed = 10f;
        const float ShootInterval = 1f / 8f;
        const float LaunchHeight = 0.487484f;
        const float SpeedPerRps = 0.5f * 2f * Mathf.PI * 0.0508f;   // 0.1596 m/s per RPS(內建行為;selftest 2~4m 全進)
        const float SpeedPerRpsReal = 0.21f;   // 真實程式模式:用真實查表+視覺位姿實測校正(用 0.16 會落在 HUB 前 1m,原因待查:Hood 震盪/飛輪降速)

        float nextFire;
        // 瞄準點取機器人 code 的 topCenterPoint (x=4.42586);實際 HUB 開口中心在 4.625(場地手冊),
        // 查表是用機器人 code 的距離定義調出來的,所以瞄準照 code、球照物理。
        public Vector2 TargetHub = new Vector2(4.42586f, SimConstants.FieldWidth / 2f);   // 這台要瞄準的 HUB(紅方機器人會改成紅方 HUB)
        public static Vector2 BlueHub => new Vector2(4.42586f, SimConstants.FieldWidth / 2f);

        static float Wrap(float a) { while (a > Mathf.PI) a -= 2f * Mathf.PI; while (a < -Mathf.PI) a += 2f * Mathf.PI; return a; }

        public MechSim Sim;              // 有的話:機構狀態全部來自真實機器人程式的馬達輸出
        const double ArmMPerRev = 0.019949;

        void RealStep()
        {
            // Intake 手臂(齒條,馬達 id 30)、滾輪(41)、飛輪(9)、Hood(15)、扳機(13)、輸送帶(31)
            float rollerRps = Mathf.Abs((float)Sim.Vel("Talon FX (v6)[41]"));
            // 手臂位置馬達的硬限位在原始座標不好還原(反轉馬達+軟限位),所以用「滾輪在轉」當作 intake 放下(程式的 intakerun = 手臂放下 + 滾輪 40 RPS)
            IntakeDown = rollerRps > 8f;
            ArmExt = Mathf.MoveTowards(ArmExt, IntakeDown ? ArmMax : 0f, ArmSpeed * Time.fixedDeltaTime);
            FlywheelRps = (float)Sim.Vel("Talon FX (v6)[9]");
            HoodDeg = Mathf.Clamp(1f + (float)(Sim.Pos("Talon FX (v6)[15]") / 14.75 * 360.0), 1f, 45f);
            float trigRps = Mathf.Abs((float)Sim.Vel("Talon FX (v6)[13]"));
            if (ArmVisual != null)
            {
                ArmVisual.localPosition = new Vector3(SimConstants.BumperLength / 2f + ArmExt * 0.5f - 0.02f, 0.02f, 0f);
                ArmVisual.localScale = new Vector3(0.05f + ArmExt, 0.10f, 0.62f);
            }
            if (ArmExt >= 0.2f && rollerRps > 10f && Held < Capacity) Collect();

            Shooting = trigRps > 15f && FlywheelRps > 15f;
            Ready = Shooting;
            if (Shooting && Held > 0 && Time.time >= nextFire)
            {
                TurretRad = 0f;      // 這台機器人沒有砲塔馬達:靠底盤轉向瞄準(程式的 AutoAlign)
                Fire(Drive.Pose2d, Drive.HeadingRad);
                nextFire = Time.time + ShootInterval;
            }
        }

        void FixedUpdate()
        {
            if (Sim != null && Sim.Hal != null && Sim.Hal.Connected) { RealStep(); return; }
            float dt = Time.fixedDeltaTime;

            // 手臂
            float armTarget = IntakeDown ? ArmMax : 0f;
            ArmExt = Mathf.MoveTowards(ArmExt, armTarget, ArmSpeed * dt);
            if (ArmVisual != null)
                ArmVisual.localPosition = new Vector3(SimConstants.BumperLength / 2f + ArmExt * 0.5f - 0.02f, 0.02f, 0f);
            if (ArmVisual != null)
                ArmVisual.localScale = new Vector3(0.05f + ArmExt, 0.10f, 0.62f);

            // 吸球:手臂放下(≥0.2m)且滾輪轉,前方範圍內的球被吸入
            if (IntakeDown && ArmExt >= 0.2f && Held < Capacity) Collect();

            // 瞄準
            Vector2 pos = Drive.Pose2d;
            float heading = Drive.HeadingRad;
            var res = ShooterCalc.Solve(pos, heading, Drive.Velocity, Drive.Omega, TargetHub);
            TargetDistance = res.Distance;
            float desiredTurret = Wrap(res.FieldAngle - heading);

            float flyTarget = Shooting ? res.FlywheelRps : 0f;
            if (FlywheelRps < flyTarget) FlywheelRps = Mathf.Min(flyTarget, FlywheelRps + FlywheelAccel * dt);
            else FlywheelRps = Mathf.Max(flyTarget, FlywheelRps - (Shooting ? FlywheelAccel : FlywheelCoast) * dt);

            float hoodTarget = Shooting ? Mathf.Clamp(res.HoodDeg, 1f, 45f) : 1f;
            HoodDeg = Mathf.MoveTowards(HoodDeg, hoodTarget, HoodSpeed * dt);

            if (Shooting)
            {
                float err = Wrap(desiredTurret - TurretRad);
                TurretRad = Wrap(TurretRad + Mathf.Clamp(err, -TurretSpeed * dt, TurretSpeed * dt));
            }
            if (TurretVisual != null) TurretVisual.localRotation = Quaternion.Euler(0f, -TurretRad * Mathf.Rad2Deg, 0f);

            Ready = Shooting
                && Mathf.Abs(FlywheelRps - res.FlywheelRps) <= 3f
                && Mathf.Abs(HoodDeg - hoodTarget) < 2f
                && Mathf.Abs(Wrap(desiredTurret - TurretRad)) < 0.05f;

            if (Shooting && Ready && Held > 0 && Time.time >= nextFire)
            {
                Fire(pos, heading);
                nextFire = Time.time + ShootInterval;
            }
        }

        void Collect()
        {
            Vector3 center = transform.TransformPoint(new Vector3(SimConstants.BumperLength / 2f + 0.22f, 0.05f, 0f));
            var hits = Physics.OverlapBox(center, new Vector3(0.28f, 0.14f, 0.34f), transform.rotation);
            foreach (var h in hits)
            {
                if (Held >= Capacity) break;
                var f = h.GetComponent<Fuel>();
                if (f == null) continue;
                Held++;
                FuelManager.Remove(f);
            }
        }

        void Fire(Vector2 pos, float heading)
        {
            float yaw = heading + TurretRad;
            // 出球模型:用機器人查表(飛輪轉速/Hood 角/飛行時間)擬合到「落在 HUB 開口」,球有空氣阻力 0.375/s(與程式的 linearDragTimeConstant 一致)
            // 擬合結果(2~4m 誤差 ≤0.26m,HUB 半寬 0.5m):速度 = 0.14*rps + 2.0 m/s,仰角 = 71° - 0.75*hood
            float elev = (71f - 0.75f * HoodDeg) * Mathf.Deg2Rad;
            float speed = 0.14f * FlywheelRps + 2.0f;

            float c = Mathf.Cos(heading), s = Mathf.Sin(heading);
            Vector2 off = new Vector2(ShooterCalc.TurretOffset.x * c, ShooterCalc.TurretOffset.x * s);
            Vector2 tv = new Vector2(Drive.Velocity.x - Drive.Omega * off.y, Drive.Velocity.y + Drive.Omega * off.x);

            Vector3 v = new Vector3(
                Mathf.Cos(yaw) * Mathf.Cos(elev) * speed + tv.x,
                Mathf.Sin(elev) * speed,
                Mathf.Sin(yaw) * Mathf.Cos(elev) * speed + tv.y);
            Vector3 p = new Vector3(pos.x + off.x, LaunchHeight, pos.y + off.y);
            FuelManager.Spawn(p, v, RobotCollider);
            Held--;
            ShotsFired++;
        }
    }
}
