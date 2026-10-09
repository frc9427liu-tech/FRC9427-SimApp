using UnityEngine;

namespace FrcSim
{
    // 鍵盤:W/S 前後、A/D 左右、Q/E 旋轉、F 切換場地/車頭座標、R 重置、Shift 慢速
    // 手把:左搖桿平移(Unity 預設 Horizontal/Vertical 軸)、LB/RB 旋轉
    public class RobotInput : MonoBehaviour
    {
        public SwerveDrive Drive;
        public RobotMechanisms Mech;
        bool slowToggle;
        bool intakeLatch;   // 真實程式模式:I/A 切換「放下吸球」(等同按住 LT)

        // 手把右搖桿/扳機軸(由打包時補進 InputManager);沒定義就當 0,不會丟例外
        static float PadAxis(string name) { try { return Input.GetAxisRaw(name); } catch (System.Exception) { return 0f; } }

        static float TankShape(float v) { v = Mathf.Abs(v) < 0.08f ? 0f : v; return Mathf.Sign(v) * v * v; }
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

            // LEO 坦克模式:左/右搖桿各控一側輪(死區 0.08 + 平方,照 LEO DriveSubsystem.tankDrive)
            bool tank = PlayerPrefs.GetInt("tankMode", 1) == 1;
            float tankL = 0f, tankR = 0f, rawL = 0f, rawR = 0f;
            if (tank)
            {
                float kbF = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                float kbR = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                rawL = Mathf.Clamp(Pad.LY + kbF + kbR, -1f, 1f);
                rawR = Mathf.Clamp(Pad.RY + kbF - kbR, -1f, 1f);
                tankL = TankShape(rawL);
                tankR = TankShape(rawR);
                Drive.FieldCentric = false;   // 坦克:往前 = 車頭(intake)方向,不是場地方向
                fwd = (tankL + tankR) * 0.5f; strafeRight = 0f; rot = (tankR - tankL) * 0.5f;
                if (Input.GetKey(KeyCode.Q)) rot += 1f; if (Input.GetKey(KeyCode.E)) rot -= 1f;
                rot = Mathf.Clamp(rot, -1f, 1f);
            }

            bool halLive = GameSession.Hal != null && GameSession.Hal.Connected;   // 真實程式還沒連上時,先用內建操控,連上後才交給程式
            if (Input.GetKeyDown(KeyCode.T)) slowToggle = !slowToggle;   // 同時按太多鍵不方便:T 可切換慢速(Shift 仍然是按住慢速)
            float scale = (Input.GetKey(KeyCode.LeftShift) || slowToggle) ? 0.35f : 1f;
            if (halLive && tank)
            {
                // LEO 真實程式:一支實體手把同時當「駕駛(device 0)」與「操作手(device 1)」,照 LEO RobotContainer 的綁定
                //  駕駛:左/右搖桿(原始值,上 = 負)= 左/右側輪,A 按住 = 放下 intake,B 按住 = 滾輪收球
                //  操作手:十字鍵左右 = 砲塔(axis0),RT = 發射,RB = 送球(Orbit),X = 吐球,十字鍵上 = 收起手臂
                var h = GameSession.Hal;
                bool kA = Input.GetKey(KeyCode.I), kB = Input.GetKey(KeyCode.O), kUp = Input.GetKey(KeyCode.U);
                for (int i = 0; i < 6; i++) { h.Axes[i] = 0f; h.Axes2[i] = 0f; }
                for (int i = 0; i < h.Buttons.Length; i++) { h.Buttons[i] = false; h.Buttons2[i] = false; }
                h.Axes[1] = -rawL; h.Axes[5] = -rawR;
                h.Buttons[0] = Pad.Held(Pad.A) || kA;
                h.Buttons[1] = Pad.Held(Pad.B) || kB;
                float turret = (Pad2.Held(Pad.DRight) || Input.GetKey(KeyCode.X) ? 1f : 0f) - (Pad2.Held(Pad.DLeft) || Input.GetKey(KeyCode.Z) ? 1f : 0f);
                h.Axes2[0] = turret;
                h.Axes2[3] = Mathf.Max((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) ? 1f : 0f, Pad2.RT);
                h.Axes2[2] = Pad2.LT;
                h.Buttons2[5] = Pad2.Held(Pad.RB) || Input.GetKey(KeyCode.V);
                h.Buttons2[2] = Pad2.Held(Pad.X);
                h.Pov2 = (Pad2.Held(Pad.DUp) || kUp) ? 0 : -1;
            }
            else if (halLive)
            {
                // 真實機器人程式:鍵盤/手把變成虛擬 Xbox 搖桿送進 HALSim(axis0=LX 右為正,axis1=LY 上為負,axis4=RX 右為正)
                var h = GameSession.Hal;
                if (tank) { h.Axes[1] = -Mathf.Clamp(Pad.LY, -1f, 1f); h.Axes[5] = -Mathf.Clamp(Pad.RY, -1f, 1f); }
                h.Axes[0] = strafeRight * scale; h.Axes[1] = -fwd * scale; h.Axes[4] = Mathf.Clamp(-rot * scale + Deadband(PadAxis("PadRX") + Pad.RX), -1f, 1f);
                for (int i = 0; i < h.Buttons.Length && i < 10; i++) h.Buttons[i] = Input.GetKey(KeyCode.JoystickButton0 + i) || (i == 0 && Pad.Held(Pad.A)) || (i == 1 && Pad.Held(Pad.B)) || (i == 2 && Pad.Held(Pad.X)) || (i == 3 && Pad.Held(Pad.Y)) || (i == 4 && Pad.Held(Pad.LB)) || (i == 5 && Pad.Held(Pad.RB)) || (i == 6 && Pad.Held(Pad.Back)) || (i == 7 && Pad.Held(Pad.Start));
                // 這份程式:LT(axis2)按住=放下 intake 吸球,RT(axis3)按住=射擊(RobotContainer.java 的綁定)
                // I / 手把 A 與內建模式一致:按一下放下、再按一下收起(用鎖存模擬「按住 LT」)
                if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.JoystickButton0) || Pad.Down(Pad.A)) intakeLatch = !intakeLatch;
                h.Axes[2] = Mathf.Max(intakeLatch ? 1f : 0f, Mathf.Clamp01(Mathf.Max(PadAxis("PadLT"), Pad.LT)));
                h.Axes[3] = Mathf.Max((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) ? 1f : 0f, Mathf.Clamp01(Mathf.Max(PadAxis("PadRT"), Pad.RT)));
            }
            else Drive.Drive(fwd * scale, -strafeRight * scale, rot * scale);

            if (Mech != null && !(halLive && tank))
            {
                if (tank) { if (Input.GetKeyDown(KeyCode.I)) intakeLatch = !intakeLatch; Mech.IntakeDown = intakeLatch || Pad.Held(Pad.A); }
                else if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.JoystickButton0) || Pad.Down(Pad.A)) Mech.IntakeDown = !Mech.IntakeDown;
                Mech.Shooting = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0) || Pad2.RT > 0.5f || (!tank && (Input.GetKey(KeyCode.JoystickButton1) || Pad.Held(Pad.B)));
            }

            if (Input.GetKeyDown(KeyCode.F)) Drive.FieldCentric = !Drive.FieldCentric;
            if (Input.GetKeyDown(KeyCode.R)) Drive.ResetPose();   // 手把 Start/Back 是暫停,重置位置請用 R 或暫停選單
        }
    }
}
