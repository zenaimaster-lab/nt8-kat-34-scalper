/*
 * Kat34Scalper.Signal.cs — Signal module (partial class Kat34Scalper).
 * Owns every signal sub-module; each sub-module evaluates its own state and fires
 * Draw + Bot on a trigger. New signals (A2, A3, ...) plug in as a new region here.
 *   A0 — EMA-ribbon fan (9/21/34/55/89/144/200): triangle marker + alert, gates A1 direction.
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
	public partial class Kat34Scalper : Indicator
	{
		// --- Signal module state ---
		private volatile bool cachedA0 = true;   // HUD toggle: A0 sub-module on/off
		private volatile bool cachedA1 = true;   // HUD toggle: A1 sub-module on/off
		private bool a0Alerted;                  // A0 alert already fired for the current fan episode
		private readonly KatA1State sellState = new KatA1State(); // A1 sell-side sequence
		private readonly KatA1State buyState = new KatA1State();  // A1 buy-side sequence

		private static KatTriggerMode ToLogicMode(Kat34ScalperTriggerMode mode)
		{
			return mode == Kat34ScalperTriggerMode.Breakdown ? KatTriggerMode.Breakdown : KatTriggerMode.RetestBounce;
		}

		#region Sub-module A0 — EMA-ribbon fan signal
		// Returns the current primary fan direction (-1 sell / 0 none / +1 buy).
		// 0 also when the sub-module is off — the Filter module then treats the fan gate as open.
		private int EvaluateA0Fan()
		{
			if (!cachedA0 || !FanFilterEnabled) return 0;
			if (fanEmas == null || CurrentBars[0] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback) return 0;

			int dir = SeriesFanDirection(0);
			if (dir != 0 && !a0Alerted)
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
			else if (dir == 0)
			{
				a0Alerted = false; // fan collapsed — re-arm the alert
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

			if (sellAllowed && Kat34ScalperLogic.Update(KatSignalKind.Sell, mode, MaxSequenceBars,
				fast < slow, high, low, close, fast, slow, sellState) == KatSignalKind.Sell)
			{
				DrawSignal(false, CurrentBar, high, low, sellState.C1, sellState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
				TrySubmitBotEntry(false, sellState.C2);
			}
			if (buyAllowed && Kat34ScalperLogic.Update(KatSignalKind.Buy, mode, MaxSequenceBars,
				fast > slow, high, low, close, fast, slow, buyState) == KatSignalKind.Buy)
			{
				DrawSignal(true, CurrentBar, high, low, buyState.C1, buyState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
				TrySubmitBotEntry(true, buyState.C2);
			}
		}
		#endregion
	}
}
