using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -leotest <LEO 專案資料夾>:啟動 LEO 真實程式(坦克+兩支手把),用虛擬手把推搖桿/按鍵,記錄底盤與機構反應到 leotest.txt
    public class LeoTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            Application.targetFrameRate = 60;
            float t = 0f;
            while ((GameSession.Hal == null || !GameSession.Hal.Connected) && t < 300f) { t += Time.deltaTime; yield return null; }
            sb.AppendLine("connected after " + t.ToString("0") + "s status=" + (GameSession.Hal != null ? GameSession.Hal.Status : "none"));
            yield return new WaitForSeconds(10f);
            var d = GameSession.Drive; var h = GameSession.Hal; var m = GameSession.Mech;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            while (FuelManager.All.Count > 0) FuelManager.Remove(FuelManager.All[FuelManager.All.Count - 1]);
            d.SetPose(new Vector2(6f, 4f), 0f); yield return new WaitForSeconds(0.5f);
            Vector2 p0 = d.Pose2d; float h0 = d.HeadingRad;
            h.Axes[1] = -1f; h.Axes[5] = -1f;   // 兩支搖桿往前推(原始值 -1)
            yield return new WaitForSeconds(3f);
            sb.AppendLine($"sticks forward 3s: dx={d.Pose2d.x - p0.x:0.00} dy={d.Pose2d.y - p0.y:0.00} headingChange={Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg):0.0} | {m.Sim.Info}");
            h.Axes[1] = 0f; h.Axes[5] = 0f; yield return new WaitForSeconds(1.5f);
            d.SetPose(new Vector2(8.2f, 5.5f), 0f); yield return new WaitForSeconds(0.5f);   // 回到空曠處再測轉向(剛才可能貼著 HUB)
            p0 = d.Pose2d; h0 = d.HeadingRad;
            h.Axes[1] = 1f; h.Axes[5] = -1f;
            for (int k = 0; k < 4; k++) { yield return new WaitForSeconds(0.25f); sb.AppendLine($"  turn t={0.25f * (k + 1):0.00}: {m.Sim.Info} heading={d.HeadingRad * Mathf.Rad2Deg:0} Omega={d.Omega:0.00} SimOmega={d.SimOmega:0.00} simDriven={d.SimDriven} | {d.RbInfoOld}"); }
            sb.AppendLine($"left back/right fwd 1s: moved={(d.Pose2d - p0).magnitude:0.00} headingChange={Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg):0.0}");
            h.Axes[1] = 0f; h.Axes[5] = 0f; yield return new WaitForSeconds(1f);
            h.Buttons[0] = true; yield return new WaitForSeconds(2f);
            sb.AppendLine($"A held 2s: IntakeDown={m.IntakeDown} ArmExt={m.ArmExt:0.00}");
            h.Buttons[0] = false; h.Buttons[1] = true; yield return new WaitForSeconds(2f);
            sb.AppendLine($"B held 2s: IntakeDown={m.IntakeDown}");
            h.Buttons[1] = false;
            m.Held = 20; m.ShotsFired = 0;
            h.Axes2[3] = 1f; yield return new WaitForSeconds(4f);
            sb.AppendLine($"RT held 4s: FlywheelRps={m.FlywheelRps:0.0} Shooting={m.Shooting} ShotsFired={m.ShotsFired}");
            h.Axes2[3] = 0f;
            h.Axes2[0] = 1f; yield return new WaitForSeconds(1.5f);
            sb.AppendLine($"turret right 1.5s: TurretRad={m.TurretRad:0.00}");
            h.Axes2[0] = 0f;
            // BUMP:從 x=3.0 往前(+x)衝過藍方下側 BUMP(y 約 2.5),看會不會卡住
            h.Buttons[0] = false; h.Axes2[3] = 0f;
            d.SetPose(new Vector2(3.0f, 2.5f), 0f); yield return new WaitForSeconds(0.5f);
            h.Axes[1] = -1f; h.Axes[5] = -1f;
            float maxPitch = 0f, maxY = 0f;
            for (int k = 0; k < 40; k++) { yield return new WaitForSeconds(0.1f); maxPitch = Mathf.Max(maxPitch, Mathf.Abs(d.BumpPitchDeg)); maxY = Mathf.Max(maxY, d.transform.position.y); }
            sb.AppendLine($"BUMP run 4s from x=3.0,y=2.5: end x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00} maxPitch={maxPitch:0.0} maxY={maxY:0.00} speed={d.Speed:0.00}");
            h.Axes[1] = 0f; h.Axes[5] = 0f;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "leotest.txt"), sb.ToString());
            h.Stop();
            Application.Quit();
        }
    }
}
