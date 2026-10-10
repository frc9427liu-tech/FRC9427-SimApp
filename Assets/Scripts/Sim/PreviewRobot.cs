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

        public static float DragDeg;   // 滑鼠左鍵在右側空白處拖曳 = 轉動檢視角度
        GUIStyle nameSt, subSt;
        void OnGUI()
        {
            if (!Active || Event.current.type != EventType.Repaint) return;
            if (nameSt == null) { nameSt = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight }; subSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight }; }
            float sc = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.8f); nameSt.fontSize = Mathf.RoundToInt(34 * sc); subSt.fontSize = Mathf.RoundToInt(16 * sc);
            string proj = Prefs.GetString("robotProject", "");
            string team = string.IsNullOrEmpty(proj) || Prefs.GetInt("useRealCode", 0) != 1 ? Loc.T("model.builtin") : System.IO.Path.GetFileName(proj.TrimEnd('\\', '/'));
            string mdl = string.IsNullOrEmpty(RobotModels.Selected) ? "" : RobotModels.Display(RobotModels.Selected);
            var r = new Rect(Screen.width * 0.45f, Screen.height - 130 * sc, Screen.width * 0.52f, 56 * sc);
            GUI.color = new Color(0, 0, 0, 0.5f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), team, nameSt); GUI.color = Color.white; GUI.Label(r, team, nameSt);
            GUI.color = new Color(1, 1, 1, 0.75f); GUI.Label(new Rect(r.x, r.y + 40 * sc, r.width, 24 * sc), mdl + "   ·   " + Loc.T("hint.drag"), subSt); GUI.color = Color.white;
        }

        void Update()
        {
            if (Want && Input.GetMouseButton(0) && Input.mousePosition.x > Screen.width * 0.42f) DragDeg -= Input.GetAxis("Mouse X") * 4f;
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
                float k = Mathf.Clamp01(enterT / 1.15f), ease = 1f - Mathf.Pow(1f - k, 3f);
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
            enterT = 0f;
            float yaw = (file.StartsWith("proc:")) ? 0f : ModelLibrary.Is(file) ? ModelLibrary.Yaw(file) : (file == "kepler.glb") ? 90f : PlayerPrefs.GetFloat("modelYaw", 0f);
            RobotModels.Attach(car.transform, file, yaw, ok => { if (my != token && car != null) { } }, false);
        }

        void Clear() { token++; if (car != null) Destroy(car); car = null; shown = "\u0001"; }
        void OnDestroy() { if (I == this) I = null; }
    }
}