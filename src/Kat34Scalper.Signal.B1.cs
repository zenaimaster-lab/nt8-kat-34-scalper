/*
 * Kat34Scalper.Signal.B1.cs — Bot Signal sub-module B1: 34bounce8+ (partial class Kat34Scalper).
 * Independent Bot Signal B1 (34bounce8+ setup — 34+8+Bounce ema34 touch pending entry).
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
		// --- B1 sub-module state ---
		private volatile bool cachedB1;
		private volatile bool b1BackfillPending;
		// private SignalRecord b1SellRecord;
		// private SignalRecord b1BuyRecord;
		// private string b1SellTextTag = "";
		// private string b1BuyTextTag = "";
		// private int b1SellState;
		// private int b1BuyState;

		private void SetB1Signal(bool on)
		{
			cachedB1 = on;
			B1Enabled = on;
			Print(string.Format("[Kat34Scalper][SignalB1] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				b1BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				b1BackfillPending = false;
				TriggerCustomEvent(o =>
				{
					CancelSignalBotEntry("B1", "B1 off");
					ClearSignalDrawings("B1");
				}, null);
			}
		}

		private void BackfillB1()
		{
			if (!cachedB1) return;
			if (ema8 == null || fastEma == null || slowEma == null || ema144 == null || ema200 == null) return;
			if (CurrentBars == null || CurrentBars[0] < 200) return;

			int start = FindHistoryStartBarsAgo(B1HistoryDays);
			int replay = 0;
			for (int ago = start; ago >= 0; ago--)
			{
				bool sellAllowed, buyAllowed;
				PassFiltersAt(ago, out sellAllowed, out buyAllowed);
				int dir = KatSignalCore.B1Direction(
					B1CondEma8Above34, B1CondEma34Above89, B1CondEma89Above144, B1CondEma144Above200,
					ema8[ago], fastEma[ago], slowEma[ago], ema144[ago], ema200[ago], 0.10);

				if (dir > 0 && buyAllowed)
				{
					DrawSignal(true, CurrentBars[0] - ago, Highs[0][ago], Lows[0][ago],
						Highs[0][ago], Highs[0][ago], B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, true, "B1");
					replay++;
				}
				else if (dir < 0 && sellAllowed)
				{
					DrawSignal(false, CurrentBars[0] - ago, Highs[0][ago], Lows[0][ago],
						Lows[0][ago], Lows[0][ago], B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, true, "B1");
					replay++;
				}
			}
			Print(string.Format("[Kat34Scalper][SignalB1] backfill done ({0} replay lines)", replay));
		}

		private void EvaluateB1Bar(bool sellAllowed, bool buyAllowed)
		{
			if (!cachedB1) return;
			if (ema8 == null || fastEma == null || slowEma == null || ema144 == null || ema200 == null) return;
			if (CurrentBars == null || CurrentBars[0] < 200) return;

			bool a1LongAllows = !cachedAlertA1 || a1LastDir > 0;
			bool a1ShortAllows = !cachedAlertA1 || a1LastDir < 0;
			int dir = KatSignalCore.B1Direction(
				B1CondEma8Above34, B1CondEma34Above89, B1CondEma89Above144, B1CondEma144Above200,
				ema8[0], fastEma[0], slowEma[0], ema144[0], ema200[0], 0.10);

			if (dir > 0 && buyAllowed && a1LongAllows)
			{
				DrawSignal(true, CurrentBars[0], Highs[0][0], Lows[0][0],
					Highs[0][0], Highs[0][0], B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, false, "B1");
				TrySubmitBotEntry(true, Highs[0][0], B1EntryOffsetTicks, "B1");
			}
			else if (dir < 0 && sellAllowed && a1ShortAllows)
			{
				DrawSignal(false, CurrentBars[0], Highs[0][0], Lows[0][0],
					Lows[0][0], Lows[0][0], B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, false, "B1");
				TrySubmitBotEntry(false, Lows[0][0], B1EntryOffsetTicks, "B1");
			}
		}
	}
}
