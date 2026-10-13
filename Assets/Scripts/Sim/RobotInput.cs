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
        float slewL, slewR;
        bool intakeLatch;   // 真實程式模式:I/A 切換「放下吸球」(等同按住 LT)

        // 手把右搖桿/扳機軸(由打包時補進 InputManager);沒定義就當 0,不會丟例外
        static float PadAxis(string name) { try { return Input.GetAxisRaw(name); } catch (System.Exception) { return 0f; } }

        static float TankShape(float v) { v = Mathf.Abs(v) < 0.08f ? 0f : v; return Mathf.Sign(v) * v * v; }
        public static bool FeelEasy => Prefs.GetInt("feelEasy", 0) == 1;   // 設定:手感模式 0=忠實(照真程式) 1=順手
        static float FeelComp(float v) { if (!FeelEasy) return v; float a = Mathf.Abs(v); return a < 0.08f ? 0f : Mathf.Sign(v) * Mathf.Sqrt(a); }
        static float SoftShape(float v) { float a = Mathf.Abs(v); if (a < 0.08f) return 0f; return Mathf.Sign(v) * Mathf.Pow(Mathf.Min(1f, (a - 0.08f) / 0.92f), 1.3f); }
        static float Deadband(float v, float d = 0.08f) => Mathf.Abs(v) < d ? 0f : v;

        static float StickCurve(float v) { return Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), 1.6f); }   // 搖桿輸出曲線:小幅度更細膩、推到底仍是 1

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
            float kbF0 = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f), kbH0 = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);   // 鍵盤只讀 WASD:Unity 舊版 Horizontal/Vertical 軸也會讀手把左搖桿,和 Pad.LX/LY 疊加會讓搖桿靈敏度變兩倍(手感怪)
            // 左搖桿:圓形死區(0.10)+ 重新縮放 + 對「向量長度」套曲線,斜推不會被各軸壓扁、也不會有一大塊死區後突然跳速
            Vector2 ls = new Vector2(Pad.LX, Pad.LY);
            float lm = ls.magnitude;
            if (lm < 0.10f) ls = Vector2.zero;
            else { float m = Mathf.Min(1f, (lm - 0.10f) / 0.88f); ls = ls / lm * Mathf.Pow(m, 1.3f); }
            float fwd = Mathf.Clamp(kbF0 + ls.y, -1f, 1f);        // W/上 = +1,手把左搖桿上 = +1
            float strafeRight = Mathf.Clamp(kbH0 + ls.x, -1f, 1f);
            fwd = Deadband(fwd, 0.02f);
            strafeRight = Deadband(strafeRight, 0.02f);

            float rot = 0f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.JoystickButton4) || Pad.Held(Pad.LB)) rot += 1f; // 逆時針
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.JoystickButton5) || Pad.Held(Pad.RB)) rot -= 1f; // 順時針
            if (Prefs.GetInt("tankMode", 0) != 1 && !(GameSession.Hal != null && GameSession.Hal.Connected)) rot = Mathf.Clamp(rot - Deadband(Pad.RX), -1f, 1f);   // 標準操控:右搖桿左右 = 轉向

            // LEO 坦克模式:左/右搖桿各控一側輪(死區 0.08 + 平方,照 LEO DriveSubsystem.tankDrive)
            bool tank = Prefs.GetInt("tankMode", 0) == 1;
            float tankL = 0f, tankR = 0f, rawL = 0f, rawR = 0f;
            if (tank)
            {
                float kbF = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                float kbR = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                if (Prefs.GetInt("easyTank", 1) == 1)
                {
                    // 簡易操控(預設):左搖桿 = 移動(上下=前後、左右=轉彎),右搖桿左右也能轉;再換算成左右輪,真實程式拿到的仍是原本的坦克軸
                    float turn = Mathf.Clamp(Pad.LX + Pad.RX + kbR, -1f, 1f);
                    float drv = Mathf.Clamp(Pad.LY + kbF, -1f, 1f);
                    float sc = Mathf.Max(1f, Mathf.Abs(drv) + Mathf.Abs(turn));   // 全推前+全推轉時不超過 1,保持比例
                    rawL = (drv + turn) / sc; rawR = (drv - turn) / sc;
                }
                else
                {
                    rawL = Mathf.Clamp(Pad.LY + kbF + kbR, -1f, 1f);
                    rawR = Mathf.Clamp(Pad.RY + kbF - kbR, -1f, 1f);
                }
                // 內建操控(沒跑真實程式):溫和曲線(死區 0.08 後重新縮放、^1.3),起步立刻有速度;不再平方(平方會讓推一半只剩 25% 速度,使用者回報「啟動超慢」)
                bool realLive = GameSession.Hal != null && GameSession.Hal.Connected;
                tankL = realLive ? TankShape(rawL) : SoftShape(rawL);
                tankR = realLive ? TankShape(rawR) : SoftShape(rawR);
                // 內建模式的加速度限制:跑真實程式時照 LEO(kSlewRatePerSec = 4/秒);純內建操控放寬到 10/秒(0.1 秒到全速)
                float slewStep = (realLive ? 4f : 10f) * Time.deltaTime;
                slewL = Mathf.MoveTowards(slewL, tankL, slewStep); slewR = Mathf.MoveTowards(slewR, tankR, slewStep);
                tankL = slewL; tankR = slewR;
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
                h.Axes[1] = -FeelComp(rawL); h.Axes[5] = -FeelComp(rawR);   // 手感模式「順手」:LEO 程式會把搖桿平方,這裡先開根號預補償,推一半就是約一半速度;「忠實」原樣送出
                bool dual = Pad2.Separate;
                bool kBrt = kB && BindingMap.IntakeOnDriverRT, kBbtn = kB && !BindingMap.IntakeOnDriverRT;
                if (dual)
                {
                    // 兩支手把:跟真的 Driver Station 一樣,實體按鍵原樣送進程式(device 0 = 駕駛、device 1 = 操作手),程式怎麼綁就怎麼動
                    h.Axes[0] = Pad.LX; h.Axes[2] = Pad.LT; h.Axes[3] = Mathf.Max(Pad.RT, kBrt ? 1f : 0f); h.Axes[4] = Pad.RX;
                    h.Buttons[0] = Pad.Held(Pad.A) || kA; h.Buttons[1] = Pad.Held(Pad.B) || kBbtn; h.Buttons[2] = Pad.Held(Pad.X); h.Buttons[3] = Pad.Held(Pad.Y);
                    h.Buttons[4] = Pad.Held(Pad.LB); h.Buttons[5] = Pad.Held(Pad.RB); h.Buttons[6] = Pad.Held(Pad.Back); h.Buttons[7] = Pad.Held(Pad.Start);
                }
                else
                {
                    // 單手把:同一支當駕駛+操作手,RT 要留給發射,所以「收球」改用 B(綁在 RT 或 B 由專案的 RobotContainer 決定)
                    h.Buttons[0] = Pad.Held(Pad.A) || kA;
                    bool bHeld = Pad.Held(Pad.B) || kB;
                    if (BindingMap.IntakeOnDriverRT) h.Axes[3] = bHeld ? 1f : 0f; else h.Buttons[1] = bHeld;
                }
                float turret = (Pad2.Held(Pad.DRight) || Input.GetKey(KeyCode.X) ? 1f : 0f) - (Pad2.Held(Pad.DLeft) || Input.GetKey(KeyCode.Z) ? 1f : 0f);
                if (dual) turret = Mathf.Clamp(turret + Pad2.LX, -1f, 1f);   // 兩支手把:操作手左搖桿左右 = 砲塔
                h.Axes2[0] = turret;
                h.Axes2[3] = Mathf.Max((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) ? 1f : 0f, Pad2.RT);
                h.Axes2[2] = Pad2.LT;
                if (dual) { h.Axes2[1] = -Pad2.LY; h.Axes2[4] = Pad2.RX; h.Axes2[5] = -Pad2.RY; h.Buttons2[0] = Pad2.Held(Pad.A); h.Buttons2[1] = Pad2.Held(Pad.B); h.Buttons2[3] = Pad2.Held(Pad.Y); h.Buttons2[4] = Pad2.Held(Pad.LB); h.Buttons2[6] = Pad2.Held(Pad.Back); h.Buttons2[7] = Pad2.Held(Pad.Start); }
                h.Buttons2[5] = Pad2.Held(Pad.RB) || Input.GetKey(KeyCode.V);
                h.Buttons2[2] = Pad2.Held(Pad.X) || Input.GetKey(KeyCode.C);
                h.Pov2 = (Pad2.Held(Pad.DUp) || kUp) ? 0 : (dual && Pad2.Held(Pad.DRight)) ? 90 : (dual && Pad2.Held(Pad.DDown)) ? 180 : (dual && Pad2.Held(Pad.DLeft)) ? 270 : -1;
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

            // 自動階段(AUTO)手把無效:真實比賽裡機器人程式自己跑。DS 其實照樣送搖桿值,而指令式程式在「沒有自動指令佔用底盤」的空檔(等待/只射擊)
            // 預設的坦克駕駛指令會吃到搖桿,看起來就像「自動中還能開」——所以 AUTO 期間直接把虛擬搖桿歸零(按 N 可跳過 AUTO)
            if (halLive && GameSession.Hal != null && GameSession.Hal.Autonomous)
            {
                var ha = GameSession.Hal;
                for (int q = 0; q < 6; q++) { ha.Axes[q] = 0f; ha.Axes2[q] = 0f; }
                for (int q = 0; q < ha.Buttons.Length; q++) { ha.Buttons[q] = false; ha.Buttons2[q] = false; }
                ha.Pov2 = -1;
            }

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




