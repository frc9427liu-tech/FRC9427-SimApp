using System;
using UnityEngine;
using UnityEngine.Profiling;
namespace FrcSim
{
    public class PerfOverlay : MonoBehaviour
    {
        const int N = 360;                                   // graph window (5 s @72 Hz)
        readonly float[] ring = new float[N], scratch = new float[N];
        readonly int[] hist = new int[401];                  // whole-session histogram, 0.25 ms bins
        int head, filled; long frames; double sumMs; float maxMs; int hitches;
        bool show, perfLog; float nextStats; string statLine = "";
        double allocAcc; long lastMono; int lastGc; float accT;
        Material mat; GUIStyle st; static readonly FrameTiming[] ft = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var g = new GameObject("PerfOverlay"); DontDestroyOnLoad(g); g.AddComponent<PerfOverlay>(); }

        void Awake()
        {
            perfLog = Array.IndexOf(Environment.GetCommandLineArgs(), "-perflog") >= 0;
            Application.quitting += Dump;
            lastMono = Profiler.GetMonoUsedSizeLong(); lastGc = GC.CollectionCount(0);
        }
        void OnDestroy() { Application.quitting -= Dump; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) show = !show;
            if (Time.frameCount < 120) return;                                     // skip start-up
            float dt = Time.unscaledDeltaTime * 1000f;
            ring[head] = dt; head = (head + 1) % N; if (filled < N) filled++;
            hist[Mathf.Min(400, (int)(dt * 4f))]++; frames++; sumMs += dt; if (dt > maxMs) maxMs = dt;
            float budget = 1000f / Mathf.Max(1, SettingsStore.EffectiveFps);
            if (dt > budget * 1.5f) hitches++;
            long m = Profiler.GetMonoUsedSizeLong(); if (m > lastMono) allocAcc += m - lastMono; lastMono = m;   // sum of heap growth = alloc estimate
            accT += Time.unscaledDeltaTime;
            if (Time.unscaledTime >= nextStats) { nextStats = Time.unscaledTime + 0.5f; Recompute(); }
        }

        void Recompute()
        {
            int n = filled; if (n < 10) return;
            Array.Copy(ring, scratch, n); Array.Sort(scratch, 0, n);
            double sum = 0; for (int i = 0; i < n; i++) sum += scratch[i];
            float avg = (float)(sum / n), p99 = scratch[Mathf.Min(n - 1, (int)(n * 0.99f))];
            int w = Mathf.Max(1, n / 100); double worst = 0; for (int i = n - w; i < n; i++) worst += scratch[i];
            float low1 = 1000f / (float)(worst / w);                                // 1% low = mean fps of the slowest 1% frames
            double cpu = 0, gpu = 0;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { cpu = ft[0].cpuFrameTime; gpu = ft[0].gpuFrameTime; }
            int gc = GC.CollectionCount(0);
            statLine = string.Format("{0:0.0} ms avg ({1:0} fps)  1% low {2:0} fps  p99 {3:0.0} ms  worst {4:0.0} ms\nCPU {5:0.0} / GPU {6:0.0} ms   alloc {7:0} KB/s   GC {8:0.0}/s   hitches {9}",
                avg, 1000f / avg, low1, p99, scratch[n - 1], cpu, gpu, allocAcc / 1024.0 / accT, (gc - lastGc) / accT, hitches);
            allocAcc = 0; lastGc = gc; accT = 0f;
        }

        void OnGUI()
        {
            if (!show || Event.current.type != EventType.Repaint) return;
            if (mat == null) { var sh = Shader.Find("Sprites/Default"); if (sh == null) return; mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave }; }
            if (st == null) { st = new GUIStyle(GUI.skin.label) { fontSize = 13 }; st.normal.textColor = Color.white; }
            GUI.depth = -200;
            const float W = 360f, H = 90f; float x0 = Screen.width - W - 14f, y0 = Screen.height - H - 16f;
            float budget = 1000f / Mathf.Max(1, SettingsStore.EffectiveFps), top = Mathf.Max(budget * 3f, 20f);
            GL.PushMatrix(); mat.SetPass(0); GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0); GL.Begin(GL.QUADS);
            Quad(x0 - 6, y0 - 6, W + 12, H + 12, 0, 0, 0, 0.55f);
            float bw = W / N;
            for (int i = 0; i < filled; i++)
            {
                float ms = ring[(head - filled + i + N) % N], h = Mathf.Min(1f, ms / top) * H;
                if (ms <= budget * 1.1f) Quad(x0 + i * bw, y0 + H - h, Mathf.Max(1f, bw), h, 0.30f, 0.90f, 0.40f, 0.95f);
                else if (ms <= budget * 2.1f) Quad(x0 + i * bw, y0 + H - h, Mathf.Max(1f, bw), h, 1f, 0.80f, 0.20f, 0.95f);
                else Quad(x0 + i * bw, y0 + H - h, Mathf.Max(1f, bw), h, 1f, 0.30f, 0.25f, 0.95f);
            }
            Quad(x0, y0 + H - budget / top * H, W, 1f, 1, 1, 1, 0.85f);                // 1 frame budget
            Quad(x0, y0 + H - 2f * budget / top * H, W, 1f, 1, 1, 1, 0.40f);          // 2 frame budgets
            GL.End(); GL.PopMatrix();
            GUI.Label(new Rect(x0 - 6, y0 - 50, W + 40, 44), statLine, st);
        }
        static void Quad(float x, float y, float w, float h, float r, float g, float b, float a)
        { GL.Color(new Color(r * a, g * a, b * a, a)); GL.Vertex3(x, y, 0); GL.Vertex3(x + w, y, 0); GL.Vertex3(x + w, y + h, 0); GL.Vertex3(x, y + h, 0); }   // Sprites/Default = premultiplied

        void Dump()
        {
            if (!perfLog || frames < 10) return;
            long total = 0; for (int i = 0; i < hist.Length; i++) total += hist[i];
            Func<double, double> pct = p => { long tgt = (long)(total * p), c = 0; for (int i = 0; i < hist.Length; i++) { c += hist[i]; if (c >= tgt) return (i + 1) * 0.25; } return 100; };
            long need = Math.Max(1, total / 100), got = 0; double s = 0;
            for (int i = hist.Length - 1; i >= 0 && got < need; i--) { long take = Math.Min(hist[i], need - got); s += take * (i + 0.5) * 0.25; got += take; }
            Debug.Log($"[PERF] frames={total} avg={sumMs / frames:0.00}ms p50={pct(.5):0.00} p95={pct(.95):0.00} p99={pct(.99):0.00} p99.9={pct(.999):0.00} max={maxMs:0.0}ms low1%={1000.0 / (s / got):0}fps hitches(>1.5x budget)={hitches} gc0={GC.CollectionCount(0)} mono={Profiler.GetMonoUsedSizeLong() / 1048576}MB budget={1000f / Mathf.Max(1, SettingsStore.EffectiveFps):0.0}ms");
        }
    }
}