using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 看台上真正的觀眾:低面數人形(軀幹/頭/手臂/大腿),約 800 人,GPU instancing 一次畫完;進球時手舉起來、頭點動(shader 內做,零 CPU)
    public class Crowd : MonoBehaviour
    {
        static Mesh personMesh;
        static Material mat;
        readonly List<Matrix4x4[]> mats = new List<Matrix4x4[]>();
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();

        static Mesh BuildPerson()
        {
            var parts = new List<CombineInstance>();
            Mesh cube = GetPrimitive(PrimitiveType.Cube), sph = GetPrimitive(PrimitiveType.Sphere);
            void Add(Mesh m, Vector3 pos, Vector3 scale, Color vc)
            {
                var src = Object.Instantiate(m);
                var cols = new Color[src.vertexCount]; for (int i = 0; i < cols.Length; i++) cols[i] = vc; src.colors = cols;
                parts.Add(new CombineInstance { mesh = src, transform = Matrix4x4.TRS(pos, Quaternion.identity, scale) });
            }
            // vertex color: R = arm, G = head, B = 1 shirt / 0.5 pants / 0 skin
            Add(cube, new Vector3(0f, 0.62f, 0f), new Vector3(0.40f, 0.52f, 0.22f), new Color(0, 0, 1f));        // torso
            Add(sph, new Vector3(0f, 1.02f, 0.02f), new Vector3(0.21f, 0.24f, 0.22f), new Color(0, 1f, 0f));      // head
            Add(cube, new Vector3(-0.26f, 0.62f, 0f), new Vector3(0.10f, 0.46f, 0.12f), new Color(1f, 0, 1f));    // arm L
            Add(cube, new Vector3(0.26f, 0.62f, 0f), new Vector3(0.10f, 0.46f, 0.12f), new Color(1f, 0, 1f));     // arm R
            Add(cube, new Vector3(-0.10f, 0.30f, 0.22f), new Vector3(0.15f, 0.14f, 0.46f), new Color(0, 0, 0.5f)); // thigh L
            Add(cube, new Vector3(0.10f, 0.30f, 0.22f), new Vector3(0.15f, 0.14f, 0.46f), new Color(0, 0, 0.5f));  // thigh R
            Add(cube, new Vector3(-0.10f, 0.12f, 0.42f), new Vector3(0.14f, 0.34f, 0.14f), new Color(0, 0, 0.5f)); // shin L
            Add(cube, new Vector3(0.10f, 0.12f, 0.42f), new Vector3(0.14f, 0.34f, 0.14f), new Color(0, 0, 0.5f));  // shin R
            var mesh = new Mesh { name = "Person" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }
        static Mesh GetPrimitive(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(g); return m; }

        public static void Build(Transform parent, float L, float W)
        {
            var sh = Resources.Load<Shader>("Shaders/FrcCrowd");
            if (sh == null || !sh.isSupported) return;
            if (personMesh == null) personMesh = BuildPerson();
            if (mat == null) { mat = new Material(sh) { enableInstancing = true }; }
            var go = new GameObject("Crowd"); go.transform.SetParent(parent, false);
            var c = go.AddComponent<Crowd>();
            var rng = new System.Random(77);
            var skins = new[] { new Color(0.96f, 0.80f, 0.66f), new Color(0.85f, 0.66f, 0.50f), new Color(0.65f, 0.47f, 0.34f), new Color(0.42f, 0.30f, 0.22f), new Color(0.98f, 0.86f, 0.74f) };
            var neutral = new[] { new Color(0.9f, 0.9f, 0.92f), new Color(0.15f, 0.15f, 0.18f), new Color(0.95f, 0.8f, 0.2f), new Color(0.25f, 0.6f, 0.35f), new Color(0.7f, 0.3f, 0.6f), new Color(0.5f, 0.5f, 0.55f), new Color(0.95f, 0.55f, 0.15f) };
            var mList = new List<Matrix4x4>(); var shirt = new List<Vector4>(); var skin = new List<Vector4>(); var phase = new List<float>();
            void Flush()
            {
                if (mList.Count == 0) return;
                for (int k = 0; k < mList.Count; k++)
                {
                    var g = new GameObject("p"); g.transform.SetParent(go.transform, false);
                    g.transform.position = mList[k].GetColumn(3);
                    g.transform.rotation = mList[k].rotation;
                    g.transform.localScale = mList[k].lossyScale;
                    g.AddComponent<MeshFilter>().sharedMesh = personMesh;
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    var b = new MaterialPropertyBlock(); b.SetColor("_Shirt", shirt[k]); b.SetColor("_Skin", skin[k]); b.SetFloat("_Phase", phase[k]);
                    r.SetPropertyBlock(b);
                }
                mList.Clear(); shirt.Clear(); skin.Clear(); phase.Clear();
            }            foreach (int side in new[] { -1, 1 })
            {
                float edge = side < 0 ? 0f : W;
                for (int row = 0; row < 10; row++)
                {
                    float depth = 0.85f, h = 0.42f * (row + 1);
                    float z = edge + side * (3.5f + row * depth + depth / 2f) - side * 0.12f;
                    float yaw = side < 0 ? 0f : 180f;   // 面向場地
                    for (float x = -5.2f + (row % 2) * 0.27f; x < L + 5.2f; x += 0.54f)
                    {
                        if (rng.NextDouble() < 0.12) continue;   // 約 88% 滿座,有空位才自然
                        float fan = Mathf.Clamp01(x / L);          // 0 = 藍方端、1 = 紅方端
                        Color sc;
                        double roll = rng.NextDouble();
                        if (roll < 0.42) sc = Color.Lerp(new Color(0.12f, 0.28f, 0.95f), new Color(0.55f, 0.65f, 1f), (float)rng.NextDouble() * 0.5f) * Mathf.Lerp(1f, 0.25f, fan * 1.1f);
                        else if (roll < 0.84) sc = Color.Lerp(new Color(0.95f, 0.15f, 0.12f), new Color(1f, 0.6f, 0.55f), (float)rng.NextDouble() * 0.5f) * Mathf.Lerp(0.25f, 1f, fan * 1.1f);
                        else sc = neutral[rng.Next(neutral.Length)];
                        float scl = 0.92f + (float)rng.NextDouble() * 0.2f;
                        mList.Add(Matrix4x4.TRS(new Vector3(x + (float)(rng.NextDouble() - 0.5) * 0.08f, h + 0.05f, z), Quaternion.Euler(0f, yaw + (float)(rng.NextDouble() - 0.5) * 14f, 0f), Vector3.one * scl));
                        shirt.Add(sc); skin.Add(skins[rng.Next(skins.Length)]); phase.Add((float)rng.NextDouble());
                        if (mList.Count == 1000) Flush();
                    }
                }
            }
            Flush();
            Debug.Log("[Crowd] built " + go.transform.childCount);
        }

        void Update() { Shader.SetGlobalFloat("_FrcExcite", Juice.Excitement); }
    }
}
