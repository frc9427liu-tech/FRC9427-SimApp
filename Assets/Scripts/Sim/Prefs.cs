using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // PlayerPrefs 的記憶體快取:Windows 上 PlayerPrefs 是登錄檔,每幀讀幾十次很浪費;讀一次就記住,寫入時同步寫回
    public static class Prefs
    {
        static readonly Dictionary<string, int> I = new Dictionary<string, int>();
        static readonly Dictionary<string, string> S = new Dictionary<string, string>();
        public static bool Isolated;   // 自動測試(-accttest)用:完全不讀不寫 PlayerPrefs(登錄檔),只用記憶體,不會動到使用者的存檔
        public static int GetInt(string k, int d) { if (!I.TryGetValue(k, out var v)) I[k] = v = Isolated ? d : PlayerPrefs.GetInt(k, d); return v; }
        public static void SetInt(string k, int v) { I[k] = v; if (!Isolated) PlayerPrefs.SetInt(k, v); }
        public static string GetString(string k, string d) { if (!S.TryGetValue(k, out var v)) S[k] = v = Isolated ? d : PlayerPrefs.GetString(k, d); return v; }
        public static void SetString(string k, string v) { S[k] = v; if (!Isolated) PlayerPrefs.SetString(k, v); }
        public static void SetStringTransient(string k, string v) { S[k] = v; }   // 只改本次執行的記憶體快取,不寫登錄檔(測試旗標 -lang/-model 用;以前每跑一次煙霧測試就永久改掉使用者的語言與機器人設定)
    }
}