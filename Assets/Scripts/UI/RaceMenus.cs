using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FrcSim
{
    // 賽車專屬選單(瑪利歐賽車式):主選單 → 排氣量/盃賽/賽道/規則 → 角色/車身/輪胎/滑翔翼(含 6 項屬性條)→ 開始。
    // 另有大獎賽積分榜、盃賽頒獎、賽車暫停選單。邏輯寫在 MenuSystem 的 partial 裡(才能用 Build/Show/Item);
    // 對外 API 全部集中在 static class RaceMenus。
    public static class RaceMenus
    {
        /// 一台車在積分榜/頒獎的一列。
        public struct Row
        {
            public int KartId;
            public string Name;
            public int Place;        // 本場名次(1..8)
            public int RacePoints;   // 本場得分
            public int TotalPoints;  // 盃賽累計(含本場)
            public bool IsPlayer;
        }

        // ---- 給整合者(盃賽流程)的狀態與掛鉤 ----
        /// 非 null 時,選單「開始」不自己 GameSession.Begin,而是 Hide 後呼叫這個(傳入 RaceSession.Pending 的複本);整合者自己管盃賽流程。
        public static Action<RaceConfig> OnStart;
        /// 對戰賽規則「賽道順序」:false = 單場、true = 依序跑完所有賽道(整合者自己用)。
        public static bool VsSequence;
        /// 目前選的盃賽編號(大獎賽)。
        public static int SelectedCup = 0;
        /// 目前模式:0 大獎賽 / 1 計時賽 / 2 對戰賽 / 3 自由探索(整合者可讀)。
        public static int Mode;

        // ---- HUD 讀的盃賽進度(整合者每場開賽前呼叫 SetCupProgress)----
        public static int CupRaceIndex, CupRaceCount, CupTotalPoints;
        public static void SetCupProgress(int raceIndex1Based, int raceCount, int totalPoints) { CupRaceIndex = raceIndex1Based; CupRaceCount = raceCount; CupTotalPoints = totalPoints; }
        public static void ClearCupProgress() { CupRaceIndex = CupRaceCount = CupTotalPoints = 0; }

        // ---- 入口 ----
        public static void ShowRaceMain() { if (MenuSystem.I != null) MenuSystem.I.RmMain(); }
        /// 大獎賽每場後的積分榜。onNext = 「下一場」(最後一場傳 null 會顯示「看頒獎」文字由 nextLabel 決定);onQuit = 回賽車選單。
        public static void ShowStandings(IList<Row> rows, int raceIndex1Based, int raceCount, Action onNext, Action onQuit, string nextLabelZh = null, string nextLabelEn = null)
        { if (MenuSystem.I != null) MenuSystem.I.RmStandings(rows, raceIndex1Based, raceCount, onNext, onQuit, nextLabelZh, nextLabelEn); }
        /// 盃賽頒獎。rows 以總分排序皆可(內部會排序);onDone = 完成後(通常回賽車主選單)。
        public static void ShowPodium(IList<Row> rows, int cup, Action onDone) { if (MenuSystem.I != null) MenuSystem.I.RmPodium(rows, cup, onDone); }
        /// 賽車專屬暫停選單(MenuSystem.ShowPause 在 GameSession.Racing 時會自動呼叫,整合者通常不用管)。
        public static void ShowRacePause() { if (MenuSystem.I != null) MenuSystem.I.RmPause(); }
        /// 啟動參數 -menu race* 的截圖用入口(race / racepick / racevs / racestand / racepodium)。
        public static void Debug(string which) { if (MenuSystem.I != null) MenuSystem.I.RmDebug(which); }
    }

    // ------------------------------------------------------------------------------------------------
    // 資料:名稱與屬性。零件與屬性來自 KartLoadout(K6 的 KartParts.cs;類別叫 KartLoadout 因為 KartModels.cs 已有外觀用的 KartParts)。
    // 盃賽資料(Cups,W6)還沒進來,先用下面的替身。
    // ------------------------------------------------------------------------------------------------
    static class RmData
    {
        public static readonly int[] Counts = { KartLoadout.DriverCount, KartLoadout.BodyCount, KartLoadout.TireCount, KartLoadout.GliderCount };   // 角色/車身/輪胎/滑翔翼
        public static readonly string[] KindZh = { "角色", "車身", "輪胎", "滑翔翼" };
        public static readonly string[] KindEn = { "Driver", "Kart body", "Tires", "Glider" };

        public static string Name(int kind, int id) { return KartLoadout.Name((PartKind)kind, id, Loc.Lang); }
        public static string StatName(int i) { return KartLoadout.StatName(i, Loc.Lang); }
        // 副標題:另一種語言的名字;角色加上動物與體重級別
        public static string Other(int kind, int id)
        {
            string o = KartLoadout.Name((PartKind)kind, id, Loc.Lang == "zh" ? "en" : "zh");
            if (kind != 0) return o;
            var wc = KartLoadout.DriverClass(id);
            string cls = Loc.Lang == "zh" ? (wc == WeightClass.Light ? "輕量級" : wc == WeightClass.Medium ? "中量級" : "重量級") : (wc == WeightClass.Light ? "Light" : wc == WeightClass.Medium ? "Medium" : "Heavy");
            return KartLoadout.DriverAnimal(id, Loc.Lang) + "  ·  " + cls + "  ·  " + o;
        }

        // 四件套 + 排氣量 -> 六條屬性 0..1(KartLoadout.Combine 的 Bars)
        public static float[] Stats(int d, int b, int t, int g) { return KartLoadout.Combine(d, b, t, g, RaceSession.Pending.CC).Bars; }

        // 盃賽:改讀 Cups(W 模組的正式定義,M7 起共 6 個盃、8 條賽道);賽道 id 超出 TrackCount 時取餘數。
        public static int CupCount => Cups.Count;
        public static string CupName(int i) { return Cups.Name(Mathf.Clamp(i, 0, Cups.Count - 1), Loc.Lang); }
        public static int[] CupRaces(int i)
        {
            int tc = Mathf.Max(1, RaceSession.TrackCount);
            var races = Cups.Races(Mathf.Clamp(i, 0, Cups.Count - 1));
            var r = new int[races.Count];
            for (int j = 0; j < r.Length; j++) r[j] = races[j].TrackId % tc;
            return r;
        }
    }

    public partial class MenuSystem
    {
        static readonly string KDriver = "race_driver", KBody = "race_body", KTire = "race_tire", KGlider = "race_glider", KCC = "race_cc", KRule = "race_itemrule", KMirror = "race_mirror", KSeq = "race_vsseq";
        static bool rmLoaded;

        static int RmGet(RaceConfig c, int kind) { return kind == 0 ? c.DriverId : kind == 1 ? c.BodyId : kind == 2 ? c.TireId : c.GliderId; }
        static void RmSet(RaceConfig c, int kind, int v) { if (kind == 0) c.DriverId = v; else if (kind == 1) c.BodyId = v; else if (kind == 2) c.TireId = v; else c.GliderId = v; }

        static void RmLoad()
        {
            if (rmLoaded) return; rmLoaded = true;
            var c = RaceSession.Pending;
            c.DriverId = Mathf.Clamp(Prefs.GetInt(KDriver, c.DriverId), 0, RmData.Counts[0] - 1);
            c.BodyId = Mathf.Clamp(Prefs.GetInt(KBody, c.BodyId), 0, RmData.Counts[1] - 1);
            c.TireId = Mathf.Clamp(Prefs.GetInt(KTire, c.TireId), 0, RmData.Counts[2] - 1);
            c.GliderId = Mathf.Clamp(Prefs.GetInt(KGlider, c.GliderId), 0, RmData.Counts[3] - 1);
            c.CC = Prefs.GetInt(KCC, c.CC); c.Infinite = c.CC == 999; if (c.Infinite) c.CC = 200; if (c.CC != 50 && c.CC != 100 && c.CC != 150 && c.CC != 200) c.CC = 150;
            c.ItemRule = Mathf.Clamp(Prefs.GetInt(KRule, c.ItemRule), 0, 5);
            c.Mirror = Prefs.GetInt(KMirror, 0) == 1;
            RaceMenus.VsSequence = Prefs.GetInt(KSeq, 0) == 1;
            c.TrackId = Mathf.Clamp(Prefs.GetInt(RaceKeys.Track, c.TrackId), 0, Mathf.Max(0, RaceSession.TrackCount - 1));
            c.Laps = Prefs.GetInt(RaceKeys.Laps, c.Laps); if (c.Laps != 1 && c.Laps != 3 && c.Laps != 5) c.Laps = 3;
            c.AiCount = Prefs.GetInt(RaceKeys.AiCount, c.AiCount); if (c.AiCount != 0 && c.AiCount != 3 && c.AiCount != 7) c.AiCount = 3;
            c.AiLevel = Mathf.Clamp(Prefs.GetInt(RaceKeys.AiLevel, c.AiLevel), 0, 3);
        }

        static void RmSave()
        {
            var c = RaceSession.Pending;
            Prefs.SetInt(KDriver, c.DriverId); Prefs.SetInt(KBody, c.BodyId); Prefs.SetInt(KTire, c.TireId); Prefs.SetInt(KGlider, c.GliderId);
            Prefs.SetInt(KCC, c.Infinite ? 999 : c.CC); Prefs.SetInt(KRule, c.ItemRule); Prefs.SetInt(KMirror, c.Mirror ? 1 : 0); Prefs.SetInt(KSeq, RaceMenus.VsSequence ? 1 : 0);
            Prefs.SetInt(RaceKeys.Track, c.TrackId); Prefs.SetInt(RaceKeys.Laps, c.Laps); Prefs.SetInt(RaceKeys.AiCount, c.AiCount); Prefs.SetInt(RaceKeys.AiLevel, c.AiLevel);
            Prefs.SetInt(RaceKeys.Kart, (int)c.Kart); Prefs.SetInt(RaceKeys.Color, c.ColorIndex);
            if (!Prefs.Isolated) PlayerPrefs.Save();
        }

        // 車身外觀由 KartLoadout.BodyStyle 決定(0..3 一對一、4..7 借用相近外觀);車色跟著角色走
        static void RmApplyLook()
        {
            var c = RaceSession.Pending;
            c.Kart = KartLoadout.BodyStyle(c.BodyId);
            c.ColorIndex = c.DriverId % RacePalette.Kart.Length;
        }

        static string RmBest(int track)
        {
            int ms = Prefs.GetInt(RaceKeys.BestLap(track), 0);
            return ms > 0 ? Z("最佳圈 ", "Best lap ") + RaceSession.FormatTime(ms / 1000f) : Z("還沒跑過", "Not raced yet");
        }

        // ---------------------------------------------------------------- 賽車主選單
        internal void RmMain()
        {
            PreviewRobot.Want = false; RmLoad();
            var s = Build(() => Z("賽車", "Kart Racing"), () => Z("選一種玩法", "Pick a mode"), new[]
            {
                new Item(() => Z("大獎賽  Grand Prix", "Grand Prix"), () => { RaceMenus.Mode = 0; RmGpCc(); }) { Sub = () => Z("選排氣量與盃賽,連跑 4 場累計積分,最後頒獎", "Pick a class & cup — 4 races, points add up, then the podium") },
                new Item(() => Z("計時賽  Time Trial", "Time Trial"), () => { RaceMenus.Mode = 1; RmTrackThen(true); }) { Sub = () => Z("一個人刷最快圈,挑戰自己的最佳紀錄", "Solo run — chase your best lap") },
                new Item(() => Z("對戰賽  VS Race", "VS Race"), () => { RaceMenus.Mode = 2; RmVsRules(); }) { Sub = () => Z("自訂規則:AI 數量、難度、道具、圈數、鏡像…", "Custom rules: AI, level, items, laps, mirror…") },
                new Item(() => Z("自由探索  Free Roam", "Free Roam"), () => { RaceMenus.Mode = 3; RmTrackThen(false); }) { Sub = () => Z("沒有對手沒有壓力,在賽道上撿金幣亂逛", "No rivals, no pressure — roam and collect coins") },
                new Item(() => Loc.T("menu.back"), ShowModes),
            }, ShowModes);
            Show(s, false);
        }

        // 舊的 ShowRacing 入口導向新選單
        void ShowRacing() { RmMain(); }

        // ---------------------------------------------------------------- 大獎賽:排氣量
        void RmGpCc()
        {
            PreviewRobot.Want = false; var P = RaceSession.Pending;
            Item Cc(int cc, bool mirror, Func<string> zh, string sub, string subEn)
            {
                return new Item(() => ((P.CC == cc && P.Mirror == mirror) ? "● " : "○ ") + zh(), () => { P.CC = mirror ? 150 : cc; P.Mirror = mirror; P.Infinite = false; P.AiLevel = mirror ? 3 : cc == 50 ? 0 : cc == 100 ? 1 : cc == 150 ? 2 : 3; RmSave(); RmGpCup(); })
                { Sub = () => Z(sub, subEn) };
            }
            var s = Build(() => Z("大獎賽:排氣量", "Grand Prix: Engine class"), () => Z("數字越大越快,對手也越強", "Bigger = faster, and tougher rivals"), new[]
            {
                Cc(50, false, () => "50cc", "最悠閒,速度約 55%,適合第一次玩", "Chill — about 55% speed, great for beginners"),
                Cc(100, false, () => "100cc", "速度約 75%,對手普通", "About 75% speed, normal rivals"),
                Cc(150, false, () => "150cc", "標準速度,對手有點強", "Standard speed, sturdy rivals"),
                Cc(200, false, () => "200cc", "速度 130%,超刺激", "130% speed — thrilling"),
                new Item(() => (P.Infinite ? "● " : "○ ") + Z("無限加速  Infinite", "Infinite"), () => { P.CC = 200; P.Mirror = false; P.Infinite = true; P.AiLevel = 3; RmSave(); RmGpCup(); }) { Sub = () => Z("200cc 起跳,速度每秒越來越快(上限 6 倍)", "Starts at 200cc, then keeps getting faster (up to 6x)") },
                Cc(150, true, () => Z("鏡像  Mirror", "Mirror"), "150cc,賽道左右顛倒", "150cc on a left-right flipped track"),
                new Item(() => Loc.T("menu.back"), RmMain),
            }, RmMain);
            Show(s, false);
        }

        // ---------------------------------------------------------------- 大獎賽:盃賽
        void RmGpCup()
        {
            PreviewRobot.Want = false;
            var items = new List<Item>();
            for (int i = 0; i < RmData.CupCount; i++)
            {
                int ci = i;
                items.Add(new Item(() => (RaceMenus.SelectedCup == ci ? "● " : "○ ") + RmData.CupName(ci), () => { RaceMenus.SelectedCup = ci; RmPickKind(0, RmGpCup); })
                {
                    Sub = () =>
                    {
                        var r = RmData.CupRaces(ci); var sb = new System.Text.StringBuilder();
                        for (int j = 0; j < r.Length; j++) { if (j > 0) sb.Append(" › "); sb.Append(RaceSession.TrackName(r[j])); }
                        return sb.ToString();
                    }
                });
            }
            items.Add(new Item(() => Loc.T("menu.back"), RmGpCc));
            var s = Build(() => Z("大獎賽:選盃賽", "Grand Prix: Pick a cup"), () => RaceSession.Pending.Mirror ? Z("鏡像 150cc", "Mirror 150cc") : RaceSession.Pending.CC + "cc", items.ToArray(), RmGpCc);
            Show(s, false);
        }

        // ---------------------------------------------------------------- 賽道選擇(計時賽 / 對戰單場 / 自由探索)
        void RmTrack(Action back, Action next, bool timeTrial)
        {
            PreviewRobot.Want = false; var P = RaceSession.Pending;
            var items = new List<Item>();
            int tc = Mathf.Max(1, RaceSession.TrackCount);
            for (int t = 0; t < tc; t++)
            {
                int ti = t;
                items.Add(new Item(() => (P.TrackId == ti ? "● " : "○ ") + RaceSession.TrackName(ti), () => { P.TrackId = ti; RmSave(); next(); }) { Sub = () => RmBest(ti) });
            }
            items.Add(new Item(() => Loc.T("menu.back"), () => back()));
            var s = Build(() => Z("選賽道", "Pick a track"), () => timeTrial ? Z("計時賽:單人刷圈", "Time Trial: solo laps") : Z("自由探索:撿金幣", "Free Roam: collect coins"), items.ToArray(), back);
            Show(s, false);
        }
        void RmTrackThen(bool timeTrial) { RmTrack(RmMain, () => RmPickKind(0, () => RmTrackThen(timeTrial)), timeTrial); }
        void RmVsTrack() { RmTrack(RmVsRules, () => RmPickKind(0, RmVsTrack), false); }

        // ---------------------------------------------------------------- 對戰賽規則面板
        void RmVsRules()
        {
            PreviewRobot.Want = false; var P = RaceSession.Pending;
            string[] lvZh = { "簡單", "普通", "困難", "超困難" }, lvEn = { "Easy", "Normal", "Hard", "Insane" };
            string[] ruZh = { "正常", "瘋狂(道具更多)", "無道具", "只有香蕉皮", "只有龜殼", "只有加速" }, ruEn = { "Normal", "Frantic", "No items", "Banana only", "Shells only", "Boost only" };
            int[] ccs = { 50, 100, 150, 200 };
            var s = Build(() => Z("對戰賽規則", "VS Rules"), () => Z("點一下切換選項", "Tap to cycle each option"), new[]
            {
                new Item(() => Z("排氣量", "Class") + ":  " + (P.Infinite ? Z("無限加速", "Infinite") : P.CC + "cc"), () => { if (P.Infinite) { P.Infinite = false; P.CC = 50; } else if (P.CC == 200) { P.Infinite = true; } else { int i = Array.IndexOf(ccs, P.CC); P.CC = ccs[(i + 1) % 4]; } }),
                new Item(() => Z("AI 數量", "AI count") + ":  " + P.AiCount, () => { P.AiCount = P.AiCount == 0 ? 3 : P.AiCount == 3 ? 7 : 0; }),
                new Item(() => Z("AI 難度", "AI level") + ":  " + (Loc.Lang == "zh" ? lvZh : lvEn)[Mathf.Clamp(P.AiLevel, 0, 3)], () => { P.AiLevel = (P.AiLevel + 1) % 4; }) { EnabledFn = () => P.AiCount > 0 },
                new Item(() => Z("道具規則", "Items") + ":  " + (Loc.Lang == "zh" ? ruZh : ruEn)[Mathf.Clamp(P.ItemRule, 0, 5)], () => { P.ItemRule = (P.ItemRule + 1) % 6; }),
                new Item(() => Z("賽道順序", "Track order") + ":  " + (RaceMenus.VsSequence ? Z("依序跑完全部", "All tracks in order") : Z("單場(自己選)", "Single race (you pick)")), () => { RaceMenus.VsSequence = !RaceMenus.VsSequence; }),
                new Item(() => Z("圈數", "Laps") + ":  " + P.Laps, () => { P.Laps = P.Laps == 1 ? 3 : P.Laps == 3 ? 5 : 1; }),
                new Item(() => Z("鏡像", "Mirror") + ":  " + Loc.T(P.Mirror ? "on" : "off"), () => { P.Mirror = !P.Mirror; }),
                new Item(() => Z("反向行駛", "Reverse") + ":  " + Loc.T(P.Reverse ? "on" : "off"), () => { P.Reverse = !P.Reverse; }),
                new Item(() => RaceMenus.VsSequence ? Z("下一步:選角色  ▸", "Next: pick driver  ▸") : Z("下一步:選賽道  ▸", "Next: pick track  ▸"), () =>
                {
                    RmSave();
                    if (RaceMenus.VsSequence) RmPickKind(0, RmVsRules);
                    else RmVsTrack();
                }),
                new Item(() => Loc.T("menu.back"), RmMain),
            }, RmMain);
            Show(s, false);
        }

        // ---------------------------------------------------------------- 角色 / 車身 / 輪胎 / 滑翔翼
        const int StatN = 6;
        void RmPickKind(int kind, Action origBack)
        {
            PreviewRobot.Want = false; RmLoad();
            var P = RaceSession.Pending;
            int n = RmData.Counts[kind];
            int before = Mathf.Clamp(RmGet(P, kind), 0, n - 1), sel = before;
            Action back = kind == 0 ? origBack : (Action)(() => RmPickKind(kind - 1, origBack));
            Action prev = () => { sel = (sel - 1 + n) % n; }, next = () => { sel = (sel + 1) % n; };
            bool last = kind == 3;

            float[] Combo(int candidate)
            {
                int[] ids = { P.DriverId, P.BodyId, P.TireId, P.GliderId }; ids[kind] = candidate;
                return RmData.Stats(ids[0], ids[1], ids[2], ids[3]);
            }
            Color Tint(int kd, int id) { var c = Color.HSVToRGB((id * 0.13f + kd * 0.31f) % 1f, 0.5f, 0.97f); c.a = 0.92f; return c; }

            Image cardImg = null; Text bigT = null, nameT = null, otherT = null, idxT = null;
            var fills = new Image[StatN]; var deltas = new Text[StatN]; var chips = new Text[4];

            var s = Build(() => (Loc.Lang == "zh" ? "選" + RmData.KindZh[kind] : "Pick " + RmData.KindEn[kind]) , () => Z("第 ", "Step ") + (kind + 1) + "/4  ·  " + (Loc.Lang == "zh" ? RmData.KindZh[kind] : RmData.KindEn[kind]), new[]
            {
                new Item(() => last ? Z("確定,開始!", "Confirm & start!") : Z("確定  ▸", "Confirm  ▸"), () => { RmSet(P, kind, sel); RmApplyLook(); RmSave(); if (last) RmStart(); else RmPickKind(kind + 1, origBack); }) { Sub = () => RmData.Name(kind, sel) + "  ·  " + RmData.Other(kind, sel) },
                new Item(() => Z("◀  上一個", "◀  Previous"), prev) { Sub = () => (sel + 1) + " / " + n },
                new Item(() => Z("下一個  ▶", "Next  ▶"), next) { Sub = () => (sel + 1) + " / " + n },
                new Item(() => Loc.T("menu.back"), () => back()),
            }, () => back(), content =>
            {
                // 預覽卡
                var card = UiKit.Glass("RmCard", content, Tint(kind, sel), 56f); UiKit.PlaceTL(card.rectTransform, 800, 80, 520, 330); cardImg = card; card.raycastTarget = false;
                var tag = UiKit.Label("RmTag", content, "", 26, new Color(0, 0, 0, 0.55f), TextAnchor.UpperLeft); UiKit.PlaceTL(tag.rectTransform, 832, 100, 300, 36); idxT = tag;
                bigT = UiKit.Label("RmBig", content, "", 150, new Color(1, 1, 1, 0.95f), TextAnchor.MiddleCenter); bigT.fontStyle = FontStyle.Bold; UiKit.PlaceTL(bigT.rectTransform, 800, 120, 520, 200);
                nameT = UiKit.Label("RmName", content, "", 44, Color.white, TextAnchor.MiddleCenter); nameT.fontStyle = FontStyle.Bold; UiKit.PlaceTL(nameT.rectTransform, 800, 318, 520, 56);
                otherT = UiKit.Label("RmName2", content, "", 22, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleCenter); UiKit.PlaceTL(otherT.rectTransform, 800, 368, 520, 30);
                // 目前組合(四件套)
                var pbg = UiKit.Glass("RmCombo", content, new Color(0.05f, 0.09f, 0.16f, 0.72f), 36f); UiKit.PlaceTL(pbg.rectTransform, 1350, 80, 510, 330); pbg.raycastTarget = false;
                var ph = UiKit.Label("RmComboH", content, Z("你的組合", "Your build"), 28, UiTheme.Accent, TextAnchor.UpperLeft); ph.fontStyle = FontStyle.Bold; UiKit.PlaceTL(ph.rectTransform, 1382, 100, 440, 36);
                for (int k = 0; k < 4; k++)
                {
                    int kk = k;
                    chips[k] = UiKit.Label("RmChip" + k, content, "", 28, UiTheme.Text, TextAnchor.MiddleLeft); UiKit.PlaceTL(chips[k].rectTransform, 1382, 150 + k * 62, 450, 52);
                }
                // 屬性條
                var sbg = UiKit.Glass("RmStats", content, new Color(0.05f, 0.09f, 0.16f, 0.72f), 36f); UiKit.PlaceTL(sbg.rectTransform, 800, 440, 1060, 440); sbg.raycastTarget = false;
                var sh = UiKit.Label("RmStatH", content, Z("屬性", "Stats"), 28, UiTheme.Accent, TextAnchor.UpperLeft); sh.fontStyle = FontStyle.Bold; UiKit.PlaceTL(sh.rectTransform, 840, 456, 300, 36);
                for (int i = 0; i < StatN; i++)
                {
                    int ii = i; float y = 508 + i * 58;
                    var lb = UiKit.Label("RmSL" + i, content, "", 28, UiTheme.Text, TextAnchor.MiddleLeft); UiKit.PlaceTL(lb.rectTransform, 840, y, 200, 44);
                    var tr = UiKit.Glass("RmST" + i, content, new Color(1, 1, 1, 0.14f), 22f); UiKit.PlaceTL(tr.rectTransform, 1050, y + 8, 560, 28); tr.raycastTarget = false;
                    var fl = UiKit.Glass("RmSF" + i, content, StatColor(i), 22f); UiKit.PlaceTL(fl.rectTransform, 1050, y + 8, 40, 28); fl.raycastTarget = false; fills[i] = fl;
                    var dl = UiKit.Label("RmSD" + i, content, "", 26, Color.white, TextAnchor.MiddleLeft); UiKit.PlaceTL(dl.rectTransform, 1630, y, 200, 44); deltas[i] = dl;
                    building.Refreshers.Add(() => lb.text = RmData.StatName(ii));
                }
                var hint = UiKit.Label("RmHint", content, Z("◀ ▶ / 方向鍵 / 十字鍵切換    Enter / A 確定    Esc / B 返回", "◀ ▶ / arrows / D-pad to switch    Enter / A confirm    Esc / B back"), 24, new Color(0.79f, 0.83f, 0.90f), TextAnchor.UpperLeft);
                UiKit.PlaceTL(hint.rectTransform, 800, 905, 1060, 34);
                var note = UiKit.Label("RmNote", content, Z("屬性為多件組合後的數值;綠色/紅色 = 和目前已選的相比", "Stats are the combined build; green/red = vs. your current pick"), 20, new Color(0.79f, 0.83f, 0.90f, 0.7f), TextAnchor.UpperLeft);
                UiKit.PlaceTL(note.rectTransform, 800, 945, 1060, 30);

                building.Refreshers.Add(() =>
                {
                    var tint = Tint(kind, sel); cardImg.color = tint;
                    string nm = RmData.Name(kind, sel);
                    bigT.text = nm.Length > 0 ? nm.Substring(0, 1) : "?";
                    nameT.text = nm; otherT.text = RmData.Other(kind, sel);
                    idxT.text = (Loc.Lang == "zh" ? RmData.KindZh[kind] : RmData.KindEn[kind]) + "  " + (sel + 1) + "/" + n;
                    var now = Combo(sel); var old = Combo(before);
                    for (int i = 0; i < StatN; i++)
                    {
                        float w = Mathf.Max(28f, 560f * now[i]);
                        UiKit.PlaceTL(fills[i].rectTransform, 1050, 508 + i * 58 + 8, w, 28);
                        float d = (now[i] - old[i]) * 10f;
                        deltas[i].text = Mathf.Abs(d) < 0.05f ? (now[i] * 10f).ToString("0.0") : (now[i] * 10f).ToString("0.0") + "  (" + (d > 0 ? "+" : "") + d.ToString("0.0") + ")";
                        deltas[i].color = Mathf.Abs(d) < 0.05f ? Color.white : d > 0 ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.55f, 0.55f);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        int id = k == kind ? sel : RmGet(P, k);
                        chips[k].text = (k == kind ? "▶ " : "   ") + (Loc.Lang == "zh" ? RmData.KindZh[k] : RmData.KindEn[k]) + ":  " + RmData.Name(k, id);
                        chips[k].color = k == kind ? Color.white : new Color(0.79f, 0.83f, 0.90f);
                        chips[k].fontStyle = k == kind ? FontStyle.Bold : FontStyle.Normal;
                    }
                });
            }, 330f);
            Show(s, false);
            StartCoroutine(RmKeys(prev, next, s));
        }

        static Color StatColor(int i)
        {
            Color[] c = { new Color(1f, 0.45f, 0.35f), new Color(1f, 0.75f, 0.25f), new Color(0.65f, 0.55f, 1f), new Color(0.35f, 0.8f, 1f), new Color(0.45f, 0.9f, 0.5f), new Color(1f, 0.5f, 0.9f) };
            return c[i % c.Length];
        }

        // 左右切換:方向鍵、十字鍵、LB/RB、左搖桿左右(邊緣觸發)
        IEnumerator RmKeys(Action prev, Action next, MenuScreen scr)
        {
            yield return null; float px = 0f;
            while (cur == scr && scr != null)
            {
                if (menuVisible && !textMode)
                {
                    float x = Pad.LX; int dir = 0;
                    if (x > 0.6f && px <= 0.6f) dir = 1; else if (x < -0.6f && px >= -0.6f) dir = -1;
                    px = x;
                    if (Input.GetKeyDown(KeyCode.LeftArrow) || Pad.Down(Pad.DLeft) || Pad.Down(Pad.LB)) dir = -1;
                    if (Input.GetKeyDown(KeyCode.RightArrow) || Pad.Down(Pad.DRight) || Pad.Down(Pad.RB)) dir = 1;
                    if (dir != 0) { if (dir < 0) prev(); else next(); Juice.UiClick(); RefreshScreen(); }
                }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- 開始比賽
        void RmStart()
        {
            var c = RaceSession.Pending; RmApplyLook();
            switch (RaceMenus.Mode)
            {
                case 0:   // 大獎賽
                    c.GrandPrix = true; c.TimeTrial = false; c.FreeRoam = false; c.Cup = RaceMenus.SelectedCup; c.AiCount = 7; c.Items = true; c.ItemRule = 0; c.Laps = 3;
                    { var r = RmData.CupRaces(RaceMenus.SelectedCup); if (r.Length > 0) c.TrackId = r[0]; }
                    break;
                case 1:   // 計時賽
                    c.GrandPrix = false; c.TimeTrial = true; c.FreeRoam = false; c.Cup = -1; c.AiCount = 0; c.Items = false; c.Mirror = false; c.Reverse = false;
                    break;
                case 2:   // 對戰賽
                    c.GrandPrix = false; c.TimeTrial = false; c.FreeRoam = false; c.Cup = -1; c.Items = c.ItemRule != 2;
                    break;
                default:  // 自由探索
                    c.GrandPrix = false; c.TimeTrial = false; c.FreeRoam = true; c.Cup = -1; c.AiCount = 0; c.Items = false; c.Mirror = false; c.Reverse = false;
                    break;
            }
            if (c.Mirror) c.CC = 150;
            RmSave();
            RaceMenus.ClearCupProgress();
            Hide();
            if (RaceMenus.OnStart != null) { RaceMenus.OnStart(c.Clone()); return; }
            GameSession.Demo = false; GameSession.Rookie = false; GameSession.PracticeMode = false; GameSession.VsAi = false; GameSession.Racing = true;
            GameSession.Begin(Rig);
        }

        // ---------------------------------------------------------------- 賽車暫停選單(沒有任何 FRC 投籃訓練項目)
        internal void RmPause()
        {
            var s = Build(() => Loc.T("pause.title"), () =>
            {
                var c = RaceSession.Config; if (c == null) return "";
                string m = c.GrandPrix ? Z("大獎賽", "Grand Prix") : c.TimeTrial ? Z("計時賽", "Time Trial") : c.FreeRoam ? Z("自由探索", "Free Roam") : Z("對戰賽", "VS Race");
                return m + "  ·  " + (c.Mirror ? Z("鏡像", "Mirror") : c.CC + "cc");
            }, new[]
            {
                new Item(() => Loc.T("pause.resume"), Resume),
                new Item(() => Z("重來這一場", "Restart this race"), () => { Resume(); RaceSession.Restart(); }),
                new Item(() => Z("重置位置(回檢查點)", "Reset position"), () => { RaceSession.ResetPlayer(); Resume(); }),
                new Item(() => Z("回賽車選單", "Back to Kart menu"), () => { Time.timeScale = 1f; RaceMenus.ClearCupProgress(); GameSession.End(Rig); RmMain(); }),
                new Item(() => Loc.T("menu.settings"), () => { settingsBack = RmPause; ShowSettings(true); }),
                new Item(() => Loc.T("pause.menu"), BackToMain),
            }, Resume);
            Time.timeScale = 0f;
            Show(s, true);
        }

        // ---------------------------------------------------------------- 大獎賽積分榜
        internal void RmStandings(IList<RaceMenus.Row> src, int raceIdx, int raceCount, Action onNext, Action onQuit, string nextZh, string nextEn)
        {
            var rows = new List<RaceMenus.Row>(src); rows.Sort((a, b) => b.TotalPoints != a.TotalPoints ? b.TotalPoints.CompareTo(a.TotalPoints) : a.Place.CompareTo(b.Place));
            bool lastRace = raceIdx >= raceCount;
            var pops = new List<RectTransform>();
            Action goNext = () => { Hide(); onNext?.Invoke(); }, quit = () => { Hide(); if (onQuit != null) onQuit(); else RmMain(); };
            var s = Build(() => Z("積分榜", "Standings"), () => Z("第 ", "Race ") + raceIdx + "/" + raceCount + Z(" 場結束", " done"), new[]
            {
                new Item(() => (nextZh != null || nextEn != null) ? Z(nextZh ?? nextEn, nextEn ?? nextZh) : lastRace ? Z("看頒獎  ▸", "Podium  ▸") : Z("下一場  ▸", "Next race  ▸"), goNext),
                new Item(() => Z("回賽車選單", "Back to Kart menu"), quit),
            }, quit, content =>
            {
                var bg = UiKit.Glass("StBg", content, new Color(0.05f, 0.09f, 0.16f, 0.78f), 40f); UiKit.PlaceTL(bg.rectTransform, 800, 70, 1060, 940); bg.raycastTarget = false;
                string[] hz = { "名次", "車手", "本場", "總分" }, he = { "Place", "Driver", "Race", "Total" };
                float[] hx = { 830, 960, 1440, 1640 };
                for (int h = 0; h < 4; h++) { var l = UiKit.Label("StH" + h, content, Z(hz[h], he[h]), 24, UiTheme.Accent, h == 0 ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft); l.fontStyle = FontStyle.Bold; UiKit.PlaceTL(l.rectTransform, hx[h], 92, h == 0 ? 100 : 200, 36); }
                int cnt = Mathf.Min(rows.Count, 8); float rh = 100f;
                for (int i = 0; i < cnt; i++)
                {
                    var r = rows[i]; float y = 140 + i * rh;
                    var rowBg = UiKit.Glass("StR" + i, content, r.IsPlayer ? new Color(0.20f, 0.50f, 1f, 0.85f) : new Color(1, 1, 1, i % 2 == 0 ? 0.10f : 0.05f), 30f); UiKit.PlaceTL(rowBg.rectTransform, 818, y, 1034, rh - 10); rowBg.raycastTarget = false;
                    var medal = UiKit.Glass("StM" + i, content, i == 0 ? RacePalette.Gold : i == 1 ? RacePalette.Silver : i == 2 ? RacePalette.Bronze : new Color(1, 1, 1, 0.25f), 40f); UiKit.PlaceTL(medal.rectTransform, 846, y + 12, 66, 66); medal.raycastTarget = false;
                    var pn = UiKit.Label("StP" + i, content, (i + 1).ToString(), 40, i < 3 ? new Color(0.2f, 0.15f, 0.05f) : Color.white, TextAnchor.MiddleCenter); pn.fontStyle = FontStyle.Bold; UiKit.PlaceTL(pn.rectTransform, 846, y + 12, 66, 66);
                    var nm = UiKit.Label("StN" + i, content, (r.IsPlayer ? "★ " : "") + r.Name, 36, Color.white, TextAnchor.MiddleLeft); nm.fontStyle = r.IsPlayer ? FontStyle.Bold : FontStyle.Normal; UiKit.PlaceTL(nm.rectTransform, 960, y + 12, 460, 66);
                    var rp = UiKit.Label("StRp" + i, content, "+" + r.RacePoints, 34, new Color(0.7f, 1f, 0.7f), TextAnchor.MiddleLeft); UiKit.PlaceTL(rp.rectTransform, 1440, y + 12, 180, 66);
                    var tp = UiKit.Label("StTp" + i, content, r.TotalPoints.ToString(), 44, Color.white, TextAnchor.MiddleLeft); tp.fontStyle = FontStyle.Bold; UiKit.PlaceTL(tp.rectTransform, 1640, y + 12, 200, 66);
                    foreach (var ch in new Graphic[] { medal, pn, nm, rp, tp }) ch.transform.SetParent(rowBg.transform, true);
                    pops.Add(rowBg.rectTransform);
                }
            });
            Show(s, true);
            // 列依序彈入
            StartCoroutine(RmSlideRows(s, pops));
        }

        IEnumerator RmSlideRows(MenuScreen scr, List<RectTransform> rts)
        {
            var origin = new List<Vector2>(); foreach (var r in rts) origin.Add(r.anchoredPosition);
            float t0 = Time.unscaledTime;
            while (cur == scr && scr != null)
            {
                float t = Time.unscaledTime - t0; bool done = true;
                for (int i = 0; i < rts.Count; i++)
                {
                    float k = Mathf.Clamp01((t - 0.12f * i) / 0.35f); if (k < 1f) done = false;
                    rts[i].anchoredPosition = origin[i] + new Vector2((1f - EaseBack(k)) * 120f, 0f);
                }
                if (done) yield break;
                yield return null;
            }
        }

        static float EaseBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2); }

        // ---------------------------------------------------------------- 盃賽頒獎
        internal void RmPodium(IList<RaceMenus.Row> src, int cup, Action onDone)
        {
            var rows = new List<RaceMenus.Row>(src); rows.Sort((a, b) => b.TotalPoints != a.TotalPoints ? b.TotalPoints.CompareTo(a.TotalPoints) : a.Place.CompareTo(b.Place));
            int me = rows.FindIndex(r => r.IsPlayer);
            Action done = () => { Hide(); if (onDone != null) onDone(); else RmMain(); };
            var blocks = new List<RectTransform>(); var pieces = new List<RectTransform>();
            string Msg()
            {
                if (me == 0) return Z("金牌!你是冠軍!", "Gold! You're the champion!");
                if (me == 1) return Z("銀牌!差一點就第一了", "Silver! So close to gold");
                if (me == 2) return Z("銅牌!上台領獎囉", "Bronze! Up on the podium");
                return Z("這次沒上台,再挑戰一次!", "No podium this time — try again!");
            }
            var s = Build(() => Z("頒獎典禮", "Podium"), () => RmData.CupName(cup), new[]
            {
                new Item(() => Z("繼續", "Continue"), done),
            }, done, content =>
            {
                var bg = UiKit.Glass("PoBg", content, new Color(0.06f, 0.10f, 0.20f, 0.70f), 48f); UiKit.PlaceTL(bg.rectTransform, 800, 70, 1060, 940); bg.raycastTarget = false;
                var msg = UiKit.Label("PoMsg", content, "", 54, me == 0 ? RacePalette.Gold : me == 1 ? RacePalette.Silver : me == 2 ? RacePalette.Bronze : Color.white, TextAnchor.MiddleCenter); msg.fontStyle = FontStyle.Bold; UiKit.PlaceTL(msg.rectTransform, 800, 100, 1060, 80);
                building.Refreshers.Add(() => msg.text = Msg());
                // 順位:銀(左) 金(中) 銅(右);由下往上彈出
                int[] order = { 1, 0, 2 }; float[] hs = { 360, 470, 290 }; Color[] cs = { RacePalette.Silver, RacePalette.Gold, RacePalette.Bronze };
                float baseY = 880f, bw = 300f;
                for (int c = 0; c < 3; c++)
                {
                    int place = order[c]; if (place >= rows.Count) continue; var r = rows[place];
                    float x = 850 + c * (bw + 10); float h = hs[c];
                    var col = UiKit.Glass("PoCol" + c, content, Color.Lerp(cs[c], Color.white, 0.15f), 36f); UiKit.PlaceTL(col.rectTransform, x, baseY - h, bw, h); col.raycastTarget = false;
                    var num = UiKit.Label("PoNum" + c, content, (place + 1).ToString(), 110, new Color(0.25f, 0.18f, 0.05f, 0.85f), TextAnchor.MiddleCenter); num.fontStyle = FontStyle.Bold; UiKit.PlaceTL(num.rectTransform, x, baseY - h + 20, bw, 130);
                    var nm = UiKit.Label("PoNm" + c, content, (r.IsPlayer ? "★ " : "") + r.Name, 34, new Color(0.15f, 0.12f, 0.05f), TextAnchor.MiddleCenter); nm.fontStyle = FontStyle.Bold; UiKit.PlaceTL(nm.rectTransform, x, baseY - h + 160, bw, 50);
                    var pt = UiKit.Label("PoPt" + c, content, r.TotalPoints + Z(" 分", " pts"), 30, new Color(0.15f, 0.12f, 0.05f, 0.85f), TextAnchor.MiddleCenter); UiKit.PlaceTL(pt.rectTransform, x, baseY - h + 210, bw, 40);
                    // 獎牌圓(站在柱子上方)
                    var medal = UiKit.Glass("PoMed" + c, content, cs[c], 60f); UiKit.PlaceTL(medal.rectTransform, x + bw / 2f - 55f, baseY - h - 130f, 110, 110); medal.raycastTarget = false;
                    var mt = UiKit.Label("PoMt" + c, content, place == 0 ? "1st" : place == 1 ? "2nd" : "3rd", 34, new Color(0.25f, 0.18f, 0.05f), TextAnchor.MiddleCenter); mt.fontStyle = FontStyle.Bold; UiKit.PlaceTL(mt.rectTransform, x + bw / 2f - 55f, baseY - h - 130f, 110, 110);
                    blocks.Add(PopPivot(col.rectTransform)); blocks.Add(PopPivot(medal.rectTransform));
                    num.transform.SetParent(col.transform, true); nm.transform.SetParent(col.transform, true); pt.transform.SetParent(col.transform, true);   // 文字跟著柱子一起彈
                    mt.transform.SetParent(medal.transform, true);
                }
                if (me >= 3 && me < rows.Count)
                {
                    var r = rows[me];
                    var self = UiKit.Label("PoSelf", content, Z("你的名次:第 ", "Your place: ") + (me + 1) + Z(" 名 · ", " · ") + r.TotalPoints + Z(" 分", " pts"), 30, Color.white, TextAnchor.MiddleCenter); UiKit.PlaceTL(self.rectTransform, 800, 940, 1060, 50);
                }
                if (me >= 0 && me < 3)   // 彩帶
                {
                    var rnd = new System.Random(9427);
                    for (int i = 0; i < 36; i++)
                    {
                        var p = UiKit.Glass("PoFx" + i, content, Color.HSVToRGB((float)rnd.NextDouble(), 0.6f, 1f), 6f); p.raycastTarget = false;
                        UiKit.PlaceTL(p.rectTransform, 820 + (float)rnd.NextDouble() * 1020f, -40f, 14 + rnd.Next(10), 22 + rnd.Next(14)); pieces.Add(p.rectTransform);
                    }
                }
            });
            Show(s, true);
            StartCoroutine(RmPodiumAnim(s, blocks, pieces));
        }

        // 把 RectTransform 的軸心改到底部中央並維持位置(彈出時從底部長高)
        static RectTransform PopPivot(RectTransform rt)
        {
            Vector2 size = rt.sizeDelta; Vector2 oldPivot = rt.pivot; Vector2 pos = rt.anchoredPosition;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = pos + new Vector2((0.5f - oldPivot.x) * size.x, (0f - oldPivot.y) * size.y);
            return rt;
        }

        IEnumerator RmPodiumAnim(MenuScreen scr, List<RectTransform> blocks, List<RectTransform> pieces)
        {
            foreach (var b in blocks) b.localScale = new Vector3(1f, 0f, 1f);
            var startY = new List<float>(); var spd = new List<float>(); var rnd = new System.Random(77);
            foreach (var p in pieces) { startY.Add(p.anchoredPosition.y); spd.Add(180f + (float)rnd.NextDouble() * 260f); }
            float t0 = Time.unscaledTime;
            // 動畫順序:銅 → 銀 → 金(blocks 內是 [銀柱,銀牌,金柱,金牌,銅柱,銅牌])
            int[] seq = { 4, 0, 2 };
            while (cur == scr && scr != null)
            {
                float t = Time.unscaledTime - t0;
                for (int q = 0; q < 3; q++)
                {
                    int bi = seq[q] * 1; if (bi + 1 >= blocks.Count) continue;
                    float k = EaseBack((t - 0.4f - q * 0.6f) / 0.5f);
                    blocks[bi].localScale = new Vector3(1f, Mathf.Max(0f, k), 1f);
                    float k2 = EaseBack((t - 0.7f - q * 0.6f) / 0.4f);
                    blocks[bi + 1].localScale = new Vector3(Mathf.Max(0f, k2), Mathf.Max(0f, k2), 1f);
                }
                if (t > 2.0f)
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        float tt = t - 2.0f; float y = startY[i] - (tt * spd[i]) % 1100f;
                        pieces[i].anchoredPosition = new Vector2(pieces[i].anchoredPosition.x + Mathf.Sin(tt * 3f + i) * 0.6f, y);
                    }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- 截圖/測試入口(-menu race*)
        internal void RmDebug(string which)
        {
            RmLoad();
            var fake = new List<RaceMenus.Row>();
            string[] nm = { "玩家", "小橘貓", "阿福狗", "兔兔", "熊熊", "嘎嘎鴨", "狐狸", "企鵝" };
            for (int i = 0; i < 8; i++) fake.Add(new RaceMenus.Row { KartId = i, Name = nm[i], Place = i + 1, RacePoints = new[] { 15, 12, 10, 9, 8, 7, 6, 5 }[i], TotalPoints = new[] { 27, 36, 20, 18, 30, 12, 14, 9 }[i], IsPlayer = i == 0 });
            if (which == "racepick") { RaceMenus.Mode = 2; RmPickKind(1, RmMain); }
            else if (which == "racevs") { RaceMenus.Mode = 2; RmVsRules(); }
            else if (which == "racestand") RaceMenus.ShowStandings(fake, 2, 4, null, null);
            else if (which == "racepodium") RaceMenus.ShowPodium(fake, 0, null);
            else if (which == "racepause") RmPause();
            else if (which == "racecup") { RaceMenus.Mode = 0; RmGpCup(); }
            else RmMain();
        }
    }
}
