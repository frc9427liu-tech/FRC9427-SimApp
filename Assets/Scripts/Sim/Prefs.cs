using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // PlayerPrefs 的記憶體快取:Windows 上 PlayerPrefs 是登錄檔,每幀讀幾十次很浪費;讀一次就記住,寫入時同步寫回
    public static class Prefs
    {
        static readonly Dictionary<string, int> I = new Dictionary<string, int>();
        static readonly Dictionary<string, string> S = new Dictionary<string, string>();
        public static int GetInt(string k, int d) { if (!I.TryGetValue(k, out var v)) I[k] = v = PlayerPrefs.GetInt(k, d); return v; }
        public static void SetInt(string k, int v) { I[k] = v; PlayerPrefs.SetInt(k, v); }
        public static string GetString(string k, string d) { if (!S.TryGetValue(k, out var v)) S[k] = v = PlayerPrefs.GetString(k, d); return v; }
        public static void SetString(string k, string v) { S[k] = v; PlayerPrefs.SetString(k, v); }
    }
}