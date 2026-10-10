namespace FrcSim
{
    // 本場玩家射擊統計:依「發射時離 HUB 的距離」分 5 段記錄發射與進球(成績卡、投籃訓練用)
    public static class ShotLog
    {
        public static readonly int[] Att = new int[5], Hit = new int[5];
        public static readonly string[] Names = { "< 2 m", "2–3 m", "3–4 m", "4–5 m", "≥ 5 m" };
        public static int DrillShots, DrillHits;
        public static int Bin(float d) { return d < 2f ? 0 : d < 3f ? 1 : d < 4f ? 2 : d < 5f ? 3 : 4; }
        public static void Reset() { for (int i = 0; i < 5; i++) { Att[i] = 0; Hit[i] = 0; } DrillShots = DrillHits = 0; }
        public static void AddShot(float d) { Att[Bin(d)]++; DrillShots++; }
        public static void AddHit(float d) { Hit[Bin(d)]++; DrillHits++; }
    }
}