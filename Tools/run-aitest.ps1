# AI 難度回歸測試:各跑 70 秒(-vsai N -aitest),讀紅方分數,檢查門檻與難度單調性。輸出 PASS/FAIL,失敗 exit 1。
$root = Split-Path $PSScriptRoot -Parent
$b = Join-Path $root 'Build\FRC9427-Sim'; $exe = Join-Path $b 'FRC9427-Sim.exe'
$min = @(20, 20, 50, 90); $sc = @(0,0,0,0); $fail = 0
foreach ($l in 0..3) {
    $f = Join-Path $b "aitest-L$l.txt"; if (Test-Path $f) { [System.IO.File]::Delete($f) }
    $p = Start-Process $exe -ArgumentList @('-norealcode','-portoffset','317','-autostart','-vsai',"$l",'-batchmode','-nographics','-aitest','-aitestsec','70') -PassThru
    if (-not $p.WaitForExit(120000)) { try { Stop-Process -Id $p.Id -Force } catch {} }
    if (Test-Path $f) { $last = (Get-Content $f)[-1]; if ($last -match 'red=(\d+)') { $sc[$l] = [int]$Matches[1] } }
    $ok = $sc[$l] -ge $min[$l]; if (-not $ok) { $fail++ }
    Write-Host ("L{0} red={1} (>= {2}) {3}" -f $l, $sc[$l], $min[$l], $(if ($ok) { 'PASS' } else { 'FAIL' }))
}
$mono = ($sc[3] -gt $sc[1] * 1.5) -and ($sc[2] -gt $sc[0])
if (-not $mono) { $fail++ }
Write-Host ("難度單調性 L3 > L1×1.5 且 L2 > L0: {0}" -f $(if ($mono) { 'PASS' } else { 'FAIL' }))
if ($fail -gt 0) { Write-Host "AI 測試失敗 $fail 項" -ForegroundColor Red; exit 1 }
Write-Host 'AI 測試全過。' -ForegroundColor Green; exit 0