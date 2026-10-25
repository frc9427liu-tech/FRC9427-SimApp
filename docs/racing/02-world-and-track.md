# 賽車模式 02:開放世界與賽道

> 範圍:地圖、地形、賽道資料、圈數名次、重置、效能預算、對外介面、檔案清單、測試。車輛手感(KartController)、AI、道具、HUD 在其他章。
> 沿用專案慣例:全部用程式生成(同 `FieldBuilder` / `ArenaBuilder`),內建 Standard shader,`namespace FrcSim`,用啟動參數跑自動測試(同 `-fieldtest`)。

---

## 1. 「開放世界」的務實定義

**不做無限世界、不做串流載入。** 開放世界的意思是:**一張邊界固定、整張都能開的島,上面有一條主賽道,外加支線、捷徑和收集物**。比賽時跑主賽道;「自由駕駛」時整張島都能開,可以去找收集物、看風景。

| 項目 | 規格 |
|---|---|
| 地圖 | **384 × 384 m** 的島,四周是海(一個大平面,y = 0)。世界原點在島的中心,直接用 Unity 的 x/z,**不用**場地的 `FieldBuilder.P()` 座標轉換 |
| 速度尺度 | 車子最高速約 25 m/s;主賽道 1.0~1.3 km,一圈 50~60 秒,3 圈約 3 分鐘 |
| 地形 | 高度圖 **193 × 193 個點(每格 2 m)**,用自己寫的值雜訊 `RaceNoise`(整數雜湊,不依賴引擎;見第 6 節)疊 3 層(大起伏 / 山丘 / 細節),乘上「島嶼遮罩」(越靠近邊緣越低,最後沉到海面下)。同一個 seed 一定得到同一張地圖 |
| 賽道刻進地形 | 沿賽道中心線把高度圖壓成路面高度(路寬 + 每邊 6 m 用 smoothstep 接回原本地形),坡度限制在 ≤ 12°,這樣路面不會有懸崖 |
| 區塊 | 地形切成 **6 × 6 = 36 塊**,每塊 64 m;每塊有自己的 Mesh、MeshRenderer 和(非凸)MeshCollider |
| 地面材質 | 每一格存一個 `Surface`(用 byte 陣列):Road / Grass / Sand / Dirt / Boost / Water / OutOfBounds。依高度和離路距離決定:路面、路肩沙地、草地、山頂岩石、海 |
| 風格 | **低多邊形、平面著色**(每個三角形有自己的頂點,做法同 `FieldBuilder.Prism`)、明亮、不貼圖。全部顏色集中在 `RaceStyle` 裡的 8 色調色盤(草綠、沙黃、路灰、白線、岩褐、海藍、強調橘、9427 隊色)。天空用純色漸層 + 線性霧(`RenderSettings.fog`),霧也用來把遠處的裁切藏起來 |
| 物件 | 樹、石頭、護欄、拱門,全部用程式做低面數 mesh(8~40 個三角形),用 `Graphics.RenderMeshInstanced` 一次畫一批 |
| 收集物 | 金幣約 80 個、道具箱約 24 個,沿主賽道與支線擺放,捷徑上放比較多當作獎勵。**不使用 Collider**,改用格子做距離判定 |

進入賽車模式時要把 `ArenaBuilder.Clear()`、場地模型和球全部清掉,再呼叫 `Resources.UnloadUnusedAssets()`,把 2026 場地 GLB 佔的記憶體放出來給島用。

## 2. 賽道資料、圈數、名次、重置、捷徑

### 2.1 資料格式(`TrackDef`,用 JsonUtility 讀寫)
放在 `Resources/Racing/Tracks/<name>.json`;找不到檔案時用 `DefaultTracks.cs` 裡寫死的那一條,所以不會因為缺檔就不能玩。
```json
{ "name":"Island GP", "seed":9427, "laps":3, "closed":true,
  "nodes":[ {"x":-120,"z":-40,"width":12,"bank":0,"flags":0}, ... ],      // 12~24 個控制點
  "shortcuts":[ {"id":0,"fromNode":7,"toNode":9,"nodes":[...],"width":7} ],
  "checkpointSpacing":40 }
```
- 中心線:用控制點做**封閉 Catmull-Rom 樣條**,每 **2 m** 取一個 `TrackSample`(位置、切線、右方向、路寬、弧長 s、側傾角)。路面高度 = 刻好路之後那一點的地形高度,所以路一定貼著地。
- 檢查點:每隔 `checkpointSpacing`(預設 40 m)沿 s 自動放一個「門」(中心、朝向、半寬 = 路寬/2 + 4 m)。第 0 個是終點線。每個門都附一個**重置點**(門往後 6 m、在路中央、朝前進方向)。

### 2.2 圈數與名次
- 每台車只看「下一個檢查點」:車子從門的背面穿到正面(這一幀和上一幀相對門平面的符號改變,而且橫向距離 ≤ 半寬)才算通過。**一定要照順序過**,所以抄近路穿過草地拿不到檢查點。
- 通過第 0 個門,而且其他門都已經過了,才算一圈:`Lap++`。`Lap == Laps` 就算完賽,記下時間。
- 名次:`Total = Lap × Length + s`。s 是把車子位置投影到中心線上得到的弧長;只在上一次 s 前後 ±60 m 的範圍內找,避免賽道交叉時跳到另一段。每 0.1 秒依 `Total` 由大到小排一次;已完賽的車依完賽時間排在最前面。
- 逆向:車頭方向和切線的夾角 > 120°,而且速度 > 3 m/s,持續超過 2 秒,HUD 就顯示「逆向」。

### 2.3 迷路或掉出地圖的重置
符合下列任一條就重置,把車送回**最後通過的那個檢查點的重置點**,速度歸零,接下來 1.5 秒不會跟其他車相撞(暫時忽略車跟車的碰撞):
1. `y < KillY`(−5 m),或在 `Water` 上(掉進海裡);
2. 超出 `WorldBounds`(島的邊界再往外 10 m);
3. 在 `OutOfBounds` 地面上連續待超過 4 秒;
4. 有踩油門但速度 < 0.5 m/s,連續 3 秒(卡住了);
5. 玩家按「重置」鍵(冷卻 2 秒)。

自由駕駛模式只有第 1、2 條會觸發,重置點改成「離車子最近的檢查點」。

### 2.4 捷徑
捷徑是一條比較窄的樣條(寬 6~8 m),從主賽道的 `fromNode` 分出去,再接回 `toNode`。地形一樣會刻成路。捷徑上的地面材質可以設成 Dirt(比較慢)或放一個 Boost 板,用「風險換時間」來做平衡。
- 捷徑上會跳過的那些主線檢查點,標記成 `ShortcutId = k`。車子從捷徑的入口門進、出口門出,就把這些檢查點一次全部算成「已通過」。
- 在捷徑上時,s 用線性換算:捷徑走了幾成,就等於主線從入口到出口走了幾成,這樣名次不會突然跳動。

## 3. 效能預算(低階筆電、空記憶體 2~4 GB)

| 項目 | 預算 | 怎麼做到 |
|---|---|---|
| 畫面上的三角形 | ≤ **150k**(地形約 45k、路約 12k、物件約 60k、車 8 台 × 3k) | 地形每塊 32×32 格 = 2,048 個三角形(LOD0),遠的用 8×8 格(LOD1) |
| Draw call / batch | ≤ **200** | **材質要共用**:`FieldBuilder.MakeMat` 每呼叫一次就 new 一個 Material,會讓 batching 失效。`RaceStyle` 會依顏色快取材質,每種顏色只有一份。物件一律 instanced |
| 碰撞器 | 靜態 ≤ **150**:地形 36 個 MeshCollider + 護欄 BoxCollider 約 60 個 + 賽道旁的樹/石頭 Capsule 約 50 個(離路 > 15 m 的物件不加碰撞器) | 收集物和檢查點不用碰撞器(用程式算) |
| 記憶體 | 賽車世界 ≤ **80 MB** | 高度圖 + 地面材質陣列 < 200 KB;所有 Mesh 合計約 15 MB |
| 建圖時間 | ≤ **1.5 秒**(低階筆電) | MeshCollider 用 `Physics.BakeMesh`,可以分到好幾幀做;建圖時顯示讀取畫面 |
| 每幀配置記憶體 | 0 B GC | 查詢函式不用 LINQ、不 new 陣列;名次排序用預先配好的陣列 |

**LOD / 裁切策略**
- 地形區塊:Unity 本身會做視錐裁切;`RaceWorldLod` 每 0.25 秒依區塊離攝影機的距離切換 LOD(< 96 m 用 LOD0,其他用 LOD1)。每塊都加 2 m 的「裙邊」,用來遮住 LOD 交界的裂縫。**碰撞器永遠用 LOD0 那一份,不跟著切換。**
- 物件:依所在的地形區塊分組,每組用一次 `RenderMeshInstanced` 畫;距離 > 140 m 的組整組不畫。攝影機遠裁切面 400 m,霧從 150 m 開始、300 m 全霧。
- 陰影:shadow distance 設 50 m;地形和物件**不投射陰影**,只接收。車子沿用 `BlobShadow`。
- 品質設定接 `Prefs`:「低」= 只用 LOD1、物件密度 50%、關即時陰影。

## 4. 對外介面(給 KartController 與 AI 用)

```csharp
namespace FrcSim {
public enum Surface : byte { Road, Grass, Sand, Dirt, Boost, Water, OutOfBounds }
public struct SurfaceProps { public float Grip, MaxSpeedMul, Drag; public bool Resets; }
public struct GroundHit { public float Height; public Vector3 Normal; public Surface Surface; public SurfaceProps Props; }
public struct TrackSample { public Vector3 Pos, Tangent, Right; public float Width, S, Bank; }
public enum ResetReason { Fell, OutOfBounds, OffTrackTooLong, Stuck, Manual }

public static class WorldBuilder {
    public const float Size = 384f, Cell = 2f, KillY = -5f;
    public static Transform Root { get; }
    public static Bounds WorldBounds { get; }
    public static Track Track { get; }                       // 目前的主賽道
    public static void Build(TrackDef def, RaceQuality q);   // 同一個 seed 結果一定相同
    public static void Clear();
    public static float HeightAt(float x, float z);          // 從高度圖用雙線性內插算(不做 raycast,headless 也能用)
    public static Vector3 NormalAt(float x, float z);
    public static Surface SurfaceAt(float x, float z);
    public static bool SampleGround(Vector3 p, out GroundHit hit);   // 超出地圖就回傳 false
    public static SurfaceProps Props(Surface s);
    public static bool InBounds(Vector3 p);
}

public sealed class Track {
    public string Name; public int Laps; public bool Closed; public float Length;
    public IReadOnlyList<TrackSample> Samples { get; }       // 每 2 m 一個
    public IReadOnlyList<Checkpoint> Checkpoints { get; }
    public IReadOnlyList<Shortcut> Shortcuts { get; }
    public IReadOnlyList<Pose> GridSlots { get; }            // 起跑格,2 排 × 4 格
    public static Track FromDef(TrackDef def);
    public float Wrap(float s);
    public TrackSample SampleAt(float s);                    // 在相鄰取樣點之間內插
    public float Project(Vector3 p, float hintS, out float lateral);   // 回傳 s;lateral 往右為正
    public Vector3 CenterAt(float s);
    public Vector3 AheadPoint(float s, float distance);      // AI 用的前瞻目標點
    public float CurvatureAt(float s);                       // AI 用來決定過彎時要減到多少速度
}

public sealed class Checkpoint { public int Index; public float S; public Vector3 Center, Forward;
    public float HalfWidth; public Pose Respawn; public bool IsFinish; public int ShortcutId = -1; }

public interface IRacerBody {                                 // KartController 實作這個介面
    Vector3 Position { get; } Vector3 Velocity { get; } Vector3 Forward { get; }
    bool ThrottleHeld { get; }
    void Respawn(Pose p);                                     // 傳送到 p、速度歸零、開始 1.5 秒不碰撞
}

public sealed class RacerProgress { public int Id, Lap, NextCp, Rank; public float S, Total,
    LastLapTime, BestLapTime, FinishTime; public bool Finished, WrongWay, OnShortcut; }

public class CheckpointSystem : MonoBehaviour {
    public static CheckpointSystem I { get; }
    public bool FreeRoam;                                     // 自由駕駛:不算圈數,只檢查會不會掉出地圖
    public int Register(IRacerBody racer, bool isPlayer);
    public void Unregister(int id);
    public RacerProgress Get(int id);
    public IReadOnlyList<RacerProgress> Standings { get; }   // 依名次排好
    public Checkpoint NextCheckpoint(int id);
    public Checkpoint LastCheckpoint(int id);
    public Checkpoint NearestCheckpoint(Vector3 p);
    public Pose GetRespawnPose(int id);
    public void RequestReset(int id, ResetReason why);
    public void StartRace(float startTime);
    public void Step(float dt);                               // FixedUpdate 會呼叫;測試可以直接手動呼叫
    public event System.Action<int, int> LapCompleted;        // (id, lap)
    public event System.Action<int> Finished;
    public event System.Action<int, ResetReason> Reset;
}
}
```
約定:**車子的離地高度由 KartController 用 `SampleGround` 自己算**(跟 `SwerveDrive` 用 `BumpHeightAt` 決定車高是同一種做法)。地形 MeshCollider 只是保險,防止車子穿到地底下,以及給球、道具等物理物件落地用。這樣 AI 和 headless 測試都不需要 raycast,結果也穩定。

## 5. 新檔案清單(`Assets/Scripts/Racing/`)

| 檔案 | 責任 |
|---|---|
| `RaceStyle.cs` | 8 色調色盤、依顏色快取材質(包一層 `FieldBuilder.MakeMat` / `Look.GlowMaterial`)、天空和霧的設定 |
| `RaceTypes.cs` | `Surface`、`SurfaceProps`、`GroundHit`、`TrackSample`、`ResetReason`、`RaceQuality`、`IRacerBody` |
| `TrackDef.cs` | 可序列化的賽道定義、讀 JSON、檢查資料是否正確(節點數、寬度、捷徑索引) |
| `DefaultTracks.cs` | 寫死在程式裡的預設賽道「Island GP」(找不到 JSON 時用) |
| `Track.cs` | Catmull-Rom 取樣、弧長、`Project` / `SampleAt` / `AheadPoint` / 曲率、自動放檢查點、起跑格 |
| `WorldBuilder.cs` | 建圖流程:高度圖 → 刻路 → 地面材質 → 區塊 → 路面 → 物件 → 收集物;地面查詢 API |
| `TerrainChunks.cs` | 每個區塊的平面著色 mesh(LOD0 / LOD1 + 裙邊)和 MeshCollider 烘焙 |
| `TrackMeshBuilder.cs` | 路面帶狀 mesh、路緣紅白條、白線、轉彎外側護欄(mesh + BoxCollider)、起終點拱門 |
| `PropScatter.cs` | 用 Poisson 圓盤法撒樹和石頭(避開路面)、依區塊分組 instanced 繪製、賽道附近的物件加碰撞器 |
| `Collectibles.cs` | 金幣/道具箱的擺放、instanced 繪製(含旋轉動畫)、用格子做距離撿取、重生計時 |
| `RaceWorldLod.cs` | 每 0.25 秒切換區塊 LOD、裁切物件組、套用品質設定 |
| `CheckpointSystem.cs` | 過門判定、圈數、名次、逆向判定、重置規則、事件 |
| `RaceWorldTest.cs` | 自動測試入口 `-racetest`(見第 6 節),結果寫到 `racetest.txt` 後離開程式 |

`SimBootstrap.cs` 只需要加兩行:`-racing` 進入賽車模式、`-racetest` 跑測試(這屬於整合的那一章,本章不改)。

## 6. 風險與自動測試

**風險與對策**
| 風險 | 對策 |
|---|---|
| `MakeMat` 每次 new 材質 → draw call 爆量 | 一律經過 `RaceStyle` 快取;測試會檢查「不同材質數 ≤ 24」 |
| 平面著色的頂點數變 3 倍,一塊超過 65,535 個頂點 | 一塊 2,048 個三角形 × 3 = 6,144 個頂點,遠低於上限;裙邊另外算 |
| 樣條自己交叉,或急彎讓投影跳到別段 | 只在 `hintS` 附近 ±60 m 找;讀資料時檢查最小彎道半徑 ≥ 12 m |
| 刻路造成懸崖或路面凹陷 | 限制坡度、smoothstep 過渡;測試會掃過整條中心線 |
| 車速快時穿過地形 | 車用 `CollisionDetectionMode.ContinuousSpeculative`,而且高度本來就由程式算 |
| 低階筆電建圖卡頓 | `BakeMesh` 分幀做;區塊和物件用協程每幀建一部分 |
| 捷徑被濫用(直接切草地) | 一定要照順序過門;門半寬只比路寬多 4 m;草地 `MaxSpeedMul` = 0.55 |
| 不同電腦/版本的 Perlin 結果不同 | 改用自己寫的整數雜湊值雜訊(`RaceNoise`,放在 `WorldBuilder.cs` 裡),不依賴引擎實作 |

**自動測試**(`FrcSim.exe -batchmode -nographics -racetest`,可以加 `-seed N`;不需要 GPU,跟 `-fieldtest` 同樣模式)
1. **確定性**:同一個 seed 建兩次,高度圖和地面材質陣列的雜湊值要一樣。
2. **查詢和碰撞一致**:隨機取 1,000 點,`HeightAt` 和對地形 MeshCollider 做 `Physics.Raycast` 的高度差 < 0.05 m。
3. **路面品質**:整條中心線 `|HeightAt − Sample.y| < 0.05 m`,坡度 ≤ 12°,路面上的點 `SurfaceAt == Road`。
4. **圈數**:一台「幽靈車」(實作 `IRacerBody`,沿中心線以 25 m/s 前進)跑完 `Laps` 圈,要剛好觸發 `Finished`;倒著開不能算圈;直接傳送跳過一個門,不能算圈。
5. **捷徑**:幽靈車走捷徑,要算一圈,而且每一步 `Total` 都只增不減。
6. **重置**:把車丟到 y = −10、丟進海裡、丟到邊界外,下一次 `Step` 就要回到最後一個檢查點的重置點,而且重置點的位置在路面上。
7. **名次**:8 台幽靈車錯開出發,`Standings` 要依 `Total` 正確排序;一個完賽、其他還在跑時排序也要對。
8. **預算**(nographics 沒辦法量 draw call,改量替代指標):三角形總數、碰撞器數、不同材質數、`Renderer` 數、建圖時間、`Profiler.GetTotalAllocatedMemoryLong` 增加量,超出第 3 節的預算就判 FAIL。
9. **長時間測試**:8 台幽靈車加隨機橫向偏移跑 300 秒,不能出現 NaN,沒有車卡住超過 10 秒,每幀 GC 配置 = 0。

每一項輸出 `PASS/FAIL + 數據` 寫到 `racetest.txt`(格式同 `fieldtest.txt`),最後一行寫 `ALL PASS` 或失敗項目數,方便 `Tools\run-gate.ps1` 讀取。

---

## M5 實作紀錄:放大的世界、3 條賽道、捷徑、自由探索(模組 W)

- **世界**:864 x 864 m(`RaceWorld.Size`),高度圖仍是 193 x 193,格距 4.5 m(`RaceTerrain.Cell`),仍切 6 x 6 = 36 塊(每塊 144 m、LOD0 2,048 個三角形),所以三角形 / 碰撞器預算不變。LOD0 距離 200 m、物件組裁切距離 260 m、霧與遠裁切面依 theme 拉遠(`RaceWorld` 建完世界後設定,RaceSession 結束時 Env.Restore 還原)。
- **賽道**(`DefaultTracks`,`RaceWorld.TrackCount == 3`):
  | id | 名稱 | 長度 | 地貌 | 特色 |
  |---|---|---|---|---|
  | 0 | Island GP / 島嶼大賽 | 1,444 m | 丘陵島(theme 0) | 北側缺口捷徑、大拱門、3 座風車、4 跳台 6 加速板 |
  | 1 | Canyon Run / 峽谷疾馳 | 1,197 m | 沙漠峽谷(theme 1,離路 12~42 m 就長成陡壁) | 連續彎、5 跳台、岩石拱門、石柱 |
  | 2 | Coast Loop / 海岸環線 | 1,596 m | 海岸(theme 2,路邊 25 m 外是懸崖) | 長直道 + 大半徑彎、燈塔、大拱門、風車 |
  每條賽道有自己的地形種子(`TrackDef.seed`;`RaceConfig.Seed` 保持預設 9427 時用賽道的)、色調(`TrackTheme`)、起跑格(在最長直道上)、檢查點間距(45 / 40 / 60 m)。
- **捷徑**(只有 Island GP):`TrackDef.shortcuts` → `RaceTrack.Shortcuts`(`ShortcutPath`)。土路(Dirt)、7 m 寬、兩塊加速板;被跳過的主線門 `Checkpoint.ShortcutId = 0`;要穿過入口門再穿過出口門才算走完,
  沒穿入口門(直接切草地 / 傳送)拿不到被跳過的門。走捷徑時 `RacerProgress.OnShortcut = true`,名次的弧長以捷徑走了幾成線性換算。捷徑穿過護欄的地方護欄會留缺口。
- **自由探索**(`RaceConfig.FreeRoam`):世界多 30 個收集物;`RaceWorld.Collectibles / CollectibleTotal / CollectedCount / TryCollect(pos) / ResetCollectibles() / CoinCollected` 事件。
  `RaceCheckpoints` 在 FreeRoam 不算圈、不過門、不判卡住,只檢查掉進海 / 出界,重生點 = 2~3 秒前的安全位置。
- **測試**:`RaceWorldTest`(`-raceworldtest`)對 3 條賽道逐條跑 T1~T4、T6~T10、T13、BUDGET;T5(捷徑)、T11(自由探索檢查點)、T12(收集物)為真測試。

## 路面導引、移動障礙、動畫造景(2026-10-24)

- **路面導引**(零額外三角形):`TrackMeshBuilder` 給路面網格第二組 UV(`Vector3`:局部曲率、前方 8~60 m 內最緊彎的曲率、沿路弧長),`RaceRoad.shader` 在彎道外側畫「主題強調色 / 白」交替邊線,彎前(半徑 < 80 m)畫雙層 V 形箭頭(尖端偏向彎的方向)。強調色表 `TrackMeshBuilder.GuideColor(theme)`(材質屬性 `_Guide`)。起終點 30~40 m 與缺口前後不畫;路肩 / 捷徑網格沒有第二組 UV,不受影響。
- **移動障礙**(`RaceHazards`,`-racehazardtest`):每條賽道 2~3 個,位置只由賽道決定(直線 / 緩彎、不在缺口 / 起跑區 / 捷徑入口出口 / 跳台加速板附近、間隔 >= 120 m),運動是「賽道 + 比賽時鐘」的純函式(`StateAt`,時鐘只由 `Tick(dt)` / `ResetClock()` 推進,GO 時歸零)。命中 = `RaceKart.Stun(0.8)`(自動掉 2 枚金幣)+ 1.5 s 無敵;星星 / 巨大化 / 子彈 / 無敵 / 暈眩中 / 羽毛跳躍中 / 空中不會被打。AI 經 `RaceAI.HazardSource`(`RaceHazards.MergedWith`)輕度閃避。主題:Island 木桶 + 螃蟹、Canyon 巨石 + 仙人掌、Coast 螃蟹 + 木桶、Meadow 乾草捲 + 青蛙、Desert 風滾草 + 仙人掌、Snow 滑動冰塊 + 雪球、Forest 落下滾動的圓木 + 巨石、Magma 彈跳火球。外觀用 `RaceToonVC` 單例材質(不新增材質),每條賽道 <= 360 個三角形。
- **動畫造景**:`RaceToonVC` 的頂點動畫(`uv1` = 類型 / 權重 / 相位;類型 1 布料擺動、2 觀眾跳動、3 氣球漂浮、4 火焰閃爍、5 熔岩氣泡脈動),由 `RaceColorAcc.A` 標記,整條賽道只多 1 個 draw call(`TrackAnim`)。旗幟(看板橫幅、彩旗、新增路邊旗桿)、看台觀眾、雪地 / 草原 / 海岸氣球串、火山火炬與熔岩氣泡都走這條路。風車葉片是獨立網格 + `RaceSpinner`(只有 2~3 個物件逐幀轉)。
