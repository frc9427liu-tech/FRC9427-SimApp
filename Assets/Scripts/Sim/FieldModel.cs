using System;
using System.IO;
using GLTFast;
using UnityEngine;

namespace FrcSim
{
    // 載入官方 2026 REBUILT 場地模型(FIRST 場地 CAD 轉出的 glb,AdvantageScope 同款 Field3d_2026FRCFieldV2),
    // 只換「外觀」:碰撞體仍然是程式照官方圖面蓋的方塊,所以物理不變。找不到檔案就維持方塊外觀。
    public static class FieldModel
    {
        public static string LastLog = "";

        public static async void Load()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Sim", "field", "Field2026.glb");
                if (!File.Exists(path)) { LastLog = "no field model file"; return; }
                var imp = new GltfImport(null, null, null, new GLTFast.Logging.ConsoleLogger());
                if (!await imp.Load(new Uri(path).AbsoluteUri)) { LastLog = "field load failed"; return; }
                var root = new GameObject("FieldModel");
                if (!await imp.InstantiateMainSceneAsync(root.transform)) { UnityEngine.Object.Destroy(root); LastLog = "field instantiate failed"; return; }

                // 模型原點在場地中心、glTF Y 向上;轉 180° 後 x = 場地長邊、z = 場地寬邊(WPILib y),再平移到場地中心
                root.transform.rotation = Quaternion.identity;
                root.transform.position = new Vector3(SimConstants.FieldLength / 2f, 0f, SimConstants.FieldWidth / 2f);

                int hidden = 0, shown = 0, texCount = 0, dbg = 0;
                var colorHist = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // 模型內附的 455 顆預擺 Fuel 由模擬器自己管理,隱藏
                    if (r.name.Contains("Fuel") || (r.transform.parent != null && r.transform.parent.name.Contains("Fuel"))) { r.gameObject.SetActive(false); hidden++; continue; }
                    shown++;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        Color c = new Color(0.6f, 0.62f, 0.66f);
                        Texture tex = null;
                        if (m != null)
                        {
                            if (m.HasProperty("baseColorFactor")) c = m.GetColor("baseColorFactor");
                            else if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                            else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
                            if (m.HasProperty("baseColorTexture")) tex = m.GetTexture("baseColorTexture");
                            else if (m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
                        }
                        c.a = 1f;
                        if (tex != null) texCount++;
                        string ck = $"{c.r:0.0},{c.g:0.0},{c.b:0.0}";
                        colorHist.TryGetValue(ck, out int cc); colorHist[ck] = cc + 1;
                        if (dbg++ < 3 && m != null) Debug.Log("[FieldModel] mat " + m.name + " shader=" + m.shader.name + " color=" + ck + " tex=" + (tex != null));
                        // 模型幾乎全是接近純白的顏色,直接打光會過曝成一片白:偏白/灰的顏色壓暗,紅藍保持鮮明
                        if (Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b) < 0.25f) c = new Color(c.r * 0.62f, c.g * 0.64f, c.b * 0.68f, 1f);
                        var nm = FieldBuilder.MakeMat(c);
                        if (tex != null) nm.mainTexture = tex;
                        mats[i] = nm;
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // 隱藏程式自己蓋的方塊外觀(碰撞體保留)
                var field = GameObject.Find("Field");
                if (field != null)
                    foreach (var r in field.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

                Bounds b = default; bool first = true;
                foreach (var r in root.GetComponentsInChildren<Renderer>(false)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                var top = new System.Text.StringBuilder();
                foreach (var kv in colorHist) top.Append(kv.Key + "x" + kv.Value + " ");
                Debug.Log("[FieldModel] textures=" + texCount + " colors: " + top);
                LastLog = $"field model loaded: shown={shown} hiddenFuel={hidden} bounds center={b.center} size={b.size}";
                Debug.Log("[FieldModel] " + LastLog);
            }
            catch (Exception e) { LastLog = "field error: " + e.Message; Debug.LogError("[FieldModel] " + e); }
        }
    }
}
