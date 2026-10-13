# 帳號/登入邏輯自動測試(Prefs.Isolated:只用記憶體,不寫登錄檔,使用者的模擬器開著也安全)。結果:persistentDataPath\accttest.txt
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe  = Join-Path $root 'Build\FRC9427-Sim\FRC9427-Sim.exe'
$res  = Join-Path $env:USERPROFILE 'AppData\LocalLow\FRC9427\FRC9427 Simulator\accttest.txt'
[IO.File]::Delete($res)
$p = Start-Process $exe -ArgumentList @('-accttest','-portoffset','263') -PassThru
$null = $p.WaitForExit(30000); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
if (Test-Path $res) { Get-Content $res } else { 'no result file' }
