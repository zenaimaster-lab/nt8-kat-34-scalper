/*
 * Kat34Scalper.Signal.B2.cs — Bot Signal sub-module B2: 34+8+Bounce (partial class Kat34Scalper).
 * Independent Bot Signal B2 (34+8+Bounce setup).
 * Standardized to B2 (Bot Signal). Controls bot entry placement when Bot is ON.
 * Spec in docs/SIGNALS.md.
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
		// --- B2 sub-module state ---
		private volatile bool cachedB2 = false;   // HUD toggle: B2 on/off (default OFF)
		private volatile bool b2BackfillPending;  // set on enable; consumed once by FlushBackfill
		private readonly KatA2State b2SellState = new KatA2State();
		private readonly KatA2State b2BuyState = new KatA2State();
		private KatSignalRecord b2SellRecord;     // live pending-entry drawing (null when inactive)
		private KatSignalRecord b2BuyRecord;
		private string b2SellTextTag;             // "Buy B2"/"Sell B2" label at the entry candle
		private string b2BuyTextTag;
		private bool b2GateInit;                  // gate-transition diagnostic state (Filter [GATE] pattern)
		private bool b2LastBuyTrend;
		private bool b2LastSellTrend;
		private int b2ReplayEntries;              // backfill replay counters — prove the window had setups
		private int b2ReplayCancels;
		private int b2ReplayFills;

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every B2 drawing (entry/SL/TP lines + labels) — nothing else is touched.
		private void SetB2Signal(bool on)
		{
			cachedB2 = on;
			B2Enabled = on;
			Print(string.Format("[Kat34Scalper][B2] toggled {0}", on ? "ON — backfilling History Days" : "OFF — drawings removed"));
			if (on)
			{
				b2BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				b2BackfillPending = false;
				b2SellState.Reset();
				b2BuyState.Reset();
				TriggerCustomEvent(o => { CancelSignalBotEntry("B2", "B2 switched OFF"); ClearB2Drawings(); }, null);
			}
		}

		private bool B2BuyTrendOk(double e8, double e34, double e89, double e144, double e200)
		{
			return (!B2CondEma8Above34 || e8 >= e34)
				&& (!B2CondEma34Above89 || e34 > e89)
				&& (!B2CondEma89Above144 || e89 > e144)
				&& (!B2CondEma144Above200 || e144 > e200);
		}

		private bool B2SellTrendOk(double e8, double e34, double e89, double e144, double e200)
		{
			return (!B2CondEma8Above34 || e8 <= e34)
				&& (!B2CondEma34Above89 || e34 < e89)
				&& (!B2CondEma89Above144 || e89 < e144)
				&& (!B2CondEma144Above200 || e144 < e200);
		}

		private void EvaluateB2(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedB2 || fastEma == null || slowEma == null || ema144 == null || ema200 == null) return;
			if (CurrentBars[0] < 200) return; // ema200 warmup
			Account acc = ResolveBotAccount();
			if (IsSignalInTrade("B2") || HasOpenPosition(acc)) return;
			RunB2Bar(0, false, b2SellState, b2BuyState,
				ref b2SellRecord, ref b2BuyRecord, ref b2SellTextTag, ref b2BuyTextTag, sellAllowed, buyAllowed);
		}

		private void RunB2Bar(int ago, bool replay, KatA2State sellState, KatA2State buyState,
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

			bool sellTrend = sellAllowed && B2SellTrendOk(e8, e34, e89, e144, e200);
			bool buyTrend = buyAllowed && B2BuyTrendOk(e8, e34, e89, e144, e200);

			if (ago == 0 && (!b2GateInit || b2LastBuyTrend != buyTrend || b2LastSellTrend != sellTrend))
			{
				b2GateInit = true;
				b2LastBuyTrend = buyTrend;
				b2LastSellTrend = sellTrend;
				Print(string.Format("[Kat34Scalper][B2][GATE] bar {0} buyTrend={1}, sellTrend={2}, buyActive={3}, sellActive={4}, e8={5:F2}, e34={6:F2}, e89={7:F2}, e144={8:F2}, e200={9:F2}",
					CurrentBars[0], buyTrend, sellTrend, buyState.Active, sellState.Active, e8, e34, e89, e144, e200));
			}

			KatA2Action sellAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell,
				sellTrend, high, low, close, e34, B2EntryOffsetTicks, TickSize, sellState);
			KatA2Action buyAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy,
				buyTrend, high, low, close, e34, B2EntryOffsetTicks, TickSize, buyState);

			B2HandleAction(sellAction, false, ago, replay, sellState, ref sellRecord, ref sellTextTag);
			B2HandleAction(buyAction, true, ago, replay, buyState, ref buyRecord, ref buyTextTag);

			if (sellAction != KatA2Action.None || buyAction != KatA2Action.None)
				Print(string.Format("[Kat34Scalper][B2] bar {0} sell={1}, buy={2}",
					CurrentBars[0] - ago, sellAction, buyAction));
		}

		private void B2HandleAction(KatA2Action action, bool isBuy, int ago, bool replay,
			KatA2State s, ref KatSignalRecord record, ref string textTag)
		{
			if (action == KatA2Action.None) return;
			int bar = CurrentBars[0] - ago;
			double high = Highs[0][ago];
			double low = Lows[0][ago];

			if (action == KatA2Action.NewEntry)
			{
				if (replay) b2ReplayEntries++;
				record = DrawSignal(isBuy, bar, high, low, s.RefExtreme, s.RefExtreme,
					B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, replay, "B2");
				record.KeepAlive = true; // pending entry — lines must not fade after Line Length bars
				textTag = DrawB2Text(isBuy, ago, high, low);
				if (!replay) TrySubmitBotEntry(isBuy, s.RefExtreme, B2EntryOffsetTicks, "B2");
			}
			else if (action == KatA2Action.Migrate)
			{
				if (record != null)
				{
					RemoveSignalRecordDrawings(record);
					FillSignalRecord(record, isBuy, bar, high, low, s.RefExtreme, s.RefExtreme,
						B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks);
					RenderSignal(record);
				}
				if (textTag != null) RemoveDrawObject(textTag);
				textTag = DrawB2Text(isBuy, ago, high, low);
			}
			else if (action == KatA2Action.Cancel)
			{
				if (replay) b2ReplayCancels++;
				if (record != null) { RemoveSignalRecord(record); record = null; }
				if (textTag != null) { RemoveDrawObject(textTag); textTag = null; }
				if (!replay) CancelB2BotEntry(isBuy, "B2 entry cancelled (close beyond ema34 / trend lost)");
			}
			else
			{
				if (replay) b2ReplayFills++;
				if (record != null) record.KeepAlive = false;
				record = null;
				textTag = null;
			}
		}

		private string DrawB2Text(bool isBuy, int ago, double high, double low)
		{
			string tag = "K34S_B2_TX_" + (isBuy ? "B" : "S") + "_" + (CurrentBars[0] - ago);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = new SolidColorBrush(isBuy ? BuyTextColor : SellTextColor);
			Draw.Text(this, tag, isBuy ? "Buy B2" : "Sell B2", ago, y, brush);
			return tag;
		}

		private void ClearB2Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "B2");
			RemoveModuleDrawings("K34S_B2_");
			b2SellRecord = null;
			b2BuyRecord = null;
			b2SellTextTag = null;
			b2BuyTextTag = null;
		}

		private void CancelB2BotEntry(bool isBuy, string reason)
		{
			if (pendingOrder != null && pendingOrderOwner == "B2" && pendingIsBuy == isBuy)
				CancelPendingBotOrder(reason);
		}

		private void BackfillB2()
		{
			int warm = 200;
			int start = Math.Min(FindHistoryStartBarsAgo(B2HistoryDays), CurrentBars[0] - warm);
			if (start < 0) return;
			var tmpSell = new KatA2State();
			var tmpBuy = new KatA2State();
			KatSignalRecord sellRecord = null;
			KatSignalRecord buyRecord = null;
			string sellTextTag = null;
			string buyTextTag = null;
			b2ReplayEntries = 0;
			b2ReplayCancels = 0;
			b2ReplayFills = 0;
			for (int ago = start; ago >= 0; ago--)
			{
				bool sellAllowed, buyAllowed;
				PassFiltersAt(ago, out sellAllowed, out buyAllowed);
				RunB2Bar(ago, true, tmpSell, tmpBuy, ref sellRecord, ref buyRecord, ref sellTextTag, ref buyTextTag, sellAllowed, buyAllowed);
			}
			b2SellState.CopyFrom(tmpSell);
			b2BuyState.CopyFrom(tmpBuy);
			b2SellRecord = sellRecord;
			b2BuyRecord = buyRecord;
			b2SellTextTag = sellTextTag;
			b2BuyTextTag = buyTextTag;
			Print(string.Format("[Kat34Scalper][B2] backfill done — {0} day(s), {1} bar(s) replayed: {2} entries, {3} cancels, {4} fills; live states synced (sell active {5}, buy active {6}).",
				B2HistoryDays, start + 1, b2ReplayEntries, b2ReplayCancels, b2ReplayFills, b2SellState.Active, b2BuyState.Active));
		}
	}
}
