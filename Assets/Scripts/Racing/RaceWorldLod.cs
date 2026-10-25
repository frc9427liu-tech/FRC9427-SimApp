using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// 模組 W / M2:每 0.25 秒依攝影機位置切換地形區塊 LOD、關掉太遠的物件組(見 02 §3 LOD / 裁切策略)。
    /// 碰撞永遠用 LOD0 那份(MeshCollider 的網格不跟著切換)。不配置記憶體(只用預先建好的陣列與 List)。
    /// 沒有攝影機時維持目前狀態(剛建好時 = 起跑格附近用 LOD0,其餘 LOD1)。
    public sealed class RaceWorldLod : MonoBehaviour
    {
        public const float Lod0Dist = 200f;    // 離區塊邊界 < 200 m 用 LOD0(M5:區塊從 64 m 變 144 m)
        public const float Hysteresis = 16f;   // 切換遲滯,避免在邊界上閃
        public const float CullDist = 260f;    // 物件組離攝影機(到組的包圍盒)超過這個距離整組不畫
        public const float Interval = 0.25f;

        /// 一塊地形。
        internal sealed class Chunk
        {
            public MeshFilter Filter;
            public Mesh Lod0, Lod1;
            public Bounds Bounds;
            public int Tris0, Tris1;
            public int Current;    // 0 或 1
        }

        /// 一組物件(樹 / 石頭;每個材質一個 Renderer)。
        internal sealed class Cell
        {
            public Renderer[] Renderers;
            public Bounds Bounds;
            public int Tris;
            public bool On = true;
            public float Cull = CullDist;   // 這組的裁切距離(路邊草簇用比較短的)
            public float MinDist = -1f;     // > 0:離攝影機小於這個距離時不畫(遠處低面數樹與近處高面數樹互補)
        }

        internal readonly List<Chunk> chunks = new List<Chunk>();
        internal readonly List<Cell> cells = new List<Cell>();
        internal int fixedTris;        // 不會被 LOD / 裁切影響的三角形(路面、山、雲、水)
        internal int fixedDraws;       // 對應的 draw call 數
        public RaceQuality Quality = RaceQuality.High;

        float timer;
        Camera cam;
        readonly Camera[] camBuf = new Camera[8];

        public static RaceWorldLod Instance { get; private set; }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = Interval;
            Camera c = FindCamera();
            if (c != null) Apply(c.transform.position);
        }

        Camera FindCamera()
        {
            if (cam != null && cam.isActiveAndEnabled) return cam;
            cam = Camera.main;
            if (cam != null) return cam;
            int n = Camera.GetAllCameras(camBuf);
            for (int i = 0; i < n; i++)
                if (camBuf[i] != null && camBuf[i].isActiveAndEnabled && camBuf[i].targetTexture == null) { cam = camBuf[i]; break; }
            return cam;
        }

        /// 以 pos 為視點更新全部區塊與物件組。
        public void Apply(Vector3 pos)
        {
            pos = transform.InverseTransformPoint(pos);   // 區塊與物件組的包圍盒都在世界根物件的本地座標
            bool low = Quality == RaceQuality.Low;
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk c = chunks[i];
                float d = Mathf.Sqrt(c.Bounds.SqrDistance(pos));
                int want = low ? 1 : (c.Current == 0 ? (d < Lod0Dist + Hysteresis ? 0 : 1) : (d < Lod0Dist ? 0 : 1));
                if (want != c.Current)
                {
                    c.Current = want;
                    c.Filter.sharedMesh = want == 0 ? c.Lod0 : c.Lod1;
                }
            }
            for (int i = 0; i < cells.Count; i++)
            {
                Cell g = cells[i];
                float d = Mathf.Sqrt(g.Bounds.SqrDistance(pos));
                bool want = g.On ? (d < g.Cull + Hysteresis && d > g.MinDist - Hysteresis * 0.5f) : (d < g.Cull && d >= g.MinDist);
                if (want != g.On)
                {
                    g.On = want;
                    for (int r = 0; r < g.Renderers.Length; r++) if (g.Renderers[r] != null) g.Renderers[r].enabled = want;
                }
            }
        }

        /// 估算視點在 pos 時一幀要畫的三角形與 draw call(不做視錐裁切 = 偏保守的最壞情況)。給預算測試用。
        public void Estimate(Vector3 pos, out int tris, out int draws)
        {
            pos = transform.InverseTransformPoint(pos);
            tris = fixedTris; draws = fixedDraws;
            bool low = Quality == RaceQuality.Low;
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk c = chunks[i];
                float d = Mathf.Sqrt(c.Bounds.SqrDistance(pos));
                tris += (!low && d < Lod0Dist) ? c.Tris0 : c.Tris1;
                draws++;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                Cell g = cells[i];
                float dd = Mathf.Sqrt(g.Bounds.SqrDistance(pos));
                if (dd < g.Cull && dd >= g.MinDist) { tris += g.Tris; draws += g.Renderers.Length; }
            }
        }

        /// 全部 LOD 都算上的總三角形數(記憶體用,不是一幀要畫的量)。
        public int TotalMeshTris()
        {
            int t = fixedTris;
            for (int i = 0; i < chunks.Count; i++) t += chunks[i].Tris0 + chunks[i].Tris1;
            for (int i = 0; i < cells.Count; i++) t += cells[i].Tris;
            return t;
        }

        public int ChunkCount { get { return chunks.Count; } }
        public int CellCount { get { return cells.Count; } }
    }
}
