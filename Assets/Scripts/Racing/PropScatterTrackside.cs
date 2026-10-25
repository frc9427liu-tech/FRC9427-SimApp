using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 路邊密度(v0.5.93):
    ///  1) 路邊小物(護欄柵欄 / 樹籬 / 輪胎堆 / 三角錐 / 旗 / 看板 / 小石堆)每 8~20 m 一組,分到原本的 144 m 區塊(ToonVC 合併網格,不增加 draw call);
    ///  2) 路肩外 3.7~8.5 m 的草簇 / 花叢十字面片(RaceCutout,依賽道主題換成雪簇 / 枯草 / 乾灌木 / 餘燼)與看板面片,
    ///     以 32 m 為一個區塊合併成網格,每塊一個 draw call,離攝影機超過 80 m 整塊關掉(RaceWorldLod.Cell.Cull),所以一幀只畫附近約 200 m 的路段。
    /// 沒有碰撞體。隨機數用獨立種子,不影響樹 / 石頭的位置(determinism 測試與 T8 規則不受影響)。
    internal static partial class PropScatter
    {
        const float TuftChunk = 64f, TuftCull = 72f;

        internal struct SignQuad { public Vector3 Pos, Dir; public float W, H, Y0; public int Idx; }

        /// 擺放小物時順便記下的看板面片(由 BuildTufts 併進草簇網格)。
        internal sealed class TracksideSet { public readonly List<SignQuad> Signs = new List<SignQuad>(); }

        enum Furn { Fence, Hedge, Tire, Cones, Flag, Sign, Rocks }

        static RaceProto boxProto;

        static RaceProto Box()
        {
            if (boxProto != null) return boxProto;
            var b = new RaceProtoBuilder();
            b.Lathe(Vector3.zero, new[] { 0.7071f, 0.7071f }, new[] { 0f, 1f }, new[] { Color.white, Color.white }, 4, true, true, 45f);
            boxProto = b.Build();
            return boxProto;
        }

        static float[] Weights(LookKind k)
        {
            //                              Fence Hedge Tire  Cones Flag  Sign  Rocks
            switch (k)
            {
                case LookKind.Snow: return new[] { 0.22f, 0.00f, 0.10f, 0.08f, 0.17f, 0.23f, 0.20f };
                case LookKind.Desert: return new[] { 0.18f, 0.00f, 0.10f, 0.10f, 0.15f, 0.22f, 0.25f };
                case LookKind.Volcano: return new[] { 0.14f, 0.00f, 0.10f, 0.08f, 0.18f, 0.22f, 0.28f };
                case LookKind.Coast: return new[] { 0.22f, 0.12f, 0.10f, 0.10f, 0.17f, 0.24f, 0.05f };
                default: return new[] { 0.20f, 0.32f, 0.08f, 0.06f, 0.14f, 0.20f, 0.04f };   // 草原系:樹籬比例提高(MK 路邊的連續彩色籬笆/方塊牆)
            }
        }

        static Vector3 SidePt(RaceTerrain t, TrackSample sm, int side, float lat)
        {
            Vector3 p = sm.Pos + sm.Right * (side * lat);
            p.y = t.Raw(p.x, p.z);
            return p;
        }

        static readonly Color32 White32 = new Color32(255, 255, 255, 255);

        static void ScatterFurniture(RaceTrack tr, RaceTerrain t, System.Random rnd, CellBuild[] cells, float density, Result res, TracksideSet set)
        {
            float Len = tr.Length;
            float[] w = Weights(look.Kind);
            Color wood = look.Kind == LookKind.Snow ? new Color(0.78f, 0.82f, 0.92f) : (look.Kind == LookKind.Volcano ? new Color(0.24f, 0.2f, 0.22f) : (look.Kind == LookKind.Desert ? Color.Lerp(RacePalette.Trunk, Color.white, 0.3f) : RacePalette.Trunk));
            Color woodDark = Color.Lerp(wood, Color.black, 0.3f);
            Color[] bright = { new Color(0.93f, 0.26f, 0.26f), new Color(1f, 0.8f, 0.2f), new Color(0.22f, 0.56f, 1f), new Color(0.3f, 0.78f, 0.4f), new Color(0.95f, 0.5f, 0.15f) };
            float prob = (tr.Theme == 8 ? 0.5f : 0.72f) * density;   // 路邊造景密度(原 0.5):路邊不再空曠;黃昏山路三角形預算貼著上限,維持 0.5
            float sum = 0f; for (int i = 0; i < w.Length; i++) sum += w[i];
            for (float s = 5f + (float)rnd.NextDouble() * 8f; s < Len - 4f; s += 8f + (float)rnd.NextDouble() * 12f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float r0 = (float)rnd.NextDouble(), r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble(), r3 = (float)rnd.NextDouble(), r4 = (float)rnd.NextDouble();
                    if (r0 > prob) continue;
                    TrackSample sm = tr.SampleAt(s);
                    if (Mathf.Abs(sm.Curvature) > 1f / 60f) continue;
                    float acc = 0f;
                    int type = 0; float pick = r1 * sum;
                    for (int i = 0; i < w.Length; i++) { acc += w[i]; if (pick <= acc) { type = i; break; } }
                    float lat = sm.Width * 0.5f + 4.6f + r2 * 1.5f;
                    Vector3 p = SidePt(t, sm, side, lat);
                    float d;
                    if (!Valid(tr, t, p.x, p.z, 4.0f, false, out d)) continue;
                    Vector3 tan = sm.Tangent; tan.y = 0f; tan.Normalize();
                    float yaw = Mathf.Atan2(-tan.z, tan.x) * Mathf.Rad2Deg;   // 本地 +x 對齊賽道前進方向
                    CellBuild cb = cells[CellOf(p.x, p.z)];
                    var b = new RaceProtoBuilder();
                    bool ok = true;
                    switch ((Furn)type)
                    {
                        case Furn.Fence:
                        {
                            int n = 2 + (int)(r3 * 2f);
                            var pts = new Vector3[n + 1];
                            for (int k = 0; k <= n && ok; k++)
                            {
                                TrackSample sk = tr.SampleAt(Mathf.Min(Len - 0.1f, s + k * 4.5f));
                                pts[k] = SidePt(t, sk, side, sk.Width * 0.5f + 4.6f + r2 * 1.5f);
                                if (!Valid(tr, t, pts[k].x, pts[k].z, 4.0f, false, out d)) ok = false;
                            }
                            if (!ok) break;
                            for (int k = 0; k <= n; k++)
                                b.Lathe(pts[k] + Vector3.down * 0.05f, new[] { 0.1f, 0.075f }, new[] { 0f, 1.1f }, new[] { woodDark, wood }, 4, false, false, 45f);
                            for (int k = 0; k < n; k++)
                            {
                                b.Tube(pts[k] + Vector3.up * 0.8f, pts[k + 1] + Vector3.up * 0.8f, 0.055f, 0.055f, 4, wood, wood);
                            }
                            cb.Acc.Add(b.Build(), Vector3.zero, 0f, Vector3.one, White32);
                            break;
                        }
                        case Furn.Hedge:
                        {
                            int n = 2;
                            for (int k = 0; k < n; k++)
                            {
                                TrackSample sk = tr.SampleAt(Mathf.Min(Len - 0.1f, s + k * 2.6f));
                                Vector3 q = SidePt(t, sk, side, sk.Width * 0.5f + 5.0f + r2 * 1.0f);
                                if (!Valid(tr, t, q.x, q.z, 4.2f, false, out d)) continue;
                                Color leaf = L(k + (int)(r4 * 3f));
                                var hb = new RaceProtoBuilder();
                                hb.Blob(new Vector3(0f, 0.55f, 0f), new Vector3(1.55f, 0.8f, 0.85f), 0, Shade(leaf, 0.32f), Sunny(leaf, 0.2f), 0f);
                                Vector3 tk = sk.Tangent; tk.y = 0f; tk.Normalize();
                                cb.Acc.Add(hb.Build(), q + Vector3.down * 0.05f, Mathf.Atan2(-tk.z, tk.x) * Mathf.Rad2Deg, Vector3.one, TintOf(r3, r4));
                            }
                            break;
                        }
                        case Furn.Tire:
                        {
                            int stacks = 1 + (int)(r3 * 1.5f);
                            Color c1 = (r4 < 0.5f) ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.92f, 0.25f, 0.25f), dark = new Color(0.14f, 0.14f, 0.17f);
                            for (int sk = 0; sk < stacks; sk++)
                            {
                                Vector3 q = p + new Vector3(tan.x, 0f, tan.z) * (sk * 1.0f);
                                q.y = t.Raw(q.x, q.z);
                                int hgt = 2 + (int)(r2 * 2f);
                                for (int k = 0; k < hgt; k++)
                                {
                                    Color tc = k % 2 == 0 ? c1 : dark;
                                    b.Lathe(q + Vector3.up * (k * 0.3f - 0.04f), new[] { 0.5f, 0.5f }, new[] { 0f, 0.3f }, new[] { tc, tc }, 5, false, false, k * 20f);
                                }
                            }
                            cb.Acc.Add(b.Build(), Vector3.zero, 0f, Vector3.one, White32);
                            break;
                        }
                        case Furn.Cones:
                        {
                            Color orng = new Color(0.95f, 0.45f, 0.1f);
                            for (int k = 0; k < 2; k++)
                            {
                                Vector3 q = p + new Vector3(tan.x, 0f, tan.z) * (k * 1.5f - 0.75f) + new Vector3(-tan.z, 0f, tan.x) * ((k % 2) * 0.4f);
                                q.y = t.Raw(q.x, q.z);
                                b.Lathe(q, new[] { 0.3f, 0.24f, 0.2f, 0.04f }, new[] { 0f, 0.06f, 0.4f, 0.75f }, new[] { orng, orng, Color.white, orng }, 4, false, false, k * 30f);
                            }
                            cb.Acc.Add(b.Build(), Vector3.zero, 0f, Vector3.one, White32);
                            break;
                        }
                        case Furn.Flag:
                        {
                            int n = 2;
                            float dirSign = r4 < 0.5f ? 1f : -1f;
                            for (int k = 0; k < n; k++)
                            {
                                TrackSample sk = tr.SampleAt(Mathf.Min(Len - 0.1f, s + k * 3.2f));
                                Vector3 q = SidePt(t, sk, side, sk.Width * 0.5f + 4.6f + r2 * 1.2f);
                                if (!Valid(tr, t, q.x, q.z, 4.0f, false, out d)) continue;
                                Vector3 tk = sk.Tangent; tk.y = 0f; tk.Normalize();
                                Color fc = bright[(k + (int)(r1 * 5f)) % bright.Length];
                                var fb = new RaceProtoBuilder();
                                fb.Tube(new Vector3(0f, -0.05f, 0f), new Vector3(0f, 3.4f, 0f), 0.06f, 0.04f, 3, new Color(0.75f, 0.75f, 0.78f), new Color(0.9f, 0.9f, 0.92f));
                                cb.Acc.Add(fb.Build(), q, 0f, Vector3.one, White32);
                                float fyaw = Mathf.Atan2(-tk.z, tk.x) * Mathf.Rad2Deg;
                                cb.Acc.Add(Box(), q + new Vector3(tk.x, 0f, tk.z) * (0.6f * dirSign) + Vector3.up * 2.55f, fyaw, new Vector3(1.2f, 0.75f, 0.04f), (Color32)fc);
                            }
                            break;
                        }
                        case Furn.Sign:
                        {
                            bool big = r3 < 0.3f;
                            float sw = big ? 5.0f : 2.5f, sh = sw * (96f / 256f), y0 = big ? 1.9f : 1.15f;
                            float py = t.Raw(p.x, p.z);
                            // 看板放在路旁、面對賽道(面片法線朝路)
                            var sq = new SignQuad { Pos = new Vector3(p.x, py, p.z), Dir = new Vector3(tan.x, 0f, tan.z), W = sw, H = sh, Y0 = y0, Idx = (int)(r4 * 12f) % 12 };
                            set.Signs.Add(sq);
                            Vector3 dd = sq.Dir;
                            float off = sw * 0.5f - 0.25f;
                            for (int k = -1; k <= 1; k += 2)
                                b.Tube(sq.Pos + dd * (off * k) + Vector3.down * 0.05f, sq.Pos + dd * (off * k) + Vector3.up * (y0 + sh + 0.12f), 0.07f, 0.06f, 3, woodDark, new Color(0.6f, 0.62f, 0.7f));
                            cb.Acc.Add(b.Build(), Vector3.zero, 0f, Vector3.one, White32);
                            res.Signs++;
                            break;
                        }
                        default:
                        {
                            int n = 2;
                            for (int k = 0; k < n; k++)
                            {
                                float a = r4 * 6.28f + k * 2.2f, rd = k == 0 ? 0f : 0.9f;
                                Vector3 q = p + new Vector3(Mathf.Cos(a) * rd, 0f, Mathf.Sin(a) * rd);
                                q.y = t.Raw(q.x, q.z) - 0.1f;
                                float sc = k == 0 ? 0.9f + r2 * 0.6f : 0.4f + r3 * 0.3f;
                                cb.Acc.Add(rocks[(k + (int)(r1 * 7f)) % rocks.Length], q, r4 * 360f + k * 70f, new Vector3(sc, sc * 0.8f, sc), TintOf(r3, r2));
                            }
                            break;
                        }
                    }
                    if (ok) res.Furniture++;
                }
            }
        }

        // ------------------------------------------------------------------ 草簇 / 看板面片

        sealed class ChunkBuf
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>(), UV1 = new List<Vector2>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<int> T = new List<int>();
            public Vector3 Min = new Vector3(1e9f, 1e9f, 1e9f), Max = new Vector3(-1e9f, -1e9f, -1e9f);
        }

        static Color32 LinearCol(Color c, float a)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) c = c.linear;
            c.a = a;
            return c;
        }

        static void AddQuad(ChunkBuf b, Vector3 bottom, Vector3 dir, float w, float h, Vector2 uv0, Vector2 uv1, float sprite, float ember, Color32 col)
        {
            int i = b.V.Count;
            Vector3 hw = dir * (w * 0.5f), up = Vector3.up * h;
            b.V.Add(bottom - hw); b.V.Add(bottom + hw); b.V.Add(bottom + hw + up); b.V.Add(bottom - hw + up);
            b.UV.Add(new Vector2(uv0.x, uv0.y)); b.UV.Add(new Vector2(uv1.x, uv0.y)); b.UV.Add(new Vector2(uv1.x, uv1.y)); b.UV.Add(new Vector2(uv0.x, uv1.y));
            for (int k = 0; k < 4; k++) { b.UV1.Add(new Vector2(sprite, ember)); b.C.Add(col); }
            b.T.Add(i); b.T.Add(i + 1); b.T.Add(i + 2); b.T.Add(i); b.T.Add(i + 2); b.T.Add(i + 3);
            for (int k = 0; k < 4; k++) { Vector3 v = b.V[i + k]; b.Min = Vector3.Min(b.Min, v); b.Max = Vector3.Max(b.Max, v); }
        }

        static void ChunkOf(Dictionary<int, ChunkBuf> d, float x, float z, out ChunkBuf buf)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((x + RaceTerrain.Half) / TuftChunk), 0, 63), cz = Mathf.Clamp(Mathf.FloorToInt((z + RaceTerrain.Half) / TuftChunk), 0, 63);
            int key = cz * 64 + cx;
            if (!d.TryGetValue(key, out buf)) { buf = new ChunkBuf(); d[key] = buf; }
        }

        static void BuildTufts(Transform parent, RaceTrack tr, RaceTerrain t, System.Random rnd, float density, List<Object> owned, RaceWorldLod lod, Result res, TracksideSet set)
        {
            Material mat = RaceShaders.Cutout();
            if (mat == null) mat = RaceArt.Mat(Color.white, 0.05f);   // shader 不支援(-nographics 測試)時用純色材質,三角形與區塊照算
            var chunks = new Dictionary<int, ChunkBuf>();
            LookKind kind = look.Kind;
            float kd = kind == LookKind.Meadow ? 1.0f : kind == LookKind.Forest ? 0.9f : kind == LookKind.Coast ? 0.75f : kind == LookKind.Snow ? 0.6f : 0.5f;
            float flowerShare = kind == LookKind.Meadow ? 0.3f : kind == LookKind.Forest ? 0.16f : kind == LookKind.Coast ? 0.22f : 0f;
            float prob = 0.82f * kd * density;
            int cap = Mathf.RoundToInt(4200 * density);
            int count = 0;
            float Len = tr.Length;
            // 草簇總數固定約 2600 簇:長賽道間距拉大(短賽道 1.2 m,長賽道到 2.6 m),近看密度由簇的大小補
            float stepLen = Mathf.Clamp(Len * 4f * prob / Mathf.Max(1, cap) * 0.95f, 1.2f, 3.6f);
            Vector2 uv0 = Vector2.zero, uv1 = Vector2.one;
            for (float s = (float)rnd.NextDouble(); s < Len && count < cap; s += stepLen * (0.8f + (float)rnd.NextDouble() * 0.5f))
            {
                TrackSample sm = tr.SampleAt(s);
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int band = 0; band < 2; band++)
                    {
                        float r0 = (float)rnd.NextDouble(), r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble(), r3 = (float)rnd.NextDouble(), r4 = (float)rnd.NextDouble(), r5 = (float)rnd.NextDouble();
                        if (r0 > prob) continue;
                        float lat = sm.Width * 0.5f + (band == 0 ? 3.7f + r1 * 1.8f : 5.5f + r1 * 3.0f);
                        Vector3 p = sm.Pos + sm.Right * (side * lat) + sm.Tangent * ((r2 - 0.5f) * 1.2f);
                        float dist;
                        if (!Valid(tr, t, p.x, p.z, 3.5f, false, out dist)) continue;
                        p.y = t.Raw(p.x, p.z) - 0.03f;
                        bool flower = r3 < flowerShare;
                        float wd = (flower ? 0.95f : 1.15f) + r4 * (kind == LookKind.Desert ? 0.9f : 0.7f);
                        float ht = wd * (0.8f + r5 * 0.3f);
                        float yaw = r5 * 180f * Mathf.Deg2Rad;
                        float v = 0.88f + 0.22f * r4;
                        Color tint; float desat; float ember = 0f;
                        switch (kind)
                        {
                            case LookKind.Snow: tint = new Color(0.9f, 0.96f, 1f) * v; desat = 0.88f; break;
                            case LookKind.Desert: tint = new Color(0.86f, 0.72f, 0.42f) * v; desat = 0.85f; break;
                            case LookKind.Volcano:
                                if (r3 < 0.14f) { tint = new Color(1f, 0.42f, 0.1f); desat = 0.9f; ember = 1f; }
                                else { tint = new Color(0.46f, 0.34f, 0.28f) * v; desat = 0.9f; }
                                flower = false; break;
                            default: tint = Color.Lerp(Color.white, theme.GrassLight, 0.3f) * v; desat = flower ? 0f : 0.12f; break;
                        }
                        tint.a = 1f;
                        if (flower) tint = Color.Lerp(Color.white, tint, 0.3f);
                        Color32 col = LinearCol(tint, desat);
                        ChunkBuf cb;
                        ChunkOf(chunks, p.x, p.z, out cb);
                        Vector3 d1 = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), d2 = new Vector3(-d1.z, 0f, d1.x);
                        AddQuad(cb, p, d1, wd, ht, uv0, uv1, flower ? 1f : 0f, ember, col);
                        AddQuad(cb, p, d2, wd, ht, uv0, uv1, flower ? 1f : 0f, ember, col);
                        count++;
                    }
                }
            }
            res.Tufts = count;
            // 看板:圖集 2 欄 x 6 列,列 0 在最上面
            Color32 wcol = LinearCol(Color.white, 0f);
            for (int i = 0; i < set.Signs.Count; i++)
            {
                SignQuad sq = set.Signs[i];
                ChunkBuf cb; ChunkOf(chunks, sq.Pos.x, sq.Pos.z, out cb);
                int ac = sq.Idx % 2, ar = sq.Idx / 2;
                Vector2 u0 = new Vector2(ac / 2f + 0.004f, 1f - (ar + 1) / 6f + 0.006f), u1 = new Vector2((ac + 1) / 2f - 0.004f, 1f - ar / 6f - 0.006f);
                AddQuad(cb, sq.Pos + Vector3.up * sq.Y0, sq.Dir, sq.W, sq.H, u0, u1, 2f, 0f, wcol);
            }
            foreach (var kv in chunks)
            {
                ChunkBuf cb = kv.Value;
                if (cb.T.Count == 0) continue;
                var m = new Mesh { name = "RaceTufts_" + kv.Key };
                m.SetVertices(cb.V); m.SetUVs(0, cb.UV); m.SetUVs(1, cb.UV1); m.SetColors(cb.C); m.SetTriangles(cb.T, 0);
                var nl = new List<Vector3>(cb.V.Count);
                for (int i = 0; i < cb.V.Count; i++) nl.Add(Vector3.up);
                m.SetNormals(nl);
                var bb = new Bounds((cb.Min + cb.Max) * 0.5f, cb.Max - cb.Min);
                m.bounds = bb;
                owned.Add(m);
                var go = new GameObject("Tufts_" + kv.Key);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                int tris = cb.T.Count / 3;
                lod.cells.Add(new RaceWorldLod.Cell { Renderers = new Renderer[] { mr }, Bounds = bb, Tris = tris, Cull = TuftCull });
                res.TuftTris += tris; res.Tris += tris; res.Cells++;
            }
        }
    }
}
