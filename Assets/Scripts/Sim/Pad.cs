using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FrcSim
{
    // 直接讀 Windows XInput(Xbox 手把),不靠 Unity 舊版 Input 的搖桿對應(那個對很多手把沒反應)。
    // 支援兩支手把:第一支(插槽編號較小)= 駕駛 Pad;第二支 = 操作手 Pad2。只接一支時,Pad2 與 Pad 共用同一支。
    // 沒接手把或讀取失敗就全部回 0/false。
    public class PadUnit
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct XState
        {
            public uint PacketNumber;
            public ushort Buttons;
            public byte LT, RT;
            public short LX, LY, RX, RY;
        }

        public XState Cur, Prev;
        public bool Connected;

        static float Norm(short v, float dead)
        {
            float f = Mathf.Clamp(v / 32767f, -1f, 1f);
            if (Mathf.Abs(f) < dead) return 0f;
            return Mathf.Sign(f) * (Mathf.Abs(f) - dead) / (1f - dead);
        }

        public float LX => Norm(Cur.LX, 0.2f);
        public float LY => Norm(Cur.LY, 0.2f);   // 上 = +1
        public float RX => Norm(Cur.RX, 0.2f);
        public float RY => Norm(Cur.RY, 0.2f);
        public float LT => Cur.LT / 255f > 0.1f ? Cur.LT / 255f : 0f;
        public float RT => Cur.RT / 255f > 0.1f ? Cur.RT / 255f : 0f;
        public bool Held(int mask) => (Cur.Buttons & mask) != 0;
        public bool Down(int mask) => (Cur.Buttons & mask) != 0 && (Prev.Buttons & mask) == 0;
    }

    // 駕駛手把(第一支)
    public static class Pad
    {
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint GetState14(uint user, out PadUnit.XState s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint GetState91(uint user, out PadUnit.XState s);

        public const int A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, LB = 0x0100, RB = 0x0200,
                         Back = 0x0020, Start = 0x0010, DUp = 0x0001, DDown = 0x0002, DLeft = 0x0004, DRight = 0x0008;

        internal static readonly PadUnit[] Slots = { new PadUnit(), new PadUnit(), new PadUnit(), new PadUnit() };
        static readonly PadUnit none = new PadUnit();
        internal static PadUnit Drv = none, Opr = none;
        static int frame = -1;
        static bool broken14, broken91;
        static bool fakeOn; static PadUnit.XState fake;
        public static int Count { get; private set; }   // 目前接了幾支(0~4)
        public static bool Connected => Count > 0;

        // ---- 手把模式與指派(開始前由使用者選):0 未選(舊行為:自動依序)、1 單手把(同一支當駕駛+操作手)、2 雙手把(駕駛/操作手各一支,依指派)
        public static int Mode { get => PlayerPrefs.GetInt("padMode", 0); set { PlayerPrefs.SetInt("padMode", value); PlayerPrefs.Save(); } }
        public static bool OperatorKeyboard { get => PlayerPrefs.GetInt("padOprKb", 0) == 1; set { PlayerPrefs.SetInt("padOprKb", value ? 1 : 0); PlayerPrefs.Save(); } }   // 雙手把模式下,操作手改用鍵盤(其中一支手把壞了/沒接時)
        public static int DriverSlot { get => PlayerPrefs.GetInt("padDrv", -1); set { PlayerPrefs.SetInt("padDrv", value); PlayerPrefs.Save(); } }
        public static int OperatorSlot { get => PlayerPrefs.GetInt("padOpr", -1); set { PlayerPrefs.SetInt("padOpr", value); PlayerPrefs.Save(); } }
        public static bool DriverOnline { get { Poll(); return Drv.Connected; } }
        public static bool OperatorOnline { get { Poll(); return Opr.Connected && Opr != Drv; } }
        public static int DriverIndex { get { Poll(); return System.Array.IndexOf(Slots, Drv); } }
        public static int OperatorIndex { get { Poll(); return Opr == none ? -1 : System.Array.IndexOf(Slots, Opr); } }
        // 指派流程:1 = 等駕駛手把按 A,2 = 等操作手把按 A,0 = 沒在指派
        public static int AssignStage;
        public static void BeginAssign() { AssignStage = 1; }

        // 測試用:假裝手把輸入(自動測試用,真實遊戲不會呼叫);假輸入只當作一支手把
        public static void SetFake(bool on, float lx = 0, float ly = 0, float rx = 0, float ry = 0, float lt = 0, float rt = 0, int buttons = 0)
        {
            fakeOn = on;
            fake = new PadUnit.XState { LX = (short)(lx * 32767), LY = (short)(ly * 32767), RX = (short)(rx * 32767), RY = (short)(ry * 32767), LT = (byte)(lt * 255), RT = (byte)(rt * 255), Buttons = (ushort)buttons };
        }
        public static string Raw() { Poll(); var c = Drv.Cur; return $"pads={Count} LX={c.LX} LY={c.LY} RX={c.RX} RY={c.RY} LT={c.LT} RT={c.RT} btn={c.Buttons}"; }

        internal static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            foreach (var u in Slots) { u.Prev = u.Cur; u.Connected = false; u.Cur = default; }
            Count = 0;
            if (fakeOn) { Slots[0].Cur = fake; Slots[0].Connected = true; Count = 1; Drv = Opr = Slots[0]; return; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            for (uint slot = 0; slot < 4; slot++)
            {
                PadUnit.XState s; uint r = 1167;
                try
                {
                    if (!broken14) r = GetState14(slot, out s);
                    else if (!broken91) r = GetState91(slot, out s);
                    else break;
                }
                catch (DllNotFoundException) { if (!broken14) broken14 = true; else broken91 = true; continue; }
                catch (EntryPointNotFoundException) { if (!broken14) broken14 = true; else broken91 = true; continue; }
                if (r == 0) { var u = Slots[slot]; u.Cur = s; u.Connected = true; Count++; }
            }
#endif
            // 指派:按 A 的那支就是駕駛(第一步)/操作手(第二步)
            if (AssignStage > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (!Slots[i].Down(A)) continue;
                    if (AssignStage == 1) { DriverSlot = i; AssignStage = 2; }
                    else if (i != DriverSlot) { OperatorSlot = i; AssignStage = 0; }
                    break;
                }
            }
            Resolve();
        }

        static void Resolve()
        {
            int first = -1, second = -1;
            for (int i = 0; i < 4; i++) if (Slots[i].Connected) { if (first < 0) first = i; else if (second < 0) second = i; }
            int mode = Mode;
            bool dual = mode == 2 || (mode == 0 && Count > 1);
            int d = DriverSlot;
            if (!dual) { Drv = Opr = first >= 0 ? Slots[first] : none; return; }
            if (mode == 0) { Drv = Slots[first]; Opr = Slots[second]; return; }   // 舊行為:依序
            if (d < 0 || d > 3 || !Slots[d].Connected) d = -1;
            int o = OperatorSlot;
            if (o < 0 || o > 3 || !Slots[o].Connected || o == d) o = -1;
            // 沒指派過 / 指派的那支掉線:用剩下連線的補位(但絕不讓駕駛與操作手是同一支)
            if (d < 0) for (int i = 0; i < 4; i++) if (Slots[i].Connected && i != o) { d = i; break; }
            if (o < 0) for (int i = 0; i < 4; i++) if (Slots[i].Connected && i != d) { o = i; break; }
            Drv = d >= 0 ? Slots[d] : none;
            Opr = o >= 0 ? Slots[o] : none;   // 雙手把模式缺一支時,操作手功能停用並由 HUD 警告(不偷用駕駛那支)
        }
        static PadUnit U { get { Poll(); return Drv; } }
        public static float LX => U.LX;
        public static float LY => U.LY;
        public static float RX => U.RX;
        public static float RY => U.RY;
        public static float LT => U.LT;
        public static float RT => U.RT;
        public static bool Held(int mask) => U.Held(mask);
        public static bool Down(int mask) => U.Down(mask);
        // 任一支手把按下(暫停等共用功能)
        public static bool AnyDown(int mask) { Poll(); foreach (var u in Slots) if (u.Down(mask)) return true; return false; }
    }

    // 操作手把(第二支;只接一支時與 Pad 相同)
    public static class Pad2
    {
        static PadUnit U { get { Pad.Poll(); return Pad.Opr; } }
        public static bool Separate { get { Pad.Poll(); return Pad.Opr != Pad.Drv; } }
        public static float LX => U.LX;
        public static float LY => U.LY;
        public static float RX => U.RX;
        public static float RY => U.RY;
        public static float LT => U.LT;
        public static float RT => U.RT;
        public static bool Held(int mask) => U.Held(mask);
        public static bool Down(int mask) => U.Down(mask);
    }
}
