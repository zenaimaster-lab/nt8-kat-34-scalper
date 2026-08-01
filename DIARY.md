# Project Diary & Graphify Knowledge Base

## 📊 Graphify System Architecture

```mermaid
graph TD
    A[NinjaTrader 8 Chart] --> B[Kat8934 Indicator]
    B --> C[Kat8934Logic pure state machine]
    B --> D[EMA Fast 34 / EMA Slow 89]
    B --> E[Signal drawing: text, arrow, Entry/SL/TP dash lines]
```

### Key Entities & Dependencies
- **Component**: `Kat8934` (NinjaTrader Indicator)
- **Domain Logic**: `Kat8934Logic` (pure state machine — touch EMA89 → U-turn close through EMA34 → trigger)
- **Execution Target**: NT8 chart (bip 0 only), `Calculate.OnBarClose`
- **Settings sections**: `1. Chuẩn bị` (reserved), `2. Sell Signal`, `3. Buy Signal`

---

## 📜 Version History & Change Log
### [v0.09] — 2026-08-01
- **Instant HUD reaction**: replaced the pending-flag consumption (which only ran on the next bar close) with direct `Dispatcher.InvokeAsync(() => ...)` marshaling to the data thread from every HUD click handler — `Clear`, `Arrow`, `Text` toggles now apply immediately. `pendingClearSignals`/`pendingDrawMode` removed; `ClearOldSignalDrawings`/`ApplyDrawMode` got boundary try/catch.
- **2x arrows**: verified NT8 8.1.9 `Draw.Arrow*` has no sizePixels overload (metadata scan of NinjaTrader.Gui.dll) — arrows drawn twice 1 tick apart (`K8934_*_ARROW_<bar>` + `_2`), Buy white / Sell black; `KatSignalRecord.ArrowY2` stores the second anchor for toggle redraws.
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.BuildHud` (Dispatcher.InvokeAsync wiring), `Kat8934.ApplyDrawMode`, `Kat8934.ClearOldSignalDrawings`, `Kat8934.DrawSignal` (double arrows, colors), `Kat8934.KatSignalRecord.ArrowY2`.

### [v0.08] — 2026-08-01
- **HUD toggle reactivity fix**: Arrow/Text toggles now apply immediately to all already-drawn signals. `DrawSignal` records each signal in `signalRecords` (max 200, FIFO); the HUD buttons set a volatile `pendingDrawMode` bitmask (1 = arrows, 2 = labels) which `OnBarUpdate` consumes on the data thread via `ApplyDrawMode` — OFF removes the matching `K8934_*_ARROW_*`/`K8934_*_TEXT_*` objects, ON redraws them from the records. `Clear` also clears `signalRecords` so toggles cannot resurrect cleared drawings.
- **UI language**: HUD buttons translated to English (`Clear`, `Arrow: ON/OFF`, `Text: ON/OFF`, `Hide/Show`); Vietnamese comments replaced.
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.ApplyDrawMode`, `Kat8934.signalRecords`, `Kat8934.pendingDrawMode`, `Kat8934.KatSignalRecord`, `Kat8934.BuildHud` (English labels + toggle wiring), `Kat8934.DrawSignal` (record add), `Kat8934.ClearOldSignalDrawings` (records cleared).

### [v0.07] — 2026-08-01
- Entry lines solid per side: `SellEntryLineColor` (bright red) / `BuyEntryLineColor` (bright lime green) replace shared gold `EntryLineColor`; SL/TP remain dashed.
- BUY/SELL labels bright (buy lime green, sell red), Buy below entry line / Sell above, `ShowLabels` default **false**; `ShowArrows` default true. HUD buttons `Mũi tên: ON/OFF` + `Chữ: ON/OFF` toggle `cachedShowArrows`/`cachedShowLabels` (volatile, write through to persisted properties), blue ON / gray OFF.
- `CreateHudButton` helper mirrors KatTradeManager's `CreateButton` (borderless, white, h24, padding 2).
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.CreateHudButton`, `Kat8934.BuildHud` (arrow/label toggles), `Kat8934.DrawSignal` (solid entry lines, per-side colors, conditional arrow/label), `Kat8934.ShowArrows`, `Kat8934.ShowLabels`, `Kat8934.SellEntryLineColor`, `Kat8934.BuyEntryLineColor`.

### [v0.06] — 2026-08-01
- **HUD layout squeeze fix**: HUD was attached to `ChartControl.Children` — ChartControl is the grid laying out the price panel, so a direct child forced empty gaps on both sides and squeezed the chart to the middle. HUD now attaches to the outer grid (`ChartControl.Parent as Grid`, matching KatTradeManager's `chartGrid` pattern); removal walks `hudBorder.Parent`.
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.BuildHud` (host = `ChartControl.Parent as Grid`), `Kat8934.RemoveHud` (parent-based removal).

### [v0.05] — 2026-08-01
- HUD restyled to match the KatTradeManager HUD (graphics + position only — no new features or buttons):
  - Panel: background `Argb(240,20,24,33)`, border `Rgb(35,42,56)` 1px, `CornerRadius(6)`, `Padding(8)`; buttons borderless (`BorderThickness 0`, `Padding(2)`, white foreground, height 24, font 12). Xóa Line uses the destructive dark `Rgb(20,20,20)`; Ẩn/Hiện uses OFF-gray `Rgb(45,50,65)`.
  - Position: bottom-left of chart, 10px left inset, 4px bottom (KatTradeManager InChart placement).
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.BuildHud` (panel/button styling), `Kat8934.hudBorder`.

### [v0.04] — 2026-08-01
- **Anchor bug fixed (long lines)**: `DrawSignal` passed `CurrentBar` (absolute index) as `barsAgo` to `Draw.Line`/`Arrow`/`Text` — NT8 measures barsAgo from the right chart edge, so anchors jumped to the chart start and every signal line spanned the full chart. All anchors now use `0` (the signal bar), lines extend `Line Length (bars)` forward.
- **HUD panel** (top-center overlay, WPF): `Xóa Line` clears all `K8934_S_`/`K8934_B_` draw objects via a volatile flag consumed on the data thread (then redraws the version label); `Ẩn/Hiện` toggles HUD visibility. Built in `DataLoaded` via dispatcher, removed on `Terminated`.
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.DrawSignal` (barsAgo anchors), `Kat8934.BuildHud`, `Kat8934.RemoveHud`, `Kat8934.ClearOldSignalDrawings`, `Kat8934.DrawVersionLabel`, `Kat8934.pendingClearSignals`.

### [v0.03] — 2026-08-01
- Entry/SL/TP lines shortened to configurable length (default 7 bars forward, `Line Length (bars)`), replacing the previous fixed anchors.
- New settings group `4. Lines & Text`: `Line Length (bars)`, `Line Width (px)`, `Entry Line Color`, `SL Line Color`, `TP Line Color`, `Sell Text Color`, `Buy Text Color` (NT8 `Color` + hidden serializable-string pattern).
- SELL/BUY label moved next to the entry line end (vertical offset 1 tick, same side as arrow direction); arrow color follows the text color.
- **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934.DrawSignal` (line/text anchors + colors), `Kat8934.ParseColor`, properties `LineLengthBars`, `LineWidth`, `EntryLineColor`, `SLLineColor`, `TPLineColor`, `SellTextColor`, `BuyTextColor` + `*Serializable`.

### [v0.02] — 2026-08-01
- Indicator moved into the **KAT** folder in NT8 Add Indicator dialog: namespace changed from `NinjaTrader.NinjaScript.Indicators` to `NinjaTrader.NinjaScript.Indicators.KAT`.
- No logic changes. 9/9 xunit tests, CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934` (namespace `NinjaTrader.NinjaScript.Indicators.KAT`).

### [v0.01] — 2026-08-01
- Initial release: EMA 34/89 rejection signal indicator.
  - Sell: EMA34 < EMA89, price touches/crosses EMA89, U-turns and closes below EMA34; trigger modes `Retest Bounce` (later bar closes back above EMA34) or `Breakdown` (immediate on U-turn close).
  - Buy: mirrored.
  - Drawing: SELL/BUY text + arrow above signal candle; dashed Entry (gold), SL (red), TP (green) lines, all distances in ticks (entry offset 1, SL 60, TP 120 defaults).
  - Version label top-left via `Draw.TextFixed` (updates on F5 recompile).
  - **Validation**: 9/9 xunit tests; CompileCheck 0 errors.
- **Graphify entity mapping**: `Kat8934Logic.Update`, `Kat8934.OnBarUpdate`, `Kat8934.DrawSignal`, `Kat8934TriggerMode`, `Kat8934LogicTests`.
