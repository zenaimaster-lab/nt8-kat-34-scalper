/*
 * Kat34Scalper.Signal.A4.cs — Signal sub-module A4: OCO Previous Bar High/Low (partial class Kat34Scalper).
 * Independent: own toggle, own settings group ("3.7 Signal A4 — OCO Prev Bar"), own drawings.
 * Spec:
 *   Always creates a BUY signal at previous bar High (+ offset) and a SELL signal at previous bar Low (- offset).
 *   OCO pair order submission: when 1 order fills, the other is cancelled.
 *   Stop-to-limit conversion: if price ran past the trigger price, convert StopMarket to Limit order.
 *   Priority rules:
 *     - Maximum 1 Buy signal and 1 Sell signal simultaneously for A4.
 *     - Buy level priority: always select the LOWEST Buy price.
 *     - Sell level priority: always select the HIGHEST Sell price.
 */

#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- A4 sub-module state ---
		private volatile bool cachedA4 = false;   // HUD toggle: A4 on/off (default OFF)
		private volatile bool a4BackfillPending;  // set on enable; consumed once by FlushBackfill

		private double a4ActiveBuyPrice = 0;
		private double a4ActiveSellPrice = 0;

		// HUD entry point. ON: compute + draw History Days window immediately.
		// OFF: remove every A4 drawing — nothing else is touched.
		private void SetA4Signal(bool on)
		{
			cachedA4 = on;
			A4Enabled = on;
			Print(string.Format("[Kat34Scalper][A4] toggled {0}", on ? "ON — backfilling History Days" : "OFF — drawings removed"));
			if (on)
			{
				a4BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				a4BackfillPending = false;
				a4ActiveBuyPrice = 0;
				a4ActiveSellPrice = 0;
				TriggerCustomEvent(o => { CancelSignalBotEntry("A4", "A4 switched OFF"); ClearA4Drawings(); }, null);
			}
		}

		private void EvaluateA4(double high, double low, double close)
		{
			if (!cachedA4) return;
			if (CurrentBars[0] < 1) return;

			Account acc = ResolveBotAccount();
			if (a4InTrade || IsSignalInTrade("A4") || HasOpenPosition(acc))
			{
				ClearA4Drawings();
				return;
			}

			double prevHigh = Highs[0][1];
			double prevLow = Lows[0][1];

			double buyPrice = prevHigh + A4EntryOffsetTicks * TickSize;
			double sellPrice = prevLow - A4EntryOffsetTicks * TickSize;

			a4ActiveBuyPrice = buyPrice;
			a4ActiveSellPrice = sellPrice;

			// Clear previous bar's A4 lines so only the current active OCO lines remain on chart
			ClearA4Drawings();

			// Draw Buy signal line (refExtreme = prevHigh), pass replay = true so alert sound is NOT spammed on every bar
			double refBuyExtreme = prevHigh;
			DrawSignal(true, CurrentBar, refBuyExtreme, refBuyExtreme, 0, 0,
				A4EntryOffsetTicks, A4StopDistanceTicks, A4TargetDistanceTicks, true, "A4");

			// Draw Sell signal line (refExtreme = prevLow), pass replay = true so alert sound is NOT spammed on every bar
			double refSellExtreme = prevLow;
			DrawSignal(false, CurrentBar, refSellExtreme, refSellExtreme, 0, 0,
				A4EntryOffsetTicks, A4StopDistanceTicks, A4TargetDistanceTicks, true, "A4");

			TrySubmitA4BotOcoEntries(buyPrice, sellPrice);
		}

		// A4 switched OFF: drop A4-owned signal records + every K34S_A4_* drawing.
		private void ClearA4Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "A4");
			RemoveModuleDrawings("K34S_A4_");
		}

		// One-shot replay over the last A4HistoryDays: draws OCO prev bar signals at each bar.
		private void BackfillA4()
		{
			int start = Math.Min(FindHistoryStartBarsAgo(A4HistoryDays), CurrentBars[0] - 1);
			if (start < 0) return;
			int signals = 0;

			for (int ago = start; ago >= 0; ago--)
			{
				if (CurrentBars[0] - ago < 1) continue;
				double pHigh = Highs[0][ago + 1];
				double pLow = Lows[0][ago + 1];

				int bar = CurrentBars[0] - ago;

				DrawSignal(true, bar, pHigh, pHigh, 0, 0,
					A4EntryOffsetTicks, A4StopDistanceTicks, A4TargetDistanceTicks, true, "A4");
				DrawSignal(false, bar, pLow, pLow, 0, 0,
					A4EntryOffsetTicks, A4StopDistanceTicks, A4TargetDistanceTicks, true, "A4");
				signals += 2;
			}
			Print(string.Format("[Kat34Scalper][A4] backfill done — {0} day(s), {1} bar(s) replayed: {2} A4 signal line(s).",
				A4HistoryDays, start + 1, signals));
		}
	}
}
