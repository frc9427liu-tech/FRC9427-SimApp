using UnityEngine;

namespace FrcSim
{
    // 場館環境(純外觀,無碰撞、不投影):看台、後牆與橫幅、天花板燈、延伸的地面。
    // 參考 moSim 宣傳片的「體育館」場景,讓場地周圍不再是一片黑。座標同 FieldBuilder:x = 場地長邊,z = 場地寬邊(WPILib y)。
    public static class ArenaBuilder
    {
        static Transform root;

        static GameObject Box(string name, Vector3 center, Vector3 size, Color c, bool emissive = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(root, false);
            g.transform.position = center;
            g.transform.localScale = size;
            Object.Destroy(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>();
            var m = FieldBuilder.MakeMat(c);
            if (emissive) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.6f); }
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = !emissive;
            return g;
        }

        public static void Build()
        {
            if (root != null) return;
            root = new GameObject("Arena").transform;
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            var seat = new Color(0.12f, 0.22f, 0.62f);
            var riser = new Color(0.55f, 0.56f, 0.60f);
            var concrete = new Color(0.30f, 0.31f, 0.34f);

            // 外圍大地板(場地地毯之外的場館地面)
            Box("ArenaFloor", new Vector3(L / 2f, -0.12f, W / 2f), new Vector3(L + 60f, 0.2f, W + 60f), new Color(0.04f, 0.045f, 0.055f));

            // 兩側看台:離護欄 3.5m 起,10 階往外升高
            foreach (int side in new[] { -1, 1 })
            {
                float edge = side < 0 ? 0f : W;
                for (int i = 0; i < 10; i++)
                {
                    float depth = 0.85f, h = 0.42f * (i + 1);
                    float z = edge + side * (3.5f + i * depth + depth / 2f);
                    Box($"StandRiser{side}_{i}", new Vector3(L / 2f, h / 2f, z), new Vector3(L + 12f, h, depth), riser);
                    Box($"StandSeat{side}_{i}", new Vector3(L / 2f, h + 0.03f, z - side * 0.1f), new Vector3(L + 12f, 0.06f, depth * 0.45f), seat);
                }
                // 看台後牆
                float backZ = edge + side * (3.5f + 10 * 0.85f + 0.3f);
                Box($"StandBack{side}", new Vector3(L / 2f, 4.5f, backZ), new Vector3(L + 14f, 9f, 0.5f), concrete);
            }

            // 兩端後牆(聯盟站後面)+ 聯盟色橫幅
            foreach (int end in new[] { -1, 1 })
            {
                float x = end < 0 ? -5.5f : L + 5.5f;
                Box($"EndWall{end}", new Vector3(x, 4.5f, W / 2f), new Vector3(0.5f, 9f, W + 14f), concrete);
                Box($"Banner{end}", new Vector3(x - end * 0.3f, 3.6f, W / 2f), new Vector3(0.1f, 1.6f, W * 0.7f), end < 0 ? new Color(0.10f, 0.22f, 0.85f) : new Color(0.85f, 0.15f, 0.15f));
            }

            // 天花板與燈(高 18m,高於所有相機;只有自發光的長條燈,不投影)
            Box("Ceiling", new Vector3(L / 2f, 18.5f, W / 2f), new Vector3(L + 40f, 0.5f, W + 40f), new Color(0.10f, 0.11f, 0.13f));
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 3; j++)
                    Box($"Light{i}_{j}", new Vector3(2f + i * ((L - 4f) / 5f), 18.1f, 1.2f + j * (W - 2.4f) / 2f), new Vector3(3.5f, 0.15f, 0.5f), new Color(1f, 0.97f, 0.9f), true);
        }

        public static void Clear()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
