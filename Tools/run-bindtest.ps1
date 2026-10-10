# 自動驗收:把機器人專案 RobotContainer 的每個按鍵綁定都按一遍,看模擬器有沒有可見效果(PASS/FAIL)
# 用法: powershell -File Tools\run-bindtest.ps1 -Project C:\路徑\LEO
param([Parameter(Mandatory=$true)][string]$Project, [string]$Exe = "")
if ($Exe -eq "") { $Exe = (Resolve-Path (Join-Path $PSScriptRoot "..\Build\FRC9427-Sim\FRC9427-Sim.exe")).Path }
$dir = Split-Path $Exe; $out = Join-Path $dir "bindtest.txt"; Remove-Item $out -ErrorAction SilentlyContinue
Start-Process $Exe -ArgumentList '-bindtest', $Project, '-batchmode', '-nographics', '-portoffset', '140'
for ($i = 0; $i -lt 60 -and -not (Test-Path $out); $i++) { Start-Sleep 3 }
if (Test-Path $out) { Get-Content $out -Encoding UTF8 } else { "bindtest 沒有產生結果(逾時)" }