# NT8 Kat8934 — EMA 34/89 Rejection Signal Indicator

**Current Version**: `v0.05` (Released: `2026-08-01`)

Signal indicator for **NinjaTrader 8 (NT8)**: draws Sell/Buy signals on the chart with entry, SL and TP dash lines. Appears under the **KAT** folder when adding to a chart.

## Signals

### 2. Sell Signal
- **Context**: Fast EMA below Slow EMA (downtrend).
- **Setup**: price rallies up to touch/cross the Slow EMA (89), then U-turns and closes back below the Fast EMA (34).
- **Trigger** (configurable):
  - `Retest Bounce`: a later bar closes back **above** the Fast EMA → SELL (sell the retest).
  - `Breakdown`: fires immediately on the U-turn close below the Fast EMA.
- **Drawing**: red `SELL` text + down arrow at the signal candle; dashed lines — Entry (gold, `offset` ticks below signal low), SL (red, `stop` ticks above entry), TP (green, `target` ticks below entry). Lines are short (default 7 bars forward) with the SELL/BUY label placed next to the entry line.

### 3. Buy Signal
- Mirror of Sell: Fast EMA above Slow EMA; price drops to touch/cross Slow EMA, U-turns and closes back above Fast EMA; trigger modes mirrored. Entry is `offset` ticks above signal high, SL below, TP above.

### 1. Chuẩn bị (preparation)
- Section reserved in settings — conditions to be added later. Currently empty (no settings group visible in the property grid until a property is added).

## Settings (3 sections)
| Section | Settings |
|---|---|
| 1. Chuẩn bị | *(reserved — none yet)* |
| 2. Sell Signal | Enabled, Fast EMA Period (34), Slow EMA Period (89), Trigger Mode, Entry Offset (1 tick), Stop Distance (60), Target Distance (120) |
| 3. Buy Signal | Enabled, Fast EMA Period (34), Slow EMA Period (89), Trigger Mode, Entry Offset (1 tick), Stop Distance (60), Target Distance (120) |
| 4. Lines & Text | Line Length (7 bars), Line Width (2 px), Entry/SL/TP Line Colors, Sell/Buy Text Colors |

Parameters group: `Show Version Label` — draws `Kat8934 vX.XX (date)` top-left on the chart (updates on every F5 recompile).

## HUD
Small panel at the bottom-left of the chart (graphics match the KatTradeManager HUD: dark navy panel, slate border, borderless white buttons) with 2 buttons:
- **Xóa Line** — removes all signal drawings (old Entry/SL/TP lines, arrows, labels).
- **Ẩn / Hiện** — hide or show the HUD panel.

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
