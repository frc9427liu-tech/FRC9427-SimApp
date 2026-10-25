# 賽車模式設計書 01:賽車物理與手感

> 範圍:單台車的運動、操控、碰撞與對外介面。賽道/開放世界生成、道具、AI 路線、HUD、選單入口、攝影機在其他章節。
> 慣例沿用現有程式:namespace `FrcSim`、全部程式碼生成、物理 60 Hz(`SimBootstrap`:`fixedDeltaTime = 1/60`、`maximumDeltaTime = 0.05`)、單位公尺/秒、新檔案一律放 `Assets/Scripts/Racing/`。

## 1. 架構選擇:Rigidbody 承載碰撞 + 自製街機運動學

**結論:用動態 Rigidbody,但不用 WheelCollider、不用 PhysX 重力/摩擦;每個 FixedUpdate 讀回實際速度 → 自己算 → 寫回 `linearVelocity`。** 這和 `SwerveDrive` 現在的做法一致(先讀 `rb.linearVelocity` 才保留被牆擋住的結果,再朝目標速度靠近)。

| 方案 | 優點 | 缺點 | 判斷 |
|---|---|---|---|
| WheelCollider 真實懸吊 | 擬真 | 調參地獄、低速抖、漂移不像瑪利歐、各種坡面接縫會彈 | 不用 |
| 純運動學(isKinematic + 自己 sweep) | 完全可控、可重播 | 牆、車對車、動態物件都要自己寫 sweep/解穿透 | 太花工 |
| **Rigidbody + 自製速度控制** | PhysX 處理穿透與連續碰撞,手感全在我們的公式 | 必須小心「寫回速度」會蓋掉碰撞反應 | **採用** |

Rigidbody 設定:`useGravity=false`、`freezeRotation=true`(旋轉完全由我們用 `MoveRotation` 設)、`interpolation=Interpolate`、`collisionDetectionMode=ContinuousDynamic`、`linearDamping=angularDamping=0`、mass 由 `KartStats.Mass`(120~200 kg)。
碰撞體:**SphereCollider(r=0.45 m)貼地**(對旋轉不敏感,越過地形接縫不卡)+ **水平 CapsuleCollider(r=0.40, 長 1.5 m,底部比球高 0.15 m)當車身**撞牆/撞車。兩者共用 PhysicsMaterial(摩擦 0、彈性 0、`Minimum` 結合),摩擦與回彈全由程式決定。車體尺寸約 1.5 × 1.0 × 0.6 m,外觀掛在子物件 `ModelPivot`(與 `CameraRig` 既有慣例相同)。

## 2. 每步流程與演算法(FixedUpdate,dt = 1/60)

```
1 讀輸入快照(Update 鎖存的 KartInput)      5 側向抓地/漂移(橫向速度衰減)
2 地面探測 → grounded, n, surface           6 坡面重力 / 空中重力
3 狀態計時(boost/stun/invincible/hop)      7 寫回 linearVelocity、MoveRotation(yaw × 對齊法線)
4 縱向加減速、轉向 yaw                       8 發事件(落地、迷你渦輪、碰撞)
```
速度一律分解成 `vF`(沿車頭,貼地平面)、`vL`(側向)、`vN`(法線),各自處理後再合成。以下數值為 150cc 中量級預設,`KartStats` 可調。

**加速**:`a = min(A0, (Vtop·mul − vF)/τ)`,A0 = 16 m/s²、τ = 1.0 s、Vtop = 24 m/s(≈86 km/h;50cc/100cc/150cc = 16/20/24)。理論 0→90% 約 2.4 s。`mul` = 路面倍率 × 道具倍率(取最大,不相加)。滑行阻力 3 m/s² + 0.05·v。
**煞車/倒車**:煞車 28 m/s²(24→0 約 0.86 s、10 m);停住後持續按 0.25 s 才進倒車,倒車上限 6 m/s、加速 8 m/s²。
**轉向**:輸入先平滑(每秒追 10 倍)再過曲線 `s = sign(x)·|x|^1.4`。`yawRate = s · lerp(120°/s, 75°/s, vF/Vtop) · clamp01(|vF|/3)`;倒車方向反轉;最高速轉彎半徑約 18 m。
**抓地**:每步 `vL *= exp(−G·dt)`,一般 G = 12;冰面 2;草地 8。
**跳躍 hop + 漂移**:按下漂移鍵 → 垂直速度 +4 m/s(空中約 0.27 s)。落地時(或按下後 0.15 s 內)若 `|steer| > 0.35` 且 `vF > 9 m/s` 就鎖定漂移方向 `dir = ±1`。漂移中:
- `yawRate = dir · (70 + 35·steer·dir) °/s`(往內拉 105、往外推 35,永遠不會轉反方向);
- 側向抓地降為 G = 2.5,外觀 `ModelPivot` 額外偏 20~25°(只影響畫面);
- 放開漂移鍵、速度 < 8 m/s、踩煞車、或撞擊法向速度 > 8 m/s → 結束漂移。

**迷你渦輪**:漂移中累積 `charge += (1 + 0.7·steer·dir)·dt`(0.3~1.7/s)。門檻 1.0 / 2.0 / 3.2 → Lv1 藍、Lv2 橘、Lv3 紫(火花用 `Look.GlowMaterial`)。中立方向盤分別約 1.0/2.0/3.2 s,一直往內壓約 0.6/1.2/1.9 s。放開時觸發 `Boost(0.5 / 1.0 / 1.6 s, +25%)`。
**加速狀態(boost)**:期間 Vtop × 1.25、A0 改 35 m/s²,並保證 `vF ≥ 進入時速度 + 3 m/s`;多個來源取「剩餘時間最大值、加成最大值」,不疊加,避免失控。
**碰撞(牆/靜態)**:在 `OnCollisionEnter/Stay` 取接觸法線 n(只取 `n.y < 0.5` 的側面),入射角 θ(速度與牆面夾角):
- θ < 25°:擦牆,`v = v_t · 0.92`(保留速度、順著牆滑);
- θ ≥ 25°:回彈,`v = v_t · 0.6 − n·vn·0.35`,鎖轉向 0.15 s,`Rumble.Driver(0.5, 0.3, 0.15)`。
同一面牆 0.1 s 冷卻,避免 Stay 每步重複扣速。
**碰撞(車對車)**:沿法線相對速度 vrel,`J = (1+e)·vrel / (1/m1 + 1/m2)`,e = 0.6,且分離速度至少 3 m/s(保證會彈開、不黏在一起)。重的車被推得少。無敵方撞人 → 對方 `Stun(1.0)`、自己不減速。
**坡面**:4 支角落射線 + 中心 SphereCast(長 0.6 m)平均出法線 n;`up` 以每秒 12 倍 Slerp 對齊 n。坡度加速度 `ProjectOnPlane(−g·up世界, n) · 0.7`(上坡減速但不會停死)。> 40° 視為牆。
**離地與貼地**:上一步著地、下方 0.6 m 內有地、法線差 < 30°、且 `vN < 2 m/s` → **吸附貼地**(高速過坡頂不會亂飛);否則轉為空中。標記 `RaceSurface.Ramp` 的面不吸附,一定飛起。空中重力 30 m/s²(約 3g,街機感比 9.8 俐落)、終端速度 40 m/s、空中轉向 35%、不能加速。落地垂直速度 > 12 m/s 扣 10% 速度;離開跳台前 0.2 s 內按漂移鍵 = 特技,落地給 `Boost(0.5)`。y < −20 → 觸發 `FellOut` 由賽事模組重生。
**路面**:`RaceSurface` 元件放在地面碰撞體上:Road(1.0/G12)、Grass(速度 0.55)、Sand(0.45)、Ice(G2)、BoostPad(觸發 Boost 1.0 s)、Ramp。
**暈眩**:`Stun(t)` 期間忽略輸入、`vF` 以 20 m/s² 減到 30%、外觀轉 720°;結束後 0.5 s 免疫再暈。無敵時呼叫 `Stun` 無效。

## 3. 操控映射(相容 Pad.cs)

輸入只在 **Update** 讀(`Pad.Down` 以 `Time.frameCount` 判定,在 FixedUpdate 讀會漏按或重複),edge 用「OR 鎖存」到下一個 FixedUpdate 消耗。`MenuSystem.Blocking` 為 true 時輸出全 0。

| 動作 | 手把(`Pad`) | 鍵盤 1P | 鍵盤 2P(分割畫面時) |
|---|---|---|---|
| 油門 | `Pad.RT` 或 `Pad.Held(Pad.A)` | W / ↑ | ↑ |
| 煞車/倒車 | `Pad.LT` 或 `Pad.Held(Pad.B)` | S / ↓ | ↓ |
| 轉向 | `Pad.LX`(再加 0.12 圓形死區)、D-pad 左右 | A D / ← → | ← → |
| 跳躍/漂移(按住) | `Pad.Held(Pad.RB)` | Space 或 LeftShift | RightShift |
| 使用道具 | `Pad.Down(Pad.LB)` 或 `Pad.Down(Pad.X)` | E | RightCtrl |
| 回頭看 | `Pad.Held(Pad.Y)` | Q | 無 |
| 重生到檢查點 | `Pad.Held(Pad.DDown)` 按住 1 s(Start/Back 既有是暫停,不搶) | R(沿用既有「R=重置」) | 無 |
| 暫停 | `Pad.AnyDown(Pad.Start)`(既有) | Esc(既有) | 無 |

2P 時第二位玩家用 `Pad2`(`Pad2.Separate` 為 true 才啟用),不跟 1P 共用鍵盤列。手把與鍵盤值取 max 合併,不相加(`RobotInput` 註解提過疊加會讓靈敏度變兩倍)。撞牆/迷你渦輪用既有 `Rumble.Driver`。

## 4. 對外介面契約

```csharp
namespace FrcSim {
public struct KartInput {
    public float Throttle;   // 0..1
    public float Brake;      // 0..1(停住後持續 = 倒車)
    public float Steer;      // -1(左)..1(右)
    public bool DriftHeld;   // 跳躍/漂移鍵按住中
    public bool DriftDown, ItemDown, RespawnDown;   // 這一幀剛按下(由 KartController 鎖存)
    public bool LookBack;
}
public interface IKartInput { KartInput Read(KartController kart); }   // 每個 Update 呼叫一次;玩家/AI/重播都實作它

public class KartController : MonoBehaviour {
    public static readonly List<KartController> All;
    public int KartId; public string DisplayName;
    public KartStats Stats;                 // 可在 Awake 前指定
    public IKartInput Input;                // null = 不動(倒數時用)
    // --- 唯讀狀態 ---
    public Vector3 Position { get; }  public Vector3 Velocity { get; }
    public float Speed { get; }             // 沿車頭的帶號速度 m/s(倒車為負)
    public float SpeedKmh { get; }
    public Vector3 Forward { get; }         // 貼地平面上的車頭方向(= transform.forward)
    public float YawDeg { get; }  public Vector3 GroundNormal { get; }
    public bool Grounded { get; }  public float AirTime { get; }
    public bool IsDrifting { get; }  public int DriftDir { get; }   // -1/0/1
    public int DriftLevel { get; }  public float DriftCharge { get; } // 0..3
    public bool IsBoosting { get; }  public float BoostRemaining { get; }
    public bool IsStunned { get; }  public bool IsInvincible { get; }
    public bool Frozen { get; set; }        // 倒數計時鎖車
    public RaceSurfaceType Surface { get; }
    // --- 給其他模組的命令 ---
    public void Boost(float seconds, float speedBonus = 0.25f);
    public bool Stun(float seconds);        // 無敵/免疫中回 false
    public void SetInvincible(float seconds);
    public void ApplyImpulse(Vector3 deltaV);
    public void Teleport(Vector3 pos, Quaternion rot, bool keepSpeed = false);
    // --- 事件 ---
    public event Action<KartController, KartBump> Bumped;    // 牆或車;KartBump{ Other, Point, Normal, Speed }
    public event Action<KartController, int> MiniTurbo;      // 等級 1~3
    public event Action<KartController> Landed, FellOut, ItemPressed;
}
}
```
注意:現有 `CameraRig` 的追車模式把機器人 `transform.right` 當前方,`Juice.OnRobotBump` 收 `SwerveDrive`;賽車用 `Forward`/`Bumped` 事件,由攝影機與特效章節各自加新模式,不要假裝自己是 `SwerveDrive`。賽車模式時 `GameSession.Drive` 會是 null,用到它的舊程式要先判 null。

## 5. 檔案清單(全部在 `Assets/Scripts/Racing/`)

| 檔案 | 責任 |
|---|---|
| `KartInput.cs` | `KartInput` 結構、`IKartInput` 介面、`NullKartInput` |
| `PlayerKartInput.cs` | 第 3 節的手把+鍵盤映射(參數 player 0/1),死區、曲線、`MenuSystem.Blocking` 處理 |
| `KartStats.cs` | 可序列化數值類別 + 靜態預設(Light/Medium/Heavy × 50/100/150cc),不用 ScriptableObject(專案無資產) |
| `KartMath.cs` | 純函式:加速曲線、轉向率、漂移充能等級、牆面回彈、車對車衝量;不依賴場景,方便單元測 |
| `KartController.cs` | Rigidbody 設定、第 2 節每步流程、狀態機(Drive/Hop/Drift/Air/Stunned)、第 4 節介面與事件 |
| `KartGroundProbe.cs` | 射線/SphereCast 探地、法線平均、吸附判定;忽略自己的碰撞體與 trigger |
| `RaceSurface.cs` | 地面元件:`RaceSurfaceType` 列舉與速度/抓地倍率 |
| `KartVisual.cs` | 程式生成車體(`FieldBuilder.MakeMat`)、車身傾斜、漂移偏角、輪子轉、火花(`Look.GlowMaterial`)、`BlobShadow` |
| `KartAudio.cs` | 引擎音高隨速度、漂移/渦輪音效(走 `SfxBus`/`SfxSynth`) |
| `KartPhysicsTest.cs` | `-karttest` 自動測試(第 6 節),結果寫 `karttest.txt` |
| `KartTestTrack.cs` | 測試用最小場地:平地、斜坡 25°/45°、跳台、牆、1 mm 錯位地磚 |

(在 `SimBootstrap` 加 `-karttest` 入口屬於整合章節;`CameraRig` 已會在參數以 `test` 結尾時跳過開場動畫。)

## 6. 風險與測試

**主要風險**
1. **寫回速度蓋掉碰撞**:一定先讀 `rb.linearVelocity`,回彈在碰撞回呼裡改「下一步的基準速度」,不要在回呼直接設速度。
2. **輸入漏按**:Pad edge 只在 Update 有效 → 鎖存(見第 3 節);用 30 fps 與 144 fps 各測一次。
3. **穿牆**:最高速 boost 30 m/s × 1/60 = 0.5 m/步,必須 ContinuousDynamic 且牆厚 ≥ 1 m。
4. **接縫彈跳/坡頂亂飛**:球形碰撞體 + 貼地吸附;地形 MeshCollider 頂點要焊接。
5. **卡頓補步**:`maximumDeltaTime=0.05` 時一幀最多 3 步,計時器都用 `fixedDeltaTime` 累加,不用 `Time.time`。
6. **數值失控**:每步檢查 NaN/超速(> 60 m/s)就夾回並記 log。
7. **Juice.HitStop** 會改時間縮放,賽車碰撞不要呼叫或改用極短值。

**自動測試**:`FRC9427Sim.exe -batchmode -nographics -karttest`(PhysX 在 nographics 下照常運算),腳本化 `IKartInput` 餵輸入,依 `DriveTest` 格式寫 `karttest.txt`,每項一行 PASS/FAIL + 數據,最後 `Application.Quit()`。

| # | 測試 | 通過條件 |
|---|---|---|
| 1 | 平地全油門 4 s | 峰值 = Vtop ±3%;0→90% 在 2.0~2.8 s |
| 2 | 最高速煞車 | 停車距離 < 12 m |
| 3 | 最高速滿舵繞圈 | 轉彎半徑 14~22 m |
| 4 | 漂移+往內壓 2.0 s 後放開 | 達 Lv2;放開後 ≥ 0.5 s 速度 > 1.2·Vtop |
| 5 | 45° 撞牆 / 90° 正撞(含 boost) | 不穿牆(越過牆面 < 0.05 m);0.5 s 後速度 > 3 m/s(沒卡住) |
| 6 | 25° 坡 / 45° 坡 | 25° 能爬且保持 > 60% 速度;45° 被擋 |
| 7 | 跳台 | 判定離地;AirTime 0.4~1.2 s;落地 up·n > 0.95、反彈 < 0.3 m |
| 8 | 20 塊 1 mm 錯位地磚 | 垂直速度尖峰 < 1 m/s,全程 Grounded |
| 9 | 兩車對撞 | 兩車分開、無 NaN、總速度損失 < 50% |
| 10 | API | 無敵中 `Stun` 回 false;`Boost` 不疊加 |
| 11 | 決定性 | 相同輸入跑兩次,終點差 < 1e-3 m |
| 12 | 幀率 | `captureFramerate` 30 與 144,指令 10 次 hop 都剛好 10 次 |
| 13 | 壓力 | 8 台隨機輸入 5 分鐘:無 NaN、無掉出地圖、平均 FixedUpdate < 0.3 ms/台 |

手感部分無法自動驗證,另做 `-kartdebug` 疊加畫面(速度、charge、grounded、法線)給真人試駕調參。
