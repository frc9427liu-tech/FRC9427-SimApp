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

        void Update()
        {
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
            float yaw = (file.StartsWith("proc:")) ? 0f : (file == "kepler.glb" || ModelLibrary.Is(file)) ? 90f : PlayerPrefs.GetFloat("modelYaw", 0f);
            RobotModels.Attach(car.transform, file, yaw, ok => { if (my != token && car != null) { } }, false);
        }

        void Clear() { token++; if (car != null) Destroy(car); car = null; shown = "\u0001"; }
        void OnDestroy() { if (I == this) I = null; }
    }
}