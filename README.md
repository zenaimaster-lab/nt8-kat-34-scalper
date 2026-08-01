# NT8 Kat8934 — EMA 34/89 Rejection Signal Indicator

**Current Version**: `v0.13` (Released: `2026-08-01`)

Signal indicator for **NinjaTrader 8 (NT8)**: draws Sell/Buy signals on the chart with entry, SL and TP dash lines. Appears under the **KAT** folder when adding to a chart.

## Signals

### 1. Filters (A0 fan + market gates)
- **A0 fan**: EMAs 9/21/34/55/89/144/200 strictly ordered **and** spreading (EMA9↔EMA200 wider than `Fan Spread Lookback` bars ago, at least `Fan Min Spread (ticks)`). First bar of a fan episode draws a small triangle (buy blue below / sell orange above) and plays the `Alert Sound`. Re-arms when the fan collapses.
- **MTF**: optional 3m / 5m / 15m ribbons must fan in the same direction (per-TF ON/OFF in settings).
- **Market**: ADX ≥ `ADX Min` (blocks sideways) and bar volume ≥ `Volume Min (x SMA)` × volume SMA (blocks dead bars).
- **Time window**: `HH:mm` machine-local start/end; overnight wraps midnight; equal start/end disables.
- A1 (Sell/Buy) fires only while a same-direction fan is active and every enabled gate passes.

### 2. Signal (shared by Sell and Buy — mirrored mechanism)
- **Context**: Fast EMA vs Slow EMA — Sell: fast below slow (downtrend); Buy: fast above slow.
- **Setup**: price touches/crosses the Slow EMA, U-turns and closes back through the Fast EMA.
- **Trigger** (configurable):
  - `Retest Bounce`: a later bar closes back through the Fast EMA → signal (sell the retest / buy the retest).
  - `Breakdown`: fires immediately on the U-turn close.
- **Dual entry (A1)**: two candidates tracked per setup — **C1** = the U-turn bar's low/high, **C2** = the best later bar that still closes on the setup side of the fast EMA (higher low for sells / lower high for buys). The solid entry line sits at the better of the two (sell = higher stop, buy = lower stop); when they differ, both candidates also show as faded dotted lines.
- **Drawing**: sell entry line **solid red**, buy entry line **solid lime green** (both with `Entry Offset` ticks); SL dashed red (`Stop Distance` above/below entry), TP dashed green; big 2x arrow near the candle (Buy white above, Sell black below, `Arrow Offset` ticks away, default 3); optional BUY/SELL label at the candle (Buy below / Sell above the entry level, default off, toggled from the HUD).

## Bot (semi-auto)
- Trades **only** while the HUD **BOT: ON** button is active (off by default) *and* `Bot Enabled` is set — never runs on its own. Switching BOT OFF cancels the pending entry immediately.
- On an A1 signal it submits a stop order (sell stop below the better candidate low / buy stop above the better candidate high) through the selected **ATM template** on the selected **account**; `None` or a missing template falls back to a bare stop order.
- **Migration**: while the entry is still working, a newer bar closing on the setup side of the fast EMA with a better extreme (sell: higher low / buy: lower high) cancels the order and re-places it at the better price once the cancel settles. A 34/89 trend flip cancels the pending entry. One bot order at a time; once filled, the ATM owns the brackets.

## Settings (4 sections)
| Section | Settings |
|---|---|
| 1. Filters | A0 Fan Filter Enabled, Fan Min Spread (20 ticks), Fan Spread Lookback (5 bars), Use 3m/5m/15m Fan (off), ADX Period (14), ADX Min (20), Volume SMA Period (20), Volume Min x SMA (1.0), Time Start/End (08:00–17:00), Alert Sound (dropdown of NT8 .wav files) |
| 2. Signal | Enabled, Fast EMA Period (34), Slow EMA Period (89), Trigger Mode, Entry Offset (1 tick), Stop Distance (60), Target Distance (120) |
| 4. Bot | Bot Enabled (off), Order Quantity (1), ATM Template (dropdown of NT8 ATM templates + None), Account Name |
| 3. Lines & Text | Line Length (7 bars), Line Width (2 px), Arrow Offset (3 ticks), Sell/Buy Entry Line Colors (solid), SL/TP Line Colors, Sell/Buy Text Colors, Show Arrows, Show Buy/Sell Labels (default off) |

Parameters group: `Show Version Label` — draws `Kat8934 vX.XX (date)` top-left on the chart (updates on every F5 recompile).

## HUD
Small panel at the bottom-left of the chart (graphics match the KatTradeManager HUD: dark navy panel, slate border, borderless white buttons):
- Row 1: **Clear** — removes all signal drawings; **Arrow: ON/OFF** — signal arrows; **Text: ON/OFF** — BUY/SELL labels. All react immediately.
- Row 2: **A0 / MTF / ADX / Vol / Time** — runtime filter toggles (blue ON / gray OFF), effective from the next bar.
- Row 3: **BOT: ON/OFF** — arms/disarms the semi-auto bot (OFF cancels its pending entry); **ATM dropdown**; **Account dropdown**; disabled **A2… / A3…** placeholders for future signals.

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
