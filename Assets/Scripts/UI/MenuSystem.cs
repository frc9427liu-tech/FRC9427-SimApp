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
            public Func<string> Text; public Action Click; public bool Enabled = true;
            public Item(Func<string> t, Action c, bool e = true) { Text = t; Click = c; Enabled = e; }
        }

        RectTransform root;
        Image dim;
        MenuScreen cur;
        bool menuVisible, splashing, inGameMenu;
        bool justOpened;
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
            if (startMenu == "setup") { ShowRobotSetup(); yield break; }
            if (!Loc.HasChosen) ShowLanguage(); else ShowMain();
        }

        // ---------------------------------------------------------------- 畫面建構
        MenuScreen Build(Func<string> titleFn, Func<string> subFn, Item[] items, Action onBack, Action<RectTransform> extra = null)
        {
            var s = new MenuScreen { TitleFn = titleFn, OnBack = onBack };
            var go = new GameObject("Screen", typeof(RectTransform));
            go.transform.SetParent(root, false);
            UiKit.Stretch(go.GetComponent<RectTransform>());
            s.Go = go;
            var rt = go.transform;

            // 左側面板
            var panel = UiKit.Img("Panel", rt, UiTheme.Panel);
            UiKit.PlaceTL(panel.rectTransform, 0, 0, 760, 1080);
            var edge = UiKit.Img("Edge", rt, UiTheme.Line);
            UiKit.PlaceTL(edge.rectTransform, 760, 0, 2, 1080);

            // 標題區
            var small = UiKit.Label("Tag", rt, "FRC 9427", 26, UiTheme.Accent, TextAnchor.UpperLeft);
            UiKit.PlaceTL(small.rectTransform, 90, 90, 600, 40);
            s.Title = UiKit.Label("Title", rt, titleFn(), 64, UiTheme.Text, TextAnchor.UpperLeft);
            s.Title.fontStyle = FontStyle.Bold;
            UiKit.PlaceTL(s.Title.rectTransform, 90, 130, 640, 90);
            var sub = UiKit.Label("Sub", rt, subFn != null ? subFn() : "", 28, UiTheme.TextDim, TextAnchor.UpperLeft);
            UiKit.PlaceTL(sub.rectTransform, 92, 225, 640, 40);
            if (subFn != null) s.Refreshers.Add(() => sub.text = subFn());
            var hr = UiKit.Img("Hr", rt, UiTheme.Line);
            UiKit.PlaceTL(hr.rectTransform, 90, 290, 580, 2);

            // 按鈕
            float y = 330f;
            float step = Mathf.Min(92f, (990f - 330f) / Mathf.Max(1, items.Length));
            float bh = Mathf.Min(78f, step - 10f);
            foreach (var it in items)
            {
                var b = new UiButton { TextFn = it.Text, OnClick = it.Click, Enabled = it.Enabled };
                b.Bg = UiKit.Img("Btn", rt, new Color(1, 1, 1, 0.04f));
                b.Rt = b.Bg.rectTransform;
                UiKit.PlaceTL(b.Rt, 90, y, 580, bh);
                b.Bar = UiKit.Img("Bar", b.Rt, UiTheme.Accent);
                UiKit.PlaceTL(b.Bar.rectTransform, 0, 0, 6, bh);
                b.Label = UiKit.Label("Text", b.Rt, it.Text(), 34, UiTheme.TextDim, TextAnchor.MiddleLeft);
                b.Label.resizeTextForBestFit = true;   // 長標籤(例如比賽計時)自動縮小,不超出按鈕
                b.Label.resizeTextMinSize = 18; b.Label.resizeTextMaxSize = 34;
                UiKit.PlaceTL(b.Label.rectTransform, 34, 0, 540, bh);
                s.Buttons.Add(b);
                y += step;
            }

            // 提示列
            var hint = UiKit.Label("Hint", rt, Loc.T("hint.nav"), 22, UiTheme.TextDim, TextAnchor.UpperLeft);
            UiKit.PlaceTL(hint.rectTransform, 90, 1010, 640, 30);
            s.Refreshers.Add(() => hint.text = Loc.T("hint.nav"));

            // 右下角浮水印
            var wm = UiKit.Label("Watermark", rt, "FRC 9427  ·  MADE IN UNITY", 22, new Color(1, 1, 1, 0.25f), TextAnchor.LowerRight);
            UiKit.PlaceTL(wm.rectTransform, 1300, 1020, 580, 30);

            extra?.Invoke(go.GetComponent<RectTransform>());
            go.SetActive(false);
            return s;
        }

        void Show(MenuScreen s, bool game)
        {
            if (cur != null) cur.Go.SetActive(false);
            cur = s;
            inGameMenu = game;
            dim.color = game ? new Color(0, 0, 0, 0.55f) : new Color(0, 0, 0, 0);
            s.Go.SetActive(true);
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
            var s = Build(() => Loc.T("app.title"), () => Loc.T("app.sub"), new[]
            {
                new Item(() => Loc.T("menu.start"),    ShowModes),
                new Item(() => Loc.T("menu.settings"), () => { settingsBack = ShowMain; ShowSettings(false); }),
                new Item(() => Loc.T("menu.controls"), ShowControls),
                new Item(() => Loc.T("menu.quit"),     Application.Quit),
            }, null);
            Show(s, false);
        }

        void ShowModes()
        {
            var s = Build(() => Loc.T("mode.title"), null, new[]
            {
                new Item(() => Loc.T("mode.free"),  ShowRobotSetup),
                // 「比賽模式/自動階段練習」還沒做,先不顯示避免新手點不動(比賽計時在機器人設定頁開關)
                new Item(() => Loc.T("menu.back"),  ShowMain),
            }, ShowMain);
            Show(s, false);
        }

        // 進遊戲前:選機器人模型、匯入 .glb、選機器人程式專案
        void ShowRobotSetup()
        {
            var s = Build(() => Loc.T("setup.title"), () => Loc.T("setup.sub"), new[]
            {
                new Item(() => Loc.T("set.model") + ":  " + (RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Selected), () =>
                {
                    var list = new List<string> { "" };
                    list.AddRange(RobotModels.List());
                    int i = list.IndexOf(RobotModels.Selected);
                    RobotModels.Selected = list[(i + 1) % list.Count];
                }),
                new Item(() => Loc.T("setup.import"), () =>
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
                }),
                new Item(() => Loc.T("setup.yaw") + ":  " + RobotModels.YawDeg.ToString("0") + "°", () =>
                {
                    RobotModels.YawDeg = (RobotModels.YawDeg + 90f) % 360f;
                }),
                new Item(() =>
                {
                    string c = PlayerPrefs.GetString("robotProject", "");
                    return Loc.T("setup.code") + ":  " + (c == "" ? Loc.T("setup.none") : System.IO.Path.GetFileName(c.TrimEnd('\\', '/')));
                }, () =>
                {
                    string p = NativeDialogs.PickFolder(Loc.T("setup.code"));
                    if (string.IsNullOrEmpty(p)) return;
                    PlayerPrefs.SetString("robotProject", FindGradleRoot(p));
                    PlayerPrefs.Save();
                }),
                new Item(() => Loc.T("setup.real") + ":  " + Loc.T(PlayerPrefs.GetInt("useRealCode", 0) == 1 ? "on" : "off"), () =>
                {
                    PlayerPrefs.SetInt("useRealCode", PlayerPrefs.GetInt("useRealCode", 0) == 1 ? 0 : 1); PlayerPrefs.Save();
                }),
                new Item(() => Loc.T("setup.clock") + ":  " + Loc.T(PlayerPrefs.GetInt("matchClock", 1) == 1 ? "on" : "off"), () =>
                {
                    PlayerPrefs.SetInt("matchClock", PlayerPrefs.GetInt("matchClock", 1) == 1 ? 0 : 1); PlayerPrefs.Save();
                }),
                new Item(() => Loc.T("setup.ctl") + ":  " + Loc.T(PlayerPrefs.GetInt("tankMode", 1) == 1 ? "ctl.tank" : "ctl.swerve"), () =>
                {
                    PlayerPrefs.SetInt("tankMode", PlayerPrefs.GetInt("tankMode", 1) == 1 ? 0 : 1); PlayerPrefs.Save();
                }),
                new Item(() => Loc.T("setup.second") + ":  " + Loc.T(PlayerPrefs.GetInt("secondRobot", 1) == 1 ? "on" : "off"), () =>
                {
                    PlayerPrefs.SetInt("secondRobot", PlayerPrefs.GetInt("secondRobot", 1) == 1 ? 0 : 1); PlayerPrefs.Save();
                }),
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
                new Item(() =>
                {
                    int f = SettingsStore.FpsOptions[SettingsStore.FpsIndex];
                    return Loc.T("set.fps") + ":  " + (f == 0 ? Loc.T("set.unlimited") : f.ToString());
                }, () =>
                {
                    SettingsStore.FpsIndex = (SettingsStore.FpsIndex + 1) % SettingsStore.FpsOptions.Length;
                    SettingsStore.Apply(); RefreshScreen();
                }),
                new Item(() => Loc.T("set.vsync") + ":  " + Loc.T(SettingsStore.VSync ? "on" : "off"), () =>
                {
                    SettingsStore.VSync = !SettingsStore.VSync; SettingsStore.Apply(); RefreshScreen();
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
                var kbT = UiKit.Label("KbT", content, Loc.T("ctl.kb"), 30, UiTheme.Accent, TextAnchor.UpperLeft);
                UiKit.PlaceTL(kbT.rectTransform, 880, 120, 900, 40);
                var kb = UiKit.Label("Kb", content, Loc.T("ctl.kb.body"), 28, UiTheme.Text, TextAnchor.UpperLeft);
                kb.lineSpacing = 1.35f;
                UiKit.PlaceTL(kb.rectTransform, 880, 170, 980, 420);
                var pdT = UiKit.Label("PadT", content, Loc.T("ctl.pad"), 30, UiTheme.Accent, TextAnchor.UpperLeft);
                UiKit.PlaceTL(pdT.rectTransform, 880, 640, 900, 40);
                var pd = UiKit.Label("Pad", content, Loc.T("ctl.pad.body"), 28, UiTheme.Text, TextAnchor.UpperLeft);
                pd.lineSpacing = 1.35f;
                UiKit.PlaceTL(pd.rectTransform, 880, 690, 980, 260);
                // 語言切換時更新內文
                lastControls = () =>
                {
                    kbT.text = Loc.T("ctl.kb"); kb.text = Loc.T("ctl.kb.body");
                    pdT.text = Loc.T("ctl.pad"); pd.text = Loc.T("ctl.pad.body");
                };
            });
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
                new Item(() => Loc.T("menu.settings"),  () => { settingsBack = ShowPause; ShowSettings(true); }),
                new Item(() => Loc.T("pause.menu"),     BackToMain),
            }, Resume);
            Time.timeScale = 0f;
            Show(s, true);
        }

        // ---------------------------------------------------------------- 流程
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

            // 遊戲中按 Esc / 手把 Back 開暫停選單
            if (!menuVisible && GameSession.Active)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton6) || Pad.Down(Pad.Start) || Pad.Down(Pad.Back)) ShowPause();
                return;
            }
            if (!menuVisible || cur == null) return;

            if (justOpened) { justOpened = false; return; }

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

        void Activate(UiButton b)
        {
            if (!b.Enabled) return;
            b.OnClick?.Invoke();
            RefreshScreen();
        }
    }
}
