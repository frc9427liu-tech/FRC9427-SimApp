using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// 賽車世界的共用貼圖陣列(Texture2DArray):RaceToonVC 以「頂點 uv0.z = 切片編號」取樣,
    /// 所以樹冠 / 松針 / 樹皮 / 山 / 磚 / 石 / 木 / 屋瓦 / 人群 / 橫幅……全部共用 1 個材質、1 個 draw call(材質數不增加)。
    /// uv0.z = 0 代表「沒有貼圖」(舊的純頂點色網格);切片 k(1 起算)對應 Names[k - 1]。
    public static class RaceTexArray
    {
        public const int Leafy = 1, Autumn = 2, Blossom = 3, Pine = 4, Bark = 5, MtnStrata = 6, MtnSnow = 7, MtnVolcano = 8,
            Brick = 9, BrickBlue = 10, Stone = 11, Wood = 12, Sand = 13, Metal = 14, RoofRed = 15, RoofBlue = 16, Banner = 17, Crowd = 18,
            Rock = 19, MtnMesa = 20, MtnForest = 21, Ice = 22, Mud = 23, MossRock = 24, Lattice = 25;

        /// 切片 1..DetailMax = 「細節」模式(貼圖亮度當作細節乘在頂點色上,色相由頂點色決定);其餘 = 貼圖原色 x 頂點色。
        public const int DetailMax = 5;

        public static readonly string[] Names =
        {
            "tree_canopy_leafy", "tree_canopy_autumn", "tree_canopy_blossom", "pine_needles", "bark_albedo",
            "mountain_strata", "mountain_snow", "mountain_volcano",
            "brick_wall", "brick_wall_blue", "stone_wall", "wood_planks", "sand_ripples", "metal_panel", "roof_tiles_red", "roof_tiles_blue",
            "banner_stripes", "crowd_atlas", "rock_gradient_atlas", "mountain_mesa", "mountain_forest", "ice_tile", "mud_tile", "moss_rock", "retaining_wall_lattice",
        };

        public const int Size = 256;
        static Texture2DArray arr;

        public static bool IsMountain(int slice) { return slice == MtnStrata || slice == MtnSnow || slice == MtnVolcano || slice == MtnMesa || slice == MtnForest; }

        /// 建(或取)貼圖陣列;無圖形裝置(-nographics)或貼圖讀不到時回 null(網格仍可建,只是沒貼圖)。
        public static Texture2DArray Get()
        {
            if (arr != null) return arr;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            try
            {
                var a = new Texture2DArray(Size, Size, Names.Length, TextureFormat.RGBA32, true, false);
                a.wrapMode = TextureWrapMode.Repeat; a.filterMode = FilterMode.Trilinear; a.anisoLevel = 4;
                a.name = "RaceTexArray";
                var px = new Color32[Size * Size];
                for (int s = 0; s < Names.Length; s++)
                {
                    Texture2D t = Resources.Load<Texture2D>("Racing/Textures/" + Names[s]);
                    bool ok = t != null && t.isReadable;
                    float mean = 0f;
                    var col = new Color[Size * Size];
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                        {
                            Color c = ok ? t.GetPixelBilinear((x + 0.5f) / Size, (y + 0.5f) / Size) : new Color(0.7f, 0.7f, 0.7f, 1f);
                            col[y * Size + x] = c;
                            mean += c.grayscale;
                        }
                    mean /= col.Length;
                    // 細節模式:亮度正規化到 0.5(shader 再 x2),讓「頂點色 x 貼圖」平均亮度不變
                    float k = (s + 1 <= DetailMax) ? 0.5f / Mathf.Max(0.05f, mean) : 1f;
                    for (int i = 0; i < px.Length; i++)
                    {
                        Color c = col[i] * k; c.a = 1f;
                        px[i] = (Color32)c;
                    }
                    a.SetPixels32(px, s);
                }
                a.Apply(true, true);
                arr = a;
            }
            catch (System.Exception e) { Debug.LogWarning("[RaceTexArray] " + e.Message); arr = null; }
            return arr;
        }

        public static void Clear() { if (arr != null) Object.Destroy(arr); arr = null; }
    }
}
