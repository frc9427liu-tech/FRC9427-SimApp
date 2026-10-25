using System;
using UnityEngine;

// 賽車模式共用契約(見 docs/racing/00-contracts.md)。
// 這個檔案只放「資料型別、列舉、介面、常數表」,不放行為邏輯;所有賽車模組都可以依賴它,它不依賴任何賽車模組。
// 修改規則:只有整合者(04)可以改這個檔;要加欄位先改 00-contracts.md。
namespace FrcSim
{
    // ------------------------------------------------------------------ 輸入

    /// 一台車這一幀的操作快照。玩家(KartInputReader)與 AI(RaceAI)走同一條路徑寫進 RaceKart。
    public struct KartInput
    {
        public float Throttle;    // 0..1 油門
        public float Brake;       // 0..1 煞車;停住後持續按 = 倒車
        public float Steer;       // -1(左)..+1(右)
        public bool DriftHeld;    // 跳躍/漂移鍵按住中
        public bool DriftDown;    // 這一幀剛按下漂移鍵(edge,由 RaceKart 鎖存到下一個 FixedUpdate)
        public bool ItemDown;     // 這一幀剛按下「使用道具」(edge)
        public bool AimBack;      // 使用道具時往後丟/射(手把下 / 鍵盤 S 按住)
        public bool AimActive;    // 有「任意方向瞄準」輸入(右搖桿 / 鍵盤 IJKL);沒有時用 AimBack(往前 / 往後)
        public float AimAngle;    // 瞄準角(度,相對車頭):0 = 正前、90 = 右、180 = 正後、270 = 左;AimBack 等價於 180
        public bool SwapHeld;     // 交換兩格道具(level,不是 edge;RaceItems 自己偵測上升緣,所以不必 RaceKart 鎖存)
        public bool LookBack;     // 回頭看(攝影機用)
        public bool RespawnDown;  // 這一幀要求重生到檢查點(edge;玩家按住 1 s 才送出)
    }

    /// 輸入來源。RaceKart 在每個 Update 呼叫一次 Read(Time.deltaTime);玩家、AI、測試腳本、重播都實作它。
    /// 刻意不帶 RaceKart 參數:實作者自己持有需要的參考,這樣本檔不必依賴 RaceKart。
    public interface IKartInput
    {
        KartInput Read(float dt);
    }

    /// 什麼都不按(倒數、完賽後鎖車用)。
    public sealed class NullKartInput : IKartInput
    {
        public static readonly NullKartInput I = new NullKartInput();
        public KartInput Read(float dt) { return default; }
    }

    // ------------------------------------------------------------------ 地面

    /// 地面材質。02 的高度圖每格存一個 byte,01 依它決定速度/抓地。
    public enum Surface : byte { Road, Grass, Dirt, Sand, Water, BoostPad, Ice, Ramp, OutOfBounds }

    /// 每種地面的手感倍率(數值表,來源:01 §2 路面、02 §1)。
    public struct SurfaceProps
    {
        public float SpeedMul;   // 極速倍率
        public float Grip;       // 側向抓地 G(01:每步 vL *= exp(-G·dt))
        public float Drag;       // 額外滑行阻力 m/s²
        public bool Resets;      // 碰到就重生(海)
        public bool NoSnap;      // 不吸附貼地(跳台,一定飛起)

        static readonly SurfaceProps[] Table =
        {
            new SurfaceProps { SpeedMul = 1.00f, Grip = 12f, Drag = 0f },                 // Road
            new SurfaceProps { SpeedMul = 0.55f, Grip = 8f,  Drag = 2f },                 // Grass
            new SurfaceProps { SpeedMul = 0.75f, Grip = 9f,  Drag = 1f },                 // Dirt
            new SurfaceProps { SpeedMul = 0.45f, Grip = 7f,  Drag = 3f },                 // Sand
            new SurfaceProps { SpeedMul = 0.30f, Grip = 4f,  Drag = 6f, Resets = true },  // Water
            new SurfaceProps { SpeedMul = 1.00f, Grip = 12f, Drag = 0f },                 // BoostPad(加速由 RaceKart 觸發)
            new SurfaceProps { SpeedMul = 1.00f, Grip = 2f,  Drag = 0f },                 // Ice
            new SurfaceProps { SpeedMul = 1.00f, Grip = 12f, Drag = 0f, NoSnap = true },  // Ramp
            new SurfaceProps { SpeedMul = 0.45f, Grip = 7f,  Drag = 3f },                 // OutOfBounds(久待會被 RaceCheckpoints 重生)
        };
        /// 查表(純資料,不做計算)。
        public static SurfaceProps Of(Surface s) { int i = (int)s; return i >= 0 && i < Table.Length ? Table[i] : Table[0]; }
    }

    /// 地面查詢結果(RaceWorld.SampleGround 回傳)。
    public struct GroundHit
    {
        public float Height;     // 地面 y
        public Vector3 Normal;   // 地面法線
        public Surface Surface;
    }

    /// 地面查詢函式型別。RaceSession 在開賽時把 RaceWorld.SampleGround 指給 RaceKart.Ground,讓 01 不必直接依賴 02。
    public delegate bool GroundSampler(Vector3 p, out GroundHit hit);

    /// 掛在地面碰撞體上的材質標記(02 擺放、01 讀取)。高度圖地形不用它,改走 GroundSampler。
    public class RaceSurface : MonoBehaviour
    {
        public Surface Type = Surface.Road;
    }

    // ------------------------------------------------------------------ 賽道資料

    /// 賽道中心線取樣點(每 2 m 一個,02 產生)。
    public struct TrackSample
    {
        public Vector3 Pos;      // 中心點(已貼地)
        public Vector3 Tangent;  // 前進方向(單位向量)
        public Vector3 Right;    // 右方向(單位向量)
        public float Width;      // 全路寬 m
        public float S;          // 沿中心線弧長 m
        public float Bank;       // 側傾角 度
        public float Curvature;  // 帶號曲率 1/m(正 = 右彎)
    }

    /// 檢查點門(02 產生)。第 0 個是終點線。
    public sealed class Checkpoint
    {
        public int Index;
        public float S;              // 在中心線上的弧長
        public Vector3 Center, Forward;
        public float HalfWidth;      // 門半寬 = 路寬/2 + 4 m
        public Pose Respawn;         // 門往後 6 m、路中央、朝前進方向
        public bool IsFinish;
        public int ShortcutId = -1;  // 被捷徑跳過的門 = 捷徑編號
    }

    /// 為什麼要重生。
    public enum ResetReason { Fell, OutOfBounds, OffTrackTooLong, Stuck, Manual }

    /// 一台車的比賽進度。由 RaceCheckpoints(02)寫入,掛在 RaceKart.Progress 上;HUD/AI/道具只讀。
    public sealed class RacerProgress
    {
        public int Id;               // 註冊編號(= RaceKart.KartId)
        public int Lap;              // 已完成圈數 0..Laps
        public int NextCp;           // 下一個要過的檢查點
        public int Rank = 1;         // 名次 1..N
        public float S;              // 本圈弧長
        public float Total;          // Lap × Length + S(排名用)
        public float LastLapTime, BestLapTime, FinishTime;   // 秒;0 = 沒有
        public bool Finished, WrongWay, OnShortcut;
    }

    /// 02 的進度系統需要的車子能力(RaceKart 實作;測試用幽靈車也實作)。讓 02 不必依賴 01。
    public interface IRacerBody
    {
        int RacerId { get; }
        Vector3 Position { get; }
        Vector3 Velocity { get; }
        Vector3 Forward { get; }
        bool ThrottleHeld { get; }
        RacerProgress Progress { get; set; }
        void Respawn(Pose p);        // 傳送到 p、速度歸零、1.5 秒不和其他車碰撞
    }

    /// 世界品質(接設定:低 = 只用 LOD1、物件密度 50%、關即時陰影)。
    public enum RaceQuality { Low, High }

    /// 樹的種類(RaceArt.Tree)。
    public enum TreeKind { Pine, Round, Palm }

    // ------------------------------------------------------------------ 車

    /// 車款(RaceArt.BuildKart 造型 + KartStats 預設)。
    public enum KartStyle { Brick, Bullet, Chonk, Robot9427 }

    /// 車輛數值(預設 = 150cc 中量級,來源 01 §2)。可序列化、純資料;各車款預設由 01 的 KartPresets 提供。
    [Serializable]
    public sealed class KartStats
    {
        public float Mass = 160f;          // kg(120~200)
        public float TopSpeed = 24f;       // Vtop m/s
        public float Accel = 16f;          // A0 m/s²
        public float AccelTau = 1.0f;      // τ s
        public float CoastDrag = 3f;       // 滑行阻力 m/s²(另加 0.05·v)
        public float BrakeDecel = 28f;     // m/s²
        public float ReverseMax = 6f;      // m/s
        public float ReverseAccel = 8f;    // m/s²
        public float TurnRateLow = 120f;   // 低速轉向率 °/s
        public float TurnRateHigh = 75f;   // 極速轉向率 °/s
        public float Grip = 12f;           // 側向抓地 G
        public float DriftGrip = 2.5f;     // 漂移中 G
        public float HopSpeed = 4f;        // 跳躍初速 m/s
        public float AirGravity = 30f;     // 空中重力 m/s²
        public float BoostMul = 1.25f;     // 迷你渦輪極速倍率
        public KartStats Clone() { return (KartStats)MemberwiseClone(); }
    }

    /// 碰撞資訊(RaceKart.Bumped 事件)。
    public struct KartBump
    {
        public int OtherKartId;    // 撞到的車 KartId;-1 = 牆/靜態物
        public Vector3 Point, Normal;
        public float Speed;        // 沿法線的接近速度 m/s
        public float K;            // 0..1 強度(= Clamp01(Speed / 12)),給特效/震動直接用
    }

    // ------------------------------------------------------------------ 比賽

    /// 比賽階段。
    public enum RacePhase { Inactive, Loading, Intro, Countdown, Racing, Finished }

    /// 一場比賽的設定。選單寫進 RaceSession.Pending,Begin 時複製成 RaceSession.Config。
    [Serializable]
    public sealed class RaceConfig
    {
        public int TrackId = 0;
        public int Laps = 3;               // 1 / 3 / 5
        public int AiCount = 7;            // 0 / 3 / 7(預設 7 台 = 8 車同場,像 MK 的熱鬧感;選單可改)
        public int AiLevel = 1;            // 0..3(同 Robot2AI)
        public KartStyle Kart = KartStyle.Brick;
        public int ColorIndex = 1;         // RacePalette.Kart 的索引
        public int Seed = 9427;            // 世界生成種子
        public bool FreeRoam;              // 自由駕駛(M5):不算圈
        public bool Items = true;          // 道具開關(M4 起)
        // ---- 瑪利歐賽車式選項(docs/racing/05-mario-kart-reference.md)----
        public int CC = 150;               // 排氣量 50 / 100 / 150 / 200(速度倍率 0.55 / 0.75 / 1.0 / 1.3)
        public bool Infinite;              // 無限加速模式(200cc 起跳,極速隨時間持續增加)
        public bool Mirror;                // 鏡像模式(賽道左右鏡射,150cc)
        public bool Reverse;               // 反向行駛(逆時針/順時針互換)
        public int DriverId;               // 角色(動森風格原創角色,0..7)
        public int BodyId;                 // 車身(0..7;0..3 對應現有 KartStyle)
        public int TireId;                 // 輪胎(0 標準 / 1 越野 / 2 滑胎 / 3 金屬 ...)
        public int GliderId;               // 滑翔翼款式(0..5)
        public int ItemRule;               // 道具規則:0 正常 / 1 瘋狂 / 2 無道具 / 3 只有香蕉皮(Oil / 三連香蕉皮) / 4 只有直線彈 / 5 只有加速
        public bool GrandPrix;             // 大獎賽(盃賽,多場累計得分)
        public int Cup = -1;               // 盃賽編號(-1 = 單場)
        public bool TimeTrial;             // 計時賽(單人、有幽靈)
        public RaceConfig Clone() { return (RaceConfig)MemberwiseClone(); }
    }

    /// 道具種類(數值見 03 §1.2)。
    public enum ItemKind
    {
        None, Boost, TripleBoost, Shield, Oil, Bumper, Homing,
        // 模組 I 擴充(只在末尾追加):
        Star, Lightning, Bomb, BlueShell, Mega, Bullet, FakeBox, TripleBumper, InkBlast,
        // 瑪利歐賽車世界式擴充(只在末尾追加,數值不變):羽毛 / 金幣道具 / 火焰花 / 黃金加速 / 超級喇叭
        Feather, Coin, FireFlower, GoldBoost, SuperHorn,
        // 2026-10-24 道具設計 v2(只在末尾追加):三連香蕉皮(車尾拖 3 片)/ 幽靈(偷前車道具 + 3 s 穿透)/ 迴力鏢花(出去再回來,最多打兩次)。ItemKind.Oil 數值不變,顯示名改為「香蕉皮」
        TripleBanana, Boo, Boomerang
    }

    /// 場上障礙/投射物的簡化資訊(RaceItems.Hazards,給 AI 避障)。
    public struct RaceHazard
    {
        public Vector3 Pos, Vel;
        public float Radius;
        public ItemKind Kind;
        public int OwnerKartId;
    }

    // ------------------------------------------------------------------ 美術 / 存檔常數

    /// 共用色盤(04 §1.1)。所有模組只從這裡拿顏色;材質一律經 RaceArt.Mat 快取。
    public static class RacePalette
    {
        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        public static readonly Color SkyTop = H("#4FB8FF"), Horizon = H("#D6F4FF");
        public static readonly Color GrassLight = H("#7ED957"), GrassDark = H("#4FA83A");
        public static readonly Color Asphalt = H("#4A4E5A"), Line = H("#FFFFFF");
        public static readonly Color CurbRed = H("#FF4D4D"), CurbWhite = H("#FFFFFF");
        public static readonly Color Sand = H("#F2D48A"), Rock = H("#A9A9BC"), Water = H("#3FB6E8");
        public static readonly Color[] Leaves = { H("#2E9E4F"), H("#4CC25E"), H("#1F7A3E") };
        public static readonly Color Trunk = H("#8B5A2B");
        public static readonly Color[] Kart = { H("#FF4A3D"), H("#3FA6FF"), H("#FFC93C"), H("#3DDC84"), H("#A66BFF"), H("#FF8A2A") };
        public static readonly Color Gold = H("#FFD23F"), Silver = H("#C9D6E3"), Bronze = H("#E0915A");
        public static readonly Color Go = H("#C6FF3D");
        public static readonly Color[] Drift = { H("#4DB8FF"), H("#FF9A2E"), H("#FF4DF0") };   // 漂移 Lv1/2/3
    }

    /// Prefs 鍵名(04 §5.3)。時間一律存毫秒整數,0 = 沒紀錄。
    public static class RaceKeys
    {
        public const string Kart = "race_kart", Color = "race_color", Track = "race_track", Laps = "race_laps", AiCount = "race_ai", AiLevel = "race_aiLevel";
        public static string BestLap(int trackId) { return "race_bestLap_" + trackId; }
        public static string BestTotal(int trackId, int laps) { return "race_bestTotal_" + trackId + "_" + laps; }
    }
}
