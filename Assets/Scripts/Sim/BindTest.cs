using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -bindtest <專案資料夾>:自動驗收。把 RobotContainer 裡「每一個」按鍵綁定都按一遍(1.8 秒),
    // 看手臂/滾輪/飛輪/砲塔/持球數/發射數有沒有可見變化;沒變化的標 FAIL(= 模擬器還沒有這個動作的效果)。結果寫 bindtest.txt。
    public class BindTest : MonoBehaviour
    {
        struct Snap { public float arm, roller, fly, turret; public int held, shots; public bool feeding, ejecting; }
        Snap Take(RobotMechanisms m) => new Snap { arm = m.ArmExt, roller = m.RollerSigned, fly = m.FlywheelRps, turret = m.TurretRad, held = m.Held, shots = m.ShotsFired, feeding = m.Feeding, ejecting = m.Ejecting };

        static int ButtonIndex(string k) { switch (k) { case "A": return 0; case "B": return 1; case "X": return 2; case "Y": return 3; case "LB": return 4; case "RB": return 5; case "Back": return 6; case "Start": return 7; } return -1; }

        void Press(HalSim h, bool driver, string key, bool on)
        {
            int bi = ButtonIndex(key);
            if (bi >= 0) { if (driver) h.Buttons[bi] = on; else h.Buttons2[bi] = on; return; }
            float v = on ? 1f : 0f;
            if (key == "LT") { if (driver) h.Axes[2] = v; else h.Axes2[2] = v; }
            else if (key == "RT") { if (driver) h.Axes[3] = v; else h.Axes2[3] = v; }
            else if (key == "左搖桿X") { if (driver) h.Axes[0] = v; else h.Axes2[0] = v; }
            else if (key == "↑" && !driver) h.Pov2 = on ? 0 : -1;
            else if (key == "→" && !driver) h.Pov2 = on ? 90 : -1;
            else if (key == "↓" && !driver) h.Pov2 = on ? 180 : -1;
            else if (key == "←" && !driver) h.Pov2 = on ? 270 : -1;
        }

        static bool Testable(BindingMap.Bind b) => ButtonIndex(b.Key) >= 0 || b.Key == "LT" || b.Key == "RT" || b.Key == "左搖桿X" || (!b.Driver && (b.Key == "↑" || b.Key == "↓" || b.Key == "←" || b.Key == "→"));

        IEnumerator Start()
        {
            var sb = new StringBuilder();
            Application.targetFrameRate = 60;
            float t = 0f;
            while ((GameSession.Hal == null || !GameSession.Hal.Connected) && t < 300f) { t += Time.deltaTime; yield return null; }
            sb.AppendLine("connected after " + t.ToString("0") + "s; bindings=" + BindingMap.List.Count);
            ScoreManager.MatchTime = 30f;   // 跳過 AUTO(自動階段機器人程式自己跑,手把無效),直接進 TELEOP
            yield return new WaitForSeconds(8f);
            var d = GameSession.Drive; var h = GameSession.Hal; var m = GameSession.Mech;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            while (FuelManager.All.Count > 0) FuelManager.Remove(FuelManager.All[FuelManager.All.Count - 1]);
            d.SetPose(new Vector2(6f, 4f), 0f);
            int pass = 0, fail = 0, skip = 0;
            foreach (var b in BindingMap.List)
            {
                string who = b.Driver ? "駕駛" : "操作手";
                if (!Testable(b)) { sb.AppendLine($"SKIP  {who} {b.Key}  → {b.Desc}   (搖桿軸/不可自動按)"); skip++; continue; }
                // 前置:手臂要先放下,才看得出「收起」
                bool isRaise = (b.Raw + b.Desc).ToLower().Contains("raise") || b.Desc.Contains("收手臂");
                if (isRaise) { h.Buttons[0] = true; yield return new WaitForSeconds(1.2f); h.Buttons[0] = false; }   // 測「收起」要先放下
                else { h.Pov2 = 0; yield return new WaitForSeconds(1.5f); h.Pov2 = -1; }                             // 其他先收起,才看得出「放下」
                yield return new WaitForSeconds(0.3f);
                m.Held = 10; m.ShotsFired = 0;
                var b0 = Take(m);
                float dArm = 0, dRoll = 0, dFly = 0, dTur = 0; bool feed = false, ej = false;
                Press(h, b.Driver, b.Key, true);
                for (int k = 0; k < 6; k++)
                {
                    yield return new WaitForSeconds(0.3f);
                    var s = Take(m);
                    dArm = Mathf.Max(dArm, Mathf.Abs(s.arm - b0.arm)); dRoll = Mathf.Max(dRoll, Mathf.Abs(s.roller)); dFly = Mathf.Max(dFly, s.fly);
                    dTur = Mathf.Max(dTur, Mathf.Abs(s.turret - b0.turret)); feed |= s.feeding; ej |= s.ejecting;
                }
                var s1 = Take(m);
                Press(h, b.Driver, b.Key, false);
                bool changed = dArm > 0.03f || dRoll > 5f || dFly > 5f || dTur > 0.03f || s1.held != b0.held || s1.shots != b0.shots || feed || ej;
                string eff = $"arm Δ{dArm:0.00} roller {dRoll:0} fly {dFly:0} turret Δ{dTur:0.00} held {b0.held}→{s1.held} shots {s1.shots}{(feed ? " feeding" : "")}{(ej ? " ejecting" : "")}";
                sb.AppendLine($"{(changed ? "PASS " : "FAIL ")} {who} {b.Key}  → {b.Desc}   [{eff}]");
                if (changed) pass++; else fail++;
                yield return new WaitForSeconds(0.8f);
            }
            sb.AppendLine($"== 結果:PASS {pass}  FAIL {fail}  SKIP {skip}");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "bindtest.txt"), sb.ToString());
            GameSession.Hal.Stop();
            Application.Quit();
        }
    }
}
