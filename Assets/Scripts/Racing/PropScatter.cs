using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 模組 W / M2:樹、灌木、花叢 / 蘑菇 / 雪堆(地被)、石頭群、遠景山脈與小島、雲(見 02 §3)。
    /// 美術(動物森友會 + 瑪利歐賽車):全部用 RaceProtoBuilder 程式生成圓潤的小原型(平滑法線 + 頂點色漸層當假環境光遮蔽),
    /// 依 RaceLook(由 TrackTheme 色票決定的風格)選樹種、樹林聚集度、地被種類、遠景山形;每個物件再乘一個隨機色調與大小,所以不會「一模一樣複製貼上」。
    /// 全部依「6 x 6 個區域」合併成靜態頂點色網格(每區 1 個材質 = RaceShaders.ToonVC,1 個 draw call;shader 缺失時最多 4 個桶),RaceWorldLod 會把太遠的區域整組關掉。
    /// 規則(測試依賴,不要改):樹離路面邊緣至少 6.5 m、不擺在彎道內側的視線範圍、不擺在海灘和陡坡上;賽道旁的樹最多 60 個加 CapsuleCollider(一個區域共用一個 GameObject)。
    /// 灌木 / 地被 / 石頭沒有碰撞體;灌木離路面邊緣 >= 5 m(護欄外側)。
    internal static partial class PropScatter
    {
        public sealed class Result
        {
            public int Trees, Rocks, Colliders, Tris, Cells, Mountains, Clouds, Bushes, Cover, Islands, Furniture, Tufts, Signs, TuftTris;
            public int FixedTris, FixedDraws;                 // 山脈 + 雲(不受裁切影響)
            public float MinRoadDist = 999f;                  // 所有樹離路面邊緣的最短距離
            public readonly List<Vector3> TreePos = new List<Vector3>();
        }

        // ------------------------------------------------------------------ 資料結構

        struct Placed { public Vector4 P; public int Veg, Var; public float Yaw, Dist, T1, T2; }

        sealed class CellBuild
        {
            public readonly RaceColorAcc Acc = new RaceColorAcc();
            public readonly RaceColorAcc TreeLo = new RaceColorAcc();   // 遠處用的低面數樹(RaceWorldLod:離區塊 >= NearDist)
            public readonly RaceColorAcc TreeHi = new RaceColorAcc();   // 近處用的高面數樹(離區塊 < NearDist)
            public readonly List<Vector4> Colliders = new List<Vector4>();   // xyz = 底部位置,w = 縮放
        }

        const int GN = 200;                                            // 撒點間距格子:每格 5 m,涵蓋 1000 m
        const int CellsPerSide = 6;
        const float CellSize = 2f * RaceTerrain.Half / CellsPerSide;   // 144 m
        const float MinRoadDist = 6.5f;                                // 離路面邊緣(不含路緣)至少多遠
        const int MaxTreeColliders = 60;
        public const float TreeNearDist = 70f;   // 區塊離攝影機小於這個距離才用高面數樹

        /// 地標位置(RaceWorld 在 Build 前設定):樹和石頭不擺在它們旁邊。
        internal static IList<Vector3> Avoid;
        /// TrackProps(造景)的足跡圓(xyz 位置,w 半徑):樹和石頭不擺進去。
        internal static IList<Vector4> AvoidCircles;

        // 目前這次 Build 用的原型與風格(Build 開頭設定,結尾清掉)
        static RaceProto[][] trees, treesHi;
        static RaceProto[] bushes, covers, rocks, verge;
        static RaceLook look;
        static TrackTheme theme;
        static int sceneSeed;

        // ------------------------------------------------------------------ 入口

        public static Result Build(Transform parent, RaceTrack track, RaceTerrain terrain, int seed, RaceQuality q, List<Object> owned, RaceWorldLod lod)
        {
            var res = new Result();
            var root = new GameObject("Props");
            if (parent != null) root.transform.SetParent(parent, false);
            float density = q == RaceQuality.Low ? 0.5f : 1f;
            theme = TrackTheme.Current ?? TrackTheme.Get(0);
            look = RaceLook.For(theme);
            sceneSeed = seed;
            // M7:長賽道(> 2300 m)的路邊樹排與樹的三角形上限隨長度反比縮小(路邊樹排數量跟賽道長度成正比,3~4 km 會讓一幀三角形逼近預算)
            float lenK = Mathf.Clamp(2300f / Mathf.Max(1f, track.Length), 0.5f, 1f);
            float tdens = Mathf.Max(0.12f, track.TreeDensity * look.TreeMass * lenK);

            // 1) 原型
            BakeProtos(seed);

            // 2) 撒點
            var rnd = new System.Random(seed ^ 0x5EED1234);
            var cellsB = new CellBuild[CellsPerSide * CellsPerSide];
            for (int i = 0; i < cellsB.Length; i++) cellsB[i] = new CellBuild();
            var spacing = new List<Vector3>[GN * GN];
            var colCand = new List<int>();            // 候選碰撞樹(離路 < 15 m)的索引
            var placed = new List<Placed>();

            // 路邊兩排(近排 + 遠排)
            RoadsideRows(track, terrain, rnd, spacing, placed, 6f, 11f, 0f, 9f, 0.9f * density * tdens, res);
            RoadsideRows(track, terrain, rnd, spacing, placed, 10f, 18f, 11f, 28f, 0.7f * density * tdens, res);
            // 丘陵(樹叢:低頻雜訊遮罩 → 一叢一叢的樹林與空地;只有通過遮罩的嘗試才算次數,所以總量不縮水)
            float lim = RaceTerrain.Half - 30f;
            int hillTarget = Mathf.RoundToInt(560 * density * tdens), tries = 0, guard = 0;
            int hillStart = placed.Count;
            while (placed.Count - hillStart < hillTarget && tries < 8000 && guard < 60000)
            {
                guard++;
                float x = (float)(rnd.NextDouble() * 2.0 * lim - lim), z = (float)(rnd.NextDouble() * 2.0 * lim - lim);
                float h = terrain.Raw(x, z);
                if (h < 3.5f) continue;
                tries++;
                int before = placed.Count;
                TryPlaceTree(track, terrain, rnd, spacing, placed, x, z, 14f, look.Grove, res);
                // 樹林核心:旁邊再補 0~2 棵小樹(同樣要通過全部規則),一叢一叢更像樹林
                float cmp = (float)rnd.NextDouble(), ca = (float)rnd.NextDouble() * 6.28f, cd = 4.8f + (float)rnd.NextDouble() * 3f;
                if (placed.Count > before && cmp < 0.7f * look.Grove)
                {
                    int nc = cmp < 0.25f * look.Grove ? 2 : 1;
                    for (int c2 = 0; c2 < nc; c2++)
                    {
                        float a2 = ca + c2 * 2.4f;
                        TryPlaceTree(track, terrain, rnd, spacing, placed, x + Mathf.Cos(a2) * cd, z + Mathf.Sin(a2) * cd, 4.2f, 0f, res);
                    }
                }
            }
            // 三角形上限:樹林很密的賽道(treeDensity 大)會讓一幀的三角形逼近預算(150k),超過就把最後放的(丘陵)樹丟掉;路邊兩排先放,所以一定保留
            int treeTriCap = Mathf.RoundToInt(31000 * density * lenK), sumT = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                int lo = trees[placed[i].Veg][placed[i].Var].T.Length / 3, hi = treesHi[placed[i].Veg][placed[i].Var].T.Length / 3;
                sumT += lo + Mathf.RoundToInt(0.3f * Mathf.Max(0, hi - lo));
                if (sumT > treeTriCap) { placed.RemoveRange(i, placed.Count - i); break; }
            }
            res.Trees = placed.Count;

            // 碰撞樹:離路 < 15 m 的候選,平均抽 60 個
            for (int i = 0; i < placed.Count; i++) if (placed[i].Dist < 15f) colCand.Add(i);
            int stepC = Mathf.Max(1, Mathf.CeilToInt(colCand.Count / (float)MaxTreeColliders));
            var colSet = new HashSet<int>();
            for (int i = 0; i < colCand.Count; i += stepC) colSet.Add(colCand[i]);

            for (int i = 0; i < placed.Count; i++)
            {
                Placed pl = placed[i];
                CellBuild cb = cellsB[CellOf(pl.P.x, pl.P.z)];
                float scale = pl.P.w;
                cb.TreeLo.Add(trees[pl.Veg][pl.Var], new Vector3(pl.P.x, pl.P.y, pl.P.z), pl.Yaw, Vector3.one * scale, TintOf(pl.T1, pl.T2));
                cb.TreeHi.Add(treesHi[pl.Veg][pl.Var], new Vector3(pl.P.x, pl.P.y, pl.P.z), pl.Yaw, Vector3.one * scale, TintOf(pl.T1, pl.T2));
                if (colSet.Contains(i)) cb.Colliders.Add(pl.P);
                res.TreePos.Add(new Vector3(pl.P.x, pl.P.y, pl.P.z));
            }

            // 灌木 / 地被 / 石頭用另一組亂數,不影響樹的位置
            var rnd2 = new System.Random(seed ^ 0x0BADC0DE);
            ScatterBushes(track, terrain, rnd2, spacing, cellsB, density, res);
            ScatterCover(track, terrain, rnd2, spacing, cellsB, density, res, lim);
            ScatterVerge(track, terrain, new System.Random(seed ^ 0x7E26E), spacing, cellsB, density, res);
            ScatterRocks(track, terrain, rnd2, cellsB, density, res, lim);
            var sideTufts = new TracksideSet();
            ScatterFurniture(track, terrain, new System.Random(seed ^ 0x51DE17), cellsB, density, res, sideTufts);

            // 3) 合併成網格
            for (int cz = 0; cz < CellsPerSide; cz++)
                for (int cx = 0; cx < CellsPerSide; cx++)
                {
                    CellBuild cb = cellsB[cz * CellsPerSide + cx];
                    // 一區兩份網格(近處:其他造景 + 高面數樹;遠處:其他造景 + 低面數樹),同一時間只畫一份,所以 draw call 不增加
                    var nearAcc = new RaceColorAcc();
                    nearAcc.Append(cb.Acc); nearAcc.Append(cb.TreeHi);
                    cb.Acc.Append(cb.TreeLo);
                    EmitCell(root.transform, "Props_" + cx + "_" + cz, cb.Acc, TreeNearDist, RaceWorldLod.CullDist, owned, lod, res);
                    EmitCell(root.transform, "PropsNear_" + cx + "_" + cz, nearAcc, -1f, TreeNearDist, owned, lod, res);

                    if (cb.Colliders.Count > 0)
                    {
                        var cg = new GameObject("TreeColliders_" + cx + "_" + cz);
                        cg.transform.SetParent(root.transform, false);
                        for (int c = 0; c < cb.Colliders.Count; c++)
                        {
                            Vector4 p = cb.Colliders[c];
                            var cap = cg.AddComponent<CapsuleCollider>();
                            cap.direction = 1;
                            cap.radius = 0.32f * p.w;
                            cap.height = 3.2f * p.w;
                            cap.center = new Vector3(p.x, p.y + 1.6f * p.w, p.z);
                            res.Colliders++;
                        }
                    }
                }

            // 3a) 路邊草簇 / 花叢 / 看板(十字面片,依 48 m 區塊各自裁切)
            BuildTufts(root.transform, track, terrain, new System.Random(seed ^ 0x7F7A11), density, owned, lod, res, sideTufts);

            // 3b) 接觸陰影:路邊的樹與造景足跡底下各鋪一片柔邊影子(合併成一個網格、一個 draw call,上限 640 片 = 1280 三角形)
            BuildBlobShadows(root.transform, terrain, placed, owned, lod, res);

            // 4) 遠景:山脈 + 小島 + 雲
            BuildBackdrop(root.transform, terrain, seed, owned, res);
            BuildClouds(root.transform, seed, owned, res);
            lod.fixedTris += res.FixedTris;
            lod.fixedDraws += res.FixedDraws;

            trees = null; treesHi = null; bushes = null; covers = null; rocks = null; verge = null; look = null; theme = null;
            System.GC.Collect();   // 建圖時的暫存清單(近 / 遠兩份合併網格)不要留到遊戲中
            return res;
        }

        static void EmitCell(Transform root, string name, RaceColorAcc acc, float minDist, float cull, List<Object> owned, RaceWorldLod lod, Result res)
        {
            if (acc.TriCount == 0) return;
            var cell = new RaceWorldLod.Cell();
            var rs = new List<Renderer>();
            int tris = 0, draws = 0;
            acc.Emit(root, name, owned, rs, ref tris, ref draws, true);
            Bounds bb = default(Bounds); bool first = true;
            for (int m = 0; m < rs.Count; m++)
            {
                Mesh mesh = rs[m].GetComponent<MeshFilter>().sharedMesh;
                if (first) { bb = mesh.bounds; first = false; } else bb.Encapsulate(mesh.bounds);
            }
            cell.Tris = tris;
            cell.Renderers = rs.ToArray();
            cell.Bounds = bb;
            cell.MinDist = minDist;
            cell.Cull = cull;
            if (cull < RaceWorldLod.CullDist) { for (int r = 0; r < rs.Count; r++) rs[r].enabled = false; cell.On = false; }
            lod.cells.Add(cell);
            res.Tris += cell.Tris; res.Cells++;
        }

        static void BuildBlobShadows(Transform parent, RaceTerrain terrain, List<Placed> placed, List<Object> owned, RaceWorldLod lod, Result res)
        {
            Material bm = RaceShaders.Blob();
            if (bm == null) return;
            const int Cap = 640;
            var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var cs = new List<Color32>(); var ts = new List<int>();
            var sg = GameObject.Find("Sun");
            Vector2 away = new Vector2(0.3f, 0.2f);
            if (sg != null) { Vector3 f = sg.transform.forward; Vector2 a2 = new Vector2(f.x, f.z); if (a2.sqrMagnitude > 1e-4f) away = a2.normalized * 0.35f; }
            int quads = 0;
            for (int i = 0; i < placed.Count && quads < Cap; i++)
            {
                Placed pl = placed[i];
                if (pl.Dist > 22f) continue;
                float r = 1.15f * pl.P.w * (pl.Veg == (int)Veg.Pine || pl.Veg == (int)Veg.SnowPine || pl.Veg == (int)Veg.Poplar ? 1.0f : 1.25f);
                AddBlob(vs, uvs, cs, ts, terrain, pl.P.x, pl.P.z, r, away, 175);
                quads++;
            }
            if (AvoidCircles != null)
                for (int i = 0; i < AvoidCircles.Count && quads < Cap; i++)
                {
                    Vector4 a = AvoidCircles[i];
                    float r = Mathf.Clamp(a.w * 1.2f, 0.6f, 5f);
                    AddBlob(vs, uvs, cs, ts, terrain, a.x, a.z, r, away, 135);
                    quads++;
                }
            if (quads == 0) return;
            var mesh = new Mesh { name = "RaceBlobShadows" };
            mesh.SetVertices(vs); mesh.SetUVs(0, uvs); mesh.SetColors(cs); mesh.SetTriangles(ts, 0);
            mesh.RecalculateBounds();
            var g = new GameObject("BlobShadows");
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bm;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            owned.Add(mesh);
            res.FixedTris += quads * 2; res.FixedDraws += 1; res.Tris += quads * 2;
        }

        static void AddBlob(List<Vector3> vs, List<Vector2> uvs, List<Color32> cs, List<int> ts, RaceTerrain t, float x, float z, float r, Vector2 away, byte alpha)
        {
            float cx = x + away.x * r, cz = z + away.y * r, rr = r * 1.55f;
            int b = vs.Count;
            float[] ox = { -1f, 1f, 1f, -1f }, oz = { -1f, -1f, 1f, 1f };
            for (int k = 0; k < 4; k++)
            {
                float px = cx + ox[k] * rr, pz = cz + oz[k] * rr;
                vs.Add(new Vector3(px, t.Raw(px, pz) + 0.2f, pz));
                uvs.Add(new Vector2(k == 0 || k == 3 ? 0f : 1f, k < 2 ? 0f : 1f));
                cs.Add(new Color32(255, 255, 255, alpha));
            }
            ts.Add(b); ts.Add(b + 2); ts.Add(b + 1); ts.Add(b); ts.Add(b + 3); ts.Add(b + 2);
        }

        static int CellOf(float x, float z)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((x + RaceTerrain.Half) / CellSize), 0, CellsPerSide - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z + RaceTerrain.Half) / CellSize), 0, CellsPerSide - 1);
            return cz * CellsPerSide + cx;
        }

        static float Sm(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        /// 隨機色調:明度 0.9~1.1,冷暖微偏(不改變色相太多,保持可愛的飽和色)。
        static Color32 TintOf(float u1, float u2)
        {
            float v = 0.9f + 0.2f * u1, d = (u2 - 0.5f) * 0.1f;
            return new Color32((byte)Mathf.Clamp(255f * v * (1f + d), 0f, 255f), (byte)(Mathf.Clamp01(v) * 255f), (byte)Mathf.Clamp(255f * v * (1f - d), 0f, 255f), 255);
        }

        /// 樹林密度場 0..1(低頻 80 m + 中頻 33 m)。
        static float GroveAt(float x, float z)
        {
            return RaceNoise.Value(x * 0.011f + 7.7f, z * 0.011f - 2.3f, sceneSeed + 91) * 0.65f + RaceNoise.Value(x * 0.03f + 1.9f, z * 0.03f + 4.1f, sceneSeed + 92) * 0.35f;
        }

        // ------------------------------------------------------------------ 擺放規則

        /// 一個位置能不能放東西:在地圖內、不在海灘/水、不太陡、離路面邊緣夠遠、不在彎道內側視線範圍。
        static bool Valid(RaceTrack tr, RaceTerrain t, float x, float z, float minRoad, bool sight, out float dist)
        {
            dist = 999f;
            float lim = RaceTerrain.Half - 24f;
            if (x < -lim || x > lim || z < -lim || z > lim) return false;
            if (t.Raw(x, z) < 1.3f) return false;
            if (t.Normal(x, z).y < 0.86f) return false;
            float lat;
            float s = tr.Project(new Vector3(x, 0f, z), -1f, out lat);
            dist = Mathf.Abs(lat) - tr.HalfWidthAt(s);
            if (dist < minRoad) return false;
            if (t.ScEdgeAt(x, z) + 1f < minRoad) return false;   // 捷徑旁也要留空
            if (Avoid != null)
                for (int i = 0; i < Avoid.Count; i++) { float ex = Avoid[i].x - x, ez = Avoid[i].z - z; if (ex * ex + ez * ez < 12f * 12f) return false; }
            if (AvoidCircles != null)
                for (int i = 0; i < AvoidCircles.Count; i++) { Vector4 a = AvoidCircles[i]; float ex = a.x - x, ez = a.z - z, rr = a.w + 1.6f; if (ex * ex + ez * ez < rr * rr) return false; }
            if (sight && dist < 45f)
            {
                float k = tr.MaxCurvature(s - 30f, s + 30f);
                if (Mathf.Abs(k) > 1f / 110f && lat * k > 0f) return false;   // 在彎的內側(圓心那一側)
            }
            return true;
        }

        static bool Crowded(List<Vector3>[] grid, float x, float z, float minSep)
        {
            int cx = Mathf.Clamp((int)((x + RaceTerrain.Half) * 0.2f), 0, GN - 1), cz = Mathf.Clamp((int)((z + RaceTerrain.Half) * 0.2f), 0, GN - 1);
            int span = minSep > 5f ? 2 : 1;
            for (int dz = -span; dz <= span; dz++)
                for (int dx = -span; dx <= span; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    if (ix < 0 || ix > GN - 1 || iz < 0 || iz > GN - 1) continue;
                    var l = grid[iz * GN + ix];
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        float ex = l[i].x - x, ez = l[i].z - z;
                        float ms = minSep + l[i].y;   // y = 對方的額外半徑(灌木 0.6、樹 0)
                        if (ex * ex + ez * ez < ms * ms) return true;
                    }
                }
            return false;
        }

        static void Mark(List<Vector3>[] grid, float x, float z, float extra = 0f)
        {
            int cx = Mathf.Clamp((int)((x + RaceTerrain.Half) * 0.2f), 0, GN - 1), cz = Mathf.Clamp((int)((z + RaceTerrain.Half) * 0.2f), 0, GN - 1);
            int k = cz * GN + cx;
            if (grid[k] == null) grid[k] = new List<Vector3>();
            grid[k].Add(new Vector3(x, extra, z));
        }

        static void RoadsideRows(RaceTrack tr, RaceTerrain t, System.Random rnd, List<Vector3>[] grid, List<Placed> placed,
                                 float stepMin, float stepMax, float off0, float off1, float prob, Result res)
        {
            float L = tr.Length;
            for (float s = (float)rnd.NextDouble() * stepMin; s < L; s += stepMin + (float)rnd.NextDouble() * (stepMax - stepMin))
            {
                TrackSample sm = tr.SampleAt(s);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (rnd.NextDouble() > prob) continue;
                    float lat = sm.Width * 0.5f + MinRoadDist + off0 + (float)rnd.NextDouble() * (off1 - off0);
                    Vector3 p = sm.Pos + sm.Right * (side * lat) + sm.Tangent * (((float)rnd.NextDouble() - 0.5f) * 4f);
                    TryPlaceTree(tr, t, rnd, grid, placed, p.x, p.z, 4.5f, look.Grove * 0.35f, res);
                }
            }
        }

        static void TryPlaceTree(RaceTrack tr, RaceTerrain t, System.Random rnd, List<Vector3>[] grid, List<Placed> placed,
                                 float x, float z, float minSep, float groveMix, Result res)
        {
            // 隨機數一律先抽(不管這次有沒有放成),這樣規則微調不會讓後面所有樹整個換位置
            float rk = (float)rnd.NextDouble(), rv = (float)rnd.NextDouble(), rs = (float)rnd.NextDouble(), ry = (float)rnd.NextDouble();
            float rq = (float)rnd.NextDouble(), rt1 = (float)rnd.NextDouble(), rt2 = (float)rnd.NextDouble();
            float gv = GroveAt(x, z);
            float keep = Mathf.Lerp(1f, Sm(0.30f, 0.62f, gv), groveMix);   // 樹叢遮罩:空地機率變高
            if (rq > keep) return;
            float dist;
            if (!Valid(tr, t, x, z, MinRoadDist, true, out dist)) return;
            float sep = minSep * (1f - 0.28f * groveMix * Sm(0.5f, 0.8f, gv));   // 樹林核心可以靠得更近
            if (Crowded(grid, x, z, sep)) return;
            float h = t.Raw(x, z);
            Veg veg = look.PickVeg(h, rk);
            int variant = Mathf.Min(2, (int)(rv * 3f));
            float sc = 0.8f + 0.55f * rs * rs + 0.2f * groveMix * Sm(0.45f, 0.85f, gv);   // 偏小的多、偶爾一棵大樹;樹林中心長得高
            placed.Add(new Placed { P = new Vector4(x, h, z, sc), Veg = (int)veg, Var = variant, Yaw = ry * 360f, Dist = dist, T1 = rt1, T2 = rt2 });
            Mark(grid, x, z);
            if (dist < res.MinRoadDist) res.MinRoadDist = dist;
        }

        // ------------------------------------------------------------------ 灌木 / 地被 / 石頭

        static void ScatterBushes(RaceTrack tr, RaceTerrain t, System.Random rnd, List<Vector3>[] grid, CellBuild[] cells, float density, Result res)
        {
            float L = tr.Length;
            float prob = 0.38f * density * Mathf.Clamp(look.BushDensity, 0f, 2.2f);
            if (prob <= 0.01f) return;
            for (float s = (float)rnd.NextDouble() * 4f; s < L; s += 5.5f + (float)rnd.NextDouble() * 6f)
            {
                TrackSample sm = tr.SampleAt(s);
                for (int side = -1; side <= 1; side += 2)
                {
                    float r0 = (float)rnd.NextDouble(), r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble(), r3 = (float)rnd.NextDouble(), r4 = (float)rnd.NextDouble();
                    if (r0 > prob) continue;
                    float lat = sm.Width * 0.5f + 5.0f + r1 * 3.2f;
                    Vector3 p = sm.Pos + sm.Right * (side * lat) + sm.Tangent * ((r2 - 0.5f) * 4f);
                    float dist;
                    if (!Valid(tr, t, p.x, p.z, 5.0f, false, out dist)) continue;
                    if (Crowded(grid, p.x, p.z, 1.6f)) continue;
                    float h = t.Raw(p.x, p.z);
                    float sc = 0.75f + r3 * 0.7f;
                    int v = Mathf.Min(bushes.Length - 1, (int)(r4 * bushes.Length));
                    cells[CellOf(p.x, p.z)].Acc.Add(bushes[v], new Vector3(p.x, h - 0.05f, p.z), r2 * 360f, new Vector3(sc, sc * (0.85f + r1 * 0.3f), sc), TintOf(r3, r4));
                    Mark(grid, p.x, p.z, 0.6f);
                    res.Bushes++;
                }
            }
        }

        /// 路邊點綴:緊貼路肩外側的草簇 / 小花叢(草地系)、雪堆(雪地)、熔岩小尖(火山)。體積小、三角形便宜,讓路邊不再空蕩。
        static void ScatterVerge(RaceTrack tr, RaceTerrain t, System.Random rnd, List<Vector3>[] grid, CellBuild[] cells, float density, Result res)
        {
            float L = tr.Length;
            float prob = (look.Kind == LookKind.Snow ? 0.55f : (look.Kind == LookKind.Volcano ? 0.35f : (look.Kind == LookKind.Desert ? 0.3f : 0.6f))) * density;
            // 三角形預算:整條賽道的路邊點綴總量上限約 4500 三角形(worst-frame 預算 150k 很吃緊),依原型平均三角形數反推機率
            float avgT = 0f;
            for (int i = 0; i < verge.Length; i++) avgT += verge[i].T.Length / 3f;
            avgT = Mathf.Max(8f, avgT / verge.Length);
            float slots = L / 4.3f * 2f;
            prob = Mathf.Min(prob, 3500f * density / (slots * avgT * 1.05f));
            for (float s = (float)rnd.NextDouble() * 3f; s < L; s += 3.0f + (float)rnd.NextDouble() * 2.6f)
            {
                TrackSample sm = tr.SampleAt(s);
                for (int side = -1; side <= 1; side += 2)
                {
                    float r0 = (float)rnd.NextDouble(), r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble(), r3 = (float)rnd.NextDouble(), r4 = (float)rnd.NextDouble();
                    if (r0 > prob) continue;
                    float lat = sm.Width * 0.5f + 4.3f + r1 * 3.4f;
                    Vector3 p = sm.Pos + sm.Right * (side * lat) + sm.Tangent * ((r2 - 0.5f) * 3f);
                    float dist;
                    if (!Valid(tr, t, p.x, p.z, 3.9f, false, out dist)) continue;
                    if (Crowded(grid, p.x, p.z, 0.9f)) continue;
                    float h = t.Raw(p.x, p.z);
                    float sc = look.Kind == LookKind.Snow ? 0.8f + r3 * 0.9f : (look.Kind == LookKind.Volcano ? 0.45f + r3 * 0.35f : 0.8f + r3 * 0.6f);
                    int v = Mathf.Min(verge.Length - 1, (int)(r4 * verge.Length));
                    cells[CellOf(p.x, p.z)].Acc.Add(verge[v], new Vector3(p.x, h - 0.04f, p.z), r2 * 360f, new Vector3(sc, sc * (0.9f + r1 * 0.3f), sc), TintOf(r3, r4));
                    Mark(grid, p.x, p.z, 0.3f);
                    res.Cover++;
                }
            }
        }

        static void ScatterCover(RaceTrack tr, RaceTerrain t, System.Random rnd, List<Vector3>[] grid, CellBuild[] cells, float density, Result res, float lim)
        {
            int target = Mathf.RoundToInt(100 * density * look.CoverDensity);
            int made = 0, guard = 0;
            while (made < target && guard < target * 14)
            {
                guard++;
                float x = (float)(rnd.NextDouble() * 2.0 * lim - lim), z = (float)(rnd.NextDouble() * 2.0 * lim - lim);
                float r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble(), r3 = (float)rnd.NextDouble(), r4 = (float)rnd.NextDouble();
                if (GroveAt(x, z) < 0.3f && r4 < 0.6f) continue;   // 地被偏愛樹林邊緣與林下
                float dist;
                if (!Valid(tr, t, x, z, 7.0f, false, out dist)) continue;
                if (Crowded(grid, x, z, 2.2f)) continue;
                float h = t.Raw(x, z);
                float sc = 0.8f + r1 * 0.7f;
                int v = Mathf.Min(covers.Length - 1, (int)(r2 * covers.Length));
                cells[CellOf(x, z)].Acc.Add(covers[v], new Vector3(x, h - 0.03f, z), r3 * 360f, Vector3.one * sc, TintOf(r4, r1));
                Mark(grid, x, z, 0.4f);
                made++;
            }
            res.Cover = made;
        }

        static void ScatterRocks(RaceTrack tr, RaceTerrain t, System.Random rnd, CellBuild[] cells, float density, Result res, float lim)
        {
            int rockTarget = Mathf.RoundToInt(70 * density * look.RockDensity), tries = 0;
            while (res.Rocks < rockTarget && tries < 2400)
            {
                tries++;
                float x = (float)(rnd.NextDouble() * 2.0 * lim - lim), z = (float)(rnd.NextDouble() * 2.0 * lim - lim);
                float dist;
                if (!Valid(tr, t, x, z, 9f, false, out dist)) continue;
                float h = t.Raw(x, z);
                if (h < 2.2f) continue;
                float sc = 1.0f + (float)rnd.NextDouble() * (float)rnd.NextDouble() * 3.4f;   // 小石頭多、偶爾一顆大石
                float yaw = (float)rnd.NextDouble() * 360f;
                CellBuild cb = cells[CellOf(x, z)];
                int n = 1 + (int)(rnd.NextDouble() * 3.0);   // 一叢 1~3 顆:主石 + 小石
                for (int k = 0; k < n; k++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, rd = k == 0 ? 0f : sc * (0.9f + (float)rnd.NextDouble() * 0.7f);
                    float ss = k == 0 ? sc : sc * (0.35f + (float)rnd.NextDouble() * 0.3f);
                    float px = x + Mathf.Cos(a) * rd, pz = z + Mathf.Sin(a) * rd;
                    float ph = Mathf.Max(h - 0.2f, t.Raw(px, pz) - 0.15f * ss);
                    var scl = new Vector3(ss * (0.85f + (float)rnd.NextDouble() * 0.45f), ss * 0.75f, ss * (0.85f + (float)rnd.NextDouble() * 0.45f));
                    int v = (int)(rnd.NextDouble() * rocks.Length) % rocks.Length;
                    cb.Acc.Add(rocks[v], new Vector3(px, ph, pz), (float)rnd.NextDouble() * 360f, scl, TintOf((float)rnd.NextDouble(), (float)rnd.NextDouble()));
                }
                res.Rocks++;
            }
        }

        // ------------------------------------------------------------------ 原型烘焙

        static Color L(int i) { return theme.Leaves[((i % theme.Leaves.Length) + theme.Leaves.Length) % theme.Leaves.Length]; }
        static Color Shade(Color c, float k) { return Color.Lerp(c, new Color(0.05f, 0.16f, 0.22f), k); }       // 偏冷的暗部
        static Color Sunny(Color c, float k) { return Color.Lerp(c, new Color(1f, 1f, 0.62f), k); }            // 偏暖的亮部
        static float Rf(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static void BakeProtos(int seed)
        {
            trees = new RaceProto[(int)Veg.Count][];
            treesHi = new RaceProto[(int)Veg.Count][];
            for (int k = 0; k < (int)Veg.Count; k++)
            {
                var vk = (Veg)k;
                if (vk != Veg.Round && !look.Uses(vk)) { trees[k] = new RaceProto[3]; treesHi[k] = new RaceProto[3]; continue; }
                treesHi[k] = new RaceProto[3];   // 用不到的樹種留空(Round 一定烘:PickVeg 的後備)   // 用不到的樹種留空(PickVeg 不會選到)
                trees[k] = new RaceProto[3];
                for (int v = 0; v < 3; v++)
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool hi = pass == 1;
                        var r = new System.Random(seed * 7919 + k * 131 + v * 17 + 1);
                        RaceProto pr;
                        switch (vk)
                        {
                            case Veg.Round: pr = MakeRound(L(v), r, hi, RaceTexArray.Leafy); break;
                            case Veg.Blossom: pr = MakeRound(Color.Lerp(look.Blossom, Color.white, 0.12f * v), r, hi, RaceTexArray.Blossom); break;
                            case Veg.Autumn: pr = MakeRound(Color.Lerp(look.Autumn, new Color(1f, 0.85f, 0.2f), 0.28f * v), r, hi, RaceTexArray.Autumn); break;
                            case Veg.Pine: pr = MakePine(Shade(L(v + 1), 0.1f), false, r, hi); break;
                            case Veg.SnowPine: pr = MakePine(Shade(L(v + 1), 0.28f), true, r, hi); break;
                            case Veg.Poplar: pr = MakePoplar(L(v), r, hi); break;
                            case Veg.Palm: pr = MakePalm(L(v), r, hi); break;
                            default: pr = MakeDead(Color.Lerp(RacePalette.Trunk, theme.Rock, 0.4f + 0.15f * v), r, hi); break;
                        }
                        if (hi) treesHi[k][v] = pr; else trees[k][v] = pr;
                    }
                }
            }

            // 灌木:3 款(其中一款帶花)
            bushes = new RaceProto[3];
            for (int v = 0; v < 3; v++)
            {
                var r = new System.Random(seed * 104729 + 31 + v);
                Color leaf = look.Kind == LookKind.Snow ? Color.Lerp(L(v), Color.white, 0.5f) : L(v + 1);
                bushes[v] = MakeBush(leaf, (v == 1 && look.BushFlowers) ? look.Flowers[0] : (Color?)null, r);
            }

            // 地被:3 款
            covers = new RaceProto[3];
            for (int v = 0; v < 3; v++)
            {
                var r = new System.Random(seed * 15485863 + 7 + v);
                switch (look.CoverKind)
                {
                    case 1: covers[v] = MakeMushrooms(v, r); break;
                    case 2: covers[v] = MakeSnowMound(r); break;
                    case 3: covers[v] = MakeLavaSpikes(r); break;
                    case 4: covers[v] = MakeTuft(r, false); break;
                    default: covers[v] = MakeFlowerPatch(look.Flowers[(v * 2) % look.Flowers.Length], L(v + 1), r); break;
                }
            }

            // 路邊點綴
            verge = new RaceProto[4];
            for (int v = 0; v < 4; v++)
            {
                var r = new System.Random(seed * 3571 + 101 + v);
                if (look.Kind == LookKind.Snow) verge[v] = MakeSnowMound(r);
                else if (look.Kind == LookKind.Volcano) verge[v] = MakeLavaSpikes(r);
                else verge[v] = MakeTuft(r, look.Kind != LookKind.Desert && look.BushFlowers && v >= 1);
            }

            // 石頭:3 款稜角石
            rocks = new RaceProto[3];
            for (int v = 0; v < 3; v++)
            {
                var b = new RaceProtoBuilder();
                Color baseC = Color.Lerp(theme.Rock, theme.MtnRock, 0.25f * v);
                b.Rock(new Vector3(0f, 0.38f, 0f), new Vector3(0.62f, 0.55f - 0.06f * v, 0.55f + 0.08f * v), seed * 31 + v * 977 + 5, Color.Lerp(baseC, Color.black, 0.18f), Color.Lerp(baseC, look.RockTop, 0.65f));
                rocks[v] = b.Build();
            }
        }

        // 樹幹:有貼圖(樹皮)的錐形旋轉體,底部有樹根外擴
        static void Trunk(RaceProtoBuilder b, float h, float r0, int sides, bool hi, System.Random r)
        {
            Color trunk = RacePalette.Trunk, tDark = Color.Lerp(trunk, Color.black, 0.32f);
            b.UseTex(RaceTexArray.Bark, 1f);
            if (hi)
                b.Lathe(Vector3.zero, new[] { r0 * 1.55f, r0 * 1.0f, r0 * 0.78f, r0 * 0.62f }, new[] { 0f, h * 0.12f, h * 0.55f, h }, new[] { tDark, Color.Lerp(tDark, trunk, 0.6f), trunk, trunk }, sides, false, false, Rf(r, 0f, 72f));
            else
                b.Lathe(Vector3.zero, new[] { r0 * 1.1f, r0 * 0.65f }, new[] { 0f, h }, new[] { tDark, trunk }, sides, false, false, Rf(r, 0f, 72f));
            b.UseTex(0);
        }

        static RaceProto MakeRound(Color leaf, System.Random r, bool hi, int slice)
        {
            // 蓬鬆樹冠:高面數 = 1 顆大球(80 面)+ 4 顆側邊小球 + 頂上一顆小球;低面數 = 3 顆(20 面)。球面 uv + 葉簇貼圖,頂點色由暗冷漸層到亮暖(假環境光遮蔽)
            var b = new RaceProtoBuilder();
            Trunk(b, hi ? 2.9f : 2.4f, 0.27f, hi ? 6 : 5, hi, r);
            Color lo = Shade(leaf, 0.34f), mid = leaf, top = Sunny(leaf, 0.24f);
            float s = Rf(r, 0.92f, 1.1f), a = Rf(r, 0f, 6.28f);
            b.UseTex(slice, 1f);
            if (hi)
            {
                b.Blob(new Vector3(0f, 4.15f, 0f), new Vector3(2.15f * s, 1.75f * s, 2.15f * s), 1, lo, mid, top, Rf(r, 0f, 72f));
                for (int k = 0; k < 4; k++)
                {
                    float ak = a + k * 1.5708f + Rf(r, -0.25f, 0.25f), d = 1.75f * s;
                    b.Blob(new Vector3(Mathf.Cos(ak) * d, 3.2f + Rf(r, -0.2f, 0.35f), Mathf.Sin(ak) * d), new Vector3(1.3f, 1.05f, 1.3f), 0, lo, mid, top, Rf(r, 0f, 72f));
                }
                b.Blob(new Vector3(Mathf.Cos(a + 0.7f) * 0.5f, 5.55f, Mathf.Sin(a + 0.7f) * 0.5f), new Vector3(1.2f, 0.95f, 1.2f), 0, mid, top, Sunny(top, 0.2f), Rf(r, 0f, 72f));
            }
            else
            {
                b.Blob(new Vector3(0f, 3.5f, 0f), new Vector3(2.2f * s, 1.95f * s, 2.2f * s), 0, lo, mid, top, Rf(r, 0f, 72f));
                b.Blob(new Vector3(Mathf.Cos(a) * 1.5f, 2.7f, Mathf.Sin(a) * 1.5f), new Vector3(1.35f, 1.1f, 1.35f), 0, lo, mid, top, Rf(r, 0f, 72f));
                b.Blob(new Vector3(-Mathf.Cos(a) * 1.1f, 4.35f, -Mathf.Sin(a) * 1.1f), new Vector3(1.15f, 1.0f, 1.15f), 0, mid, top, Sunny(top, 0.2f), Rf(r, 0f, 72f));
            }
            return b.Build();
        }

        static RaceProto MakePine(Color leaf, bool snow, System.Random r, bool hi)
        {
            // 松樹:高面數 = 4 層向外下垂的錐形層(每層 3 圈:下垂裙邊 -> 外緣 -> 腰 -> 尖),雪松的上半部蓋白雪;低面數 = 3 層簡單錐
            var b = new RaceProtoBuilder();
            Trunk(b, hi ? 1.7f : 1.8f, 0.25f, 5, hi, r);
            Color snowC = new Color(0.97f, 0.99f, 1f);
            b.UseTex(RaceTexArray.Pine, 1f);
            int tiers = hi ? 4 : 3;
            for (int i = 0; i < tiers; i++)
            {
                float t01 = i / (float)(tiers - 1);
                float y0 = 0.85f + i * (hi ? 1.02f : 1.15f), rad = (hi ? 2.15f : 1.95f) - i * (hi ? 0.52f : 0.52f), hgt = (hi ? 1.9f : 1.95f) - i * 0.1f;
                Color lip = Shade(leaf, 0.2f + 0.03f * i), edge = Shade(leaf, 0.08f), waist = leaf, tip = Sunny(leaf, 0.3f);
                if (snow) { lip = Color.Lerp(lip, snowC, 0.25f); edge = Color.Lerp(edge, snowC, 0.55f); waist = Color.Lerp(waist, snowC, 0.85f); tip = snowC; }
                float yaw = Rf(r, 0f, 50f);
                if (hi)
                    b.Lathe(new Vector3(0f, y0, 0f), new[] { rad * 0.8f, rad, rad * 0.58f, 0f }, new[] { -0.16f, 0f, hgt * 0.48f, hgt }, new[] { lip, edge, waist, tip }, 7, true, false, yaw);
                else
                    b.Lathe(new Vector3(0f, y0, 0f), new[] { rad, 0f }, new[] { 0f, hgt }, new[] { snow ? Color.Lerp(lip, snowC, 0.4f) : lip, tip }, 7, true, false, yaw);
            }
            return b.Build();
        }

        static RaceProto MakePoplar(Color leaf, System.Random r, bool hi)
        {
            var b = new RaceProtoBuilder();
            Trunk(b, 1.7f, 0.25f, 5, hi, r);
            float s = Rf(r, 0.92f, 1.1f);
            Color lo = Shade(leaf, 0.36f), top = Sunny(leaf, 0.22f);
            b.UseTex(RaceTexArray.Leafy, 0.9f);
            if (hi)
            {
                b.Blob(new Vector3(0f, 2.7f, 0f), new Vector3(1.25f * s, 1.5f, 1.25f * s), 0, lo, leaf, top, Rf(r, 0f, 72f));
                b.Blob(new Vector3(0f, 4.0f, 0f), new Vector3(1.1f * s, 1.5f, 1.1f * s), 0, lo, leaf, top, Rf(r, 0f, 72f));
                b.Blob(new Vector3(0f, 5.3f, 0f), new Vector3(0.9f * s, 1.45f, 0.9f * s), 0, leaf, top, Sunny(top, 0.2f), Rf(r, 0f, 72f));
                b.Blob(new Vector3(0f, 6.4f, 0f), new Vector3(0.55f * s, 1.0f, 0.55f * s), 0, leaf, top, Sunny(top, 0.3f), Rf(r, 0f, 72f));
            }
            else
            {
                b.Blob(new Vector3(0f, 3.0f, 0f), new Vector3(1.2f * s, 2.5f, 1.2f * s), 0, lo, leaf, top, Rf(r, 0f, 72f));
                b.Blob(new Vector3(0f, 5.0f, 0f), new Vector3(0.85f * s, 1.6f, 0.85f * s), 0, leaf, top, Sunny(top, 0.2f), Rf(r, 0f, 72f));
            }
            return b.Build();
        }

        static RaceProto MakePalm(Color leaf, System.Random r, bool hi)
        {
            var b = new RaceProtoBuilder();
            Color t0 = Color.Lerp(RacePalette.Trunk, new Color(0.85f, 0.7f, 0.45f), 0.35f), t1 = Color.Lerp(t0, Color.black, 0.18f);
            float lean = Rf(r, 0.8f, 1.3f);
            Vector3[] p = { Vector3.zero, new Vector3(0.25f * lean, 1.7f, 0f), new Vector3(0.65f * lean, 3.3f, 0f), new Vector3(1.15f * lean, 4.7f, 0f) };
            float[] rad = { 0.36f, 0.29f, 0.23f, 0.18f };
            b.UseTex(RaceTexArray.Bark, 1f);
            for (int i = 0; i < 3; i++) b.Tube(p[i], p[i + 1], rad[i], rad[i + 1], hi ? 6 : 4, (i & 1) == 0 ? t0 : t1, (i & 1) == 0 ? t1 : t0);
            b.UseTex(0);
            Vector3 top = p[3];
            Color fDark = Shade(leaf, 0.2f), fLight = Sunny(leaf, 0.3f);
            int fronds = hi ? 8 : 5;
            for (int i = 0; i < fronds; i++)
            {
                float ang = (i * (360f / fronds) + Rf(r, -12f, 12f)) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float len = Rf(r, 2.6f, 3.4f);
                Vector3 bs = top + Vector3.up * 0.1f, mid = bs + dir * (len * 0.5f) + Vector3.up * 0.85f, tip = bs + dir * len + Vector3.up * -0.55f;
                b.Blade(bs, mid, tip, hi ? 1.25f : 1.0f, Vector3.Cross(dir, Vector3.up), fDark, fLight);
            }
            if (hi) b.Blob(top + Vector3.up * 0.15f, new Vector3(0.28f, 0.22f, 0.28f), 0, t1, t0, t0);   // 椰子叢
            return b.Build();
        }

        static RaceProto MakeDead(Color wood, System.Random r, bool hi)
        {
            var b = new RaceProtoBuilder();
            Color dark = Color.Lerp(wood, Color.black, 0.3f);
            b.UseTex(RaceTexArray.Bark, 1f);
            b.Lathe(Vector3.zero, hi ? new[] { 0.55f, 0.38f, 0.27f, 0.14f } : new[] { 0.38f, 0.27f, 0.14f }, hi ? new[] { 0f, 0.4f, 1.7f, 3.5f } : new[] { 0f, 1.7f, 3.5f }, hi ? new[] { dark, dark, wood, wood } : new[] { dark, wood, wood }, hi ? 6 : 5, false, false, Rf(r, 0f, 72f));
            float a = Rf(r, 0f, 6.28f);
            Vector3 d1 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), d2 = new Vector3(Mathf.Cos(a + 2.4f), 0f, Mathf.Sin(a + 2.4f));
            b.Tube(new Vector3(0f, 2.2f, 0f), new Vector3(0f, 3.5f, 0f) + d1 * 1.2f, 0.14f, 0.05f, 4, wood, dark);
            b.Tube(new Vector3(0f, 2.8f, 0f), new Vector3(0f, 4.0f, 0f) + d2 * 1.0f, 0.12f, 0.04f, 4, wood, dark);
            if (hi) b.Tube(new Vector3(0f, 1.5f, 0f), new Vector3(0f, 2.4f, 0f) - d1 * 0.9f, 0.1f, 0.04f, 4, wood, dark);
            return b.Build();
        }

        static RaceProto MakeBush(Color leaf, Color? flower, System.Random r)
        {
            var b = new RaceProtoBuilder();
            Color lo = Shade(leaf, 0.3f), hi = Sunny(leaf, 0.2f);
            b.Blob(new Vector3(0f, 0.55f, 0f), new Vector3(1.05f, 0.8f, 1.05f), 0, lo, hi, Rf(r, 0f, 72f));
            if (flower.HasValue)
                for (int i = 0; i < 3; i++)
                {
                    float a = Rf(r, 0f, 6.28f), e = Rf(r, 0.35f, 0.9f);
                    b.Octa(new Vector3(Mathf.Cos(a) * 0.8f * e, 0.55f + 0.75f * (1f - e) + 0.1f, Mathf.Sin(a) * 0.8f * e), 0.15f, 0.8f, flower.Value, Color.Lerp(flower.Value, Color.white, 0.5f));
                }
            else if (r.NextDouble() < 0.5)
                b.Blob(new Vector3(0.75f, 0.38f, 0.3f), new Vector3(0.65f, 0.5f, 0.65f), 0, lo, leaf, Rf(r, 0f, 72f));
            return b.Build();
        }

        static RaceProto MakeFlowerPatch(Color main, Color leaf, System.Random r)
        {
            var b = new RaceProtoBuilder();
            b.Blob(new Vector3(0f, 0.12f, 0f), new Vector3(0.95f, 0.25f, 0.95f), 0, Shade(leaf, 0.3f), Sunny(leaf, 0.1f), Rf(r, 0f, 72f));
            for (int i = 0; i < 4; i++)
            {
                float a = Rf(r, 0f, 6.28f), d = Rf(r, 0.1f, 0.8f);
                Color c = r.NextDouble() < 0.65 ? main : Color.Lerp(main, Color.white, 0.6f);
                b.Octa(new Vector3(Mathf.Cos(a) * d, 0.42f + Rf(r, 0f, 0.12f), Mathf.Sin(a) * d), 0.16f, 0.85f, c, Color.Lerp(c, Color.white, 0.45f));
            }
            return b.Build();
        }

        static RaceProto MakeMushrooms(int v, System.Random r)
        {
            var b = new RaceProtoBuilder();
            Color[] caps = { new Color(1f, 0.32f, 0.3f), new Color(1f, 0.62f, 0.2f), new Color(0.78f, 0.45f, 0.95f) };
            Color cap = caps[v % caps.Length], stem = new Color(1f, 0.95f, 0.82f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 o = i == 0 ? Vector3.zero : new Vector3(Rf(r, 0.5f, 0.8f), 0f, Rf(r, -0.4f, 0.4f));
                float k = i == 0 ? 1f : 0.65f;
                b.Lathe(o, new[] { 0.16f * k, 0.12f * k }, new[] { 0f, 0.55f * k }, new[] { Color.Lerp(stem, Color.black, 0.15f), stem }, 5, false, false);
                b.Lathe(o + new Vector3(0f, 0.5f * k, 0f), new[] { 0.62f * k, 0.46f * k, 0f }, new[] { 0f, 0.2f * k, 0.38f * k }, new[] { Color.Lerp(cap, Color.black, 0.2f), cap, Sunny(cap, 0.25f) }, 6, true, false);
            }
            return b.Build();
        }

        static RaceProto MakeSnowMound(System.Random r)
        {
            var b = new RaceProtoBuilder();
            b.Blob(new Vector3(0f, 0.2f, 0f), new Vector3(1.5f, 0.62f, 1.2f), 0, new Color(0.78f, 0.86f, 1f), new Color(0.98f, 0.99f, 1f), Rf(r, 0f, 72f));
            return b.Build();
        }

        static RaceProto MakeLavaSpikes(System.Random r)
        {
            var b = new RaceProtoBuilder();
            Color dark = new Color(0.2f, 0.16f, 0.2f), glow = new Color(1f, 0.46f, 0.12f, 0.1f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + Rf(r, 0f, 0.5f), d = i == 0 ? 0f : Rf(r, 0.5f, 0.85f);
                float h = (i == 0 ? 2.1f : 1.3f) * Rf(r, 0.85f, 1.15f), rr = i == 0 ? 0.55f : 0.38f;
                b.Lathe(new Vector3(Mathf.Cos(a) * d, -0.05f, Mathf.Sin(a) * d), new[] { rr, 0f }, new[] { 0f, h }, new[] { dark, glow }, 5, true, false, Rf(r, 0f, 72f));
            }
            return b.Build();
        }

        static RaceProto MakeTuft(System.Random r, bool flowers = false)
        {
            var b = new RaceProtoBuilder();
            bool dry = look.Kind == LookKind.Desert;
            Color lo = dry ? Shade(Color.Lerp(theme.GrassDark, theme.Sand, 0.25f), 0.1f) : Shade(theme.GrassDark, 0.12f);
            Color hi = dry ? Sunny(Color.Lerp(theme.GrassLight, theme.Sand, 0.3f), 0.1f) : Sunny(Color.Lerp(theme.GrassLight, Color.white, 0.12f), 0.2f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.6f + Rf(r, 0f, 0.5f), d = i == 0 ? 0f : Rf(r, 0.15f, 0.42f);
                b.Lathe(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), new[] { 0.17f, 0f }, new[] { 0f, Rf(r, 0.6f, 1.35f) }, new[] { lo, hi }, 3, false, false, Rf(r, 0f, 90f));
            }
            if (flowers)
                for (int i = 0; i < 2; i++)
                {
                    float a = Rf(r, 0f, 6.28f), d = Rf(r, 0.25f, 0.55f);
                    Color c = look.Flowers[r.Next(look.Flowers.Length)];
                    b.Octa(new Vector3(Mathf.Cos(a) * d, 0.5f + Rf(r, 0f, 0.25f), Mathf.Sin(a) * d), 0.13f, 0.85f, c, Color.Lerp(c, Color.white, 0.45f));
                }
            return b.Build();
        }

        // ------------------------------------------------------------------ 遠景:山脈 + 小島

        static readonly Color Lava = new Color(1f, 0.45f, 0.12f, 0.2f);   // alpha < 1 = 自發光(RaceToonVC)

        /// 一座遠山。shape 決定剖面:Alpine 尖山帶雪帽、Round 圓丘、Mesa 方山(平頂)、Volcano 火山(有火口)。
        static void AddPeak(RaceColorAcc acc, System.Random rnd, Vector3 c, float H, float R, PeakShape shape, Color foot, Color rock, Color snow, int S)
        {
            float[] pr, ph; int[] band;
            switch (shape)
            {
                case PeakShape.Round: pr = new[] { 1.0f, 0.78f, 0.42f, 0f }; ph = new[] { 0f, 0.5f, 0.88f, 1f }; band = new[] { 0, 0, 0 }; break;
                case PeakShape.Mesa: pr = new[] { 1.0f, 0.72f, 0.66f, 0f }; ph = new[] { 0f, 0.5f, 0.9f, 0.92f }; band = new[] { 0, 1, 2 }; break;
                case PeakShape.Volcano: pr = new[] { 1.0f, 0.55f, 0.30f, 0.22f, 0f }; ph = new[] { 0f, 0.62f, 1.0f, 0.9f, 0.8f }; band = new[] { 0, 1, 1, 3 }; break;
                default: pr = new[] { 1.0f, 0.62f, 0.32f, 0f }; ph = new[] { 0f, 0.38f, 0.72f, 1f }; band = new[] { 0, 1, 2 }; break;
            }
            int rings = pr.Length;
            float rot = (float)rnd.NextDouble() * Mathf.PI * 2f;
            var P = new Vector3[rings][];
            for (int k = 0; k < rings; k++)
            {
                P[k] = new Vector3[S];
                float jx = (float)rnd.NextDouble() * 0.5f * (k == rings - 1 ? 0.3f : 1f);
                for (int i = 0; i < S; i++)
                {
                    float a = rot + i * Mathf.PI * 2f / S + (k > 0 ? 0.1f * k : 0f);
                    float j = 0.88f + (float)rnd.NextDouble() * 0.26f;
                    float rr = pr[k] * R * (k == 0 ? j : (pr[k] > 0f ? j : 1f));
                    P[k][i] = c + new Vector3(Mathf.Cos(a) * rr + (pr[k] == 0f ? (jx - 0.25f) * R * 0.2f : 0f), ph[k] * H, Mathf.Sin(a) * rr);
                }
            }
            Vector3 refP = c + new Vector3(0f, H * 0.45f, 0f);
            Color[] bc = { foot, rock, snow, Lava };
            for (int k = 0; k < rings - 1; k++)
            {
                Color col = bc[band[k]];
                for (int i = 0; i < S; i++)
                {
                    int j = (i + 1) % S;
                    float shadeJ = 0.94f + (float)rnd.NextDouble() * 0.12f;
                    Color cf = new Color(col.r * shadeJ, col.g * shadeJ, col.b * shadeJ, 1f);
                    Color32 c32 = cf;
                    Vector3 a0 = P[k][i], a1 = P[k][j], b0 = P[k + 1][i], b1 = P[k + 1][j];
                    bool apex = pr[k + 1] == 0f;
                    FaceOut(acc, a0, a1, b1, refP, c32);
                    if (!apex) FaceOut(acc, a0, b1, b0, refP, c32);
                }
            }
        }

        static readonly float[][] MtnHf = {
            new[] { 0f, 0.20f, 0.42f, 0.64f, 0.82f, 1.00f },      // Alpine
            new[] { 0f, 0.26f, 0.52f, 0.76f, 0.92f, 1.00f },      // Round
            new[] { 0f, 0.30f, 0.58f, 0.82f, 0.90f, 0.90f },      // Mesa
            new[] { 0f, 0.30f, 0.62f, 0.90f, 1.00f, 0.86f } };    // Volcano
        static readonly float[][] MtnRf = {
            new[] { 1f, 0.76f, 0.52f, 0.32f, 0.15f, 0f },
            new[] { 1f, 0.88f, 0.68f, 0.42f, 0.2f, 0f },
            new[] { 1f, 0.90f, 0.76f, 0.68f, 0.62f, 0f },
            new[] { 1f, 0.70f, 0.44f, 0.28f, 0.22f, 0.12f } };

        /// 一座有稜線的山塊(可橢圓、可偏心):環形網格 + 平滑法線 + 山貼圖 uv(v = 高度,下草 / 林 -> 中岩 -> 上雪)。
        /// segs 邊、6 圈(最上一圈 = 尖頂點或平頂蓋);ridge = 稜線起伏強度。
        static void AddMassif(RaceColorAcc acc, System.Random rnd, Vector3 c, float H, float R, float ax, float az, float yaw, PeakShape shape, int slice, float vMax, float vH, Color32 tint, int S, float ridge)
        {
            float[] hf = MtnHf[(int)shape], rf = MtnRf[(int)shape];
            int rings = hf.Length;
            float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f, p3 = (float)rnd.NextDouble() * 6.28f, p4 = (float)rnd.NextDouble() * 6.28f;
            float rot = (float)rnd.NextDouble() * 6.28f;
            float cy = Mathf.Cos(yaw * Mathf.Deg2Rad), sy = Mathf.Sin(yaw * Mathf.Deg2Rad);
            int cols = S + 1;
            var P = new Vector3[rings * cols]; var U = new Vector3[rings * cols]; var Cc = new Color32[rings * cols]; var Nn = new Vector3[rings * cols];
            float amp = shape == PeakShape.Mesa ? 0.45f : (shape == PeakShape.Round ? 0.55f : 1f);
            for (int k = 0; k < rings; k++)
                for (int i = 0; i < cols; i++)
                {
                    int ii = i % S;
                    float th = rot + ii * 6.2832f / S;
                    float hh = hf[k];
                    float rid = (1f - Mathf.Abs(Mathf.Sin(th * 2.5f + p1))) * 0.55f + (1f - Mathf.Abs(Mathf.Sin(th * 4.5f + p2))) * 0.3f + Mathf.Sin(th * 7f + p3) * 0.08f;
                    float rad = 1f + ridge * amp * (rid - 0.45f) * (0.25f + 0.75f * hh) + 0.05f * Mathf.Sin(th * 11f + p4) * hh;
                    float rr = R * rf[k] * rad;
                    float lx = Mathf.Cos(th) * rr * ax, lz = Mathf.Sin(th) * rr * az;
                    float yy = hh * H * (1f + 0.09f * ridge * amp * (rid - 0.45f) * hh);
                    int vi = k * cols + i;
                    P[vi] = new Vector3(c.x + lx * cy - lz * sy, c.y + yy, c.z + lx * sy + lz * cy);
                    float vv = Mathf.Clamp01(yy / Mathf.Max(1f, vH)) * vMax;
                    U[vi] = new Vector3((float)i / S * (2f + R / 160f), 0.02f + vv * 0.96f, slice);
                    float sh = 0.93f + 0.1f * (float)rnd.NextDouble();
                    byte al = 255;
                    if (shape == PeakShape.Volcano) { if (k == rings - 1) al = 60; else if (k == rings - 2) al = 170; }
                    Cc[vi] = new Color32((byte)(tint.r * sh), (byte)(tint.g * sh), (byte)(tint.b * sh), al);
                }
            // 平滑法線:累加面法線,最後朝外
            var tri = new List<int>();
            for (int k = 0; k < rings - 1; k++)
                for (int i = 0; i < S; i++)
                {
                    int a = k * cols + i, b = k * cols + i + 1, d = (k + 1) * cols + i, e = (k + 1) * cols + i + 1;
                    tri.Add(a); tri.Add(d); tri.Add(b); tri.Add(b); tri.Add(d); tri.Add(e);
                }
            Vector3 mid = c + new Vector3(0f, H * 0.4f, 0f);
            for (int t = 0; t < tri.Count; t += 3)
            {
                Vector3 n = Vector3.Cross(P[tri[t + 1]] - P[tri[t]], P[tri[t + 2]] - P[tri[t]]);
                if (Vector3.Dot(n, (P[tri[t]] + P[tri[t + 1]] + P[tri[t + 2]]) / 3f - mid) < 0f) { int tmp = tri[t + 1]; tri[t + 1] = tri[t + 2]; tri[t + 2] = tmp; n = -n; }
                Nn[tri[t]] += n; Nn[tri[t + 1]] += n; Nn[tri[t + 2]] += n;
            }
            for (int k = 0; k < rings; k++) { Nn[k * cols + S] = Nn[k * cols] = Nn[k * cols] + Nn[k * cols + S]; }
            for (int i = 0; i < Nn.Length; i++) Nn[i] = Nn[i].sqrMagnitude < 1e-10f ? Vector3.up : Nn[i].normalized;
            // 頂點太靠近的(尖頂 rf = 0)三角形略過
            var keep = new List<int>();
            for (int t = 0; t < tri.Count; t += 3)
            {
                Vector3 n = Vector3.Cross(P[tri[t + 1]] - P[tri[t]], P[tri[t + 2]] - P[tri[t]]);
                if (n.sqrMagnitude < 1e-3f) continue;
                keep.Add(tri[t]); keep.Add(tri[t + 1]); keep.Add(tri[t + 2]);
            }
            acc.AddRaw(P, Nn, U, Cc, keep);
        }

        static void FaceOut(RaceColorAcc acc, Vector3 a, Vector3 b, Vector3 c, Vector3 refP, Color32 col)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-8f) return;
            n.Normalize();
            if (Vector3.Dot(n, (a + b + c) / 3f - refP) < 0f) n = -n;
            acc.Tri(a, b, c, n, col);
        }

        static void BuildBackdrop(Transform parent, RaceTerrain terrain, int seed, List<Object> owned, Result res)
        {
            var rnd = new System.Random(seed ^ 0x0A0B0C0D);
            var acc = new RaceColorAcc();
            const float BaseY = -10f;
            const int S = 8;
            // 3 層:前排 20 座山塊(半徑 600~680)、中排 14 座(780~880,較高、往地平線色靠 = 空氣透視)、後排 12 條長稜線(980~1100,最淡);島的半邊長 432 m,所以山在海的對岸
            int slice;
            switch (look.Kind)
            {
                case LookKind.Snow: slice = RaceTexArray.MtnSnow; break;
                case LookKind.Desert: slice = RaceTexArray.MtnMesa; break;
                case LookKind.Volcano: slice = RaceTexArray.MtnVolcano; break;
                case LookKind.Forest: slice = RaceTexArray.MtnForest; break;
                default: slice = RaceTexArray.MtnStrata; break;
            }
            Color hz = theme.Horizon;
            for (int layer = 0; layer < 3; layer++)
            {
                int cnt = layer == 0 ? 17 : (layer == 1 ? 12 : 10);
                float fade = layer == 0 ? 0.0f : (layer == 1 ? 0.28f : 0.5f);
                Color tc = Color.Lerp(Color.white, Color.Lerp(hz, Color.white, 0.35f), fade);
                if (look.Kind == LookKind.Meadow || look.Kind == LookKind.Coast) tc = Color.Lerp(tc, Color.Lerp(theme.MtnRock, Color.white, 0.6f), 0.18f);
                Color32 tint = tc;
                for (int k = 0; k < cnt; k++)
                {
                    float ang = (k + (float)rnd.NextDouble() * 0.8f) * Mathf.PI * 2f / cnt + layer * 0.17f;
                    float rad = layer == 0 ? 600f + (float)rnd.NextDouble() * 80f : (layer == 1 ? 780f + (float)rnd.NextDouble() * 100f : 980f + (float)rnd.NextDouble() * 120f);
                    float H = layer == 0 ? 90f + (float)rnd.NextDouble() * 70f : (layer == 1 ? 160f + (float)rnd.NextDouble() * 90f : 110f + (float)rnd.NextDouble() * 90f);
                    float R = H * (1.0f + (float)rnd.NextDouble() * 0.5f);
                    PeakShape shape = look.Peaks;
                    if (shape == PeakShape.Volcano && k % 6 != 0) shape = PeakShape.Mesa;          // 火山地貌:偶爾一座火山,其餘是焦岩方山
                    else if (shape == PeakShape.Mesa && k % 4 == 3) shape = PeakShape.Alpine;       // 沙漠:偶爾一座尖岩
                    else if (shape == PeakShape.Round && k % 5 == 4) shape = PeakShape.Alpine;      // 森林:偶爾一座尖頂
                    float vMax = shape == PeakShape.Round ? 0.58f : 1f;
                    if (shape == PeakShape.Round) { H *= 0.75f; R *= 1.15f; }
                    float ax = 1f, az = 1f;
                    if (layer == 2) { ax = 2.3f; az = 0.8f; }
                    else if (k % 3 == 0) { ax = 1.5f; az = 0.85f; }
                    float yaw = ang * Mathf.Rad2Deg + 90f + (float)rnd.NextDouble() * 40f - 20f;
                    int segs = layer == 0 ? 12 : (layer == 1 ? 10 : 8);
                    float ridge = look.Kind == LookKind.Snow || shape == PeakShape.Alpine ? 1.15f : 0.8f;
                    Vector3 c = new Vector3(Mathf.Cos(ang) * rad, BaseY, Mathf.Sin(ang) * rad);
                    AddMassif(acc, rnd, c, H, R, ax, az, yaw, shape, slice, vMax, H, tint, segs, ridge);
                    // 副峰 / 肩部:1~2 座較矮的山塊貼在主峰旁(layer 2 不加),讓天際線不是一座座孤立的錐
                    int extra = layer == 2 ? 0 : (rnd.NextDouble() < 0.7 ? (rnd.NextDouble() < 0.4 ? 2 : 1) : 0);
                    for (int e = 0; e < extra; e++)
                    {
                        float ea = (float)rnd.NextDouble() * 6.28f, ed = R * (0.55f + (float)rnd.NextDouble() * 0.45f);
                        float eh = H * (0.5f + (float)rnd.NextDouble() * 0.3f), er = R * (0.55f + (float)rnd.NextDouble() * 0.3f);
                        PeakShape es = shape == PeakShape.Mesa ? PeakShape.Mesa : (e == 0 ? PeakShape.Round : PeakShape.Alpine);
                        float evm = es == PeakShape.Round ? Mathf.Min(vMax, 0.62f) : vMax;
                        AddMassif(acc, rnd, c + new Vector3(Mathf.Cos(ea) * ed, 0f, Mathf.Sin(ea) * ed), eh, er, 1f, 1f, 0f, es, slice, evm, H, tint, Mathf.Max(7, segs - 3), ridge);
                    }
                    res.Mountains++;
                }
            }

            // 小島(島外的海面上;圓丘 + 沙邊 + 幾叢樹),位置跟地形比對,絕不壓到海岸
            var white = new Color32(255, 255, 255, 255);
            int made = 0;
            for (int k = 0; k < look.Islands; k++)
            {
                float ang = (k + (float)rnd.NextDouble() * 0.7f) * Mathf.PI * 2f / Mathf.Max(1, look.Islands);
                float r = 24f + (float)rnd.NextDouble() * 38f, rad = 500f + (float)rnd.NextDouble() * 110f;
                float cr1 = (float)rnd.NextDouble(), cr2 = (float)rnd.NextDouble();
                int tries = 0;
                bool ok = false;
                float x = 0f, z = 0f;
                while (tries++ < 4 && !ok)
                {
                    x = Mathf.Cos(ang) * rad; z = Mathf.Sin(ang) * rad;
                    ok = true;
                    for (int q = 0; q < 8 && ok; q++)
                    {
                        float a = q * Mathf.PI * 0.25f;
                        if (terrain.Raw(x + Mathf.Cos(a) * r * 1.3f, z + Mathf.Sin(a) * r * 1.3f) > -0.8f) ok = false;
                    }
                    if (!ok) rad += 45f;
                }
                if (!ok) continue;
                var b = new RaceProtoBuilder();
                Color sand = look.IslandBase, grass = look.IslandTop;
                b.Blob(new Vector3(0f, -r * 0.07f, 0f), new Vector3(r, r * 0.34f, r), 1, sand, Color.Lerp(sand, grass, 0.12f), grass, cr1 * 90f);
                Color leaf = look.Kind == LookKind.Volcano ? Lava : (look.Kind == LookKind.Snow ? Color.white : theme.Leaves[0]);
                int nt = 1 + (int)(cr2 * 3f);
                for (int t = 0; t < nt; t++)
                {
                    float a = cr1 * 6.28f + t * 2.3f, d = r * (0.1f + 0.28f * t / 3f);
                    float ts = r * (0.16f + 0.05f * t);
                    b.Blob(new Vector3(Mathf.Cos(a) * d, r * 0.28f + ts * 0.5f, Mathf.Sin(a) * d), new Vector3(ts, ts * 0.9f, ts), 0, Shade(leaf, 0.3f), Sunny(leaf, 0.15f));
                }
                acc.Add(b.Build(), new Vector3(x, 0f, z), 0f, Vector3.one, white);
                made++;
            }
            res.Islands = made;

            int tris = 0, draws = 0;
            acc.Emit(parent, "Backdrop", owned, null, ref tris, ref draws);
            res.FixedTris += tris; res.FixedDraws += draws; res.Tris += tris;
        }

        // ------------------------------------------------------------------ 雲

        static void BuildClouds(Transform parent, int seed, List<Object> owned, Result res)
        {
            var rnd = new System.Random(seed ^ 0x0C10D5);
            var acc = new RaceColorAcc();
            Color top = Color.Lerp(Color.white, theme.Horizon, look.Kind == LookKind.Volcano ? 0.45f : 0.08f);
            Color bottom = Color.Lerp(top, new Color(0.62f, 0.7f, 0.86f), look.Kind == LookKind.Volcano ? 0.35f : 0.28f);
            var white = new Color32(255, 255, 255, 255);
            int n = Mathf.Max(3, look.CloudCount);
            for (int k = 0; k < n; k++)
            {
                var cr = new System.Random(seed * 31 + k);
                var b = new RaceProtoBuilder();
                int puffs = 3 + cr.Next(4);
                for (int i = 0; i < puffs; i++)
                {
                    float sz = 10f + (float)cr.NextDouble() * 10f;
                    Vector3 o = new Vector3(i * 8f - puffs * 4f, (float)cr.NextDouble() * 3f, (float)cr.NextDouble() * 6f - 3f);
                    b.Blob(o, new Vector3(sz * 0.5f, sz * 0.3f, sz * 0.5f), 1, bottom, top, (float)cr.NextDouble() * 90f);
                }
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, rad = 80f + (float)rnd.NextDouble() * 520f;
                Vector3 pos = new Vector3(Mathf.Cos(ang) * rad, 190f + (float)rnd.NextDouble() * 80f, Mathf.Sin(ang) * rad);
                float sc = 3.0f + (float)rnd.NextDouble() * 2.2f, yaw = (float)rnd.NextDouble() * 360f;
                acc.Add(b.Build(), pos, yaw, new Vector3(sc, sc, sc), white);
                res.Clouds++;
            }
            int tris = 0, draws = 0;
            acc.Emit(parent, "Clouds", owned, null, ref tris, ref draws);
            res.FixedTris += tris; res.FixedDraws += draws; res.Tris += tris;
        }
    }
}
