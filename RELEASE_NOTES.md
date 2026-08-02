# Release Notes — v0.23 (2026-08-02)

- **Default ATM = MNQ 1ct template**: `ATM Template` now defaults to `mnq. 1ct. 15-be20-35move15-50triggertrail5step1`, so every signal draws the ATM's entry/SL 60/TP 120 lines plus BE (dash-dot DeepSkyBlue) and trail trigger lines out of the box. Missing template file falls back to the settings distances (same 60/120).
- **Stale-deploy compile errors fixed**: NT8's Indicators folder still held the pre-split monolith next to the new partial-class modules (CS0111/CS0102/CS0121/CS0229 collisions). Full redeploy of the current sources resolves it — press F5 in NinjaScript Editor to recompile.
- Verification: 35/35 xunit, CompileCheck 0 errors.

# Release Notes — v0.22 (2026-08-02)

- **Every filter gate OFF by default**: `FanFilterEnabled` and all HUD filter toggles (MTF/ADX/Volume/Time) start OFF — A1 fires on trend alone out of the box.
- **Arrow outline pass**: buy = white up-arrow with black outline below the candle, sell = black down-arrow with white outline above (outline drawn 1 tick beyond the candle edge).
- Verification: 35/35 xunit, CompileCheck 0 errors.

# Release Notes — v0.21 (2026-08-02)

- **Bot cancel-account safety**: pending entry cancel now uses owner account captured at submit time, avoiding cancel misses after changing HUD account selection.
- **HUD ATM sync fix**: when saved template is missing, HUD fallback selection (`None`) now syncs back to runtime state immediately (no stale template execution path).
- **Deploy script hardening**: `scripts/Deploy-NT8.ps1` now includes focused timeout diagnostics and optional strict mode (`-FailOnMissingRecompile`) so local sync can complete while still surfacing root-cause hints when NT8 does not auto-recompile.
- **Tests expanded**: added boundary tests for time-window start/end semantics and trigger==market stop/limit decision.
- Verification: 35/35 xunit, CompileCheck 0 errors.

# Release Notes — v0.20 (2026-08-02)

- **Renamed Kat8934 → Kat 34 Scalper** everywhere: code, files, indicator name, HUD, repo folder, GitHub repo. NT8 sees it as a new indicator — re-add it on charts that used Kat8934.

# Release Notes — v0.10 (2026-08-01)

- **Text column bug fixed**: the Text toggle redrew labels with `barsAgo` relative to the current bar — for old signals that anchored near the right edge, stacking all labels into a column outside the chart. Redraws now compute `barsAgo = CurrentBars[0] - r.Bar` so every object lands back on its signal candle. (Root cause confirmed by metadata probe: NT8 `Draw.Text` has no simple `DateTime` overload — only a 13-arg one with font/alignment.)
- **Hide button removed** from the HUD (now: Clear, Arrow, Text).
- **Arrow distance setting**: `Arrow Offset (ticks)` (default 3) moves the arrow away from the candle.
- **Sell/Buy settings merged** into one `2. Signal` group — both directions share the same mirrored mechanism (Enabled, Fast/Slow EMA, Trigger Mode, Entry Offset, Stop, Target).
- Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.09 (2026-08-01)

- **Instant HUD reaction fix**: toggles previously queued a pending flag consumed on the next `OnBarUpdate` (next bar close) — so buttons appeared to only affect future signals. All HUD buttons now marshal directly to the data thread via `Dispatcher.InvokeAsync` and apply immediately (Clear removes drawings now, Arrow/Text toggles redraw/remove instantly).
- **Bigger arrows**: NT8 `Draw.Arrow*` has no size parameter (verified in the 8.1.9 assemblies) — arrows are now drawn twice, 1 tick apart, rendering a visually ~2x marker. Buy arrows are white, Sell arrows black.
- Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.08 (2026-08-01)

- **HUD toggles now react on screen**: the Arrow/Text toggles previously only affected future signals. The indicator now records drawn signals (max 200) and the toggles immediately remove or redraw arrows/labels on all already-drawn signals (processed on the data thread via a pending bitmask).
- **UI translated to English**: `Clear`, `Arrow: ON/OFF`, `Text: ON/OFF`, `Hide/Show`; Vietnamese comments/section names replaced.
- Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.07 (2026-08-01)

- Entry lines are now **solid** with per-side colors: Sell entry = bright red, Buy entry = bright lime green (settings `Sell/Buy Entry Line Color` replace the shared gold `Entry Line Color`). SL/TP stay dashed.
- BUY/SELL labels: bright colors (Buy lime green, Sell red), Buy label below the entry line end, Sell above; **default off** (`Show Buy/Sell Labels` = false).
- HUD gained 2 toggle buttons: **Mũi tên: ON/OFF** (arrows) and **Chữ: ON/OFF** (labels) — blue when ON, gray when OFF, write through to the persisted settings.
- Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.06 (2026-08-01)

- **HUD layout squeeze fix**: the HUD was added to `ChartControl.Children`; ChartControl is the grid that lays out the price panel, so a direct child forced empty side gaps and squeezed the chart to the middle. The HUD now attaches to the outer grid (`ChartControl.Parent`) like the KatTradeManager HUD, restoring the full-width chart.
- Verification: 9/9 xunit, CompileCheck 0 errors.

# Release Notes — v0.05 (2026-08-01)

- HUD restyled to mirror the KatTradeManager HUD (graphics + position only, no new buttons/features):
  - Panel: dark navy `Argb(240,20,24,33)`, slate border `Rgb(35,42,56)` 1px, corner radius 6, padding 8.
  - Buttons: borderless, white foreground, height 24, padding 2; Xóa Line dark `Rgb(20,20,20)` (matches the Close/flatten style), Ẩn/Hiện OFF-gray `Rgb(45,50,65)`.
  - Position: bottom-left of the chart with 10px left inset (KatTradeManager InChart placement).
- Verification: 9/9 xunit, CompileCheck 0 errors.

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
