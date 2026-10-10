using UnityEngine;

namespace FrcSim
{
    // Procedural, tileable textures. Generation cost: ~65k pixels (carpet, 256^2), well under 50 ms.
    public static class ProcTex
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
        static int Mod(int a, int m) { a %= m; return a < 0 ? a + m : a; }

        // value noise that tiles with period `period` cells over u,v in [0,1)
        static float VNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
        static float Fbm(float u, float v, int period, int oct, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0; int p = period;
            for (int o = 0; o < oct; o++) { sum += amp * VNoise(u * p, v * p, p, seed + o * 17); norm += amp; amp *= 0.5f; p *= 2; }
            return sum / norm;
        }
        static byte B(float f) { return (byte)Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255); }

        // data: R = fibre speckle, G = macro dirt/wear (large blotches), B = clump (mid-frequency)
        // normal: RGB = tangent-space normal packed 0..1 (decoded manually in FrcCarpet.shader, NOT a Unity "normal map" import)
        public static void Carpet(out Texture2D data, out Texture2D normal, int size = 256)
        {
            var d = new Color32[size * size];
            var n = new Color32[size * size];
            var hg = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float fine = Mathf.Clamp01(0.55f * Hash(x, y, 7) + 0.45f * VNoise(u * 64f, v * 64f, 64, 3));
                    float macro = Mathf.Clamp01((Fbm(u, v, 4, 4, 11) - 0.5f) * 2.2f + 0.5f);
                    float clump = Mathf.Clamp01((Fbm(u, v, 16, 3, 23) - 0.5f) * 1.8f + 0.5f);
                    int i = y * size + x;
                    hg[i] = 0.6f * fine + 0.4f * clump;
                    d[i] = new Color32(B(fine), B(macro), B(clump), 255);
                }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float hl = hg[y * size + Mod(x - 1, size)], hr = hg[y * size + Mod(x + 1, size)];
                    float hd = hg[Mod(y - 1, size) * size + x], hu = hg[Mod(y + 1, size) * size + x];
                    var nv = new Vector3(-(hr - hl) * 3f, -(hu - hd) * 3f, 1f).normalized;
                    n[y * size + x] = new Color32(B(nv.x * 0.5f + 0.5f), B(nv.y * 0.5f + 0.5f), B(nv.z * 0.5f + 0.5f), 255);
                }
            data = Make(d, size, size, true, "CarpetData");
            normal = Make(n, size, size, true, "CarpetNormal");
        }

        static Texture2D Make(Color32[] px, int w, int h, bool linear, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        // Crowd strip for the stand seats: a row of `cols` cells, each a seated person (colored body + head dot) or an empty seat.
        public static Texture2D Crowd(int cols = 8, int cell = 8)
        {
            int w = cols * cell, h = cell;
            var px = new Color32[w * h];
            var bg = new Color(0.055f, 0.07f, 0.16f);
            Color[] pal = {
                new Color(0.10f,0.25f,0.85f), new Color(0.85f,0.15f,0.15f), new Color(0.85f,0.85f,0.88f),
                new Color(0.30f,0.32f,0.38f), new Color(0.95f,0.80f,0.10f), new Color(0.15f,0.55f,0.35f) };
            for (int c = 0; c < cols; c++)
            {
                bool seated = Hash(c, 0, 5) > 0.18f;
                Color shirt = pal[(int)(Hash(c, 1, 9) * pal.Length) % pal.Length] * (0.65f + 0.5f * Hash(c, 2, 3));
                Color skin = new Color(0.78f, 0.6f, 0.5f) * (0.6f + 0.5f * Hash(c, 3, 4));
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        Color col = bg;
                        if (seated)
                        {
                            float bx = x - 3.5f, by = y - 2.2f;
                            if (bx * bx / 7.5f + by * by / 3.2f < 1f) col = shirt;                  // torso
                            float hx = x - 3.5f, hy = y - 5.6f;
                            if (hx * hx + hy * hy < 2.4f) col = skin;                               // head
                        }
                        px[y * w + c * cell + x] = col;
                    }
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            { name = "Crowd", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }
    }
}