/*
 * Kat34Scalper.Signal.cs — Signal module (partial class Kat34Scalper).
 * Owns every signal sub-module; each sub-module evaluates its own state and fires
 * Draw + Bot on a trigger. New signals (A2, A3, ...) plug in as a new region here.
 *   A0 — EMA-ribbon fan (9/21/34/55/89/144/200): independent triangle marker + alert.
 *   A1 — 89-34 pullback: arm beyond ema34 -> close-basis cross -> ema89 touch -> U-turn close.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// ponytail: no ': Indicator' here — NT8's codegen injects its generated region into EVERY
	// file that declares the base class, duplicating cacheKat34Scalper/wrappers across files
	// (CS0111/CS0102/CS0121/CS0229). Only Kat34Scalper.cs carries the base spec.
	public partial class Kat34Scalper
	{
		// --- Signal module state ---
		private volatile bool cachedA0 = false;  // HUD toggle: A0 sub-module on/off (default OFF)
		private volatile bool cachedA1 = true;   // HUD toggle: A1 sub-module on/off
		private bool a0Alerted;                  // A0 alert already fired for the current fan episode
		private readonly KatA1State sellState = new KatA1State(); // A1 sell-side sequence
		private readonly KatA1State buyState = new KatA1State();  // A1 buy-side sequence
		private bool diagnosticGateInitialized;
		private int diagnosticA0Dir;
		private bool diagnosticSellAllowed;
		private bool diagnosticBuyAllowed;

		private static KatTriggerMode ToLogicMode(Kat34ScalperTriggerMode mode)
		{
			return mode == Kat34ScalperTriggerMode.Breakdown ? KatTriggerMode.Breakdown : KatTriggerMode.RetestBounce;
		}

		#region Sub-module A0 — EMA-ribbon fan signal
		// Returns the current primary fan direction (-1 sell / 0 none / +1 buy).
		// Signal rendering is controlled by cachedA0; returned direction remains available to A1 filters.
		private int EvaluateA0Fan()
		{
			if (fanEmas == null || CurrentBars[0] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback) return 0;

			int dir = SeriesFanDirection(0);
			if (dir != 0 && cachedA0 && !a0Alerted)
			{
				a0Alerted = true;
				PlayAlertSound();
				double y = dir > 0 ? Lows[0][0] - ArrowOffsetTicks * TickSize : Highs[0][0] + ArrowOffsetTicks * TickSize;
				if (dir > 0)
					Draw.TriangleUp(this, "K34S_A0_" + CurrentBar, false, 0, y, Brushes.DodgerBlue);
				else
					Draw.TriangleDown(this, "K34S_A0_" + CurrentBar, false, 0, y, Brushes.OrangeRed);
				Print(string.Format("[Kat34Scalper] A0 {0} fan @ bar {1}", dir > 0 ? "BUY" : "SELL", CurrentBar));
			}
			else if (dir == 0 || !cachedA0)
			{
				a0Alerted = false; // fan collapsed or A0 signal disabled — re-arm on next enabled episode
			}
			return dir;
		}
		#endregion

		#region Sub-module A1 — 89-34 pullback signal (Sell / Buy mirrored)
		private void EvaluateA1(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedA1 || !SignalEnabled || fastEma == null || slowEma == null) return;
			if (CurrentBars[0] < Math.Max(EmaFastPeriod, EmaSlowPeriod)) return;

			double fast = fastEma[0];
			double slow = slowEma[0];
			KatTriggerMode mode = ToLogicMode(TriggerMode);
			int sellPhaseBefore = sellState.Phase;
			int buyPhaseBefore = buyState.Phase;
			KatSignalKind? sellSignal = null;
			KatSignalKind? buySignal = null;

			// Advance both state machines on every primary bar while their 34/89 trend is valid.
			// The A0/fan and other filters gate signal emission, not setup progression; otherwise
			// a normal pullback that collapses the ribbon freezes before it can reach the U-turn.
			sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, mode, MaxSequenceBars,
				fast < slow, high, low, close, fast, slow, sellState);
			buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, mode, MaxSequenceBars,
				fast > slow, high, low, close, fast, slow, buyState);

			if (sellSignal == KatSignalKind.Sell)
			{
				if (sellAllowed)
				{
					DrawSignal(false, CurrentBar, high, low, sellState.C1, sellState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
					TrySubmitBotEntry(false, sellState.C2);
				}
				else
					Print(string.Format("[Kat34Scalper][A1] bar {0} SELL result suppressed by filters; A0={1}, allowed={2}",
						CurrentBar, diagnosticA0Dir, sellAllowed));
			}
			if (buySignal == KatSignalKind.Buy)
			{
				if (buyAllowed)
				{
					DrawSignal(true, CurrentBar, high, low, buyState.C1, buyState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
					TrySubmitBotEntry(true, buyState.C2);
				}
				else
					Print(string.Format("[Kat34Scalper][A1] bar {0} BUY result suppressed by filters; A0={1}, allowed={2}",
						CurrentBar, diagnosticA0Dir, buyAllowed));
			}

			DrawA1PhaseStatus(false, sellState.Phase, sellState.Touched89, fast, slow);
			DrawA1PhaseStatus(true, buyState.Phase, buyState.Touched89, fast, slow);

			if (sellState.Phase != sellPhaseBefore)
				Print(string.Format("[Kat34Scalper][A1] bar {0} SELL phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, sellPhaseBefore, sellState.Phase, sellAllowed, fast < slow, close, fast, slow));
			if (buyState.Phase != buyPhaseBefore)
				Print(string.Format("[Kat34Scalper][A1] bar {0} BUY phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, buyPhaseBefore, buyState.Phase, buyAllowed, fast > slow, close, fast, slow));
			if (sellSignal.HasValue || buySignal.HasValue)
				Print(string.Format("[Kat34Scalper][A1] bar {0} result sell={1}, buy={2}, mode={3}",
					CurrentBar, sellSignal.HasValue ? sellSignal.Value.ToString() : "none",
					buySignal.HasValue ? buySignal.Value.ToString() : "none", mode));
		}

		// Live per-side A1 phase status marker on the chart - proves A1 is alive and shows where
		// the current setup stalls (arm/pull/touch/U-turn). One marker per side; replaces each bar.
		private void DrawA1PhaseStatus(bool isBuy, int phase, bool touched, double fast, double slow)
		{
			if (!cachedA1) return;
			string tag = "K34S_A1ST_" + (isBuy ? "B" : "S");
			if (phase == 0)
			{
				RemoveDrawObject(tag);
				return;
			}
			string label;
			double price = fast;
			if (phase == 1)
				label = "A1-arm";
			else if (phase == 2)
			{
				label = touched ? "A1-pull-T" : "A1-pull";
				price = touched ? slow : fast;
			}
			else
				label = "A1-U-turn"; // phase == 3
			Brush brush = isBuy ? Brushes.DodgerBlue : Brushes.OrangeRed;
			Draw.Text(this, tag, label, 0, price, brush);
		}
		#endregion
	}
}
