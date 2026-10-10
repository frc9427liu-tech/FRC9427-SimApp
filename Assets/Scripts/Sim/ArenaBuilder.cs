using UnityEngine;

namespace FrcSim
{
    // Arena: DARKER than the field on purpose (subject separation). Pure visuals, no colliders, no shadows cast.
    public static class ArenaBuilder
    {
        static Transform root;

        static GameObject Box(string name, Vector3 center, Vector3 size, Material m, bool receive = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(root, false);
            g.transform.position = center;
            g.transform.localScale = size;
            Object.Destroy(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = receive;
            return g;
        }

        public static void Build()
        {
            if (root != null) return;
            root = new GameObject("Arena").transform;
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;

            var floorMat  = Look.ArenaMaterial(new Color(0.12f, 0.125f, 0.15f), 1f, 1f, 1f);
            var riserMat  = Look.ArenaMaterial(new Color(0.30f, 0.31f, 0.34f), 5f, 0.6f, 1f);
            var wallMat   = Look.ArenaMaterial(new Color(0.20f, 0.21f, 0.25f), 9f, 0.55f, 1f);
            var standRib  = Look.ArenaMaterial(new Color(0.20f, 0.21f, 0.25f), 9f, 0.55f, 1f, new Color(0.35f, 0.55f, 1f), 6.5f, 2.2f);
            var endBlue   = Look.ArenaMaterial(new Color(0.20f, 0.21f, 0.25f), 9f, 0.55f, 1f, new Color(0.10f, 0.25f, 1f), 6.5f, 2.2f);
            var endRed    = Look.ArenaMaterial(new Color(0.20f, 0.21f, 0.25f), 9f, 0.55f, 1f, new Color(1f, 0.15f, 0.12f), 6.5f, 2.2f);
            var seatMat   = Look.CrowdMaterial(L + 12f);
            var ceilMat   = Look.ArenaMaterial(new Color(0.05f, 0.055f, 0.065f), 1f, 1f, 1f);
            var lightMat  = Look.GlowMaterial(new Color(1f, 0.97f, 0.9f), 3.5f);

            Box("ArenaFloor", new Vector3(L / 2f, -0.12f, W / 2f), new Vector3(L + 60f, 0.2f, W + 60f), floorMat);

            foreach (int side in new[] { -1, 1 })
            {
                float edge = side < 0 ? 0f : W;
                for (int i = 0; i < 10; i++)
                {
                    float depth = 0.85f, h = 0.42f * (i + 1);
                    float z = edge + side * (3.5f + i * depth + depth / 2f);
                    Box($"StandRiser{side}_{i}", new Vector3(L / 2f, h / 2f, z), new Vector3(L + 12f, h, depth), riserMat);
                    Box($"StandSeat{side}_{i}", new Vector3(L / 2f, h + 0.03f, z - side * 0.1f), new Vector3(L + 12f, 0.06f, depth * 0.45f), seatMat);
                }
                float backZ = edge + side * (3.5f + 10 * 0.85f + 0.3f);
                Box($"StandBack{side}", new Vector3(L / 2f, 4.5f, backZ), new Vector3(L + 14f, 9f, 0.5f), standRib);
            }

            foreach (int end in new[] { -1, 1 })
            {
                float x = end < 0 ? -5.5f : L + 5.5f;
                Box($"EndWall{end}", new Vector3(x, 4.5f, W / 2f), new Vector3(0.5f, 9f, W + 14f), end < 0 ? endBlue : endRed);
                var banner = Look.GlowMaterial(end < 0 ? new Color(0.10f, 0.22f, 0.85f) : new Color(0.85f, 0.15f, 0.15f), 1.3f);
                Box($"Banner{end}", new Vector3(x - end * 0.3f, 3.6f, W / 2f), new Vector3(0.1f, 1.6f, W * 0.7f), banner, false);
            }

            Crowd.Build(root, L, W);
            VenueScreens.Build(root, L, W);
            ScoreCube.Build(root, L, W);   // 桁架 + 懸吊計分板   // 真正的觀眾(約 800 人,GPU instancing)
            Box("Ceiling", new Vector3(L / 2f, 18.5f, W / 2f), new Vector3(L + 40f, 0.5f, W + 40f), ceilMat);
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 3; j++)
                    Box($"Light{i}_{j}", new Vector3(2f + i * ((L - 4f) / 5f), 18.1f, 1.2f + j * (W - 2.4f) / 2f), new Vector3(3.5f, 0.15f, 0.5f), lightMat, false);
        }

        public static void Clear()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}