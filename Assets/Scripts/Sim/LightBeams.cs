using UnityEngine;

namespace FrcSim
{
    // 天花板燈下的淡淡光柱(假體積光):倒錐形網格 + 加色混合 + 邊緣淡出,增加場館氛圍。
    public static class LightBeams
    {
        public static void Build(Transform parent, System.Collections.Generic.IEnumerable<Vector3> lightPositions, float bottomY)
        {
            var sh = Resources.Load<Shader>("Shaders/FrcBeam"); if (sh == null) return;
            var mat = new Material(sh); mat.SetColor("_Color", new Color(0.88f, 0.95f, 1f)); mat.SetFloat("_Intensity", 0.07f);
            var mesh = ConeMesh(16);
            var root = new GameObject("LightBeams").transform; root.SetParent(parent, false);
            foreach (var p in lightPositions)
            {
                var g = new GameObject("Beam"); g.transform.SetParent(root, false);
                float h = p.y - bottomY; g.transform.position = new Vector3(p.x, bottomY, p.z); g.transform.localScale = new Vector3(3.6f, h, 3.6f);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        // 單位錐:頂點在 y=1(半徑 0.08),底面在 y=0(半徑 1);頂端 alpha=1,往下漸淡到 0
        static Mesh ConeMesh(int seg)
        {
            var v = new Vector3[seg * 2]; var n = new Vector3[seg * 2]; var c = new Color[seg * 2]; var t = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f, cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                v[i] = new Vector3(cx * 0.08f, 1f, sz * 0.08f); v[seg + i] = new Vector3(cx, 0f, sz);
                n[i] = n[seg + i] = new Vector3(cx, 0.2f, sz).normalized;
                c[i] = new Color(1, 1, 1, 1f); c[seg + i] = new Color(1, 1, 1, 0f);
                int j = (i + 1) % seg, k = i * 6;
                t[k] = i; t[k + 1] = seg + i; t[k + 2] = seg + j; t[k + 3] = i; t[k + 4] = seg + j; t[k + 5] = j;
            }
            var m = new Mesh { name = "BeamCone", vertices = v, normals = n, colors = c, triangles = t }; m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2.2f, 1.2f, 2.2f)); return m;
        }
    }
}
