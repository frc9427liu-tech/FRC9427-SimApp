using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 一個收集物(金幣)。Pos 是硬幣中心(地面上方約 1.3 m)。
    public sealed class Collectible
    {
        public int Id;
        public Vector3 Pos;
        public bool Taken;
    }

    /// 收集物位置的挑選(純資料 + 數學,不用 Unity 物件,-batchmode 與單元測試都能直接跑;同一個種子結果相同)。
    internal static class CollectiblePlacer
    {
        public const int Total = 30;

        public static List<Vector3> Pick(RaceTrack tr, RaceTerrain terr, int seed, IList<Vector3> avoid)
        {
            // 1) 候選點:陸地、不太陡、不在水邊 / 沙灘邊緣、不在地標旁
            var rnd = new System.Random(seed ^ 0x0C017E57);
            float lim = RaceTerrain.Half - 45f;
            var cand = new List<Vector3>();
            var cdist = new List<float>();      // 離主線路緣的距離
            for (int tries = 0; tries < 9000 && cand.Count < 1800; tries++)
            {
                float x = (float)(rnd.NextDouble() * 2.0 * lim - lim), z = (float)(rnd.NextDouble() * 2.0 * lim - lim);
                if (!Ok(terr, x, z)) continue;
                bool nearLm = false;
                if (avoid != null) for (int i = 0; i < avoid.Count; i++) { float ex = avoid[i].x - x, ez = avoid[i].z - z; if (ex * ex + ez * ez < 14f * 14f) { nearLm = true; break; } }
                if (nearLm) continue;
                float lat; float s = tr.Project(new Vector3(x, 0f, z), -1f, out lat);
                cand.Add(new Vector3(x, 0f, z));
                cdist.Add(Mathf.Abs(lat) - tr.HalfWidthAt(s));
            }

            // 2) 最遠點取樣:前 8 個只從離賽道 30 m 以內挑(新手撿得到),其餘整座島
            var picked = new List<Vector3>();
            var minD = new float[cand.Count];
            for (int i = 0; i < minD.Length; i++) minD[i] = float.MaxValue;
            for (int n = 0; n < Total && cand.Count > 0; n++)
            {
                int best = -1; float bd = -1f;
                for (int pass = 0; pass < 2 && best < 0; pass++)
                {
                    bool nearOnly = n < 8 && pass == 0;
                    for (int i = 0; i < cand.Count; i++)
                    {
                        if (minD[i] < 0f) continue;                    // 已經選過
                        if (nearOnly && cdist[i] > 30f) continue;
                        float d = picked.Count == 0 ? 1f : minD[i];     // 第一個:第一個合格的
                        if (d > bd) { bd = d; best = i; }
                    }
                }
                if (best < 0) break;
                Vector3 p = cand[best];
                picked.Add(p);
                for (int i = 0; i < cand.Count; i++)
                {
                    float ex = cand[i].x - p.x, ez = cand[i].z - p.z;
                    float d = Mathf.Sqrt(ex * ex + ez * ez);
                    if (d < minD[i]) minD[i] = d;
                }
                minD[best] = -1f;   // 不會再被選
            }

            return picked;
        }

        static bool Ok(RaceTerrain t, float x, float z)
        {
            float raw = t.Raw(x, z);
            if (raw < 1.2f || raw > 24f) return false;
            Surface sf = t.SurfaceAt(x, z);
            if (sf != Surface.Grass && sf != Surface.Dirt && sf != Surface.Sand) return false;
            if (t.ScEdgeAt(x, z) < 1f) return false;
            if (t.Normal(x, z).y < 0.88f) return false;
            // 周圍 6 m 都是陸地(不貼著海)
            const float d = 6f;
            if (t.Raw(x + d, z) < 0.3f || t.Raw(x - d, z) < 0.3f || t.Raw(x, z + d) < 0.3f || t.Raw(x, z - d) < 0.3f) return false;
            return true;
        }

    }

    /// 自由探索模式的收集物(模組 W,M5;RaceConfig.FreeRoam = true 時 RaceWorld.Build 才會生成,正常比賽沒有)。
    /// 30 個金色旋轉硬幣散佈在整座島上(最遠點取樣,同一個種子結果相同),不使用 Collider,撿取用水平距離判定(RaceWorld.TryCollect)。
    /// 撿到的音效由 04 負責:訂閱 RaceWorld.CoinCollected。
    public sealed class CollectibleSet : MonoBehaviour
    {
        public const int Total = CollectiblePlacer.Total;
        public const float PickupRadius = 3.2f;       // 水平距離(m)
        public const float PickupHeight = 3.5f;       // 垂直容許(m)

        public readonly List<Collectible> Items = new List<Collectible>();
        public int Collected { get; private set; }
        public event Action<int, Vector3> Picked;     // (索引, 位置)

        GameObject[] coins;
        float t;

        internal static CollectibleSet Build(RaceTrack tr, RaceTerrain terr, Transform parent, int seed, List<UnityEngine.Object> owned, RaceWorldLod lod, IList<Vector3> avoid)
        {
            var go = new GameObject("Collectibles");
            go.transform.SetParent(parent, false);
            var set = go.AddComponent<CollectibleSet>();

            var picked = CollectiblePlacer.Pick(tr, terr, seed, avoid);

            // 3) 實體
            Mesh coin = MakeCoin();
            owned.Add(coin);
            Material mat = RaceArt.Mat(RacePalette.Gold, 0.55f);
            set.coins = new GameObject[picked.Count];
            for (int i = 0; i < picked.Count; i++)
            {
                Vector3 p = picked[i];
                p.y = terr.Height(p.x, p.z) + 1.3f;
                set.Items.Add(new Collectible { Id = i, Pos = p });
                var c = new GameObject("Coin" + i);
                c.transform.SetParent(go.transform, false);
                c.transform.position = p;
                c.AddComponent<MeshFilter>().sharedMesh = coin;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                set.coins[i] = c;
            }
            lod.fixedDraws += set.coins.Length;       // 最壞情況全部都在畫面內(各自一個 draw call)
            lod.fixedTris += set.coins.Length * (coin.triangles.Length / 3);
            return set;
        }

        /// 直立的 10 邊形硬幣(半徑 0.9、厚 0.2;法線沿本地 z)。
        static Mesh MakeCoin()
        {
            var acc = new RaceMeshAcc();
            const int S = 10; const float R = 0.9f, Hh = 0.1f;
            Vector3 front = new Vector3(0f, 0f, Hh), back = new Vector3(0f, 0f, -Hh);
            for (int i = 0; i < S; i++)
            {
                float a0 = i * Mathf.PI * 2f / S, a1 = (i + 1) * Mathf.PI * 2f / S;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, 0f), p1 = new Vector3(Mathf.Cos(a1) * R, Mathf.Sin(a1) * R, 0f);
                acc.Tri(front, p0 + front, p1 + front, Vector3.forward);
                acc.Tri(back, p0 + back, p1 + back, Vector3.back);
                float am = (a0 + a1) * 0.5f;
                acc.Quad(p0 + front, p1 + front, p1 + back, p0 + back, new Vector3(Mathf.Cos(am), Mathf.Sin(am), 0f));
                // 中間的凹痕(內圈較淺的環,讓硬幣有層次)
                Vector3 q0 = p0 * 0.62f, q1 = p1 * 0.62f;
                acc.Quad(q0 + front * 1.25f, q1 + front * 1.25f, p1 * 0.7f + front, p0 * 0.7f + front, Vector3.forward);
            }
            Mesh m = acc.ToMesh("RaceCoin");
            return m;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (coins == null) return;
            float yaw = t * 180f;
            for (int i = 0; i < coins.Length; i++)
            {
                if (Items[i].Taken) continue;
                Transform tr = coins[i].transform;
                tr.rotation = Quaternion.Euler(0f, yaw + i * 37f, 0f);
                Vector3 p = Items[i].Pos; p.y += Mathf.Sin(t * 2.2f + i) * 0.18f;
                tr.position = p;
            }
        }

        /// 撿取判定:水平距離 <= PickupRadius 而且高度差 <= PickupHeight 的收集物全部撿起。回傳這次新撿到幾個。不配置記憶體(沒撿到時)。
        public int TryCollect(Vector3 pos, float radius = PickupRadius)
        {
            int got = 0;
            float r2 = radius * radius;
            for (int i = 0; i < Items.Count; i++)
            {
                Collectible c = Items[i];
                if (c.Taken) continue;
                float dx = c.Pos.x - pos.x, dz = c.Pos.z - pos.z;
                if (dx * dx + dz * dz > r2 || Mathf.Abs(c.Pos.y - pos.y) > PickupHeight) continue;
                c.Taken = true; Collected++; got++;
                if (coins != null && i < coins.Length && coins[i] != null) coins[i].SetActive(false);
                if (Picked != null) Picked(i, c.Pos);
            }
            return got;
        }

        /// 全部還原(重玩自由探索時用)。
        public void ResetAll()
        {
            Collected = 0;
            for (int i = 0; i < Items.Count; i++) { Items[i].Taken = false; if (coins != null && coins[i] != null) coins[i].SetActive(true); }
        }
    }
}
