using UnityEngine;

namespace FrcSim
{
    // 精簡 HUD:頂部中央 = 比分與計時;左上 = 一個小面板(速度/持球/吸球/飛輪/射擊);
    // F3 顯示詳細數據、F1 顯示按鍵說明(開場 15 秒自動顯示)。真實程式啟動中時中央有提示。
    public class SimHud : MonoBehaviour
    {
        public SwerveDrive Drive;
        public CameraRig Rig;
        public RobotMechanisms Mech;
        float fps;
        float born;
        bool showDebug, showHelp;
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

        void Start() { born = Time.unscaledTime; }

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug;
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; born = showHelp ? Time.unscaledTime - 9999f : Time.unscaledTime - 9999f; }
        }

        static GUIStyle Style(int size, Color c, TextAnchor a = TextAnchor.UpperLeft, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, font = UiTheme.Font, alignment = a, fontStyle = fs, wordWrap = false };
            s.normal.textColor = c;
            return s;
        }

        static void Panel(Rect r, float alpha = 0.55f)
        {
            var old = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.10f, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.30f, 0.50f, 0.75f, 0.45f);
            GUI.DrawTexture(new Rect(r.x, r.y, 3f, r.height), Texture2D.whiteTexture);   // 左側細強調條
            GUI.color = old;
        }

        void OnGUI()
        {
            if (Drive == null) return;
            GUI.depth = -100;   // HUD 畫在最上層(超取樣貼圖在 depth 1000,畫在最底)
            // 依螢幕高度縮放(高解析度螢幕上字不會太小/面板不會太擠)
            float sc = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.8f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float W = Screen.width / sc, H = Screen.height / sc;
            var dim = new Color(0.78f, 0.84f, 0.92f);

            // ---- 左上小面板
            var lines = new System.Collections.Generic.List<string>();
            lines.Add($"{Drive.Speed:0.0} m/s   {L("朝向", "hdg")} {Drive.HeadingRad * Mathf.Rad2Deg:0}°   FPS {fps:0}");
            if (Mech != null)
            {
                lines.Add($"{L("持球", "Fuel")} {Mech.Held}/{RobotMechanisms.Capacity}   {L("吸球器", "intake")} {(Mech.IntakeDown ? L("放下", "DOWN") : L("收起", "up"))}   {L("球道", "chute")} {HumanPlayer.BlueChute}(H)");
                lines.Add($"{L("飛輪", "flywheel")} {Mech.FlywheelRps:0} rps   {(Mech.Shooting ? (Mech.Ready ? L("發射中", "firing") : L("加速中…", "spinning up…")) : L("待機", "idle"))}   {L("已射", "shots")} {Mech.ShotsFired}");
            }
            if (showDebug)
            {
                Vector2 p = Drive.Pose2d;
                lines.Add($"x {p.x:0.00}  y {p.y:0.00} m   ω {Drive.Omega:0.0} rad/s   {(Drive.FieldCentric ? L("場地座標", "field") : L("車體座標", "robot"))}  {Rig.ModeName}");
                if (Mech != null) lines.Add($"{L("仰角板", "hood")} {Mech.HoodDeg:0.0}°   {L("距 HUB", "hub dist")} {Mech.TargetDistance:0.00} m");
                if (GameSession.Hal != null) lines.Add(L("機器人程式: ", "ROBOT CODE: ") + GameSession.Hal.Status + L("  訊息 ", "  msgs ") + GameSession.Hal.MessagesIn);
            }
            float lh = 22f;
            var pr = new Rect(10, 10, 420, 14 + lines.Count * lh);
            Panel(pr);
            var st = Style(15, Color.white);
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(pr.x + 12, pr.y + 7 + i * lh, pr.width - 16, lh), lines[i], st);
            GUI.Label(new Rect(pr.x + 12, pr.yMax + 2, 300, 20), L("F1 按鍵說明   F3 詳細數據", "F1 help   F3 details"), Style(12, new Color(1f, 1f, 1f, 0.45f)));

            // ---- 頂部中央:比分 + 計時
            float cx = W / 2f;
            Panel(new Rect(cx - 150, 10, 300, 40), 0.6f);
            GUI.Label(new Rect(cx - 150, 12, 300, 38),
                $"{L("藍", "BLUE")} {ScoreManager.BlueScore}  :  {ScoreManager.RedScore} {L("紅", "RED")}", Style(26, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
            if (ScoreManager.ClockOn)
            {
                int tl = Mathf.CeilToInt(ScoreManager.Ended ? 0f : ScoreManager.TimeLeft);
                string hubs = ScoreManager.Ended ? "" : $"   {L("藍", "B")} HUB {(ScoreManager.BlueActive ? "●" : "○")}  {L("紅", "R")} HUB {(ScoreManager.RedActive ? "●" : "○")}";
                Panel(new Rect(cx - 220, 54, 440, 26), 0.5f);
                GUI.Label(new Rect(cx - 220, 56, 440, 24), $"{tl / 60}:{tl % 60:00}   {PhaseName(ScoreManager.Phase)}{hubs}", Style(15, dim, TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 真實程式模式的 AUTO 階段:機器人程式自己跑,手把無效(符合真實比賽),提示並可按 N 跳過
            if (GameSession.Hal != null && GameSession.Hal.Connected && ScoreManager.ClockOn && ScoreManager.MatchTime < 20f)
            {
                Panel(new Rect(cx - 280, 90, 560, 30), 0.75f);
                GUI.Label(new Rect(cx - 280, 93, 560, 26), L("自動階段:你的機器人程式自己跑,手把暫時無效 — 按 N 跳過", "AUTO: your robot code is driving, sticks disabled — press N to skip"), Style(15, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 真實程式啟動中提示
            if (GameSession.Hal != null && !GameSession.Hal.Connected)
            {
                Panel(new Rect(cx - 260, H * 0.42f, 520, 54), 0.75f);
                GUI.Label(new Rect(cx - 260, H * 0.42f + 6, 520, 24), L("機器人程式啟動中…", "Starting robot code…"), Style(20, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                GUI.Label(new Rect(cx - 260, H * 0.42f + 30, 520, 22), L("第一次約 20~60 秒;期間先用內建操控,連上後自動交給你的程式", "First start takes 20–60 s; built-in controls until it connects"), Style(13, dim, TextAnchor.UpperCenter));
            }

            // ---- 在自己的機器人上方標「你」
            if (hudCam == null) hudCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            var cam = hudCam;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(Drive.transform.position + Vector3.up * 1.3f);
                if (SuperSample.Scale > 1.01f) { sp.x /= SuperSample.Scale; sp.y /= SuperSample.Scale; }   // 超取樣時相機像素是螢幕的 Scale 倍
                if (sp.z > 0f)
                {
                    var ys = Style(16, new Color(1f, 0.92f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold);
                    var shadow = Style(16, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
                    var rc = new Rect(sp.x / sc - 40, (Screen.height - sp.y) / sc - 14 - 30, 80, 28);
                    GUI.Label(new Rect(rc.x + 1.2f, rc.y + 1.2f, rc.width, rc.height), L("▼ 你", "▼ YOU"), shadow);
                    GUI.Label(rc, L("▼ 你", "▼ YOU"), ys);
                }
            }

            // ---- 按鍵說明:開場 15 秒顯示,之後 F1 切換
            bool help = showHelp || Time.unscaledTime - born < 15f;
            if (help)
            {
                bool tank = PlayerPrefs.GetInt("tankMode", 1) == 1;
                string t1 = tank
                    ? L("手把:左/右搖桿 = 左/右側輪   A 放手臂   B 滾輪   RT 發射   RB 送球   十字鍵左右 砲塔   Start 暫停",
                        "Pad: L/R stick = left/right side   A arm   B roller   RT shoot   RB feed   D-pad L/R turret   Start pause")
                    : L("WASD 移動   Q/E 旋轉   Shift 慢速   I 吸球   空白/滑鼠 射擊   F 場地/車體座標", "WASD move   Q/E rotate   Shift slow   I intake   Space/Mouse shoot   F field/robot");
                string t2 = L("H 人類球員放球   C 視角   R 重置   Esc 暫停   2P(紅):方向鍵移動  ,. 旋轉  / 吸球  右Ctrl 射擊", "H human-player chute   C camera   R reset   Esc pause   P2 (red): arrows move  ,. rotate  / intake  RCtrl shoot");
                float hw = Mathf.Min(W - 20, 1100);
                Panel(new Rect(cx - hw / 2f, H - 62, hw, 52), 0.6f);
                var hs = Style(14, dim);
                GUI.Label(new Rect(cx - hw / 2f + 12, H - 58, hw - 16, 22), t1, hs);
                GUI.Label(new Rect(cx - hw / 2f + 12, H - 36, hw - 16, 22), t2, hs);
            }
        }
    }
}
