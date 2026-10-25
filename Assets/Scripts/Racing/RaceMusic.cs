using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽車配樂(程式合成,沒有外部素材)。風格:動物森友會的溫暖木琴/鋼琴/撥弦/口哨 + 瑪利歐賽車的明快節奏。
    // 作法:每首曲子是「和弦進行 + 旋律字串 + 貝斯/鼓/伴奏型態」,用簡單加法合成把每個音符「寫」進一塊環狀緩衝區
    //       (音符尾巴會繞回開頭),所以輸出的 loop 天生無縫,不需要交叉淡化。22.05 kHz 單聲道,16 小節,每首約 0.2~0.5 秒生成。
    // 播放走 SfxBus 的 Music 群組(自動套用 JuiceSettings.Master × Music、暫停時降低、可被 SfxBus.Duck 壓低)。
    // 曲目編號:0 Island GP / 1 Canyon Run / 2 Coast Loop(對應 DefaultTracks)、TrackMenu=100、TrackResult=101(3 小節勝利 jingle,不 loop)。
    public static class RaceMusic
    {
        public const int SR = 22050;
        public const int TrackMenu = 100, TrackResult = 101;
        public const float FinalSpeed = 1.08f;     // 最後一圈:速度 +8%、音高約 +1.3 個半音(用 AudioSource.pitch),再加疊一層閃爍琶音
        public const float DefaultVolume = 0.35f;
        const float TAU = 6.2831853f;

        public static readonly int[] AllIds = { 0, 1, 2, TrackMenu, TrackResult };
        public struct SongInfo { public string Name; public int Bpm, Bars; public bool Loop; }

        // ------------------------------------------------------------------ 播放介面
        static float volume = DefaultVolume;
        public static float Volume { get { return volume; } }
        public static int Seed;                       // 想要不同變化時改這個(改完下次 Play 才生效,已快取的曲子要 ClearCache)
        static string curKey; static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        public static void Play(int trackId) { Start(trackId, false); }
        public static void PlayFinal(int trackId) { Start(trackId, true); }
        public static void PlayMenu() { Start(TrackMenu, false); }
        public static void PlayResult()
        {
            Stop();
            if (!SfxBus.Enabled) return;
            var key = Key(TrackResult, false);
            if (GetClip(TrackResult, false) == null) return;
            SfxBus.Play(key, Mathf.Clamp01(volume * 2.2f), 1f, 0f, SfxGroup.Music);
        }
        public static void Stop()
        {
            if (curKey != null && SfxBus.Enabled) SfxBus.SetLoop(curKey, curKey, 0f, 1f, SfxGroup.Music);
            curKey = null;
        }
        /// v = 0..1,乘上 JuiceSettings.Master × Music 之後才是實際音量。預設 0.35。
        public static void SetVolume(float v)
        {
            volume = Mathf.Clamp01(v);
            if (curKey != null && SfxBus.Enabled) SfxBus.SetLoop(curKey, curKey, volume, curFinal ? FinalSpeed : 1f, SfxGroup.Music);
        }
        /// 使用道具(閃電/炸彈等大音效)時把音樂壓低一下;level 0.5 約壓一半,hold 秒後慢慢恢復。
        public static void Duck(float level = 0.5f, float hold = 0.8f) { SfxBus.Duck(level, hold); }
        public static string CurrentKey { get { return curKey; } }
        public static void ClearCache() { clips.Clear(); }

        static bool curFinal;
        static void Start(int id, bool fin)
        {
            if (!SfxBus.Enabled) return;
            var key = Key(id, fin);
            if (GetClip(id, fin) == null) return;
            if (curKey != null && curKey != key) SfxBus.SetLoop(curKey, curKey, 0f, 1f, SfxGroup.Music);
            curKey = key; curFinal = fin;
            SfxBus.SetLoop(key, key, volume, fin ? FinalSpeed : 1f, SfxGroup.Music);
        }
        static string Key(int id, bool fin) { return "rm_" + id + (fin ? "_f" : "") + (Seed != 0 ? "_s" + Seed : ""); }

        static AudioClip GetClip(int id, bool fin)
        {
            var key = Key(id, fin);
            AudioClip c;
            if (clips.TryGetValue(key, out c) && c != null) return c;
            try
            {
                var d = Render(id, fin, Seed);
                if (d == null) return null;
                c = AudioClip.Create(key, d.Length, 1, SR, false); c.SetData(d, 0);
                clips[key] = c; SfxSynth.Register(key, c);
                return c;
            }
            catch (Exception e) { Debug.LogWarning("[RaceMusic] synth " + key + " failed: " + e.Message); return null; }
        }

        /// 在選單/載入畫面呼叫:每幀合成一首(含最後一圈版本),之後 Play 不會卡。可重複呼叫。
        public static void Prewarm()
        {
            if (!SfxBus.Enabled || runner != null) return;
            var g = new GameObject("RaceMusicPrewarm"); UnityEngine.Object.DontDestroyOnLoad(g);
            runner = g.AddComponent<PrewarmRunner>();
        }
        static PrewarmRunner runner;
        sealed class PrewarmRunner : MonoBehaviour
        {
            IEnumerator Start()
            {
                foreach (var id in AllIds)
                {
                    GetClip(id, false); yield return null;
                    if (id <= 2) { GetClip(id, true); yield return null; }
                }
                runner = null; Destroy(gameObject);
            }
        }

        // ------------------------------------------------------------------ 曲目資料
        sealed class Song
        {
            public string name; public int bpm, bars, root, bassBase, style, seed; public bool loop;
            public int[] scale; public float swing;
            public string chords, melody;
        }
        struct Chord { public int r; public int[] t; }
        struct Note { public int step, len, midi; }

        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] PhrygDom = { 0, 1, 4, 5, 7, 8, 10 };

        static string Abab(string a, string b, string b2) { return a + " " + b + " " + a + " " + b2; }

        static Song GetSong(int id)
        {
            switch (id)
            {
                case 0: // Island GP:C 大調 120 BPM,木琴 + 口哨主旋律,動森式離拍鋼琴、彈跳貝斯
                    return new Song
                    {
                        name = "Island GP", bpm = 120, bars = 16, root = 60, bassBase = 36, style = 0, seed = 9427, loop = true, scale = Major, swing = 0.18f,
                        chords = "0:M 7:M 9:m 5:M  5:M 7:M 4:m 9:m  0:M 7:M 9:m 5:M  5:M 2:m 7:M 7:7",
                        melody = Abab(
                            "5:2 3:2 5:2 1':2 3':4 r:2 1':2 | 2':2 7:2 5:2 7:2 2':4 r:4 | 1':2 6:2 3':2 1':2 6:4 r:2 5:2 | 4:2 6:2 1':2 6:2 4:4 5:4",
                            "6:4 4:2 6:2 1':4 6:4 | 7:4 5:2 7:2 2':4 7:4 | 3':4 7:2 5:2 3:4 5:4 | 6:2 1':2 3':2 5':2 3':4 r:4",
                            "6:4 4:2 6:2 1':4 6:4 | 6:4 4:2 2:2 4:4 6:4 | 7:4 5:2 7:2 2':4 r:4 | 2':2 7:2 5:2 2:2 5:8")
                    };
                case 1: // Canyon Run:D 弗里吉安屬音階(沙漠味)135 BPM,撥弦主旋律、推進感 8 分音符貝斯
                    return new Song
                    {
                        name = "Canyon Run", bpm = 135, bars = 16, root = 62, bassBase = 38, style = 1, seed = 4711, loop = true, scale = PhrygDom, swing = 0f,
                        chords = "0:M 1:M 0:M 10:m  5:m 0:M 5:m 7:M  0:M 1:M 0:M 10:m  5:m 1:M 0:M 7:M",
                        melody = Abab(
                            "1:3 2:1 3:2 2:2 1:4 r:2 5,:2 | 2:2 4:2 6:4 4:2 2:2 r:4 | 3:2 5:2 1':4 7:2 5:2 3:2 r:2 | 7:4 6:2 5:2 3:4 5:4",
                            "4:4 6:2 4:2 1':4 6:4 | 3':2 1':2 5:4 3:2 5:2 1':4 | 4':4 1':2 6:2 4:4 6:4 | 5:4 3:2 2:2 5:4 r:4",
                            "4:4 6:2 4:2 1':4 6:4 | 6:4 4:2 2:2 4:4 6:4 | 3:2 5:2 1':2 3':2 1':4 5:4 | 5:4 3:2 2:2 5:4 r:4")
                    };
                case 2: // Coast Loop:G 大調 110 BPM,口哨主旋律、柔和 pad、琶音吉他、微搖擺
                    return new Song
                    {
                        name = "Coast Loop", bpm = 110, bars = 16, root = 55, bassBase = 43, style = 2, seed = 2468, loop = true, scale = Major, swing = 0.3f,
                        chords = "0:M7 9:m7 5:M7 7:7  5:M7 7:M 4:m7 9:m7  0:M7 9:m7 5:M7 7:7  5:M7 2:m7 7:7 0:M7",
                        melody = Abab(
                            "5:4 3:2 5:2 1':6 r:2 | 6:4 5:2 3:2 6:4 5:4 | 1':4 4:2 5:2 3:4 4:4 | 5:3 7:1 2':4 7:4 5:4",
                            "3':4 1':2 3:2 4:8 | 5:4 7:2 2':2 7:4 5:4 | 3:4 5:2 7:2 3':4 2':4 | 6:2 5:2 3:2 5:2 6:8",
                            "3':4 1':2 3:2 4:8 | 4:4 6:2 4:2 2:4 4:4 | 2':4 7:4 5:4 7:4 | 1':8 5:4 r:4")
                    };
                case TrackMenu: // 選單:F 大調 84 BPM,悠閒小鎮(動森味):馬林巴 + 鋼琴分解和弦
                    return new Song
                    {
                        name = "Menu", bpm = 84, bars = 16, root = 53, bassBase = 41, style = 3, seed = 1103, loop = true, scale = Major, swing = 0.25f,
                        chords = "0:M7 4:m7 5:M7 7:7  9:m7 2:m7 7:7 0:M7  0:M7 4:m7 5:M7 7:7  9:m7 5:M7 2:m7 7:7",
                        melody = Abab(
                            "r:4 3:2 5:2 1':6 5:2 | 3':4 5:4 3:4 2:4 | 4:4 6:2 1':2 6:4 4:4 | 5:4 7:2 5:2 2:8",
                            "6:4 1':2 3':2 1':4 6:4 | 2':4 1':2 6:2 4:4 2:4 | 5:4 7:2 5:2 4:8 | 3:2 5:2 1':4 r:8",
                            "6:4 1':2 3':2 1':4 6:4 | 4:4 6:2 1':2 6:4 4:4 | 2:4 4:2 6:2 4:4 2:4 | 7:4 5:4 2:4 5:4")
                    };
                case TrackResult: // 完賽 jingle:C 大調 138 BPM,3 小節 ≈ 5.2 秒,不 loop
                    return new Song
                    {
                        name = "Result", bpm = 138, bars = 3, root = 60, bassBase = 36, style = 4, seed = 777, loop = false, scale = Major, swing = 0f,
                        chords = "0:M 7:7 0:M",
                        melody = "1:2 3:2 5:2 1':2 3':4 1':2 3':2 | 2':2 7:2 5:2 7:2 2':4 4':2 2':2 | 5:2 3':2 1':12"
                    };
            }
            return null;
        }

        public static SongInfo Info(int id)
        {
            var s = GetSong(id);
            return s == null ? default(SongInfo) : new SongInfo { Name = s.name, Bpm = s.bpm, Bars = s.bars, Loop = s.loop };
        }
        public static int SongSamples(int id) { var s = GetSong(id); return s == null ? 0 : Samples(s); }
        static int Samples(Song s) { return Mathf.RoundToInt(s.bars * 4f * 60f / s.bpm * SR); }
        /// 測試用:旋律字串加總的 16 分音符數(必須 = bars × 16)。
        public static int MelodySteps(int id)
        {
            var s = GetSong(id); if (s == null) return 0;
            int steps = 0; foreach (var tok in s.melody.Replace("|", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) steps += int.Parse(tok.Substring(tok.IndexOf(':') + 1));
            return steps;
        }
        public static int ChordCount(int id)
        {
            var s = GetSong(id); return s == null ? 0 : s.chords.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        // ------------------------------------------------------------------ 合成核心
        sealed class Rn
        {
            uint s; public Rn(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
            public uint U() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
            public float F() { return (U() & 0xFFFFFF) / 16777216f; }
            public float Nz() { return F() * 2f - 1f; }
        }

        sealed class Mix
        {
            public float[] b; public int n; public bool wrap; public Rn r;
            public void Add(int i, float v)
            {
                if (i < 0) return;
                if (i >= n) { if (!wrap) return; i %= n; }
                b[i] += v;
            }
            static float Hz(float midi) { return 440f * Mathf.Pow(2f, (midi - 69f) / 12f); }
            public static float Freq(float midi) { return Hz(midi); }

            // 木琴/馬林巴:基音 + 約 4 倍的短命泛音 + 打擊瞬間的小撞擊雜訊
            public void Mallet(int s0, float f, float vol, float dec)
            {
                int len = (int)(Mathf.Min(7f / dec, 1.8f) * SR);
                float w = TAU * f / SR, w2 = w * 3.97f, e = 1f, ef = Mathf.Exp(-dec / SR), e2 = 1f, ef2 = Mathf.Exp(-dec * 3.3f / SR);
                for (int i = 0; i < len; i++)
                {
                    float a = i < 40 ? i / 40f : 1f;
                    float v = Mathf.Sin(w * i) * e + 0.4f * Mathf.Sin(w2 * i) * e2;
                    if (i < 110) v += r.Nz() * 0.22f * (1f - i / 110f);
                    Add(s0 + i, v * vol * a); e *= ef; e2 *= ef2;
                }
            }
            // 鋼琴(簡化):4 個泛音,高次泛音衰減更快;dur 之後快速收尾
            public void Piano(int s0, float f, float vol, float dur)
            {
                float d = 2.4f + f / 380f;
                int len = (int)(Mathf.Min(dur + 0.15f, 2.6f) * SR), durS = (int)(dur * SR);
                float w = TAU * f / SR; float rel = 1f, rf = Mathf.Exp(-22f / SR);
                float e1 = 1f, e2 = 1f, e3 = 1f, e4 = 1f;
                float f1 = Mathf.Exp(-d / SR), f2 = Mathf.Exp(-d * 1.7f / SR), f3 = Mathf.Exp(-d * 2.4f / SR), f4 = Mathf.Exp(-d * 3.1f / SR);
                for (int i = 0; i < len; i++)
                {
                    float a = i < 60 ? i / 60f : 1f;
                    float v = Mathf.Sin(w * i) * e1 * 0.6f + Mathf.Sin(w * 2.002f * i) * e2 * 0.28f + Mathf.Sin(w * 3.005f * i) * e3 * 0.13f + Mathf.Sin(w * 4.01f * i) * e4 * 0.06f;
                    if (i >= durS) rel *= rf;
                    Add(s0 + i, v * vol * a * rel); e1 *= f1; e2 *= f2; e3 *= f3; e4 *= f4;
                }
            }
            // 撥弦(烏克麗麗/吉他/薩茲):5 個諧波,bright 控制高次泛音量
            public void Pluck(int s0, float f, float vol, float dec, float bright)
            {
                int len = (int)(Mathf.Min(6f / dec, 1.3f) * SR);
                float w = TAU * f / SR;
                float e1 = 1f, e2 = 1f, e3 = 1f, e4 = 1f, e5 = 1f;
                float f1 = Mathf.Exp(-dec / SR), f2 = Mathf.Exp(-dec * 1.6f / SR), f3 = Mathf.Exp(-dec * 2.2f / SR), f4 = Mathf.Exp(-dec * 2.8f / SR), f5 = Mathf.Exp(-dec * 3.4f / SR);
                float b2 = bright / 2f, b3 = bright * bright / 3f, b4 = bright * bright * bright / 4f, b5 = bright * bright * bright * bright / 5f;
                for (int i = 0; i < len; i++)
                {
                    float a = i < 44 ? i / 44f : 1f;
                    float v = Mathf.Sin(w * i) * e1 + b2 * Mathf.Sin(w * 2f * i) * e2 + b3 * Mathf.Sin(w * 3f * i) * e3 + b4 * Mathf.Sin(w * 4f * i) * e4 + b5 * Mathf.Sin(w * 5f * i) * e5;
                    if (i < 80) v += r.Nz() * 0.12f * (1f - i / 80f);
                    Add(s0 + i, v * vol * a); e1 *= f1; e2 *= f2; e3 *= f3; e4 *= f4; e5 *= f5;
                }
            }
            // 口哨:正弦 + 微弱 2 倍音 + 氣音;慢起音、延遲顫音
            public void Whistle(int s0, float f, float vol, float dur)
            {
                int len = (int)((dur + 0.1f) * SR); float ph = 0f, lp = 0f;
                for (int i = 0; i < len; i++)
                {
                    float u = i / (float)SR;
                    float vib = u > 0.12f ? Mathf.Min(1f, (u - 0.12f) / 0.2f) * 0.006f * Mathf.Sin(TAU * 5.3f * u) : 0f;
                    ph += TAU * f * (1f + vib) / SR;
                    float env = Mathf.Min(1f, u / 0.04f) * (1f - 0.15f * Mathf.Min(1f, u / Mathf.Max(0.05f, dur)));
                    if (u > dur) env *= Mathf.Max(0f, 1f - (u - dur) / 0.08f);
                    lp += 0.2f * (r.Nz() - lp);
                    Add(s0 + i, (Mathf.Sin(ph) + 0.12f * Mathf.Sin(2f * ph) + lp * 0.18f) * env * vol);
                }
            }
            public void Bass(int s0, float f, float vol, float dur)
            {
                int len = (int)((dur + 0.06f) * SR); float w = TAU * f / SR;
                for (int i = 0; i < len; i++)
                {
                    float u = i / (float)SR;
                    float env = Mathf.Min(1f, u / 0.006f) * (0.4f + 0.6f * Mathf.Exp(-u * 3f));
                    if (u > dur) env *= Mathf.Max(0f, 1f - (u - dur) / 0.05f);
                    Add(s0 + i, (Mathf.Sin(w * i) + 0.35f * Mathf.Sin(2f * w * i) + 0.1f * Mathf.Sin(3f * w * i)) * env * vol);
                }
            }
            public void Pad(int s0, float f, float vol, float dur)
            {
                int len = (int)((dur + 0.5f) * SR); float w1 = TAU * f * 0.998f / SR, w2 = TAU * f * 1.003f / SR;
                for (int i = 0; i < len; i++)
                {
                    float u = i / (float)SR;
                    float env = Mathf.Min(1f, u / 0.25f); if (u > dur) env *= Mathf.Max(0f, 1f - (u - dur) / 0.5f);
                    Add(s0 + i, (Mathf.Sin(w1 * i) + Mathf.Sin(w2 * i)) * 0.5f * env * vol);
                }
            }
            public void Kick(int s0, float vol)
            {
                int len = (int)(0.22f * SR); float ph = 0f;
                for (int i = 0; i < len; i++)
                {
                    float u = i / (float)SR; ph += TAU * (46f + 95f * Mathf.Exp(-u * 32f)) / SR;
                    Add(s0 + i, Mathf.Sin(ph) * Mathf.Exp(-u * 10f) * Mathf.Min(1f, u / 0.0015f) * vol);
                }
            }
            public void Snare(int s0, float vol)
            {
                int len = (int)(0.2f * SR); float lp = 0f;
                for (int i = 0; i < len; i++)
                {
                    float u = i / (float)SR, n = r.Nz(); lp += 0.45f * (n - lp);
                    float v = ((n - lp) * 0.9f + lp * 0.3f) * Mathf.Exp(-u * 24f) + Mathf.Sin(TAU * 190f * u) * 0.45f * Mathf.Exp(-u * 32f);
                    Add(s0 + i, v * vol * Mathf.Min(1f, u / 0.001f));
                }
            }
            public void Hat(int s0, float vol, float dec)
            {
                int len = (int)(Mathf.Min(6f / dec, 0.3f) * SR); float prev = 0f;
                for (int i = 0; i < len; i++)
                {
                    float n = r.Nz(); float u = i / (float)SR;
                    Add(s0 + i, (n - prev) * 0.5f * Mathf.Exp(-u * dec) * (i < 20 ? i / 20f : 1f) * vol); prev = n;
                }
            }
            public void Shaker(int s0, float vol)
            {
                int len = (int)(0.07f * SR); float prev = 0f;
                for (int i = 0; i < len; i++)
                {
                    float n = r.Nz(), s = Mathf.Sin(Mathf.PI * i / len);
                    Add(s0 + i, (n - prev) * 0.5f * s * s * vol); prev = n;
                }
            }
            public void Wood(int s0, float vol, float f)
            {
                int len = (int)(0.07f * SR);
                for (int i = 0; i < len; i++)
                {
                    float u = i / (float)SR;
                    Add(s0 + i, (Mathf.Sin(TAU * f * u) * Mathf.Exp(-u * 85f) + 0.4f * Mathf.Sin(TAU * 1.58f * f * u) * Mathf.Exp(-u * 120f)) * Mathf.Min(1f, u / 0.0006f) * vol);
                }
            }
            public void Cymbal(int s0, float vol)
            {
                int len = (int)(1.6f * SR); float prev = 0f;
                for (int i = 0; i < len; i++)
                {
                    float n = r.Nz(), u = i / (float)SR;
                    Add(s0 + i, (n - prev) * 0.5f * Mathf.Exp(-u * 2.8f) * Mathf.Min(1f, u / 0.002f) * vol); prev = n;
                }
            }
        }

        static int[] Tones(string type)
        {
            switch (type)
            {
                case "m": return new[] { 0, 3, 7 };
                case "M7": return new[] { 0, 4, 7, 11 };
                case "m7": return new[] { 0, 3, 7, 10 };
                case "7": return new[] { 0, 4, 7, 10 };
                case "sus": return new[] { 0, 5, 7 };
                case "5": return new[] { 0, 7, 12 };
                default: return new[] { 0, 4, 7 };
            }
        }
        static Chord[] ParseChords(string s)
        {
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var o = new Chord[parts.Length];
            for (int i = 0; i < parts.Length; i++) { var kv = parts[i].Split(':'); o[i] = new Chord { r = int.Parse(kv[0]), t = Tones(kv[1]) }; }
            return o;
        }
        static int ScaleMidi(Song s, int degree, int oct, int acc)
        {
            int idx = degree - 1; oct += idx / 7; idx %= 7;
            return s.root + 12 + s.scale[idx] + 12 * oct + acc;
        }
        static List<Note> ParseMelody(Song s, Rn r)
        {
            var list = new List<Note>(); int step = 0;
            foreach (var tok in s.melody.Replace("|", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = tok.IndexOf(':'); string p = tok.Substring(0, c); int len = int.Parse(tok.Substring(c + 1));
                if (p == "r") { step += len; continue; }
                int acc = 0, k = 0, oct = 0;
                if (p[0] == 'b') { acc = -1; k = 1; } else if (p[0] == '#') { acc = 1; k = 1; }
                int d = p[k] - '0';
                for (int j = k + 1; j < p.Length; j++) { if (p[j] == '\'') oct++; else if (p[j] == ',') oct--; }
                // 種子決定的裝飾音:長音有機率前面加一個相鄰音階音(倚音)
                if (len >= 4 && r.F() < 0.18f)
                {
                    list.Add(new Note { step = step, len = 1, midi = ScaleMidi(s, d + 1, oct, 0) });
                    list.Add(new Note { step = step + 1, len = len - 1, midi = ScaleMidi(s, d, oct, acc) });
                }
                else list.Add(new Note { step = step, len = len, midi = ScaleMidi(s, d, oct, acc) });
                step += len;
            }
            return list;
        }
        static int Voice(Song s, Chord c, int interval, int lo)
        {
            int p = s.root + c.r + interval;
            while (p < lo) p += 12; while (p >= lo + 12) p -= 12;
            return p;
        }
        static int BassMidi(Song s, Chord c, int interval)
        {
            int p = s.bassBase + c.r; if (c.r > 7) p -= 12; return p + interval;
        }

        /// 合成一首曲子的單聲道樣本(已正規化到峰值 0.85)。final=true 為最後一圈版(多一層閃爍琶音/加厚節奏)。seed 影響裝飾音與細節。
        public static float[] Render(int id, bool final, int seed)
        {
            var s = GetSong(id); if (s == null) return null;
            int n = Samples(s);
            var m = new Mix { b = new float[n], n = n, wrap = s.loop, r = new Rn(unchecked((uint)(s.seed * 7919 + seed * 104729 + 12345))) };
            double spb = n / (double)(s.bars * 16);   // 每個 16 分音符的樣本數
            var chords = ParseChords(s.chords);
            var mel = ParseMelody(s, m.r);

            Func<double, int> At = st => (int)(st * spb + 0.5);
            Func<int, double> Sw = st => st + ((st % 4) == 2 ? s.swing : 0f);
            Func<int, float> Fq = mi => Mix.Freq(mi);

            for (int bar = 0; bar < s.bars; bar++)
            {
                var c = chords[bar % chords.Length];
                int b0 = bar * 16; int sec = (bar / 4) % 4; bool phraseEnd = (bar % 4) == 3;
                float stepSec = (float)(spb / SR);
                switch (s.style)
                {
                    case 0: // Island GP
                        m.Kick(At(b0), 0.5f); m.Kick(At(b0 + 8), 0.45f);
                        if (sec == 3 || m.r.F() < 0.2f) m.Kick(At(b0 + 10), 0.3f);
                        m.Wood(At(b0 + 4), 0.2f, 1100f); m.Wood(At(b0 + 12), 0.2f, 1400f);
                        for (int st = 0; st < 16; st += 2) m.Hat(At(Sw(b0 + st)), (st % 4 == 2) ? 0.13f : 0.09f, 80f);
                        for (int st = 1; st < 16; st += 2) m.Shaker(At(b0 + st), 0.05f + 0.02f * m.r.F());
                        if (phraseEnd) for (int st = 12; st < 16; st++) m.Snare(At(b0 + st), 0.1f + 0.03f * (st - 12));
                        {
                            int[] bs = { 0, 4, 6, 8, 12, 14 }; int[] bi = { 0, 7, 12, 0, 7, 12 };
                            for (int j = 0; j < bs.Length; j++) m.Bass(At(b0 + bs[j]), Fq(BassMidi(s, c, bi[j])), 0.42f, stepSec * (j == 0 || j == 3 ? 3f : 1.6f));
                        }
                        foreach (int st in new[] { 2, 6, 10, 14 })
                            for (int k = 0; k < c.t.Length; k++) m.Piano(At(Sw(b0 + st)), Fq(Voice(s, c, c.t[k], 57)), 0.15f, stepSec * 1.6f);
                        break;
                    case 1: // Canyon Run
                        foreach (int st in new[] { 0, 6, 8, 14 }) m.Kick(At(b0 + st), 0.55f);
                        m.Snare(At(b0 + 4), 0.28f); m.Snare(At(b0 + 12), 0.3f);
                        for (int st = 0; st < 16; st += 2) m.Hat(At(b0 + st), 0.11f, 75f);
                        for (int st = 0; st < 16; st++) m.Shaker(At(b0 + st), 0.045f + 0.03f * m.r.F());
                        if (phraseEnd) for (int st = 12; st < 16; st++) m.Snare(At(b0 + st), 0.16f);
                        for (int st = 0; st < 16; st += 2)
                        {
                            int iv = (st == 6 || st == 14) ? 12 : (st == 10 ? 7 : 0);
                            m.Bass(At(b0 + st), Fq(BassMidi(s, c, iv)), 0.4f, stepSec * 1.7f);
                        }
                        foreach (int st in new[] { 0, 3, 6, 8, 11, 14 })
                        {
                            m.Pluck(At(b0 + st), Fq(Voice(s, c, 0, 50)), 0.17f, 4.5f, 0.6f);
                            m.Pluck(At(b0 + st), Fq(Voice(s, c, 7, 50)), 0.14f, 4.5f, 0.6f);
                            if (c.t.Length > 1) m.Pluck(At(b0 + st), Fq(Voice(s, c, c.t[1], 57)), 0.1f, 4.5f, 0.6f);
                        }
                        break;
                    case 2: // Coast Loop
                        m.Kick(At(b0), 0.3f); m.Kick(At(b0 + 10), 0.22f);
                        m.Wood(At(b0 + 12), 0.1f, 900f);
                        for (int st = 0; st < 16; st += 2) m.Shaker(At(Sw(b0 + st)), (st % 4 == 0) ? 0.07f : 0.1f);
                        {
                            int[] bs = { 0, 8, 12 }; int[] bi = { 0, 7, 0 }; float[] bl = { 6f, 3.5f, 3f };
                            for (int j = 0; j < 3; j++) m.Bass(At(b0 + bs[j]), Fq(BassMidi(s, c, bi[j])), 0.4f, stepSec * bl[j]);
                        }
                        for (int k = 0; k < 3; k++) m.Pad(At(b0), Fq(Voice(s, c, c.t[k], 52)), 0.1f, stepSec * 16f);
                        {
                            int[] pat = { 0, 1, 2, 3, 2, 1, 2, 3 };
                            for (int j = 0; j < 8; j++)
                            {
                                int tIdx = pat[j] % c.t.Length;
                                m.Pluck(At(Sw(b0 + j * 2)), Fq(Voice(s, c, c.t[tIdx], 60)), 0.17f, 4.2f, 0.45f);
                            }
                        }
                        break;
                    case 3: // Menu
                        m.Kick(At(b0), 0.22f); m.Kick(At(b0 + 8), 0.18f);
                        m.Wood(At(b0 + 12), 0.07f, 850f);
                        for (int st = 0; st < 16; st += 2) m.Hat(At(Sw(b0 + st)), 0.045f, 90f);
                        {
                            int[] bs = { 0, 8 }; int[] bi = { 0, 7 };
                            for (int j = 0; j < 2; j++) m.Bass(At(b0 + bs[j]), Fq(BassMidi(s, c, bi[j])), 0.36f, stepSec * 6f);
                        }
                        {
                            int[] ps = { 0, 4, 6, 8, 12, 14 }; int[] pi = { 0, 2, 1, 3, 1, 2 };
                            for (int j = 0; j < ps.Length; j++)
                                m.Piano(At(Sw(b0 + ps[j])), Fq(Voice(s, c, c.t[pi[j] % c.t.Length], 55)), 0.2f, stepSec * 3f);
                        }
                        break;
                    case 4: // Result jingle
                        if (bar < 2) { m.Kick(At(b0), 0.5f); m.Kick(At(b0 + 8), 0.45f); m.Snare(At(b0 + 4), 0.2f); m.Snare(At(b0 + 12), 0.22f); for (int st = 0; st < 16; st += 2) m.Hat(At(b0 + st), 0.1f, 80f); }
                        else { m.Cymbal(At(b0), 0.35f); m.Kick(At(b0), 0.6f); }
                        if (bar < 2) { foreach (int st in new[] { 0, 4, 8, 12 }) for (int k = 0; k < c.t.Length; k++) m.Piano(At(b0 + st), Fq(Voice(s, c, c.t[k], 55)), 0.16f, stepSec * 3f); }
                        else for (int k = 0; k < c.t.Length; k++) { m.Piano(At(b0), Fq(Voice(s, c, c.t[k], 55)), 0.2f, stepSec * 14f); m.Pad(At(b0), Fq(Voice(s, c, c.t[k], 52)), 0.1f, stepSec * 14f); }
                        m.Bass(At(b0), Fq(BassMidi(s, c, 0)), 0.45f, bar < 2 ? stepSec * 6f : stepSec * 12f);
                        if (bar < 2) m.Bass(At(b0 + 8), Fq(BassMidi(s, c, 7)), 0.4f, stepSec * 6f);
                        break;
                }

                if (final && s.loop)   // 最後一圈加疊:高八度閃爍琶音 + 厚一點的節奏
                {
                    foreach (int st in new[] { 0, 3, 6, 9, 12, 15 })
                        m.Mallet(At(b0 + st), Fq(Voice(s, c, c.t[(st / 3) % c.t.Length], 84)), 0.07f, 9f);
                    if (s.style == 0 || s.style == 1) { m.Kick(At(b0 + 4), 0.28f); m.Kick(At(b0 + 12), 0.28f); }
                    for (int st = 1; st < 16; st += 2) m.Hat(At(b0 + st), 0.06f, 95f);
                }
            }

            // 旋律
            foreach (var nt in mel)
            {
                int sec = (nt.step / 64) % 4; int pos = At(Sw(nt.step)); float f = Mix.Freq(nt.midi);
                float dur = (float)(nt.len * spb / SR) * 0.95f;
                switch (s.style)
                {
                    case 0:
                        if (sec % 2 == 0) m.Mallet(pos, f, 0.55f, 6.5f); else m.Whistle(pos, f, 0.4f, dur);
                        if (final) m.Mallet(pos, f * 2f, 0.16f, 8f);
                        break;
                    case 1:
                        m.Pluck(pos, f, 0.5f, 3.4f, 0.85f);
                        if (nt.len >= 4) m.Whistle(pos, f, 0.12f, dur);
                        if (final) m.Pluck(pos, f * 2f, 0.14f, 4.5f, 0.6f);
                        break;
                    case 2:
                        m.Whistle(pos, f, 0.5f, dur);
                        if (final) m.Mallet(pos, f * 2f, 0.12f, 7f);
                        break;
                    case 3:
                        m.Mallet(pos, f, 0.55f, 4.5f);
                        if (nt.len >= 6) m.Whistle(pos, f, 0.12f, dur);
                        if (final) m.Mallet(pos, f * 2f, 0.12f, 7f);
                        break;
                    case 4:
                        m.Mallet(pos, f, 0.6f, 5f); m.Piano(pos, f, 0.18f, dur); if (nt.len >= 8) m.Whistle(pos, f, 0.3f, dur);
                        break;
                }
            }

            Finish(m.b, s.loop);
            return m.b;
        }

        static void Finish(float[] d, bool loop)
        {
            float pk = 1e-5f; for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > pk) pk = a; }
            float k = 0.85f / pk; for (int i = 0; i < d.Length; i++) d[i] *= k;
            if (!loop) { int m = Mathf.Min(SR / 10, d.Length / 4); for (int i = 0; i < m; i++) d[d.Length - 1 - i] *= i / (float)m; }
        }
    }
}
