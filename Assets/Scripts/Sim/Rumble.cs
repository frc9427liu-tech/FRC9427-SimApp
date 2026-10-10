using System.Runtime.InteropServices;
using UnityEngine;

namespace FrcSim
{
    public static class Rumble
    {
        [StructLayout(LayoutKind.Sequential)] struct Vib { public ushort Left, Right; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] static extern uint Set14(uint user, ref Vib v);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")] static extern uint Set91(uint user, ref Vib v);
        static bool broken14, broken91;
        public static bool Enabled = true;   // false in automated test runs
        static readonly float[] pkL = new float[4], pkH = new float[4], rem = new float[4], dur = new float[4];
        static readonly int[] sentL = new int[4], sentH = new int[4];

        /// padIndex = XInput slot 0..3 (-1 = none). low/high 0..1. dur seconds; strength decays linearly.
        public static void Pulse(int padIndex, float low, float high, float seconds)
        {
            if (!Enabled || padIndex < 0 || padIndex > 3 || !JuiceSettings.Rumble) return;
            float k = rem[padIndex] > 0f ? rem[padIndex] / dur[padIndex] : 0f;     // keep the stronger of old/new envelope
            pkL[padIndex] = Mathf.Max(pkL[padIndex] * k, low); pkH[padIndex] = Mathf.Max(pkH[padIndex] * k, high);
            dur[padIndex] = Mathf.Max(seconds, 0.02f); rem[padIndex] = dur[padIndex];
        }
        public static void Driver(float low, float high, float seconds) { Pulse(Pad.DriverIndex, low, high, seconds); }
        public static void Operator(float low, float high, float seconds)
        { int o = Pad.OperatorIndex; Pulse(o >= 0 ? o : Pad.DriverIndex, low, high, seconds); }

        public static void Tick(float dt)
        {
            bool on = Enabled && JuiceSettings.Rumble && Time.timeScale > 0f && Application.isFocused;
            for (int u = 0; u < 4; u++)
            {
                if (!on || rem[u] <= 0f) { rem[u] = 0f; if (sentL[u] != 0 || sentH[u] != 0) Send(u, 0f, 0f); continue; }
                rem[u] -= dt;
                float k = Mathf.Clamp01(rem[u] / dur[u]);
                Send(u, pkL[u] * k, pkH[u] * k);
            }
        }
        public static void StopAll() { for (int u = 0; u < 4; u++) { rem[u] = 0f; sentL[u] = sentH[u] = -1; Send(u, 0f, 0f); } }

        static void Send(int u, float l, float h)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int il = (int)(Mathf.Clamp01(l) * 255f), ih = (int)(Mathf.Clamp01(h) * 255f);
            if (il == sentL[u] && ih == sentH[u]) return;      // only talk to the driver when the 8-bit value changes
            sentL[u] = il; sentH[u] = ih;
            var v = new Vib { Left = (ushort)(il * 257), Right = (ushort)(ih * 257) };
            try
            {
                if (!broken14) Set14((uint)u, ref v);
                else if (!broken91) Set91((uint)u, ref v);
            }
            catch (System.DllNotFoundException) { if (!broken14) broken14 = true; else broken91 = true; }
            catch (System.EntryPointNotFoundException) { if (!broken14) broken14 = true; else broken91 = true; }
#endif
        }
    }
}