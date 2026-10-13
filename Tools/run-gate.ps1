# 發版關卡:依序跑自動測試,任何一項沒有 PASS 或時間戳不新鮮就失敗(exit 1)。publish-release.ps1 會呼叫它。
# 用法:powershell -File Tools\run-gate.ps1 [-NoSmoke]   (結果摘要寫到 Tools\gate-report.txt;發版說明「測過」可直接引用)
param([switch]$NoSmoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$b    = Join-Path $root 'Build\FRC9427-Sim'
$exe  = Join-Path $b 'FRC9427-Sim.exe'
$rep  = @(); $fail = 0
function Run-Test($name, $argv, $out, $timeoutMs) {
    $path = Join-Path $b $out
    if (Test-Path $path) { [System.IO.File]::Delete($path) }
    $p = Start-Process $exe -ArgumentList $argv -PassThru
    if (-not $p.WaitForExit($timeoutMs)) { try { Stop-Process -Id $p.Id -Force } catch {} }
    $ok = $false; $why = '沒有輸出檔'
    if (Test-Path $path) {
        $txt = Get-Content $path -Raw -Encoding UTF8
        if ($txt -match '(?m)^FAIL\b|\bFAIL\b') { $why = '含 FAIL' }
        elseif ($txt -match '\bPASS\b') { $ok = $true; $why = 'PASS' }
        else { $why = '沒有 PASS 判定' }
    }
    if (-not $ok) { $script:fail++ }
    $script:rep += ('{0,-14} {1}  {2}' -f $name, $(if ($ok) { 'PASS' } else { 'FAIL' }), $why)
    Write-Host $script:rep[-1]
}
Run-Test 'drivetest'     @('-drivetest','-norealcode','-batchmode','-nographics','-portoffset','311') 'drivetest.txt'     90000
Run-Test 'intaketest'    @('-intaketest','-norealcode','-batchmode','-nographics','-portoffset','311') 'intaketest.txt'    90000
Run-Test 'padtest'       @('-padtest','-norealcode','-batchmode','-nographics','-portoffset','311')    'padtest.txt'       90000
Run-Test 'carswitchtest' @('-carswitchtest','-menu','picker','-norealcode','-portoffset','313','-screen-width','960','-screen-height','540','-screen-fullscreen','0') 'carswitchtest.txt' 170000
if (-not $NoSmoke) {
    $sm = Join-Path $PSScriptRoot 'run-smoke.ps1'
    & powershell -NoProfile -File $sm | Out-Null
    $r = Join-Path $PSScriptRoot 'smoke\report.txt'
    $line = if (Test-Path $r) { (Get-Content $r -Encoding UTF8 | Select-Object -Last 1) } else { '' }
    $smokeOk = (Test-Path $r) -and (((Get-Item $r).LastWriteTime) -gt (Get-Date).AddMinutes(-15)) -and ($line -match '0\s*/\s*\d+$' -or $line -match '\s0 /')
    if (-not $smokeOk) { $fail++ }
    $rep += ('{0,-14} {1}  {2}' -f 'smoke', $(if ($smokeOk) { 'PASS' } else { 'FAIL' }), $line)
    Write-Host $rep[-1]
}
$rep += "---`n失敗 $fail 項  ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"
$rep | Set-Content (Join-Path $PSScriptRoot 'gate-report.txt') -Encoding utf8
if ($fail -gt 0) { Write-Host "關卡未過:$fail 項失敗,拒絕發版。" -ForegroundColor Red; exit 1 }
Write-Host '關卡全過。' -ForegroundColor Green
exit 0
