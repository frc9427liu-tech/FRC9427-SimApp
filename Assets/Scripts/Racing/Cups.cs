using System;
using System.Collections.Generic;

namespace FrcSim
{
    // 盃賽定義與計分(模組 W,瑪利歐賽車式大獎賽,見 docs/racing/05 §1)。純資料 + 數學,不碰 Unity 物件。
    //   Cups.Count / Cups.Name(i, lang) / Cups.Races(i) -> 4 場 (trackId, mirror, reverse)
    //   Cups.Points(place) = {15,12,10,9,8,7,6,5}(8 台車,名次 1 起算),Totals / Ranking / RankOf 算累計與名次。
    //   Cups.Apply(cfg, cup, raceIndex) 把某一場的賽道 / 鏡像 / 反向 / 盃賽編號寫進 RaceConfig。

    /// 盃賽中的一場。
    public struct CupRace
    {
        public int TrackId;
        public bool Mirror, Reverse;
        public CupRace(int trackId, bool mirror = false, bool reverse = false) { TrackId = trackId; Mirror = mirror; Reverse = reverse; }
        public void Deconstruct(out int trackId, out bool mirror, out bool reverse) { trackId = TrackId; mirror = Mirror; reverse = Reverse; }
    }

    public enum CupTrophy { None, Bronze, Silver, Gold }

    public static class Cups
    {
        public const int Count = 6, RacesPerCup = 4, Racers = 8;

        /// 名次 1..8 的得分。
        public static readonly int[] PointTable = { 15, 12, 10, 9, 8, 7, 6, 5 };

        static readonly string[] NameZh = { "葉子盃", "殼殼盃", "星星盃", "雲朵盃", "草原盃", "火山盃" };
        static readonly string[] NameEn = { "Leaf Cup", "Shell Cup", "Star Cup", "Cloud Cup", "Meadow Cup", "Magma Cup" };

        // 賽道 0 = Island GP、1 = Canyon Run、2 = Coast Loop、3 = Meadow Run、4 = Dune Dash、5 = Frost Peak、6 = Pine Hollow、7 = Magma Rush(3..7 由 TrackGen 產生,有飛越缺口)。
        // 每盃 4 場,相鄰兩場不同賽道,盃內組合不重複。每盃 4 場都是 4 個不同場地(用滿 8 條賽道+鏡像/反向)。
        static readonly CupRace[][] Table =
        {
            // 葉子盃:島嶼、草原、峽谷、松林(4 個不同場地)
            new[] { new CupRace(0), new CupRace(3), new CupRace(1), new CupRace(6) },
            // 殼殼盃:海岸、沙漠、島嶼鏡像、霜峰
            new[] { new CupRace(2), new CupRace(4), new CupRace(0, true, false), new CupRace(5) },
            // 星星盃:火山、海岸反向、草原鏡像、峽谷鏡像反向
            new[] { new CupRace(7), new CupRace(2, false, true), new CupRace(3, true, false), new CupRace(1, true, true) },
            // 雲朵盃:松林開場、霜峰反向、沙漠鏡像、島嶼反向
            new[] { new CupRace(6), new CupRace(5, false, true), new CupRace(4, true, false), new CupRace(0, false, true) },
            // 草原盃:草原、沙漠、雪地、森林各一場(原版)
            new[] { new CupRace(3), new CupRace(4), new CupRace(5), new CupRace(6) },
            // 火山盃:火山開場,森林反向,草原鏡像,雪地鏡像反向
            new[] { new CupRace(7), new CupRace(6, false, true), new CupRace(3, true, false), new CupRace(5, true, true) },
        };

        static int Clamp(int i) { return i < 0 ? 0 : (i >= Count ? Count - 1 : i); }

        /// 盃名。lang = "en" 回英文,其他(含 "zh")回中文。
        public static string Name(int cup, string lang) { cup = Clamp(cup); return lang == "en" ? NameEn[cup] : NameZh[cup]; }
        /// 用目前設定的語言(Loc.Lang)。
        public static string Name(int cup) { return Name(cup, Loc.Lang); }

        /// 這個盃的 4 場。
        public static IReadOnlyList<CupRace> Races(int cup) { return Table[Clamp(cup)]; }

        /// 一場的顯示名稱,例如「島嶼大賽 反向」/ "Island GP (Mirror + Reverse)"。
        public static string RaceLabel(CupRace r, string lang)
        {
            bool en = lang == "en";
            string nm = DefaultTracks.NameOf(r.TrackId, en);
            if (!r.Mirror && !r.Reverse) return nm;
            if (en) return nm + " (" + (r.Mirror && r.Reverse ? "Mirror + Reverse" : (r.Mirror ? "Mirror" : "Reverse")) + ")";
            return nm + " " + (r.Mirror && r.Reverse ? "鏡像反向" : (r.Mirror ? "鏡像" : "反向"));
        }

        /// 把盃賽第 raceIndex 場(0..3)寫進設定:賽道、鏡像、反向、Cup、GrandPrix = true。
        public static void Apply(RaceConfig cfg, int cup, int raceIndex)
        {
            if (cfg == null) return;
            cup = Clamp(cup);
            var races = Table[cup];
            CupRace r = races[raceIndex < 0 ? 0 : (raceIndex >= races.Length ? races.Length - 1 : raceIndex)];
            cfg.TrackId = r.TrackId; cfg.Mirror = r.Mirror; cfg.Reverse = r.Reverse;
            cfg.Cup = cup; cfg.GrandPrix = true; cfg.TimeTrial = false; cfg.FreeRoam = false;
        }

        // ------------------------------------------------------------------ 計分

        /// 名次得分(1 起算;超出 1..8 或沒完賽(<= 0)= 0)。
        public static int Points(int place) { return place >= 1 && place <= PointTable.Length ? PointTable[place - 1] : 0; }

        /// 累計總分。placesPerRace[race][racer] = 該車在那一場的名次(1 起算;0 = 沒完賽)。回傳每台車的總分(索引 = 車的編號)。
        public static int[] Totals(IList<int[]> placesPerRace)
        {
            int racers = 0;
            for (int r = 0; r < placesPerRace.Count; r++) if (placesPerRace[r] != null && placesPerRace[r].Length > racers) racers = placesPerRace[r].Length;
            var tot = new int[racers];
            for (int r = 0; r < placesPerRace.Count; r++)
            {
                int[] pl = placesPerRace[r];
                if (pl == null) continue;
                for (int k = 0; k < pl.Length; k++) tot[k] += Points(pl[k]);
            }
            return tot;
        }

        /// 依總分排序後的車編號(第 1 名在最前面)。同分比較:第 1 名次數多者勝,再比第 2 名次數…,再比最後一場名次,最後比編號小者。
        /// placesPerRace 可以是 null(同分就只比編號)。
        public static int[] Ranking(int[] totals, IList<int[]> placesPerRace = null)
        {
            int n = totals.Length;
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Comparison<int> cmp = (a, b) =>
            {
                if (totals[a] != totals[b]) return totals[b].CompareTo(totals[a]);
                if (placesPerRace != null)
                {
                    for (int p = 1; p <= Racers; p++)
                    {
                        int ca = CountPlace(placesPerRace, a, p), cb = CountPlace(placesPerRace, b, p);
                        if (ca != cb) return cb.CompareTo(ca);
                    }
                    int la = LastPlace(placesPerRace, a), lb = LastPlace(placesPerRace, b);
                    if (la != lb) return la.CompareTo(lb);
                }
                return a.CompareTo(b);
            };
            // 插入排序(穩定,車數很少)
            for (int i = 1; i < n; i++)
            {
                int x = idx[i], j = i - 1;
                while (j >= 0 && cmp(x, idx[j]) < 0) { idx[j + 1] = idx[j]; j--; }
                idx[j + 1] = x;
            }
            return idx;
        }

        /// 每台車的最終名次(1 起算,索引 = 車編號)。
        public static int[] RankOf(int[] totals, IList<int[]> placesPerRace = null)
        {
            int[] order = Ranking(totals, placesPerRace);
            var rank = new int[totals.Length];
            for (int i = 0; i < order.Length; i++) rank[order[i]] = i + 1;
            return rank;
        }

        /// 盃賽獎盃:第 1 名金、第 2 名銀、第 3 名銅。
        public static CupTrophy Trophy(int rank) { return rank == 1 ? CupTrophy.Gold : rank == 2 ? CupTrophy.Silver : rank == 3 ? CupTrophy.Bronze : CupTrophy.None; }

        static int CountPlace(IList<int[]> races, int racer, int place)
        {
            int c = 0;
            for (int r = 0; r < races.Count; r++) if (races[r] != null && racer < races[r].Length && races[r][racer] == place) c++;
            return c;
        }

        static int LastPlace(IList<int[]> races, int racer)
        {
            for (int r = races.Count - 1; r >= 0; r--)
                if (races[r] != null && racer < races[r].Length && races[r][racer] > 0) return races[r][racer];
            return 99;
        }
    }
}
