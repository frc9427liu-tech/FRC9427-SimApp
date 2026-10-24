using UnityEngine;

namespace FrcSim
{
    /// 賽道色調 / 氣氛(模組 W,M5;M7 加 3..7)。每條賽道一個 theme 編號(TrackDef.theme):0 島嶼、1 峽谷(沙漠風)、2 海岸、3 草原、4 沙漠、5 雪地、6 森林、7 火山。
    /// 3..7 純資料(Water = 深坑 / 海的顏色:雪地 = 冰藍、火山 = 熔岩);TrackProps / PropScatter 對 theme > 2 暫時沿用島嶼造景,視覺升級由他們接手。
    /// 色票取自 RacePalette,只有沙漠與海岸的幾個顏色是新加的;RaceWorld.Build 會在建地形之前把 Current 設好。
    /// 材質一律走 RaceArt.Mat 快取;一次只會有一組 theme 在場景裡,所以不同材質數不會增加。
    public sealed class TrackTheme
    {
        public Color GrassLight, GrassDark, GrassHigh, Sand, Rock, Seabed, Water, Asphalt, Dirt;
        public Color MtnFoot, MtnRock, MtnSnow;
        public Color Sky, Horizon;
        public Color[] Leaves;
        public float FogStart, FogEnd, FarClip;

        static Color H(string hex) { Color c; ColorUtility.TryParseHtmlString(hex, out c); return c; }

        static TrackTheme island, canyon, coast, meadow, desert, snow, forest, volcano, dusk;

        public static TrackTheme Current { get; set; }

        /// theme 編號總數(0..Count-1 都有效)。
        public const int Count = 9;
        static readonly string[] NamesEn = { "Island", "Canyon", "Coast", "Meadow", "Desert", "Snow", "Forest", "Volcano", "Dusk Pass" };
        static readonly string[] NamesZh = { "島嶼", "峽谷", "海岸", "草原", "沙漠", "雪地", "森林", "火山", "黃昏山路" };
        public static string Name(int id, bool en) { id = Mathf.Clamp(id, 0, Count - 1); return en ? NamesEn[id] : NamesZh[id]; }

        public static TrackTheme Get(int id)
        {
            switch (id)
            {
                case 1: return canyon ?? (canyon = MakeCanyon());
                case 2: return coast ?? (coast = MakeCoast());
                case 3: return meadow ?? (meadow = MakeMeadow());
                case 4: return desert ?? (desert = MakeDesert());
                case 5: return snow ?? (snow = MakeSnow());
                case 6: return forest ?? (forest = MakeForest());
                case 7: return volcano ?? (volcano = MakeVolcano());
                case 8: return dusk ?? (dusk = MakeDusk());
                default: return island ?? (island = MakeIsland());
            }
        }

        // 草原:明亮的黃綠色丘陵、淺色沙、清澈的青綠色水。
        static TrackTheme MakeMeadow()
        {
            Color sand = H("#F3E6B3");
            return new TrackTheme
            {
                GrassLight = H("#A8E070"), GrassDark = H("#62B84C"), GrassHigh = H("#C4E888"),
                Sand = sand, Rock = H("#A9A59A"),
                Seabed = Color.Lerp(sand, H("#35C2C8"), 0.5f), Water = H("#35C2C8"),
                Asphalt = RacePalette.Asphalt, Dirt = Color.Lerp(sand, RacePalette.Asphalt, 0.3f),
                MtnFoot = H("#6DBF84"), MtnRock = H("#8FA2B8"), MtnSnow = Color.white,
                Sky = H("#62B8FF"), Horizon = H("#EAF7FF"),
                Leaves = new[] { H("#58CB5C"), H("#7AD95E"), H("#3FB24E") },
                FogStart = 320f, FogEnd = 1300f, FarClip = 1800f,
            };
        }

        // 沙漠:金黃沙丘、綠洲色的水、霧濛濛的暖色天空。
        static TrackTheme MakeDesert()
        {
            Color sand = H("#F2D58A");
            return new TrackTheme
            {
                GrassLight = H("#E8C77F"), GrassDark = H("#DDB96C"), GrassHigh = H("#D2A860"),
                Sand = sand, Rock = H("#C28A55"),
                Seabed = Color.Lerp(sand, H("#2FC1B0"), 0.45f), Water = H("#2FC1B0"),
                Asphalt = H("#59504E"), Dirt = Color.Lerp(sand, H("#C28A55"), 0.4f),
                MtnFoot = H("#D79B5C"), MtnRock = H("#B4713F"), MtnSnow = H("#F3DDB0"),
                Sky = H("#79C2EE"), Horizon = H("#FFE9C4"),
                Leaves = new[] { H("#7FA648"), H("#93B455"), H("#6A8F3B") },
                FogStart = 260f, FogEnd = 1100f, FarClip = 1800f,
            };
        }

        // 雪地:白藍色地面、冰藍色的水(深坑 = 冰河裂縫)、深松綠。
        static TrackTheme MakeSnow()
        {
            Color snowC = H("#E6F0F8");
            return new TrackTheme
            {
                GrassLight = snowC, GrassDark = H("#B2CCE2"), GrassHigh = H("#F7FBFE"),
                Sand = H("#9DB3C7"), Rock = H("#8FA3B8"),
                Seabed = Color.Lerp(snowC, H("#7FD0E8"), 0.5f), Water = H("#6FC8EC"),
                Asphalt = H("#3B404B"), Dirt = H("#A3B5C7"),
                MtnFoot = H("#B5CDE2"), MtnRock = H("#7388A2"), MtnSnow = H("#F4F9FF"),
                Sky = H("#6FAEEE"), Horizon = H("#D4E6F6"),
                Leaves = new[] { H("#2F6B4F"), H("#3C7F5E"), H("#245A42") },
                FogStart = 230f, FogEnd = 1000f, FarClip = 1800f,
            };
        }

        // 森林:深淺不一的綠、泥土色路肩、濕潤的藍綠色水、近一點的霧。
        static TrackTheme MakeForest()
        {
            Color sand = H("#B79B62");
            return new TrackTheme
            {
                GrassLight = H("#6FB24F"), GrassDark = H("#3A7A35"), GrassHigh = H("#86BB5C"),
                Sand = sand, Rock = H("#6E6A5B"),
                Seabed = Color.Lerp(sand, H("#2E8B8F"), 0.5f), Water = H("#2E8B8F"),
                Asphalt = H("#403D3B"), Dirt = Color.Lerp(sand, H("#5B4A33"), 0.4f),
                MtnFoot = H("#4E8F5A"), MtnRock = H("#667564"), MtnSnow = H("#DDE8DC"),
                Sky = H("#7FC4E8"), Horizon = H("#D5EBD0"),
                Leaves = new[] { H("#2C7A3A"), H("#3E9647"), H("#1F5F2E") },
                FogStart = 170f, FogEnd = 800f, FarClip = 1800f,
            };
        }

        // 火山:灰黑色的岩地與火山灰、橘紅色熔岩(深坑 = 熔岩河)、暗紅的黃昏天空。
        static TrackTheme MakeVolcano()
        {
            Color ash = H("#7C6054");
            return new TrackTheme
            {
                GrassLight = ash, GrassDark = H("#5F473F"), GrassHigh = H("#9A7762"),
                Sand = H("#9C725A"), Rock = H("#7A655E"),
                Seabed = H("#8A3416"), Water = H("#FF6A24"),
                Asphalt = H("#34343E"), Dirt = H("#85624C"),
                MtnFoot = H("#8A6350"), MtnRock = H("#6E5850"), MtnSnow = H("#B49C92"),
                Sky = H("#6A4A78"), Horizon = H("#FFB27A"),
                Leaves = new[] { H("#7A4B34"), H("#8C5A3C"), H("#663E2C") },
                FogStart = 220f, FogEnd = 950f, FarClip = 1800f,
            };
        }

        static TrackTheme MakeIsland()
        {
            var t = new TrackTheme
            {
                GrassLight = RacePalette.GrassLight, GrassDark = RacePalette.GrassDark,
                GrassHigh = Color.Lerp(RacePalette.GrassLight, RacePalette.Sand, 0.3f),
                Sand = RacePalette.Sand, Rock = RacePalette.Rock,
                Seabed = Color.Lerp(RacePalette.Sand, RacePalette.Water, 0.55f), Water = RacePalette.Water,
                Asphalt = RacePalette.Asphalt, Dirt = Color.Lerp(RacePalette.Sand, RacePalette.Asphalt, 0.35f),
                MtnFoot = new Color(0.42f, 0.70f, 0.58f), MtnRock = Color.Lerp(RacePalette.Rock, RacePalette.Water, 0.25f), MtnSnow = Color.white,
                Sky = Color.Lerp(RacePalette.SkyTop, RacePalette.Horizon, 0.25f), Horizon = RacePalette.Horizon,
                Leaves = RacePalette.Leaves,
                FogStart = 260f, FogEnd = 1100f, FarClip = 1800f,
            };
            return t;
        }

        static TrackTheme MakeCanyon()
        {
            Color sand = H("#EBC98B"), rock = H("#B5593A");
            var t = new TrackTheme
            {
                GrassLight = H("#D9A25E"), GrassDark = H("#C98A4B"), GrassHigh = H("#C47A45"),
                Sand = sand, Rock = rock,
                Seabed = Color.Lerp(sand, RacePalette.Water, 0.4f), Water = H("#35B0B8"),
                Asphalt = H("#4F4A4C"), Dirt = Color.Lerp(sand, rock, 0.35f),
                MtnFoot = H("#C77B4E"), MtnRock = H("#A24A31"), MtnSnow = H("#E8C9A0"),
                Sky = H("#7EC8F0"), Horizon = H("#FBE3C0"),
                Leaves = new[] { H("#8FA14B"), H("#A3A957"), H("#76853C") },
                FogStart = 220f, FogEnd = 950f, FarClip = 1800f,
            };
            return t;
        }

        static TrackTheme MakeCoast()
        {
            Color sand = H("#F5E2A8"), rock = H("#9AA5B5");
            var t = new TrackTheme
            {
                GrassLight = H("#8FD66A"), GrassDark = H("#6FBF55"), GrassHigh = H("#A5D071"),
                Sand = sand, Rock = rock,
                Seabed = Color.Lerp(sand, H("#2FA7E0"), 0.55f), Water = H("#2FA7E0"),
                Asphalt = RacePalette.Asphalt, Dirt = Color.Lerp(sand, RacePalette.Asphalt, 0.3f),
                MtnFoot = H("#5FA98A"), MtnRock = H("#7F8FA6"), MtnSnow = Color.white,
                Sky = H("#55BBFF"), Horizon = H("#E3F6FF"),
                Leaves = new[] { H("#2FAE5B"), H("#58CB6C"), H("#238A47") },
                FogStart = 300f, FogEnd = 1250f, FarClip = 1800f,
            };
            return t;
        }

        // 黃昏山路:紫粉天空、暖色楓葉、藍灰遠山(色票量自參考截圖:天頂 #5f5996 → 地平線 #d0885a)。
        static TrackTheme MakeDusk()
        {
            Color sand = H("#D9B48A");
            return new TrackTheme
            {
                GrassLight = H("#6FA84A"), GrassDark = H("#3F7A3C"), GrassHigh = H("#8FB85A"),
                Sand = sand, Rock = H("#B9B4B8"),
                Seabed = Color.Lerp(sand, H("#4A6FA8"), 0.5f), Water = H("#4A6FA8"),
                Asphalt = H("#3A3640"), Dirt = Color.Lerp(sand, H("#3A3640"), 0.3f),
                MtnFoot = H("#5E7A7E"), MtnRock = H("#7A6F9A"), MtnSnow = H("#F0D8E8"),
                Sky = H("#5F5996"), Horizon = H("#D0885A"),
                Leaves = new[] { H("#E0733A"), H("#C8453F"), H("#E8A04A") },
                FogStart = 200f, FogEnd = 1000f, FarClip = 1800f,
            };
        }
    }
}
