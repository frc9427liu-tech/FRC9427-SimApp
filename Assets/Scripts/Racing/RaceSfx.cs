using UnityEngine;

namespace FrcSim
{
    // 賽車音色(程式合成,模組 P)。Init 時合成並用 SfxSynth.Register 註冊;之後 SfxBus.Play / SetLoop 用同名播放。
    // M1:kart_engine_loop、count_beep、count_go、lap、final_lap。M2:kart_skid_loop、drift_lv、boost、kart_hit、kart_hop。
    public static class RaceSfx
    {
        public const string Engine = "kart_engine_loop", Beep = "count_beep", Go = "count_go", Lap = "lap", FinalLap = "final_lap",
            Skid = "kart_skid_loop", DriftLv = "drift_lv", Boost = "boost", Hit = "kart_hit", Hop = "kart_hop";
        // M5 額外音效(配樂模組):道具箱撞破、輪盤 tick、拿到道具、星星、閃電、炸彈、藍殼警告、跳台起飛/落地、金幣、名次升降、過終點線
        public const string BoxBreak = "box_break", ItemRoll = "item_roll", ItemGet = "item_get", StarOn = "star_on", Zap = "zap", Explode = "explode",
            ShellWarn = "shell_warn", JumpLaunch = "jump_launch", JumpLand = "jump_land", Coin = "coin", RankUp = "rank_up", RankDown = "rank_down", FinishLine = "finish_line";
        public static readonly string[] ExtraNames = { BoxBreak, ItemRoll, ItemGet, StarOn, Zap, Explode, ShellWarn, JumpLaunch, JumpLand, Coin, RankUp, RankDown, FinishLine };
        const float TAU = 6.2831853f;
        static readonly System.Random rng = new System.Random(94270);
        static bool ready;

        public static void Init()
        {
            if (ready && SfxSynth.Get(Engine) != null) return;
            if (!SfxBus.Enabled) return;   // 批次/自動測試不出聲,不必合成
            try
            {
                Reg(Engine, MakeEngine(), SfxSynth.SRL, true);
                Reg(Beep, MakeBeep(), SfxSynth.SR, false);
                Reg(Go, MakeGo(), SfxSynth.SR, false);
                Reg(Lap, MakeArp(new[] { 659.3f, 880f }, 0.45f), SfxSynth.SR, false);
                Reg(FinalLap, MakeArp(new[] { 659.3f, 830.6f, 1046.5f }, 0.6f), SfxSynth.SR, false);
                Reg(Skid, MakeSkid(), SfxSynth.SRL, true);
                Reg(DriftLv, MakeDriftLv(), SfxSynth.SR, false);
                Reg(Boost, MakeBoost(), SfxSynth.SR, false);
                Reg(Hit, MakeHit(), SfxSynth.SR, false);
                Reg(Hop, MakeHop(), SfxSynth.SR, false);
                foreach (var nm in ExtraNames) Reg(nm, BuildExtra(nm), SfxSynth.SR, false);
                ready = true;
            }
            catch (System.Exception e) { Debug.LogWarning("[RaceSfx] synth failed: " + e.Message); }
        }

        static void Reg(string name, float[] d, int sr, bool loop)
        {
            float pk = 1e-5f; for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > pk) pk = a; }
            float k = 0.89f / pk; for (int i = 0; i < d.Length; i++) d[i] *= k;
            if (!loop) { int m = Mathf.Min(sr / 200, d.Length / 4); for (int i = 0; i < m; i++) d[d.Length - 1 - i] *= i / (float)m; }
            var c = AudioClip.Create(name, d.Length, 1, sr, false); c.SetData(d, 0);
            SfxSynth.Register(name, c);
        }

        static float Nz() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

        // ------------------------------------------------------------------ M5 額外音效(44.1 kHz 單聲道;BuildExtra 不需要音訊裝置,測試可直接呼叫)
        public static float[] BuildExtra(string name)
        {
            switch (name)
            {
                case "box_break":   // 玻璃碎裂般的亮雜訊 + 三個下行鈴音
                    return Synth(0.42f, (t, st) =>
                    {
                        float v = st.Hp(Nz()) * 1.1f * Mathf.Exp(-t * 28f);
                        v += Bell(t, 0f, 2400f, 16f) * 0.5f + Bell(t, 0.03f, 1800f, 14f) * 0.5f + Bell(t, 0.06f, 1300f, 12f) * 0.5f;
                        v += Mathf.Sin(TAU * (180f - 90f * Mathf.Min(1f, t * 8f)) * t) * Mathf.Exp(-t * 25f) * 0.6f;
                        return v;
                    });
                case "item_roll":   // 輪盤 tick:木琴短音(播放時依序提高 pitch 會有輪盤轉動感)
                    return Synth(0.07f, (t, st) => (Mathf.Sin(TAU * 1250f * t) + 0.4f * Mathf.Sin(TAU * 3100f * t) * Mathf.Exp(-t * 60f)) * Mathf.Exp(-t * 55f));
                case "item_get":    // 拿到道具:四音上行 + 閃光
                    return Synth(0.6f, (t, st) => Bell(t, 0f, 784f, 7f) + Bell(t, 0.07f, 988f, 7f) + Bell(t, 0.14f, 1175f, 7f) + 1.2f * Bell(t, 0.21f, 1568f, 5f));
                case "star_on":     // 星星無敵:快速上下行鈴音琶音 + 亮尾
                    return Synth(1.1f, (t, st) =>
                    {
                        float[] fs = { 1047f, 1319f, 1568f, 2093f, 1568f, 1319f };
                        float v = 0f;
                        for (int k = 0; k < 10; k++) v += Bell(t, k * 0.065f, fs[k % 6] * (k >= 6 ? 1.5f : 1f), 9f) * (1f - k * 0.05f);
                        return v;
                    });
                case "zap":         // 閃電:爆裂雜訊 + 高到低鋸齒掃頻 + 低頻隆隆
                    return Synth(0.75f, (t, st) =>
                    {
                        st.ph += TAU * (2400f * Mathf.Exp(-t * 6f) + 70f) / SfxSynth.SR;
                        float saw = (st.ph / TAU % 1f) * 2f - 1f;
                        float crack = Nz() * Mathf.Exp(-t * 22f) * 1.3f;
                        float rumble = st.Lp(Nz(), 300f) * 2.4f * Mathf.Exp(-t * 4f);
                        float flutter = 0.6f + 0.4f * Mathf.Sin(TAU * 28f * t);
                        return crack + saw * 0.55f * Mathf.Exp(-t * 5f) * flutter + rumble;
                    });
                case "explode":     // 炸彈:低頻下滑 thump + 低通雜訊爆發 + 碎片
                    return Synth(0.95f, (t, st) =>
                    {
                        st.ph += TAU * (30f + 110f * Mathf.Exp(-t * 14f)) / SfxSynth.SR;
                        float boom = Mathf.Sin(st.ph) * Mathf.Exp(-t * 4.5f) * 1.5f;
                        float blast = st.Lp(Nz(), 900f + 2500f * Mathf.Exp(-t * 9f)) * 2.4f * Mathf.Exp(-t * 6f);
                        float debris = st.Hp(Nz()) * 0.35f * Mathf.Exp(-t * 14f);
                        return boom + blast + debris;
                    });
                case "shell_warn":  // 藍殼警告:兩聲短促方波嗶(可重複播放,越近越密)
                    return Synth(0.3f, (t, st) =>
                    {
                        float u = t < 0.15f ? t : t - 0.15f; if (u > 0.09f) return 0f;
                        return Sq(1480f, t) * 0.45f * Mathf.Min(1f, u / 0.003f) * Mathf.Max(0f, 1f - u / 0.09f);
                    });
                case "jump_launch": // 跳台起飛:上揚掃頻 + 風聲
                    return Synth(0.4f, (t, st) =>
                    {
                        st.ph += TAU * Mathf.Lerp(260f, 980f, t / 0.4f) / SfxSynth.SR;
                        float wind = st.Lp(Nz(), 500f + 3000f * t) * 1.2f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t / 0.4f));
                        return (Mathf.Sin(st.ph) * 0.6f + wind) * Mathf.Min(1f, t / 0.01f) * Mathf.Max(0f, 1f - t / 0.4f);
                    });
                case "jump_land":   // 落地:低頻 thump + 短塵土雜訊
                    return Synth(0.28f, (t, st) =>
                    {
                        st.ph += TAU * Mathf.Lerp(110f, 48f, Mathf.Min(1f, t / 0.1f)) / SfxSynth.SR;
                        return (Mathf.Sin(st.ph) * Mathf.Exp(-t * 14f) * 1.2f + st.Lp(Nz(), 700f) * 1.2f * Mathf.Exp(-t * 30f)) * Mathf.Min(1f, t / 0.002f);
                    });
                case "coin":        // 金幣:B5 → E6 兩音
                    return Synth(0.34f, (t, st) =>
                    {
                        float v = t < 0.07f ? Sq(988f, t) : Sq(1319f, t) * Mathf.Exp(-(t - 0.07f) * 9f);
                        return v * Mathf.Min(1f, t / 0.002f) * 0.5f;
                    });
                case "rank_up":     // 名次提升:兩音上行
                    return Synth(0.24f, (t, st) => Bell(t, 0f, 1319f, 12f) + Bell(t, 0.08f, 1760f, 10f));
                case "rank_down":   // 名次下降:兩音下行(較低較悶)
                    return Synth(0.26f, (t, st) => (Bell(t, 0f, 523f, 9f) + 0.9f * Bell(t, 0.1f, 392f, 8f)) * 0.9f);
                case "finish_line": // 過終點線:雜訊掃過 + C 大調和弦鈴
                    return Synth(1.2f, (t, st) =>
                    {
                        float e = Mathf.Sin(Mathf.PI * Mathf.Min(1f, t / 0.5f));
                        float v = st.Lp(Nz(), 400f + 5000f * e) * 0.9f * e;
                        v += Bell(t, 0.25f, 523.3f, 3.5f) + Bell(t, 0.25f, 659.3f, 3.5f) + Bell(t, 0.25f, 784f, 3.5f) + Bell(t, 0.25f, 1046.5f, 3f);
                        return v;
                    });
            }
            return null;
        }

        sealed class SynthState
        {
            public float ph, lp, prev;
            public float Lp(float x, float fc) { lp += (1f - Mathf.Exp(-TAU * fc / SfxSynth.SR)) * (x - lp); return lp; }
            public float Hp(float x) { float y = x - prev; prev = x; return y * 0.5f; }
        }
        static float[] Synth(float len, System.Func<float, SynthState, float> f)
        {
            int n = Mathf.RoundToInt(len * SfxSynth.SR); var d = new float[n]; var st = new SynthState();
            for (int i = 0; i < n; i++) d[i] = f(i / (float)SfxSynth.SR, st);
            return d;
        }
        static float Bell(float t, float t0, float f, float dec)
        {
            if (t < t0) return 0f; float u = t - t0;
            return (Mathf.Sin(TAU * f * u) + 0.3f * Mathf.Sin(TAU * f * 2.76f * u) * Mathf.Exp(-u * dec * 1.5f)) * Mathf.Exp(-u * dec) * Mathf.Min(1f, u / 0.002f) * 0.5f;
        }
        static float Sq(float f, float t) { return (Mathf.Sin(TAU * f * t) >= 0f ? 1f : -1f) * 0.6f + 0.4f * Mathf.Sin(TAU * f * t); }

        // 引擎:鋸齒波 + 2/3 倍泛音 + 少量低通雜訊。基頻 110 Hz、1 秒 = 整數個週期;雜訊尾端交叉淡化,無縫循環。
        static float[] MakeEngine()
        {
            int sr = SfxSynth.SRL, n = sr, tail = 2048; var raw = new float[n + tail];
            float lp = 0f, a = 1f - Mathf.Exp(-TAU * 900f / sr);
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)sr;
                float ph = (t * 110f) % 1f, saw = 2f * ph - 1f;
                float h2 = Mathf.Sin(TAU * 220f * t), h3 = Mathf.Sin(TAU * 330f * t);
                lp += a * (Nz() - lp);
                float wob = 1f + 0.08f * Mathf.Sin(TAU * 11f * t);   // 11 整數 Hz:循環內剛好整數週期
                raw[i] = (saw * 0.55f * wob + h2 * 0.25f + h3 * 0.12f + lp * 0.35f);
            }
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < tail) { float k = i / (float)tail; o[i] = raw[n + i] * (1f - k) + raw[i] * k; }
                else o[i] = raw[i];
            }
            return o;
        }

        // 帶通雜訊(RBJ biquad),尾端交叉淡化成無縫循環:輪胎尖叫(中心約 2 kHz)
        static float[] MakeSkid()
        {
            int sr = SfxSynth.SRL, n = sr, tail = 2048; var raw = new float[n + tail];
            float w0 = TAU * 2000f / sr, q = 2.5f, al = Mathf.Sin(w0) / (2f * q), cw = Mathf.Cos(w0);
            float b0 = al, b2 = -al, a0 = 1f + al, a1 = -2f * cw, a2 = 1f - al;
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float x = Nz();
                float y = (b0 * x + b2 * x2 - a1 * y1 - a2 * y2) / a0;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                float t = i / (float)sr;
                raw[i] = y * (1f + 0.25f * Mathf.Sin(TAU * 7f * t)) + 0.25f * Mathf.Sin(TAU * 1500f * t) * y;
            }
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < tail) { float k = i / (float)tail; o[i] = raw[n + i] * (1f - k) + raw[i] * k; }
                else o[i] = raw[i];
            }
            return o;
        }

        // 迷你渦輪升級提示:短上升掃頻(播放時用 pitch = 1.26^(lv-1) 區分等級)
        static float[] MakeDriftLv()
        {
            int sr = SfxSynth.SR, n = Mathf.RoundToInt(0.16f * sr); var d = new float[n]; float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, f = Mathf.Lerp(620f, 1500f, t / 0.16f);
                ph += TAU * f / sr;
                d[i] = (Mathf.Sin(ph) + 0.3f * Mathf.Sin(2f * ph)) * Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t * 14f);
            }
            return d;
        }

        // 加速噴發:掃頻雜訊呼嘯 + 60 Hz 低頻 thump
        static float[] MakeBoost()
        {
            int sr = SfxSynth.SR, n = Mathf.RoundToInt(0.6f * sr); var d = new float[n];
            float lp = 0f, ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, u = t / 0.6f;
                float fc = Mathf.Lerp(500f, 4500f, Mathf.Sin(u * Mathf.PI * 0.5f));
                lp += (1f - Mathf.Exp(-TAU * fc / sr)) * (Nz() - lp);
                ph += TAU * Mathf.Lerp(90f, 55f, u) / sr;
                float whoosh = lp * 1.6f * Mathf.Sin(u * Mathf.PI) * (0.4f + 0.6f * (1f - u));
                float thump = Mathf.Sin(ph) * Mathf.Exp(-t * 9f) * 1.2f;
                d[i] = (whoosh + thump) * Mathf.Min(1f, t / 0.004f);
            }
            return d;
        }

        // 撞擊:下降低頻 thump + 短雜訊
        static float[] MakeHit()
        {
            int sr = SfxSynth.SR, n = Mathf.RoundToInt(0.32f * sr); var d = new float[n];
            float ph = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                ph += TAU * Mathf.Lerp(120f, 42f, Mathf.Min(1f, t / 0.12f)) / sr;
                lp += 0.35f * (Nz() - lp);
                d[i] = (Mathf.Sin(ph) * Mathf.Exp(-t * 12f) + lp * 0.7f * Mathf.Exp(-t * 35f)) * Mathf.Min(1f, t / 0.002f);
            }
            return d;
        }

        // 跳躍:短促上揚 "boing"
        static float[] MakeHop()
        {
            int sr = SfxSynth.SR, n = Mathf.RoundToInt(0.12f * sr); var d = new float[n]; float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr; ph += TAU * Mathf.Lerp(260f, 520f, t / 0.12f) / sr;
                d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * 20f) * Mathf.Min(1f, t / 0.004f);
            }
            return d;
        }

        static float[] MakeBeep()
        {
            int n = Mathf.RoundToInt(0.18f * SfxSynth.SR); var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)SfxSynth.SR; float s = Mathf.Sin(TAU * 440f * t) >= 0f ? 1f : -1f; d[i] = s * 0.5f * Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t * 6f); }
            return d;
        }

        static float[] MakeGo()
        {
            int n = Mathf.RoundToInt(0.5f * SfxSynth.SR); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SfxSynth.SR;
                float s = Mathf.Sin(TAU * 880f * t) + 0.7f * Mathf.Sin(TAU * 1320f * t) + 0.4f * Mathf.Sin(TAU * 1760f * t);
                d[i] = s * Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t * 5f);
            }
            return d;
        }

        static float[] MakeArp(float[] notes, float len)
        {
            int n = Mathf.RoundToInt(len * SfxSynth.SR); var d = new float[n]; float step = 0.09f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SfxSynth.SR, v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float u = t - k * step; if (u < 0f) continue;
                    v += (Mathf.Sin(TAU * notes[k] * u) + 0.35f * Mathf.Sin(TAU * 2f * notes[k] * u)) * Mathf.Exp(-u * 7f) * Mathf.Min(1f, u / 0.004f);
                }
                d[i] = v;
            }
            return d;
        }
    }
}
