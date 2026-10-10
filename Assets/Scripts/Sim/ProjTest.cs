using System.Collections;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 通用專案冒煙測試(-projtest <專案資料夾> -noclock):連上程式、前進 3 秒量位移,再依序按 A/B/X/Y/LB/RB/LT/RT,記錄哪些馬達電壓有反應
    public class ProjTest : MonoBehaviour
    {
        static bool DriverOnly => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-driveronly") >= 0;
        IEnumerator Start()
        {
            var sb = new StringBuilder(); Application.targetFrameRate = 60; float t = 0f;
            while ((GameSession.Hal == null || !GameSession.Hal.Connected) && t < 300f) { t += Time.deltaTime; yield return null; }
            sb.AppendLine("PT connected after " + t.ToString("0") + "s");
            yield return new WaitForSeconds(10f);
            var d = GameSession.Drive; var h = GameSession.Hal;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            { var ms = d.GetComponent<MechSim>(); sb.AppendLine("PT mech: " + (ms != null ? ms.Info : "none") + " status=" + h.Status + " enabled=" + h.Enabled + " auto=" + h.Autonomous); }
            float h0 = d.HeadingRad * Mathf.Rad2Deg; Vector2 p0 = d.Pose2d; h.Axes[1] = -1f; yield return new WaitForSeconds(3f); h.Axes[1] = 0f;
            sb.AppendLine($"PT forward3s dx={d.Pose2d.x - p0.x:0.00} dy={d.Pose2d.y - p0.y:0.00} speed={d.Speed:0.00} heading0={h0:0} heading={d.HeadingRad * Mathf.Rad2Deg:0}");
            h.Axes[1] = 0f; h.Axes[0] = 1f; p0 = d.Pose2d; yield return new WaitForSeconds(2f); h.Axes[0] = 0f;
            sb.AppendLine($"PT strafe2s dx={d.Pose2d.x - p0.x:0.00} dy={d.Pose2d.y - p0.y:0.00}");
            if (DriverOnly) { h.Buttons2[1] = true; yield return new WaitForSeconds(0.4f); h.Buttons2[1] = false; yield return new WaitForSeconds(2f); }   // 操作手按 B(選射速模式)
            string[] names = { "A", "B", "X", "Y", "LB", "RB" };
            for (int b = 0; b < 8; b++)
            {
                if (b < 6) { h.Buttons[b] = true; if (!DriverOnly) h.Buttons2[b] = true; } else { h.Axes[b == 6 ? 2 : 3] = 1f; if (!DriverOnly) h.Axes2[b == 6 ? 2 : 3] = 1f; }
                yield return new WaitForSeconds(System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-longhold") >= 0 ? 7f : 2.5f);
                var sbm = new StringBuilder();
                foreach (var kv in h.Devices) if (kv.Key.StartsWith("CANMotor/")) { double v = kv.Value["<motorVoltage"] != null ? (double)kv.Value["<motorVoltage"] : 0; if (System.Math.Abs(v) > 0.3) sbm.Append(kv.Key.Substring(9) + "=" + v.ToString("0.0") + " "); }
                var mm = GameSession.Mech;
                sb.AppendLine($"PT btn {(b < 6 ? names[b] : b == 6 ? "LT" : "RT")}: motors {sbm} | fly={(mm != null ? mm.FlywheelRps : 0):0.0} held={(mm != null ? mm.Held : 0)} shots={(mm != null ? mm.ShotsFired : 0)}");
                if (b < 6) { h.Buttons[b] = false; h.Buttons2[b] = false; } else { h.Axes[b == 6 ? 2 : 3] = 0f; h.Axes2[b == 6 ? 2 : 3] = 0f; }
                yield return new WaitForSeconds(1.5f);
            }
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "projtest.txt");
            System.IO.File.WriteAllText(path, sb.ToString()); Application.Quit();
        }
    }
}