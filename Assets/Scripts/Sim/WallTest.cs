using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -walltest:機器人往藍方牆開,量能開到多近(原 OUTPOST 隱形方塊會把它擋在 x≈1.1);再測進 TOWER 兩根立柱之間。結果 walltest.txt
    public class WallTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            yield return new WaitForSeconds(2f);
            var d = GameSession.Drive;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            d.FieldCentric = true;
            foreach (float y in new[] { 0.88f, 2.0f })
            {
                d.SetPose(new Vector2(3.0f, y), 180f); yield return new WaitForSeconds(0.4f);
                for (int i = 0; i < 40; i++) { d.Drive(-1f, 0f, 0f); yield return new WaitForSeconds(0.1f); }
                d.Drive(0, 0, 0);
                sb.AppendLine($"drive to wall at y={y:0.00}: final x={d.Pose2d.x:0.00} (expect ~0.40 = touching wall)");
            }
            d.SetPose(new Vector2(3.0f, 3.75f), 180f); yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < 40; i++) { d.Drive(-1f, 0f, 0f); yield return new WaitForSeconds(0.1f); }
            d.Drive(0, 0, 0);
            sb.AppendLine($"drive into tower pocket z=3.75: final x={d.Pose2d.x:0.00} y={d.Pose2d.y:0.00}");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "walltest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
