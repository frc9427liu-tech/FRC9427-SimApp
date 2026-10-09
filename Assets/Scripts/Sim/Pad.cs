using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FrcSim
{
    // 直接讀 Windows XInput(Xbox 手把),不靠 Unity 舊版 Input 的搖桿對應(那個對很多手把沒反應)。
    // 沒接手把或讀取失敗就全部回 0/false。
    public static class Pad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XState
        {
            public uint PacketNumber;
            public ushort Buttons;
            public byte LT, RT;
            public short LX, LY, RX, RY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint GetState14(uint user, out XState s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint GetState91(uint user, out XState s);

        public const int A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, LB = 0x0100, RB = 0x0200,
                         Back = 0x0020, Start = 0x0010, DUp = 0x0001, DDown = 0x0002, DLeft = 0x0004, DRight = 0x0008;

        static XState cur, prev;
        static int frame = -1;
        static bool broken14, broken91;
        public static bool Connected { get; private set; }

        static bool fakeOn; static XState fake;
        // 測試用:假裝手把輸入(自動測試用,真實遊戲不會呼叫)
        public static void SetFake(bool on, float lx = 0, float ly = 0, float rx = 0, float ry = 0, float lt = 0, float rt = 0, int buttons = 0)
        {
            fakeOn = on;
            fake = new XState { LX = (short)(lx * 32767), LY = (short)(ly * 32767), RX = (short)(rx * 32767), RY = (short)(ry * 32767), LT = (byte)(lt * 255), RT = (byte)(rt * 255), Buttons = (ushort)buttons };
        }
        public static string Raw() { Poll(); return $"connected={Connected} LX={cur.LX} LY={cur.LY} RX={cur.RX} RY={cur.RY} LT={cur.LT} RT={cur.RT} btn={cur.Buttons}"; }

        static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            prev = cur;
            if (fakeOn) { cur = fake; Connected = true; return; }
            Connected = false;
            cur = default;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            for (uint u = 0; u < 4 && !Connected; u++)
            {
                XState s; uint r = 1167;
                try
                {
                    if (!broken14) r = GetState14(u, out s);
                    else if (!broken91) r = GetState91(u, out s);
                    else break;
                }
                catch (DllNotFoundException) { if (!broken14) broken14 = true; else broken91 = true; continue; }
                catch (EntryPointNotFoundException) { if (!broken14) broken14 = true; else broken91 = true; continue; }
                if (r == 0) { Connected = true; cur = s; }
            }
#endif
        }

        static float Norm(short v, float dead)
        {
            float f = Mathf.Clamp(v / 32767f, -1f, 1f);
            if (Mathf.Abs(f) < dead) return 0f;
            return Mathf.Sign(f) * (Mathf.Abs(f) - dead) / (1f - dead);
        }

        public static float LX { get { Poll(); return Norm(cur.LX, 0.2f); } }
        public static float LY { get { Poll(); return Norm(cur.LY, 0.2f); } }   // 上 = +1
        public static float RX { get { Poll(); return Norm(cur.RX, 0.2f); } }
        public static float RY { get { Poll(); return Norm(cur.RY, 0.2f); } }
        public static float LT { get { Poll(); return cur.LT / 255f > 0.1f ? cur.LT / 255f : 0f; } }
        public static float RT { get { Poll(); return cur.RT / 255f > 0.1f ? cur.RT / 255f : 0f; } }

        public static bool Held(int mask) { Poll(); return (cur.Buttons & mask) != 0; }
        public static bool Down(int mask) { Poll(); return (cur.Buttons & mask) != 0 && (prev.Buttons & mask) == 0; }
    }
}
