using UnityEngine;

namespace FrcSim
{
    /// 玩家輸入(模組 K,01 §3):Pad(Xbox 手把)+ 鍵盤,取最大值合併、不相加。
    /// 只在 Update 讀(RaceKart.Update 呼叫);不吃 Start/Back(留給暫停);MenuSystem.Blocking 時全 0。
    /// player 0 = Pad + 鍵盤 1P(WASD + 方向鍵);player 1 = Pad2(Pad2.Separate 時)+ 鍵盤 2P(方向鍵、RightShift、RightCtrl)。
    public sealed class KartInputReader : IKartInput
    {
        const float Dead = 0.12f;
        const float RespawnHoldSec = 1f;
        readonly int player;
        bool prevDrift = true;
        float respawnHold; bool respawnSent;

        public KartInputReader(int player = 0) { this.player = player; }

        static float Pick(float a, float b) { return Mathf.Abs(a) >= Mathf.Abs(b) ? a : b; }

        // 道具瞄準 + 交換:右搖桿(推出 > 0.5 才算)/ 鍵盤 IJKL(1P)決定丟射角度,0 = 車頭;交換 = 十字鍵上 / F(1P)/ 小鍵盤 0(2P)。
        // 沒有瞄準輸入時 AimActive = false,仍走 AimBack(左搖桿下 / S)的舊行為。
        static void ReadItemAim(ref KartInput k, bool p1, bool padOn)
        {
            float rx = 0f, ry = 0f; bool swap = false;
            if (padOn)
            {
                if (p1) { rx = Pad.RX; ry = Pad.RY; swap = Pad.Held(Pad.DUp); }
                else { rx = Pad2.RX; ry = Pad2.RY; swap = Pad2.Held(Pad.DUp); }
            }
            if (p1)
            {
                float kx = (Input.GetKey(KeyCode.L) ? 1f : 0f) - (Input.GetKey(KeyCode.J) ? 1f : 0f);
                float ky = (Input.GetKey(KeyCode.I) ? 1f : 0f) - (Input.GetKey(KeyCode.K) ? 1f : 0f);
                if (kx != 0f || ky != 0f) { rx = kx; ry = ky; }
                swap |= Input.GetKey(KeyCode.F);
            }
            else swap |= Input.GetKey(KeyCode.Keypad0);
            if (rx * rx + ry * ry > 0.25f)
            {
                k.AimActive = true;
                k.AimAngle = Mathf.Atan2(rx, ry) * Mathf.Rad2Deg;   // 上 = 0、右 = 90、下 = 180
            }
            k.SwapHeld = swap;
        }

        static readonly bool Demo = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-racedemo") >= 0;   // 截圖測試用:油門全開 + 緩慢左右轉,看輪子 / 車手動畫

        public KartInput Read(float dt)
        {
            var k = new KartInput();
            if (Demo && player == 0) { k.Throttle = 1f; k.Steer = Mathf.Sin(Time.time * 1.7f) * 0.35f; return k; }
            if (MenuSystem.Blocking) { prevDrift = true; respawnHold = 0f; respawnSent = false; return k; }

            bool p1 = player == 0;
            bool padOn = p1 || Pad2.Separate;
            float rt = 0f, lt = 0f, lx = 0f, ly = 0f;
            bool btnA = false, btnB = false, btnRB = false, btnY = false, dL = false, dR = false, dDown = false, itemLB = false, itemX = false;
            if (padOn)
            {
                if (p1)
                {
                    rt = Pad.RT; lt = Pad.LT; lx = Pad.LX; ly = Pad.LY;
                    btnA = Pad.Held(Pad.A); btnB = Pad.Held(Pad.B); btnRB = Pad.Held(Pad.RB); btnY = Pad.Held(Pad.Y);
                    dL = Pad.Held(Pad.DLeft); dR = Pad.Held(Pad.DRight); dDown = Pad.Held(Pad.DDown);
                    itemLB = Pad.Down(Pad.LB); itemX = Pad.Down(Pad.X);
                }
                else
                {
                    rt = Pad2.RT; lt = Pad2.LT; lx = Pad2.LX; ly = Pad2.LY;
                    btnA = Pad2.Held(Pad.A); btnB = Pad2.Held(Pad.B); btnRB = Pad2.Held(Pad.RB); btnY = Pad2.Held(Pad.Y);
                    dL = Pad2.Held(Pad.DLeft); dR = Pad2.Held(Pad.DRight); dDown = Pad2.Held(Pad.DDown);
                    itemLB = Pad2.Down(Pad.LB); itemX = Pad2.Down(Pad.X);
                }
            }

            // 搖桿圓形死區(0.12)並重新縮放
            float m = Mathf.Sqrt(lx * lx + ly * ly);
            if (m < Dead) { lx = 0f; ly = 0f; }
            else { float s = Mathf.Min(1f, (m - Dead) / (1f - Dead)) / m; lx *= s; ly *= s; }

            bool kbUp = p1 ? (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) : Input.GetKey(KeyCode.UpArrow);
            bool kbDown = p1 ? (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) : Input.GetKey(KeyCode.DownArrow);
            bool kbLeft = p1 ? (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) : Input.GetKey(KeyCode.LeftArrow);
            bool kbRight = p1 ? (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) : Input.GetKey(KeyCode.RightArrow);
            bool kbDrift = p1 ? (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift)) : Input.GetKey(KeyCode.RightShift);
            bool kbItem = p1 ? Input.GetKeyDown(KeyCode.E) : Input.GetKeyDown(KeyCode.RightControl);
            bool kbLook = p1 && Input.GetKey(KeyCode.Q);
            bool kbRespawn = p1 && Input.GetKey(KeyCode.R);

            k.Throttle = Mathf.Max(rt, btnA ? 1f : 0f, kbUp ? 1f : 0f);
            k.Brake = Mathf.Max(lt, btnB ? 1f : 0f, kbDown ? 1f : 0f);
            float padSteer = Pick(lx, dL ? -1f : dR ? 1f : 0f);
            float kbSteer = (kbRight ? 1f : 0f) - (kbLeft ? 1f : 0f);
            float joyAxis = 0f;   // 方向盤/街機搖桿等其他搖桿裝置:Unity 的 Horizontal 軸(含鍵盤與搖桿 1 軸);只在 1P 讀
            if (p1) { float h = Input.GetAxis("Horizontal"); if (Mathf.Abs(h) > 0.12f) joyAxis = h; }
            k.Steer = Mathf.Clamp(Pick(Pick(padSteer, kbSteer), joyAxis), -1f, 1f);

            bool drift = btnRB || kbDrift;
            k.DriftHeld = drift;
            k.DriftDown = drift && !prevDrift;
            prevDrift = drift;

            k.ItemDown = itemLB || itemX || kbItem;
            k.AimBack = ly < -0.5f || kbDown;
            ReadItemAim(ref k, p1, padOn);   // 兩格道具 / 任意方向瞄準(獨立區塊,2026-10-23)
            k.LookBack = btnY || kbLook;

            // 重生:按住 1 秒才送出一次(手把十字鍵下 / 鍵盤 R)
            if (dDown || kbRespawn)
            {
                respawnHold += dt;
                if (respawnHold >= RespawnHoldSec && !respawnSent) { k.RespawnDown = true; respawnSent = true; }
            }
            else { respawnHold = 0f; respawnSent = false; }
            return k;
        }
    }
}
