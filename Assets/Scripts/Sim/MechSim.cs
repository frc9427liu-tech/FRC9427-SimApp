using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FrcSim
{
    // 虛擬硬體:讀真實機器人程式在 HALSim 輸出的馬達電壓 → 馬達物理 → 把轉子位置/速度、CANcoder、陀螺儀寫回給程式,
    // 並用「程式自己看到的」輪速與轉向角算出底盤速度,推動 Unity 裡的機器人(照 maple-sim/舊網頁橋接的作法移植)。
    public class MechSim : MonoBehaviour
    {
        public HalSim Hal;
        public SwerveDrive Drive;
        public string MechPath;               // <專案>.mech.json(馬達負載、齒比、模組位置;由原始碼推導)

        class Motor
        {
            public double Vel, Pos;           // rad/s, rev
            public double R, Kt, Ke;
            public bool Invert;               // Clockwise_Positive:HALSim 原始座標與使用者座標鏡像
            public double Damping, Lead;
            public double Inertia = 0.002, Friction = 0.01, MinRot = double.NegativeInfinity, MaxRot = double.PositiveInfinity;
        }
        class Link { public string Sensor, Motor; public double Ratio = 1, Offset; public bool Invert; }
        class Module { public string Name, Drive, Encoder; public double X, Y; public int UserSign = 1; }

        readonly Dictionary<string, Motor> motors = new Dictionary<string, Motor>();
        readonly List<Link> links = new List<Link>();
        readonly Dictionary<string, JObject> loads = new Dictionary<string, JObject>();
        readonly List<Module> modules = new List<Module>();
        double driveRatio = 4.71, wheelRadius = 0.0508;
        double angleSign = 1, omegaSign = 1, gyroSign = 1;   // 符號除錯開關(mech.json chassis.angleSign / omegaSign / gyroSign,預設 1)
        string gyro;
        bool loaded;
        float acc;

        readonly Dictionary<string, List<KeyValuePair<double, double>>> sentLogs = new Dictionary<string, List<KeyValuePair<double, double>>>();
        public float EchoLagMs, MaxEchoLagMs;
        public string SkipName;   // 測試探針用:不模擬這顆馬達
        public string Debug = "";
        public int Steps, ModuleHits;
        public string Info => $"loaded={loaded} modules={modules.Count} motors={motors.Count} steps={Steps} moduleHits={ModuleHits} {Debug}";

        // 給 RobotMechanisms 用:馬達轉速(rev/s)、位置(rev,轉子側)
        public string Probe()
        {
            string D(string k, string p) => Hal.Devices.TryGetValue(k, out var d) && d[p] != null ? ((double)d[p]).ToString("0.000") : "-";
            return $"volt[2]={D("CANMotor/Talon FX (v6)[2]", "<motorVoltage")} vel[2]={Vel("Talon FX (v6)[2]"):0.000} pos[2]={Pos("Talon FX (v6)[2]"):0.000} " +
                   $"volt[46]={D("CANMotor/Talon FX (v6)[46]", "<motorVoltage")} vel[46]={Vel("Talon FX (v6)[46]"):0.000} cancoder[1]={D("CANEncoder/CANcoder (v6)[1]", "<position")} " +
                   $"DS={D("DriverStation/", ">enabled")} ax1={(Hal.Axes.Length > 1 ? Hal.Axes[1] : 0f)}";
        }

        public double Vel(string name) => motors.TryGetValue(name, out var m) ? m.Vel / (2 * Math.PI) : 0.0;
        public double Pos(string name) => motors.TryGetValue(name, out var m) ? (m.Invert ? -m.Pos : m.Pos) : 0.0;   // 使用者座標

        [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

        void Start()
        {
            try { timeBeginPeriod(1); } catch { }   // Task.Delay 才能到 ~1ms 精度
            try
            {
                if (File.Exists(MechPath))
                {
                    var j = JObject.Parse(File.ReadAllText(MechPath));
                    foreach (var l in (JArray)j["links"] ?? new JArray())
                        links.Add(new Link { Sensor = (string)l["sensor"], Motor = (string)l["motor"], Ratio = (double?)l["ratio"] ?? 1, Offset = (double?)l["offset"] ?? 0, Invert = (bool?)l["invert"] ?? false });
                    foreach (var p in (JObject)j["loads"] ?? new JObject()) loads[p.Key] = (JObject)p.Value;
                    var ch = j["chassis"] as JObject;
                    if (ch != null)
                    {
                        driveRatio = (double?)ch["driveRatio"] ?? driveRatio;
                        wheelRadius = (double?)ch["wheelRadius"] ?? wheelRadius;
                        angleSign = (double?)ch["angleSign"] ?? 1; omegaSign = (double?)ch["omegaSign"] ?? 1; gyroSign = (double?)ch["gyroSign"] ?? 1;
                        gyro = (string)ch["gyro"];
                        foreach (var m in (JArray)ch["modules"])
                            modules.Add(new Module { Name = (string)m["name"], Drive = (string)m["drive"], Encoder = (string)m["encoder"], X = (double)m["x"], Y = (double)m["y"], UserSign = (int?)m["userSign"] ?? 1 });
                    }
                    loaded = true;
                }
                else UnityEngine.Debug.LogWarning("MechSim: mech file not found: " + MechPath);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("MechSim load: " + e.Message); }
        }

        JObject LoadFor(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, @"\[(\d+)\]$");
            string id = m.Success ? "[" + m.Groups[1].Value + "]" : null;
            if (loads.TryGetValue(name, out var a)) return a;
            if (id != null && loads.TryGetValue(id, out var b)) return b;
            return null;
        }

        void FixedUpdate()
        {
            if (Hal == null || !Hal.Connected) return;
            acc += Time.fixedDeltaTime;
            const float dt = 0.02f;          // 機器人程式本身 20ms 一個週期
            if (acc < dt) return;
            acc -= dt;
            Steps++;
            StepMotors(dt);
            StepChassis(dt);
        }

        void StepMotors(float dt)
        {
            var keys = new List<string>(Hal.Devices.Keys);
            foreach (var key in keys)
            {
                if (!key.StartsWith("CANMotor/")) continue;
                string name = key.Substring(9);
                if (name == SkipName) continue;
                var dev = Hal.Devices[key];
                if (!motors.TryGetValue(name, out var m))
                {
                    // Kraken X60:自由轉速 100 rev/s、堵轉 7.09 N·m / 366 A(12V)
                    double stallA = 366, stallNm = 7.09, freeRps = 100;
                    if (name.Contains("X44")) { stallA = 275; stallNm = 4.05; freeRps = 130; }
                    m = new Motor { R = 12.0 / stallA, Kt = stallNm / stallA, Ke = 12.0 / (freeRps * 2 * Math.PI) };
                    motors[name] = m;
                }
                var ld = LoadFor(name);
                if (ld != null)
                {
                    m.Inertia = (double?)ld["inertia"] ?? m.Inertia;
                    m.Friction = (double?)ld["friction"] ?? m.Friction;
                    m.Damping = (double?)ld["damping"] ?? 0.0;
                    m.Lead = (double?)ld["lead"] ?? 0.0;
                    m.MinRot = (double?)ld["minRot"] ?? double.NegativeInfinity;
                    m.MaxRot = (double?)ld["maxRot"] ?? double.PositiveInfinity;
                }
                double volts = dev["<motorVoltage"] != null ? (double)dev["<motorVoltage"] : 0.0;
                const int n = 4; double h = dt / n;
                double damp = m.Kt * m.Ke / m.R + m.Damping;   // 馬達反電動勢 + 機構黏滯阻尼(補償模擬迴路延遲)
                for (int i = 0; i < n; i++)
                {
                    double drive = m.Kt * volts / m.R;
                    double torque = drive - damp * m.Vel;
                    double fr = Math.Abs(m.Vel) < 1e-3 && Math.Abs(torque) < m.Friction ? -torque : -Math.Sign(m.Vel) * m.Friction;
                    m.Vel = (m.Vel + ((drive + fr) / m.Inertia) * h) / (1 + (damp / m.Inertia) * h);
                    m.Pos += (m.Vel / (2 * Math.PI)) * h;
                    if (m.Pos < m.MinRot) { m.Pos = m.MinRot; if (m.Vel < 0) m.Vel = 0; }
                    if (m.Pos > m.MaxRot) { m.Pos = m.MaxRot; if (m.Vel > 0) m.Vel = 0; }
                }
                double pw = m.Pos + m.Lead * m.Vel / (2 * Math.PI);   // 模擬迴路有延遲:位置回授往前預測,等同加了微分,避免純 P 的位置迴路震盪
                Hal.QueueDevice("CANEncoder", name + "/Rotor Sensor", ">rawPositionInput", pw, ">velocity", m.Vel / (2 * Math.PI));
                if (name == "Talon FX (v6)[9]" || name == "Talon FX (v6)[2]")   // 飛輪(轉動時值會變)與一顆驅動馬達
                {
                    // 往返延遲量測:記下送出的值與時間,等機器人端回聲(Devices 內 >rawPositionInput)出現同樣的值
                    if (!sentLogs.TryGetValue(name, out var sentLog)) sentLogs[name] = sentLog = new List<KeyValuePair<double, double>>();
                    sentLog.Add(new KeyValuePair<double, double>(pw, Time.realtimeSinceStartupAsDouble));
                    if (sentLog.Count > 400) sentLog.RemoveRange(0, 100);
                    if (Hal.Devices.TryGetValue("CANEncoder/" + name + "/Rotor Sensor", out var echoDev) && echoDev[">rawPositionInput"] != null)
                    {
                        double ev = (double)echoDev[">rawPositionInput"];
                        for (int i = sentLog.Count - 1; i >= 0; i--)
                            if (Math.Abs(sentLog[i].Key - ev) < 1e-9) { EchoLagMs = (float)((Time.realtimeSinceStartupAsDouble - sentLog[i].Value) * 1000.0); if (EchoLagMs > MaxEchoLagMs) MaxEchoLagMs = EchoLagMs; break; }
                    }
                }
                foreach (var l in links)
                {
                    if (l.Motor != name) continue;
                    double r = (l.Ratio == 0 ? 1 : l.Ratio) * (l.Invert ? -1 : 1);
                    Hal.QueueDevice("CANEncoder", l.Sensor, ">rawPositionInput", pw / r + l.Offset, ">velocity", m.Vel / (2 * Math.PI) / r);
                }
            }
        }

        void StepChassis(float dt)
        {
            if (!loaded || modules.Count == 0 || Drive == null) return;
            // 最小平方法求 (vx, vy, omega):每個模組速度 v_i = (vx - w*y_i, vy + w*x_i)
            int n = 0; double svx = 0, svy = 0, srr = 0, srv = 0;
            foreach (var m in modules)
            {
                if (!motors.TryGetValue(m.Drive, out var mot)) continue;
                if (!Hal.Devices.TryGetValue("CANEncoder/" + m.Encoder, out var enc) || enc["<position"] == null) continue;
                double speed = (m.UserSign * (mot.Vel / (2 * Math.PI)) / driveRatio) * 2 * Math.PI * wheelRadius;
                double a = angleSign * (double)enc["<position"] * 2 * Math.PI;
                double vx = speed * Math.Cos(a), vy = speed * Math.Sin(a);
                n++; svx += vx; svy += vy;
                srr += m.X * m.X + m.Y * m.Y;
                srv += m.X * vy - m.Y * vx;
            }
            ModuleHits = n;
            if (n < 2) return;
            double cvx = svx / n, cvy = svy / n, omega = omegaSign * srv / (srr == 0 ? 1 : srr);
            Debug = $"chassis v=({cvx:0.00},{cvy:0.00}) w={omega:0.00}";

            // 機器人座標 → 場地座標
            float th = Drive.HeadingRad;
            float c = Mathf.Cos(th), s = Mathf.Sin(th);
            Vector2 vField = new Vector2((float)(cvx * c - cvy * s), (float)(cvx * s + cvy * c));
            Drive.SimDriven = true;
            Drive.SimVelField = vField;
            Drive.SimOmega = (float)omega;

            // 陀螺儀回寫:航向(度)與角速度,讓場向駕駛與里程計閉環
            if (gyro != null)
                Hal.QueueDevice("CANGyro", gyro, ">rawYawInput", gyroSign * th * Mathf.Rad2Deg, ">angularVelZ", gyroSign * omega * Mathf.Rad2Deg);
        }
    }
}
