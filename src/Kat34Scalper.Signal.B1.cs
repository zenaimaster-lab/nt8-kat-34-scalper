/*
 * Kat34Scalper.Signal.B1.cs — Bot Signal sub-module B1: 89-34 pullback (partial class Kat34Scalper).
 * Independent Bot Signal B1 (89-34 pullback setup).
 * Standardized to B1 (Bot Signal). Controls bot entry placement when Bot is ON.
 * Stages per side (Sell mirrored by Buy) — full spec in docs/SIGNALS.md.
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
		// --- B1 sub-module state ---
		private volatile bool cachedB1 = false;   // HUD toggle: B1 on/off (default OFF)
		private volatile bool b1BackfillPending;  // set on enable; consumed once by FlushBackfill
		private readonly KatA1State b1SellState = new KatA1State(); // sell-side sequence
		private readonly KatA1State b1BuyState = new KatA1State();  // buy-side sequence

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every B1 drawing — nothing else is touched.
		private void SetB1Signal(bool on)
		{
			cachedB1 = on;
			B1Enabled = on;
			if (on)
			{
				b1BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				b1BackfillPending = false;
				b1SellState.Reset();
				b1BuyState.Reset();
				TriggerCustomEvent(o => { CancelSignalBotEntry("B1", "B1 switched OFF"); ClearB1Drawings(); }, null);
			}
		}

		private void EvaluateB1(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedB1 || fastEma == null || slowEma == null) return;
			if (CurrentBars[0] < Math.Max(EmaFastPeriod, EmaSlowPeriod)) return;
			Account acc = ResolveBotAccount();
			if (IsSignalInTrade("B1") || HasOpenPosition(acc)) return;

			double fast = fastEma[0];
			double slow = slowEma[0];
			int sellPhaseBefore = b1SellState.Phase;
			int buyPhaseBefore = b1BuyState.Phase;
			bool sellTouchedBefore = b1SellState.Touched89;
			bool buyTouchedBefore = b1BuyState.Touched89;
			KatSignalKind? sellSignal = null;
			KatSignalKind? buySignal = null;

			sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
				fast < slow, high, low, close, fast, slow, b1SellState);
			buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
				fast > slow, high, low, close, fast, slow, b1BuyState);

			if (sellSignal == KatSignalKind.Sell)
			{
				if (sellAllowed)
				{
					DrawSignal(false, CurrentBar, high, low, b1SellState.C1, b1SellState.C2, B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, false, "B1");
					TrySubmitBotEntry(false, b1SellState.C2, B1EntryOffsetTicks, "B1");
				}
				else
					Print(string.Format("[Kat34Scalper][B1] bar {0} SELL result suppressed by filters; A0={1}, allowed={2}",
						CurrentBar, diagnosticA0Dir, sellAllowed));
			}
			if (buySignal == KatSignalKind.Buy)
			{
				if (buyAllowed)
				{
					DrawSignal(true, CurrentBar, high, low, b1BuyState.C1, b1BuyState.C2, B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, false, "B1");
					TrySubmitBotEntry(true, b1BuyState.C2, B1EntryOffsetTicks, "B1");
				}
				else
					Print(string.Format("[Kat34Scalper][B1] bar {0} BUY result suppressed by filters; A0={1}, allowed={2}",
						CurrentBar, diagnosticA0Dir, buyAllowed));
			}

			if (b1SellState.Phase != sellPhaseBefore)
			{
				DrawB1PhaseMarkerAt(false, 0, high, low, b1SellState.Phase, b1SellState.Touched89);
				Print(string.Format("[Kat34Scalper][B1] bar {0} SELL phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, sellPhaseBefore, b1SellState.Phase, sellAllowed, fast < slow, close, fast, slow));
			}
			if (b1BuyState.Phase != buyPhaseBefore)
			{
				DrawB1PhaseMarkerAt(true, 0, high, low, b1BuyState.Phase, b1BuyState.Touched89);
				Print(string.Format("[Kat34Scalper][B1] bar {0} BUY phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, buyPhaseBefore, b1BuyState.Phase, buyAllowed, fast > slow, close, fast, slow));
			}
			if (!sellTouchedBefore && b1SellState.Touched89 && b1SellState.Phase == 2)
				DrawB1PhaseMarkerAt(false, 0, high, low, 2, true);
			if (!buyTouchedBefore && b1BuyState.Touched89 && b1BuyState.Phase == 2)
				DrawB1PhaseMarkerAt(true, 0, high, low, 2, true);

			if (sellSignal.HasValue || buySignal.HasValue)
				Print(string.Format("[Kat34Scalper][B1] bar {0} result sell={1}, buy={2}",
					CurrentBar, sellSignal.HasValue ? sellSignal.Value.ToString() : "none",
					buySignal.HasValue ? buySignal.Value.ToString() : "none"));
		}

		private void DrawB1PhaseMarkerAt(bool isBuy, int barsAgo, double high, double low, int phase, bool touched)
		{
			string label;
			if (phase == 1) label = "B1-arm";
			else if (phase == 2) label = touched ? "B1-pull-T" : "B1-pull";
			else return;
			string tag = "K34S_B1ST_" + (isBuy ? "B" : "S") + "_" + (CurrentBars[0] - barsAgo);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = isBuy ? Brushes.DodgerBlue : Brushes.OrangeRed;
			Draw.Text(this, tag, label, barsAgo, y, brush);
		}

		private void BackfillB1()
		{
			int warm = Math.Max(EmaFastPeriod, EmaSlowPeriod);
			int start = Math.Min(FindHistoryStartBarsAgo(B1HistoryDays), CurrentBars[0] - warm);
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
				PassFiltersAt(ago, out sellAllowed, out buyAllowed);

				KatSignalKind? sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
					f < sl, h, l, c, f, sl, tmpSell);
				KatSignalKind? buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
					f > sl, h, l, c, f, sl, tmpBuy);

				if (sellSignal == KatSignalKind.Sell && sellAllowed)
					DrawSignal(false, CurrentBars[0] - ago, h, l, tmpSell.C1, tmpSell.C2, B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, true, "B1");
				if (buySignal == KatSignalKind.Buy && buyAllowed)
					DrawSignal(true, CurrentBars[0] - ago, h, l, tmpBuy.C1, tmpBuy.C2, B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, true, "B1");

				if (tmpSell.Phase != sellPhaseBefore)
					DrawB1PhaseMarkerAt(false, ago, h, l, tmpSell.Phase, tmpSell.Touched89);
				if (tmpBuy.Phase != buyPhaseBefore)
					DrawB1PhaseMarkerAt(true, ago, h, l, tmpBuy.Phase, tmpBuy.Touched89);
				if (!sellTouchedBefore && tmpSell.Touched89 && tmpSell.Phase == 2)
					DrawB1PhaseMarkerAt(false, ago, h, l, 2, true);
				if (!buyTouchedBefore && tmpBuy.Touched89 && tmpBuy.Phase == 2)
					DrawB1PhaseMarkerAt(true, ago, h, l, 2, true);
			}
			b1SellState.CopyFrom(tmpSell);
			b1BuyState.CopyFrom(tmpBuy);
			Print(string.Format("[Kat34Scalper][B1] backfill done — {0} day(s), {1} bar(s) replayed; live states synced (sell phase {2}, buy phase {3}).",
				B1HistoryDays, start + 1, b1SellState.Phase, b1BuyState.Phase));
		}

		private void ClearB1Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "B1");
			RemoveModuleDrawings("K34S_B1_");
			RemoveModuleDrawings("K34S_B1ST_");
		}
	}
}
