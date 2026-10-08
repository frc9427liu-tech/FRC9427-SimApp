using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -fieldtest:驗證場地幾何。①穿過隧道(開口 y≈0.64) ②撞隧道立柱(y≈1.43,應被擋住) ③開過 BUMP(車身高度應隨斜坡抬升)。結果寫 fieldtest.txt 後離開。
    public class FieldTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            yield return new WaitForSeconds(2f);
            var d = GameSession.Drive;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            d.FieldCentric = true;
            float baseY = SimConstants.BumperHeight / 2f + 0.03f;

            // ① 隧道:從 x=2.8 往 +x 開 3 秒(最高速 4m/s,隧道在 x 3.79~5.46)
            d.SetPose(new Vector2(2.8f, 0.64f), 0f);
            yield return new WaitForSeconds(0.3f);
            for (int i = 0; i < 60; i++) { d.Drive(1f, 0f, 0f); yield return new WaitForSeconds(0.05f); }
            d.Drive(0f, 0f, 0f);
            sb.AppendLine($"TUNNEL y=0.64: end x={d.Pose2d.x:0.00} (期望 > 5.6 才算穿過;隧道 x 3.79~5.46)");

            // ② 立柱:y=1.43 在立柱範圍內(1.279~1.584),應被擋在 x≈3.4
            d.SetPose(new Vector2(2.8f, 1.43f), 0f);
            yield return new WaitForSeconds(0.3f);
            for (int i = 0; i < 60; i++) { d.Drive(1f, 0f, 0f); yield return new WaitForSeconds(0.05f); }
            d.Drive(0f, 0f, 0f);
            sb.AppendLine($"POST y=1.43: end x={d.Pose2d.x:0.00} (期望 < 3.5 = 被立柱擋住)");

            // ③ BUMP:y=2.5(藍方下側 BUMP y 1.58~3.44),從 x=3.2 往 +x
            d.SetPose(new Vector2(3.2f, 2.5f), 0f);
            yield return new WaitForSeconds(0.3f);
            float maxY = 0f; float yAt = 0f;
            for (int i = 0; i < 50; i++)
            {
                d.Drive(0.6f, 0f, 0f);
                yield return new WaitForSeconds(0.05f);
                float h = d.transform.position.y - baseY;
                if (h > maxY) { maxY = h; yAt = d.Pose2d.x; }
            }
            d.Drive(0f, 0f, 0f);
            sb.AppendLine($"BUMP y=2.5: end x={d.Pose2d.x:0.00} 最大抬升={maxY:0.000}m 於 x={yAt:0.00}(期望約 0.165m 於 x≈4.625)");

            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "fieldtest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
