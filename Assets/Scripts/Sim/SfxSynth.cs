using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // Procedural SFX: no audio assets. One-shots are 44.1 kHz mono; loops are 22.05 kHz mono with integer-cycle
    // partials + a crossfaded tail so they loop without clicks. All clips peak-normalised to 0.89; loudness is set by SfxBus.
    public static class SfxSynth
    {
        public const int SR = 44100, SRL = 22050;
        const float TAU = 6.2831853f;
        const int LoopTail = 2048;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static readonly System.Random rng = new System.Random(20261010);

        static readonly Dictionary<string, float> Lens = new Dictionary<string, float>
        {
            {"ding",0.5f},{"ballin",0.65f},{"thump",0.3f},{"shoot",0.14f},{"ballhit",0.10f},{"bump",0.35f},{"ramp",0.4f},{"pluck",0.12f},{"beep",0.16f},
            {"buzzer",1.6f},{"tick",0.05f},{"uiclick",0.06f},{"stinger_start",1.0f},{"stinger_end",1.3f},{"stinger_win",1.7f},
            {"stinger_lose",1.7f},{"roar",2.4f},{"intake_loop",1f},{"fly_loop",1f},{"crowd_loop",4f},{"music_loop",8f}
        };
        public static IEnumerable<string> Names { get { return Lens.Keys; } }

        static float Nz() { return (float)(rng.NextDouble() * 2.0 - 1.0); }
        static float Atk(float t, float a) { return t >= a ? 1f : t / a; }
        static float Rel(float t, float len, float r) { return t > len - r ? Mathf.Max(0f, (len - t) / r) : 1f; }
        static float Lp(float fc, int sr) { return 1f - Mathf.Exp(-TAU * fc / sr); }
        static float Note(float t, float t0, float f, float decay)
        {
            if (t < t0) return 0f; float u = t - t0;
            return (Mathf.Sin(TAU * f * u) + 0.35f * Mathf.Sin(TAU * 2f * f * u) + 0.12f * Mathf.Sin(TAU * 3f * f * u)) * Mathf.Exp(-u * decay) * Atk(u, 0.004f);
        }

        public static AudioClip Get(string name)
        {
            AudioClip c;
            if (cache.TryGetValue(name, out c) && c != null) return c;
            try
            {
                float len; if (!Lens.TryGetValue(name, out len)) return null;
                bool loop = name.EndsWith("_loop");
                int sr = loop ? SRL : SR;
                int n = Mathf.RoundToInt(len * sr);
                float[] d = Gen(name, n + (loop ? LoopTail : 0), sr);
                if (d == null) return null;
                if (loop) d = Seamless(d, n);
                Normalize(d, 0.89f);
                if (!loop) FadeTail(d, Mathf.Min(sr / 200, n / 4));
                c = AudioClip.Create(name, d.Length, 1, sr, false);
                c.SetData(d, 0);
                cache[name] = c;
                return c;
            }
            catch (Exception e) { Debug.LogWarning("[Sfx] synth '" + name + "' failed: " + e.Message); return null; }
        }

        static float[] Seamless(float[] src, int n)
        {
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < LoopTail) { float a = i / (float)LoopTail; o[i] = src[n + i] * (1f - a) + src[i] * a; }
                else o[i] = src[i];
            }
            return o;
        }
        static void Normalize(float[] d, float target)
        {
            float pk = 1e-5f; for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > pk) pk = a; }
            float k = target / pk; for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
        static void FadeTail(float[] d, int m)
        {
            for (int i = 0; i < m; i++) d[d.Length - 1 - i] *= i / (float)m;
        }

        static float[] Gen(string name, int n, int sr)
        {
            var d = new float[n]; float dt = 1f / sr, ph = 0f, lp = 0f, lp2 = 0f;
            switch (name)
            {
                case "ding":   // bell-ish: fundamental + 2nd + 3rd partials. Pitch it with AudioSource.pitch.
                    for (int i = 0; i < n; i++) { float t = i * dt;
                        d[i] = (0.6f * Mathf.Sin(TAU * 880f * t) + 0.25f * Mathf.Sin(TAU * 1760f * t) * Mathf.Exp(-t * 8f) + 0.15f * Mathf.Sin(TAU * 2640f * t) * Mathf.Exp(-t * 14f)) * Mathf.Exp(-t * 7f) * Atk(t, 0.002f); }
                    break;
                case "ballin":  // 球掉進 HUB:漏斗內滑落的氣流聲 + 三次逐漸變小的泡棉球落地「噗、噗、噗」
                {
                    float a1 = Lp(1400f, sr), a2 = Lp(700f, sr), l2 = 0f;
                    for (int i = 0; i < n; i++) { float t = i * dt; lp += a1 * (Nz() - lp); l2 += a2 * (Nz() - l2);
                        float v = lp * 0.5f * Mathf.Exp(-Mathf.Pow((t - 0.05f) * 14f, 2f));   // 短促的滑落氣流
                        float[] bt = { 0.16f, 0.30f, 0.41f }; float[] ba = { 1f, 0.55f, 0.28f };
                        for (int k = 0; k < 3; k++) { float u = t - bt[k]; if (u > 0f) v += (l2 * 2.0f * Mathf.Exp(-u * 60f) + Mathf.Sin(TAU * (190f - 50f * Mathf.Min(1f, u * 18f)) * u) * 0.8f * Mathf.Exp(-u * 40f)) * ba[k]; }
                        d[i] = v * Atk(t, 0.003f); }
                    break;
                }                case "thump":  // filtered sub sweep + 4 ms click
                    for (int i = 0; i < n; i++) { float t = i * dt; ph += TAU * (45f + 110f * Mathf.Exp(-t * 28f)) * dt;
                        d[i] = (Mathf.Sin(ph) * Mathf.Exp(-t * 9f) + (t < 0.004f ? Nz() * 0.5f * (1f - t / 0.004f) : 0f)) * Atk(t, 0.001f); }
                    break;
                case "shoot":  // crack (low-passed noise) + punchy pitch-dropping body
                {
                    float a = Lp(1800f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; ph += TAU * (60f + 220f * Mathf.Exp(-t * 45f)) * dt; lp += a * (Nz() - lp);
                        d[i] = (lp * 2.2f * Mathf.Exp(-t * 38f) + Mathf.Sin(ph) * 0.9f * Mathf.Exp(-t * 22f)) * Atk(t, 0.001f); }
                    break;
                }
                case "ballhit": // FUEL 是 15cm 高密度泡棉球:悶悶的「噗」——低通噪音 + 低頻短身(沒有亮的「叩」),衰減很快
                {
                    float a = Lp(900f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; lp += a * (Nz() - lp);
                        d[i] = (lp * 2.2f * Mathf.Exp(-t * 55f) + Mathf.Sin(TAU * (230f - 70f * Mathf.Min(1f, t * 20f)) * t) * 0.7f * Mathf.Exp(-t * 48f)) * Atk(t, 0.0012f); }
                    break;
                }
                case "bump":   // 機器人互撞/撞牆:保險桿布面的悶響(低頻 50~120Hz 的「咚」 + 低中頻布料拍擊),不是金屬聲
                {
                    float a = Lp(380f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; ph += TAU * (40f + 80f * Mathf.Exp(-t * 22f)) * dt; lp += a * (Nz() - lp);
                        d[i] = (Mathf.Sin(ph) * Mathf.Exp(-t * 9f) + lp * 1.6f * Mathf.Exp(-t * 16f)) * Atk(t, 0.0015f); }
                    break;
                }
                case "ramp":   // 過 BUMP:前輪上坡「咚」+ 後輪跟著「咚」(兩段低頻 thud,間隔約 90ms),帶一點底盤共鳴
                {
                    float a = Lp(300f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; lp += a * (Nz() - lp);
                        float e1 = Mathf.Exp(-t * 12f), t2 = Mathf.Max(0f, t - 0.09f), e2 = t > 0.09f ? 0.8f * Mathf.Exp(-t2 * 13f) : 0f;
                        d[i] = (Mathf.Sin(TAU * (62f + 40f * Mathf.Exp(-t * 25f)) * t) * e1 + Mathf.Sin(TAU * (52f + 30f * Mathf.Exp(-t2 * 25f)) * t2) * e2 + lp * 1.1f * (Mathf.Exp(-t * 20f) + 0.7f * (t > 0.09f ? Mathf.Exp(-t2 * 22f) : 0f))) * Atk(t, 0.002f); }
                    break;
                }
                case "pluck":  // rising blip (collect)
                    for (int i = 0; i < n; i++) { float t = i * dt; ph += TAU * (450f + 650f * (1f - Mathf.Exp(-t * 60f))) * dt;
                        d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * 28f) * Atk(t, 0.002f); }
                    break;
                case "beep":   // FRC-style countdown tone, soft-square
                {
                    float len = n * dt;
                    for (int i = 0; i < n; i++) { float t = i * dt;
                        d[i] = (Mathf.Sin(TAU * 1000f * t) + 0.33f * Mathf.Sin(TAU * 3000f * t) + 0.2f * Mathf.Sin(TAU * 5000f * t)) * Atk(t, 0.004f) * Rel(t, len, 0.02f); }
                    break;
                }
                case "buzzer": // 真實場地結束蜂鳴器(實拍 05:12):刺耳的方波/鋸齒,中心約 1.8~2.5kHz,約 1.5 秒,突然收掉
                {
                    float len = n * dt;
                    for (int i = 0; i < n; i++) { float t = i * dt; float s = 0f;
                        for (int k = 1; k <= 7; k += 2) s += Mathf.Sin(TAU * 1500f * k * t) / k;
                        s += 0.35f * Mathf.Sin(TAU * 2250f * t);
                        d[i] = s * (0.9f + 0.1f * Mathf.Sin(TAU * 90f * t)) * Atk(t, 0.012f) * Rel(t, len, 0.03f); }
                    break;
                }
                case "tick":
                    for (int i = 0; i < n; i++) { float t = i * dt; d[i] = Mathf.Sin(TAU * 2200f * t) * Mathf.Exp(-t * 160f) * Atk(t, 0.0005f); }
                    break;
                case "uiclick":
                {
                    float a = Lp(4000f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; lp += a * (Nz() - lp);
                        d[i] = (Mathf.Sin(TAU * 1400f * t) * Mathf.Exp(-t * 110f) + lp * 0.25f * Mathf.Exp(-t * 200f)) * Atk(t, 0.0005f); }
                    break;
                }
                case "stinger_start": // 真實開賽「charge」號角(實拍 05:32):約 400Hz 上升到 1kHz 的合成上行約 0.8 秒,再接一聲短蜂鳴
                {
                    float len = n * dt; float a = Lp(2600f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; float s = 0f;
                        if (t < 0.8f) { float f = 400f + 600f * (t / 0.8f) * (t / 0.8f); ph += TAU * f * dt; s = Mathf.Sin(ph) + 0.5f * Mathf.Sin(2f * ph) + 0.25f * Mathf.Sin(3f * ph); s *= Atk(t, 0.02f); }
                        else if (t < 1.0f) { s = (Mathf.Sin(TAU * 1500f * t) + 0.33f * Mathf.Sin(TAU * 4500f * t)) * Mathf.Exp(-(t - 0.8f) * 6f); }
                        d[i] = s * Rel(t, len, 0.05f); }
                    break;
                }
                case "stinger_end":   // tension riser: noise + saw sweep, cut at 0.9 s
                {
                    float a = Lp(3000f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; lp += a * (Nz() - lp);
                        float e = t < 0.9f ? (t / 0.9f) * (t / 0.9f) : Mathf.Exp(-(t - 0.9f) * 14f);
                        ph += TAU * (110f + 160f * (t / 1.3f)) * dt;
                        d[i] = (Mathf.Sin(ph) + 0.5f * Mathf.Sin(2f * ph) + 0.3f * Mathf.Sin(3f * ph) + lp * 1.2f) * e; }
                    break;
                }
                case "stinger_win":
                    for (int i = 0; i < n; i++) { float t = i * dt;
                        d[i] = Note(t, 0f, 523.25f, 3.2f) + Note(t, 0.12f, 659.25f, 3.2f) + Note(t, 0.24f, 783.99f, 3.2f) + 1.2f * Note(t, 0.36f, 1046.5f, 2.2f); }
                    break;
                case "stinger_lose":
                    for (int i = 0; i < n; i++) { float t = i * dt;
                        d[i] = Note(t, 0f, 440f, 2.6f) + Note(t, 0.28f, 349.23f, 2.6f) + 1.1f * Note(t, 0.56f, 293.66f, 1.8f); }
                    break;
                case "roar":   // crowd swell: band-passed noise with raised-sine envelope
                {
                    float a1 = Lp(2500f, sr), a2 = Lp(250f, sr); float len = n * dt;
                    for (int i = 0; i < n; i++) { float t = i * dt; float x = Nz(); lp += a1 * (x - lp); lp2 += a2 * (x - lp2);
                        float s = Mathf.Sin(Mathf.PI * t / len); d[i] = (lp - lp2) * 4f * s * s * (0.7f + 0.3f * Mathf.Sin(TAU * 5f * t)); }
                    break;
                }
                case "intake_loop": // roller whir: 80 Hz harmonics + 8 Hz AM + grit (len 1 s => all partials integer-cycle)
                {
                    float a = Lp(2500f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; float s = 0f;
                        for (int k = 1; k <= 6; k++) s += Mathf.Sin(TAU * 80f * k * t + k * 1.3f) / k;
                        lp += a * (Nz() - lp);
                        d[i] = (s * (1f + 0.25f * Mathf.Sin(TAU * 8f * t)) + lp * 0.5f); }
                    break;
                }
                case "fly_loop": // flywheel whine: 140 Hz saw-ish + 8th-harmonic whine (1120 Hz). Pitch it 0.4..2.5 with rps
                {
                    float a = Lp(4000f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; float s = 0f;
                        for (int k = 1; k <= 8; k++) s += Mathf.Sin(TAU * 140f * k * t + k * 0.7f) / k;
                        s += 0.5f * Mathf.Sin(TAU * 1120f * t) * (0.8f + 0.2f * Mathf.Sin(TAU * 3f * t));
                        lp += a * (Nz() - lp); d[i] = s + lp * 0.3f; }
                    break;
                }
                case "crowd_loop": // 4 s murmur: band-passed noise, slow integer-cycle modulation
                {
                    float a1 = Lp(2200f, sr), a2 = Lp(250f, sr);
                    for (int i = 0; i < n; i++) { float t = i * dt; float x = Nz(); lp += a1 * (x - lp); lp2 += a2 * (x - lp2);
                        float m = 0.65f + 0.2f * Mathf.Sin(TAU * 0.5f * t + 1f) + 0.15f * Mathf.Sin(TAU * 1.25f * t + 2f);
                        d[i] = (lp - lp2) * m * 3.5f; }
                    break;
                }
                case "music_loop": // 8 s, 120 BPM: Am add-chord pad (detuned pairs, 0.125 Hz beating) + soft kick + hats
                {
                    float[] fs = { 110f, 165f, 220f, 262.5f, 330f };
                    for (int i = 0; i < n; i++) { float t = i * dt; float pad = 0f;
                        for (int k = 0; k < fs.Length; k++) pad += Mathf.Sin(TAU * fs[k] * t) + 0.6f * Mathf.Sin(TAU * (fs[k] + 0.125f) * t);
                        pad *= 0.12f * (0.75f + 0.25f * Mathf.Sin(TAU * 0.25f * t));
                        float tau = t % 0.5f;
                        float kick = Mathf.Sin(TAU * (50f * tau + (80f / 30f) * (1f - Mathf.Exp(-30f * tau)))) * Mathf.Exp(-tau * 10f) * 0.55f;
                        float tau2 = (t + 0.25f) % 0.5f;
                        float hat = Nz() * Mathf.Exp(-tau2 * 140f) * 0.06f;
                        d[i] = pad + kick + hat; }
                    break;
                }
            }
            return d;
        }
    }
}