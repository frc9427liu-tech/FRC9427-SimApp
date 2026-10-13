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
            Prefs.Isolated = true;   // padtest 會改 tankMode/easyTank:只在記憶體,不動使用者的存檔
            sb.AppendLine("real XInput: " + Pad.Raw());
            yield return new WaitForSeconds(1.0f);
            var d = GameSession.Drive; var m = GameSession.Mech;
            while (FuelManager.All.Count > 0) FuelManager.Remove(FuelManager.All[FuelManager.All.Count - 1]);
            Prefs.SetInt("tankMode", 1); Prefs.SetInt("easyTank", 0);   // 這段測「左右搖桿各控一側輪」的經典坦克;簡易操控(預設)另測
            d.SetPose(new Vector2(6f, 4f), 0f); yield return new WaitForSeconds(0.3f);
            Vector2 p0 = d.Pose2d; float h0 = d.HeadingRad;
            Pad.SetFake(true, ly: 1f, ry: 1f); yield return new WaitForSeconds(1.5f);
            sb.AppendLine($"tank both sticks forward 1.5s: moved {(d.Pose2d - p0).magnitude:0.00} m, heading change {Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg):0.0} deg");
            Pad.SetFake(true); yield return new WaitForSeconds(1.0f);
            p0 = d.Pose2d; h0 = d.HeadingRad;
            Pad.SetFake(true, ly: -1f, ry: 1f); yield return new WaitForSeconds(1.0f);
            sb.AppendLine($"tank left back/right fwd 1s: moved {(d.Pose2d - p0).magnitude:0.00} m, heading change {Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg):0.0} deg");
            // 簡易操控(預設):左搖桿推一半,0.5 秒後速度應 >= 1.0 m/s(舊的平方曲線+4/s 限速只有約 0.3)
            Prefs.SetInt("easyTank", 1); Pad.SetFake(true); yield return new WaitForSeconds(1.0f);
            d.SetPose(new Vector2(6f, 4f), 0f); yield return new WaitForSeconds(0.3f);
            Pad.SetFake(true, ly: 0.5f); yield return new WaitForSeconds(0.5f);
            float sp = d.Speed; Pad.SetFake(true); yield return new WaitForSeconds(0.8f);
            sb.AppendLine($"easy tank half stick 0.5s: speed={sp:0.00} m/s (expect >= 1.0) {(sp >= 1.0f ? "PASS" : "FAIL")}");
            Pad.SetFake(true, buttons: Pad.A); yield return new WaitForSeconds(0.5f);
            sb.AppendLine($"A held: IntakeDown={m.IntakeDown}");
            Pad.SetFake(true); yield return new WaitForSeconds(0.5f);
            sb.AppendLine($"A released: IntakeDown={m.IntakeDown}");
            m.Held = 20; m.ShotsFired = 0;
            Pad.SetFake(true, rt: 1f); yield return new WaitForSeconds(2.0f);
            sb.AppendLine($"RT held 2s: Shooting={m.Shooting} ShotsFired={m.ShotsFired}");
            int chute0 = HumanPlayer.BlueChute, fuel0 = FuelManager.All.Count;
            Pad.SetFake(true, buttons: Pad.Y); yield return new WaitForSeconds(1.2f);
            Pad.SetFake(true); yield return new WaitForSeconds(0.3f);
            sb.AppendLine($"human-player chute (Y held 1.2s): chute {chute0} -> {HumanPlayer.BlueChute}, field fuel {fuel0} -> {FuelManager.All.Count} (expect ~5 released)");
            int chuteB = HumanPlayer.BlueChute;
            FuelManager.Spawn(new Vector3(0.12f, 0.08f, 0.88f), Vector3.zero); yield return new WaitForSeconds(0.4f);
            sb.AppendLine($"corral: ball pushed into OUTPOST bottom opening → chute {chuteB} -> {HumanPlayer.BlueChute} (expect +1)");
            sb.AppendLine($"body lean peak: {BodyLean.PeakAbs:0.00} deg (signed {BodyLean.PeakSigned:0.00}); expect > 0.3 if inertia visual works");
            Pad.SetFake(false);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "padtest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}