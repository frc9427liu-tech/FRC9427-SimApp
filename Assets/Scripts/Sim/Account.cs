using System;
using UnityEngine;

namespace FrcSim
{
    // 玩家帳號資料(MOBA 風格個人頁用):暱稱/頭像色/等級經驗/段位積分/戰績/最近 5 場。
    // 存在目前玩家檔(Profile)底下,各玩家各自一份。Google 登入成功後只是把名稱填進來。
    public static class Account
    {
        public static readonly string[] Tiers = { "Iron", "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Master", "Challenger" };
        public static readonly string[] TiersZh = { "鐵牌", "銅牌", "銀牌", "金牌", "白金", "鑽石", "大師", "菁英" };
        public static readonly Color[] TierColor = {
            new Color(0.55f,0.55f,0.58f), new Color(0.80f,0.50f,0.28f), new Color(0.78f,0.82f,0.88f), new Color(1f,0.82f,0.30f),
            new Color(0.40f,0.85f,0.80f), new Color(0.45f,0.65f,1f), new Color(0.80f,0.50f,1f), new Color(1f,0.45f,0.45f) };
        static readonly Color[] AvatarColors = {
            new Color(0.25f,0.65f,1f), new Color(1f,0.55f,0.25f), new Color(0.40f,0.85f,0.50f), new Color(0.85f,0.45f,0.95f),
            new Color(1f,0.40f,0.50f), new Color(0.95f,0.85f,0.30f) };

        public static bool HasAccount { get { return Profile.Guest || Profile.GetInt("acctSet", 0) == 1; } }
        public static string Name { get { return Profile.GetString("acctName", Profile.CurrentName); } }
        public static string Email { get { return Profile.GetString("acctEmail", ""); } }
        public static bool IsGoogle { get { return Profile.GetInt("acctGoogle", 0) == 1; } }
        public static int AvatarIdx { get { return Mathf.Clamp(Profile.GetInt("acctAv", 0), 0, AvatarColors.Length - 1); } }
        public static Color AvatarColor { get { return AvatarColors[AvatarIdx]; } }
        public static void NextAvatar() { Profile.SetInt("acctAv", (AvatarIdx + 1) % AvatarColors.Length); }
        // ---- 自訂頭像:本機選圖 → 置中裁成正方形 → 256x256 → 存 persistentDataPath\avatars\<檔位>.png(完全離線)
        static Texture2D avTex; static int avTexIdx = -1, avTexVer = -1; static int avVer;
        public static string AvatarPath { get { return System.IO.Path.Combine(Application.persistentDataPath, SuperSample.IsTestRun() ? "avatars_test" : "avatars"); } }   // 測試執行寫到 avatars_test,不碰真正的頭像
        static string AvatarFile { get { return System.IO.Path.Combine(AvatarPath, Profile.Index + ".png"); } }
        static Texture2D guestTex;   // 訪客頭像只留在記憶體,不寫檔
        public static bool HasCustomAvatar { get { return Profile.Guest ? guestTex != null : System.IO.File.Exists(AvatarFile); } }
        public static Texture2D AvatarTex
        {
            get
            {
                if (Profile.Guest) return guestTex;
                if (!HasCustomAvatar) return null;
                if (avTex != null && avTexIdx == Profile.Index && avTexVer == avVer) return avTex;
                try
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(t, System.IO.File.ReadAllBytes(AvatarFile))) return null;
                    t.wrapMode = TextureWrapMode.Clamp; avTex = t; avTexIdx = Profile.Index; avTexVer = avVer; return t;
                }
                catch (Exception) { return null; }
            }
        }
        // 最近一次匯入的結果文字(給畫面上的提示用,中英依目前語言)
        public static string LastMsg = "";
        static bool Fail(string zh, string en, string why) { LastMsg = Loc.Lang == "zh" ? zh : en; Debug.LogWarning("[Avatar] FAIL " + why + " -> " + en); return false; }
        // 用檔頭判斷真正的格式(Discord 下載的 .png 常常其實是 WebP,副檔名不可信)
        static string Sniff(byte[] b)
        {
            if (b == null || b.Length < 12) return "bad";
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return "gif";
            if (b[0] == 'B' && b[1] == 'M') return "bmp";
            if ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 0x2A) || (b[0] == 0x4D && b[1] == 0x4D && b[3] == 0x2A)) return "tiff";
            return "?";
        }
        // Unity 只會讀 PNG/JPG;其他格式(WebP/GIF/BMP/TIFF)借 Windows 內建解碼器(WIC)轉成 PNG。轉不了(沒裝 WebP 解碼)就回 null。
        static byte[] ViaWic(string path)
        {
            try
            {
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frcsim_av_" + Guid.NewGuid().ToString("N") + ".png");
                string ps = "Add-Type -AssemblyName PresentationCore; $d=[Windows.Media.Imaging.BitmapDecoder]::Create([Uri]'" + path.Replace("'", "''") + "',[Windows.Media.Imaging.BitmapCreateOptions]::None,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad); $e=New-Object Windows.Media.Imaging.PngBitmapEncoder; $e.Frames.Add($d.Frames[0]); $f=[IO.File]::Create('" + tmp.Replace("'", "''") + "'); $e.Save($f); $f.Close()";
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps))) { CreateNoWindow = true, UseShellExecute = false };
                using (var p = System.Diagnostics.Process.Start(psi)) { if (!p.WaitForExit(20000)) { try { p.Kill(); } catch (Exception) { } return null; } }
                if (!System.IO.File.Exists(tmp)) return null;
                var r = System.IO.File.ReadAllBytes(tmp); try { System.IO.File.Delete(tmp); } catch (Exception) { } return r;
            }
            catch (Exception e) { Debug.LogWarning("[Avatar] WIC convert: " + e.Message); return null; }
        }
        public static bool ImportAvatar(string path)
        {
            LastMsg = "";
            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(path); }
            catch (Exception e) { return Fail("讀不到這個檔案", "Can't read that file", "read " + path + ": " + e.Message); }
            string kind = Sniff(bytes);
            Debug.Log("[Avatar] import " + path + " (" + bytes.Length + " bytes) detected=" + kind);
            if (kind == "webp" || kind == "gif" || kind == "bmp" || kind == "tiff")
            {
                var png = ViaWic(path);
                if (png == null) return Fail("這是 " + kind.ToUpper() + " 圖片,這台電腦轉不了。請用「小畫家」或「相片」另存成 PNG 或 JPG 再試", "That's a " + kind.ToUpper() + " image and this PC can't convert it. Re-save it as PNG or JPG and try again", "WIC failed for " + kind);
                bytes = png;
            }
            else if (kind != "png" && kind != "jpg") return Fail("這不是圖片檔(支援 PNG / JPG / WebP / GIF / BMP)", "Not an image file (PNG / JPG / WebP / GIF / BMP)", "unknown format");
            try
            {
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(src, bytes)) return Fail("圖片檔損毀,讀不出來", "The image is damaged and can't be read", "LoadImage false");
                int s = Mathf.Min(src.width, src.height), x0 = (src.width - s) / 2, y0 = (src.height - s) / 2;
                var rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
                src.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rt, new Vector2((float)s / src.width, (float)s / src.height), new Vector2((float)x0 / src.width, (float)y0 / src.height));
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var dst = new Texture2D(256, 256, TextureFormat.RGBA32, false); dst.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); dst.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.Destroy(src);
                if (Profile.Guest) { dst.wrapMode = TextureWrapMode.Clamp; guestTex = dst; avVer++; LastMsg = Loc.Lang == "zh" ? "頭像已更新(訪客:關閉遊戲後會消失)" : "Avatar updated (guest: gone after you quit)"; Debug.Log("[Avatar] OK (guest, memory only)"); return true; }
                System.IO.Directory.CreateDirectory(AvatarPath);
                System.IO.File.WriteAllBytes(AvatarFile, ImageConversion.EncodeToPNG(dst));
                UnityEngine.Object.Destroy(dst); avVer++;
                LastMsg = Loc.Lang == "zh" ? "頭像已更新" : "Avatar updated"; Debug.Log("[Avatar] OK saved " + AvatarFile);
                return true;
            }
            catch (Exception e) { return Fail("頭像處理失敗:" + e.Message, "Avatar failed: " + e.Message, "exception " + e); }
        }
        public static void ClearAvatar() { if (Profile.Guest) { guestTex = null; avVer++; return; } try { if (HasCustomAvatar) System.IO.File.Delete(AvatarFile); } catch (Exception) { } avVer++; }

        public static string Initial { get { string n = Name; return string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant(); } }

        // 創作者稱號:只有「真的 Google 登入、且 email 是開發者本人」才有(示範登入的 email 是 demo@local,拿不到)。
        // 測試截圖專用旗標 -creatorpreview 只在測試執行(-menu/-shot...)有效,不寫入任何存檔。
        public const string CreatorEmail = "frc9427liu@gmail.com";
        public static bool IsCreator
        {
            get
            {
                if (Profile.Guest) return false;
                if (SuperSample.IsTestRun() && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-creatorpreview") >= 0) return true;
                return IsGoogle && Email.Trim().ToLowerInvariant() == CreatorEmail;
            }
        }
        // ---- 個性簽名:最多 40 字、單行(換行/Tab 轉空白、去頭尾空白)。存在目前帳號底下;訪客走 Profile 的記憶體,不會存檔
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(c == '\r' || c == '\n' || c == '\t' ? ' ' : char.IsControl(c) ? '\0' : c);
            s = sb.ToString().Replace("\0", "").Trim();
            return s.Length > 40 ? s.Substring(0, 40).TrimEnd() : s;
        }
        public static string SigOverride;   // 只給測試截圖用(-sig "文字"):蓋掉顯示的簽名,不讀不寫存檔
        public static string Signature { get { return SigOverride != null ? Sanitize(SigOverride) : Sanitize(Profile.GetString("sig", "")); } }
        public static void SetSignature(string s) { if (SuperSample.IsTestRun() && !Profile.Guest) return; Profile.SetString("sig", Sanitize(s)); }   // 測試執行不寫真存檔(訪客只在記憶體,無所謂)

        // 訪客:資料只在記憶體(見 Profile.Guest),不存戰績/經驗/最佳分數/暱稱/頭像
        public static bool IsGuest { get { return Profile.Guest; } }
        public static string GuestNote(bool zh) { return zh ? "訪客 Guest — 不會儲存遊戲紀錄" : "Guest — progress is not saved"; }
        public static void SetGuest() { guestTex = null; avVer++; Profile.EnterGuest(); Profile.SetInt("acctSet", 1); Profile.SetInt("acctGoogle", 0); Profile.SetString("acctName", Profile.CurrentName); }
        public static void SetGoogle(string name, string email) { Profile.SetInt("acctSet", 1); Profile.SetInt("acctGoogle", 1); Profile.SetString("acctName", name); Profile.SetString("acctEmail", email ?? ""); EnsureUid(); }

        // ---- UID:每個 Google 帳號一組固定的 8 位數字(第一次登入時隨機產生並存起來,同 email 永遠同一組);訪客沒有
        // 舊版登入過的 Google 帳號(當時還沒有 UID)在第一次被讀到時補發,之後永遠同一組;測試執行不補發(不寫存檔)
        public static string Uid
        {
            get
            {
                if (Profile.Guest) return "";
                string u = Profile.GetString("acctUid", "");
                if (u == "" && IsGoogle && !SuperSample.IsTestRun()) { EnsureUid(); u = Profile.GetString("acctUid", ""); }
                return u;
            }
        }
        public static string UidText { get { return Profile.Guest ? "GUEST" : Uid == "" ? "—" : Uid; } }
        static void EnsureUid()
        {
            if (Profile.Guest || Profile.GetString("acctUid", "") != "") return;
            for (int tries = 0; tries < 50; tries++)
            {
                string u = UnityEngine.Random.Range(10000000, 100000000).ToString();
                bool used = false;
                for (int i = 0; i < Profile.Count; i++) if (Profile.GetStringOf(i, "acctUid", "") == u) { used = true; break; }
                if (!used) { Profile.SetString("acctUid", u); return; }
            }
        }
        public static void CopyUid() { if (Uid != "") GUIUtility.systemCopyBuffer = Uid; }

        // 啟動時:上次是 Google 登入就直接回到那個帳號(沒有保存 token,只記「哪個檔位」);上次是訪客或從沒登入 → 回登入畫面
        public static bool RestoreLast()
        {
            int s = Prefs.GetInt("lastGoogleSlot", -1);
            if (s < 0 || s >= Profile.Count) return false;
            Profile.ExitGuest(); Profile.Index = s;
            if (!IsGoogle) return false;
            avVer++; return true;
        }
        // Google 登入成功:用 email 找這台電腦上屬於這個帳號的檔位。找不到 = 沒玩過 → 開新檔位(isNew=true,要跑新手教學)。
        // 還沒被任何 email 認領、但有舊進度的檔位(舊版 0 號玩家)由第一個登入的帳號接手,不會讓舊存檔消失。
        public static void SignInGoogle(string name, string email, out bool isNew)
        {
            string em = (email ?? "").Trim().ToLowerInvariant();
            int slot = -1;
            for (int i = 0; i < Profile.Count; i++) if (Profile.GetStringOf(i, "acctEmail", "").Trim().ToLowerInvariant() == em && em != "") { slot = i; break; }
            if (slot < 0)
                for (int i = 0; i < Profile.Count; i++)
                    if (Profile.GetStringOf(i, "acctEmail", "") == "" && (Profile.GetIntOf(i, "tutSeen", 0) == 1 || Profile.GetIntOf(i, "acctW", 0) + Profile.GetIntOf(i, "acctL", 0) + Profile.GetIntOf(i, "acctD", 0) > 0)) { slot = i; break; }
            bool fresh = slot < 0;
            Profile.ExitGuest();
            if (fresh) Profile.AddNew(); else Profile.Index = slot;
            SetGoogle(name, email);
            Prefs.SetInt("lastGoogleSlot", Profile.Index); PlayerPrefs.Save();
            avVer++;
            isNew = fresh || (Profile.GetInt("tutSeen", 0) == 0 && Matches == 0);
        }
        public static void SignOut() { Prefs.SetInt("lastGoogleSlot", -1); PlayerPrefs.Save(); Profile.ExitGuest(); avVer++; }
        public static void SetName(string n) { if (!string.IsNullOrWhiteSpace(n)) Profile.SetString("acctName", n.Trim()); }

        // ---- 等級 / 經驗:第 L 級升下一級需要 200 + 60*L 點
        public static int Xp { get { return Profile.GetInt("acctXp", 0); } }
        public static int Need(int lvl) { return 200 + 60 * lvl; }
        public static void LevelInfo(out int lvl, out int into, out int need)
        {
            int xp = Xp; lvl = 1;
            while (xp >= Need(lvl) && lvl < 99) { xp -= Need(lvl); lvl++; }
            into = xp; need = Need(lvl);
        }
        public static int Level { get { int l, i, n; LevelInfo(out l, out i, out n); return l; } }

        // ---- 段位:積分 0~799,每 100 分一階,小段 IV~I 每 25 分
        public static int Rp { get { return Profile.GetInt("acctRp", 0); } }
        public static int TierIdx { get { return Mathf.Clamp(Rp / 100, 0, Tiers.Length - 1); } }
        public static string TierName(bool zh) { return (zh ? TiersZh : Tiers)[TierIdx]; }
        public static string Division { get { if (TierIdx >= 6) return ""; int d = 4 - (Rp % 100) / 25; return d == 4 ? "IV" : d == 3 ? "III" : d == 2 ? "II" : "I"; } }
        public static Color TierCol { get { return TierColor[TierIdx]; } }

        // ---- 戰績
        public static int Wins { get { return Profile.GetInt("acctW", 0); } }
        public static int Losses { get { return Profile.GetInt("acctL", 0); } }
        public static int Draws { get { return Profile.GetInt("acctD", 0); } }
        public static int Matches { get { return Wins + Losses + Draws; } }
        public static int BestScore { get { return Profile.GetInt("acctBest", 0); } }
        public static int Streak { get { return Profile.GetInt("acctStreak", 0); } }

        // 最近 5 場:字串 "W|藍分|紅分|難度;..."
        public static string[] Recent() { string s = Profile.GetString("acctRecent", ""); return s.Length == 0 ? new string[0] : s.Split(';'); }

        public static string[] AchievementNames(bool zh) { return zh
            ? new[] { "初次登場(打完 1 場)", "常勝軍(贏 5 場)", "三連勝", "百分得分(單場 ≥100)", "老將(20 場)", "晉升銀牌" }
            : new[] { "Debut (1 match)", "Winner (5 wins)", "3-win streak", "Century (score ≥100)", "Veteran (20 matches)", "Reach Silver" }; }
        public static bool[] Achievements() { return new[] { Matches >= 1, Wins >= 5, Profile.GetInt("acctMaxStreak", 0) >= 3, BestScore >= 100, Matches >= 20, TierIdx >= 2 }; }

        // 比賽結束時呼叫一次。vsAi 難度 0~3;回傳獲得的 XP 與積分(給結算畫面顯示)
        public static void RecordMatch(int blue, int red, bool vsAi, int aiLevel, out int gainXp, out int gainRp)
        {
            bool win = blue > red, lose = blue < red;
            float diff = vsAi ? 0.8f + 0.2f * Mathf.Clamp(aiLevel, 0, 3) : 1f;
            gainXp = Mathf.RoundToInt(40f + Mathf.Min(blue, 200) * 1.2f + (win ? 80f : lose ? 0f : 30f));
            gainRp = win ? Mathf.RoundToInt(22 * diff) : lose ? -Mathf.RoundToInt(14 / diff) : 0;
            Profile.SetInt("acctXp", Xp + gainXp);
            Profile.SetInt("acctRp", Mathf.Clamp(Rp + gainRp, 0, 799));
            if (win) { Profile.SetInt("acctW", Wins + 1); int st = Mathf.Max(0, Streak) + 1; Profile.SetInt("acctStreak", st); Profile.SetInt("acctMaxStreak", Mathf.Max(st, Profile.GetInt("acctMaxStreak", 0))); }
            else if (lose) { Profile.SetInt("acctL", Losses + 1); Profile.SetInt("acctStreak", 0); }
            else Profile.SetInt("acctD", Draws + 1);
            if (blue > BestScore) Profile.SetInt("acctBest", blue);
            var list = new System.Collections.Generic.List<string>(Recent());
            list.Insert(0, (win ? "W" : lose ? "L" : "D") + "|" + blue + "|" + red + "|" + (vsAi ? aiLevel.ToString() : "-"));
            if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
            Profile.SetString("acctRecent", string.Join(";", list.ToArray()));
        }
    }
}
