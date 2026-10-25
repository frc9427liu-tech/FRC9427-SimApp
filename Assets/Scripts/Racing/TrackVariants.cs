using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽道變體(模組 W):鏡像 / 反向。RaceWorld.Build 依 RaceConfig.Mirror / Reverse 先把 TrackDef 轉成變體,再照一般流程生成,
    // 所以 RaceTrack 取樣、RaceCheckpoints、起跑格、重置點、AI 追線(都讀 RaceTrack)自動跟著對。
    //
    //  鏡像(Mirror):所有 x 取負(控制點、捷徑、地標/跳台的橫向位移也取負);弧長 s 不變;地形雜訊用 -x 取樣(RaceTerrain.Mirror),所以整張地形是原版的左右鏡射。
    //  反向(Reverse):控制點順序改成 [0, n-1, ..., 1](終點線留在原地,方向相反)→ 起跑格在線的另一側;捷徑的入口/出口互換;
    //                 跳台/加速板/地標的弧長 s' = L - s、橫向位移取負。反向之後有些東西會撞到新的檢查點門/重置點/起跑格/急彎,
    //                 FixFeatures 會把它們在 ±(12~60) m 內移開,移不開就拿掉(跳台前方 95 m 內不會有別的跳台)。
    //  滑翔跳台在反向時要重新驗證「落點」,不合格就拿掉(例如 Canyon Run 的第 1 個滑翔跳台後面是急彎,反向就沒有)。
    internal static class TrackVariants
    {
        public const float GliderZone = 95f;   // 滑翔跳台前緣之後這段距離內不放別的跳台
        const float GapRampLength = 14f;       // 缺口的起飛跳台(DefaultTracks.GliderLength / Width / Pitch)

        public static TrackDef Apply(TrackDef src, bool mirror, bool reverse)
        {
            if (src == null || (!mirror && !reverse)) return src;
            TrackDef d = Clone(src);
            if (mirror) MirrorX(d);
            if (reverse) { ReverseDir(d); FixFeatures(d); }
            return d;
        }

        // ------------------------------------------------------------------ 複製 / 鏡像

        static TrackNode CloneNode(TrackNode n) { return new TrackNode { x = n.x, z = n.z, width = n.width, bank = n.bank, flags = n.flags }; }

        public static TrackDef Clone(TrackDef s)
        {
            var d = new TrackDef
            {
                name = s.name, nameZh = s.nameZh, seed = s.seed, laps = s.laps, closed = s.closed, checkpointSpacing = s.checkpointSpacing,
                theme = s.theme, relief = s.relief, hillAmp = s.hillAmp, wallStart = s.wallStart, wallEnd = s.wallEnd, cliff = s.cliff, treeDensity = s.treeDensity, autoBank = s.autoBank,
            };
            foreach (var n in s.nodes) d.nodes.Add(CloneNode(n));
            foreach (var sc in s.shortcuts)
            {
                var c = new TrackShortcut { id = sc.id, fromNode = sc.fromNode, toNode = sc.toNode, width = sc.width, entryDist = sc.entryDist, exitDist = sc.exitDist };
                foreach (var n in sc.nodes) c.nodes.Add(CloneNode(n));
                d.shortcuts.Add(c);
            }
            foreach (var f in s.features)
                d.features.Add(new TrackFeature { kind = f.kind, s = f.s, lateral = f.lateral, length = f.length, width = f.width, pitch = f.pitch, path = f.path, locked = f.locked });
            foreach (var r in s.rails)
                d.rails.Add(new TrackRail { kind = r.kind, s0 = r.s0, s1 = r.s1, lateral = r.lateral, height = r.height });
            foreach (var g in s.gaps)
                d.gaps.Add(new TrackGap { s0 = g.s0, s1 = g.s1, halfWidth = g.halfWidth, depth = g.depth });
            foreach (var l in s.landmarks)
                d.landmarks.Add(new TrackLandmark { kind = l.kind, s = l.s, lateral = l.lateral, scale = l.scale });
            return d;
        }

        static void MirrorX(TrackDef d)
        {
            foreach (var n in d.nodes) { n.x = -n.x; n.bank = -n.bank; }
            foreach (var sc in d.shortcuts) foreach (var n in sc.nodes) { n.x = -n.x; n.bank = -n.bank; }
            foreach (var f in d.features) f.lateral = -f.lateral;
            foreach (var l in d.landmarks) l.lateral = -l.lateral;
            foreach (var r in d.rails) r.lateral = -r.lateral;
        }

        // ------------------------------------------------------------------ 反向

        static void ReverseDir(TrackDef d)
        {
            RaceTrack t0 = RaceTrack.FromDef(d);
            float L = t0.Length;
            int n = d.nodes.Count;

            var nn = new List<TrackNode>(n) { d.nodes[0] };
            for (int i = n - 1; i >= 1; i--) nn.Add(d.nodes[i]);
            d.nodes = nn;

            // 捷徑:從 b 走到 a;fromNode = 0 的捷徑沒辦法反向(會跨過終點線),整條拿掉
            var map = new int[d.shortcuts.Count];
            var scLen = new float[d.shortcuts.Count];
            var newList = new List<TrackShortcut>();
            for (int k = 0; k < d.shortcuts.Count; k++)
            {
                TrackShortcut sc = d.shortcuts[k];
                scLen[k] = t0.Shortcuts[k].Length;
                if (sc.fromNode < 1) { map[k] = -1; continue; }
                int a = sc.fromNode, b = sc.toNode;
                sc.fromNode = n - b; sc.toNode = n - a;
                sc.nodes.Reverse();
                float tmp = sc.entryDist; sc.entryDist = sc.exitDist; sc.exitDist = tmp;
                map[k] = newList.Count;
                newList.Add(sc);
            }
            d.shortcuts = newList;

            // 缺口(M7):弧長反過來 s' = L - s,前緣 / 落地邊緣互換;原本的起飛跳台(locked)丟掉,在新的前緣重新放一個
            foreach (var g in d.gaps) { float a = L - g.s1, b = L - g.s0; g.s0 = a; g.s1 = b; }

            var feats = new List<TrackFeature>();
            foreach (var f in d.features)
            {
                if (f.locked) continue;
                f.lateral = -f.lateral;
                if (f.path >= 0)
                {
                    int m = f.path < map.Length ? map[f.path] : -1;
                    if (m < 0) continue;
                    f.s = Mathf.Clamp(scLen[f.path] - f.s, 0f, scLen[f.path]);
                    f.path = m;
                }
                else f.s = Wrap(L - f.s, L);
                feats.Add(f);
            }
            foreach (var g in d.gaps)
            {
                var ramp = new TrackFeature(TrackFeatureKind.GliderRamp, g.s0 - GapRampLength * 0.5f, GapRampLength, 10f, 0f, 22f);
                ramp.locked = true;
                feats.Add(ramp);
            }
            d.features = feats;
            foreach (var l in d.landmarks) { l.s = Wrap(L - l.s, L); l.lateral = -l.lateral; }
            foreach (var r in d.rails) { float a = L - r.s1, b = L - r.s0; r.s0 = a; r.s1 = b; r.lateral = -r.lateral; }   // 軌道方向永遠沿新的行進方向(s 增加)
        }

        static float Wrap(float s, float L)
        {
            s %= L;
            if (s < 0f) s += L;
            return s >= L ? 0f : s;
        }

        /// 弧長差(a - b)包到 [-L/2, L/2]。
        static float Diff(float a, float b, float L)
        {
            float x = (a - b) % L;
            if (x > L * 0.5f) x -= L; else if (x < -L * 0.5f) x += L;
            return x;
        }

        // ------------------------------------------------------------------ 反向後整理跳台 / 加速板

        static void FixFeatures(TrackDef d)
        {
            RaceTrack t = RaceTrack.FromDef(d);
            float L = t.Length;
            var gates = new List<float>();
            for (int i = 0; i < t.CheckpointCount; i++) gates.Add(t.Checkpoints[i].S);

            var main = new List<TrackFeature>(); var other = new List<TrackFeature>(); var locked = new List<TrackFeature>();
            foreach (var f in d.features) { if (f.path < 0 && f.locked) locked.Add(f); else (f.path < 0 ? main : other).Add(f); }

            var kept = new List<TrackFeature>(locked);   // 缺口的起飛跳台不動,別的東西讓它
            int[] order = { 2, 0, 1 };   // 滑翔跳台優先,再來跳台,最後加速板
            foreach (int kind in order)
                foreach (var f in main)
                {
                    if (f.kind != kind) continue;
                    int radius = kind == 0 ? 60 : (kind == 2 ? 30 : 15);
                    bool placed = false;
                    float s0 = f.s;
                    for (int k = 0; k <= 2 * radius && !placed; k++)
                    {
                        float off = k == 0 ? 0f : ((k & 1) == 1 ? (k + 1) / 2 : -(k / 2));
                        float s2 = Wrap(s0 + off, L);
                        if (Conflicts(t, L, gates, kept, f, s2)) continue;
                        f.s = s2; placed = true;
                    }
                    if (placed) kept.Add(f);
                }

            // 滑翔跳台前方 95 m 內不放別的跳台
            for (int g = 0; g < kept.Count; g++)
            {
                if (kept[g].kind != 2) continue;
                float lip = kept[g].s + kept[g].length * 0.5f;
                for (int i = kept.Count - 1; i >= 0; i--)
                {
                    if (kept[i].kind != 0) continue;
                    float ahead = Wrap(kept[i].s - lip, L);
                    if (ahead <= GliderZone + 4f || ahead >= L - 2f) kept.RemoveAt(i);
                }
            }

            // 維持原本的順序(依 s 排序,方便閱讀)
            kept.Sort((a, b) => a.s.CompareTo(b.s));
            kept.AddRange(other);
            d.features = kept;
        }

        static bool Conflicts(RaceTrack t, float L, List<float> gates, List<TrackFeature> kept, TrackFeature f, float s2)
        {
            float half = f.length * 0.5f;
            bool ramp = f.kind != 1;
            float m = ramp ? 2.5f : 1.0f;
            for (int i = 0; i < gates.Count; i++)
            {
                float g = gates[i], rp = Wrap(g - 6f, L);
                if (ramp && Mathf.Abs(Diff(g, s2, L)) <= half + m) return true;     // 門線上的點不能被擋住
                if (Mathf.Abs(Diff(rp, s2, L)) <= half + m) return true;             // 重置點不能在上面
            }
            // 缺口(M7):跳台前 40 m 到落地後 20 m 不放跳台;加速板不放在缺口裡(落地後 6 m 內也不放);缺口中央門的重置點(跳台底部之前 70 m)也不能被擋
            if (t.InGap(s2, f.kind == 1 ? 0f : 40f, f.kind == 1 ? 6f : 20f)) return true;
            for (int gi = 0; gi < t.Gaps.Count; gi++)
                if (Mathf.Abs(Diff(Wrap(t.Gaps[gi].RampBase - 70f, L), s2, L)) <= half + m) return true;
            // 起跑線與起跑格(線後 6~34 m)
            if (Mathf.Abs(Diff(Wrap(-16.25f, L), s2, L)) <= 19.25f + half) return true;
            for (int i = 0; i < kept.Count; i++)
            {
                float need = (f.kind != 1 && kept[i].kind != 1) ? 30f : 3f;   // 跳台之間至少隔 30 m(落地區要是平路);加速板只要不重疊
                if (Mathf.Abs(Diff(s2, kept[i].s, L)) - (half + kept[i].length * 0.5f) < need) return true;
            }
            if (f.kind == 0 && Mathf.Abs(t.MaxCurvature(s2 - 6f, s2 + 24f)) > 1f / 45f) return true;      // 同 RaceWorldTest T10:跳台不放在急彎上
            if (f.kind == 2)
            {
                string why;
                if (!GliderLandingOk(t, s2, half, out why)) return true;
            }
            return false;
        }

        /// 滑翔跳台的落點檢查(沿行進方向):起飛段直(跳台前 10 m 到前緣後 14 m 的最大曲率 &lt;= 1/100),
        /// 前緣後 50 / 70 / 90 m 處的路中心線,離「從前緣沿切線直飛」的射線不超過 8 / 15 / 26 m(滑翔中可以轉向修正)。
        /// centerS = 跳台中心弧長,halfLen = 半長。
        public static bool GliderLandingOk(RaceTrack t, float centerS, float halfLen, out string why)
        {
            why = null;
            float lipS = centerS + halfLen;
            float k = Mathf.Abs(t.MaxCurvature(centerS - 10f, lipS + 14f));
            if (k > 1f / 100f) { why = "launch section not straight (1/" + (1f / Mathf.Max(k, 1e-6f)).ToString("0") + ")"; return false; }
            TrackSample lip = t.SampleAt(lipS);
            Vector3 fwd = t.PointAt(lipS + 1f) - lip.Pos; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-8f) { why = "degenerate"; return false; }
            fwd.Normalize();
            float[] dist = { 50f, 70f, 90f }, lim = { 8f, 15f, 26f };
            for (int i = 0; i < dist.Length; i++)
            {
                Vector3 q = t.PointAt(lipS + dist[i]) - lip.Pos;
                float dev = Mathf.Abs(fwd.x * q.z - fwd.z * q.x);
                if (dev > lim[i]) { why = "landing @" + dist[i] + " m is " + dev.ToString("0.0") + " m off the flight line (limit " + lim[i] + ")"; return false; }
            }
            return true;
        }
    }
}
