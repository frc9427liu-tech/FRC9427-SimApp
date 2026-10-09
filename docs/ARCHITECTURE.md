# 架構說明(給想看懂或接手的人)

## 兩種執行模式
1. **內建行為**:Unity 內自己算底盤、機構、射擊(`SwerveDrive` / `RobotMechanisms` / `ShooterCalc`)。啟動快、穩定。
2. **真實機器人程式**:直接執行隊上的 WPILib Java 專案(`gradlew simulateJava`),Unity 當「虛擬硬體」。

## 真實程式模式的資料流
```
Java 機器人程式(WPILib simulateJava)
   │  HALSim WebSocket (ws://127.0.0.1:3300)           ← 搖桿、DS 狀態、馬達電壓
   │  本機 TCP 3399(SimAgent 推轉子位置/速度)         ← 低延遲的馬達回授
   ▼
Unity (HalSim.cs / MechSim.cs / NtSim.cs)
   · HalSim:WebSocket 收發,送虛擬搖桿(1~2 支)
   · SimAgent.java(-javaagent):在機器人 JVM 內用 Phoenix 6 SimState 同進程積分馬達物理,避開網路延遲
   · MechSim:把馬達轉速換成底盤速度(swerve:最小平方法;坦克:左右側平均)並推動 Unity 的車
   · RobotMechanisms:由飛輪/滾輪/手臂馬達狀態決定吸球、射擊(依 Sim\<專案>.mech.json 設定)
```
- `Tools/sim/*.mech.json`:每個機器人專案一份設定(馬達慣量/摩擦/限位、底盤型式、機構對應)。
- `Tools/sim/overlay/`:模擬專用的替身原始檔。例如 SparkMax 在部分 Windows 電腦會被「應用程式控制原則」擋掉 `REVLibDriver.dll`,所以在**工作副本**把 `DriveIOSparkMax` 換成 TalonFX 版本(原專案不會被改)。
- 專案在 OneDrive 或路徑含中文時,GradleRIO 不能 build → 自動複製到 `C:\FRC9427SimWork\<名稱>`。

## 場地
- 視覺:`Sim/field/Field2026.glb`(FIRST 官方 2026 REBUILT 場地 CAD,經 AdvantageScope 轉出)。載入後把程式蓋的方塊外觀隱藏。
- 物理:`FieldBuilder.cs` 的碰撞體,依官方圖面與場地模型逐項對照:HUB、TRENCH、BUMP(車身高度與傾斜由 `SwerveDrive` 依 BUMP 高度強制設定,所以機器人不和地板碰撞)、TOWER(兩根立柱)、DEPOT(貼地,無碰撞)。OUTPOST 在聯盟牆後面,場內無碰撞。
- FUEL 起始擺法直接取自場地模型內的預擺球位置。
- 傾印工具:`-dumpfield`(依名稱彙總模型零件外框,用來對照碰撞體)。

## 自動測試(皆加 `-batchmode -nographics`)
| 參數 | 內容 |
|---|---|
| `-padtest -norealcode` | 假手把輸入走一遍:坦克開車、轉向、Intake、發射、車身傾斜 |
| `-fieldtest` | 隧道、立柱、BUMP 通過與抬升 |
| `-walltest` | 貼牆、進入 TOWER |
| `-balltest` | 衝進球堆不被卡住 |
| `-soaktest 秒數` | 長時間隨機操作,記錄記憶體/球數/幀時間 |
| `-leotest <專案> -noclock` | LEO(坦克 + 兩支手把)真實程式(加 `-noclock`,不然前 20 秒是 AUTO,手把無效) |
| `-leoauto <專案>` | LEO 真實程式不碰手把,記錄 AUTO(20 秒)路徑/射擊,用來檢查與調整自動模式 |
| `-realtest <專案> -noclock` | swerve 真實程式(射擊、吃球) |
| `-portoffset N` | 連線埠偏移,可與已開著的模擬器同時測試 |

## 發佈與更新
- `Tools/publish-release.ps1`:打包完整 zip + **差異包** `delta-vX.zip`(只含改過的檔案)+ manifest,建立 GitHub Release。
- 程式啟動時查 GitHub 最新 Release;主畫面按 U → 下載差異包 → SHA-256 校驗 → 關閉 → `apply.ps1` 覆蓋檔案 → 重新開啟(`UpdateCheck.cs`)。

## 效能
- 幀率上限 30~144(預設 60,選單 30);物理 60Hz。
- 畫質:超取樣 100/125/150/200%(第二台相機貼圖,UI 在其後)、陰影開關、MSAA;連續 FPS 過低會自動降級;第一次啟動依 GPU 選預設(內顯 → 低)。
- `Tools/Set-High-Performance-GPU.bat`:筆電雙顯卡時讓 Windows 用高效能顯卡。
