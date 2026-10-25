using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -racehazardtest:驗證賽道移動障礙(RaceHazards)。
    //   FrcSim.exe -batchmode -nographics -racehazardtest   → 結果寫 racehazardtest.txt(專案/exe 同層),全 PASS 才印 "RESULT: PASS"
    //   H1 8 條賽道(含鏡像 + 反向)各 2~3 個障礙、種類符合主題、彼此間隔 >= MinSpacing、同賽道兩次建構完全相同(確定性)
    //   H2 沒有障礙在缺口(含助跑 / 落地區)、起跑區、終點前、捷徑入口 / 出口、跳台 / 加速板附近,且都在直線段
    //   H3 StateAt 是純函式(同時間同結果)、週期性、完整橫越路面、有休息時間
    //   H4 命中 = 暈眩 + 掉金幣 + 事件  H5 無敵 / 星星 / 巨大化不被打  H6 空中 / 羽毛跳躍 / 遠離 / 跳躍中的高處不被打
    //   H7 AI 避障清單  H8 Shutdown 清乾淨、Enabled = false 不建  H9 外觀三角形預算、時鐘只由 Tick(dt)/ResetClock 控制
    public sealed class RaceHazardTest : MonoBehaviour
    {
        const float Dt = 0.02f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-racehazardtest") < 0) return;
            var g = new GameObject("RaceHazardTest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceHazardTest>();
        }

        readonly StringBuilder sb = new StringBuilder();
        int fails;
        void Report(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail);
            if (!ok) fails++;
        }

        GameObject root;
        RaceTrack track;
        readonly List<RaceKart> made = new List<RaceKart>();

        IEnumerator Start()
        {
            yield return null;
            yield return null;
            float oldFixed = Time.fixedDeltaTime;
            int oldCapture = Time.captureFramerate;
            Time.fixedDeltaTime = Dt;
            Time.captureFramerate = 50;
            RaceKart.IgnoreMenuBlock = true;
            bool oldEnabled = RaceHazards.Enabled;
            RaceHazards.Enabled = true;

            Run("H1 placement", H1);
            Run("H2 exclusions", H2);
            Run("H3 pure state", H3);
            bool ok = true;
            try { Setup(); }
            catch (Exception e) { Report("setup", false, "exception: " + e); ok = false; }
            if (ok)
            {
                yield return new WaitForFixedUpdate();
                Run("H4 hit", H4);
                Run("H5 invincible", H5);
                Run("H6 misses", H6);
                Run("H7 ai list", H7);
                Run("H9 budget + clock", H9);
                Run("H8 shutdown", H8);
                yield return null;
                Run("H8b shutdown after frame", H8b);
            }
            try { Teardown(); } catch (Exception e) { sb.AppendLine("teardown exception: " + e.Message); }
            RaceHazards.Enabled = oldEnabled;
            RaceKart.IgnoreMenuBlock = false;
            Time.captureFramerate = oldCapture;
            Time.fixedDeltaTime = oldFixed;

            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "racehazardtest.txt"), sb.ToString()); }
            catch (Exception e) { Debug.LogError("[RaceHazardTest] write failed: " + e); }
            Debug.Log("[RaceHazardTest]\n" + sb);
            Application.Quit(fails == 0 ? 0 : 1);
        }

        void Run(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Report(name, false, "exception: " + e); }
        }

        void Setup()
        {
            var cfg = new RaceConfig { TrackId = 0, Laps = 3, Seed = 9427, AiCount = 0, AiLevel = 0, Items = true };
            root = new GameObject("RaceHazardTestRoot");
            track = RaceWorld.Build(cfg, root.transform, RaceQuality.Low);
            RaceKart.Ground = RaceWorld.SampleGround;
            RaceItems.Init(track, cfg, root.transform);
            RaceHazards.Init(track, root.transform, true);
        }

        void Teardown()
        {
            RaceHazards.Shutdown();
            RaceItems.Shutdown();
            ClearKarts();
            RaceKart.Ground = null;
            RaceWorld.Destroy();
            if (root != null) Destroy(root);
        }

        RaceKart MakeKart(int id, Vector3 pos)
        {
            var k = RaceKart.Create(root.transform, new Pose(pos, Quaternion.identity), KartPresets.For(KartStyle.Brick), id, "T" + id);
            k.Frozen = true;
            k.Progress = new RacerProgress { Id = id, Rank = 1 };
            var vis = new GameObject("Visual"); vis.transform.SetParent(k.transform, false);
            made.Add(k);
            return k;
        }

        void ClearKarts()
        {
            for (int i = 0; i < made.Count; i++) if (made[i] != null) DestroyImmediate(made[i].gameObject);
            made.Clear();
        }

        static List<RaceTrack> AllTracks(out List<string> names)
        {
            var list = new List<RaceTrack>(); names = new List<string>();
            for (int id = 0; id < RaceWorld.TrackCount; id++)
                for (int v = 0; v < 2; v++)
                {
                    bool mv = v == 1;
                    TrackDef def = TrackVariants.Apply(DefaultTracks.Get(id), mv, mv);
                    RaceTrack t = RaceTrack.FromDef(def);
                    t.Id = id;
                    list.Add(t); names.Add(id + (mv ? "m+r" : ""));
                }
            return list;
        }

        // ------------------------------------------------------------------ H1 / H2 / H3(純資料,不需要世界)

        void H1()
        {
            List<string> names;
            var tracks = AllTracks(out names);
            var errs = new StringBuilder(); var kinds = new HashSet<HazardKind>(); var counts = new StringBuilder();
            for (int i = 0; i < tracks.Count; i++)
            {
                RaceTrack t = tracks[i];
                RaceHazards.Init(t, null, false);
                var a = new List<HazardSpot>(RaceHazards.Spots);
                RaceHazards.Init(t, null, false);
                var b = RaceHazards.Spots;
                counts.Append(names[i] + ":" + a.Count + " ");
                if (a.Count < 2 || a.Count > 3) errs.Append(names[i] + " count=" + a.Count + "; ");
                if (a.Count != b.Count) { errs.Append(names[i] + " nondeterministic count; "); continue; }
                for (int j = 0; j < a.Count; j++)
                {
                    if (a[j].S != b[j].S || a[j].Kind != b[j].Kind || a[j].Phase != b[j].Phase || a[j].Dir != b[j].Dir) errs.Append(names[i] + " nondeterministic spot " + j + "; ");
                    if (a[j].Kind != RaceHazards.KindFor(t.Theme, j)) errs.Append(names[i] + " wrong kind for theme; ");
                    kinds.Add(a[j].Kind);
                    for (int q = j + 1; q < a.Count; q++) { float d = Mathf.Abs(a[j].S - a[q].S); d = Mathf.Min(d, t.Length - d); if (d < RaceHazards.MinSpacing - 0.01f) errs.Append(names[i] + " spacing " + d + "; "); }
                }
            }
            RaceHazards.Shutdown();
            Report("H1 placement/determinism", errs.Length == 0 && kinds.Count >= 8, "spots " + counts + " distinctKinds=" + kinds.Count + (errs.Length > 0 ? " ERR: " + errs : ""));
        }

        void H2()
        {
            List<string> names;
            var tracks = AllTracks(out names);
            var errs = new StringBuilder(); int total = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                RaceTrack t = tracks[i];
                RaceHazards.Init(t, null, false);
                foreach (HazardSpot h in RaceHazards.Spots)
                {
                    total++;
                    string tag = names[i] + "#" + h.Index + "@" + h.S.ToString("0") + " ";
                    if (h.S < RaceHazards.StartClear) errs.Append(tag + "in start zone; ");
                    if (h.S > t.Length - RaceHazards.EndClear) errs.Append(tag + "in grid/finish zone; ");
                    for (float d = -40f; d <= 40f; d += 10f)
                        if (t.InGap(h.S + d, RaceHazards.GapBefore - 40f, RaceHazards.GapAfter - 40f)) { errs.Append(tag + "near gap; "); break; }
                    foreach (var sc in t.Shortcuts)
                        if (Mathf.Abs(h.S - sc.FromS) < RaceHazards.ShortcutClear || Mathf.Abs(h.S - sc.ToS) < RaceHazards.ShortcutClear) errs.Append(tag + "near shortcut " + sc.Id + "; ");
                    foreach (var f in t.Features)
                        if (f.path < 0 && Mathf.Abs(h.S - f.s) < f.length * 0.5f + RaceHazards.FeatureClear) errs.Append(tag + "near feature; ");
                    float cw = h.Relaxed ? RaceHazards.RelaxWindow : RaceHazards.CurvWindow, cl = h.Relaxed ? RaceHazards.RelaxCurv : RaceHazards.StraightCurv;
                    if (Mathf.Abs(t.MaxCurvature(h.S - cw, h.S + cw)) > cl + 1e-5f) errs.Append(tag + "not straight; ");
                    // 不在任何捷徑的路徑上(障礙只在主線)
                    foreach (var sc in t.Shortcuts)
                        for (int q = 0; q < sc.Count; q += 4)
                        {
                            Vector3 dd = sc.Pos[q] - h.Pos; dd.y = 0f;
                            if (dd.magnitude < h.HalfWidth + 4f && Mathf.Abs(h.S - sc.FromS) > 5f && Mathf.Abs(h.S - sc.ToS) > 5f && q > 2 && q < sc.Count - 3) errs.Append(tag + "touches shortcut road; ");
                        }
                }
            }
            RaceHazards.Shutdown();
            Report("H2 exclusions (gap/start/shortcut/feature)", errs.Length == 0 && total >= 16, "spotsChecked=" + total + (errs.Length > 0 ? " ERR: " + errs : ""));
        }

        void H3()
        {
            List<string> names;
            var tracks = AllTracks(out names);
            var errs = new StringBuilder(); int checkedN = 0;
            for (int i = 0; i < tracks.Count; i += 2)
            {
                RaceHazards.Init(tracks[i], null, false);
                foreach (HazardSpot h in RaceHazards.Spots)
                {
                    checkedN++;
                    float maxAbs = 0f, restTime = 0f;
                    for (float t = 0.0037f; t < h.Period; t += 0.02f)
                    {
                        float la, li, lb, lj; bool ma, mb;
                        RaceHazards.StateAt(h, t, out la, out li, out ma);
                        RaceHazards.StateAt(h, t, out lb, out lj, out mb);
                        if (la != lb || li != lj || ma != mb) { errs.Append(names[i] + " impure; "); break; }
                        float lc, ld; bool mc;
                        RaceHazards.StateAt(h, t + h.Period, out lc, out ld, out mc);
                        if (Mathf.Abs(lc - la) > 1e-3f || Mathf.Abs(ld - li) > 1e-3f || mc != ma) { errs.Append(names[i] + " not periodic; "); break; }
                        if (ma) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(la)); else restTime += 0.02f;
                    }
                    if (maxAbs < h.HalfWidth + 0.5f) errs.Append(names[i] + "#" + h.Index + " does not cross the road (" + maxAbs.ToString("0.0") + "); ");
                    if (restTime < 2f) errs.Append(names[i] + "#" + h.Index + " no rest time; ");
                    if (h.Period < h.TravelTime + 2f) errs.Append("period too short; ");
                }
            }
            RaceHazards.Shutdown();
            Report("H3 pure/periodic/crossing", errs.Length == 0 && checkedN >= 8, "spots=" + checkedN + (errs.Length > 0 ? " ERR: " + errs : ""));
        }

        // ------------------------------------------------------------------ 世界內測試(Island GP)

        /// 找一個時間:障礙在路中央(|lat| < 0.25)、離地 <= maxLift(or >= minLift)。
        static float FindTime(HazardSpot h, float minLift, float maxLift, float latTol = 0.25f)
        {
            for (float t = 0f; t < h.Period; t += 0.01f)
            {
                float lat, lift; bool mv;
                RaceHazards.StateAt(h, t, out lat, out lift, out mv);
                if (mv && Mathf.Abs(lat) < latTol && lift >= minLift && lift <= maxLift) return t;
            }
            return -1f;
        }

        /// 把障礙校到 tHit 的前一步,車放在它的位置,Tick 一次。
        RaceKart PlaceAndTick(HazardSpot h, float tHit, int id, float yOffset = 0f, float latOffset = 0f)
        {
            float lat, lift; bool mv;
            RaceHazards.StateAt(h, tHit, out lat, out lift, out mv);
            Vector3 c = RaceHazards.WorldPos(h, lat, lift);
            Vector3 pos = c - Vector3.up * (h.Radius * 0.9f) + Vector3.up * (0.3f + yOffset) + h.Right * latOffset;
            RaceKart k = MakeKart(id, pos);
            k.Teleport(pos, Quaternion.LookRotation(h.Tangent, Vector3.up));
            RaceHazards.SetClock(tHit - Dt);
            return k;
        }

        void H4()
        {
            HazardSpot h = RaceHazards.Spots[0];
            float t = FindTime(h, 0f, 0.05f);
            if (t < 0f) { Report("H4 hit", false, "no center-crossing time found"); return; }
            RaceKart k = PlaceAndTick(h, t, 1);
            k.Coins = 6;
            int hits = 0; RaceKart who = null; Action<RaceKart, HazardSpot> hh = (kk, sp) => { hits++; who = kk; };
            RaceHazards.Hit += hh;
            int before = RaceHazards.HitCount;
            RaceHazards.Tick(Dt);
            RaceHazards.Hit -= hh;
            bool stunned = k.Stunned;
            Report("H4a hit stuns kart", stunned && hits == 1 && who == k && RaceHazards.HitCount == before + 1, "stunned=" + stunned + " events=" + hits + " kind=" + h.Kind);
            Report("H4b hit drops coins", k.Coins == 4, "coins 6 -> " + k.Coins + " (want 4: Stun drops 2)");
            Report("H4c stun length constant", Mathf.Abs(RaceHazards.StunSeconds - 0.8f) < 1e-4f, "StunSeconds=" + RaceHazards.StunSeconds);
            // 不會對同一台車連續命中(暈眩 + 之後的無敵)
            RaceHazards.SetClock(t - Dt);
            RaceHazards.Tick(Dt);
            Report("H4d no double hit while stunned/invincible", RaceHazards.HitCount == before + 1, "hits=" + (RaceHazards.HitCount - before));
            // 空手(0 金幣)被打也會暈,不會出負數
            ClearKarts();
            RaceKart k2 = PlaceAndTick(h, t, 2);
            k2.Coins = 0;
            RaceHazards.Tick(Dt);
            Report("H4e zero coins ok", k2.Stunned && k2.Coins == 0, "stunned=" + k2.Stunned + " coins=" + k2.Coins);
            ClearKarts();
        }

        void H5()
        {
            HazardSpot h = RaceHazards.Spots[0];
            float t = FindTime(h, 0f, 0.05f);
            RaceKart k = PlaceAndTick(h, t, 3);
            k.Coins = 5; k.SetInvincible(3f);
            int b = RaceHazards.HitCount;
            RaceHazards.Tick(Dt);
            Report("H5a invincible kart ignored", !k.Stunned && k.Coins == 5 && RaceHazards.HitCount == b, "stunned=" + k.Stunned + " coins=" + k.Coins);
            ClearKarts();

            RaceKart s = PlaceAndTick(h, t, 4);
            s.Coins = 5;
            ItemEffects.StartStar(s);
            RaceHazards.Tick(Dt);
            Report("H5b star ignored", !s.Stunned && s.Coins == 5, "stunned=" + s.Stunned + " coins=" + s.Coins);
            ClearKarts();

            RaceKart m = PlaceAndTick(h, t, 5);
            m.Coins = 5;
            ItemEffects.StartMega(m);
            RaceHazards.Tick(Dt);
            Report("H5c mega ignored", !m.Stunned && m.Coins == 5, "stunned=" + m.Stunned + " coins=" + m.Coins);
            ClearKarts();

            RaceKart bl = PlaceAndTick(h, t, 6);
            bl.Coins = 5;
            ItemEffects.StartBullet(bl, track);
            RaceHazards.Tick(Dt);
            Report("H5d bullet ignored", !bl.Stunned && bl.Coins == 5, "stunned=" + bl.Stunned + " coins=" + bl.Coins);
            ClearKarts();

            // 已經在暈眩中的車不會重複被打
            RaceKart st = PlaceAndTick(h, t, 7);
            st.Stun(1f); st.Coins = 5;
            int b2 = RaceHazards.HitCount;
            RaceHazards.Tick(Dt);
            Report("H5e already stunned ignored", RaceHazards.HitCount == b2 && st.Coins == 5, "hits=" + (RaceHazards.HitCount - b2));
            ClearKarts();
        }

        void H6()
        {
            HazardSpot h = RaceHazards.Spots[0];
            float t = FindTime(h, 0f, 0.05f);
            RaceKart air = PlaceAndTick(h, t, 8, 8f);
            RaceHazards.Tick(Dt);
            Report("H6a airborne kart passes over", !air.Stunned, "stunned=" + air.Stunned);
            ClearKarts();

            RaceKart far = PlaceAndTick(h, t, 9, 0f, 6f);
            RaceHazards.Tick(Dt);
            Report("H6b kart 6 m to the side not hit", !far.Stunned, "stunned=" + far.Stunned);
            ClearKarts();

            RaceKart fe = PlaceAndTick(h, t, 10);
            ItemEffects.StartFeather(fe);
            RaceHazards.Tick(Dt);
            Report("H6c feather hop passes", !fe.Stunned, "stunned=" + fe.Stunned);
            ClearKarts();

            // 跳躍類(Island 第 2 個 = 螃蟹):跳到高處時打不到;落地時打得到
            HazardSpot hop = RaceHazards.Spots.Count > 1 ? RaceHazards.Spots[1] : null;
            if (hop == null || hop.Motion != HazardMotion.Hop) { Report("H6d hop hazard high point", false, "no hop hazard at slot 1"); return; }
            float th = FindTime(hop, hop.HitLift + 0.05f, 99f, 100f);
            float tl = FindTime(hop, 0f, 0.1f, 100f);
            if (th < 0f || tl < 0f) { Report("H6d hop hazard high point", false, "times high=" + th + " low=" + tl); return; }
            RaceKart kh = PlaceAndTick(hop, th, 11, 0.3f);
            RaceHazards.Tick(Dt);
            bool highMiss = !kh.Stunned;
            ClearKarts();
            RaceKart kl = PlaceAndTick(hop, tl, 12);
            RaceHazards.Tick(Dt);
            Report("H6d hop hazard: high = safe, low = hit", highMiss && kl.Stunned, "highStunned=" + !highMiss + " lowStunned=" + kl.Stunned);
            ClearKarts();
        }

        void H7()
        {
            HazardSpot h = RaceHazards.Spots[0];
            float t = FindTime(h, 0f, 0.05f);
            RaceHazards.SetClock(t - Dt);
            RaceHazards.Tick(Dt);
            var cur = RaceHazards.Current;
            bool listed = cur.Count >= 1 && cur[0].OwnerKartId == -1 && cur[0].Radius > h.Radius && cur[0].Vel.magnitude > 1f;
            float aheadErr = cur.Count >= 1 ? Vector3.Distance(cur[0].Pos, RaceHazards.WorldPos(h, 0f, 0f)) : 999f;
            var items = new List<RaceHazard> { new RaceHazard { Pos = Vector3.zero, Radius = 1f, Kind = ItemKind.Oil, OwnerKartId = 3 } };
            var merged = RaceHazards.MergedWith(items);
            Report("H7a active hazard in AI list", listed && aheadErr < 0.6f && merged.Count == cur.Count + 1, "listed=" + listed + " posErr=" + aheadErr.ToString("0.00") + " merged=" + merged.Count);
            // 休息時(不在路上)清單是空的,MergedWith 原樣回傳道具清單
            float rest = -1f;
            for (float tt = 0f; tt < h.Period; tt += 0.05f)
            {
                float la, li; bool mv;
                bool others = false;
                foreach (HazardSpot o in RaceHazards.Spots) { float l2, i2; bool m2; RaceHazards.StateAt(o, tt, out l2, out i2, out m2); if (m2) others = true; }
                RaceHazards.StateAt(h, tt, out la, out li, out mv);
                if (!others) { rest = tt; break; }
            }
            if (rest >= 0f)
            {
                RaceHazards.SetClock(rest - Dt);
                RaceHazards.Tick(Dt);
                var m2 = RaceHazards.MergedWith(items);
                Report("H7b nothing active -> items list unchanged", RaceHazards.Current.Count == 0 && ReferenceEquals(m2, items), "current=" + RaceHazards.Current.Count);
            }
            else Report("H7b nothing active -> items list unchanged", true, "no all-rest moment in one period (skipped)");
        }

        void H9()
        {
            RaceHazards.ResetClock();
            Report("H9a ResetClock", RaceHazards.Clock == 0f, "clock=" + RaceHazards.Clock);
            RaceHazards.Tick(Dt); RaceHazards.Tick(Dt); RaceHazards.Tick(Dt);
            Report("H9b clock advances only by Tick(dt)", Mathf.Abs(RaceHazards.Clock - 3 * Dt) < 1e-5f, "clock=" + RaceHazards.Clock);
            RaceHazards.Init(track, root.transform, true);
            int tris = RaceHazards.VisualTris;
            Report("H9c visual triangles (Island)", tris > 0 && tris <= 1500, "tris=" + tris);
            // 8 條賽道的視覺預算 + 不新增材質(全部用 RaceToonVC 單例)
            var worst = 0; var mats = new HashSet<Material>(); bool sameMat = true;
            for (int id = 0; id < RaceWorld.TrackCount; id++)
            {
                RaceTrack t = RaceTrack.FromDef(DefaultTracks.Get(id));
                var tmp = new GameObject("tmpHaz").transform;
                TrackTheme.Current = TrackTheme.Get(t.Theme);
                RaceHazards.Init(t, tmp, true);
                worst = Mathf.Max(worst, RaceHazards.VisualTris);
                foreach (var r in tmp.GetComponentsInChildren<Renderer>(true)) { if (r.sharedMaterial != null) mats.Add(r.sharedMaterial); }
                RaceHazards.Shutdown();
                DestroyImmediate(tmp.gameObject);
            }
            Material vc = RaceShaders.ToonVC();
            if (vc != null) sameMat = mats.Count == 1 && mats.Contains(vc);
            Report("H9d all themes: tris <= 1500, one shared material", worst <= 1500 && sameMat, "worstTris=" + worst + " distinctMaterials=" + mats.Count);
            RaceHazards.Init(track, root.transform, true);
        }

        void H8()
        {
            RaceHazards.Shutdown();
            Report("H8a shutdown clears", !RaceHazards.Active && RaceHazards.Spots.Count == 0 && RaceHazards.Current.Count == 0, "active=" + RaceHazards.Active + " spots=" + RaceHazards.Spots.Count);
            RaceHazards.Enabled = false;
            RaceHazards.Init(track, root.transform, true);
            bool none = !RaceHazards.Active && RaceHazards.Spots.Count == 0;
            RaceHazards.Enabled = true;
            RaceHazards.Init(null, root.transform, true);
            bool none2 = !RaceHazards.Active;
            Report("H8b Enabled=false / null track builds nothing", none && none2, "disabled=" + none + " nullTrack=" + none2);
            // Tick 在沒有啟用時不動時鐘
            RaceHazards.SetClock(0f);
            RaceHazards.Tick(Dt);
            Report("H8c inactive Tick is a no-op", RaceHazards.Clock == 0f, "clock=" + RaceHazards.Clock);
        }

        void H8b()
        {
            GameObject g = GameObject.Find("RaceHazards");
            Report("H8d visuals destroyed after shutdown", g == null, g == null ? "no RaceHazards object" : "RaceHazards object still exists");
        }
    }
}
