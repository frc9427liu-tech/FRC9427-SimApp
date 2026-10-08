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

        public static void Build()
        {
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
            }

            // BUMP x2:HUB 兩側(場寬 y = W/2 ± (hub/2 + bump/2))
            float bumpY = SimConstants.HubSize / 2f + SimConstants.BumpWidth / 2f;
            foreach (float s in new[] { -1f, 1f })
            {
                Box(root, "Bump", new Vector3(hubCx, SimConstants.BumpHeight / 2f, W / 2f + s * bumpY),
                    new Vector3(SimConstants.BumpDepth, SimConstants.BumpHeight, SimConstants.BumpWidth), Gray, false);
            }

            // TRENCH:貼場邊,x 同 HUB 區,y 在場邊 1.194m 內(用細柱代表)
            foreach (float yEdge in new[] { 0.6f, W - 0.6f })
            {
                Box(root, "Trench", new Vector3(hubCx, 0.5f, yEdge),
                    new Vector3(0.15f, 1.0f, 1.19f), c, true);
            }

            // TOWER:靠聯盟牆
            // 位置依官方 2026 AprilTag 場地配置(WPILib 2026-rebuilt-welded.json):藍方 TOWER 牆面 tag 31/32 的 y 平均 = 3.965 m,
            // 紅方 15/16 的 y 平均 = 4.105 m(場地旋轉對稱:y → W - y)
            float towerY = blue ? 3.965f : W - 3.965f;
            Box(root, (blue ? "BlueTower" : "RedTower"),
                new Vector3(X(SimConstants.TowerDepth / 2f), SimConstants.TowerHeight / 2f, towerY),
                new Vector3(SimConstants.TowerDepth, SimConstants.TowerHeight, SimConstants.TowerWidth), c, true);

            // DEPOT:沿聯盟牆(貼地)
            Box(root, "Depot", new Vector3(X(SimConstants.DepotDepth / 2f), 0.03f, W / 2f + 2.2f * sgn),
                new Vector3(SimConstants.DepotDepth, 0.06f, SimConstants.DepotWidth), c, false);
        }
    }
}
