using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrcSim
{
    /// 道具箱(模組 I / M4):旋轉發光方塊,被撞到後消失、3 秒後重生。
    /// 只做視覺與計時;誰撞到、抽什麼由 RaceItems 判定(RaceItems.Tick 內以距離判斷,可 headless 測試)。
    /// 材質一律走 RaceArt.Glow / RaceArt.Mat(快取);沒有碰撞體。
    public sealed class ItemBox : MonoBehaviour
    {
        public const float RespawnSeconds = 3f;
        public const float HitRadius = 1.7f;      // 水平命中半徑(車心到箱心)
        public const float HitHeight = 2.4f;      // 垂直容許差
        public const float Size = 0.8f;
        public const float Spacing = 80f;         // 沿賽道每排間距(約)
        public const int PerRow = 3;

        public bool Active { get; private set; }
        public float RespawnLeft { get; private set; }
        public int Row { get; private set; }
        public bool IsFake { get; private set; }

        Transform cube, inner;
        float phase;
        int hueStep = -1;
        Renderer cubeRenderer;

        static Mesh cubeMesh;
        const int HueSteps = 12;
        static Material[] hueMats;

        /// 在 pos(世界座標,箱子中心)建立一個箱子。
        public static ItemBox Create(Transform parent, Vector3 pos, int row, int index)
        {
            var go = new GameObject("ItemBox_" + row + "_" + index);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var b = go.AddComponent<ItemBox>();
            b.Row = row;
            b.phase = (row * 0.37f + index * 0.21f) * Mathf.PI;
            b.Build();
            b.Active = true;
            return b;
        }

        /// 假道具箱:外觀幾乎和真的一樣(只有內層方塊略帶紅),不計入 RaceItems.Boxes;命中判定由 ItemProjectiles 負責。
        public static ItemBox CreateFake(Transform parent, Vector3 pos)
        {
            var go = new GameObject("FakeItemBox");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var b = go.AddComponent<ItemBox>();
            b.IsFake = true;
            b.phase = (pos.x + pos.z) * 0.13f;
            b.Build();
            b.Active = true;
            return b;
        }

        /// 沿賽道擺一圈:每約 Spacing m 一排、每排 PerRow 個,橫向均分在路中間。
        public static List<ItemBox> BuildAlongTrack(RaceTrack track, Transform parent)
        {
            var list = new List<ItemBox>();
            if (track == null || track.Length < 100f) return list;
            int rows = Mathf.Max(3, Mathf.RoundToInt(track.Length / Spacing));
            float step = track.Length / rows;
            float s0 = Mathf.Min(40f, step * 0.5f);   // 起跑格後面一點,避免一開始就壓到
            for (int r = 0; r < rows; r++)
            {
                float s = track.Wrap(s0 + r * step);
                TrackSample sm = track.SampleAt(s);
                float gap = Mathf.Min(3.2f, sm.Width * 0.22f);
                for (int i = 0; i < PerRow; i++)
                {
                    float lat = (i - (PerRow - 1) * 0.5f) * gap;
                    Vector3 p = sm.Pos + sm.Right * lat;
                    float gy = p.y;
                    if (RaceKart.Ground != null && RaceKart.Ground(p, out GroundHit gh)) gy = gh.Height;
                    p.y = gy + 1.0f;
                    list.Add(Create(parent, p, r, i));
                }
            }
            return list;
        }

        void Build()
        {
            if (cubeMesh == null) cubeMesh = MakeCube();
            var c = new GameObject("Cube");
            c.transform.SetParent(transform, false);
            c.transform.localScale = Vector3.one * Size;
            c.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
            cubeRenderer = c.AddComponent<MeshRenderer>();
            cubeRenderer.sharedMaterial = HueMat(0);
            cube = c.transform;

            // MK 式:方塊裡漂浮一個永遠面向鏡頭的「?」(像素字,零字型依賴)
            if (qMesh == null) qMesh = MakeQuestionMesh();
            var n = new GameObject("Inner");
            n.transform.SetParent(transform, false);   // 不跟方塊一起轉,才能面向鏡頭
            n.transform.localScale = Vector3.one * 0.5f;
            n.AddComponent<MeshFilter>().sharedMesh = qMesh;
            n.AddComponent<MeshRenderer>().sharedMaterial = IsFake ? RaceArt.Glow(new Color(1f, 0.7f, 0.7f), 1.3f) : RaceArt.Glow(Color.white, 1.3f);
            inner = n.transform;
        }

        static Mesh qMesh;
        static Mesh MakeQuestionMesh()
        {
            string[] rows = { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." };
            var ci = new List<CombineInstance>();
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < 5; x++)
                    if (rows[y][x] == '#')
                        ci.Add(new CombineInstance { mesh = cubeMesh, transform = Matrix4x4.TRS(new Vector3((x - 2f) * 0.2f, (3f - y) * 0.2f, 0f), Quaternion.identity, new Vector3(0.2f, 0.2f, 0.12f)) });
            var m = new Mesh { name = "ItemBoxQ" };
            m.CombineMeshes(ci.ToArray(), true, true);
            return m;
        }

        static Camera cam;

        static Mesh MakeCube()
        {
            // Unity 手冊的 8 頂點立方體(順時針 = 正面朝外),再用 Flat 拆成平面著色
            var v = new Vector3[]
            {
                new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0),
                new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,0,1), new Vector3(0,0,1)
            };
            for (int i = 0; i < v.Length; i++) v[i] -= new Vector3(0.5f, 0.5f, 0.5f);
            int[] t =
            {
                0,2,1, 0,3,2,  2,3,4, 2,4,5,  1,2,5, 1,5,6,
                0,7,4, 0,4,3,  5,4,7, 5,7,6,  0,6,7, 0,1,6
            };
            var m = new Mesh { name = "ItemBoxCube", vertices = v, triangles = t };
            m.RecalculateNormals();
            var f = RaceArt.Flat(m);
            Object.Destroy(m);
            return f;
        }

        static Material HueMat(int step)
        {
            if (hueMats == null) hueMats = new Material[HueSteps];
            step = ((step % HueSteps) + HueSteps) % HueSteps;
            if (hueMats[step] == null)
                hueMats[step] = RaceArt.Glow(Color.HSVToRGB(step / (float)HueSteps, 0.65f, 1f), 1.6f);
            return hueMats[step];
        }

        void Update()
        {
            if (!Active || cube == null) return;
            float t = Time.time;
            cube.localRotation = Quaternion.Euler(20f, t * 90f + phase * 57.3f, 20f);
            if (cam == null) cam = Camera.main;
            if (cam != null) inner.rotation = Quaternion.LookRotation(inner.position - cam.transform.position, Vector3.up);   // 面向鏡頭(?)的正面朝 -z
            inner.localPosition = new Vector3(0f, 0.12f * Mathf.Sin(t * 2.6f + phase), 0f);
            Vector3 lp = cube.localPosition;
            lp.y = 0.15f * Mathf.Sin(t * 2.2f + phase);
            cube.localPosition = lp;
            int hs = Mathf.FloorToInt(Mathf.Repeat(t * 0.35f + phase * 0.1f, 1f) * HueSteps);
            if (hs != hueStep)
            {
                hueStep = hs;
                if (cubeRenderer != null) cubeRenderer.sharedMaterial = HueMat(hs);
            }
        }

        /// 被撞破:消失,RespawnSeconds 後由 Step 重生。
        public void Break()
        {
            Active = false;
            RespawnLeft = RespawnSeconds;
            if (cube != null) cube.gameObject.SetActive(false);
            if (inner != null) inner.gameObject.SetActive(false);
        }

        /// 推進重生計時;這一步剛重生回傳 true。
        public bool Step(float dt)
        {
            if (Active) return false;
            RespawnLeft -= dt;
            if (RespawnLeft > 0f) return false;
            RespawnLeft = 0f;
            Active = true;
            if (cube != null) cube.gameObject.SetActive(true);
            if (inner != null) inner.gameObject.SetActive(true);
            return true;
        }

        /// 車 (kartPos) 是否碰到這個(啟用中的)箱子。
        public bool Touches(Vector3 kartPos)
        {
            if (!Active) return false;
            Vector3 d = kartPos - transform.position;
            if (Mathf.Abs(d.y) > HitHeight) return false;
            return d.x * d.x + d.z * d.z <= HitRadius * HitRadius;
        }
    }
}
