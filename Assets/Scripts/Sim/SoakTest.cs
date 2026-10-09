using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -soaktest [秒數]:長時間自動操作(隨機開車、吸球、射擊),每 15 秒記錄記憶體/球數/FPS,檢查有沒有洩漏或越跑越慢。結果 soaktest.txt
    public class SoakTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var a = System.Environment.GetCommandLineArgs(); int ix = System.Array.IndexOf(a, "-soaktest");
            float total = 300f; if (ix >= 0 && ix + 1 < a.Length) float.TryParse(a[ix + 1], out total);
            var sb = new StringBuilder("t(s), managedMB, workingSetMB, fuelCount, held, shots, score(blue), avgFrameMs\n");
            yield return new WaitForSeconds(10f);   // 等場地模型和球
            var d = GameSession.Drive; var m = GameSession.Mech;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            d.FieldCentric = true;
            var rng = new System.Random(5);
            float t = 0f, nextLog = 0f, dirT = 0f, vx = 0, vy = 0, w = 0;
            float frameAcc = 0f; int frames = 0;
            while (t < total)
            {
                float dt = Time.deltaTime; t += dt; dirT -= dt; frameAcc += dt; frames++;
                if (dirT <= 0f) { dirT = 0.5f + (float)rng.NextDouble() * 1.5f; vx = (float)(rng.NextDouble() * 2 - 1); vy = (float)(rng.NextDouble() * 2 - 1); w = (float)(rng.NextDouble() * 2 - 1) * 0.6f; m.IntakeDown = rng.NextDouble() < 0.5; m.Shooting = rng.NextDouble() < 0.4; if (m.Held < 3) m.Held = 8; }
                d.Drive(vx, vy, w);
                if (t >= nextLog)
                {
                    nextLog += 15f;
                    long managed = System.GC.GetTotalMemory(false) / (1024 * 1024);
                    long ws = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                    sb.AppendLine($"{t:0}, {managed}, {ws}, {FuelManager.All.Count}, {m.Held}, {m.ShotsFired}, {ScoreManager.BlueScore}, {(frames > 0 ? frameAcc / frames * 1000f : 0f):0.0}");
                    frameAcc = 0f; frames = 0;
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "soaktest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
