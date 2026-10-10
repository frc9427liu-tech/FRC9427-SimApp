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
        public float FireInterval;       // >0:AI 難度用的發射間隔(秒);玩家 0 = 預設 1/8 秒
        public float CollectPerSec;      // >0:AI 難度用的吸球速率上限(顆/秒);玩家 0 = 不限
        float nextCollect;
        public float RollerRps;          // 滾輪轉速(真實程式模式,測試/除錯用)
        public int TotalCollected;       // 本場吸進的球數(成績卡統計)
        public float SpreadDeg;          // 出球散布(度,AI 難度用;玩家 0)
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

        public float ShotElevDeg = -1f, ShotSpeedPerRps = 0.14f, ShotSpeedBase = 2.0f;   // 出球仰角(<=0 用 Hood 公式)與球速模型
        public MechSim Sim;              // 有的話:機構狀態全部來自真實機器人程式的馬達輸出
        const double ArmMPerRev = 0.019949;

        // 依 mech.json 的 mechanisms 區塊找馬達(LEO 這類沒有 Hood、手動瞄準的機器人)
        void RealStepCfg(Newtonsoft.Json.Linq.JObject cfg)
        {
            string Name(string k) => (string)cfg[k];
            float rollerRps = Mathf.Abs((float)Sim.Vel(Name("roller"))); RollerRps = rollerRps;
            double armDown = (double?)cfg["armDownRev"] ?? 2.0;
            IntakeDown = Sim.Pos(Name("arm")) > armDown;
            ArmExt = Mathf.MoveTowards(ArmExt, IntakeDown ? ArmMax : 0f, ArmSpeed * Time.fixedDeltaTime);
            FlywheelRps = Mathf.Abs((float)Sim.Vel(Name("flywheel")));
            HoodDeg = 15f;   // LEO 沒有 Hood 馬達:固定仰角
            // 出球參數可在 mech.json 的 "shot" 調(實車量測後填):elevDeg = 仰角、speedPerRps/speedBase = 球速(m/s)= 係數×飛輪 rps + 基準
            var shot = cfg["shot"] as Newtonsoft.Json.Linq.JObject;
            if (shot != null)
            {
                ShotElevDeg = (float?)shot["elevDeg"] ?? -1f;
                ShotSpeedPerRps = (float?)shot["speedPerRps"] ?? 0.14f;
                ShotSpeedBase = (float?)shot["speedBase"] ?? 2.0f;
            }
            float feedRps = Mathf.Abs((float)Sim.Vel(Name("feeder")));
            double ratio = (double?)cfg["turretRatio"] ?? 20.0;
            TurretRad = Wrap((float)(-Sim.Pos(Name("turret")) / ratio * 2.0 * System.Math.PI));   // 往右(正)= 順時針 = 場地角度減少
            if (TurretVisual != null) TurretVisual.localRotation = Quaternion.Euler(0f, -TurretRad * Mathf.Rad2Deg, 0f);
            if (ArmVisual != null)
            {
                ArmVisual.localPosition = new Vector3(SimConstants.BumperLength / 2f + ArmExt * 0.5f - 0.02f, 0.02f, 0f);
                ArmVisual.localScale = new Vector3(0.05f + ArmExt, 0.10f, 0.62f);
            }
            if (ArmExt >= 0.2f && rollerRps > 10f && Held < Capacity) Collect();
            Shooting = feedRps > 15f && FlywheelRps > 15f;
            Ready = Shooting;
            if (Shooting && Held > 0 && Time.time >= nextFire)
            {
                Fire(Drive.Pose2d, Drive.HeadingRad);
                nextFire = Time.time + (FireInterval > 0f ? FireInterval : ShootInterval);
            }
        }

        void RealStep()
        {
            if (Sim.MechCfg != null) { RealStepCfg(Sim.MechCfg); return; }
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
                nextFire = Time.time + (FireInterval > 0f ? FireInterval : ShootInterval);
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
                nextFire = Time.time + (FireInterval > 0f ? FireInterval : ShootInterval);
            }
        }

        void Collect()
        {
            if (CollectPerSec > 0f && Time.time < nextCollect) return;
            Vector3 center = transform.TransformPoint(new Vector3(SimConstants.BumperLength / 2f + 0.22f, 0.05f, 0f));
            var hits = Physics.OverlapBox(center, new Vector3(0.28f, 0.14f, 0.34f), transform.rotation);
            foreach (var h in hits)
            {
                if (Held >= Capacity) break;
                var f = h.GetComponent<Fuel>();
                if (f == null) continue;
                Held++; TotalCollected++;
                if (CollectPerSec > 0f) nextCollect = Time.time + 1f / CollectPerSec;
                SpawnAbsorb(f.transform.position);
                FuelManager.Remove(f);
                if (CollectPerSec > 0f) break;
            }
        }

        // 吸球動畫:原球移除後留一顆純視覺球,0.18 秒飛進車內再消失
        void SpawnAbsorb(Vector3 from)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.transform.position = from;
            g.transform.localScale = Vector3.one * (Fuel.Radius * 2f);
            g.GetComponent<Renderer>().sharedMaterial = FuelManager.BallMat;
            g.AddComponent<AbsorbAnim>().Init(transform, SlotPos(Mathf.Min(Held - 1, 23)));
        }

        // 車內球數顯示:Held 顆小球疊在車內(最多 24 顆),球數變多肉眼看得到
        static Vector3 SlotPos(int i) { int col = i % 4, row = (i / 4) % 3, layer = i / 12; return new Vector3(-0.18f + col * 0.12f, 0.30f + layer * 0.14f, -0.12f + row * 0.12f); }
        readonly System.Collections.Generic.List<Transform> heldViz = new System.Collections.Generic.List<Transform>();
        void LateUpdate()
        {
            int n = Mathf.Min(Held, 24);
            while (heldViz.Count < n)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(transform, false);
                g.transform.localScale = Vector3.one * (Fuel.Radius * 1.8f);
                g.GetComponent<Renderer>().sharedMaterial = FuelManager.BallMat;
                int i = heldViz.Count;
                int col = i % 4, row = (i / 4) % 3, layer = i / 12;
                g.transform.localPosition = SlotPos(i);
                heldViz.Add(g.transform);
            }
            for (int i = 0; i < heldViz.Count; i++) heldViz[i].gameObject.SetActive(i < n);
        }

        // ---- 預測彈道:用「現在的飛輪轉速」算球會落哪,畫在場上(進 = 綠、沒進 = 橘),並給 HUD 顯示太短/太長
        public string ShotHint = "";
        public bool ShotWillScore;
        LineRenderer arc;
        Material matOk, matNo;
        readonly System.Collections.Generic.List<Vector3> arcPts = new System.Collections.Generic.List<Vector3>();

        void UpdatePrediction()
        {
            if (arc == null)
            {
                var go = new GameObject("ShotArc");
                arc = go.AddComponent<LineRenderer>();
                arc.widthMultiplier = 0.035f;
                arc.numCapVertices = 4;
                arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arc.receiveShadows = false;
            }
            if (FlywheelRps < 8f || Drive == null) { arc.enabled = false; ShotHint = ""; return; }
            Vector2 pos = Drive.Pose2d; float heading = Drive.HeadingRad;
            float yaw = heading + TurretRad;
            float elev = (ShotElevDeg > 0f ? ShotElevDeg : 71f - 0.75f * HoodDeg) * Mathf.Deg2Rad;
            float speed = ShotSpeedPerRps * FlywheelRps + ShotSpeedBase;
            float c = Mathf.Cos(heading), s = Mathf.Sin(heading);
            Vector2 off = new Vector2(ShooterCalc.TurretOffset.x * c, ShooterCalc.TurretOffset.x * s);
            Vector3 p = new Vector3(pos.x + off.x, LaunchHeight, pos.y + off.y);
            Vector3 v = new Vector3(Mathf.Cos(yaw) * Mathf.Cos(elev) * speed + Drive.Velocity.x,
                                    Mathf.Sin(elev) * speed,
                                    Mathf.Sin(yaw) * Mathf.Cos(elev) * speed + Drive.Velocity.y);
            float W = SimConstants.FieldWidth, L = SimConstants.FieldLength;
            float hd = SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f;
            Vector2 hubB = new Vector2(hd, W / 2f), hubR = new Vector2(L - hd, W / 2f);
            Vector2 hub = (new Vector2(p.x, p.z) - hubB).sqrMagnitude < (new Vector2(p.x, p.z) - hubR).sqrMagnitude ? hubB : hubR;
            arcPts.Clear(); arcPts.Add(p);
            bool crossed = false, inside = false; float along = 0f;
            const float dt = 0.02f, rim = SimConstants.HubRimHeight;
            for (int i = 0; i < 150; i++)
            {
                Vector3 pn = p + v * dt;
                v.y -= 9.81f * dt;
                v /= (1f + 0.375f * dt);
                if (!crossed && v.y < 0f && p.y >= rim && pn.y < rim)
                {
                    float f = (p.y - rim) / Mathf.Max(p.y - pn.y, 1e-4f);
                    Vector3 cp = Vector3.Lerp(p, pn, f);
                    crossed = true;
                    inside = Mathf.Abs(cp.x - hub.x) < 0.5f && Mathf.Abs(cp.z - hub.y) < 0.5f;
                    Vector2 dir = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
                    along = Vector2.Dot(new Vector2(cp.x, cp.z) - hub, dir);   // 負 = 還沒到 HUB 中心(太短)
                    arcPts.Add(cp);
                    break;
                }
                p = pn; arcPts.Add(p);
                if (p.y < 0.05f) break;
            }
            ShotWillScore = crossed && inside;
            if (!crossed) ShotHint = "球飛不到 HUB 高度";
            else if (inside) ShotHint = "預測進球";
            else ShotHint = along < 0f ? $"太短 {Mathf.Abs(along):0.0} m" : $"太長 {along:0.0} m";
            arc.enabled = true;
            arc.positionCount = arcPts.Count;
            arc.SetPositions(arcPts.ToArray());
            if (matOk == null) { matOk = FieldBuilder.MakeMat(new Color(0.3f, 1f, 0.4f)); matNo = FieldBuilder.MakeMat(new Color(1f, 0.6f, 0.15f)); }
            arc.sharedMaterial = ShotWillScore ? matOk : matNo;
        }

        void Update() { UpdatePrediction(); }

        void Fire(Vector2 pos, float heading)
        {
            float yaw = heading + TurretRad + (SpreadDeg > 0f ? Random.Range(-SpreadDeg, SpreadDeg) * Mathf.Deg2Rad : 0f);
            // 出球模型:用機器人查表(飛輪轉速/Hood 角/飛行時間)擬合到「落在 HUB 開口」,球有空氣阻力 0.375/s(與程式的 linearDragTimeConstant 一致)
            // 擬合結果(2~4m 誤差 ≤0.26m,HUB 半寬 0.5m):速度 = 0.14*rps + 2.0 m/s,仰角 = 71° - 0.75*hood
            float elev = (ShotElevDeg > 0f ? ShotElevDeg : 71f - 0.75f * HoodDeg) * Mathf.Deg2Rad;
            float speed = ShotSpeedPerRps * FlywheelRps + ShotSpeedBase;

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
