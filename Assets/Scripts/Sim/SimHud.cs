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

        bool wasPadOk = true, wasLoading; float padLostUntil;
        void Start() { born = Time.unscaledTime; }

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug;
            if (Input.GetKeyDown(KeyCode.F5) && !MenuSystem.Blocking) GameSession.Rematch();
            { bool loading = GameSession.Hal != null && !GameSession.Hal.Connected && !GameSession.Hal.Failed; if (loading) { Time.timeScale = 0f; wasLoading = true; } else if (wasLoading) { wasLoading = false; Time.timeScale = 1f; if (GameSession.Hal != null && GameSession.Hal.LoadedSecs > 5f) { PlayerPrefs.SetFloat("robotLoadSecs", GameSession.Hal.LoadedSecs); PlayerPrefs.Save(); } } }
            if (GameSession.AutoDrillTest && GameSession.Drill && Mech != null) Mech.Shooting = Mech.Held > 0;
            bool padOk = Pad.Mode != 2 || (Pad.DriverOnline && (Pad.OperatorOnline || Pad.OperatorKeyboard));
            if (wasPadOk && !padOk && GameSession.Active && !MenuSystem.Blocking) { padLostUntil = Time.unscaledTime + 8f; MenuSystem.PauseNow(); }   // 手把掉線:自動暫停並提示
            wasPadOk = padOk;
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; born = showHelp ? Time.unscaledTime - 9999f : Time.unscaledTime - 9999f; }
        }

        static GUIStyle Style(int size, Color c, TextAnchor a = TextAnchor.UpperLeft, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, font = UiTheme.Font, alignment = a, fontStyle = fs, wordWrap = false };
            s.normal.textColor = c;
            return s;
        }

        static void Rounded(Rect r, Color c, float rad = 10f)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(rad, rad, rad, rad));
        }

        static void Outline(Rect r, Color c, float rad, float w)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, new Vector4(w, w, w, w), new Vector4(rad, rad, rad, rad));
        }

        // 液態玻璃風:半透明深色底(確保字看得清)+ 淡藍白漸層高光 + 亮邊框 + 大圓角
        static readonly GlassStyle stHud = GlassStyle.HudPanel(), stCard = GlassStyle.HudCard(), stPill = GlassStyle.Pill(), stPrim = GlassStyle.PillPrimary(), stTrack = GlassStyle.BarTrack();
        static void Panel(Rect r, float alpha = 0.55f)
        {
            if (UiGlass.Ready) { GlassGL.Draw(r, Mathf.Min(alpha >= 0.9f ? 44f : 22f, r.height * 0.5f), alpha >= 0.9f ? stCard : stHud); return; }
            float rad = Mathf.Min(18f, r.height * 0.5f);
            Rounded(new Rect(r.x - 1, r.y + 3, r.width + 2, r.height + 2), new Color(0f, 0f, 0f, 0.18f), rad + 2f);
            Rounded(r, new Color(0.05f, 0.09f, 0.16f, Mathf.Clamp01(alpha * 0.55f + 0.18f)), rad);
            Rounded(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height * 0.5f), new Color(0.75f, 0.88f, 1f, 0.10f), rad - 2f);
            Outline(r, new Color(0.85f, 0.93f, 1f, 0.42f), rad, 1.5f);
        }
        // 玻璃按鈕:膠囊、滑過變亮;回傳是否被點擊
        class BtnAnim { public Spring hov = new Spring(0), prs = new Spring(0); }
        static readonly System.Collections.Generic.Dictionary<string, BtnAnim> anims = new System.Collections.Generic.Dictionary<string, BtnAnim>();
        // 玻璃按鈕:膠囊 + 彈簧(滑過微微放大、按下縮);回傳是否被點擊
        static bool GBtn(Rect r, string text, bool primary)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (!UiGlass.Ready)
            {
                Color fill = primary ? new Color(0.35f, 0.65f, 1f, hover ? 0.75f : 0.55f) : new Color(1f, 1f, 1f, hover ? 0.22f : 0.12f);
                Rounded(r, fill, r.height / 2f);
                Outline(r, new Color(1f, 1f, 1f, hover ? 0.8f : 0.4f), r.height / 2f, 1.5f);
                GUI.Label(r, text, Style(20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
                return GUI.Button(r, GUIContent.none, GUIStyle.none);
            }
            if (!anims.TryGetValue(text, out var a)) anims[text] = a = new BtnAnim();
            bool down = hover && Input.GetMouseButton(0);
            if (Event.current.type == EventType.Repaint)
            { a.hov.target = hover ? 1 : 0; a.prs.target = down ? 1 : 0; a.hov.Step(Time.unscaledDeltaTime, .22f, .8f); a.prs.Step(Time.unscaledDeltaTime, .18f, .7f); }
            float sc = 1f + 0.02f * a.hov.x - 0.04f * a.prs.x;
            var rr = new Rect(r.center.x - r.width * sc / 2f, r.center.y - r.height * sc / 2f, r.width * sc, r.height * sc);
            GlassGL.Draw(rr, rr.height / 2f, primary ? stPrim : stPill, a.hov.x, a.prs.x, 1f, 0f);
            GUI.Label(rr, text, Style(20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
        static readonly System.Collections.Generic.Dictionary<Color, GlassStyle> fillStyles = new System.Collections.Generic.Dictionary<Color, GlassStyle>();
        static void Bar(Rect r, float frac, Color fill)
        {
            if (UiGlass.Ready)
            {
                GlassGL.Draw(r, r.height / 2f, stTrack);
                if (frac > 0.01f)
                {
                    if (!fillStyles.TryGetValue(fill, out var fs)) fillStyles[fill] = fs = GlassStyle.BarFill(fill);
                    GlassGL.Draw(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * Mathf.Clamp01(frac)), r.height), r.height / 2f, fs);
                }
                return;
            }
            Rounded(r, new Color(1f, 1f, 1f, 0.12f), 5f);
            if (frac > 0.01f) Rounded(new Rect(r.x, r.y, Mathf.Max(8f, r.width * Mathf.Clamp01(frac)), r.height), fill, 5f);
        }
        void OnGUI()
        {
            if (Drive == null) return;
            if (Event.current.type == EventType.Repaint) UiGlass.HudFrame = Time.frameCount;
            GUI.depth = -100;   // HUD 畫在最上層(超取樣貼圖在 depth 1000,畫在最底)
            // 依螢幕高度縮放(高解析度螢幕上字不會太小/面板不會太擠)
            float sc = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.8f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float W = Screen.width / sc, H = Screen.height / sc;
            var dim = new Color(0.78f, 0.84f, 0.92f);

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

            // ---- 左上面板:車速 / 持球條 / 飛輪條 / 射擊預測
            float x0 = 12f, y0 = 12f, pw = 330f;
            float ph = Mech != null ? 150f : 44f;
            if (showDebug) ph += 22f * 3;
            var pr = new Rect(x0, y0, pw, ph);
            Panel(pr, 0.6f);
            GUI.Label(new Rect(x0 + 14, y0 + 8, pw - 28, 24), $"{Drive.Speed:0.0} m/s    {L("朝向", "hdg")} {Mathf.DeltaAngle(0f, Drive.HeadingRad * Mathf.Rad2Deg):0}°    {fps:0} FPS    {L("手把", "pads")} {Pad.Count}", Style(15, dim));
            if (Mech != null)
            {
                float yy = y0 + 38f;
                GUI.Label(new Rect(x0 + 14, yy, 120, 22), L("儲球", "Fuel"), Style(14, dim));
                GUI.Label(new Rect(x0 + pw - 114, yy, 100, 22), $"{Mech.Held} / {RobotMechanisms.Capacity}", Style(16, Color.white, TextAnchor.UpperRight, FontStyle.Bold));
                Bar(new Rect(x0 + 14, yy + 24, pw - 28, 8), Mech.Held / (float)RobotMechanisms.Capacity, new Color(1f, 0.85f, 0.1f));
                yy += 42f;
                bool spun = Mech.FlywheelRps > 8f;
                GUI.Label(new Rect(x0 + 14, yy, 150, 22), L("飛輪", "Flywheel") + $" {Mech.FlywheelRps:0} rps", Style(14, dim));
                string st2 = Mech.Shooting ? L("發射中", "FIRING") : (spun ? L("轉速中", "SPINNING") : L("待機", "IDLE"));
                GUI.Label(new Rect(x0 + pw - 134, yy, 120, 22), st2, Style(14, Mech.Shooting ? new Color(0.4f, 1f, 0.5f) : dim, TextAnchor.UpperRight, FontStyle.Bold));
                Bar(new Rect(x0 + 14, yy + 24, pw - 28, 8), Mech.FlywheelRps / 40f, Mech.ShotWillScore ? new Color(0.3f, 1f, 0.4f) : new Color(0.4f, 0.7f, 1f));
                yy += 42f;
                string hint = Mech.ShotHint != "" ? Mech.ShotHint : L("開飛輪後顯示預測落點", "spin flywheel to preview shot");
                Color hc = Mech.ShotHint == "" ? new Color(1f, 1f, 1f, 0.45f) : (Mech.ShotWillScore ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.7f, 0.25f));
                GUI.Label(new Rect(x0 + 14, yy, pw - 28, 22), hint + $"      {L("吸球器", "intake")} " + (Mech.IntakeDown ? L("放下", "DOWN") : L("收起", "up")) + (Mech.Feeding ? L(" · 送球", " · feed") : "") + (Mech.Ejecting ? L(" · 吐球", " · eject") : ""), Style(14, hc, TextAnchor.UpperLeft, FontStyle.Bold));
            }
            if (showDebug)
            {
                Vector2 p = Drive.Pose2d;
                float dy = y0 + ph - 66f;
                GUI.Label(new Rect(x0 + 14, dy, pw - 20, 20), $"x {p.x:0.00}  y {p.y:0.00} m   ω {Drive.Omega:0.0} rad/s  {(Drive.FieldCentric ? L("場地座標", "field") : L("車體座標", "robot"))}", Style(12, dim));
                if (Mech != null) GUI.Label(new Rect(x0 + 14, dy + 20, pw - 20, 20), $"{L("仰角板", "hood")} {Mech.HoodDeg:0.0}°   {L("距 HUB", "hub dist")} {Mech.TargetDistance:0.00} m   {L("已射", "shots")} {Mech.ShotsFired}", Style(12, dim));
                if (GameSession.Hal != null) GUI.Label(new Rect(x0 + 14, dy + 40, pw - 20, 20), L("機器人程式: ", "ROBOT CODE: ") + GameSession.Hal.Status + L("  訊息 ", "  msgs ") + GameSession.Hal.MessagesIn, Style(12, dim));
            }
            GUI.Label(new Rect(pr.x + 6, pr.yMax + 2, 300, 20), L("F1 按鍵說明   F3 詳細數據", "F1 help   F3 details"), Style(13, new Color(1f, 1f, 1f, 0.75f)));
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

            if (Time.unscaledTime < padLostUntil) { var tr = new Rect(cx - 250, H - 130, 500, 44); Panel(tr, 0.9f); GUI.Label(tr, L("⚠ 手把掉線了,已暫停。請重新插上 USB", "⚠ Controller lost - paused. Re-plug USB"), Style(18, new Color(1f, 0.8f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold)); }

            // ---- 比賽結束:成績卡(含各距離命中率) + 再來一場 / 回主選單(手把 A / B,Enter 再來一場)
            if (ScoreManager.ClockOn && ScoreManager.Ended)
            {
                int rows = 0; for (int q = 0; q < 5; q++) if (ShotLog.Att[q] > 0) rows++;
                float extra = rows > 0 ? rows * 26f + 34f : 0f;
                float cw = 560, ch = 372 + extra;
                var cr = new Rect(cx - cw / 2f, H / 2f - ch / 2f, cw, ch);
                Panel(cr, 0.95f);
                int bs = ScoreManager.BlueScore, rs = ScoreManager.RedScore;
                string res = bs > rs ? L("勝利", "VICTORY") : bs < rs ? L("落敗", "DEFEAT") : L("平手", "DRAW");
                Color rc2 = bs > rs ? new Color(0.4f, 1f, 0.55f) : bs < rs ? new Color(1f, 0.5f, 0.45f) : new Color(1f, 0.9f, 0.4f);
                GUI.Label(new Rect(cr.x, cr.y + 22, cw, 30), L("比賽結束", "MATCH OVER"), Style(18, dim, TextAnchor.UpperCenter, FontStyle.Bold));
                GUI.Label(new Rect(cr.x, cr.y + 54, cw, 70), res, Style(54, rc2, TextAnchor.UpperCenter, FontStyle.Bold));
                GUI.Label(new Rect(cr.x, cr.y + 132, cw, 50), $"{L("藍", "BLUE")} {bs}  :  {rs} {L("紅", "RED")}", Style(34, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                if (Mech != null) { int sh = ShotLog.DrillShots, hi = ShotLog.DrillHits; GUI.Label(new Rect(cr.x, cr.y + 182, cw, 28), $"{L("你發射", "Shots")} {sh}    {L("進球", "Scored")} {hi}    {L("命中率", "Accuracy")} {(sh > 0 ? hi * 100 / sh : 0)}%    {L("吸球", "Collected")} {Mech.TotalCollected}", Style(16, dim, TextAnchor.UpperCenter)); }
                if (rows > 0)
                {
                    float ry = cr.y + 216f;
                    GUI.Label(new Rect(cr.x + 40, ry - 4, cw - 80, 22), L("各距離命中率(發射時離 HUB)", "Accuracy by distance"), Style(13, dim, TextAnchor.UpperLeft, FontStyle.Bold));
                    ry += 22f;
                    for (int q = 0; q < 5; q++)
                    {
                        if (ShotLog.Att[q] == 0) continue;
                        float fr = ShotLog.Hit[q] / (float)ShotLog.Att[q];
                        GUI.Label(new Rect(cr.x + 40, ry, 70, 22), ShotLog.Names[q], Style(14, dim, TextAnchor.UpperLeft));
                        Bar(new Rect(cr.x + 112, ry + 6, cw - 112 - 150, 9), fr, fr >= 0.6f ? new Color(0.4f, 1f, 0.5f) : fr >= 0.3f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.5f, 0.4f));
                        GUI.Label(new Rect(cr.x + cw - 140, ry, 100, 22), $"{ShotLog.Hit[q]}/{ShotLog.Att[q]}  {Mathf.RoundToInt(fr * 100f)}%", Style(14, Color.white, TextAnchor.UpperRight, FontStyle.Bold));
                        ry += 26f;
                    }
                }
                if (GBtn(new Rect(cr.x + 40, cr.y + 256 + extra, 230, 56), L("再來一場  (A)", "Rematch  (A)"), true) || Pad.Down(Pad.A) || Input.GetKeyDown(KeyCode.Return)) GameSession.Rematch();
                if (GBtn(new Rect(cr.x + cw - 270, cr.y + 256 + extra, 230, 56), L("回主選單  (B)", "Main menu  (B)"), false) || Pad.Down(Pad.B)) MenuSystem.GoMain();
            }

            // ---- 投籃訓練:頂部橫幅 + 射完的結果卡
            if (GameSession.Drill && Mech != null)
            {
                int sh2 = ShotLog.DrillShots, hi2 = ShotLog.DrillHits;
                var br = new Rect(cx - 230, 90, 460, 34);
                Panel(br, 0.6f);
                GUI.Label(br, $"{L("投籃訓練", "Shooting drill")}   {GameSession.DrillDist:0.0} m   {L("剩", "left")} {Mech.Held}   {L("進", "hit")} {hi2}/{sh2}", Style(16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
                bool done = Mech.Held <= 0 && sh2 >= GameSession.DrillBalls && Time.time - Mech.LastFireTime > 4f;
                if (done)
                {
                    float dw = 520, dh = 300;
                    var dr = new Rect(cx - dw / 2f, H / 2f - dh / 2f, dw, dh);
                    Panel(dr, 0.95f);
                    int pct = sh2 > 0 ? hi2 * 100 / sh2 : 0;
                    GUI.Label(new Rect(dr.x, dr.y + 20, dw, 28), $"{L("訓練結果", "Drill result")}  ·  {GameSession.DrillDist:0.0} m", Style(18, dim, TextAnchor.UpperCenter, FontStyle.Bold));
                    GUI.Label(new Rect(dr.x, dr.y + 54, dw, 70), $"{hi2} / {sh2}", Style(56, pct >= 60 ? new Color(0.4f, 1f, 0.55f) : pct >= 30 ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.55f, 0.45f), TextAnchor.UpperCenter, FontStyle.Bold));
                    GUI.Label(new Rect(dr.x, dr.y + 128, dw, 30), $"{L("命中率", "Accuracy")} {pct}%   " + (pct >= 70 ? L("很穩!試試更遠", "Solid! try further") : pct >= 40 ? L("不錯,再微調", "Decent, tune it") : L("距離不對,換個距離看看", "Try another distance")), Style(16, dim, TextAnchor.UpperCenter));
                    if (GBtn(new Rect(dr.x + 24, dr.y + 200, 150, 52), L("同距離 (A)", "Again (A)"), true) || Pad.Down(Pad.A) || Input.GetKeyDown(KeyCode.Return)) GameSession.StartDrill();
                    if (GBtn(new Rect(dr.x + 185, dr.y + 200, 150, 52), L("+0.5 m (Y)", "+0.5 m (Y)"), false) || Pad.Down(Pad.Y)) { GameSession.DrillDist = GameSession.DrillDist >= 5.5f ? 2.0f : GameSession.DrillDist + 0.5f; GameSession.StartDrill(); }
                    if (GBtn(new Rect(dr.x + 346, dr.y + 200, 150, 52), L("結束 (B)", "Exit (B)"), false) || Pad.Down(Pad.B)) { GameSession.Rematch(); }
                }
            }
            // ---- 開局提醒(例如專案沒有模擬設定檔、改用內建行為)
            if (GameSession.Notice != "" && Time.time < GameSession.NoticeUntil)
            {
                float nw = Mathf.Min(W - 20, 820);
                Panel(new Rect(cx - nw / 2f, 124, nw, 48), 0.85f);
                var ns = Style(15, new Color(1f, 0.85f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold); ns.wordWrap = true;
                GUI.Label(new Rect(cx - nw / 2f + 12, 128, nw - 24, 44), GameSession.Notice, ns);
            }

            // ---- 真實程式模式的 AUTO 階段:機器人程式自己跑,手把無效(符合真實比賽),提示並可按 N 跳過
            if (GameSession.Hal != null && GameSession.Hal.Connected && ScoreManager.ClockOn && ScoreManager.MatchTime < 20f)
            {
                Panel(new Rect(cx - 280, 90, 560, 30), 0.75f);
                GUI.Label(new Rect(cx - 280, 93, 560, 26), L("自動階段:你的機器人程式自己跑,手把暫時無效 — 按 N 跳過", "AUTO: your robot code is driving, sticks disabled — press N to skip"), Style(15, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 真實程式載入中:整個遊戲凍結,顯示進度 %(載入完才開始計時、才能動)
            if (GameSession.Hal != null && !GameSession.Hal.Connected)
            {
                var hal = GameSession.Hal;
                float pg = hal.LoadProgress;
                float lw = 620, lh = 190;
                var lr = new Rect(cx - lw / 2f, H * 0.5f - lh / 2f, lw, lh);
                Panel(lr, 0.95f);
                if (hal.Failed)
                {
                    GUI.Label(new Rect(lr.x, lr.y + 28, lw, 36), L("機器人程式啟動失敗", "Robot code failed to start"), Style(24, new Color(1f, 0.6f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
                    GUI.Label(new Rect(lr.x + 20, lr.y + 74, lw - 40, 80), hal.Status, Style(14, dim, TextAnchor.UpperCenter));
                }
                else
                {
                    GUI.Label(new Rect(lr.x, lr.y + 22, lw, 36), L("機器人程式載入中…", "Loading robot code…"), Style(24, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                    Bar(new Rect(lr.x + 40, lr.y + 78, lw - 80, 14), pg, new Color(0.4f, 0.75f, 1f));
                    GUI.Label(new Rect(lr.x, lr.y + 98, lw, 40), $"{Mathf.RoundToInt(pg * 100f)}%", Style(30, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                    string stg = pg < 0.25f ? L("啟動 Gradle…", "Starting Gradle…") : pg < 0.55f ? L("編譯機器人程式…", "Compiling…") : pg < 0.85f ? L("啟動模擬器…", "Launching sim…") : L("連線中…", "Connecting…");
                    GUI.Label(new Rect(lr.x, lr.y + 144, lw, 28), stg + "   " + L("載入完成前遊戲暫停,不會計時", "Game is paused until loaded"), Style(14, dim, TextAnchor.UpperCenter));
                }
            }
            // ---- 按鍵說明:開場 15 秒顯示,之後 F1 切換
            bool help = showHelp || Time.unscaledTime - born < 15f;
            if (help)
            {
                bool tank = PlayerPrefs.GetInt("tankMode", 1) == 1;
                string t1 = tank
                    ? L("手把:左/右搖桿 = 左/右側輪   A 放手臂   駕駛 RT 滾輪(單手把用 B)   操作 RT 發射(到速自動送球)   RB 強制送球   十字鍵左右 砲塔   十字鍵上 收手臂   Start 暫停",
                        "Pad: L/R stick = left/right side   A arm   B roller   RT shoot   RB feed   D-pad L/R turret   Start pause")
                    : L("WASD 移動   Q/E 旋轉   Shift 慢速   I 吸球   空白/滑鼠 射擊   F 場地/車體座標", "WASD move   Q/E rotate   Shift slow   I intake   Space/Mouse shoot   F field/robot");
                string t2 = L("H 人類球員放球   C 視角   R 重置   Esc 暫停   2P(紅):方向鍵移動  ,. 旋轉  / 吸球  右Ctrl 射擊", "H human-player chute   C camera   R reset   Esc pause   P2 (red): arrows move  ,. rotate  / intake  RCtrl shoot");
                bool bound = BindingMap.Loaded && tank && GameSession.Hal != null;
                if (bound)
                {
                    string cc = Pad2.Separate ? "" : L("   [單手把:砲塔=十字鍵左右,收球=B]", "   [single pad: turret = D-pad, intake = B]");
                    t1 = L("駕駛手  ", "Driver  ") + BindingMap.Line(true);
                    t2 = L("操作手  ", "Operator  ") + BindingMap.Line(false) + cc + "      " + L("(說明由程式的 RobotContainer 自動產生)", "(auto-generated from RobotContainer)");
                }                float hw = Mathf.Min(W - 20, 1100);
                Panel(new Rect(cx - hw / 2f, H - (bound ? 84 : 62), hw, bound ? 74 : 52), 0.6f);
                var hs = Style(14, dim);
                GUI.Label(new Rect(cx - hw / 2f + 12, H - (bound ? 80 : 58), hw - 16, 22), t1, hs);
                GUI.Label(new Rect(cx - hw / 2f + 12, H - (bound ? 58 : 36), hw - 16, 22), t2, hs);
                if (bound) GUI.Label(new Rect(cx - hw / 2f + 12, H - 36, hw - 16, 22), L("H 球員放球   C 視角   R 重置   F5 再來一場   Esc 暫停   F1 隱藏說明", "H chute   C camera   R reset   F5 rematch   Esc pause   F1 hide"), hs);
            }
        }
    }
}
