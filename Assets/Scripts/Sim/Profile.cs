using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 本機玩家檔案(像遊戲存檔槽):每個玩家各自記「看過教學」「最佳分數」「更新內容已讀」。
    // 0 號玩家沿用舊的無前綴鍵,所以升級前的存檔不會消失。
    // 訪客模式(Guest):所有讀寫只在記憶體,關掉遊戲就消失,絕不寫入 PlayerPrefs(戰績/經驗/最佳分數/暱稱都不存)。
    public static class Profile
    {
        public static bool Guest { get; private set; }
        static readonly Dictionary<string, object> mem = new Dictionary<string, object>();
        public static void EnterGuest() { mem.Clear(); Guest = true; }
        public static void ExitGuest() { mem.Clear(); Guest = false; }

        public static int Count { get { return Mathf.Max(1, Prefs.GetInt("profCount", 1)); } }
        public static int Index { get { return Mathf.Clamp(Prefs.GetInt("profIdx", 0), 0, Count - 1); } set { Prefs.SetInt("profIdx", value); PlayerPrefs.Save(); } }
        public static string Name(int i) { return Prefs.GetString("profName" + i, Loc.Lang == "en" ? "Player " + (i + 1) : "玩家 " + (i + 1)); }
        public static string CurrentName { get { return Guest ? (Loc.Lang == "en" ? "Guest" : "訪客") : Name(Index); } }
        public static string Key(string k) { return Index == 0 ? k : "p" + Index + "_" + k; }
        public static string KeyOf(int slot, string k) { return slot == 0 ? k : "p" + slot + "_" + k; }
        public static int GetInt(string k, int def) { if (Guest) { object o; return mem.TryGetValue(k, out o) && o is int ? (int)o : def; } return Prefs.GetInt(Key(k), def); }
        public static void SetInt(string k, int v) { if (Guest) { mem[k] = v; return; } Prefs.SetInt(Key(k), v); PlayerPrefs.Save(); }
        public static string GetString(string k, string def) { if (Guest) { object o; return mem.TryGetValue(k, out o) && o is string ? (string)o : def; } return Prefs.GetString(Key(k), def); }
        public static void SetString(string k, string v) { if (Guest) { mem[k] = v; return; } Prefs.SetString(Key(k), v); PlayerPrefs.Save(); }
        // 讀別的檔位(登入時用 email 找對應檔位)
        public static string GetStringOf(int slot, string k, string def) { return Prefs.GetString(KeyOf(slot, k), def); }
        public static int GetIntOf(int slot, string k, int def) { return Prefs.GetInt(KeyOf(slot, k), def); }
        public static void Next() { Index = (Index + 1) % Count; }
        public static void AddNew() { int n = Count; Prefs.SetInt("profCount", n + 1); Prefs.SetString("profName" + n, ""); Prefs.SetString("profName" + n, Loc.Lang == "en" ? "Player " + (n + 1) : "玩家 " + (n + 1)); Index = n; PlayerPrefs.Save(); }
    }
}
