using UnityEngine;

namespace FrcSim
{
    // 人類球員補球:官方規則 FUEL 可由人類球員從 OUTPOST 的 CHUTE 送進場內(每個 CHUTE 開賽時有 24 顆,出口離地 71.4cm、約 15° 斜坡滾出)。
    // 按住 H(或操作手手把 Y)= 打開 CHUTE DOOR,每 0.25 秒放出一顆,直到 24 顆放完。藍方 OUTPOST 在 y≈0.88 的聯盟牆,紅方對稱。
    // (只模擬己方「我方」藍方 CHUTE;紅方 CHUTE 給第二台機器人用右 Shift 控制。)
    public class HumanPlayer : MonoBehaviour
    {
        public static int BlueChute = 24, RedChute = 24;
        const int Capacity = 24;
        float nextBlue, nextRed;

        public static void ResetMatch() { BlueChute = RedChute = Capacity; }

        void Awake() { ResetMatch(); }

        void Update()
        {
            if (MenuSystem.Blocking) return;
            bool blue = Input.GetKey(KeyCode.H) || Pad2.Held(Pad.Y);
            bool red = Input.GetKey(KeyCode.RightShift);
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            if (blue && BlueChute > 0 && Time.time >= nextBlue)
            {
                nextBlue = Time.time + 0.25f; BlueChute--;
                Release(new Vector3(0.30f, 0.82f, 0.882f), 1f);
            }
            if (red && RedChute > 0 && Time.time >= nextRed)
            {
                nextRed = Time.time + 0.25f; RedChute--;
                Release(new Vector3(L - 0.30f, 0.82f, W - 0.882f), -1f);
            }
        }

        static void Release(Vector3 pos, float dirX)
        {
            // 從 15° 斜坡滾出來:朝場內約 1.2 m/s、微微向下,橫向有一點散開
            float spread = Random.Range(-0.25f, 0.25f);
            FuelManager.Spawn(pos + new Vector3(0f, 0f, Random.Range(-0.25f, 0.25f)), new Vector3(dirX * Random.Range(0.9f, 1.5f), -0.3f, spread));
        }
    }
}
