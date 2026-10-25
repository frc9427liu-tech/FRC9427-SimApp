using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽車自製 shader 工廠。shader 放在 Resources/Shaders,找不到或不支援就回退 Standard(避免粉紅)。
    public static class RaceShaders
    {
        static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        static readonly Dictionary<string, bool> warned = new Dictionary<string, bool>();
        static readonly Dictionary<long, Material> toons = new Dictionary<long, Material>();
        static Material water, sky, toonVc;

        /// 載入自製 shader;失敗回 null(呼叫端自行回退)。
        static Shader Load(string file, string findName)
        {
            if (shaders.TryGetValue(file, out var s) && s != null) return s;
            s = Resources.Load<Shader>("Shaders/" + file);
            if (s == null) s = Shader.Find(findName);
            if (s == null || !s.isSupported)
            {
                if (!warned.ContainsKey(file)) { warned[file] = true; Debug.LogWarning("[RaceShaders] " + file + " missing/unsupported; falling back to Standard"); }
                return null;
            }
            shaders[file] = s;
            return s;
        }

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        /// 載入 Resources/Racing/Textures/<name>(程式生成的原創貼圖)。tile = 可平鋪(Repeat + 各向異性 4 + 三線性)。找不到回 null。
        public static Texture2D Tex(string name, bool tile = false)
        {
            if (texCache.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("Racing/Textures/" + name);
            if (t == null) return null;
            t.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = tile ? FilterMode.Trilinear : FilterMode.Bilinear;
            if (tile) t.anisoLevel = 4;
            texCache[name] = t;
            return t;
        }

        static void SetTex(Material m, string prop, string name, bool tile)
        {
            if (m == null || !m.HasProperty(prop)) return;
            var t = Tex(name, tile);
            if (t != null) m.SetTexture(prop, t);
        }

        static Material Fallback(Color c, float smooth)
        {
            return FieldBuilder.MakeMat(c, smooth, 0f);
        }

        static long Key(Color c, float smooth, float outline)
        {
            Color32 a = c;
            return ((long)a.r << 40) ^ ((long)a.g << 32) ^ ((long)a.b << 24) ^ ((long)a.a << 16)
                ^ ((long)Mathf.RoundToInt(Mathf.Clamp(smooth, 0f, 1f) * 50f) << 8) ^ (long)Mathf.RoundToInt(Mathf.Clamp(outline, 0f, 0.03f) * 1000f) ^ ((long)1 << 56);
        }

        /// 卡通材質。smooth 0..1 控制高光量;outline > 0 才開描邊(螢幕空間寬度,建議 0.004~0.008)。快取。
        public static Material Toon(Color c, float smooth = 0.1f, float outline = 0f)
        {
            long k = Key(c, smooth, outline);
            if (toons.TryGetValue(k, out var m) && m != null) return m;
            Shader sh = Load("RaceToon", "FrcSim/RaceToon");
            if (sh == null) m = Fallback(c, smooth);
            else
            {
                m = new Material(sh);
                m.color = c;
                m.SetFloat("_SpecAmount", Mathf.Clamp01(smooth) * 0.5f);
                SetTex(m, "_CelTex", "cel_ramp", false);
                if (m.HasProperty("_CelTex") && m.GetTexture("_CelTex") != null) m.SetFloat("_CelAmount", 0.8f);
                if (outline > 0f)   // 車身:matcap 高光 + 邊緣光
                {
                    SetTex(m, "_MatCap", "matcap_kart", false);
                    if (m.HasProperty("_MatCap") && m.GetTexture("_MatCap") != null) m.SetFloat("_MatCapAmount", 0.5f);
                }
                if (outline > 0f) m.SetFloat("_OutlineWidth", outline);
                else m.SetShaderPassEnabled("Always", false);
            }
            m.name = "RaceToon";
            toons[k] = m;
            return m;
        }

        /// 頂點色卡通材質(單例):顏色 = 頂點色 x 白。合併網格(樹 / 灌木 / 石頭 / 遠景山)用,一個材質畫出各種顏色。shader 找不到回傳 null(呼叫端自行改走後備)。
        public static Material ToonVC()
        {
            if (toonVc != null) return toonVc;
            Shader sh = Load("RaceToonVC", "FrcSim/RaceToonVC");
            if (sh == null) return null;
            toonVc = new Material(sh);
            toonVc.color = Color.white;
            SetTex(toonVc, "_CelTex", "cel_ramp", false);
            if (toonVc.GetTexture("_CelTex") != null) toonVc.SetFloat("_CelAmount", 0.8f);
            Texture2DArray ta = RaceTexArray.Get();
            if (ta != null && toonVc.HasProperty("_TexArr")) toonVc.SetTexture("_TexArr", ta);
            toonVc.name = "RaceToonVC";
            return toonVc;
        }

        static readonly Dictionary<long, Material> roads = new Dictionary<long, Material>();

        /// 路面材質(RaceRoad shader:磨痕 / 淺邊 / 顆粒,靠 uv.x = 橫向、uv.y = 半寬)。wear / edge = 0 時只剩顆粒(路肩用)。找不到 shader 回退 RaceArt.Mat。
        public static Material Road(Color c, float wear, float edge, float grain = 0.07f)
        {
            long k = Key(c, wear, 0f) ^ ((long)Mathf.RoundToInt(edge * 100f) << 3) ^ ((long)Mathf.RoundToInt(grain * 100f) << 20);
            if (roads.TryGetValue(k, out var m) && m != null) return m;
            Shader sh = Load("RaceRoad", "FrcSim/RaceRoad");
            if (sh == null) return RaceArt.Mat(c, 0.05f);
            m = new Material(sh);
            m.color = c;
            m.SetFloat("_Wear", wear); m.SetFloat("_EdgeLight", edge); m.SetFloat("_Grain", grain);
            RaceLook lk = RaceLook.For(TrackTheme.Current ?? TrackTheme.Get(0));
            m.SetColor("_ShadowColor", Color.Lerp(Color.white, lk.ShadeTint, 0.8f));
            SetTex(m, "_AsphaltTex", "asphalt_albedo", true);
            SetTex(m, "_WearTex", "asphalt_wear", true);
            m.SetFloat("_TexAmount", wear > 0f ? 3.2f : 2.4f);
            m.SetFloat("_WearTexAmount", wear > 0f ? 1.0f : 0f);
            m.name = "RaceRoad";
            roads[k] = m;
            return m;
        }

        static Material cutout;
        /// 路邊草簇 / 花叢十字面片材質(單例;alpha 裁切,一個材質兩張貼圖)。shader 找不到回 null。
        public static Material Cutout()
        {
            if (cutout != null) return cutout;
            Shader sh = Load("RaceCutout", "FrcSim/RaceCutout");
            if (sh == null) return null;
            cutout = new Material(sh) { name = "RaceCutout" };
            foreach (var pair in new[] { new[] { "_MainTex", "grass_tuft" }, new[] { "_FlowerTex", "flower_cluster" }, new[] { "_SignTex", "sign_atlas" } })
            {
                Texture2D t = Tex(pair[1], false);
                if (t == null) continue;
                if (t.mipmapCount > 1) { t.filterMode = FilterMode.Trilinear; t.anisoLevel = 2; }
                cutout.SetTexture(pair[0], t);
            }
            return cutout;
        }

        static Material blob;
        /// 接觸陰影貼片材質(單例)。
        public static Material Blob()
        {
            if (blob != null) return blob;
            Shader sh = Load("RaceBlob", "FrcSim/RaceBlob");
            if (sh == null) return null;
            blob = new Material(sh) { name = "RaceBlob" };
            SetTex(blob, "_MainTex", "blob_shadow", false);
            return blob;
        }

        /// 水面(半透明,單例)。要有海岸泡沫/深淺請對比賽相機呼叫 EnableDepth。
        public static Material Water()
        {
            if (water != null) return water;
            Color wc = (TrackTheme.Current ?? TrackTheme.Get(0)).Water;
            Shader sh = Load("RaceWater", "FrcSim/RaceWater");
            if (sh == null) { water = Fallback(wc, 0.55f); }
            else
            {
                water = new Material(sh);
                water.color = wc;
                water.SetColor("_DeepColor", Color.Lerp(wc, new Color(0.1f, 0.3f, 0.7f, 1f), 0.5f));
                if (RaceLook.For(TrackTheme.Current ?? TrackTheme.Get(0)).Kind == LookKind.Volcano) water.SetFloat("_Lava", 1f);
            }
            water.name = "RaceWater";
            return water;
        }

        /// 水面顏色改主題時用。
        public static void SetWaterColor(Color shallow)
        {
            var w = Water();
            w.color = shallow;
            if (w.HasProperty("_DeepColor")) w.SetColor("_DeepColor", Color.Lerp(shallow, new Color(0.1f, 0.3f, 0.7f, 1f), 0.5f));
        }

        /// 對相機開深度貼圖,水面的海岸泡沫與深淺漸層才會啟用。
        public static void EnableDepth(Camera cam)
        {
            if (cam != null) cam.depthTextureMode |= DepthTextureMode.Depth;
            Shader.SetGlobalFloat("_RaceWaterUseDepth", 1f);
        }

        /// 天空材質(單例),套在任何球體上(見 CreateSkyDome)。
        public static Material Sky()
        {
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
            RaceLook lk = RaceLook.For(th);
            if (sky == null)
            {
                Shader sh = Load("RaceSky", "FrcSim/RaceSky");
                if (sh == null) { sky = Fallback(th.Horizon, 0f); }
                else
                {
                    sky = new Material(sh);
                    Vector3 sd = new Vector3(0.4f, 0.6f, 0.3f);
                    var sun = RenderSettings.sun;
                    if (sun != null) sd = -sun.transform.forward;
                    sky.SetVector("_SunDir", new Vector4(sd.x, sd.y, sd.z, 0f));
                }
                SetTex(sky, "_GlowTex", "sun_glow", false);
                sky.name = "RaceSky";
            }
            // 每次取用都依目前主題重設顏色(單例跨賽道重用):天頂、地平線 = 霧色,太陽與雲的色調跟光色一致
            if (sky.HasProperty("_TopColor"))
            {
                sky.SetColor("_TopColor", RaceLook.SkyTop(th));
                sky.SetColor("_HorizonColor", th.Horizon);
                sky.SetColor("_SunColor", Color.Lerp(new Color(1f, 0.93f, 0.75f), lk.SunColor, 0.5f));
                sky.SetColor("_CloudColor", Color.Lerp(Color.white, th.Horizon, 0.25f));
                sky.SetFloat("_CloudAmount", lk.CloudAmount);
            }
            else sky.color = th.Horizon;
            return sky;
        }

        /// 建一顆天空球(無碰撞、不投影)。shader 內以相機為中心重算位置,所以放哪都行;scale 只為避免被視錐裁掉。
        public static GameObject CreateSkyDome(Transform parent)
        {
            // 氣氛:RaceSession 在 RaceWorld.Build 之後才建天空球,所以這裡套主題的霧色 / 霧距 / 環境光 / 太陽色最合適
            // (RaceWorld.ApplyAtmosphere 已經先設過一次基本值;Env.Restore 會把這些全部還原)
            RaceLook.ApplyMood(TrackTheme.Current ?? TrackTheme.Get(0));
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "RaceSkyDome";
            var col = g.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.localScale = Vector3.one * 8000f;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = Sky();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        /// 地形材質:pal 是 TerrainChunks 的 palette 貼圖。每次回傳新材質,由呼叫端持有/銷毀。
        public static Material Terrain(Texture2D pal)
        {
            Shader sh = Load("RaceTerrain", "FrcSim/RaceTerrain");
            Material m;
            if (sh == null) { m = Fallback(Color.white, 0.05f); }
            else
            {
                m = new Material(sh);
                m.color = Color.white;
                TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
                RaceLook lk = RaceLook.For(th);
                m.SetColor("_FarColor", th.Horizon);                 // 遠景色 = 霧色 = 天空地平線色(接縫看不出來)
                m.SetColor("_PatchTint", lk.PatchTint);
                m.SetColor("_ShadowColor", lk.ShadeTint);
                m.SetFloat("_NoiseAmount", lk.NoiseAmount);
                m.SetFloat("_Crack", lk.CrackAmount);
                m.SetFloat("_Speckle", lk.Speckle);
                m.SetFloat("_Sparkle", lk.Sparkle);
                m.SetFloat("_FarStart", Mathf.Max(60f, RaceLook.FogStart(th) * 0.75f));
                m.SetFloat("_FarEnd", RaceLook.FogEnd(th));
                // 地面貼圖:依風格挑主貼圖(草 / 雪 / 沙 / 土),陡坡混岩土;熔岩用滾動的發光裂縫貼圖
                string main, rock = "dirt_albedo";
                switch (lk.Kind)
                {
                    case LookKind.Snow: main = "snow_albedo"; rock = "dirt_albedo"; break;
                    case LookKind.Desert: main = "sand_dirt_albedo"; rock = "dirt_albedo"; break;
                    case LookKind.Volcano: main = "dirt_albedo"; rock = "sand_dirt_albedo"; break;
                    default: main = "grass_albedo"; break;
                }
                SetTex(m, "_DetailTex", main, true);
                SetTex(m, "_RockTex", rock, true);
                SetTex(m, "_LavaTex", "lava_emissive", true);
                float amt = lk.Kind == LookKind.Snow ? 5f : (lk.Kind == LookKind.Desert ? 5f : (lk.Kind == LookKind.Volcano ? 3.5f : 3.2f));
                m.SetFloat("_DetailAmount", amt);
                m.SetFloat("_DetailScale", lk.Kind == LookKind.Snow ? 0.18f : 0.26f);
            }
            m.name = "RaceTerrainMat";
            m.mainTexture = pal;
            return m;
        }

        public static void ClearCache()
        {
            foreach (var m in toons.Values) if (m != null) Object.Destroy(m);
            toons.Clear();
            if (water != null) Object.Destroy(water); water = null;
            if (sky != null) Object.Destroy(sky); sky = null;
            if (toonVc != null) Object.Destroy(toonVc); toonVc = null;
            if (blob != null) Object.Destroy(blob); blob = null;
            if (cutout != null) Object.Destroy(cutout); cutout = null;
            foreach (var rm in roads.Values) if (rm != null) Object.Destroy(rm);
            roads.Clear();
        }
    }
}
