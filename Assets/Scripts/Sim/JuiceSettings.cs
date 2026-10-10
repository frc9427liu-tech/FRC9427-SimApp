using UnityEngine;

namespace FrcSim
{
    // All juice/accessibility settings, cached in memory (PlayerPrefs on Windows = registry; do not read it per frame).
    public static class JuiceSettings
    {
        static bool loaded;
        static float master = 0.8f, sfx = 1f, music = 0.5f, shake = 1f;
        static bool rumble = true, reduceFlash;

        static void L()
        {
            if (loaded) return; loaded = true;
            try
            {
                master = PlayerPrefs.GetFloat("jMaster", 0.8f);
                sfx = PlayerPrefs.GetFloat("jSfx", 1f);
                music = PlayerPrefs.GetFloat("jMusic", 0.5f);
                shake = PlayerPrefs.GetFloat("jShake", 1f);
                rumble = Prefs.GetInt("jRumble", 1) == 1;
                reduceFlash = Prefs.GetInt("jNoFlash", 0) == 1;
            }
            catch { }
        }
        static void SetF(string k, float v) { PlayerPrefs.SetFloat(k, v); PlayerPrefs.Save(); }

        public static float Master { get { L(); return master; } set { L(); master = Mathf.Clamp01(value); SetF("jMaster", master); } }
        public static float Sfx { get { L(); return sfx; } set { L(); sfx = Mathf.Clamp01(value); SetF("jSfx", sfx); } }
        public static float Music { get { L(); return music; } set { L(); music = Mathf.Clamp01(value); SetF("jMusic", music); } }
        // 1 = normal, 0.5 = reduced, 0 = off. Scales camera shake, kick, FOV punch and speed FOV.
        public static float Shake { get { L(); return shake; } set { L(); shake = Mathf.Clamp01(value); SetF("jShake", shake); } }
        public static bool Rumble { get { L(); return rumble; } set { L(); rumble = value; Prefs.SetInt("jRumble", value ? 1 : 0); PlayerPrefs.Save(); } }
        // Photosensitivity: halves particle counts and drops full-screen-ish flash particles.
        public static bool ReduceFlash { get { L(); return reduceFlash; } set { L(); reduceFlash = value; Prefs.SetInt("jNoFlash", value ? 1 : 0); PlayerPrefs.Save(); } }

        // Click-to-cycle helpers for the existing Item-based menu (no slider widget exists): 0,20,40,60,80,100 %
        public static float Step(float v) { int i = Mathf.RoundToInt(v * 5f); return ((i + 1) % 6) / 5f; }
        public static float ShakeCycle(float v) { return v > 0.75f ? 0.5f : v > 0.25f ? 0f : 1f; }
    }
}