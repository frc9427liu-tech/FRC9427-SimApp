using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    /// 賽車物理(模組 K,01 §1~2)。動態 Rigidbody 只負責碰撞與穿透處理;速度每個 FixedUpdate 先讀回、自己算、再寫回。
    /// M1:油門 / 轉向 / 煞車 / 倒車 / 貼地 / 撞牆 / 空中 / 路面 / 暈眩 / 無敵 / 加速 / 油漬。
    /// M2:hop / 漂移(方向鎖定)/ 迷你渦輪三段 / 空中 / 跳台特技 / 撞牆掃掠防穿透。
    /// M3 車對車:PhysX 對「車 vs 車」整組忽略(OnEnable 時對所有已存在的車 IgnoreCollision),改由每個物理步開頭的 StepKartPairs
    /// 用 1.2 x 2.0 m 的有向矩形(SAT)自己解:預測下一步會重疊就沿法線交換動量(質量加權、e=0.6、不增加動能)+ 位置修正;
    /// 同一對 0.15 s 內只彈一次(之後只做無彈性接觸,不會穿也不會黏)。事件走 Bumped(OtherKartId = 對方 KartId)。
    /// 車體座標:transform.position = 球形碰撞體中心(離地約 0.48 m);Create / Respawn 的 Pose 位置視為「地面接觸點」:
    /// 會向下探測真正的地面(碰撞體優先、其次 Ground 取樣器),把球心放在地面上方剛好 RideDist 的位置,出生不掉落、不陷地。
    [DisallowMultipleComponent]
    public class RaceKart : MonoBehaviour, IRacerBody
    {
        // ---- 瑪利歐賽車式機制(模組 K5:滑翔翼 / 特技 / 金幣 / 起跑加速 / 輪胎越野 / 滑翔翼升力)----
        public const int MaxCoins = 10;
        public const float CoinSpeedPerCoin = 0.007f;         // 每枚金幣最高速 +0.7%(乘法,獨立於 SpeedMul,不會被橡皮筋/道具覆寫)
        public const float GlideGravityMul = 0.35f;           // 滑翔中重力倍率
        // M8 飛越缺口的起飛跳台(GliderRamp.FixedLaunch):固定出場速度(水平 m/s 夾在 31..33、垂直 >= 11.8),與排氣量無關,50cc 也飛得過;
        // 滑翔重力 x0.26、下降速度上限 5.5 m/s(/GlideLift)、油門下壓的懲罰很輕 -> 中性滑翔 4~5 s、飛 ~140 m,最差情況(GlideLift 0.9 + 一直下壓)也有 ~130 m
        public const float GlideLaunchSpeed = 31f, GlideLaunchMax = 33f, GlideLaunchUp = 11.8f;
        public const float GapGlideGravityMul = 0.26f, GapGlideSinkBase = 5.5f, GapGlideDiveG = 0.15f, GapGlideDiveSink = 1f, GapGlideDiveSpeed = 2f;
        public const float GlideMaxSeconds = 8f;              // 滑翔最長秒數
        public const float GlideLandBoostSeconds = 0.6f;      // 平順落地加速秒數
        public const float GlideChargeRate = 2.0f;            // 滑翔中按住漂移鍵的充能速率(秒充能 / 秒;門檻同漂移 1.0 / 2.0 / 3.2)
        public const float TrickBoostSeconds = 0.7f;          // 特技落地加速秒數
        public const float TrickDuration = 0.6f;              // TrickProgress 0->1 所需秒數
        public const float TrickWindow = 0.45f;               // 離開跳台後多久內按漂移鍵 / 甩搖桿算特技
        public const float StartBoostSeconds = 0.8f, StartSpinSeconds = 0.8f;
        /// 離地、空中高度 > 3 m、垂直速度 > -4 m/s(仍在上升或剛過頂點)、水平速度 >= 10 m/s、離地至少 0.6 s -> 自動滑翔。
        /// (一般 20 度跳台飛 0.8 s 不會觸發,所以不影響普通跳台特技。)設 false 可關閉(只靠 BeginGlide / GliderRamp)。
        public static bool AutoGlide = true;
        /// 輪胎越野能力 0..1(KartLoadout.Combine 的 OffRoad;RaceSession 套用):草地/泥地/沙地的減速打折,0 = 沒有效果。
        public float OffRoad;
        /// 滑翔翼升力倍率(KartLoadout.Combine 的 GlideLift,約 0.9~1.2):越大下降越慢。
        public float GlideLift = 1f;

        int coins;
        /// 目前金幣數(0..10;寫入會夾範圍並發 CoinsChanged)。
        public int Coins { get { return coins; } set { ChangeCoins(value - coins); } }
        /// 金幣帶來的最高速倍率(1 + 0.007 x 金幣數)。
        public float CoinSpeedMul { get { return 1f + CoinSpeedPerCoin * coins; } }
        /// 無限加速模式:每秒增加的極速比例(0 = 關)。RaceSession 依 RaceConfig.Infinite 設定;所有車(含 AI)共用。
        public static float InfiniteRate;
        public const float InfiniteMaxMul = 6f;
        float infTime;
        public float InfiniteMul { get { return InfiniteRate > 0f ? Mathf.Min(InfiniteMaxMul, 1f + InfiniteRate * infTime) : 1f; } }
        public bool Gliding { get { return gliding; } }                       // 是否正在使用滑翔翼
        public float GlideTime { get { return glideTime; } }                  // 本次(或最近一次)滑翔經過秒數
        public float GlidePitch { get { return gliding ? glidePitch : 0f; } } // 滑翔俯仰 -1(上拉)..+1(下壓),給 KartVisual 傾斜翼面
        public float GlideCharge { get { return gliding ? glideCharge : 0f; } }                       // 空中迷你渦輪充能秒數
        public int GlideChargeLevel { get { return gliding ? KartMath.DriftLevel(glideCharge) : 0; } } // 0..3
        public bool Tricking { get { return trickArmed && !grounded; } }      // 是否正在做特技(落地後有 0.7 s 加速)
        public float TrickProgress { get { return Tricking ? Mathf.Clamp01(trickClock / TrickDuration) : 0f; } }   // 特技翻轉進度 0..1
        public bool StartSpinning { get { return spinTime > 0f; } }           // 起跑太早按油門:空轉中
        public float StartSpinRemaining { get { return spinTime; } }
        public StartBoostResult LastStartResult { get { return lastStart; } } // 最近一次起跑判定

        public static event Action<RaceKart> Glided;                          // 滑翔翼展開
        public static event Action<RaceKart, bool> GlideEnded;                // 滑翔結束(true = 平順落地加速;false = 超時 / 撞牆 / 被打 / 重生)
        public static event Action<RaceKart, int, int> CoinsChanged;          // (車, 新金幣數, 變化量)
        public static event Action<RaceKart, float> StartSpinBegan;           // 起跑空轉開始(秒數)

        // ---- 世界式移動技(集氣跳 / 跑牆 / 軌道滑行,原創實作;AI(RaceAI)一律不參與)----
        // 集氣跳規則(旁邊 4 m 內有別台車時放開不起跳):按漂移鍵 = 照舊先小跳(Hopped);小跳落地時若鍵還按著、|轉向| <= 0.35(KartMath.ChargeMinSteer)就開始集氣(火花 Lv1/2/3 = 0.5/1.1/1.8 s);
        //   放開 = 起跳(Lv1/2/3 初速 8/10.5/13 m/s)+ 自動特技,落地 0.7 s(+0.1 s/級)加速;集氣中轉向 > 0.35 且車速 > 9 = 轉成一般漂移(集氣作廢)。
        //   所以「按住同時轉向」仍是漂移(原有行為不變),只有「直行按住」才是集氣跳。
        // 跑牆規則:空中且在跑牆資格內(集氣跳 / 特技 / 離開跳台後 2.5 s),水平車速 >= 9,以與牆面夾角 <= 35 度碰到掛 RideableWall 的牆 = 吸附,
        //   沿牆水平前進最長 2 s;夾角決定爬升(夾角 / 35 x 8 m/s 初速,減速 9 m/s^2:近乎平行 = 水平跑,35 度約爬 3 m);牆沒了 / 時間到 / 往外打方向 / 按特技鍵 = 脫離,
        //   跑超過 0.35 s 脫離給 0.8 s 加速 + 自動特技(落地再加速);煞車脫離沒有加速。夾角 > 35 度 = 照舊撞牆。
        // 軌道滑行規則:空中車底進入軌道 0.85 m 以內(垂直 -0.4 ~ +0.6 m)、垂直速度 <= 1.5、不逆向 = 吸附;沿軌道自動前進(目標 1.08 x 極速),
        //   左右鍵微調橫移 +-0.9 m,特技鍵 = 跳躍加速(0.8 s 冷卻,加速 0.5 s),軌道尾端脫離 = 1.0 s 加速 + 自動特技;煞車 = 跳下(無加速)。
        // 特技無敵:TrickInvincible = true 時,特技中(空中特技預備 / 跑牆 / 軌道滑行)Invincible 為 true(道具擊中無效);落地即結束,不延長。
        public static bool TrickInvincible = true;
        public const float WallRideMaxSeconds = 2f, WallRideWindow = 2.5f, WallStick = 0.62f, WallMinSpeed = 9f, WallBoostMinTime = 0.35f, WallExitBoostSeconds = 0.8f, WallMaxClimb = 6f;
        public const float RailRideH = 0.55f, RailSteerMax = 0.9f, RailMinSpeed = 12f, RailSpeedMul = 1.08f, RailAccel = 14f, RailExitHop = 3f;
        public const float RailHopCooldown = 0.8f, RailHopBoostSeconds = 0.5f, RailHopSpeedGain = 2f, RailExitBoostSeconds = 1f;
        /// false = 這台車不使用集氣跳 / 跑牆 / 軌道(RaceAI 駕駛的車一律不用,不必設)。
        public bool AllowTech = true;
        public bool TechOn { get { return AllowTech && !(Source is RaceAI); } }
        public bool Charging { get { return charging; } }
        public float ChargeTime { get { return charging ? chargeTime : 0f; } }
        public int ChargeLevel { get { return charging ? KartMath.ChargeLevel(chargeTime) : 0; } }
        public bool ChargeJumping { get { return chargeAir && !grounded; } }
        public bool WallRiding { get { return wallRiding; } }
        public float WallRideTime { get { return wallRiding ? wallTime : 0f; } }
        public bool RailGrinding { get { return railing; } }
        public float RailTime { get { return railing ? railTime : 0f; } }
        /// 特技中(空中特技預備 / 跑牆 / 軌道滑行)。TrickInvincible 時視為無敵。
        public bool InTrick { get { return (trickArmed && !grounded) || wallRiding || railing; } }
        public static event Action<RaceKart, int> ChargeLevelUp, ChargeJumped;   // 集氣升級 / 集氣跳起跳(等級 1~3)
        public static event Action<RaceKart> WallRideBegan, RailBegan, RailHopped;
        public static event Action<RaceKart, bool> WallRideEnded, RailEnded;      // bool = 有給脫離加速
        // ------------------------------------------------------------ 靜態 / 事件
        public static readonly List<RaceKart> All = new List<RaceKart>();
        public static GroundSampler Ground;                 // RaceSession 設 RaceWorld.SampleGround;null = 只靠碰撞體 + RaceSurface
        /// 測試用:true = 不理 MenuSystem.Blocking(自動測試啟動時選單可能還開著)。正式遊戲不要設。
        public static bool IgnoreMenuBlock;
        /// 效能統計(測試 13 用):RaceKart 物理步累計 Stopwatch ticks 與次數。
        public static long PerfTicks, PerfCount;

        public static event Action<RaceKart, KartBump> Bumped;
        public static event Action<RaceKart, int> DriftLevelUp;       // 漂移充能升級瞬間(1 藍 / 2 橘 / 3 紫)
        public static event Action<RaceKart, float> BoostStart;       // 加速開始(秒數)
        public static event Action<RaceKart> Hopped, Landed, FellOut, ItemPressed, RespawnPressed, StunStart;

        // ------------------------------------------------------------ 幾何常數
        public const float SphereRadius = 0.45f;
        public const float BodyRadius = 0.40f;
        public const float BodyLength = 1.5f;
        public const float BodyWidth = 1.2f, BodyLen = 2.0f;   // 車對車用的矩形碰撞體(與外觀相符:寬約 1.2、長約 2.0)
        public const float Skin = 0.03f;                    // 球底離地的空隙,平常不接觸,碰撞靠車身膠囊與跳台
        public const float RideDist = SphereRadius + Skin;  // 車身中心離地平面的目標距離
        const float SnapReach = 0.6f;                       // 球底以下 0.6 m 內有地就吸附
        const float WallNormalY = 0.766f;                   // 法線 y 小於此(坡度大於 40 度)= 牆
        const float Gravity = 9.81f;
        const float KillY = -20f;
        const float CastRadCapsule = BodyRadius - 0.01f;    // 掃掠半徑略小於實體,留 1 cm 縫(不和 PhysX 接觸偏移打架)
        const float CastRadSphere = SphereRadius - 0.01f;
        const float BounceTurnTime = 0.12f;                 // 撞牆後車頭轉向彈開方向的時間
        const float HalfW = BodyWidth * 0.5f, HalfL = BodyLen * 0.5f;
        const float PairCooldown = 0.15f;                    // 同一對車兩次「彈開」的最短間隔
        const float PairSlop = 0.005f, PairMaxPush = 0.25f, PairMaxDy = 1.0f;   // 位置修正容許 / 每步最大推開量 / 高度差超過就不算碰撞
        const float DriftScrub = 0.15f, GripScrub = 0.25f;  // 抓地時側向動能變成速度損失的比例(見 KartMath.ApplyGrip)

        // ------------------------------------------------------------ 公開欄位
        public int KartId;
        public string DisplayName;
        public bool IsPlayer;
        public KartStats Stats;                             // null = new KartStats()
        public IKartInput Source;                           // null = 不動
        public RacerProgress Progress { get; set; }
        public float SpeedMul { get { return speedMulExt; } set { speedMulExt = value; } }
        public bool Frozen { get; set; }

        // ------------------------------------------------------------ 唯讀狀態
        public KartInput Input { get { return used; } }
        public Vector3 Position { get { return rb != null ? rb.position : transform.position; } }
        public Vector3 Velocity { get { return rb != null ? rb.linearVelocity : Vector3.zero; } }
        public Vector3 Forward { get { return heading; } }
        public float Speed { get { return speedF; } }
        public float SpeedKmh { get { return speedF * 3.6f; } }
        public float MaxSpeed
        {
            get
            {
                return S.TopSpeed * speedMulExt * CoinSpeedMul * InfiniteMul * SurfSpeedMul(surface, SurfaceProps.Of(surface).SpeedMul)
                       * (slipTime > 0f ? slipSpeedMul : 1f) * (boostTime > 0f ? 1f + boostBonus : 1f);
            }
        }
        public float YawDeg { get { return Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg; } }
        public Vector3 GroundNormal { get { return grounded ? groundNormal : upVec; } }
        public bool Grounded { get { return grounded; } }
        public float AirTime { get { return airTime; } }
        public bool Drifting { get { return drifting; } }
        public int DriftDir { get { return drifting ? driftDir : 0; } }
        public int DriftLevel { get { return drifting ? driftLevel : 0; } }
        /// 漂移充能秒數(0..3.2;門檻 1.0 / 2.0 / 3.2 = Lv1/2/3)。沒在漂移 = 0。
        public float DriftCharge { get { return drifting ? driftCharge : 0f; } }
        public bool Boosting { get { return boostTime > 0f; } }
        public float BoostRemaining { get { return boostTime; } }
        public bool Stunned { get { return stunTime > 0f; } }
        /// 暈眩 / 打轉剩餘秒數與總長(KartVisual 用來畫車身打轉動畫)。
        public float StunRemaining { get { return stunTime; } }
        public float StunTotal { get { return stunTotal; } }
        public bool Invincible { get { return invTime > 0f || (TrickInvincible && InTrick); } }
        public Surface Surface { get { return surface; } }
        public int RacerId { get { return KartId; } }
        public bool ThrottleHeld { get { return used.Throttle > 0.1f; } }

        // ------------------------------------------------------------ 內部狀態
        Rigidbody rb;
        Collider[] cols;
        KartStats S { get { return Stats ?? (Stats = new KartStats()); } }
        static PhysicsMaterial slickMat;

        KartInput cur, used;                                // Update 讀到的 / FixedUpdate 實際用的
        bool pDrift, pItem, pRespawn;                       // 邊緣鎖存

        Vector3 heading = Vector3.forward;                  // 車頭(垂直於 upVec)
        Vector3 upVec = Vector3.up;                         // 平滑後的車身上方向
        Vector3 groundNormal = Vector3.up;
        Vector3 lastWritten, lastGoodPos;
        bool grounded, prevNoSnap;
        float airTime, speedF, steerSm, reverseTimer, fixedClock;
        Surface surface = Surface.Road;
        float speedMulExt = 1f;

        float boostTime, boostBonus, boostFloor;
        float stunTime, stunTotal, stunImmune, stunTargetV;
        float invTime;
        float slipTime, slipGripMul = 1f, slipSpeedMul = 1f;
        float steerLock, ghostTime, bumpSlipTime;
        bool kbPending; KartBump kb;                         // 車對車事件(StepKartPairs 設,同一步結束時發)
        Vector3 pendingImpulse;
        // 脫困偵測:有油門卻被牆/別台車夾住(0.75 s 內位移 < 0.5 m 且有接觸)就往接觸反方向給一個脫困速度
        Vector3 escAcc, stuckPos; int escN; float stuckT0; bool stuckInit;
        bool fellFired, warnedNaN, warnedSpeed;

        // M2:hop / 漂移 / 迷你渦輪 / 特技
        bool drifting, driftIntent, hopAir, trickArmed;
        int driftDir, driftLevel;
        float driftCharge, hopClock;
        float lastDriftPressClock = -10f, rampPressTimer, sinceRampLeave = 99f;
        float bounceYawRate, bounceYawTime;
        // K5:滑翔 / 特技 / 起跑
        bool gliding, prevGlideRamp, prevGlideFixed, fixedGlide, pendingGlided, pendingGlideEnd, pendingGlideSmooth, glideHitWall;
        float glideTime, glideCharge, glidePitch, glideArmed, glideCap;
        float trickClock, prevSteerRaw;
        float spinTime, sbPressRemain; bool sbActive, sbHeld; StartBoostResult lastStart;
        float pendingBoostEvt;                              // > 0 = 這一步結束時要發 BoostStart(秒數)
        static readonly RaycastHit[] sweepHits = new RaycastHit[16];

        // 世界式移動技
        bool charging, chargeAir, wallRiding, railing;
        float chargeTime, rideWindow, wallTime, wallHS, wallVy, wallStartY, wallCooldown, railCooldown, railHopCd, railT, railLat, railTime, railSpeed;
        int chargeAirLv, chargeLvSeen, evChargeUp, evChargeJump, evWallE, evRailE;
        bool evWallB, evRailB, evRailHop;
        Vector3 wallN, wallTan;
        KartRail rail;

        // 撞牆(OnCollision 記錄,下一個 FixedUpdate 套用)
        bool bumpPending; Vector3 bumpN, bumpPoint; Collider bumpCol;
        Collider lastBumpCol; float lastBumpClock = -10f;

        // ------------------------------------------------------------ 建立
        /// 建立一台車(Rigidbody + 碰撞體,不含外觀)。p.position = 地面接觸點。
        public static RaceKart Create(Transform parent, Pose p, KartStats stats, int id, string name)
        {
            var go = new GameObject("Kart_" + id);
            if (parent != null) go.transform.SetParent(parent, false);
            var k = go.AddComponent<RaceKart>();
            k.KartId = id; k.DisplayName = name; k.Stats = stats ?? new KartStats();
            k.rb.mass = Mathf.Max(1f, k.Stats.Mass);
            k.Teleport(k.SeatOnGround(p.position, p.rotation), p.rotation, false);
            return k;
        }

        void Awake()
        {
            uid = ++uidSeq;
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = 0f; rb.angularDamping = 0f;
            rb.sleepThreshold = 0f;
            rb.maxDepenetrationVelocity = 8f;
            rb.mass = Mathf.Max(1f, S.Mass);

            if (slickMat == null)
            {
                slickMat = new PhysicsMaterial("KartSlick")
                {
                    dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
            var sph = gameObject.AddComponent<SphereCollider>();
            sph.radius = SphereRadius; sph.sharedMaterial = slickMat;
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 2; cap.radius = BodyRadius; cap.height = BodyLength;
            cap.center = new Vector3(0f, -0.30f + BodyRadius, 0f);   // 膠囊底部比球底高 0.15 m
            cap.sharedMaterial = slickMat;
            cols = new Collider[] { sph, cap };
            rb.centerOfMass = Vector3.zero;

            heading = Flatten(transform.forward, Vector3.up, Vector3.forward);
            lastGoodPos = transform.position;
        }

        void OnEnable()
        {
            if (All.Contains(this)) return;
            for (int i = 0; i < All.Count; i++) IgnorePhysX(this, All[i]);   // 車對車碰撞改由 StepKartPairs 處理
            All.Add(this);
        }
        void OnDisable() { All.Remove(this); }

        // ------------------------------------------------------------ 輸入(Update 讀,FixedUpdate 用)
        void Update()
        {
            KartInput k = default;
            if (Source != null && !Frozen && (IgnoreMenuBlock || !MenuSystem.Blocking))
            {
                k = Source.Read(Time.deltaTime);
                k.Throttle = Clean01(k.Throttle); k.Brake = Clean01(k.Brake);
                k.Steer = float.IsNaN(k.Steer) ? 0f : Mathf.Clamp(k.Steer, -1f, 1f);
            }
            cur = k;
            if (k.DriftDown) pDrift = true;
            if (k.ItemDown) pItem = true;
            if (k.RespawnDown) pRespawn = true;
        }

        static float Clean01(float x) { return float.IsNaN(x) ? 0f : Mathf.Clamp01(x); }

        KartInput TakeInput()
        {
            KartInput k = cur;
            k.DriftDown = pDrift; k.ItemDown = pItem; k.RespawnDown = pRespawn;
            pDrift = pItem = pRespawn = false;
            if (Frozen || (!IgnoreMenuBlock && MenuSystem.Blocking)) k = default;
            return k;
        }

        // ------------------------------------------------------------ 撞擊回呼
        void OnCollisionEnter(Collision c) { HandleContacts(c); }
        void OnCollisionStay(Collision c) { HandleContacts(c); }

        void HandleContacts(Collision c)
        {
            if (c.collider == null) return;
            if (c.rigidbody != null && c.rigidbody.GetComponent<RaceKart>() != null) return;   // 車對車:StepKartPairs(PhysX 已忽略,保險)
            Collider id = c.collider;
            if (id == lastBumpCol && fixedClock - lastBumpClock < 0.1f) return;                 // 同一面牆 0.1 s 冷卻
            Vector3 me = rb.position;
            Vector3 nSum = Vector3.zero, pSum = Vector3.zero; int w = 0;
            for (int i = 0; i < c.contactCount; i++)
            {
                ContactPoint cp = c.GetContact(i);
                Vector3 n = cp.normal;
                if (Vector3.Dot(n, me - cp.point) < 0f) n = -n;       // 保證指向車這一側
                if (n.y >= WallNormalY) continue;                      // 地面 / 緩坡不是牆
                nSum += n; pSum += cp.point; w++;
            }
            if (w == 0) return;
            bumpPending = true;
            bumpN += nSum; bumpPoint = pSum / w; bumpCol = id;
        }

        // ------------------------------------------------------------ 物理步
        void FixedUpdate()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            StepBody();
            PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; PerfCount++;
        }

        void StepBody()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f || rb == null) return;
            KartStats st = S;
            if (st.Mass > 1f && !Mathf.Approximately(rb.mass, st.Mass)) rb.mass = st.Mass;
            fixedClock += dt;
            if (InfiniteRate > 0f && speedF > 3f) infTime += dt;   // 開跑後(車速 > 3 m/s)才累計
            if (Time.fixedTime != lastPairTime) { lastPairTime = Time.fixedTime; StepKartPairs(dt); }   // 每個物理步只解一次,在任何車算自己之前

            KartInput inp = TakeInput();
            if (sbActive && !Frozen) ResolveStartWindow();       // 倒數結束(解凍)那一刻判定起跑加速 / 空轉
            TickTimers(dt);
            bool stunned = stunTime > 0f;
            CheckStuck(rb.position, inp.Throttle, stunned);
            if (stunned) { inp.Throttle = 0f; inp.Brake = 0f; inp.Steer = 0f; inp.DriftHeld = false; inp.DriftDown = false; }
            if (spinTime > 0f) inp.Throttle = 0f;                // 起跑太早按油門:空轉,動不了
            used = inp;
            if (inp.DriftDown) lastDriftPressClock = fixedClock;

            Vector3 v = rb.linearVelocity;
            Vector3 pos = rb.position;
            if (!IsFinite(v) || !IsFinite(pos))
            {
                if (!warnedNaN) { warnedNaN = true; Debug.LogWarning("RaceKart " + KartId + ": NaN velocity/position, reset"); }
                v = Vector3.zero; pos = lastGoodPos; rb.position = pos; lastWritten = Vector3.zero;
            }
            lastGoodPos = pos;
            if (pendingImpulse != Vector3.zero) { v += pendingImpulse; pendingImpulse = Vector3.zero; }

            // 撞牆(PhysX 回呼記錄的接觸;主要防線是下面的掃掠,這裡是備援)
            bool bumped = false; KartBump bump = default;
            if (bumpPending && !wallRiding && !railing) { v = ResolveBump(v, out bumped, out bump); if (bumped) NoteContact(bump.Normal); }
            bumpPending = false; bumpN = Vector3.zero;

            // ---- M2:hop 請求 / 漂移意圖與結束
            bool hopNow = false;
            if (inp.DriftDown)
            {
                if (grounded && !drifting && !hopAir && !stunned)
                {
                    if (prevNoSnap) rampPressTimer = 0.2f;                  // 在跳台上按 = 特技預備,不 hop
                    else { hopNow = true; driftIntent = true; hopClock = 0f; }
                }
                else if (!grounded && !gliding && sinceRampLeave < TrickWindow) ArmTrick();   // 剛飛出跳台 0.45 s 內按
            }
            // 甩搖桿也算特技:離台後 0.45 s 內單步轉向變化 > 1.3(左右甩到底)
            if (!grounded && !gliding && !trickArmed && sinceRampLeave < TrickWindow && Mathf.Abs(inp.Steer - prevSteerRaw) > 1.3f) ArmTrick();
            prevSteerRaw = inp.Steer;
            if (driftIntent)
            {
                hopClock += dt;
                if (!inp.DriftHeld || stunned || hopClock > 0.8f) driftIntent = false;      // 放開 = 只是小跳
                else if (!drifting && (hopClock >= KartMath.HopDriftDelay || (grounded && hopClock > 0.05f)))
                {
                    if (KartMath.CanStartDrift(inp.Steer, speedF)) StartDrift(inp.Steer > 0f ? 1 : -1);
                    else if (grounded)                                                        // 落地了還不夠條件 = 只是小跳;鍵還按著且直行 = 開始集氣
                    {
                        driftIntent = false;
                        if (TechOn && inp.DriftHeld && Mathf.Abs(inp.Steer) <= KartMath.ChargeMinSteer && inp.Brake <= 0.5f && !stunned) BeginCharge();
                    }
                }
            }
            if (drifting)
            {
                if (!inp.DriftHeld) EndDrift(true);                                           // 放開 = 迷你渦輪
                else if (stunned || speedF < KartMath.DriftEndSpeed || inp.Brake > 0.5f || airTime > 0.6f) EndDrift(false);
            }

            // 集氣跳:放開 = 起跳;轉彎 = 轉成漂移;煞車 / 暈眩 / 離地 = 作廢
            bool chargeNow = false; int chargeLv = 0;
            if (charging)
            {
                if (!inp.DriftHeld)
                {
                    chargeLv = KartMath.ChargeLevel(chargeTime);
                    CancelCharge();
                    chargeNow = chargeLv >= 1 && grounded && !stunned && JumpClear();   // 旁邊 4 m 內有別台車就不起跳(跳過去落地會疊在對方身上)
                }
                else if (stunned || inp.Brake > 0.5f || !grounded || drifting) CancelCharge();
                else if (Mathf.Abs(inp.Steer) > KartMath.ChargeMinSteer && speedF > KartMath.DriftMinSpeed) { CancelCharge(); StartDrift(inp.Steer > 0f ? 1 : -1); }
                else
                {
                    chargeTime += dt;
                    int cl = KartMath.ChargeLevel(chargeTime);
                    if (cl > chargeLvSeen) { chargeLvSeen = cl; evChargeUp = cl; }
                }
            }

            // 轉向(先平滑再過曲線),先轉車頭再分解速度
            float steerTarget = (stunned || steerLock > 0f) ? 0f : inp.Steer;
            steerSm += (steerTarget - steerSm) * (1f - Mathf.Exp(-10f * dt));
            float yawRate;
            if (drifting) yawRate = KartMath.DriftYawRate(steerSm, driftDir);
            else
            {
                yawRate = KartMath.YawRate(st, speedF, KartMath.SteerCurve(steerSm));
                if (!grounded) yawRate *= gliding ? 0.7f : chargeAir ? 0.6f : hopAir ? 1f : 0.35f;                // hop 的滯空不減轉向;跳台飛行才減;滑翔可以偏航
            }
            if (bounceYawTime > 0f) yawRate += bounceYawRate;
            if (yawRate != 0f) heading = Flatten(Quaternion.AngleAxis(yawRate * dt, upVec) * heading, upVec, heading);

            int levelUp = 0;
            if (drifting)
            {
                driftCharge = Mathf.Min(driftCharge + KartMath.DriftChargeRate(steerSm, driftDir) * dt, KartMath.DriftLv3);
                int lv = KartMath.DriftLevel(driftCharge);
                if (lv > driftLevel) { driftLevel = lv; levelUp = lv; }
            }

            // 地面探測
            KartProbeResult pr = KartGroundProbe.Probe(pos, upVec, heading, RideDist + SnapReach + 0.2f, Ground);
            bool wasGrounded = grounded;
            bool nowGrounded = false;
            float gap = 0f;
            Vector3 gN = upVec;
            if (pr.Found)
            {
                gN = pr.Normal;
                gap = pr.Dist - RideDist;
                float vNg = Vector3.Dot(v, gN);
                if (wasGrounded)
                {
                    float lim = prevNoSnap ? 0.1f : SnapReach;     // 跳台:不吸附,離開邊緣一定飛起
                    nowGrounded = gap < lim && vNg < 2f && Vector3.Angle(groundNormal, gN) < 30f;
                }
                else
                {
                    nowGrounded = gap <= 0.05f && vNg <= 0.5f;
                }
            }

            if (hopNow || chargeNow) nowGrounded = false;                   // hop / 集氣跳:這一步離地
            if (railing) nowGrounded = false;                               // 軌道上不貼地
            if (wasGrounded && !nowGrounded && !hopNow && !chargeNow && prevNoSnap)       // 飛出跳台
            {
                sinceRampLeave = 0f; rideWindow = WallRideWindow;
                if (rampPressTimer > 0f) ArmTrick();                        // 離台前 0.2 s 內按過 = 特技
            }
            if (wasGrounded && !nowGrounded && !hopNow && !chargeNow && !gliding && (glideArmed > 0f || prevGlideRamp))   // 滑翔跳台 / BeginGlide() 預約
            {
                bool rampLaunch = prevGlideRamp && prevGlideFixed;
                BeginGlideInternal();
                if (rampLaunch) { fixedGlide = true; v = GliderLaunch(v); }
            }

            if (nowGrounded && wallRiding) EndWallRide(false);               // 跑牆時碰到地面 = 落地
            bool landed = false, hopLanding = false;
            if (nowGrounded)
            {
                Vector3 n = gN;
                SurfaceProps props = SurfaceProps.Of(pr.Surface);
                landed = !wasGrounded;
                if (landed) { hopLanding = hopAir && !chargeAir; hopAir = false; sinceRampLeave = 99f; }
                float landSpeed = landed ? -Vector3.Dot(lastWritten, n) : 0f;
                if (landed && gliding) GlideLanding(landSpeed, st);
                if (landed && trickArmed)
                {
                    trickArmed = false;
                    float tb = TrickBoostSeconds + (chargeAirLv > 1 ? 0.1f * (chargeAirLv - 1) : 0f);   // 集氣跳高等級多一點點
                    StartBoost(tb, BoostBonus(st), false);   // StartBoost 取最大不疊加:跳台特技只給這一次
                    pendingBoostEvt = Mathf.Max(pendingBoostEvt, tb);
                }
                if (landed) { chargeAir = false; chargeAirLv = 0; rideWindow = 0f; }
                surface = pr.Surface; groundNormal = n; prevNoSnap = props.NoSnap; prevGlideRamp = pr.GliderRamp; prevGlideFixed = pr.GliderFixed;

                upVec = Vector3.Slerp(upVec, n, 1f - Mathf.Exp(-12f * dt)).normalized;
                heading = Flatten(heading, upVec, heading);
                Vector3 f = Flatten(heading, n, heading);
                Vector3 r = Vector3.Cross(n, f);
                float vF = Vector3.Dot(v, f), vL = Vector3.Dot(v, r);

                if (landed && landSpeed > 12f) { vF *= 0.9f; vL *= 0.9f; }

                if (pr.Surface == Surface.BoostPad) StartBoost(1.0f, 0.4f, boostTime <= 0f);

                float top = st.TopSpeed * speedMulExt * CoinSpeedMul * InfiniteMul * SurfSpeedMul(pr.Surface, props.SpeedMul) * (slipTime > 0f ? slipSpeedMul : 1f)
                            * (boostTime > 0f ? 1f + boostBonus : 1f);
                if (boostFloor > 0f) { vF = Mathf.Max(vF, Mathf.Min(boostFloor, top)); boostFloor = 0f; }

                // 坡面重力
                Vector3 aSlope = Vector3.ProjectOnPlane(Vector3.down * Gravity, n) * 0.7f;
                vF += Vector3.Dot(aSlope, f) * dt;
                vL += Vector3.Dot(aSlope, r) * dt;

                // 漂移中總速度不超過極速:車頭方向的目標速度要扣掉側滑分量
                float topF = drifting ? Mathf.Sqrt(Mathf.Max(0f, top * top - vL * vL)) : top;
                vF = StepLongitudinal(vF, inp, topF, props, dt, st, stunned);

                float grip = props.Grip * ((drifting ? st.DriftGrip : st.Grip) / 12f) * (slipTime > 0f ? slipGripMul : 1f)
                           * (bumpSlipTime > 0f ? 0.4f : 1f);       // 剛被車撞完短暫放鬆抓地,推開的速度才滑得出去
                KartMath.ApplyGrip(ref vF, ref vL, grip, dt, drifting ? DriftScrub : GripScrub, topF);

                float vN = Mathf.Clamp(-gap * 20f, -10f, 10f);   // 貼地:每步修正約 1/3 的高度誤差
                v = f * vF + r * vL + n * vN;
                speedF = vF;
                airTime = 0f;
            }
            else
            {
                upVec = Vector3.Slerp(upVec, Vector3.up, 1f - Mathf.Exp(-3f * dt)).normalized;
                heading = Flatten(heading, upVec, heading);
                airTime += dt;
                if (sinceRampLeave < 90f) sinceRampLeave += dt;
                if (trickArmed) trickClock += dt;
                if (rideWindow > 0f) rideWindow -= dt;
                if (hopNow) { v += (grounded ? groundNormal : upVec) * st.HopSpeed; hopAir = true; }
                else if (chargeNow)
                {
                    v += (grounded ? groundNormal : upVec) * KartMath.ChargeJumpSpeed(chargeLv);
                    hopAir = true; chargeAir = true; chargeAirLv = chargeLv; evChargeJump = chargeLv;
                    ArmTrick();                                   // 自動特技(ArmTrick 同時給跑牆資格)
                }
                else if (hopAir && airTime > 0.5f && !chargeAir) hopAir = false;
                if (!gliding && AutoGlide && !wallRiding && !railing) TryAutoGlide(pos, v);
                if (gliding) v = GlideStep(v, inp, dt, st);
                else v += Vector3.down * st.AirGravity * dt;
                if (v.y < -40f) v.y = -40f;                       // 終端速度
                if (boostFloor > 0f) boostFloor = 0f;
                speedF = Vector3.Dot(v, heading);
            }
            grounded = nowGrounded;
            if (!nowGrounded) v = AirTech(pos, v, inp, dt, stunned);

            // 掃掠防穿牆:最高速 boost 一步 0.58 m,PhysX CCD 擋不住時由這裡把速度限到「剛好貼牆」,並直接做撞牆回彈
            {
                bool b2 = false; KartBump bp2 = default;
                if (!wallRiding && !railing) v = WallSweep(pos, v, dt, out b2, out bp2);
                if (b2) { bumped = true; bump = bp2; speedF = Vector3.Dot(v, heading); NoteContact(bp2.Normal); }
            }
            if (gliding && bumped)                                 // 滑翔中撞牆 / 撞樹:失速並解除
            {
                glideHitWall = true;
                v.x *= 0.6f; v.z *= 0.6f;
                speedF = Vector3.Dot(v, heading);
                EndGlide(false);
            }

            if (v.sqrMagnitude > 3600f)
            {
                if (!warnedSpeed) { warnedSpeed = true; Debug.LogWarning("RaceKart " + KartId + ": speed > 60 m/s clamped"); }
                v = v.normalized * 60f;
            }

            rb.linearVelocity = v;
            rb.MoveRotation(Quaternion.LookRotation(heading, upVec));
            lastWritten = v;

            // 事件(最後才發,避免監聽者呼叫 Teleport 後又被這一步的速度蓋掉)
            if (hopNow && Hopped != null) Hopped(this);
            if (evChargeUp > 0) { int e = evChargeUp; evChargeUp = 0; if (ChargeLevelUp != null) ChargeLevelUp(this, e); }
            if (evChargeJump > 0) { int e = evChargeJump; evChargeJump = 0; if (ChargeJumped != null) ChargeJumped(this, e); }
            if (evWallB) { evWallB = false; if (WallRideBegan != null) WallRideBegan(this); }
            if (evWallE > 0) { bool bo = evWallE == 2; evWallE = 0; if (WallRideEnded != null) WallRideEnded(this, bo); }
            if (evRailB) { evRailB = false; if (RailBegan != null) RailBegan(this); }
            if (evRailHop) { evRailHop = false; if (RailHopped != null) RailHopped(this); }
            if (evRailE > 0) { bool bo = evRailE == 2; evRailE = 0; if (RailEnded != null) RailEnded(this, bo); }
            if (levelUp > 0 && DriftLevelUp != null) DriftLevelUp(this, levelUp);
            if (pendingBoostEvt > 0f) { float pb = pendingBoostEvt; pendingBoostEvt = 0f; if (BoostStart != null) BoostStart(this, pb); }
            if (pendingGlided) { pendingGlided = false; if (Glided != null) Glided(this); }
            if (pendingGlideEnd) { pendingGlideEnd = false; if (GlideEnded != null) GlideEnded(this, pendingGlideSmooth); }
            if (landed && airTimeBeforeLanding > 0.1f && !hopLanding) { if (Landed != null) Landed(this); }
            if (bumped && Bumped != null) Bumped(this, bump);
            if (kbPending) { kbPending = false; if (Bumped != null) Bumped(this, kb); }
            if (inp.ItemDown && ItemPressed != null) ItemPressed(this);
            if (inp.RespawnDown && RespawnPressed != null) RespawnPressed(this);
            if (pos.y < KillY && !fellFired) { fellFired = true; if (FellOut != null) FellOut(this); }
        }

        void NoteContact(Vector3 awayN) { escAcc += HorizontalOf(awayN).normalized; escN++; }

        void CheckStuck(Vector3 pos, float throttle, bool stunned)
        {
            if (!stuckInit) { stuckInit = true; stuckT0 = fixedClock; stuckPos = pos; return; }
            if (fixedClock - stuckT0 < 0.75f) return;
            bool want = throttle > 0.3f && !stunned && !Frozen && grounded;
            if (want && escN > 0 && HorizontalOf(pos - stuckPos).magnitude < 0.5f)
            {
                Vector3 e = HorizontalOf(escAcc);
                e = e.sqrMagnitude > 0.01f ? e.normalized : -HeadH(this);   // 接觸方向互相抵銷(被夾在中間)就往車尾退
                pendingImpulse += e * 5f;
            }
            stuckT0 = fixedClock; stuckPos = pos; escAcc = Vector3.zero; escN = 0;
        }

        float airTimeBeforeLanding;   // 落地那一步之前的滯空時間(給 Landed 事件判斷用)

        void TickTimers(float dt)
        {
            airTimeBeforeLanding = airTime;
            if (boostTime > 0f) { boostTime -= dt; if (boostTime <= 0f) { boostTime = 0f; boostBonus = 0f; } }
            if (stunTime > 0f) { stunTime -= dt; if (stunTime <= 0f) { stunTime = 0f; stunImmune = 0.5f; } }
            else if (stunImmune > 0f) stunImmune -= dt;
            if (invTime > 0f) invTime -= dt;
            if (slipTime > 0f) { slipTime -= dt; if (slipTime <= 0f) { slipTime = 0f; slipGripMul = 1f; slipSpeedMul = 1f; } }
            if (steerLock > 0f) steerLock -= dt;
            if (bounceYawTime > 0f) bounceYawTime -= dt;
            if (rampPressTimer > 0f) rampPressTimer -= dt;
            if (ghostTime > 0f) { ghostTime -= dt; if (ghostTime <= 0f) ghostTime = 0f; }
            if (bumpSlipTime > 0f) bumpSlipTime -= dt;
            if (glideArmed > 0f) glideArmed -= dt;
            if (spinTime > 0f) { spinTime -= dt; if (spinTime < 0f) spinTime = 0f; }
            if (wallCooldown > 0f) wallCooldown -= dt;
            if (railCooldown > 0f) railCooldown -= dt;
            if (railHopCd > 0f) railHopCd -= dt;
        }

        float StepLongitudinal(float vF, KartInput inp, float top, SurfaceProps props, float dt, KartStats st, bool stunned)
        {
            if (stunned) return Mathf.MoveTowards(vF, stunTargetV, 20f * dt);
            float t = inp.Throttle, b = inp.Brake;
            if (b > 0.05f)
            {
                if (vF > 0.5f) { reverseTimer = 0f; return Mathf.Max(0f, vF - st.BrakeDecel * b * dt); }
                reverseTimer += dt;
                if (reverseTimer < 0.25f && vF > -0.5f) return Mathf.MoveTowards(vF, 0f, st.BrakeDecel * dt);
                float rmax = st.ReverseMax * props.SpeedMul;
                if (vF < -rmax) return Mathf.MoveTowards(vF, -rmax, 6f * dt);
                return Mathf.Max(vF - st.ReverseAccel * b * dt, -rmax);
            }
            reverseTimer = 0f;
            if (t > 0.05f)
            {
                if (vF < -0.5f) return Mathf.Min(0f, vF + st.BrakeDecel * dt);   // 倒車中踩油門:先煞到停
                float target = top * t;
                if (vF > target + 0.01f)                                         // 超速(加速結束、進草地):快速掉回
                {
                    float d = Mathf.Max(st.CoastDrag, 6f) + props.Drag;
                    return Mathf.Max(target, vF - d * dt);
                }
                return vF + KartMath.Accel(st, vF, target, boostTime > 0f) * dt;
            }
            float drag = st.CoastDrag + 0.05f * Mathf.Abs(vF) + props.Drag;       // 滑行
            return Mathf.MoveTowards(vF, 0f, drag * dt);
        }

        Vector3 ResolveBump(Vector3 v, out bool applied, out KartBump bump)
        {
            applied = false; bump = default;
            Vector3 nH = new Vector3(bumpN.x, 0f, bumpN.z);
            if (nH.sqrMagnitude < 1e-4f) return v;
            nH.Normalize();
            Vector3 prevH = new Vector3(lastWritten.x, 0f, lastWritten.z);
            float approach; bool scrape;
            Vector3 newH = KartMath.WallResponse(prevH, nH, out approach, out scrape);
            if (approach <= 0f) return v;
            applied = true;
            OnWallResponse(nH, approach, scrape, newH, bumpCol);
            bump = new KartBump
            {
                OtherKartId = -1, Point = bumpPoint, Normal = nH,
                Speed = approach, K = Mathf.Clamp01(approach / 12f)
            };
            float vy = grounded ? Mathf.Min(v.y, 0f) : v.y;
            return new Vector3(newH.x, vy, newH.z);
        }

        /// 撞牆回應的共同副作用(回呼備援與掃掠共用):冷卻、鎖轉向、車頭轉向彈開方向、結束漂移。
        void OnWallResponse(Vector3 nH, float approach, bool scrape, Vector3 newH, Collider col)
        {
            lastBumpClock = fixedClock; lastBumpCol = col;
            if (!scrape)
            {
                steerLock = 0.15f;
                float deg = KartMath.BounceTurnDeg(new Vector3(heading.x, 0f, heading.z), newH);
                if (deg != 0f) { bounceYawRate = deg / BounceTurnTime; bounceYawTime = BounceTurnTime; }
            }
            if (approach > 8f) EndDrift(false);
        }

        /// 沿速度方向掃掠(膠囊車身 + 球),找最近的「牆面」(法線 y < 0.766,忽略有 Rigidbody 的碰撞體與 trigger)。
        bool SweepForWall(Vector3 pos, Vector3 v, float dt, out RaycastHit best)
        {
            best = default;
            float speed = v.magnitude;
            if (speed < 0.5f) return false;
            Vector3 dir = v / speed;
            float dist = speed * dt + 0.05f;
            float bestD = float.MaxValue; bool found = false;
            Vector3 c = pos + upVec * 0.1f;
            int n = Physics.CapsuleCastNonAlloc(c + heading * 0.35f, c - heading * 0.35f, CastRadCapsule, dir, sweepHits, dist,
                                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            PickWall(n, dir, ref best, ref bestD, ref found);
            n = Physics.SphereCastNonAlloc(pos, CastRadSphere, dir, sweepHits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            PickWall(n, dir, ref best, ref bestD, ref found);
            return found;
        }

        static void PickWall(int n, Vector3 dir, ref RaycastHit best, ref float bestD, ref bool found)
        {
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = sweepHits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null) continue;       // 自己 / 其他車 / 動態物
                if (h.distance <= 0f) continue;                                                  // 起點就重疊:法線不可靠,交給 PhysX 回呼
                if (h.normal.y >= WallNormalY || h.normal.y < -0.5f) continue;                   // 地面 / 緩坡 / 天花板
                if (Vector3.Dot(dir, h.normal) > -0.01f) continue;                               // 不是迎面
                if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
            }
        }

        /// 掃掠防穿牆。這一步會碰到牆時:撞擊夠大(法向接近速度 >= 1 m/s 且不在 0.1 s 冷卻)= 直接做撞牆回應(擦牆 / 回彈);
        /// 否則只把「朝牆的速度」限制到剛好貼牆(順著牆滑)。最多處理兩面牆(牆角)。
        Vector3 WallSweep(Vector3 pos, Vector3 v, float dt, out bool bumped, out KartBump bump)
        {
            bumped = false; bump = default;
            for (int it = 0; it < 2; it++)
            {
                RaycastHit h;
                if (!SweepForWall(pos, v, dt, out h)) break;
                Vector3 nH = new Vector3(h.normal.x, 0f, h.normal.z);
                if (nH.sqrMagnitude < 0.01f) break;
                nH.Normalize();
                Vector3 vH = new Vector3(v.x, 0f, v.z);
                float vn = -Vector3.Dot(vH, nH);                       // 朝牆的速度(> 0 = 接近)
                if (vn <= 0.05f) break;
                float cosA = Mathf.Max(0.05f, -Vector3.Dot(v.normalized, h.normal));
                float gapN = h.distance * cosA;                         // 到牆面的法向距離
                if (vn * dt <= gapN) break;                             // 這一步碰不到
                bool cooling = h.collider == lastBumpCol && fixedClock - lastBumpClock < 0.1f;
                if (!cooling && vn >= 1f)
                {
                    float approach; bool scrape;
                    Vector3 newH = KartMath.WallResponse(vH, nH, out approach, out scrape);
                    if (approach > 0f)
                    {
                        OnWallResponse(nH, approach, scrape, newH, h.collider);
                        if (!bumped) bump = new KartBump { OtherKartId = -1, Point = h.point, Normal = nH, Speed = approach, K = Mathf.Clamp01(approach / 12f) };
                        bumped = true;
                        v = new Vector3(newH.x, v.y, newH.z);
                        continue;
                    }
                }
                v += nH * (vn - Mathf.Max(0f, gapN) / dt);            // 限制朝牆速度到剛好貼牆,切線速度不動
            }
            return v;
        }

        // ------------------------------------------------------------ 漂移
        static float BoostBonus(KartStats st) { return Mathf.Max(0.05f, st.BoostMul - 1f); }

        void StartDrift(int dir)
        {
            drifting = true; driftDir = dir; driftCharge = 0f; driftLevel = 0; driftIntent = false;
        }

        /// 結束漂移。giveBoost = 放開漂移鍵:依等級給迷你渦輪(0.5 / 1.0 / 1.6 s);其他原因結束(撞牆、煞車、暈眩、太慢)充能作廢。
        void EndDrift(bool giveBoost)
        {
            if (!drifting) return;
            int lv = driftLevel;
            drifting = false; driftDir = 0; driftCharge = 0f; driftLevel = 0; driftIntent = false;
            if (giveBoost && lv > 0)
            {
                float sec = KartMath.DriftBoostSeconds(lv);
                StartBoost(sec, BoostBonus(S), false);
                pendingBoostEvt = Mathf.Max(pendingBoostEvt, sec);
            }
        }

        // ------------------------------------------------------------ K5:特技 / 滑翔 / 金幣 / 起跑
        void ArmTrick()
        {
            if (trickArmed) return;
            trickArmed = true; trickClock = 0f; rideWindow = WallRideWindow;
        }

        // ------------------------------------------------------------ 集氣跳 / 跑牆 / 軌道滑行
        /// 集氣跳起跳前的淨空檢查:水平 4 m、垂直 4 m 內沒有別台車(車對車碰撞在空中高度差 > 1 m 時不解,落下來會和對方重疊)。
        bool JumpClear()
        {
            Vector3 me = rb.position;
            for (int i = 0; i < All.Count; i++)
            {
                RaceKart o = All[i];
                if (o == null || o == this || o.rb == null) continue;
                Vector3 d = o.rb.position - me;
                if (Mathf.Abs(d.y) < 4f && d.x * d.x + d.z * d.z < 16f) return false;
            }
            return true;
        }

        void BeginCharge() { charging = true; chargeTime = 0f; chargeLvSeen = 0; }
        void CancelCharge() { charging = false; chargeTime = 0f; chargeLvSeen = 0; }

        /// 空中的移動技:軌道滑行中 / 跑牆中就繼續該狀態;否則看有沒有可吸附的軌道(優先)或牆。回傳新速度。
        Vector3 AirTech(Vector3 pos, Vector3 v, KartInput inp, float dt, bool stunned)
        {
            bool on = TechOn && !stunned;
            if (railing) { if (!on) { EndRail(false); return v; } return RailStep(pos, v, inp, dt); }
            if (wallRiding) { if (!on) { EndWallRide(false); return v; } return WallRideStep(pos, v, inp, dt); }
            if (!on || gliding || Frozen) return v;
            if (railCooldown <= 0f && KartRails.All.Count > 0)
            {
                KartRail r; float t;
                if (KartRails.Find(pos + Vector3.down * SphereRadius, v, out r, out t)) { BeginRail(r, t, v); return RailStep(pos, v, inp, dt); }
            }
            if (rideWindow > 0f && wallCooldown <= 0f && TryWallAttach(pos, v, dt)) return WallRideStep(pos, v, inp, dt);
            return v;
        }

        // ---- 軌道
        void BeginRail(KartRail r, float t, Vector3 v)
        {
            railing = true; rail = r; railT = t; railLat = 0f; railTime = 0f; railHopCd = 0.25f;
            railSpeed = Mathf.Max(HorizontalOf(v).magnitude, Mathf.Min(MaxSpeed, RailMinSpeed));
            EndDrift(false); CancelCharge(); hopAir = false; evRailB = true;
        }

        Vector3 RailStep(Vector3 pos, Vector3 v, KartInput inp, float dt)
        {
            railTime += dt;
            if (inp.Brake > 0.5f)                                  // 煞車 = 跳下(無加速)
            {
                Vector3 d0 = rail.DirAt(railT);
                float sp0 = railSpeed * 0.8f;
                EndRail(false);
                speedF = sp0;
                return d0 * sp0;
            }
            if (inp.DriftDown && railHopCd <= 0f)                  // 特技鍵 = 跳躍加速
            {
                railHopCd = RailHopCooldown; railSpeed += RailHopSpeedGain; evRailHop = true;
                StartBoost(RailHopBoostSeconds, BoostBonus(S), false);
                pendingBoostEvt = Mathf.Max(pendingBoostEvt, RailHopBoostSeconds);
            }
            float target = Mathf.Max(MaxSpeed * RailSpeedMul, RailMinSpeed);
            railSpeed = Mathf.MoveTowards(railSpeed, target, (railSpeed < target ? RailAccel : 6f) * dt);
            railT += railSpeed * dt;
            if (railT >= rail.Length)                              // 尾端脫離:加速 + 自動特技
            {
                Vector3 d = rail.DirAt(rail.Length);
                float sp = railSpeed;
                EndRail(true);
                speedF = sp;
                return d * sp + Vector3.up * RailExitHop;
            }
            Vector3 pt = rail.PointAt(railT), dir = rail.DirAt(railT);
            Vector3 dh = HorizontalOf(dir);
            Vector3 right = dh.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, dh).normalized : Vector3.right;
            railLat += (Mathf.Clamp(inp.Steer, -1f, 1f) * RailSteerMax - railLat) * (1f - Mathf.Exp(-6f * dt));
            Vector3 want = pt + right * railLat + Vector3.up * RailRideH;
            Vector3 err = want - pos - dir * (railSpeed * dt);
            float cap = 12f * dt;
            if (err.magnitude > cap) err = err.normalized * cap;
            if (dh.sqrMagnitude > 1e-6f) heading = Flatten(Vector3.Slerp(heading, dh.normalized, 1f - Mathf.Exp(-14f * dt)), Vector3.up, dh);
            upVec = Vector3.Slerp(upVec, Vector3.up, 1f - Mathf.Exp(-8f * dt)).normalized;
            speedF = railSpeed;
            return dir * railSpeed + err / dt;
        }

        /// 結束軌道滑行。exitBoost = 走到尾端:給脫離加速 + 自動特技(特技中無敵直到落地)。
        void EndRail(bool exitBoost)
        {
            if (!railing) return;
            railing = false; rail = null; railCooldown = 0.7f;
            evRailE = exitBoost ? 2 : 1;
            if (exitBoost)
            {
                StartBoost(RailExitBoostSeconds, BoostBonus(S), false);
                pendingBoostEvt = Mathf.Max(pendingBoostEvt, RailExitBoostSeconds);
                ArmTrick();
            }
        }

        // ---- 跑牆
        static bool IsWallHit(in RaycastHit h)
        {
            return h.collider != null && h.collider.attachedRigidbody == null && h.distance > 0f && h.normal.y < 0.55f && h.normal.y > -0.3f;
        }

        bool TryWallAttach(Vector3 pos, Vector3 v, float dt)
        {
            Vector3 vH = HorizontalOf(v);
            float hs = vH.magnitude;
            if (hs < WallMinSpeed) return false;
            Vector3 dir = vH / hs;
            int n = Physics.SphereCastNonAlloc(pos, 0.3f, dir, sweepHits, hs * dt + 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue; int bi = -1;
            for (int i = 0; i < n; i++)
                if (IsWallHit(sweepHits[i]) && sweepHits[i].distance < bestD) { bestD = sweepHits[i].distance; bi = i; }
            if (bi < 0) return false;
            RaycastHit h = sweepHits[bi];
            if (h.collider.GetComponentInParent<RideableWall>() == null) return false;
            Vector3 nH = HorizontalOf(h.normal);
            if (nH.sqrMagnitude < 0.01f) return false;
            nH.Normalize();
            float deg = KartMath.WallApproachDeg(vH, nH);
            if (!KartMath.CanWallAttach(deg)) return false;         // 太正面 = 照舊撞牆
            wallRiding = true; wallN = nH; wallTime = 0f; wallHS = hs; wallStartY = pos.y;
            wallVy = KartMath.WallClimbVy(deg);
            Vector3 t = Vector3.Cross(Vector3.up, nH);
            wallTan = Vector3.Dot(t, vH) >= 0f ? t : -t;
            EndDrift(false); CancelCharge(); hopAir = false; rideWindow = 0f; evWallB = true;
            return true;
        }

        bool WallRay(Vector3 origin, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, -wallN, sweepHits, 1.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = sweepHits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.normal.y >= 0.55f || h.normal.y < -0.3f) continue;
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found && best.collider.GetComponentInParent<RideableWall>() != null;
        }

        Vector3 WallRideStep(Vector3 pos, Vector3 v, KartInput inp, float dt)
        {
            wallTime += dt;
            Vector3 hh = HorizontalOf(heading); if (hh.sqrMagnitude < 1e-6f) hh = wallTan; hh.Normalize();
            Vector3 rightH = Vector3.Cross(Vector3.up, hh);
            bool peel = inp.Steer * Vector3.Dot(rightH, wallN) > 0.6f;      // 往外打方向 = 離開牆
            RaycastHit h;
            bool ok = WallRay(pos, out h);
            if (!ok) ok = WallRay(pos + wallTan * 0.9f, out h);              // 前方一點(牆彎進來 / 牆尾)
            if (!ok || wallTime >= WallRideMaxSeconds || peel || inp.Brake > 0.5f || inp.DriftDown)
                return LeaveWall(inp.Brake <= 0.5f);
            Vector3 nn = HorizontalOf(h.normal);
            if (nn.sqrMagnitude > 0.01f) wallN = Vector3.Slerp(wallN, nn.normalized, 0.5f).normalized;
            Vector3 t = Vector3.Cross(Vector3.up, wallN);
            wallTan = Vector3.Dot(t, wallTan) >= 0f ? t : -t;
            wallVy = Mathf.Max(0f, wallVy - KartMath.WallClimbDecel * dt);
            if (pos.y - wallStartY >= WallMaxClimb) wallVy = 0f;
            wallHS = Mathf.MoveTowards(wallHS, Mathf.Max(wallHS, MaxSpeed * 0.9f), 8f * dt);
            float corr = h.distance - WallStick;
            Vector3 nv = wallTan * wallHS + Vector3.up * wallVy - wallN * Mathf.Clamp(corr * 15f, -4f, 4f);
            heading = Flatten(Vector3.Slerp(heading, wallTan, 1f - Mathf.Exp(-14f * dt)), Vector3.up, wallTan);
            upVec = Vector3.Slerp(upVec, (wallN * 0.85f + Vector3.up * 0.35f).normalized, 1f - Mathf.Exp(-8f * dt)).normalized;   // 輪子貼牆的傾斜
            speedF = wallHS;
            return nv;
        }

        Vector3 LeaveWall(bool allowBoost)
        {
            bool boost = allowBoost && wallTime >= WallBoostMinTime;
            Vector3 vel = wallTan * wallHS + wallN * 3f + Vector3.up * (boost ? 2f : 0f);
            EndWallRide(boost);
            speedF = Vector3.Dot(vel, heading);
            return vel;
        }

        /// 結束跑牆。boost = 脫離加速 + 自動特技;false = 純脫離(被打 / 落地 / 失去資格),照常掉下來。
        void EndWallRide(bool boost)
        {
            if (!wallRiding) return;
            wallRiding = false; wallCooldown = 0.6f;
            evWallE = boost ? 2 : 1;
            if (boost)
            {
                StartBoost(WallExitBoostSeconds, BoostBonus(S), false);
                pendingBoostEvt = Mathf.Max(pendingBoostEvt, WallExitBoostSeconds);
                ArmTrick();
            }
            rideWindow = 0f;
        }


        /// 草地 / 泥地 / 沙地 / 界外的減速依 OffRoad 打折(OffRoad = 0 時完全等於原本的 SpeedMul)。
        float SurfSpeedMul(Surface s, float baseMul)
        {
            if (OffRoad <= 0f || baseMul >= 1f || s == Surface.Water) return baseMul;
            return Mathf.Lerp(baseMul, 1f, 0.5f * Mathf.Clamp01(OffRoad));
        }

        /// 模組 W 的滑翔跳台觸發器呼叫。已在空中 = 立刻展開;還在地面(跳台上)= 預約 1.5 s,離地那一步展開。
        /// 滑翔中 / 暈眩中 / 被凍結回 false。
        public bool BeginGlide()
        {
            if (gliding || Frozen || stunTime > 0f) return false;
            if (!grounded) { BeginGlideInternal(); return true; }
            glideArmed = 1.5f;
            return true;
        }

        /// M8:滑翔跳台的固定出場:水平速度夾到 [GlideLaunchSpeed, GlideLaunchMax](50cc 也一樣快),垂直速度至少 GlideLaunchUp,滑翔上限同步放寬。
        Vector3 GliderLaunch(Vector3 v)
        {
            Vector3 vh = HorizontalOf(v);
            float hs = vh.magnitude;
            Vector3 dir = hs > 0.5f ? vh / hs : HeadH(this);
            hs = Mathf.Clamp(hs, GlideLaunchSpeed, GlideLaunchMax);
            glideCap = GlideLaunchMax + 2f;
            return new Vector3(dir.x * hs, Mathf.Max(v.y, GlideLaunchUp), dir.z * hs);
        }

        void BeginGlideInternal()
        {
            if (gliding) return;
            gliding = true; fixedGlide = false; glideTime = 0f; glideCharge = 0f; glidePitch = 0f; glideArmed = 0f; glideHitWall = false;
            trickArmed = false; hopAir = false; driftIntent = false;
            EndDrift(false);
            Vector3 vh = HorizontalOf(rb.linearVelocity);
            glideCap = Mathf.Max(MaxSpeed * 1.2f, vh.magnitude);
            pendingGlided = true;
        }

        /// 結束滑翔。smooth = 平順落地(UI / 特效區分)。
        void EndGlide(bool smooth)
        {
            if (!gliding) return;
            gliding = false; glidePitch = 0f; glideCharge = 0f;
            pendingGlideEnd = true; pendingGlideSmooth = smooth;
        }

        void TryAutoGlide(Vector3 pos, Vector3 v)
        {
            if (hopAir || Frozen || stunTime > 0f || airTime < 0.6f || v.y < -4f) return;
            if (v.x * v.x + v.z * v.z < 100f) return;
            if ((int)(fixedClock * 60f + 0.5f) % 3 != 0) return;            // 每 3 步探一次,省射線
            KartProbeResult pr = KartGroundProbe.Probe(pos, upVec, heading, 3.5f, Ground);
            if (!pr.Found) BeginGlideInternal();                            // 腳下 3.5 m 內沒有地 = 空中高度 > 3 m
        }

        /// 滑翔一步:重力 x0.35、前進速度保持(輕微阻力)、油門 = 下壓(增速、下降快)、煞車 = 上拉(減速、增升力)、轉向 = 偏航
        /// (速度方向跟著車頭轉)。下降速度有上限(約 6 m/s,依俯仰與 GlideLift 增減),按住漂移鍵累積空中迷你渦輪。
        Vector3 GlideStep(Vector3 v, KartInput inp, float dt, KartStats st)
        {
            glideTime += dt;
            float dive = Mathf.Clamp01(inp.Throttle), pull = Mathf.Clamp01(inp.Brake);
            glidePitch += ((dive - pull) - glidePitch) * (1f - Mathf.Exp(-6f * dt));

            Vector3 vH = HorizontalOf(v);
            float hs = vH.magnitude;
            Vector3 hd = HeadH(this);
            Vector3 dir = hs > 0.1f ? vH / hs : hd;
            dir = Vector3.Slerp(dir, hd, 1f - Mathf.Exp(-5f * dt));
            dir.y = 0f; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : hd;

            hs -= 0.4f * dt;                                               // 輕微空氣阻力
            hs += (dive * (fixedGlide ? GapGlideDiveSpeed : 5f) - pull * 6f) * dt;   // 下壓增速、上拉減速
            hs = Mathf.Clamp(hs, Mathf.Min(8f, glideCap), glideCap);

            float g = st.AirGravity * (fixedGlide ? GapGlideGravityMul * (1f + GapGlideDiveG * dive - 0.5f * pull) : GlideGravityMul * (1f + 0.8f * dive - 0.5f * pull));
            v.y -= g * dt;
            float sink = Mathf.Max(1.5f, (fixedGlide ? GapGlideSinkBase + GapGlideDiveSink * dive - 2f * pull : 6f + 6f * dive - 3f * pull) / Mathf.Max(0.3f, GlideLift));
            if (v.y < -sink) v.y = Mathf.MoveTowards(v.y, -sink, 30f * dt);

            if (inp.DriftHeld) glideCharge = Mathf.Min(glideCharge + GlideChargeRate * dt, KartMath.DriftLv3);

            if (glideTime >= GlideMaxSeconds) EndGlide(false);              // 滑翔最長 8 s,之後是一般空中
            return new Vector3(dir.x * hs, v.y, dir.z * hs);
        }

        /// 滑翔中落地:垂直落地速度 <= 10 m/s、沒撞牆、至少滑 0.3 s = 平順落地 -> 0.6 s 加速;空中累積的迷你渦輪依等級給 0.5 / 1.0 / 1.6 s(兩者取大不疊加)。
        void GlideLanding(float landSpeed, KartStats st)
        {
            bool smooth = landSpeed <= 13f && !glideHitWall && glideTime >= 0.3f;
            float sec = smooth ? GlideLandBoostSeconds : 0f;
            sec = Mathf.Max(sec, KartMath.DriftBoostSeconds(KartMath.DriftLevel(glideCharge)));
            if (sec > 0f)
            {
                StartBoost(sec, BoostBonus(st), false);
                pendingBoostEvt = Mathf.Max(pendingBoostEvt, sec);
            }
            EndGlide(smooth);
        }

        /// 撿到金幣(模組 W 的撿取偵測呼叫)。已滿 10 枚回 false。
        public bool AddCoin(int n = 1)
        {
            if (n <= 0 || coins >= MaxCoins) return false;
            ChangeCoins(n);
            return true;
        }

        /// 掉金幣(被撞 / 暈眩 Stun 會自動呼叫 LoseCoins(2))。回傳實際掉了幾枚。
        public int LoseCoins(int n)
        {
            if (n <= 0 || coins <= 0) return 0;
            int before = coins;
            ChangeCoins(-n);
            return before - coins;
        }

        void ChangeCoins(int delta)
        {
            int nc = Mathf.Clamp(coins + delta, 0, MaxCoins);
            if (nc == coins) return;
            int d = nc - coins;
            coins = nc;
            if (CoinsChanged != null) CoinsChanged(this, coins, d);
        }

        /// 起跑加速窗口(整合者在 RaceSession 倒數中「每幀」呼叫,最後 GO 那刻再呼叫一次 remain <= 0):
        /// countdownRemain = 倒數剩餘秒數(3 -> 0),throttle = 玩家目前油門(0..1;倒數時車子是 Frozen,RaceKart 自己讀不到輸入,所以要傳進來)。
        /// 判定看「油門按下那一刻的剩餘秒數」:0.4~1.0 -> GO 時加速 0.8 s;> 1.5 太早 -> GO 後空轉 0.8 s;其他 -> 沒事;GO 時沒按著 -> 沒事。
        /// 就算沒呼叫 remain <= 0,Frozen 解除的那個物理步也會自動判定。
        public void StartBoostWindow(float countdownRemain, float throttle)
        {
            sbActive = true;
            if (countdownRemain <= 0f) { ResolveStartWindow(); return; }
            bool held = throttle > 0.1f;
            if (held && !sbHeld) sbPressRemain = countdownRemain;           // 油門按下的瞬間(上升緣)
            sbHeld = held;
        }

        /// 同上,但油門由 RaceKart 自己從 Source 讀(只有玩家車有效;AI 車請用 RollAiStartBoost)。
        public void StartBoostWindow(float countdownRemain)
        {
            float thr = 0f;
            if (IsPlayer && Source != null && (IgnoreMenuBlock || !MenuSystem.Blocking)) thr = Clean01(Source.Read(Time.deltaTime).Throttle);
            StartBoostWindow(countdownRemain, thr);
        }

        void ResolveStartWindow()
        {
            if (!sbActive) return;
            sbActive = false;
            StartBoostResult r = StartBoostResult.None;
            if (sbHeld)
            {
                if (sbPressRemain >= 0.4f && sbPressRemain <= 1.0f) r = StartBoostResult.Boost;
                else if (sbPressRemain > 1.5f) r = StartBoostResult.Spin;
            }
            sbHeld = false;
            lastStart = r;
            if (r == StartBoostResult.Boost) StartBoost(StartBoostSeconds, BoostBonus(S), true);
            else if (r == StartBoostResult.Spin)
            {
                spinTime = StartSpinSeconds;
                EndDrift(false);
                if (StartSpinBegan != null) StartSpinBegan(this, StartSpinSeconds);
            }
        }

        /// AI 起跑加速:依難度(0..3)隨機,簡單 20% ... 困難 80%(KartMath.AiStartBoostChance)。GO 那刻呼叫一次;回傳有沒有拿到加速。
        public bool RollAiStartBoost(int aiLevel, System.Random rng)
        {
            bool ok = rng != null && rng.NextDouble() < KartMath.AiStartBoostChance(aiLevel);
            lastStart = ok ? StartBoostResult.Boost : StartBoostResult.None;
            if (ok) StartBoost(StartBoostSeconds, BoostBonus(S), true);
            return ok;
        }

        // ------------------------------------------------------------ 命令
        /// 加速:不疊加,取剩餘時間最大、加成最大。
        public void Boost(float seconds, float speedBonus = 0.25f) { StartBoost(seconds, speedBonus, true); }

        void StartBoost(float seconds, float bonus, bool fire)
        {
            if (seconds <= 0f) return;
            bool fresh = boostTime <= 0f;
            if (fresh) boostFloor = Mathf.Max(0f, speedF) + 3f;   // 保證 vF 至少比進入時快 3 m/s
            boostTime = Mathf.Max(boostTime, seconds);
            boostBonus = Mathf.Max(boostBonus, bonus);
            if (fire && BoostStart != null) BoostStart(this, seconds);
        }

        /// 暈眩 / 打轉;無敵、免疫、已在暈眩中回 false。coinLoss = 掉幾枚金幣(預設 2,香蕉皮 1)。
        public bool Stun(float seconds, int coinLoss = 2)
        {
            if (seconds <= 0f || Invincible || stunImmune > 0f || stunTime > 0f) return false;
            stunTime = seconds; stunTotal = seconds;
            EndDrift(false); driftIntent = false;
            CancelCharge(); EndWallRide(false); EndRail(false);
            EndGlide(false);                                   // 滑翔中被打:翼收起來
            LoseCoins(coinLoss);                               // 被撞 / 暈眩掉金幣(預設 2 枚;香蕉皮 1 枚)
            stunTargetV = Mathf.Max(0f, speedF) * 0.3f;
            boostTime = 0f; boostBonus = 0f; boostFloor = 0f;
            if (StunStart != null) StunStart(this);
            return true;
        }

        public void SetInvincible(float seconds) { invTime = Mathf.Max(invTime, seconds); }

        /// 油漬:一段時間內抓地與極速變差。
        public void Slip(float seconds, float gripMul = 0.3f, float speedMul = 0.6f)
        {
            slipTime = Mathf.Max(slipTime, seconds);
            slipGripMul = gripMul; slipSpeedMul = speedMul;
        }

        /// 瞬間速度變化(世界座標),下一個物理步套用。
        public void ApplyImpulse(Vector3 deltaV) { pendingImpulse += deltaV; }

        /// 傳送。pos 為車身中心(球心)位置,不做抬高;keepSpeed = 沿新車頭保留目前車速。
        public void Teleport(Vector3 pos, Quaternion rot, bool keepSpeed = false)
        {
            float keep = keepSpeed ? speedF : 0f;
            transform.SetPositionAndRotation(pos, rot);
            rb.position = pos; rb.rotation = rot;
            upVec = Vector3.up;
            heading = Flatten(rot * Vector3.forward, upVec, Vector3.forward);
            Vector3 nv = keepSpeed ? heading * keep : Vector3.zero;
            rb.linearVelocity = nv; rb.angularVelocity = Vector3.zero;
            lastWritten = nv; lastGoodPos = pos;
            speedF = keep;
            grounded = false; airTime = 0f; steerSm = 0f; reverseTimer = 0f; fellFired = false;
            bumpPending = false; bumpN = Vector3.zero; pendingImpulse = Vector3.zero; kbPending = false; bumpSlipTime = 0f;
            drifting = false; driftIntent = false; hopAir = false; trickArmed = false;
            driftDir = 0; driftLevel = 0; driftCharge = 0f; rampPressTimer = 0f; sinceRampLeave = 99f;
            bounceYawTime = 0f; pendingBoostEvt = 0f;
            EndGlide(false); glideArmed = 0f; prevGlideRamp = false; trickClock = 0f; prevSteerRaw = 0f;
            CancelCharge(); chargeAir = false; chargeAirLv = 0; rideWindow = 0f; wallRiding = false; railing = false; rail = null;
            wallCooldown = railCooldown = railHopCd = 0f; evChargeUp = evChargeJump = evWallE = evRailE = 0; evWallB = evRailB = evRailHop = false;
        }

        /// 把「地面接觸點」換成球心位置:向下探測真正的地面(碰撞體優先,沒有就用 Ground 取樣器),球心放在地面上方 RideDist。
        /// 允許 pose 在地面上方約 1 m 或下方約 2.5 m(Grid / 重生點慣例是地面 + 0.3 m);都探不到就退回 pose + RideDist。
        Vector3 SeatOnGround(Vector3 groundPos, Quaternion rot)
        {
            Vector3 guess = groundPos + Vector3.up * RideDist;
            Physics.SyncTransforms();                              // 世界剛生成時碰撞體的位置要先同步,射線才查得到
            Vector3 fwd = Flatten(rot * Vector3.forward, Vector3.up, Vector3.forward);
            KartProbeResult pr = KartGroundProbe.Probe(guess, Vector3.up, fwd, 3f, Ground);
            if (pr.Found && Mathf.Abs(pr.Point.y - groundPos.y) < 4f) return pr.Point + pr.Normal * RideDist;
            return guess;
        }

        /// 重生到 Pose(p.position = 地面接觸點):傳送 + 速度歸零 + 狀態清除 + 1.5 秒不和其他車碰撞。
        public void Respawn(Pose p)
        {
            Teleport(SeatOnGround(p.position, p.rotation), p.rotation, false);
            boostTime = boostBonus = boostFloor = 0f;
            stunTime = stunImmune = 0f; slipTime = 0f; slipGripMul = slipSpeedMul = 1f; steerLock = 0f;
            ghostTime = 1.5f;                                   // 重生後 1.5 s 不參與車對車碰撞(StepKartPairs 會跳過)
            spinTime = 0f; sbActive = false; sbHeld = false;
        }

        // ------------------------------------------------------------ 車對車
        static float lastPairTime = -1f, pairClock;
        static int uidSeq;
        int uid;                                             // 每個實例唯一(冷卻表的 key,KartId 可能重複)
        static readonly Dictionary<long, float> pairLast = new Dictionary<long, float>();

        static void IgnorePhysX(RaceKart a, RaceKart b)
        {
            if (a.cols == null || b.cols == null) return;
            for (int i = 0; i < a.cols.Length; i++)
                for (int j = 0; j < b.cols.Length; j++)
                    if (a.cols[i] != null && b.cols[j] != null) Physics.IgnoreCollision(a.cols[i], b.cols[j], true);
        }

        static Vector3 HorizontalOf(Vector3 v) { v.y = 0f; return v; }

        static Vector3 HeadH(RaceKart k)
        {
            Vector3 h = HorizontalOf(k.heading);
            float m = h.magnitude;
            return m > 1e-4f ? h / m : Vector3.forward;
        }

        static bool SatAxis(Vector3 u, Vector3 d, Vector3 fa, Vector3 ra, Vector3 fb, Vector3 rbv, ref float best, ref Vector3 bestN)
        {
            float rad = HalfL * Mathf.Abs(Vector3.Dot(fa, u)) + HalfW * Mathf.Abs(Vector3.Dot(ra, u))
                      + HalfL * Mathf.Abs(Vector3.Dot(fb, u)) + HalfW * Mathf.Abs(Vector3.Dot(rbv, u));
            float dist = Vector3.Dot(d, u);
            float ov = rad - Mathf.Abs(dist);
            if (ov <= 0f) return false;
            if (ov < best) { best = ov; bestN = dist >= 0f ? u : -u; }
            return true;
        }

        /// 兩個有向矩形(1.2 x 2.0 m,水平面)的 SAT。重疊回傳 true:depth = 最小穿透深度,n = 該軸、方向由 A 指向 B。
        static bool Sat(Vector3 pa, Vector3 fa, Vector3 pb, Vector3 fb, out float depth, out Vector3 n)
        {
            depth = 0f; n = Vector3.forward;
            Vector3 d = HorizontalOf(pb - pa);
            Vector3 ra = new Vector3(fa.z, 0f, -fa.x), rbv = new Vector3(fb.z, 0f, -fb.x);
            float best = float.MaxValue; Vector3 bn = Vector3.forward;
            if (!SatAxis(fa, d, fa, ra, fb, rbv, ref best, ref bn)) return false;
            if (!SatAxis(ra, d, fa, ra, fb, rbv, ref best, ref bn)) return false;
            if (!SatAxis(fb, d, fa, ra, fb, rbv, ref best, ref bn)) return false;
            if (!SatAxis(rbv, d, fa, ra, fb, rbv, ref best, ref bn)) return false;
            depth = best; n = bn;
            return true;
        }

        /// 兩台車車身矩形目前的穿透深度(公尺,沒重疊 = 0;高度差超過 1 m = 0)。測試與除錯用。
        public static float BodyOverlap(RaceKart a, RaceKart b)
        {
            if (a == null || b == null || a.rb == null || b.rb == null) return 0f;
            Vector3 pa = a.rb.position, pb = b.rb.position;
            if (Mathf.Abs(pa.y - pb.y) > PairMaxDy) return 0f;
            float d; Vector3 n;
            return Sat(pa, HeadH(a), pb, HeadH(b), out d, out n) ? d : 0f;
        }

        /// 每個物理步開頭呼叫一次(第一台車的 FixedUpdate 觸發):所有車兩兩解碰撞,迭代 3 次(多車擠在一起時收斂)。
        static void StepKartPairs(float dt)
        {
            int n = All.Count;
            if (n < 2) return;
            pairClock += dt;
            if (pairLast.Count > 512) pairLast.Clear();
            for (int pass = 0; pass < 4; pass++)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        RaceKart a = All[i], b = All[j];
                        if (a == null || b == null || a.rb == null || b.rb == null) continue;
                        if (a.ghostTime > 0f || b.ghostTime > 0f) continue;      // 剛重生的幽靈期
                        if (a.Frozen && b.Frozen) continue;
                        ResolvePair(a, b, dt);
                    }
        }

        static void ResolvePair(RaceKart a, RaceKart b, float dt)
        {
            Vector3 pa = a.rb.position, pb = b.rb.position;
            if (Mathf.Abs(pa.y - pb.y) > PairMaxDy) return;
            Vector3 va = a.rb.linearVelocity, vb = b.rb.linearVelocity;
            Vector3 vaH = HorizontalOf(va), vbH = HorizontalOf(vb);
            Vector3 dH = HorizontalOf(pb - pa);
            float lim = 2.4f + (vaH.magnitude + vbH.magnitude) * dt;
            if (dH.sqrMagnitude > lim * lim) return;

            Vector3 fa = HeadH(a), fb = HeadH(b);
            float depthNow, depthPred; Vector3 nNow, nPred;
            bool nowHit = Sat(pa, fa, pb, fb, out depthNow, out nNow);
            bool predHit = Sat(pa + vaH * dt, fa, pb + vbH * dt, fb, out depthPred, out nPred);   // 下一步會不會重疊(防高速一步穿過)
            if (!nowHit && !predHit) return;

            Vector3 nrm = predHit ? nPred : nNow;                      // A 指向 B
            float closing = Vector3.Dot(vaH - vbH, nrm);
            bool aInv = a.invTime > 0f, bInv = b.invTime > 0f;
            float m1 = Mathf.Max(1f, a.rb.mass), m2 = Mathf.Max(1f, b.rb.mass);

            if (closing > 0.02f)
            {
                long ia = a.uid, ib = b.uid;
                long key = ia < ib ? (ia << 32) ^ (ib & 0xffffffffL) : (ib << 32) ^ (ia & 0xffffffffL);
                float last;
                bool cooled = pairLast.TryGetValue(key, out last) && pairClock - last < PairCooldown;
                float dvA, dvB;
                KartMath.KartContact(m1, m2, closing, !cooled, out dvA, out dvB);
                if (aInv && !bInv) { dvB += dvA; dvA = 0f; }            // 無敵方不減速,對方吃下全部相對速度變化
                else if (bInv && !aInv) { dvA += dvB; dvB = 0f; }
                Vector3 na = va - nrm * dvA, nb = vb + nrm * dvB;
                a.rb.linearVelocity = na; b.rb.linearVelocity = nb;
                a.lastWritten = na; b.lastWritten = nb;
                if (!cooled)
                {
                    pairLast[key] = pairClock;
                    Vector3 pt = (pa + pb) * 0.5f;
                    a.QueueKartBump(b.KartId, pt, -nrm, closing);
                    b.QueueKartBump(a.KartId, pt, nrm, closing);
                    a.bumpSlipTime = b.bumpSlipTime = 0.15f;
                    if (closing > 8f) { a.EndDrift(false); b.EndDrift(false); }
                    if (closing >= 2f)
                    {
                        if (aInv && !bInv) b.Stun(1f);                  // 無敵撞人 = 對方暈 1 s(對方無敵 / 免疫時 Stun 自己回 false)
                        else if (bInv && !aInv) a.Stun(1f);
                    }
                }
            }

            if (nowHit)
            {
                a.NoteContact(-nNow); b.NoteContact(nNow);
                float pen = Mathf.Min(depthNow - PairSlop, PairMaxPush);
                if (pen > 0f)
                {
                    float wa = (a.Frozen || (aInv && !bInv)) ? 0f : 1f / m1;
                    float wb = (b.Frozen || (bInv && !aInv)) ? 0f : 1f / m2;
                    float sum = wa + wb;
                    if (sum <= 0f) { wa = 1f / m1; wb = 1f / m2; sum = wa + wb; }
                    float dA = pen * wa / sum, dB = pen * wb / sum;
                    // 貼牆的那台推不動(PhysX 會把它彈回來,穿透就殘留):推不動的份量轉給另一台
                    if (dA > 0f) { float room = WallRoom(a, -nNow, dA); if (room < dA) { dB += dA - room; dA = room; } }
                    if (dB > 0f)
                    {
                        float room = WallRoom(b, nNow, dB);
                        if (room < dB)
                        {
                            float rem = dB - room; dB = room;
                            if (!a.Frozen) dA = WallRoom(a, -nNow, dA + rem);
                        }
                    }
                    a.rb.position = pa - nNow * dA;
                    b.rb.position = pb + nNow * dB;
                }
            }
        }

        static readonly RaycastHit[] roomHits = new RaycastHit[8];

        /// k 沿水平方向 dir 最多還能移動多遠(want 以內)而不撞到「非車」的牆 / 靜態物。車身膠囊掃掠,忽略其他車(含自己)與地面。
        static float WallRoom(RaceKart k, Vector3 dir, float want)
        {
            dir = HorizontalOf(dir);
            float m = dir.magnitude;
            if (m < 1e-4f || want <= 0f) return want;
            dir /= m;
            Vector3 h = HeadH(k);
            Vector3 c = k.rb.position + Vector3.up * (-0.30f + BodyRadius);
            float half = BodyLength * 0.5f - BodyRadius;
            int n = Physics.CapsuleCastNonAlloc(c + h * half, c - h * half, CastRadCapsule, dir, roomHits, want + 0.02f,
                                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float room = want;
            for (int i = 0; i < n; i++)
            {
                Collider col = roomHits[i].collider;
                if (col == null) continue;
                Rigidbody orb = col.attachedRigidbody;
                if (orb != null && orb.GetComponent<RaceKart>() != null) continue;
                if (roomHits[i].normal.y >= WallNormalY) continue;
                if (roomHits[i].distance < room) room = roomHits[i].distance;
            }
            return Mathf.Max(0f, room);
        }

        void QueueKartBump(int otherId, Vector3 point, Vector3 normal, float speed)
        {
            if (kbPending && kb.Speed >= speed) return;
            kbPending = true;
            kb = new KartBump { OtherKartId = otherId, Point = point, Normal = normal, Speed = speed, K = Mathf.Clamp01(speed / 12f) };
        }

        // ------------------------------------------------------------ 工具
        static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        /// 把 h 投影到 up 的垂直面並正規化;太短就用 fallback。
        static Vector3 Flatten(Vector3 h, Vector3 up, Vector3 fallback)
        {
            Vector3 p = Vector3.ProjectOnPlane(h, up);
            float m = p.magnitude;
            if (m > 1e-4f) return p / m;
            p = Vector3.ProjectOnPlane(fallback, up);
            m = p.magnitude;
            return m > 1e-4f ? p / m : Vector3.forward;
        }
    }

    /// 起跑判定結果(RaceKart.LastStartResult)。
    public enum StartBoostResult { None, Boost, Spin }
}
