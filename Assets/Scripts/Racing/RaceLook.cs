using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽道「外觀風格」表(模組 W 美術,動物森友會 + 瑪利歐賽車:柔和飽和色、圓潤造型、乾淨的卡通光):
    // 地形起伏 / 層次、植被配方、遠景山形、氣氛(霧 / 光 / 天空)都由這裡依 TrackTheme 決定,不寫死在單一賽道上。
    //   RaceLook.For(theme)   依色票分類(Meadow / Desert / Snow / Forest / Volcano)+ 海岸(TrackTheme.Get(2) 且色調偏草地);未知的主題 = Meadow(安全預設)
    //   RaceLook.Current      目前世界的風格(= For(TrackTheme.Current))
    //   RaceLook.ApplyMood    把霧色 / 霧距 / 環境光 / 太陽色套到 RenderSettings(RaceSession.Env.Restore 會還原這些值)
    // 新主題只要 TrackTheme 色票合理(GrassLight 是地面主色、Leaves 是葉色),就會自動得到對應風格;要強制指定可以呼叫 RaceLook.Register(theme, kind)。
    internal enum LookKind { Meadow, Desert, Snow, Forest, Volcano, Coast }

    /// 樹 / 灌木種類(PropScatter 內部用)。
    internal enum Veg { Round, Blossom, Pine, Poplar, Palm, Dead, SnowPine, Autumn, Count }

    internal enum PeakShape { Alpine, Round, Mesa, Volcano }

    internal sealed class RaceLook
    {
        public LookKind Kind;

        // ---- 地形
        public float Roll = 1.6f;                 // 遠離路面處的緩起伏幅度(m)
        public float Terrace = 0.3f;              // 丘陵階地化強度 0..1(1 = 一階一階的梯田 / 方山)
        public int TerraceSteps = 4;
        public float BeachLow = 0.5f, BeachHigh = 1.6f;   // 沙灘混色的高度範圍(m)
        public float RockStart = 0.55f;           // 坡度(rise/run)超過這個開始染岩石色
        public float Strata = 0.06f, StrataFreq = 1.3f;   // 岩壁橫紋對比 / 頻率
        public float VergeWidth = 6.5f, VergeAmount = 0.65f; // 路邊漸層寬度 / 強度
        public Color PatchTint = new Color(0.90f, 0.98f, 0.84f);   // RaceTerrain shader 的大塊色斑
        public Color ShadeTint = new Color(0.80f, 0.80f, 0.92f);   // 暗面染色(偏冷 = 卡通陰影)
        public float NoiseAmount = 0.07f;
        public float CrackAmount = 0f;            // 熔岩裂縫發光(RaceTerrain shader,火山用)
        public float Speckle = 0f;                // 花 / 三葉草斑點(草地用)
        public float Sparkle = 0f;                // 雪地閃光

        // ---- 氣氛
        public float FogNear = 1f, FogFar = 1f;  // 乘在 TrackTheme.FogStart / FogEnd 上
        public Color SunColor = new Color(1f, 0.95f, 0.85f);
        public float SunIntensity = 0.8f, AmbientIntensity = 0.7f;
        public float CloudAmount = 0.5f;
        public int CloudCount = 10;
        public float SkyDeepen = 1.25f;          // 天頂飽和度倍率

        // ---- 植被 / 景物
        public float TreeMass = 1f;              // 樹的總量倍率(乘在賽道自己的 treeDensity 上)
        public float Grove = 0.6f;               // 樹叢聚集度 0 = 均勻 … 1 = 明顯的樹林與空地
        public float BushDensity = 1f, CoverDensity = 1f, RockDensity = 1f;
        public float[] LowW = new float[(int)Veg.Count], MidW = new float[(int)Veg.Count], HighW = new float[(int)Veg.Count];
        public Color Blossom = new Color(1f, 0.62f, 0.78f);
        public Color Autumn = new Color(1f, 0.55f, 0.18f);
        public Color[] Flowers = { new Color(1f, 0.56f, 0.72f), new Color(1f, 0.82f, 0.25f), Color.white, new Color(0.35f, 0.7f, 1f), new Color(1f, 0.3f, 0.3f) };
        public Color RockTop = new Color(0.5f, 0.75f, 0.4f);      // 石頭上緣色(青苔 / 雪帽 / 灰燼)
        public int CoverKind;                    // 地被:0 花叢 1 蘑菇 2 雪堆 3 熔岩尖 4 草簇(沙漠乾草)
        public bool BushFlowers = true;

        // ---- 遠景
        public PeakShape Peaks = PeakShape.Alpine;
        public int Islands = 6;
        public Color IslandBase = new Color(0.96f, 0.85f, 0.55f), IslandTop = new Color(0.4f, 0.8f, 0.4f);

        static readonly Dictionary<TrackTheme, RaceLook> cache = new Dictionary<TrackTheme, RaceLook>();
        static readonly Dictionary<TrackTheme, LookKind> forced = new Dictionary<TrackTheme, LookKind>();

        /// 預設風格(沒有 TrackTheme 時用)。
        public static RaceLook Default { get { return For(TrackTheme.Get(0)); } }

        public static RaceLook Current { get { return For(TrackTheme.Current ?? TrackTheme.Get(0)); } }

        /// 強制指定某個主題的風格種類(之後查到的 For 會重算)。
        public static void Register(TrackTheme th, LookKind kind)
        {
            if (th == null) return;
            forced[th] = kind;
            cache.Remove(th);
        }

        public static RaceLook For(TrackTheme th)
        {
            if (th == null) th = TrackTheme.Get(0);
            RaceLook lk;
            if (cache.TryGetValue(th, out lk)) return lk;
            LookKind k;
            if (!forced.TryGetValue(th, out k)) k = Classify(th);
            lk = Make(k, th);
            cache[th] = lk;
            return lk;
        }

        /// 色票分類:地面主色 GrassLight 的 HSV。
        static LookKind Classify(TrackTheme th)
        {
            // 已知編號(TrackTheme.Get(id)):0 島嶼 1 峽谷 2 海岸 3 草原 4 沙漠 5 雪地 6 森林 7 火山。只比對參考,不會建立新物件以外的副作用
            for (int id = 0; id < TrackTheme.Count; id++)
            {
                if (!ReferenceEquals(th, TrackTheme.Get(id))) continue;
                switch (id)
                {
                    case 1: case 4: return LookKind.Desert;
                    case 2: return LookKind.Coast;
                    case 5: return LookKind.Snow;
                    case 6: return LookKind.Forest;
                    case 7: return LookKind.Volcano;
                    default: return LookKind.Meadow;
                }
            }
            float h, s, v;
            Color.RGBToHSV(th.GrassLight, out h, out s, out v);
            if (v > 0.82f && s < 0.22f) return LookKind.Snow;
            if (v < 0.42f) return LookKind.Volcano;
            if (s > 0.28f && h >= 0.03f && h <= 0.17f) return LookKind.Desert;
            if (h > 0.18f && h < 0.5f && v < 0.68f) return LookKind.Forest;
            return LookKind.Meadow;
        }

        static void W(float[] w, Veg v, float x) { w[(int)v] = x; }

        static RaceLook Make(LookKind k, TrackTheme th)
        {
            var l = new RaceLook { Kind = k };
            // 通用:從色票推出的顏色
            l.RockTop = Color.Lerp(th.Rock, th.GrassDark, 0.55f);
            l.IslandBase = th.Sand;
            l.IslandTop = th.GrassLight;
            switch (k)
            {
                case LookKind.Desert:
                    l.Roll = 1.0f; l.Terrace = 0.75f; l.TerraceSteps = 5; l.BeachLow = 0.4f; l.BeachHigh = 1.2f; l.RockStart = 0.42f; l.Strata = 0.14f; l.StrataFreq = 1.8f;
                    l.VergeAmount = 0.55f; l.PatchTint = new Color(1f, 0.95f, 0.88f); l.ShadeTint = new Color(0.86f, 0.74f, 0.80f); l.NoiseAmount = 0.06f;
                    l.FogNear = 0.9f; l.FogFar = 0.95f; l.SunColor = new Color(1f, 0.90f, 0.72f); l.SunIntensity = 0.88f; l.AmbientIntensity = 0.66f; l.CloudAmount = 0.22f; l.CloudCount = 6;
                    l.TreeMass = 0.8f; l.Grove = 0.8f; l.BushDensity = 0.5f; l.CoverDensity = 0.8f; l.RockDensity = 1.5f;
                    W(l.LowW, Veg.Palm, 1f);
                    W(l.MidW, Veg.Dead, 0.55f); W(l.MidW, Veg.Round, 0.2f); W(l.MidW, Veg.Palm, 0.25f);
                    W(l.HighW, Veg.Dead, 1f);
                    l.CoverKind = 4; l.BushFlowers = false; l.Peaks = PeakShape.Mesa; l.Islands = 3;
                    l.RockTop = Color.Lerp(th.Rock, th.Sand, 0.45f);
                    l.Flowers = new[] { new Color(1f, 0.55f, 0.35f), new Color(1f, 0.85f, 0.35f), new Color(0.95f, 0.45f, 0.6f) };
                    break;
                case LookKind.Snow:
                    l.Roll = 2.1f; l.Terrace = 0.2f; l.TerraceSteps = 4; l.BeachLow = 0.3f; l.BeachHigh = 1.0f; l.RockStart = 0.6f; l.Strata = 0.05f;
                    l.VergeAmount = 0.4f; l.PatchTint = new Color(0.88f, 0.95f, 1f); l.ShadeTint = new Color(0.70f, 0.76f, 0.94f); l.NoiseAmount = 0.05f;
                    l.FogNear = 0.85f; l.FogFar = 0.95f; l.SunColor = new Color(1f, 0.97f, 0.92f); l.SunIntensity = 0.85f; l.AmbientIntensity = 0.72f; l.Sparkle = 1f; l.CloudAmount = 0.8f; l.CloudCount = 12;
                    l.TreeMass = 1.25f; l.Grove = 0.7f; l.BushDensity = 0.6f; l.CoverDensity = 1.1f; l.RockDensity = 0.9f; l.SkyDeepen = 1.1f;
                    W(l.LowW, Veg.SnowPine, 1f);
                    W(l.MidW, Veg.SnowPine, 0.8f); W(l.MidW, Veg.Poplar, 0.1f); W(l.MidW, Veg.Round, 0.1f);
                    W(l.HighW, Veg.SnowPine, 1f);
                    l.CoverKind = 2; l.BushFlowers = false; l.Peaks = PeakShape.Alpine; l.Islands = 5;
                    l.RockTop = new Color(0.95f, 0.97f, 1f);
                    l.IslandBase = new Color(0.82f, 0.9f, 0.98f); l.IslandTop = Color.white;
                    l.Flowers = new[] { new Color(0.55f, 0.8f, 1f), Color.white };
                    break;
                case LookKind.Forest:
                    l.Roll = 1.9f; l.Terrace = 0.28f; l.TerraceSteps = 4; l.RockStart = 0.58f; l.Strata = 0.05f;
                    l.VergeAmount = 0.6f; l.PatchTint = new Color(0.80f, 0.95f, 0.74f); l.ShadeTint = new Color(0.68f, 0.76f, 0.80f); l.NoiseAmount = 0.11f; l.Speckle = 0.55f;
                    l.FogNear = 0.7f; l.FogFar = 0.85f; l.SunColor = new Color(1f, 0.96f, 0.78f); l.SunIntensity = 0.76f; l.AmbientIntensity = 0.74f; l.CloudAmount = 0.55f;
                    l.TreeMass = 1.0f; l.Grove = 0.85f; l.CoverDensity = 1.2f; l.RockDensity = 0.9f;
                    W(l.LowW, Veg.Round, 0.7f); W(l.LowW, Veg.Palm, 0.3f);
                    W(l.MidW, Veg.Round, 0.35f); W(l.MidW, Veg.Pine, 0.3f); W(l.MidW, Veg.Poplar, 0.15f); W(l.MidW, Veg.Autumn, 0.2f);
                    W(l.HighW, Veg.Pine, 0.8f); W(l.HighW, Veg.Autumn, 0.2f);
                    l.CoverKind = 1; l.BushDensity = 0.85f; l.BushFlowers = true; l.Peaks = PeakShape.Round; l.Islands = 4;
                    l.Autumn = new Color(1f, 0.6f, 0.2f);
                    break;
                case LookKind.Volcano:
                    l.Roll = 1.3f; l.Terrace = 0.55f; l.TerraceSteps = 3; l.BeachLow = 0.3f; l.BeachHigh = 1.1f; l.RockStart = 0.45f; l.Strata = 0.1f; l.StrataFreq = 1.5f;
                    l.VergeAmount = 0.5f; l.PatchTint = new Color(1f, 0.88f, 0.80f); l.ShadeTint = new Color(0.88f, 0.70f, 0.78f); l.NoiseAmount = 0.10f; l.CrackAmount = 1.1f;
                    l.FogNear = 0.9f; l.FogFar = 1.0f; l.SunColor = new Color(1f, 0.82f, 0.64f); l.SunIntensity = 1.0f; l.AmbientIntensity = 1.05f; l.CloudAmount = 0.65f; l.CloudCount = 9;
                    l.TreeMass = 0.7f; l.Grove = 0.9f; l.BushDensity = 0.3f; l.CoverDensity = 1.1f; l.RockDensity = 1.8f; l.SkyDeepen = 1.05f;
                    W(l.LowW, Veg.Dead, 1f); W(l.MidW, Veg.Dead, 0.75f); W(l.MidW, Veg.Palm, 0.25f); W(l.HighW, Veg.Dead, 1f);
                    l.CoverKind = 3; l.BushFlowers = false; l.Peaks = PeakShape.Volcano; l.Islands = 5;
                    l.RockTop = Color.Lerp(th.Rock, Color.black, 0.35f);
                    l.IslandBase = Color.Lerp(th.Rock, Color.black, 0.4f); l.IslandTop = new Color(1f, 0.45f, 0.15f);
                    break;
                case LookKind.Coast:
                    l.Roll = 0.9f; l.Terrace = 0.2f; l.TerraceSteps = 3; l.BeachLow = 0.7f; l.BeachHigh = 2.1f; l.RockStart = 0.5f;
                    l.FogNear = 1.1f; l.FogFar = 1.1f; l.CloudAmount = 0.45f; l.Speckle = 0.5f; l.NoiseAmount = 0.09f;
                    l.TreeMass = 0.9f; l.Grove = 0.5f; l.BushDensity = 0.9f; l.CoverDensity = 0.9f; l.RockDensity = 1.2f;
                    W(l.LowW, Veg.Palm, 0.8f); W(l.LowW, Veg.Round, 0.2f);
                    W(l.MidW, Veg.Round, 0.4f); W(l.MidW, Veg.Palm, 0.3f); W(l.MidW, Veg.Pine, 0.15f); W(l.MidW, Veg.Poplar, 0.15f);
                    W(l.HighW, Veg.Pine, 0.7f); W(l.HighW, Veg.Round, 0.3f);
                    l.CoverKind = 0; l.Peaks = PeakShape.Alpine; l.Islands = 8;
                    break;
                default:   // Meadow
                    l.TreeMass = 1f; l.Grove = 0.6f; l.Speckle = 0.9f; l.NoiseAmount = 0.11f; l.BushDensity = 1.15f; l.PatchTint = new Color(0.86f, 0.98f, 0.78f);
                    W(l.LowW, Veg.Palm, 0.45f); W(l.LowW, Veg.Round, 0.4f); W(l.LowW, Veg.Blossom, 0.15f);
                    W(l.MidW, Veg.Round, 0.32f); W(l.MidW, Veg.Blossom, 0.14f); W(l.MidW, Veg.Pine, 0.24f); W(l.MidW, Veg.Poplar, 0.16f); W(l.MidW, Veg.Palm, 0.14f);
                    W(l.HighW, Veg.Pine, 0.7f); W(l.HighW, Veg.Round, 0.2f); W(l.HighW, Veg.Poplar, 0.1f);
                    l.CoverKind = 0; l.Peaks = PeakShape.Alpine; l.Islands = 6;
                    break;
            }
            return l;
        }

        // ------------------------------------------------------------------ 輔助

        /// 依高度帶選一種樹(權重表為空時回傳 Round)。u = 0..1 的隨機數。
        public Veg PickVeg(float h, float u)
        {
            float[] w = h < 2.6f ? LowW : (h > 9f ? HighW : MidW);
            float sum = 0f;
            for (int i = 0; i < w.Length; i++) sum += w[i];
            if (sum <= 1e-4f) { w = MidW; sum = 0f; for (int i = 0; i < w.Length; i++) sum += w[i]; }
            if (sum <= 1e-4f) return Veg.Round;
            float t = u * sum, acc = 0f;
            for (int i = 0; i < w.Length; i++) { if (w[i] <= 0f) continue; acc += w[i]; if (t <= acc) return (Veg)i; }
            return Veg.Round;
        }

        /// 這個風格用得到哪些樹種(PropScatter 只烘這些原型)。
        public bool Uses(Veg v)
        {
            int i = (int)v;
            return LowW[i] > 0f || MidW[i] > 0f || HighW[i] > 0f;
        }

        public static float FogStart(TrackTheme th) { return th.FogStart * For(th).FogNear; }
        public static float FogEnd(TrackTheme th) { return th.FogEnd * For(th).FogFar; }

        /// 把 c 的飽和度乘 sat、明度乘 val。
        public static Color Tweak(Color c, float sat, float val)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            Color o = Color.HSVToRGB(h, Mathf.Clamp01(s * sat), Mathf.Clamp01(v * val));
            o.a = 1f;
            return o;
        }

        /// 天空球頂端色(由 TrackTheme.Sky 加深飽和度)。
        public static Color SkyTop(TrackTheme th) { return Tweak(th.Sky, For(th).SkyDeepen, 0.93f); }

        /// 氣氛預設:霧(顏色 = 地平線色,和天空球 / 水面 / 地形遠景色一致)、環境光三色、太陽色與強度。
        /// 所有改動的欄位都在 RaceSession.Env.Save / Restore 的涵蓋範圍內(fog*、ambient*、Sun 的 color / intensity)。
        public static void ApplyMood(TrackTheme th)
        {
            if (th == null || !Application.isPlaying) return;
            RaceLook lk = For(th);
            RenderSettings.fog = true;
            RenderSettings.fogColor = th.Horizon;
            RenderSettings.fogStartDistance = th.FogStart * lk.FogNear;
            float fogEnd = Mathf.Max(th.FogEnd * lk.FogFar, th.FogStart * lk.FogNear + 50f);
            RenderSettings.fogEndDistance = fogEnd;
            // 指數平方霧(近處乾淨、遠處快速收進地平線色),濃度依賽道霧距換算;火山 / 沙漠稍濃(熱霾)
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            float fk = lk.Kind == LookKind.Volcano ? 1.75f : (lk.Kind == LookKind.Desert ? 1.6f : (lk.Kind == LookKind.Snow ? 1.7f : 1.5f));
            RenderSettings.fogDensity = fk / Mathf.Max(100f, fogEnd);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(SkyTop(th), Color.white, 0.35f);
            RenderSettings.ambientEquatorColor = Color.Lerp(th.Horizon, th.GrassLight, 0.4f);
            RenderSettings.ambientGroundColor = Color.Lerp(th.GrassDark, th.Horizon, 0.25f) * 0.75f;
            RenderSettings.ambientIntensity = lk.AmbientIntensity;
            var sg = GameObject.Find("Sun");
            var sun = sg != null ? sg.GetComponent<Light>() : null;
            if (sun != null) { sun.color = lk.SunColor; sun.intensity = lk.SunIntensity; }
            ApplyGrade(lk);
        }

        /// 每主題調色:LUT 條 + 柔和泛光 + 暈映(PostFX 可用時;RaceSession.Env.Restore 會還原)。
        static void ApplyGrade(RaceLook lk)
        {
            var fx = PostFX.Instance;
            if (fx == null) return;
            string n;
            switch (lk.Kind)
            {
                case LookKind.Snow: n = "lut_snow"; break;
                case LookKind.Volcano: n = "lut_volcano"; break;
                case LookKind.Forest: n = "lut_forest"; break;
                case LookKind.Desert: n = "lut_desert"; break;
                default: n = "lut_meadow"; break;
            }
            float bloom = lk.Kind == LookKind.Volcano ? 0.34f : (lk.Kind == LookKind.Snow ? 0.2f : 0.26f);
            fx.SetRaceGrade(RaceShaders.Tex(n, false), 0.65f, 0.88f, bloom, 0.4f, 1.1f);
        }
    }
}
