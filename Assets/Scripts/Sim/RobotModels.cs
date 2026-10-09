using System;
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
                if (!Directory.Exists(Dir)) return new string[0];
                return Directory.GetFiles(Dir, "*.glb").Select(Path.GetFileName).OrderBy(x => x).ToArray();
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
                    string s = PlayerPrefs.GetString("robotModel");
                    return s == "" || List().Contains(s) ? s : "";
                }
                var l = List();
                return l.Length > 0 ? l[0] : "";
            }
            set { PlayerPrefs.SetString("robotModel", value); PlayerPrefs.Save(); }
        }

        public static float YawDeg
        {
            // kepler.glb 的 intake 在模型 +z 側,車頭(+x)要轉 90° 才對;使用者沒調過時預設用這個
            get => PlayerPrefs.GetFloat("modelYaw", Selected == "kepler.glb" ? 90f : 0f);
            set { PlayerPrefs.SetFloat("modelYaw", value); PlayerPrefs.Save(); }
        }

        public static string LastError = "";

        // 載入並掛到機器人底下,貼地置中。成功後隱藏內建方塊外觀。
        public static async void Attach(Transform robot, string file, float yawDeg, Action<bool> done)
        {
            bool ok = false;
            try
            {
                var path = Path.Combine(Dir, file);
                Debug.Log($"[RobotModels] loading {path} exists={File.Exists(path)}");
                var imp = new GltfImport(null, null, null, new GLTFast.Logging.ConsoleLogger());
                bool loaded = await imp.Load(new Uri(path).AbsoluteUri);
                Debug.Log($"[RobotModels] Load returned {loaded}");
                if (loaded)
                {
                    var root = new GameObject("ModelRoot");
                    root.transform.SetParent(robot, false);
                    bool inst = await imp.InstantiateMainSceneAsync(root.transform);
                    Debug.Log($"[RobotModels] Instantiate returned {inst}");
                    if (inst)
                    {
                        Fix(root, robot, yawDeg);
                        ok = true;
                    }
                    else UnityEngine.Object.Destroy(root);
                }
                else LastError = "load failed";
            }
            catch (Exception e) { LastError = e.Message; Debug.LogError("RobotModels: " + e); }
            done?.Invoke(ok);
        }

        static void Fix(GameObject root, Transform robot, float yawDeg)
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
                    c.a = 1f;
                    mats[i] = FieldBuilder.MakeMat(c);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            Debug.Log($"[RobotModels] Fix: renderers={root.GetComponentsInChildren<Renderer>(true).Length} meshFilters={root.GetComponentsInChildren<MeshFilter>(true).Length} children={root.transform.childCount} active={root.activeInHierarchy}");
            root.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
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
