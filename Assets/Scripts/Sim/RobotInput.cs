using UnityEngine;

namespace FrcSim
{
    // 鍵盤:W/S 前後、A/D 左右、Q/E 旋轉、F 切換場地/車頭座標、R 重置、Shift 慢速
    // 手把:左搖桿平移(Unity 預設 Horizontal/Vertical 軸)、LB/RB 旋轉
    public class RobotInput : MonoBehaviour
    {
        public SwerveDrive Drive;
        public RobotMechanisms Mech;
        bool intakeLatch;   // 真實程式模式:I/A 切換「放下吸球」(等同按住 LT)

        // 手把右搖桿/扳機軸(由打包時補進 InputManager);沒定義就當 0,不會丟例外
        static float PadAxis(string name) { try { return Input.GetAxisRaw(name); } catch (System.Exception) { return 0f; } }

        static float Deadband(float v, float d = 0.08f) => Mathf.Abs(v) < d ? 0f : v;

        void Update()
        {
            if (Drive == null) return;
            if (MenuSystem.Blocking)
            {
                // 選單開著:停下來,不吃遊戲按鍵
                Drive.Drive(0f, 0f, 0f);
                if (Mech != null) Mech.Shooting = false;
                return;
            }

            // 第二台機器人用方向鍵,所以這裡要扣掉方向鍵對 Horizontal/Vertical 軸的貢獻(WASD 與手把仍有效)
            float arrowV = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            float arrowH = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float fwd = Mathf.Clamp(Input.GetAxisRaw("Vertical") - arrowV + Pad.LY, -1f, 1f);        // W/上 = +1,手把左搖桿上 = +1
            float strafeRight = Mathf.Clamp(Input.GetAxisRaw("Horizontal") - arrowH + Pad.LX, -1f, 1f);
            fwd = Deadband(fwd);
            strafeRight = Deadband(strafeRight);

            float rot = 0f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.JoystickButton4) || Pad.Held(Pad.LB)) rot += 1f; // 逆時針
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.JoystickButton5) || Pad.Held(Pad.RB)) rot -= 1f; // 順時針

            float scale = Input.GetKey(KeyCode.LeftShift) ? 0.35f : 1f;
            if (GameSession.Hal != null)
            {
                // 真實機器人程式:鍵盤/手把變成虛擬 Xbox 搖桿送進 HALSim(axis0=LX 右為正,axis1=LY 上為負,axis4=RX 右為正)
                var h = GameSession.Hal;
                h.Axes[0] = strafeRight * scale; h.Axes[1] = -fwd * scale; h.Axes[4] = Mathf.Clamp(-rot * scale + Deadband(PadAxis("PadRX") + Pad.RX), -1f, 1f);
                for (int i = 0; i < h.Buttons.Length && i < 10; i++) h.Buttons[i] = Input.GetKey(KeyCode.JoystickButton0 + i) || (i == 0 && Pad.Held(Pad.A)) || (i == 1 && Pad.Held(Pad.B)) || (i == 2 && Pad.Held(Pad.X)) || (i == 3 && Pad.Held(Pad.Y)) || (i == 4 && Pad.Held(Pad.LB)) || (i == 5 && Pad.Held(Pad.RB)) || (i == 6 && Pad.Held(Pad.Back)) || (i == 7 && Pad.Held(Pad.Start));
                // 這份程式:LT(axis2)按住=放下 intake 吸球,RT(axis3)按住=射擊(RobotContainer.java 的綁定)
                // I / 手把 A 與內建模式一致:按一下放下、再按一下收起(用鎖存模擬「按住 LT」)
                if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.JoystickButton0) || Pad.Down(Pad.A)) intakeLatch = !intakeLatch;
                h.Axes[2] = Mathf.Max(intakeLatch ? 1f : 0f, Mathf.Clamp01(Mathf.Max(PadAxis("PadLT"), Pad.LT)));
                h.Axes[3] = Mathf.Max((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) ? 1f : 0f, Mathf.Clamp01(Mathf.Max(PadAxis("PadRT"), Pad.RT)));
            }
            else Drive.Drive(fwd * scale, -strafeRight * scale, rot * scale);

            if (Mech != null)
            {
                if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.JoystickButton0) || Pad.Down(Pad.A)) Mech.IntakeDown = !Mech.IntakeDown;
                Mech.Shooting = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton1) || Pad.Held(Pad.B) || Pad.RT > 0.3f;
            }

            if (Input.GetKeyDown(KeyCode.F)) Drive.FieldCentric = !Drive.FieldCentric;
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton7) || Pad.Down(Pad.Start)) Drive.ResetPose();
        }
    }
}
