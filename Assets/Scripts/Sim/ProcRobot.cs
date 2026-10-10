using UnityEngine;

namespace FrcSim
{
    // 沒有 CAD 時用的程序化機器人外觀:規格字串 "proc:長,寬,高,#主色"(長 = 車頭方向 +x)。
    // 結構:底盤框 + 4 個輪模組 + 保險桿(藍色,對手會被自動換成紅色)+ 前方吸球滾輪 + 後方射擊塔 + 主色飾條。
    public static class ProcRobot
    {
        public static bool Is(string file) { return file != null && file.StartsWith("proc:"); }

        static Material Mat(Color c) { var m = new Material(Shader.Find("Standard")); m.SetColor("_Color", c); m.SetFloat("_Glossiness", 0.25f); return m; }
        static GameObject Part(Transform p, PrimitiveType t, Vector3 pos, Vector3 scale, Color c, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(t); Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(p, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            g.GetComponent<Renderer>().sharedMaterial = Mat(c); return g;
        }

        public static void Build(string spec, Transform root)
        {
            var f = spec.Substring(5).Split(',');
            float L = 0.7f, W = 0.7f, H = 0.55f; Color acc = new Color(0.12f, 0.36f, 1f);
            try { L = float.Parse(f[0]); W = float.Parse(f[1]); H = float.Parse(f[2]); ColorUtility.TryParseHtmlString(f[3], out acc); } catch { }
            Color dark = new Color(0.16f, 0.17f, 0.2f), steel = new Color(0.62f, 0.64f, 0.68f), blue = new Color(0.1f, 0.25f, 0.95f);
            // 底盤板
            Part(root, PrimitiveType.Cube, new Vector3(0, 0.08f, 0), new Vector3(L - 0.1f, 0.05f, W - 0.1f), dark);
            // 保險桿(四邊)
            float bh = 0.12f, by = 0.14f, bt = 0.045f;
            Part(root, PrimitiveType.Cube, new Vector3(L / 2f - bt / 2f, by, 0), new Vector3(bt, bh, W), blue);
            Part(root, PrimitiveType.Cube, new Vector3(-L / 2f + bt / 2f, by, 0), new Vector3(bt, bh, W), blue);
            Part(root, PrimitiveType.Cube, new Vector3(0, by, W / 2f - bt / 2f), new Vector3(L, bh, bt), blue);
            Part(root, PrimitiveType.Cube, new Vector3(0, by, -W / 2f + bt / 2f), new Vector3(L, bh, bt), blue);
            // 輪模組
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                Part(root, PrimitiveType.Cylinder, new Vector3(sx * (L / 2f - 0.14f), 0.05f, sz * (W / 2f - 0.11f)), new Vector3(0.1f, 0.025f, 0.1f), new Color(0.07f, 0.07f, 0.08f), new Vector3(90, 0, 0));
            // 立柱框架
            float fx = -L * 0.18f;
            foreach (int sz in new[] { -1, 1 }) Part(root, PrimitiveType.Cube, new Vector3(fx, 0.08f + H * 0.45f, sz * (W / 2f - 0.14f)), new Vector3(0.05f, H * 0.9f, 0.04f), steel);
            Part(root, PrimitiveType.Cube, new Vector3(fx, 0.08f + H * 0.9f, 0), new Vector3(0.05f, 0.04f, W - 0.22f), steel);
            // 射擊塔(飛輪箱 + 護罩)
            Part(root, PrimitiveType.Cube, new Vector3(fx, 0.32f, 0), new Vector3(0.28f, 0.2f, W * 0.45f), dark);
            Part(root, PrimitiveType.Cube, new Vector3(fx + 0.1f, 0.46f, 0), new Vector3(0.16f, 0.06f, W * 0.4f), acc, new Vector3(0, 0, -25));
            Part(root, PrimitiveType.Cylinder, new Vector3(fx, 0.34f, 0), new Vector3(0.1f, W * 0.2f, 0.1f), acc, new Vector3(90, 0, 0));
            // 前方吸球滾輪 + 兩側擋板
            Part(root, PrimitiveType.Cylinder, new Vector3(L / 2f - 0.02f, 0.12f, 0), new Vector3(0.09f, W * 0.42f, 0.09f), new Color(1f, 0.82f, 0.1f), new Vector3(90, 0, 0));
            foreach (int sz in new[] { -1, 1 }) Part(root, PrimitiveType.Cube, new Vector3(L / 2f - 0.14f, 0.14f, sz * W * 0.42f), new Vector3(0.28f, 0.12f, 0.025f), steel);
            // 儲球槽
            Part(root, PrimitiveType.Cube, new Vector3(L * 0.12f, 0.2f, 0), new Vector3(L * 0.34f, 0.025f, W * 0.7f), steel);
            // 主色飾條(隊色)
            Part(root, PrimitiveType.Cube, new Vector3(0, 0.215f, 0), new Vector3(L - 0.02f, 0.015f, 0.05f), acc);
        }
    }
}