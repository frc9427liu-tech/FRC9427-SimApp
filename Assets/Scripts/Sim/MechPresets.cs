using UnityEngine;

namespace FrcSim
{
    // 5 款自建機構(內建行為,不用機器人程式):各自有外觀與手感差異,選了就用內建模式跑
    public static class MechPresets
    {
        public class P { public string ZhName, EnName, ZhDesc, EnDesc, Model; public float FireInt, Collect, Spread; }
        public static readonly P[] All =
        {
            new P { ZhName = "平衡型", EnName = "Balanced", ZhDesc = "吸球 7 顆/秒、發射 8 發/秒,各項均衡", EnDesc = "7 intake/s, 8 shots/s — all-rounder",
                Model = "proc:0.72,0.74,0.55,#28B0FF", FireInt = 0f, Collect = 7f, Spread = 0f },
            new P { ZhName = "速射型", EnName = "Rapid fire", ZhDesc = "發射 14 發/秒但散布大,適合近距離洗分", EnDesc = "14 shots/s but wide spread — close-range spam",
                Model = "proc:0.70,0.70,0.55,#FF3B30", FireInt = 0.07f, Collect = 7f, Spread = 4.5f },
            new P { ZhName = "狙擊手", EnName = "Sniper", ZhDesc = "發射 3 發/秒、幾乎零散布,遠距離穩穩進", EnDesc = "3 shots/s, near-zero spread — long-range accuracy",
                Model = "proc:0.78,0.68,0.62,#B061FF", FireInt = 0.33f, Collect = 7f, Spread = 0.2f },
            new P { ZhName = "吸球怪", EnName = "Vacuum", ZhDesc = "吸球 11 顆/秒,發射偏慢;擅長搶中立區球堆", EnDesc = "11 intake/s, slower fire — wins the neutral zone",
                Model = "proc:0.68,0.80,0.50,#FFD60A", FireInt = 0.18f, Collect = 11f, Spread = 1.2f },
            new P { ZhName = "重裝型", EnName = "Heavy hopper", ZhDesc = "吸球偏慢(4 顆/秒),但射擊穩定連發", EnDesc = "slow intake (4/s) but steady rapid volleys",
                Model = "proc:0.84,0.84,0.60,#34C759", FireInt = 0.10f, Collect = 4f, Spread = 1.8f },
        };

        public static int Index { get => Mathf.Clamp(Prefs.GetInt("mechPreset", 0), 0, All.Length - 1); set { Prefs.SetInt("mechPreset", value); PlayerPrefs.Save(); } }

        // 選用:使用內建行為 + 該機構外觀
        public static void Choose(int i)
        {
            Index = i; var p = All[i];
            Prefs.SetInt("useRealCode", 0); Prefs.SetString("robotModel", p.Model); PlayerPrefs.DeleteKey("modelYaw"); PlayerPrefs.Save();
        }

        // 內建行為時把手感套到玩家機構(真實程式模式不套用)
        public static void Apply(RobotMechanisms m)
        {
            if (m == null || Prefs.GetInt("useRealCode", 0) == 1 || !PlayerPrefs.HasKey("mechPreset")) return;
            var p = All[Index]; m.FireInterval = p.FireInt; m.CollectPerSec = p.Collect; m.SpreadDeg = p.Spread;
        }
    }
}