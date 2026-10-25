# 賽車模式設計書 04:攝影機、介面、美術、音效回饋與整合

> 範圍:賽車模式(類瑪利歐賽車、開放世界)的「看起來、聽起來、怎麼接進現有程式」。物理手感見 01、世界與賽道見 02、道具與 AI 見 03。
> 原則:**全部用程式生成、不用外部素材**;**FRC 模式一行行為都不能變**;沿用現有慣例(`FieldBuilder.MakeMat`、`Look.GlowMaterial`、`GlassGL.Draw`、`SfxBus`/`SfxSynth`、`Rumble`、`Prefs`、`MenuSystem` 的 `Item` + `ShowXxx()`)。
> 新程式一律放 `Assets/Scripts/Race/`,`namespace FrcSim`,類別以 `Race`/`Kart` 開頭,避免和現有類別撞名。

---

## 1. 美術風格指南

**關鍵字:明亮、飽和、平面著色(flat shading)、圓潤比例、大色塊少細節。** 光源只用一盞暖色太陽 + 天空色環境光(`RenderSettings.ambientMode = Trilight`),陰影距離 80 m。

### 1.1 色盤(所有模組共用,寫在 `RaceArt.Palette`)

| 用途 | 色碼 | 用途 | 色碼 |
|---|---|---|---|
| 天頂 / 地平線 | `#4FB8FF` / `#D6F4FF` | 草地亮 / 暗 | `#7ED957` / `#4FA83A` |
| 柏油路 / 路線 | `#4A4E5A` / `#FFFFFF` | 路緣(紅白相間) | `#FF4D4D` / `#FFFFFF` |
| 沙地 / 岩石 | `#F2D48A` / `#A9A9BC` | 水面 | `#3FB6E8`(smooth 0.9) |
| 樹葉 3 色 | `#2E9E4F` `#4CC25E` `#1F7A3E` | 樹幹 | `#8B5A2B` |
| 車色 6 色 | 紅 `#FF4A3D` 藍 `#3FA6FF`(=UiTheme.Accent) 黃 `#FFC93C` 綠 `#3DDC84` 紫 `#A66BFF` 橘 `#FF8A2A` | 名次金/銀/銅 | `#FFD23F` / `#C9D6E3` / `#E0915A` |

材質規則:地形/樹/雲 `smooth 0.1、metal 0`(霧面);車殼 `smooth 0.6`;加速板、終點門、霓虹用 `Look.GlowMaterial(c, 2~3)`。
**一定要經 `RaceArt.Mat(Color, smooth)` 拿材質**(內部用 Dictionary 快取 `FieldBuilder.MakeMat` 的結果),否則幾千棵樹會產生幾千個材質。

### 1.2 車體造型(基本幾何組合,4 款)

每款由 `RaceArt.BuildKart(KartStyle, Color)` 生成,階層固定:`Kart(物理,01 擁有)` → `Visual`(傾斜/擠壓用)→ `Body`、`WheelFL/FR/RL/RR`、`ExhaustL/R`(空物件,噴焰位置)、`Driver`。**用 `CreatePrimitive` 後立刻 `Destroy` 它的 Collider**(碰撞由 01 的單一 BoxCollider 負責)。

| 款式 | 組成 | 個性 |
|---|---|---|
| ① 方塊小子 Brick | 扁長方體車身 1.2×0.35×1.8、方塊座艙、後翼(薄方塊)、4 個圓柱輪 r0.28 | 平均型,MVP 預設 |
| ② 子彈 Bullet | 橫躺膠囊車身 + 球形車鼻 + 2 片三角鰭(自製 4 頂點網格)+ 小輪 r0.22 | 極速型 |
| ③ 胖胖卡車 Chonk | 高方塊車身 + 細方塊防滾架 + 大輪 r0.45 | 重量型、被撞不易飛 |
| ④ 9427 機器人車 | 藍色保險桿方框(4 條方塊)+ 4 顆 swerve 模組(圓柱+小方塊)+ 旗桿與「9427」旗 | 向 FRC 致敬,隊伍識別 |

駕駛 = 膠囊身體 + 球形安全帽(車色)+ 黑色面罩(壓扁球)。輪子每幀依車速旋轉、前輪依轉向 ±25°;`Visual` 依側向加速度傾斜(最多 6°)、落地時 y 方向擠壓 0.85→1(0.15 s 彈回)。

### 1.3 地形 / 天空 / 雲 / 樹(02 負責擺放,04 提供生成函式)

- **低多邊形工具**:`RaceArt.Flat(Mesh)` 把共用頂點拆開再 `RecalculateNormals()`,得到稜角分明的平面著色。
- **地形顏色**:內建 Standard shader **不吃頂點色**,所以用「調色盤貼圖」:16×1 點取樣貼圖(`FilterMode.Point`),每個三角形的 UV 指到一格顏色(依高度/坡度:沙→草亮→草暗→岩石)。整片地形只要 1 個材質。
- **天空**:相機 `clearFlags = SolidColor`(地平線色)+ 內翻球體天穹,材質用 `Sprites/Default`(Fx 已在用,確定會被打包;不受光、雙面),貼 1×64 漸層貼圖,`renderQueue = 1000` 最先畫,跟著相機移動。加線性霧 `fogColor = 地平線色`、`150~600 m`,遠景自然融入。
- **雲**:每朵 3~6 顆白球(y 壓扁 0.6)用 `Mesh.CombineMeshes` 合成一個網格,不投影,高度 60~90 m,`RaceCloud` 元件以 1.5 m/s 飄,出界就繞回另一邊。
- **樹**:3 種(松樹 = 圓柱幹 + 2~3 層自製 6 邊圓錐;圓樹 = 幹 + 自製 icosphere;棕櫚 = 斜幹 + 5 片扁方塊葉)。用固定亂數種子(`System.Random(seed)`)抖動格點擺放、避開路面寬度 +3 m;**每 64 m 區塊合併成一個網格**(`IndexFormat.UInt32`),控制在 < 300 draw call。

### 1.4 粒子特效(`RaceFx`,沿用 `Fx` 的做法:常駐 ParticleSystem + `Emit()`)

| 特效 | 做法 | 參數 |
|---|---|---|
| 漂移火花 | 加色(`Look.Sh("FrcParticle")`),從兩個後輪接地點噴,向後+外側+往上 1~2 m/s,重力 0.8 | 等級 1 藍 `#4DB8FF`、2 橘 `#FF9A2E`、3 粉紫 `#FF4DF0`;40~70 顆/s、壽命 0.3 s |
| 輪胎煙 / 草屑 | 半透明 puff,路面灰、草地綠褐 | 8 顆/s,越飄越大 |
| 加速噴焰 | 獨立系統 `renderMode = Stretch`,從 `ExhaustL/R` 向後 6 m/s,顏色白→黃→橘→透明 | 80 顆/s、壽命 0.15 s;HUD 另畫螢幕邊緣速度線 |
| 撞擊 | 12 顆黃色星星 + 一圈 puff + 呼叫現有 `Fx.Bump(pos,k)` | 依撞擊速度 k 縮放 |
| 落地 / 道具箱破裂 | 落地灰塵圈;道具箱彩色碎片(03 呼叫 `RaceFx.ItemBox(pos)`) | — |

`JuiceSettings.ReduceFlash` 開啟時粒子數減半(同 `Fx.Cnt()`)。

---

## 2. 追尾攝影機(`RaceCamera`)

**接法:不新增相機,在 `CameraRig.LateUpdate` 加一個 `Mode == 10` 分支**,呼叫 `RaceCamera.Compute(Target, out pos, out rot)`,其餘(SmoothDamp、JuiceFX 震動)沿用。賽車模式不呼叫 `rig.StartIntro()`(那段有 FRC 名牌與等 HalSim 的邏輯)。

| 行為 | 規格 |
|---|---|
| 平滑跟隨 | 距離 `lerp(5.5, 7.0, 速度/極速)`、高 2.2 m,注視點 = 車 + 上 1.0 + 前 3.0。航向追「速度方向」(速度 > 3 m/s 時)與車頭混合,指數平滑速率 6/s;沿用 CameraRig 的 SphereCast 防遮擋(用 `GetComponentInParent<RaceKart>()` 排除車,**不新增 Layer**,因為 Layer 要改 ProjectSettings)。另做地形射線,鏡頭至少離地 0.6 m |
| 漂移外擺 | 漂移時鏡頭偏航 `-漂移方向 × 12°`、橫移 0.6 m、滾轉 2°,0.3 s 緩入緩出,讓玩家看到彎道出口 |
| 加速 FOV | 基準 62°;`+6° × 速度/極速`、加速中再 `+10°` 並後拉 0.8 m;用彈簧回復 |
| 倒車回望 | 按住「回望鍵」(鍵盤 Q / 手把 LB)→ 0.1 s 切到車前看向車後;倒車速度 < −2 m/s 超過 0.5 s 時鏡頭自動緩轉到車尾方向 |
| 起跑運鏡 | 共 5 s、上下黑邊(仿現有 intro):① 2 s 沿賽道前 25% 中線高 25 m 飛越 ② 1.5 s 繞起跑格 ③ 1.5 s 俯衝到玩家車後 → 接 3-2-1。按 A/Enter 可跳過;`SuperSample.IsTestRun()` 時直接略過 |
| 終點 | 過線後改為繞玩家車慢速環繞(半徑 5 m、0.15 rad/s),2 s 後跳成績卡 |

**衝突點(必改兩行):** `JuiceFX` 目前只在 `ActiveMode == 1` 才震動,且每幀把 `fieldOfView` 覆蓋成 `baseFov + speedFov`(速度讀 `GameSession.Drive`)。解法:`JuiceFX` 的條件改為 `ActiveMode == 1 || ActiveMode == 10`,並新增 `public static float BaseFovOverride = float.NaN;` — 非 NaN 時用它當 baseFov 且不加 speedFov,由 `RaceCamera` 每幀設定,離開賽車模式時設回 NaN。`C` 鍵在賽車模式只在 10(追尾)/ 11(高空俯瞰)之間切換。

---

## 3. 比賽 HUD(`RaceHud`,OnGUI)

**不重用 `SimHud` 元件**(它綁 `SwerveDrive`/`RobotMechanisms`/`ScoreManager`),賽車模式不生成 SimHud。但**視覺語言完全相容**:同樣 `GUI.depth = -100`、`sc = Clamp(Screen.height/900, 0.8, 1.8)` 的 `GUI.matrix` 縮放、`MenuSystem.Blocking` 時不畫、Repaint 時設 `UiGlass.HudFrame`、底板用 `GlassGL.Draw(r, radius, GlassStyle.HudPanel()/HudCard()/Pill())`,`UiGlass.Ready == false` 時退回半透明圓角矩形。以下座標以虛擬畫面 `W × H`(= 螢幕 / sc)的比例表示:

| 元件 | 位置 (x, y, w, h) | 內容 |
|---|---|---|
| 道具欄 | `(0.02W, 0.03H, 120, 120)` HudCard + 右側小格 70×70 | 道具圖示(OnGUI 畫幾何圖形);抽道具時 0.08 s 換一格的輪盤 |
| 圈數 / 計時 | 右上 `(W−0.02W−300, 0.03H, 300, 120)` | `LAP 2/3` 大字;總時間 `1:23.456`;本圈、最佳圈(暗色) |
| 名次 | 右下 `(W−0.03W−220, H−0.05H−150, 220, 150)` | 120 px 粗體 `1st`,名次變動時 `Juice.PopMatrix` 彈跳,顏色金/銀/銅/白 |
| 小地圖 | 左下 `(0.02W, H−0.02H−220, 220, 220)` | 用 `RaceTrack.ToMap()` 畫賽道折線,每台車一個彩點,玩家為三角箭頭(同 SimHud 小地圖畫法) |
| 速度/漂移槽 | 底部中央 `(W/2−160, H−70, 320, 44)` Pill | 速度數字 + 漂移等級 3 段色條 |
| 倒數 | 中央 `(W/2, 0.38H)` | `3`、`2`、`1` 白色 220 px,每次 1.4→1.0 縮放;`GO!` 黃綠 `#C6FF3D`,0.8 s 淡出 |
| 中央提示 | `(W/2, 0.22H)` | 「第 2 圈」「最後一圈!」「逆向!」,2 s 淡出 |
| 終點成績卡 | 置中 `560 × 420` HudCard | 名次、總時間、各圈時間(最佳圈標金)、「🏆 新紀錄」、按鈕「再跑一次 / 回主選單」(和 SimHud 結束畫面一樣在非 Repaint 事件也處理按鍵) |

---

## 4. 音效與手把震動(`RaceJuice` + `SfxSynth` 新音色)

全部在 `SfxSynth` 新增合成音(加進 `Lens` 表與 `Get()` 的生成分支),播放走 `SfxBus.Play / SetLoop / Duck`,音量受 `JuiceSettings` 控制。

| 音色名稱 | 合成方式 | 觸發 |
|---|---|---|
| `kart_engine_loop` | 鋸齒波基頻 + 2 倍、3 倍泛音 + 少量雜訊,整數週期無縫循環(22.05 kHz) | `SetLoop` 音高 `0.6 + 1.4 × 速度/極速`,油門放開降 15% |
| `kart_skid_loop` | 帶通雜訊(約 2 kHz) | 漂移 / 急煞時音量 0~0.35 |
| `drift_lv` | 短上升正弦掃頻,每升一級音高 ×1.26 | 漂移等級提升 |
| `boost` | 雜訊呼嘯 + 60 Hz 低音 thump,0.6 s | 加速開始,同時 `SfxBus.Duck(0.6, 0.4)` |
| `count_beep` / `count_go` | 440 Hz 方波 0.18 s / 880 Hz + 五度和弦 0.5 s | 3-2-1 / GO |
| `lap`、`final_lap` | 兩音 / 三音上行琶音(用現有 `Note()`) | 過圈 |
| `item_get`、`hit_spin` | 亮音 ding / 下降顫音 | 道具、被擊中 |
| `race_music_loop` | 8 s、140 BPM 五聲音階琶音 + 低音 | 比賽中循環;最後一圈音高 1.06 |
| 撞牆 / 撞車 | 沿用現有 `bump` | 依撞擊速度調音量 |

**手把震動(`Rumble.Driver(low, high, sec)`,已自動遵守 `JuiceSettings.Rumble`):** 漂移中 `0.15, 0, 0.1`(每 0.1 s 補發);漂移升級 `0, 0.5, 0.08`;加速 `0.6, 0.3, 0.3`;撞擊 `0.3+0.7k, 0.2+0.5k, 0.12+0.2k`(同 `Juice.OnRobotBump`);被道具擊中 `0.9, 0.6, 0.5`;倒數 `0, 0.2, 0.05`、GO `0.5, 0, 0.2`;拿第一名三下脈衝。畫面震動用 `JuiceFX.AddTrauma`(撞擊 0.3~0.8、加速 0.1)。

**衝突點:** `Juice.Tick()` 在 `GameSession.Mech == null` 時會播 FRC 主選單音樂與群眾聲。解法:`Juice.Tick` 開頭加 `if (GameSession.Racing) { Rumble.Tick(dt); RaceJuice.Tick(dt); return; }`,並在進入賽車時把 `intake/drive/fly/crowd/music` 五個 loop 音量設 0。

---

## 5. 整合計畫

### 5.1 GameSession(加旗標、分流,不動 FRC 流程)
```csharp
public static bool Racing;                 // 新增
// Begin(): 在 if (Active) return; 之後
if (Racing) { Active = true; StartTime = Time.time; Time.timeScale = 1f; RaceSession.Begin(rig); return; }
// Rematch(): 最前面
if (Racing) { RaceSession.Restart(); return; }
// End(): Active = false 之後
if (Racing) { RaceSession.End(rig); Racing = false; rig.Target = null; rig.Orbit = true; return; }
```
`RaceSession.Begin` 做的事:把 `Field`、`Arena` 根物件 `SetActive(false)`(含碰撞)→ 存下 `RenderSettings`、相機 clearFlags、`Time.fixedDeltaTime` → `RaceWorld.Build()` → 生成車與 AI → `rig.Target = 玩家車; rig.Orbit = false; rig.Mode = 10` → 生成 `RaceHud` → 起跑運鏡 → 倒數。`End` 反向全部還原。`Active = true` 保留,這樣 Esc 暫停選單照常能叫出。

### 5.2 MenuSystem(Item + ShowXxx 慣例)
- `ShowModes()` 的「比賽 ▸」下面加:`new Item(() => Z("賽車  ▸", "Kart racing  ▸"), ShowRacing) { Sub = () => Z("開放世界賽車:漂移、道具、對戰 AI", "Open-world kart racing") }`
- `ShowRacing()`:賽道(輪替)、圈數 1/3/5、AI 數量 0/3/7、AI 難度、車款 ▸(`ShowKartPick(ShowRacing)`)、「開始」、返回 `ShowModes`。每項 `Sub` 顯示該賽道最佳圈。
- 「開始」:`GameSession.Racing = true; RaceSession.Pending = cfg; Hide(); GameSession.Begin(Rig);` **不走 `Go()`/`SetupOk()`**(那是 FRC 機器人設定流程)。
- `ShowPause()`:賽車模式下隱藏投籃/駕駛訓練項目(`EnabledFn = () => !GameSession.Racing`),「再來一場」走 `GameSession.Rematch()`(已分流)。
- 測試旗標 `-race <賽道編號>`:跳過選單直接開賽,給自動截圖/煙霧測試用。

### 5.3 存檔
最佳圈 `Prefs.GetInt("race_bestLap_" + trackId, 0)`、最佳總時間 `"race_bestTotal_" + trackId + "_" + laps`,單位**毫秒**(`Mathf.RoundToInt(t * 1000f)`),0 = 沒紀錄;寫入後 `PlayerPrefs.Save()`(同 `DrivingCourse`)。車款 `race_kart`、車色 `race_color`。`Prefs.Isolated`(測試)時自動不寫登錄檔。計時用「比賽中且 `!MenuSystem.Blocking`」才累加 `Time.unscaledDeltaTime`(暫停會把 timeScale 設 0,但計時要明確停)。

---

## 6. 模組契約總表

| 設計書 | 類別 / 簽名(全部 `namespace FrcSim`) |
|---|---|
| **01 物理手感** | `struct KartInput { float Steer, Throttle, Brake; bool Drift, UseItem, LookBack; }`<br>`class RaceKart : MonoBehaviour { KartInput Input; float Speed, MaxSpeed; Vector3 Velocity; bool Grounded, Drifting; int DriftDir, DriftLevel /*0..3*/; float Boost /*0..1*/; Surface Surface; KartStats Stats; void Teleport(Vector3 p, Quaternion r); void ApplyHit(float spinSec); event Action<int> DriftLevelUp; event Action<float> BoostStart; event Action<Vector3,float> Impact; event Action Landed; }`<br>`static class KartInputReader { static KartInput Read(); }`(讀 `Pad` + 鍵盤,**不吃 Start/Back**,留給暫停) |
| **02 世界與賽道** | `static class RaceWorld { static RaceTrack Build(int trackId, int seed, Transform root); static void Destroy(); static int TrackCount; static string TrackName(int id); }`<br>`class RaceTrack { Vector3[] Center; float[] Width; int Checkpoints; Pose Grid(int slot); float Progress(Vector3 p) /*0..1 一圈*/; bool IsWrongWay(Vector3 p, Vector3 fwd); Surface SurfaceAt(Vector3 p); float HeightAt(float x, float z); Vector2 ToMap(Vector3 p) /*0..1*/; }`<br>`enum Surface { Road, Grass, Dirt, Sand, Water, BoostPad }` |
| **03 道具與 AI** | `enum ItemKind { None, Mushroom, GreenShell, Banana, Star }`<br>`static class RaceItems { static ItemKind Held(RaceKart k); static bool Rolling(RaceKart k); static void Use(RaceKart k); event Action<RaceKart,ItemKind> Got, Used; event Action<RaceKart,RaceKart,ItemKind> Hit; }`<br>`class RaceAI : MonoBehaviour { RaceKart Kart; RaceTrack Track; int Level; }`(每幀寫 `Kart.Input`,和玩家同一條輸入路徑) |
| **04 本份** | `static class RaceSession { static bool Active; static RaceConfig Pending; static RacePhase Phase /*Intro,Countdown,Racing,Finished*/; static RaceKart Player; static List<RaceKart> Karts; static int Lap(RaceKart k); static int Place(RaceKart k); static float RaceTime; static List<float> LapTimes; static void Begin(CameraRig rig); static void Restart(); static void End(CameraRig rig); }`<br>`static class RaceCamera { static void Compute(Transform t, out Vector3 pos, out Quaternion rot); }` · `class RaceHud : MonoBehaviour`<br>`static class RaceArt { static Material Mat(Color c, float smooth = 0.1f); static GameObject BuildKart(KartStyle s, Color c); static Mesh Flat(Mesh m); static Mesh Cone(int sides); static Mesh IcoSphere(int sub); static GameObject Tree(TreeKind k, System.Random r); static GameObject Cloud(System.Random r); static void ApplyOutdoor(); static void RestoreIndoor(); }`<br>`static class RaceFx { static void Init(Transform p); static void DriftSpark(Vector3 p, Vector3 v, int lv); static void Flame(Transform ex, float k); static void Impact(Vector3 p, float k); static void ItemBox(Vector3 p); }` · `static class RaceJuice { static void Tick(float dt); }`(訂閱 01/03 的 event) |

**依賴方向:01 ← 02 ← 03 ← 04**(04 可以用所有人;01 不准參考 03/04)。特效與音效**一律由 04 訂閱事件觸發**,01/03 不直接呼叫 `SfxBus`/`RaceFx`(唯一例外 `RaceFx.ItemBox`)。

### 最可能衝突的地方與解法
1. **名次 / 圈數算兩次**:03 的 AI 想知道排名、04 的 HUD 也要。→ 只有 `RaceSession.Place/Lap` 能算,都根據 `RaceTrack.Progress`;03 讀取不自己算。
2. **FOV、畫面震動被 JuiceFX 覆蓋** → §2 的 `BaseFovOverride` + `ActiveMode == 10`。
3. **FRC 音樂/群眾聲在賽車中響** → §4 的 `Juice.Tick` 分流。
4. **FRC 場地碰撞還在、座標重疊** → `Field`/`Arena` 根物件 SetActive(false),End 還原;不要把賽道疊在 FRC 場地上。
5. **物理設定被改沒還原**(`fixedDeltaTime`、`Physics.gravity`)→ 01 若要改,只能透過 `RaceSession` 在 Begin 存、End 還原。
6. **Layer / Tag**:不新增(需改 ProjectSettings),一律用元件判斷。
7. **材質爆量**:02 擺放時必須用 `RaceArt.Mat` / `RaceArt.Tree` / 合併網格。
8. **共用檔案多人改**:`GameSession.cs`、`MenuSystem.cs`、`CameraRig.cs`、`JuiceFX.cs`、`Juice.cs`、`SfxSynth.cs` 只由 04 的整合者修改,而且每處只加「`if (Racing)` 分流」的小段。

---

## 7. 實作階段分工與順序

| 里程碑 | 內容 | 負責 | 驗收 |
|---|---|---|---|
| **M0 骨架**(0.5 天) | `Race/` 資料夾、`GameSession.Racing` 分流、選單「賽車」項目、`RaceSession` 隱藏/還原 FRC 場地、平面草地 + 方塊車、`CameraRig` Mode 10 | 04 | 進出賽車 5 次不殘留物件;FRC 模式現有自動測試全部 PASS |
| **M1 最小可玩版本** | 一台車(Brick,01 基本油門/轉向/煞車)+ 一條賽道(02:封閉樣條路面帶 + 起終點 + 8 個檢查點)+ **一圈計時**;倒數 3-2-1-GO、計時 HUD、成績卡、最佳圈存 Prefs 毫秒、引擎聲 | 01 + 02 + 04 | 開始→倒數→跑一圈→看到成績;重開後最佳圈還在;跳檢查點不算圈 |
| **M2 手感** | 漂移 + 三段小加速、火花/噴焰、鏡頭外擺與 FOV、手把震動、3 圈、地形起伏 + 樹 + 雲 + 天空 | 01 + 02 + 04 | 60 FPS(1080p);漂移三色火花分得清楚 |
| **M3 對手** | 7 台 AI、名次 HUD、小地圖、車與車碰撞與撞擊特效 | 03 + 04 | AI 不卡牆跑完 3 圈;名次即時正確 |
| **M4 道具** | 道具箱、輪盤道具欄、4 種道具、被擊中反應 | 03 + 04 | 每種道具都有音效、特效、震動 |
| **M5 開放世界與打磨** | 自由探索模式(無比賽,地圖上散佈加速板/收集物)、多條賽道、4 款車選擇、起跑運鏡、終點環繞、配樂、`-race` 自動截圖測試、效能檢查 | 全員 | draw call < 300;FRC 模式與賽車模式交替進出無錯誤 log |

**順序理由:** M0 先把「接進去且不弄壞 FRC」證明完,後面所有人才能平行開發;M1 只做計時(不含 AI/道具),最快拿到可玩、可展示的版本;手感(M2)比對手和道具更影響好不好玩,所以排在前面。
