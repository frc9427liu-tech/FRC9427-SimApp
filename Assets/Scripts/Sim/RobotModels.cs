using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using GLTFast;

namespace FrcSim
{
    // 匯入機器人 3D 模型(.glb):放在 exe 旁邊的 Robots 資料夾,設定裡切換。
    // Onshape/CAD 匯出的模型座標:Y 向上、單位公尺。ModelYawDeg 用來把車頭對到模擬器的 +X。
    public static class RobotModels
    {
        public static string Dir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Robots");

        public static string[] List()
        {
            try
            {
                var l = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.glb").Select(Path.GetFileName).ToList() : new List<string>();
                foreach (var m in ModelLibrary.All) if (ModelLibrary.Installed(m)) l.Add(m.File); foreach (var m in ModelLibrary.Customs()) l.Add(m.File);   // 開源模型庫下載到 C:\FRC\models 的也算
                return l.OrderBy(x => x).ToArray();
            }
            catch { return new string[0]; }
        }

        // "" = 內建方塊。第一次沒設定過就用資料夾裡的第一個模型。
        public static string Selected
        {
            get
            {
                if (PlayerPrefs.HasKey("robotModel"))
                {
                    string s = Prefs.GetString("robotModel", "");
                    return s == "" || ProcRobot.Is(s) || List().Contains(s) ? s : "";
                }
                var l = List();
                return l.Length > 0 ? l[0] : "";
            }
            set { Prefs.SetString("robotModel", value); PlayerPrefs.Save(); }
        }

        // 選單顯示用的友善名稱:程序化機器人("proc:..." 規格字串)顯示成內建機構名稱/開源隊名,不要露出規格字串
        public static string Display(string file)
        {
            if (ModelLibrary.Is(file)) return ModelLibrary.Name(ModelLibrary.Find(file));
            if (!ProcRobot.Is(file)) return file;
            foreach (var p in MechPresets.All) if (p.Model == file) return Loc.Lang == "en" ? p.EnName : p.ZhName;
            foreach (var c in OpenSourceCatalog.All) if (c.Model == file) return c.Team;
            return Loc.Lang == "en" ? "Generic robot" : "通用機器人";
        }
        public static float YawDeg
        {
            // kepler.glb 的 intake 在模型 +z 側,車頭(+x)要轉 90° 才對;使用者沒調過時預設用這個
            get => PlayerPrefs.GetFloat("modelYaw", ModelLibrary.Is(Selected) ? ModelLibrary.Yaw(Selected) : (Selected == "kepler.glb") ? 90f : 0f);
            set { PlayerPrefs.SetFloat("modelYaw", value); PlayerPrefs.Save(); }
        }

        public static string LastError = "";
        static Quaternion fixPre = Quaternion.identity;   // 模型庫 Z-up 模型的先行俯仰修正(由 Attach 設定、Fix 使用)

        // 載入並掛到機器人底下,貼地置中。成功後隱藏內建方塊外觀。
        public static async void Attach(Transform robot, string file, float yawDeg, Action<bool> done, bool red = false)
        {
            bool ok = false;
            { var lib = ModelLibrary.Find(file); if (lib != null) { yawDeg = ModelLibrary.Yaw(file); fixPre = Quaternion.Euler(lib.Pitch, 0f, 0f); } else fixPre = Quaternion.identity; }
            if (ProcRobot.Is(file))
            {
                var pv = new GameObject("ModelPivot"); pv.transform.SetParent(robot, false); pv.transform.localPosition = new Vector3(0f, -(SimConstants.BumperHeight / 2f + 0.03f), 0f); pv.AddComponent<BodyLean>();
                var pr = new GameObject("ModelRoot"); pr.transform.SetParent(pv.transform, false); ProcRobot.Build(file, pr.transform);
                Fix(pr, robot, yawDeg, red); CombineByMaterial(pr); done?.Invoke(true); return;
            }
            try
            {
                var path = ModelLibrary.Is(file) ? ModelLibrary.Find(file).Path : Path.Combine(Dir, file);
                Debug.Log($"[RobotModels] loading {path} exists={File.Exists(path)}");
                var imp = new GltfImport(null, null, null, new GLTFast.Logging.ConsoleLogger());
                bool loaded = await imp.Load(new Uri(path).AbsoluteUri);
                Debug.Log($"[RobotModels] Load returned {loaded}");
                if (loaded)
                {
                    // 外觀掛在「慣性傾斜」軸心下:加速時車頭微微上揚、煞車時點頭(以地面接觸點為軸),看得出底盤慣性
                    var pivot = new GameObject("ModelPivot");
                    pivot.transform.SetParent(robot, false);
                    pivot.transform.localPosition = new Vector3(0f, -(SimConstants.BumperHeight / 2f + 0.03f), 0f);
                    pivot.AddComponent<BodyLean>();
                    var root = new GameObject("ModelRoot");
                    root.transform.SetParent(pivot.transform, false);
                    bool inst = await imp.InstantiateMainSceneAsync(root.transform);
                    Debug.Log($"[RobotModels] Instantiate returned {inst}");
                    if (inst)
                    {
                        cadModel = file.StartsWith("team_"); Fix(root, robot, yawDeg, red); cadModel = false; doubleSide = file.StartsWith("team_"); CombineByMaterial(root); doubleSide = false;
                        ok = true;
                    }
                    else UnityEngine.Object.Destroy(root);
                }
                else LastError = "load failed";
            }
            catch (Exception e) { LastError = e.Message; Debug.LogError("RobotModels: " + e); }
            done?.Invoke(ok);
        }

        static readonly Dictionary<int, Material> matCache = new Dictionary<int, Material>();

        // 把整個模型依材質合併成少數幾個 Mesh(模型不會動,只會跟著車走),draw call 從幾百降到十幾個;不可讀的 Mesh 就略過
        static bool cadModel;
        static bool doubleSide;   // CAD 匯入的薄板模型(Standard 材質剔除背面會破洞):合併時補一份反向面

        static void DoubleSide(Mesh m)
        {
            var v = m.vertices; var n = m.normals; var t = m.triangles; int vc = v.Length;
            var nv = new Vector3[vc * 2]; var nn = new Vector3[vc * 2];
            System.Array.Copy(v, nv, vc); System.Array.Copy(v, 0, nv, vc, vc);
            for (int i = 0; i < vc; i++) { nn[i] = n.Length == vc ? n[i] : Vector3.up; nn[vc + i] = -nn[i]; }
            var nt = new int[t.Length * 2]; System.Array.Copy(t, nt, t.Length);
            for (int i = 0; i < t.Length; i += 3) { nt[t.Length + i] = t[i] + vc; nt[t.Length + i + 1] = t[i + 2] + vc; nt[t.Length + i + 2] = t[i + 1] + vc; }
            var uv = m.uv; Vector2[] nuv = null; if (uv.Length == vc) { nuv = new Vector2[vc * 2]; System.Array.Copy(uv, nuv, vc); System.Array.Copy(uv, 0, nuv, vc, vc); }
            m.Clear(); m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; m.vertices = nv; m.normals = nn; if (nuv != null) m.uv = nuv; m.triangles = nt;
        }

        static void CombineByMaterial(GameObject root)
        {
            try
            {
                var groups = new Dictionary<Material, List<CombineInstance>>();
                var used = new List<Renderer>();
                Matrix4x4 w2l = root.transform.worldToLocalMatrix;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = mf.GetComponent<Renderer>();
                    if (r == null || !r.enabled || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    var mats = r.sharedMaterials;
                    for (int sm = 0; sm < mf.sharedMesh.subMeshCount && sm < mats.Length; sm++)
                    {
                        if (!groups.TryGetValue(mats[sm], out var l)) groups[mats[sm]] = l = new List<CombineInstance>();
                        l.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sm, transform = w2l * mf.transform.localToWorldMatrix });
                    }
                    used.Add(r);
                }
                if (used.Count == 0) { Debug.Log("[RobotModels] combine skipped (meshes not readable)"); return; }
                foreach (var kv in groups)
                {
                    var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                    if (doubleSide) DoubleSide(mesh);
                    var go = new GameObject("Combined");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                foreach (var r in used) r.enabled = false;
                Debug.Log($"[RobotModels] combined {used.Count} renderers into {groups.Count} meshes");
            }
            catch (Exception e) { Debug.LogWarning("[RobotModels] combine failed: " + e.Message); }
        }

        static void Fix(GameObject root, Transform robot, float yawDeg, bool red = false)
        {
            // 材質換成內建 Standard(保留 glTF 的基本色),避免 shader 被打包流程裁掉
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    Color c = new Color(0.6f, 0.62f, 0.66f);
                    if (m != null)
                    {
                        if (m.HasProperty("baseColorFactor")) c = m.GetColor("baseColorFactor");
                        else if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                        else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
                    }
                    c.a = 1f; if (cadModel) c = new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 1f);   // CAD 匯入件原色偏亮,場館強光下頂面會過曝成白
                    if (red && c.b > c.r + 0.15f && c.b > c.g) c = new Color(c.b, c.g * 0.4f, c.r * 0.6f, 1f);   // 紅方:藍色保險桿改紅色
                    { Color32 c32 = c; int key = (c32.r << 16) | (c32.g << 8) | c32.b | (cadModel ? 1 << 24 : 0); if (!matCache.TryGetValue(key, out var cm)) { cm = cadModel ? FieldBuilder.MakeMat(c, 0.06f, 0f) : FieldBuilder.MakeMat(c); matCache[key] = cm; } mats[i] = cm; }   // 同色共用材質,才能合批
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            Debug.Log($"[RobotModels] Fix: renderers={root.GetComponentsInChildren<Renderer>(true).Length} meshFilters={root.GetComponentsInChildren<MeshFilter>(true).Length} children={root.transform.childCount} active={root.activeInHierarchy}");
            root.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f) * fixPre;
            // 貼地置中:底部對齊車底,XZ 置中於機器人中心
            Bounds b = default; bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            Debug.Log($"[RobotModels] bounds first={first} center={b.center} size={b.size}");
            if (first) return;
            {
                var rs = root.GetComponentsInChildren<Renderer>(true);
                var mins = new System.Collections.Generic.List<float>();
                foreach (var r in rs) mins.Add(r.bounds.min.y);
                mins.Sort();
                var sbd = new System.Text.StringBuilder("[RobotModels] lowest renderers minY: ");
                for (int i = 0; i < Mathf.Min(12, mins.Count); i++) sbd.Append(mins[i].ToString("0.000")).Append(' ');
                sbd.Append("| p10=" + mins[mins.Count / 10].ToString("0.000") + " median=" + mins[mins.Count / 2].ToString("0.000") + " robotY=" + robot.position.y.ToString("0.000"));
                foreach (var r in rs) if (r.bounds.min.y <= mins[Mathf.Min(2, mins.Count - 1)]) sbd.Append(" lowName=" + r.name + " size=" + r.bounds.size.ToString("0.00"));
                Debug.Log(sbd.ToString());
            }
            // 底部取「主體」最低點:少數零件(<8%)若單獨掉在下方一大段(間隔 >5cm),不算進底部,否則整台機器人會懸空
            float bottom = b.min.y;
            {
                var ys = new System.Collections.Generic.List<float>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) ys.Add(r.bounds.min.y);
                ys.Sort();
                int start = 0;
                while (true)
                {
                    int end = start;
                    while (end + 1 < ys.Count && ys[end + 1] - ys[end] <= 0.05f) end++;
                    int size = end - start + 1;
                    if (end + 1 < ys.Count && size < ys.Count * 0.08f) { start = end + 1; continue; }
                    break;
                }
                bottom = ys[start];
            }
            Vector3 target = new Vector3(robot.position.x, robot.position.y - SimConstants.BumperHeight / 2f, robot.position.z);
            root.transform.position += new Vector3(target.x - b.center.x, target.y - bottom, target.z - b.center.z);

            // 隱藏內建方塊外觀
            foreach (var n in new[] { "Body", "IntakeArm", "Turret" })
            {
                var t = robot.Find(n);
                if (t != null) t.gameObject.SetActive(false);
            }
        }
    }
}


