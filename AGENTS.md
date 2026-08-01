# AGENTS.md — nt8-kat-8934

## Caveman Mode — ULTRA
- Respond terse like smart caveman. All technical substance, no fluff.
- Drop: articles (a/an/the), filler (just/really/basically), pleasantries, hedging.
- Fragments OK. Short synonyms. Technical terms exact. Code unchanged.
- Preserve user's language. Reply in same language.
- Code/commits/PRs written normal (not caveman).

## Pony Tail — Full
- Laziest solution that works. YAGNI first.
- Stdlib before custom code. Native before dependency.
- One line before fifty. No speculative abstractions.
- Mark intentional shortcuts with `ponytail:` comment naming the ceiling + upgrade path.
- Deletion over addition. Boring over clever. Fewest files.

## Karpathy Guidelines
- Think before coding: state assumptions, ask if uncertain, present multiple interpretations.
- Simplicity first: minimum code, nothing speculative, no features beyond asked.
- Surgical changes: touch only what must change, match existing style, remove YOUR orphans.
- Goal-driven: define success criteria before starting, loop until verified.

## Graphify Best Practices
- Read graphify-out/GRAPH_REPORT.md before answering codebase questions.
- Navigate graphify-out/wiki/ for module context instead of raw files.
- Run `graphify update .` at END of session or after significant milestone.
- AST-only updates between major passes (zero token cost).
- Respect .gitignore — excludes node_modules, venv, __pycache__, logs/, .git.

## Auto GitHub Connection
- Remote: https://github.com/zenaimaster-lab/nt8-kat-8934.git (origin/main).
- All changes commit + push to origin main.
- Use `gh` for PRs/issues if needed.

## Version Bump Workflow (MANDATORY)
On every code change, BEFORE closing session:

1. **Bump version** +0.01 from current (baseline v0.01).
2. **Stamp date** in format YYYY-MM-DD.
3. **Update all locations**:
   - `Kat8934.cs`: header comment + `VERSION` + `RELEASE_DATE` constants
   - `README.md`: "Current Version" line
   - `DIARY.md`: new version history entry
4. **Update Graphify**: run `graphify update .`
5. **Update Diary**: add entry with timestamp, changes summary, Graphify entity mapping.
6. **Deploy NT8 (MANDATORY FULL SYNC)**: copy ALL source `.cs` files (`Kat8934.cs` AND `src/*.cs`) to `C:\Users\kieuanhtuan\Documents\NinjaTrader 8\bin\Custom\Indicators\` with force overwrite (`scripts\Deploy-NT8.ps1` does this + verifies recompile):
   - `Kat8934.cs`
   - `src\Kat8934Logic.cs`
7. **Git sync**:
   - `git add .`
   - `git commit -m "vX.XX (YYYY-MM-DD): Description"`
   - `git push origin main`

## Verification Layers (run `pwsh scripts/Run-AllChecks.ps1`)
1. xunit suite: `dotnet test tests\Kat8934.Tests` — pure logic `src\Kat8934Logic.cs`.
2. Compile gate: `dotnet build tools\CompileCheck` — net48 + NT8 assemblies, mirrors NT8 Roslyn compile.
3. Live NT8 recompile after deploy (Deploy-NT8.ps1 checks NinjaTrader.Custom.dll timestamp).

## Version Tracking
- Code versions: Kat8934.cs VERSION constant
- Doc versions: README.md, DIARY.md
- **Current: v0.06 (2026-08-01)**
