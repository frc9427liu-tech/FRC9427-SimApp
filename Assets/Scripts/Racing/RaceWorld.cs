using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    // 賽車世界(模組 W,見 docs/racing/00-contracts.md §3.4)。
    // M5:864 x 864 m 的島(格距 4.5 m,格子 / 區塊數量沒變,所以三角形與碰撞器預算不變)、3 條賽道(DefaultTracks)、捷徑、地標、自由探索收集物。
    // 賽車式選項(RaceConfig.Mirror / Reverse):Build 先用 TrackVariants 把 TrackDef 轉成鏡像 / 反向版,再照一般流程生成,地形(RaceTerrain.Mirror)也跟著左右鏡射。
    // 比賽金幣(RaceCoinSet)與滑翔觸發(StepPickups)見檔案下半部。
    // M2:193 x 193 高度圖(RaceTerrain,種子決定、賽道沿線刻平)切成 6 x 6 個地形區塊(TerrainChunks,LOD0/LOD1 + MeshCollider),
    // 四周是海(Surface.Water,掉進去會被 RaceCheckpoints 重置);樹/石頭/遠景山脈/雲由 PropScatter 產生;RaceWorldLod 每 0.25 秒切 LOD 與裁切。
    // HeightAt / NormalAt / SurfaceAt / SampleGround 都直接查高度圖(不 raycast、不配置記憶體),簽名與 M1 相同。
    public static class RaceWorld
    {
        public const float Size = 864f, Cell = 4.5f, KillY = -5f;

        public static int TrackCount { get { return DefaultTracks.Count; } }

        public static string TrackName(int id)
        {
            return DefaultTracks.NameOf(id, Loc.Lang == "en");   // 不必生成賽道就能取名(選單一次問 8 條)
        }

        static Transform root;
        static RaceTrack track;
        static RaceTerrain terrain;
        static FeatureSet feats;   // M6:跳台 / 加速板
        static CollectibleSet coins;   // M5:自由探索收集物(只有 RaceConfig.FreeRoam 才有)
        static RaceCoinSet raceCoins;  // 比賽金幣(每圈 44~56 枚,非自由探索才有)
        static readonly List<GliderLip> gliderLips = new List<GliderLip>();
        static readonly Dictionary<int, Vector3> lastPos = new Dictionary<int, Vector3>();
        static bool mirrored, reversed;
        static readonly List<Collectible> noCoins = new List<Collectible>();
        static bool ready;
        static readonly List<Mesh> meshes = new List<Mesh>();
        static readonly List<Object> owned = new List<Object>();   // 材質 / 貼圖 / 網格(M2 新增的),Destroy 時一起釋放

        public static Transform Root { get { return root; } }
        public static Bounds WorldBounds { get { return new Bounds(Vector3.zero, new Vector3(Size, 200f, Size)); } }
        public static RaceTrack Track { get { return track; } }
        public static bool Ready { get { return ready; } }
        /// 目前這個世界是不是鏡像 / 反向版。
        public static bool IsMirror { get { return mirrored; } }
        public static bool IsReverse { get { return reversed; } }

        // 給測試與預算統計用(同組件內可見)
        internal static RaceTerrain Terrain { get { return terrain; } }
        internal static FeatureSet Features { get { return feats; } }
        internal static TerrainChunks.BuildStats TerrainStats { get; private set; }
        internal static PropScatter.Result Props { get; private set; }
        internal static TrackLandmarks.Result Landmarks { get; private set; }
        internal static TrackProps.Result Decor { get; private set; }

        // ------------------------------------------------------------------ 自由探索收集物(M5)

        /// 收集物清單(FreeRoam 世界 30 個,其他情況是空清單)。Collectible.Taken = 已被撿走。
        public static IReadOnlyList<Collectible> Collectibles { get { return coins != null ? (IReadOnlyList<Collectible>)coins.Items : noCoins; } }
        /// 收集物總數(HUD 顯示「x / 30」用)。
        public static int CollectibleTotal { get { return coins != null ? coins.Items.Count : 0; } }
        /// 已撿到的數量。
        public static int CollectedCount { get { return coins != null ? coins.Collected : 0; } }
        /// 撿到一個收集物時觸發(索引, 位置);音效由 04 訂閱這個事件。
        public static event System.Action<int, Vector3> CoinCollected;

        /// 玩家車子每個 FixedUpdate 呼叫一次:pos 附近(水平 3.2 m、垂直 3.5 m)的收集物全部撿起,回傳這次新撿到幾個。沒有收集物(非自由探索)回 0。
        public static int TryCollect(Vector3 pos) { return coins != null ? coins.TryCollect(pos) : 0; }

        /// 重置全部收集物(重玩自由探索不必重建世界)。
        public static void ResetCollectibles() { if (coins != null) coins.ResetAll(); }

        /// 建世界。同樣的 cfg(含 Seed)結果完全相同。同步建完(高階筆電約數百毫秒)。
        public static RaceTrack Build(RaceConfig cfg, Transform parent, RaceQuality q = RaceQuality.High)
        {
            Destroy();
            int id = cfg != null ? cfg.TrackId : 0;
            mirrored = cfg != null && cfg.Mirror; reversed = cfg != null && cfg.Reverse;
            TrackDef def = TrackVariants.Apply(DefaultTracks.Get(id), mirrored, reversed);
            TrackTheme.Current = TrackTheme.Get(def.theme);
            track = RaceTrack.FromDef(def);
            track.Id = id;
            if (cfg != null && cfg.Laps > 0) track.Laps = cfg.Laps;
            int seed = (cfg != null && cfg.Seed != 9427) ? cfg.Seed : def.seed;   // M5:每條賽道有自己的地形種子;選單沒動過種子(預設 9427)時用賽道的,明確指定別的數字才覆蓋

            var go = new GameObject("RaceWorld");
            if (parent != null) go.transform.SetParent(parent, false);
            root = go.transform;

            // 地形(會把刻好的路面高度寫回 track 的取樣點,所以要在建路面網格之前)
            terrain = RaceTerrain.Generate(track, seed, mirrored);
            TrackGapBuilder.CarvePits(track, terrain);   // M7:飛越缺口 = 把地形挖到海平面下(要在 TerrainChunks.Build 之前)
            var lod = go.AddComponent<RaceWorldLod>();
            lod.Quality = q;
            TerrainChunks.BuildStats st;
            TerrainChunks.Build(terrain, root, q, owned, lod.chunks, out st);
            TerrainStats = st;
            lod.fixedDraws += 1;   // 水面

            GameObject trackGo = TrackMeshBuilder.Build(track, root, meshes);
            TrackGapBuilder.CutRoad(track, trackGo.transform);   // M7:缺口範圍的路面 / 路肩 / 路緣 / 護欄碰撞拿掉
            TrackShortcutBuilder.Build(track, trackGo.transform, meshes);
            feats = FeatureSet.Resolve(track);
            TrackFeatureBuilder.Build(feats, terrain, trackGo.transform, meshes);
            TrackRailBuilder.Build(track, trackGo.transform, root, meshes);   // 模組 K:軌道滑行(外觀 + KartRails 登記)+ 跑牆標記
            TrackGapBuilder.BuildMarkers(track, terrain, root, meshes);   // M8:缺口的箭頭門架 / 落地大旗
            lod.fixedTris += TrackGapBuilder.LastMarkerTris; lod.fixedDraws += TrackGapBuilder.LastMarkerTris > 0 ? 3 : 0;
            TrackCorridor.Build(track, terrain, root, owned, seed);   // M8:路邊連續斷崖牆(2~3 段)
            lod.fixedTris += TrackCorridor.LastTris; lod.fixedDraws += TrackCorridor.LastTris > 0 ? 1 : 0;
            int trackTris, trackDraws;
            CountTrack(out trackTris, out trackDraws);
            lod.fixedTris += trackTris; lod.fixedDraws += trackDraws;

            Landmarks = TrackLandmarks.Build(track, terrain, root, owned);
            lod.fixedTris += Landmarks.Tris; lod.fixedDraws += Landmarks.Draws;

            // 造景(花叢 / 小屋 / 觀眾席 / 路面細節…):先於樹建好,樹和石頭會避開它們的足跡
            Decor = TrackProps.Build(def.theme, track, new System.Random(seed ^ 0x7A3C51), root, owned, Landmarks.Bases, q == RaceQuality.Low ? 0.5f : 1f, Landmarks.Blades);
            lod.fixedTris += Decor.Tris; lod.fixedDraws += Decor.Draws;
            TrackGapBuilder.CleanDecor(track, root);   // M7:漂在深坑上空的造景拿掉

            PropScatter.Avoid = Landmarks.Bases;
            PropScatter.AvoidCircles = Decor.Foot;
            Props = PropScatter.Build(root, track, terrain, seed, q, owned, lod);
            PropScatter.Avoid = null;
            PropScatter.AvoidCircles = null;

            if (cfg != null && cfg.FreeRoam)
            {
                coins = CollectibleSet.Build(track, terrain, root, seed, owned, lod, Landmarks.Bases);
                coins.Picked += (i, p) => { if (CoinCollected != null) CoinCollected(i, p); };
            }
            else
            {
                raceCoins = RaceCoinSet.Build(track, terrain, feats, root, owned);
                raceCoins.Picked += (k, i, p) => { if (RaceCoinPicked != null) RaceCoinPicked(k, i, p); };
                lod.fixedDraws += 1; lod.fixedTris += raceCoins.TriangleCount;
            }
            BuildGliderLips();
            lastPos.Clear();
            lod.Apply(track.Grid(0).position);   // 初始視點 = 起跑格,攝影機出現後由 Update 接手
            ApplyAtmosphere(TrackTheme.Current);
            ready = true;
            return track;
        }

        /// 依 theme 設定霧 / 遠裁切面 / 天空色(世界放大後霧與遠裁切要跟著拉遠)。RaceSession 的 Env.Restore 會在結束時還原。
        static void ApplyAtmosphere(TrackTheme th)
        {
            if (th == null || !Application.isPlaying) return;
            Camera c = Camera.main;
            if (c != null)
            {
                c.farClipPlane = Mathf.Max(c.farClipPlane, th.FarClip);
                if (c.clearFlags == CameraClearFlags.SolidColor) c.backgroundColor = th.Sky;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = th.Horizon;
            RenderSettings.fogStartDistance = th.FogStart;
            RenderSettings.fogEndDistance = th.FogEnd;
        }

        static void CountTrack(out int tris, out int draws)
        {
            tris = 0; draws = 0;
            Transform tr = root.Find("Track");
            if (tr == null) return;
            foreach (var mf in tr.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3); draws++;
            }
        }

        /// 刪全部物件與網格;可重複呼叫。
        public static void Destroy()
        {
            ready = false;
            if (root != null)
            {
                root.gameObject.SetActive(false);   // 立刻讓碰撞體失效(Destroy 要到幀尾才生效)
                if (Application.isPlaying) Object.Destroy(root.gameObject); else Object.DestroyImmediate(root.gameObject);
            }
            root = null;
            foreach (var m in meshes)
            {
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m);
            }
            meshes.Clear();
            foreach (var o in owned)
            {
                if (o == null) continue;
                if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
            }
            owned.Clear();
            track = null;
            terrain = null;
            feats = null;
            coins = null;
            raceCoins = null;
            KartRails.Clear();
            gliderLips.Clear();
            lastPos.Clear();
            mirrored = false; reversed = false;
            Landmarks = null;
            Decor = null;
            TerrainStats = null;
            Props = null;
        }

        // ------------------------------------------------------------------ 比賽金幣與滑翔觸發

        /// 比賽金幣清單(自由探索 / 沒有世界時是空清單)。Taken = 被撿走(5 秒後重生)。
        public static IReadOnlyList<RaceCoin> RaceCoins { get { return raceCoins != null ? (IReadOnlyList<RaceCoin>)raceCoins.Items : noRaceCoins; } }
        static readonly List<RaceCoin> noRaceCoins = new List<RaceCoin>();
        /// 撿到比賽金幣時觸發(kartId, 金幣索引, 位置);音效 / 特效訂閱這個。
        public static event System.Action<int, int, Vector3> RaceCoinPicked;
        /// 車越過滑翔跳台前緣時觸發(kartId, GliderLip.Index)。整合者也可以只看 StepPickups 的回傳值。
        public static event System.Action<int, int> GliderLaunched;
        /// 滑翔跳台前緣資料(沒有 = 空清單)。
        public static IReadOnlyList<GliderLip> GliderLips { get { return gliderLips; } }

        static void BuildGliderLips()
        {
            gliderLips.Clear();
            if (feats == null) return;
            for (int i = 0; i < feats.Count; i++)
            {
                FeatureSet.Inst f = feats.Items[i];
                if (!f.IsGlider || f.Path >= 0) continue;
                Vector3 c = new Vector3(f.Cx + f.Fx * f.HL, 0f, f.Cz + f.Fz * f.HL);
                float rise = f.TanP * 2f * f.HL;
                c.y = HeightAt(c.x, c.z) + rise;
                gliderLips.Add(new GliderLip { Index = i, Center = c, Forward = new Vector3(f.Fx, 0f, f.Fz), HalfWidth = f.HW, Rise = rise });
            }
        }

        /// 每個物理步驟對每台車呼叫一次(pos = 車的位置;now = Time.time):撿金幣 + 偵測滑翔起飛。
        ///   int n = r.Coins -> 呼叫 kart.AddCoin() n 次;r.Glide -> 呼叫 kart.BeginGlide()(RaceKart 還沒有這兩個方法時由整合者接線)。
        /// 車的上一個位置由這裡記(以 kartId 區分),傳送(重生)後第一步不會誤觸。不配置記憶體(第一次看到某個 kartId 除外)。
        public static PickupResult StepPickups(int kartId, Vector3 pos, float now)
        {
            var r = new PickupResult { RampIndex = -1 };
            if (!ready) return r;
            if (raceCoins != null) r.Coins = raceCoins.Collect(kartId, pos, now);
            Vector3 prev;
            if (lastPos.TryGetValue(kartId, out prev))
            {
                if (feats != null)
                {
                    int g = feats.GliderLipCrossed(prev, pos);
                    if (g >= 0)
                    {
                        r.Glide = true; r.RampIndex = g;
                        if (GliderLaunched != null) GliderLaunched(kartId, g);
                    }
                }
            }
            lastPos[kartId] = pos;
            return r;
        }

        /// 重置比賽金幣(新的一場比賽不必重建世界)。
        public static void ResetRaceCoins() { if (raceCoins != null) raceCoins.ResetAll(); lastPos.Clear(); }

        // ------------------------------------------------------------------ 查詢(不做 raycast,不配置記憶體,headless 也能用)

        /// 車該踩的高度。陸地 = 地形(與 MeshCollider 完全一致);海面下 = 海平面 0(此時 SurfaceAt = Water)。沒有世界時回 0。
        public static float HeightAt(float x, float z) { return terrain != null ? terrain.Height(x, z) : 0f; }

        /// 含跳台的地面高度:跳台範圍內 = 地形 + 斜面高度(與跳台 MeshCollider 的頂面一致),其他地方 = HeightAt。
        /// HeightAt 刻意維持「純地形」(和地形碰撞體逐點一致,測試 T2/T3 依賴它);車子碰撞體沒查到時走 SampleGround,那裡用的是這個。
        public static float GroundHeightAt(float x, float z)
        {
            float h = HeightAt(x, z), off; Vector3 tilt;
            if (feats != null && feats.RampOffset(x, z, out off, out tilt)) h += off;
            return h;
        }

        public static Vector3 NormalAt(float x, float z) { return terrain != null ? terrain.Normal(x, z) : Vector3.up; }

        public static bool InBounds(Vector3 p)
        {
            float h = Size * 0.5f;
            return p.x >= -h && p.x <= h && p.z >= -h && p.z <= h;
        }

        /// 地面材質:路面(含路緣)Road、路肩 Dirt、海灘 Sand、草地 Grass、海 Water、島外/邊緣陸地 OutOfBounds。
        public static Surface SurfaceAt(float x, float z)
        {
            if (terrain == null)
            {
                float e = Size * 0.5f - 4f;
                return (x < -e || x > e || z < -e || z > e) ? Surface.OutOfBounds : Surface.Grass;
            }
            Surface s0 = terrain.SurfaceAt(x, z);
            if (feats != null && (s0 == Surface.Road || s0 == Surface.Dirt))
            {
                Surface fs;
                if (feats.TrySurface(x, z, out fs)) return fs;
            }
            return s0;
        }

        /// 符合 GroundSampler;超出地圖回 false。
        public static bool SampleGround(Vector3 p, out GroundHit hit)
        {
            if (!InBounds(p)) { hit = default; return false; }
            hit.Height = HeightAt(p.x, p.z);
            hit.Normal = NormalAt(p.x, p.z);
            hit.Surface = SurfaceAt(p.x, p.z);
            float off; Vector3 tilt;
            if (feats != null && feats.RampOffset(p.x, p.z, out off, out tilt))
            {
                hit.Height += off;
                hit.Normal = (hit.Normal + tilt).normalized;
            }
            return true;
        }
    }
}
