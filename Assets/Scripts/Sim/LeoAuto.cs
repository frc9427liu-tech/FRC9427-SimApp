using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -leoauto <專案>:真實程式模式,不碰手把,記錄 AUTO 階段(前 20 秒)機器人的路徑、航向、得分,拿來檢查/調整自動模式(轉彎秒數、出力)
    public class LeoAuto : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            Application.targetFrameRate = 60;
            float t = 0f;
            while ((GameSession.Hal == null || !GameSession.Hal.Connected) && t < 300f) { t += Time.deltaTime; yield return null; }
            sb.AppendLine("connected after " + t.ToString("0") + "s; match clock at connect = " + ScoreManager.MatchTime.ToString("0.0") + "s (should be ~0)");
            var d = GameSession.Drive; var m = GameSession.Mech;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            d.SetPose(new Vector2(2.0f, 4.035f), 0f);
            float next = 0f;
            while (ScoreManager.MatchTime < 21f)
            {
                if (ScoreManager.MatchTime >= next)
                {
                    next += 1f;
                    sb.AppendLine($"clock={ScoreManager.MatchTime:0.0} phase={ScoreManager.Phase} pose=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00}) heading={Mathf.DeltaAngle(0, d.HeadingRad * Mathf.Rad2Deg):0} speed={d.Speed:0.00} fly={m.FlywheelRps:0.0} shooting={m.Shooting} shots={m.ShotsFired} blueScore={ScoreManager.BlueScore}");
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "leoauto.txt"), sb.ToString());
            GameSession.Hal.Stop();
            Application.Quit();
        }
    }
}
