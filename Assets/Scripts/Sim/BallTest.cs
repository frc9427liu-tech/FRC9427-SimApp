using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -balltest:機器人以全速衝進中立區的球堆(以及把球推向牆),記錄速度與位置,檢查會不會被球卡住。結果寫 balltest.txt。
    public class BallTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            yield return new WaitForSeconds(12f);   // 等官方場地模型載入並重新擺球
            sb.AppendLine("fuel count=" + FuelManager.All.Count + " modelFuel=" + (FieldModel.FuelPositions != null ? FieldModel.FuelPositions.Count : -1));
            var d = GameSession.Drive;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            d.FieldCentric = true;
            // 場上最左/最右的球 x 範圍(中立區)
            float minX = 99, maxX = -99;
            foreach (var f in FuelManager.All) { float x = f.transform.position.x; if (x > 6f && x < 11f) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); } }
            sb.AppendLine($"neutral fuel x range {minX:0.00}..{maxX:0.00}");
            d.SetPose(new Vector2(minX - 1.5f, 4.0f), 0f);
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < 12; i++)
            {
                d.Drive(1f, 0f, 0f);
                yield return new WaitForSeconds(0.5f);
                sb.AppendLine($"t={0.5f * (i + 1):0.0}s x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00} speed={d.Speed:0.00}");
            }
            d.Drive(0, 0, 0);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "balltest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
