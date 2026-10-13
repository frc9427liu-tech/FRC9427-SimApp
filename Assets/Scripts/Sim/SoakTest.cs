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
            var mem = new System.Collections.Generic.List<long>(); var fuel = new System.Collections.Generic.List<int>(); var ft = new System.Collections.Generic.List<float>(); var unityMem = new System.Collections.Generic.List<long>();
            while (t < total)
            {
                float dt = Time.deltaTime; t += dt; dirT -= dt; frameAcc += dt; frames++;
                if (dirT <= 0f) { dirT = 0.5f + (float)rng.NextDouble() * 1.5f; vx = (float)(rng.NextDouble() * 2 - 1); vy = (float)(rng.NextDouble() * 2 - 1); w = (float)(rng.NextDouble() * 2 - 1) * 0.6f; m.IntakeDown = rng.NextDouble() < 0.5; m.Shooting = rng.NextDouble() < 0.4; if (m.Held < 3) m.Held = 8; }
                d.Drive(vx, vy, w);
                if (t >= nextLog)
                {
                    nextLog += 15f;
                    long managed = System.GC.GetTotalMemory(false) / (1024 * 1024);
                    long ws = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);   // Unity 配置的總記憶體(Process.WorkingSet64 在 Mono 下回 0)
                    float fms = frames > 0 ? frameAcc / frames * 1000f : 0f;
                    sb.AppendLine($"{t:0}, {managed}, {ws}, {FuelManager.All.Count}, {m.Held}, {m.ShotsFired}, {ScoreManager.BlueScore}, {fms:0.0}");
                    mem.Add(managed); fuel.Add(FuelManager.All.Count); ft.Add(fms); unityMem.Add(ws);
                    frameAcc = 0f; frames = 0;
                }
                yield return null;
            }
            // 判定:托管記憶體成長 <= 20MB、球數 <= 1550(上限 1400+緩衝)、後 1/3 平均幀時間 <= 前 1/3 × 1.25(+0.5ms 容差)、Unity 記憶體量得到(>0)
            if (mem.Count >= 3)
            {
                int n3 = Mathf.Max(1, mem.Count / 3); float f0 = 0, f1 = 0; for (int i = 0; i < n3; i++) { f0 += ft[i]; f1 += ft[ft.Count - 1 - i]; } f0 /= n3; f1 /= n3;
                long growth = mem[mem.Count - 1] - mem[0]; int fmax = 0; foreach (var c in fuel) fmax = Mathf.Max(fmax, c);
                bool ok = growth <= 20 && fmax <= 1550 && f1 <= f0 * 1.25f + 0.5f && unityMem[unityMem.Count - 1] > 0;
                sb.AppendLine($"managedGrowth={growth}MB (<=20)  fuelMax={fmax} (<=1550)  frameMs first/last third={f0:0.0}/{f1:0.0} (last<=first*1.25+0.5)  unityMem={unityMem[unityMem.Count - 1]}MB (>0)");
                sb.AppendLine(ok ? "PASS" : "FAIL");
            }
            else sb.AppendLine("FAIL (samples < 3;請用 -soaktest 60 以上)");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "soaktest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
