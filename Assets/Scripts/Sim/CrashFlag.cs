using System.IO;
using UnityEngine;

namespace FrcSim
{
    // 異常結束偵測:啟動時建立旗標檔,正常離開時刪除;下次啟動若旗標還在,代表上次是崩潰/被強制結束,在主選單提示並指出日誌位置。
    public static class CrashFlag
    {
        public static string Msg = "";
        static string Flag => Path.Combine(Application.persistentDataPath, "running.flag");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-crashtest") >= 0) { Msg = Loc.Lang == "en" ? "Last run crashed" : "上次異常結束"; return; }
            if (SuperSample.IsTestRun()) return;   // 自動測試常被強制關閉,不算
            try
            {
                if (File.Exists(Flag))
                    Msg = Loc.Lang == "en" ? "Last run crashed" : "上次異常結束";
                File.WriteAllText(Flag, System.DateTime.Now.ToString("s"));
            }
            catch { }
            Application.quitting += () => { try { File.Delete(Flag); } catch { } };
        }
    }
}



