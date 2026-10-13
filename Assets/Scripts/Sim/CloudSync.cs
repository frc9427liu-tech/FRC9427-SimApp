using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace FrcSim
{
    public class CloudProfile
    {
        public string Uid, Name = "", Sig = "";
        public int Level, Rp;
        public string Status = "";   // menu / train / match(對方最後回報的狀態)
        public long Seen;            // 對方最後回報時間(Unix 秒);超過 150 秒沒更新視為離線
        public bool Online { get { return Seen > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Seen < 150; } }
        public bool Hidden, Loaded, NotFound, Busy;
        public Texture2D Tex;
    }

    // 雲端同步(Firebase Firestore REST + 匿名登入):玩家名片(暱稱/等級/段位/簽名/128px 頭貼)公開給其他玩家看。
    // 規則在 Firebase 後台:所有登入者可讀;只有建立者(owner = 匿名登入 uid)能改自己的文件;檢舉寫進 reports(只能新增、不能讀)。
    // 訪客與測試執行不上傳。上傳失敗只記 log,不影響遊戲。
    public class CloudSync : MonoBehaviour
    {
        const string ProjectId = "frc9427-sim-online";
        const string ApiKey = "AIzaSyDvDoDDBtcpt_GBZ0JrZs1L2-uhDYps5Zk";   // Firebase Web API Key 本來就是公開識別碼,安全靠 Firestore 規則
        static string Docs { get { return "https://firestore.googleapis.com/v1/projects/" + ProjectId + "/databases/(default)/documents"; } }

        public static string Status = "";     // 給設定/好友畫面顯示
        public static bool Enabled { get { return (!SuperSample.IsTestRun() || Array.IndexOf(Environment.GetCommandLineArgs(), "-cloudtest") >= 0) && Application.internetReachability != NetworkReachability.NotReachable; } }
        static CloudSync inst;
        static readonly Dictionary<string, CloudProfile> cache = new Dictionary<string, CloudProfile>();
        static string idToken, authUid; static float tokenExpire;
        string lastKey = ""; bool pushing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null) return;
            var go = new GameObject("CloudSync"); DontDestroyOnLoad(go); inst = go.AddComponent<CloudSync>();
        }

        void Start()
        {
            StartCoroutine(PushLoop()); StartCoroutine(PresenceLoop());
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cloudpushtest") >= 0) StartCoroutine(PushTestCo());   // 測試:直接寫一份假名片,驗證上傳路徑
        }

        IEnumerator PushTestCo() { yield return PushDoc("t", "00000001", "PushTest", 7, 250, "hello cloud", ""); yield return PushPresence("00000001", "train"); }

        // ---------------------------------------------------------------- 登入(匿名,token 一小時過期自動換)
        IEnumerator EnsureAuth(Action<bool> done)
        {
            if (idToken != null && Time.realtimeSinceStartup < tokenExpire) { done(true); yield break; }
            string refresh = Prefs.GetString("fbRefresh", "");
            UnityWebRequest rq;
            if (refresh != "")
            {
                var form = new WWWForm(); form.AddField("grant_type", "refresh_token"); form.AddField("refresh_token", refresh);
                rq = UnityWebRequest.Post("https://securetoken.googleapis.com/v1/token?key=" + ApiKey, form);
            }
            else
            {
                rq = new UnityWebRequest("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + ApiKey, "POST");
                rq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"returnSecureToken\":true}")); rq.downloadHandler = new DownloadHandlerBuffer();
                rq.SetRequestHeader("Content-Type", "application/json");
            }
            rq.timeout = 15;
            yield return rq.SendWebRequest();
            if (rq.result != UnityWebRequest.Result.Success) { Status = "auth failed: " + rq.error; Debug.LogWarning("[Cloud] " + Status + " " + rq.downloadHandler.text); rq.Dispose(); done(false); yield break; }
            string t = rq.downloadHandler.text; rq.Dispose();
            idToken = Pick(t, "idToken") ?? Pick(t, "id_token"); string rt = Pick(t, "refreshToken") ?? Pick(t, "refresh_token"); authUid = Pick(t, "localId") ?? Pick(t, "user_id");
            if (idToken == null || authUid == null) { Status = "auth parse failed"; done(false); yield break; }
            if (rt != null) { Prefs.SetString("fbRefresh", rt); PlayerPrefs.Save(); }
            tokenExpire = Time.realtimeSinceStartup + 3000f;
            done(true);
        }
        static string Pick(string json, string key) { var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\""); return m.Success ? m.Groups[1].Value : null; }

        // ---------------------------------------------------------------- 工具:Firestore 欄位
        static string Esc(string s) { var sb = new StringBuilder(); foreach (char c in s ?? "") { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c < 32) sb.Append(' '); else sb.Append(c); } return sb.ToString(); }
        static string FStr(string k, string v) { return "\"" + k + "\":{\"stringValue\":\"" + Esc(v) + "\"}"; }
        static string FInt(string k, long v) { return "\"" + k + "\":{\"integerValue\":\"" + v + "\"}"; }
        static string FBool(string k, bool v) { return "\"" + k + "\":{\"booleanValue\":" + (v ? "true" : "false") + "}"; }
        static string GStr(string json, string key) { var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""); if (!m.Success) return ""; try { return Regex.Unescape(m.Groups[1].Value.Replace("\\/", "/")); } catch (Exception) { return m.Groups[1].Value; } }
        static int GInt(string json, string key) { var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\{\\s*\"integerValue\"\\s*:\\s*\"(-?\\d+)\""); int v; return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : 0; }
        static bool GBool(string json, string key) { return Regex.IsMatch(json, "\"" + key + "\"\\s*:\\s*\\{\\s*\"booleanValue\"\\s*:\\s*true"); }

        // 目前頭像縮成 128x128 JPG → base64(約 5~10KB,規則上限 30000 字元)
        static string AvatarB64()
        {
            try
            {
                var src = Account.AvatarTex; if (src == null) return "";
                var rt = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t = new Texture2D(128, 128, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); t.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                string b = Convert.ToBase64String(ImageConversion.EncodeToJPG(t, 75)); Destroy(t);
                return b.Length < 28000 ? b : "";
            }
            catch (Exception e) { Debug.LogWarning("[Cloud] avatar encode: " + e.Message); return ""; }
        }

        // ---------------------------------------------------------------- 自己的名片:有變動就上傳
        IEnumerator PushLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(8f);
                if (pushing || !Enabled || SuperSample.IsTestRun() || Account.IsGuest || !Account.HasAccount || Account.Uid == "" || Account.Uid == null) continue;
                var tex = Account.AvatarTex;
                string key = Account.Uid + "|" + Account.Name + "|" + Account.Level + "|" + Account.Rp + "|" + Account.Signature + "|" + (tex != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(tex) : 0);
                if (key == lastKey) continue;
                pushing = true;
                yield return PushNow(key);
                pushing = false;
                yield return new WaitForSecondsRealtime(20f);
            }
        }

        // ---------------------------------------------------------------- 上線狀態:每 60 秒寫一次(只更新 status/seen 兩個欄位,省額度)
        public static string MyStatus() { return !GameSession.Active ? "menu" : (GameSession.PracticeMode || GameSession.Drill || GameSession.Rookie) ? "train" : "match"; }
        IEnumerator PresenceLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(60f);
                if (!Enabled || SuperSample.IsTestRun() || Account.IsGuest || !Account.HasAccount || string.IsNullOrEmpty(Account.Uid) || lastKey == "") continue;   // lastKey 空 = 名片還沒成功上傳過
                yield return PushPresence(Account.Uid, MyStatus());
            }
        }
        IEnumerator PushPresence(string uid, string status)
        {
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) yield break;
            string json = "{\"fields\":{" + FStr("status", status) + "," + FInt("seen", DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + "}}";
            var w = new UnityWebRequest(Docs + "/users/" + uid + "?updateMask.fieldPaths=status&updateMask.fieldPaths=seen", "PATCH");
            w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); w.downloadHandler = new DownloadHandlerBuffer();
            w.SetRequestHeader("Content-Type", "application/json"); w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 15;
            yield return w.SendWebRequest();
            if (w.result == UnityWebRequest.Result.Success) Debug.Log("[Cloud] presence " + status);
            else Debug.LogWarning("[Cloud] presence failed " + w.responseCode + " " + w.downloadHandler.text);
            w.Dispose();
        }

        IEnumerator PushNow(string key) { return PushDoc(key, Account.Uid, Account.Name, Account.Level, Account.Rp, Account.Signature, AvatarB64()); }

        IEnumerator PushDoc(string key, string uid, string name, int level, int rp, string sig, string av)
        {
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) yield break;
            string url = Docs + "/users/" + uid;
            // 先看這個 UID 的文件在不在、是不是自己的
            var g = UnityWebRequest.Get(url); g.SetRequestHeader("Authorization", "Bearer " + idToken); g.timeout = 15;
            yield return g.SendWebRequest();
            bool exists = g.result == UnityWebRequest.Result.Success; long code = g.responseCode; string body = g.downloadHandler.text; g.Dispose();
            if (!exists && code != 404) { Status = "read failed " + code; Debug.LogWarning("[Cloud] " + Status + " " + body); yield break; }
            if (exists && GStr(body, "owner") != authUid) { Status = "UID owned by another device"; Debug.LogWarning("[Cloud] " + Status); lastKey = key; yield break; }
            var f = new StringBuilder("{\"fields\":{");
            f.Append(FStr("owner", authUid)).Append(',').Append(FStr("name", name)).Append(',').Append(FInt("level", level)).Append(',').Append(FInt("rp", rp)).Append(',')
             .Append(FStr("sig", sig)).Append(',').Append(FStr("avatar", av)).Append(',').Append(FInt("updated", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            string mask = "";
            if (!exists) f.Append(',').Append(FBool("hidden", false));
            else foreach (var p in new[] { "owner", "name", "level", "rp", "sig", "avatar", "updated" }) mask += (mask == "" ? "?" : "&") + "updateMask.fieldPaths=" + p;
            f.Append("}}");
            var w = new UnityWebRequest(url + mask, "PATCH");
            w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(f.ToString())); w.downloadHandler = new DownloadHandlerBuffer();
            w.SetRequestHeader("Content-Type", "application/json"); w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 20;
            yield return w.SendWebRequest();
            if (w.result == UnityWebRequest.Result.Success) { lastKey = key; Status = "synced"; Debug.Log("[Cloud] profile synced uid=" + uid + " avatar=" + (av.Length / 1024) + "KB"); }
            else { Status = "write failed " + w.responseCode; Debug.LogWarning("[Cloud] " + Status + " " + w.downloadHandler.text); }
            w.Dispose();
        }

        // ---------------------------------------------------------------- 讀別人的名片(好友)
        public static CloudProfile Get(string uid) { CloudProfile p; return cache.TryGetValue(uid, out p) ? p : null; }
        public static void Fetch(string uid, bool force = false)
        {
            if (inst == null || !Enabled || string.IsNullOrEmpty(uid)) { Debug.Log("[Cloud] Fetch skipped inst=" + (inst != null) + " enabled=" + Enabled); return; }
            CloudProfile p; if (!cache.TryGetValue(uid, out p)) { p = new CloudProfile { Uid = uid }; cache[uid] = p; }
            if (p.Busy || (p.Loaded && !force)) return;
            p.Busy = true; inst.StartCoroutine(inst.FetchCo(p));
        }
        IEnumerator FetchCo(CloudProfile p)
        {
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) { p.Busy = false; yield break; }
            var g = UnityWebRequest.Get(Docs + "/users/" + p.Uid); g.SetRequestHeader("Authorization", "Bearer " + idToken); g.timeout = 15;
            yield return g.SendWebRequest();
            Debug.Log("[Cloud] fetch " + p.Uid + " -> " + g.result + " " + g.responseCode);
            if (g.result == UnityWebRequest.Result.Success)
            {
                string b = g.downloadHandler.text;
                p.Status = GStr(b, "status"); p.Seen = GInt(b, "seen");
                p.Name = GStr(b, "name"); p.Sig = GStr(b, "sig"); p.Level = GInt(b, "level"); p.Rp = GInt(b, "rp"); p.Hidden = GBool(b, "hidden"); p.NotFound = false;
                string av = GStr(b, "avatar");
                if (!p.Hidden && av != "") { try { var t = new Texture2D(2, 2, TextureFormat.RGB24, false); if (ImageConversion.LoadImage(t, Convert.FromBase64String(av))) { t.wrapMode = TextureWrapMode.Clamp; p.Tex = t; } } catch (Exception) { } }
                else p.Tex = null;
                p.Loaded = true;
            }
            else if (g.responseCode == 404) { p.NotFound = true; p.Loaded = true; }
            g.Dispose(); p.Busy = false;
        }

        // ---------------------------------------------------------------- 好友:先申請、對方同意才成為好友
        // 文件 friendreqs/{對方uid}_{申請人uid},status = pending / accepted。規則:申請人才能建立;只有被申請人能把 pending 改成 accepted;任一方都能刪除(解除好友/拒絕)。
        public static List<string> Incoming = new List<string>();   // 想加我、還沒處理的人的 UID
        public static List<string> Friends = new List<string>();    // 已成為好友的 UID
        public static int FriendsVer;                               // 每次同步完成 +1(畫面用來知道要重畫)
        public static bool FriendsSynced; static float lastSync = -100f;
        static string Mine { get { return Account.Uid ?? ""; } }

        public static void SyncFriends()
        {
            if (inst == null || !Enabled || Account.IsGuest || Mine == "") return;
            if (Time.realtimeSinceStartup - lastSync < 12f) return;   // 剛同步過就不再同步(避免畫面重畫 ↔ 同步 無限循環)
            lastSync = Time.realtimeSinceStartup;
            inst.StartCoroutine(inst.SyncCo());
        }
        IEnumerator Query(string field, string uid, List<string[]> outDocs)
        {   // outDocs: {docId, status}
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) yield break;
            string q = "{\"structuredQuery\":{\"from\":[{\"collectionId\":\"friendreqs\"}],\"where\":{\"fieldFilter\":{\"field\":{\"fieldPath\":\"" + field + "\"},\"op\":\"EQUAL\",\"value\":{\"stringValue\":\"" + uid + "\"}}}}}";
            var w = new UnityWebRequest(Docs + ":runQuery", "POST");
            w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(q)); w.downloadHandler = new DownloadHandlerBuffer();
            w.SetRequestHeader("Content-Type", "application/json"); w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 15;
            yield return w.SendWebRequest();
            if (w.result == UnityWebRequest.Result.Success)
            {
                foreach (var chunk in w.downloadHandler.text.Split(new[] { "\"document\":" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var m = Regex.Match(chunk, "friendreqs/(\\d{8})_(\\d{8})\"");
                    if (m.Success) outDocs.Add(new[] { m.Groups[1].Value + "_" + m.Groups[2].Value, GStr(chunk, "status") });
                }
            }
            else Debug.LogWarning("[Cloud] query " + field + " failed " + w.responseCode + " " + w.downloadHandler.text);
            w.Dispose();
        }
        IEnumerator SyncCo()
        {
            var toMe = new List<string[]>(); var fromMe = new List<string[]>();
            yield return Query("to", Mine, toMe); yield return Query("from", Mine, fromMe);
            var inc = new List<string>(); var fr = new List<string>();
            foreach (var d in toMe) { string from = d[0].Substring(9); if (d[1] == "accepted") { if (!fr.Contains(from)) fr.Add(from); } else if (d[1] == "pending") inc.Add(from); }
            foreach (var d in fromMe) { string to = d[0].Substring(0, 8); if (d[1] == "accepted" && !fr.Contains(to)) fr.Add(to); }
            Incoming = inc; Friends = fr; FriendsSynced = true; FriendsVer++;
            foreach (var u in inc) Fetch(u); foreach (var u in fr) Fetch(u);
        }
        public static void SendRequest(string toUid, Action<string> done)
        {   // done(null) = 成功;否則錯誤文字
            if (inst == null || !Enabled) { done(Loc.Lang == "zh" ? "沒有網路,無法送出申請" : "Offline — can't send a request"); return; }
            inst.StartCoroutine(inst.SendReqCo(toUid, done));
        }
        IEnumerator SendReqCo(string to, Action<string> done)
        {
            bool zh = Loc.Lang == "zh";
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) { done(zh ? "連不上雲端" : "Can't reach the cloud"); yield break; }
            var g = UnityWebRequest.Get(Docs + "/users/" + to); g.SetRequestHeader("Authorization", "Bearer " + idToken); g.timeout = 15;
            yield return g.SendWebRequest(); long code = g.responseCode; bool ex = g.result == UnityWebRequest.Result.Success; g.Dispose();
            if (!ex) { done(code == 404 ? (zh ? "找不到這個 UID(對方還沒用新版登入過?)" : "UID not found (they haven't signed in on a cloud version?)") : (zh ? "連不上雲端" : "Can't reach the cloud")); yield break; }
            var f = new List<string>(); yield return Query("to", Mine, new List<string[]>());   // 暖身(確保已登入)
            if (Friends.Contains(to)) { done(zh ? "已經是好友了" : "Already friends"); yield break; }
            string json = "{\"fields\":{" + FStr("from", Mine) + "," + FStr("to", to) + "," + FStr("status", "pending") + "," + FInt("ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + "}}";
            var w = new UnityWebRequest(Docs + "/friendreqs?documentId=" + to + "_" + Mine, "POST");
            w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); w.downloadHandler = new DownloadHandlerBuffer();
            w.SetRequestHeader("Content-Type", "application/json"); w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 15;
            yield return w.SendWebRequest();
            if (w.result == UnityWebRequest.Result.Success) done(null);
            else if (w.responseCode == 409) done(zh ? "已經申請過了,等對方同意" : "Request already sent — waiting for them");
            else { Debug.LogWarning("[Cloud] request failed " + w.responseCode + " " + w.downloadHandler.text); done(zh ? "送出失敗(" + w.responseCode + ")" : "Failed (" + w.responseCode + ")"); }
            w.Dispose();
        }
        public static void Respond(string fromUid, bool accept, Action done)
        {
            if (inst == null || !Enabled) { if (done != null) done(); return; }
            inst.StartCoroutine(inst.RespondCo(fromUid, accept, done));
        }
        IEnumerator RespondCo(string from, bool accept, Action done)
        {
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) { if (done != null) done(); yield break; }
            string url = Docs + "/friendreqs/" + Mine + "_" + from;
            UnityWebRequest w;
            if (accept)
            {
                w = new UnityWebRequest(url + "?updateMask.fieldPaths=status", "PATCH");
                w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"fields\":{" + FStr("status", "accepted") + "}}"));
                w.SetRequestHeader("Content-Type", "application/json");
            }
            else w = new UnityWebRequest(url, "DELETE");
            w.downloadHandler = new DownloadHandlerBuffer(); w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 15;
            yield return w.SendWebRequest();
            if (w.result != UnityWebRequest.Result.Success) Debug.LogWarning("[Cloud] respond failed " + w.responseCode + " " + w.downloadHandler.text);
            w.Dispose();
            yield return SyncCo(); if (done != null) done();
        }
        public static void RemoveFriend(string uid, Action done)
        {
            if (inst == null || !Enabled) { if (done != null) done(); return; }
            inst.StartCoroutine(inst.RemoveCo(uid, done));
        }
        IEnumerator RemoveCo(string uid, Action done)
        {
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) { if (done != null) done(); yield break; }
            foreach (var id in new[] { Mine + "_" + uid, uid + "_" + Mine })   // 兩個方向都試刪(只會有一個存在)
            {
                var w = new UnityWebRequest(Docs + "/friendreqs/" + id, "DELETE"); w.downloadHandler = new DownloadHandlerBuffer();
                w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 15; yield return w.SendWebRequest(); w.Dispose();
            }
            yield return SyncCo(); if (done != null) done();
        }

        // ---------------------------------------------------------------- 檢舉(進待審清單,由開發者審查後可隱藏頭貼)
        public static void Report(string targetUid, string reason, Action<bool> done)
        {
            if (inst == null || !Enabled) { if (done != null) done(false); return; }
            inst.StartCoroutine(inst.ReportCo(targetUid, reason, done));
        }
        IEnumerator ReportCo(string target, string reason, Action<bool> done)
        {
            bool ok = false; yield return EnsureAuth(r => ok = r); if (!ok) { if (done != null) done(false); yield break; }
            string json = "{\"fields\":{" + FStr("target", target) + "," + FStr("reporter", authUid) + "," + FStr("reason", reason ?? "") + "," + FStr("from", Account.Uid ?? "") + "," + FInt("ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + "}}";
            var w = new UnityWebRequest(Docs + "/reports", "POST");
            w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); w.downloadHandler = new DownloadHandlerBuffer();
            w.SetRequestHeader("Content-Type", "application/json"); w.SetRequestHeader("Authorization", "Bearer " + idToken); w.timeout = 15;
            yield return w.SendWebRequest();
            bool good = w.result == UnityWebRequest.Result.Success; if (!good) Debug.LogWarning("[Cloud] report failed " + w.responseCode + " " + w.downloadHandler.text);
            w.Dispose(); if (done != null) done(good);
        }
    }
}
