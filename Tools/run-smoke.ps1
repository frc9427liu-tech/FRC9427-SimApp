# 一鍵煙霧測試:逐一開各個選單畫面與比賽畫面並截圖,檢查沒崩潰、截圖不是空白,輸出報告。
# 用法:powershell -File Tools\run-smoke.ps1   (結果:Tools\smoke\report.txt 與各畫面 png)
# 只用 -norealcode -portoffset 260,不會碰使用者正在跑的模擬器。
param([int]$PortOffset = 260)
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'Build\FRC9427-Sim\FRC9427-Sim.exe'
$out  = Join-Path $PSScriptRoot 'smoke'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.Drawing

$cases = @(
    @{ n = 'menu-setup';    a = @('-menu','setup') },
    @{ n = 'menu-settings'; a = @('-menu','settings') },
    @{ n = 'menu-modes';    a = @('-menu','modes') },
    @{ n = 'menu-controls'; a = @('-menu','controls') },
    @{ n = 'menu-catalog';  a = @('-menu','catalog') },
    @{ n = 'menu-models';   a = @('-menu','models') },
    @{ n = 'menu-presets';  a = @('-menu','presets') },
    @{ n = 'menu-about';    a = @('-menu','about') },
    @{ n = 'menu-pause';    a = @('-menu','pause') },
    @{ n = 'game-chase';    a = @('-autostart','-freeze','-camera','1') },
    @{ n = 'game-overview'; a = @('-autostart','-freeze','-camera','0') },
    @{ n = 'game-end';      a = @('-autostart','-clockstart','157','-camera','1') },
    @{ n = 'game-en';       a = @('-autostart','-freeze','-camera','1','-lang','en') }
)
$lines = @(); $fail = 0
foreach ($c in $cases) {
    $png = Join-Path $out ($c.n + '.png'); Remove-Item $png -ErrorAction SilentlyContinue
    $argv = @('-norealcode','-portoffset',$PortOffset) + $c.a + @('-shot',$png,'-shotdelay','8','-screen-width','1280','-screen-height','720','-screen-fullscreen','0')
    $p = Start-Process $exe -ArgumentList $argv -PassThru
    $null = $p.WaitForExit(22000)
    if (-not $p.HasExited) { try { Stop-Process -Id $p.Id -Force } catch {} }
    $ok = $false; $why = 'no screenshot'
    if (Test-Path $png) {
        $bmp = [System.Drawing.Bitmap]::FromFile($png); $vals = @()
        foreach ($x in 100,400,700,1000,1200) { foreach ($y in 100,300,500,650) { $vals += $bmp.GetPixel($x,$y).GetBrightness() } }
        $bmp.Dispose()
        $sd = [math]::Sqrt((($vals | ForEach-Object { ($_ - ($vals | Measure-Object -Average).Average) * ($_ - ($vals | Measure-Object -Average).Average) }) | Measure-Object -Average).Average)
        if ($sd -gt 0.02) { $ok = $true; $why = 'ok' } else { $why = 'blank/flat image' }
    }
    if (-not $ok) { $fail++ }
    $lines += ('{0,-14} {1}  {2}' -f $c.n, $(if ($ok) { 'PASS' } else { 'FAIL' }), $why)
}
$lines += "---`n失敗 $fail / $($cases.Count)"
$lines | Set-Content (Join-Path $out 'report.txt') -Encoding utf8
$lines



