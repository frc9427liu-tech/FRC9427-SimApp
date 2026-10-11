using UnityEngine;

namespace FrcSim
{
    // 場地周圍的低調地燈(展廳質感):外圍一圈細隊色燈條 + 場中央細白環。亮度很低,只當氛圍,不干擾比賽判讀。
    public static class FloorAccents
    {
        public static void Build(Transform parent, float L, float W)
        {
            var root = new GameObject("FloorAccents").transform; root.SetParent(parent, false);
            var blue = Look.GlowMaterial(new Color(0.12f, 0.32f, 1f), 0.9f);
            var red = Look.GlowMaterial(new Color(1f, 0.2f, 0.2f), 0.9f);
            var white = Look.GlowMaterial(new Color(0.85f, 0.9f, 1f), 0.5f);
            float m = 0.9f, t = 0.05f, y = 0.006f;   // 離場地牆外 0.9m
            void Strip(string n, Vector3 c, Vector3 size, Material mt)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(g.GetComponent<Collider>()); g.name = n; g.transform.SetParent(root, false);
                g.transform.position = c; g.transform.localScale = size; var r = g.GetComponent<Renderer>(); r.sharedMaterial = mt; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            float x0 = -m, x1 = L + m, z0 = -m, z1 = W + m, xm = L / 2f;
            // 長邊:左半藍、右半紅
            foreach (float z in new[] { z0, z1 })
            {
                Strip("EdgeB", new Vector3((x0 + xm) / 2f, y, z), new Vector3(xm - x0, 0.01f, t), blue);
                Strip("EdgeR", new Vector3((xm + x1) / 2f, y, z), new Vector3(x1 - xm, 0.01f, t), red);
            }
            // 短邊
            Strip("EndB", new Vector3(x0, y, W / 2f), new Vector3(t, 0.01f, z1 - z0), blue);
            Strip("EndR", new Vector3(x1, y, W / 2f), new Vector3(t, 0.01f, z1 - z0), red);
            // 看台前緣冷白細光(像階梯式座席的踏面燈),兩側各一條
            var cool = Look.GlowMaterial(new Color(0.75f, 0.85f, 1f), 1.1f);
            foreach (int side in new[] { -1, 1 })
            {
                float edge = side < 0 ? 0f : W;
                Strip("StandGlow", new Vector3(L / 2f, 0.07f, edge + side * 3.38f), new Vector3(L + 10f, 0.03f, 0.05f), cool);
                for (int i = 1; i < 10; i += 3) Strip("StandStep", new Vector3(L / 2f, 0.42f * i + 0.05f, edge + side * (3.5f + i * 0.85f - 0.02f)), new Vector3(L + 10f, 0.025f, 0.04f), cool);
            }
            // 場中央細環
            var ring = new GameObject("CenterRing"); ring.transform.SetParent(root, false);
            var lr = ring.AddComponent<LineRenderer>(); lr.useWorldSpace = true; lr.loop = true; lr.widthMultiplier = 0.035f; lr.sharedMaterial = white; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.alignment = LineAlignment.TransformZ;
            int n = 72; lr.positionCount = n; float rad = 1.4f;
            for (int i = 0; i < n; i++) { float a = i / (float)n * Mathf.PI * 2f; lr.SetPosition(i, new Vector3(L / 2f + Mathf.Cos(a) * rad, 0.012f, W / 2f + Mathf.Sin(a) * rad)); }
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}

