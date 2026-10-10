using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FrcSim
{
    // 啟動畫面 → (第一次)選語言 → 主畫面 → 選模式 / 設定 / 操作說明;遊戲中 Esc 暫停選單。
    // 滑鼠、鍵盤(W/S 或 上/下、Enter、Esc)、Xbox 手把(左搖桿、A、B)都能操作。
    public class MenuSystem : MonoBehaviour
    {
        public static MenuSystem I;
        public static bool Blocking => I != null && (I.menuVisible || I.splashing);

        public CameraRig Rig;

        class MenuScreen
        {
            public GameObject Go;
            public Text Title;
            public Func<string> TitleFn;
            public List<UiButton> Buttons = new List<UiButton>();
            public List<Action> Refreshers = new List<Action>();
            public int Sel;
            public Action OnBack;
        }

        class Item
        {
            public Func<string> Text; public Action Click; public bool Enabled = true; public Func<bool> EnabledFn;
            public Item(Func<string> t, Action c, bool e = true) { Text = t; Click = c; Enabled = e; }
        }

        RectTransform root;
        Image dim;
        MenuScreen cur;
        bool menuVisible, splashing, inGameMenu;
        bool justOpened, shownNewer; string shownProgress = "";
        float prevAxis;
        Action settingsBack;

        // ---------------------------------------------------------------- 初始化
        void Awake()
        {
            I = this;
            var cg = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler));
            cg.transform.SetParent(transform, false);
            var canvas = cg.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var sc = cg.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 1f;
            root = cg.GetComponent<RectTransform>();

            dim = UiKit.Img("Dim", root, new Color(0, 0, 0, 0));
            UiKit.Stretch(dim.rectTransform);
        }

        void Start()
        {
            StartCoroutine(SplashRoutine());
        }

        // ---------------------------------------------------------------- 啟動畫面
        IEnumerator SplashRoutine()
        {
            splashing = true;
            var bg = UiKit.Img("Splash", root, Color.black);
            UiKit.Stretch(bg.rectTransform);
            var made = UiKit.Label("Made", bg.transform, Loc.T("splash.made"), 44, UiTheme.TextDim, TextAnchor.MiddleCenter);
            UiKit.PlaceTL(made.rectTransform, 460, 400, 1000, 70);
            var line = UiKit.Img("Line", bg.transform, UiTheme.Line);
            UiKit.PlaceTL(line.rectTransform, 760, 500, 400, 2);
            var team = UiKit.Label("Team", bg.transform, "FRC 9427", 110, UiTheme.Text, TextAnchor.MiddleCenter);
            team.fontStyle = FontStyle.Bold;
            UiKit.PlaceTL(team.rectTransform, 360, 530, 1200, 140);

            string startMenu = null; { var a = System.Environment.GetCommandLineArgs(); int mi = System.Array.IndexOf(a, "-menu"); if (mi >= 0 && mi + 1 < a.Length) startMenu = a[mi + 1]; }
            float t = 0f, total = startMenu != null ? 0.3f : 4.0f;
            var texts = new[] { made, team };
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                float a = t < 0.8f ? t / 0.8f : (t > total - 0.8f ? (total - t) / 0.8f : 1f);
                foreach (var tx in texts) { var c = tx.color; c.a = a; tx.color = c; }
                var lc = line.color; lc.a = a * 0.55f; line.color = lc;
                if (t > 0.4f && (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Pad.Down(Pad.A) || Pad.Down(Pad.Start))) break;
                yield return null;
            }
            Destroy(bg.gameObject);
            splashing = false;
            if (startMenu == "setup") { ShowRobotSetup(); yield break; } if (startMenu == "pad") { ShowPadSetup(ShowRobotSetup, ShowMain); yield break; }
            if (startMenu == "controls") { ShowControls(); yield break; }
            if (startMenu == "settings") { settingsBack = ShowMain; ShowSettings(false); yield break; }
            if (startMenu == "modes") { ShowModes(); yield break; }
            if (startMenu == "main") { ShowMain(); yield break; }
            if (startMenu == "language") { ShowLanguage(); yield break; }
            if (!Loc.HasChosen) ShowLanguage(); else ShowMain();
        }

        // ---------------------------------------------------------------- 畫面建構
        MenuScreen Build(Func<string> titleFn, Func<string> subFn, Item[] items, Action onBack, Action<RectTransform> extra = null, float firstY = 330f)
        {
            var s = new MenuScreen { TitleFn = titleFn, OnBack = onBack };
            var go = new GameObject("Screen", typeof(RectTransform));
            go.transform.SetParent(root, false);
            UiKit.Stretch(go.GetComponent<RectTransform>());
            s.Go = go;
            var rt = go.transform;

            // 左側面板
            // 液態玻璃面板(真的背景模糊 + 邊緣折射 + 高光邊,見 UiGlass.cs)
            var card = GlassPanel.Make(rt, "Card", 40, 40, 722, 1000, 64f, GlassStyle.Card());
            card.Opacity = 1f;

            // 標題區
            var small = UiKit.Label("Tag", rt, "FRC 9427", 26, UiTheme.Accent, TextAnchor.UpperLeft);
            UiKit.PlaceTL(small.rectTransform, 90, 90, 600, 40);
            s.Title = UiKit.Label("Title", rt, titleFn(), 64, UiTheme.Text, TextAnchor.UpperLeft);
            s.Title.fontStyle = FontStyle.Bold;
            UiKit.PlaceTL(s.Title.rectTransform, 90, 130, 640, 90);
            var sub = UiKit.Label("Sub", rt, subFn != null ? subFn() : "", 28, new Color(0.79f, 0.83f, 0.90f), TextAnchor.UpperLeft);
            UiKit.PlaceTL(sub.rectTransform, 92, 225, 640, 40);
            if (subFn != null) s.Refreshers.Add(() => sub.text = subFn());
            var hr = UiKit.Img("Hr", rt, UiTheme.Line);
            UiKit.PlaceTL(hr.rectTransform, 90, 290, 580, 2);

            // 按鈕
            float y = firstY;
            float step = firstY > 330f ? 70f : Mathf.Min(92f, (990f - 330f) / Mathf.Max(1, items.Length));
            float bh = firstY > 330f ? 60f : Mathf.Min(78f, step - 10f);
            foreach (var it in items)
            {
                var b = new UiButton { TextFn = it.Text, OnClick = it.Click, Enabled = it.Enabled };
                if (it.EnabledFn != null) { var itc = it; var bc = b; s.Refreshers.Add(() => bc.Enabled = itc.EnabledFn()); }
                var gp = GlassPanel.Make(rt, "Btn", 72, y, 658, bh, bh / 2f, GlassStyle.Pill());
                b.Glass = gp; b.Rt = gp.Root;
                b.Label = UiKit.Label("Text", b.Rt, it.Text(), 34, UiTheme.TextDim, TextAnchor.MiddleLeft);
                b.Label.resizeTextForBestFit = true;   // 長標籤(例如比賽計時)自動縮小,不超出按鈕
                b.Label.resizeTextMinSize = 18; b.Label.resizeTextMaxSize = 34;
                UiKit.PlaceTL(b.Label.rectTransform, 30, 0, 598, bh);
                s.Buttons.Add(b);
                y += step;
            }

            // 提示列
            var hint = UiKit.Label("Hint", rt, Loc.T("hint.nav"), 26, new Color(0.79f, 0.83f, 0.90f), TextAnchor.UpperLeft);
            UiKit.PlaceTL(hint.rectTransform, 90, firstY > 330f ? 1046 : 1010, 640, 30);
            s.Refreshers.Add(() => hint.text = Loc.T("hint.nav"));

            // 右下角浮水印
            var wm = UiKit.Label("Watermark", rt, "FRC 9427  ·  MADE IN UNITY  ·  v" + UpdateCheck.Current, 22, new Color(1, 1, 1, 0.25f), TextAnchor.LowerRight);
            UiKit.PlaceTL(wm.rectTransform, 1300, 1020, 580, 30);

            extra?.Invoke(go.GetComponent<RectTransform>());
            go.SetActive(false);
            return s;
        }

        void Show(MenuScreen s, bool game)
        {
            padScreen = false;
            if (cur != null) cur.Go.SetActive(false);
            cur = s;
            inGameMenu = game;
            dim.color = game ? new Color(0, 0, 0, 0.35f) : new Color(0.02f, 0.04f, 0.08f, 0.22f);
            s.Go.SetActive(true); { var cg = s.Go.GetComponent<CanvasGroup>(); if (cg == null) cg = s.Go.AddComponent<CanvasGroup>(); StartCoroutine(Intro(s.Go.GetComponent<RectTransform>(), cg)); }
            menuVisible = true;
            justOpened = true;
            s.Sel = 0;
            for (int i = 0; i < s.Buttons.Count; i++) if (s.Buttons[i].Enabled) { s.Sel = i; break; }
            RefreshScreen();
        }

        void RefreshScreen()
        {
            if (cur == null) return;
            cur.Title.text = cur.TitleFn();
            foreach (var r in cur.Refreshers) r();
            for (int i = 0; i < cur.Buttons.Count; i++) cur.Buttons[i].Refresh(i == cur.Sel);
        }

        void Hide()
        {
            if (cur != null) cur.Go.SetActive(false);
            cur = null;
            menuVisible = false;
            dim.color = new Color(0, 0, 0, 0);
        }

        // ---------------------------------------------------------------- 各畫面
        void ShowLanguage()
        {
            var s = Build(() => Loc.T("lang.pick"), null, new[]
            {
                new Item(() => "繁體中文", () => { Loc.Lang = "zh"; ShowMain(); }),
                new Item(() => "English",  () => { Loc.Lang = "en"; ShowMain(); }),
            }, null);
            Show(s, false);
        }

        void ShowMain()
        {
            var s = Build(() => Loc.T("app.title"), () => Loc.T("app.sub") + (UpdateCheck.Progress != "" ? "   ●" + UpdateCheck.Progress : UpdateCheck.Newer ? (Loc.Lang == "zh" ? "   ●有新版 v" + UpdateCheck.Latest + "(按 U 自動更新)" : "   ●New v" + UpdateCheck.Latest + " (press U to update)") : ""), new[]
            {
                new Item(() => Loc.T("menu.start"),    ShowModes),
                new Item(() => Loc.T("menu.settings"), () => { settingsBack = ShowMain; ShowSettings(false); }),
                new Item(() => Loc.T("menu.controls"), ShowControls),
                new Item(() => Loc.T("menu.quit"),     Application.Quit),
            }, null);
            Show(s, false);
        }

        static string Z(string zh, string en) => Loc.Lang == "zh" ? zh : en;
        bool padScreen; float nextLive;

        string PadStatus(bool driver)
        {
            bool on = driver ? Pad.DriverOnline : Pad.OperatorOnline;
            if (Pad.Mode == 1 && !driver) return Z("(單手把:同一支)", "(single pad)");
            if (!on && !driver && Pad.Mode == 2 && Pad.OperatorKeyboard) return Z("⌨ 鍵盤(空白=發射 Z/X=砲塔 C=吐球)", "⌨ keyboard (Space/Z/X/C)");
            if (!on) return Z("✗ 未偵測(請接上手把)", "✗ not detected");
            int idx = driver ? Pad.DriverIndex : Pad.OperatorIndex;
            return Z("✓ 已連線(第 ", "✓ connected (#") + (idx + 1) + Z(" 槽)", ")");
        }

        // 進遊戲前先選「單手把 / 雙手把」,雙手把再依序按 A 指派駕駛與操作手;狀態即時更新(掉線一眼看得出來)
        void ShowPadSetup(Action next, Action back)
        {
            MenuScreen s = null;
            s = Build(
                () => Pad.AssignStage == 1 ? Z("按「駕駛」手把 A", "Press A: DRIVER") : Pad.AssignStage == 2 ? Z("按「操作手」手把 A", "Press A: OPERATOR") : Z("手把設定", "Controllers"),
                () => Pad.AssignStage > 0 ? Z("在要當這個角色的那支手把上按 A(Enter 跳過,自動分)", "Press A on that pad (Enter = skip, auto)") : Z("先選要用幾支,再開始;沒手把也能用鍵盤", "Choose pad count first; keyboard works too"),
                new[]
                {
                    new Item(() => Z("手把數量", "Pads") + ":  " + (Pad.Mode == 2 ? Z("2 支(駕駛+操作手)", "2 (driver+operator)") : Pad.Mode == 1 ? Z("1 支(一人全包)", "1 (all-in-one)") : Z("請選擇 ▸", "choose ▸")), () =>
                    {
                        Pad.Mode = Pad.Mode == 2 ? 1 : 2;
                        Pad.AssignStage = 0;   // 兩支都連上就自動分(第 1 槽駕駛、第 2 槽操作手),指派是選用的
                    }),
                    new Item(() => Z("駕駛", "Driver") + ":  " + PadStatus(true), null, false),
                    new Item(() => Z("操作手", "Operator") + ":  " + PadStatus(false), null, false),
                    new Item(() => Z("重新指派(各按一次 A)", "Re-assign (press A on each)"), () => { Pad.Mode = 2; Pad.BeginAssign(); }) { EnabledFn = () => Pad.Mode == 2 },
                    new Item(() => Z("操作手改用鍵盤", "Operator on keyboard") + ":  " + Loc.T(Pad.OperatorKeyboard ? "on" : "off"), () => { Pad.OperatorKeyboard = !Pad.OperatorKeyboard; }) { EnabledFn = () => Pad.Mode == 2 },
                    new Item(() => Pad.Mode == 2 && !(Pad.DriverOnline && (Pad.OperatorOnline || Pad.OperatorKeyboard)) ? Z("下一步(需兩支都連線)", "Next (needs both pads)") : Z("下一步", "Next"), () => { Pad.AssignStage = 0; next(); }) { EnabledFn = () => Pad.Mode == 1 || (Pad.Mode == 2 && Pad.DriverOnline && (Pad.OperatorOnline || Pad.OperatorKeyboard)) },
                    new Item(() => Loc.T("menu.back"), () => back()),
                }, () => back());
            padScreen = true;
            Show(s, inGameMenu);
        }

        void ShowModes()
        {
            var s = Build(() => Loc.T("mode.title"), () => Z("練習不限時;比賽 160 秒、紅方由 AI 對戰", "Practice: untimed. Match: 160 s vs AI"), new[]
            {
                new Item(() => Z("練習模式(不限時)", "Practice (no time limit)"), () => { GameSession.PracticeMode = true; GameSession.VsAi = false; ShowPadSetup(ShowRobotSetup, ShowModes); }),
                new Item(() => Z("模擬比賽(對戰 AI)", "Match vs AI"), ShowAiLevel),
                new Item(() => Loc.T("menu.back"),  ShowMain),
            }, ShowMain);
            Show(s, false);
        }

        void ShowAiLevel()
        {
            string[] zh = { "簡單", "普通", "困難", "超困難" }, en = { "Easy", "Normal", "Hard", "Insane" };
            Item Lv(int i) => new Item(() => (GameSession.AiLevel == i ? "● " : "○ ") + (Loc.Lang == "zh" ? zh[i] : en[i]), () => { GameSession.AiLevel = i; GameSession.PracticeMode = false; GameSession.VsAi = true; ShowPadSetup(ShowRobotSetup, ShowAiLevel); });
            var s = Build(() => Z("對手難度", "Opponent level"), () => Z("紅隊 AI 會自己撿球、射 HUB", "The red team plays by itself"), new[]
            {
                Lv(0), Lv(1), Lv(2), Lv(3),
                new Item(() => Loc.T("menu.back"), ShowModes),
            }, ShowModes);
            Show(s, false);
        }
        // 進遊戲前:選機器人模型、匯入 .glb、選機器人程式專案
        // ---- 機器人設定:分成「外觀 / 機器人程式 / 手感 / 比賽」四個分類,主頁只留入口與「開始」
        Item ItModel() => new Item(() => Loc.T("set.model") + ":  " + (RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Selected), () =>
        {
            var list = new List<string> { "" };
            list.AddRange(RobotModels.List());
            int i = list.IndexOf(RobotModels.Selected);
            RobotModels.Selected = list[(i + 1) % list.Count];
        });
        Item ItImport() => new Item(() => Loc.T("setup.import"), () =>
        {
            string p = NativeDialogs.OpenFile(Loc.T("setup.import"), "GLB (*.glb)\0*.glb\0");
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                System.IO.Directory.CreateDirectory(RobotModels.Dir);
                string name = System.IO.Path.GetFileName(p);
                System.IO.File.Copy(p, System.IO.Path.Combine(RobotModels.Dir, name), true);
                RobotModels.Selected = name;
            }
            catch (Exception e) { Debug.LogError("import model: " + e.Message); }
        });
        Item ItYaw() => new Item(() => Loc.T("setup.yaw") + ":  " + RobotModels.YawDeg.ToString("0") + "°", () => { RobotModels.YawDeg = (RobotModels.YawDeg + 90f) % 360f; });
        Item ItProject() => new Item(() =>
        {
            string c = Prefs.GetString("robotProject", "");
            return Loc.T("setup.code") + ":  " + (c == "" ? Loc.T("setup.none") : System.IO.Path.GetFileName(c.TrimEnd('\\', '/')));
        }, () =>
        {
            string p = NativeDialogs.PickFolder(Loc.T("setup.code"));
            if (string.IsNullOrEmpty(p)) return;
            Prefs.SetString("robotProject", FindGradleRoot(p));
            PlayerPrefs.Save();
        });
        Item[] CatalogItems()
        {
            var l = new List<Item>();
            foreach (var en in OpenSourceCatalog.All)
            {
                var en2 = en;
                l.Add(new Item(() =>
                {
                    string st = en2.State == "…" ? Z("下載中…", "Downloading…") : en2.State != "" ? en2.State
                        : OpenSourceCatalog.Installed(en2) ? (Prefs.GetString("robotProject", "") == en2.Dir ? Z("使用中 ✓", "In use ✓") : Z("已下載,按一下使用", "Installed — click to use")) : Z("按一下下載", "Click to download");
                    string tag = en2.Status == "ok" ? Z("可用", "Works") : en2.Status == "partial" ? Z("部分", "Partial") : Z("未測試", "Untested");
                    return en2.Team + "  [" + tag + "]  " + st;
                }, () =>
                {
                    if (!OpenSourceCatalog.Installed(en2)) { OpenSourceCatalog.Install(en2); return; }
                    Prefs.SetString("robotProject", en2.Dir); Prefs.SetInt("useRealCode", 1); Prefs.SetInt("tankMode", 0); PlayerPrefs.Save();
                }));
            }
            return l.ToArray();
        }        Item ItReal() => new Item(() => Loc.T("setup.real") + ":  " + Loc.T(Prefs.GetInt("useRealCode", 0) == 1 ? "on" : "off"), () => { Prefs.SetInt("useRealCode", Prefs.GetInt("useRealCode", 0) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItCtl() => new Item(() => Loc.T("setup.ctl") + ":  " + Loc.T(Prefs.GetInt("tankMode", 1) == 1 ? "ctl.tank" : "ctl.swerve"), () => { Prefs.SetInt("tankMode", Prefs.GetInt("tankMode", 1) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItSpeed() => new Item(() => Loc.T("setup.speed") + ":  " + SettingsStore.MaxSpeedChoice.ToString("0.0") + " m/s", () => { SettingsStore.SpeedIndex = (SettingsStore.SpeedIndex + 1) % SettingsStore.SpeedOptions.Length; });
        Item ItAccel() => new Item(() => Loc.T("setup.accel") + ":  " + SettingsStore.AccelChoice.ToString("0") + " m/s²", () => { SettingsStore.AccelIndex = (SettingsStore.AccelIndex + 1) % SettingsStore.AccelOptions.Length; });
        Item ItSecond() => new Item(() => Loc.T("setup.second") + ":  " + Loc.T(Prefs.GetInt("secondRobot", 1) == 1 ? "on" : "off"), () => { Prefs.SetInt("secondRobot", Prefs.GetInt("secondRobot", 1) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItLevel()
        {
            string[] zh = { "簡單", "普通", "困難", "超困難" }, en = { "Easy", "Normal", "Hard", "Insane" };
            return new Item(() => Z("對手難度", "Opponent") + ":  " + (Loc.Lang == "zh" ? zh : en)[GameSession.AiLevel], () => { GameSession.AiLevel = (GameSession.AiLevel + 1) % 4; }) { EnabledFn = () => GameSession.VsAi };
        }

        void ShowSub(Func<string> title, Func<string> sub, Item[] items)
        {
            var all = new List<Item>(items) { new Item(() => Loc.T("menu.back"), ShowRobotSetup) };
            Show(Build(title, sub, all.ToArray(), ShowRobotSetup), false);
        }

        void ShowRobotSetup()
        {
            string Sum()
            {
                string m = RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Selected;
                string c = Prefs.GetString("robotProject", "");
                string cc = c == "" ? Loc.T("setup.none") : System.IO.Path.GetFileName(c.TrimEnd('\\', '/'));
                return m + "  ·  " + cc;
            }
            var s = Build(() => Loc.T("setup.title"), Sum, new[]
            {
                new Item(() => Z("外觀  ▸", "Appearance  ▸"), () => ShowSub(() => Z("外觀", "Appearance"), () => Z("機器人模型與方向", "Robot model & orientation"), new[] { ItModel(), ItImport(), ItYaw() })),
                new Item(() => Z("機器人程式  ▸", "Robot code  ▸"), () => ShowSub(() => Z("機器人程式", "Robot code"), () => Z("專案、是否跑真實程式、操控方式", "Project, real code, controls"), new[] { ItProject(), ItReal(), ItCtl(), new Item(() => Z("開源機器人程式庫  ▸", "Open-source robot library  ▸"), () => ShowSub(() => Z("開源機器人程式庫", "Open-source robot library"), () => Z("別隊公開的 2026 程式:下載後直接在模擬器跑(來源 GitHub,未打包)", "Public 2026 team code: download and run (from GitHub, not bundled)"), CatalogItems())) })),
                new Item(() => Z("手感  ▸", "Handling  ▸"), () => ShowSub(() => Z("手感", "Handling"), () => Z("最高車速與加速度(慣性)", "Top speed & acceleration"), new[] { ItSpeed(), ItAccel() })),
                new Item(() => Z("比賽  ▸", "Match  ▸"), () => ShowSub(() => Z("比賽", "Match"), () => Z("對手機器人與難度", "Opponent robot & level"), new[] { ItSecond(), ItLevel() })),
                new Item(() => Loc.T("setup.start"), StartGame),
                new Item(() => Loc.T("menu.back"), ShowModes),
            }, ShowModes);
            Show(s, false);
        }
        // 使用者可能選到外層資料夾:往下找 3 層內有 build.gradle 的那個
        static string FindGradleRoot(string dir)
        {
            try
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "build.gradle"))) return dir;
                foreach (var f in System.IO.Directory.GetFiles(dir, "build.gradle", System.IO.SearchOption.AllDirectories))
                {
                    string d = System.IO.Path.GetDirectoryName(f);
                    if (d.Substring(dir.Length).Split(System.IO.Path.DirectorySeparatorChar).Length <= 4) return d;
                }
            }
            catch (Exception) { }
            return dir;
        }
        void ShowSoundSettings(bool game)
        {
            MenuScreen s = null;
            string P(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }
            s = Build(() => Z("聲音與回饋", "Sound & feedback"), null, new[]
            {
                new Item(() => Z("主音量", "Master volume") + ":  " + P(JuiceSettings.Master), () => { JuiceSettings.Master = JuiceSettings.Step(JuiceSettings.Master); SfxBus.Play("tick", 0.8f); RefreshScreen(); }),
                new Item(() => Z("音效", "Effects") + ":  " + P(JuiceSettings.Sfx), () => { JuiceSettings.Sfx = JuiceSettings.Step(JuiceSettings.Sfx); SfxBus.Play("ding", 0.6f); RefreshScreen(); }),
                new Item(() => Z("音樂", "Music") + ":  " + P(JuiceSettings.Music), () => { JuiceSettings.Music = JuiceSettings.Step(JuiceSettings.Music); RefreshScreen(); }),
                new Item(() => Z("鏡頭晃動", "Camera shake") + ":  " + (JuiceSettings.Shake > 0.75f ? Z("正常", "Normal") : JuiceSettings.Shake > 0.25f ? Z("減弱", "Reduced") : Z("關", "Off")), () => { JuiceSettings.Shake = JuiceSettings.ShakeCycle(JuiceSettings.Shake); RefreshScreen(); }),
                new Item(() => Z("手把震動", "Controller rumble") + ":  " + Loc.T(JuiceSettings.Rumble ? "on" : "off"), () => { JuiceSettings.Rumble = !JuiceSettings.Rumble; Rumble.Pulse(Pad.DriverIndex, 0.4f, 0.4f, 0.15f); RefreshScreen(); }),
                new Item(() => Z("場館殘響", "Arena reverb") + ":  " + Loc.T(SfxBus.ReverbOn ? "on" : "off"), () => { SfxBus.ReverbOn = !SfxBus.ReverbOn; SfxBus.Play("ding", 0.5f); RefreshScreen(); }),
                new Item(() => Z("減少閃光與粒子", "Reduce flashes & particles") + ":  " + Loc.T(JuiceSettings.ReduceFlash ? "on" : "off"), () => { JuiceSettings.ReduceFlash = !JuiceSettings.ReduceFlash; RefreshScreen(); }),
                new Item(() => Loc.T("menu.back"), () => ShowSettings(game)),
            }, () => ShowSettings(game));
            Show(s, game);
        }
        void ShowSettings(bool game)
        {
            MenuScreen s = null;
            s = Build(() => Loc.T("set.title"), null, new[]
            {
                new Item(() => Loc.T("set.lang") + ":  " + Loc.T("lang.name"), () =>
                {
                    Loc.Lang = Loc.Lang == "zh" ? "en" : "zh";
                    RefreshScreen();
                }),
                new Item(() => Loc.T("set.fps") + ":  " + SettingsStore.PaceLabel(SettingsStore.FpsIndex) + Z("(垂直同步)", " (vsync)"), () => { SettingsStore.FpsIndex = (SettingsStore.FpsIndex + 1) % 4; SettingsStore.Apply(); RefreshScreen(); }),
                new Item(() => Loc.T("set.rscale") + ":  " + Mathf.RoundToInt(SettingsStore.RenderScale * 100f) + "%", () =>
                {
                    SettingsStore.RenderScaleIndex = (SettingsStore.RenderScaleIndex + 1) % SettingsStore.RenderScales.Length;
                    RefreshScreen();
                }),
                new Item(() => Loc.T("set.shadow") + ":  " + Loc.T(SettingsStore.Shadows ? "on" : "off"), () =>
                {
                    SettingsStore.Shadows = !SettingsStore.Shadows; SettingsStore.Apply(); RefreshScreen();
                }),
                
                new Item(() => Loc.T("set.full") + ":  " + Loc.T(SettingsStore.Fullscreen ? "on" : "off"), () =>
                {
                    SettingsStore.Fullscreen = !SettingsStore.Fullscreen; SettingsStore.Apply(); RefreshScreen();
                }),
                new Item(() => Loc.T("set.model") + ":  " + (RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Selected), () =>
                {
                    var list = new List<string> { "" };
                    list.AddRange(RobotModels.List());
                    int i = list.IndexOf(RobotModels.Selected);
                    RobotModels.Selected = list[(i + 1) % list.Count];
                    RefreshScreen();
                }),
                new Item(() => Z("手把設定", "Controllers"), () => ShowPadSetup(() => ShowSettings(game), () => ShowSettings(game))),
                new Item(() => Z("聲音與回饋  ▸", "Sound & feedback  ▸"), () => ShowSoundSettings(game)),
                new Item(() => Z("後製特效", "Post effects") + ":  " + (PostFX.Quality == 0 ? Z("關", "Off") : PostFX.Quality == 1 ? Z("基本", "Basic") : PostFX.Quality == 2 ? Z("泛光", "Bloom") : "AO"), () => { PostFX.Quality = (PostFX.Quality + 1) % 3; }),
                new Item(() => Z("液態玻璃(毛玻璃)", "Liquid glass") + ":  " + (UiGlass.Disabled ? Z("關", "Off") : Z("開", "On")), () => { UiGlass.Disabled = !UiGlass.Disabled; if (UiGlass.Disabled) UiGlass.Ready = false; Prefs.SetInt("noGlass", UiGlass.Disabled ? 1 : 0); PlayerPrefs.Save(); }),
                new Item(() => Loc.T("menu.back"), () => settingsBack?.Invoke()),
            }, () => settingsBack?.Invoke());
            Show(s, game);
        }

        void ShowControls()
        {
            var s = Build(() => Loc.T("ctl.title"), null, new[]
            {
                new Item(() => Loc.T("menu.back"), ShowMain),
            }, ShowMain, content =>
            {
                // 內容放在左側面板內(窄視窗也看得到),不再放到右邊 3D 畫面上
                var kbT = UiKit.Label("KbT", content, Loc.T("ctl.kb"), 24, UiTheme.Accent, TextAnchor.UpperLeft);
                UiKit.PlaceTL(kbT.rectTransform, 90, 300, 600, 32);
                var kb = UiKit.Label("Kb", content, Loc.T("ctl.kb.body"), 19, UiTheme.Text, TextAnchor.UpperLeft);
                kb.lineSpacing = 1.1f;
                UiKit.PlaceTL(kb.rectTransform, 90, 334, 650, 270);
                var pdT = UiKit.Label("PadT", content, Loc.T("ctl.pad"), 24, UiTheme.Accent, TextAnchor.UpperLeft);
                UiKit.PlaceTL(pdT.rectTransform, 90, 612, 600, 32);
                var pd = UiKit.Label("Pad", content, Loc.T("ctl.pad.body"), 19, UiTheme.Text, TextAnchor.UpperLeft);
                pd.lineSpacing = 1.1f;
                UiKit.PlaceTL(pd.rectTransform, 90, 646, 650, 290);
                // 語言切換時更新內文
                lastControls = () =>
                {
                    kbT.text = Loc.T("ctl.kb"); kb.text = Loc.T("ctl.kb.body");
                    pdT.text = Loc.T("ctl.pad"); pd.text = Loc.T("ctl.pad.body");
                };
            }, 940f);
            s.Refreshers.Add(() => lastControls?.Invoke());
            Show(s, false);
        }
        Action lastControls;

        void ShowPause()
        {
            var s = Build(() => Loc.T("pause.title"), null, new[]
            {
                new Item(() => Loc.T("pause.resume"),   Resume),
                new Item(() => Loc.T("pause.reset"),    () => { GameSession.Drive?.ResetPose(); Resume(); }),
                new Item(() => Z("再來一場(重置場地)", "Restart match (reset field)"), () => { Resume(); GameSession.Rematch(); }),
                new Item(() => Z("投籃訓練:開始(10 顆)", "Shooting drill: start (10)"), () => { Resume(); GameSession.StartDrill(); }) { EnabledFn = () => GameSession.PracticeMode },
                new Item(() => Z("訓練距離", "Drill distance") + ":  " + GameSession.DrillDist.ToString("0.0") + " m", () => { GameSession.DrillDist = GameSession.DrillDist >= 5.5f ? 2.0f : GameSession.DrillDist + 0.5f; }) { EnabledFn = () => GameSession.PracticeMode },
                new Item(() => Loc.T("menu.settings"),  () => { settingsBack = ShowPause; ShowSettings(true); }),
                new Item(() => Loc.T("pause.menu"),     BackToMain),
            }, Resume);
            Time.timeScale = 0f;
            Show(s, true);
        }

        // ---------------------------------------------------------------- 流程
        public static void PauseNow() { if (I != null && !I.menuVisible) I.ShowPause(); }
        public static void GoMain() { if (I != null) I.BackToMain(); }

        void StartGame()
        {
            Hide();
            GameSession.Begin(Rig);
        }

        void Resume()
        {
            Time.timeScale = 1f;
            Hide();
        }

        void BackToMain()
        {
            Time.timeScale = 1f;
            GameSession.End(Rig);
            ShowMain();
        }

        // ---------------------------------------------------------------- 輸入
        void Update()
        {
            if (splashing) return;
            // 手把設定畫面:即時刷新連線狀態;指派中只吃「按 A 指派」,不讓手把去操作選單
            if (padScreen && cur != null && menuVisible)
            {
                Pad.Poll();
                if (Time.unscaledTime >= nextLive) { nextLive = Time.unscaledTime + 0.2f; RefreshScreen(); }
                if (Pad.AssignStage > 0) { if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)) Pad.AssignStage = 0; return; }
            }

            // 遊戲中按 Esc / 手把 Back 開暫停選單
            if (!menuVisible && GameSession.Active)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton6) || Pad.AnyDown(Pad.Start) || Pad.AnyDown(Pad.Back)) ShowPause();
                return;
            }
            if (!menuVisible || cur == null) return;

            if (justOpened) { justOpened = false; return; }

            // 有新版:主畫面按 U 開啟下載頁;更新結果晚到時重畫副標題
            if (!inGameMenu && UpdateCheck.Newer && Input.GetKeyDown(KeyCode.U)) UpdateCheck.StartUpdate();
            if (UpdateCheck.Newer != shownNewer || UpdateCheck.Progress != shownProgress) { shownNewer = UpdateCheck.Newer; shownProgress = UpdateCheck.Progress; RefreshScreen(); }

            // 滑鼠 hover / 點擊
            Vector2 mp = Input.mousePosition;
            for (int i = 0; i < cur.Buttons.Count; i++)
            {
                var b = cur.Buttons[i];
                if (b.Enabled && RectTransformUtility.RectangleContainsScreenPoint(b.Rt, mp, null))
                {
                    if (cur.Sel != i && (Input.GetAxisRaw("Mouse X") != 0f || Input.GetAxisRaw("Mouse Y") != 0f))
                    { cur.Sel = i; RefreshScreen(); }
                    if (Input.GetMouseButtonDown(0)) { cur.Sel = i; Activate(b); return; }
                }
            }

            // 上/下(W/S、方向鍵、手把左搖桿)
            float ax = Input.GetAxisRaw("Vertical") + Pad.LY + (Pad.Held(Pad.DUp) ? 1f : 0f) - (Pad.Held(Pad.DDown) ? 1f : 0f);
            int move = 0;
            if (ax > 0.6f && prevAxis <= 0.6f) move = -1;
            if (ax < -0.6f && prevAxis >= -0.6f) move = 1;
            prevAxis = ax;
            if (move != 0 && cur.Buttons.Count > 0)
            {
                int n = cur.Buttons.Count, i = cur.Sel;
                for (int k = 0; k < n; k++)
                {
                    i = (i + move + n) % n;
                    if (cur.Buttons[i].Enabled) break;
                }
                cur.Sel = i; RefreshScreen();
            }

            if (cur.Sel >= 0 && cur.Sel < cur.Buttons.Count &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                 Input.GetKeyDown(KeyCode.JoystickButton0) || Pad.Down(Pad.A)))
                Activate(cur.Buttons[cur.Sel]);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1) || Pad.Down(Pad.B))
                cur?.OnBack?.Invoke();
        }

        System.Collections.IEnumerator Intro(RectTransform r, CanvasGroup cg)
        {
            var sp = new Spring(0) { target = 1 };
            while (sp.Step(Time.unscaledDeltaTime, .40f, .85f) || sp.x < .999f)
            {
                if (r == null) yield break;
                cg.alpha = Mathf.Clamp01(sp.x * 1.4f);
                r.anchoredPosition = new Vector2(-28f * (1f - sp.x), 0f);
                yield return null;
            }
            if (r != null) { cg.alpha = 1f; r.anchoredPosition = Vector2.zero; }
        }

        System.Collections.IEnumerator PressPulse(GlassPanel g)
        {
            g.PressT = 1f; float t = Time.unscaledTime + 0.12f;
            while (Time.unscaledTime < t) yield return null;
            if (g != null) g.PressT = 0f;
        }
        void Activate(UiButton b)
        { if (b.Enabled && b.Glass != null) StartCoroutine(PressPulse(b.Glass)); if (b.Enabled) Juice.UiClick();
            if (!b.Enabled) return;
            b.OnClick?.Invoke();
            RefreshScreen();
        }
    }
}
