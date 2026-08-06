# connect-Repos.ps1 - clone/verify the independent sibling signal repos (A1, StackEMA).
# No submodules: Scalper connects to them by sibling path only.
# Usage: pwsh scripts/connect-Repos.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$siblings = Split-Path -Parent $repoRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git not found in PATH.'
}

$repos = @(
    @{ Name = 'nt8-kat-A1-TradeBackground'; Url = 'https://github.com/zenaimaster-lab/nt8-kat-A1-TradeBackground.git' },
    @{ Name = 'nt8-kat-StackEMA';           Url = 'https://github.com/zenaimaster-lab/nt8-kat-StackEMA.git' }
)

foreach ($r in $repos) {
    $path = Join-Path $siblings $r.Name
    if (-not (Test-Path (Join-Path $path '.git'))) {
        Write-Host "cloning $($r.Name)..."
        git clone $r.Url $path
        if ($LASTEXITCODE -ne 0) { throw "git clone failed for $($r.Name)" }
    }
    $commit = (& git -C $path rev-parse HEAD).Trim()
    $remote = (& git -C $path remote get-url origin 2>$null).Trim()
    Write-Host "$($r.Name) connected."
    Write-Host ("  Path:   {0}" -f $path)
    Write-Host ("  Commit: {0}" -f $commit)
    Write-Host ("  Remote: {0}" -f $remote)
}
Write-Host 'Scalper connects to these sibling repos by path; no submodules are used.'
