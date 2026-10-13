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
            public List<KeyValuePair<RectTransform, Action>> Clicks = new List<KeyValuePair<RectTransform, Action>>();   // 非按鈕的可點區域(例如主選單右上角玩家卡)
        }
        // ---- 畫面下方的提示條(成功綠/失敗紅,4 秒後消失);掛在 root 底下所以重建畫面後還在
        Image toastBg; Text toastT; float toastUntil; string avatarTestMsg; bool avatarTestOk;
        public void Toast(string msg, bool ok)
        {
            if (toastBg == null)
            {
                toastBg = UiKit.Glass("Toast", root, Color.black, 30f); UiKit.PlaceTL(toastBg.rectTransform, 360, 940, 1200, 72);
                toastT = UiKit.Label("ToastT", toastBg.transform, "", 28, Color.white, TextAnchor.MiddleCenter);
                toastT.horizontalOverflow = HorizontalWrapMode.Wrap; toastT.verticalOverflow = VerticalWrapMode.Truncate; UiKit.PlaceTL(toastT.rectTransform, 24, 0, 1152, 72);
            }
            toastBg.color = ok ? new Color(0.10f, 0.40f, 0.22f, 0.96f) : new Color(0.50f, 0.14f, 0.14f, 0.96f);
            toastT.text = (ok ? "✔  " : "✖  ") + msg; toastBg.gameObject.SetActive(true); toastBg.transform.SetAsLastSibling(); toastUntil = Time.unscaledTime + 4.5f;
        }
        MenuScreen building;
        public void RegisterSigClick(RectTransform rt) { AddClick(rt, () => { if (SigPanel.Current != null) SigPanel.Current.Begin(); }); }
        void AddClick(RectTransform rt, Action a) { if (building != null) building.Clicks.Add(new KeyValuePair<RectTransform, Action>(rt, a)); }
        // 好友頁的 UID 輸入:開啟時鍵盤字元直接進這個欄位(Enter 確認、Esc 取消、Ctrl+V 貼上)
        bool textMode; string textBuf = ""; Action<string> textDone;

        class Item
        {
            public Func<string> Text; public Action Click; public bool Enabled = true; public Func<bool> EnabledFn;
            public Func<string> Sub;   // 第二行小字(說明);有的話按鈕變成兩行版型
            public Item(Func<string> t, Action c, bool e = true) { Text = t; Click = c; Enabled = e; }
        }

        RectTransform root;
        Image dim;
        MenuScreen cur;
        bool menuVisible, splashing, inGameMenu;
        float busyRefreshT;
        bool justOpened, shownNewer; string shownProgress = "";
        float prevAxis;
        Action settingsBack;

        // ---------------------------------------------------------------- 初始化
        void Awake()
        {
            I = this;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-accttest") >= 0) { AcctTest.Run(); return; }   // 邏輯測試:只用記憶體,結果在 persistentDataPath\accttest.txt
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pickertest") >= 0)
            {   // 真實開檔視窗測試:開和「上傳頭像圖片」一模一樣的視窗,選完用同一個匯入函式(訪客=只在記憶體),結果寫 pickertest.txt 後離開
                Prefs.Isolated = true; Account.SetGuest();
                string pf = NativeDialogs.OpenFile("選擇頭像圖片", "圖片 (PNG, JPG, WebP, GIF, BMP)\0*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp\0所有檔案 (*.*)\0*.*\0");
                bool pok = !string.IsNullOrEmpty(pf) && Account.ImportAvatar(pf);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "pickertest.txt"), "picked=" + pf + "\nresult=" + NativeDialogs.LastResult + "\nimport=" + pok + "\nmsg=" + Account.LastMsg + "\nhasAvatar=" + (Account.AvatarTex != null) + "\n");
                Application.Quit(); return;
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-guest") >= 0) Account.SetGuest();   // 測試用:以訪客(只在記憶體)進場,截圖驗證訪客標示
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
            if (startMenu == "about") { ShowAbout(); yield break; }
            if (startMenu == "tutorial") { ShowTutorial(); yield break; }
            if (startMenu == "whatsnew") { ShowWhatsNew(); yield break; }
            if (startMenu == "pause") { ShowPause(); yield break; }
            if (startMenu == "settings") { settingsBack = ShowMain; ShowSettings(false); yield break; }
            if (startMenu == "modes") { ShowModes(); yield break; }
            if (startMenu == "catalog") { ShowSub(() => Z("開源機器人程式庫", "Open-source robot library"), () => Z("別隊公開的 2026 程式:下載後直接在模擬器跑", "Public 2026 team code: download and run"), CatalogItems()); yield break; }
            if (startMenu == "projects") { ShowProjectScan(); yield break; }
            if (startMenu == "models") { ShowSub(() => Z("開源機器人模型", "Open-source robot models"), () => Z("真實隊伍的 3D 機器人(AdvantageScope 公開資產)", "Real FRC team 3D robots (AdvantageScope assets)"), ModelLibItems()); yield break; }
            if (startMenu == "presets") { ShowSub(() => Z("內建機構", "Built-in mechanisms"), () => Z("不用機器人程式,直接用內建行為", "No robot code needed"), PresetItems()); yield break; }
            if (startMenu == "main") { ShowMain(); yield break; }
            if (startMenu == "picker") { ShowRobotPicker(); yield break; }
            if (startMenu == "language") { ShowLanguage(); yield break; }
            { // 測試:-avatarfile <圖檔> 走和「上傳頭像圖片」按鈕選完檔案後完全相同的程式(Account.ImportAvatar + 提示條);測試執行寫到 avatars_test,不碰真存檔
                var aa = System.Environment.GetCommandLineArgs(); int ai = System.Array.IndexOf(aa, "-avatarfile");
                if (ai >= 0 && ai + 1 < aa.Length) { bool aok = Account.ImportAvatar(aa[ai + 1]); avatarTestMsg = Account.LastMsg; avatarTestOk = aok; }
                int si = System.Array.IndexOf(aa, "-sig"); if (si >= 0 && si + 1 < aa.Length && SuperSample.IsTestRun()) Account.SigOverride = aa[si + 1];   // 截圖用:顯示指定簽名(不寫存檔)
            }
            if (startMenu == "profile" || startMenu == "profileguest")
            {
                if (startMenu == "profileguest") Account.SetGuest();
                ShowProfile(ShowMain); if (avatarTestMsg != null) Toast(avatarTestMsg, avatarTestOk);
                var ae = System.Environment.GetCommandLineArgs(); int ei = System.Array.IndexOf(ae, "-sigedit");   // 截圖用:直接進入編輯狀態並塞入文字
                if (ei >= 0 && ei + 1 < ae.Length && SigPanel.Current != null) SigPanel.Current.DebugEdit(ae[ei + 1]);
                yield break;
            }   // 測試:唯讀顯示目前檔位,不改任何存檔
            if (startMenu == "mainguest") { Account.SetGuest(); ShowMain(); yield break; }
            if (startMenu == "mainavatar") { ShowMain(); if (avatarTestMsg != null) Toast(avatarTestMsg, avatarTestOk); yield break; }
            if (startMenu == "friends") { ShowFriends(ShowMain); yield break; }
            if (startMenu == "friendsguest") { Account.SetGuest(); ShowFriends(ShowMain); yield break; }
            if (startMenu == "login") { ShowLogin(); yield break; }
            if (!Loc.HasChosen) ShowLanguage(); else MainOrLogin();
        }

        // 啟動:上次是 Google 帳號就直接進主選單(有玩過 → 正常運行);否則(沒登入過/上次是訪客)先選登入方式。測試旗標一律不碰存檔。
        void MainOrLogin() { if (SuperSample.IsTestRun() || Account.RestoreLast() || Profile.Guest) ShowMain(); else ShowLogin(); }

        // ---------------------------------------------------------------- 畫面建構
        static void FitTitle(Text t) { int n = 0; foreach (char c in t.text ?? "") n += c > 0x2E80 ? 2 : 1; t.fontSize = n <= 13 ? 64 : Mathf.Clamp(Mathf.RoundToInt(64f * 13.5f / n), 32, 64); t.horizontalOverflow = HorizontalWrapMode.Overflow; }
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
            s.Title.fontStyle = FontStyle.Bold; FitTitle(s.Title);   // 英文標題較長:依字數縮小字級,不超出面板
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
            bool twoLine = false; foreach (var it0 in items) if (it0.Sub != null) twoLine = true;
            if (twoLine) { step = Mathf.Min(96f, (985f - 330f) / Mathf.Max(1, items.Length)); bh = step - 8f; }
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
                if (it.Sub != null)
                {
                    // 兩行版型:上面主標題(固定 30),下面小字說明(20,淡色)
                    b.Label.resizeTextForBestFit = false; b.Label.fontSize = 29; b.Label.alignment = TextAnchor.MiddleLeft;
                    UiKit.PlaceTL(b.Label.rectTransform, 30, 3, 598, 38f);
                    var subL = UiKit.Label("SubText", b.Rt, it.Sub(), 18, new Color(0.79f, 0.83f, 0.90f, 0.9f), TextAnchor.UpperLeft);
                    subL.horizontalOverflow = HorizontalWrapMode.Wrap; subL.verticalOverflow = VerticalWrapMode.Truncate;
                    UiKit.PlaceTL(subL.rectTransform, 30, 41f, 598, bh - 43f);
                    var itS = it; s.Refreshers.Add(() => subL.text = itS.Sub());
                }
                s.Buttons.Add(b);
                y += step;
            }

            // 提示列
            var hint = UiKit.Label("Hint", rt, Loc.T("hint.nav"), 26, new Color(0.79f, 0.83f, 0.90f), TextAnchor.UpperLeft);
            UiKit.PlaceTL(hint.rectTransform, 90, 1010, 640, 30);   // 所有頁面提示列位置一致(原本長頁面是 1046,掉到面板外)
            s.Refreshers.Add(() => hint.text = Loc.T("hint.nav"));

            // 右下角浮水印
            var wm = UiKit.Label("Watermark", rt, "FRC 9427  ·  MADE IN UNITY  ·  v" + UpdateCheck.Current, 22, new Color(1, 1, 1, 0.25f), TextAnchor.LowerRight);
            UiKit.PlaceTL(wm.rectTransform, 1300, 1020, 580, 30);

            building = s;
            extra?.Invoke(go.GetComponent<RectTransform>());
            building = null;
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
            cur.Title.text = cur.TitleFn(); FitTitle(cur.Title);
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
        { PreviewRobot.Want = false;
            var s = Build(() => Loc.T("lang.pick"), null, new[]
            {
                new Item(() => "繁體中文", () => { Loc.Lang = "zh"; MainOrLogin(); }),
                new Item(() => "English",  () => { Loc.Lang = "en"; MainOrLogin(); }),
            }, null);
            Show(s, false);
        }

        void ShowMain()
        { PreviewRobot.Want = false;
            var s = Build(() => Loc.T("app.title"), () => Loc.T("app.sub") + "   ·   " + (Account.IsGuest ? Z("訪客(不存檔)", "Guest (not saved)") : Profile.CurrentName) + (CrashFlag.Msg != "" ? "   ⚠ " + CrashFlag.Msg : "") + (UpdateCheck.Progress != "" ? "   ●" + UpdateCheck.Progress : UpdateCheck.Newer ? (Loc.Lang == "zh" ? "   ●有新版 v" + UpdateCheck.Latest + "(按 U 自動更新)" : "   ●New v" + UpdateCheck.Latest + " (press U to update)") : ""), new[]
            {
                new Item(() => Z("快速對戰", "Quick match"), () => { GameSession.Rookie = false; GameSession.PracticeMode = false; GameSession.VsAi = true; ShowPadSetup(ShowRobotSetup, ShowMain); }),
                new Item(() => Loc.T("menu.start"),    ShowModes),
                new Item(() => Account.IsGuest ? Z("玩家檔案  ·  訪客(不存檔)", "Profile  ·  Guest (not saved)") : Z("玩家檔案", "Profile") + "  ·  Lv." + Account.Level + " " + Account.TierName(Loc.Lang == "zh"), () => ShowProfile(ShowMain)),
                new Item(() => Z("好友", "Friends"), () => ShowFriends(ShowMain)),
                new Item(() => Loc.T("menu.settings"), () => { settingsBack = ShowMain; ShowSettings(false); }),
                new Item(() => Loc.T("menu.controls"), ShowControls),
                new Item(() => Z("關於 / 致謝", "About / Credits"), ShowAbout),
                new Item(() => (WnUnseen() ? "● " : "") + Z("更新內容", "What's new"), ShowWhatsNew),
                new Item(() => Loc.T("menu.quit"),     Application.Quit),
            }, null, DrawPlayerCard);
            Show(s, false);
        }

        // ---- 主選單右上角玩家卡(液態玻璃):頭像(上傳的圖,沒有才用首字)/暱稱/等級+經驗條/段位徽章/UID;訪客只有「訪客」標籤;點整張卡開玩家檔案
        void DrawAvatar(RectTransform parent, float x, float y, float size, Color ring)
        {
            if (Account.IsCreator) CreatorFx.Glow(parent, x, y, size);
            var rg = UiKit.Glass("AvRing", parent, ring, size / 2f); UiKit.PlaceTL(rg.rectTransform, x, y, size, size);
            if (Account.IsCreator) CreatorFx.RingPulse(rg);
            float pad = size * 0.06f, inner = size - pad * 2f;
            var av = UiKit.Glass("Av", parent, Account.AvatarColor, inner / 2f); UiKit.PlaceTL(av.rectTransform, x + pad, y + pad, inner, inner);
            var tex = Account.AvatarTex;
            if (tex != null)
            {
                av.gameObject.AddComponent<Mask>().showMaskGraphic = true;   // 圓形裁切
                var ri = new GameObject("AvImg", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(av.transform, false); ri.texture = tex; ri.raycastTarget = false;
                var rr = ri.rectTransform; rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;
            }
            else { var ini = UiKit.Label("Ini", parent, Account.Initial, Mathf.RoundToInt(inner * 0.5f), Color.white, TextAnchor.MiddleCenter); ini.fontStyle = FontStyle.Bold; UiKit.PlaceTL(ini.rectTransform, x + pad, y + pad, inner, inner); }
        }
        void DrawPlayerCard(RectTransform content)
        {
            bool zh = Loc.Lang == "zh", guest = Account.IsGuest;
            bool cr = Account.IsCreator; float ex = cr ? 46f : 0f;   // 創作者多一行金色稱號,卡片加高
            var card = GlassPanel.Make(content, "PlayerCard", 1380, 40, 500, 250 + ex, 44f, GlassStyle.Card());
            card.Opacity = 1f;
            Color tc = guest ? new Color(1f, 0.62f, 0.25f) : Account.TierCol;
            DrawAvatar(content, 1406, 66, 150, tc);
            var nm = UiKit.Label("PName", content, Account.Name, 36, UiTheme.Text, TextAnchor.MiddleLeft); nm.fontStyle = FontStyle.Bold; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.PlaceTL(nm.rectTransform, 1580, 62, 285, 48);
            if (guest)
            {
                var chip = UiKit.Glass("GuestTag", content, new Color(1f, 0.62f, 0.25f, 0.40f), 17f); UiKit.PlaceTL(chip.rectTransform, 1580, 116, 130, 34);
                var ct = UiKit.Label("GuestTagT", content, Z("訪客", "GUEST"), 22, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter); ct.fontStyle = FontStyle.Bold; UiKit.PlaceTL(ct.rectTransform, 1580, 116, 130, 34);
                var gn = UiKit.Label("GuestNote", content, Z("不會儲存遊戲紀錄\n關閉遊戲後資料就消失", "Progress is not saved\nand is gone after you quit"), 17, new Color(1f, 0.85f, 0.6f, 0.95f), TextAnchor.UpperLeft); gn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.PlaceTL(gn.rectTransform, 1580, 152, 290, 52);
            }
            else
            {
                int lvl, into, need; Account.LevelInfo(out lvl, out into, out need);
                var pill = UiKit.Glass("RankPill", content, new Color(tc.r, tc.g, tc.b, 0.38f), 17f); UiKit.PlaceTL(pill.rectTransform, 1580, 116, 150, 34);
                var rt = UiKit.Label("RankT", content, Account.TierName(zh) + " " + Account.Division, 20, tc, TextAnchor.MiddleCenter); rt.fontStyle = FontStyle.Bold; UiKit.PlaceTL(rt.rectTransform, 1580, 116, 150, 34);
                var lv = UiKit.Label("Lv", content, "Lv." + lvl, 26, UiTheme.Text, TextAnchor.MiddleLeft); lv.fontStyle = FontStyle.Bold; UiKit.PlaceTL(lv.rectTransform, 1745, 114, 120, 38);
                Bar9(content, 1580, 168, 285, 12, need > 0 ? (float)into / need : 0f, UiTheme.Accent);
                var xp = UiKit.Label("Xp", content, "XP " + into + " / " + need, 22, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(xp.rectTransform, 1580, 184, 285, 24);
            }
            { // 個性簽名(單行,超出就刪成「…」,不加引號)
                string sg = Account.Signature; int u = 0, k = 0; foreach (char c in sg) { u += c > 0x2E80 ? 2 : 1; if (u > 24) break; k++; }
                if (sg.Length > k) sg = sg.Substring(0, k).TrimEnd() + "…";
                var sgl = UiKit.Label("PSig", content, sg, 22, new Color(0.91f, 0.933f, 0.969f, 0.85f), TextAnchor.MiddleLeft); UiKit.PlaceTL(sgl.rectTransform, 1580, 208, 288, 24);
            }
            if (cr) CreatorFx.Chip(content, 1406, 238, 460, 34);
            var uid = UiKit.Label("Uid", content, "UID  " + Account.UidText, 20, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(uid.rectTransform, 1410, 232 + ex, 330, 34);
            if (!guest && Account.Uid != "")
            {
                var cp = UiKit.Glass("CopyUid", content, new Color(1, 1, 1, 0.14f), 15f); UiKit.PlaceTL(cp.rectTransform, 1765, 234 + ex, 100, 30);
                var cl = UiKit.Label("CopyUidT", content, Z("複製", "Copy"), 18, UiTheme.Text, TextAnchor.MiddleCenter); UiKit.PlaceTL(cl.rectTransform, 1765, 234 + ex, 100, 30);
                AddClick(cp.rectTransform, () => { Account.CopyUid(); cl.text = Z("已複製", "Copied"); });
            }
            AddClick(card.Root, () => ShowProfile(ShowMain));
            if (!guest && Account.IsGoogle)
            {   // 卡片下方一行:目前登入的是誰 + 切換帳號/登出(點了回登入畫面,並忘掉「記住的帳號」)
                string em = Account.Email; if (em.Length > 26) em = em.Substring(0, 25) + "…";
                var so = UiKit.Glass("SignOutChip", content, new Color(0.05f, 0.08f, 0.14f, 0.55f), 18f); UiKit.PlaceTL(so.rectTransform, 1380, 40 + 250 + ex + 12, 500, 40);
                var sl = UiKit.Label("SignOutT", content, Z("已登入 ", "Signed in ") + em + Z("  ·  切換帳號 / 登出", "  ·  Switch / Sign out"), 20, new Color(0.88f, 0.92f, 0.98f, 0.95f), TextAnchor.MiddleCenter); UiKit.PlaceTL(sl.rectTransform, 1380, 40 + 250 + ex + 12, 500, 38);
                AddClick(so.rectTransform, () => { GoogleAuth.Cancel(); Account.SignOut(); ShowLogin(); });
            }
        }

        // ---------------------------------------------------------------- 好友(目前只有本機名單;線上同步要等伺服器)
        void ShowFriends(Action back)
        { PreviewRobot.Want = false;
            string err = null;
            Item[] items;
            var list = new List<FriendInfo>(Friends.Backend.List()); { var ca = Environment.GetCommandLineArgs(); int ci = Array.IndexOf(ca, "-cloudtestfriend"); if (ci >= 0 && ci + 1 < ca.Length) list.Add(new FriendInfo { Uid = ca[ci + 1], Name = "" }); }   // 測試:假裝有這個好友(只在畫面上,不存檔)
            bool cloudFr = CloudSync.Enabled && !Account.IsGuest;
            if (cloudFr)
            {   // 雲端好友:名單以雲端為準(申請→同意才算);進畫面先同步,同步完自動重畫一次
                list = new List<FriendInfo>(); foreach (var u in CloudSync.Friends) list.Add(new FriendInfo { Uid = u, Name = "" });
                { var ca2 = Environment.GetCommandLineArgs(); int ci2 = Array.IndexOf(ca2, "-cloudtestfriend"); if (ci2 >= 0 && ci2 + 1 < ca2.Length) list.Add(new FriendInfo { Uid = ca2[ci2 + 1], Name = "" }); }
                CloudSync.SyncFriends(); StartCoroutine(FriendsResync(back, CloudSync.FriendsVer));
            }
            var its = new List<Item> { new Item(() => Loc.T("menu.back"), back) };
            if (cloudFr) foreach (var inUid in CloudSync.Incoming)
            {
                string iu = inUid; CloudSync.Fetch(iu);
                its.Add(new Item(() => { var cp = CloudSync.Get(iu); return Z("同意 ", "Accept ") + (cp != null && cp.Loaded && cp.Name != "" ? cp.Name : "UID " + iu) + Z(" 的好友申請", "'s request"); }, () => CloudSync.Respond(iu, true, () => ShowFriends(back))) { Sub = () => "UID " + iu + Z("  ·  點一下同意", "  ·  tap to accept") });
                its.Add(new Item(() => Z("拒絕 UID ", "Decline UID ") + iu, () => CloudSync.Respond(iu, false, () => ShowFriends(back))));
            }
            if (Account.IsGuest)
                its.Add(new Item(() => Z("訪客沒有 UID,無法加好友", "Guests have no UID, so no friends"), () => { Account.SignOut(); ShowLogin(); }, true) { Sub = () => Z("點這裡回登入畫面,用 Google 登入後才有 UID", "Tap to go back and sign in with Google to get a UID") });
            else
            {
                its.Add(new Item(() => textMode ? Z("輸入對方 UID:", "Enter friend UID: ") + textBuf + "_" : Z("用 UID 加好友", "Add friend by UID"), () =>
                {
                    textMode = true; textBuf = ""; textDone = v =>
                    {
                        if (cloudFr)
                        {
                            ShowFriends(back);
                            if (!Friends.ValidUid(v)) { lastFriendErr = Z("UID 必須是 8 位數字", "UID must be 8 digits"); RefreshScreen(); return; }
                            if (v == Account.Uid) { lastFriendErr = Z("這是你自己的 UID", "That's your own UID"); RefreshScreen(); return; }
                            lastFriendErr = Z("送出中…", "Sending…"); RefreshScreen();
                            CloudSync.SendRequest(v, er => { lastFriendErr = er ?? Z("已送出申請,等對方同意後才會成為好友", "Request sent — you become friends once they accept"); RefreshScreen(); });
                            return;
                        }
                        string e; if (Friends.Backend.Add(v, Account.Uid, out e)) ShowFriends(back); else { ShowFriends(back); lastFriendErr = e; RefreshScreen(); }
                    };
                    RefreshScreen();
                }) { Sub = () => lastFriendErr != null ? lastFriendErr : Z("輸入 8 位數字後按 Enter(Esc 取消,Ctrl+V 可貼上)", "Type 8 digits then Enter (Esc cancels, Ctrl+V pastes)") });
                its.Add(new Item(() => Z("複製我的 UID", "Copy my UID") + ":  " + Account.UidText, () => Account.CopyUid()));
                its.Add(new Item(() => textMode ? Z("輸入要檢舉頭貼的 UID:", "Enter UID to report: ") + textBuf + "_" : Z("檢舉某人的頭貼", "Report someone's avatar"), () =>
                {
                    textMode = true; textBuf = ""; textDone = v => { ShowFriends(back); if (!Friends.ValidUid(v)) { lastFriendErr = Z("UID 必須是 8 位數字", "UID must be 8 digits"); RefreshScreen(); return; } CloudSync.Report(v, "avatar", ok => { lastFriendErr = ok ? Z("已送出檢舉,開發者會審查", "Report sent; the developer will review it") : Z("檢舉送出失敗(沒網路?)", "Report failed (offline?)"); RefreshScreen(); }); };
                    RefreshScreen();
                }) { Sub = () => lastFriendErr != null ? lastFriendErr : Z("頭貼不當就檢舉,會由開發者審查並可隱藏", "Report an inappropriate avatar; the developer reviews and can hide it") });
                foreach (var f in list)
                {
                    var ff = f;
                    its.Add(new Item(() => "UID " + ff.Uid + (ff.Name != "" ? "  ·  " + ff.Name : ""), () => { if (cloudFr) CloudSync.RemoveFriend(ff.Uid, () => ShowFriends(back)); else { Friends.Backend.Remove(ff.Uid); ShowFriends(back); } }) { Sub = () => (CloudSync.Enabled ? Z("雲端好友  ·  點一下移除", "Cloud friend  ·  tap to remove") : Z("離線(本機名單)  ·  點一下移除", "Offline (local list)  ·  tap to remove")) });
                }
            }
            lastFriendErr = null;
            items = its.ToArray();
            var s = Build(() => Z("好友", "Friends"), () => Z("你的 UID:", "Your UID: ") + Account.UidText, items, back, content =>
            {
                var card = GlassPanel.Make(content, "FrCard", 800, 40, 1080, 1000, 56f, GlassStyle.Card()); card.Opacity = 1f;
                var h = UiKit.Label("FrH", content, CloudSync.Enabled ? Z("線上好友", "Online friends") : Z("好友(目前離線)", "Friends (offline)"), 40, UiTheme.Accent, TextAnchor.UpperLeft); h.fontStyle = FontStyle.Bold; UiKit.PlaceTL(h.rectTransform, 860, 90, 960, 60);
                var hint = UiKit.Label("FrT", content, Z("對方的暱稱、等級、簽名和頭貼會從雲端讀取;你的名片也會公開給知道你 UID 的人看。頭貼被檢舉會交給開發者審查。", "Names, levels, signatures and avatars load from the cloud; your card is visible to anyone who has your UID. Reported avatars are reviewed by the developer."), 20, UiTheme.TextDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap; hint.verticalOverflow = VerticalWrapMode.Truncate; UiKit.PlaceTL(hint.rectTransform, 862, 150, 960, 56);
                var rows = new List<Action>();
                int nRows = Mathf.Min(list.Count, 8);
                for (int ri0 = 0; ri0 < nRows; ri0++)
                {
                    var fi = list[ri0]; float ry = 225 + ri0 * 96;
                    CloudSync.Fetch(fi.Uid);
                    var bg = UiKit.Glass("FrRow", content, new Color(1, 1, 1, 0.08f), 24f); UiKit.PlaceTL(bg.rectTransform, 862, ry, 960, 84);
                    var avc = UiKit.Glass("FrAv", content, new Color(0.25f, 0.4f, 0.7f, 0.9f), 32f); UiKit.PlaceTL(avc.rectTransform, 880, ry + 10, 64, 64);
                    avc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                    var img = new GameObject("FrImg", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                    img.transform.SetParent(avc.transform, false); img.raycastTarget = false; img.enabled = false;
                    var ir = img.rectTransform; ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one; ir.offsetMin = ir.offsetMax = Vector2.zero;
                    var ini = UiKit.Label("FrIni", content, "?", 30, Color.white, TextAnchor.MiddleCenter); UiKit.PlaceTL(ini.rectTransform, 880, ry + 10, 64, 64);
                    var nm = UiKit.Label("FrNm", content, "", 28, UiTheme.Text, TextAnchor.MiddleLeft); nm.fontStyle = FontStyle.Bold; UiKit.PlaceTL(nm.rectTransform, 962, ry + 6, 600, 36);
                    var sg = UiKit.Label("FrSg", content, "", 20, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(sg.rectTransform, 962, ry + 44, 840, 30);
                    rows.Add(() =>
                    {
                        var cp = CloudSync.Get(fi.Uid);
                        if (cp == null || !cp.Loaded) { nm.text = "UID " + fi.Uid + (fi.Name != "" ? "  ·  " + fi.Name : ""); sg.text = CloudSync.Enabled ? Z("讀取中…", "Loading…") : Z("離線", "Offline"); return; }
                        if (cp.NotFound) { nm.text = "UID " + fi.Uid; sg.text = Z("找不到這個玩家(對方還沒登入過新版,或 UID 打錯)", "Player not found (not signed in on a cloud version yet, or wrong UID)"); return; }
                        nm.text = (cp.Name != "" ? cp.Name : "UID " + fi.Uid) + "   Lv." + cp.Level + "  " + (Loc.Lang == "zh" ? Account.TiersZh : Account.Tiers)[Mathf.Clamp(cp.Rp / 100, 0, Account.Tiers.Length - 1)];
                        string pres = !cp.Online ? Z("離線", "Offline") : cp.Status == "match" ? Z("對戰中", "In match") : cp.Status == "train" ? Z("訓練中", "Training") : Z("線上", "Online");
                        sg.text = pres + "  ·  " + (cp.Hidden ? Z("(頭貼已被隱藏)  ", "(avatar hidden)  ") + cp.Sig : cp.Sig);
                        if (cp.Tex != null) { img.texture = cp.Tex; img.enabled = true; ini.text = ""; } else { img.enabled = false; ini.text = string.IsNullOrEmpty(cp.Name) ? "?" : cp.Name.Substring(0, 1).ToUpperInvariant(); }
                    });
                }
                if (nRows == 0) { var n0 = UiKit.Label("FrN", content, Z("還沒有好友。左邊用 UID 加好友,對方的名片會顯示在這裡。", "No friends yet. Add by UID on the left; their card shows here."), 26, UiTheme.TextDim, TextAnchor.UpperLeft); UiKit.PlaceTL(n0.rectTransform, 862, 240, 960, 60); }
                if (rows.Count > 0) { building.Refreshers.Add(() => { foreach (var r in rows) r(); }); StartCoroutine(FriendsLive(rows)); }
            }, firstY: 330f);
            Show(s, false);
        }
        string lastFriendErr;
        IEnumerator FriendsResync(Action back, int ver)
        {   // 好友畫面開著時,雲端名單同步完(版本變了)就重畫一次;輸入 UID 中不重畫
            yield return null; var scr = cur;
            float t0 = Time.unscaledTime;
            while (cur == scr && scr != null && Time.unscaledTime - t0 < 20f)
            {
                if (CloudSync.FriendsVer != ver && !textMode) { ShowFriends(back); yield break; }
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }
        IEnumerator FriendsLive(List<Action> rows)
        {   // 好友畫面開著時,每 0.5 秒刷新一次(雲端資料回來才會顯示)
            yield return null; var scr = cur;
            while (cur == scr && scr != null) { foreach (var r in rows) r(); yield return new WaitForSecondsRealtime(0.5f); }
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
            if (!Pad.DriverOnline && Pad.Mode != 1) Pad.Mode = 1;   // 沒接手把(純鍵盤):預設「1 支」,「下一步」才不會被鎖住(學長回報:沒手把開不了遊戲)
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
        { PreviewRobot.Want = false;
            var s = Build(() => Loc.T("mode.title"), () => Z("練習不限時;比賽 160 秒、紅方由 AI 對戰", "Practice: untimed. Match: 160 s vs AI"), new[]
            {
                new Item(() => Z("練習模式(不限時)", "Practice (no time limit)"), () => { GameSession.Rookie = false; GameSession.PracticeMode = true; GameSession.VsAi = false; ShowPadSetup(ShowRobotSetup, ShowModes); }),
                new Item(() => Z("新手練習場(對戰超弱 AI)", "Rookie field (vs a terrible AI)"), () => { GameSession.Rookie = true; GameSession.PracticeMode = true; GameSession.VsAi = true; ShowPadSetup(ShowRobotSetup, ShowModes); }) { Sub = () => Z("不限時,有步驟教你移動、吸球、射擊", "Untimed, with step-by-step coaching") },
                new Item(() => Z("模擬比賽(對戰 AI)", "Match vs AI"), ShowAiLevel),
                new Item(() => Z("單人挑戰(160 秒,刷最高分)", "Solo score attack (160 s)"), () => { GameSession.Rookie = false; GameSession.PracticeMode = false; GameSession.VsAi = false; ShowPadSetup(ShowRobotSetup, ShowModes); }),
                new Item(() => Z("自動展示(Demo)", "Auto demo"), () => { GameSession.Demo = true; GameSession.Rookie = false; GameSession.PracticeMode = false; GameSession.VsAi = true; Hide(); GameSession.Begin(Rig); }) { Sub = () => Z("AI 幫你開一整場,不用操作(給人看模擬器用)", "AI plays a full match for you — hands-free showcase") },
                new Item(() => Loc.T("menu.back"),  ShowMain),
            }, ShowMain);
            Show(s, false);
        }

        void ShowAiLevel()
        {
            string[] zh = { "簡單", "普通", "困難", "超困難" }, en = { "Easy", "Normal", "Hard", "Insane" };
            Item Lv(int i) => new Item(() => (GameSession.AiLevel == i ? "● " : "○ ") + (Loc.Lang == "zh" ? zh[i] : en[i]), () => { GameSession.Rookie = false; GameSession.AiLevel = i; GameSession.PracticeMode = false; GameSession.VsAi = true; ShowPadSetup(ShowRobotSetup, ShowAiLevel); });
            var s = Build(() => Z("對手難度", "Opponent level"), () => Z("紅隊 AI 會自己撿球、射 HUB", "The red team plays by itself"), new[]
            {
                Lv(0), Lv(1), Lv(2), Lv(3),
                new Item(() => Loc.T("menu.back"), ShowModes),
            }, ShowModes);
            Show(s, false);
        }
        // 進遊戲前:選機器人模型、匯入 .glb、選機器人程式專案
        // ---- 機器人設定:分成「外觀 / 機器人程式 / 手感 / 比賽」四個分類,主頁只留入口與「開始」
        Item ItModel() => new Item(() => Loc.T("set.model") + ":  " + (RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Display(RobotModels.Selected)), () =>
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
        // ---- 機器人程式:上面一格 = 目前使用的程式,下面一格 = 匯入自己的程式(大視窗挑選,像 Minecraft 匯入資源包)
        Item ItProjectCur() => new Item(() =>
        {
            string c = Prefs.GetString("robotProject", "");
            return Z("目前使用的程式", "Current program") + ":  " + (c == "" ? Loc.T("setup.none") : System.IO.Path.GetFileName(c.TrimEnd('\\', '/')));
        }, () => { }) { Sub = () => { string c = Prefs.GetString("robotProject", ""); return c == "" ? Z("使用內建行為(可從下面匯入自己的程式)", "Using built-in behavior (import yours below)") : c; } };
        Item ItProjectImport() => new Item(() => Z("匯入自己的程式…", "Import your own program…"), ShowProjectScan) { Sub = () => Z("自動找出電腦裡的 FRC 專案,點選即可使用", "Finds FRC projects on this PC — click one to use it") };

        static List<string> foundProjects; static bool scanDone; static string foundErr;
        static void ScanProjects()
        {
            var found = new List<string>();
            try
            {
                string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                var roots = new List<string>
                {
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    System.IO.Path.Combine(home, "Downloads"), @"C:\FRC", home,
                    System.IO.Path.Combine(home, "OneDrive"), System.IO.Path.Combine(home, "source"), System.IO.Path.Combine(home, "Documents", "GitHub"),
                };
                string[] skip = { "node_modules", ".git", "build", "bin", "obj", ".gradle", "appdata", "library", "temp", ".claude", ".vscode", "wpilib" };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var t0 = DateTime.UtcNow; int visited = 0;
                void Walk(string dir, int depth)
                {
                    if (depth > 4 || visited > 30000 || (DateTime.UtcNow - t0).TotalSeconds > 10) return;
                    visited++;
                    string bg = System.IO.Path.Combine(dir, "build.gradle");
                    if (System.IO.File.Exists(bg))
                    {
                        try { if (System.IO.File.ReadAllText(bg).IndexOf("wpilib", StringComparison.OrdinalIgnoreCase) >= 0 && seen.Add(dir)) found.Add(dir); } catch { }
                        return;
                    }
                    string[] subs; try { subs = System.IO.Directory.GetDirectories(dir); } catch { return; }
                    foreach (var sd in subs)
                    {
                        string n = System.IO.Path.GetFileName(sd).ToLowerInvariant();
                        if (n.StartsWith(".") || Array.IndexOf(skip, n) >= 0) continue;
                        Walk(sd, depth + 1);
                    }
                }
                foreach (var r in roots) if (System.IO.Directory.Exists(r)) Walk(r, 0);
                foundErr = null;
            }
            catch (Exception e) { foundErr = e.Message; }
            foundProjects = found; scanDone = true;
        }

        void ShowProjectScan()
        { PreviewRobot.Want = false;
            foundProjects = null; scanDone = false;
            System.Threading.Tasks.Task.Run((Action)ScanProjects);
            var s = Build(() => Z("匯入自己的程式", "Import your program"), () => Z("正在掃描電腦裡的 FRC 專案…", "Scanning this PC for FRC projects…"), new[] { new Item(() => Loc.T("menu.back"), BackToRobotCode) }, BackToRobotCode);
            Show(s, false);
            StartCoroutine(WaitScan(s));
        }
        IEnumerator WaitScan(MenuScreen s)
        {
            float t0 = Time.unscaledTime;
            while (!scanDone && Time.unscaledTime - t0 < 15f) yield return null;
            if (cur == s) ShowProjectPicker(0);
        }
        void BackToRobotCode()
        {
            ShowSub(() => Z("機器人程式", "Robot code"), () => Z("專案、是否跑真實程式、操控方式", "Project, real code, controls"), RobotCodeItems());
        }

        void ShowProjectPicker(int page)
        { PreviewRobot.Want = false;
            var all = foundProjects ?? new List<string>();
            { string c0 = Prefs.GetString("robotProject", ""); if (c0 != "" && System.IO.Directory.Exists(c0) && !all.Exists(x => string.Equals(x, c0, StringComparison.OrdinalIgnoreCase))) all.Insert(0, c0); }
            const int PerPage = 5;
            int pages = Mathf.Max(1, (all.Count + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            var pageList = all.GetRange(page * PerPage, Mathf.Min(PerPage, all.Count - page * PerPage));
            var items = new List<Item>();
            string curProj = Prefs.GetString("robotProject", "");
            foreach (var p0 in pageList)
            {
                string p = p0;
                items.Add(new Item(() => (string.Equals(curProj, p, StringComparison.OrdinalIgnoreCase) ? "✓ " : "") + System.IO.Path.GetFileName(p.TrimEnd('\\', '/')) + "   ·   " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p.TrimEnd('\\', '/')) ?? ""), () =>
                {
                    Prefs.SetString("robotProject", p); Prefs.SetInt("useRealCode", 1); PlayerPrefs.Save();
                    Toast(Z("已選用:", "Now using: ") + System.IO.Path.GetFileName(p.TrimEnd('\\', '/')), true);
                    BackToRobotCode();
                }));
            }
            if (all.Count == 0) items.Add(new Item(() => Z("沒找到 FRC 專案", "No FRC projects found"), () => { }) { Sub = () => foundErr ?? Z("請按下面「瀏覽其他資料夾」手動選", "Use “Browse other folder” below") });
            if (pages > 1) items.Add(new Item(() => Z("下一頁", "Next page") + "  (" + (page + 1) + "/" + pages + ")", () => ShowProjectPicker((page + 1) % pages)));
            items.Add(new Item(() => Z("瀏覽其他資料夾…", "Browse other folder…"), () =>
            {
                string p = NativeDialogs.PickFolder(Loc.T("setup.code"));
                if (string.IsNullOrEmpty(p)) return;
                Prefs.SetString("robotProject", FindGradleRoot(p)); Prefs.SetInt("useRealCode", 1); PlayerPrefs.Save();
                BackToRobotCode();
            }));
            items.Add(new Item(() => Z("重新掃描", "Rescan"), ShowProjectScan));
            items.Add(new Item(() => Loc.T("menu.back"), BackToRobotCode));
            int nProj = pageList.Count;
            var s = Build(() => Z("匯入自己的程式", "Import your program"), () => Z("找到 " + all.Count + " 個專案 · 點一下就使用", all.Count + " projects found · click to use"), items.ToArray(), BackToRobotCode, content =>
            {
                var card = GlassPanel.Make(content, "DetailCard", 800, 40, 1080, 1000, 64f, GlassStyle.Card());
                card.Opacity = 1f;
                var head = UiKit.Label("DetHead", content, "", 44, UiTheme.Text, TextAnchor.UpperLeft); head.fontStyle = FontStyle.Bold; head.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.PlaceTL(head.rectTransform, 850, 100, 980, 70);
                var body = UiKit.Label("DetBody", content, "", 26, UiTheme.TextDim, TextAnchor.UpperLeft);
                body.lineSpacing = 1.2f; body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.PlaceTL(body.rectTransform, 850, 200, 980, 700);
                building.Refreshers.Add(() =>
                {
                    int i = cur != null ? cur.Sel : 0;
                    if (i < 0 || i >= nProj) { head.text = Z("匯入自己的程式", "Import your program"); body.text = Z("左邊列出電腦裡找到的 FRC 機器人專案(有 build.gradle 的資料夾)。\n\n點一下專案就會選用,並開啟「跑真實程式」。\n找不到的話按「瀏覽其他資料夾…」。", "Projects found on this PC are listed on the left (folders with a build.gradle).\n\nClick one to use it; “run real code” is turned on.\nNot listed? Use “Browse other folder…”."); return; }
                    string p = pageList[i];
                    head.text = System.IO.Path.GetFileName(p.TrimEnd('\\', '/'));
                    string mod = ""; try { mod = System.IO.Directory.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm"); } catch { }
                    string prof = ""; try { prof = System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Sim", SimProject.Profile(p) + ".mech.json")) ? Z("有(能用真實程式模擬)", "Yes (real-code sim works)") : Z("沒有(會改用內建行為)", "No (falls back to built-in)"); } catch { }
                    body.text = Z("位置", "Location") + "\n" + p + "\n\n" + Z("最後修改", "Last modified") + ":  " + mod + "\n" + Z("模擬設定檔", "Simulation profile") + ":  " + prof + "\n\n" + (string.Equals(Prefs.GetString("robotProject", ""), p, StringComparison.OrdinalIgnoreCase) ? Z("✓ 目前使用中", "✓ Currently in use") : Z("按 Enter / 點一下 = 使用這個程式", "Press Enter / click = use this program"));
                });
            });
            Show(s, false);
        }

        Item[] RobotCodeItems() => new[] { ItProjectCur(), ItProjectImport(), ItReal(), ItCtl(), new Item(() => Z("內建機構(5 款)  ▸", "Built-in mechanisms (5)  ▸"), () => ShowSub(() => Z("內建機構", "Built-in mechanisms"), () => Z("不用機器人程式,直接用內建行為;各有外觀與手感", "No robot code needed — each has its own look and feel"), PresetItems())), new Item(() => Z("開源機器人模型庫(3D)  ▸", "Open-source robot models (3D)  ▸"), () => ShowSub(() => Z("開源機器人模型", "Open-source robot models"), () => Z("真實隊伍的 3D 機器人(AdvantageScope 公開資產),按一下下載並使用", "Real FRC team 3D robots (AdvantageScope public assets) — click to download & use"), ModelLibItems())), new Item(() => Z("開源機器人程式庫  ▸", "Open-source robot library  ▸"), () => ShowSub(() => Z("開源機器人程式庫", "Open-source robot library"), () => Z("別隊公開的 2026 程式:下載後直接在模擬器跑(來源 GitHub,未打包)", "Public 2026 team code: download and run (from GitHub, not bundled)"), CatalogItems())) };
        Item[] PresetItems()
        {
            var l = new List<Item>();
            for (int i = 0; i < MechPresets.All.Length; i++)
            {
                int i2 = i; var p = MechPresets.All[i];
                l.Add(new Item(() => (PlayerPrefs.HasKey("mechPreset") && Prefs.GetInt("useRealCode", 0) == 0 && MechPresets.Index == i2 ? "✓ " : "") + Z(p.ZhName, p.EnName), () => MechPresets.Choose(i2)) { Sub = () => Z(p.ZhDesc, p.EnDesc) });
            }
            return l.ToArray();
        }        Item[] ModelLibItems()
        {
            var l = new List<Item>();
            var allM = new List<ModelLibrary.M>(ModelLibrary.All); allM.AddRange(ModelLibrary.Customs());
            foreach (var mm in allM)
            {
                var m2 = mm;
                l.Add(new Item(() =>
                {
                    string st = m2.State == "…" ? Z("下載中…", "Downloading…") : m2.State != "" ? m2.State : ModelLibrary.Installed(m2) ? (RobotModels.Selected == m2.File ? Z("使用中 ✓", "In use ✓") : Z("已下載", "Installed")) : Z("按一下下載", "Download");
                    return ModelLibrary.Name(m2) + "  —  " + st;
                }, () =>
                {
                    if (!ModelLibrary.Installed(m2)) { ModelLibrary.Install(m2, () => { RobotModels.Selected = m2.File; PlayerPrefs.DeleteKey("modelYaw"); }); return; }
                    RobotModels.Selected = m2.File; PlayerPrefs.DeleteKey("modelYaw");
                }));
            }
            l.Add(new Item(() => importing ? Z("匯入中…(轉檔)", "Importing…") : Z("匯入 CAD…(incoming 資料夾)", "Import CAD… (incoming folder)"), () => { if (importing) return; importing = true; ModelLibrary.ImportIncoming(() => importing = false); }));
            return l.ToArray();
        }
        bool importing;
        Item[] CatalogItems()
        {
            var l = new List<Item>();
            foreach (var en in OpenSourceCatalog.All)
            {
                var en2 = en;
                l.Add(new Item(() =>
                {
                    string st = en2.State == "…" ? OpenSourceCatalog.Busy(en2) : en2.State != "" ? en2.State
                        : OpenSourceCatalog.Installed(en2) ? (Prefs.GetString("robotProject", "") == en2.Dir ? Z("使用中 ✓", "In use ✓") : Z("已下載,按一下使用", "Installed — click to use")) : Z("按一下下載", "Click to download");
                    string tag = en2.Status == "ok" ? Z("可用", "Works") : en2.Status == "partial" ? Z("部分", "Partial") : Z("未測試", "Untested");
                    return en2.Team + "   [" + tag + "]";
                }, () =>
                {
                    if (!OpenSourceCatalog.Installed(en2)) { OpenSourceCatalog.Install(en2); return; }
                    Prefs.SetString("robotProject", en2.Dir); Prefs.SetInt("useRealCode", 1); OpenSourceCatalog.UseModel(en2); Prefs.SetInt("tankMode", 0); PlayerPrefs.Save();
                })
                {
                    Sub = () =>
                    {
                        var lm = string.IsNullOrEmpty(en2.LibModel) ? null : ModelLibrary.Find(en2.LibModel);
                        string mdl = lm == null ? "" : Z("模型", "Model") + ": " + ModelLibrary.Name(lm) + (ModelLibrary.Installed(lm) ? "" : (lm.State == "…" ? Z("(下載中…)", "(downloading…)") : "")) + "   ·   ";
                        return (en2.State == "…" ? OpenSourceCatalog.Busy(en2) : en2.State != "" ? en2.State : OpenSourceCatalog.Installed(en2) ? (Prefs.GetString("robotProject", "") == en2.Dir ? Z("使用中 ✓", "In use ✓") : Z("已下載,按一下使用", "Installed — click to use")) : Z("按一下下載", "Click to download")) + "   ·   " + mdl + OpenSourceCatalog.NoteFor(en2);
                    }
                });
            }
            return l.ToArray();
        }        Item ItReal() => new Item(() => Loc.T("setup.real") + ":  " + Loc.T(Prefs.GetInt("useRealCode", 0) == 1 ? "on" : "off"), () => { Prefs.SetInt("useRealCode", Prefs.GetInt("useRealCode", 0) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItCtl() => new Item(() => Loc.T("setup.ctl") + ":  " + (Prefs.GetInt("useRealCode", 0) == 1 && !string.IsNullOrEmpty(Prefs.GetString("robotProject", "")) ? Z("依機器人程式自動", "Auto (from robot code)") : Loc.T(Prefs.GetInt("tankMode", 0) == 1 ? "ctl.tank" : "ctl.swerve")), () => { if (Prefs.GetInt("useRealCode", 0) == 1 && !string.IsNullOrEmpty(Prefs.GetString("robotProject", ""))) return; Prefs.SetInt("tankMode", Prefs.GetInt("tankMode", 0) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItFeel() => new Item(() => Z("手感模式", "Handling mode") + ":  " + (RobotInput.FeelEasy ? Z("順手(推一半≈一半速度)", "Easy (half stick ≈ half speed)") : Z("忠實(照機器人程式)", "Faithful (as the robot code)")), () => { Prefs.SetInt("feelEasy", RobotInput.FeelEasy ? 0 : 1); PlayerPrefs.Save(); }) { Sub = () => Z("只影響跑真實機器人程式時的搖桿:忠實=跟真車一樣肉;順手=先補償程式的平方曲線", "Only affects real robot code: faithful = same sluggish curve as the real robot; easy = pre-compensates the code's squaring") };
        Item ItSpeed() => new Item(() => Loc.T("setup.speed") + ":  " + SettingsStore.MaxSpeedChoice.ToString("0.0") + " m/s", () => { SettingsStore.SpeedIndex = (SettingsStore.SpeedIndex + 1) % SettingsStore.SpeedOptions.Length; });
        Item ItAccel() => new Item(() => Loc.T("setup.accel") + ":  " + SettingsStore.AccelChoice.ToString("0") + " m/s²", () => { SettingsStore.AccelIndex = (SettingsStore.AccelIndex + 1) % SettingsStore.AccelOptions.Length; });
        Item ItAutoHP() => new Item(() => Z("人類球員自動放球", "Auto human player") + ":  " + Loc.T(Prefs.GetInt("autoHP", 1) == 1 ? "on" : "off"), () => { Prefs.SetInt("autoHP", Prefs.GetInt("autoHP", 1) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItSecond() => new Item(() => Loc.T("setup.second") + ":  " + Loc.T(Prefs.GetInt("secondRobot", 1) == 1 ? "on" : "off"), () => { Prefs.SetInt("secondRobot", Prefs.GetInt("secondRobot", 1) == 1 ? 0 : 1); PlayerPrefs.Save(); });
        Item ItLevel()
        {
            string[] zh = { "簡單", "普通", "困難", "超困難" }, en = { "Easy", "Normal", "Hard", "Insane" };
            return new Item(() => Z("對手難度", "Opponent") + ":  " + (Loc.Lang == "zh" ? zh : en)[GameSession.AiLevel], () => { GameSession.AiLevel = (GameSession.AiLevel + 1) % 4; }) { EnabledFn = () => GameSession.VsAi };
        }

        void ShowSub(Func<string> title, Func<string> sub, Item[] items)
        { PreviewRobot.Boot(); PreviewRobot.Want = true;
            var all = new List<Item>(items) { new Item(() => Loc.T("menu.back"), ShowRobotSetup) };
            Show(Build(title, sub, all.ToArray(), ShowRobotSetup), false);
        }

        void ShowRobotSetup()
        { PreviewRobot.Boot(); PreviewRobot.Want = true;
            string Sum()
            {
                string m = RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Display(RobotModels.Selected);
                string c = Prefs.GetString("robotProject", "");
                string cc = c == "" ? Loc.T("setup.none") : System.IO.Path.GetFileName(c.TrimEnd('\\', '/'));
                return m + "  ·  " + cc;
            }
            var s = Build(() => Loc.T("setup.title"), Sum, new[]
            {
                new Item(() => Z("選擇機器人  ▸", "Choose robot  ▸"), ShowRobotPicker) { Sub = () => Z("預覽車在場上,底下一排卡片點選", "Preview on the field, pick from the cards below") },
                new Item(() => Z("外觀  ▸", "Appearance  ▸"), () => ShowSub(() => Z("外觀", "Appearance"), () => Z("機器人模型與方向", "Robot model & orientation"), new[] { ItModel(), ItImport(), ItYaw() })),
                new Item(() => Z("機器人程式  ▸", "Robot code  ▸"), BackToRobotCode),
                new Item(() => Z("手感  ▸", "Handling  ▸"), () => ShowSub(() => Z("手感", "Handling"), () => Z("最高車速與加速度(慣性)", "Top speed & acceleration"), new[] { ItFeel(), ItSpeed(), ItAccel() })),
                new Item(() => Z("比賽  ▸", "Match  ▸"), () => ShowSub(() => Z("比賽", "Match"), () => Z("對手機器人與難度", "Opponent robot & level"), new[] { ItAutoHP(), ItSecond(), ItLevel() })),
                new Item(() => Loc.T("setup.start"), StartGame),
                new Item(() => Loc.T("menu.back"), ShowModes),
            }, ShowModes);
            Show(s, false);
        }
        // ---------------------------------------------------------------- 選擇機器人(MoSim 風格:預覽車在場上、底下一排卡片,點選即換)
        int pickerFirst;   // 卡片條目前顯示的第一張索引
        void ShowRobotPicker()
        { PreviewRobot.Boot(); PreviewRobot.Want = true;
            var all = new List<string> { "" }; all.AddRange(RobotModels.List());
            int sel = Mathf.Max(0, all.IndexOf(RobotModels.Selected));
            const int Vis = 7;
            if (sel < pickerFirst) pickerFirst = sel; else if (sel >= pickerFirst + Vis) pickerFirst = sel - Vis + 1;
            pickerFirst = Mathf.Clamp(pickerFirst, 0, Mathf.Max(0, all.Count - Vis));
            void Pick(int i) { RobotModels.Selected = all[Mathf.Clamp(i, 0, all.Count - 1)]; PlayerPrefs.DeleteKey("modelYaw"); ShowRobotPicker(); }
            string Num(string f) { if (f == "") return Z("內建", "Built-in"); var m = System.Text.RegularExpressions.Regex.Match(f, @"^team_(\d+)"); if (m.Success) return m.Groups[1].Value; string nm = System.IO.Path.GetFileNameWithoutExtension(f).Replace("_", " "); return nm.Length > 9 ? nm.Substring(0, 8) + "…" : nm; }
            string Tag(string f) { if (f == "") return Z("方塊車", "Block bot"); if (f.StartsWith("team_")) return "★ " + Z("真車 CAD", "Real CAD"); return Z("自訂模型", "Custom"); }
            var s = Build(() => Z("選擇機器人", "Choose robot"), () => (all[sel] == "" ? Loc.T("model.builtin") : RobotModels.Display(all[sel])) + "   (" + (sel + 1) + "/" + all.Count + ")", new[]
            {
                new Item(() => Loc.T("setup.start"), StartGame) { Sub = () => Z("用目前選的車與設定開始比賽", "Start the match with this robot") },   // 主要動作放第一個
                new Item(() => Z("◀ 上一台", "◀ Previous"), () => Pick((sel - 1 + all.Count) % all.Count)),
                new Item(() => Z("下一台 ▶", "Next ▶"), () => Pick((sel + 1) % all.Count)),
                new Item(() => Z("比賽選項  ▸", "Match options  ▸"), () => ShowSub(() => Z("比賽選項", "Match options"), () => Z("對手難度、第二台機器人、人類球員", "Opponent level, second robot, human player"), new[] { ItLevel(), ItSecond(), ItAutoHP() })),   // 像 MoSim 右側 Match Settings,收進子頁讓主列表不擁擠
                new Item(() => Z("機器人程式  ▸", "Robot code  ▸"), BackToRobotCode),
                new Item(() => Loc.T("menu.back"), ShowRobotSetup),
            }, ShowRobotSetup, content =>
            {
                var hd = UiKit.Label("PkH", content, Z("機器人", "Robots") + "   " + Z("點卡片或用 ◀ ▶ 切換", "click a card or use ◀ ▶"), 30, UiTheme.Text, TextAnchor.MiddleLeft); hd.fontStyle = FontStyle.Bold; UiKit.PlaceTL(hd.rectTransform, 800, 735, 1000, 40);
                for (int k = 0; k < Vis && pickerFirst + k < all.Count; k++)
                {
                    int idx = pickerFirst + k; string f = all[idx]; bool on = idx == sel; float x = 800 + k * 152;
                    var bg = UiKit.Glass("PkCard", content, on ? new Color(0.20f, 0.45f, 0.95f, 0.92f) : CardTint(f), 22f); UiKit.PlaceTL(bg.rectTransform, x, 780, 142, 160);
                    var bt = bg.gameObject.AddComponent<Button>(); bt.targetGraphic = bg; int ii = idx; bt.onClick.AddListener(() => Pick(ii));
                    var n = UiKit.Label("PkN", content, Num(f), Num(f).Length > 4 ? 26 : 40, on ? Color.white : UiTheme.Text, TextAnchor.MiddleCenter); n.fontStyle = FontStyle.Bold; n.raycastTarget = false; UiKit.PlaceTL(n.rectTransform, x, 810, 142, 60);
                    var t = UiKit.Label("PkT", content, Tag(f), 17, on ? Color.white : new Color(0.78f, 0.84f, 0.94f, 1f), TextAnchor.MiddleCenter); t.raycastTarget = false; UiKit.PlaceTL(t.rectTransform, x, 875, 142, 28);
                    if (on) { var c = UiKit.Label("PkC", content, "✓ " + Z("使用中", "In use"), 18, Color.white, TextAnchor.MiddleCenter); c.raycastTarget = false; UiKit.PlaceTL(c.rectTransform, x, 905, 142, 28); }
                }
                if (pickerFirst > 0) { var l = UiKit.Label("PkL", content, "◀", 28, UiTheme.TextDim, TextAnchor.MiddleCenter); l.raycastTarget = false; UiKit.PlaceTL(l.rectTransform, 762, 840, 36, 40); }
                if (pickerFirst + Vis < all.Count) { var r = UiKit.Label("PkR", content, "▶", 28, UiTheme.TextDim, TextAnchor.MiddleCenter); r.raycastTarget = false; UiKit.PlaceTL(r.rectTransform, 1866, 840, 36, 40); }
            });
            Show(s, false);
            StartCoroutine(PickerKeys(() => Pick((sel - 1 + all.Count) % all.Count), () => Pick((sel + 1) % all.Count)));
        }
        // 卡片底色:真車 CAD 依隊號給固定色相(像隊徽的顏色辨識),其他維持深藍灰
        static Color CardTint(string f)
        {
            var m = System.Text.RegularExpressions.Regex.Match(f ?? "", @"^team_(\d+)");
            if (!m.Success) return new Color(0.08f, 0.12f, 0.2f, 0.78f);
            int n = int.Parse(m.Groups[1].Value); float hue = ((n * 37) % 100) / 100f;
            var c = Color.HSVToRGB(hue, 0.55f, 0.34f); c.a = 0.82f; return c;
        }
        IEnumerator PickerKeys(Action prev, Action next)
        {   // 選車畫面:鍵盤 ← → 直接換車(畫面一換掉就結束)
            yield return null; var scr = cur;
            while (cur == scr && scr != null)
            {
                if (!textMode) { if (Input.GetKeyDown(KeyCode.LeftArrow)) { prev(); yield break; } if (Input.GetKeyDown(KeyCode.RightArrow)) { next(); yield break; } }
                yield return null;
            }
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
        { PreviewRobot.Want = false;
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
                new Item(() => Loc.T("set.model") + ":  " + (RobotModels.Selected == "" ? Loc.T("model.builtin") : RobotModels.Display(RobotModels.Selected)), () =>
                {
                    var list = new List<string> { "" };
                    list.AddRange(RobotModels.List());
                    int i = list.IndexOf(RobotModels.Selected);
                    RobotModels.Selected = list[(i + 1) % list.Count];
                    RefreshScreen();
                }),
                new Item(() => Z("帳號", "Account") + ":  " + (Account.IsGuest ? Z("訪客(不存檔)", "Guest (not saved)") : Account.Name), () => { Account.SignOut(); ShowLogin(); }) { Sub = () => Z("點一下登出並回到登入畫面", "Tap to sign out and return to the sign-in screen"), EnabledFn = () => !game },
                new Item(() => Z("手把設定  ▸", "Controllers  ▸"), () => ShowPadSetup(() => ShowSettings(game), () => ShowSettings(game))),
                new Item(() => Z("聲音與回饋  ▸", "Sound & feedback  ▸"), () => ShowSoundSettings(game)),
                new Item(() => Z("後製特效", "Post effects") + ":  " + (PostFX.Quality == 0 ? Z("關", "Off") : PostFX.Quality == 1 ? Z("基本", "Basic") : PostFX.Quality == 2 ? Z("泛光", "Bloom") : "AO"), () => { PostFX.Quality = (PostFX.Quality + 1) % 3; }),
                new Item(() => Z("液態玻璃(毛玻璃)", "Liquid glass") + ":  " + (UiGlass.Disabled ? Z("關", "Off") : Z("開", "On")), () => { UiGlass.Disabled = !UiGlass.Disabled; if (UiGlass.Disabled) UiGlass.Ready = false; Prefs.SetInt("noGlass", UiGlass.Disabled ? 1 : 0); PlayerPrefs.Save(); }),
                new Item(() => Loc.T("menu.back"), () => settingsBack?.Invoke()),
            }, () => settingsBack?.Invoke());
            Show(s, game);
        }

        void ShowControls()
        { PreviewRobot.Want = false;
            var s = Build(() => Loc.T("ctl.title"), null, new[]
            {
                new Item(() => Loc.T("menu.back"), ShowMain),
            }, ShowMain, content =>
            {
                // 右側大卡片、兩欄(鍵盤 | 手把),字級 22:原本擠在左側窄面板裡只有 17px,還把底部提示列擠出面板
                var card = GlassPanel.Make(content, "CtlCard", 800, 40, 1080, 1000, 56f, GlassStyle.Card()); card.Opacity = 1f;
                var kbT = UiKit.Label("KbT", content, Loc.T("ctl.kb"), 34, UiTheme.Accent, TextAnchor.UpperLeft); kbT.fontStyle = FontStyle.Bold;
                UiKit.PlaceTL(kbT.rectTransform, 850, 84, 480, 46);
                var kb = UiKit.Label("Kb", content, Loc.T("ctl.kb.body"), 22, UiTheme.Text, TextAnchor.UpperLeft);
                kb.lineSpacing = 1.18f; kb.horizontalOverflow = HorizontalWrapMode.Wrap; kb.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.PlaceTL(kb.rectTransform, 850, 140, 500, 840);
                var pdT = UiKit.Label("PadT", content, Loc.T("ctl.pad"), 34, UiTheme.Accent, TextAnchor.UpperLeft); pdT.fontStyle = FontStyle.Bold;
                UiKit.PlaceTL(pdT.rectTransform, 1380, 84, 460, 46);
                var pd = UiKit.Label("Pad", content, Loc.T("ctl.pad.body"), 22, UiTheme.Text, TextAnchor.UpperLeft);
                pd.lineSpacing = 1.18f; pd.horizontalOverflow = HorizontalWrapMode.Wrap; pd.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.PlaceTL(pd.rectTransform, 1380, 140, 470, 840);
                // 語言切換時更新內文
                lastControls = () =>
                {
                    kbT.text = Loc.T("ctl.kb"); kb.text = Loc.T("ctl.kb.body");
                    pdT.text = Loc.T("ctl.pad"); pd.text = Loc.T("ctl.pad.body");
                };
            }, 890f);
            s.Refreshers.Add(() => lastControls?.Invoke());
            Show(s, false);
        }
        Action lastControls;

        static string WnPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "whatsnew.txt");
        static bool WnUnseen() { try { return System.IO.File.Exists(WnPath) && Profile.GetString("wnSeen", "") != UpdateCheck.Current; } catch { return false; } }
        void ShowWhatsNew()
        { PreviewRobot.Want = false;
            try { Profile.SetString("wnSeen", UpdateCheck.Current); } catch { }
            string Body() { try { return System.IO.File.Exists(WnPath) ? System.IO.File.ReadAllText(WnPath, System.Text.Encoding.UTF8) : Z("(沒有更新紀錄)", "(no release notes)"); } catch { return ""; } }
            var s = Build(() => Z("更新內容", "What's new"), null, new[]
            {
                new Item(() => Loc.T("menu.back"), ShowMain),
            }, ShowMain, content =>
            {
                var tx = UiKit.Label("Wn", content, Body(), 17, UiTheme.Text, TextAnchor.UpperLeft);
                tx.lineSpacing = 1.1f; tx.horizontalOverflow = HorizontalWrapMode.Wrap; tx.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.PlaceTL(tx.rectTransform, 90, 320, 640, 590);
            }, 890f);
            Show(s, false);
        }

        // ---------------------------------------------------------------- 玩家檔案(MOBA 風:頭像框/等級/段位/戰績/成就)
        void ShowLogin()
        { PreviewRobot.Want = false;
            var s = Build(() => Z("歡迎,選手", "Welcome, player"), () => Z("選一種方式進入遊戲", "Choose how to play"), new[]
            {
                new Item(() => GoogleBusy ? Z("取消 Google 登入", "Cancel Google sign-in") : Z("以 Google 帳號登入", "Sign in with Google"), () => { if (GoogleBusy) GoogleAuth.Cancel(); else StartCoroutine(GoogleFlow(AfterLogin(ShowMain))); }) { Sub = GoogleSub },
                new Item(() => Z("以訪客開始", "Play as guest"), () => { GoogleAuth.Cancel(); Account.SetGuest(); if (Prefs.GetInt("guestTut", 0) == 0 && !SuperSample.IsTestRun()) { Prefs.SetInt("guestTut", 1); PlayerPrefs.Save(); ShowTutorial(() => { GameSession.Rookie = true; GameSession.PracticeMode = true; GameSession.VsAi = true; Hide(); GameSession.Begin(Rig); }); } else ShowMain(); }) { Sub = () => Z("訪客:不會儲存遊戲紀錄(經驗/段位/戰績/最佳分數),關閉遊戲就消失", "Guest: progress is NOT saved (XP / rank / record / best scores) and is gone when you quit") },
            }, null, DrawLoginPanel);
            Show(s, false);
        }

        // 登入畫面右側:只放「中性的功能介紹」,絕不讀取或顯示任何已儲存/記住的帳號資料(登入前不該知道你是誰)
        void DrawLoginPanel(RectTransform content)
        {
            var card = GlassPanel.Make(content, "LoginCard", 800, 40, 1080, 1000, 56f, GlassStyle.Card()); card.Opacity = 1f;
            var h = UiKit.Label("LgH", content, Z("FRC 9427 模擬器", "FRC 9427 Simulator"), 52, UiTheme.Text, TextAnchor.UpperLeft); h.fontStyle = FontStyle.Bold; UiKit.PlaceTL(h.rectTransform, 862, 90, 960, 70);
            var s = UiKit.Label("LgS", content, Z("像打遊戲一樣練習 FRC 機器人", "Practice FRC like a game"), 28, UiTheme.Accent, TextAnchor.UpperLeft); UiKit.PlaceTL(s.rectTransform, 864, 160, 960, 40);
            // 範例玩家卡(純示意,內容寫死,不是你的資料)
            var sample = UiKit.Glass("LgSample", content, new Color(1, 1, 1, 0.07f), 28f); UiKit.PlaceTL(sample.rectTransform, 862, 240, 960, 190);
            var ring = UiKit.Glass("LgRing", content, new Color(0.6f, 0.65f, 0.75f, 0.9f), 70f); UiKit.PlaceTL(ring.rectTransform, 900, 272, 126, 126);
            var av = UiKit.Glass("LgAv", content, new Color(0.25f, 0.65f, 1f), 58f); UiKit.PlaceTL(av.rectTransform, 909, 281, 108, 108);
            var q = UiKit.Label("LgQ", content, "?", 64, Color.white, TextAnchor.MiddleCenter); q.fontStyle = FontStyle.Bold; UiKit.PlaceTL(q.rectTransform, 909, 281, 108, 108);
            var pn = UiKit.Label("LgN", content, Z("選手(範例)", "Player (sample)"), 40, UiTheme.Text, TextAnchor.MiddleLeft); pn.fontStyle = FontStyle.Bold; UiKit.PlaceTL(pn.rectTransform, 1056, 278, 700, 54);
            var pl = UiKit.Label("LgL", content, Z("Lv.1  ·  鐵牌 IV  ·  0 勝 0 敗", "Lv.1  ·  Iron IV  ·  0 W 0 L"), 24, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(pl.rectTransform, 1058, 336, 700, 34);
            var pb = UiKit.Label("LgB", content, Z("登入後這裡會變成你自己的玩家卡", "This becomes your own player card after you sign in"), 20, UiTheme.Disabled, TextAnchor.MiddleLeft); UiKit.PlaceTL(pb.rectTransform, 1058, 374, 740, 30);
            string[] bullets = Loc.Lang == "zh"
                ? new[] { "▸ 內建 2026 REBUILT 場地、球物理與 AI 對手", "▸ 載入開源隊伍程式與真實機器人 3D 模型", "▸ 等級、段位、戰績、個性簽名、好友(UID)", "▸ 用 Google 登入:紀錄存在你的帳號底下,同一台電腦可以有多個帳號", "▸ 訪客:直接開玩,但不會儲存任何紀錄" }
                : new[] { "▸ Built-in 2026 REBUILT field, ball physics and AI opponents", "▸ Run open-source team code and real robot 3D models", "▸ Level, rank, record, signature, friends (UID)", "▸ Google sign-in keeps progress under your account; several accounts can share one PC", "▸ Guest: jump in, but nothing is saved" };
            for (int i = 0; i < bullets.Length; i++) { var b = UiKit.Label("LgBul", content, bullets[i], 26, UiTheme.Text, TextAnchor.MiddleLeft); b.horizontalOverflow = HorizontalWrapMode.Wrap; UiKit.PlaceTL(b.rectTransform, 866, 470 + i * 78, 950, 70); }
            var note = UiKit.Label("LgNote", content, Z("用 Google 登入後,下次開遊戲會自動回到這個帳號;想換帳號,到「玩家檔案」按「登出」。", "After you sign in with Google, the game reopens on that account next time; to switch, press Sign out in Profile."), 22, new Color(0.79f, 0.83f, 0.90f), TextAnchor.UpperLeft); note.horizontalOverflow = HorizontalWrapMode.Wrap; UiKit.PlaceTL(note.rectTransform, 866, 900, 950, 70);
        }

        // ---- Google 登入(實作在 GoogleAuth.cs;沒設定 Client ID 時是明確標示 DEMO 的示範登入)
        static bool GoogleBusy { get { return GoogleAuth.Status == GoogleAuth.State.Waiting || GoogleAuth.Status == GoogleAuth.State.Exchanging; } }
        static string GoogleSub()
        {
            switch (GoogleAuth.Status)
            {
                case GoogleAuth.State.Waiting: return GoogleAuth.Message == "DEMO" ? Z("示範登入中…(沒有 Client ID,不是真的 Google)", "Demo sign-in… (no client ID; not real Google)") : Z("已開啟瀏覽器,請在瀏覽器完成登入…", "Browser opened - finish signing in there…");
                case GoogleAuth.State.Exchanging: return Z("取得帳號資料中…", "Getting your account…");
                case GoogleAuth.State.Failed: return Z("失敗:", "Failed: ") + GoogleAuth.Message;
                default: return GoogleAuth.Configured ? Z("會開啟瀏覽器讓你選 Google 帳號", "Opens your browser to pick a Google account") : Z("DEMO:尚未設定 Client ID,先跑示範登入看介面", "DEMO: no client ID yet - runs a demo sign-in");
            }
        }
        // 登入完成後:沒玩過的新帳號 → 自動跳新手教學(看完回 next);玩過的帳號 → 載入它的存檔,直接 next
        Action<bool> AfterLogin(Action next) { return isNew => { if (isNew) ShowTutorial(() => { GameSession.Rookie = true; GameSession.PracticeMode = true; GameSession.VsAi = true; Hide(); GameSession.Begin(Rig); }); else next(); }; }   // 新帳號:看完教學直接進新手練習場
        IEnumerator GoogleFlow(Action<bool> done)
        {
            StartCoroutine(GoogleAuth.SignIn());
            while (GoogleBusy) { RefreshScreen(); yield return null; }
            RefreshScreen();
            if (GoogleAuth.Status == GoogleAuth.State.Done)
            {
                bool isNew;
                Account.SignInGoogle(GoogleAuth.Demo ? GoogleAuth.Name + " (DEMO)" : GoogleAuth.Name, GoogleAuth.Demo ? "demo@local (DEMO - not a real Google account)" : GoogleAuth.Email, out isNew);
                GoogleAuth.Status = GoogleAuth.State.Idle;
                if (done != null) done(isNew);
            }
        }
        void ShowProfile(Action back)
        { PreviewRobot.Want = false;
            var s = Build(() => Z("玩家檔案", "Profile"), () => Account.Name, new[]
            {
                new Item(() => Loc.T("menu.back"), back),
                new Item(() => Z("上傳頭像圖片", "Upload avatar image"), () =>
                {
                    string f = NativeDialogs.OpenFile(Z("選擇頭像圖片", "Choose avatar image"), Z("圖片 (PNG, JPG, WebP, GIF, BMP)", "Images (PNG, JPG, WebP, GIF, BMP)") + "\0*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp\0" + Z("所有檔案 (*.*)", "All files (*.*)") + "\0*.*\0");
                    if (string.IsNullOrEmpty(f))
                    {
                        Debug.Log("[Avatar] picker returned no path: " + NativeDialogs.LastResult);   // cancelled = 使用者按取消;error = 視窗本身出問題
                        if (NativeDialogs.LastResult.StartsWith("error")) Toast(Z("檔案選擇視窗出錯:", "File picker error: ") + NativeDialogs.LastResult, false);
                        return;
                    }
                    bool ok = Account.ImportAvatar(f);
                    ShowProfile(back);   // 重畫玩家檔案(頭像立刻換);主選單右上角的卡片回主選單時重建
                    Toast(Account.LastMsg, ok);
                }) { Sub = () => Account.HasCustomAvatar ? Z("已使用自訂頭像", "Custom avatar in use") : Z("頭像只存在你的電腦裡,不會傳到網路", "Your picture stays on this PC and is never uploaded") },
                new Item(() => Account.HasCustomAvatar ? Z("改回預設頭像(顏色)", "Use default avatar (color)") : Z("換頭像顏色", "Change avatar color"), () => { if (Account.HasCustomAvatar) Account.ClearAvatar(); else Account.NextAvatar(); ShowProfile(back); }),
                new Item(() => GoogleBusy ? Z("取消 Google 登入", "Cancel Google sign-in") : Account.IsGuest ? Z("改用 Google 登入(才會存檔)", "Sign in with Google (to save progress)") : Z("切換 Google 帳號", "Switch Google account"), () => { if (GoogleBusy) GoogleAuth.Cancel(); else StartCoroutine(GoogleFlow(AfterLogin(() => ShowProfile(back)))); }) { Sub = () => GoogleBusy || GoogleAuth.Status == GoogleAuth.State.Failed || !Account.IsGuest ? GoogleSub() : Z("訪客的暫時資料不會帶過去", "Temporary guest data is not carried over") },
                new Item(() => Z("個性簽名", "Signature"), () => { if (SigPanel.Current != null) SigPanel.Current.Begin(); }) { Sub = () => Z("手把:A 開始 → LB/RB 換預設句、A 儲存、B 取消", "Gamepad: A to start, LB/RB presets, A saves, B cancels") },
                new Item(() => Z("複製我的 UID", "Copy my UID") + ":  " + Account.UidText, () => Account.CopyUid()) { EnabledFn = () => !Account.IsGuest && Account.Uid != "" },
                new Item(() => Z("好友", "Friends"), () => ShowFriends(() => ShowProfile(back))),
                new Item(() => Z("新手教學", "Tutorial"), () => ShowTutorial(() => ShowProfile(back))),
                new Item(() => Account.IsGuest ? Z("回到登入畫面", "Back to sign-in") : Z("登出", "Sign out"), () => { GoogleAuth.Cancel(); Account.SignOut(); ShowLogin(); }),
            }, back, content => DrawProfileCard(content, true));
            Show(s, false);
        }

        static Image Bar9(Transform p, float x, float y, float w, float h, float frac, Color c)
        {
            var tr = UiKit.Glass("BarT", p, new Color(1, 1, 1, 0.14f), h / 2f); UiKit.PlaceTL(tr.rectTransform, x, y, w, h);
            var f = UiKit.Glass("BarF", p, c, h / 2f); UiKit.PlaceTL(f.rectTransform, x, y, Mathf.Max(h, w * Mathf.Clamp01(frac)), h);
            return f;
        }

        void DrawProfileCard(RectTransform content, bool full)
        {
            bool zh = Loc.Lang == "zh";
            var card = GlassPanel.Make(content, "ProfCard", 800, 40, 1080, 1000, 56f, GlassStyle.Card());
            card.Opacity = 1f;
            Color tc = Account.TierCol, ac = Account.AvatarColor;
            // 頭像:段位色外框 + 底色圓 + 首字
            if (Account.IsCreator) CreatorFx.Glow(content, 860, 90, 200);
            var ring = UiKit.Glass("AvRing", content, tc, 100f); UiKit.PlaceTL(ring.rectTransform, 860, 90, 200, 200);
            if (Account.IsCreator) CreatorFx.RingPulse(ring);
            var av = UiKit.Glass("Av", content, ac, 88f); UiKit.PlaceTL(av.rectTransform, 872, 102, 176, 176);
            var avTex = Account.AvatarTex;
            if (avTex != null)
            {
                av.gameObject.AddComponent<Mask>().showMaskGraphic = true;   // 用圓形底圖當遮罩,自訂照片才會是圓的
                var ri = new GameObject("AvImg", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(av.transform, false); ri.texture = avTex; ri.raycastTarget = false;
                var rr = ri.rectTransform; rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;
            }
            else { var ini = UiKit.Label("Ini", content, Account.Initial, 96, Color.white, TextAnchor.MiddleCenter); ini.fontStyle = FontStyle.Bold; UiKit.PlaceTL(ini.rectTransform, 872, 102, 176, 176); }
            var lvBg = UiKit.Glass("LvBg", content, new Color(0.08f, 0.10f, 0.16f, 0.95f), 18f); UiKit.PlaceTL(lvBg.rectTransform, 905, 268, 110, 40);
            int lvl, into, need; Account.LevelInfo(out lvl, out into, out need);
            var lvT = UiKit.Label("Lv", content, "Lv." + lvl, 26, tc, TextAnchor.MiddleCenter); lvT.fontStyle = FontStyle.Bold; UiKit.PlaceTL(lvT.rectTransform, 905, 268, 110, 40);
            // 名稱 + 帳號類型 + 經驗條
            var nm = UiKit.Label("Name", content, Account.Name, 60, UiTheme.Text, TextAnchor.MiddleLeft); nm.fontStyle = FontStyle.Bold; UiKit.PlaceTL(nm.rectTransform, 1100, 96, 740, 76);
            bool gst = Account.IsGuest;
            var sub = UiKit.Label("Acct", content, gst ? Account.GuestNote(zh) : Account.IsGoogle ? "Google  ·  " + Account.Email : Z("本機玩家檔", "Local profile"), 24, gst ? new Color(1f, 0.78f, 0.45f) : UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(sub.rectTransform, 1102, 176, 740, 34);
            var uidL = UiKit.Label("UidP", content, "UID  " + Account.UidText, 22, UiTheme.TextDim, TextAnchor.MiddleRight); UiKit.PlaceTL(uidL.rectTransform, 1540, 180, 300, 30);
            if (Account.IsCreator) CreatorFx.Chip(content, 1520, 248, 320, 36);
            Bar9(content, 1102, 230, 720, 18, into / (float)need, UiTheme.Accent);
            var xpT = UiKit.Label("Xp", content, "EXP  " + into + " / " + need, 20, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(xpT.rectTransform, 1102, 254, 400, 28);

            // 個性簽名(頭像+Lv 與排名卡之間;寬度 = 排名卡寬)。登入畫面的預覽卡不顯示
            if (full) SigPanel.Make(content, 860, 322, 470, true, Account.IsCreator, null);

            // 段位面板(整塊下移 68 讓出簽名面板的位置)
            const float Y0 = 408f;
            var rk = UiKit.Glass("RankBg", content, new Color(1, 1, 1, 0.07f), 28f); UiKit.PlaceTL(rk.rectTransform, 860, Y0, 470, 222);
            var dia = UiKit.Img("Diamond", content, tc); UiKit.PlaceTL(dia.rectTransform, 905, Y0 + 70, 84, 84); dia.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var dia2 = UiKit.Img("Diamond2", content, new Color(tc.r * 0.45f, tc.g * 0.45f, tc.b * 0.45f, 1f)); UiKit.PlaceTL(dia2.rectTransform, 918, Y0 + 83, 58, 58); dia2.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var rkT = UiKit.Label("Tier", content, Account.TierName(zh) + " " + Account.Division, 44, tc, TextAnchor.MiddleLeft); rkT.fontStyle = FontStyle.Bold; UiKit.PlaceTL(rkT.rectTransform, 1070, Y0 + 52, 250, 60);
            var rpT = UiKit.Label("Rp", content, Z("段位積分 ", "Rating ") + Account.Rp + " RP", 22, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(rpT.rectTransform, 1070, Y0 + 116, 250, 30);
            Bar9(content, 890, Y0 + 186, 410, 14, (Account.Rp % 25) / 25f, tc);

            // 戰績面板
            var st = UiKit.Glass("StatBg", content, new Color(1, 1, 1, 0.07f), 28f); UiKit.PlaceTL(st.rectTransform, 1350, Y0, 490, 222);
            int m = Account.Matches; int wr = m > 0 ? Account.Wins * 100 / m : 0;
            var st1 = UiKit.Label("Rec", content, Account.Wins + Z(" 勝  ", "W  ") + Account.Losses + Z(" 敗  ", "L  ") + Account.Draws + Z(" 平", "D"), 32, UiTheme.Text, TextAnchor.MiddleLeft); st1.fontStyle = FontStyle.Bold; UiKit.PlaceTL(st1.rectTransform, 1380, Y0 + 20, 440, 50);
            var st2 = UiKit.Label("Wr", content, Z("勝率 ", "Win rate ") + wr + "%   ·   " + Z("共 ", "") + m + Z(" 場", " matches"), 24, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(st2.rectTransform, 1382, Y0 + 75, 440, 34);
            Bar9(content, 1382, Y0 + 122, 430, 14, wr / 100f, new Color(0.4f, 0.9f, 0.55f));
            var st3 = UiKit.Label("Best", content, Z("最高得分 ", "Best score ") + Account.BestScore + "     " + Z("目前連勝 ", "Streak ") + Account.Streak, 22, UiTheme.TextDim, TextAnchor.MiddleLeft); UiKit.PlaceTL(st3.rectTransform, 1382, Y0 + 158, 440, 32);

            // 最近 5 場
            var h1 = UiKit.Label("RecH", content, Z("最近戰績", "Recent matches"), 26, UiTheme.Accent, TextAnchor.MiddleLeft); h1.fontStyle = FontStyle.Bold; UiKit.PlaceTL(h1.rectTransform, 862, 646, 400, 36);
            var rec = Account.Recent();
            for (int i = 0; i < 5; i++)
            {
                float ry = 686 + i * 40;
                var row = UiKit.Glass("Row", content, new Color(1, 1, 1, 0.06f), 16f); UiKit.PlaceTL(row.rectTransform, 860, ry, 980, 34);
                if (i >= rec.Length) { var e = UiKit.Label("Empty", content, i == 0 ? Z("還沒有比賽,去打一場吧!", "No matches yet — go play!") : "", 22, UiTheme.Disabled, TextAnchor.MiddleLeft); UiKit.PlaceTL(e.rectTransform, 884, ry, 800, 34); continue; }
                var p = rec[i].Split('|');
                bool w = p[0] == "W", l = p[0] == "L";
                Color rc = w ? new Color(0.4f, 0.9f, 0.5f) : l ? new Color(1f, 0.5f, 0.45f) : new Color(1f, 0.9f, 0.4f);
                var bar = UiKit.Img("Res", content, rc); UiKit.PlaceTL(bar.rectTransform, 862, ry + 4, 6, 26);
                var rt1 = UiKit.Label("R", content, w ? Z("勝利", "VICTORY") : l ? Z("落敗", "DEFEAT") : Z("平手", "DRAW"), 24, rc, TextAnchor.MiddleLeft); rt1.fontStyle = FontStyle.Bold; UiKit.PlaceTL(rt1.rectTransform, 884, ry, 200, 34);
                var rt2 = UiKit.Label("S", content, Z("藍 ", "Blue ") + p[1] + " : " + p[2] + Z(" 紅", " Red"), 24, UiTheme.Text, TextAnchor.MiddleLeft); UiKit.PlaceTL(rt2.rectTransform, 1090, ry, 400, 34);
                string[] dn = zh ? new[] { "簡單", "普通", "困難", "地獄" } : new[] { "Easy", "Normal", "Hard", "Insane" };
                int di; string mode = p.Length > 3 && int.TryParse(p[3], out di) ? Z("對 AI·", "vs AI · ") + dn[Mathf.Clamp(di, 0, 3)] : Z("雙人對戰", "2-player");
                var rt3 = UiKit.Label("M", content, mode, 20, UiTheme.TextDim, TextAnchor.MiddleRight); UiKit.PlaceTL(rt3.rectTransform, 1500, ry, 320, 34);
            }

            // 成就
            var h2 = UiKit.Label("AchH", content, Z("成就", "Achievements"), 26, UiTheme.Accent, TextAnchor.MiddleLeft); h2.fontStyle = FontStyle.Bold; UiKit.PlaceTL(h2.rectTransform, 862, 892, 400, 36);
            var an = Account.AchievementNames(zh); var ad = Account.Achievements();
            for (int i = 0; i < an.Length; i++)
            {
                float ax = 860 + (i % 3) * 330, ay = 934 + (i / 3) * 40;
                var chip = UiKit.Glass("Chip", content, ad[i] ? new Color(1f, 0.82f, 0.3f, 0.30f) : new Color(1, 1, 1, 0.05f), 14f); UiKit.PlaceTL(chip.rectTransform, ax, ay, 320, 34);
                var ct = UiKit.Label("C", content, (ad[i] ? "★ " : "☆ ") + an[i], 16, ad[i] ? new Color(1f, 0.9f, 0.5f) : UiTheme.Disabled, TextAnchor.MiddleLeft); ct.resizeTextForBestFit = true; ct.resizeTextMinSize = 11; ct.resizeTextMaxSize = 16; UiKit.PlaceTL(ct.rectTransform, ax + 10, ay, 304, 34);
            }
        }

        void ShowAbout()
        { PreviewRobot.Want = false;
            string Body() => Z(
                "FRC 9427 模擬器  v" + UpdateCheck.Current + "   ·   MADE IN UNITY\n\n本軟體為非官方的學生專案,與 FIRST 無關。比賽規則依 2026 REBUILT 官方文件。\n\n機器人 3D 模型\n  • AdvantageScope 公開資產(Mechanical Advantage 6328,BSD-3-Clause)\n  • 2026 真機 CAD:971 / 604 / 967 / 3928 / 48 / 1678 / 2910(各隊在 Onshape 公開釋出,僅供個人學習使用)\n\n開源機器人程式(各隊 GitHub 公開,下載後在本機執行)\n  • FRC 6901、1710、1405、364、4915、REV ION StarterBot\n\n向以上所有團隊致謝。",
                "FRC 9427 Simulator  v" + UpdateCheck.Current + "   ·   MADE IN UNITY\n\nUnofficial student project, not affiliated with FIRST. Rules follow the official 2026 REBUILT documents.\n\nRobot 3D models\n  • AdvantageScope public assets (Mechanical Advantage 6328, BSD-3-Clause)\n  • 2026 real CAD: 971 / 604 / 967 / 3928 / 48 / 1678 / 2910 (publicly released on Onshape; personal study use only)\n\nOpen-source robot code (public on each team's GitHub, run locally after download)\n  • FRC 6901, 1710, 1405, 364, 4915, REV ION StarterBot\n\nThank you to all of these teams.");
            var s = Build(() => Z("關於 / 致謝", "About / Credits"), null, new[]
            {
                new Item(() => Loc.T("menu.back"), ShowMain),
            }, ShowMain, content =>
            {
                var tx = UiKit.Label("About", content, Body(), 19, UiTheme.Text, TextAnchor.UpperLeft);
                tx.lineSpacing = 1.15f; tx.horizontalOverflow = HorizontalWrapMode.Wrap; tx.verticalOverflow = VerticalWrapMode.Overflow;
                UiKit.PlaceTL(tx.rectTransform, 90, 320, 640, 600);
                lastControls = () => { tx.text = Body(); };
            }, 890f);
            s.Refreshers.Add(() => lastControls?.Invoke());
            Show(s, false);
        }

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
            if (Profile.GetInt("tutSeen", 0) == 0 && !SuperSample.IsTestRun() && !Profile.Guest) { ShowTutorial(null); return; }   // 第一次:先看一頁教學(訪客不自動跳,想看從玩家檔案進)
            Hide();
            GameSession.Begin(Rig);
        }

        // next == null:看完直接開始比賽(第一次按「開始」時);有 next:看完回到 next(登入後的新手教學、玩家檔案裡的「新手教學」)
        void ShowTutorial(Action next = null)
        { PreviewRobot.Want = false;
            string Body() => Z(
                "目標:把球(FUEL)射進自己的 HUB 得分。HUB 會輪流啟動:發亮=可得分,暗灰=現在不算分。\n\n移動\n  • 鍵盤 W A S D 移動、Q / E 旋轉\n  • 手把:左搖桿移動、右搖桿轉向\n\n收球與射擊\n  • I 放下/收起吸球手臂,開到球上自動吸\n  • 按住 空白鍵(或滑鼠左鍵)射擊,會自動瞄準 HUB\n\n其他\n  • C 切換視角   M 旁白   P 自動路線   G 小地圖   Esc 暫停   F1 全部按鍵",
                "Goal: shoot FUEL into your HUB to score. HUBs take turns being active: glowing = scores, dim grey = no points right now.\n\nMove\n  • Keyboard: W A S D to move, Q / E to rotate\n  • Gamepad: left stick moves, right stick turns\n\nIntake & shoot\n  • I lowers/raises the intake; drive over fuel to collect\n  • Hold Space (or left click) to shoot — it auto-aims at the HUB\n\nMore\n  • C camera   M commentary   P auto paths   G minimap   Esc pause   F1 all keys");
            var s = Build(() => next != null ? Z("怎麼玩", "How to play") : Z("怎麼玩(只會出現一次)", "How to play (shown once)"), null, new[]
            {
                new Item(() => next != null ? Z("知道了!", "Got it!") : Z("知道了,開始!", "Got it — start!"), () => { Profile.SetInt("tutSeen", 1); if (next != null) next(); else StartGame(); }),
            }, next != null ? next : (Action)ShowRobotSetup, content =>
            {
                var tx = UiKit.Label("Tut", content, Body(), 18, UiTheme.Text, TextAnchor.UpperLeft);
                tx.lineSpacing = 1.12f; tx.horizontalOverflow = HorizontalWrapMode.Wrap; tx.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.PlaceTL(tx.rectTransform, 90, 300, 650, 600);
            }, 890f);
            Show(s, false);
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
            if (toastBg != null && toastBg.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBg.gameObject.SetActive(false);
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
            if (SigPanel.Editing) return;   // 簽名編輯中:鍵盤/手把全部交給 SigPanel(打字、Enter、Esc、LB/RB、A、B)

            if (textMode)
            {
                foreach (char c in Input.inputString)
                {
                    if (c == '\b') { if (textBuf.Length > 0) textBuf = textBuf.Substring(0, textBuf.Length - 1); }
                    else if (c >= '0' && c <= '9' && textBuf.Length < 8) textBuf += c;
                }
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
                { foreach (char c in GUIUtility.systemCopyBuffer ?? "") if (c >= '0' && c <= '9' && textBuf.Length < 8) textBuf += c; }
                if (Input.GetKeyDown(KeyCode.Escape)) { textMode = false; textBuf = ""; RefreshScreen(); return; }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { textMode = false; var d = textDone; string v = textBuf; textBuf = ""; if (d != null) d(v); return; }
                RefreshScreen();
                return;
            }

            // 非按鈕的可點區域(玩家卡)
            if (Input.GetMouseButtonDown(0)) foreach (var kv in cur.Clicks) if (RectTransformUtility.RectangleContainsScreenPoint(kv.Key, Input.mousePosition, null)) { kv.Value(); return; }

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
























