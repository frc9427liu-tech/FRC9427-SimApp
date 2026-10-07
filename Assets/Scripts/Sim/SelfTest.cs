using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -selftest:自動在不同距離靜止射擊,統計進球率,寫到 selftest.txt 後離開。
    public class SelfTest : MonoBehaviour
    {
        public SwerveDrive Drive;
        public RobotMechanisms Mech;

        IEnumerator Start()
        {
            var ri = Drive.GetComponent<RobotInput>();
            if (ri != null) ri.enabled = false;   // 否則每幀把 Shooting 設回 false
            var sb = new StringBuilder();
            sb.AppendLine("distance_m, shots, scored");
            float[] dists = { 2.0f, 2.5f, 3.0f, 3.5f, 4.0f };
            Vector2 hub = RobotMechanisms.BlueHub;
            // 先清掉場上的球,避免干擾
            yield return new WaitForSeconds(0.5f);
            while (FuelManager.All.Count > 0) FuelManager.Remove(FuelManager.All[FuelManager.All.Count - 1]);
            foreach (float d in dists)
            {
                // 站在 HUB 前面(場中央側),面向 HUB:藍方 HUB 在 x=4.6,往 -x 看(heading 180°)
                Drive.SetPose(new Vector2(hub.x + d + 0.15f, hub.y + 0.0f), 180f);
                Drive.Drive(0, 0, 0);
                Mech.Held = 20;
                Mech.ShotsFired = 0;
                int s0 = ScoreManager.BlueScore;
                Mech.Shooting = true;
                float t = 0f;
                while (Mech.ShotsFired < 20 && t < 8f) { t += Time.deltaTime; yield return null; }
                Mech.Shooting = false;
                yield return new WaitForSeconds(2.0f);
                sb.AppendLine($"{d:0.00}, {Mech.ShotsFired}, {ScoreManager.BlueScore - s0}");
                // 清理飛出的球(避免場上累積造成卡頓)
                yield return null;
            }
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "selftest.txt"), sb.ToString());
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "selftest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
