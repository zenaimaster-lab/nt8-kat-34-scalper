# Run-AllChecks.ps1 — one-shot verification: xunit suite, then net48 compile gate.
# Exit 0 only when both pass. Usage:  pwsh scripts/Run-AllChecks.ps1

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host '=== 1/2: xunit suite ==='
dotnet test (Join-Path $repoRoot 'tests\Kat34Scalper.Tests') --nologo --verbosity quiet
$testsOk = ($LASTEXITCODE -eq 0)

Write-Host '=== 2/2: CompileCheck (net48 gate) ==='
dotnet build (Join-Path $repoRoot 'tools\CompileCheck') --nologo --verbosity quiet
$gateOk = ($LASTEXITCODE -eq 0)

if ($testsOk -and $gateOk) {
    Write-Host 'ALL CHECKS GREEN.'
    exit 0
}

if (-not $testsOk) { Write-Host 'FAILED: xunit suite' }
if (-not $gateOk)  { Write-Host 'FAILED: compile gate' }
exit 1
