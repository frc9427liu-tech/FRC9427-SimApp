@echo off
rem 一鍵:打包 -> 世界測試 -> 兩張截圖。雙擊執行(不受 Claude 記憶體保護機制影響)。結果寫在 Tools\build-and-test.log
setlocal
set P=C:\Users\frc94\FRC9427-SimApp
set U=C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe
set T=C:\Users\frc94\.claude\jobs\b85baca7\tmp
set L=%P%\Tools\build-and-test.log
echo start %date% %time% > "%L%"
taskkill /f /im FRC9427-Sim.exe >nul 2>&1
"%U%" -batchmode -quit -projectPath "%P%" -executeMethod BuildTool.BuildWindows -logFile "%T%\build-bat.log"
echo build exit %errorlevel% >> "%L%"
if not "%errorlevel%"=="0" goto done
if exist "%P%\Build\FRC9427-Sim\raceworldtest.txt" del "%P%\Build\FRC9427-Sim\raceworldtest.txt"
start /wait "" "%P%\Build\FRC9427-Sim\FRC9427-Sim.exe" -raceworldtest -batchmode -nographics
findstr /b /c:"RESULT" "%P%\Build\FRC9427-Sim\raceworldtest.txt" >> "%L%"
start "" "%P%\Build\FRC9427-Sim\FRC9427-Sim.exe" -autostart -race -raceauto -racetrack 3 -shot "%T%\bat-a.png" -shotdelay 42 -screen-width 1280 -screen-height 720 -screen-fullscreen 0
ping -n 70 127.0.0.1 >nul
start "" "%P%\Build\FRC9427-Sim\FRC9427-Sim.exe" -autostart -race -raceauto -racetrack 2 -shot "%T%\bat-b.png" -shotdelay 55 -screen-width 1280 -screen-height 720 -screen-fullscreen 0
ping -n 80 127.0.0.1 >nul
:done
echo done %date% %time% >> "%L%"
