using UnityEngine;

namespace FrcSim
{
    // 用程式碼蓋出場地(先用方塊代替,尺寸取自官方手冊),不依賴任何外部模型。
    public static class FieldBuilder
    {
        static readonly Color Blue = new Color(0.15f, 0.35f, 0.9f);
        static readonly Color Red = new Color(0.9f, 0.2f, 0.2f);
        static readonly Color Floor = new Color(0.22f, 0.24f, 0.27f);
        static readonly Color Gray = new Color(0.55f, 0.57f, 0.6f);

        public static Material MakeMat(Color c)
        {
            var sh = Shader.Find("Standard");
            var m = new Material(sh);
            m.color = c;
            m.SetFloat("_Glossiness", 0.15f);
            return m;
        }

        // WPILib (x,y) -> Unity 位置
        public static Vector3 P(float x, float y, float z = 0f) => new Vector3(x, z, y);

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Color c, bool collider)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = MakeMat(c);
            if (!collider) Object.Destroy(g.GetComponent<Collider>());
            return g;
        }

        public const float BumpLength = 1.128f;   // 44.4in:官方模型 Bump Plastic 沿 x 的實際長度(x 4.06~5.19),峰高 0.165
        // BUMP 區域 (中心 x, 中心 y, 半長 x, 半寬 y)
        public static readonly System.Collections.Generic.List<Vector4> BumpRegions = new System.Collections.Generic.List<Vector4>();

        // 場地座標 (x,y) 處的 BUMP 高度(m);不在 BUMP 上為 0。雙斜坡:中心最高 BumpHeight,線性降到邊緣 0(約 15°)
        public static float BumpHeightAt(float x, float y)
        {
            foreach (var r in BumpRegions)
            {
                float dx = Mathf.Abs(x - r.x), dy = Mathf.Abs(y - r.y);
                if (dx <= r.z && dy <= r.w) return SimConstants.BumpHeight * (1f - dx / r.z);
            }
            return 0f;
        }

        // 山形稜柱(沿 x 兩側斜坡、中央稜線),底面中心在 center、底面高度 y=center.y
        static GameObject Prism(Transform parent, string name, Vector3 center, float lenX, float h, float widZ, Color c)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            float hx = lenX / 2f, hz = widZ / 2f;
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            Vector3 inside = new Vector3(0, h / 3f, 0);
            void Tri(Vector3 a, Vector3 b, Vector3 cc)
            {
                // 每個面獨立頂點(平面著色),繞向自動調整成「法線朝外」(Unity 左手座標:Cross(b-a, c-a) 朝外 = 順時針從外面看)
                Vector3 outward = (a + b + cc) / 3f - inside;
                if (Vector3.Dot(Vector3.Cross(b - a, cc - a), outward) < 0f) { var tmp = b; b = cc; cc = tmp; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(cc);
                t.AddRange(new[] { i, i + 1, i + 2 });
            }
            Vector3 A = new Vector3(-hx, 0, -hz), B = new Vector3(hx, 0, -hz), C = new Vector3(hx, 0, hz), D = new Vector3(-hx, 0, hz);
            Vector3 R1 = new Vector3(0, h, -hz), R2 = new Vector3(0, h, hz);
            Tri(A, D, R2); Tri(A, R2, R1);      // -x 側斜坡
            Tri(B, R1, R2); Tri(B, R2, C);      // +x 側斜坡
            Tri(A, B, R1); Tri(D, R2, C);       // 兩個端面
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = MakeMat(c);
            return g;
        }

        public static void Build()
        {
            BumpRegions.Clear();
            var root = new GameObject("Field").transform;
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;

            // 地板
            Box(root, "Floor", new Vector3(L / 2, -0.05f, W / 2), new Vector3(L + 2f, 0.1f, W + 2f), Floor, true);

            // 外牆
            float wh = 0.6f, wt = 0.1f;
            Box(root, "WallBottom", new Vector3(L / 2, wh / 2, -wt / 2), new Vector3(L + 2 * wt, wh, wt), Gray, true);
            Box(root, "WallTop", new Vector3(L / 2, wh / 2, W + wt / 2), new Vector3(L + 2 * wt, wh, wt), Gray, true);
            Box(root, "WallBlue", new Vector3(-wt / 2, wh / 2, W / 2), new Vector3(wt, wh, W), Blue, true);
            Box(root, "WallRed", new Vector3(L + wt / 2, wh / 2, W / 2), new Vector3(wt, wh, W), Red, true);

            // 聯盟區分界、中線(貼地線條)
            Line(root, "CenterLine", L / 2, Color.white);
            Line(root, "BlueZoneLine", SimConstants.AllianceZoneDepth, Blue);
            Line(root, "RedZoneLine", L - SimConstants.AllianceZoneDepth, Red);

            // 兩邊各一組物件
            Alliance(root, true);
            Alliance(root, false);
        }

        static void Line(Transform root, string name, float x, Color c)
        {
            Box(root, name, new Vector3(x, 0.005f, SimConstants.FieldWidth / 2),
                new Vector3(0.05f, 0.01f, SimConstants.FieldWidth), c, false);
        }

        static void Alliance(Transform root, bool blue)
        {
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            Color c = blue ? Blue : Red;
            float sgn = blue ? 1f : -1f;
            // 以聯盟牆為 0 的距離 d -> 場地 x
            System.Func<float, float> X = d => blue ? d : L - d;

            // HUB:離聯盟牆 158.6in(近緣),47in 方柱,位於場寬中央
            float hubCx = X(SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f * 0f + SimConstants.HubSize / 2f);
            // 近緣在 4.0284 -> 中心再往場內 0.597(模擬器先用中心 = 近緣 + 半寬)
            hubCx = X(SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f);
            // 中空方筒(上方開口),球從頂端落入;計分在 ScoreManager 判定
            {
                float hs = SimConstants.HubSize, t = 0.07f, hh = SimConstants.HubRimHeight;
                string nm = blue ? "BlueHub" : "RedHub";
                Box(root, nm + "_N", new Vector3(hubCx, hh / 2f, W / 2f + hs / 2f - t / 2f), new Vector3(hs, hh, t), c, true);
                Box(root, nm + "_S", new Vector3(hubCx, hh / 2f, W / 2f - hs / 2f + t / 2f), new Vector3(hs, hh, t), c, true);
                Box(root, nm + "_E", new Vector3(hubCx + hs / 2f - t / 2f, hh / 2f, W / 2f), new Vector3(t, hh, hs - 2 * t), c, true);
                Box(root, nm + "_W", new Vector3(hubCx - hs / 2f + t / 2f, hh / 2f, W / 2f), new Vector3(t, hh, hs - 2 * t), c, true);
                // 底板(球掉進去前的保險,不讓球卡在地板下)
                Box(root, nm + "_Base", new Vector3(hubCx, 0.15f, W / 2f), new Vector3(hs - 2 * t, 0.3f, hs - 2 * t), c, true);
                // 擋板(球網):在面向中立區那一側,從 0.4m(底下留縫)到 3.3m;從中立區射會被擋,射太遠的球撞網掉回中立區(官方 HUB 導覽影片)
                Box(root, nm + "_Net", new Vector3(hubCx + (blue ? 1f : -1f) * (hs / 2f + 0.05f), 1.85f, W / 2f), new Vector3(0.06f, 2.9f, hs + 0.5f), c, true);
            }

            // BUMP x2:HUB 兩側(場寬 y = W/2 ± (hub/2 + bump/2))。官方 GE-26100:雙斜坡(15°)山形剖面,
            // 沿場長總長 48.93in(1.243m)、峰高 6.51in(0.1654m)、寬 73in(1.854m);車高會隨斜坡抬升(SwerveDrive 用 BumpHeightAt)
            float bumpY = SimConstants.HubSize / 2f + SimConstants.BumpWidth / 2f;
            foreach (float s in new[] { -1f, 1f })
            {
                Prism(root, "Bump", new Vector3(hubCx, 0f, W / 2f + s * bumpY), BumpLength, SimConstants.BumpHeight, SimConstants.BumpWidth, new Color(0.09f, 0.10f, 0.12f));   // 官方 BUMP 是深色塑膠,和灰地毯才分得出來
                BumpRegions.Add(new Vector4(hubCx, W / 2f + s * bumpY, BumpLength / 2f, SimConstants.BumpWidth / 2f));
            }

            // TRENCH(依官方圖面 FE-2026 / GE-26200,單位 in→m):沿場邊的隧道,機器人沿場長方向(x)穿過。
            //   x 長 65.65in(1.668m,以 HUB 中心對齊,與機器人程式 FieldTagMap 的 HALF_WIDTH 0.834 一致);
            //   y:場牆起 50.35in(1.279m)是開口,其內側是 12.00in(0.305m)厚立柱,合計 62.35in(1.584m)(接著就是 73in 的 BUMP);
            //   開口淨高 22.25in(0.565m),整體高 40.25in(1.022m)=頂板厚 0.457m。
            {
                const float tl = 1.194f, open = 1.279f, post = 0.3048f, clear = 0.5652f, top = 1.0224f;
                foreach (bool south in new[] { true, false })
                {
                    System.Func<float, float> Y = y => south ? y : W - y;
                    Box(root, "TrenchPost", new Vector3(hubCx, top / 2f, Y(open + post / 2f)), new Vector3(tl, top, post), c, true);
                    Box(root, "TrenchTop", new Vector3(hubCx, clear + (top - clear) / 2f, Y((open + post) / 2f)), new Vector3(tl, top - clear, open + post), c, true);
                }
            }

            // TOWER:靠聯盟牆
            // 位置依官方 2026 AprilTag 場地配置(WPILib 2026-rebuilt-welded.json):藍方 TOWER 牆面 tag 31/32 的 y 平均 = 3.965 m,
            // 紅方 15/16 的 y 平均 = 4.105 m(場地旋轉對稱:y → W - y)
            // 依官方場地模型(FE-2026 CAD)重新量過:TOWER 是「開放式」框架——靠牆一面板、前面兩根細立柱(x 1.02~1.11 m,
            // y 3.30~3.34 與 4.16~4.19,高 1.83 m),橫桿(rung)離地 0.66 m 以上。機器人本體(保險桿高度)可以開進兩立柱之間,
            // 原本用一整塊實心方塊會把機器人擋在外面,也和畫面上看到的形狀對不上,所以只留兩根立柱當碰撞體。
            foreach (float zc in new[] { 3.32f, 4.175f })
                Box(root, (blue ? "BlueTowerPost" : "RedTowerPost"),
                    new Vector3(X(1.065f), 0.915f, blue ? zc : W - zc),
                    new Vector3(0.10f, 1.83f, 0.045f), c, true);

            // DEPOT:沿聯盟牆(貼地)
            // 位置依官方圖面 FE-2026 第 3 頁:藍方 DEPOT 中心離計分台側牆 234.85in(5.965m),紅方對稱(W - y)
            Box(root, "Depot", new Vector3(X(SimConstants.DepotDepth / 2f), 0.03f, blue ? 5.965f : W - 5.965f),
                new Vector3(SimConstants.DepotDepth, 0.06f, SimConstants.DepotWidth), c, false);

            // OUTPOST:貼聯盟牆,AprilTag 29/30(藍)y=26.22/43.22in → 中心 34.72in(0.882m);圖面寬 49.84in(1.266m)、深 28.13in(0.7145m)。
            // 高度圖面文字沒標,先用 1.0m 的方塊(有碰撞,機器人不能穿過);紅方對稱
            // 2026-10-10 更正:官方場地模型裡 OUTPOST 在聯盟牆「後面」(場外的人類球員區,場內只有牆上的出球口與膠帶),
            // 場內根本沒有東西。原本放的 1m 實心方塊是看不見的牆,機器人去接球(補球處)會被卡住,所以拿掉碰撞體,只留不可見參考。
            Box(root, blue ? "BlueOutpost" : "RedOutpost", new Vector3(X(0.7145f / 2f), 0.5f, blue ? 0.882f : W - 0.882f),
                new Vector3(0.7145f, 1.0f, 1.266f), c, false);
        }
    }
}
