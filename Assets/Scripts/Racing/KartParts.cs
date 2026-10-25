using UnityEngine;

namespace FrcSim
{
    /// 四件套零件種類。
    public enum PartKind { Driver, Body, Tire, Glider }

    /// 角色體重級別(由角色的重量屬性推得,UI 顯示輕 / 中 / 重量級)。
    public enum WeightClass { Light, Medium, Heavy }

    /// 四件套組合結果。Stats = 實際物理數值(已套排氣量);Bars / Stars = UI 的六條屬性。
    public sealed class LoadoutResult
    {
        public KartStats Stats;
        /// 六條屬性 0..1,順序 = KartLoadout.StatCount:Speed / Accel / Weight / Handling / Traction / MiniTurbo。
        public float[] Bars = new float[KartLoadout.StatCount];
        /// 同上換成 0..5 星(Bars x 5)。
        public float[] Stars = new float[KartLoadout.StatCount];
        public float OffRoad;           // 0..1 -> RaceKart.OffRoad
        public float GlideLift;         // 約 0.9~1.2 -> RaceKart.GlideLift
        public WeightClass Class;       // 角色體重級別
        public KartStyle BodyStyle;     // 車身對應的外觀款式(0..3 一對一;4..7 借用相近的外觀)
    }

    /// 四件套屬性系統(模組 K6,docs/racing/05 §2):角色 8 位(原創動物)x 車身 8 款 x 輪胎 6 種 x 滑翔翼 6 款。
    /// 每個零件有六項屬性 Speed / Accel / Weight / Handling / Traction / MiniTurbo(0..1.0),四件加權相加(角色 .30、車身 .35、輪胎 .25、滑翔翼 .10,權重和 = 1,
    /// 所以結果仍是 0..1)再線性映射成 KartStats。全部取 0.5 = 現有 Brick 150cc 預設(KartPresets.For(KartStyle.Brick, 150))。
    /// 注意:類別叫 KartLoadout 而不是 KartParts,因為 KartModels.cs 已經有一個外觀用的 KartParts 類別。
    public static class KartLoadout
    {
        public const int StatCount = 6;
        public const int DriverCount = 8, BodyCount = 8, TireCount = 6, GliderCount = 6;
        public const float WDriver = 0.30f, WBody = 0.35f, WTire = 0.25f, WGlider = 0.10f;

        // 順序:Speed, Accel, Weight, Handling, Traction, MiniTurbo
        static readonly float[][] Drivers =
        {
            new[] { 0.50f, 0.50f, 0.50f, 0.50f, 0.50f, 0.50f },   // 0 麻糬(貓,中量級,標準)
            new[] { 0.55f, 0.45f, 0.55f, 0.50f, 0.55f, 0.45f },   // 1 餅乾(狗,中量級,穩)
            new[] { 0.35f, 0.75f, 0.20f, 0.70f, 0.45f, 0.65f },   // 2 幸運草(兔,輕量級,起步快)
            new[] { 0.30f, 0.70f, 0.15f, 0.75f, 0.50f, 0.70f },   // 3 啾啾(鴨,輕量級,最靈活)
            new[] { 0.80f, 0.25f, 0.85f, 0.30f, 0.45f, 0.30f },   // 4 布魯諾(熊,重量級)
            new[] { 0.55f, 0.60f, 0.45f, 0.60f, 0.40f, 0.55f },   // 5 小楓(狐,中量級,靈巧)
            new[] { 0.90f, 0.15f, 0.95f, 0.20f, 0.40f, 0.25f },   // 6 壯壯(犀牛,重量級,最高速)
            new[] { 0.40f, 0.65f, 0.30f, 0.60f, 0.60f, 0.55f },   // 7 絨絨(羊,輕量級,抓地好)
        };
        static readonly float[][] Bodies =
        {
            new[] { 0.50f, 0.50f, 0.50f, 0.50f, 0.50f, 0.50f },   // 0 Brick 磚塊 = 預設中量級
            new[] { 0.40f, 0.86f, 0.11f, 0.70f, 0.75f, 0.50f },   // 1 Bullet 子彈(輕、加速快、轉得靈)
            new[] { 0.90f, 0.14f, 0.94f, 0.15f, 0.25f, 0.45f },   // 2 Chonk 胖胖(重、極速高、轉得鈍)
            new[] { 0.62f, 0.57f, 0.50f, 0.62f, 0.62f, 0.50f },   // 3 Robot9427(均衡偏靈活)
            new[] { 0.75f, 0.35f, 0.60f, 0.40f, 0.45f, 0.45f },   // 4 巡洋艦(高速取向)
            new[] { 0.35f, 0.70f, 0.25f, 0.65f, 0.55f, 0.70f },   // 5 小綿羊(輕巧,迷你渦輪強)
            new[] { 0.60f, 0.30f, 0.80f, 0.35f, 0.75f, 0.40f },   // 6 推土機(重、抓地好)
            new[] { 0.70f, 0.55f, 0.40f, 0.55f, 0.35f, 0.60f },   // 7 彗星(快但滑)
        };
        static readonly float[][] Tires =
        {
            new[] { 0.50f, 0.50f, 0.50f, 0.50f, 0.50f, 0.50f },   // 0 標準
            new[] { 0.40f, 0.45f, 0.60f, 0.40f, 0.85f, 0.35f },   // 1 越野(抓地強,草地 / 泥地 / 沙地少減速)
            new[] { 0.75f, 0.40f, 0.35f, 0.35f, 0.25f, 0.60f },   // 2 滑胎(速度快、抓地差)
            new[] { 0.70f, 0.30f, 0.75f, 0.35f, 0.40f, 0.30f },   // 3 金屬(重、速度高)
            new[] { 0.30f, 0.75f, 0.20f, 0.70f, 0.65f, 0.75f },   // 4 柔軟(加速、操控、迷你渦輪)
            new[] { 0.60f, 0.40f, 0.65f, 0.45f, 0.50f, 0.45f },   // 5 硬質
        };
        static readonly float[] TireOffRoad = { 0.10f, 1.00f, 0.00f, 0.30f, 0.20f, 0.20f };
        static readonly float[] BodyOffRoad = { 0f, 0f, 0.1f, 0f, 0f, 0f, 0.3f, 0f };
        static readonly float[][] Gliders =
        {
            new[] { 0.50f, 0.50f, 0.50f, 0.50f, 0.50f, 0.50f },   // 0 降落傘(中性、浮得久)
            new[] { 0.60f, 0.45f, 0.30f, 0.45f, 0.45f, 0.50f },   // 1 紙飛機(快但下降快)
            new[] { 0.55f, 0.55f, 0.50f, 0.50f, 0.50f, 0.55f },   // 2 翼
            new[] { 0.40f, 0.55f, 0.35f, 0.65f, 0.60f, 0.60f },   // 3 花朵
            new[] { 0.50f, 0.60f, 0.45f, 0.50f, 0.45f, 0.65f },   // 4 星星
            new[] { 0.45f, 0.50f, 0.55f, 0.60f, 0.65f, 0.45f },   // 5 雲(最浮)
        };
        static readonly float[] GliderLift = { 1.15f, 0.90f, 1.00f, 1.05f, 1.00f, 1.20f };
        static readonly KartStyle[] BodyLook =
        {
            KartStyle.Brick, KartStyle.Bullet, KartStyle.Chonk, KartStyle.Robot9427,
            KartStyle.Brick, KartStyle.Bullet, KartStyle.Chonk, KartStyle.Bullet
        };

        static readonly string[] DriverZh = { "麻糬", "餅乾", "幸運草", "啾啾", "布魯諾", "小楓", "壯壯", "絨絨" };
        static readonly string[] DriverEn = { "Mochi", "Biscuit", "Clover", "Pip", "Bruno", "Maple", "Tank", "Fluff" };
        static readonly string[] DriverAnimalZh = { "貓", "狗", "兔", "鴨", "熊", "狐", "犀牛", "羊" };
        static readonly string[] DriverAnimalEn = { "Cat", "Dog", "Rabbit", "Duck", "Bear", "Fox", "Rhino", "Sheep" };
        static readonly string[] BodyZh = { "磚塊", "子彈", "胖胖", "機器人 9427", "巡洋艦", "小綿羊", "推土機", "彗星" };
        static readonly string[] BodyEn = { "Brick", "Bullet", "Chonk", "Robot 9427", "Cruiser", "Scooter", "Dozer", "Comet" };
        static readonly string[] TireZh = { "標準", "越野", "滑胎", "金屬", "柔軟", "硬質" };
        static readonly string[] TireEn = { "Standard", "Off-road", "Slick", "Metal", "Soft", "Hard" };
        static readonly string[] GliderZh = { "降落傘", "紙飛機", "翼", "花朵", "星星", "雲" };
        static readonly string[] GliderEn = { "Parachute", "Paper Plane", "Wing", "Flower", "Star", "Cloud" };
        static readonly string[] StatZh = { "速度", "加速", "重量", "操控", "抓地", "迷你渦輪" };
        static readonly string[] StatEn = { "Speed", "Acceleration", "Weight", "Handling", "Traction", "Mini-Turbo" };

        static bool En(string lang) { return lang != null && lang.StartsWith("en"); }
        static int C(int id, int n) { return Mathf.Clamp(id, 0, n - 1); }

        public static int Count(PartKind kind)
        {
            switch (kind) { case PartKind.Driver: return DriverCount; case PartKind.Body: return BodyCount; case PartKind.Tire: return TireCount; default: return GliderCount; }
        }

        /// 零件名稱(lang = "zh" 或 "en",與 Loc.Lang 同;id 超出範圍會夾到有效值)。
        public static string Name(PartKind kind, int id, string lang)
        {
            bool en = En(lang);
            switch (kind)
            {
                case PartKind.Driver: return (en ? DriverEn : DriverZh)[C(id, DriverCount)];
                case PartKind.Body: return (en ? BodyEn : BodyZh)[C(id, BodyCount)];
                case PartKind.Tire: return (en ? TireEn : TireZh)[C(id, TireCount)];
                default: return (en ? GliderEn : GliderZh)[C(id, GliderCount)];
            }
        }
        public static string DriverName(int id, string lang) { return Name(PartKind.Driver, id, lang); }
        public static string BodyName(int id, string lang) { return Name(PartKind.Body, id, lang); }
        public static string TireName(int id, string lang) { return Name(PartKind.Tire, id, lang); }
        public static string GliderName(int id, string lang) { return Name(PartKind.Glider, id, lang); }
        /// 角色的動物種類(貓、狗、兔…),給角色選擇畫面的副標題。
        public static string DriverAnimal(int id, string lang) { return (En(lang) ? DriverAnimalEn : DriverAnimalZh)[C(id, DriverCount)]; }
        /// 六條屬性的名稱(index 0..5)。
        public static string StatName(int index, string lang) { return (En(lang) ? StatEn : StatZh)[Mathf.Clamp(index, 0, StatCount - 1)]; }

        /// 單一零件的六項屬性(0..1;回傳複本)。UI 預覽單件時用。
        public static float[] PartStats(PartKind kind, int id)
        {
            float[][] t = kind == PartKind.Driver ? Drivers : kind == PartKind.Body ? Bodies : kind == PartKind.Tire ? Tires : Gliders;
            return (float[])t[C(id, t.Length)].Clone();
        }

        public static WeightClass ClassOf(float weight01)
        {
            return weight01 < 0.33f ? WeightClass.Light : weight01 < 0.66f ? WeightClass.Medium : WeightClass.Heavy;
        }
        public static WeightClass DriverClass(int driverId) { return ClassOf(Drivers[C(driverId, DriverCount)][2]); }

        /// 外觀款式(RaceArt.BuildKart / KartVisual 用):車身 0..3 對應 KartStyle,4..7 借用相近外觀。
        public static KartStyle BodyStyle(int bodyId) { return BodyLook[C(bodyId, BodyCount)]; }

        /// 四件套 + 排氣量 -> 屬性。id 超出範圍會夾到有效值(不丟例外)。cc 預設 150。
        /// 映射(0.5 = Brick):Speed -> 極速 24 x [0.92, 1.08];Accel -> A0 [12.5, 19.5]、tau [1.2, 0.8];Weight -> 質量 [115, 205]、煞車 [30, 26];
        /// Handling -> 轉向率 低速 [100, 140]、高速 [60, 90];Traction -> 抓地 [10, 14]、漂移抓地 [2.0, 3.0];MiniTurbo -> 迷你渦輪倍率 [1.18, 1.32]。最後套 KartPresets.ApplyCC。
        public static LoadoutResult Combine(int driverId, int bodyId, int tireId, int gliderId, int cc = 150)
        {
            int d = C(driverId, DriverCount), b = C(bodyId, BodyCount), t = C(tireId, TireCount), g = C(gliderId, GliderCount);
            var r = new LoadoutResult();
            for (int i = 0; i < StatCount; i++)
            {
                float v = WDriver * Drivers[d][i] + WBody * Bodies[b][i] + WTire * Tires[t][i] + WGlider * Gliders[g][i];
                v = Mathf.Clamp01(v);
                r.Bars[i] = v; r.Stars[i] = v * 5f;
            }
            float sp = r.Bars[0], ac = r.Bars[1], wt = r.Bars[2], hd = r.Bars[3], tr = r.Bars[4], mt = r.Bars[5];

            var k = new KartStats();
            k.TopSpeed = KartPresets.BaseTopSpeed * Mathf.Lerp(0.92f, 1.08f, sp);
            k.Accel = Mathf.Lerp(12.5f, 19.5f, ac);
            k.AccelTau = Mathf.Lerp(1.2f, 0.8f, ac);
            k.Mass = Mathf.Lerp(115f, 205f, wt);
            k.BrakeDecel = Mathf.Lerp(30f, 26f, wt);
            k.TurnRateLow = Mathf.Lerp(100f, 140f, hd);
            k.TurnRateHigh = Mathf.Lerp(60f, 90f, hd);
            k.Grip = Mathf.Lerp(10f, 14f, tr);
            k.DriftGrip = Mathf.Lerp(2.0f, 3.0f, tr);
            k.BoostMul = Mathf.Lerp(1.18f, 1.32f, mt);
            KartPresets.ApplyCC(k, cc);
            r.Stats = k;
            r.OffRoad = Mathf.Clamp01(TireOffRoad[t] + BodyOffRoad[b]);
            r.GlideLift = GliderLift[g];
            r.Class = ClassOf(Drivers[d][2]);
            r.BodyStyle = BodyLook[b];
            return r;
        }

        public static LoadoutResult Combine(RaceConfig cfg)
        {
            if (cfg == null) return Combine(0, 0, 0, 0, 150);
            return Combine(cfg.DriverId, cfg.BodyId, cfg.TireId, cfg.GliderId, cfg.CC);
        }

        /// 把組合套到一台車:Stats、質量、OffRoad、GlideLift(整合者在 RaceSession 建車時呼叫一行即可)。
        public static void Apply(RaceKart kart, RaceConfig cfg)
        {
            if (kart == null || cfg == null) return;
            LoadoutResult r = Combine(cfg);
            kart.Stats = r.Stats;
            kart.OffRoad = r.OffRoad;
            kart.GlideLift = r.GlideLift;
        }
    }
}
