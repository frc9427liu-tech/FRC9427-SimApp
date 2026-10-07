using UnityEngine;

namespace FrcSim
{
    // 第二台機器人(紅方,內建行為):方向鍵移動、逗號/句點旋轉、右 Shift 慢速、/ 放下收起 intake、右 Ctrl 射擊(自動瞄準紅方 HUB)
    public class Robot2Input : MonoBehaviour
    {
        public SwerveDrive Drive;
        public RobotMechanisms Mech;

        void Update()
        {
            if (Drive == null) return;
            if (MenuSystem.Blocking) { Drive.Drive(0f, 0f, 0f); if (Mech != null) Mech.Shooting = false; return; }

            float fwd = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            float left = (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f);
            float rot = (Input.GetKey(KeyCode.Comma) ? 1f : 0f) - (Input.GetKey(KeyCode.Period) ? 1f : 0f);
            float scale = Input.GetKey(KeyCode.RightShift) ? 0.35f : 1f;
            Drive.Drive(fwd * scale, left * scale, rot * scale);

            if (Mech != null)
            {
                if (Input.GetKeyDown(KeyCode.Slash)) Mech.IntakeDown = !Mech.IntakeDown;
                Mech.Shooting = Input.GetKey(KeyCode.RightControl);
            }
        }
    }
}
