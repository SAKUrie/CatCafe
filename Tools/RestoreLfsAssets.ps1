$ErrorActionPreference = 'Continue'
$projectRoot = (Get-Location).Path
$logPath = Join-Path $env:TEMP 'many_face_lfs_restore.log'

"[$(Get-Date -Format s)] LFS restore started" | Set-Content -LiteralPath $logPath -Encoding UTF8
$paths = @(git lfs ls-files -n)
$pending = @(
    $paths | Where-Object {
        (Test-Path -LiteralPath $_) -and ((Get-Item -LiteralPath $_).Length -lt 1000)
    }
)
"[$(Get-Date -Format s)] Pending pointer files: $($pending.Count)" | Add-Content -LiteralPath $logPath

$index = 0
foreach ($path in $pending) {
    $index++
    "[$(Get-Date -Format s)] [$index/$($pending.Count)] Fetching $path" | Add-Content -LiteralPath $logPath
    git -c lfs.concurrenttransfers=1 -c lfs.dialtimeout=20 -c lfs.tlstimeout=60 -c lfs.activitytimeout=120 lfs fetch origin main --include="$path" 2>&1 | Add-Content -LiteralPath $logPath
    if ($LASTEXITCODE -eq 0) {
        git lfs checkout -- "$path" 2>&1 | Add-Content -LiteralPath $logPath
    } else {
        "[$(Get-Date -Format s)] FAILED fetch exit=$LASTEXITCODE path=$path" | Add-Content -LiteralPath $logPath
    }
}

$remaining = @(
    $paths | Where-Object {
        (Test-Path -LiteralPath $_) -and ((Get-Item -LiteralPath $_).Length -lt 1000)
    }
)
"[$(Get-Date -Format s)] LFS restore finished. Remaining pointer files: $($remaining.Count)" | Add-Content -LiteralPath $logPath
$remaining | Add-Content -LiteralPath $logPath
