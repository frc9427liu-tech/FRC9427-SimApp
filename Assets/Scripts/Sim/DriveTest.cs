using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -drivetest:內建操控全速前進 1.0 秒(中立區入口,避開 HUB)再放開,每 0.1 秒取樣「實際位移速度」,量峰值、到 90% 的時間、放開後停下距離。結果寫 drivetest.txt。
    // PASS:實際峰值速度在 MaxSpeed ±8%、實際到 90% <= 0.6 s、停下距離 < 0.8 m。
    public class DriveTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            yield return new WaitForSeconds(12f);
            var d = GameSession.Drive;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            d.FieldCentric = true;
            FuelManager.Clear();
            d.SetPose(new Vector2(6.2f, 4.0f), 0f); yield return new WaitForSeconds(0.6f);
            float max = SimConstants.MaxSpeed, peak = 0f, t90 = -1f;
            var prof = new StringBuilder();
            Vector2 p0 = d.Pose2d, pl = p0;
            for (int i = 1; i <= 9; i++)
            {
                d.Drive(1f, 0f, 0f); yield return new WaitForSeconds(0.1f);
                Vector2 pc = d.Pose2d; float v = (pc - pl).magnitude / 0.1f; pl = pc;
                peak = Mathf.Max(peak, v); if (t90 < 0f && v >= 0.9f * max) t90 = i * 0.1f;
                prof.Append($" {i * 0.1f:0.0}s:{v:0.0}/int{d.Speed:0.0}{(ri != null && ri.enabled ? "(RI on!)" : "")}");
            }
            Vector2 p1 = d.Pose2d;
            d.Drive(0f, 0f, 0f); yield return new WaitForSeconds(1.5f);
            float stop = (d.Pose2d - p1).magnitude;
            bool ok = Mathf.Abs(peak - max) <= 0.08f * max && t90 > 0f && t90 <= 0.6f && stop < 0.8f;
            sb.AppendLine($"MaxSpeed={max:0.00} MaxAccel={SimConstants.MaxAccel:0.0}  actualPeak={peak:0.00} m/s  actualT90={(t90 < 0 ? "never" : t90.ToString("0.0") + "s")} (<=0.6)  stopDist={stop:0.00} m (<0.8)  travelled={(p1 - p0).magnitude:0.00} m");
            sb.AppendLine("speed profile(actual m/s per 0.1s):" + prof);
            sb.AppendLine(ok ? "PASS" : "FAIL");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "drivetest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}

