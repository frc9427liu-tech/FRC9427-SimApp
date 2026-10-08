using UnityEngine;

namespace FrcSim
{
    public class SimHud : MonoBehaviour
    {
        public SwerveDrive Drive;
        public CameraRig Rig;
        public RobotMechanisms Mech;
        float fps;
        Camera hudCam;
        static bool Zh => Loc.Lang == "zh";
        static string L(string zh, string en) => Zh ? zh : en;
        static string PhaseName(string p)
        {
            if (!Zh) return p;
            if (p.StartsWith("SHIFT ")) return "輪替 " + p.Substring(6);
            switch (p)
            {
                case "AUTO": return "自動階段";
                case "TRANSITION": return "過渡期";
                case "END GAME": return "終局";
                case "SCORING GRACE": return "計分寬限";
                case "MATCH OVER": return "比賽結束";
                case "FREE PLAY": return "自由練習";
                default: return p;
            }
        }

        void Update() { fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f); }

        void OnGUI()
        {
            if (Drive == null) return;
            var s = new GUIStyle(GUI.skin.label) { fontSize = 16, font = UiTheme.Font };
            s.normal.textColor = Color.white;
            Vector2 p = Drive.Pose2d;
            string txt =
                $"FRC 9427 {L("模擬器","Simulator")}  |  FPS {fps:0}\n" +
                $"{L("位置","Pose")} x {p.x:0.00}  y {p.y:0.00} m   {L("朝向","heading")} {Drive.HeadingRad * Mathf.Rad2Deg:0}°\n" +
                $"{L("速度","speed")} {Drive.Speed:0.00} m/s  ({L("上限","max")} {SimConstants.MaxSpeed:0.0})   {L("角速度","omega")} {Drive.Omega:0.0} rad/s\n" +
                $"{L("模式","mode")} {(Drive.FieldCentric ? L("場地座標","field-centric") : L("車體座標","robot-centric"))}   {L("視角","view")} {Rig.ModeName}";
            GUI.Box(new Rect(10, 10, 560, 96), "");
            GUI.Label(new Rect(18, 14, 548, 90), txt, s);

            if (Mech != null)
            {
                string m =
                    $"{L("持球","Fuel held")} {Mech.Held}/{RobotMechanisms.Capacity}   {L("吸球器","intake")} {(Mech.IntakeDown ? L("放下","DOWN") : L("收起","up"))}\n" +
                    $"{L("飛輪","flywheel")} {Mech.FlywheelRps:0.0} rps   {L("仰角板","hood")} {Mech.HoodDeg:0.0}°   {L("距 HUB","dist to hub")} {Mech.TargetDistance:0.00} m\n" +
                    $"{L("射擊","shooter")} {(Mech.Shooting ? (Mech.Ready ? L("就緒・發射中","READY - firing") : L("加速中…","spinning up...")) : L("待機","idle"))}   {L("已射","shots")} {Mech.ShotsFired}";
                GUI.Box(new Rect(10, 112, 560, 76), "");
                GUI.Label(new Rect(18, 116, 548, 70), m, s);
            }

            if (GameSession.Hal != null)
            {
                var ntc = GameSession.Hal.GetComponent<NtSim>();
                string hs = L("機器人程式: ","ROBOT CODE: ") + GameSession.Hal.Status + L("  訊息 ","  msgs ") + GameSession.Hal.MessagesIn + (ntc != null ? "  | Limelight " + ntc.Status + " tags " + ntc.TagsSeen : "");
                GUI.Box(new Rect(10, 194, 560, 30), "");
                GUI.Label(new Rect(18, 197, 548, 26), hs, s);
            }

            var big = new GUIStyle(GUI.skin.label) { font = UiTheme.Font, fontSize = 28, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width / 2f - 130, 10, 260, 46), "");
            GUI.Label(new Rect(Screen.width / 2f - 130, 14, 260, 40),
                $"{L("藍","BLUE")} {ScoreManager.BlueScore}  :  {ScoreManager.RedScore} {L("紅","RED")}", big);

            if (ScoreManager.ClockOn)
            {
                var ts = new GUIStyle(GUI.skin.label) { font = UiTheme.Font, fontSize = 18, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                ts.normal.textColor = Color.white;
                int tl = Mathf.CeilToInt(ScoreManager.Ended ? 0f : ScoreManager.TimeLeft);
                string hubs = ScoreManager.Ended ? "" : $"   {L("藍","BLUE")} HUB {(ScoreManager.BlueActive ? L("啟動","ACTIVE") : L("關閉","inactive"))} | {L("紅","RED")} HUB {(ScoreManager.RedActive ? L("啟動","ACTIVE") : L("關閉","inactive"))}";
                GUI.Box(new Rect(Screen.width / 2f - 260, 58, 520, 28), "");
                GUI.Label(new Rect(Screen.width / 2f - 260, 60, 520, 26), $"{tl / 60}:{tl % 60:00}   {PhaseName(ScoreManager.Phase)}{hubs}", ts);
            }

            // 在自己的機器人上方標「你」,俯視/遠景時才分得出哪台是自己
            if (hudCam == null) hudCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            var cam = hudCam;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(Drive.transform.position + Vector3.up * 1.3f);
                if (sp.z > 0f)
                {
                    var ys = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, font = UiTheme.Font };
                    ys.normal.textColor = new Color(1f, 0.92f, 0.2f);
                    GUI.Label(new Rect(sp.x - 40, Screen.height - sp.y - 14, 80, 28), L("▼ 你", "▼ YOU"), ys);
                }
            }

            var h = new GUIStyle(GUI.skin.label) { fontSize = 14, font = UiTheme.Font };
            h.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
            GUI.Box(new Rect(6, Screen.height - 60, Mathf.Min(Screen.width - 12, 1240), 56), "");   // 半透明底框,避免提示被 3D 場景蓋住看不到
            GUI.Label(new Rect(10, Screen.height - 54, 1300, 50),
                L("WASD 移動   Q/E 旋轉   Shift 慢速   I 吸球   空白/滑鼠 射擊(自動瞄準)   F 場地/車體座標   C 視角   R 重置   Esc 暫停\n2P(紅): 方向鍵移動   , . 旋轉   / 吸球   右Ctrl 射擊","WASD move   Q/E rotate   Shift slow   I intake   Space/Mouse shoot (auto-aim)   F field/robot   C camera   R reset   Esc pause\nP2 (red): arrows move   , . rotate   / intake   RCtrl shoot"), h);
        }
    }
}
