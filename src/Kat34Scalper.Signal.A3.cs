/*
 * Kat34Scalper.Signal.A3.cs — Signal sub-module A3: 8cross34 (partial class Kat34Scalper).
 * Independent: own toggle, own settings group ("3.6 Signal A3 — 8cross34"), own drawings.
 * Spec in docs/SIGNALS.md:
 *   EMA 8 crosses UP through EMA 34   -> BUY signal.
 *   EMA 8 crosses DOWN through EMA 34 -> SELL signal.
 * Stateless (single-bar event, no sequence): cross = previous bar on one side, current bar
 * on the other (an exact touch on the previous bar counts as the old side). EMA 34 is the
 * fixed 34 from the fan (fanEmas[0][2]) — independent of A1's configurable Fast EMA Period.
 * No stage markers, no filters. Drawings: entry/SL/TP lines (shared pipeline).
 * Default OFF. When switched ON, BackfillA3 computes + draws over "History Days" (default 3).
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- A3 sub-module state ---
		private volatile bool cachedA3 = false;   // HUD toggle: A3 on/off (default OFF)
		private volatile bool a3BackfillPending;  // set on enable; consumed once by FlushBackfill

		private const int A3WarmupBars = 35; // ema34 warmup + 1 previous bar for the cross compare

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every A3 drawing — nothing else is touched.
		private void SetA3Signal(bool on)
		{
			cachedA3 = on;
			A3Enabled = on;
			Print(string.Format("[Kat34Scalper][A3] toggled {0}", on ? "ON — backfilling History Days" : "OFF — drawings removed"));
			if (on)
			{
				a3BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				a3BackfillPending = false;
				TriggerCustomEvent(o => { CancelSignalBotEntry("A3", "A3 switched OFF"); ClearA3Drawings(); }, null);
			}
		}

		private void EvaluateA3(double high, double low, double close)
		{
			if (!cachedA3 || ema8 == null || fanEmas == null) return;
			if (CurrentBars[0] < A3WarmupBars) return;

			double e8Prev = ema8[1];
			double e34Prev = fanEmas[0][2][1];
			double e8 = ema8[0];
			double e34 = fanEmas[0][2][0];
			int cross = Kat34ScalperLogic.CrossDirection(e8Prev, e34Prev, e8, e34);
			if (cross == 0) return;

			bool isBuy = cross > 0;
			// c1 = c2 = 0 -> the entry falls back to the signal bar's high (buy) / low (sell).
			DrawSignal(isBuy, CurrentBar, high, low, 0, 0,
				A3EntryOffsetTicks, A3StopDistanceTicks, A3TargetDistanceTicks, false, "A3");
			Print(string.Format("[Kat34Scalper][A3] bar {0} {1} cross (e8 {2:F5}, e34 {3:F5})",
				CurrentBar, isBuy ? "BUY" : "SELL", e8, e34));

			// An opposite cross kills this module's own pending entry before submitting the new one.
			// The new cross submits once the cancelled order is terminal (next bar at the latest,
			// via ManageBotEntry) — crosses are rare, so a stale opposite pending is the exception.
			if (pendingOrder != null && pendingOrderOwner == "A3" && pendingIsBuy != isBuy)
				CancelPendingBotOrder("A3 opposite cross");
			TrySubmitBotEntry(isBuy, isBuy ? high : low, A3EntryOffsetTicks, "A3");
		}

		// A3 switched OFF: drop A3-owned signal records + every K34S_A3_* drawing.
		private void ClearA3Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "A3");
			RemoveModuleDrawings("K34S_A3_");
		}

		// One-shot replay over the last A3HistoryDays: draws every cross signal at its bar.
		// Stateless — nothing to hand over to live evaluation.
		private void BackfillA3()
		{
			int start = Math.Min(FindHistoryStartBarsAgo(A3HistoryDays), CurrentBars[0] - A3WarmupBars);
			if (start < 0) return;
			int signals = 0;
			for (int ago = start; ago >= 0; ago--)
			{
				double e8Prev = ema8[ago + 1];
				double e34Prev = fanEmas[0][2][ago + 1];
				double e8 = ema8[ago];
				double e34 = fanEmas[0][2][ago];
				int cross = Kat34ScalperLogic.CrossDirection(e8Prev, e34Prev, e8, e34);
				if (cross == 0) continue;
				signals++;
				DrawSignal(cross > 0, CurrentBars[0] - ago, Highs[0][ago], Lows[0][ago], 0, 0,
					A3EntryOffsetTicks, A3StopDistanceTicks, A3TargetDistanceTicks, true, "A3");
			}
			Print(string.Format("[Kat34Scalper][A3] backfill done — {0} day(s), {1} bar(s) replayed: {2} cross signal(s).",
				A3HistoryDays, start + 1, signals));
		}
	}
}
