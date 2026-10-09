# 發新版用:打包完整版 zip + 差異更新包 delta-vX.zip(只含改過的檔案)+ manifest,並建立 GitHub Release。
# 用法:powershell -File Tools\publish-release.ps1 -Version 0.1.4 -Notes "更新內容"
# 前提:已用 BuildTool 打包好 Build\FRC9427-Sim,且 Assets\Scripts\Sim\UpdateCheck.cs 的 Current 已改成這個版本號。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Notes,
    [switch]$NoRelease
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root  = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'Build\FRC9427-Sim'
$work  = Join-Path $env:TEMP "frcsim-release-$Version"
$stage = Join-Path $work 'FRC9427-Sim'
$relDir = Join-Path $root 'Tools\releases'
if (Test-Path $work) { [System.IO.Directory]::Delete($work, $true) }
New-Item -ItemType Directory -Force $stage | Out-Null
New-Item -ItemType Directory -Force $relDir | Out-Null

# 版本號要一致,否則程式會一直以為有新版
$cs = Get-Content (Join-Path $root 'Assets\Scripts\Sim\UpdateCheck.cs') -Raw
if ($cs -notmatch "Current = `"$([regex]::Escape($Version))`"") { throw "UpdateCheck.cs 的 Current 不是 $Version,先改好再重新打包。" }

# 1. 暫存要發佈的檔案(排除測試輸出與 log)
robocopy $build $stage /E /XF padtest.txt selftest.txt leotest.txt halsimtest.txt realtest.txt fieldtest.txt balltest.txt walltest.txt outpostdump.txt *.log /XD logs _update /NFL /NDL /NJH /NJS | Out-Null
Copy-Item (Join-Path $root 'Tools\使用說明.txt') (Join-Path $stage '使用說明.txt') -Force

# 2. 這一版的 manifest
$files = Get-ChildItem $stage -Recurse -File | ForEach-Object {
    [pscustomobject]@{ p = $_.FullName.Substring($stage.Length + 1).Replace('\', '/'); h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(); s = $_.Length }
}
$manifest = @{ version = $Version; files = @($files) }

# 3. 找上一版 manifest(版本號比這版小的最大者)
$prev = Get-ChildItem $relDir -Filter 'manifest-*.json' | ForEach-Object {
    $v = $_.BaseName.Substring('manifest-'.Length); [pscustomobject]@{ v = [version]$v; f = $_.FullName }
} | Where-Object { $_.v -lt [version]$Version } | Sort-Object v | Select-Object -Last 1
$prevMap = @{}
if ($prev) { foreach ($f in (Get-Content $prev.f -Raw | ConvertFrom-Json).files) { $prevMap[$f.p] = $f.h } }
$changed = @($files | Where-Object { -not $prevMap.ContainsKey($_.p) -or $prevMap[$_.p] -ne $_.h })
$newSet = @{}; foreach ($f in $files) { $newSet[$f.p] = 1 }
$deleted = @($prevMap.Keys | Where-Object { -not $newSet.ContainsKey($_) })
Write-Host "上一版: $(if ($prev) { $prev.v } else { '無(全部視為新檔)' });改動 $($changed.Count) 個、刪除 $($deleted.Count) 個檔案"

function New-Zip($zipPath, $entries, $extra) {
    if (Test-Path $zipPath) { [System.IO.File]::Delete($zipPath) }
    $za = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($e in $entries) { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $e.src, $e.name, 'Optimal') }
        foreach ($kv in $extra.GetEnumerator()) {
            $en = $za.CreateEntry($kv.Key, 'Optimal'); $sw = New-Object System.IO.StreamWriter($en.Open(), (New-Object System.Text.UTF8Encoding $false)); $sw.Write($kv.Value); $sw.Dispose()
        }
    } finally { $za.Dispose() }
}

# 4. 差異包:改動的檔案 + _manifest.json + _delete.txt
$deltaZip = Join-Path $work "delta-v$Version.zip"
$entries = @($changed | ForEach-Object { @{ src = (Join-Path $stage ($_.p.Replace('/', '\'))); name = $_.p } })
New-Zip $deltaZip $entries @{ '_manifest.json' = ($manifest | ConvertTo-Json -Depth 4 -Compress); '_delete.txt' = ($deleted -join "`n") }

# 5. 完整版 zip
$fullZip = Join-Path $work "FRC9427-Sim-win64-v$Version.zip"
$entries = @($files | ForEach-Object { @{ src = (Join-Path $stage ($_.p.Replace('/', '\'))); name = "FRC9427-Sim/$($_.p)" } })
New-Zip $fullZip $entries @{}
Write-Host ("完整版 {0:0.0} MB;差異包 {1:0.00} MB" -f ((Get-Item $fullZip).Length / 1MB), ((Get-Item $deltaZip).Length / 1MB))

# 6. 存 manifest(下一版比對用)並提交
$manifest | ConvertTo-Json -Depth 4 -Compress | Set-Content (Join-Path $relDir "manifest-$Version.json") -Encoding utf8
if ($NoRelease) { Write-Host "NoRelease:產物在 $work"; return }
git -C $root add Tools/releases "Tools/publish-release.ps1" 2>$null
git -C $root commit -q -m "Release manifest v$Version" 2>$null
git -C $root push -q 2>$null

# 7. 建立 Release
gh release create "v$Version" $fullZip $deltaZip -R frc9427liu-tech/FRC9427-SimApp --title "FRC 9427 模擬器 v$Version (Windows)" --notes $Notes --target unity-sim
