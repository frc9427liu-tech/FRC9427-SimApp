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
        public static System.Collections.Generic.List<Vector3> FuelPositions;   // 官方起始擺法(場地座標系的 Unity 世界位置);沒載入模型為 null

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

                // 官方預擺球位置:模型裡 GE-26900_Fuel* 的節點(官方 CAD 的起始擺法),取各節點的中心
                var fuelPos = new System.Collections.Generic.List<Vector3>();
                {
                    // 名稱含 Fuel 的 renderer 取中心;同一顆球可能由多個 mesh 組成,0.04m 內視為同一顆
                    var seen = new System.Collections.Generic.HashSet<long>();
                    int namesLogged = 0;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        string nm = r.name + "/" + (r.transform.parent != null ? r.transform.parent.name : "");
                        if (!nm.Contains("Fuel")) continue;
                        if (namesLogged++ < 3) Debug.Log("[FieldModel] fuel renderer name=" + nm + " size=" + r.bounds.size.ToString("0.000"));
                        Vector3 c0 = r.bounds.center;
                        long key = ((long)Mathf.RoundToInt(c0.x / 0.04f) * 73856093L) ^ ((long)Mathf.RoundToInt(c0.y / 0.04f) * 19349663L) ^ ((long)Mathf.RoundToInt(c0.z / 0.04f) * 83492791L);
                        if (!seen.Add(key)) continue;
                        fuelPos.Add(c0);
                    }
                }                FuelPositions = fuelPos;
                Debug.Log($"[FieldModel] staged fuel nodes={fuelPos.Count}");
                int hidden = 0, shown = 0, texCount = 0, dbg = 0;
                var matCache = new System.Collections.Generic.Dictionary<int, Material>();
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
                        // 同色共用一個材質(否則 2000 多個物件各一個材質,無法合批,FPS 掉很多)
                        Color32 c32 = c;
                        int mkey = (c32.r << 16) | (c32.g << 8) | c32.b;
                        if (!matCache.TryGetValue(mkey, out var nm)) { nm = FieldBuilder.MakeMat(c); matCache[mkey] = nm; }
                        if (tex != null) nm.mainTexture = tex;
                        mats[i] = nm;
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = r.bounds.size.magnitude < 0.3f ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;   // 小零件不投影,省陰影運算
                    r.receiveShadows = true;
                }

                // 美化:大面積貼地的平面 = 地毯,改成深藍灰(官方地毯是深色),場地才不會一片白
                {
                    var carpet = FieldBuilder.MakeMat(new Color(0.16f, 0.185f, 0.23f));
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var bb = r.bounds;
                        if (bb.size.y < 0.08f && bb.max.y < 0.12f && bb.size.x > 5f && bb.size.z > 3f)
                        {
                            var ms = r.sharedMaterials;
                            for (int i = 0; i < ms.Length; i++) ms[i] = carpet;
                            r.sharedMaterials = ms;
                        }
                    }
                }

                // 靜態合批:把同材質的 mesh 合併,大幅減少 draw call
                try { StaticBatchingUtility.Combine(root); } catch (Exception e) { Debug.LogWarning("[FieldModel] static batching failed: " + e.Message); }

                // 隱藏程式自己蓋的方塊外觀(碰撞體保留)
                var field = GameObject.Find("Field");
                if (field != null)
                    foreach (var r in field.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

                if (System.Array.IndexOf(Environment.GetCommandLineArgs(), "-dumpoutpost") >= 0)
                {
                    var sbd = new System.Text.StringBuilder();
                    foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                    {
                        var bb = r.bounds;
                        if (bb.center.x < 2.6f && bb.center.z > 2.6f && bb.center.z < 5.5f && bb.size.magnitude > 0.3f && bb.max.y > 0.05f)
                            sbd.AppendLine($"{r.name} min=({bb.min.x:0.00},{bb.min.y:0.00},{bb.min.z:0.00}) max=({bb.max.x:0.00},{bb.max.y:0.00},{bb.max.z:0.00})");
                    }
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "outpostdump.txt"), sbd.ToString());
                }
                if (System.Array.IndexOf(Environment.GetCommandLineArgs(), "-dumpfield") >= 0)
                {
                    // 依名稱關鍵字彙總模型零件的外框(藍方 x<8.27 / 紅方),拿來對照 FieldBuilder 的碰撞體尺寸與位置
                    var sbf = new System.Text.StringBuilder();
                    string[] keys = { "hub", "trench", "bump", "depot", "tower", "outpost", "guardrail", "polycarbonate", "alliance wall", "driver" };
                    foreach (var key in keys)
                        foreach (bool blueSide in new[] { true, false })
                        {
                            Bounds gb = default; bool gf = true; int cnt = 0;
                            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                            {
                                if (r.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                if ((r.bounds.center.x < SimConstants.FieldLength / 2f) != blueSide) continue;
                                if (gf) { gb = r.bounds; gf = false; } else gb.Encapsulate(r.bounds);
                                cnt++;
                            }
                            if (key == "hub" || key == "trench" || key == "bump")
                                foreach (int zh in new[] { 0, 1 })
                                    foreach (bool low in new[] { false, true })
                                    {
                                        Bounds zb = default; bool zf = true; int zc = 0;
                                        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                                        {
                                            if (r.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                            if ((r.bounds.center.x < SimConstants.FieldLength / 2f) != blueSide) continue;
                                            if ((r.bounds.center.z > SimConstants.FieldWidth / 2f ? 1 : 0) != zh) continue;
                                            if (low && r.bounds.max.y > 1.2f) continue;
                                            if (zf) { zb = r.bounds; zf = false; } else zb.Encapsulate(r.bounds);
                                            zc++;
                                        }
                                        if (!zf) sbf.AppendLine($"  {key} [{(blueSide ? "blue" : "red")}] zhalf={zh} low={low} n={zc} min=({zb.min.x:0.000},{zb.min.y:0.000},{zb.min.z:0.000}) max=({zb.max.x:0.000},{zb.max.y:0.000},{zb.max.z:0.000})");
                                    }
                            if (!gf) sbf.AppendLine($"{key} [{(blueSide ? "blue" : "red")}] n={cnt} min=({gb.min.x:0.000},{gb.min.y:0.000},{gb.min.z:0.000}) max=({gb.max.x:0.000},{gb.max.y:0.000},{gb.max.z:0.000})");
                        }
                    foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                    {
                        var bb = r.bounds;
                        if (bb.center.x < 8.27f && bb.center.x > 3.9f && bb.center.x < 5.4f && bb.center.z < 4.035f && (r.name.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("trench", StringComparison.OrdinalIgnoreCase) >= 0))
                            sbf.AppendLine($"PART {r.name.Substring(0, Math.Min(48, r.name.Length))} min=({bb.min.x:0.00},{bb.min.y:0.00},{bb.min.z:0.00}) max=({bb.max.x:0.00},{bb.max.y:0.00},{bb.max.z:0.00})");
                    }
                    // HUB 外框:名稱不含 hub 的話,用 GE-261xx / 4x4 之類的零件名取樣
                    var names = new System.Collections.Generic.SortedDictionary<string, int>();
                    foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                    {
                        string nm = r.name; int colon = nm.IndexOf(':'); string pre = colon > 0 ? nm.Substring(0, colon) : nm;
                        if (pre.Length > 14) pre = pre.Substring(0, 14);
                        names.TryGetValue(pre, out int c0); names[pre] = c0 + 1;
                    }
                    sbf.AppendLine("--- part-name prefixes (count) ---");
                    foreach (var kv in names) if (kv.Key.StartsWith("GE-") || kv.Key.StartsWith("FE-000")) sbf.AppendLine(kv.Key + " " + kv.Value);
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "fielddump.txt"), sbf.ToString());
                }
                Bounds b = default; bool first = true;
                foreach (var r in root.GetComponentsInChildren<Renderer>(false)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                var top = new System.Text.StringBuilder();
                foreach (var kv in colorHist) top.Append(kv.Key + "x" + kv.Value + " ");
                Debug.Log("[FieldModel] textures=" + texCount + " colors: " + top);
                LastLog = $"field model loaded: shown={shown} hiddenFuel={hidden} bounds center={b.center} size={b.size}";
                Debug.Log("[FieldModel] " + LastLog);
                // 遊戲在模型載入完成前就開始了:用官方擺法換掉暫用的格狀擺法
                if (GameSession.Active && Time.time - GameSession.StartTime < 20f) FuelManager.ReplaceStartLayout();
            }
            catch (Exception e) { LastLog = "field error: " + e.Message; Debug.LogError("[FieldModel] " + e); }
        }
    }
}
