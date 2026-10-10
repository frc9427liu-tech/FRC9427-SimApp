using UnityEngine;

namespace FrcSim
{
    public class JuiceDriver : MonoBehaviour
    {
        void Update() { Juice.Tick(Time.unscaledDeltaTime); }
        void OnDisable() { Rumble.StopAll(); }
        void OnApplicationQuit() { Rumble.StopAll(); }
    }

    public static class Juice
    {
        // ---- values the HUD can read ----
        public static float ScorePop;      // 1 -> 0 after the player's alliance scores (scale the score label)
        public static float BeepPulse;     // 1 -> 0 after each countdown beep (tint/scale the timer label)
        static float excitement; public static float Excitement => excitement;           // crowd energy 0..1.2

        static bool booted; static Camera cam;
        static readonly int[] streak = new int[2]; static readonly float[] lastScoreT = new float[2];
        static readonly int[] Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };   // major pentatonic semitones
        static string lastPhase; static int lastSec = 99; static bool startPlayed, lastBlueA = true, lastRedA = true;
        static float nextBall, nextBump, hitT; const float HitScale = 0.0937f;

        public static void Boot(GameObject camObj)
        {
            if (booted) return; booted = true;
            SfxBus.Create();
            Rumble.Enabled = !SuperSample.IsTestRun();
            var root = new GameObject("Juice"); Object.DontDestroyOnLoad(root);
            Fx.Init(root.transform);
            camObj.AddComponent<JuiceFX>();
            if (Object.FindFirstObjectByType<AudioListener>() == null) camObj.AddComponent<AudioListener>();
            root.AddComponent<JuiceDriver>();
            cam = camObj.GetComponent<Camera>();
        }
        public static void ResetMatch()
        {
            streak[0] = streak[1] = 0; excitement = 0f; lastPhase = null; lastSec = 99; startPlayed = false;
            lastBlueA = lastRedA = true; ScorePop = BeepPulse = 0f;
        }

        // ---- helpers ----
        static Camera Cam { get { if (cam == null) cam = Camera.main; return cam; } }
        static bool PlayerIsBlue { get { var m = GameSession.Mech; return m == null || m.TargetHub.x < SimConstants.FieldLength * 0.5f; } }
        public static float Pan(Vector3 p)
        {
            var c = Cam; if (c == null) return 0f;
            Vector3 d = p - c.transform.position; float m = d.magnitude; if (m < 0.01f) return 0f;
            return Mathf.Clamp(Vector3.Dot(c.transform.right, d / m) * 0.8f, -1f, 1f);
        }
        public static float Atten(Vector3 p)
        {
            var c = Cam; if (c == null) return 1f;
            return 1f / (1f + (p - c.transform.position).magnitude * 0.12f);
        }
        static Vector3 HubPos(bool blue)
        {
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth, d = SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f;
            return new Vector3(blue ? d : L - d, 1.9f, W / 2f);
        }
        public static Matrix4x4 PopMatrix(Vector2 pivot, float s)   // for IMGUI: GUI.matrix = GUI.matrix * PopMatrix(...)
        {
            return Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one) * Matrix4x4.Scale(new Vector3(s, s, 1f)) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        }
        public static void HitStop(float seconds)   // internal-physics mode only (HALSim runs against wall-clock)
        {
            return;   // hit-stop 已停用:把 timeScale 拉到 0.09 會和剛體物理/插值互相干擾(球穿透嫌疑),手感改用鏡頭震動+粒子
            if (GameSession.Hal != null || Time.timeScale == 0f || JuiceSettings.Shake <= 0f) return;
            hitT = Mathf.Max(hitT, seconds); Time.timeScale = HitScale;
        }

        // ---- event handlers (called from the existing code, see section 2.9) ----
        public static void OnScore(bool blueHub, bool active, Vector3 pos)
        {
            bool mine = blueHub == PlayerIsBlue; int side = blueHub ? 0 : 1;
            if (!active) { SfxBus.Play("thump", 0.25f, 1.5f, Pan(pos)); Fx.Dust(pos); return; }   // dull "no points" thunk
            float now = Time.unscaledTime;
            streak[side] = (now - lastScoreT[side] < 2.5f) ? streak[side] + 1 : 1; lastScoreT[side] = now;
            int s = streak[side];
            float pitch = Mathf.Pow(2f, Scale[Mathf.Min(s - 1, Scale.Length - 1)] / 12f);
            SfxBus.Play("ballin", mine ? 0.8f : 0.4f, Random.Range(0.92f, 1.12f), Pan(pos));
               // octave sparkle on every 5th
            Fx.HubScore(pos, blueHub, s);
            excitement = Mathf.Min(1.2f, excitement + (mine ? 0.35f : 0.2f));
            if (mine)
            {
                ScorePop = 1f;
                JuiceFX.AddTrauma(0.12f, 0.5f); JuiceFX.FovPunch(1.2f);
                Rumble.Driver(0f, 0.35f, 0.08f);
                
            }
        }
        public static void OnShot(RobotMechanisms m, Vector3 pos, Vector3 dir)
        {
            bool mine = m == GameSession.Mech; float a = mine ? 1f : Atten(pos) * 0.6f;
            SfxBus.Play("shoot", 0.55f * a, 0.92f + Random.value * 0.16f + m.FlywheelRps * 0.0015f, Pan(pos));
            Fx.Muzzle(pos, dir);
            if (mine)
            {
                JuiceFX.AddTrauma(0.035f, 0.22f); JuiceFX.Kick(-dir * 0.015f);
                Rumble.Operator(0.2f, 0.4f, 0.07f);
            }
        }
        public static void OnCollect(RobotMechanisms m, Vector3 pos)
        {
            bool mine = m == GameSession.Mech; float a = mine ? 1f : Atten(pos) * 0.5f;
            SfxBus.Play("pluck", 0.35f * a, 1f + Mathf.Min(m.Held, 40) * 0.012f, Pan(pos));
            Fx.Collect(pos);
            if (mine) Rumble.Driver(0f, 0.12f, 0.04f);
        }
        public static void OnBallImpact(Vector3 pos, float v)
        {
            float now = Time.unscaledTime; if (now < nextBall) return; nextBall = now + 0.02f;   // global cap 50/s
            float vol = Mathf.Clamp01(v / 7f) * 0.55f * Atten(pos);
            if (vol < 0.02f) return;
            SfxBus.Play("ballhit", vol, 0.85f + Random.value * 0.35f, Pan(pos));
            if (v > 4f) Fx.Dust(pos);
        }
        public static void OnRobotBump(SwerveDrive d, Vector3 pos, float v)
        {
            if (v < 1f) return; float now = Time.unscaledTime; if (now < nextBump) return; nextBump = now + 0.15f;
            bool mine = d == GameSession.Drive; float k = Mathf.Clamp01(v / 6f);
            SfxBus.Play("bump", (0.35f + 0.65f * k) * (mine ? 1f : Atten(pos)), 0.85f + 0.3f * (1f - k), Pan(pos));
            Fx.Bump(pos, k);
            if (mine)
            {
                JuiceFX.AddTrauma(0.15f + 0.45f * k, 0.8f);
                Rumble.Driver(0.3f + 0.7f * k, 0.2f + 0.5f * k, 0.12f + 0.2f * k);
                if (k > 0.7f) HitStop(0.04f);
            }
        }
        public static void UiClick() { SfxBus.Play("uiclick", 0.5f); }
        public static void UiHover() { SfxBus.Play("tick", 0.25f, 1.1f); }

        // ---- per-frame ----
        static bool wasOnBump;
        public static void Tick(float dt)
        {
            if (GameSession.Drive != null)   // 過 BUMP:車身俯仰超過門檻的瞬間(上坡/下坡各一次)放「咚、咚」
            {
                bool onBump = Mathf.Abs(GameSession.Drive.BumpPitchDeg) > 4f;
                if (onBump != wasOnBump && GameSession.Drive.Speed > 0.6f) { SfxBus.Play("ramp", Mathf.Clamp01(0.3f + GameSession.Drive.Speed * 0.12f), 0.9f + Random.value * 0.2f); JuiceFX.AddTrauma(0.06f, 0.15f); }
                wasOnBump = onBump;
            }
            excitement = Mathf.MoveTowards(excitement, 0f, dt * 0.22f);
            ScorePop = Mathf.MoveTowards(ScorePop, 0f, dt * 3.2f);
            BeepPulse = Mathf.MoveTowards(BeepPulse, 0f, dt * 2.5f);
            if (hitT > 0f) { hitT -= dt; if (hitT <= 0f && Mathf.Approximately(Time.timeScale, HitScale)) Time.timeScale = 1f; }
            Rumble.Tick(dt);
            if (!SfxBus.Enabled) return;

            var m = GameSession.Mech;
            bool play = m != null && m.Drive != null && !MenuSystem.Blocking;
            float rps = 0f, fly = 0f;
            if (play)
            {
                rps = m.Sim != null ? m.RollerRps : (m.IntakeDown ? 40f * Mathf.Clamp01(m.ArmExt / RobotMechanisms.ArmMax) : 0f);
                fly = m.FlywheelRps;
            }
            SfxBus.SetLoop("intake", "intake_loop", rps > 6f ? 0.10f + 0.10f * Mathf.Clamp01(rps / 40f) : 0f, Mathf.Clamp(0.5f + rps / 40f * 0.7f, 0.4f, 2f));
            SfxBus.SetLoop("fly", "fly_loop", fly > 4f ? 0.08f + 0.10f * Mathf.Clamp01(fly / 80f) : 0f, Mathf.Clamp(0.4f + fly / 75f, 0.4f, 2.5f));
            SfxBus.SetLoop("crowd", "crowd_loop", play ? 0.03f + 0.07f * Mathf.Clamp01(excitement) : 0f, 1f + 0.10f * excitement, SfxGroup.Amb);
            SfxBus.SetLoop("music", "music_loop", play ? 0.35f : 0.6f, ScoreManager.Phase == "END GAME" ? 1.05f : 1f, SfxGroup.Music);

            if (!play || !ScoreManager.ClockOn) return;

            // match start horn (waits until the clock really runs: real-program mode holds MatchTime at 0 until the robot code is up)
            if (!startPlayed && ScoreManager.MatchTime > 0.1f && ScoreManager.MatchTime < 5f)
            { startPlayed = true; SfxBus.Play("stinger_start", 0.8f, 1f, 0f, SfxGroup.Music); SfxBus.Duck(0.4f, 1.2f); }

            string ph = ScoreManager.Phase;
            if (ph != lastPhase) { OnPhase(lastPhase, ph); lastPhase = ph; }

            // countdown beeps, last 10 s (rising pitch, louder in the last 3)
            int sec = Mathf.CeilToInt(ScoreManager.TimeLeft);
            if (!ScoreManager.Ended && ScoreManager.MatchTime > 30f && sec <= 10 && sec > 0 && sec != lastSec)
            {
                SfxBus.Play("beep", sec <= 3 ? 0.7f : 0.45f, 1f + (10 - sec) * 0.03f); BeepPulse = 1f;
            }
            lastSec = sec;

            // hub became active (SHIFT swap): ring flash + tick
            if (ph.StartsWith("SHIFT"))
            {
                if (ScoreManager.BlueActive && !lastBlueA) { Fx.HubFlash(HubPos(true), true); SfxBus.Play("tick", 0.5f, 0.8f); }
                if (ScoreManager.RedActive && !lastRedA) { Fx.HubFlash(HubPos(false), false); SfxBus.Play("tick", 0.5f, 0.8f); }
            }
            lastBlueA = ScoreManager.BlueActive; lastRedA = ScoreManager.RedActive;
        }

        static void OnPhase(string from, string to)
        {
            if (from == null) return;                      // first frame: no sound
            if (to == "TRANSITION") { SfxBus.Play("buzzer", 0.45f, 1.5f); SfxBus.Duck(0.5f, 0.8f); }
            else if (to.StartsWith("SHIFT")) { SfxBus.Play("beep", 0.4f, 0.75f); }
            else if (to == "END GAME") { SfxBus.Play("stinger_end", 0.8f, 1f, 0f, SfxGroup.Music); SfxBus.Duck(0.45f, 1.4f); JuiceFX.AddTrauma(0.2f, 0.3f); }
            else if (to == "SCORING GRACE")
            {
                SfxBus.Play("buzzer", 1f); SfxBus.Duck(0.3f, 2.0f); 
                JuiceFX.AddTrauma(0.35f, 0.5f); Rumble.Pulse(Pad.DriverIndex, 0.6f, 0.4f, 0.5f);
            }
            else if (to == "MATCH OVER")
            {
                bool tie = ScoreManager.BlueScore == ScoreManager.RedScore;
                bool win = PlayerIsBlue ? ScoreManager.BlueScore > ScoreManager.RedScore : ScoreManager.RedScore > ScoreManager.BlueScore;
                SfxBus.PlayDelayed(win ? "stinger_win" : "stinger_lose", 0.8f, tie ? 0.6f : 1f, 1f, 0f, SfxGroup.Music);
                if (win) { var c = Cam; if (c != null) Fx.Confetti(c.transform.position + c.transform.forward * 5f + Vector3.up * 1.5f); }
            }
        }
    }
}