# Repository Development & Release Rules

This file is supplemented by `AGENTS.md` which configures agent behavior modes (Caveman, Pony Tail, Karpathy, Graphify).

Whenever any changes are made to this repository, the following workflow MUST be strictly executed:

## 1. Version Bumping & Date Stamping
- Incremental version bump of **+0.01** (Starting baseline: `v0.01`).
- Embed version & current execution date (e.g., `v0.27 - 2026-07-25`) in:
  - `Kat34Scalper.cs` (Header comments, `VERSION` constant, `RELEASE_DATE` constant)
  - `README.md` (Current Version line)
  - `DIARY.md` (new version history entry)

## 2. Graphify & Project Diary Integration
- Run `graphify update .` at end of session.
- Maintain `DIARY.md` with:
  - Version history entry with timestamp.
  - Graphify entity mapping (Components, Dependencies, Actions, Data Flow).
- Apply Karpathy Guidelines: surgical minimal diffs, zero unnecessary abstractions, clear success criteria.

## 3. NinjaTrader 8 Deployment (MANDATORY FULL SYNC HARD RULE)
- MUST copy/deploy ALL source `.cs` files (`Kat34Scalper.cs` + all `src/*.cs` files) directly to NinjaTrader 8 custom indicators directory with force overwrite on every code change to prevent stale file compilation mismatches:
  - `Kat34Scalper.cs` -> `C:\Users\kieuanhtuan\Documents\NinjaTrader 8\bin\Custom\Indicators\Kat34Scalper.cs`
  - `src\Kat34ScalperLogic.cs` -> `C:\Users\kieuanhtuan\Documents\NinjaTrader 8\bin\Custom\Indicators\Kat34ScalperLogic.cs`
- Compile gate: `dotnet build tools/CompileCheck` must succeed (net48 + NT8 assemblies, mirrors NT8's internal Roslyn compile).
- Signal indicators live in INDEPENDENT sibling repos (no submodules): canonical A1 source is `../nt8-kat-A1-TradeBackground/Kat34Scalper.AlertSignal.A1.cs`; canonical StackEMA sources are `../nt8-kat-StackEMA/nt8-kat-StackEMA.cs` + `../nt8-kat-StackEMA/StackEmaLogic.cs`. Compile/deploy scripts include them directly by sibling path.
- Fresh checkout: run `pwsh scripts/connect-Repos.ps1` to verify the sibling A1 + StackEMA repos.

## 4. Git & GitHub Synchronization
- Stage all modified files (`git add .`).
- Commit with version & bump message (`git commit -m "vX.XX (YYYY-MM-DD): Description"`).
- Push directly to `origin main` on GitHub.
