using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -intaketest:驗證吸球不是磁鐵。手臂放下、滾輪轉,車不動,在車前不同距離各放一顆球:
    //   遠球(保險桿前 60cm、40cm)必須原地不被吸;近球(滾輪前 10cm)要被拉進來並計入 Held。結果寫 intaketest.txt。
    public class IntakeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            yield return new WaitForSeconds(12f);
            var d = GameSession.Drive; var m = GameSession.Mech;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            FuelManager.Clear();
            d.SetPose(new Vector2(4f, 4f), 0f);
            yield return new WaitForSeconds(0.5f);
            // 規格(code-spec-intake-hopper):手臂 0→0.25m 約 0.4s;達到可吸球門檻 0.2m 應在 0.5s 內
            float tArm = -1f, t0 = Time.time;
            while (Time.time - t0 < 1.5f) { m.IntakeDown = true; yield return null; if (tArm < 0f && m.ArmExt >= 0.2f) tArm = Time.time - t0; }
            sb.AppendLine($"arm 到 0.2m 用時={tArm:0.00}s (規格 ≤0.5s) ArmMax={RobotMechanisms.ArmMax} (規格 0.25)");
            for (int i = 0; i < 10; i++) { m.IntakeDown = true; yield return new WaitForSeconds(0.1f); }   // 手臂伸到底
            Vector3 fwd = d.transform.right; fwd.y = 0f; fwd.Normalize();
            float bh = SimConstants.BumperLength / 2f;
            sb.AppendLine($"armExt={m.ArmExt:0.000} held0={m.Held} bumperHalf={bh:0.000}");
            int held0 = m.Held; Vector3 c = d.transform.position; c.y = Fuel.Radius;
            var far = FuelManager.Spawn(c + fwd * (bh + m.ArmExt + 0.60f), Vector3.zero);
            var mid = FuelManager.Spawn(c + fwd * (bh + m.ArmExt + 0.35f), Vector3.zero);
            var near = FuelManager.Spawn(c + fwd * (bh + m.ArmExt + 0.10f), Vector3.zero);
            Vector3 farP = far.transform.position, midP = mid.transform.position;
            for (int i = 0; i < 20; i++) { m.IntakeDown = true; yield return new WaitForSeconds(0.1f); }
            bool farMoved = (far != null ? (far.transform.position - farP).magnitude : 99f) > 0.05f;
            bool midMoved = (mid != null ? (mid.transform.position - midP).magnitude : 99f) > 0.05f;
            sb.AppendLine($"held增加={m.Held - held0} (期望 1)  遠球(滾輪前60cm)被吸/移動={farMoved} (期望 False)  中球(滾輪前35cm)被吸/移動={midMoved} (期望 False)");
            sb.AppendLine((tArm > 0f && tArm <= 0.5f && m.Held - held0 == 1 && !farMoved && !midMoved) ? "PASS" : "FAIL");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "intaketest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}

