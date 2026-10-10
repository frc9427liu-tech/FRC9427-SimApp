using UnityEngine;

namespace FrcSim
{
    // Attach to each menu button/panel: scale 0.92 -> 1.0 with a damped spring (~8% overshoot). delay = i * 0.035f staggers a list.
    public class UiPop : MonoBehaviour
    {
        public float delay;
        float t, s = 0.92f, v;
        void OnEnable() { t = 0f; v = 0f; s = 0.92f; transform.localScale = Vector3.one * s; if (JuiceSettings.Shake <= 0f) { transform.localScale = Vector3.one; enabled = false; } }
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);
            t += dt; if (t < delay) return;
            v += (-260f * (s - 1f) - 20f * v) * dt; s += v * dt;          // omega 16 rad/s, zeta ~0.62
            transform.localScale = Vector3.one * s;
            if (Mathf.Abs(s - 1f) < 0.0005f && Mathf.Abs(v) < 0.01f) { transform.localScale = Vector3.one; enabled = false; }
        }
    }
}