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

        internal static readonly PadUnit[] Units = { new PadUnit(), new PadUnit() };
        static int frame = -1;
        static bool broken14, broken91;
        static bool fakeOn; static PadUnit.XState fake;
        public static int Count { get; private set; }   // 目前接了幾支(0~2)
        public static bool Connected => Count > 0;

        // 測試用:假裝手把輸入(自動測試用,真實遊戲不會呼叫);假輸入只當作一支手把
        public static void SetFake(bool on, float lx = 0, float ly = 0, float rx = 0, float ry = 0, float lt = 0, float rt = 0, int buttons = 0)
        {
            fakeOn = on;
            fake = new PadUnit.XState { LX = (short)(lx * 32767), LY = (short)(ly * 32767), RX = (short)(rx * 32767), RY = (short)(ry * 32767), LT = (byte)(lt * 255), RT = (byte)(rt * 255), Buttons = (ushort)buttons };
        }
        public static string Raw() { Poll(); var c = Units[0].Cur; return $"pads={Count} LX={c.LX} LY={c.LY} RX={c.RX} RY={c.RY} LT={c.LT} RT={c.RT} btn={c.Buttons}"; }

        internal static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            foreach (var u in Units) { u.Prev = u.Cur; u.Connected = false; u.Cur = default; }
            Count = 0;
            if (fakeOn) { Units[0].Cur = fake; Units[0].Connected = true; Count = 1; return; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            for (uint slot = 0; slot < 4 && Count < 2; slot++)
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
                if (r == 0) { var u = Units[Count++]; u.Cur = s; u.Connected = true; }
            }
#endif
        }

        static PadUnit U { get { Poll(); return Units[0]; } }
        public static float LX => U.LX;
        public static float LY => U.LY;
        public static float RX => U.RX;
        public static float RY => U.RY;
        public static float LT => U.LT;
        public static float RT => U.RT;
        public static bool Held(int mask) => U.Held(mask);
        public static bool Down(int mask) => U.Down(mask);
        // 任一支手把按下(暫停等共用功能)
        public static bool AnyDown(int mask) { Poll(); return Units[0].Down(mask) || (Count > 1 && Units[1].Down(mask)); }
    }

    // 操作手把(第二支;只接一支時與 Pad 相同)
    public static class Pad2
    {
        static PadUnit U { get { Pad.Poll(); return Pad.Count > 1 ? Pad.Units[1] : Pad.Units[0]; } }
        public static bool Separate { get { Pad.Poll(); return Pad.Count > 1; } }
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
