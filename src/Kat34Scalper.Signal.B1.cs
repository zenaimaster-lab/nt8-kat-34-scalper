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
		private readonly KatA2State b1SellState = new KatA2State();
		private readonly KatA2State b1BuyState = new KatA2State();
		private KatSignalRecord b1SellRecord;     // live pending-entry drawing (null when inactive)
		private KatSignalRecord b1BuyRecord;
		private string b1SellTextTag;             // "Buy B1"/"Sell B1" label at the entry candle
		private string b1BuyTextTag;
		private bool b1GateInit;                  // gate-transition diagnostic state (Filter [GATE] pattern)
		private bool b1LastBuyTrend;
		private bool b1LastSellTrend;
		private int b1ReplayEntries;              // backfill replay counters — prove the window had setups
		private int b1ReplayCancels;
		private int b1ReplayFills;

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every B1 drawing (entry/SL/TP lines + labels) — nothing else is touched.
		private void SetB1Signal(bool on)
		{
			cachedB1 = on;
			B1Enabled = on;
			Print(string.Format("[Kat34Scalper][B1] toggled {0}", on ? "ON — backfilling History Days" : "OFF — drawings removed"));
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

		private bool B1BuyTrendOk(double e8, double e34, double e89, double e144, double e200)
		{
			return (!B1CondEma8Above34 || e8 >= e34)
				&& (!B1CondEma34Above89 || e34 > e89)
				&& (!B1CondEma89Above144 || e89 > e144)
				&& (!B1CondEma144Above200 || e144 > e200);
		}

		private bool B1SellTrendOk(double e8, double e34, double e89, double e144, double e200)
		{
			return (!B1CondEma8Above34 || e8 <= e34)
				&& (!B1CondEma34Above89 || e34 < e89)
				&& (!B1CondEma89Above144 || e89 < e144)
				&& (!B1CondEma144Above200 || e144 < e200);
		}

		private void EvaluateB1(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedB1 || fastEma == null || slowEma == null || ema144 == null || ema200 == null) return;
			if (CurrentBars[0] < 200) return; // ema200 warmup
			Account acc = ResolveBotAccount();
			if (IsSignalInTrade("B1") || HasOpenPosition(acc)) return;
			RunB1Bar(0, false, b1SellState, b1BuyState,
				ref b1SellRecord, ref b1BuyRecord, ref b1SellTextTag, ref b1BuyTextTag, sellAllowed, buyAllowed);
		}

		private void RunB1Bar(int ago, bool replay, KatA2State sellState, KatA2State buyState,
			ref KatSignalRecord sellRecord, ref KatSignalRecord buyRecord,
			ref string sellTextTag, ref string buyTextTag, bool sellAllowed = true, bool buyAllowed = true)
		{
			double high = Highs[0][ago];
			double low = Lows[0][ago];
			double close = Closes[0][ago];
			double e8 = ema8[ago];
			double e34 = fastEma[ago];
			double e89 = slowEma[ago];
			double e144 = ema144[ago];
			double e200 = ema200[ago];

			bool sellTrend = sellAllowed && B1SellTrendOk(e8, e34, e89, e144, e200);
			bool buyTrend = buyAllowed && B1BuyTrendOk(e8, e34, e89, e144, e200);

			if (ago == 0 && (!b1GateInit || b1LastBuyTrend != buyTrend || b1LastSellTrend != sellTrend))
			{
				b1GateInit = true;
				b1LastBuyTrend = buyTrend;
				b1LastSellTrend = sellTrend;
				Print(string.Format("[Kat34Scalper][B1][GATE] bar {0} buyTrend={1}, sellTrend={2}, buyActive={3}, sellActive={4}, e8={5:F2}, e34={6:F2}, e89={7:F2}, e144={8:F2}, e200={9:F2}",
					CurrentBars[0], buyTrend, sellTrend, buyState.Active, sellState.Active, e8, e34, e89, e144, e200));
			}

			KatA2Action sellAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell,
				sellTrend, high, low, close, e34, B1EntryOffsetTicks, TickSize, sellState);
			KatA2Action buyAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy,
				buyTrend, high, low, close, e34, B1EntryOffsetTicks, TickSize, buyState);

			B1HandleAction(sellAction, false, ago, replay, sellState, ref sellRecord, ref sellTextTag);
			B1HandleAction(buyAction, true, ago, replay, buyState, ref buyRecord, ref buyTextTag);

			if (sellAction != KatA2Action.None || buyAction != KatA2Action.None)
				Print(string.Format("[Kat34Scalper][B1] bar {0} sell={1}, buy={2}",
					CurrentBars[0] - ago, sellAction, buyAction));
		}

		private void B1HandleAction(KatA2Action action, bool isBuy, int ago, bool replay,
			KatA2State s, ref KatSignalRecord record, ref string textTag)
		{
			if (action == KatA2Action.None) return;
			int bar = CurrentBars[0] - ago;
			double high = Highs[0][ago];
			double low = Lows[0][ago];

			if (action == KatA2Action.NewEntry)
			{
				if (replay) b1ReplayEntries++;
				record = DrawSignal(isBuy, bar, high, low, s.RefExtreme, s.RefExtreme,
					B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks, replay, "B1");
				record.KeepAlive = true; // pending entry — lines must not fade after Line Length bars
				textTag = DrawB1Text(isBuy, ago, high, low);
				if (!replay) TrySubmitBotEntry(isBuy, s.RefExtreme, B1EntryOffsetTicks, "B1");
			}
			else if (action == KatA2Action.Migrate)
			{
				if (record != null)
				{
					RemoveSignalRecordDrawings(record);
					FillSignalRecord(record, isBuy, bar, high, low, s.RefExtreme, s.RefExtreme,
						B1EntryOffsetTicks, B1StopDistanceTicks, B1TargetDistanceTicks);
					RenderSignal(record);
				}
				if (textTag != null) RemoveDrawObject(textTag);
				textTag = DrawB1Text(isBuy, ago, high, low);
			}
			else if (action == KatA2Action.Cancel)
			{
				if (replay) b1ReplayCancels++;
				if (record != null) { RemoveSignalRecord(record); record = null; }
				if (textTag != null) { RemoveDrawObject(textTag); textTag = null; }
				if (!replay) CancelB1BotEntry(isBuy, "B1 entry cancelled (close beyond ema34 / trend lost)");
			}
			else
			{
				if (replay) b1ReplayFills++;
				if (record != null) record.KeepAlive = false;
				record = null;
				textTag = null;
			}
		}

		private string DrawB1Text(bool isBuy, int ago, double high, double low)
		{
			string tag = "K34S_B1_TX_" + (isBuy ? "B" : "S") + "_" + (CurrentBars[0] - ago);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = new SolidColorBrush(isBuy ? BuyTextColor : SellTextColor);
			Draw.Text(this, tag, isBuy ? "Buy B1" : "Sell B1", ago, y, brush);
			return tag;
		}

		private void ClearB1Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "B1");
			RemoveModuleDrawings("K34S_B1_");
			b1SellRecord = null;
			b1BuyRecord = null;
			b1SellTextTag = null;
			b1BuyTextTag = null;
		}

		private void CancelB1BotEntry(bool isBuy, string reason)
		{
			if (pendingOrder != null && pendingOrderOwner == "B1" && pendingIsBuy == isBuy)
				CancelPendingBotOrder(reason);
		}

		private void BackfillB1()
		{
			int warm = 200;
			int start = Math.Min(FindHistoryStartBarsAgo(B1HistoryDays), CurrentBars[0] - warm);
			if (start < 0) return;
			var tmpSell = new KatA2State();
			var tmpBuy = new KatA2State();
			KatSignalRecord sellRecord = null;
			KatSignalRecord buyRecord = null;
			string sellTextTag = null;
			string buyTextTag = null;
			b1ReplayEntries = 0;
			b1ReplayCancels = 0;
			b1ReplayFills = 0;
			for (int ago = start; ago >= 0; ago--)
			{
				bool sellAllowed, buyAllowed;
				PassFiltersAt(ago, out sellAllowed, out buyAllowed);
				RunB1Bar(ago, true, tmpSell, tmpBuy, ref sellRecord, ref buyRecord, ref sellTextTag, ref buyTextTag, sellAllowed, buyAllowed);
			}
			b1SellState.CopyFrom(tmpSell);
			b1BuyState.CopyFrom(tmpBuy);
			b1SellRecord = sellRecord;
			b1BuyRecord = buyRecord;
			b1SellTextTag = sellTextTag;
			b1BuyTextTag = buyTextTag;
			Print(string.Format("[Kat34Scalper][B1] backfill done — {0} day(s), {1} bar(s) replayed: {2} entries, {3} cancels, {4} fills; live states synced (sell active {5}, buy active {6}).",
				B1HistoryDays, start + 1, b1ReplayEntries, b1ReplayCancels, b1ReplayFills, b1SellState.Active, b1BuyState.Active));
		}
	}
}
