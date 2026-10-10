using UnityEngine;

namespace FrcSim
{
    // 軟邊接觸陰影:即時陰影關閉(預設在內顯上關)時,機器人看起來會「飄」在地上;在車底鋪一塊柔邊黑色圓角影子,成本幾乎為零
    public class BlobShadow : MonoBehaviour
    {
        static Texture2D tex; static Material mat;
        Transform quad;
        public float Size = 1.25f, Alpha = 0.55f;

        void Awake()
        {
            if (tex == null)
            {
                tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[64 * 64];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    // 圓角方形的有號距離 → 柔邊
                    float dx = Mathf.Abs((x - 31.5f) / 31.5f), dy = Mathf.Abs((y - 31.5f) / 31.5f);
                    float d = Mathf.Max(dx, dy) * 0.55f + Mathf.Sqrt(dx * dx + dy * dy) * 0.45f;
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.45f, 1f, d));
                    px[y * 64 + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, renderQueue = 2460 };
            }
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(g.GetComponent<Collider>());
            g.name = "BlobShadow"; quad = g.transform; quad.SetParent(null);
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (quad == null) return;
            var p = transform.position;
            quad.position = new Vector3(p.x, 0.014f, p.z);
            quad.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            quad.localScale = Vector3.one * Size;
        }
        void OnDestroy() { if (quad != null) Destroy(quad.gameObject); }
    }
}