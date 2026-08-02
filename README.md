# NT8 Kat8934 — EMA 34/89 Rejection Signal Indicator

**Current Version**: `v0.17` (Released: `2026-08-02`)

Signal indicator for **NinjaTrader 8 (NT8)**: draws Sell/Buy signals on the chart with entry, SL and TP dash lines. Appears under the **KAT** folder when adding to a chart.

## Signals

### 1. Filters (A0 fan + market gates)
- **A0 fan**: EMAs 9/21/34/55/89/144/200 strictly ordered **and** spreading (EMA9↔EMA200 wider than `Fan Spread Lookback` bars ago, at least `Fan Min Spread (ticks)`). First bar of a fan episode draws a small triangle (buy blue below / sell orange above) and plays the `Alert Sound`. Re-arms when the fan collapses.
- **MTF**: optional 3m / 5m / 15m ribbons must fan in the same direction (per-TF ON/OFF in settings). A secondary data series is added **only** for enabled timeframes — with all MTF off (default) the chart keeps its single primary series and every other chart indicator (your EMAs) is completely untouched.
- **Market**: ADX ≥ `ADX Min` (blocks sideways) and bar volume ≥ `Volume Min (x SMA)` × volume SMA (blocks dead bars).
- **Time window**: `HH:mm` machine-local start/end; overnight wraps midnight; equal start/end disables.
- A1 (Sell/Buy) fires only while a same-direction fan is active and every enabled gate passes.

### 2. Signal (shared by Sell and Buy — mirrored mechanism)
- **Context**: Fast EMA vs Slow EMA — Sell: fast below slow (downtrend); Buy: fast above slow.
- **Sequence** (every step on close basis, wicks don't cross):
  1. **Armed**: price closes beyond the fast EMA (Sell: below / Buy: above).
  2. **Pullback**: price crosses back through the fast EMA toward the slow EMA — the sequence clock starts.
  3. **Touch**: price touches or crosses the slow EMA.
  4. **U-turn**: price reverses and closes back through the fast EMA (with-trend again).
  - The whole sequence must complete within **`Max Sequence Bars`** (default 30, counted from the cross bar) or the setup expires and rearms. A pullback that reverses before ever touching the slow EMA simply rearms.
- **Trigger** (configurable):
  - `Retest Bounce`: a later bar closes back through the Fast EMA → signal (sell the retest / buy the retest).
  - `Breakdown`: fires immediately on the U-turn close.
- **Dual entry (A1)**: two candidates tracked per setup — **C1** = the U-turn bar's low/high, **C2** = the best later bar that still closes on the setup side of the fast EMA (higher low for sells / lower high for buys). The solid entry line sits at the better of the two (sell = higher stop, buy = lower stop); when they differ, both candidates also show as faded dotted lines.
- **Drawing (KatTradeManager style)**: sell entry line **solid red**, buy entry line **solid lime green** (both with `Entry Offset` ticks); SL dashed red, TP dashed green — taken from the selected **ATM template** when it defines StopLoss/Target (settings `Stop/Target Distance` are the fallback); ATM **trailing-SL trigger lines** when the template defines them — **BE** DeepSkyBlue dash-dot, **SL1** orange dot, **SL2** magenta dot (1 px, profit side of entry); big 2x arrow near the candle (**Buy white** above / **Sell black** below, `Arrow Offset` ticks away, default 3); optional BUY/SELL label at the candle (default off, toggled from the HUD).

## Bot (semi-auto)
- Trades **only** while the HUD **BOT: ON** button is active (off by default) *and* `Bot Enabled` is set — never runs on its own. Switching BOT OFF cancels the pending entry immediately.
- On an A1 signal it submits a stop order (sell stop below the better candidate low / buy stop above the better candidate high) through the selected **ATM template** on the selected **account**; `None` or a missing template falls back to a bare stop order.
- **Migration**: while the entry is still working, a newer bar closing on the setup side of the fast EMA with a better extreme (sell: higher low / buy: lower high) cancels the order and re-places it at the better price once the cancel settles. A 34/89 trend flip cancels the pending entry. One bot order at a time; once filled, the ATM owns the brackets.

## Settings (4 sections)
| Section | Settings |
|---|---|
| 1. Filters | A0 Fan Filter Enabled, Fan Min Spread (20 ticks), Fan Spread Lookback (5 bars), Use 3m/5m/15m Fan (off), ADX Period (14), ADX Min (20), Volume SMA Period (20), Volume Min x SMA (1.0), Time Start/End (08:00–17:00), Alert Sound (dropdown of NT8 .wav files) |
| 2. Signal | Enabled, Fast EMA Period (34), Slow EMA Period (89), Max Sequence Bars (30), Trigger Mode, Entry Offset (1 tick), Stop Distance (60, ATM fallback), Target Distance (120, ATM fallback) |
| 4. Bot | Bot Enabled (off), Order Quantity (1), ATM Template (dropdown of NT8 ATM templates + None), Account Name |
| 3. Lines & Text | Line Length (7 bars), Line Width (2 px), Arrow Offset (3 ticks), Sell/Buy Entry Line Colors (solid), SL/TP Line Colors, Sell/Buy Text Colors, Show Arrows, Show Buy/Sell Labels (default off) |

Parameters group: `Show Version Label` — draws `Kat8934 vX.XX (date) [chart timeframe]` top-left on the chart (updates on every F5 recompile). All signal math runs on the primary series of the chart the indicator is added to — the label proves which timeframe that is (e.g. `[30 Second]`).

## HUD
TradeManager-style panel (same colors, sizes and structure): dark navy card `Argb(240,20,24,33)` on a draggable canvas (drag anywhere outside the buttons, clamped so it can't leave the chart), `⚡ KAT 8934 vX.XX` steel-blue header, and a status line (5 s auto-clear) that mirrors bot events — submits, migrations, cancels, fills.
- **Section 1 — Account & ATM**: `Acc:` row (account dropdown) + ATM template dropdown (sorted, `None` = bare stop order).
- **Section 2 — Filters**: `A0 fan | MTF`, `ADX | Volume`, `Time window` toggles — blue ON / gray OFF (LightGray text), effective from the next bar.
- **Section 3 — Bot & Display**: `⚡ BOT: ON/OFF` (default OFF; OFF cancels the pending entry immediately), `Arrow | Text` drawing toggles, disabled `A2… | A3…` placeholders for future signals, dark `Clear` button.

## Installation in NinjaTrader 8

1. Open **NinjaTrader 8**.
2. Go to **Tools** -> **NinjaScript Editor**.
3. Open or import `Kat8934.cs` under `Indicators`.
4. Press **F5** to Compile (chart indicators auto-reload with the new version label).
5. Add `Kat8934` to any NT8 Chart.

## Development workflow
- `pwsh scripts/Run-AllChecks.ps1` — xunit suite + net48 compile gate.
- `pwsh scripts/Deploy-NT8.ps1` — copies sources into NT8 + verifies auto-recompile.
- Version bump, diary, graphify and GitHub sync per `AGENTS.md` / `RULES.md`.

## License

MIT
