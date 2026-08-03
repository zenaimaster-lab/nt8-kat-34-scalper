/*
 * Kat34Scalper.Signal.A1.cs — Signal sub-module A1: 89-34 pullback (partial class Kat34Scalper).
 * Independent: own toggle, own settings group ("3. Signal A1 — 89/34 Pullback"), own drawings.
 * Stages per side (Sell mirrored by Buy) — full spec in docs/SIGNALS.md:
 *   A1-arm    (phase 1) — close beyond the fast EMA on the pullback side.
 *   A1-pull   (phase 2) — close back through the fast EMA toward the slow EMA, no touch yet.
 *   A1-pull-T (phase 2) — pullback touched/crossed the slow EMA.
 *   signal    — fires on the U-turn close back through the fast EMA (after pull-T).
 * Default OFF. When switched ON, BackfillA1 computes + draws stages and signals over
 * "History Days" (default 3) and hands the replayed state to the live state machines.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- A1 sub-module state ---
		private volatile bool cachedA1 = false;   // HUD toggle: A1 on/off (default OFF)
		private volatile bool a1BackfillPending;  // set on enable; consumed once by FlushBackfill
		private readonly KatA1State sellState = new KatA1State(); // sell-side sequence
		private readonly KatA1State buyState = new KatA1State();  // buy-side sequence

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every A1 drawing (stage markers + entry/SL/TP lines) — nothing else is touched.
		private void SetA1Signal(bool on)
		{
			cachedA1 = on;
			A1Enabled = on;
			if (on)
			{
				a1BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				a1BackfillPending = false;
				sellState.Reset();
				buyState.Reset();
				TriggerCustomEvent(o => { CancelSignalBotEntry("A1", "A1 switched OFF"); ClearA1Drawings(); }, null);
			}
		}

		private void EvaluateA1(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedA1 || fastEma == null || slowEma == null) return;
			if (CurrentBars[0] < Math.Max(EmaFastPeriod, EmaSlowPeriod)) return;
			Account acc = ResolveBotAccount();
			if (IsSignalInTrade("A1") || HasOpenPosition(acc)) return;

			double fast = fastEma[0];
			double slow = slowEma[0];
			int sellPhaseBefore = sellState.Phase;
			int buyPhaseBefore = buyState.Phase;
			bool sellTouchedBefore = sellState.Touched89;
			bool buyTouchedBefore = buyState.Touched89;
			KatSignalKind? sellSignal = null;
			KatSignalKind? buySignal = null;

			// Advance both state machines on every primary bar while their 34/89 trend is valid.
			// The A0/fan and other filters gate signal emission, not setup progression; otherwise
			// a normal pullback that collapses the ribbon freezes before it can reach the U-turn.
			sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
				fast < slow, high, low, close, fast, slow, sellState);
			buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
				fast > slow, high, low, close, fast, slow, buyState);

			if (sellSignal == KatSignalKind.Sell)
			{
				if (sellAllowed)
				{
					DrawSignal(false, CurrentBar, high, low, sellState.C1, sellState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks, false, "A1");
					TrySubmitBotEntry(false, sellState.C2, EntryOffsetTicks);
				}
				else
					Print(string.Format("[Kat34Scalper][A1] bar {0} SELL result suppressed by filters; A0={1}, allowed={2}",
						CurrentBar, diagnosticA0Dir, sellAllowed));
			}
			if (buySignal == KatSignalKind.Buy)
			{
				if (buyAllowed)
				{
					DrawSignal(true, CurrentBar, high, low, buyState.C1, buyState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks, false, "A1");
					TrySubmitBotEntry(true, buyState.C2, EntryOffsetTicks);
				}
				else
					Print(string.Format("[Kat34Scalper][A1] bar {0} BUY result suppressed by filters; A0={1}, allowed={2}",
						CurrentBar, diagnosticA0Dir, buyAllowed));
			}

			// Phase-transition milestones (arm / pull / U-turn) + touch milestone - persistent
			// per-bar markers so A1 setup progression is visible on chart history, not just live.
			if (sellState.Phase != sellPhaseBefore)
			{
				DrawA1PhaseMarkerAt(false, 0, high, low, sellState.Phase, sellState.Touched89);
				Print(string.Format("[Kat34Scalper][A1] bar {0} SELL phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, sellPhaseBefore, sellState.Phase, sellAllowed, fast < slow, close, fast, slow));
			}
			if (buyState.Phase != buyPhaseBefore)
			{
				DrawA1PhaseMarkerAt(true, 0, high, low, buyState.Phase, buyState.Touched89);
				Print(string.Format("[Kat34Scalper][A1] bar {0} BUY phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, buyPhaseBefore, buyState.Phase, buyAllowed, fast > slow, close, fast, slow));
			}
			// Touch milestone - pullback reached ema89 (happens inside phase 2, not a phase change).
			if (!sellTouchedBefore && sellState.Touched89 && sellState.Phase == 2)
				DrawA1PhaseMarkerAt(false, 0, high, low, 2, true);
			if (!buyTouchedBefore && buyState.Touched89 && buyState.Phase == 2)
				DrawA1PhaseMarkerAt(true, 0, high, low, 2, true);

			if (sellSignal.HasValue || buySignal.HasValue)
				Print(string.Format("[Kat34Scalper][A1] bar {0} result sell={1}, buy={2}",
					CurrentBar, sellSignal.HasValue ? sellSignal.Value.ToString() : "none",
					buySignal.HasValue ? buySignal.Value.ToString() : "none"));
		}

		// Persistent per-bar milestone marker drawn at the bar where an A1 phase changes or ema89
		// is first touched. Unique tag per bar so each milestone survives on chart history.
		// Label: A1-arm (phase 1) / A1-pull (phase 2, no touch yet) / A1-pull-T (phase 2, touched).
		// Buy below the low, sell above the high.
		private void DrawA1PhaseMarkerAt(bool isBuy, int barsAgo, double high, double low, int phase, bool touched)
		{
			string label;
			if (phase == 1) label = "A1-arm";
			else if (phase == 2) label = touched ? "A1-pull-T" : "A1-pull";
			else return;
			string tag = "K34S_A1ST_" + (isBuy ? "B" : "S") + "_" + (CurrentBars[0] - barsAgo);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = isBuy ? Brushes.DodgerBlue : Brushes.OrangeRed;
			Draw.Text(this, tag, label, barsAgo, y, brush);
		}

		// One-shot replay over the last A1HistoryDays: fresh temp state machines walk the window,
		// drawing stage markers and full signals at their bars; filters are evaluated per bar.
		// No bot orders and no alert sounds during replay. After the pass the temp states replace
		// the live states so realtime evaluation continues an in-flight sequence seamlessly.
		private void BackfillA1()
		{
			int warm = Math.Max(EmaFastPeriod, EmaSlowPeriod);
			int start = Math.Min(FindHistoryStartBarsAgo(A1HistoryDays), CurrentBars[0] - warm);
			if (start < 0) return;
			var tmpSell = new KatA1State();
			var tmpBuy = new KatA1State();
			for (int ago = start; ago >= 0; ago--)
			{
				double h = Highs[0][ago];
				double l = Lows[0][ago];
				double c = Closes[0][ago];
				double f = fastEma[ago];
				double sl = slowEma[ago];
				int sellPhaseBefore = tmpSell.Phase;
				int buyPhaseBefore = tmpBuy.Phase;
				bool sellTouchedBefore = tmpSell.Touched89;
				bool buyTouchedBefore = tmpBuy.Touched89;

				bool sellAllowed, buyAllowed;
				PassFiltersAt(ago, SeriesFanDirectionAt(0, ago), out sellAllowed, out buyAllowed);

				KatSignalKind? sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
					f < sl, h, l, c, f, sl, tmpSell);
				KatSignalKind? buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
					f > sl, h, l, c, f, sl, tmpBuy);

				if (sellSignal == KatSignalKind.Sell && sellAllowed)
					DrawSignal(false, CurrentBars[0] - ago, h, l, tmpSell.C1, tmpSell.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks, true, "A1");
				if (buySignal == KatSignalKind.Buy && buyAllowed)
					DrawSignal(true, CurrentBars[0] - ago, h, l, tmpBuy.C1, tmpBuy.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks, true, "A1");

				if (tmpSell.Phase != sellPhaseBefore)
					DrawA1PhaseMarkerAt(false, ago, h, l, tmpSell.Phase, tmpSell.Touched89);
				if (tmpBuy.Phase != buyPhaseBefore)
					DrawA1PhaseMarkerAt(true, ago, h, l, tmpBuy.Phase, tmpBuy.Touched89);
				if (!sellTouchedBefore && tmpSell.Touched89 && tmpSell.Phase == 2)
					DrawA1PhaseMarkerAt(false, ago, h, l, 2, true);
				if (!buyTouchedBefore && tmpBuy.Touched89 && tmpBuy.Phase == 2)
					DrawA1PhaseMarkerAt(true, ago, h, l, 2, true);
			}
			sellState.CopyFrom(tmpSell);
			buyState.CopyFrom(tmpBuy);
			Print(string.Format("[Kat34Scalper][A1] backfill done — {0} day(s), {1} bar(s) replayed; live states synced (sell phase {2}, buy phase {3}).",
				A1HistoryDays, start + 1, sellState.Phase, buyState.Phase));
		}
	}
}
