using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    public class FriendInfo { public string Uid, Name; }

    // 好友後端介面:現在只有本機版(LocalFriendBackend);之後有伺服器時實作這個介面並指定給 Friends.Backend 即可,UI 不用改。
    public interface IFriendBackend
    {
        bool Online { get; }                              // 是否真的連線同步(本機版 = false)
        IList<FriendInfo> List();
        bool Add(string uid, string myUid, out string err);
        bool Remove(string uid);
    }

    // 本機好友名單:存在目前玩家檔位底下("friends" = "uid|name;uid|name")。訪客不存(只在記憶體,見 Profile.Guest)。
    public class LocalFriendBackend : IFriendBackend
    {
        public bool Online { get { return false; } }
        public IList<FriendInfo> List()
        {
            var r = new List<FriendInfo>();
            string s = Profile.GetString("friends", "");
            if (s.Length == 0) return r;
            foreach (var e in s.Split(';')) { var p = e.Split('|'); if (p.Length >= 1 && p[0].Length > 0) r.Add(new FriendInfo { Uid = p[0], Name = p.Length > 1 ? p[1] : "" }); }
            return r;
        }
        void Save(IList<FriendInfo> l) { var sb = new StringBuilder(); foreach (var f in l) { if (sb.Length > 0) sb.Append(';'); sb.Append(f.Uid).Append('|').Append(f.Name); } Profile.SetString("friends", sb.ToString()); }
        public bool Add(string uid, string myUid, out string err)
        {
            err = null; uid = (uid ?? "").Trim();
            if (!Friends.ValidUid(uid)) { err = Loc.Lang == "zh" ? "UID 必須是 8 位數字" : "UID must be 8 digits"; return false; }
            if (uid == myUid) { err = Loc.Lang == "zh" ? "這是你自己的 UID" : "That's your own UID"; return false; }
            var l = List();
            foreach (var f in l) if (f.Uid == uid) { err = Loc.Lang == "zh" ? "已經是好友了" : "Already a friend"; return false; }
            l.Add(new FriendInfo { Uid = uid, Name = "" }); Save(l); return true;
        }
        public bool Remove(string uid) { var l = List(); for (int i = 0; i < l.Count; i++) if (l[i].Uid == uid) { l.RemoveAt(i); Save(l); return true; } return false; }
    }

    public static class Friends
    {
        public static IFriendBackend Backend = new LocalFriendBackend();   // 之後換成雲端後端
        public static bool ValidUid(string s) { if (s == null || s.Length != 8) return false; foreach (char c in s) if (c < '0' || c > '9') return false; return true; }
    }
}
