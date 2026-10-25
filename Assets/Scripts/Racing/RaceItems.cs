using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrcSim
{
    /// 依名次的道具機率表(03 §1.3,模組 I 擴充到 15 種;2026-10-23 再加羽毛 / 金幣 / 火焰花 / 黃金加速 / 超級喇叭 = 20 種;2026-10-24 再加三連香蕉皮 / 幽靈 / 迴力鏢花 = 23 種)。每列總和必須 = 100。
    /// Oil 欄 = 香蕉皮(ItemKind.Oil 數值不變,只改顯示名與行為);幽靈只出現在第 2 名以後(要有人在前面才有東西偷)。
    /// 欄位順序 = Order。落後者拿強力道具(星星 / 閃電 / 藍殼 / 巨大化 / 子彈),領先者拿弱的(護盾 / 油漬 / 假箱 / 炸彈)。
    /// 閃電 / 子彈只出現在後段(列 3、4),藍殼只出現在列 3(2%)與列 4(8%);第 1、2 名與中段(列 0~2)完全不出。
    public static class ItemTable
    {
        public static readonly ItemKind[] Order =
        {
            ItemKind.Boost, ItemKind.TripleBoost, ItemKind.Shield, ItemKind.Oil, ItemKind.Bumper, ItemKind.Homing,
            ItemKind.Star, ItemKind.Lightning, ItemKind.Bomb, ItemKind.BlueShell, ItemKind.Mega, ItemKind.Bullet,
            ItemKind.FakeBox, ItemKind.TripleBumper, ItemKind.InkBlast,
            ItemKind.Feather, ItemKind.Coin, ItemKind.FireFlower, ItemKind.GoldBoost, ItemKind.SuperHorn,
            ItemKind.TripleBanana, ItemKind.Boo, ItemKind.Boomerang
        };

        // 列:0 = 第1名,1 = 第2名,2 = 3~4,3 = 5~6,4 = 7~8(以 8 台車為準)
        public static readonly int[,] Weights =
        {
            // 加速 三連 護盾 香蕉 直線 追蹤 星星 閃電 炸彈 藍殼 巨大 子彈 假箱 三連彈 墨水 羽毛 金幣 火花 黃金 喇叭 三蕉 幽靈 迴鏢
            {   3,   0,  15,  16,  10,   0,   0,   0,  10,   0,   0,   0,  12,   9,   0,   4,   6,   4,   0,   2,   6,   0,   3 },
            {   9,   0,  10,   8,  10,   4,   3,   0,   7,   0,   2,   0,   7,   8,   4,   4,   5,   4,   0,   3,   5,   3,   4 },
            {   8,   5,   7,   5,   6,   9,   8,   0,   5,   0,   6,   0,   3,   7,   4,   3,   4,   3,   2,   2,   4,   5,   4 },
            {   9,   9,   4,   1,   3,   8,  10,   6,   3,   2,   7,   5,   1,   3,   3,   3,   3,   3,   4,   2,   2,   6,   3 },
            {   5,  10,   2,   0,   1,  10,  14,  11,   1,   8,   8,   7,   0,   2,   0,   2,   2,   2,   4,   2,   0,   6,   3 },
        };

        public const int Rows = 5;

        /// 名次 → 列。車數不是 8 時用比例 r=(名次-1)/(車數-1) 換算成 8 台車的名次。
        public static int RowFor(int place, int count)
        {
            if (count <= 1) return 0;
            place = Mathf.Clamp(place, 1, count);
            int p8 = Mathf.Clamp(Mathf.RoundToInt(1f + (place - 1) / (float)(count - 1) * 7f), 1, 8);
            if (p8 == 1) return 0;
            if (p8 == 2) return 1;
            if (p8 <= 4) return 2;
            if (p8 <= 6) return 3;
            return 4;
        }

        public static int RowSum(int row)
        {
            int s = 0;
            for (int i = 0; i < Order.Length; i++) s += Weights[row, i];
            return s;
        }

        public static ItemKind Pick(int row, System.Random rng)
        {
            int r = rng.Next(100), acc = 0;
            for (int i = 0; i < Order.Length; i++)
            {
                acc += Weights[row, i];
                if (r < acc) return Order[i];
            }
            return Order[Order.Length - 1];
        }
    }

    /// 道具系統(模組 I / M4,static)。介面照 docs/racing/00-contracts.md §3.8。
    /// 流程:撞道具箱 → 抽獎 1.2 s(輪盤)→ 決定當下依名次查表 → 持有(最多 1 個)→ 按道具鍵(RaceKart.ItemPressed)→ TryUse。
    /// 所有計時用 Tick(dt) 的 dt 累加(RaceSession 每個 FixedUpdate 呼叫),不用 Time.time,測試可重現。
    public static class RaceItems
    {
        // ------------------------------------------------------------ 數值(03 §1.2)
        public const float RollSeconds = 1.2f;
        public const float BoostSeconds = 1.5f, BoostBonus = 0.45f;          // 極速 ×1.45
        public const float TripleSeconds = 1.2f, TripleGap = 0.3f;
        public const float ShieldSeconds = 8f;
        public const float PostStunInvincible = 1.5f;                         // 暈眩結束後無敵
        public const float OilSlipSeconds = 1.0f, OilSlipGrip = 0.3f, OilSlipSpeed = 0.6f;
        public const float NoHomingBefore = 10f;                              // 比賽開始後 10 s 內不出追蹤彈
        public const int MaxHomingInPlay = 2;
        public const int MaxBlueShellInPlay = 1;
        public const float TripleBumperGap = 0.25f;
        // 瑪利歐賽車世界式擴充(2026-10-23)
        public const float FireGap = 0.2f;                                     // 火焰花三連發間隔
        public const int CoinAmount = 3;                                       // 金幣道具:+3 金幣
        public const float CoinBoostSeconds = 0.8f, CoinBoostBonus = 0.2f;     // 金幣道具附贈的短暫加速
        public const float GoldSeconds = 6f, GoldGap = 0.35f, GoldBoostSeconds = 1.2f;   // 黃金加速:6 s 內可無限次加速(每次間隔 0.35 s)
        public const float BoomGap = 0.4f;                                     // 迴力鏢花:3 次,間隔 0.4 s
        public const float BananaGap = 0.3f;                                   // 三連香蕉皮:連續丟的最短間隔
        public const float HornGuardSeconds = 10f;                             // 超級喇叭:之後 10 s 內擋掉一次藍殼
        /// 測試鉤子:喇叭累計消除的投射物數、兩格道具被閃電打掉的事件
        public static int HornDestroyedTotal { get; private set; }
        /// 測試鉤子:幽靈偷到的道具累計數、三連香蕉皮擋下的殼累計數
        public static int BooStolenTotal { get; private set; }
        public static int TrailBlockedTotal { get; private set; }
        // 使用道具回饋(2026-10-23):任何道具使用成功 → 光環 + 車身閃爍 + 短暫無敵(像瑪利歐賽車的使用手感)
        public static float UseInvincibleSeconds = 0f;                        // 使用道具「不」給無敵(只有特技給);保留欄位供測試 / 調整。SetInvincible 取 max,不會縮短星星 / 巨大化 / 子彈
        public static bool UseFxEnabled = true;                               // 關掉就沒有光環 / 閃爍 / 音效(無敵時間仍由 UseInvincibleSeconds 決定)
        /// 測試鉤子:使用回饋觸發次數與最後一次(種類 / 使用者)。
        public static int UseFxCount { get; private set; }
        public static ItemKind UseFxLastKind { get; private set; }
        public static RaceKart UseFxLastKart { get; private set; }

        sealed class KState
        {
            public RaceKart Kart;
            public ItemKind Held, Held2;       // 第 1 格(使用)/ 第 2 格(使用後遞補)
            public int Charges, Charges2;
            public bool Rolling, PrevSwap;
            public float GoldLeft, HornGuard;
            public float RollTimer, HeldTime, ShieldLeft, Cooldown;
            public float AiDelay, AiRoll;
            public GameObject ShieldVis;
        }

        static bool inited;
        static RaceTrack track;
        static RaceConfig cfg;
        static Transform root;
        static System.Random rng;
        static float elapsed;
        static Action<RaceKart> pressedHandler;

        static readonly Dictionary<int, KState> states = new Dictionary<int, KState>();
        static readonly List<KState> stateList = new List<KState>();
        static readonly List<ItemBox> boxes = new List<ItemBox>();
        static readonly List<RaceHazard> hazards = new List<RaceHazard>();

        // ------------------------------------------------------------ 事件
        public static event Action<RaceKart> RollStart;
        public static event Action<RaceKart, ItemKind> Got, Used, Blocked;     // Blocked = 護盾擋下
        public static event Action<RaceKart, RaceKart, ItemKind> Hit;          // (victim, by 可為 null, kind)
        public static event Action<Vector3> BoxBroken;
        /// 兩格道具互換 / 被閃電打掉的道具(每個道具一次)。
        public static event Action<RaceKart> Swapped;
        public static event Action<RaceKart, ItemKind> Dropped;
        /// 持續型效果開始 / 結束(Star、Mega、Bullet、Lightning = 被縮小、InkBlast = 被墨水):特效 / 音效訂閱用。
        public static event Action<RaceKart, ItemKind> EffectStart, EffectEnd;
        /// 爆炸(炸彈 / 藍殼):位置、半徑、種類。
        public static event Action<Vector3, float, ItemKind> Exploded;

        internal static void RaiseEffect(bool start, RaceKart k, ItemKind kind)
        {
            var e = start ? EffectStart : EffectEnd;
            if (e != null) e(k, kind);
        }
        internal static void RaiseExploded(Vector3 pos, float radius, ItemKind kind)
        {
            if (Exploded != null) Exploded(pos, radius, kind);
        }

        // ------------------------------------------------------------ 生命週期
        public static bool Active { get { return inited; } }
        public static float Elapsed { get { return elapsed; } }
        public static IReadOnlyList<ItemBox> Boxes { get { return boxes; } }
        public static IReadOnlyList<RaceHazard> Hazards { get { return hazards; } }

        /// 契約簽名:生成道具箱、訂閱 RaceKart.ItemPressed。cfg.Items == false 時不生成箱子(仍可 Give)。
        public static void Init(RaceTrack t, RaceConfig config, Transform parent)
        {
            Shutdown();
            track = t;
            cfg = config ?? new RaceConfig();
            rng = new System.Random(unchecked(cfg.Seed * 31 + 7));
            elapsed = 0f;
            var g = new GameObject("RaceItems");
            if (parent != null) g.transform.SetParent(parent, false);
            root = g.transform;
            ItemProjectiles.Init(track, root);
            ItemEffects.Init(track);
            root.gameObject.AddComponent<ItemInkOverlay>();
            if (cfg.Items && track != null) boxes.AddRange(ItemBox.BuildAlongTrack(track, root));
            pressedHandler = k => { if (k != null) TryUse(k, k.Input.AimBack, k.Input.AimActive, k.Input.AimAngle); };
            RaceKart.ItemPressed += pressedHandler;
            inited = true;
        }

        /// 方便版:順便預先登記車輛(不登記也行,Tick 會自動從 RaceKart.All 補)。
        public static void Init(RaceTrack t, IList<RaceKart> karts, RaceConfig config = null, Transform parent = null)
        {
            Init(t, config, parent);
            if (karts != null) for (int i = 0; i < karts.Count; i++) if (karts[i] != null) St(karts[i]);
        }

        public static void Shutdown()
        {
            if (pressedHandler != null) RaceKart.ItemPressed -= pressedHandler;
            pressedHandler = null;
            for (int i = 0; i < stateList.Count; i++) if (stateList[i].ShieldVis != null) Object.Destroy(stateList[i].ShieldVis);
            states.Clear(); stateList.Clear();
            ItemProjectiles.Shutdown();
            ItemEffects.Shutdown();
            boxes.Clear(); hazards.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null; track = null; cfg = null; rng = null;
            inited = false; elapsed = 0f;
        }

        static KState St(RaceKart k, bool create = true)
        {
            if (k == null) return null;
            KState s;
            if (states.TryGetValue(k.KartId, out s)) { if (s.Kart == k) return s; Drop(s); }
            if (!create) return null;
            s = new KState { Kart = k };
            states[k.KartId] = s; stateList.Add(s);
            return s;
        }

        static void Drop(KState s)
        {
            if (s.ShieldVis != null) Object.Destroy(s.ShieldVis);
            states.Remove(s.Kart != null ? s.Kart.KartId : -999);
            stateList.Remove(s);
        }

        // ------------------------------------------------------------ 每步
        public static void Tick(float dt)
        {
            if (!inited || dt <= 0f) return;
            elapsed += dt;

            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++) if (all[i] != null) St(all[i]);
            for (int i = stateList.Count - 1; i >= 0; i--)
                if (stateList[i].Kart == null) { var d = stateList[i]; if (d.ShieldVis != null) Object.Destroy(d.ShieldVis); stateList.RemoveAt(i); RemoveByValue(d); }

            // 道具箱
            for (int b = 0; b < boxes.Count; b++)
            {
                ItemBox box = boxes[b];
                if (box == null) continue;
                if (!box.Active) { box.Step(dt); continue; }
                for (int i = 0; i < stateList.Count; i++)
                {
                    KState s = stateList[i];
                    if (SlotsFull(s) || s.Rolling) continue;                // 兩格都滿 / 抽獎中:穿過去,箱子不破
                    if (!box.Touches(s.Kart.Position)) continue;
                    box.Break();
                    if (BoxBroken != null) BoxBroken(box.transform.position);
                    OnBoxHit(s.Kart);
                    break;
                }
            }

            // 每台車的計時
            for (int i = 0; i < stateList.Count; i++)
            {
                KState s = stateList[i];
                if (s.Rolling)
                {
                    s.RollTimer += dt;
                    if (s.RollTimer >= RollSeconds) FinishRoll(s);
                }
                if (s.Held != ItemKind.None) s.HeldTime += dt;
                if (s.Cooldown > 0f) s.Cooldown -= dt;
                if (s.Kart != null)
                {
                    bool sw = s.Kart.Input.SwapHeld;                       // 交換鍵:偵測上升緣(RaceKart 沒有幫它鎖存)
                    if (sw && !s.PrevSwap) Swap(s.Kart);
                    s.PrevSwap = sw;
                }
                if (s.GoldLeft > 0f)
                {
                    s.GoldLeft -= dt;
                    if (s.GoldLeft <= 0f)
                    {
                        s.GoldLeft = 0f;
                        if (s.Held == ItemKind.GoldBoost) ShiftSlots(s);
                        RaiseEffect(false, s.Kart, ItemKind.GoldBoost);
                    }
                }
                if (s.HornGuard > 0f)
                {
                    s.HornGuard -= dt;
                    if (s.HornGuard <= 0f) { s.HornGuard = 0f; RaiseEffect(false, s.Kart, ItemKind.SuperHorn); }
                }
                if (s.ShieldLeft > 0f)
                {
                    s.ShieldLeft -= dt;
                    if (s.ShieldLeft <= 0f) { s.ShieldLeft = 0f; DestroyShieldVis(s); }
                    else if (s.ShieldVis != null && s.Kart != null)
                        s.ShieldVis.transform.Rotate(0f, 120f * dt, 0f, Space.Self);
                }
            }

            for (int i = 0; i < stateList.Count; i++)
            {
                KState s = stateList[i];
                if (s.Kart != null) SyncVis(s);
            }
            ItemEffects.Tick(dt);
            ItemProjectiles.Step(dt);
            hazards.Clear();
            ItemProjectiles.FillHazards(hazards);
        }

        // 車身附屬視覺:三連直線彈環繞球 / 三連香蕉皮車尾拖尾(都只對第 1 格有效)
        static void SyncVis(KState s)
        {
            ItemEffects.SyncOrbit(s.Kart, s.Held == ItemKind.TripleBumper ? s.Charges : 0);
            ItemEffects.SyncTrail(s.Kart, s.Held == ItemKind.TripleBanana ? s.Charges : 0);
        }

        static void RemoveByValue(KState d)
        {
            int key = int.MinValue; bool found = false;
            foreach (var kv in states) if (kv.Value == d) { key = kv.Key; found = true; break; }
            if (found) states.Remove(key);
        }

        // ------------------------------------------------------------ 查詢(HUD / AI)
        public static ItemKind Held(RaceKart k) { var s = St(k, false); return s != null ? s.Held : ItemKind.None; }
        public static int Charges(RaceKart k) { var s = St(k, false); return s != null && s.Held != ItemKind.None ? s.Charges : 0; }
        /// 第 2 格道具與剩餘次數;SlotsFree = 還空幾格(0..2)。
        public static ItemKind HeldSecond(RaceKart k) { var s = St(k, false); return s != null ? s.Held2 : ItemKind.None; }
        public static int Charges2(RaceKart k) { var s = St(k, false); return s != null && s.Held2 != ItemKind.None ? s.Charges2 : 0; }
        public static int SlotsFree(RaceKart k) { var s = St(k, false); return s == null ? 2 : (s.Held == ItemKind.None ? 1 : 0) + (s.Held2 == ItemKind.None ? 1 : 0); }
        static bool SlotsFull(KState s) { return s.Held != ItemKind.None && s.Held2 != ItemKind.None; }
        public static bool Rolling(RaceKart k) { var s = St(k, false); return s != null && s.Rolling; }
        public static float RollT(RaceKart k) { var s = St(k, false); return s != null && s.Rolling ? Mathf.Clamp01(s.RollTimer / RollSeconds) : 0f; }
        public static bool Shielded(RaceKart k) { var s = St(k, false); return s != null && s.ShieldLeft > 0f; }
        public static float ShieldRemaining(RaceKart k) { var s = St(k, false); return s != null ? s.ShieldLeft : 0f; }
        public static bool Targeted(RaceKart k) { return ItemProjectiles.IsTargeting(k); }
        /// 持續型效果剩餘秒數:Star / Mega / Bullet(自動駕駛段)/ Lightning(被縮小)/ InkBlast(被墨水)。0 = 沒有。
        public static float EffectRemaining(RaceKart k, ItemKind kind)
        {
            if (kind == ItemKind.GoldBoost) { var g = St(k, false); return g != null ? g.GoldLeft : 0f; }
            if (kind == ItemKind.SuperHorn) { var h = St(k, false); return h != null ? h.HornGuard : 0f; }
            return ItemEffects.Remaining(k, kind);
        }
        public static bool HasEffect(RaceKart k, ItemKind kind) { return EffectRemaining(k, kind) > 0f; }
        /// 車體目前的視覺縮放(巨大化 2.2、被閃電 0.5、一般 1)。
        public static float KartScale(RaceKart k) { return ItemEffects.ScaleOf(k); }
        /// 玩家畫面被墨水遮住的剩餘秒數(HUD 若要自己畫可用;預設 ItemInkOverlay 已畫)。
        public static float InkRemaining(RaceKart k) { return ItemEffects.Remaining(k, ItemKind.InkBlast); }

        /// 現在能不能出追蹤彈(開賽 10 s 後,且場上 + 手上追蹤彈合計 < 2)。
        public static bool HomingAllowed { get { return elapsed >= NoHomingBefore && HomingInPlay() < MaxHomingInPlay; } }

        /// 藍殼限制:場上 + 手上合計 &lt; 1 顆。
        public static bool BlueShellAllowed { get { return BlueShellInPlay() < MaxBlueShellInPlay; } }

        public static int BlueShellInPlay()
        {
            int n = ItemProjectiles.Count(ItemKind.BlueShell);
            for (int i = 0; i < stateList.Count; i++) if (stateList[i].Held == ItemKind.BlueShell) n++;
            return n;
        }

        public static int HomingInPlay()
        {
            int n = ItemProjectiles.Count(ItemKind.Homing);
            for (int i = 0; i < stateList.Count; i++) if (stateList[i].Held == ItemKind.Homing) n++;
            return n;
        }

        // ------------------------------------------------------------ 抽獎
        /// 純函式:依名次抽(不含追蹤彈限制)。count = 車數。
        public static ItemKind Roll(int place, int count, System.Random r)
        {
            return ItemTable.Pick(ItemTable.RowFor(place, count), r);
        }

        /// 含限制版本:allowHoming = false 時抽到追蹤彈改成三連加速(03 §1.3 規則)。
        public static ItemKind Roll(int place, int count, System.Random r, bool allowHoming)
        {
            ItemKind k = Roll(place, count, r);
            if (k == ItemKind.Homing && !allowHoming) k = ItemKind.TripleBoost;
            return k;
        }

        /// 完整規則版:追蹤彈與藍殼的場上數量限制。藍殼額滿 → 星星。
        public static ItemKind Roll(int place, int count, System.Random r, bool allowHoming, bool allowBlue)
        {
            ItemKind k = Roll(place, count, r, allowHoming);
            if (k == ItemKind.BlueShell && !allowBlue) k = ItemKind.Star;
            return k;
        }

        /// 車撞到箱子:開始抽獎(已有道具或抽獎中則忽略)。
        public static void OnBoxHit(RaceKart k)
        {
            if (!inited) return;
            var s = St(k);
            if (s == null || SlotsFull(s) || s.Rolling) return;
            s.Rolling = true; s.RollTimer = 0f;
            if (RollStart != null) RollStart(k);
        }

        static void FinishRoll(KState s)
        {
            s.Rolling = false; s.RollTimer = 0f;
            int count = Mathf.Max(1, RaceKart.All.Count);
            ItemKind kind = Roll(RankOf(s.Kart), count, rng, HomingAllowed, BlueShellAllowed);
            kind = ApplyRule(kind);
            if (s.Held == ItemKind.None) Grant(s, kind);
            else if (s.Held2 == ItemKind.None) GrantSecond(s, kind);      // 第 1 格已有道具:抽到的進第 2 格
        }

        // 賽前選單的道具規則(RaceConfig.ItemRule):0 正常 / 1 瘋狂(強力道具機率高:隨機換成星星 / 子彈 / 巨大化 / 黃金加速) / 3 只有香蕉皮 / 4 只有直線彈 / 5 只有加速;2 無道具 = 不生成箱子
        static ItemKind ApplyRule(ItemKind k)
        {
            int rule = cfg != null ? cfg.ItemRule : 0;
            switch (rule)
            {
                case 1:
                    if (rng.Next(100) < 35) { ItemKind[] wild = { ItemKind.Star, ItemKind.Bullet, ItemKind.Mega, ItemKind.GoldBoost, ItemKind.TripleBumper, ItemKind.FireFlower, ItemKind.Boomerang }; return wild[rng.Next(wild.Length)]; }
                    return k;
                case 3: return rng.Next(100) < 45 ? ItemKind.TripleBanana : ItemKind.Oil;
                case 4: return rng.Next(100) < 40 ? ItemKind.TripleBumper : ItemKind.Bumper;
                case 5: return rng.Next(100) < 40 ? ItemKind.TripleBoost : ItemKind.Boost;
            }
            return k;
        }

        /// 直接給道具(除錯 / 測試 / 腳本);會取代目前持有並結束抽獎。
        public static void Give(RaceKart k, ItemKind kind)
        {
            if (!inited || k == null) return;
            var s = St(k);
            s.Rolling = false; s.RollTimer = 0f;
            if (kind == ItemKind.None)
            {
                s.Held = ItemKind.None; s.Charges = 0; s.Held2 = ItemKind.None; s.Charges2 = 0; s.GoldLeft = 0f; s.HornGuard = 0f;
                return;
            }
            s.GoldLeft = 0f;
            Grant(s, kind);                                              // 取代第 1 格(第 2 格不動)
        }

        /// 直接給第 2 格道具(測試 / 腳本);取代目前第 2 格。None = 清空第 2 格。
        public static void GiveSecond(RaceKart k, ItemKind kind)
        {
            if (!inited || k == null) return;
            var s = St(k);
            if (kind == ItemKind.None) { s.Held2 = ItemKind.None; s.Charges2 = 0; return; }
            GrantSecond(s, kind);
        }

        /// 交換兩格道具(兩格都有才行;黃金加速進行中不能換)。
        public static bool Swap(RaceKart k)
        {
            var s = St(k, false);
            if (s == null || s.Held == ItemKind.None || s.Held2 == ItemKind.None || s.GoldLeft > 0f) return false;
            ItemKind h = s.Held; s.Held = s.Held2; s.Held2 = h;
            int c = s.Charges; s.Charges = s.Charges2; s.Charges2 = c;
            s.HeldTime = 0f;
            SyncVis(s);
            if (Swapped != null) Swapped(k);
            return true;
        }

        static int ChargesFor(ItemKind kind)
        {
            return (kind == ItemKind.TripleBoost || kind == ItemKind.TripleBumper || kind == ItemKind.FireFlower
                    || kind == ItemKind.TripleBanana || kind == ItemKind.Boomerang) ? 3 : 1;
        }

        // 第 1 格用完:第 2 格遞補
        static void ShiftSlots(KState s)
        {
            s.Held = s.Held2; s.Charges = s.Charges2;
            s.Held2 = ItemKind.None; s.Charges2 = 0;
            if (s.Held == ItemKind.None) s.Charges = 0;
            s.HeldTime = 0f;
        }

        static void GrantSecond(KState s, ItemKind kind)
        {
            s.Held2 = kind; s.Charges2 = ChargesFor(kind);
            if (Got != null) Got(s.Kart, kind);
        }

        /// 閃電:被打到的車掉光兩格道具(Dropped 事件每個道具一次)。
        internal static void DropHeld(RaceKart victim)
        {
            var s = St(victim, false);
            if (s == null) return;
            ItemKind a = s.Held, b = s.Held2;
            if (a == ItemKind.None && b == ItemKind.None) return;
            s.Held = ItemKind.None; s.Charges = 0; s.Held2 = ItemKind.None; s.Charges2 = 0; s.HeldTime = 0f;
            if (s.GoldLeft > 0f) { s.GoldLeft = 0f; RaiseEffect(false, victim, ItemKind.GoldBoost); }
            ItemEffects.SyncOrbit(victim, 0);
            ItemEffects.SyncTrail(victim, 0);
            if (Dropped != null)
            {
                if (a != ItemKind.None) Dropped(victim, a);
                if (b != ItemKind.None) Dropped(victim, b);
            }
        }

        static void Grant(KState s, ItemKind kind)
        {
            s.Held = kind;
            s.Charges = ChargesFor(kind);
            s.HeldTime = 0f;
            s.AiDelay = 1f + 2f * (float)rng.NextDouble();      // AI:拿到後 1~3 s 才會用(追蹤彈等)
            s.AiRoll = (float)rng.NextDouble();                 // AI:ItemSmart 判定用
            if (Got != null) Got(s.Kart, kind);
        }

        /// 名次:優先 Progress.Rank(RaceCheckpoints 算);沒有進度時用賽道弧長粗估。
        public static int RankOf(RaceKart k)
        {
            if (k == null) return 1;
            if (k.Progress != null) return Mathf.Max(1, k.Progress.Rank);
            if (track == null) return 1;
            float lat;
            float my = track.Project(k.Position, -1f, out lat);
            int rank = 1;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i] == k) continue;
                if (track.Project(all[i].Position, -1f, out lat) > my) rank++;
            }
            return rank;
        }

        /// 追蹤彈目標:名次在前一名的車;領先者回 null(彈會沿賽道飛、途中鎖定前方的車)。
        public static RaceKart PickHomingTarget(RaceKart owner)
        {
            int rank = RankOf(owner);
            if (rank <= 1) return null;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == owner) continue;
                if (o.Progress != null && o.Progress.Rank == rank - 1) return o;
            }
            if (track == null) return null;
            // 沒有 Progress:取弧長上最近的前車
            float lat;
            float my = track.Project(owner.Position, -1f, out lat);
            RaceKart best = null; float bd = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == owner) continue;
                float ds = track.Project(o.Position, -1f, out lat) - my;
                if (ds < 0f) ds += track.Length;
                if (ds > 0f && ds < track.Length * 0.5f && ds < bd) { bd = ds; best = o; }
            }
            return best;
        }

        /// 藍殼目標:排除 owner 後名次最前的車(跑完的車不算)。
        public static RaceKart PickFirst(RaceKart owner)
        {
            RaceKart best = null; int br = int.MaxValue;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == owner) continue;
                if (o.Progress != null && o.Progress.Finished) continue;
                int r = RankOf(o);
                if (r < br) { br = r; best = o; }
            }
            return best;
        }

        // ------------------------------------------------------------ 使用
        public static bool TryUse(RaceKart k, bool aimBack) { return TryUse(k, aimBack, false, 0f); }

        // 丟射方向:有瞄準輸入就用 aimAngle(相對車頭的度數,0 前 / 90 右 / 180 後),否則 aimBack 把「道具預設方向」轉 180 度。
        // defDeg = 該道具不瞄準時的預設方向(往前射的是 0,油漬 / 假箱預設丟後方 = 180)。
        static Vector3 AimDir(RaceKart k, bool aimBack, bool aimActive, float aimAngle, float defDeg)
        {
            float a = aimActive ? aimAngle : (aimBack ? defDeg + 180f : defDeg);
            Vector3 f = k.Forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            return Quaternion.AngleAxis(a, Vector3.up) * f.normalized;
        }

        /// 使用第 1 格道具。aimActive = true 時以 aimAngle 為丟射方向(任意角度),否則照 aimBack 往前 / 往後。
        public static bool TryUse(RaceKart k, bool aimBack, bool aimActive, float aimAngle)
        {
            if (!inited || k == null) return false;
            var s = St(k, false);
            if (s == null || s.Held == ItemKind.None || s.Cooldown > 0f) return false;   // 第 2 格抽獎中也能用第 1 格
            ItemKind kind = s.Held;
            bool consume = true;
            ItemKind stolen = ItemKind.None; int stolenCh = 0;
            switch (kind)
            {
                case ItemKind.Boost:
                    k.Boost(BoostSeconds, BoostBonus);
                    break;
                case ItemKind.TripleBoost:
                    k.Boost(TripleSeconds, BoostBonus);
                    s.Cooldown = TripleGap;
                    break;
                case ItemKind.Shield:
                    s.ShieldLeft = ShieldSeconds;
                    CreateShieldVis(s);
                    break;
                case ItemKind.Oil:       // 香蕉皮
                    ItemProjectiles.DropOilAt(k, AimDir(k, aimBack, aimActive, aimAngle, 180f));      // 預設丟後方;手把下 = 往前丟 8 m
                    break;
                case ItemKind.TripleBanana:
                    ItemProjectiles.DropOilAt(k, AimDir(k, aimBack, aimActive, aimAngle, 180f));      // 一次丟一片(車尾還拖著的剩餘幾片會擋殼)
                    s.Cooldown = BananaGap;
                    break;
                case ItemKind.Boomerang:
                    ItemProjectiles.FireBoomerangAt(k, AimDir(k, aimBack, aimActive, aimAngle, 0f));
                    s.Cooldown = BoomGap;
                    break;
                case ItemKind.Boo:
                    stolen = StealFrom(k, out stolenCh);
                    ItemEffects.StartBoo(k);
                    break;
                case ItemKind.Bumper:
                    ItemProjectiles.FireHomingAt(k, AimDir(k, aimBack, aimActive, aimAngle, 0f));    // 2026-10-24:直線彈改龜殼(自動追蹤);預設往前,手把下 = 往後
                    break;
                case ItemKind.Homing:
                    ItemProjectiles.FireHomingAt(k, AimDir(k, aimBack, aimActive, aimAngle, 0f));
                    break;
                case ItemKind.Star:
                    ItemEffects.StartStar(k);
                    break;
                case ItemKind.Lightning:
                    UseAhead(k, ItemKind.Lightning, true);
                    break;
                case ItemKind.InkBlast:
                    UseAhead(k, ItemKind.InkBlast, false);
                    break;
                case ItemKind.Bomb:
                    ItemProjectiles.PlaceBombAt(k, AimDir(k, aimBack, aimActive, aimAngle, 0f));     // 預設往前拋 10 m;手把下 = 丟車後
                    break;
                case ItemKind.BlueShell:
                    ItemProjectiles.FireBlueShell(k);
                    break;
                case ItemKind.Mega:
                    ItemEffects.StartMega(k);
                    break;
                case ItemKind.Bullet:
                    ItemEffects.StartBullet(k, track);
                    break;
                case ItemKind.FakeBox:
                    ItemProjectiles.PlaceFakeBoxAt(k, AimDir(k, aimBack, aimActive, aimAngle, 180f)); // 預設丟車後;手把下 = 往前丟 14 m(同油漬)
                    break;
                case ItemKind.TripleBumper:
                    ItemProjectiles.FireHomingAt(k, AimDir(k, aimBack, aimActive, aimAngle, 0f));    // 依序射出 3 顆龜殼(追蹤)
                    s.Cooldown = TripleBumperGap;
                    break;
                case ItemKind.FireFlower:
                    ItemProjectiles.FireFlowerAt(k, AimDir(k, aimBack, aimActive, aimAngle, 0f));    // 3 發火球,間隔 FireGap
                    s.Cooldown = FireGap;
                    break;
                case ItemKind.Feather:
                    ItemEffects.StartFeather(k);
                    break;
                case ItemKind.Coin:
                    k.AddCoin(CoinAmount);
                    k.Boost(CoinBoostSeconds, CoinBoostBonus);
                    break;
                case ItemKind.GoldBoost:
                    if (s.GoldLeft <= 0f) { s.GoldLeft = GoldSeconds; RaiseEffect(true, k, ItemKind.GoldBoost); }
                    k.Boost(GoldBoostSeconds, BoostBonus);
                    s.Cooldown = GoldGap;
                    consume = false;                               // 6 s 內不消耗,時間到才由 Tick 收掉
                    break;
                case ItemKind.SuperHorn:
                    UseHorn(s, k);
                    break;
                default:
                    return false;
            }
            if (consume)
            {
                s.Charges--;
                if (s.Charges <= 0) { s.Held = ItemKind.None; s.Charges = 0; ShiftSlots(s); }
            }
            if (stolen != ItemKind.None)                                   // 幽靈偷到的道具:進空出來的格子,次數沿用對方剩下的
            {
                if (s.Held == ItemKind.None) { Grant(s, stolen); s.Charges = Mathf.Max(1, stolenCh); }
                else if (s.Held2 == ItemKind.None) { GrantSecond(s, stolen); s.Charges2 = Mathf.Max(1, stolenCh); }
            }
            SyncVis(s);
            if (UseInvincibleSeconds > 0f) k.SetInvincible(UseInvincibleSeconds);
            if (UseFxEnabled)
            {
                UseFxCount++; UseFxLastKind = kind; UseFxLastKart = k;
                RaceJuice.ItemUsed(k, kind, Mathf.Max(0.2f, UseInvincibleSeconds));
            }
            if (Used != null) Used(k, kind);
            return true;
        }

        // 幽靈:從「名次在前面且手上有道具」的車隨機偷一個(優先偷第 1 格,兩格都有時各 50%)。黃金加速進行中的不偷。
        static ItemKind StealFrom(RaceKart user, out int charges)
        {
            charges = 0;
            int myRank = RankOf(user);
            var cand = new List<KState>();
            for (int i = 0; i < stateList.Count; i++)
            {
                KState v = stateList[i];
                if (v.Kart == null || v.Kart == user || RankOf(v.Kart) >= myRank) continue;
                bool a = v.Held != ItemKind.None && v.GoldLeft <= 0f, b = v.Held2 != ItemKind.None;
                if (a || b) cand.Add(v);
            }
            if (cand.Count == 0) return ItemKind.None;
            KState victim = cand[rng.Next(cand.Count)];
            bool takeFirst = victim.Held != ItemKind.None && victim.GoldLeft <= 0f && (victim.Held2 == ItemKind.None || rng.Next(2) == 0);
            ItemKind got;
            if (takeFirst) { got = victim.Held; charges = victim.Charges; ShiftSlots(victim); }
            else { got = victim.Held2; charges = victim.Charges2; victim.Held2 = ItemKind.None; victim.Charges2 = 0; }
            SyncVis(victim);
            BooStolenTotal++;
            if (Dropped != null) Dropped(victim.Kart, got);
            return got;
        }

        /// 三連香蕉皮擋殼:victim 第 1 格是三連香蕉皮(車尾拖著)、殼從車後方來(projPos 在車頭方向的後半面)時,打掉一片香蕉皮並回傳 true(殼消失)。
        internal static bool TrailBlock(RaceKart victim, Vector3 projPos, ItemKind kind)
        {
            if (victim == null || victim.Invincible || victim.Stunned) return false;
            var s = St(victim, false);
            if (s == null || s.Held != ItemKind.TripleBanana || s.Charges <= 0) return false;
            Vector3 f = victim.Forward; f.y = 0f;
            Vector3 d = projPos - victim.Position; d.y = 0f;
            if (f.sqrMagnitude < 1e-6f || Vector3.Dot(f.normalized, d) > 0.3f) return false;     // 從前面 / 側前方來的不算
            s.Charges--;
            if (s.Charges <= 0) { s.Held = ItemKind.None; s.Charges = 0; ShiftSlots(s); }
            SyncVis(s);
            TrailBlockedTotal++;
            if (Blocked != null) Blocked(victim, kind);
            return true;
        }

        // 超級喇叭:12 m 內所有障礙 / 投射物消除(不暈眩任何人),並在 HornGuardSeconds 內擋掉一次藍殼
        static void UseHorn(KState s, RaceKart k)
        {
            HornDestroyedTotal += ItemProjectiles.DestroyNear(k.Position, ItemProjectiles.HornRadius, k);
            bool fresh = s.HornGuard <= 0f;
            s.HornGuard = HornGuardSeconds;
            ItemEffects.Blast(k.Position, ItemProjectiles.HornRadius, new Color(1f, 1f, 0.7f));
            if (fresh) RaiseEffect(true, k, ItemKind.SuperHorn);
        }

        // 閃電 / 墨水:對「名次比使用者前面」的所有車生效(走 Resolve,護盾 / 無敵規則一致)
        static void UseAhead(RaceKart user, ItemKind kind, bool bolt)
        {
            int myRank = RankOf(user);
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == user) continue;
                if (RankOf(o) >= myRank) continue;
                if (bolt) ItemEffects.Bolt(o.Position);
                Resolve(o, 0f, user, kind);
            }
        }

        // ------------------------------------------------------------ 命中
        /// 契約:檢查護盾 / 無敵後才 victim.Stun。stunSec 對油漬無意義(油漬走 Slip)。
        public static void ApplyHit(RaceKart victim, float stunSec, RaceKart by, ItemKind kind)
        {
            Resolve(victim, stunSec, by, kind);
        }

        /// 內部:回傳 true = 這次命中「被消耗」(護盾擋下或生效),彈體應該消失;
        /// false = 穿過去(目標無敵 / 暈眩中 / 暈眩免疫中),彈體繼續飛。
        internal static bool Resolve(RaceKart victim, float stunSec, RaceKart by, ItemKind kind)
        {
            if (victim == null) return false;
            if (victim.Invincible || victim.Stunned) return false;
            // 羽毛跳躍中:地面障礙(油漬 / 假箱 / 直線彈 / 火球 / 炸彈)碰不到
            if ((kind == ItemKind.Oil || kind == ItemKind.FakeBox || kind == ItemKind.Bumper || kind == ItemKind.FireFlower || kind == ItemKind.Bomb)
                && ItemEffects.Hopping(victim)) return false;
            var s = St(victim, false);
            if (s != null && s.ShieldLeft > 0f)
            {
                s.ShieldLeft = 0f; DestroyShieldVis(s);
                if (Blocked != null) Blocked(victim, kind);
                return true;
            }
            if (s != null && s.HornGuard > 0f && kind == ItemKind.BlueShell)     // 超級喇叭擋一次藍殼
            {
                s.HornGuard = 0f; RaiseEffect(false, victim, ItemKind.SuperHorn);
                if (Blocked != null) Blocked(victim, kind);
                return true;
            }
            bool applied;
            if (kind == ItemKind.Oil)
            {
                // 香蕉皮:打轉 1.0 s(整台車停下來轉圈,不只是變慢)+ 掉 1 枚金幣;之後有一般的暈眩後無敵,不會被連環踩
                applied = victim.Stun(ItemProjectiles.BananaSpin, ItemProjectiles.BananaCoinLoss);
                if (applied) victim.SetInvincible(ItemProjectiles.BananaSpin + PostStunInvincible);
            }
            else if (kind == ItemKind.Lightning)
            {
                ItemEffects.Shrink(victim, ItemEffects.ShrinkSeconds);
                DropHeld(victim);                                  // 閃電:兩格道具全部掉光
                applied = true;
            }
            else if (kind == ItemKind.InkBlast)
            {
                ItemEffects.Ink(victim, ItemEffects.InkSeconds);
                applied = true;
            }
            else
            {
                applied = victim.Stun(stunSec);
                if (applied) victim.SetInvincible(stunSec + PostStunInvincible);
            }
            if (applied && Hit != null) Hit(victim, by, kind);
            return applied;
        }

        // ------------------------------------------------------------ 護盾外觀(3 圈旋轉的環,半徑 1.6 m;不遮住車)
        static void CreateShieldVis(KState s)
        {
            if (s.ShieldVis != null || s.Kart == null) return;
            var g = new GameObject("ShieldVis");
            g.transform.SetParent(s.Kart.transform, false);
            g.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            Material m = RaceArt.Glow(new Color(0.35f, 0.8f, 1f), 1.4f);
            for (int r = 0; r < 3; r++)
            {
                var ring = new GameObject("Ring" + r);
                ring.transform.SetParent(g.transform, false);
                ring.transform.localRotation = Quaternion.Euler(r * 60f, r * 40f, 0f);
                var lr = ring.AddComponent<LineRenderer>();
                lr.useWorldSpace = false; lr.loop = true;
                const int N = 32;
                lr.positionCount = N;
                for (int i = 0; i < N; i++)
                {
                    float a = i * Mathf.PI * 2f / N;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 1.6f);
                }
                lr.widthMultiplier = 0.07f;
                lr.sharedMaterial = m;
                lr.startColor = lr.endColor = new Color(0.35f, 0.8f, 1f, 0.9f);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            s.ShieldVis = g;
        }

        static void DestroyShieldVis(KState s)
        {
            if (s.ShieldVis != null) Object.Destroy(s.ShieldVis);
            s.ShieldVis = null;
        }

        // ------------------------------------------------------------ AI 使用邏輯(RaceAI 之後呼叫;03 §2.4)
        /// AI 現在該不該用手上的道具。aimBack = 該往後(丟/射)還是往前;smart = ItemSmart(0..1),
        /// 沒抽中「好時機」機率的 AI 拿到 AiDelay(1~3 s)後就亂用。回 true 後呼叫端應該呼叫 TryUse(k, aimBack)。
        public static bool AiShouldUse(RaceKart k, out bool aimBack, float smart = 1f)
        {
            aimBack = false;
            if (!inited || k == null) return false;
            var s = St(k, false);
            if (s == null || s.Held == ItemKind.None || s.Cooldown > 0f) return false;
            float t = s.HeldTime;
            if (t < 0.3f) return false;
            if (s.AiRoll > smart) return t >= s.AiDelay;          // 不聰明:時間到就亂用(往前)

            bool targeted = Targeted(k);
            switch (s.Held)
            {
                case ItemKind.Boost:
                case ItemKind.TripleBoost:
                    if (k.Boosting) return false;
                    return StraightAhead(k, 40f) || t > 15f;
                case ItemKind.Shield:
                    return targeted || (RankOf(k) == 1 && t > 2f) || t > 20f;
                case ItemKind.Oil:
                    if (targeted) return true;                    // 被鎖定:丟香蕉皮干擾
                    if (KartInCone(k, 18f, 18f, true)) return true;
                    return t > 25f;
                case ItemKind.TripleBanana:
                    if (KartInCone(k, 18f, 18f, true)) return true;     // 後面有人追:丟一片;平常留著當車尾護盾
                    return t > 30f;
                case ItemKind.Bumper:
                    if (KartInCone(k, 40f, 5f, false)) return true;
                    if (KartInCone(k, 25f, 8f, true)) { aimBack = true; return true; }
                    return t > 20f;
                case ItemKind.Homing:
                    // 前面有人(有目標)才往前丟;自己是第 1 名(沒有目標)就等後面有人追上來,往後直線丟;偶爾(AiRoll 小)有人貼在屁股後也往後丟
                    if (RankOf(k) > 1)
                    {
                        if (s.AiRoll < 0.25f && KartInCone(k, 12f, 15f, true)) { aimBack = true; return true; }
                        return t >= s.AiDelay;
                    }
                    if (KartInCone(k, 25f, 12f, true)) { aimBack = true; return true; }
                    return t > 20f;
                case ItemKind.Boomerang:
                    if (KartInCone(k, 24f, 8f, false)) return true;
                    if (KartInCone(k, 18f, 10f, true)) { aimBack = true; return true; }
                    return t > 20f;
                case ItemKind.Boo:
                    return targeted || AheadHoldsItem(k) || t > 12f;
                case ItemKind.Star:
                    return true;
                case ItemKind.Bullet:
                    return t >= 0.5f;
                case ItemKind.BlueShell:
                    return true;
                case ItemKind.Lightning:
                    return AnyoneAhead(k) || t > 10f;
                case ItemKind.Mega:
                    if (KartInCone(k, 25f, 60f, false) || KartInCone(k, 12f, 180f, false)) return true;
                    return t > 8f;
                case ItemKind.InkBlast:
                    return AnyoneAhead(k) && (KartInCone(k, 45f, 25f, false) || t > 6f);
                case ItemKind.Bomb:
                    if (targeted) { aimBack = true; return true; }
                    if (KartInCone(k, 14f, 30f, false)) return true;
                    if (KartInCone(k, 8f, 30f, true)) { aimBack = true; return true; }
                    return t > 20f;
                case ItemKind.FakeBox:
                    if (targeted) return true;
                    if (KartInCone(k, 25f, 25f, true)) return true;
                    return t > 20f;
                case ItemKind.TripleBumper:
                case ItemKind.FireFlower:
                    if (KartInCone(k, 40f, 5f, false)) return true;
                    if (KartInCone(k, 25f, 8f, true)) { aimBack = true; return true; }
                    return t > 20f;
                case ItemKind.Feather:
                    return GroundHazardAhead(k, 14f) || t > 25f;
                case ItemKind.Coin:
                    return true;
                case ItemKind.GoldBoost:
                    if (s.GoldLeft > 0f) return true;                 // 窗口內一有空檔就再加速
                    if (k.Boosting) return false;
                    return StraightAhead(k, 40f) || t > 15f;
                case ItemKind.SuperHorn:
                    return targeted || HazardNear(k, 10f) || t > 25f;
            }
            return false;
        }

        public static bool AiShouldUse(RaceKart k)
        {
            bool b;
            return AiShouldUse(k, out b, 1f);
        }

        // 前面(名次比自己前)有人手上有道具(幽靈值得用)
        static bool AheadHoldsItem(RaceKart k)
        {
            int myRank = RankOf(k);
            for (int i = 0; i < stateList.Count; i++)
            {
                KState v = stateList[i];
                if (v.Kart == null || v.Kart == k) continue;
                if ((v.Held != ItemKind.None || v.Held2 != ItemKind.None) && RankOf(v.Kart) < myRank) return true;
            }
            return false;
        }

        // 半徑 r 內有別人的障礙 / 投射物(RaceItems.Hazards,Tick 更新)
        static bool HazardNear(RaceKart k, float r)
        {
            for (int i = 0; i < hazards.Count; i++)
            {
                RaceHazard h = hazards[i];
                if (h.OwnerKartId == k.KartId) continue;
                Vector3 d = h.Pos - k.Position; d.y = 0f;
                if (d.sqrMagnitude <= r * r) return true;
            }
            return false;
        }

        // 車頭前方 dist 公尺內、橫向 3 m 以內有地面障礙(羽毛可以跳過去的)
        static bool GroundHazardAhead(RaceKart k, float dist)
        {
            Vector3 f = k.Forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) return false;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            for (int i = 0; i < hazards.Count; i++)
            {
                RaceHazard h = hazards[i];
                if (h.OwnerKartId == k.KartId) continue;
                if (h.Kind != ItemKind.Oil && h.Kind != ItemKind.FakeBox && h.Kind != ItemKind.Bomb && h.Kind != ItemKind.Bumper && h.Kind != ItemKind.FireFlower) continue;
                Vector3 d = h.Pos - k.Position; d.y = 0f;
                float along = Vector3.Dot(d, f);
                if (along > 0f && along <= dist && Mathf.Abs(Vector3.Dot(d, right)) <= 3f) return true;
            }
            return false;
        }

        // 前方 dist 公尺內賽道曲率小(直線段)
        static bool StraightAhead(RaceKart k, float dist)
        {
            if (track == null) return true;
            float lat;
            float st = track.Project(k.Position, -1f, out lat);
            return Mathf.Abs(track.MaxCurvature(st, st + dist)) < 1f / 60f;
        }

        static bool AnyoneAhead(RaceKart k)
        {
            int myRank = RankOf(k);
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i] != k && RankOf(all[i]) < myRank) return true;
            return false;
        }

        // 前方(behind = false)或後方(behind = true)dist 公尺內、夾角 halfAngle 度以內有別的車
        static bool KartInCone(RaceKart k, float dist, float halfAngle, bool behind)
        {
            Vector3 f = k.Forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) return false;
            if (behind) f = -f;
            var all = RaceKart.All;
            for (int i = 0; i < all.Count; i++)
            {
                RaceKart o = all[i];
                if (o == null || o == k) continue;
                Vector3 d = o.Position - k.Position; d.y = 0f;
                float m = d.magnitude;
                if (m < 0.5f || m > dist) continue;
                if (Vector3.Angle(f, d) <= halfAngle) return true;
            }
            return false;
        }
    }
}
