using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace FrcSim
{
    // 中文導播旁白(只在導播視角 Mode 8 出現):棒球/NBA 轉播風格,有情緒;字幕 + Windows 內建中文(zh-TW)語音,M 鍵開關
    public class Commentary : MonoBehaviour
    {
        public static bool On = true;
        string line = ""; float lineT;
        int lb, lr; string lphase = ""; int lsec = 999; float quiet, nextOk;
        Process speaking; GUIStyle st;
        static readonly string[] Goal = {
            "{0}進球了!漂亮!", "空心入網!{0}拿下這一分!", "{0}出手,球進了!乾淨俐落!", "太準了!{0}的射手今天手感火燙!",
            "好球!{0}把球送進了 HUB!", "{0}連續得分,氣勢如虹!" };
        static readonly string[] Hot = { "又是{0}!這節奏誰擋得住?!", "{0}連得分數,場邊的觀眾都站起來了!", "停不下來了!{0}一球接一球!" };
        static readonly string[] Idle = {
            "雙方都在搶中立區的球,節奏非常快。", "注意看這個循環時間,比賽就在這些細節裡決定。", "吸球滾輪全力運轉,司機的走位相當漂亮。",
            "過 BUMP 的瞬間是整場最關鍵的一刻,車身起伏一點都不能亂。", "防守壓上來了,這下射手得找新的角度。" };
        static bool Zh => Loc.Lang != "en";
        static string T(string zh, string en) { return Zh ? zh : en; }
        static string Cul => Zh ? "zh-TW" : "en-US";
        static readonly string[] GoalEn = { "{0} scores! Beautiful!", "Nothing but net! {0} cashes in!", "{0} lets it fly, and it's good!", "The {0} shooter is on fire today!", "That is a clean shot for {0}!", "{0} keeps the points coming!" };
        static readonly string[] HotEn = { "{0} again! Who can stop this rhythm?!", "{0} is on a run, the crowd is on its feet!", "They can't be stopped! {0} one after another!" };
        static readonly string[] IdleEn = { "Both alliances fighting for the neutral zone, the pace is fast.", "Watch the cycle time here, matches are won in these details.", "The intake rollers are working overtime, lovely driving.", "Crossing the bump is the key moment, the chassis has to stay composed.", "Defense is coming in, the shooter needs a new angle." };
        static string who(bool blue) { return blue ? T("藍隊", "Blue") : T("紅隊", "Red"); }
        int streakB, streakR, lastLead; float lastGoal;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.M) && !MenuSystem.Blocking) { On = !On; if (!On) Stop(); }
            if (!On || CameraRig.ActiveMode != 8) { lb = ScoreManager.BlueScore; lr = ScoreManager.RedScore; return; }
            float t = Time.unscaledTime;
            int b = ScoreManager.BlueScore, r = ScoreManager.RedScore;
            string ph = ScoreManager.Phase ?? ""; int sec = Mathf.CeilToInt(ScoreManager.TimeLeft);
            if (b < lb || r < lr) { lb = b; lr = r; streakB = streakR = 0; }
            if (ph != lphase) { if (lphase != "") Say(ph.Contains("AUTO") ? T("自動階段開始!程式碼說了算,看誰的路線更穩!", "Autonomous begins! The code does the talking!") : T("遙控階段!司機們,現在交給你們了!", "Teleop is live! Drivers, it is all yours!"), 2.5f, 1, 100); lphase = ph; }
            else if (b > lb || r > lr)
            {
                bool blue = b > lb; if (blue) { streakB++; streakR = 0; } else { streakR++; streakB = 0; }
                int sk = blue ? streakB : streakR; 
                string[] pool = Zh ? (sk >= 3 ? Hot : Goal) : (sk >= 3 ? HotEn : GoalEn);
                int newLead = (int)Mathf.Sign(b - r) * (b == r ? 0 : 1); bool flip = newLead != 0 && lastLead != 0 && newLead != lastLead; lastLead = newLead != 0 ? newLead : lastLead;
                if (flip) Say(T(who(blue) + "反超比分!局勢逆轉!", who(blue) + " takes the lead! What a swing!"), 1.2f, 4, 100);
                else if (b == r && b > 0) Say(T("追平了!比分又拉回平手!", "All square again! We are tied!"), 1.2f, 3, 100);
                else Say(string.Format(pool[Random.Range(0, pool.Length)], who(blue)), 1.2f, sk >= 3 ? 4 : 2, 100);
                lastGoal = t;
            }
            else if (sec <= 60 && lsec > 60 && lsec != 999) Say(b == r ? T("還剩一分鐘,雙方難分難解。", "One minute left and nothing between them.") : T("還剩一分鐘," + who(b > r) + "暫時領先 " + Mathf.Abs(b - r) + " 分。", "One minute to go. " + who(b > r) + " leads by " + Mathf.Abs(b - r) + "."), 2.5f, 2, 100);
            else if (sec <= 30 && lsec > 30 && lsec != 999) Say(T("剩下三十秒,終盤衝刺!", "Thirty seconds left, the final push!"), 2.5f, 3, 100);
            else if (sec <= 10 && lsec > 10 && lsec != 999) Say(b == r ? T("最後十秒,雙方平手!誰能搶下最後一分?!", "Ten seconds and it is tied! Who takes the last point?!") : T("最後十秒," + who(b > r) + "暫時領先!", "Ten seconds, " + who(b > r) + " leads!"), 2.5f, 4, 100);
            else if (sec <= 0 && lsec > 0 && lsec != 999) Say(b == r ? T("比賽結束,平手!太驚險了!", "Final buzzer, a tie! What a finish!") : T("比賽結束!" + who(b > r) + "拿下勝利!", "That is the match! " + who(b > r) + " takes the win!"), 3f, 4, 100);
            else if (t - quiet > 16f && t > nextOk) { var ip = Zh ? Idle : IdleEn; Say(ip[Random.Range(0, ip.Length)], 4f, 0, 85); };
            lb = b; lr = r; lsec = sec;
        }

        void Say(string s, float cooldown, int rate, int vol)
        {
            float t = Time.unscaledTime; if (t < nextOk && cooldown < 2f && t - lastGoal > 1f) return;
            line = s; lineT = t; quiet = t; nextOk = t + cooldown;
            Stop();
            if (EdgeOk == 1) { EdgeSpeak(s, rate); return; }
            try
            {
                string cmd = "Add-Type -AssemblyName System.Speech; $s=New-Object System.Speech.Synthesis.SpeechSynthesizer; try{$s.SelectVoiceByHints('NotSet','NotSet',0,[Globalization.CultureInfo]'" + Cul + "')}catch{}; $s.Rate=" + rate + "; $s.Volume=" + vol + "; $s.Speak('" + s.Replace("'", "") + "')";
                var psi = new ProcessStartInfo("powershell", "-NoProfile -WindowStyle Hidden -Command \"" + cmd + "\"") { CreateNoWindow = true, UseShellExecute = false };
                speaking = Process.Start(psi);
            }
            catch { }
        }
        // ---- 自然語音(可選):有裝 Python 的 edge-tts(pip install edge-tts)且能連網時,用微軟神經語音(zh-TW-YunJheNeural / en-US-GuyNeural);否則退回 Windows 內建語音
        static int EdgeOk = -1;   // -1 未檢查 0 不可用 1 可用
        AudioSource asrc; int speakId;
        void Start()
        {
            asrc = gameObject.AddComponent<AudioSource>(); asrc.spatialBlend = 0f; asrc.volume = 1f;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cmttest") >= 0) StartCoroutine(CmtTest());
            new System.Threading.Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("python", "-c \"import edge_tts\"") { CreateNoWindow = true, UseShellExecute = false };
                    using (var p = Process.Start(psi)) { p.WaitForExit(15000); EdgeOk = p.ExitCode == 0 ? 1 : 0; }
                }
                catch { EdgeOk = 0; }
            }) { IsBackground = true }.Start();
        }
        void EdgeSpeak(string s, int rate)
        {
            int id = ++speakId; string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frcsim_cmt_" + id + ".mp3"); bool zh = Zh;
            new System.Threading.Thread(() =>
            {
                try
                {
                    string voice = zh ? "zh-TW-YunJheNeural" : "en-US-GuyNeural";
                    string r = "+" + (10 + rate * 8) + "%", pitch = "+" + (rate * 2) + "Hz";
                    string text = s.Replace("\"", "").Replace("'", "");
                    var psi = new ProcessStartInfo("python", "-m edge_tts --voice " + voice + " --rate=" + r + " --pitch=" + pitch + " --text \"" + text + "\" --write-media \"" + file + "\"") { CreateNoWindow = true, UseShellExecute = false };
                    using (var p = Process.Start(psi)) { if (!p.WaitForExit(12000)) { try { p.Kill(); } catch { } return; } if (p.ExitCode != 0) { EdgeOk = 0; return; } }
                    pending = new KeyValuePair<int, string>(id, file);
                }
                catch { EdgeOk = 0; }
            }) { IsBackground = true }.Start();
        }
        KeyValuePair<int, string>? pending;
        System.Collections.IEnumerator CmtTest()
        {
            yield return new WaitForSecondsRealtime(5f);
            Say(Zh ? "藍隊進球了!漂亮!" : "Blue scores! Beautiful!", 1f, 2, 100);
            yield return new WaitForSecondsRealtime(9f);
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "cmttest.txt"), "edge=" + EdgeOk + " clip=" + (asrc.clip != null ? asrc.clip.length.ToString("0.00") + "s" : "none") + " playing=" + asrc.isPlaying);
            Application.Quit();
        }
        System.Collections.IEnumerator PlayFile(int id, string file)
        {
            using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file:///" + file.Replace('\\', '/'), AudioType.MPEG))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success && id == speakId) { var c = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req); asrc.Stop(); asrc.clip = c; asrc.Play(); }
            }
            try { System.IO.File.Delete(file); } catch { }
        }
        void LateUpdate() { if (pending.HasValue) { var p = pending.Value; pending = null; if (p.Key == speakId) StartCoroutine(PlayFile(p.Key, p.Value)); } }
        void Stop() { try { if (speaking != null && !speaking.HasExited) speaking.Kill(); } catch { } speaking = null; if (asrc != null) asrc.Stop(); speakId++; }
        void OnDestroy() { Stop(); }
        void OnApplicationQuit() { Stop(); }

        void OnGUI()
        {
            if (!On || CameraRig.ActiveMode != 8 || string.IsNullOrEmpty(line) || Time.unscaledTime - lineT > 5f) return;
            if (st == null) st = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            float w = Mathf.Min(1000f, Screen.width - 40f), h = 48f;
            var rc = new Rect((Screen.width - w) / 2f, Screen.height - 96f, w, h);
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(rc, Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(rc, line, st);
        }
    }
}