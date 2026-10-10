using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    public enum SfxGroup { Sfx, Amb, Music }

    public class SfxBus : MonoBehaviour
    {
        public static SfxBus I;
        public static bool Enabled;           // false in -batchmode / automated test flags / -nosound
        const int Voices = 20;
        readonly AudioSource[] pool = new AudioSource[Voices];
        readonly float[] started = new float[Voices];
        readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
        static readonly Dictionary<string, float> MinGap = new Dictionary<string, float>
        { {"ballhit",0.02f},{"shoot",0.04f},{"pluck",0.045f},{"ding",0.05f},{"tick",0.03f},{"bump",0.10f},{"uiclick",0.03f} };

        class LoopV { public AudioSource src; public float vol, pitch = 1f, tVol, tPitch = 1f; public SfxGroup g; }
        readonly Dictionary<string, LoopV> loops = new Dictionary<string, LoopV>();
        struct Delayed { public float at; public string name; public float vol, pitch, pan; public SfxGroup g; }
        readonly List<Delayed> delayed = new List<Delayed>(16);
        float duck = 1f, duckTarget = 1f, duckHold;

        // ---------------- creation ----------------
        public static void Create()
        {
            if (I != null) return;
            Enabled = DecideEnabled();
            var go = new GameObject("SfxBus"); DontDestroyOnLoad(go);
            I = go.AddComponent<SfxBus>();
        }
        static bool DecideEnabled()
        {
            var a = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(a, "-nosound") >= 0) return false;
            if (System.Array.IndexOf(a, "-sound") >= 0) return true;
            return !Application.isBatchMode && !SuperSample.IsTestRun();
        }
        void Awake()
        {
            if (!Enabled) return;
            try
            {
                var cfg = AudioSettings.GetConfiguration();
                if (cfg.dspBufferSize > 512) { cfg.dspBufferSize = 512; AudioSettings.Reset(cfg); }   // lower latency = tighter feel
            }
            catch { }
            for (int i = 0; i < Voices; i++)
            {
                var g = new GameObject("v" + i); g.transform.SetParent(transform, false);
                var s = g.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0f; pool[i] = s;
            }
            SetupReverb();
            StartCoroutine(Prewarm());
        }
        IEnumerator Prewarm()   // synthesize one clip per frame during the menu so no hitch on first play
        {
            foreach (var n in new List<string>(SfxSynth.Names)) { SfxSynth.Get(n); yield return null; }
        }
        void OnApplicationFocus(bool f) { AudioListener.pause = !f; }

        // ---------------- public API ----------------
        static float GroupVol(SfxGroup g) { return JuiceSettings.Master * (g == SfxGroup.Music ? JuiceSettings.Music : JuiceSettings.Sfx); }

        /// One-shot. vol 0..1 (before sliders), pitch 1 = normal, pan -1..1. Returns null if skipped (rate limit/disabled).
        public static AudioSource Play(string name, float vol = 1f, float pitch = 1f, float pan = 0f, SfxGroup g = SfxGroup.Sfx)
        {
            if (I == null || !Enabled) return null;
            return I.PlayInternal(name, vol, pitch, pan, g);
        }
        public static void PlayDelayed(string name, float delay, float vol = 1f, float pitch = 1f, float pan = 0f, SfxGroup g = SfxGroup.Sfx)
        {
            if (I == null || !Enabled) return;
            I.delayed.Add(new Delayed { at = Time.unscaledTime + delay, name = name, vol = vol, pitch = pitch, pan = pan, g = g });
        }
        /// Persistent loop; call every frame (or on change) with target volume/pitch; it smooths itself. vol 0 = silent (kept playing).
        public static void SetLoop(string id, string clip, float vol, float pitch, SfxGroup g = SfxGroup.Sfx)
        {
            if (I == null || !Enabled) return;
            LoopV L;
            if (!I.loops.TryGetValue(id, out L))
            {
                var c = SfxSynth.Get(clip); if (c == null) return;
                var go = new GameObject("loop_" + id); go.transform.SetParent(I.transform, false);
                var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.playOnAwake = false;
                L = new LoopV { src = s, g = g }; I.loops[id] = L; s.Play();
            }
            L.tVol = vol; L.tPitch = pitch;
        }
        /// Lower music/ambience to `level` (e.g. 0.35) for `hold` seconds, then recover slowly.
        public static void Duck(float level, float hold) { if (I == null) return; I.duckTarget = Mathf.Min(I.duckTarget, level); I.duck = Mathf.Min(I.duck, level < 1f ? Mathf.Max(level, I.duck) : I.duck); I.duckHold = Mathf.Max(I.duckHold, hold); }

        // ---- 場館殘響:掛在 AudioListener 上的 AudioReverbFilter 會作用在整個混音(體育館的短長尾),聲音不再「貼在耳朵上」
        AudioReverbFilter rev;
        public static bool ReverbOn { get => Prefs.GetInt("reverb", 1) == 1; set { Prefs.SetInt("reverb", value ? 1 : 0); if (I != null && I.rev != null) I.rev.enabled = value; } }
        void SetupReverb()
        {
            try
            {
                var lis = FindFirstObjectByType<AudioListener>();
                if (lis == null) return;
                rev = lis.gameObject.GetComponent<AudioReverbFilter>() ?? lis.gameObject.AddComponent<AudioReverbFilter>();
                rev.reverbPreset = AudioReverbPreset.Arena;
                rev.dryLevel = 0f; rev.reverbLevel = -900f; rev.reflectionsLevel = -1400f;
                rev.enabled = ReverbOn;
            }
            catch { }
        }
        // 簡易限幅:同時發聲數很多(400 顆球亂撞)時整體音量往下收,避免爆音
        float limiter = 1f;
        void UpdateLimiter(float dt)
        {
            int active = 0; for (int i = 0; i < Voices; i++) if (pool[i].isPlaying) active++;
            float target = 1f / (1f + 0.045f * Mathf.Max(0, active - 7));
            limiter = Mathf.MoveTowards(limiter, target, dt * (target < limiter ? 6f : 0.8f));
            AudioListener.volume = limiter;
        }
        // ---------------- internals ----------------
        AudioSource PlayInternal(string name, float vol, float pitch, float pan, SfxGroup g)
        {
            float now = Time.unscaledTime, gap;
            if (MinGap.TryGetValue(name, out gap))
            {
                float lp; lastPlay.TryGetValue(name, out lp);
                if (now - lp < gap) return null;
                lastPlay[name] = now;
            }
            float v = vol * GroupVol(g);
            if (v < 0.004f) return null;
            var clip = SfxSynth.Get(name); if (clip == null) return null;
            int idx = -1; float oldest = float.MaxValue;
            for (int i = 0; i < Voices; i++)
            {
                if (!pool[i].isPlaying) { idx = i; break; }
                if (started[i] < oldest) { oldest = started[i]; idx = i; }   // steal the oldest voice
            }
            var s = pool[idx];
            s.Stop(); s.clip = clip; s.volume = Mathf.Clamp01(v); s.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            s.panStereo = Mathf.Clamp(pan, -1f, 1f); s.loop = false; s.Play();
            started[idx] = now;
            return s;
        }

        void Update()
        {
            if (!Enabled) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f), now = Time.unscaledTime;
            for (int i = delayed.Count - 1; i >= 0; i--)
            {
                var d = delayed[i];
                if (now >= d.at) { delayed[i] = delayed[delayed.Count - 1]; delayed.RemoveAt(delayed.Count - 1); PlayInternal(d.name, d.vol, d.pitch, d.pan, d.g); }
            }
            UpdateLimiter(dt);
            if (duckHold > 0f) { duckHold -= dt; duck = Mathf.MoveTowards(duck, duckTarget, dt * 8f); }
            else { duckTarget = 1f; duck = Mathf.MoveTowards(duck, 1f, dt * 0.7f); }
            bool paused = Time.timeScale == 0f;
            foreach (var kv in loops)
            {
                var L = kv.Value;
                float pm = paused ? (L.g == SfxGroup.Music ? 0.5f : 0f) : 1f;
                float target = L.tVol * GroupVol(L.g) * (L.g == SfxGroup.Sfx ? 1f : duck) * pm;
                L.vol += (target - L.vol) * (1f - Mathf.Exp(-10f * dt));
                L.pitch += (L.tPitch - L.pitch) * (1f - Mathf.Exp(-14f * dt));
                L.src.volume = Mathf.Clamp01(L.vol); L.src.pitch = Mathf.Clamp(L.pitch, 0.3f, 3f);
                L.src.mute = L.vol < 0.0004f;
            }
        }
    }
}