$ErrorActionPreference = 'Continue'
$logPath = Join-Path $env:TEMP 'many_face_runtime_lfs_restore.log'
"[$(Get-Date -Format s)] Runtime LFS restore started" | Set-Content -LiteralPath $logPath -Encoding UTF8
$paths = @(git lfs ls-files -n)
$pending = @(
    $paths | Where-Object {
        ($_ -like 'Assets/Resources/CatCafe/*' -or $_ -like 'Assets/Resources/Fonts/*') -and
        (Test-Path -LiteralPath $_) -and ((Get-Item -LiteralPath $_).Length -lt 1000)
    }
)
"[$(Get-Date -Format s)] Pending runtime files: $($pending.Count)" | Add-Content -LiteralPath $logPath
$index = 0
foreach ($path in $pending) {
    $index++
    "[$(Get-Date -Format s)] [$index/$($pending.Count)] Fetching $path" | Add-Content -LiteralPath $logPath
    git -c lfs.concurrenttransfers=1 -c lfs.dialtimeout=20 -c lfs.tlstimeout=60 -c lfs.activitytimeout=120 lfs fetch origin main --include="$path" 2>&1 | Add-Content -LiteralPath $logPath
    if ($LASTEXITCODE -eq 0) {
        git lfs checkout -- "$path" 2>&1 | Add-Content -LiteralPath $logPath
    } else {
        "[$(Get-Date -Format s)] FAILED exit=$LASTEXITCODE path=$path" | Add-Content -LiteralPath $logPath
    }
}
"[$(Get-Date -Format s)] Runtime LFS restore finished" | Add-Content -LiteralPath $logPath
