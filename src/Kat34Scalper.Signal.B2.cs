/*
 * Kat34Scalper.Signal.B2.cs — Bot Signal sub-module B2: 89uturn34 (partial class Kat34Scalper).
 * Independent Bot Signal B2 (89uturn34 setup — 89-34 pullback setup).
 * Controls bot entry placement when Bot is ON. Spec in docs/SIGNALS.md.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using KAT.Signals;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- B2 sub-module state ---
		private volatile bool cachedB2;
		private volatile bool b2BackfillPending;
		// private SignalRecord b2SellRecord;
		// private SignalRecord b2BuyRecord;
		// private string b2SellTextTag = "";
		// private string b2BuyTextTag = "";
		// private int b2SellState;
		// private int b2BuyState;

		private void SetB2Signal(bool on)
		{
			cachedB2 = on;
			B2Enabled = on;
			Print(string.Format("[Kat34Scalper][SignalB2] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				b2BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				b2BackfillPending = false;
				TriggerCustomEvent(o =>
				{
					CancelSignalBotEntry("B2", "B2 off");
					ClearSignalDrawings("B2");
				}, null);
			}
		}

		private void BackfillB2()
		{
			if (!cachedB2) return;
			if (fastEma == null || slowEma == null) return;
			if (CurrentBars == null || CurrentBars[0] < 100) return;

			int start = FindHistoryStartBarsAgo(B2HistoryDays);
			int replay = 0;
			for (int ago = start; ago >= 1; ago--)
			{
				bool sellAllowed, buyAllowed;
				PassFiltersAt(ago, out sellAllowed, out buyAllowed);
				int dir = KatSignalCore.B2Direction(fastEma[ago], slowEma[ago], slowEma[ago + 1]);

				if (dir > 0 && buyAllowed)
				{
					DrawSignal(true, CurrentBars[0] - ago, Highs[0][ago], Lows[0][ago],
						Highs[0][ago], Highs[0][ago], B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, true, "B2");
					replay++;
				}
				else if (dir < 0 && sellAllowed)
				{
					DrawSignal(false, CurrentBars[0] - ago, Highs[0][ago], Lows[0][ago],
						Lows[0][ago], Lows[0][ago], B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, true, "B2");
					replay++;
				}
			}
			Print(string.Format("[Kat34Scalper][SignalB2] backfill done ({0} replay lines)", replay));
		}

		private void EvaluateB2Bar(bool sellAllowed, bool buyAllowed)
		{
			if (!cachedB2) return;
			if (fastEma == null || slowEma == null) return;
			if (CurrentBars == null || CurrentBars[0] < 100) return;

			bool a1LongAllows = !cachedAlertA1 || a1LastDir > 0;
			bool a1ShortAllows = !cachedAlertA1 || a1LastDir < 0;
			int dir = KatSignalCore.B2Direction(fastEma[0], slowEma[0], slowEma[1]);

			if (dir > 0 && buyAllowed && a1LongAllows)
			{
				DrawSignal(true, CurrentBars[0], Highs[0][0], Lows[0][0],
					Highs[0][0], Highs[0][0], B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, false, "B2");
				TrySubmitBotEntry(true, Highs[0][0], B2EntryOffsetTicks, "B2");
			}
			else if (dir < 0 && sellAllowed && a1ShortAllows)
			{
				DrawSignal(false, CurrentBars[0], Highs[0][0], Lows[0][0],
					Lows[0][0], Lows[0][0], B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, false, "B2");
				TrySubmitBotEntry(false, Lows[0][0], B2EntryOffsetTicks, "B2");
			}
		}
	}
}
