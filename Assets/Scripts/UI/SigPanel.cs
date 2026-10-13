using System;
using UnityEngine;
using UnityEngine.UI;

namespace FrcSim
{
    // 個性簽名面板(玩家檔案左欄,排名卡上方):淡玻璃底 + 細邊框 + 引號;點一下行內編輯(最多 40 字)。
    // 手把:A(從按鈕)開始編輯 → LB/RB 輪流換預設句、A 儲存、B 取消。訪客可改但只在記憶體(Profile.Guest 會擋寫檔)。
    public class SigPanel : MonoBehaviour
    {
        public static bool Editing;           // 編輯中:MenuSystem 不處理方向/點擊,避免打字被當成選單操作
        public static SigPanel Current;
        public static string[] Presets { get { return Loc.Lang == "zh" ? new[] { "先調 PID,再談人生。", "機器人會失敗,但我不會放棄。", "今天也要把球投進 HUB!", "今天的 bug,明天的經驗值。", "FRC 9427,出發!" } : new[] { "Tune the PID first, then life.", "Robots fail, but I won't give up.", "Another day, another ball into the HUB!", "Today's bug, tomorrow's XP.", "FRC 9427, let's go!" }; } }
        static string lastRevealed = null;    // 這個程式執行期間已經跑過打字機動畫的文字(沒變就直接顯示)
        static bool guestHinted;

        RectTransform box; Image tint, ring, topHl, shine; Text quoteO, quoteC, label, hint, counter, star;
        bool editable, creator, hover;
        float hoverT, shakeT, revealStart = -1f, caretT, shineT; int presetIdx = -1;
        string shown = "", buf = ""; bool selected; Action onChanged;
        public bool IsEditing { get { return editing; } }
        bool editing; int beginFrame = -1;
        Color gold = new Color(0.961f, 0.769f, 0.318f);   // #F5C451

        static int Units(string s) { int n = 0; foreach (char c in s) n += c > 0x2E80 ? 2 : 1; return n; }
        public static float HeightFor(string s) { return Units(s) > 34 ? 76f : 56f; }

        public static SigPanel Make(RectTransform content, float x, float y, float w, bool editable, bool creator, Action onChanged)
        {
            var box = UiKit.Rect("Sig", content); UiKit.Stretch(box);
            var sp = box.gameObject.AddComponent<SigPanel>();
            sp.box = box; sp.editable = editable; sp.creator = creator; sp.onChanged = onChanged;
            sp.Build(x, y, w);
            return sp;
        }

        void Build(float x, float y, float w)
        {
            string txt = Account.Signature; float h = HeightFor(txt);
            Color accent = creator ? gold : UiTheme.Accent;
            tint = UiKit.Glass("SigTint", box, new Color(1, 1, 1, 0.06f), 28f); UiKit.PlaceTL(tint.rectTransform, x, y, w, h);
            tint.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            shine = UiKit.Img("SigShine", tint.transform, creator ? new Color(gold.r, gold.g, gold.b, 0.16f) : new Color(1, 1, 1, 0.12f)); UiKit.PlaceTL(shine.rectTransform, -80, -20, 36, h + 40); shine.rectTransform.localRotation = Quaternion.Euler(0, 0, 20);
            ring = UiKit.Glass("SigRing", box, creator ? new Color(gold.r, gold.g, gold.b, 0.45f) : new Color(1, 1, 1, 0.18f), 28f, true); UiKit.PlaceTL(ring.rectTransform, x, y, w, h);
            topHl = UiKit.Img("SigHl", box, new Color(1, 1, 1, 0.30f)); UiKit.PlaceTL(topHl.rectTransform, x + 26, y + 2, w - 52, 1.5f);
            var qc = new Color(accent.r, accent.g, accent.b, 0.60f);
            quoteO = UiKit.Label("Qo", box, "“", 40, qc, TextAnchor.UpperLeft); quoteO.fontStyle = FontStyle.BoldAndItalic; UiKit.PlaceTL(quoteO.rectTransform, x + 12, y - 2, 40, 44);
            quoteC = UiKit.Label("Qc", box, "”", 40, qc, TextAnchor.LowerRight); quoteC.fontStyle = FontStyle.BoldAndItalic; UiKit.PlaceTL(quoteC.rectTransform, x + w - 52, y + h - 40, 40, 44);
            if (creator) { star = UiKit.Label("Star", box, "✦", 22, gold, TextAnchor.UpperLeft); UiKit.PlaceTL(star.rectTransform, x + 40, y + 4, 30, 30); }
            label = UiKit.Label("SigText", box, "", 20, new Color(0.91f, 0.933f, 0.969f, 0.90f), TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; label.lineSpacing = 1.15f; UiKit.PlaceTL(label.rectTransform, x + 30, y + 8, w - 60, h - 16);
            hint = UiKit.Label("SigHint", box, Loc.Lang == "zh" ? "點擊這裡,寫下你的個性簽名…" : "Click to write your signature…", 20, new Color(1, 1, 1, 0.40f), TextAnchor.MiddleCenter); hint.fontStyle = FontStyle.Italic; UiKit.PlaceTL(hint.rectTransform, x + 16, y + 8, w - 32, h - 16);
            counter = UiKit.Label("SigCnt", box, "", 16, new Color(1, 1, 1, 0.6f), TextAnchor.LowerRight); UiKit.PlaceTL(counter.rectTransform, x + w - 120, y + h - 26, 100, 22);
            shown = txt;
            bool reduce = Prefs.GetInt("reduceMotion", 0) == 1;
            if (txt != "" && txt != lastRevealed && !reduce) revealStart = Time.unscaledTime; else revealStart = -1f;
            lastRevealed = txt;
            Current = this; Render();
            if (editable) { var m = FindMenu(); if (m != null) m.RegisterSigClick(tint.rectTransform); }
        }
        static MenuSystem FindMenu() { return MenuSystem.I; }

        string Display()
        {
            if (editing) { string c = (Input.compositionString ?? ""); return buf + c + ((Time.unscaledTime - caretT < 0.6f || ((int)((Time.unscaledTime - caretT) * 2f)) % 2 == 0) ? "|" : " "); }
            if (revealStart >= 0f) { int n = Mathf.Clamp((int)((Time.unscaledTime - revealStart) / 0.030f), 0, shown.Length); if (n >= shown.Length) revealStart = -1f; return shown.Substring(0, n); }
            return shown;
        }
        void Render()
        {
            string d = Display();
            bool empty = !editing && shown == "";
            label.text = d; hint.gameObject.SetActive(empty);
            quoteO.gameObject.SetActive(!empty); quoteC.gameObject.SetActive(!empty);
            label.color = editing && selected ? new Color(0.45f, 0.75f, 1f, 1f) : new Color(0.91f, 0.933f, 0.969f, 0.90f);   // 全選時整段上色(像反白)
            counter.gameObject.SetActive(editing);
            if (editing) { int n = buf.Length; counter.text = n + "/40"; counter.color = n >= 40 ? new Color(1f, 0.4f, 0.4f) : n >= 36 ? new Color(1f, 0.75f, 0.25f) : new Color(1, 1, 1, 0.6f); }
        }

        public void Begin()
        {
            if (!editable || editing) return;
            editing = true; Editing = true; beginFrame = Time.frameCount; buf = shown; selected = buf.Length > 0; caretT = Time.unscaledTime; presetIdx = -1;
            Input.imeCompositionMode = IMECompositionMode.On;
            if (Profile.Guest && !guestHinted) { guestHinted = true; if (MenuSystem.I != null) MenuSystem.I.Toast(Loc.Lang == "zh" ? "訪客簽名不會儲存" : "Guest signatures aren't saved", true); }
            Render();
        }
        public void DebugEdit(string t) { Begin(); buf = Account.Sanitize(t); selected = false; beginFrame = -2; Render(); }   // 測試截圖用
        void Commit() { string s = Account.Sanitize(buf); Account.SetSignature(s); shown = s; End(); if (onChanged != null) onChanged(); }
        void Cancel() { End(); }
        void End() { editing = false; Editing = false; selected = false; Input.imeCompositionMode = IMECompositionMode.Auto; revealStart = -1f; Render(); }
        void OnDisable() { if (editing) Commit(); }
        void OnDestroy() { if (Current == this) { Current = null; if (editing) { Editing = false; Input.imeCompositionMode = IMECompositionMode.Auto; } } }

        void Block() { shakeT = 0.25f; }
        void Type(string s)
        {
            foreach (char c in s)
            {
                if (c == '\b') { if (selected) { buf = ""; selected = false; } else if (buf.Length > 0) buf = buf.Substring(0, buf.Length - 1); continue; }
                if (c == '\n' || c == '\r' || c == '\t' || char.IsControl(c)) continue;
                if (selected) { buf = ""; selected = false; }
                if (buf.Length >= 40) { Block(); continue; }
                buf += c;
            }
            caretT = Time.unscaledTime;
        }

        void Update()
        {
            // 懸停:底色 6%→10%、邊框 18%→28%,150ms
            bool over = RectTransformUtility.RectangleContainsScreenPoint(tint.rectTransform, Input.mousePosition, null);
            hoverT = Mathf.MoveTowards(hoverT, (over && editable && !Editing) || editing ? 1f : 0f, Time.unscaledDeltaTime / 0.15f);
            tint.color = new Color(1, 1, 1, Mathf.Lerp(0.06f, 0.10f, hoverT));
            float ra = Mathf.Lerp(0.18f, 0.28f, hoverT);
            Color rc = creator ? new Color(gold.r, gold.g, gold.b, Mathf.Lerp(0.45f, 0.6f, hoverT)) : new Color(1, 1, 1, ra);
            ring.color = editing ? new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 1f) : rc;   // 編輯中:強調色 2px 焦點環
            // 掃光:每 6 秒一次、持續 1.2 秒;編輯中關閉;減少動態 = 關閉
            bool reduce = Prefs.GetInt("reduceMotion", 0) == 1;
            shineT += Time.unscaledDeltaTime; if (shineT > 6f) shineT -= 6f;
            if (!editing && !reduce && shown != "" && shineT < 1.2f) { shine.gameObject.SetActive(true); var ap = shine.rectTransform.anchoredPosition; ap.x = Mathf.Lerp(-80f, tint.rectTransform.sizeDelta.x + 40f, shineT / 1.2f); shine.rectTransform.anchoredPosition = ap; }
            else shine.gameObject.SetActive(false);
            // 抖動(超過 40 字時)
            if (shakeT > 0f) { shakeT -= Time.unscaledDeltaTime; box.anchoredPosition = new Vector2(Mathf.Sin(shakeT * 70f) * 5f * Mathf.Clamp01(shakeT / 0.25f), 0); } else if (box.anchoredPosition.x != 0f) box.anchoredPosition = Vector2.zero;

            if (editing && Time.frameCount != beginFrame)   // 開始編輯的那一幀不處理(按 Enter/A 開始時不要馬上被當成「儲存」)
            {
                Type(Input.inputString);
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V)) Type(GUIUtility.systemCopyBuffer ?? "");
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Pad.Down(Pad.A)) { Commit(); return; }
                if (Input.GetKeyDown(KeyCode.Escape) || Pad.Down(Pad.B)) { Cancel(); return; }
                if (Pad.Down(Pad.RB)) { var pr = Presets; presetIdx = (presetIdx + 1) % pr.Length; buf = pr[presetIdx]; selected = false; }
                if (Pad.Down(Pad.LB)) { var pr = Presets; presetIdx = (presetIdx - 1 + pr.Length) % pr.Length; buf = pr[presetIdx]; selected = false; }
                if (Input.GetMouseButtonDown(0) && !over) { Commit(); return; }   // 點外面 = 儲存
            }
            Render();
        }
    }
}
