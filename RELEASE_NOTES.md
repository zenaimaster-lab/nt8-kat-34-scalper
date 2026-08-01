# Release Notes — v0.04 (2026-08-01)

- **Root-cause fix for over-long lines**: `DrawSignal` passed `CurrentBar` (absolute bar index) into the `barsAgo` parameter of `Draw.Line`/`Draw.Arrow`/`Draw.Text`. NT8 measures `barsAgo` from the right edge, so every signal's anchors jumped to the chart start and lines spanned the whole chart. Anchors are now `0` (signal bar) with the line extending `Line Length (bars)` forward.
- **HUD panel** at top-center of the chart: `Xóa Line` button (clears all K8934_S_/K8934_B_ draw objects on the data thread, redraws the version label) and `Ẩn/Hiện` toggle.
- Verification: 9/9 xunit, CompileCheck 0 errors (CS0436 warnings vs the previously compiled copy in NinjaTrader.Custom.dll are expected — source wins).

# Release Notes — v0.03 (2026-08-01)

- Entry/SL/TP lines shortened: length configurable (`Line Length (bars)`, default 7), same for all three lines.
- New settings group `4. Lines & Text`: line length, line width, Entry/SL/TP line colors, Sell/Buy text colors (SELL/BUY label now drawn next to the entry line end instead of above the candle; arrow uses the same color).
- Color properties use the standard NT8 `Color` + hidden serializable-string pattern.
- Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.02 (2026-08-01)

- Indicator moved into the **KAT** folder in NT8's Add Indicator dialog via namespace `NinjaTrader.NinjaScript.Indicators.KAT`.
- No logic changes. Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.01 (2026-08-01)

Initial release of **Kat8934** — EMA 34/89 rejection signal indicator for NinjaTrader 8.

## Features
- 3 settings sections: `1. Chuẩn bị` (reserved, empty), `2. Sell Signal`, `3. Buy Signal`.
- Sell/Buy state machine (pure, testable): downtrend/uptrend context via Fast/Slow EMA; price touches/crosses Slow EMA, U-turns and closes back through the Fast EMA; configurable trigger mode:
  - `Retest Bounce` — signal on the later bar closing back through the Fast EMA (retest).
  - `Breakdown` — signal immediately on the U-turn close.
- Chart drawing per signal: text + arrow above the signal candle; dashed Entry (gold), SL (red), TP (green) lines with configurable tick offsets/distances.
- Version label top-left on chart (`Kat8934 vX.XX (date)`) — auto-updates after F5 recompile.
- All EMA periods and trigger modes configurable per side.

## Structure
- `Kat8934.cs` — indicator: lifecycle, signal evaluation, drawing, NinjaScript properties.
- `src/Kat8934Logic.cs` — pure state machine, zero NT8 dependencies (xunit-testable).
- `tests/Kat8934.Tests` — 9 unit tests.
- `tools/CompileCheck` — net48 compile gate mirroring NT8's Roslyn compile.
- `scripts/Deploy-NT8.ps1` / `scripts/Run-AllChecks.ps1`.

## Verification
| Layer | Result |
|---|---|
| xunit suite | 9/9 passed |
| CompileCheck (net48 gate) | 0 errors |
