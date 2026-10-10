using System.Diagnostics;
using UnityEngine;

namespace FrcSim
{
    // 英文 AI 導播旁白:依比分/階段/倒數事件產生台詞,字幕 + Windows 內建語音(System.Speech,背景隱藏程序);M 鍵開關
    public class Commentary : MonoBehaviour
    {
        public static bool On = true;
        string line = ""; float lineT;
        int lb, lr; string lphase = ""; int lsec = 999; float quiet, nextOk;
        Process speaking; GUIStyle st;
        static readonly string[] Goal = { "And that's a score for {0}!", "{0} finds the hub — beautiful shot!", "Nothing but the opening! {0} scores.", "{0} keeps the fuel flowing." };
        static readonly string[] Idle = { "Both alliances racing to the neutral zone.", "Watch the cycle time here — it's all about efficiency.", "Great driving out there, the intake rollers are working hard.", "The bump crossing is where matches are won and lost." };

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.M) && !MenuSystem.Blocking) { On = !On; if (!On) Stop(); }
            if (!On) return;
            float t = Time.unscaledTime;
            int b = ScoreManager.BlueScore, r = ScoreManager.RedScore;
            string ph = ScoreManager.Phase ?? ""; int sec = Mathf.CeilToInt(ScoreManager.TimeLeft);
            if (b < lb || r < lr) { lb = b; lr = r; }                       // 新的一場
            if (ph != lphase) { if (lphase != "") Say(ph.Contains("AUTO") ? "The autonomous period is underway." : ph.Contains("TELEOP") || ph.Contains("遙控") ? "Teleop is live! Drivers, take control!" : "Phase change — here we go.", 2.5f); lphase = ph; }
            else if (b > lb) { Say(string.Format(Goal[Random.Range(0, Goal.Length)], "blue"), 1.5f); }
            else if (r > lr) { Say(string.Format(Goal[Random.Range(0, Goal.Length)], "red"), 1.5f); }
            else if (sec <= 30 && lsec > 30 && lsec != 999) Say("Thirty seconds remaining, the endgame is here!", 2.5f);
            else if (sec <= 10 && lsec > 10 && lsec != 999) Say(b == r ? "Ten seconds, and it is all tied up!" : (b > r ? "Ten seconds left, blue holds the lead." : "Ten seconds left, red holds the lead."), 2.5f);
            else if (t - quiet > 18f && t > nextOk) Say(Idle[Random.Range(0, Idle.Length)], 4f);
            lb = b; lr = r; lsec = sec;
        }

        void Say(string s, float cooldown)
        {
            float t = Time.unscaledTime; if (t < nextOk && cooldown < 2f) return;
            line = s; lineT = t; quiet = t; nextOk = t + cooldown;
            Stop();
            try
            {
                var psi = new ProcessStartInfo("powershell", "-NoProfile -WindowStyle Hidden -Command \"Add-Type -AssemblyName System.Speech; $s=New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.Rate=1; $s.Speak('" + s.Replace("'", "") + "')\"")
                { CreateNoWindow = true, UseShellExecute = false };
                speaking = Process.Start(psi);
            }
            catch { }
        }
        void Stop() { try { if (speaking != null && !speaking.HasExited) speaking.Kill(); } catch { } speaking = null; }
        void OnDestroy() { Stop(); }
        void OnApplicationQuit() { Stop(); }

        void OnGUI()
        {
            if (!On || string.IsNullOrEmpty(line) || Time.unscaledTime - lineT > 5f) return;
            if (st == null) st = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            float w = Mathf.Min(900f, Screen.width - 40f), h = 44f;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - 90f, w, h);
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(r, "🎙 " + line, st);
        }
    }
}