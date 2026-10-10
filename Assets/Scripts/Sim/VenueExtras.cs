using UnityEngine;

namespace FrcSim
{
    // 場館配件:場地上方的桁架、懸吊式四面計分板(顯示比分與倒數)
    public class ScoreCube : MonoBehaviour
    {
        TextMesh[] tms = new TextMesh[4];
        public static void Build(Transform parent, float L, float W)
        {
            Font font = null; try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            var steel = new Material(Shader.Find("Standard")); steel.SetColor("_Color", new Color(0.12f, 0.13f, 0.16f)); steel.SetFloat("_Glossiness", 0.3f);
            // 桁架:兩根沿長邊 + 數根橫梁 + 吊桿
            float y = 9.2f;
            void Beam(Vector3 c, Vector3 s) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(g.GetComponent<Collider>()); g.transform.SetParent(parent, false); g.transform.position = c; g.transform.localScale = s; g.GetComponent<Renderer>().sharedMaterial = steel; g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            Beam(new Vector3(L / 2f, y, W / 2f - 3.2f), new Vector3(L + 6f, 0.25f, 0.25f));
            Beam(new Vector3(L / 2f, y, W / 2f + 3.2f), new Vector3(L + 6f, 0.25f, 0.25f));
            for (int i = 0; i <= 8; i++) Beam(new Vector3(-1f + i * (L + 2f) / 8f, y, W / 2f), new Vector3(0.2f, 0.2f, 6.6f));
            // 計分立方體(四面)掛在場地正中央上方
            var cube = new GameObject("ScoreCube"); cube.transform.SetParent(parent, false); cube.transform.position = new Vector3(L / 2f, 6.9f, W / 2f);
            var sc = cube.AddComponent<ScoreCube>();
            Beam(new Vector3(L / 2f, 8.2f, W / 2f), new Vector3(0.06f, 2.2f, 0.06f));
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(body.GetComponent<Collider>()); body.transform.SetParent(cube.transform, false); body.transform.localScale = new Vector3(3.6f, 1.7f, 3.6f); body.GetComponent<Renderer>().sharedMaterial = steel;
            for (int s = 0; s < 4; s++)
            {
                var face = new GameObject("face" + s); face.transform.SetParent(cube.transform, false); face.transform.localRotation = Quaternion.Euler(0, s * 90f, 0); face.transform.localPosition = face.transform.localRotation * new Vector3(0, 0, -1.82f);
                var tm = face.AddComponent<TextMesh>(); tm.font = font; tm.fontSize = 120; tm.characterSize = 0.045f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = Color.white; tm.text = "0 : 0";
                if (font != null) face.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                sc.tms[s] = tm;
            }
        }
        float next;
        void Update()
        {
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 0.25f;
            int t = Mathf.CeilToInt(ScoreManager.TimeLeft);
            string txt = "<color=#5AA0FF>" + ScoreManager.BlueScore + "</color>  :  <color=#FF5A5A>" + ScoreManager.RedScore + "</color>\n" + (t / 60) + ":" + (t % 60).ToString("00");
            foreach (var tm in tms) if (tm != null) tm.text = txt;
        }
    }
}