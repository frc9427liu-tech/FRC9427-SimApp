using UnityEngine;

namespace FrcSim
{
    [DefaultExecutionOrder(10000)]
    public class JuiceFX : MonoBehaviour
    {
        static JuiceFX I;
        Camera cam; float baseFov = 60f;
        float trauma, fovOff, fovVel, speedFov;
        Vector3 kickOff, kickVel;
        Vector3 lastPosOff; Quaternion lastRotBase; bool applied;
        float seed;

        /// a = trauma to add (0..1). cap = do not push above this value (so 8 shots/s cannot stack to a violent shake).
        public static void AddTrauma(float a, float cap = 1f)
        { if (I == null || JuiceSettings.Shake <= 0f) return; I.trauma = Mathf.Min(I.trauma + a, Mathf.Max(I.trauma, cap)); }
        /// World-space displacement impulse in metres (peak offset ~= magnitude). Use -shotDirection * 0.015.
        public static void Kick(Vector3 worldImpulse) { if (I != null) I.kickVel += worldImpulse * 14f; }
        /// Field-of-view punch in degrees (positive = zoom out then settle).
        public static void FovPunch(float deg) { if (I != null) I.fovVel += deg * 15f; }

        void Awake() { I = this; cam = GetComponent<Camera>(); baseFov = cam.fieldOfView; seed = Random.value * 100f; }
        void OnDestroy() { if (I == this) I = null; }

        void Update() { Undo(); }
        void OnDisable() { Undo(); }
        void Undo()
        {
            if (!applied) return;
            transform.position -= lastPosOff;
            transform.rotation = lastRotBase;
            applied = false;
        }

        float N(int i, float t) { return Mathf.PerlinNoise(seed + i * 17.3f, t) * 2f - 1f; }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float motion = JuiceSettings.Shake;
            if (Time.timeScale == 0f || motion <= 0f)
            {
                trauma = 0f; kickOff = kickVel = Vector3.zero; fovOff = fovVel = 0f; speedFov = 0f;
                cam.fieldOfView = baseFov; return;
            }
            trauma = Mathf.Max(0f, trauma - 1.5f * dt);
            float shake = trauma * trauma * motion;                       // quadratic: small hits are subtle, big hits violent
            float tn = Time.unscaledTime * 24f;
            Vector3 off = (transform.right * N(0, tn) + transform.up * N(1, tn)) * (0.20f * shake);

            kickVel += (-180f * kickOff - 18f * kickVel) * dt; kickOff += kickVel * dt;      // damped spring (zeta ~0.67)
            fovVel += (-220f * fovOff - 16f * fovVel) * dt; fovOff += fovVel * dt;           // damped spring (zeta ~0.54)
            float sp = GameSession.Drive != null ? GameSession.Drive.Speed : 0f;
            speedFov += (Mathf.Clamp01(sp / 4f) * 2.5f - speedFov) * (1f - Mathf.Exp(-3f * dt));

            lastRotBase = transform.rotation;
            lastPosOff = off + kickOff * motion;
            transform.position += lastPosOff;
            transform.rotation = lastRotBase * Quaternion.Euler(N(3, tn) * 1.2f * shake * 3f, N(4, tn) * 1.2f * shake * 3f, N(2, tn) * 2.5f * shake * 3f);
            applied = true;
            cam.fieldOfView = baseFov + (fovOff + speedFov) * motion;
        }
    }
}