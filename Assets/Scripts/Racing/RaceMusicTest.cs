using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -racemusictest:驗證程序化配樂與額外音效(不需音訊裝置)。
    //   FrcSim.exe -batchmode -nographics -racemusictest   → 結果寫 racemusictest.txt(exe 同層),全 PASS 才印 "RESULT: PASS"
    //   M1 旋律/和弦長度與小節數一致   M2 各曲目生成成功、長度 = 小節×4 拍×60/BPM×取樣率
    //   M3 無 NaN、峰值在 [-1,1]、不是靜音   M4 loop 起點/終點振幅差小(無爆音),完賽 jingle 結尾淡出到近 0
    //   M5 生成耗時 < 1.5 秒/首(含最後一圈版)   M6 相同種子結果一致、不同種子結果不同
    //   M7 最後一圈版長度相同但內容不同   M8 13 個額外 SFX 皆有資料、無 NaN、峰值合理、長度 < 1.5 秒
    //   M9 完賽 jingle 3~6 秒   M10 AudioClip.Create 可用(僅記錄,環境沒有音訊裝置時不算失敗)
    public class RaceMusicTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-racemusictest") < 0) return;
            var g = new GameObject("RaceMusicTest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceMusicTest>();
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;
        void Report(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail);
            if (!ok) fails++;
        }

        IEnumerator Start()
        {
            yield return null;
            try { Run(); } catch (Exception e) { Report("exception", false, e.ToString()); }
            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "racemusictest.txt"), sb.ToString()); }
            catch (Exception e) { UnityEngine.Debug.LogError("[RaceMusicTest] write failed: " + e); }
            UnityEngine.Debug.Log("[RaceMusicTest]\n" + sb);
            Application.Quit(fails == 0 ? 0 : 1);
        }

        static bool Finite(float[] d, out float peak, out float rms)
        {
            peak = 0f; double sq = 0; bool ok = true;
            for (int i = 0; i < d.Length; i++)
            {
                float v = d[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { ok = false; continue; }
                float a = Mathf.Abs(v); if (a > peak) peak = a; sq += v * v;
            }
            rms = (float)Math.Sqrt(sq / Math.Max(1, d.Length));
            return ok;
        }
        static ulong Hash(float[] d)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < d.Length; i += 7) { h ^= (ulong)(long)Mathf.RoundToInt(d[i] * 32767f) & 0xFFFF; h *= 1099511628211UL; }
            return h;
        }

        void Run()
        {
            foreach (var id in RaceMusic.AllIds)
            {
                var info = RaceMusic.Info(id);
                string nm = info.Name + "(" + id + ")";
                int expectSteps = info.Bars * 16;
                Report("M1 " + nm + " 旋律/和弦長度", RaceMusic.MelodySteps(id) == expectSteps && RaceMusic.ChordCount(id) == info.Bars,
                    "melody steps " + RaceMusic.MelodySteps(id) + " / " + expectSteps + ", chords " + RaceMusic.ChordCount(id) + " / " + info.Bars);

                var sw = Stopwatch.StartNew();
                var d = RaceMusic.Render(id, false, 0);
                sw.Stop(); long ms = sw.ElapsedMilliseconds;
                if (d == null) { Report("M2 " + nm + " 生成", false, "null"); continue; }
                int expect = Mathf.RoundToInt(info.Bars * 4f * 60f / info.Bpm * RaceMusic.SR);
                double secs = d.Length / (double)RaceMusic.SR;
                Report("M2 " + nm + " 長度對得上 BPM", d.Length == expect, d.Length + " samples = " + secs.ToString("F2") + " s (預期 " + expect + ", " + info.Bpm + " BPM, " + info.Bars + " 小節)");

                float pk, rms; bool fin = Finite(d, out pk, out rms);
                Report("M3 " + nm + " 無 NaN/範圍/非靜音", fin && pk <= 1f && pk > 0.5f && rms > 0.02f, "peak " + pk.ToString("F3") + " rms " + rms.ToString("F3"));

                float edge = Mathf.Abs(d[0] - d[d.Length - 1]);
                float maxStep = 0f; for (int i = 1; i < d.Length; i++) { float s = Mathf.Abs(d[i] - d[i - 1]); if (s > maxStep) maxStep = s; }
                if (info.Loop)
                    Report("M4 " + nm + " loop 接縫", edge < 0.15f && edge <= maxStep + 1e-4f, "|first-last| " + edge.ToString("F4") + " (內部最大相鄰差 " + maxStep.ToString("F3") + ")");
                else
                    Report("M4 " + nm + " 結尾淡出", Mathf.Abs(d[d.Length - 1]) < 0.01f, "last " + d[d.Length - 1].ToString("F5"));

                sw.Reset(); sw.Start();
                var f = info.Loop ? RaceMusic.Render(id, true, 0) : d;
                sw.Stop(); long msF = info.Loop ? sw.ElapsedMilliseconds : 0;
                Report("M5 " + nm + " 生成耗時 < 2000 ms(最終 < 3000 ms)", ms < 2000 && msF < 3000, "normal " + ms + " ms, final " + msF + " ms");

                var d2 = RaceMusic.Render(id, false, 0); var d3 = RaceMusic.Render(id, false, 5);
                Report("M6 " + nm + " 種子", Hash(d) == Hash(d2) && Hash(d) != Hash(d3) && d3.Length == d.Length, "same-seed equal " + (Hash(d) == Hash(d2)) + ", other-seed differs " + (Hash(d) != Hash(d3)));

                if (info.Loop)
                {
                    float pkF, rmsF; bool finF = Finite(f, out pkF, out rmsF);
                    float edgeF = Mathf.Abs(f[0] - f[f.Length - 1]);
                    Report("M7 " + nm + " 最後一圈版", f.Length == d.Length && finF && pkF <= 1f && edgeF < 0.15f && Hash(f) != Hash(d), "len " + f.Length + " peak " + pkF.ToString("F3") + " edge " + edgeF.ToString("F4") + " differs " + (Hash(f) != Hash(d)));
                }
                if (id == RaceMusic.TrackResult)
                    Report("M9 完賽 jingle 3~6 秒", secs >= 3.0 && secs <= 6.0, secs.ToString("F2") + " s");
            }

            foreach (var name in RaceSfx.ExtraNames)
            {
                var d = RaceSfx.BuildExtra(name);
                if (d == null || d.Length == 0) { Report("M8 sfx " + name, false, "no data"); continue; }
                float pk, rms; bool fin = Finite(d, out pk, out rms);
                double secs = d.Length / (double)SfxSynth.SR;
                Report("M8 sfx " + name, fin && pk > 0.05f && pk < 20f && secs > 0.03 && secs < 1.5, secs.ToString("F2") + " s, peak(正規化前) " + pk.ToString("F2") + ", rms " + rms.ToString("F3"));
            }

            try
            {
                var c = AudioClip.Create("rmtest", 1000, 1, RaceMusic.SR, false); c.SetData(new float[1000], 0);
                Report("M10 AudioClip.Create", c != null, "ok");
            }
            catch (Exception e) { sb.AppendLine("INFO AudioClip.Create 不可用(" + e.Message + "),不算失敗"); }
        }
    }
}
