using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FrcSim
{
    // ---------------------------------------------------------------------------------------------
    // 其他模組(K5 / W6 / UI7)的 API 還沒落地時的「軟連結」:用反射找,找不到就走替身。
    // 對方的 API 一出現就自動接上,不必改這個檔。待接清單見 RaceSession / 回報。
    // ---------------------------------------------------------------------------------------------
    public static class RaceSoft
    {
        static readonly Dictionary<string, MethodInfo> cache = new Dictionary<string, MethodInfo>();

        static MethodInfo Find(string type, string method, Type[] args, bool inst = false)
        {
            string key = type + "." + method + "/" + (args != null ? args.Length : -1);
            if (cache.TryGetValue(key, out var m)) return m;
            try
            {
                Type t = Type.GetType("FrcSim." + type);
                if (t != null)
                {
                    var fl = BindingFlags.Public | BindingFlags.NonPublic | (inst ? BindingFlags.Instance : BindingFlags.Static);
                    m = args != null ? t.GetMethod(method, fl, null, args, null) : t.GetMethod(method, fl);
                }
            }
            catch (Exception) { m = null; }
            cache[key] = m;
            return m;
        }

        // ---- K5:KartPresets.ApplyCC(KartStats, int cc)
        public static bool HasApplyCC { get { return Find("KartPresets", "ApplyCC", new[] { typeof(KartStats), typeof(int) }) != null; } }
        public static bool ApplyCC(KartStats s, int cc)
        {
            var m = Find("KartPresets", "ApplyCC", new[] { typeof(KartStats), typeof(int) });
            if (m == null) return false;
            try { m.Invoke(null, new object[] { s, cc }); return true; } catch (Exception e) { Debug.LogWarning("[RaceSoft] ApplyCC: " + e.Message); return false; }
        }

        // ---- K5:RaceKart.StartBoostWindow(float remain)
        public static bool HasStartBoostWindow { get { return Find("RaceKart", "StartBoostWindow", new[] { typeof(float) }, true) != null; } }
        public static bool StartBoostWindow(RaceKart k, float remain)
        {
            var m = Find("RaceKart", "StartBoostWindow", new[] { typeof(float) }, true);
            if (m == null || k == null) return false;
            try { m.Invoke(k, new object[] { remain }); return true; } catch (Exception e) { Debug.LogWarning("[RaceSoft] StartBoostWindow: " + e.Message); return false; }
        }

        // ---- W6:Cups.Races(cup) / Cups.Points(place)
        public static readonly int[] DefaultPoints = { 15, 12, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        public static int Points(int place)
        {
            var m = Find("Cups", "Points", new[] { typeof(int) });
            if (m != null)
            {
                try { return Convert.ToInt32(m.Invoke(null, new object[] { place })); } catch (Exception) { }
            }
            return place >= 1 && place <= DefaultPoints.Length ? DefaultPoints[place - 1] : 0;
        }

        static object Member(object o, params string[] names)
        {
            var t = o.GetType();
            foreach (var n in names)
            {
                var f = t.GetField(n, BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(o);
                var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
            }
            return null;
        }

        /// 盃賽的 4 場賽道(+ 是否反向)。Cups.Races 回傳 int[] / IEnumerable&lt;int&gt; / 帶 Track(Id)+Reverse 欄位的物件都吃;失敗就用預設(3 條賽道 + 第 1 條的反向版)。
        public static void CupRaces(int cup, out int[] tracks, out bool[] reverse)
        {
            tracks = null; reverse = null;
            var m = Find("Cups", "Races", new[] { typeof(int) });
            if (m != null)
            {
                try
                {
                    var res = m.Invoke(null, new object[] { cup }) as IEnumerable;
                    if (res != null)
                    {
                        var tl = new List<int>(); var rl = new List<bool>();
                        foreach (var x in res)
                        {
                            if (x == null) continue;
                            if (x is int xi) { tl.Add(xi); rl.Add(false); continue; }
                            object tv = Member(x, "Track", "TrackId", "Id", "track", "trackId");
                            object rv = Member(x, "Reverse", "Rev", "reverse");
                            if (tv == null) continue;
                            tl.Add(Convert.ToInt32(tv)); rl.Add(rv is bool b && b);
                        }
                        if (tl.Count > 0) { tracks = tl.ToArray(); reverse = rl.ToArray(); }
                    }
                }
                catch (Exception e) { Debug.LogWarning("[RaceSoft] Cups.Races: " + e.Message); }
            }
            if (tracks == null)
            {
                int n = Mathf.Max(1, RaceWorld.TrackCount);
                int c = Mathf.Max(0, cup);
                tracks = new[] { (c * 3) % n, (c * 3 + 1) % n, (c * 3 + 2) % n, (c * 3) % n };
                reverse = new[] { false, false, false, true };
            }
        }

        // ---- UI7:RaceMenus.ShowStandings() / ShowPodium()(無參數版;有參數的版本先用 OnGUI 替身,UI 直接讀 RaceGrandPrix.Entries)
        public static bool TryShowMenu(string method)
        {
            var m = Find("RaceMenus", method, null);
            if (m == null || m.GetParameters().Length != 0) return false;
            try { m.Invoke(null, null); return true; } catch (Exception e) { Debug.LogWarning("[RaceSoft] RaceMenus." + method + ": " + e.Message); return false; }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 大獎賽(盃賽)
    // ---------------------------------------------------------------------------------------------
    public enum GpState { Off, Racing, WaitOthers, Standings, Podium, Done }

    public sealed class GpEntry
    {
        public int KartId;
        public string Name;
        public bool IsPlayer;
        public int Total;                                  // 累計得分
        public int LastPlace, LastPoints;                  // 最近一場
        public float LastTime;                             // 最近一場完賽時間(沒完賽 = 0)
        public int Rank;                                   // 目前總名次 1..N
        public readonly List<int> Places = new List<int>();
        public readonly List<int> Points = new List<int>();
    }

    public static class RaceGrandPrix
    {
        public const float WaitSeconds = 15f;              // 玩家完賽後等其他車的上限

        public static GpState State = GpState.Off;
        public static bool Active { get { return State != GpState.Off; } }
        public static int RaceIndex;                       // 0-based,目前第幾場
        public static int RaceCount;
        public static int CupId;
        public static int[] RacesOverride;                 // 測試:指定賽道,不查 Cups
        public static bool ExitOnDone = true;              // 頒獎後按 A/Enter 回主選單;測試關掉
        public static readonly List<GpEntry> Entries = new List<GpEntry>();   // 依總名次排序(場間積分榜 / 頒獎讀這個)
        public static int PlayerRank;                      // 最終玩家名次
        public static int Medal;                           // 0 無 / 1 銅 / 2 銀 / 3 金
        public static bool MenuHandled;                    // true = RaceMenus 接手畫面;false = 用 OnGUI 替身
        public static event Action<GpState> StateChanged;

        static int[] trackIds; static bool[] revFlags; static bool baseReverse;
        static float waitT;
        static GameObject gui;

        public static string MedalKey(int cup, int cc) { return "race_cup_" + cup + "_" + cc; }
        public static int TrackOf(int i) { return trackIds != null && i >= 0 && i < trackIds.Length ? trackIds[i] : 0; }
        public static bool ReverseOf(int i) { return revFlags != null && i >= 0 && i < revFlags.Length && (revFlags[i] ^ baseReverse); }

        /// RaceSession.Begin 呼叫:不是大獎賽就保持 Off。會把 cfg 改成第 1 場的賽道。
        public static void Begin(RaceConfig cfg)
        {
            Reset();
            if (cfg == null || !cfg.GrandPrix || cfg.TimeTrial) return;
            CupId = Mathf.Max(0, cfg.Cup);
            if (RacesOverride != null && RacesOverride.Length > 0)
            {
                trackIds = (int[])RacesOverride.Clone(); revFlags = new bool[trackIds.Length];
            }
            else RaceSoft.CupRaces(CupId, out trackIds, out revFlags);
            int tc = Mathf.Max(1, RaceWorld.TrackCount);
            for (int i = 0; i < trackIds.Length; i++) trackIds[i] = Mathf.Clamp(trackIds[i], 0, tc - 1);
            baseReverse = cfg.Reverse;
            RaceCount = trackIds.Length; RaceIndex = 0;
            ApplyRace(cfg);
            SetState(GpState.Racing);
        }

        static void ApplyRace(RaceConfig cfg)
        {
            cfg.TrackId = TrackOf(RaceIndex);
            cfg.Reverse = ReverseOf(RaceIndex);
        }

        public static void Reset()
        {
            State = GpState.Off; RaceIndex = 0; RaceCount = 0; Entries.Clear(); PlayerRank = 0; Medal = 0; waitT = 0f;
            trackIds = null; revFlags = null; MenuHandled = false;
            if (gui != null) UnityEngine.Object.Destroy(gui);
            gui = null;
        }

        static void SetState(GpState s) { State = s; StateChanged?.Invoke(s); }

        /// 每場世界建好後呼叫:確保每台車都有積分列。
        public static void OnRaceBuilt(IList<RaceKart> karts)
        {
            if (!Active) return;
            foreach (var k in karts)
            {
                if (k == null) continue;
                GpEntry e = Find(k.KartId);
                if (e == null) { e = new GpEntry { KartId = k.KartId }; Entries.Add(e); }
                e.Name = k.DisplayName; e.IsPlayer = k.IsPlayer || k.KartId == 0;
            }
            waitT = 0f;
            SetState(GpState.Racing);
        }

        /// 暫停選單「重來這場」:取消等待中的結算,回到比賽狀態。
        public static void OnRestart() { if (State == GpState.WaitOthers) SetState(GpState.Racing); }

        public static GpEntry Find(int kartId) { foreach (var e in Entries) if (e.KartId == kartId) return e; return null; }

        public static void OnPlayerFinished()
        {
            if (State != GpState.Racing) return;
            waitT = 0f;
            SetState(GpState.WaitOthers);
        }

        /// RaceSession.Tick 每幀呼叫(選單開著時不呼叫)。
        public static void Tick(float dt)
        {
            if (State != GpState.WaitOthers) return;
            waitT += dt;
            if (waitT >= WaitSeconds || AllFinished()) ScoreRace();
        }

        /// 玩家不想等其他人跑完:直接結算這場(只在 WaitOthers 有效;UI 的「看積分榜」按鈕呼叫)。
        public static void SkipWait() { if (State == GpState.WaitOthers) ScoreRace(); }

        static bool AllFinished()
        {
            foreach (var k in RaceSession.Karts)
                if (k != null && k.Progress != null && !k.Progress.Finished) return false;
            return true;
        }

        /// 這場的名次:已完賽的依完賽時間,其餘依進度(Total)由大到小。
        static List<RaceKart> OrderThisRace()
        {
            var l = new List<RaceKart>();
            foreach (var k in RaceSession.Karts) if (k != null && k.Progress != null) l.Add(k);
            l.Sort((a, b) =>
            {
                var pa = a.Progress; var pb = b.Progress;
                if (pa.Finished != pb.Finished) return pa.Finished ? -1 : 1;
                if (pa.Finished) { int c = pa.FinishTime.CompareTo(pb.FinishTime); if (c != 0) return c; }
                else { int c = pb.Total.CompareTo(pa.Total); if (c != 0) return c; }
                return a.KartId.CompareTo(b.KartId);
            });
            return l;
        }

        static void ScoreRace()
        {
            var order = OrderThisRace();
            for (int i = 0; i < order.Count; i++)
            {
                var k = order[i]; var e = Find(k.KartId);
                if (e == null) { e = new GpEntry { KartId = k.KartId, Name = k.DisplayName, IsPlayer = k.IsPlayer }; Entries.Add(e); }
                int pts = RaceSoft.Points(i + 1);
                e.Total += pts; e.LastPlace = i + 1; e.LastPoints = pts;
                e.LastTime = k.Progress.Finished ? k.Progress.FinishTime : 0f;
                e.Places.Add(i + 1); e.Points.Add(pts);
            }
            SortEntries();
            bool last = RaceIndex >= RaceCount - 1;
            if (last) FinishCup();
            else { SetState(GpState.Standings); ShowUi("ShowStandings"); }
            Debug.Log("[GrandPrix] race " + (RaceIndex + 1) + "/" + RaceCount + " scored: " + Summary());
        }

        static void SortEntries()
        {
            Entries.Sort((a, b) =>
            {
                int c = b.Total.CompareTo(a.Total);
                if (c != 0) return c;
                c = a.LastPlace.CompareTo(b.LastPlace);   // 同分:最近一場名次好的在前
                return c != 0 ? c : a.KartId.CompareTo(b.KartId);
            });
            for (int i = 0; i < Entries.Count; i++) Entries[i].Rank = i + 1;
        }

        static void FinishCup()
        {
            PlayerRank = 0;
            foreach (var e in Entries) if (e.IsPlayer) PlayerRank = e.Rank;
            Medal = PlayerRank == 1 ? 3 : PlayerRank == 2 ? 2 : PlayerRank == 3 ? 1 : 0;
            var cfg = RaceSession.Config;
            int cc = cfg != null ? cfg.CC : 150;
            string key = MedalKey(CupId, cc);
            if (Medal > Prefs.GetInt(key, 0)) { Prefs.SetInt(key, Medal); if (!Prefs.Isolated) PlayerPrefs.Save(); }
            SetState(GpState.Podium);
            ShowUi("ShowPodium");
        }

        static void ShowUi(string method)
        {
            MenuHandled = RaceSoft.TryShowMenu(method);
            if (!MenuHandled && gui == null)
            {
                gui = new GameObject("GpGui");
                gui.AddComponent<GpGui>();
            }
        }

        /// 玩家按 A / Enter:積分榜 → 下一場;頒獎 → 結束(回主選單)。UI 模組的按鈕也呼叫這個。
        public static void Continue()
        {
            if (State == GpState.Standings)
            {
                RaceIndex++;
                var cfg = RaceSession.Config;
                if (cfg == null) return;
                ApplyRace(cfg);
                SetState(GpState.Racing);
                RaceSession.GpNextRace();
            }
            else if (State == GpState.Podium)
            {
                SetState(GpState.Done);
                if (ExitOnDone) { try { MenuSystem.GoMain(); } catch (Exception e) { Debug.LogWarning("[GrandPrix] GoMain: " + e.Message); } }
            }
        }

        public static string Summary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in Entries) sb.Append(e.Name).Append('=').Append(e.Total).Append(' ');
            return sb.ToString();
        }

        // ---- OnGUI 替身(RaceMenus.ShowStandings / ShowPodium 還沒做時用)
        sealed class GpGui : MonoBehaviour
        {
            GUIStyle big, row;
            void Update()
            {
                if (State != GpState.Standings && State != GpState.Podium) return;
                bool go = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                try { go |= Pad.Down(Pad.A); } catch (Exception) { }
                if (go) Continue();
            }
            void OnGUI()
            {
                if (State != GpState.Standings && State != GpState.Podium) return;
                if (big == null)
                {
                    big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    row = new GUIStyle(GUI.skin.label) { fontSize = 24 };
                }
                bool en = Loc.Lang == "en";
                float w = 620f, h = 120f + Entries.Count * 34f;
                var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                GUI.Box(r, GUIContent.none);
                string title = State == GpState.Podium
                    ? (en ? "Podium" : "頒獎") + (Medal == 3 ? "  (Gold)" : Medal == 2 ? "  (Silver)" : Medal == 1 ? "  (Bronze)" : "")
                    : (en ? "Standings " : "積分榜 ") + (RaceIndex + 1) + "/" + RaceCount;
                GUI.Label(new Rect(r.x, r.y + 8f, w, 50f), title, big);
                for (int i = 0; i < Entries.Count; i++)
                {
                    var e = Entries[i];
                    GUI.Label(new Rect(r.x + 30f, r.y + 66f + i * 34f, w - 60f, 32f),
                        e.Rank + ".  " + e.Name + (e.IsPlayer ? " <" : "") + "   +" + e.LastPoints + "   =  " + e.Total, row);
                }
                GUI.Label(new Rect(r.x, r.yMax - 40f, w, 32f), en ? "Enter / A : continue" : "Enter / A:繼續", row);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 起跑加速(倒數 3-2-1-GO)。K5 的 RaceKart.StartBoostWindow 存在就交給它;沒有就用這個替身。
    // 玩家:倒數剩 1.0~0.4 s 之間才按下油門並一路按住 → GO 時 Boost 0.8 s;太早(剩 > 1.0 s 就按住)→ GO 後打滑 0.8 s。
    // AI:依難度隨機給(Level 0..3 → 好起步機率 0.15/0.35/0.6/0.85、打滑 0.15/0.10/0.05/0)。
    // ---------------------------------------------------------------------------------------------
    public static class RaceStartBoost
    {
        public const float WindowHi = 1.0f, WindowLo = 0.4f, BoostSec = 0.8f, SlipSec = 0.8f;
        static readonly float[] GoodP = { 0.15f, 0.35f, 0.6f, 0.85f };
        static readonly float[] SlipP = { 0.15f, 0.10f, 0.05f, 0f };

        static KartInputReader probe;
        static RaceKart player; static bool playerIsHuman;
        static float pressRemain = -1f;
        static readonly Dictionary<RaceKart, int> plan = new Dictionary<RaceKart, int>();   // 1 好起步 2 打滑
        public static int LastPlayerResult;     // 0 無 / 1 加速 / 2 打滑(測試讀)

        public static void Begin(RaceKart pl, IList<RaceKart> karts, RaceConfig cfg, bool human, int seed)
        {
            player = pl; playerIsHuman = human; pressRemain = -1f; LastPlayerResult = 0;
            probe = human ? new KartInputReader(0) : null;
            plan.Clear();
            var rng = new System.Random(seed * 7919 + 13);
            int lvl = Mathf.Clamp(cfg != null ? cfg.AiLevel : 1, 0, 3);
            foreach (var k in karts)
            {
                if (k == null) continue;
                if (k == pl && human) continue;
                double r = rng.NextDouble();
                if (r < GoodP[lvl]) plan[k] = 1;
                else if (r < GoodP[lvl] + SlipP[lvl]) plan[k] = 2;
            }
        }

        public static void Tick(float dt, float remain)
        {
            if (player == null || !playerIsHuman) return;
            if (RaceSoft.StartBoostWindow(player, remain)) return;   // K5 的版本接手
            bool held = probe != null && probe.Read(dt).Throttle > 0.5f;
            if (held) { if (pressRemain < 0f) pressRemain = remain; }
            else pressRemain = -1f;
        }

        /// GO 的那一刻(車已解凍)。
        public static void Go()
        {
            if (player != null && playerIsHuman && !RaceSoft.HasStartBoostWindow)
            {
                if (pressRemain >= 0f)
                {
                    if (pressRemain <= WindowHi && pressRemain >= WindowLo) { player.Boost(BoostSec, 0.3f); LastPlayerResult = 1; }
                    else if (pressRemain > WindowHi) { player.Slip(SlipSec, 0.4f, 0.7f); LastPlayerResult = 2; }
                }
            }
            foreach (var kv in plan)
            {
                if (kv.Key == null) continue;
                if (kv.Value == 1) kv.Key.Boost(BoostSec, 0.3f);
                else if (kv.Value == 2) kv.Key.Slip(SlipSec, 0.4f, 0.7f);
            }
            plan.Clear(); pressRemain = -1f;
        }

        public static void Clear() { plan.Clear(); player = null; probe = null; pressRemain = -1f; }
    }
}
