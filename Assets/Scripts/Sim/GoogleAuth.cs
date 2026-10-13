using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace FrcSim
{
    // Google 桌面 OAuth(loopback + PKCE):開系統瀏覽器 → 使用者同意 → 瀏覽器導回 http://127.0.0.1:埠 → 換 token → 讀名稱/信箱。
    // 本機監聽只在登入期間存在(收到一次回呼或逾時/取消就關掉),不常駐。token 不存檔,只取身分資料。
    // Client ID 來源:%persistentDataPath%\google-oauth-client.txt(第 1 行 client id,第 2 行 client secret,可省略),或 PlayerPrefs "googleClientId"。
    // 沒有 Client ID → 走「示範登入」,介面會明確標示 DEMO,不會假裝成真的 Google 登入。
    public static class GoogleAuth
    {
        public enum State { Idle, Waiting, Exchanging, Done, Failed }
        public static State Status = State.Idle;
        public static string Message = "";
        public static string Name = "", Email = "";
        public static bool Demo;

        static volatile string codeResult, errResult;
        static volatile bool cancel;
        static TcpListener listener;

        public static string ConfigPath => Path.Combine(Application.persistentDataPath, "google-oauth-client.txt");
        public static bool Configured { get { return !string.IsNullOrEmpty(ClientId()); } }
        static string ClientId()
        {
            try { if (File.Exists(ConfigPath)) { var ls = File.ReadAllLines(ConfigPath); if (ls.Length > 0 && ls[0].Trim().Length > 10) return ls[0].Trim(); } } catch { }
            string p = Prefs.GetString("googleClientId", "");
            return p != "" ? p : Built(0);
        }
        static string ClientSecret()
        {
            try { if (File.Exists(ConfigPath)) { var ls = File.ReadAllLines(ConfigPath); if (ls.Length > 1) return ls[1].Trim(); } } catch { }
            string p = Prefs.GetString("googleClientSecret", "");
            return p != "" ? p : Built(1);
        }
        // 內建於打包版:Assets/Resources/google-client.txt(兩行:id、secret)。桌面版 OAuth 密鑰依 Google 規定不算機密,但此檔不進 git(.gitignore),只在本機打包時帶入。
        static string Built(int line)
        {
            try { var ta = Resources.Load<TextAsset>("google-client"); if (ta != null) { var ls = ta.text.Replace("\r", "").Split('\n'); if (ls.Length > line) return ls[line].Trim(); } } catch { }
            return "";
        }

        public static void Cancel() { cancel = true; CloseListener(); if (Status == State.Waiting) { Status = State.Idle; Message = ""; } }
        static void CloseListener() { try { listener?.Stop(); } catch { } listener = null; }

        // 呼叫端用 StartCoroutine(GoogleAuth.SignIn()),結束後看 Status / Name / Email
        public static IEnumerator SignIn()
        {
            cancel = false; codeResult = errResult = null; Name = Email = ""; Demo = false;
            string cid = ClientId();
            if (string.IsNullOrEmpty(cid))
            {
                // 示範登入:跑完整流程給你看介面,但明確標示不是真的 Google
                Status = State.Waiting; Message = "DEMO";
                float t = 0f; while (t < 1.2f && !cancel) { t += Time.unscaledDeltaTime; yield return null; }
                if (cancel) { Status = State.Idle; yield break; }
                Demo = true; Name = "Demo_" + UnityEngine.Random.Range(1000, 9999); Email = "demo@example.invalid"; Status = State.Done; yield break;
            }

            int port; string verifier = RandomString(64), state = RandomString(24);
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (Exception e) { Fail("無法開啟本機回呼埠:" + e.Message); yield break; }

            string redirect = "http://127.0.0.1:" + port;
            string url = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Uri.EscapeDataString(cid) + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile") + "&code_challenge=" + Challenge(verifier) + "&code_challenge_method=S256&state=" + state;

            var l = listener; var th = new Thread(() => Listen(l, state)) { IsBackground = true };
            th.Start();
            Status = State.Waiting; Message = "";
            Application.OpenURL(url);

            float wait = 0f;
            while (codeResult == null && errResult == null && !cancel && wait < 180f) { wait += Time.unscaledDeltaTime; yield return null; }
            CloseListener();
            if (cancel) { Status = State.Idle; yield break; }
            if (errResult != null) { Fail(errResult); yield break; }
            if (codeResult == null) { Fail("等太久了(3 分鐘),已取消"); yield break; }

            Status = State.Exchanging;
            var form = new WWWForm();
            form.AddField("code", codeResult); form.AddField("client_id", cid); form.AddField("redirect_uri", redirect);
            form.AddField("grant_type", "authorization_code"); form.AddField("code_verifier", verifier);
            string sec = ClientSecret(); if (sec != "") form.AddField("client_secret", sec);
            string accessToken = null;
            using (var req = UnityWebRequest.Post("https://oauth2.googleapis.com/token", form))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Fail("換取 token 失敗:" + req.error); yield break; }
                accessToken = JsonStr(req.downloadHandler.text, "access_token");
            }
            if (string.IsNullOrEmpty(accessToken)) { Fail("Google 沒有回傳 token"); yield break; }
            using (var req = UnityWebRequest.Get("https://openidconnect.googleapis.com/v1/userinfo"))
            {
                req.SetRequestHeader("Authorization", "Bearer " + accessToken);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Fail("讀取帳號資料失敗:" + req.error); yield break; }
                string js = req.downloadHandler.text;
                Name = JsonStr(js, "name"); Email = JsonStr(js, "email");
                if (string.IsNullOrEmpty(Name)) Name = (Email ?? "").Split('@')[0];
            }
            Status = State.Done;   // accessToken 離開作用域即丟棄,不存檔
        }

        static void Fail(string m) { Status = State.Failed; Message = m; CloseListener(); }

        static void Listen(TcpListener l, string state)
        {
            try
            {
                while (!cancel)
                {
                    if (!l.Pending()) { Thread.Sleep(80); continue; }
                    using (var c = l.AcceptTcpClient())
                    {
                        c.ReceiveTimeout = 4000;
                        var ns = c.GetStream(); var buf = new byte[8192]; int n = ns.Read(buf, 0, buf.Length);
                        string req = Encoding.ASCII.GetString(buf, 0, Math.Max(0, n));
                        string line = req.Split('\n')[0];
                        int a = line.IndexOf(' '), b = line.LastIndexOf(' ');
                        string path = a >= 0 && b > a ? line.Substring(a + 1, b - a - 1) : "";
                        int q = path.IndexOf('?');
                        if (q < 0) { Reply(ns, "…"); continue; }   // 例如 favicon,忽略
                        string code = Query(path.Substring(q + 1), "code"), st = Query(path.Substring(q + 1), "state"), err = Query(path.Substring(q + 1), "error");
                        if (!string.IsNullOrEmpty(err)) { errResult = "Google 回報:" + err; Reply(ns, "Sign-in was cancelled. 登入已取消,可以關掉這個視窗。"); return; }
                        if (st != state || string.IsNullOrEmpty(code)) { Reply(ns, "Invalid request."); continue; }
                        codeResult = code;
                        Reply(ns, "Signed in! 登入成功,可以關掉這個視窗回到 FRC 9427 模擬器。");
                        return;
                    }
                }
            }
            catch (Exception e) { if (!cancel) errResult = "回呼監聽錯誤:" + e.Message; }
            finally { try { l.Stop(); } catch { } }
        }

        static void Reply(NetworkStream ns, string text)
        {
            string html = "<html><head><meta charset=\"utf-8\"><title>FRC 9427 Sim</title></head><body style=\"font-family:sans-serif;background:#0b1220;color:#eaf2ff;text-align:center;padding-top:20vh\"><h2>" + text + "</h2></body></html>";
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            ns.Write(head, 0, head.Length); ns.Write(body, 0, body.Length);
        }

        static string Query(string qs, string key)
        {
            foreach (var kv in qs.Split('&')) { int i = kv.IndexOf('='); if (i > 0 && kv.Substring(0, i) == key) return Uri.UnescapeDataString(kv.Substring(i + 1).Replace('+', ' ')); }
            return "";
        }
        static string RandomString(int n)
        {
            const string cs = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            var bytes = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(bytes);
            var sb = new StringBuilder(n); foreach (var b in bytes) sb.Append(cs[b % cs.Length]); return sb.ToString();
        }
        static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        // 極簡 JSON 取字串欄位(只用在 Google 的扁平回應)
        static string JsonStr(string js, string key)
        {
            int i = js.IndexOf("\"" + key + "\""); if (i < 0) return "";
            i = js.IndexOf(':', i); if (i < 0) return "";
            i = js.IndexOf('"', i); if (i < 0) return "";
            var sb = new StringBuilder();
            for (int j = i + 1; j < js.Length; j++)
            {
                char c = js[j];
                if (c == '\\' && j + 1 < js.Length)
                {
                    char d = js[++j];
                    if (d == 'u' && j + 4 < js.Length) { sb.Append((char)Convert.ToInt32(js.Substring(j + 1, 4), 16)); j += 4; }
                    else sb.Append(d == 'n' ? '\n' : d);
                }
                else if (c == '"') break; else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
