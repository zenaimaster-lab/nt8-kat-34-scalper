# Release Notes — v0.36 (2026-08-02)

- **A2 self-diagnostics**: a silent A2 now explains itself in NinjaScript Output. New `[Kat34Scalper][A2][GATE]` print on every trend-stack transition (live bar): buyTrend/sellTrend, active-pending flags and the raw e8/e34/e89/e144/e200 values. `SetA2Signal` prints the toggle. The backfill summary now reports replay counters — `backfill done — N day(s), M bar(s) replayed: X entries, Y cancels, Z fills` — so an empty chart is immediately distinguishable: 0 entries (no valid setup in the window) vs entries>0 with cancels catching up (setups died on closes beyond ema34 / trend-stack flips, drawings removed per spec).
- Verification: 47/47 xunit, CompileCheck 0 errors.

# Release Notes — v0.35 (2026-08-02)

- **A2 missing Entry/SL/TP lines fixed**: `RenderSignal` caps line rendering at `Line Length` (7) bars of age, but the A2 label draws permanently — so any A2 pending entry older than 7 bars (or replayed from the History Days backfill) showed `Buy A2` text with no lines. New `KatSignalRecord.KeepAlive` flag: A2 sets it on NewEntry and clears it on Filled; while set, the entry/SL/TP lines render from the entry candle to the current bar regardless of age. Cancel removes the setup outright; filled setups fade normally.
- Verification: 47/47 xunit, CompileCheck 0 errors.

# Release Notes — v0.34 (2026-08-02)

- **New signal sub-module A2 (34+8+Bounce)**: pending stop entry on an EMA-34 bounce inside a stacked trend. BUY: EMA 8 above/touching EMA 34 (no cross down) + EMA 34 > 89 > 144 > 200 — every condition individually toggleable in the new settings group `3.5 Signal A2 — 34+8+Bounce`; SELL mirrored. Price pulls back, touches EMA 34 (wick) and closes above it → pending stop LONG at the touch candle's high (+ Entry Offset); later touch candles with lower highs migrate the entry down; a close below EMA 34 (or trend loss) cancels it; reaching the trigger marks it filled. No stage markers — entry/SL/TP lines + `Buy A2`/`Sell A2` label at the entry candle. No filters gate A2 yet.
- **Bot owner tracking**: each pending order now records its signal owner + entry offset (`pendingOrderOwner`/`pendingOffsetTicks`); A2 cancels only its own order and the bot entry price uses the calling signal's offset (order now matches the drawn entry line for every signal).
- **Draw pipeline**: `DrawSignal` returns the created record; level math extracted into `FillSignalRecord` (shared with A2 migration); new `RemoveSignalRecord(Drawings)` helpers; `RenderSignal` gates per owner (A1/A2). HUD SIGNAL section: real `A2 34+8` toggle replaces the disabled `A2…` placeholder.
- **Backtest verification**: 3 synthetic-series replay tests through the full A2 state machine (buy run touch→migrate→fill, cancel→re-entry→fill, sell trend-loss cancel→re-entry→migrate→fill) assert exact per-bar action sequences; 12 unit tests cover every A2 action/edge both sides.
- Verification: 47/47 xunit, CompileCheck 0 errors (CS0436 warnings vs NinjaTrader.Custom.dll expected).

# Release Notes — v0.29 (2026-08-02)

- **A0 signal/filter separation**: the SIGNAL `A0 fan` toggle now controls only A0 triangle/alert output. A0 direction is still calculated independently for A1 evaluation.
- **A1-only A0 Fan Filter**: moved the `A0 Fan` toggle into the HUD FILTER section and made `FanFilterEnabled` gate A1 only; disabling the A0 signal no longer disables A1 fan filtering.
- **Filter defaults**: A0 Fan Filter, MTF, ADX, Volume, and Time gates remain OFF by default.
- Verification: 35/35 xunit, CompileCheck 0 errors, NT8 deployment/recompile accepted, Graphify updated.

# Release Notes — v0.28 (2026-08-02)

- **A1 setup progression fixed**: A1 state machines now advance on every primary bar while 34/89 trend is valid. Previously the A0/fan gate prevented state updates during ribbon compression, making touch/U-turn sequences impossible and leaving DrawSignal unreachable. Filters still gate completed signal emission.
- Verification: pending after NT8 reload; pure tests and CompileCheck run before deploy.

# Release Notes — v0.27 (2026-08-02)

- **A1/draw runtime telemetry**: added low-noise NinjaScript Output markers for loaded configuration (`[DIAG]`), gate transitions (`[GATE]`), A1 phase/result transitions (`[A1]`), and draw record creation (`[DRAW]`). This separates “no A1 signal” from “draw object API failure” without logging every bar.
- Verification: pending after NT8 reload; pure tests and CompileCheck run before deploy.

# Release Notes — v0.26 (2026-08-02)

- **Legacy workspace cleanup**: workspaces retaining the removed `Kat8934` indicator can leave persisted `K8934_*` triangles/arrows behind after migration. Kat34Scalper now removes those stale objects once on the first primary bar; HUD Clear also removes both legacy and current prefixes.
- Verification: 35/35 xunit, CompileCheck 0 errors.

# Release Notes — v0.25 (2026-08-02)

- **Draw pipeline repaired**: replaced unsupported negative future `barsAgo` anchors with rolling signal-candle-to-current-bar lines using non-negative anchors, capped by `Line Length`. Stored signal records now retain entry, SL, TP and ATM trigger prices for reliable redraws on historical and realtime bars.
- **Arrow rendering simplified**: removed the misaligned two-arrow outline trick; A1 signals now render one deterministic arrow in the configured per-side entry color. A0 fan triangles remain independent.
- **HUD synchronization fixed**: UI-triggered drawing and BOT-off order cancellation now cross into the NinjaScript data context through `TriggerCustomEvent`.
- Verification: 35/35 xunit, CompileCheck 0 errors.

# Release Notes — v0.24 (2026-08-02)

- **Root-cause fix for the persistent NT8 compile storm (CS0111/CS0102/CS0121/CS0229)**: NT8's codegen injects its `#region NinjaScript generated code` (cache field + Indicator/Strategy/MarketAnalyzerColumn wrappers) into EVERY file declaring `partial class Kat34Scalper : Indicator` — 5 files each defining the same members. Module files now declare bare `partial class Kat34Scalper` (KatTradeManager pattern); only `Kat34Scalper.cs` carries `: Indicator`, so only it receives the generated region. NT8 recompiled clean — deploy accepted.
- Verification: 35/35 xunit, CompileCheck 0 errors, NT8 live recompile OK.

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
