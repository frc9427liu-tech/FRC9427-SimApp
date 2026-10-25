using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽車 HUD(OnGUI,模組 P)。視覺語言同 SimHud(液態玻璃、依螢幕高度縮放、選單開著時不畫)。
    // M1:倒數 3-2-1-GO、圈數/計時、最佳圈、速度條、中央提示、終點成績卡。
    // M2/M3:大字名次(右下,變動時彈跳)、小地圖(右上、烘成貼圖)、漂移充能條、成績卡加名次與各圈時間。道具欄 M4 補。
    public class RaceHud : MonoBehaviour
    {
        static readonly GlassStyle stHud = GlassStyle.HudPanel(), stCard = GlassStyle.HudCard(), stPill = GlassStyle.Pill(), stPrim = GlassStyle.PillPrimary(), stTrack = GlassStyle.BarTrack();
        static readonly Dictionary<long, GUIStyle> styleCache = new Dictionary<long, GUIStyle>();

        int lastLap = -1; string banner = ""; float bannerUntil;
        bool lastWrong;
        int lastPlace = -1; float placePop = -9f;
        float finishedAt = -1f;
        int lastCoins; float coinPop = -9f;
        bool lastTricking; float trickAt = -9f;
        ItemKind lastHeld; float itemPop = -9f;
        const float StartWindowFrom = 2.0f, StartWindowTo = 0.5f;   // 倒數剩餘秒數落在這段 = 起跑加速時間窗(提示條用;RaceKart 目前沒有起跑加速 API,先只做視覺提示)
        const float ResultDelay = 2f;   // 完賽後鏡頭環繞 2 s 再跳成績卡
        Texture2D mapTex, arrowTex; RaceTrack mapTrack; float mapBw = 1f, mapBh = 1f, mapS = 1f, mapOx, mapOy;
        const int MapPx = 256;
        readonly Dictionary<RaceKart, Color> kartColors = new Dictionary<RaceKart, Color>();

        void OnDestroy()
        {
            if (mapTex != null) Destroy(mapTex);
            if (arrowTex != null) Destroy(arrowTex);
        }

        static GUIStyle Style(int size, Color c, TextAnchor a = TextAnchor.UpperLeft, FontStyle fs = FontStyle.Normal)
        {
            Color32 c32 = c;
            long key = ((long)size << 40) ^ ((long)c32.r << 32) ^ ((long)c32.g << 24) ^ ((long)c32.b << 16) ^ ((long)c32.a << 8) ^ ((long)(int)a << 4) ^ (long)(int)fs;
            if (!styleCache.TryGetValue(key, out var s) || s == null)
            {
                s = new GUIStyle(GUI.skin.label) { fontSize = size, font = UiTheme.Font, alignment = a, fontStyle = fs, wordWrap = false };
                s.normal.textColor = c;
                styleCache[key] = s;
            }
            return s;
        }

        static void Rounded(Rect r, Color c, float rad)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(rad, rad, rad, rad));
        }

        static void Panel(Rect r, bool card)
        {
            if (UiGlass.Ready) { GlassGL.Draw(r, Mathf.Min(card ? 44f : 22f, r.height * 0.5f), card ? stCard : stHud); return; }
            float rad = Mathf.Min(18f, r.height * 0.5f);
            Rounded(new Rect(r.x - 1, r.y + 3, r.width + 2, r.height + 2), new Color(0f, 0f, 0f, 0.18f), rad + 2f);
            Rounded(r, new Color(0.05f, 0.09f, 0.16f, card ? 0.82f : 0.6f), rad);
            Rounded(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height * 0.5f), new Color(0.75f, 0.88f, 1f, 0.10f), rad - 2f);
        }

        static void Bar(Rect r, float frac, Color fill)
        {
            if (UiGlass.Ready)
            {
                GlassGL.Draw(r, r.height / 2f, stTrack);
                if (frac > 0.01f) GlassGL.Draw(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * Mathf.Clamp01(frac)), r.height), r.height / 2f, GlassStyle.BarFill(fill));
                return;
            }
            Rounded(r, new Color(1f, 1f, 1f, 0.12f), 5f);
            if (frac > 0.01f) Rounded(new Rect(r.x, r.y, Mathf.Max(8f, r.width * Mathf.Clamp01(frac)), r.height), fill, 5f);
        }

        static bool GBtn(Rect r, string text, bool primary)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (UiGlass.Ready) GlassGL.Draw(r, r.height / 2f, primary ? stPrim : stPill, hover ? 1f : 0f, 0f, 1f, 0f);
            else Rounded(r, primary ? new Color(0.35f, 0.65f, 1f, hover ? 0.8f : 0.6f) : new Color(1f, 1f, 1f, hover ? 0.25f : 0.14f), r.height / 2f);
            GUI.Label(r, text, Style(22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        static string Z(string zh, string en) { return Loc.Lang == "en" ? en : zh; }

        void Update()
        {
            if (!RaceSession.Active) return;
            var p = RaceSession.Player != null ? RaceSession.Player.Progress : null;
            if (p == null) return;
            if (p.Lap != lastLap)
            {
                if (lastLap >= 0 && p.Lap > lastLap && RaceSession.Phase == RacePhase.Racing)
                {
                    int total = RaceSession.Config != null ? RaceSession.Config.Laps : 1;
                    if (p.Lap == total - 1) Say(Z("最後一圈!", "FINAL LAP!"));
                    else if (p.Lap < total) Say(Z("第 " + (p.Lap + 1) + " 圈", "LAP " + (p.Lap + 1)));
                }
                lastLap = p.Lap;
            }
            if (p.WrongWay && !lastWrong) Say(Z("逆向!", "WRONG WAY!"));
            lastWrong = p.WrongWay;
            if (RaceSession.Phase == RacePhase.Countdown) { lastLap = -1; lastPlace = -1; }
            int pl = RaceSession.Place(RaceSession.Player);
            if (pl != lastPlace) { if (lastPlace > 0 && RaceSession.Phase == RacePhase.Racing) placePop = Time.unscaledTime; lastPlace = pl; }
            var pk = RaceSession.Player;
            if (pk != null)
            {
                if (pk.Coins != lastCoins) { if (pk.Coins > lastCoins) coinPop = Time.unscaledTime; lastCoins = pk.Coins; }
                if (pk.Tricking && !lastTricking) trickAt = Time.unscaledTime;
                lastTricking = pk.Tricking;
                var hk = RaceItems.Held(pk); if (hk != lastHeld) { if (hk != ItemKind.None) itemPop = Time.unscaledTime; lastHeld = hk; }
            }
            if (RaceSession.Phase == RacePhase.Finished) { if (finishedAt < 0f) finishedAt = Time.unscaledTime; }
            else finishedAt = -1f;
        }

        void Say(string s) { banner = s; bannerUntil = Time.unscaledTime + 2f; }

        // 裁判提示(RaceReferee 呼叫):中央偏下一行小字,獨立於大字 banner(不會蓋掉「最後一圈」)
        static string toastMsg = ""; static float toastUntil;
        public static void Toast(string zh, string en, float seconds = 2.8f) { toastMsg = Loc.Lang == "en" ? en : zh; toastUntil = Time.unscaledTime + seconds; }

        void OnGUI()
        {
            if (!RaceSession.Active || MenuSystem.Blocking) return;
            bool finished = RaceSession.Phase == RacePhase.Finished;
            if (Event.current.type != EventType.Repaint && !finished) return;   // 只有成績卡的按鈕需要非 Repaint 事件
            if (Event.current.type == EventType.Repaint) UiGlass.HudFrame = Time.frameCount;
            GUI.depth = -100;
            float sc = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.8f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float W = Screen.width / sc, H = Screen.height / sc;
            var ph = RaceSession.Phase;

            bool showCard = finished && finishedAt >= 0f && Time.unscaledTime - finishedAt >= ResultDelay;
            if (ph == RacePhase.Countdown || ph == RacePhase.Racing || ph == RacePhase.Finished)
            {
                if (!finished && Event.current.type == EventType.Repaint) { DrawTimer(W, H); DrawMinimap(W, H); DrawPlace(W, H); DrawItem(W, H); DrawStatus(W, H); DrawGlide(W, H); DrawDrift(W, H); DrawSpeed(W, H); DrawBanner(W, H); }
                if (finished && !showCard && Event.current.type == EventType.Repaint) DrawFinishBanner(W, H);
                DrawCountdown(W, H);
                if (ph == RacePhase.Countdown && Event.current.type == EventType.Repaint) DrawStartBoost(W, H);
            }
            if (showCard) DrawResult(W, H);
            GUI.matrix = Matrix4x4.identity;
        }

        // ---- 倒數 / GO
        void DrawCountdown(float W, float H)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (RaceSession.Phase == RacePhase.Countdown)
            {
                float left = Mathf.Max(0f, RaceSession.CountdownLeft);
                int n = Mathf.CeilToInt(left);
                if (n < 1) return;
                float frac = n - left;   // 0 → 1 在這一秒內
                float s = Mathf.Lerp(1.4f, 1.0f, Mathf.Clamp01(frac * 4f));
                var c = Color.white; c.a = Mathf.Clamp01(1.4f - frac);
                Big(n.ToString(), W / 2f, H * 0.38f, 220, c, s);
            }
            else if (RaceSession.Phase == RacePhase.Racing && RaceSession.RaceTime < 0.8f)
            {
                float t = RaceSession.RaceTime / 0.8f;
                var c = RacePalette.Go; c.a = 1f - t * t;
                Big("GO!", W / 2f, H * 0.38f, 200, c, Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(t * 5f)));
            }
        }

        static void Big(string text, float cx, float cy, int size, Color c, float scale)
        {
            var m = GUI.matrix;
            GUI.matrix = m * Juice.PopMatrix(new Vector2(cx, cy), scale);
            var sh = new Color(0f, 0f, 0f, c.a * 0.45f);
            GUI.Label(new Rect(cx - 500f + 5f, cy - size * 0.6f + 6f, 1000f, size * 1.2f), text, Style(size, sh, TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.Label(new Rect(cx - 500f, cy - size * 0.6f, 1000f, size * 1.2f), text, Style(size, c, TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.matrix = m;
        }

        // ---- 圈數 / 計時(右上)
        void DrawTimer(float W, float H)
        {
            var p = RaceSession.Player != null ? RaceSession.Player.Progress : null;
            int total = RaceSession.Config != null ? RaceSession.Config.Laps : 1;
            int lap = Mathf.Clamp((p != null ? p.Lap : 0) + 1, 1, total);
            var r = new Rect(W - 0.02f * W - 300f, 0.03f * H, 300f, 128f);
            Panel(r, false);
            GUI.Label(new Rect(r.x + 18f, r.y + 6f, 264f, 48f), "LAP " + lap + "/" + total, Style(36, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold));
            GUI.Label(new Rect(r.x + 18f, r.y + 48f, 264f, 40f), RaceSession.FormatTime(RaceSession.RaceTime), Style(30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold));
            float done = 0f; foreach (var t in RaceSession.LapTimes) done += t;
            float cur = Mathf.Max(0f, RaceSession.RaceTime - done);
            float best = RaceSession.BestLapTime();
            var dim = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(r.x + 18f, r.y + 88f, 128f, 30f), Z("本圈 ", "Lap ") + RaceSession.FormatTime(cur), Style(15, dim, TextAnchor.MiddleLeft));
            GUI.Label(new Rect(r.x + 150f, r.y + 88f, 140f, 30f), Z("最佳 ", "Best ") + (best > 0f ? RaceSession.FormatTime(best) : "--:--.---"), Style(15, dim, TextAnchor.MiddleRight));
        }

        // ---- 速度(底部中央)
        void DrawSpeed(float W, float H)
        {
            var k = RaceSession.Player; if (k == null) return;
            var r = new Rect(W / 2f - 160f, H - 70f, 320f, 44f);
            if (UiGlass.Ready) GlassGL.Draw(r, r.height / 2f, stPill); else Rounded(r, new Color(0.05f, 0.09f, 0.16f, 0.6f), r.height / 2f);
            float kmh = Mathf.Abs(k.SpeedKmh);
            GUI.Label(new Rect(r.x + 18f, r.y, 110f, r.height), Mathf.RoundToInt(kmh).ToString() + " km/h", Style(20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold));
            float frac = Mathf.Clamp01(Mathf.Abs(k.Speed) / Mathf.Max(1f, k.MaxSpeed));
            Bar(new Rect(r.x + 140f, r.y + 15f, 164f, 14f), frac, Color.Lerp(RacePalette.Drift[0], RacePalette.Drift[1], frac));
        }

        // ---- 名次(右下大字;變動時彈跳)
        static string Ord(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return "th";
            switch (n % 10) { case 1: return "st"; case 2: return "nd"; case 3: return "rd"; default: return "th"; }
        }
        static Color PlaceColor(int n) { return n == 1 ? RacePalette.Gold : n == 2 ? RacePalette.Silver : n == 3 ? RacePalette.Bronze : Color.white; }

        // 道具顯示名(中/英):15 種
        static string ItemLabel(ItemKind k)
        {
            bool en = Loc.Lang == "en";
            switch (k)
            {
                case ItemKind.Boost: return en ? "Boost" : "加速";
                case ItemKind.TripleBoost: return en ? "Triple Boost" : "三連加速";
                case ItemKind.Shield: return en ? "Shield" : "護盾";
                case ItemKind.Oil: return en ? "Banana" : "香蕉皮";
                case ItemKind.TripleBanana: return en ? "Triple Banana" : "三連香蕉皮";
                case ItemKind.Boo: return en ? "Boo" : "幽靈";
                case ItemKind.Boomerang: return en ? "Boomerang" : "迴力鏢花";
                case ItemKind.Bumper: return en ? "Shell" : "龜殼";
                case ItemKind.Homing: return en ? "Red Shell" : "紅龜殼";
                case ItemKind.Star: return en ? "Star" : "星星";
                case ItemKind.Lightning: return en ? "Lightning" : "閃電";
                case ItemKind.Bomb: return en ? "Bomb" : "炸彈";
                case ItemKind.BlueShell: return en ? "Blue Shell" : "藍殼";
                case ItemKind.Mega: return en ? "Mega" : "巨大化";
                case ItemKind.Bullet: return en ? "Bullet" : "子彈加速";
                case ItemKind.FakeBox: return en ? "Fake Box" : "假道具箱";
                case ItemKind.TripleBumper: return en ? "Triple Shell" : "三連龜殼";
                case ItemKind.InkBlast: return en ? "Ink" : "墨水";
                case ItemKind.Feather: return en ? "Feather" : "羽毛";
                case ItemKind.Coin: return en ? "Coins" : "金幣";
                case ItemKind.FireFlower: return en ? "Fire Flower" : "火焰花";
                case ItemKind.GoldBoost: return en ? "Gold Boost" : "黃金加速";
                case ItemKind.SuperHorn: return en ? "Super Horn" : "超級喇叭";
                default: return k.ToString();
            }
        }
        // 目前持有的道具(左下角):名稱+剩餘次數;抽獎中顯示「抽選中…」
        // 道具欄(左上角圓角大框):圖示色塊+名稱+剩餘次數;抽獎中色塊快速輪轉
        static Color ItemColor(ItemKind k)
        {
            if (k == ItemKind.Oil || k == ItemKind.TripleBanana) return new Color(1f, 0.86f, 0.18f);     // 香蕉皮:黃
            if (k == ItemKind.Boo) return new Color(0.82f, 0.9f, 1f);                                      // 幽靈:淡藍白
            if (k == ItemKind.Boomerang) return new Color(1f, 0.62f, 0.2f);                                // 迴力鏢花:橘
            return Color.HSVToRGB(((int)k * 0.137f) % 1f, 0.55f, 1f);
        }
        // 道具圖示字:中文取第一字;香蕉類用「蕉 / Bn」避免三連香蕉皮和香蕉皮同字看不出差別時以名稱區分
        static Texture2D ItemIcon(ItemKind k)
        {
            return k == ItemKind.None ? null : RaceShaders.Tex("ui_item_" + k.ToString().ToLowerInvariant(), false);
        }
        static string ItemGlyph(ItemKind k, string lab)
        {
            if (k == ItemKind.Oil) return Loc.Lang == "en" ? "B" : "蕉";
            if (k == ItemKind.TripleBanana) return Loc.Lang == "en" ? "B3" : "蕉3";
            return lab.Substring(0, 1);
        }
        void DrawItem(float W, float H)
        {
            var p = RaceSession.Player; if (p == null) return;
            var k = RaceItems.Held(p);
            bool rolling = RaceItems.Rolling(p) && k == ItemKind.None;     // 第 1 格已有道具時,抽獎轉的是第 2 格
            bool has = rolling || k != ItemKind.None;
            var r = new Rect(0.02f * W, 0.03f * H, 140f, 140f);
            float age = Time.unscaledTime - itemPop; float pop = age < 0.35f ? Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(age / 0.35f)) : 1f;
            var m = GUI.matrix; GUI.matrix = m * Juice.PopMatrix(r.center, pop);
            Rounded(new Rect(r.x - 2f, r.y + 4f, r.width + 4f, r.height + 4f), new Color(0f, 0f, 0f, 0.25f), 34f);
            var frameTex = RaceShaders.Tex("ui_item_frame", false);   // 原創道具框貼圖(白邊 + 深藍底 + 上方高光);沒有就用圓角矩形
            if (frameTex != null)
            {
                var gc = GUI.color; GUI.color = has ? Color.white : new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(r, frameTex, ScaleMode.StretchToFill, true);
                GUI.color = gc;
            }
            else
            {
                Rounded(r, has ? new Color(1f, 1f, 1f, 0.92f) : new Color(1f, 1f, 1f, 0.35f), 32f);
                Rounded(new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 12f), new Color(0.07f, 0.11f, 0.20f, 0.88f), 27f);
            }
            if (has)
            {
                var shown = rolling ? ItemTable.Order[(int)(Time.unscaledTime * 14f) % ItemTable.Order.Length] : k;
                if (shown == ItemKind.None) shown = ItemKind.Boost;
                var col = ItemColor(shown); float bob = rolling ? Mathf.Sin(Time.unscaledTime * 30f) * 3f : 0f;
                var disc = new Rect(r.center.x - 38f, r.y + 20f + bob, 76f, 76f);
                string lab = rolling ? "?" : ItemLabel(k);
                var icon = ItemIcon(shown);
                if (icon != null)
                    GUI.DrawTexture(new Rect(r.center.x - 50f, r.y + 6f + bob, 100f, 100f), icon, ScaleMode.ScaleToFit, true);   // MK 式:道具就是一張圖案
                else
                {
                    Rounded(new Rect(disc.x - 3f, disc.y - 3f, disc.width + 6f, disc.height + 6f), new Color(1f, 1f, 1f, 0.85f), 41f);
                    Rounded(disc, col, 38f);
                    GUI.Label(disc, rolling ? "?" : ItemGlyph(k, lab), Style(rolling ? 52 : 44, new Color(0.1f, 0.12f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold));
                }
                GUI.Label(new Rect(r.x, r.yMax - 40f, r.width, 30f), rolling ? Z("抽選中…", "Rolling…") : lab, Style(lab.Length > 5 && Loc.Lang == "en" ? 17 : 21, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
                int c = rolling ? 1 : RaceItems.Charges(p);
                if (c > 1) { var cb = new Rect(r.xMax - 40f, r.y - 8f, 46f, 36f); Rounded(cb, RacePalette.Gold, 18f); GUI.Label(cb, "x" + c, Style(22, new Color(0.2f, 0.12f, 0.02f), TextAnchor.MiddleCenter, FontStyle.Bold)); }
            }
            else GUI.Label(r, Z("道具", "ITEM"), Style(24, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.matrix = m;
            if (has && !rolling) GUI.Label(new Rect(r.x - 10f, r.yMax + 6f, r.width + 20f, 24f), Z("[E / X / LB 使用]", "[E / X / LB]"), Style(15, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter));
            DrawItemSecond(p, r, k);
        }

        // 第 2 格(主格下方的小框):有道具顯示名稱 / 次數;第 1 格有道具而箱子正在抽 → 顯示「?」;交換鍵提示
        void DrawItemSecond(RaceKart p, Rect main, ItemKind first)
        {
            var k2 = RaceItems.HeldSecond(p);
            bool roll2 = RaceItems.Rolling(p) && first != ItemKind.None;
            bool has2 = k2 != ItemKind.None || roll2;
            var r = new Rect(main.x + 38f, main.yMax + 34f, 64f, 64f);
            Rounded(new Rect(r.x - 1f, r.y + 3f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.2f), 18f);
            Rounded(r, has2 ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.25f), 17f);
            Rounded(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), new Color(0.07f, 0.11f, 0.20f, 0.88f), 14f);
            if (!has2) return;
            var shown = roll2 ? ItemTable.Order[(int)(Time.unscaledTime * 14f) % ItemTable.Order.Length] : k2;
            var disc = new Rect(r.x + 12f, r.y + 8f, 40f, 40f);
            string lab = roll2 ? "?" : ItemLabel(k2);
            var icon2 = ItemIcon(shown);
            if (icon2 != null) GUI.DrawTexture(new Rect(r.x + 7f, r.y + 3f, 50f, 50f), icon2, ScaleMode.ScaleToFit, true);
            else
            {
                Rounded(disc, ItemColor(shown), 20f);
                GUI.Label(disc, roll2 ? "?" : ItemGlyph(k2, lab), Style(26, new Color(0.1f, 0.12f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold));
            }
            if (!roll2)
            {
                GUI.Label(new Rect(r.x - 8f, r.yMax - 16f, r.width + 16f, 16f), lab, Style(12, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
                int c2 = RaceItems.Charges2(p);
                if (c2 > 1) { var cb = new Rect(r.xMax - 22f, r.y - 8f, 30f, 24f); Rounded(cb, RacePalette.Gold, 12f); GUI.Label(cb, "x" + c2, Style(14, new Color(0.2f, 0.12f, 0.02f), TextAnchor.MiddleCenter, FontStyle.Bold)); }
                if (first != ItemKind.None) GUI.Label(new Rect(main.x - 10f, r.yMax + 4f, main.width + 20f, 20f), Z("[F / 十字鍵上 交換]", "[F / D-Pad Up swap]"), Style(13, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleCenter));
            }
        }

        // 左上狀態列(道具欄右邊):排氣量、金幣 x/10、盃賽進度
        void DrawStatus(float W, float H)
        {
            var cfg = RaceSession.Config; var k = RaceSession.Player; if (cfg == null) return;
            float x = 0.02f * W + 140f + 14f, y = 0.03f * H;
            string cc = cfg.Mirror ? Z("鏡像 150cc", "Mirror 150cc") : cfg.Infinite ? Z("無限 x", "Infinite x") + (k != null ? k.InfiniteMul : 1f).ToString("0.0") : cfg.CC + "cc";
            var r1 = new Rect(x, y, cfg.Mirror ? 190f : cfg.Infinite ? 200f : 120f, 42f);
            if (UiGlass.Ready) GlassGL.Draw(r1, r1.height / 2f, stPill); else Rounded(r1, new Color(0.05f, 0.09f, 0.16f, 0.6f), r1.height / 2f);
            GUI.Label(r1, cc, Style(24, RacePalette.Go, TextAnchor.MiddleCenter, FontStyle.Bold));
            int coins = k != null ? Mathf.Clamp(k.Coins, 0, 10) : 0;
            float age = Time.unscaledTime - coinPop; float pop = age < 0.3f ? Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(age / 0.3f)) : 1f;
            var r2 = new Rect(x, y + 50f, 150f, 42f);
            var m = GUI.matrix; GUI.matrix = m * Juice.PopMatrix(r2.center, pop);
            if (UiGlass.Ready) GlassGL.Draw(r2, r2.height / 2f, stPill); else Rounded(r2, new Color(0.05f, 0.09f, 0.16f, 0.6f), r2.height / 2f);
            Rounded(new Rect(r2.x + 8f, r2.y + 6f, 30f, 30f), new Color(0.55f, 0.38f, 0.02f, 1f), 15f);
            Rounded(new Rect(r2.x + 10f, r2.y + 8f, 26f, 26f), RacePalette.Gold, 13f);
            GUI.Label(new Rect(r2.x + 10f, r2.y + 8f, 26f, 26f), "$", Style(18, new Color(0.5f, 0.32f, 0f), TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.Label(new Rect(r2.x + 46f, r2.y, 100f, r2.height), coins + "/10", Style(26, coins >= 10 ? RacePalette.Gold : Color.white, TextAnchor.MiddleLeft, FontStyle.Bold));
            GUI.matrix = m;
            if (cfg.GrandPrix && RaceMenus.CupRaceCount > 0)
            {
                var r3 = new Rect(x, y + 100f, 330f, 42f);
                if (UiGlass.Ready) GlassGL.Draw(r3, r3.height / 2f, stPill); else Rounded(r3, new Color(0.05f, 0.09f, 0.16f, 0.6f), r3.height / 2f);
                GUI.Label(r3, Z("第 " + RaceMenus.CupRaceIndex + "/" + RaceMenus.CupRaceCount + " 場  ·  總分 " + RaceMenus.CupTotalPoints, "Race " + RaceMenus.CupRaceIndex + "/" + RaceMenus.CupRaceCount + "  ·  " + RaceMenus.CupTotalPoints + " pts"), Style(21, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
            }
        }

        // 滑翔中 / 特技提示(畫面中央偏上)
        void DrawGlide(float W, float H)
        {
            var k = RaceSession.Player; if (k == null) return;
            if (k.Gliding)
            {
                var r = new Rect(W / 2f - 150f, H * 0.14f + Mathf.Sin(Time.unscaledTime * 6f) * 3f, 300f, 54f);
                if (UiGlass.Ready) GlassGL.Draw(r, r.height / 2f, stPill); else Rounded(r, new Color(0.05f, 0.09f, 0.16f, 0.6f), r.height / 2f);
                Rounded(new Rect(r.x + 16f, r.y + 16f, 34f, 22f), RacePalette.Drift[0], 11f);
                Rounded(new Rect(r.x + 30f, r.y + 12f, 22f, 30f), Color.Lerp(RacePalette.Drift[0], Color.white, 0.4f), 11f);
                GUI.Label(new Rect(r.x + 64f, r.y, 220f, r.height), Z("滑翔中  ", "GLIDING  ") + k.GlideTime.ToString("0.0") + "s", Style(28, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold));
            }
            if (k.Tricking)
            {
                float age = Time.unscaledTime - trickAt; float sc = Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(age / 0.25f));
                var cx = new Vector2(W / 2f, H * 0.27f);
                var m = GUI.matrix; GUI.matrix = m * Juice.PopMatrix(cx, sc);
                GUI.Label(new Rect(cx.x - 300f + 3f, cx.y - 30f + 4f, 600f, 60f), Z("特技!", "TRICK!"), Style(54, new Color(0f, 0f, 0f, 0.45f), TextAnchor.MiddleCenter, FontStyle.Bold));
                GUI.Label(new Rect(cx.x - 300f, cx.y - 30f, 600f, 60f), Z("特技!", "TRICK!"), Style(54, RacePalette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold));
                GUI.matrix = m;
            }
        }

        // 起跑加速提示條(倒數中):亮綠色區段 = 這段時間按住油門
        void DrawStartBoost(float W, float H)
        {
            float left = Mathf.Max(0f, RaceSession.CountdownLeft), total = RaceSession.CountdownLen;
            var r = new Rect(W / 2f - 200f, H - 150f, 400f, 54f);
            if (UiGlass.Ready) GlassGL.Draw(r, r.height / 2f, stPill); else Rounded(r, new Color(0.05f, 0.09f, 0.16f, 0.6f), r.height / 2f);
            var bar = new Rect(r.x + 24f, r.y + 28f, r.width - 48f, 14f);
            Rounded(bar, new Color(1f, 1f, 1f, 0.16f), 7f);
            float fromX = bar.x + bar.width * (1f - StartWindowFrom / total), toX = bar.x + bar.width * (1f - StartWindowTo / total);
            bool inWin = left <= StartWindowFrom && left >= StartWindowTo;
            Rounded(new Rect(fromX, bar.y - 2f, toX - fromX, bar.height + 4f), inWin ? RacePalette.Go : new Color(RacePalette.Go.r, RacePalette.Go.g, RacePalette.Go.b, 0.45f), 8f);
            float px = bar.x + bar.width * (1f - left / total);
            Rounded(new Rect(px - 4f, bar.y - 6f, 8f, bar.height + 12f), Color.white, 4f);
            GUI.Label(new Rect(r.x, r.y + 2f, r.width, 26f), inWin ? Z("現在!按住油門", "NOW! Hold throttle") : left > StartWindowFrom ? Z("準備起跑加速…", "Get ready…") : Z("起跑加速", "Start boost"), Style(20, inWin ? RacePalette.Go : Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
        }
        void DrawPlace(float W, float H)
        {
            if (RaceSession.Karts.Count <= 1) return;
            int total = RaceSession.Karts.Count, pl = Mathf.Clamp(RaceSession.Place(RaceSession.Player), 1, total);
            var r = new Rect(W - 0.03f * W - 230f, H - 0.05f * H - 150f, 230f, 150f);
            float age = Time.unscaledTime - placePop;
            float sc = age < 0.4f ? Mathf.Lerp(1.35f, 1f, Mathf.Clamp01(age / 0.4f)) : 1f;
            var m = GUI.matrix;
            GUI.matrix = m * Juice.PopMatrix(new Vector2(r.xMax - 40f, r.center.y), sc);
            var c = PlaceColor(pl); var sh = new Color(0f, 0f, 0f, 0.5f);
            var nr = new Rect(r.x, r.y, 130f, r.height); var sr = new Rect(r.x + 132f, r.y + 22f, 100f, 60f); var tr = new Rect(r.x + 132f, r.y + 80f, 100f, 40f);
            GUI.Label(new Rect(nr.x + 4f, nr.y + 5f, nr.width, nr.height), pl.ToString(), Style(120, sh, TextAnchor.MiddleRight, FontStyle.Bold));
            GUI.Label(nr, pl.ToString(), Style(120, c, TextAnchor.MiddleRight, FontStyle.Bold));
            GUI.Label(new Rect(sr.x + 3f, sr.y + 3f, sr.width, sr.height), Ord(pl), Style(52, sh, TextAnchor.MiddleLeft, FontStyle.Bold));
            GUI.Label(sr, Ord(pl), Style(52, c, TextAnchor.MiddleLeft, FontStyle.Bold));
            GUI.Label(tr, "/ " + total, Style(28, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft, FontStyle.Bold));
            GUI.matrix = m;
        }

        // ---- 漂移充能條(速度條上方;依 DriftLevel 顯色。門檻 1.0 / 2.0 / 3.2 s = Lv1/2/3)
        void DrawDrift(float W, float H)
        {
            var k = RaceSession.Player; if (k == null) return;
            bool drifting = k.Drifting, boosting = k.Boosting;
            if (!drifting && !boosting) return;
            float ch = k.DriftCharge;
            float x0 = W / 2f - 160f, y = H - 70f - 24f, tot = 320f, gap = 6f;
            float[] edge = { 0f, 1f / 3.2f, 2f / 3.2f, 1f };
            float[] lo = { 0f, 1f, 2f }, hi = { 1f, 2f, 3.2f };
            int lv = k.DriftLevel;
            bool flash = ((int)(Time.unscaledTime * 8f) & 1) == 0;
            for (int i = 0; i < 3; i++)
            {
                float a = x0 + edge[i] * tot + (i > 0 ? gap * 0.5f : 0f), b = x0 + edge[i + 1] * tot - (i < 2 ? gap * 0.5f : 0f);
                var seg = new Rect(a, y, b - a, 14f);
                Rounded(new Rect(seg.x - 1f, seg.y - 1f, seg.width + 2f, seg.height + 2f), new Color(0f, 0f, 0f, 0.45f), 8f);
                Rounded(seg, new Color(1f, 1f, 1f, 0.16f), 7f);
                float f; Color col;
                if (drifting) { f = Mathf.Clamp01((ch - lo[i]) / (hi[i] - lo[i])); col = RacePalette.Drift[i]; }
                else { f = Mathf.Clamp01(k.BoostRemaining); col = RacePalette.Go; }
                if (f > 0.01f) Rounded(new Rect(seg.x, seg.y, Mathf.Max(10f, seg.width * f), seg.height), col, 7f);
                if (drifting && lv == i + 1 && flash && ch - lo[i] < 0.25f) Rounded(seg, new Color(1f, 1f, 1f, 0.4f), 7f);   // 剛升級閃一下
            }
        }

        // ---- 小地圖(右上,圈數面板下方):賽道烘成貼圖一次,之後只畫車點
        void BakeMap(RaceTrack tr)
        {
            mapTrack = tr;
            if (mapTex != null) { Destroy(mapTex); mapTex = null; }
            if (tr == null || tr.Samples.Count < 2) return;
            var bounds = tr.MapBounds; mapBw = Mathf.Max(1f, bounds.width); mapBh = Mathf.Max(1f, bounds.height);
            mapS = (MapPx - 28f) / Mathf.Max(mapBw, mapBh); mapOx = (MapPx - mapBw * mapS) * 0.5f; mapOy = (MapPx - mapBh * mapS) * 0.5f;
            var px = new Color32[MapPx * MapPx];
            int n = tr.Samples.Count;
            for (int pass = 0; pass < 2; pass++)
            {
                int rad = pass == 0 ? 6 : 3; Color32 col = pass == 0 ? new Color32(8, 14, 26, 235) : new Color32(235, 240, 250, 255);
                Vector2 prev = Vector2.zero; bool has = false;
                for (int i = 0; i <= n; i++)
                {
                    Vector2 q = TexPt(tr.Samples[i % n].Pos);
                    if (has)
                    {
                        int steps = Mathf.Max(1, Mathf.CeilToInt((q - prev).magnitude / 1.5f));
                        for (int s = 1; s <= steps; s++) Disc(px, Vector2.Lerp(prev, q, s / (float)steps), rad, col);
                    }
                    else Disc(px, q, rad, col);
                    prev = q; has = true;
                }
            }
            Disc(px, TexPt(tr.Samples[0].Pos), 5, new Color32(255, 77, 77, 255));   // 起終點
            mapTex = new Texture2D(MapPx, MapPx, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            mapTex.SetPixels32(px); mapTex.Apply(false, true);
        }

        Vector2 TexPt(Vector3 p)
        {
            var u = mapTrack.ToMap(p);
            return new Vector2(mapOx + u.x * mapBw * mapS, mapOy + u.y * mapBh * mapS);
        }

        static void Disc(Color32[] px, Vector2 c, int rad, Color32 col)
        {
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y);
            for (int y = -rad; y <= rad; y++)
                for (int x = -rad; x <= rad; x++)
                {
                    if (x * x + y * y > rad * rad) continue;
                    int X = cx + x, Y = cy + y; if (X < 0 || Y < 0 || X >= MapPx || Y >= MapPx) continue;
                    px[Y * MapPx + X] = col;
                }
        }

        Color KartColor(RaceKart k)
        {
            if (kartColors.TryGetValue(k, out var c)) return c;
            c = Color.white;
            var kv = k.GetComponentInChildren<KartVisual>();
            if (kv != null)
            {
                var body = kv.transform.Find("Body");
                var rend = body != null && body.childCount > 0 ? body.GetChild(0).GetComponent<Renderer>() : null;
                if (rend != null && rend.sharedMaterial != null) c = rend.sharedMaterial.color;
            }
            kartColors[k] = c; return c;
        }

        void DrawMinimap(float W, float H)
        {
            var tr = RaceSession.Track; if (tr == null) return;
            if (tr != mapTrack) BakeMap(tr);
            if (mapTex == null) return;
            float size = Mathf.Min(210f, 0.26f * H);
            var r = new Rect(W - 0.02f * W - size, 0.03f * H + 128f + 10f, size, size);
            Panel(r, false);
            var pad = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
            GUI.DrawTexture(pad, mapTex, ScaleMode.StretchToFill, true);
            foreach (var k in RaceSession.Karts)
            {
                if (k == null || k == RaceSession.Player) continue;
                var g = MapGui(pad, k.Position); var col = KartColor(k);
                Rounded(new Rect(g.x - 6f, g.y - 6f, 12f, 12f), new Color(0f, 0f, 0f, 0.7f), 6f);
                Rounded(new Rect(g.x - 4.5f, g.y - 4.5f, 9f, 9f), col, 4.5f);
            }
            var pl = RaceSession.Player;
            if (pl != null)
            {
                if (arrowTex == null) arrowTex = MakeArrow();
                var g = MapGui(pad, pl.Position); var f = pl.Forward;
                float ang = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, g);
                GUI.DrawTexture(new Rect(g.x - 11f, g.y - 11f, 22f, 22f), arrowTex);
                GUI.matrix = m;
            }
        }

        Vector2 MapGui(Rect pad, Vector3 p)
        {
            var t = TexPt(p);
            return new Vector2(pad.x + t.x / MapPx * pad.width, pad.yMax - t.y / MapPx * pad.height);
        }

        static Texture2D MakeArrow()
        {
            const int N = 32; var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float fx = (x + 0.5f) / N - 0.5f, fy = (y + 0.5f) / N;   // fy: 0 底 → 1 頂
                float half = (1f - fy) * 0.42f;                           // 頂端尖、底端寬
                bool inside = fy > 0.06f && fy < 0.94f && Mathf.Abs(fx) < half - 0.02f;
                bool edge = fy > 0.02f && fy < 0.98f && Mathf.Abs(fx) < half + 0.06f;
                px[y * N + x] = inside ? new Color32(255, 214, 63, 255) : edge ? new Color32(10, 14, 24, 255) : new Color32(0, 0, 0, 0);
            }
            t.SetPixels32(px); t.Apply(false, true); return t;
        }

        // ---- 完賽後(成績卡出現前)的中央大字
        void DrawFinishBanner(float W, float H)
        {
            float a = Mathf.Clamp01((Time.unscaledTime - finishedAt) / 0.25f);
            var c = RacePalette.Gold; c.a = a;
            Big(Z("完賽!", "FINISH!"), W / 2f, H * 0.32f, 120, c, Mathf.Lerp(1.4f, 1f, a));
        }

        // ---- 中央提示
        void DrawBanner(float W, float H)
        {
            if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toastMsg))
            {
                var tc = Color.white; tc.a = Mathf.Clamp01((toastUntil - Time.unscaledTime) / 0.4f);
                GUI.Label(new Rect(0f, H * 0.30f, W, 44f), toastMsg, Style(30, tc, TextAnchor.MiddleCenter, FontStyle.Bold));
            }
            if (Time.unscaledTime >= bannerUntil || string.IsNullOrEmpty(banner)) return;
            float left = bannerUntil - Time.unscaledTime;
            var c = Color.white; c.a = Mathf.Clamp01(left / 0.5f);
            GUI.Label(new Rect(0f, H * 0.22f - 30f, W, 60f), banner, Style(48, c, TextAnchor.MiddleCenter, FontStyle.Bold));
        }

        // ---- 終點成績卡
        void DrawResult(float W, float H)
        {
            int rows = Mathf.Clamp(RaceSession.LapTimes.Count, 1, 7);
            float ch = 330f + rows * 28f;
            var r = new Rect(W / 2f - 280f, H / 2f - ch / 2f, 560f, ch);
            if (Event.current.type == EventType.Repaint)
            {
                Panel(r, true);
                GUI.Label(new Rect(r.x, r.y + 14f, r.width, 50f), Z("完賽!", "FINISH!"), Style(40, RacePalette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold));
                GUI.Label(new Rect(r.x, r.y + 62f, r.width, 56f), RaceSession.FormatTime(RaceSession.FinishTime), Style(46, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold));
                int cnt = RaceSession.Karts.Count, pl = Mathf.Clamp(RaceSession.Place(RaceSession.Player), 1, Mathf.Max(1, cnt));
                string pt = cnt > 1 ? Z("第 " + pl + " 名 / " + cnt, pl + Ord(pl) + " / " + cnt) : "";
                bool rec = RaceSession.NewRecord, hasPt = pt.Length > 0;
                if (hasPt) GUI.Label(new Rect(r.x, r.y + 120f, rec ? r.width * 0.5f : r.width, 36f), pt, Style(28, PlaceColor(pl), rec ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter, FontStyle.Bold));
                if (rec) GUI.Label(new Rect(hasPt ? r.x + r.width * 0.5f + 16f : r.x, r.y + 120f, hasPt ? r.width * 0.5f - 16f : r.width, 36f), Z("新紀錄!", "NEW RECORD!"), Style(24, RacePalette.Go, hasPt ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, FontStyle.Bold));
                float best = float.MaxValue; foreach (var t in RaceSession.LapTimes) if (t > 0f && t < best) best = t;
                int n = RaceSession.LapTimes.Count, show = Mathf.Min(n, rows);
                GUI.Label(new Rect(r.x + 90f, r.y + 160f, 380f, 24f), Z("各圈時間", "LAP TIMES"), Style(15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft, FontStyle.Bold));
                for (int i = 0; i < show; i++)
                {
                    float t = RaceSession.LapTimes[i];
                    bool isBest = show > 1 && Mathf.Approximately(t, best);
                    var c = isBest ? RacePalette.Gold : new Color(1f, 1f, 1f, 0.85f);
                    float y = r.y + 186f + i * 28f;
                    GUI.Label(new Rect(r.x + 90f, y, 160f, 28f), Z("第 " + (i + 1) + " 圈", "Lap ") + (Loc.Lang == "en" ? (i + 1).ToString() : ""), Style(20, c, TextAnchor.MiddleLeft));
                    GUI.Label(new Rect(r.x + 250f, y, 220f, 28f), (isBest ? "* " : "") + RaceSession.FormatTime(t), Style(20, c, TextAnchor.MiddleRight, FontStyle.Bold));
                }
            }
            // 大獎賽:玩家完賽後要等其他人,這時按鈕是「看積分榜(進下一場)」而不是「再跑一次」
            // 2026-10-24 修:積分榜(Standings / Podium)狀態下按鈕也是「下一場」,不能退回「再跑一次」(會重跑第 1 場)
            var gpSt = RaceGrandPrix.State;
            bool gp = RaceGrandPrix.Active && (gpSt == GpState.WaitOthers || gpSt == GpState.Standings || gpSt == GpState.Podium);
            string gpLab = gpSt == GpState.Podium ? Z("頒獎 ▸ 結束", "Podium ▸ Finish") : gpSt == GpState.Standings ? Z("下一場 ▸", "Next race ▸") : Z("看積分榜 ▸ 下一場", "Standings ▸ Next race");
            bool again = GBtn(new Rect(r.x + 40f, r.yMax - 76f, 230f, 48f), gp ? gpLab : Z("再跑一次", "Race again"), true);
            bool menu = GBtn(new Rect(r.xMax - 270f, r.yMax - 76f, 230f, 48f), Z("回主選單", "Main menu"), false);
            if (Event.current.type == EventType.Layout) return;
            if (again && gp) { Event.current.Use(); if (RaceGrandPrix.State == GpState.WaitOthers) RaceGrandPrix.SkipWait(); else RaceGrandPrix.Continue(); }
            else if (again) { Event.current.Use(); RaceSession.Restart(); }
            else if (menu) { Event.current.Use(); MenuSystem.GoMain(); }
        }
    }
}
