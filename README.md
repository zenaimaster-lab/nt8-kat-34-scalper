# NT8 Kat8934 — EMA 34/89 Rejection Signal Indicator

**Current Version**: `v0.10` (Released: `2026-08-01`)

Signal indicator for **NinjaTrader 8 (NT8)**: draws Sell/Buy signals on the chart with entry, SL and TP dash lines. Appears under the **KAT** folder when adding to a chart.

## Signals

### 2. Signal (shared by Sell and Buy — mirrored mechanism)
- **Context**: Fast EMA vs Slow EMA — Sell: fast below slow (downtrend); Buy: fast above slow.
- **Setup**: price touches/crosses the Slow EMA, U-turns and closes back through the Fast EMA.
- **Trigger** (configurable):
  - `Retest Bounce`: a later bar closes back through the Fast EMA → signal (sell the retest / buy the retest).
  - `Breakdown`: fires immediately on the U-turn close.
- **Drawing**: sell entry line **solid red** below the signal low, buy entry line **solid lime green** above the signal high (both with `Entry Offset` ticks); SL dashed red (`Stop Distance` above/below entry), TP dashed green; big 2x arrow near the candle (Buy white above, Sell black below, `Arrow Offset` ticks away, default 3); optional BUY/SELL label at the candle (Buy below / Sell above the entry level, default off, toggled from the HUD).

### 1. Preparation
- Section reserved in settings — conditions to be added later. Currently empty (no settings group visible in the property grid until a property is added).

## Settings (3 sections)
| Section | Settings |
|---|---|
| 1. Preparation | *(reserved — none yet)* |
| 2. Signal | Enabled, Fast EMA Period (34), Slow EMA Period (89), Trigger Mode, Entry Offset (1 tick), Stop Distance (60), Target Distance (120) |
| 3. Lines & Text | Line Length (7 bars), Line Width (2 px), Arrow Offset (3 ticks), Sell/Buy Entry Line Colors (solid), SL/TP Line Colors, Sell/Buy Text Colors, Show Arrows, Show Buy/Sell Labels (default off) |

Parameters group: `Show Version Label` — draws `Kat8934 vX.XX (date)` top-left on the chart (updates on every F5 recompile).

## HUD
Small panel at the bottom-left of the chart (graphics match the KatTradeManager HUD: dark navy panel, slate border, borderless white buttons) with 3 buttons:
- **Clear** — removes all signal drawings (old Entry/SL/TP lines, arrows, labels) — reacts immediately.
- **Arrow: ON/OFF** — show or hide the signal arrows — reacts immediately.
- **Text: ON/OFF** — show or hide the BUY/SELL labels — reacts immediately.

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
