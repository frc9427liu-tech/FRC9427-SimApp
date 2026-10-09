using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -padtest:用假手把輸入走一遍遊戲內流程(坦克開車、轉向、放 intake、發射),並記錄真實 XInput 讀值,寫到 padtest.txt 後離開。
    public class PadTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            sb.AppendLine("real XInput: " + Pad.Raw());
            yield return new WaitForSeconds(1.0f);
            var d = GameSession.Drive; var m = GameSession.Mech;
            while (FuelManager.All.Count > 0) FuelManager.Remove(FuelManager.All[FuelManager.All.Count - 1]);
            PlayerPrefs.SetInt("tankMode", 1);
            d.SetPose(new Vector2(6f, 4f), 0f); yield return new WaitForSeconds(0.3f);
            Vector2 p0 = d.Pose2d; float h0 = d.HeadingRad;
            Pad.SetFake(true, ly: 1f, ry: 1f); yield return new WaitForSeconds(1.5f);
            sb.AppendLine($"tank both sticks forward 1.5s: moved {(d.Pose2d - p0).magnitude:0.00} m, heading change {Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg):0.0} deg");
            Pad.SetFake(true); yield return new WaitForSeconds(1.0f);
            p0 = d.Pose2d; h0 = d.HeadingRad;
            Pad.SetFake(true, ly: -1f, ry: 1f); yield return new WaitForSeconds(1.0f);
            sb.AppendLine($"tank left back/right fwd 1s: moved {(d.Pose2d - p0).magnitude:0.00} m, heading change {Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg):0.0} deg");
            Pad.SetFake(true, buttons: Pad.A); yield return new WaitForSeconds(0.5f);
            sb.AppendLine($"A held: IntakeDown={m.IntakeDown}");
            Pad.SetFake(true); yield return new WaitForSeconds(0.5f);
            sb.AppendLine($"A released: IntakeDown={m.IntakeDown}");
            m.Held = 20; m.ShotsFired = 0;
            Pad.SetFake(true, rt: 1f); yield return new WaitForSeconds(2.0f);
            sb.AppendLine($"RT held 2s: Shooting={m.Shooting} ShotsFired={m.ShotsFired}");
            sb.AppendLine($"body lean peak: {BodyLean.PeakAbs:0.00} deg (signed {BodyLean.PeakSigned:0.00}); expect > 0.3 if inertia visual works");
            Pad.SetFake(false);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "padtest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}