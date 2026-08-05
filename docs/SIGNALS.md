# SIGNALS.md — Independent signal indicators (Kat34Scalper ecosystem)

**Rule (v1.04+):** every signal is its **own NT8 indicator** under `indicators/`.  
**Never** re-embed signal evaluation into the Scalper shell. Scalper only **reads** signals via `KatSignalBus`.

## Architecture

```
Chart
 ├─ KatA1  (alert)  ──publish──┐
 ├─ KatA2  (alert)  ──publish──┼──► KatSignalBus ──► Kat34Scalper (bot shell)
 ├─ KatB1  (bot sig)──publish──┤         │
 └─ KatB2  (bot sig)──publish──┘         ▼
                                   Filter + Bot orders
```

| Indicator | File | Bot? | Publishes |
|---|---|---|---|
| KatA1 | `indicators/KatA1.cs` | no | EnvDir (LONG/SHORT/RANGE) |
| KatA2 | `indicators/KatA2.cs` | no | placeholder status |
| KatB1 | `indicators/KatB1.cs` | yes | HasPending + ref extreme + offsets |
| KatB2 | `indicators/KatB2.cs` | yes | HasFire one-shot + ref extreme |
| Kat34Scalper | shell | executes | TradeB1/TradeB2 gates + filters |

Pure math shared: `src/Kat34ScalperLogic.cs` (xunit). Bus: `src/KatSignalBus.cs`.

## How to add a new signal (e.g. B3)

1. Create `indicators/KatB3.cs` implementing `IKatSignalProvider`.
2. `KatSignalBus.Register` in DataLoaded / Unregister in Terminated.
3. Draw with unique tag prefix `KATB3_*`.
4. Publish pending and/or fire in `GetSnapshot()`.
5. Optionally add `TradeB3` gate on Scalper HUD/settings.
6. Document here. **Do not** put evaluation code in Scalper partials.

## A1 — EmaZone30s (alert)

- Fan EMA 8>34>89>144>200 + optional angle + EMA34 zone TFs.
- Edge vertical line + sound; episode bands; Break Bars debounce.
- Settings on **KatA1** indicator only.

## A2 — placeholder (alert)

- Stub for future alert signal.

## B1 — 34bounce8+ (bot signal)

- Trend stack + ema34 bounce pending stop entry (UpdateA2 state machine).
- Draws entry/SL/TP + `Buy B1`/`Sell B1`.
- Publishes pending snapshot every bar for Scalper orchestrator.

## B2 — 89uturn34 (bot signal)

- 89-34 pullback U-turn sequence (Update / KatA1State).
- Draws phase markers + levels on fire.
- Publishes one-shot `HasFire`; Scalper acks via `MarkFireConsumed`.

## Scalper trade gates

- `Trade B1` / `Trade B2` (settings + HUD): allow bot to act on that provider.
- Bot filters (ADX rising/MTF, ER, CI, Volume, Time) still gate execution on the shell.
- Signal indicators themselves do **not** run bot filters (open/independent); shell filters at submit time.
