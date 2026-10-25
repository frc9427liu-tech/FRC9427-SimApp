# 賽車模式設計書 00:統一契約(實作者必讀,優先於 01~04)

> 01~04 各自寫的介面名稱不一致,**實作一律以本文件為準**;本文件沒寫到的細節(數值、演算法、手感)再回去看 01~04。
> 共用型別已實作在 `Assets/Scripts/Racing/RaceContracts.cs`(已通過 `bash Tools/compile-check.sh`)。
> M0 骨架已完成:`RaceSession.cs`、`RaceCamera.cs` 與共用檔案的分流掛勾(見 §6)。

---

## 0. 總規則

| 項目 | 決定 |
|---|---|
| 資料夾 | **`Assets/Scripts/Racing/`**(04 寫的 `Race/` 作廢) |
| namespace | **`FrcSim`**(03 寫的 `FrcSim.Racing` 作廢) |
| 命名 | 以 04 §6 為準:`RaceKart / RaceTrack / RaceWorld / RaceSession / RaceItems / RaceAI / RaceArt / RaceFx / RaceJuice / RaceCamera / RaceHud / KartInputReader` |
| 模組代號 | **K** = 01 物理、**W** = 02 世界賽道、**I** = 03 道具 AI、**P** = 04 呈現與整合(整合者) |
| 依賴方向 | `RaceContracts` ← K ← W ← I ← P。W 不准參考 `RaceKart`(用 `IRacerBody`);K 不准參考 W/I/P(地面用 `GroundSampler` 注入);I 不准參考 P(特效/音效用事件) |
| 特效/音效/震動 | K、I **不呼叫** `SfxBus / Fx / Rumble / JuiceFX / RaceFx`,只發事件;**由 P 的 `RaceJuice` 訂閱**觸發(04 原本的 `RaceFx.ItemBox` 例外取消,改成 `RaceItems.BoxBroken` 事件) |
| 物件外觀 | K 只做物理(可 headless 測試);車的外觀與動畫(`RaceArt.BuildKart`、`KartVisual`)歸 P。I 自己的道具物件(箱子、彈、油漬、護盾球)外觀歸 I,但材質一律用 `RaceArt.Mat` |
| 材質 | 一律 `RaceArt.Mat(color, smooth)`(快取);禁止在迴圈裡 `FieldBuilder.MakeMat`。02 的 `RaceStyle` 作廢,併入 `RaceArt` |
| 顏色 | 一律 `RacePalette.*`(RaceContracts.cs) |
| Layer / Tag / ProjectSettings | 不新增,一律用元件判斷 |
| 共用 FRC 檔案 | `GameSession / MenuSystem / CameraRig / JuiceFX / Juice / SfxSynth / SimBootstrap / FieldModel` **只有整合者能改**;M0 已預留掛勾,M1~M4 實作者不需要改它們 |
| 車子階層 | `Kart_<id>`(RaceKart + Rigidbody + 碰撞體,K)→ `Visual`(P,01 的 `ModelPivot` 改名為 `Visual`,避開 CameraRig 的 FRC 進場滑入邏輯)→ `Body`、`WheelFL/FR/RL/RR`、`ExhaustL/R`、`Driver` |
| 時間 | 物理計時用 `Time.fixedDeltaTime` 累加;比賽計時 `RaceSession.RaceTime` 只在 `!MenuSystem.Blocking` 時累加 unscaled 時間 |
| 隨機 | 一律 `System.Random(seed)`,不用 `UnityEngine.Random`(可重現) |
| 測試旗標 | K `-karttest`、W **`-raceworldtest`**(原 `-racetest`)、I **`-raceaitest`**(原 `-racetest`,兩份撞名)。每個測試類別自己用 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 檢查旗標並等 1 幀再開始,**不改 SimBootstrap**;`-race <id>` 直接開賽由整合者在 M5 加進 SimBootstrap |

---

## 1. 改名對照表(01/02/03 原名 → 統一名)

| 原文件 | 原名 | 統一為 |
|---|---|---|
| 01 | `KartController` | `RaceKart` |
| 01 | `KartController.All` | `RaceKart.All` |
| 01 | `IKartInput Read(KartController)` | `IKartInput.Read(float dt)`(不帶車參數) |
| 01 | `IKartInput Input`(在車上) | `RaceKart.Source`;`RaceKart.Input` 是「本步實際使用、已鎖存的 `KartInput`」唯讀快照(04 想讀的那個) |
| 01 | `IsDrifting / IsBoosting / IsStunned / IsInvincible` | `Drifting / Boosting / Stunned / Invincible` |
| 01 | `RaceSurfaceType` | `Surface`(列舉合併,見 §2) |
| 01 | `ModelPivot` | `Visual` |
| 01 | `MiniTurbo` 事件 | `DriftLevelUp`(升級瞬間)+ `BoostStart`(放開觸發) |
| 01 | `KartAudio.cs`、`KartVisual.cs` | 移到 P(`RaceSfx`/`RaceJuice`、`KartVisual`) |
| 01 | `RaceSurface.cs`、`KartInput.cs` | 併入 `RaceContracts.cs` |
| 02 | `WorldBuilder` | `RaceWorld` |
| 02 | `Track` | `RaceTrack` |
| 02 | `CheckpointSystem`(MonoBehaviour 單例) | `RaceCheckpoints`(一般類別,`RaceSession` 持有一份) |
| 02 | `RaceStyle` | `RaceArt`(P)+ `RacePalette`(契約) |
| 02 | `RaceTypes.cs` | 併入 `RaceContracts.cs` |
| 02 | `Track.Project` | `RaceTrack.Project`(03 的 `ProjectToCenterline` 也是它) |
| 02 | `Track.CenterAt` | `RaceTrack.PointAt` |
| 02 | `WorldBuilder.Build(TrackDef, RaceQuality)` | `RaceWorld.Build(RaceConfig, Transform parent, RaceQuality)` |
| 02 | `IRacerBody.Respawn` | 保留,`RaceKart` 實作 |
| 03 | `namespace FrcSim.Racing` | `FrcSim` |
| 03 | `IKartInput { Throttle, Steer, Drift, UseItemDown, AimBack, Tick }` | `IKartInput.Read(dt)` 回傳 `KartInput` 結構;`Throttle` 不再是 −1..1,拆成 `Throttle` + `Brake` |
| 03 | `PlayerKartInput` | `KartInputReader`(K) |
| 03 | `ItemType { Boost, TripleBoost, Shield, Oil, Bumper, Homing }` | **`ItemKind`**,**內容沿用 03 的 6 種**(04 的 Mushroom/GreenShell/Banana/Star 作廢:03 有完整數值與機率表,也避開任天堂名稱) |
| 03 | `ItemSystem`(MonoBehaviour 單例) | `RaceItems`(static) |
| 03 | `ItemSystem.ItemGot / ItemUsed` | `RaceItems.Got / Used` |
| 03 | `ItemBox.cs`、`ItemFx.cs` | `RaceItemBoxes.cs`;`ItemFx` 取消(外觀併入 `Projectiles.cs`,聲光震動走事件給 `RaceJuice`) |
| 03 | `KartController.TopSpeed / SpeedMul / GripMul / Shielded / Invulnerable` | `RaceKart.MaxSpeed / SpeedMul / Slip() / RaceItems.Shielded(k) / Invincible` |
| 03 | `KartController.Boost(mul, sec)` | `RaceKart.Boost(seconds, speedBonus)`(道具加速 = `Boost(1.5f, 0.45f)`) |
| 03 | `KartController.Place / Lap / TrackS` | `kart.Progress.Rank / Lap / S`(`RacerProgress`,由 `RaceCheckpoints` 寫) |
| 03 | `Track.RespawnPose / ItemBoxRows / MaxCurvature / NextCheckpoint` | 保留在 `RaceTrack` |
| 03 | `RaceManager` | `RaceSession` |
| 03 | `RaceTest.cs`(`-racetest`) | `RaceAITest.cs`(`-raceaitest`) |
| 04 | `RaceKart.ApplyHit(spinSec)` | `RaceKart.Stun(seconds)`(護盾判定在 `RaceItems.ApplyHit`) |
| 04 | `RaceKart.Boost`(float 欄位 0..1) | `BoostRemaining`(秒)——`Boost` 是方法名 |
| 04 | `RaceTrack.SurfaceAt / HeightAt` | 移到 `RaceWorld`(高度圖在 W 的世界,不在賽道) |
| 04 | `RaceTrack.Center[] / Width[] / Checkpoints(int)` | `RaceTrack.Samples`(`TrackSample` 列表)/ `Checkpoints`(列表)/ `CheckpointCount` |
| 04 | `RaceWorld.Build(trackId, seed, root)` | `RaceWorld.Build(RaceConfig cfg, Transform parent, RaceQuality q)` |
| 04 | `RaceCamera.Compute(t, out pos, out rot)` | `RaceCamera.Compute(t, int mode, out pos, out rot)`(mode 10 追尾 / 11 俯瞰) |
| 04 | `RaceItems.Use(k)` | `RaceItems.TryUse(k, aimBack)` |
| 04 | `KartInput { Drift, UseItem }` | `DriftHeld / DriftDown / ItemDown`(01 的 edge 鎖存設計) |

---

## 2. 共用型別(全部在 `Assets/Scripts/Racing/RaceContracts.cs`,只有整合者能改)

| 型別 | 種類 | 內容 / 用途 |
|---|---|---|
| `KartInput` | struct | `Throttle, Brake, Steer; DriftHeld, DriftDown, ItemDown, AimBack, LookBack, RespawnDown` |
| `IKartInput` | interface | `KartInput Read(float dt)`;`RaceKart.Update` 每幀呼叫一次 |
| `NullKartInput` | class | 什麼都不按(`NullKartInput.I`) |
| `Surface` | enum : byte | `Road, Grass, Dirt, Sand, Water, BoostPad, Ice, Ramp, OutOfBounds`(01/02/04 三份聯集) |
| `SurfaceProps` | struct | `SpeedMul, Grip, Drag, Resets, NoSnap`;`SurfaceProps.Of(Surface)` 查表 |
| `GroundHit` | struct | `Height, Normal, Surface` |
| `GroundSampler` | delegate | `bool (Vector3 p, out GroundHit hit)`;`RaceKart.Ground = RaceWorld.SampleGround` |
| `RaceSurface` | MonoBehaviour | `Surface Type`;掛在非高度圖的地面碰撞體上(測試場、M0 平地、跳台) |
| `TrackSample` | struct | `Pos, Tangent, Right, Width, S, Bank, Curvature` |
| `Checkpoint` | class | `Index, S, Center, Forward, HalfWidth, Respawn(Pose), IsFinish, ShortcutId` |
| `ResetReason` | enum | `Fell, OutOfBounds, OffTrackTooLong, Stuck, Manual` |
| `RacerProgress` | class | `Id, Lap, NextCp, Rank, S, Total, LastLapTime, BestLapTime, FinishTime, Finished, WrongWay, OnShortcut` |
| `IRacerBody` | interface | `RacerId, Position, Velocity, Forward, ThrottleHeld, Progress{get;set;}, Respawn(Pose)` |
| `RaceQuality` | enum | `Low, High` |
| `TreeKind` | enum | `Pine, Round, Palm` |
| `KartStyle` | enum | `Brick, Bullet, Chonk, Robot9427` |
| `KartStats` | class | 01 §2 全部數值(預設 150cc 中量級),`Clone()` |
| `KartBump` | struct | `OtherKartId(-1=牆), Point, Normal, Speed, K(0..1)` |
| `RacePhase` | enum | `Inactive, Loading, Intro, Countdown, Racing, Finished` |
| `RaceConfig` | class | `TrackId, Laps, AiCount, AiLevel, Kart, ColorIndex, Seed, FreeRoam, Items`,`Clone()` |
| `ItemKind` | enum | `None, Boost, TripleBoost, Shield, Oil, Bumper, Homing` |
<!-- 2026-10-24:ItemKind 後續追加(只在末尾)見 RaceContracts.cs 與 docs/racing/08-item-design.md:...SuperHorn, TripleBanana, Boo, Boomerang;Oil 顯示名改為「香蕉皮」 -->
| `RaceHazard` | struct | `Pos, Vel, Radius, Kind, OwnerKartId` |
| `RacePalette` | static | 04 §1.1 全部色碼 + `Drift[3]` 火花色 + `Go` |
| `RaceKeys` | static | Prefs 鍵名:`Kart, Color, Track, Laps, AiCount, AiLevel, BestLap(id), BestTotal(id, laps)`(毫秒) |

各模組**內部**型別(不共用、放自己檔案):`TrackDef`、`TrackShortcut`(W);`KartPresets`、`KartMath`(K);`ItemTable`、`Projectile`(I)。

---

## 3. 類別契約(誰擁有、誰呼叫、public 簽名)

### 3.1 `RaceKart : MonoBehaviour, IRacerBody` — 擁有者 K(`RaceKart.cs`)

呼叫者:P(`RaceSession` 生成、`RaceCamera/RaceHud/KartVisual/RaceJuice` 讀狀態)、I(`RaceAI` 讀狀態寫 `SpeedMul`、`RaceItems` 呼叫命令)、W(只透過 `IRacerBody`)。

```csharp
public class RaceKart : MonoBehaviour, IRacerBody
{
    public static readonly List<RaceKart> All;          // OnEnable 加入 / OnDisable 移除
    public static GroundSampler Ground;                  // RaceSession 設;null = 只靠碰撞體 + RaceSurface
    public static RaceKart Create(Transform parent, Pose p, KartStats stats, int id, string name);   // 建 GameObject + Rigidbody + 碰撞體(01 §1),不含外觀

    public int KartId; public string DisplayName; public bool IsPlayer;
    public KartStats Stats;                              // null = new KartStats()
    public IKartInput Source;                            // null = 不動
    public KartInput Input { get; }                      // 本步實際使用的(已鎖存)輸入
    public RacerProgress Progress { get; set; }          // RaceCheckpoints 寫
    public float SpeedMul { get; set; }                  // 外部極速倍率(橡皮筋),預設 1
    public bool Frozen { get; set; }                     // 倒數/完賽鎖車

    // 唯讀狀態
    public Vector3 Position { get; }  public Vector3 Velocity { get; }  public Vector3 Forward { get; }
    public float Speed { get; }        // 沿車頭帶號 m/s
    public float SpeedKmh { get; }
    public float MaxSpeed { get; }     // 目前有效極速 = Stats.TopSpeed × SpeedMul × 路面 × 加速
    public float YawDeg { get; }  public Vector3 GroundNormal { get; }
    public bool Grounded { get; }  public float AirTime { get; }
    public bool Drifting { get; }  public int DriftDir { get; }  public int DriftLevel { get; }  public float DriftCharge { get; }
    public bool Boosting { get; }  public float BoostRemaining { get; }
    public bool Stunned { get; }  public bool Invincible { get; }
    public Surface Surface { get; }
    public int RacerId => KartId;  public bool ThrottleHeld { get; }   // IRacerBody

    // 命令
    public void Boost(float seconds, float speedBonus = 0.25f);   // 不疊加:取剩餘時間最大、加成最大
    public bool Stun(float seconds);                               // 無敵/免疫中回 false
    public void SetInvincible(float seconds);
    public void Slip(float seconds, float gripMul = 0.3f, float speedMul = 0.6f);   // 油漬
    public void ApplyImpulse(Vector3 deltaV);
    public void Teleport(Vector3 pos, Quaternion rot, bool keepSpeed = false);
    public void Respawn(Pose p);                                    // Teleport + 速度歸零 + 1.5 s 不撞車

    // 事件(static;第一個參數是發出的車)。RaceJuice / RaceItems / RaceSession 訂閱,End 時退訂
    public static event Action<RaceKart, KartBump> Bumped;
    public static event Action<RaceKart, int> DriftLevelUp;        // 1..3
    public static event Action<RaceKart, float> BoostStart;        // 秒數
    public static event Action<RaceKart> Hopped, Landed, FellOut, ItemPressed, RespawnPressed, StunStart;
}
```
- 輸入:`Update` 呼叫 `Source.Read(dt)`,edge(`DriftDown/ItemDown/RespawnDown`)OR 鎖存到下一個 `FixedUpdate`;`MenuSystem.Blocking` 或 `Frozen` 時視為全 0。
- `ItemPressed` 在 `ItemDown` 被消耗的那一步發出;`RaceItems` 訂閱後呼叫 `TryUse(k, k.Input.AimBack)`。
- `y < −20` 發 `FellOut`(由 `RaceCheckpoints`/`RaceSession` 決定重生點)。
- 改 `Time.fixedDeltaTime`、`Physics.gravity` 一律禁止(RaceSession 已存/還原,真要改先找整合者)。

### 3.2 `KartInputReader : IKartInput` — 擁有者 K(`KartInputReader.cs`)
呼叫者:P(`RaceSession` 給玩家車 `kart.Source = new KartInputReader(0)`)。
```csharp
public sealed class KartInputReader : IKartInput
{
    public KartInputReader(int player = 0);   // 0 = Pad + 鍵盤 1P;1 = Pad2(Pad2.Separate 時)+ 鍵盤 2P
    public KartInput Read(float dt);          // 01 §3 映射;不吃 Start/Back(暫停);MenuSystem.Blocking 時全 0
}
```

### 3.3 `KartPresets`、`KartMath` — 擁有者 K(內部)
`static KartStats KartPresets.For(KartStyle s, int cc = 150)`;`KartMath` 純函式(加速、轉向率、充能等級、牆面回彈、車對車衝量)。P 只呼叫 `KartPresets.For`。

### 3.4 `RaceWorld`(static)— 擁有者 W(`RaceWorld.cs`)
呼叫者:P(`RaceSession`、`RaceHud` 小地圖、`RaceCamera` 離地)、I(`RaceAI`、彈道貼地)、K 透過 `RaceKart.Ground` 間接使用。
```csharp
public static class RaceWorld
{
    public const float Size = 384f, Cell = 2f, KillY = -5f;
    public static int TrackCount { get; }
    public static string TrackName(int id);                  // 依 Loc.Lang
    public static Transform Root { get; }
    public static Bounds WorldBounds { get; }
    public static RaceTrack Track { get; }
    public static bool Ready { get; }
    public static RaceTrack Build(RaceConfig cfg, Transform parent, RaceQuality q = RaceQuality.High);  // 同 seed 結果相同;M1 可同步建
    public static void Destroy();                             // 刪全部物件/Mesh/貼圖;可重複呼叫
    public static float HeightAt(float x, float z);
    public static Vector3 NormalAt(float x, float z);
    public static Surface SurfaceAt(float x, float z);
    public static bool SampleGround(Vector3 p, out GroundHit hit);   // 符合 GroundSampler;超出地圖回 false
    public static bool InBounds(Vector3 p);
}
```
地面碰撞體:地形 `MeshCollider`(LOD0)一定要有(K 的探地用 raycast/SphereCast);`Surface` 由 `SampleGround` 提供。

### 3.5 `RaceTrack`(sealed class)— 擁有者 W(`RaceTrack.cs`)
呼叫者:P(起跑格、HUD 小地圖、逆向)、I(AI 追線、道具箱、追蹤彈)。
```csharp
public sealed class RaceTrack
{
    public int Id; public string Name; public int Laps; public bool Closed; public float Length;
    public IReadOnlyList<TrackSample> Samples { get; }       // 每 2 m
    public IReadOnlyList<Checkpoint> Checkpoints { get; }    // [0] = 終點線
    public int CheckpointCount { get; }
    public IReadOnlyList<Vector3> ItemBoxRows { get; }       // 每 ~150 m 一排的中心點(I 擺 4 個)
    public Rect MapBounds { get; }                           // x/z 範圍(小地圖)
    public static RaceTrack FromDef(TrackDef def);
    public float Wrap(float s);
    public TrackSample SampleAt(float s);                    // 內插
    public Vector3 PointAt(float s);  public Vector3 TangentAt(float s);  public float HalfWidthAt(float s);
    public float Project(Vector3 p, float hintS, out float lateral);   // hintS < 0 = 全圈搜尋;否則 ±60 m 局部;lateral 右正
    public Vector3 AheadPoint(float s, float distance);
    public float CurvatureAt(float s);                       // 帶號(正 = 右彎)
    public float MaxCurvature(float s0, float s1);           // 區間內 |κ| 最大者(保留正負號)
    public int NextCheckpoint(float s);
    public Pose Grid(int slot);                              // 0..7,2 排 × 4 格;0 = 桿位
    public Pose RespawnPose(int checkpoint);
    public float Progress(Vector3 p);                        // 0..1(粗估,HUD 用;名次用 RacerProgress)
    public bool IsWrongWay(Vector3 p, Vector3 fwd, float hintS = -1f);
    public Vector2 ToMap(Vector3 p);                         // 0..1(MapBounds 正規化)
}
```

### 3.6 `RaceCheckpoints`(sealed class)— 擁有者 W(`RaceCheckpoints.cs`)
呼叫者:只有 P 的 `RaceSession`(建立、註冊、每個 FixedUpdate `Step`、訂閱事件)。HUD/AI/道具**只讀 `kart.Progress`**,不碰這個類別。
```csharp
public sealed class RaceCheckpoints
{
    public RaceCheckpoints(RaceTrack track, int laps, bool freeRoam);
    public void Register(IRacerBody body, bool isPlayer);    // 建 RacerProgress(Id = body.RacerId)並設給 body.Progress
    public void Unregister(int id);
    public RacerProgress Get(int id);
    public IReadOnlyList<RacerProgress> Standings { get; }   // 依名次;每 0.1 s 重排、不配置記憶體
    public Checkpoint NextCheckpoint(int id);  public Checkpoint LastCheckpoint(int id);
    public Checkpoint NearestCheckpoint(Vector3 p);
    public Pose GetRespawnPose(int id);
    public void RequestReset(int id, ResetReason why);       // 會呼叫 body.Respawn
    public void StartRace(float raceTime);
    public void Step(float dt, float raceTime);
    public event Action<int, int> LapCompleted;              // (id, 已完成圈數)
    public event Action<int> Finished;
    public event Action<int, ResetReason> Reset;
}
```
**名次與圈數只在這裡算**(04 §6 衝突 1);`RaceSession.Lap(k)/Place(k)` 只是讀 `k.Progress`。

### 3.7 `RaceAI : MonoBehaviour, IKartInput` — 擁有者 I(`RaceAI.cs`、`Rubberband.cs`)
呼叫者:P(`RaceSession` 生成並設 `kart.Source = ai`)。
```csharp
public class RaceAI : MonoBehaviour, IKartInput
{
    public int Level = 1;               // 0..3(03 §2.5 陣列)
    public bool Rookie;                 // = GameSession.Rookie 那組弱參數
    public float LanePersonality;       // -1..1
    public int Seed;
    public RaceKart Kart;               // 自己開的車
    public RaceTrack Track;
    public RaceKart Rival;              // 橡皮筋參考(玩家);null = 不用橡皮筋
    public float RubberbandMul { get; }
    public string DebugState { get; }   // "Follow/Overtake/Avoid/Recover"
    public KartInput Read(float dt);    // 同時把 Kart.SpeedMul 設成 RubberbandMul
}
public static class Rubberband { public static float Mul(float deltaS, int level, bool lastLapLast25, bool visible); }
```
卡住重置:AI 不自己傳送,呼叫 `kart.RespawnPressed` 等效的 `KartInput.RespawnDown = true`(由 `RaceSession` 轉給 `RaceCheckpoints.RequestReset`)。

### 3.8 `RaceItems`(static)— 擁有者 I(`RaceItems.cs`、`ItemTable.cs`、`RaceItemBoxes.cs`、`Projectiles.cs`)
呼叫者:P(`RaceSession` Init/Tick/Shutdown;`RaceHud` 讀;`RaceJuice` 訂閱事件)、I(`RaceAI` 讀/用)。
```csharp
public static class RaceItems
{
    public static void Init(RaceTrack track, RaceConfig cfg, Transform parent);   // 生成道具箱,訂閱 RaceKart.ItemPressed
    public static void Shutdown();                                                  // 刪物件、退訂、清狀態
    public static void Tick(float dt);                                              // RaceSession 每個 FixedUpdate 呼叫
    public static ItemKind Held(RaceKart k);  public static int Charges(RaceKart k);
    public static bool Rolling(RaceKart k);   public static float RollT(RaceKart k);  // 抽獎中 / 0..1 進度(HUD 輪盤)
    public static bool Shielded(RaceKart k);  public static bool Targeted(RaceKart k); // 被追蹤彈鎖定(HUD 警告)
    public static void OnBoxHit(RaceKart k);
    public static bool TryUse(RaceKart k, bool aimBack);
    public static void ApplyHit(RaceKart victim, float stunSec, RaceKart by, ItemKind kind);   // 檢查護盾/無敵後才 victim.Stun
    public static IReadOnlyList<RaceHazard> Hazards { get; }
    public static ItemKind Roll(int place, int count, System.Random rng);          // 純函式
    public static event Action<RaceKart> RollStart;
    public static event Action<RaceKart, ItemKind> Got, Used, Blocked;             // Blocked = 護盾擋下
    public static event Action<RaceKart, RaceKart, ItemKind> Hit;                  // (victim, by 可為 null, kind)
    public static event Action<Vector3> BoxBroken;                                 // 取代 RaceFx.ItemBox
}
```
抽獎用的名次 = `k.Progress.Rank`,車數 = `RaceKart.All.Count`。

### 3.9 `RaceSession`(static)— 擁有者 P(`RaceSession.cs`,M0 已建)
呼叫者:`GameSession`(Begin/Restart/End)、`MenuSystem`(Pending、TrackCount/TrackName、ResetPlayer、FormatTime)、`Juice.Tick`(JuiceTick)、`RaceHud/RaceCamera/RaceAI`(讀)。
```csharp
public static class RaceSession
{
    // M0 已有
    public static bool Active;
    public static RaceConfig Pending, Config;
    public static RacePhase Phase;
    public static float RaceTime, CountdownLeft;
    public static readonly List<float> LapTimes;           // 玩家各圈秒數
    public static Transform Root, PlayerBody;
    public static int TrackCount { get; }  public static string TrackName(int id);   // M1 起轉呼叫 RaceWorld
    public static void Begin(CameraRig rig);  public static void Restart();  public static void End(CameraRig rig);
    public static void ResetPlayer();
    public static void JuiceTick(float dt);                 // M2 起在這裡呼叫 RaceJuice.Tick(dt)
    public static string FormatTime(float seconds);         // "1:23.456"
    // M1 起新增
    public static RaceKart Player;  public static readonly List<RaceKart> Karts;
    public static RaceTrack Track;  public static RaceCheckpoints Progress;
    public static int Lap(RaceKart k);  public static int Place(RaceKart k);
    public static event Action<RacePhase> PhaseChanged;
    public static event Action<RaceKart, int> LapDone;      // (車, 已完成圈數)
    public static event Action<RaceKart> KartFinished;
}
```
Begin 順序(M1 起):`Env.Save → HideFrc → RaceArt.ApplyOutdoor → RaceWorld.Build → RaceKart.Ground = RaceWorld.SampleGround → 生成車(RaceKart.Create + RaceArt.BuildKart 掛成 Visual + KartVisual)→ RaceCheckpoints 註冊 → (M3) RaceAI → (M4) RaceItems.Init → RaceFx.Init / RaceJuice.Init → RaceHud → rig.Target/Mode=10 → Intro → Countdown`。End 反向,最後 `Env.Restore`、`ShowFrc`、`RaceKart.Ground = null`、退訂所有 static 事件。

### 3.10 `RaceCamera`(static)— 擁有者 P(`RaceCamera.cs`,M0 已建)
呼叫者:`CameraRig.LateUpdate`(Mode 10/11)、`RaceSession`(Reset)。
```csharp
public static class RaceCamera
{
    public const float BaseFov = 62f;
    public static float MaxSpeedHint;                       // RaceSession 每幀設 Player.MaxSpeed
    public static void Reset();
    public static void Compute(Transform target, int mode, out Vector3 pos, out Quaternion rot);   // 回傳最終姿態(已平滑);會設 JuiceFX.BaseFovOverride
}
```
M2 起要讀漂移/加速狀態時用 `target.GetComponent<RaceKart>()`(快取)。起跑運鏡/終點環繞也在這裡依 `RaceSession.Phase` 切換。

### 3.11 其他 P 類別
| 類別 | 檔案 | 簽名 | 呼叫者 |
|---|---|---|---|
| `RaceArt` | `RaceArt.cs` | `static Material Mat(Color c, float smooth = 0.1f)`;`static Material Glow(Color c, float intensity)`;`static GameObject BuildKart(KartStyle s, Color c)`;`static Mesh Flat(Mesh m)`;`static Mesh Cone(int sides)`;`static Mesh IcoSphere(int sub)`;`static GameObject Tree(TreeKind k, System.Random r)`;`static GameObject Cloud(System.Random r)`;`static void ApplyOutdoor(Camera cam)`;`static void ClearCache()` | W(Mat/Flat/Cone/IcoSphere/Tree/Cloud)、I(Mat/Glow)、P |
| `KartVisual` | `KartVisual.cs` | `MonoBehaviour`,`public RaceKart Kart;` 讀狀態轉輪子/傾斜/擠壓/漂移偏角 | RaceSession 生成時 AddComponent |
| `RaceFx` | `RaceFx.cs` | `static void Init(Transform p)`;`Shutdown()`;`DriftSpark(Vector3 p, Vector3 v, int lv)`;`Flame(Transform ex, float k)`;`Impact(Vector3 p, float k)`;`ItemBox(Vector3 p)`;`Landing(Vector3 p)` | 只有 RaceJuice / KartVisual |
| `RaceSfx` | `RaceSfx.cs` | `static void Init()`:合成 04 §4 的音色並 `SfxSynth.Register(name, clip)` | RaceJuice.Init |
| `RaceJuice` | `RaceJuice.cs` | `static void Init()`(訂閱 RaceKart/RaceItems/RaceSession 事件);`Shutdown()`;`Tick(float dt)`(引擎/打滑 loop、配樂) | RaceSession |
| `RaceHud` | `RaceHud.cs` | `MonoBehaviour`(OnGUI,04 §3 版面) | RaceSession 生成 |

**ApplyOutdoor 不需要 RestoreIndoor**:還原統一由 `RaceSession` 的 `Env.Restore()` 做(已存相機、霧、環境光、太陽、陰影距離、`fixedDeltaTime`、重力)。

---

## 4. 呼叫關係總覽

```
GameSession.Begin/Rematch/End ──(Racing)──▶ RaceSession.Begin/Restart/End
MenuSystem.ShowRacing ──▶ RaceSession.Pending / TrackName / TrackCount ;「開始」→ GameSession.Racing = true → GameSession.Begin
CameraRig.LateUpdate(Mode 10/11) ──▶ RaceCamera.Compute ──▶ JuiceFX.BaseFovOverride
Juice.Tick(Racing) ──▶ RaceSession.JuiceTick ──▶ RaceJuice.Tick
RaceSession ──▶ RaceWorld.Build / RaceKart.Create / RaceArt.BuildKart / RaceCheckpoints.Step / RaceItems.Tick / RaceHud
RaceKart.Update ──▶ Source.Read(dt)   (KartInputReader 或 RaceAI)
RaceKart.FixedUpdate ──▶ RaceKart.Ground(= RaceWorld.SampleGround)
RaceKart 事件 ──▶ RaceJuice(聲光震動)、RaceItems(ItemPressed)、RaceSession(FellOut/RespawnPressed → RaceCheckpoints.RequestReset)
RaceItems 事件 ──▶ RaceJuice、RaceHud
RaceCheckpoints 事件 ──▶ RaceSession(LapDone/KartFinished、存最佳圈)──▶ RaceHud / RaceJuice
```
FixedUpdate 順序:`RaceKart`(預設 0)→ `RaceSession` 的 runner 用 `[DefaultExecutionOrder(100)]` 呼叫 `RaceCheckpoints.Step`、`RaceItems.Tick`。

---

## 5. 里程碑檔案清單與允許依賴

「可依賴」欄只列賽車檔案;所有檔案都可依賴 `RaceContracts.cs` 與現有 FRC 工具(`FieldBuilder.MakeMat`(只限 RaceArt 內)、`Look`、`Pad/Pad2`、`Prefs`、`Loc`、`MenuSystem.Blocking`、`JuiceSettings`、`SuperSample.IsTestRun`、`BlobShadow`)。`SfxBus/SfxSynth/Rumble/Fx/JuiceFX` **只有 P 能用**。

### M0 骨架(已完成,整合者)
| 檔案 | 模組 | 可依賴 |
|---|---|---|
| `Racing/RaceContracts.cs` | 契約 | (無) |
| `Racing/RaceSession.cs`(替身車 `RaceStubCar`、平地) | P | RaceCamera |
| `Racing/RaceCamera.cs`(簡單追尾) | P | — |
| 共用檔分流:`GameSession`、`MenuSystem`、`CameraRig`、`JuiceFX`、`Juice`、`SfxSynth`(`Register`)、`FieldModel` | 整合者 | RaceSession、RaceCamera |

### M1 最小可玩(一台車、一條賽道、一圈計時)
| 檔案 | 模組 | 可依賴 | 備註 |
|---|---|---|---|
| `RaceArt.cs` | P | — | **最先合併**(W 要用 `Mat/Flat`);M1 只要 Mat、Glow、Flat、BuildKart(Brick)、ApplyOutdoor |
| `KartPresets.cs`、`KartMath.cs` | K | — | |
| `KartInputReader.cs` | K | — | |
| `KartGroundProbe.cs` | K | — | |
| `RaceKart.cs` | K | KartPresets、KartMath、KartGroundProbe | 油門/轉向/煞車/貼地/撞牆;漂移欄位先回 0 |
| `KartTestTrack.cs`、`KartPhysicsTest.cs` | K | 上面全部 K 檔 | `-karttest`,01 §6 測試 1、2、3、5、6、10、11 |
| `TrackDef.cs`、`DefaultTracks.cs` | W | — | |
| `RaceTrack.cs` | W | TrackDef | |
| `TrackMeshBuilder.cs` | W | RaceTrack、RaceArt | |
| `RaceWorld.cs`(M1 可先平坦地形 + 路面帶) | W | RaceTrack、TrackDef、DefaultTracks、TrackMeshBuilder、RaceArt | |
| `RaceCheckpoints.cs` | W | RaceTrack | 只用 `IRacerBody` |
| `RaceWorldTest.cs` | W | W 全部 | `-raceworldtest`,02 §6 測試 1、3、4、6 |
| `KartVisual.cs` | P | RaceKart | 只轉輪子 |
| `RaceSfx.cs` | P | — | 引擎 loop、count_beep/go、lap |
| `RaceJuice.cs` | P | RaceKart、RaceSession、RaceSfx | 引擎聲、倒數音 |
| `RaceHud.cs` | P | RaceSession、RaceKart、RaceTrack | 倒數、圈數/計時、成績卡 |
| `RaceSession.cs`(改) | P | 以上全部 | 拿掉 RaceStubCar;最佳圈/總時間存 `RaceKeys`(毫秒) |

### M2 手感
| 檔案 | 模組 | 可依賴 |
|---|---|---|
| `RaceKart.cs`、`KartMath.cs`(改:hop/漂移/迷你渦輪/boost/空中/跳台) | K | 同 M1 |
| `KartPhysicsTest.cs`(加測試 4、7、8、12) | K | 同 M1 |
| `TerrainChunks.cs`、`PropScatter.cs`、`RaceWorldLod.cs`;`RaceWorld.cs`(高度圖、刻路) | W | RaceTrack、RaceArt |
| `RaceFx.cs` | P | — |
| `RaceArt.cs`(改:Tree、Cloud、天穹、IcoSphere、Cone) | P | — |
| `RaceCamera.cs`(改:外擺、加速 FOV、回望) | P | RaceKart、RaceSession |
| `KartVisual.cs`、`RaceJuice.cs`(改:傾斜擠壓、火花、噴焰、震動) | P | RaceKart、RaceFx、RaceSfx |

### M3 對手
| 檔案 | 模組 | 可依賴 |
|---|---|---|
| `Rubberband.cs` | I | — |
| `RaceAI.cs` | I | RaceKart、RaceTrack、RaceWorld、Rubberband |
| `RaceAITest.cs` | I | I 全部、RaceKart、RaceTrack、RaceWorld、RaceCheckpoints | `-raceaitest`,不含道具 |
| `RaceKart.cs`(改:車對車,01 §6 測試 9、13) | K | 同 M1 |
| `RaceCheckpoints.cs`(改:8 車名次,02 §6 測試 7、9) | W | 同 M1 |
| `RaceSession.cs`、`RaceHud.cs`(名次、小地圖)、`RaceJuice.cs`(撞擊) | P | 加上 RaceAI |
| `MenuSystem.ShowRacing` 加 AI 數量/難度 | 整合者 | RaceSession |

### M4 道具
| 檔案 | 模組 | 可依賴 |
|---|---|---|
| `ItemTable.cs` | I | — |
| `Projectiles.cs` | I | RaceKart、RaceTrack、RaceWorld、RaceArt |
| `RaceItemBoxes.cs` | I | RaceKart、RaceTrack、RaceArt |
| `RaceItems.cs` | I | ItemTable、Projectiles、RaceItemBoxes、RaceKart |
| `RaceAI.cs`(改:用道具、避障) | I | 加上 RaceItems |
| `RaceHud.cs`(道具欄、輪盤、鎖定警告)、`RaceJuice.cs`、`RaceSfx.cs`(道具音) | P | 加上 RaceItems |

### M5 開放世界與打磨
W `Collectibles.cs`(金幣)、`RaceWorld` FreeRoam;P 起跑運鏡/終點環繞、車款選單 `ShowKartPick`、配樂;整合者 `SimBootstrap` 加 `-race <id>`、選單記住設定(`RaceKeys`)。

**禁止循環**:同一里程碑內若 A 檔「可依賴」沒有列 B 檔,就不准用 B 的任何成員;要新增依賴先改本文件。

---

## 6. M0 已預留的掛勾(實作者不要再改這些共用檔)

| 位置 | 掛勾 |
|---|---|
| `GameSession.Racing` | 旗標;`Begin` 在 `if (Active) return;` 後分流,`Rematch` 最前面分流,`End` 在 `Active=false` 後分流 |
| `MenuSystem.ShowModes` | 「賽車 ▸」→ `ShowRacing()`(賽道輪替、圈數 1/3/5、開始、返回);暫停選單「重置位置」→ `RaceSession.ResetPlayer()`;「再來一場」→ `GameSession.Rematch()`(已分流) |
| `CameraRig` | Mode 10/11 呼叫 `RaceCamera.Compute(Target, Mode, …)` 直接套用;C 鍵在賽車中只切 10↔11;賽車中不跑 FRC 的 `ModelPivot` 進場滑入 |
| `JuiceFX` | `BaseFovOverride`(NaN = 不覆蓋);Mode 10 也有畫面震動 |
| `Juice.Tick` | `if (GameSession.Racing) { Rumble.Tick(dt); RaceSession.JuiceTick(dt); return; }` |
| `SfxSynth.Register(name, clip)` | RaceSfx 用它註冊合成音,之後 `SfxBus.Play/SetLoop` 用同名播放 |
| `FieldModel` | 場地 GLB 晚載入時不在賽車中補擺 FRC 球 |
| `RaceSession.Env` | 存/還原:相機 clearFlags/背景/遠裁切/FOV、霧、環境光、太陽方向/顏色/亮度、陰影距離、`fixedDeltaTime`、重力;隱藏/還原 `Field`、`Arena`、`FieldModel`、`HubGlows` 根物件(每 0.5 s 再檢查一次晚載入的) |
