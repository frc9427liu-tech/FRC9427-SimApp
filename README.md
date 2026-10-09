# FRC 9427 模擬器(FRC 2026 REBUILT)

FRC 9427 隊自製的 Unity 桌面模擬器:官方 2026 REBUILT 場地、FUEL 球物理、HUB 計分、可選機器人模型,並能直接執行機器人的 Java 程式(WPILib HALSim)。

## 下載(Windows)
到 [Releases](../../releases/latest) 下載最新的 `FRC9427-Sim-win64-vX.Y.Z.zip` → 解壓縮 → 雙擊 `FRC9427-Sim.exe`。
筆電有獨顯(如 RTX)的話,先雙擊 `Set-High-Performance-GPU.bat` 一次。
操作方式見 zip 內的 `使用說明.txt`。支援 1~2 支 Xbox 手把。程式啟動時會檢查有沒有新版。

## 從原始碼建置
- Unity 6000.6.3f1,專案根目錄直接開啟。
- 命令列打包:`Unity.exe -batchmode -nographics -quit -projectPath <本資料夾> -executeMethod BuildTool.BuildWindows`(輸出在 `Build/`)。
- 自動測試參數:`-padtest -fieldtest -balltest -walltest -leotest <專案資料夾>`(加 `-batchmode -nographics`)。

## 授權與素材
- 場地模型:FIRST 官方 2026 REBUILT 場地 CAD(經 AdvantageScope 轉出的 glb)。
- `ImportSamples/` 內的機器人模型為各隊公開素材,版權屬原作者。
- 本專案是學生專案,與 FIRST 無關聯。