using UnityEngine;

namespace FrcSim
{
    // 選單裡的機器人預覽:在機器人設定/選擇畫面,背景的場地中間放一台目前選中的車(旋轉鏡頭繞著看);
    // 換選擇時舊車滑出、新車「開進場」(賽車遊戲選車的感覺)。進入比賽後自動消失。
    public class PreviewRobot : MonoBehaviour
    {
        public static bool Want;                 // 選單設定:目前畫面要不要顯示預覽車
        public static PreviewRobot I;
        public static Vector3 Center => new Vector3(2.4f, 0f, SimConstants.FieldWidth * 0.5f);   // 藍方聯盟區起始點附近(沒有球堆、背景好看)
        public bool Active => car != null && Want;

        GameObject car; string shown = "\u0001"; float checkT, enterT = 9f; int token;
        Vector3 restPos;

        public static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("PreviewRobot"); I = go.AddComponent<PreviewRobot>();
        }

        void Start() { if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-carswitchtest") >= 0) StartCoroutine(CarSwitchTest()); }

        // -carswitchtest -menu picker:循環切換全部車款 2 輪,量「切換到車載入完成」的時間、場上 PreviewCar 數量(洩漏)、記憶體成長。結果 carswitchtest.txt。
        // PASS:每次 <= 4.0 s、PreviewCar 恆為 1、記憶體成長 < 400MB。
        System.Collections.IEnumerator CarSwitchTest()
        {
            var sb = new System.Text.StringBuilder(); yield return new WaitForSecondsRealtime(8f);
            var list = new System.Collections.Generic.List<string> { "" }; list.AddRange(RobotModels.List());
            long mem0 = System.GC.GetTotalMemory(true); float worst = 0f; int maxCars = 0; bool ok = true;
            for (int round = 0; round < 2; round++)
                foreach (var f in list)
                {
                    float t0 = Time.realtimeSinceStartup; RobotModels.Selected = f;
                    string want = string.IsNullOrEmpty(f) ? "proc:0.72,0.74,0.55,#28B0FF" : f;
                    while (Time.realtimeSinceStartup - t0 < 10f) { if (car != null && shown == want && enterT >= 0f && car.GetComponentInChildren<Renderer>() != null) break; yield return null; }
                    float dt = Time.realtimeSinceStartup - t0; worst = Mathf.Max(worst, dt);
                    yield return null;   // 等舊車的 Destroy 在這個影格結束時生效,再數數量
                    int n = 0; foreach (var go in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (go.name == "PreviewCar") n++;
                    maxCars = Mathf.Max(maxCars, n);
                    sb.AppendLine($"r{round} {(f == "" ? "(builtin)" : f)}: ready in {dt:0.00}s  PreviewCar objects={n}");
                    if (dt > 4f || n != 1) ok = false;
                    yield return new WaitForSecondsRealtime(0.3f);
                }
            long mem1 = System.GC.GetTotalMemory(true); float growMB = (mem1 - mem0) / 1048576f; if (growMB > 400f) ok = false;
            sb.AppendLine($"worst={worst:0.00}s maxCars={maxCars} managedMemGrowth={growMB:0.0}MB");
            sb.AppendLine(ok ? "PASS" : "FAIL");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "carswitchtest.txt"), sb.ToString());
            Application.Quit();
        }

        public static float Dist = 2.3f;   // 2.7→2.3:預覽車更大(Apple 評審:車只占畫面 1/4);再近會壓到卡片條   // 滾輪縮放:鏡頭距離(公尺)
        public static float DragDeg;   // 滑鼠左鍵在右側空白處拖曳 = 轉動檢視角度
        GUIStyle nameSt, subSt; string dims = ""; float dimsT;
        void OnGUI()
        {
            if (!Active || Event.current.type != EventType.Repaint) return;
            if (nameSt == null) { nameSt = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight }; subSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight }; }
            float sc = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.8f); nameSt.fontSize = Mathf.RoundToInt(34 * sc); subSt.fontSize = Mathf.RoundToInt(16 * sc);
            string proj = Prefs.GetString("robotProject", "");
            string team = string.IsNullOrEmpty(proj) || Prefs.GetInt("useRealCode", 0) != 1 ? Loc.T("model.builtin") : System.IO.Path.GetFileName(proj.TrimEnd('\\', '/'));
            string mdl = string.IsNullOrEmpty(RobotModels.Selected) ? "" : RobotModels.Display(RobotModels.Selected);
            if (car != null && dimsT < Time.unscaledTime)
            {   // 量尺寸(模型載入後才有 Renderer):長寬高,每 0.5 秒更新
                dimsT = Time.unscaledTime + 0.5f; Bounds bb = default; bool any = false;
                foreach (var rr in car.GetComponentsInChildren<Renderer>()) { if (!rr.enabled) continue; if (!any) { bb = rr.bounds; any = true; } else bb.Encapsulate(rr.bounds); }
                dims = any ? Mathf.Max(bb.size.x, bb.size.z).ToString("0.00") + " × " + Mathf.Min(bb.size.x, bb.size.z).ToString("0.00") + " × " + bb.size.y.ToString("0.00") + " m" : "";
            }
            if (dims != "") mdl = mdl + "   ·   " + dims;
            var r = new Rect(Screen.width * 0.45f, 34 * sc, Screen.width * 0.52f, 56 * sc);   // 移到右上角:原本在右下,和選車卡片條/說明列互相壓住
            GUI.color = new Color(0, 0, 0, 0.5f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), team, nameSt); GUI.color = Color.white; GUI.Label(r, team, nameSt);
            GUI.color = new Color(1, 1, 1, 0.75f); GUI.Label(new Rect(r.x, r.y + 56 * sc, r.width, 24 * sc), mdl + "   ·   " + Loc.T("hint.drag"), subSt); GUI.color = Color.white;
        }

        void Update()
        {
            if (Want && Input.GetMouseButton(0) && Input.mousePosition.x > Screen.width * 0.42f) DragDeg -= Input.GetAxis("Mouse X") * 4f;
            if (Want) Dist = Mathf.Clamp(Dist - Input.mouseScrollDelta.y * 0.25f, 1.8f, 4.5f);
            var rig = FindFirstObjectByType<CameraRig>();
            bool menuMode = rig != null && rig.Orbit && GameSession.Drive == null;
            if (!menuMode || !Want) { Clear(); return; }
            checkT -= Time.unscaledDeltaTime;
            if (checkT <= 0f)
            {
                checkT = 0.25f;
                string want = RobotModels.Selected; if (string.IsNullOrEmpty(want)) want = "proc:0.72,0.74,0.55,#28B0FF";
                if (want != shown) Spawn(want);
            }
            if (car != null)
            {
                enterT += Time.unscaledDeltaTime;
                if (enterT > 0.15f && enterT < 1.3f && Input.anyKeyDown) enterT = 1.3f;   // 任何按鍵/點擊直接跳過進場動畫(換車要連續按時不用等)
                float k = Mathf.Clamp01(Mathf.Max(enterT, 0f) / 1.3f), ease = 1f - Mathf.Pow(1f - k, 3f);
                ease += 0.035f * Mathf.Sin(k * Mathf.PI) * (1f - k);   // 到位前微微衝過頭再回彈(煞車感)
                Vector3 start = restPos + new Vector3(-9f, 0f, 1.5f);
                car.transform.position = Vector3.Lerp(start, restPos, ease);
                car.transform.rotation = Quaternion.Euler(0f, Mathf.Lerp(18f, 0f, ease), 0f);   // 滑進來時車頭略偏,到位後轉正
            }
        }

        void Spawn(string file)
        {
            Clear(); shown = file; token++;
            int my = token;
            restPos = Center + Vector3.up * 0.13f;
            car = new GameObject("PreviewCar"); car.transform.position = restPos + new Vector3(-9f, 0f, 1.5f);
            enterT = -100f;   // 模型載入完才開始開進場(否則載入要 1 秒以上,動畫在看不到的地方跑完,車直接「蹦」出來)
            float yaw = (file.StartsWith("proc:")) ? 0f : ModelLibrary.Is(file) ? ModelLibrary.Yaw(file) : (file == "kepler.glb") ? 90f : PlayerPrefs.GetFloat("modelYaw", 0f);
            RobotModels.Attach(car.transform, file, yaw, ok => { if (my == token && car != null) enterT = 0f; }, false);
        }

        void Clear() { token++; if (car != null) Destroy(car); car = null; shown = "\u0001"; }
        void OnDestroy() { if (I == this) I = null; }
    }
}

