using UnityEngine;

namespace FrcSim
{
    // 場館配件:場地上方的桁架、懸吊式四面計分板(顯示比分與倒數)
    public class ScoreCube : MonoBehaviour
    {
        TextMesh[] tms = new TextMesh[4];
        public static GameObject Overhead;   // 桁架+計分板;俯視/全景機位在它們下方會被擋住,CameraRig 在那些機位把它藏起來
        public static void Build(Transform parent0, float L, float W)
        {
            Overhead = new GameObject("Overhead"); Overhead.transform.SetParent(parent0, false); Transform parent = Overhead.transform;
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
                if (font != null) { var ts = Resources.Load<Shader>("Shaders/FrcText3D"); if (ts != null) { var fm = new Material(ts); fm.mainTexture = font.material.mainTexture; face.GetComponent<MeshRenderer>().sharedMaterial = fm; } else face.GetComponent<MeshRenderer>().sharedMaterial = font.material; }   // 自訂字型 shader:ZTest 正常,不會穿透方塊顯示對面的鏡像字
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
    // 場館牆面:大型 LED 影片看板(動態流動色彩 + 標語)與天花板垂下的橫幅
    public static class VenueScreens
    {
        static Material Font3D(Font f) { var ts = Resources.Load<Shader>("Shaders/FrcText3D"); if (ts == null || f == null) return null; var m = new Material(ts); m.mainTexture = f.material.mainTexture; return m; }
        static void Text(Transform p, string s, Vector3 pos, Quaternion rot, float size, Color c, Font f, int fs = 100)
        {
            var go = new GameObject("T"); go.transform.SetParent(p, false); go.transform.position = pos; go.transform.rotation = rot;
            var tm = go.AddComponent<TextMesh>(); tm.font = f; tm.fontSize = fs; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c; tm.text = s;
            var m = Font3D(f); if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        public static void Build(Transform parent, float L, float W)
        {
            var root = new GameObject("VenueScreens").transform; root.SetParent(parent, false);
            Font font = null; try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            var sh = Resources.Load<Shader>("Shaders/FrcScreen");
            Material ScreenMat(Color a, Color b, float speed) { var m = new Material(sh != null ? sh : Shader.Find("Unlit/Color")); if (sh != null) { m.SetColor("_ColA", a); m.SetColor("_ColB", b); m.SetFloat("_Speed", speed); } else m.color = a; return m; }
            var frame = new Material(Shader.Find("Standard")); frame.SetColor("_Color", new Color(0.07f, 0.075f, 0.09f));
            void Quad(Vector3 c, Vector3 size, Quaternion rot, Material m, string name)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(g.GetComponent<Collider>()); g.name = name; g.transform.SetParent(root, false);
                g.transform.position = c; g.transform.rotation = rot; g.transform.localScale = size; var r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            string[] slogans = { "REBUILT 2026", "FIRST ROBOTICS", "GRACIOUS PROFESSIONALISM", "FRC 9427" };
            int si = 0;
            foreach (int side in new[] { -1, 1 })
            {
                float edge = side < 0 ? 0f : W;
                float wallZ = edge + side * (3.5f + 10 * 0.85f + 0.3f) - side * 0.28f;   // 看台後牆面向場地的那一面
                Quaternion face = side < 0 ? Quaternion.Euler(0, 180f, 0) : Quaternion.identity;   // 看板/文字朝向場地
                foreach (float fx in new[] { 0.27f, 0.73f })
                {
                    Vector3 c = new Vector3(L * fx, 7.0f, wallZ - side * 0.02f);   // 抬高到觀眾頭上,才不會被後排人擋住
                    Quad(c + side * new Vector3(0, 0, 0.03f), new Vector3(10.6f, 3.9f, 1f), face, frame, "ScreenFrame");   // 框在螢幕後方(靠牆那側)
                    Quad(c, new Vector3(10f, 3.3f, 1f), face, ScreenMat(new Color(0.1f, 0.35f, 1f), new Color(1f, 0.2f, 0.25f), 0.25f + 0.08f * si), "Screen");
                    Text(root, slogans[si++ % slogans.Length], c - new Vector3(0, 0, side * 0.04f), face, 0.036f, new Color(1f, 1f, 1f, 0.96f), font, 120);
                }
                // 天花板垂下的橫幅(隊色交錯)
                for (int i = 0; i < 6; i++)
                {
                    float bx = L * (0.08f + i * 0.168f);
                    Color bc = i % 2 == 0 ? new Color(0.08f, 0.2f, 0.85f) : new Color(0.85f, 0.14f, 0.14f);
                    Vector3 bp = new Vector3(bx, 12.6f, edge + side * 8.5f);   // 掛在螢幕上方(從天花板垂下),不擋螢幕
                    var bm = new Material(Shader.Find("Standard")); bm.SetColor("_Color", bc); bm.SetFloat("_Glossiness", 0.15f);
                    Quad(bp, new Vector3(1.5f, 4.2f, 1f), face, bm, "Banner");
                    Quad(bp + new Vector3(0, 2.15f, 0), new Vector3(1.6f, 0.08f, 1f), face, frame, "BannerBar"); Quad(bp + new Vector3(0, 3.6f, 0), new Vector3(0.04f, 3.0f, 1f), face, frame, "BannerRope");
                    string[] bt = { "REBUILT", "9427", "FRC", "2026", "GP", "FIRST" };
                    Text(root, bt[i], bp - new Vector3(0, 0.3f, side * 0.02f), face, 0.03f, Color.white, font, 100);
                }
            }
        }
    }
}