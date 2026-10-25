using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -raceitemtest:驗證模組 I 的道具系統(M4)。
    //   FrcSim.exe -batchmode -nographics -raceitemtest   → 結果寫 raceitemtest.txt(專案/exe 同層),每項 PASS/FAIL,全 PASS 才印 "RESULT: PASS"
    // 手動呼叫 RaceItems.Tick(dt)(每個 FixedUpdate 一次),所以計時可重現;車用 Frozen 的 RaceKart,放在 Island GP 起跑直線上。
    //   I1 機率表:每列總和 = 100,名次 → 列 的換算
    //   I2 機率表分布:固定種子 20000 次抽獎,各名次各道具比例與表差 < 1.5 個百分點,0% 的絕對不出現;同種子可重現
    //   I3 追蹤彈限制:開賽前 10 s 與場上已有 2 顆時不出追蹤彈
    //   I4 道具箱:數量/位置在路上、撞破 → 抽獎 1.2 s → 拿到道具、3 s 後重生、已有道具穿過不破
    //   I5 加速 / I6 三連加速 / I7 護盾 / I8 油漬 / I9 直線彈(命中 + 反彈) / I10 追蹤彈命中前車(靜止 + AI 駕駛)
    //   I11 無敵不被命中(含彈體穿過) / I12 AI 使用邏輯 / I13 Shutdown 清乾淨
    //   I14 15 種道具機率表規則(領先者不出閃電 / 藍殼 / 子彈,藍殼限量) / I15 星星 / I16 閃電 / I17 炸彈 / I18 藍殼
    //   I19 巨大化 / I20 子彈加速(接管與歸還) / I21 假道具箱 / I22 三連直線彈 / I23 墨水 / I24 新道具 AI 使用 / I25 效果清理
    //   I26 使用道具回饋:任何道具使用成功 → 無敵 >= UseInvincibleSeconds、回饋事件被記錄、無敵會過期、星星 / 巨大化不被縮短
    //   I27 兩格道具(撞箱進第 2 格 / 用完遞補 / 交換 / 交換鍵上升緣)  I28 任意方向瞄準(含 AimBack 等價 180 度)  I29 羽毛  I30 金幣道具
    //   I31 火焰花  I32 黃金加速  I33 超級喇叭  I34 閃電掉光兩格道具  I35 新道具 AI 使用
    //   I36 瞄準規則(紅殼往後 / 大於 90 度不追蹤、綠殼永不追蹤、藍殼追第 1 名)  I37 香蕉皮(打轉 1.0 s + 掉 1 枚金幣)  I38 三連香蕉皮(車尾 3 片 + 擋後方來的殼)
    //   I39 幽靈(偷前車道具 + 3 s 穿透,含閃電)  I40 迴力鏢花(去程 + 回程各打一次)  I41 AI v2 與機率表 v2
    //   註:UseInvincibleSeconds 預設已改為 0(使用道具不給無敵,只有特技給)。I26 自己明確設 0.4 s 來驗證 i-frames 機制,並驗證預設值 = 0。
    public class RaceItemsTest : MonoBehaviour
    {
        const float Dt = 0.02f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-raceitemtest") < 0) return;
            var g = new GameObject("RaceItemsTest");
            DontDestroyOnLoad(g);
            g.AddComponent<RaceItemsTest>();
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
        RaceConfig cfg;
        readonly List<RaceKart> made = new List<RaceKart>();

        // 事件記錄
        readonly List<KeyValuePair<RaceKart, ItemKind>> gotLog = new List<KeyValuePair<RaceKart, ItemKind>>();
        readonly List<KeyValuePair<RaceKart, ItemKind>> usedLog = new List<KeyValuePair<RaceKart, ItemKind>>();
        readonly List<KeyValuePair<RaceKart, ItemKind>> blockedLog = new List<KeyValuePair<RaceKart, ItemKind>>();
        readonly List<RaceKart[]> hitLog = new List<RaceKart[]>();        // [victim, by]
        readonly List<ItemKind> hitKinds = new List<ItemKind>();
        int rollStarts, boxBroken;
        readonly List<KeyValuePair<RaceKart, ItemKind>> effStart = new List<KeyValuePair<RaceKart, ItemKind>>();
        readonly List<KeyValuePair<RaceKart, ItemKind>> effEnd = new List<KeyValuePair<RaceKart, ItemKind>>();
        readonly List<KeyValuePair<float, ItemKind>> explodeLog = new List<KeyValuePair<float, ItemKind>>();
        Action<RaceKart, ItemKind> hEffS, hEffE;
        float oldUseInv; bool oldUseFx;
        Action<Vector3, float, ItemKind> hExp;

        int CountEff(List<KeyValuePair<RaceKart, ItemKind>> l, ItemKind k) { int n = 0; for (int i = 0; i < l.Count; i++) if (l[i].Value == k) n++; return n; }

        // 測試用輸入:固定打方向、不給油門(證明子彈期間輸入被覆蓋)
        sealed class ConstInput : IKartInput
        {
            public float Steer = 1f, Throttle = 0f;
            public bool Swap;
            public KartInput Read(float dt) { return new KartInput { Steer = Steer, Throttle = Throttle, SwapHeld = Swap }; }
        }
        Action<RaceKart, ItemKind> hGot, hUsed, hBlocked;
        Action<RaceKart, RaceKart, ItemKind> hHit;
        Action<RaceKart> hRoll;
        Action<Vector3> hBox;

        void ClearLogs()
        {
            gotLog.Clear(); usedLog.Clear(); blockedLog.Clear(); hitLog.Clear(); hitKinds.Clear();
            rollStarts = 0; boxBroken = 0;
            effStart.Clear(); effEnd.Clear(); explodeLog.Clear();
        }

        // ------------------------------------------------------------------ 流程

        IEnumerator Start()
        {
            yield return null;
            yield return null;
            float oldFixed = Time.fixedDeltaTime;
            int oldCapture = Time.captureFramerate;
            Time.fixedDeltaTime = Dt;
            Time.captureFramerate = 50;
            RaceKart.IgnoreMenuBlock = true;
            bool oldDrift = RaceAI.EnableDrift;
            RaceAI.EnableDrift = false;

            bool ok = true;
            try { Setup(); }
            catch (Exception e) { Report("setup", false, "exception: " + e); ok = false; }

            if (ok)
            {
                yield return new WaitForFixedUpdate();
                yield return Safe("I1 table", TestTable());
                yield return Safe("I2 distribution", TestDistribution());
                yield return Safe("I3 homing rules", TestHomingRules());
                yield return Safe("I4 boxes", TestBoxes());
                yield return Safe("I5 boost", TestBoost());
                yield return Safe("I6 triple", TestTriple());
                yield return Safe("I7 shield", TestShield());
                yield return Safe("I8 oil", TestOil());
                yield return Safe("I9 bumper", TestBumper());
                yield return Safe("I10 homing", TestHoming());
                yield return Safe("I11 invincible", TestInvincible());
                yield return Safe("I12 ai use", TestAiUse());
                yield return Safe("I14 table rules", TestTableRules());
                yield return Safe("I15 star", TestStar());
                yield return Safe("I16 lightning", TestLightning());
                yield return Safe("I17 bomb", TestBomb());
                yield return Safe("I18 blue shell", TestBlueShell());
                yield return Safe("I19 mega", TestMega());
                yield return Safe("I20 bullet", TestBullet());
                yield return Safe("I21 fake box", TestFakeBox());
                yield return Safe("I22 triple bumper", TestTripleBumper());
                yield return Safe("I23 ink", TestInk());
                yield return Safe("I24 ai new items", TestAiNew());
                yield return Safe("I25 effect cleanup", TestEffectCleanup());
                yield return Safe("I26 use fx + i-frames", TestUseFx());
                yield return Safe("I27 two slots", TestTwoSlots());
                yield return Safe("I28 aim any direction", TestAim());
                yield return Safe("I29 feather", TestFeather());
                yield return Safe("I30 coin item", TestCoinItem());
                yield return Safe("I31 fire flower", TestFireFlower());
                yield return Safe("I32 gold boost", TestGoldBoost());
                yield return Safe("I33 super horn", TestSuperHorn());
                yield return Safe("I34 lightning drops items", TestLightningDrop());
                yield return Safe("I35 ai new items", TestAiNew2());
                yield return Safe("I36 targeting rules", TestTargetingRules());
                yield return Safe("I37 banana peel", TestBananaPeel());
                yield return Safe("I38 triple banana", TestTripleBanana());
                yield return Safe("I39 boo", TestBoo());
                yield return Safe("I40 boomerang", TestBoomerang());
                yield return Safe("I41 ai v2 + table v2", TestAiV2());
                yield return Safe("I13 shutdown", TestShutdown());
            }

            try { Teardown(); } catch (Exception e) { sb.AppendLine("teardown exception: " + e.Message); }
            RaceKart.IgnoreMenuBlock = false;
            RaceAI.EnableDrift = oldDrift;
            Time.captureFramerate = oldCapture;
            Time.fixedDeltaTime = oldFixed;

            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fails + ")");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "raceitemtest.txt"), sb.ToString()); }
            catch (Exception e) { Debug.LogError("[RaceItemsTest] write failed: " + e); }
            Debug.Log("[RaceItemsTest]\n" + sb);
            Application.Quit(fails == 0 ? 0 : 1);
        }

        IEnumerator Safe(string name, IEnumerator body)
        {
            while (true)
            {
                bool more;
                try { more = body.MoveNext(); }
                catch (Exception e) { Report(name, false, "exception: " + e); yield break; }
                if (!more) yield break;
                yield return body.Current;
            }
        }

        void Setup()
        {
            cfg = new RaceConfig { TrackId = 0, Laps = 3, Seed = 9427, AiCount = 0, AiLevel = 0, Items = true };
            root = new GameObject("RaceItemsTestRoot");
            track = RaceWorld.Build(cfg, root.transform, RaceQuality.Low);
            RaceKart.Ground = RaceWorld.SampleGround;
            RaceItems.Init(track, cfg, root.transform);
            oldUseInv = RaceItems.UseInvincibleSeconds; oldUseFx = RaceItems.UseFxEnabled;
            RaceItems.UseInvincibleSeconds = 0f;       // I1..I25 不要使用後 i-frames(I26 自己開)
            hGot = (k, i) => gotLog.Add(new KeyValuePair<RaceKart, ItemKind>(k, i));
            hUsed = (k, i) => usedLog.Add(new KeyValuePair<RaceKart, ItemKind>(k, i));
            hBlocked = (k, i) => blockedLog.Add(new KeyValuePair<RaceKart, ItemKind>(k, i));
            hHit = (v, b, i) => { hitLog.Add(new[] { v, b }); hitKinds.Add(i); };
            hRoll = k => rollStarts++;
            hBox = p => boxBroken++;
            RaceItems.Got += hGot; RaceItems.Used += hUsed; RaceItems.Blocked += hBlocked;
            RaceItems.Hit += hHit; RaceItems.RollStart += hRoll; RaceItems.BoxBroken += hBox;
            hEffS = (k, i) => effStart.Add(new KeyValuePair<RaceKart, ItemKind>(k, i));
            hEffE = (k, i) => effEnd.Add(new KeyValuePair<RaceKart, ItemKind>(k, i));
            hExp = (pos, r, i) => explodeLog.Add(new KeyValuePair<float, ItemKind>(r, i));
            RaceItems.EffectStart += hEffS; RaceItems.EffectEnd += hEffE; RaceItems.Exploded += hExp;
        }

        void Teardown()
        {
            RaceItems.Got -= hGot; RaceItems.Used -= hUsed; RaceItems.Blocked -= hBlocked;
            RaceItems.Hit -= hHit; RaceItems.RollStart -= hRoll; RaceItems.BoxBroken -= hBox;
            RaceItems.EffectStart -= hEffS; RaceItems.EffectEnd -= hEffE; RaceItems.Exploded -= hExp;
            RaceItems.Shutdown();
            RaceItems.UseInvincibleSeconds = oldUseInv; RaceItems.UseFxEnabled = oldUseFx;
            ClearKarts();
            RaceKart.Ground = null;
            RaceWorld.Destroy();
            if (root != null) Destroy(root);
        }

        // ------------------------------------------------------------------ 工具

        RaceKart MakeKart(int id, float s, float lat, int rank = 1)
        {
            TrackSample sm = track.SampleAt(s);
            Vector3 pos = sm.Pos + sm.Right * lat + Vector3.up * 0.3f;
            var k = RaceKart.Create(root.transform, new Pose(pos, Quaternion.LookRotation(sm.Tangent, Vector3.up)), KartPresets.For(KartStyle.Brick), id, "T" + id);
            k.Frozen = true;
            k.Progress = new RacerProgress { Id = id, Rank = rank };
            var vis = new GameObject("Visual"); vis.transform.SetParent(k.transform, false);   // 替身外觀,測縮放 / 隱藏
            made.Add(k);
            return k;
        }

        void ClearKarts()
        {
            for (int i = 0; i < made.Count; i++) if (made[i] != null) { DestroyImmediate(made[i].gameObject); }
            made.Clear();
        }

        // 每個測試之間清乾淨:車、彈、持有(用 Give None)
        IEnumerator Reset()
        {
            for (int i = 0; i < made.Count; i++) if (made[i] != null) RaceItems.Give(made[i], ItemKind.None);
            ClearKarts();
            ItemProjectiles.ClearAll();
            yield return null;
            RaceItems.Tick(Dt);
            ClearLogs();
        }

        IEnumerator Run(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Dt - 1e-4f);
            for (int i = 0; i < n; i++)
            {
                RaceItems.Tick(Dt);
                yield return new WaitForFixedUpdate();
            }
        }

        // 一直跑到 cond 成立或超時;回傳花的秒數(超時 = -1)
        float runT;
        IEnumerator RunUntil(Func<bool> cond, float maxSeconds)
        {
            runT = -1f;
            int n = Mathf.CeilToInt(maxSeconds / Dt);
            for (int i = 0; i < n; i++)
            {
                RaceItems.Tick(Dt);
                yield return new WaitForFixedUpdate();
                if (cond()) { runT = (i + 1) * Dt; yield break; }
            }
        }

        float StraightS { get { return track.Length - 45f; } }

        // ------------------------------------------------------------------ I1 / I2 / I3

        IEnumerator TestTable()
        {
            bool sums = true; string d = "";
            for (int r = 0; r < ItemTable.Rows; r++)
            {
                int s = ItemTable.RowSum(r);
                d += s + " ";
                if (s != 100) sums = false;
            }
            Report("I1a table rows sum 100", sums, "sums: " + d);
            bool map = ItemTable.RowFor(1, 8) == 0 && ItemTable.RowFor(2, 8) == 1 && ItemTable.RowFor(3, 8) == 2 && ItemTable.RowFor(4, 8) == 2
                && ItemTable.RowFor(5, 8) == 3 && ItemTable.RowFor(6, 8) == 3 && ItemTable.RowFor(7, 8) == 4 && ItemTable.RowFor(8, 8) == 4
                && ItemTable.RowFor(1, 4) == 0 && ItemTable.RowFor(4, 4) == 4 && ItemTable.RowFor(1, 1) == 0;
            Report("I1b place->row mapping", map, "8 cars 1..8 -> rows 0,1,2,2,3,3,4,4; 4 cars 1/4 -> " + ItemTable.RowFor(1, 4) + "/" + ItemTable.RowFor(4, 4));
            yield break;
        }

        IEnumerator TestDistribution()
        {
            const int N = 20000;
            int[] places = { 1, 2, 3, 4, 5, 6, 7, 8 };
            bool allOk = true; double worst = 0;
            var rng = new System.Random(20260101);
            var lines = new StringBuilder();
            for (int pi = 0; pi < places.Length; pi++)
            {
                int row = ItemTable.RowFor(places[pi], 8);
                var cnt = new int[ItemTable.Order.Length];
                for (int n = 0; n < N; n++)
                {
                    ItemKind k = RaceItems.Roll(places[pi], 8, rng);
                    int idx = Array.IndexOf(ItemTable.Order, k);
                    if (idx < 0) { allOk = false; continue; }
                    cnt[idx]++;
                }
                lines.Append("  place " + places[pi] + ":");
                for (int i = 0; i < cnt.Length; i++)
                {
                    double pct = cnt[i] * 100.0 / N;
                    double diff = Math.Abs(pct - ItemTable.Weights[row, i]);
                    if (diff > worst) worst = diff;
                    if (diff > 1.5) allOk = false;
                    if (ItemTable.Weights[row, i] == 0 && cnt[i] != 0) allOk = false;
                    lines.Append(" " + ItemTable.Order[i] + "=" + pct.ToString("F1") + "(" + ItemTable.Weights[row, i] + ")");
                }
                lines.AppendLine();
            }
            Report("I2a distribution vs table", allOk, "worst diff " + worst.ToString("F2") + " pts over " + N + " rolls/place\n" + lines.ToString().TrimEnd());

            var r1 = new System.Random(77); var r2 = new System.Random(77);
            bool same = true;
            for (int i = 0; i < 200; i++) if (RaceItems.Roll(1 + i % 8, 8, r1) != RaceItems.Roll(1 + i % 8, 8, r2)) same = false;
            Report("I2b same seed reproducible", same, "200 rolls identical");
            yield break;
        }

        IEnumerator TestHomingRules()
        {
            var rng = new System.Random(5);
            int homing = 0, triple = 0;
            for (int i = 0; i < 5000; i++)
            {
                ItemKind k = RaceItems.Roll(8, 8, rng, false);
                if (k == ItemKind.Homing) homing++;
                if (k == ItemKind.TripleBoost) triple++;
            }
            Report("I3a no homing when disallowed", homing == 0 && triple > 900, "homing=" + homing + " triple=" + triple + " (place 8: 10% homing converted to triple, ~22% total)");

            bool notYet = !RaceItems.HomingAllowed;
            Report("I3b no homing in first 10 s", notYet && RaceItems.Elapsed < 10f, "elapsed=" + RaceItems.Elapsed.ToString("F1") + " allowed=" + RaceItems.HomingAllowed);

            yield return Run(10.2f);
            var a = MakeKart(1, StraightS, -3f); var b = MakeKart(2, StraightS, 0f); var c = MakeKart(3, StraightS, 3f);
            yield return null;
            RaceItems.Tick(Dt);
            bool afterTen = RaceItems.HomingAllowed;
            RaceItems.Give(a, ItemKind.Homing); RaceItems.Give(b, ItemKind.Homing);
            bool capped = !RaceItems.HomingAllowed && RaceItems.HomingInPlay() == 2;
            Report("I3c homing allowed after 10 s, capped at 2 in play", afterTen && capped, "afterTen=" + afterTen + " inPlay=" + RaceItems.HomingInPlay() + " allowedWith2=" + RaceItems.HomingAllowed);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I4 道具箱

        IEnumerator TestBoxes()
        {
            var boxes = RaceItems.Boxes;
            int rows = Mathf.RoundToInt(track.Length / ItemBox.Spacing);
            bool countOk = boxes.Count == rows * ItemBox.PerRow && rows >= 3;
            bool onRoad = true; float maxLat = 0f;
            for (int i = 0; i < boxes.Count; i++)
            {
                float lat; float s = track.Project(boxes[i].transform.position, -1f, out lat);
                maxLat = Mathf.Max(maxLat, Mathf.Abs(lat));
                if (Mathf.Abs(lat) > track.HalfWidthAt(s) - 1f) onRoad = false;
            }
            Report("I4a boxes along track", countOk && onRoad, boxes.Count + " boxes in " + rows + " rows (track " + track.Length.ToString("F0") + " m), max |lateral| " + maxLat.ToString("F1") + " m");

            ItemBox box = boxes[0];
            var k = MakeKart(10, 0f, 0f, 4);
            k.Teleport(box.transform.position - Vector3.up * 0.5f, k.transform.rotation);
            yield return new WaitForFixedUpdate();
            ClearLogs();
            RaceItems.Tick(Dt);
            bool broke = !box.Active && boxBroken == 1 && rollStarts == 1 && RaceItems.Rolling(k);
            Report("I4b touching box breaks it and starts roll", broke, "active=" + box.Active + " boxBroken=" + boxBroken + " rollStart=" + rollStarts + " rolling=" + RaceItems.Rolling(k));

            yield return Run(0.58f);
            bool midRoll = RaceItems.Rolling(k) && RaceItems.Held(k) == ItemKind.None && RaceItems.RollT(k) > 0.4f && RaceItems.RollT(k) < 0.6f;
            yield return Run(0.7f);
            bool done = !RaceItems.Rolling(k) && RaceItems.Held(k) != ItemKind.None && gotLog.Count == 1;
            RaceItems.GiveSecond(k, ItemKind.Shield);          // 兩格道具後(2026-10-23):第 2 格也填滿,I4d / I4e 的箱子才會「穿過去不破」
            Report("I4c roll takes 1.2 s then grants one item", midRoll && done, "midRollT ok=" + midRoll + " held=" + RaceItems.Held(k) + " got=" + gotLog.Count);

            // 此刻距撞破約 0.02+0.58+0.7 = 1.3 s;箱子應仍在重生中,到 3 s 後重生
            bool stillOut = !box.Active;
            yield return Run(1.5f);                // ≈ 2.8 s
            bool at28 = !box.Active;
            yield return Run(0.4f);                // ≈ 3.2 s
            bool respawned = box.Active;
            Report("I4d box respawns after 3 s", stillOut && at28 && respawned, "out@1.3s=" + stillOut + " out@2.8s=" + at28 + " back@3.2s=" + respawned);

            // 已有道具:箱子在腳下、仍啟用,應該不破、不抽
            ClearLogs();
            yield return Run(0.3f);
            Report("I4e holding an item: pass through box", box.Active && boxBroken == 0 && !RaceItems.Rolling(k), "active=" + box.Active + " boxBroken=" + boxBroken);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I5~I7

        IEnumerator TestBoost()
        {
            var k = MakeKart(20, StraightS, 0f);
            RaceItems.Give(k, ItemKind.Boost);
            bool had = RaceItems.Held(k) == ItemKind.Boost && RaceItems.Charges(k) == 1;
            bool used = RaceItems.TryUse(k, false);
            bool eff = k.Boosting && k.BoostRemaining > 1.2f && k.BoostRemaining <= 1.5f + 0.01f;
            bool gone = RaceItems.Held(k) == ItemKind.None && !RaceItems.TryUse(k, false);
            bool evt = usedLog.Count == 1 && usedLog[0].Value == ItemKind.Boost;
            Report("I5 boost", had && used && eff && gone && evt, "boosting=" + k.Boosting + " remaining=" + k.BoostRemaining.ToString("F2") + " heldAfter=" + RaceItems.Held(k) + " usedEvt=" + evt);
            yield return Reset();
        }

        IEnumerator TestTriple()
        {
            var k = MakeKart(21, StraightS, 0f);
            RaceItems.Give(k, ItemKind.TripleBoost);
            bool c3 = RaceItems.Charges(k) == 3;
            bool u1 = RaceItems.TryUse(k, false);
            bool blocked = !RaceItems.TryUse(k, false);       // 兩次間至少 0.3 s
            yield return Run(0.32f);
            bool u2 = RaceItems.TryUse(k, false);
            bool c1 = RaceItems.Charges(k) == 1;
            yield return Run(0.32f);
            bool u3 = RaceItems.TryUse(k, false);
            bool empty = RaceItems.Held(k) == ItemKind.None && RaceItems.Charges(k) == 0;
            bool evt = usedLog.Count == 3 && k.Boosting;
            Report("I6 triple boost (3 charges, 0.3 s gap)", c3 && u1 && blocked && u2 && c1 && u3 && empty && evt,
                "charges3=" + c3 + " u1=" + u1 + " gapBlocks=" + blocked + " u2=" + u2 + " u3=" + u3 + " empty=" + empty + " usedEvents=" + usedLog.Count);
            yield return Reset();
        }

        IEnumerator TestShield()
        {
            var k = MakeKart(22, StraightS, 0f);
            RaceItems.Give(k, ItemKind.Shield);
            RaceItems.TryUse(k, false);
            bool on = RaceItems.Shielded(k) && RaceItems.ShieldRemaining(k) > 7.9f;
            RaceItems.ApplyHit(k, 1f, null, ItemKind.Bumper);
            bool absorbed = !k.Stunned && blockedLog.Count == 1 && hitLog.Count == 0 && !RaceItems.Shielded(k);
            RaceItems.ApplyHit(k, 1f, null, ItemKind.Bumper);
            bool thenHit = k.Stunned && hitLog.Count == 1;
            Report("I7a shield absorbs exactly one hit", on && absorbed && thenHit, "on=" + on + " absorbed=" + absorbed + " nextHitStuns=" + thenHit);

            yield return Reset();
            var k2 = MakeKart(23, StraightS, 0f);
            RaceItems.Give(k2, ItemKind.Shield);
            RaceItems.TryUse(k2, false);
            yield return Run(7.9f);
            bool still = RaceItems.Shielded(k2);
            yield return Run(0.3f);
            bool expired = !RaceItems.Shielded(k2);
            Report("I7b shield expires after 8 s", still && expired, "@7.9s=" + still + " @8.2s shielded=" + !expired);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I8 油漬

        IEnumerator TestOil()
        {
            var a = MakeKart(30, StraightS, 0f, 1);
            Vector3 fwd = a.Forward; fwd.y = 0f; fwd.Normalize();
            RaceItems.Give(a, ItemKind.Oil);
            bool used = RaceItems.TryUse(a, false);
            RaceItems.Tick(Dt);
            RaceHazard oil = default(RaceHazard); bool found = false;
            for (int i = 0; i < RaceItems.Hazards.Count; i++) if (RaceItems.Hazards[i].Kind == ItemKind.Oil) { oil = RaceItems.Hazards[i]; found = true; }
            float behind = found ? Vector3.Dot(oil.Pos - a.Position, fwd) : 0f;
            Report("I8a oil dropped behind & in Hazards", used && found && behind < -1.5f && oil.OwnerKartId == a.KartId && oil.Radius > 0.9f, "found=" + found + " behindDist=" + behind.ToString("F1") + " radius=" + oil.Radius);

            // 受害者開到油漬上
            float lat; float os = track.Project(oil.Pos, -1f, out lat);
            var v = MakeKart(31, os, lat, 2);
            ClearLogs();
            yield return Run(0.1f);
            bool hit = hitLog.Count == 1 && hitLog[0][0] == v && hitLog[0][1] == a && hitKinds[0] == ItemKind.Oil;
            bool gone = true;
            for (int i = 0; i < RaceItems.Hazards.Count; i++) if (RaceItems.Hazards[i].Kind == ItemKind.Oil) gone = false;
            Report("I8b kart on oil: Hit event, oil consumed", hit && gone, "hit=" + hit + " oilConsumed=" + gone);

            // 往前丟
            yield return Reset();
            var b = MakeKart(32, StraightS, 0f);
            Vector3 f2 = b.Forward; f2.y = 0f; f2.Normalize();
            RaceItems.Give(b, ItemKind.Oil);
            RaceItems.TryUse(b, true);
            RaceItems.Tick(Dt);
            float ahead = 0f;
            for (int i = 0; i < RaceItems.Hazards.Count; i++) if (RaceItems.Hazards[i].Kind == ItemKind.Oil) ahead = Vector3.Dot(RaceItems.Hazards[i].Pos - b.Position, f2);
            Report("I8c oil aimBack throws 8 m ahead", ahead > 6.5f && ahead < 9.5f, "ahead=" + ahead.ToString("F1") + " m");

            // 護盾擋油漬
            yield return Reset();
            var c = MakeKart(33, StraightS, 0f);
            RaceItems.Give(c, ItemKind.Shield); RaceItems.TryUse(c, false);
            ClearLogs();
            RaceItems.ApplyHit(c, 0f, null, ItemKind.Oil);
            Report("I8d shield blocks oil", blockedLog.Count == 1 && hitLog.Count == 0, "blocked=" + blockedLog.Count);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I9 直線彈

        IEnumerator TestBumper()
        {
            var a = MakeKart(40, StraightS, 0f, 2);
            var b = MakeKart(41, StraightS + 25f, 0f, 1);
            RaceItems.Give(a, ItemKind.Bumper);
            ClearLogs();
            RaceItems.TryUse(a, false);
            yield return RunUntil(() => hitLog.Count > 0, 2f);
            float t = runT;
            bool hit = hitLog.Count == 1 && hitLog[0][0] == b && hitLog[0][1] == a && hitKinds[0] == ItemKind.Bumper && b.Stunned;
            Report("I9a bumper hits car 25 m ahead (stun)", hit && t > 0.3f && t < 1.2f, "hit=" + hit + " time=" + t.ToString("F2") + " s stunned=" + b.Stunned
                + " hitLog=" + hitLog.Count + (hitLog.Count > 0 ? " victimIsB=" + (hitLog[0][0] == b) + " byIsA=" + (hitLog[0][1] == a) + " kind=" + hitKinds[0] : ""));

            yield return Reset();

            // 反彈:從路邊斜著射出去,應該撞牆反彈
            var a2 = MakeKart(42, StraightS - 20f, 0f);
            TrackSample sm = track.SampleAt(StraightS + 10f);
            Vector3 p0 = sm.Pos + sm.Right * 4f;
            Vector3 dir = (sm.Tangent + sm.Right * 0.7f).normalized;
            int b0 = ItemProjectiles.TotalBounces;
            ItemProjectiles.FireBumper(a2, p0, dir);
            yield return Run(1.5f);
            int bounces = ItemProjectiles.TotalBounces - b0;
            Report("I9b bumper bounces off track walls", bounces >= 1 && bounces <= ItemProjectiles.MaxBounces, "bounces=" + bounces);

            yield return Run(4f);
            int alive = ItemProjectiles.Count(ItemKind.Bumper);
            Report("I9c bumper expires (life 4 s / 3 bounces)", alive == 0, "alive after 5.5 s: " + alive);

            // 護盾擋直線彈 + 彈體消失
            yield return Reset();
            var a3 = MakeKart(43, StraightS, 0f);
            var b3 = MakeKart(44, StraightS + 20f, 0f);
            RaceItems.Give(b3, ItemKind.Shield); RaceItems.TryUse(b3, false);
            RaceItems.Give(a3, ItemKind.Bumper); RaceItems.TryUse(a3, false);
            yield return RunUntil(() => blockedLog.Count > 0, 2f);
            Report("I9d bumper blocked by shield", blockedLog.Count == 1 && !b3.Stunned && ItemProjectiles.Count(ItemKind.Bumper) == 0, "blocked=" + blockedLog.Count + " stunned=" + b3.Stunned + " bumpersLeft=" + ItemProjectiles.Count(ItemKind.Bumper));
            yield return Reset();
        }

        // ------------------------------------------------------------------ I10 追蹤彈

        IEnumerator TestHoming()
        {
            // (a) 前車靜止,在彎道後面 ~115 m,偏路右
            var a = MakeKart(50, StraightS, 0f, 2);
            var b = MakeKart(51, 70f, 3f, 1);
            RaceItems.Give(a, ItemKind.Homing);
            ClearLogs();
            bool fired = RaceItems.TryUse(a, false);
            bool locked = RaceItems.Targeted(b) && !RaceItems.Targeted(a);
            yield return RunUntil(() => hitLog.Count > 0, 8f);
            float t = runT;
            bool hit = hitLog.Count >= 1 && hitLog[0][0] == b && hitLog[0][1] == a && hitKinds[0] == ItemKind.Homing && b.Stunned;
            Report("I10a homing locks & hits the car ahead (static, past a corner)", fired && locked && hit, "fired=" + fired + " locked=" + locked + " hit=" + hit + " time=" + t.ToString("F2") + " s");
            yield return Reset();

            // (b) 前車由 AI 駕駛(Level 0)
            var a2 = MakeKart(52, StraightS - 10f, 0f, 2);
            var b2 = MakeKart(53, StraightS + 5f, 0f, 1);
            RaceAI.Spawn(b2, track, cfg, 1, null);
            b2.Frozen = false;
            yield return Run(1.5f);
            ClearLogs();
            RaceItems.Give(a2, ItemKind.Homing);
            RaceItems.TryUse(a2, false);
            float dist0 = Vector3.Distance(a2.Position, b2.Position);
            yield return RunUntil(() => hitLog.Count > 0, 8f);
            float t2 = runT;
            bool hit2 = hitLog.Count >= 1 && hitLog[0][0] == b2 && hitLog[0][1] == a2 && hitKinds[0] == ItemKind.Homing;
            Report("I10b homing catches a moving AI car", hit2, "hit=" + hit2 + " time=" + t2.ToString("F2") + " s, initial gap " + dist0.ToString("F0") + " m, target speed " + b2.Speed.ToString("F1") + " m/s");
            RaceAI.Detach(b2);
            yield return Reset();

            // (c) 領先者(沒有前車)發射:不爆、不報錯,彈最後消失
            var lead = MakeKart(54, StraightS, 0f, 1);
            RaceItems.Give(lead, ItemKind.Homing);
            RaceItems.TryUse(lead, false);
            yield return Run(0.5f);
            bool flying = ItemProjectiles.Count(ItemKind.Homing) == 1 && !RaceItems.Targeted(lead);
            yield return Run(8f);
            Report("I10c leader's homing has no target, flies on and expires", flying && ItemProjectiles.Count(ItemKind.Homing) == 0, "flying@0.5s=" + flying + " alive@8.5s=" + ItemProjectiles.Count(ItemKind.Homing));
            yield return Reset();
        }

        // ------------------------------------------------------------------ I11 無敵

        IEnumerator TestInvincible()
        {
            var k = MakeKart(60, StraightS, 0f);
            k.SetInvincible(3f);
            RaceItems.ApplyHit(k, 1f, null, ItemKind.Bumper);
            RaceItems.ApplyHit(k, 1.4f, null, ItemKind.Homing);
            RaceItems.ApplyHit(k, 0f, null, ItemKind.Oil);
            Report("I11a invincible kart ignores ApplyHit", !k.Stunned && hitLog.Count == 0 && blockedLog.Count == 0, "stunned=" + k.Stunned + " hits=" + hitLog.Count);

            yield return Reset();
            var a = MakeKart(61, StraightS, 0f);
            var b = MakeKart(62, StraightS + 18f, 0f);
            b.SetInvincible(5f);
            RaceItems.Give(a, ItemKind.Bumper); RaceItems.TryUse(a, false);
            yield return Run(1.2f);
            Report("I11b bumper passes through invincible kart", !b.Stunned && hitLog.Count == 0, "stunned=" + b.Stunned + " hits=" + hitLog.Count);

            // 被打中後 1.5 s 無敵:連續第二發無效
            yield return Reset();
            var c = MakeKart(63, StraightS, 0f);
            RaceItems.ApplyHit(c, 1f, null, ItemKind.Bumper);
            bool first = c.Stunned && c.Invincible;
            yield return Run(1.1f);
            bool second = false;
            RaceItems.ApplyHit(c, 1f, null, ItemKind.Bumper);
            second = hitLog.Count == 1;
            Report("I11c invincible right after being hit (no chain)", first && second, "firstOk=" + first + " secondIgnored=" + second);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I12 AI

        IEnumerator TestAiUse()
        {
            // 起跑線前可能有加速板(踩著就一直在加速,AI 規則本來就不會用):往前找沒加速板的直線再測
            RaceKart k = null; bool tooEarly = false, useOnStraight = false;
            float[] offs = { 0f, -120f, -240f, -360f, -480f, -600f };
            for (int oi = 0; oi < offs.Length && !useOnStraight; oi++)
            {
                yield return Reset();
                k = MakeKart(70, StraightS + offs[oi], 0f, 5);
                RaceItems.Give(k, ItemKind.Boost);
                tooEarly = !RaceItems.AiShouldUse(k);
                yield return Run(0.5f);
                if (k.Boosting) continue;
                useOnStraight = RaceItems.AiShouldUse(k);
            }
            Report("I12a AI uses boost on a straight (after short hold)", tooEarly && useOnStraight, "tooEarlyFalse=" + tooEarly + " straightTrue=" + useOnStraight + " boosting=" + k.Boosting);
            yield return Reset();
            var s = MakeKart(71, StraightS, 0f, 5);
            RaceItems.Give(s, ItemKind.Shield);
            yield return Run(3f);
            bool keeps = !RaceItems.AiShouldUse(s);
            var shooter = MakeKart(72, StraightS - 60f, 0f, 6);
            RaceItems.Give(shooter, ItemKind.Homing);
            RaceItems.Give(s, ItemKind.None);
            RaceItems.Give(s, ItemKind.Shield);
            RaceItems.TryUse(shooter, false);                    // 鎖定名次前一名的 s(rank 5 < 6)
            yield return Run(0.4f);
            bool locked = RaceItems.Targeted(s);
            RaceItems.Give(s, ItemKind.Shield);
            yield return Run(0.4f);
            bool reacts = RaceItems.AiShouldUse(s);
            Report("I12b AI keeps shield, uses it when targeted", keeps && locked && reacts, "keeps=" + keeps + " targeted=" + locked + " usesWhenTargeted=" + reacts);

            yield return Reset();
            var h = MakeKart(73, StraightS, 0f, 4);
            RaceItems.Give(h, ItemKind.Homing);
            yield return Run(0.4f);
            bool early = !RaceItems.AiShouldUse(h);
            yield return Run(3.0f);
            bool late = RaceItems.AiShouldUse(h);
            Report("I12c AI fires homing 1~3 s after getting it", early && late, "@0.4s=" + !early + " @3.4s=" + late);

            yield return Reset();
            var fa = MakeKart(74, StraightS, 0f, 2);
            var fb = MakeKart(75, StraightS + 25f, 0f, 1);
            RaceItems.Give(fa, ItemKind.Bumper);
            yield return Run(0.4f);
            bool aimBack;
            bool shoot = RaceItems.AiShouldUse(fa, out aimBack, 1f);
            Report("I12d AI shoots bumper at car dead ahead", shoot && !aimBack, "shoot=" + shoot + " aimBack=" + aimBack);
            yield return Reset();
        }


        // ------------------------------------------------------------------ I14 機率表規則

        IEnumerator TestTableRules()
        {
            bool nonZero = true; int kinds = 0; string miss = "";
            for (int i = 0; i < ItemTable.Order.Length; i++)
            {
                bool any = false;
                for (int r = 0; r < ItemTable.Rows; r++) if (ItemTable.Weights[r, i] > 0) any = true;
                if (any) kinds++; else { nonZero = false; miss += ItemTable.Order[i] + " "; }
            }
            Report("I14a every kind reachable, 23 kinds (15 original + feather/coin/fire flower/gold boost/super horn + triple banana/boo/boomerang)", nonZero && kinds == 23 && ItemTable.Order.Length == 23, "kinds=" + kinds + " missing=" + miss);

            bool zeros = true; string zd = "";
            ItemKind[] late = { ItemKind.Lightning, ItemKind.BlueShell, ItemKind.Bullet };
            for (int r = 0; r < 3; r++)
                for (int k = 0; k < late.Length; k++)
                    if (ItemTable.Weights[r, Array.IndexOf(ItemTable.Order, late[k])] != 0) { zeros = false; zd += "row" + r + ":" + late[k] + " "; }
            bool blueTail = ItemTable.Weights[4, Array.IndexOf(ItemTable.Order, ItemKind.BlueShell)] > ItemTable.Weights[3, Array.IndexOf(ItemTable.Order, ItemKind.BlueShell)];
            Report("I14b table: leaders/mid rows have 0% lightning/blueshell/bullet", zeros && blueTail, "violations=" + zd + " blueTailHeavier=" + blueTail);

            var rng = new System.Random(2026);
            int bad = 0, lastBlue = 0, star = 0, lastLight = 0;
            for (int n = 0; n < 8000; n++)
            {
                for (int place = 1; place <= 4; place++)
                {
                    ItemKind k = RaceItems.Roll(place, 8, rng);
                    if (k == ItemKind.Lightning || k == ItemKind.BlueShell || k == ItemKind.Bullet) bad++;
                }
                ItemKind l = RaceItems.Roll(8, 8, rng, true, false);
                if (l == ItemKind.BlueShell) lastBlue++;
                if (l == ItemKind.Star) star++;
                if (RaceItems.Roll(8, 8, rng) == ItemKind.Lightning) lastLight++;
            }
            Report("I14c rolls: places 1-4 never lightning/blueshell/bullet; blue shell cap honored", bad == 0 && lastBlue == 0 && star > 1000 && lastLight > 600,
                "leaderBad=" + bad + " blueWhenCapped=" + lastBlue + " starWhenCapped=" + star + " lastLightning=" + lastLight);

            var r1 = new System.Random(31); var r2 = new System.Random(31);
            bool same = true;
            for (int i = 0; i < 300; i++) if (RaceItems.Roll(1 + i % 8, 8, r1, true, true) != RaceItems.Roll(1 + i % 8, 8, r2, true, true)) same = false;
            Report("I14d fixed seed reproducible", same, "300 rolls identical");
            yield break;
        }

        // ------------------------------------------------------------------ I15 星星

        IEnumerator TestStar()
        {
            var a = MakeKart(100, StraightS, 0f, 3);
            var b = MakeKart(101, StraightS, 1.3f, 5);
            RaceItems.Give(a, ItemKind.Star);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            yield return Run(0.1f);
            bool inv = a.Invincible && RaceItems.EffectRemaining(a, ItemKind.Star) > 9.7f && effStart.Count == 1;
            bool spd = Mathf.Abs(a.SpeedMul - 1.2f) < 0.01f;
            RaceItems.ApplyHit(a, 1f, null, ItemKind.Bumper);
            bool immune = !a.Stunned;
            bool crush = b.Stunned && hitLog.Count >= 1 && hitLog[0][0] == b && hitLog[0][1] == a && hitKinds[0] == ItemKind.Star;
            Report("I15a star: invincible, +20% speed, stuns kart it touches", used && inv && spd && immune && crush,
                "used=" + used + " inv=" + a.Invincible + " left=" + RaceItems.EffectRemaining(a, ItemKind.Star).ToString("F1") + " speedMul=" + a.SpeedMul.ToString("F2") + " immune=" + immune + " bStunned=" + b.Stunned);

            yield return Run(10.2f);
            bool ended = RaceItems.EffectRemaining(a, ItemKind.Star) <= 0f && !a.Invincible && Mathf.Abs(a.SpeedMul - 1f) < 0.001f && CountEff(effEnd, ItemKind.Star) == 1;
            Report("I15b star lasts 10 s, then speed/invincibility restored", ended, "remaining=" + RaceItems.EffectRemaining(a, ItemKind.Star) + " inv=" + a.Invincible + " speedMul=" + a.SpeedMul.ToString("F3") + " endEvt=" + CountEff(effEnd, ItemKind.Star));

            // 護盾擋下星星一次
            yield return Reset();
            var c = MakeKart(102, StraightS, 0f, 3);
            var d = MakeKart(103, StraightS, 1.3f, 5);
            RaceItems.Give(d, ItemKind.Shield); RaceItems.TryUse(d, false);
            RaceItems.Give(c, ItemKind.Star); RaceItems.TryUse(c, false);
            yield return Run(0.3f);
            Report("I15c star blocked by shield (once, no instant re-hit)", blockedLog.Count == 1 && !d.Stunned && !RaceItems.Shielded(d), "blocked=" + blockedLog.Count + " stunned=" + d.Stunned);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I16 閃電

        IEnumerator TestLightning()
        {
            var a = MakeKart(110, StraightS, 0f, 5);
            var b = MakeKart(111, StraightS + 20f, 0f, 1);
            var c = MakeKart(112, StraightS + 10f, 3f, 3);
            var e = MakeKart(113, StraightS + 30f, -3f, 2);
            var d = MakeKart(114, StraightS - 15f, 0f, 7);
            RaceItems.Give(e, ItemKind.Shield); RaceItems.TryUse(e, false);
            RaceItems.Give(a, ItemKind.Lightning);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            yield return Run(0.5f);
            bool shrunk = RaceItems.KartScale(b) < 0.62f && RaceItems.KartScale(c) < 0.62f
                && Mathf.Abs(b.SpeedMul - ItemEffects.ShrinkSpeed) < 0.01f;
            bool visualScaled = b.transform.Find("Visual").localScale.x < 0.62f;
            bool untouched = RaceItems.KartScale(d) > 0.99f && RaceItems.KartScale(a) > 0.99f && Mathf.Abs(d.SpeedMul - 1f) < 0.001f;
            bool evts = hitLog.Count == 2 && hitKinds[0] == ItemKind.Lightning && hitKinds[1] == ItemKind.Lightning && CountEff(effStart, ItemKind.Lightning) == 2;
            bool shield = blockedLog.Count == 1 && blockedLog[0].Key == e && RaceItems.KartScale(e) > 0.99f;
            Report("I16a lightning shrinks + slows only karts ahead (shield blocks)", used && shrunk && visualScaled && untouched && evts && shield,
                "used=" + used + " scaleB=" + RaceItems.KartScale(b).ToString("F2") + " scaleC=" + RaceItems.KartScale(c).ToString("F2") + " speedB=" + b.SpeedMul.ToString("F2")
                + " behindUntouched=" + untouched + " hits=" + hitLog.Count + " blocked=" + blockedLog.Count);

            yield return Run(4.4f);
            bool back = RaceItems.KartScale(b) > 0.98f && Mathf.Abs(b.SpeedMul - 1f) < 0.001f && CountEff(effEnd, ItemKind.Lightning) == 2 && b.transform.Find("Visual").localScale.x > 0.98f;
            Report("I16b lightning ends after 4 s: size and speed recover", back, "scaleB=" + RaceItems.KartScale(b).ToString("F3") + " speedB=" + b.SpeedMul.ToString("F3") + " endEvts=" + CountEff(effEnd, ItemKind.Lightning));
            yield return Reset();
        }

        // ------------------------------------------------------------------ I17 炸彈

        Vector3 OnTrack(float s, float lat = 0f)
        {
            TrackSample sm = track.SampleAt(track.Wrap(s));
            return sm.Pos + sm.Right * lat;
        }

        IEnumerator TestBomb()
        {
            var a = MakeKart(120, StraightS - 30f, 0f, 3);
            Vector3 bp = OnTrack(StraightS + 10f);
            var near1 = MakeKart(121, StraightS + 14f, 0f, 2);
            var near2 = MakeKart(122, StraightS + 4.5f, 0f, 4);
            var far = MakeKart(123, StraightS + 19f, 0f, 1);
            ClearLogs();
            var bomb = ItemProjectiles.PlaceBomb(a, bp);
            float d1 = Vector3.Distance(new Vector3(near1.Position.x, 0, near1.Position.z), new Vector3(bomb.Pos.x, 0, bomb.Pos.z));
            float d2 = Vector3.Distance(new Vector3(near2.Position.x, 0, near2.Position.z), new Vector3(bomb.Pos.x, 0, bomb.Pos.z));
            float d3 = Vector3.Distance(new Vector3(far.Position.x, 0, far.Position.z), new Vector3(bomb.Pos.x, 0, bomb.Pos.z));
            yield return RunUntil(() => explodeLog.Count > 0, 4f);
            float t = runT;
            bool timing = t > 2.9f && t < 3.2f;
            bool inside = near1.Stunned && near2.Stunned && !far.Stunned && !a.Stunned;
            bool evt = hitLog.Count == 2 && hitKinds[0] == ItemKind.Bomb && hitKinds[1] == ItemKind.Bomb && explodeLog.Count == 1
                && Mathf.Abs(explodeLog[0].Key - 6f) < 0.01f && explodeLog[0].Value == ItemKind.Bomb && ItemProjectiles.Count(ItemKind.Bomb) == 0;
            Report("I17a bomb explodes after 3 s: 6 m radius stuns, outside unaffected", timing && inside && evt,
                "t=" + t.ToString("F2") + " d(near1,near2,far)=" + d1.ToString("F1") + "/" + d2.ToString("F1") + "/" + d3.ToString("F1")
                + " stunned=" + near1.Stunned + "," + near2.Stunned + "," + far.Stunned + " hits=" + hitLog.Count);
            yield return Reset();

            // 被碰就爆
            var o = MakeKart(124, StraightS - 30f, 0f, 3);
            var v = MakeKart(125, StraightS, 0f, 2);
            ItemProjectiles.PlaceBomb(o, v.Position - Vector3.up * 0.3f);
            ClearLogs();
            yield return RunUntil(() => explodeLog.Count > 0, 1f);
            Report("I17b bomb triggers on touch immediately", explodeLog.Count == 1 && runT > 0f && runT < 0.2f && v.Stunned, "t=" + runT.ToString("F2") + " stunned=" + v.Stunned);
            yield return Reset();

            // 使用:預設往前拋、AimBack 丟車後;落地前不爆
            var u = MakeKart(126, StraightS, 0f, 4);
            Vector3 fwd = u.Forward; fwd.y = 0f; fwd.Normalize();
            RaceItems.Give(u, ItemKind.Bomb);
            bool used = RaceItems.TryUse(u, false);
            yield return Run(0.6f);
            float ahead = 0f; bool found = false;
            for (int i = 0; i < RaceItems.Hazards.Count; i++) if (RaceItems.Hazards[i].Kind == ItemKind.Bomb) { ahead = Vector3.Dot(RaceItems.Hazards[i].Pos - u.Position, fwd); found = true; }
            Report("I17c bomb thrown forward ~10 m by default", used && found && ahead > 8.5f && ahead < 11.5f && usedLog.Count == 1 && usedLog[0].Value == ItemKind.Bomb, "found=" + found + " ahead=" + ahead.ToString("F1"));
            yield return Reset();
            var u2 = MakeKart(127, StraightS, 0f, 4);
            fwd = u2.Forward; fwd.y = 0f; fwd.Normalize();
            RaceItems.Give(u2, ItemKind.Bomb);
            RaceItems.TryUse(u2, true);
            yield return Run(0.6f);
            float back = 0f; found = false;
            for (int i = 0; i < RaceItems.Hazards.Count; i++) if (RaceItems.Hazards[i].Kind == ItemKind.Bomb) { back = Vector3.Dot(RaceItems.Hazards[i].Pos - u2.Position, fwd); found = true; }
            Report("I17d bomb AimBack drops behind", found && back < -2f && back > -4.5f, "found=" + found + " along=" + back.ToString("F1"));
            yield return Reset();
        }

        // ------------------------------------------------------------------ I18 藍殼

        IEnumerator TestBlueShell()
        {
            yield return Reset();
            var a = MakeKart(130, StraightS, 0f, 8);
            var mid = MakeKart(131, StraightS + 60f, 0f, 4);
            var first = MakeKart(132, StraightS + 150f, 2f, 1);
            bool capBefore = RaceItems.BlueShellAllowed;   // 手上拿著的藍殼也算「在場」,所以要在 Give 之前量
            RaceItems.Give(a, ItemKind.BlueShell);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            yield return Run(0.1f);
            bool lockedFirst = RaceItems.Targeted(first) && !RaceItems.Targeted(mid);
            bool cap = capBefore && !RaceItems.BlueShellAllowed && RaceItems.BlueShellInPlay() == 1;
            yield return RunUntil(() => first.Stunned, 8f);
            float t = runT;
            bool hit = first.Stunned && hitLog.Count >= 1 && hitLog[0][0] == first && hitLog[0][1] == a && hitKinds[0] == ItemKind.BlueShell;
            bool gone = ItemProjectiles.Count(ItemKind.BlueShell) == 0 && explodeLog.Count == 1 && explodeLog[0].Value == ItemKind.BlueShell;
            Report("I18a blue shell follows the track spline and hits 1st place", used && lockedFirst && cap && hit && gone && !mid.Stunned && t > 1.5f && t < 6f,
                "used=" + used + " lockedFirst=" + lockedFirst + " cap=" + cap + " (capBefore=" + capBefore + " inPlayNow=" + RaceItems.BlueShellInPlay() + ") hit=" + hit + " time=" + t.ToString("F2") + " s, midStunned=" + mid.Stunned + " explode=" + explodeLog.Count);
            yield return Reset();

            // 護盾擋藍殼
            var a2 = MakeKart(133, StraightS, 0f, 8);
            var f2 = MakeKart(134, StraightS + 100f, 0f, 1);
            RaceItems.Give(f2, ItemKind.Shield); RaceItems.TryUse(f2, false);
            RaceItems.Give(a2, ItemKind.BlueShell); RaceItems.TryUse(a2, false);
            yield return RunUntil(() => explodeLog.Count > 0, 6f);
            Report("I18b shield blocks blue shell blast", blockedLog.Count == 1 && !f2.Stunned && hitLog.Count == 0, "blocked=" + blockedLog.Count + " stunned=" + f2.Stunned);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I19 巨大化

        IEnumerator TestMega()
        {
            var a = MakeKart(140, StraightS, 0f, 4);
            var b = MakeKart(141, StraightS, 1.8f, 5);
            var c = MakeKart(142, StraightS + 8f, 0f, 6);
            RaceItems.Give(a, ItemKind.Mega);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            yield return Run(0.6f);
            bool big = RaceItems.KartScale(a) > 2.1f && a.transform.Find("Visual").localScale.x > 2.1f && Mathf.Abs(a.SpeedMul - 1.15f) < 0.01f && a.Invincible;
            bool squashed = b.Stunned && hitLog.Count == 1 && hitLog[0][0] == b && hitKinds[0] == ItemKind.Mega && ItemEffects.SquashOf(b) < 0.5f && !c.Stunned;
            Report("I19a mega: 2.2x size, +15% speed, crushes touched kart", used && big && squashed,
                "scaleA=" + RaceItems.KartScale(a).ToString("F2") + " speedMul=" + a.SpeedMul.ToString("F2") + " bStunned=" + b.Stunned + " squashB=" + ItemEffects.SquashOf(b).ToString("F2") + " farStunned=" + c.Stunned);
            yield return Run(8.2f);
            bool ended = RaceItems.KartScale(a) < 1.02f && Mathf.Abs(a.SpeedMul - 1f) < 0.001f && CountEff(effEnd, ItemKind.Mega) == 1 && !a.Invincible;
            Report("I19b mega ends after 8 s: size/speed restored", ended, "scaleA=" + RaceItems.KartScale(a).ToString("F3") + " speedMul=" + a.SpeedMul.ToString("F3") + " inv=" + a.Invincible);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I20 子彈加速

        IEnumerator TestBullet()
        {
            var a = MakeKart(150, StraightS - 60f, 0f, 8);
            a.Frozen = false;
            var inp = new ConstInput { Steer = 1f, Throttle = 0f };   // 玩家一直打右、不給油 → 若沒被覆蓋會原地繞圈
            a.Source = inp;
            Transform vis = a.transform.Find("Visual");
            RaceItems.Give(a, ItemKind.Bullet);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            yield return Run(0.1f);
            bool took = a.Source is BulletInput && a.Invincible && vis != null && !vis.gameObject.activeSelf && CountEff(effStart, ItemKind.Bullet) == 1;
            float lat; float s0 = track.Project(a.Position, -1f, out lat);
            float maxLat = 0f; float maxSpeed = 0f;
            for (int i = 0; i < 6; i++)
            {
                yield return Run(0.5f);
                float l; track.Project(a.Position, -1f, out l);
                maxLat = Mathf.Max(maxLat, Mathf.Abs(l)); maxSpeed = Mathf.Max(maxSpeed, a.Speed);
            }
            float s1 = track.Project(a.Position, -1f, out lat);
            float adv = s1 - s0; if (adv < 0f) adv += track.Length;
            bool driving = maxSpeed > 20f && adv > 50f && maxLat < track.HalfWidthAt(s1) + 1.5f;
            Report("I20a bullet takes over input: drives the centerline at high speed", used && took && driving,
                "took=" + took + " maxSpeed=" + maxSpeed.ToString("F1") + " m/s advance=" + adv.ToString("F0") + " m maxLat=" + maxLat.ToString("F1") + " m");

            // 此刻約 3.1 s;到 6.5 s 進入歸還段:輸入已混合、仍無敵、外觀恢復
            yield return Run(3.4f);
            bool blendPhase = a.Source is BulletInput && a.Invincible && vis.gameObject.activeSelf && RaceItems.EffectRemaining(a, ItemKind.Bullet) <= 0f;
            yield return Run(1.3f);
            bool returned = ReferenceEquals(a.Source, inp) && Mathf.Abs(a.SpeedMul - 1f) < 0.001f && CountEff(effEnd, ItemKind.Bullet) == 1;
            Report("I20b bullet ends after 6 s + 1 s blend, input handed back", blendPhase && returned,
                "blendPhase=" + blendPhase + " returned=" + returned + " speedMul=" + a.SpeedMul.ToString("F3") + " srcIsOrig=" + ReferenceEquals(a.Source, inp));
            yield return Run(0.6f);
            Report("I20c invincibility expires shortly after handback", !a.Invincible, "inv=" + a.Invincible);
            a.Frozen = true;
            yield return Reset();
        }

        // ------------------------------------------------------------------ I21 假道具箱

        IEnumerator TestFakeBox()
        {
            var a = MakeKart(160, StraightS, 0f, 3);
            RaceItems.Give(a, ItemKind.FakeBox);
            bool used = RaceItems.TryUse(a, false);
            RaceItems.Tick(Dt);
            Projectile fake = null;
            for (int i = 0; i < ItemProjectiles.All.Count; i++) if (ItemProjectiles.All[i].Kind == ItemKind.FakeBox) fake = ItemProjectiles.All[i];
            ItemBox vb = fake != null && fake.Go != null ? fake.Go.GetComponent<ItemBox>() : null;
            bool looks = vb != null && vb.IsFake && vb.Active && !(RaceItems.Boxes as IList<ItemBox>).Contains(vb);
            Vector3 f = a.Forward; f.y = 0f; f.Normalize();
            float behind = fake != null ? Vector3.Dot(fake.Pos - a.Position, f) : 0f;
            Report("I21a fake box: item-box look-alike dropped behind", used && looks && behind < -2f, "used=" + used + " lookalike=" + looks + " along=" + behind.ToString("F1"));

            ClearLogs();
            var v = MakeKart(161, StraightS, 0f, 5);
            v.Teleport(fake.Pos - Vector3.up * 0.5f, v.transform.rotation);
            yield return Run(0.1f);
            Report("I21b kart touching fake box is stunned (Hit event), box consumed", v.Stunned && hitLog.Count == 1 && hitKinds[0] == ItemKind.FakeBox && hitLog[0][1] == a && ItemProjectiles.Count(ItemKind.FakeBox) == 0,
                "stunned=" + v.Stunned + " hits=" + hitLog.Count + " left=" + ItemProjectiles.Count(ItemKind.FakeBox));
            yield return Reset();

            // 護盾擋 + 向前丟 14 m
            var b = MakeKart(162, StraightS, 0f, 3);
            RaceItems.Give(b, ItemKind.FakeBox);
            RaceItems.TryUse(b, true);
            RaceItems.Tick(Dt);
            Vector3 fb = b.Forward; fb.y = 0f; fb.Normalize();
            float aheadD = 0f; Vector3 fp = Vector3.zero;
            for (int i = 0; i < ItemProjectiles.All.Count; i++) if (ItemProjectiles.All[i].Kind == ItemKind.FakeBox) { fp = ItemProjectiles.All[i].Pos; aheadD = Vector3.Dot(fp - b.Position, fb); }
            var sh = MakeKart(163, StraightS + 14f, 0f, 5);
            RaceItems.Give(sh, ItemKind.Shield); RaceItems.TryUse(sh, false);
            ClearLogs();
            yield return Run(0.2f);
            Report("I21c fake box AimBack throws ~14 m ahead; shield blocks it", aheadD > 12f && aheadD < 16f && blockedLog.Count == 1 && !sh.Stunned, "ahead=" + aheadD.ToString("F1") + " blocked=" + blockedLog.Count);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I22 三連直線彈

        IEnumerator TestTripleBumper()
        {
            var a = MakeKart(170, StraightS, 0f, 2);
            var b = MakeKart(171, StraightS + 25f, 0f, 1);
            RaceItems.Give(a, ItemKind.TripleBumper);
            yield return null;
            RaceItems.Tick(Dt);
            Transform orbit = a.transform.Find("BumperOrbit");
            int active = 0;
            if (orbit != null) for (int i = 0; i < orbit.childCount; i++) if (orbit.GetChild(i).gameObject.activeSelf) active++;
            bool c3 = RaceItems.Charges(a) == 3 && orbit != null && active == 3;
            ClearLogs();
            bool u1 = RaceItems.TryUse(a, false);
            bool gap = !RaceItems.TryUse(a, false);
            RaceItems.Tick(Dt);
            int active2 = 0;
            for (int i = 0; i < orbit.childCount; i++) if (orbit.GetChild(i).gameObject.activeSelf) active2++;
            bool c2 = RaceItems.Charges(a) == 2 && ItemProjectiles.Count(ItemKind.Bumper) == 1 && active2 == 2;
            yield return RunUntil(() => hitLog.Count > 0, 2f);
            bool hit = hitLog.Count == 1 && hitLog[0][0] == b && hitKinds[0] == ItemKind.Bumper;
            yield return Run(0.3f);
            bool u2 = RaceItems.TryUse(a, true);              // 第二發往後
            RaceItems.Tick(Dt);
            Vector3 f = a.Forward; f.y = 0f; f.Normalize();
            bool backward = false;
            for (int i = 0; i < ItemProjectiles.All.Count; i++) if (ItemProjectiles.All[i].Kind == ItemKind.Bumper && Vector3.Dot(ItemProjectiles.All[i].Dir, f) < -0.5f) backward = true;
            yield return Run(0.3f);
            bool u3 = RaceItems.TryUse(a, false);
            yield return null;
            RaceItems.Tick(Dt);
            bool empty = RaceItems.Held(a) == ItemKind.None && a.transform.Find("BumperOrbit") == null && usedLog.Count == 3;
            Report("I22 triple bumper: 3 orbiting balls, fired one by one (forward/back), orbit removed", c3 && u1 && gap && c2 && hit && u2 && backward && u3 && empty,
                "orbit3=" + c3 + " u1=" + u1 + " gapBlocks=" + gap + " after1=" + c2 + " hit=" + hit + " u2=" + u2 + " backward=" + backward + " u3=" + u3 + " empty=" + empty);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I23 墨水

        IEnumerator TestInk()
        {
            var a = MakeKart(180, StraightS, 0f, 5);
            var pl = MakeKart(181, StraightS + 20f, 0f, 1); pl.IsPlayer = true;
            var ai = MakeKart(182, StraightS + 10f, 3f, 3);
            var d = MakeKart(183, StraightS - 15f, 0f, 7);
            var sh = MakeKart(184, StraightS + 30f, -3f, 2);
            RaceItems.Give(sh, ItemKind.Shield); RaceItems.TryUse(sh, false);
            RaceItems.Give(a, ItemKind.InkBlast);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            yield return Run(0.2f);
            bool inked = RaceItems.InkRemaining(pl) > 2.7f && RaceItems.InkRemaining(ai) > 2.7f && RaceItems.InkRemaining(d) <= 0f && RaceItems.InkRemaining(a) <= 0f;
            bool nostun = !pl.Stunned && !ai.Stunned;
            bool evts = hitLog.Count == 2 && hitKinds[0] == ItemKind.InkBlast && CountEff(effStart, ItemKind.InkBlast) == 2 && blockedLog.Count == 1 && RaceItems.InkRemaining(sh) <= 0f;
            Report("I23a ink blast hits all karts ahead for 3 s (player = screen ink, AI = less grip; shield blocks)", used && inked && nostun && evts,
                "inkPlayer=" + RaceItems.InkRemaining(pl).ToString("F2") + " inkAI=" + RaceItems.InkRemaining(ai).ToString("F2") + " behind=" + RaceItems.InkRemaining(d) + " hits=" + hitLog.Count + " blocked=" + blockedLog.Count);
            yield return Run(3.0f);
            Report("I23b ink wears off after 3 s", RaceItems.InkRemaining(pl) <= 0f && RaceItems.InkRemaining(ai) <= 0f && CountEff(effEnd, ItemKind.InkBlast) == 2, "endEvts=" + CountEff(effEnd, ItemKind.InkBlast));
            yield return Reset();
        }

        // ------------------------------------------------------------------ I24 新道具的 AI 使用

        IEnumerator TestAiNew()
        {
            bool ab;
            var a = MakeKart(190, StraightS, 0f, 6);
            var ahead = MakeKart(191, StraightS + 25f, 0f, 2);
            ItemKind[] instant = { ItemKind.Star, ItemKind.BlueShell, ItemKind.Bullet, ItemKind.Lightning, ItemKind.InkBlast };
            string res = ""; bool allInstant = true;
            for (int i = 0; i < instant.Length; i++)
            {
                RaceItems.Give(a, ItemKind.None);
                RaceItems.Give(a, instant[i]);
                bool early = RaceItems.AiShouldUse(a, out ab, 1f);
                yield return Run(0.6f);
                bool ok = RaceItems.AiShouldUse(a, out ab, 1f);
                res += instant[i] + ":" + ok + " ";
                if (early || !ok) allInstant = false;
            }
            Report("I24a AI uses star/blue shell/bullet/lightning/ink (not before 0.3 s)", allInstant, res);

            yield return Reset();
            var lead = MakeKart(192, StraightS, 0f, 1);
            RaceItems.Give(lead, ItemKind.Lightning);
            yield return Run(0.6f);
            bool wasteSafe = !RaceItems.AiShouldUse(lead, out ab, 1f);
            Report("I24b AI does not waste lightning when nobody is ahead", wasteSafe, "usesWhenAlone=" + !wasteSafe);

            yield return Reset();
            var k = MakeKart(193, StraightS, 0f, 4);
            var f1 = MakeKart(194, StraightS + 10f, 0f, 3);
            RaceItems.Give(k, ItemKind.Bomb);
            yield return Run(0.5f);
            bool bombFwd = RaceItems.AiShouldUse(k, out ab, 1f) && !ab;
            yield return Reset();
            var k2 = MakeKart(195, StraightS, 0f, 4);
            var bk = MakeKart(196, StraightS - 6f, 0f, 5);
            RaceItems.Give(k2, ItemKind.Bomb);
            yield return Run(0.5f);
            bool bombBack = RaceItems.AiShouldUse(k2, out ab, 1f) && ab;
            yield return Reset();
            var k3 = MakeKart(197, StraightS, 0f, 4);
            var bk3 = MakeKart(198, StraightS - 15f, 0f, 5);
            RaceItems.Give(k3, ItemKind.FakeBox);
            yield return Run(0.5f);
            bool fakeUse = RaceItems.AiShouldUse(k3, out ab, 1f);
            yield return Reset();
            var k4 = MakeKart(199, StraightS, 0f, 4);
            var f4 = MakeKart(200, StraightS + 25f, 0f, 3);
            RaceItems.Give(k4, ItemKind.TripleBumper);
            yield return Run(0.5f);
            bool tbFwd = RaceItems.AiShouldUse(k4, out ab, 1f) && !ab;
            yield return Reset();
            var k5 = MakeKart(201, StraightS, 0f, 4);
            var n5 = MakeKart(202, StraightS + 6f, 0f, 3);
            RaceItems.Give(k5, ItemKind.Mega);
            yield return Run(0.5f);
            bool megaUse = RaceItems.AiShouldUse(k5, out ab, 1f);
            Report("I24c AI aims bomb/fake box/triple bumper and uses mega near rivals", bombFwd && bombBack && fakeUse && tbFwd && megaUse,
                "bombFwd=" + bombFwd + " bombBack=" + bombBack + " fakeBehind=" + fakeUse + " tripleBumperFwd=" + tbFwd + " megaNear=" + megaUse);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I25 效果清理

        IEnumerator TestEffectCleanup()
        {
            var a = MakeKart(210, StraightS, 0f, 5);
            var inp = new ConstInput();
            a.Source = inp;
            Transform vis = a.transform.Find("Visual");
            RaceItems.Give(a, ItemKind.Bullet); RaceItems.TryUse(a, false);
            RaceItems.Give(a, ItemKind.Mega); RaceItems.TryUse(a, false);
            yield return Run(0.5f);
            bool active = !(a.Source == (IKartInput)inp) && vis.localScale.x > 1.5f && !vis.gameObject.activeSelf && Mathf.Abs(a.SpeedMul - 1f) > 0.1f;
            ItemEffects.Shutdown();
            ItemEffects.Init(track);
            bool restored = ReferenceEquals(a.Source, inp) && vis.gameObject.activeSelf && Mathf.Abs(vis.localScale.x - 1f) < 0.001f && Mathf.Abs(a.SpeedMul - 1f) < 0.001f;
            Report("I25 effect Shutdown restores input source, visual scale/visibility and speed", active && restored, "active=" + active + " restored=" + restored);
            a.Source = null;
            yield return Reset();
        }

        // ------------------------------------------------------------------ I26 使用道具回饋 + i-frames

        IEnumerator TestUseFx()
        {
            Report("I26e default UseInvincibleSeconds is 0 (item use grants no i-frames)", oldUseInv == 0f, "default=" + oldUseInv);
            float want = 0.4f;
            RaceItems.UseInvincibleSeconds = want;       // 這個測試自己明確開 i-frames
            RaceItems.UseFxEnabled = true;
            yield return Reset();

            // a) 一般道具:每種使用後都有 i-frames(>= want)、回饋計數 +1、種類 / 使用者正確
            var kinds = new[] { ItemKind.Boost, ItemKind.TripleBoost, ItemKind.Shield, ItemKind.Oil, ItemKind.Bumper, ItemKind.Homing, ItemKind.Bomb, ItemKind.FakeBox, ItemKind.InkBlast, ItemKind.TripleBumper };
            var sbFail = new StringBuilder();
            for (int i = 0; i < kinds.Length; i++)
            {
                var k = MakeKart(260 + i, StraightS, 0f, 3);
                RaceItems.Give(k, kinds[i]);
                int before = RaceItems.UseFxCount;
                bool used = RaceItems.TryUse(k, false);
                bool inv = k.Invincible;
                bool fx = RaceItems.UseFxCount == before + 1 && RaceItems.UseFxLastKind == kinds[i] && RaceItems.UseFxLastKart == k;
                if (!used || !inv || !fx) sbFail.Append(kinds[i]).Append("(used=").Append(used).Append(" inv=").Append(inv).Append(" fx=").Append(fx).Append(") ");
                yield return Reset();
            }
            Report("I26a every item use grants i-frames and records a use-FX event", sbFail.Length == 0, sbFail.Length == 0 ? "all " + kinds.Length + " kinds ok" : sbFail.ToString());

            // b) 無敵期間 ApplyHit 被忽略(護盾也不被消耗),過期後恢復;Boost 的 i-frames 約 0.4 s
            var s = MakeKart(280, StraightS, 0f, 3);
            RaceItems.Give(s, ItemKind.Shield); RaceItems.TryUse(s, false);
            ClearLogs();
            RaceItems.ApplyHit(s, 1f, null, ItemKind.Bumper);
            bool ignored = !s.Stunned && blockedLog.Count == 0 && RaceItems.Shielded(s);
            yield return Run(want + 0.1f);
            bool expired = !s.Invincible;
            RaceItems.ApplyHit(s, 1f, null, ItemKind.Bumper);
            bool shieldNow = blockedLog.Count == 1 && !s.Stunned && !RaceItems.Shielded(s);
            Report("I26b i-frames ignore hits (shield kept), expire after UseInvincibleSeconds, then shield blocks again", ignored && expired && shieldNow,
                "ignored=" + ignored + " expired=" + expired + " shieldBlocksAfter=" + shieldNow);
            yield return Reset();

            // c) 星星 / 巨大化的無敵不被縮短(取 max)
            var st = MakeKart(281, StraightS, 0f, 3);
            RaceItems.Give(st, ItemKind.Star); RaceItems.TryUse(st, false);
            yield return Run(want + 0.1f);
            bool starLong = st.Invincible;
            yield return Reset();
            var mg = MakeKart(282, StraightS, 0f, 3);
            RaceItems.Give(mg, ItemKind.Mega); RaceItems.TryUse(mg, false);
            yield return Run(want + 0.1f);
            bool megaLong = mg.Invincible;
            yield return Reset();
            Report("I26c star / mega invincibility is not shortened by the use window", starLong && megaLong, "star=" + starLong + " mega=" + megaLong);

            // d) UseFxEnabled = false:不記錄回饋、但 i-frames 仍在;UseInvincibleSeconds = 0:沒有 i-frames
            RaceItems.UseFxEnabled = false;
            var f1 = MakeKart(283, StraightS, 0f, 3);
            int c0 = RaceItems.UseFxCount;
            RaceItems.Give(f1, ItemKind.Boost); RaceItems.TryUse(f1, false);
            bool noFx = RaceItems.UseFxCount == c0 && f1.Invincible;
            RaceItems.UseFxEnabled = true;
            yield return Reset();
            RaceItems.UseInvincibleSeconds = 0f;
            var f2 = MakeKart(284, StraightS, 0f, 3);
            RaceItems.Give(f2, ItemKind.Boost); RaceItems.TryUse(f2, false);
            bool noInv = !f2.Invincible && RaceItems.UseFxCount == c0 + 1;
            RaceItems.UseInvincibleSeconds = want;
            Report("I26d UseFxEnabled=false skips FX only; UseInvincibleSeconds=0 skips i-frames only", noFx && noInv, "noFx=" + noFx + " noInv=" + noInv);
            RaceItems.UseInvincibleSeconds = 0f;       // 還給後面的測試
            yield return Reset();
        }

        // ------------------------------------------------------------------ 2026-10-23 瑪利歐賽車世界式道具

        static Vector3 FlatV(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }

        static Projectile FindProj(ItemKind kind)
        {
            for (int i = 0; i < ItemProjectiles.All.Count; i++) if (!ItemProjectiles.All[i].Dead && ItemProjectiles.All[i].Kind == kind) return ItemProjectiles.All[i];
            return null;
        }

        // I27 兩格道具
        IEnumerator TestTwoSlots()
        {
            var k = MakeKart(300, StraightS, 0f, 4);
            RaceItems.Give(k, ItemKind.Bumper);
            bool oneFree = RaceItems.SlotsFree(k) == 1;
            ClearLogs();
            RaceItems.OnBoxHit(k);
            bool rolls = RaceItems.Rolling(k) && RaceItems.Held(k) == ItemKind.Bumper && rollStarts == 1;
            yield return Run(1.3f);
            bool filled = !RaceItems.Rolling(k) && RaceItems.Held(k) == ItemKind.Bumper && RaceItems.HeldSecond(k) != ItemKind.None && RaceItems.SlotsFree(k) == 0;
            RaceItems.OnBoxHit(k);
            bool ignoredWhenFull = !RaceItems.Rolling(k);
            Report("I27a slot 1 full: box rolls into slot 2; both full: box ignored", oneFree && rolls && filled && ignoredWhenFull,
                "oneFree=" + oneFree + " rolls=" + rolls + " slot2=" + RaceItems.HeldSecond(k) + " ignoredWhenFull=" + ignoredWhenFull);
            yield return Reset();

            var a = MakeKart(301, StraightS, 0f, 4);
            RaceItems.Give(a, ItemKind.Boost); RaceItems.GiveSecond(a, ItemKind.Shield);
            bool swapped = RaceItems.Swap(a) && RaceItems.Held(a) == ItemKind.Shield && RaceItems.HeldSecond(a) == ItemKind.Boost;
            RaceItems.Swap(a);
            bool used = RaceItems.TryUse(a, false);
            bool shifted = used && a.Boosting && RaceItems.Held(a) == ItemKind.Shield && RaceItems.HeldSecond(a) == ItemKind.None && RaceItems.SlotsFree(a) == 1;
            bool noSwapSingle = !RaceItems.Swap(a);
            Report("I27b swap exchanges slots; using slot 1 shifts slot 2 up", swapped && shifted && noSwapSingle, "swapped=" + swapped + " shifted=" + shifted + " noSwapWithOneItem=" + noSwapSingle);
            yield return Reset();

            var t = MakeKart(302, StraightS, 0f, 4);
            RaceItems.Give(t, ItemKind.TripleBoost); RaceItems.GiveSecond(t, ItemKind.Shield);
            RaceItems.TryUse(t, false);
            bool partial = RaceItems.Held(t) == ItemKind.TripleBoost && RaceItems.Charges(t) == 2 && RaceItems.HeldSecond(t) == ItemKind.Shield;
            Report("I27c multi-charge item keeps slot 1 until empty", partial, "held=" + RaceItems.Held(t) + " charges=" + RaceItems.Charges(t) + " slot2=" + RaceItems.HeldSecond(t));
            yield return Reset();

            // 交換鍵(SwapHeld,level)只在上升緣換一次
            var w = MakeKart(303, StraightS, 0f, 4);
            var inp = new ConstInput { Steer = 0f, Throttle = 0f };
            w.Source = inp; w.Frozen = false;
            RaceItems.Give(w, ItemKind.Boost); RaceItems.GiveSecond(w, ItemKind.Shield);
            inp.Swap = true;
            yield return Run(0.3f);
            bool once = RaceItems.Held(w) == ItemKind.Shield && RaceItems.HeldSecond(w) == ItemKind.Boost;
            inp.Swap = false;
            yield return Run(0.1f);
            inp.Swap = true;
            yield return Run(0.1f);
            bool twice = RaceItems.Held(w) == ItemKind.Boost && RaceItems.HeldSecond(w) == ItemKind.Shield;
            inp.Swap = false; w.Source = null;
            Report("I27d swap key swaps once per press (rising edge)", once && twice, "afterFirstPress(held=Shield)=" + once + " afterSecondPress(held=Boost)=" + twice);
            yield return Reset();
        }

        // I28 任意方向瞄準
        IEnumerator TestAim()
        {
            var a = MakeKart(310, StraightS, 0f, 3);
            Vector3 fwd = FlatV(a.Forward); Vector3 right = Vector3.Cross(Vector3.up, fwd);
            RaceItems.Give(a, ItemKind.Bumper); RaceItems.TryUse(a, false, true, 90f);
            Projectile p = FindProj(ItemKind.Bumper);
            bool r90 = p != null && Vector3.Dot(p.Dir, right) > 0.97f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(a, ItemKind.Bumper); RaceItems.TryUse(a, false, true, 315f);
            p = FindProj(ItemKind.Bumper);
            bool l45 = p != null && Vector3.Dot(p.Dir, fwd) > 0.6f && Vector3.Dot(p.Dir, right) < -0.6f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(a, ItemKind.Bumper); RaceItems.TryUse(a, false);
            p = FindProj(ItemKind.Bumper);
            bool def = p != null && Vector3.Dot(p.Dir, fwd) > 0.97f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(a, ItemKind.Bumper); RaceItems.TryUse(a, true);
            p = FindProj(ItemKind.Bumper);
            bool back = p != null && Vector3.Dot(p.Dir, fwd) < -0.97f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(a, ItemKind.Bumper); RaceItems.TryUse(a, false, true, 180f);
            p = FindProj(ItemKind.Bumper);
            bool back180 = p != null && Vector3.Dot(p.Dir, fwd) < -0.97f;
            Report("I28a bumper: default forward, AimBack = 180 deg, any angle (90 right / 315 left-forward)", r90 && l45 && def && back && back180,
                "right90=" + r90 + " left45=" + l45 + " default=" + def + " aimBack=" + back + " angle180=" + back180);
            yield return Reset();

            var o = MakeKart(311, StraightS, 0f, 3);
            fwd = FlatV(o.Forward); right = Vector3.Cross(Vector3.up, fwd);
            RaceItems.Give(o, ItemKind.Oil); RaceItems.TryUse(o, false, true, 90f);
            RaceItems.Tick(Dt);
            Projectile oil = FindProj(ItemKind.Oil);
            Vector3 d = oil != null ? oil.Pos - o.Position : Vector3.zero;
            float lat = Vector3.Dot(d, right), along = Vector3.Dot(d, fwd);
            bool oilSide = oil != null && lat > 4f && lat < 6.5f && Mathf.Abs(along) < 1f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(o, ItemKind.FakeBox); RaceItems.TryUse(o, false, true, 90f);
            Projectile fake = FindProj(ItemKind.FakeBox);
            float flat = fake != null ? Vector3.Dot(fake.Pos - o.Position, right) : 0f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(o, ItemKind.Bomb); RaceItems.TryUse(o, false, true, 270f);
            Projectile bomb = FindProj(ItemKind.Bomb);
            float blat = bomb != null ? Vector3.Dot(bomb.To - o.Position, right) : 0f;
            ItemProjectiles.ClearAll();
            RaceItems.Give(o, ItemKind.Homing); RaceItems.TryUse(o, false, true, 90f);
            Projectile hm = FindProj(ItemKind.Homing);
            bool homingDir = hm != null && Vector3.Dot(hm.Dir, right) > 0.97f;
            Report("I28b oil / fake box / bomb / homing thrown sideways when aimed", oilSide && flat > 6f && blat < -5f && homingDir,
                "oilLat=" + lat.ToString("F1") + " along=" + along.ToString("F1") + " fakeLat=" + flat.ToString("F1") + " bombLat=" + blat.ToString("F1") + " homingRight=" + homingDir);
            yield return Reset();
        }

        // I29 羽毛
        IEnumerator TestFeather()
        {
            var a = MakeKart(320, StraightS, 0f, 3);
            var b = MakeKart(321, StraightS + 40f, 0f, 2);
            RaceItems.Give(a, ItemKind.Feather);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            float rem = RaceItems.EffectRemaining(a, ItemKind.Feather);
            bool on = used && ItemEffects.Hopping(a) && rem > 1.4f && rem <= ItemEffects.FeatherSeconds + 0.01f && CountEff(effStart, ItemKind.Feather) == 1;
            RaceItems.ApplyHit(a, 0f, null, ItemKind.Oil);
            RaceItems.ApplyHit(a, 1f, null, ItemKind.Bumper);
            RaceItems.ApplyHit(a, 1f, null, ItemKind.FireFlower);
            RaceItems.ApplyHit(a, 1f, null, ItemKind.FakeBox);
            bool evaded = !a.Stunned && hitLog.Count == 0 && blockedLog.Count == 0;
            ItemProjectiles.PlaceBomb(b, a.Position - Vector3.up * 0.3f);
            yield return Run(0.2f);
            bool bombSkipped = explodeLog.Count == 0 && ItemProjectiles.Count(ItemKind.Bomb) == 1;
            RaceItems.ApplyHit(a, 1f, null, ItemKind.Homing);
            bool homingHits = a.Stunned && hitLog.Count == 1;
            Report("I29a feather: 1.5 s hop evades ground hazards (oil/fake/bumper/fire/bomb) but not homing", on && evaded && bombSkipped && homingHits,
                "on=" + on + " rem=" + rem.ToString("F2") + " evaded=" + evaded + " bombSkipped=" + bombSkipped + " homingHits=" + homingHits);
            yield return Reset();

            var c = MakeKart(322, StraightS, 0f, 3);
            RaceItems.Give(c, ItemKind.Feather); RaceItems.TryUse(c, false);
            yield return Run(1.6f);
            ClearLogs();
            RaceItems.ApplyHit(c, 0f, null, ItemKind.Oil);
            bool ended = !ItemEffects.Hopping(c) && hitLog.Count == 1 && hitKinds[0] == ItemKind.Oil;
            Report("I29b feather ends after 1.5 s: oil affects the kart again", ended, "hopping=" + ItemEffects.Hopping(c) + " hits=" + hitLog.Count);
            yield return Reset();
        }

        // I30 金幣道具
        IEnumerator TestCoinItem()
        {
            var k = MakeKart(330, StraightS, 0f, 6);
            int c0 = k.Coins;
            RaceItems.Give(k, ItemKind.Coin);
            bool used = RaceItems.TryUse(k, false);
            bool ok = used && k.Coins == c0 + RaceItems.CoinAmount && k.Boosting && RaceItems.Held(k) == ItemKind.None;
            Report("I30 coin item: +3 coins and a short speed bump", ok, "used=" + used + " coins=" + c0 + "->" + k.Coins + " boosting=" + k.Boosting);
            yield return Reset();
        }

        // I31 火焰花
        IEnumerator TestFireFlower()
        {
            var a = MakeKart(340, StraightS, 0f, 2);
            var b = MakeKart(341, StraightS + 25f, 0f, 1);
            RaceItems.Give(a, ItemKind.FireFlower);
            bool c3 = RaceItems.Charges(a) == 3;
            ClearLogs();
            bool u1 = RaceItems.TryUse(a, false);
            bool gap = !RaceItems.TryUse(a, false);
            yield return RunUntil(() => hitLog.Count > 0, 2f);
            bool hit = hitLog.Count == 1 && hitLog[0][0] == b && hitKinds[0] == ItemKind.FireFlower && b.Stunned;
            bool u2 = RaceItems.TryUse(a, false);
            yield return Run(0.22f);
            bool u3 = RaceItems.TryUse(a, false);
            bool empty = RaceItems.Held(a) == ItemKind.None && usedLog.Count == 3;
            Report("I31a fire flower: 3 quick fireballs (0.2 s gap), hit stuns", c3 && u1 && gap && hit && u2 && u3 && empty,
                "charges3=" + c3 + " u1=" + u1 + " gapBlocks=" + gap + " hit=" + hit + " u2=" + u2 + " u3=" + u3 + " empty=" + empty);
            yield return Reset();

            var a2 = MakeKart(342, StraightS - 20f, 0f);
            TrackSample sm = track.SampleAt(StraightS + 10f);
            Vector3 p0 = sm.Pos + sm.Right * 4f;
            Vector3 dir = (sm.Tangent + sm.Right * 0.7f).normalized;
            int b0 = ItemProjectiles.TotalBounces;
            ItemProjectiles.FireFlower(a2, p0, dir);
            yield return Run(2f);
            int bounces = ItemProjectiles.TotalBounces - b0;
            yield return Run(3f);
            Report("I31b fireball bounces at most once off a wall, then dies", bounces >= 1 && bounces <= ItemProjectiles.FireMaxBounces && ItemProjectiles.Count(ItemKind.FireFlower) == 0,
                "bounces=" + bounces + " alive=" + ItemProjectiles.Count(ItemKind.FireFlower));
            yield return Reset();
        }

        // I32 黃金加速
        IEnumerator TestGoldBoost()
        {
            var k = MakeKart(350, StraightS, 0f, 5);
            RaceItems.Give(k, ItemKind.GoldBoost); RaceItems.GiveSecond(k, ItemKind.Shield);
            ClearLogs();
            bool u1 = RaceItems.TryUse(k, false);
            bool again = !RaceItems.TryUse(k, false);
            float rem = RaceItems.EffectRemaining(k, ItemKind.GoldBoost);
            bool open = u1 && again && RaceItems.Held(k) == ItemKind.GoldBoost && rem > 5.8f && k.Boosting && CountEff(effStart, ItemKind.GoldBoost) == 1;
            bool noSwap = !RaceItems.Swap(k);
            yield return Run(0.4f);
            bool u2 = RaceItems.TryUse(k, false);
            yield return Run(2.5f);
            bool u3 = RaceItems.TryUse(k, false);
            bool stillHeld = RaceItems.Held(k) == ItemKind.GoldBoost;
            Report("I32a gold boost: unlimited boosts for 6 s (item not consumed), no swapping meanwhile", open && noSwap && u2 && u3 && stillHeld && usedLog.Count == 3,
                "open=" + open + " rem=" + rem.ToString("F2") + " noSwap=" + noSwap + " u2=" + u2 + " u3=" + u3 + " stillHeld=" + stillHeld);
            yield return Run(3.5f);
            bool done = RaceItems.EffectRemaining(k, ItemKind.GoldBoost) <= 0f && RaceItems.Held(k) == ItemKind.Shield && RaceItems.HeldSecond(k) == ItemKind.None && CountEff(effEnd, ItemKind.GoldBoost) == 1;
            Report("I32b gold boost ends after 6 s and slot 2 shifts up", done, "held=" + RaceItems.Held(k) + " remaining=" + RaceItems.EffectRemaining(k, ItemKind.GoldBoost) + " endEvt=" + CountEff(effEnd, ItemKind.GoldBoost));
            yield return Reset();
        }

        // I33 超級喇叭
        IEnumerator TestSuperHorn()
        {
            var a = MakeKart(360, StraightS, 0f, 4);
            var c = MakeKart(362, StraightS + 8f, 0f, 3);
            var d = MakeKart(363, StraightS + 30f, 0f, 2);
            ItemProjectiles.PlaceBomb(c, OnTrack(StraightS + 5f));
            ItemProjectiles.DropOil(c, false);            // 約 a 前方 5.4 m
            ItemProjectiles.DropOil(d, false);            // 約 a 前方 27 m(範圍外)
            int h0 = RaceItems.HornDestroyedTotal;
            bool before = ItemProjectiles.Count(ItemKind.Bomb) == 1 && ItemProjectiles.Count(ItemKind.Oil) == 2;
            RaceItems.Give(a, ItemKind.SuperHorn);
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            bool cleared = ItemProjectiles.Count(ItemKind.Bomb) == 0 && ItemProjectiles.Count(ItemKind.Oil) == 1 && RaceItems.HornDestroyedTotal - h0 == 2;
            yield return Run(0.2f);
            bool nothingElse = explodeLog.Count == 0 && hitLog.Count == 0 && !a.Stunned && !c.Stunned && !d.Stunned;
            Report("I33a horn destroys hazards within 12 m (not beyond), stuns nothing", before && used && cleared && nothingElse,
                "before=" + before + " used=" + used + " cleared=" + cleared + " destroyed=" + (RaceItems.HornDestroyedTotal - h0) + " nothingElse=" + nothingElse);
            bool guard = RaceItems.EffectRemaining(a, ItemKind.SuperHorn) > 9f;
            RaceItems.ApplyHit(a, 2f, null, ItemKind.BlueShell);
            bool blocked = blockedLog.Count == 1 && !a.Stunned && RaceItems.EffectRemaining(a, ItemKind.SuperHorn) <= 0f;
            RaceItems.ApplyHit(a, 2f, null, ItemKind.BlueShell);
            bool second = a.Stunned && hitLog.Count == 1;
            Report("I33b horn blocks exactly one blue shell", guard && blocked && second, "guard=" + guard + " blocked=" + blocked + " secondHits=" + second);
            yield return Reset();
        }

        // I34 閃電掉光兩格道具
        IEnumerator TestLightningDrop()
        {
            var a = MakeKart(370, StraightS, 0f, 5);
            var b = MakeKart(371, StraightS + 20f, 0f, 1);
            var c = MakeKart(372, StraightS + 10f, 3f, 3);
            var e = MakeKart(373, StraightS + 30f, -3f, 2);
            var d = MakeKart(374, StraightS - 15f, 0f, 7);
            RaceItems.Give(e, ItemKind.Shield); RaceItems.TryUse(e, false); RaceItems.Give(e, ItemKind.Bumper);
            RaceItems.Give(b, ItemKind.Bumper); RaceItems.GiveSecond(b, ItemKind.Oil);
            RaceItems.Give(c, ItemKind.TripleBoost);
            RaceItems.Give(d, ItemKind.Boost);
            RaceItems.Give(a, ItemKind.Lightning); RaceItems.GiveSecond(a, ItemKind.Shield);
            int drops = 0;
            Action<RaceKart, ItemKind> h = (kk, ii) => drops++;
            RaceItems.Dropped += h;
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            RaceItems.Dropped -= h;
            bool bGone = RaceItems.Held(b) == ItemKind.None && RaceItems.HeldSecond(b) == ItemKind.None;
            bool cGone = RaceItems.Held(c) == ItemKind.None;
            bool eKeeps = RaceItems.Held(e) == ItemKind.Bumper && blockedLog.Count == 1;
            bool dKeeps = RaceItems.Held(d) == ItemKind.Boost;
            bool userKeeps = RaceItems.Held(a) == ItemKind.Shield;
            Report("I34 lightning makes victims drop both slots (shielded / behind / user keep theirs)", used && bGone && cGone && eKeeps && dKeeps && userKeeps && drops == 3,
                "used=" + used + " bothDropped=" + bGone + " cDropped=" + cGone + " shieldedKeeps=" + eKeeps + " behindKeeps=" + dKeeps + " userShifted=" + userKeeps + " dropEvents=" + drops);
            yield return Reset();
        }

        // I35 新道具的 AI 使用
        IEnumerator TestAiNew2()
        {
            var k1 = MakeKart(380, StraightS, 0f, 4);
            var o1 = MakeKart(381, StraightS + 9f, 0f, 3);
            ItemProjectiles.DropOil(o1, false);
            RaceItems.Give(k1, ItemKind.Feather);
            yield return Run(0.5f);
            bool featherHaz = RaceItems.AiShouldUse(k1);
            RaceItems.Give(k1, ItemKind.SuperHorn);
            yield return Run(0.4f);
            bool hornHaz = RaceItems.AiShouldUse(k1);
            yield return Reset();
            var k2 = MakeKart(382, StraightS, 0f, 4);
            RaceItems.Give(k2, ItemKind.Feather);
            yield return Run(0.5f);
            bool featherIdle = !RaceItems.AiShouldUse(k2);
            RaceItems.Give(k2, ItemKind.SuperHorn);
            yield return Run(0.5f);
            bool hornIdle = !RaceItems.AiShouldUse(k2);
            RaceItems.Give(k2, ItemKind.Coin);
            yield return Run(0.5f);
            bool coin = RaceItems.AiShouldUse(k2);
            Report("I35a AI: feather / horn only when a hazard is near, coin immediately", featherHaz && hornHaz && featherIdle && hornIdle && coin,
                "featherNearOil=" + featherHaz + " hornNearOil=" + hornHaz + " featherIdle=" + featherIdle + " hornIdle=" + hornIdle + " coin=" + coin);
            yield return Reset();

            var k3 = MakeKart(383, StraightS, 0f, 4);
            var f3 = MakeKart(384, StraightS + 25f, 0f, 3);
            RaceItems.Give(k3, ItemKind.FireFlower);
            yield return Run(0.5f);
            bool ab;
            bool fire = RaceItems.AiShouldUse(k3, out ab, 1f) && !ab;
            yield return Reset();
            var k4 = MakeKart(385, StraightS, 0f, 4);
            RaceItems.Give(k4, ItemKind.GoldBoost);
            yield return Run(0.4f);
            RaceItems.TryUse(k4, false);
            yield return Run(0.4f);
            bool goldAgain = RaceItems.AiShouldUse(k4);
            Report("I35b AI: fire flower at a car ahead; gold boost keeps pressing inside its window", fire && goldAgain, "fire=" + fire + " goldAgain=" + goldAgain);
            yield return Reset();
        }

        // ------------------------------------------------------------------ 2026-10-24 道具設計 v2

        // I36 瞄準規則:紅殼只有往前丟才鎖定;綠殼(直線彈)永不追蹤;藍殼永遠追第 1 名
        IEnumerator TestTargetingRules()
        {
            // a) 追蹤彈往後丟:不鎖定、沿直線飛、前車沒有警告
            yield return Reset();
            var a = MakeKart(400, StraightS, 0f, 2);
            var b = MakeKart(401, StraightS + 25f, 0f, 1);
            Vector3 fwd = FlatV(a.Forward);
            RaceItems.Give(a, ItemKind.Homing);
            RaceItems.TryUse(a, true);                              // aimBack
            Projectile p = FindProj(ItemKind.Homing);
            bool noHome = p != null && p.NoHome && p.Target == null && !RaceItems.Targeted(b) && Vector3.Dot(p.Dir, -fwd) > 0.99f;
            yield return Run(0.6f);
            p = FindProj(ItemKind.Homing);
            bool straight = p != null && Vector3.Dot(p.Dir, -fwd) > 0.99f && !RaceItems.Targeted(b) && !b.Stunned && Vector3.Dot(p.Pos - a.Position, fwd) < -10f;
            Report("I36a red shell thrown backward: no lock-on, no warning, flies straight back", noHome && straight, "noHome=" + noHome + " straight=" + straight);
            yield return Reset();

            // b) 瞄準角:> 90 度 = 直線;<= 90 度 = 鎖定
            var a2 = MakeKart(402, StraightS, 0f, 2);
            var b2 = MakeKart(403, StraightS + 25f, 0f, 1);
            RaceItems.Give(a2, ItemKind.Homing);
            RaceItems.TryUse(a2, false, true, 120f);
            Projectile p120 = FindProj(ItemKind.Homing);
            bool back120 = p120 != null && p120.NoHome && !RaceItems.Targeted(b2);
            yield return Reset();
            var a3 = MakeKart(404, StraightS, 0f, 2);
            var b3 = MakeKart(405, StraightS + 25f, 0f, 1);
            RaceItems.Give(a3, ItemKind.Homing);
            RaceItems.TryUse(a3, false, true, 60f);
            Projectile p60 = FindProj(ItemKind.Homing);
            bool fwd60 = p60 != null && !p60.NoHome && RaceItems.Targeted(b3);
            yield return Reset();
            var a4 = MakeKart(406, StraightS, 0f, 2);
            var b4 = MakeKart(407, StraightS + 25f, 0f, 1);
            RaceItems.Give(a4, ItemKind.Homing);
            RaceItems.TryUse(a4, false, true, 90f);
            Projectile p90 = FindProj(ItemKind.Homing);
            bool side90 = p90 != null && !p90.NoHome;               // 剛好 90 度還算往前(<= 90)
            Report("I36b homing aim angle: 120 deg = straight/no warning, 60 deg and 90 deg = lock on", back120 && fwd60 && side90, "120=" + back120 + " 60=" + fwd60 + " 90=" + side90);
            yield return Reset();

            // c) 往前丟仍然追得到(回歸)
            var a5 = MakeKart(408, StraightS, 0f, 2);
            var b5 = MakeKart(409, StraightS + 30f, 0f, 1);
            RaceItems.Give(a5, ItemKind.Homing);
            ClearLogs();
            RaceItems.TryUse(a5, false);
            bool locked = RaceItems.Targeted(b5);
            yield return RunUntil(() => hitLog.Count > 0, 4f);
            Report("I36c red shell thrown forward still locks on and hits", locked && hitLog.Count == 1 && hitLog[0][0] == b5, "locked=" + locked + " hits=" + hitLog.Count);
            yield return Reset();

            // d) 綠殼(直線彈):永不追蹤,方向不變;旁邊的車不會被鎖定
            var g = MakeKart(410, StraightS, 0f, 5);
            var side = MakeKart(411, StraightS + 20f, 6f, 1);
            Vector3 right = Vector3.Cross(Vector3.up, FlatV(g.Forward));
            RaceItems.Give(g, ItemKind.Bumper);
            RaceItems.TryUse(g, false, true, 10f);
            Projectile bp = FindProj(ItemKind.Bumper);
            Vector3 d0 = bp != null ? bp.Dir : Vector3.zero;
            yield return Run(0.4f);
            bool never = bp != null && !bp.Dead && bp.Target == null && Vector3.Dot(bp.Dir, d0) > 0.999f && !RaceItems.Targeted(side);
            Report("I36d green shell (bumper) never homes: direction fixed, nobody locked", never, "never=" + never);
            yield return Reset();

            // e) 藍殼:永遠追第 1 名(不管從哪個角度發射)
            var blue = MakeKart(412, StraightS, 0f, 5);
            var rank2 = MakeKart(413, StraightS + 15f, 0f, 2);
            var rank1 = MakeKart(414, StraightS + 90f, 3f, 1);
            RaceItems.Give(blue, ItemKind.BlueShell);
            RaceItems.TryUse(blue, true);                           // 往後按也一樣
            bool lock1 = RaceItems.Targeted(rank1) && !RaceItems.Targeted(rank2);
            yield return RunUntil(() => explodeLog.Count > 0, 8f);
            Report("I36e blue shell always homes on 1st place (even when fired 'backward')", lock1 && rank1.Stunned && !rank2.Stunned, "lock1=" + lock1 + " rank1Stunned=" + rank1.Stunned + " rank2Stunned=" + rank2.Stunned);
            yield return Reset();

            // f) 炸彈 / 假箱 / 香蕉皮 / 火球 / 迴力鏢都不追蹤:放出去之後沒有任何車被鎖定
            var h = MakeKart(415, StraightS, 0f, 3);
            var tgt = MakeKart(416, StraightS + 25f, 0f, 1);
            RaceItems.Give(h, ItemKind.FireFlower); RaceItems.TryUse(h, false);
            yield return Run(0.1f);
            bool fireNo = !RaceItems.Targeted(tgt);
            RaceItems.Give(h, ItemKind.Boomerang); RaceItems.TryUse(h, false);
            yield return Run(0.1f);
            bool boomNo = !RaceItems.Targeted(tgt);
            Report("I36f fireball / boomerang / peel / bomb / fake box never lock on", fireNo && boomNo, "fire=" + fireNo + " boom=" + boomNo);
            yield return Reset();
        }

        // I37 香蕉皮(Oil):黃色翻捲果皮外觀、第一台踩到的車打轉 1.0 s、掉 1 枚金幣
        IEnumerator TestBananaPeel()
        {
            yield return Reset();
            var a = MakeKart(420, StraightS, 0f, 1);
            RaceItems.Give(a, ItemKind.Oil);
            RaceItems.TryUse(a, false);
            RaceItems.Tick(Dt);
            Projectile peel = FindProj(ItemKind.Oil);
            bool shape = peel != null && peel.Go != null && peel.Go.name == "BananaPeel" && ItemProjectiles.PeelMesh().vertexCount >= 40;
            bool ownerSafe = true;
            yield return Run(0.2f);
            ownerSafe = !a.Stunned;                                  // 自己丟的在 OilOwnerGrace 內不會踩到
            Report("I37a banana peel: curved peel mesh object dropped behind, owner not hit", shape && ownerSafe, "shape=" + shape + " verts=" + ItemProjectiles.PeelMesh().vertexCount + " ownerSafe=" + ownerSafe);

            float lat; float os = track.Project(peel.Pos, -1f, out lat);
            var v = MakeKart(421, os, lat, 2);
            v.AddCoin(5);
            ClearLogs();
            yield return Run(0.1f);
            bool spin = v.Stunned && Mathf.Abs(v.StunTotal - ItemProjectiles.BananaSpin) < 0.01f && v.StunRemaining > 0.7f && v.StunRemaining <= 1.0f;
            bool coin = v.Coins == 4;
            bool eaten = ItemProjectiles.Count(ItemKind.Oil) == 0 && hitLog.Count == 1 && hitKinds[0] == ItemKind.Oil;
            yield return Run(0.95f);
            bool done = !v.Stunned;
            Report("I37b first kart on the peel spins out 1.0 s and drops exactly 1 coin; peel consumed", spin && coin && eaten && done, "spin=" + spin + " coins(5->)" + v.Coins + " eaten=" + eaten + " freeAfter1s=" + done);

            // 護盾擋香蕉皮;已在暈眩 / 無敵中的車穿過去,香蕉皮還在
            yield return Reset();
            var o = MakeKart(422, StraightS, 0f, 1);
            RaceItems.Give(o, ItemKind.Oil); RaceItems.TryUse(o, false);
            yield return Run(1.2f);
            Projectile pl = FindProj(ItemKind.Oil);
            float lat2; float os2 = track.Project(pl.Pos, -1f, out lat2);
            var inv = MakeKart(423, os2, lat2, 2);
            inv.SetInvincible(2f);
            yield return Run(0.2f);
            bool pass = !inv.Stunned && ItemProjectiles.Count(ItemKind.Oil) == 1;
            Report("I37c an invincible kart passes over the peel without consuming it", pass, "stunned=" + inv.Stunned + " peels=" + ItemProjectiles.Count(ItemKind.Oil));
            yield return Reset();
        }

        // I38 三連香蕉皮:車尾拖 3 片;一片一片丟;從後面來的殼會先打掉一片
        IEnumerator TestTripleBanana()
        {
            yield return Reset();
            var k = MakeKart(430, StraightS, 0f, 3);
            RaceItems.Give(k, ItemKind.TripleBanana);
            yield return Run(0.1f);
            Transform trail = k.transform.Find("BananaTrail");
            int shown = 0;
            if (trail != null) for (int i = 0; i < trail.childCount; i++) if (trail.GetChild(i).gameObject.activeSelf) shown++;
            bool has3 = RaceItems.Charges(k) == 3 && shown == 3;
            Report("I38a triple banana: 3 charges and 3 peels trail behind the kart", has3, "charges=" + RaceItems.Charges(k) + " shownPeels=" + shown);

            // 後面的車射直線彈:打掉一片
            var sh = MakeKart(431, StraightS - 18f, 0f, 4);
            RaceItems.Give(sh, ItemKind.Bumper);
            ClearLogs();
            int b0 = RaceItems.TrailBlockedTotal;
            RaceItems.TryUse(sh, false);
            yield return RunUntil(() => blockedLog.Count > 0, 2f);
            bool blockedBack = blockedLog.Count == 1 && !k.Stunned && RaceItems.Charges(k) == 2 && RaceItems.TrailBlockedTotal == b0 + 1 && ItemProjectiles.Count(ItemKind.Bumper) == 0;
            // 後面的追蹤彈也擋
            RaceItems.Give(sh, ItemKind.Homing);
            ClearLogs();
            RaceItems.TryUse(sh, false);
            yield return RunUntil(() => blockedLog.Count > 0 || hitLog.Count > 0, 4f);
            bool blockedHoming = blockedLog.Count == 1 && !k.Stunned && RaceItems.Charges(k) == 1;
            Report("I38b shells from behind (bumper, homing) each pop one trailing peel", blockedBack && blockedHoming, "bumper=" + blockedBack + " homing=" + blockedHoming + " charges=" + RaceItems.Charges(k));

            // 丟一片:剩 0 → 空手,拖尾消失
            ClearLogs();
            yield return Run(0.4f);
            bool dropped = RaceItems.TryUse(k, false);
            RaceItems.Tick(Dt);
            bool hazard = false;
            for (int i = 0; i < RaceItems.Hazards.Count; i++) if (RaceItems.Hazards[i].Kind == ItemKind.Oil) hazard = true;
            yield return Run(0.1f);
            bool empty = dropped && hazard && RaceItems.Held(k) == ItemKind.None && k.transform.Find("BananaTrail") == null;
            Report("I38c using the last peel drops it behind and removes the trail", empty, "dropped=" + dropped + " hazard=" + hazard + " held=" + RaceItems.Held(k));
            yield return Reset();

            // 從前面來的殼不會被擋(殼朝後射到前車的臉)
            var front = MakeKart(432, StraightS + 20f, 0f, 1);
            var tb = MakeKart(433, StraightS, 0f, 4);
            RaceItems.Give(tb, ItemKind.TripleBanana);
            RaceItems.Give(front, ItemKind.Bumper);
            ClearLogs();
            yield return Run(0.1f);
            RaceItems.TryUse(front, true);                           // 往後射,從前方打向 tb
            yield return RunUntil(() => hitLog.Count > 0 || blockedLog.Count > 0, 2f);
            bool noBlock = blockedLog.Count == 0 && hitLog.Count == 1 && hitLog[0][0] == tb && RaceItems.Charges(tb) == 3;
            Report("I38d a shell coming from the front is NOT blocked by the trail", noBlock, "blocked=" + blockedLog.Count + " hit=" + hitLog.Count + " charges=" + RaceItems.Charges(tb));
            yield return Reset();
        }

        // I39 幽靈:偷前車一個道具 + 3 s 隱形無敵(閃電也打不到)
        IEnumerator TestBoo()
        {
            yield return Reset();
            var user = MakeKart(440, StraightS, 0f, 4);
            var f1 = MakeKart(441, StraightS + 20f, 0f, 1);
            var f2 = MakeKart(442, StraightS + 10f, 3f, 2);
            var back = MakeKart(443, StraightS - 30f, 0f, 8);
            RaceItems.Give(f1, ItemKind.Mega);
            RaceItems.Give(f2, ItemKind.Boost);
            RaceItems.Give(user, ItemKind.Boo);
            ClearLogs();
            int s0 = RaceItems.BooStolenTotal;
            bool used = RaceItems.TryUse(user, false);
            ItemKind got = RaceItems.Held(user);
            bool stole = used && (got == ItemKind.Mega || got == ItemKind.Boost) && RaceItems.BooStolenTotal == s0 + 1;
            bool victimLost = got == ItemKind.Mega ? RaceItems.Held(f1) == ItemKind.None && RaceItems.Held(f2) == ItemKind.Boost
                                                   : RaceItems.Held(f2) == ItemKind.None && RaceItems.Held(f1) == ItemKind.Mega;
            bool ghost = user.Invincible && RaceItems.EffectRemaining(user, ItemKind.Boo) > 2.5f && CountEff(effStart, ItemKind.Boo) == 1;
            Report("I39a boo steals exactly one item from a kart ahead and grants 3 s ghost", stole && victimLost && ghost, "used=" + used + " got=" + got + " victimLost=" + victimLost + " ghost=" + ghost);

            // 閃電:幽靈車不受影響,其他前車縮小
            RaceItems.Give(back, ItemKind.Lightning);
            RaceItems.TryUse(back, false);
            yield return Run(0.1f);
            bool ignoresBolt = !RaceItems.HasEffect(user, ItemKind.Lightning) && RaceItems.KartScale(user) > 0.95f;
            bool othersHit = RaceItems.HasEffect(f1, ItemKind.Lightning);
            // 道具 / 香蕉皮都穿過去
            ItemProjectiles.DropOil(back, false);
            Report("I39b ghost ignores lightning (others ahead are shrunk)", ignoresBolt && othersHit, "ignoresBolt=" + ignoresBolt + " othersShrunk=" + othersHit);

            yield return Run(3.0f);
            bool ended = !user.Invincible && !RaceItems.HasEffect(user, ItemKind.Boo) && CountEff(effEnd, ItemKind.Boo) == 1;
            Report("I39c ghost ends after 3 s", ended, "invincible=" + user.Invincible + " endEvents=" + CountEff(effEnd, ItemKind.Boo));
            yield return Reset();

            // 沒人在前面有道具(或自己是第 1 名):不爆、不偷、仍然隱形
            var lone = MakeKart(444, StraightS, 0f, 1);
            RaceItems.Give(lone, ItemKind.Boo);
            int s1 = RaceItems.BooStolenTotal;
            bool u2 = RaceItems.TryUse(lone, false);
            bool nothing = u2 && RaceItems.BooStolenTotal == s1 && RaceItems.Held(lone) == ItemKind.None && lone.Invincible;
            Report("I39d boo with nobody to steal from: still a ghost, no crash", nothing, "used=" + u2 + " held=" + RaceItems.Held(lone) + " inv=" + lone.Invincible);
            yield return Reset();
        }

        // I40 迴力鏢花:3 次,飛出去再飛回來,可以打到兩次(去程 + 回程)
        IEnumerator TestBoomerang()
        {
            yield return Reset();
            var a = MakeKart(450, StraightS, 0f, 3);
            var x = MakeKart(451, StraightS + 20f, 0f, 1);         // 去程打到
            var z = MakeKart(452, StraightS + 10f, 0f, 2);         // 去程無敵穿過,回程才打到
            z.SetInvincible(0.9f);
            RaceItems.Give(a, ItemKind.Boomerang);
            bool three = RaceItems.Charges(a) == 3;
            ClearLogs();
            bool used = RaceItems.TryUse(a, false);
            bool two = RaceItems.Charges(a) == 2 && ItemProjectiles.Count(ItemKind.Boomerang) == 1;
            yield return RunUntil(() => ItemProjectiles.Count(ItemKind.Boomerang) == 0, 5f);
            float life = runT;
            bool hitX = false, hitZ = false;
            for (int i = 0; i < hitLog.Count; i++) { if (hitLog[i][0] == x && hitKinds[i] == ItemKind.Boomerang) hitX = true; if (hitLog[i][0] == z && hitKinds[i] == ItemKind.Boomerang) hitZ = true; }
            Report("I40a boomerang: 3 charges; one kart hit on the way out, another on the way back", three && used && two && hitX && hitZ && hitLog.Count == 2 && !a.Stunned,
                "three=" + three + " used=" + used + " two=" + two + " hitOut(x)=" + hitX + " hitBack(z)=" + hitZ + " hits=" + hitLog.Count);
            bool returned = life > 0.9f && life < BoomMaxLife();
            Report("I40b boomerang flies out, turns and is caught by its owner before its life runs out", returned, "flight=" + life.ToString("F2") + " s (< " + BoomMaxLife().ToString("F1") + ")");
            yield return Reset();

            // 往後丟:去程朝後,回程回到主人
            var b = MakeKart(453, StraightS, 0f, 3);
            Vector3 fwd = FlatV(b.Forward);
            RaceItems.Give(b, ItemKind.Boomerang);
            RaceItems.TryUse(b, true);
            Projectile pr = FindProj(ItemKind.Boomerang);
            bool backOut = pr != null && Vector3.Dot(pr.Dir, -fwd) > 0.99f;
            yield return RunUntil(() => ItemProjectiles.Count(ItemKind.Boomerang) == 0, 5f);
            Report("I40c boomerang thrown backward goes behind and comes back", backOut && runT > 0.9f && runT < BoomMaxLife(), "backOut=" + backOut + " flight=" + runT.ToString("F2"));
            yield return Reset();
        }

        static float BoomMaxLife() { return ItemProjectiles.BoomLife - 0.05f; }

        // I41 AI v2:朝後的紅殼、幽靈、迴力鏢、香蕉皮
        IEnumerator TestAiV2()
        {
            yield return Reset();
            // 領先者拿追蹤彈:沒目標 -> 等有人追才往後丟
            var lead = MakeKart(460, StraightS, 0f, 1);
            RaceItems.Give(lead, ItemKind.Homing);
            yield return Run(4f);
            bool wait = !RaceItems.AiShouldUse(lead);
            var chaser = MakeKart(461, StraightS - 15f, 0f, 2);
            bool back;
            bool throwsBack = RaceItems.AiShouldUse(lead, out back, 1f) && back;
            Report("I41a AI leader holds a red shell until someone is behind, then throws it backward", wait && throwsBack, "waits=" + wait + " throwsBack=" + throwsBack);
            yield return Reset();

            var mid = MakeKart(462, StraightS, 0f, 5);
            RaceItems.Give(mid, ItemKind.Homing);
            yield return Run(3.5f);
            bool fb;
            bool fwdShot = RaceItems.AiShouldUse(mid, out fb, 1f) && !fb;
            Report("I41b AI with a car ahead throws the red shell forward", fwdShot, "fwd=" + fwdShot);
            yield return Reset();

            // 幽靈:前面有人拿道具才用
            var bk = MakeKart(463, StraightS, 0f, 5);
            RaceItems.Give(bk, ItemKind.Boo);
            yield return Run(0.5f);
            bool noTarget = !RaceItems.AiShouldUse(bk);
            var ahead = MakeKart(464, StraightS + 30f, 0f, 1);
            RaceItems.Give(ahead, ItemKind.Star);
            bool steal = RaceItems.AiShouldUse(bk);
            Report("I41c AI uses boo when a kart ahead holds an item (not before)", noTarget && steal, "idle=" + noTarget + " steal=" + steal);
            yield return Reset();

            // 迴力鏢:前車在 24 m 內正前方
            var bm = MakeKart(465, StraightS, 0f, 5);
            var bt = MakeKart(466, StraightS + 18f, 0f, 1);
            RaceItems.Give(bm, ItemKind.Boomerang);
            yield return Run(0.5f);
            bool bb;
            bool shoot = RaceItems.AiShouldUse(bm, out bb, 1f) && !bb;
            Report("I41d AI throws the boomerang at a car dead ahead", shoot, "shoot=" + shoot);
            yield return Reset();

            // 香蕉皮:後面有人追就丟(往後)
            var pk = MakeKart(467, StraightS, 0f, 3);
            var pur = MakeKart(468, StraightS - 12f, 0f, 4);
            RaceItems.Give(pk, ItemKind.TripleBanana);
            yield return Run(0.5f);
            bool pb;
            bool drop = RaceItems.AiShouldUse(pk, out pb, 1f) && !pb;
            Report("I41e AI drops a banana when chased closely from behind", drop, "drop=" + drop);
            yield return Reset();

            // 表 v2:幽靈第 1 名 0%,三連香蕉皮 / 迴力鏢花存在、每列總和 100(I1a 已驗證)
            int iBoo = Array.IndexOf(ItemTable.Order, ItemKind.Boo), iTb = Array.IndexOf(ItemTable.Order, ItemKind.TripleBanana), iBo = Array.IndexOf(ItemTable.Order, ItemKind.Boomerang);
            bool tbl = ItemTable.Weights[0, iBoo] == 0 && iTb >= 20 && iBo >= 20 && ItemTable.Weights[0, iTb] > 0 && ItemTable.Weights[4, iBo] > 0 && ItemTable.Weights[4, iBoo] > 0;
            Report("I41f table v2: no boo for 1st place; banana/boomerang/boo columns present", tbl, "boo1st=" + ItemTable.Weights[0, iBoo] + " tbCol=" + iTb + " boCol=" + iBo);
            yield return Reset();
        }

        // ------------------------------------------------------------------ I13

        IEnumerator TestShutdown()
        {
            var k = MakeKart(80, StraightS, 0f);
            RaceItems.Give(k, ItemKind.Oil);
            RaceItems.TryUse(k, false);
            RaceItems.Tick(Dt);
            bool hadHazard = RaceItems.Hazards.Count > 0;
            RaceItems.Shutdown();
            yield return null;
            bool clean = !RaceItems.Active && RaceItems.Hazards.Count == 0 && RaceItems.Boxes.Count == 0 && RaceItems.Held(k) == ItemKind.None
                && !RaceItems.TryUse(k, false) && ItemProjectiles.All.Count == 0;
            Report("I13 Shutdown clears everything", hadHazard && clean, "hadHazard=" + hadHazard + " active=" + RaceItems.Active + " hazards=" + RaceItems.Hazards.Count + " boxes=" + RaceItems.Boxes.Count);
        }
    }
}
