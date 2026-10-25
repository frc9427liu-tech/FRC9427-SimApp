using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 路邊低多邊形地標(模組 W,M5):大拱門、風車、燈塔、岩石拱門、石柱。
    /// 位置來自 TrackDef.landmarks(主線弧長 s + 橫向位移 lateral)。全部依顏色合併成最多 5 個網格(紅 / 白 / 岩 / 木 / 橘),
    /// 材質都是別的東西已經在用的(RaceArt.Mat 同顏色同平滑度的快取),所以不會增加不同材質數。
    /// 只有立柱 / 塔身加 BoxCollider(最多十幾個),拱門的橫樑在路面上方 15 m 以上,車過得去。
    internal static class TrackLandmarks
    {
        public sealed class Result
        {
            public int Count, Tris, Draws, Colliders;
            public readonly List<Vector3> Bases = new List<Vector3>();
            public RaceColorAcc Blades;   // 風車葉片(旋轉動畫網格);由 TrackProps 併進它的動畫網格(省 1 個 draw call)
        }

        static readonly Color Orange = new Color(1f, 0.54f, 0.16f);   // 與 TrackFeatureBuilder.Orange 同色 → 共用快取材質

        static float Y(RaceTerrain t, float x, float z) { return Mathf.Max(0f, t.Raw(x, z)); }

        public static Result Build(RaceTrack tr, RaceTerrain terr, Transform parent, List<Object> owned)
        {
            var res = new Result();
            if (tr.Landmarks.Count == 0) return res;
            TrackTheme th = TrackTheme.Current ?? TrackTheme.Get(0);
            var red = new RaceMeshAcc(); var white = new RaceMeshAcc(); var rock = new RaceMeshAcc(); var wood = new RaceMeshAcc(); var orange = new RaceMeshAcc();
            var blades = new RaceColorAcc { A = new List<Vector4>(), P = new List<Vector4>() };
            var root = new GameObject("Landmarks");
            root.transform.SetParent(parent, false);

            for (int i = 0; i < tr.Landmarks.Count; i++)
            {
                TrackLandmark lm = tr.Landmarks[i];
                TrackSample sm = tr.SampleAt(lm.s);
                float k = Mathf.Max(0.3f, lm.scale);
                float hw = sm.Width * 0.5f;
                Vector3 b = sm.Pos + sm.Right * lm.lateral;
                b.y = Y(terr, b.x, b.z);
                switch ((TrackLandmarkKind)lm.kind)
                {
                    case TrackLandmarkKind.BigArch: BigArch(res, root.transform, red, white, sm, hw, k, terr); break;
                    case TrackLandmarkKind.Windmill: Windmill(res, root.transform, white, red, wood, b, sm.Pos, k, blades, i); break;
                    case TrackLandmarkKind.Lighthouse: Lighthouse(res, root.transform, white, red, orange, b, k); break;
                    case TrackLandmarkKind.RockArch: RockArch(res, root.transform, rock, sm, hw, k, terr); break;
                    default: Spire(res, root.transform, rock, orange, b, k, Mathf.Abs(lm.lateral) < 45f); break;
                }
                res.Count++;
                res.Bases.Add(b);
            }

            if (blades.TriCount > 0) res.Blades = blades;
            Add(root.transform, owned, res, "LmRed", red, RacePalette.CurbRed);
            Add(root.transform, owned, res, "LmWhite", white, RacePalette.CurbWhite);
            Add(root.transform, owned, res, "LmRock", rock, th.Rock);
            Add(root.transform, owned, res, "LmWood", wood, RacePalette.Trunk);
            Add(root.transform, owned, res, "LmOrange", orange, Orange);
            return res;
        }

        static void Add(Transform parent, List<Object> owned, Result res, string name, RaceMeshAcc acc, Color color)
        {
            if (acc.TriCount == 0) return;
            Mesh m = acc.ToMesh(name);
            owned.Add(m);
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceArt.Mat(color, 0.1f);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            res.Tris += acc.TriCount; res.Draws++;
        }

        static void BoxCol(Result res, Transform parent, Vector3 center, Vector3 fwd, Vector3 size)
        {
            var g = new GameObject("LmCollider");
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            Vector3 f = fwd; f.y = 0f;
            g.transform.rotation = f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
            g.AddComponent<BoxCollider>().size = size;
            res.Colliders++;
        }

        /// 多邊柱 / 圓錐(r1 = 0)。bottom 為底面中心。
        static void Frustum(RaceMeshAcc acc, Vector3 bottom, float r0, float r1, float h, int sides, float rotDeg)
        {
            float slope = (r0 - r1) / Mathf.Max(0.01f, h);
            Vector3 top = bottom + Vector3.up * h;
            for (int i = 0; i < sides; i++)
            {
                float a0 = rotDeg * Mathf.Deg2Rad + i * Mathf.PI * 2f / sides, a1 = rotDeg * Mathf.Deg2Rad + (i + 1) * Mathf.PI * 2f / sides;
                float am = (a0 + a1) * 0.5f;
                Vector3 n = new Vector3(Mathf.Cos(am), slope, Mathf.Sin(am)).normalized;
                Vector3 b0 = bottom + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r0, b1 = bottom + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r0;
                if (r1 < 0.01f) { acc.Tri(b0, b1, top, n); continue; }
                Vector3 t0 = top + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r1, t1 = top + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r1;
                acc.Quad(b0, b1, t1, t0, n);
                acc.Tri(top, t0, t1, Vector3.up);
            }
        }

        // ------------------------------------------------------------------ 大拱門(橫跨賽道,梁離路面 17 m 以上)

        static void BigArch(Result res, Transform col, RaceMeshAcc red, RaceMeshAcc white, TrackSample sm, float hw, float k, RaceTerrain terr)
        {
            Vector3 up = Vector3.up, right = sm.Right, fwd = sm.Tangent;
            float lat = hw + 6f, H = 18f * k, pw = 1.3f * k;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = sm.Pos + right * (side * lat);
                float gy = Y(terr, p.x, p.z);
                float top = sm.Pos.y + H;
                float hgt = top - gy;
                Vector3 c = new Vector3(p.x, gy + hgt * 0.5f, p.z);
                red.Box(c, right, up, fwd, new Vector3(pw, hgt * 0.5f, pw));
                // 柱腳白色底座
                white.Box(new Vector3(p.x, gy + 0.6f, p.z), right, up, fwd, new Vector3(pw * 1.5f, 0.6f, pw * 1.5f));
                BoxCol(res, col, c, fwd, new Vector3(pw * 2f, hgt, pw * 2f));
            }
            Vector3 mid = sm.Pos + up * H;
            white.Box(mid + up * 0.2f, right, up, fwd, new Vector3(lat + 2.5f * k, 1.0f * k, 1.1f * k));
            red.Box(mid + up * (1.9f * k), right, up, fwd, new Vector3(lat + 4.0f * k, 0.7f * k, 1.5f * k));
            red.Box(mid - up * (1.2f * k), right, up, fwd, new Vector3(lat, 0.4f * k, 0.8f * k));
        }

        // ------------------------------------------------------------------ 風車

        static void Windmill(Result res, Transform col, RaceMeshAcc white, RaceMeshAcc red, RaceMeshAcc wood, Vector3 b, Vector3 roadPt, float k, RaceColorAcc blades, int index)
        {
            float h = 15f * k;
            Frustum(white, b + Vector3.down * 1.5f, 3.6f * k, 2.2f * k, h + 1.5f, 8, 11f);
            Frustum(red, b + Vector3.up * h, 2.9f * k, 0f, 4.2f * k, 8, 11f);
            Vector3 face = roadPt - b; face.y = 0f;
            face = face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, face).normalized;
            Vector3 hub = b + Vector3.up * (h - 1.5f * k) + face * (2.3f * k);
            wood.Box(hub, side, Vector3.up, face, new Vector3(0.9f * k, 0.9f * k, 0.9f * k));
            // 風車葉片:世界座標的靜態幾何,但整批標記為「繞輪轂水平軸旋轉」(RaceToonVC 頂點動畫類型 6),所有風車共用 1 個網格 / 1 個 draw call,不需要逐幀 C#
            var bw = new RaceMeshAcc(); var br = new RaceMeshAcc();
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 dir = Vector3.up * Mathf.Cos(a) + side * Mathf.Sin(a);
                Vector3 width = Vector3.Cross(dir, face).normalized;
                float len = 9.5f * k;
                bw.Box(hub + face * (0.5f * k) + dir * (len * 0.5f + 0.6f * k), width, dir, face, new Vector3(0.18f * k, len * 0.5f, 0.12f * k));          // 桿
                br.Box(hub + face * (0.55f * k) + dir * (len * 0.62f + 0.6f * k) + width * (0.95f * k), width, dir, face, new Vector3(0.95f * k, len * 0.34f, 0.08f * k));   // 帆
            }
            float yaw = Mathf.Atan2(face.x, face.z);
            float spd = (index % 2 == 0 ? 1f : -1f) * 26f, ph0 = index * 37f;
            blades.AddSpinning(bw, (Color32)RacePalette.Trunk, spd, ph0, hub, yaw);
            blades.AddSpinning(br, (Color32)RacePalette.CurbRed, spd, ph0, hub, yaw);
            BoxCol(res, col, b + Vector3.up * (h * 0.5f), face, new Vector3(5.2f * k, h, 5.2f * k));
        }

        // ------------------------------------------------------------------ 燈塔

        static void Lighthouse(Result res, Transform col, RaceMeshAcc white, RaceMeshAcc red, RaceMeshAcc orange, Vector3 b, float k)
        {
            float seg = 6.5f * k;
            float r0 = 4.4f * k, r1 = 2.9f * k;
            for (int i = 0; i < 4; i++)
            {
                float ra = Mathf.Lerp(r0, r1, i / 4f), rb = Mathf.Lerp(r0, r1, (i + 1) / 4f);
                Frustum((i % 2 == 0) ? white : red, b + Vector3.up * (-1.5f + i * seg), ra, rb, seg + (i == 0 ? 1.5f : 0f), 8, 7f);
            }
            float topY = 4 * seg - 1.5f;
            white.Box(b + Vector3.up * (topY + 0.3f * k), Vector3.right, Vector3.up, Vector3.forward, new Vector3(3.6f * k, 0.3f * k, 3.6f * k));
            orange.Box(b + Vector3.up * (topY + 1.9f * k), Vector3.right, Vector3.up, Vector3.forward, new Vector3(1.5f * k, 1.3f * k, 1.5f * k));
            Frustum(red, b + Vector3.up * (topY + 3.2f * k), 2.6f * k, 0f, 2.8f * k, 8, 0f);
            BoxCol(res, col, b + Vector3.up * (topY * 0.5f), Vector3.forward, new Vector3(r0 * 1.7f, topY, r0 * 1.7f));
        }

        // ------------------------------------------------------------------ 岩石拱門(峽谷,橫跨賽道)

        static void RockArch(Result res, Transform col, RaceMeshAcc rock, TrackSample sm, float hw, float k, RaceTerrain terr)
        {
            Vector3 up = Vector3.up, right = sm.Right, fwd = sm.Tangent;
            float lat = hw + 9f, H = 17f * k;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = sm.Pos + right * (side * lat);
                float gy = Y(terr, p.x, p.z) - 2f;
                float top = sm.Pos.y + H;
                float hgt = top - gy;
                Frustum(rock, new Vector3(p.x, gy, p.z), 6.5f * k, 4.6f * k, hgt, 7, side * 20f);
                BoxCol(res, col, new Vector3(p.x, gy + hgt * 0.5f, p.z), fwd, new Vector3(9f * k, hgt, 9f * k));
            }
            Vector3 mid = sm.Pos + up * (H + 1.6f * k);
            rock.Box(mid, right, up, fwd, new Vector3(lat + 3.5f * k, 2.6f * k, 5.2f * k));
            rock.Box(mid + up * (2.8f * k) + fwd * (1.2f * k), right, up, fwd, new Vector3(lat * 0.55f, 1.4f * k, 3.4f * k));
        }

        // ------------------------------------------------------------------ 石柱

        static void Spire(Result res, Transform col, RaceMeshAcc rock, RaceMeshAcc orange, Vector3 b, float k, bool collider)
        {
            float y = -2f;
            float h = 10f * k;
            for (int i = 0; i < 3; i++)
            {
                float r0 = 7.2f * k * (1f - 0.24f * i), r1 = r0 * 0.78f;
                Frustum(rock, b + Vector3.up * y + new Vector3(0.7f * k * i, 0f, -0.5f * k * i), r0, r1, h + (i == 0 ? 2f : 0f), 6, i * 17f);
                y += h * (i == 0 ? 1.0f : 0.95f);
            }
            Frustum(rock, b + Vector3.up * y + new Vector3(1.4f * k, 0f, -1.0f * k), 3.3f * k, 0f, 6f * k, 6, 5f);
            Frustum(orange, b + Vector3.up * (h * 0.55f), 7.4f * k * 0.9f, 7.45f * k * 0.9f, 1.1f * k, 6, 0f);   // 一圈橘色岩層
            if (collider) BoxCol(res, col, b + Vector3.up * (h * 1.2f), Vector3.forward, new Vector3(9f * k, h * 2.4f, 9f * k));
        }
    }
}
