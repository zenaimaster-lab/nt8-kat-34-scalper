# connect-A1.ps1 - initialize Scalper's pinned A1 submodule.
# Usage: pwsh scripts/connect-A1.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$gitmodules = Join-Path $repoRoot '.gitmodules'
$a1Path = Join-Path $repoRoot 'nt8-kat-A1-TradeBackground'
$a1Source = Join-Path $a1Path 'Kat34Scalper.AlertSignal.A1.cs'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git not found in PATH.'
}
if (-not (Test-Path -LiteralPath $gitmodules)) {
    throw "Missing .gitmodules: $gitmodules"
}

Push-Location $repoRoot
try {
    git submodule sync --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule sync failed.' }

    git submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule update failed.' }
} finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $a1Source)) {
    throw "A1 source missing after initialization: $a1Source"
}

$a1Commit = (& git -C $a1Path rev-parse HEAD).Trim()
$a1Remote = (& git -C $a1Path remote get-url origin 2>$null).Trim()
Write-Host 'A1 connected.'
Write-Host ("Path:   {0}" -f $a1Path)
Write-Host ("Commit: {0}" -f $a1Commit)
Write-Host ("Remote: {0}" -f $a1Remote)
Write-Host 'Scalper remains pinned to this A1 commit; no latest-version update performed.'
