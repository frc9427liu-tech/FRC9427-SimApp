using System;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace FrcSim
{
    // 啟動時在背景檢查 GitHub 上有沒有更新的 Release(不會卡住遊戲、失敗就安靜略過);有新版時主畫面副標題會提示,按 U 開啟下載頁。
    // 注意:倉庫若是「私人」,未登入的請求讀不到 → 查不到就當作沒有新版(不會報錯)。倉庫改公開後自動生效。
    public class UpdateCheck : MonoBehaviour
    {
        public const string Current = "0.1.3";
        const string Api = "https://api.github.com/repos/frc9427liu-tech/FRC9427-SimApp/releases/latest";
        public const string Page = "https://github.com/frc9427liu-tech/FRC9427-SimApp/releases/latest";

        public static volatile string Latest = "";
        public static volatile bool Newer;
        public static volatile string Status = "not checked";

        void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") >= 0) return;   // 自動測試不連網
            new Thread(Check) { IsBackground = true }.Start();
        }

        static void Check()
        {
            try
            {
                using (var c = new System.Net.WebClient())
                {
                    c.Headers.Add("User-Agent", "FRC9427-Sim/" + Current);
                    string json = c.DownloadString(Api);
                    var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(?:\\.[0-9]+){1,3})\"");
                    if (!m.Success) { Status = "no tag"; return; }
                    Latest = m.Groups[1].Value;
                    Newer = new Version(Latest) > new Version(Current);
                    Status = Newer ? "update available" : "up to date";
                }
            }
            catch (Exception e) { Status = "check failed: " + e.Message; }   // 私人倉庫(404)/沒網路:略過
        }
    }
}
