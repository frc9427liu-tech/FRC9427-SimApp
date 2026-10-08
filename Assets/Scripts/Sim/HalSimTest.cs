using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -halsimtest <機器人專案資料夾>:啟動真實機器人程式、連 HALSim、Enable、推搖桿,把收到的裝置/馬達輸出寫到 halsimtest.txt
    public class HalSimTest : MonoBehaviour
    {
        public string Project;

        IEnumerator Start()
        {
            var hs = gameObject.AddComponent<HalSim>();
            hs.Enabled = true;
            hs.StartRobot(Project);
            var report = new StringBuilder();
            float t = 0f;
            while (hs.Status != "connected" && t < 260f)
            {
                t += Time.deltaTime;
                if (hs.Status.StartsWith("robot process exited") || hs.Status.StartsWith("could not") || hs.Status.StartsWith("error") || hs.Status.StartsWith("gradlew")) break;
                yield return null;
            }
            report.AppendLine("connect status after " + t.ToString("0") + "s: " + hs.Status);
            if (hs.Status == "connected")
            {
                yield return new WaitForSeconds(10f);
                report.AppendLine("--- idle (enabled, sticks 0) ---");
                report.Append(hs.Summary());
                hs.Axes[1] = -1f;           // 左搖桿往前
                yield return new WaitForSeconds(3f);
                report.AppendLine("--- left stick forward ---");
                report.Append(hs.Summary());
                hs.Axes[1] = 0f; hs.Axes[0] = 1f;   // 向右平移
                yield return new WaitForSeconds(3f);
                report.AppendLine("--- left stick right ---");
                report.Append(hs.Summary());
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "halsimtest.txt"), report.ToString());
            hs.Stop();
            Application.Quit();
        }
    }

    // -realtest <機器人專案>:進遊戲、用真實程式驅動底盤,推搖桿後記錄機器人位姿變化(realtest.txt)
    public class RealTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            Application.targetFrameRate = 60;   // batchmode 預設不限速(150fps)會把 CPU 吃光,餓死旁邊的 Java 機器人程式
            float t = 0f;
            while ((GameSession.Hal == null || !GameSession.Hal.Connected) && t < 260f) { t += Time.deltaTime; yield return null; }
            sb.AppendLine("connected after " + t.ToString("0") + "s status=" + (GameSession.Hal != null ? GameSession.Hal.Status : "none"));
            yield return new WaitForSeconds(12f);
            var d = GameSession.Drive; var h = GameSession.Hal;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;   // 否則每幀用鍵盤(全 0)覆蓋測試的搖桿值
            sb.AppendLine($"start pose x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00} heading={d.HeadingRad * Mathf.Rad2Deg:0}");
            var msim = d.GetComponent<MechSim>();

            // --- 公平射擊測試:一開始就站在出生點不動,等 25 秒讓程式靠 Limelight 把位姿估計器收斂,再按 RT ---
            {
                var nt0 = d.GetComponent<NtSim>();
                yield return new WaitForSeconds(25f);
                if (nt0 != null) foreach (var kv in nt0.CodePoses) sb.AppendLine($"FAIR code pose {kv.Key} = ({kv.Value[0]:0.00},{kv.Value[1]:0.00},{kv.Value[2] * 57.2958:0}deg)");
                sb.AppendLine($"FAIR unity pose=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00}) heading={d.HeadingRad * 57.2958f:0} tags={(nt0 != null ? nt0.TagsSeen : -1)}");
                var mm = GameSession.Mech; int sc0 = ScoreManager.BlueScore;
                sb.AppendLine("RTPRESS " + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                h.Axes[3] = 1f;
                {
                    double tp = Time.realtimeSinceStartupAsDouble; double echoMs = -1, volMs = -1;
                    while (Time.realtimeSinceStartupAsDouble - tp < 5.0 && (echoMs < 0 || volMs < 0))
                    {
                        var ca = nt0 != null ? nt0.CodeAxes : null;
                        if (echoMs < 0 && ca != null && ca.Length > 3 && ca[3] > 0.5) echoMs = (Time.realtimeSinceStartupAsDouble - tp) * 1000;
                        if (volMs < 0 && Mathf.Abs(h.Num("CANMotor/Talon FX (v6)[9]", "<motorVoltage")) > 0.05f) volMs = (Time.realtimeSinceStartupAsDouble - tp) * 1000;
                        yield return null;
                    }
                    msim.MaxEchoLagMs = 0;
                    sb.AppendLine($"FAIR RT latency:joystickEcho={echoMs:0}ms flywheelVoltage={volMs:0}ms");
                }
                for (int q = 0; q < 80; q++)
                {
                    yield return new WaitForSeconds(0.1f);
                    double tx = 0, ty = 0, tr = 0;
                    if (nt0 != null && nt0.CodePoses.TryGetValue("/AdvantageKit/RealOutputs/Pose", out var cp)) { tx = cp[0]; ty = cp[1]; tr = cp[2] * 57.2958; }
                    string sc = "";
                    if (nt0 != null) foreach (var kv in nt0.CodeScalars) if (kv.Key.Contains("lookahead") || kv.Key.Contains("flywheelgoal") || kv.Key.ToLower().Contains("hood")) sc += $" {kv.Key.Substring(kv.Key.LastIndexOf('/') + 1)}={kv.Value:0.00}";
                    { var rs = ShooterCalc.Solve(d.Pose2d, d.HeadingRad, d.Velocity, d.Omega, RobotMechanisms.BlueHub); sb.AppendLine($"FAIRA q={q} unityTargetAngle={rs.FieldAngle * 57.2958f:0} head={d.HeadingRad * 57.2958f:0} dist={rs.Distance:0.00} vel=({d.Velocity.x:0.00},{d.Velocity.y:0.00})"); }
                    sb.AppendLine($"FAIRT q={q} maxdt={h.MaxDt * 1000:0}ms "); h.MaxDt = 0f;
                    sb.AppendLine($"FAIRT q={q}unity=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00},{d.HeadingRad * 57.2958f:0}) w={d.Omega:0.00} code=({tx:0.00},{ty:0.00},{tr:0}) fly={mm.FlywheelRps:0.0} hood={mm.HoodDeg:0.0} v15={(h.Devices.TryGetValue("CANMotor/Talon FX (v6)[15]", out var d15) && d15["<motorVoltage"] != null ? ((double)d15["<motorVoltage"]).ToString("0.0") : "-")} pos15={msim.Pos("Talon FX (v6)[15]"):0.000} v9={(h.Devices.TryGetValue("CANMotor/Talon FX (v6)[9]", out var d9) && d9["<motorVoltage"] != null ? ((double)d9["<motorVoltage"]).ToString("0.0") : "-")} I15={(h.Devices.TryGetValue("CANMotor/Talon FX (v6)[15]", out var e15) && e15["<torqueCurrent"] != null ? ((double)e15["<torqueCurrent"]).ToString("0.0") : "-")}/{(e15 != null && e15["<supplyCurrent"] != null ? ((double)e15["<supplyCurrent"]).ToString("0.0") : "-")} echoLag={msim.EchoLagMs:0}ms/max{msim.MaxEchoLagMs:0} talonPos15={h.Num("CANEncoder/Talon FX (v6)[15]/Rotor Sensor", "<position"):0.000} accIn={h.Num("CANEncoder/Talon FX (v6)[15]/Rotor Sensor", ">rawPositionInput"):0.000} duty15={(e15 != null && e15["<dutyCycle"] != null ? ((double)e15["<dutyCycle"]).ToString("0.00") : "-")} v13={(h.Devices.TryGetValue("CANMotor/Talon FX (v6)[13]", out var d13) && d13["<motorVoltage"] != null ? ((double)d13["<motorVoltage"]).ToString("0.0") : "-")} en={h.Num("DriverStation/", ">enabled")} rio={(h.Devices.TryGetValue("RoboRIO/", out var rr) ? rr.ToString(Newtonsoft.Json.Formatting.None) : "-")}{sc}");
                }
                h.Axes[3] = 0f;
                yield return new WaitForSeconds(3f);
                { var tcs = new System.Text.StringBuilder(); foreach (var kv in h.TypeCounts) tcs.Append(kv.Key + "=" + kv.Value + " "); sb.AppendLine("FAIR inbound by type: " + tcs + " sample=" + h.Sample); }
                sb.AppendLine($"FAIR echo lag while flywheel spinning (Unity->Java->back): last={msim.EchoLagMs:0}ms max={msim.MaxEchoLagMs:0}ms");
                sb.AppendLine($"FAIR maxInboxBacklog={h.MaxBacklog} maxOutbox={h.MaxOutbox} fps={1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}");
                sb.AppendLine($"FAIR shoot result:shots={mm.ShotsFired} blue {sc0}->{ScoreManager.BlueScore} heading={d.HeadingRad * 57.2958f:0} pos=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00}) fly={mm.FlywheelRps:0.0} hood={mm.HoodDeg:0.0}");
                foreach (var tr in ScoreManager.Trace) sb.AppendLine("TRACE " + tr);
                mm.Held = 8;
                d.SetPose(new Vector2(2.0f, 4.03f), 0f);
                yield return new WaitForSeconds(2f);
            }
            h.Axes[1] = -1f;
            for (int k = 0; k < 8; k++)
            {
                yield return new WaitForSeconds(0.5f);
                sb.AppendLine("   " + d.RbInfo + $" simVel=({d.SimVelField.x:0.00},{d.SimVelField.y:0.00}) simDriven={d.SimDriven}");
                sb.AppendLine($"t={0.5f * (k + 1):0.0} pose=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00}) head={d.HeadingRad * Mathf.Rad2Deg:0} {msim.Debug} " +
                              $"cc=[{h.Num("CANEncoder/CANcoder (v6)[1]", "<position"):0.00},{h.Num("CANEncoder/CANcoder (v6)[2]", "<position"):0.00},{h.Num("CANEncoder/CANcoder (v6)[3]", "<position"):0.00},{h.Num("CANEncoder/CANcoder (v6)[4]", "<position"):0.00}] " +
                              $"drvVel=[{msim.Vel("Talon FX (v6)[2]"):0},{msim.Vel("Talon FX (v6)[4]"):0},{msim.Vel("Talon FX (v6)[6]"):0},{msim.Vel("Talon FX (v6)[8]"):0}] " +
                              $"halV=[{h.Num("CANMotor/Talon FX (v6)[2]", "<motorVoltage"):0.0},{h.Num("CANMotor/Talon FX (v6)[4]", "<motorVoltage"):0.0},{h.Num("CANMotor/Talon FX (v6)[6]", "<motorVoltage"):0.0},{h.Num("CANMotor/Talon FX (v6)[8]", "<motorVoltage"):0.0}] " +
                              $"echoPos=[{h.Num("CANEncoder/Talon FX (v6)[2]/Rotor Sensor", ">rawPositionInput"):0.0},{h.Num("CANEncoder/Talon FX (v6)[4]/Rotor Sensor", ">rawPositionInput"):0.0},{h.Num("CANEncoder/Talon FX (v6)[6]/Rotor Sensor", ">rawPositionInput"):0.0},{h.Num("CANEncoder/Talon FX (v6)[8]/Rotor Sensor", ">rawPositionInput"):0.0}]");
            }
            sb.AppendLine($"after forward 4s: x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00} speed={d.Speed:0.00} heading={d.HeadingRad * Mathf.Rad2Deg:0}");
            h.Axes[1] = 0f; h.Axes[0] = 1f;
            yield return new WaitForSeconds(3f);
            sb.AppendLine($"after strafe right 3s: x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00} speed={d.Speed:0.00} heading={d.HeadingRad * Mathf.Rad2Deg:0}");
            h.Axes[0] = 0f; h.Axes[4] = 1f;
            yield return new WaitForSeconds(2f);
            sb.AppendLine($"after rotate 2s: x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00} heading={d.HeadingRad * Mathf.Rad2Deg:0} omega={d.Omega:0.00}");
            h.Axes[4] = 0f;
            yield return new WaitForSeconds(2f);
            sb.AppendLine($"stopped: speed={d.Speed:0.00}");

            // --- 射擊測試:站在 HUB 前面 2.3m 面向 HUB,按住 RT(axis3)6 秒 ---
            var m = GameSession.Mech;
            d.SetPose(new Vector2(2.3f, 4.03f), 0f);
            yield return new WaitForSeconds(7f);   // 讓程式的位姿估計器靠 Limelight 收斂到傳送後的新位置
            var ntq = d.GetComponent<NtSim>();
            if (ntq != null) foreach (var kv in ntq.CodePoses) sb.AppendLine($"   code pose {kv.Key} = ({kv.Value[0]:0.00},{kv.Value[1]:0.00},{kv.Value[2] * 57.2958:0}deg)");
            sb.AppendLine("before shoot: tagsSeen=" + (ntq != null ? ntq.TagsSeen : -1) + " unityPose=(" + d.Pose2d.x.ToString("0.00") + "," + d.Pose2d.y.ToString("0.00") + ") heading=" + (d.HeadingRad * 57.2958f).ToString("0"));
            int held0 = m.Held, score0 = ScoreManager.BlueScore;
            h.Axes[3] = 1f;
            for (int k = 0; k < 6; k++)
            {
                yield return new WaitForSeconds(1f);
                sb.AppendLine($"shoot t={k + 1}: fly={m.FlywheelRps:0.0}rps hood={m.HoodDeg:0.0} shooting={m.Shooting} shots={m.ShotsFired} held={m.Held} blue={ScoreManager.BlueScore} heading={d.HeadingRad * Mathf.Rad2Deg:0}");
            }
            h.Axes[3] = 0f;
            yield return new WaitForSeconds(3f);
            sb.AppendLine($"shoot result: shots={m.ShotsFired} held {held0}->{m.Held} blue score {score0}->{ScoreManager.BlueScore}");

            // --- 吸球測試:到中立區球堆前,按住 LT(axis2)往前開 ---
            d.SetPose(new Vector2(4.9f, 5.2f), 0f);   // 球堆在 x≥5.6 附近
            yield return new WaitForSeconds(1f);
            int heldA = m.Held;
            h.Axes[2] = 1f; h.Axes[1] = -0.5f;
            for (int k = 0; k < 4; k++)
            {
                yield return new WaitForSeconds(1f);
                string V(string key) => h.Devices.TryGetValue("CANMotor/Talon FX (v6)[" + key + "]", out var dv) && dv["<motorVoltage"] != null ? ((double)dv["<motorVoltage"]).ToString("0.00") : "-";
                sb.AppendLine($"intake t={k + 1}: arm={m.ArmExt:0.00}m held={m.Held} pos=({d.Pose2d.x:0.0},{d.Pose2d.y:0.0}) ax2={h.Axes[2]} volt[30]={V("30")} rotorPos[30]={msim.Pos("Talon FX (v6)[30]"):0.00} volt[41]={V("41")} volt[45]={V("45")} volt[31]={V("31")} DS={h.Num("DriverStation/", ">enabled")}");
            }
            h.Axes[2] = 0f; h.Axes[1] = 0f;
            sb.AppendLine($"intake result: held {heldA}->{m.Held}");
            var ms = d.GetComponent<MechSim>();
            var ntt = d.GetComponent<NtSim>();
            sb.AppendLine("ntsim: " + (ntt != null ? $"status={ntt.Status} published={ntt.Published} tagsNow={ntt.TagsSeen}" : "none"));
            sb.AppendLine("mechsim: " + (ms != null ? ms.Info : "none") + " simDriven=" + d.SimDriven + " hal=" + h.Status + " msgs=" + h.MessagesIn);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "realtest.txt"), sb.ToString());
            GameSession.Hal?.Stop();
            Application.Quit();
        }
    }
}
