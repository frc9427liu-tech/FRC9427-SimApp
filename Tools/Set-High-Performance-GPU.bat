@echo off
REM Use the high-performance GPU (e.g. RTX) for FRC9427-Sim. Run once, then restart the simulator.
REM Windows Settings > System > Display > Graphics > add FRC9427-Sim.exe > Options > High performance does the same thing.
reg add "HKCU\Software\Microsoft\DirectX\UserGpuPreferences" /v "%~dp0FRC9427-Sim.exe" /t REG_SZ /d "GpuPreference=2;" /f
echo.
echo Done. Close the simulator if it is open, then start FRC9427-Sim.exe again.
pause