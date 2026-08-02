# Deploy-NT8.ps1 — copy ALL Kat34Scalper sources into NT8's Indicators folder with overwrite,
# then verify NT8's file watcher recompiled NinjaTrader.Custom.dll (timestamp newer than deploy).
# Usage:  pwsh scripts/Deploy-NT8.ps1 [-TimeoutSeconds 60] [-FailOnMissingRecompile]
param(
    [int]$TimeoutSeconds = 60,
    [switch]$FailOnMissingRecompile
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$indicators = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\bin\Custom\Indicators'
$customDll  = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Custom.dll'

# Main file + every module under src\ (Logic, Signal, Filter, Bot, Draw, ...).
$files = @('Kat34Scalper.cs') + (Get-ChildItem (Join-Path $repoRoot 'src') -Filter '*.cs' |
    ForEach-Object { 'src\' + $_.Name })

# Legacy cleanup after the Kat8934 -> Kat34Scalper rename (v0.20): stale files would keep
# the OLD indicator alive next to the new one inside NT8's single NinjaScript assembly.
Get-ChildItem $indicators -Filter 'Kat8934*.cs' -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force
    Write-Host "removed legacy: $($_.Name)"
}

$deployTime = Get-Date
foreach ($f in $files) {
    $src = Join-Path $repoRoot $f
    if (-not (Test-Path $src)) { throw "Missing source: $f" }
    Copy-Item $src (Join-Path $indicators (Split-Path $f -Leaf)) -Force
    Write-Host "deployed: $f"
}

# NT8 recompiles automatically when NinjaTrader is running. A newer dll = accepted; older = rejected
# (open NinjaScript Editor for errors). Skip wait silently when NT8 is not running.
$ntRunning = Get-Process -Name 'NinjaTrader' -ErrorAction SilentlyContinue
if (-not $ntRunning) {
    Write-Host 'NinjaTrader not running — files deployed; recompile happens on next start.'
    exit 0
}

$deadline = $deployTime.AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $customDll) -and (Get-Item $customDll).LastWriteTime -gt $deployTime) {
        Write-Host 'OK: NinjaTrader.Custom.dll recompiled — deploy accepted.'
        exit 0
    }
    Start-Sleep -Seconds 2
}
Write-Host 'WARNING: NinjaTrader.Custom.dll not recompiled within timeout.'
if (Test-Path $customDll) {
    Write-Host ("Current NinjaTrader.Custom.dll timestamp: {0}" -f (Get-Item $customDll).LastWriteTime)
}

$traceDir = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\trace'
$logDir = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\log'
$pattern = 'ERROR:|Failed to restore Indicator|compile|Compile|recompiled'

function Show-LatestDiagnosticLines([string]$dirPath, [string]$filter, [string]$title) {
    if (-not (Test-Path $dirPath)) { return }
    $latest = Get-ChildItem $dirPath -Filter $filter -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $latest) { return }
    $hits = Select-String -Path $latest.FullName -Pattern $pattern -ErrorAction SilentlyContinue
    if ($null -ne $hits -and $hits.Count -gt 0) {
        $hits = $hits | Where-Object { $_.Line -notmatch 'HotKeys' }
    }
    if ($null -ne $hits -and $hits.Count -gt 0) {
        Write-Host ("{0} ({1}):" -f $title, $latest.Name)
        $hits | Select-Object -Last 10 | ForEach-Object { Write-Host ("  " + $_.Line) }
    }
}

Show-LatestDiagnosticLines -dirPath $traceDir -filter 'trace.*.txt' -title 'Recent trace hints'
Show-LatestDiagnosticLines -dirPath $logDir -filter 'log.*.txt' -title 'Recent log hints'

Write-Host 'Hint: open NinjaScript Editor and press F5 (Reload NinjaScript), then rerun this deploy script.'
if ($FailOnMissingRecompile) {
    exit 1
}

Write-Host 'Deploy sync completed, but recompile could not be verified automatically.'
exit 0
exit 1
