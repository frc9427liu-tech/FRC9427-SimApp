using UnityEngine;

namespace FrcSim
{
    public class SimHud : MonoBehaviour
    {
        public SwerveDrive Drive;
        public CameraRig Rig;
        public RobotMechanisms Mech;
        float fps;

        void Update() { fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f); }

        void OnGUI()
        {
            if (Drive == null) return;
            var s = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            s.normal.textColor = Color.white;
            Vector2 p = Drive.Pose2d;
            string txt =
                $"FRC 9427 Simulator  |  FPS {fps:0}\n" +
                $"Pose x {p.x:0.00}  y {p.y:0.00} m   heading {Drive.HeadingRad * Mathf.Rad2Deg:0}°\n" +
                $"speed {Drive.Speed:0.00} m/s  (max {SimConstants.MaxSpeed:0.0})   omega {Drive.Omega:0.0} rad/s\n" +
                $"mode {(Drive.FieldCentric ? "field-centric" : "robot-centric")}   view {Rig.ModeName}";
            GUI.Box(new Rect(10, 10, 560, 96), "");
            GUI.Label(new Rect(18, 14, 548, 90), txt, s);

            if (Mech != null)
            {
                string m =
                    $"Fuel held {Mech.Held}/{RobotMechanisms.Capacity}   intake {(Mech.IntakeDown ? "DOWN" : "up")}\n" +
                    $"flywheel {Mech.FlywheelRps:0.0} rps   hood {Mech.HoodDeg:0.0}°   dist to hub {Mech.TargetDistance:0.00} m\n" +
                    $"shooter {(Mech.Shooting ? (Mech.Ready ? "READY - firing" : "spinning up...") : "idle")}   shots {Mech.ShotsFired}";
                GUI.Box(new Rect(10, 112, 560, 76), "");
                GUI.Label(new Rect(18, 116, 548, 70), m, s);
            }

            if (GameSession.Hal != null)
            {
                var ntc = GameSession.Hal.GetComponent<NtSim>();
                string hs = "ROBOT CODE: " + GameSession.Hal.Status + "  msgs " + GameSession.Hal.MessagesIn + (ntc != null ? "  | Limelight " + ntc.Status + " tags " + ntc.TagsSeen : "");
                GUI.Box(new Rect(10, 194, 560, 30), "");
                GUI.Label(new Rect(18, 197, 548, 26), hs, s);
            }

            var big = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width / 2f - 130, 10, 260, 46), "");
            GUI.Label(new Rect(Screen.width / 2f - 130, 14, 260, 40),
                $"BLUE {ScoreManager.BlueScore}  :  {ScoreManager.RedScore} RED", big);

            if (ScoreManager.ClockOn)
            {
                var ts = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                ts.normal.textColor = Color.white;
                int tl = Mathf.CeilToInt(ScoreManager.Ended ? 0f : ScoreManager.TimeLeft);
                string hubs = ScoreManager.Ended ? "" : $"   BLUE HUB {(ScoreManager.BlueActive ? "ACTIVE" : "inactive")} | RED HUB {(ScoreManager.RedActive ? "ACTIVE" : "inactive")}";
                GUI.Box(new Rect(Screen.width / 2f - 260, 58, 520, 28), "");
                GUI.Label(new Rect(Screen.width / 2f - 260, 60, 520, 26), $"{tl / 60}:{tl % 60:00}   {ScoreManager.Phase}{hubs}", ts);
            }

            var h = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            h.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
            GUI.Label(new Rect(10, Screen.height - 54, 1300, 50),
                "WASD move   Q/E rotate   Shift slow   I intake   Space/Mouse shoot (auto-aim)   F field/robot   C camera   R reset   Esc pause\nP2 (red): arrows move   , . rotate   / intake   RCtrl shoot", h);
        }
    }
}
