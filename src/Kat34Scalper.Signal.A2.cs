/*
 * Kat34Scalper.Signal.A2.cs — Signal sub-module A2: 34+8+Bounce (partial class Kat34Scalper).
 * Independent: own toggle, own settings group ("3.5 Signal A2 — 34+8+Bounce"), own drawings.
 * Spec in docs/SIGNALS.md. Buy (Sell mirrored):
 *   trend stack — ema8 >= ema34 (touch allowed, no cross down), ema34 > ema89 > ema144 > ema200
 *     (each condition individually toggleable in settings).
 *   setup     — price runs above ema34, pulls back and TOUCHES ema34 (wick low <= ema34) while
 *               CLOSING above it -> pending stop LONG at the touch candle's HIGH (wick included)
 *               + Entry Offset. A later touch candle with a lower high migrates the entry down
 *               (a higher high would already have filled the stop). A close below ema34 — or
 *               trend loss — cancels the entry (no entry if the touch candle closes below).
 * No stage markers (single-phase setup). Drawings: entry/SL/TP lines + "Buy A2"/"Sell A2" text
 * at the entry candle (per-side text color). No filters gate A2 yet (future filters plug into
 * the Filter module). All math runs on the chart's primary series.
 * Default OFF. When switched ON, BackfillA2 computes + draws over "History Days" (default 3)
 * and hands the replayed states to the live machines.
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
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- A2 sub-module state ---
		private volatile bool cachedA2 = false;   // HUD toggle: A2 on/off (default OFF)
		private volatile bool a2BackfillPending;  // set on enable; consumed once by FlushBackfill
		private EMA ema8;                          // fixed period 8 (fanEmas starts at 9)
		private readonly KatA2State a2SellState = new KatA2State();
		private readonly KatA2State a2BuyState = new KatA2State();
		private KatSignalRecord a2SellRecord;     // live pending-entry drawing (null when inactive)
		private KatSignalRecord a2BuyRecord;
		private string a2SellTextTag;             // "Buy A2"/"Sell A2" label at the entry candle
		private string a2BuyTextTag;

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every A2 drawing (entry/SL/TP lines + labels) — nothing else is touched.
		private void SetA2Signal(bool on)
		{
			cachedA2 = on;
			A2Enabled = on;
			if (on)
			{
				a2BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				a2BackfillPending = false;
				a2SellState.Reset();
				a2BuyState.Reset();
				TriggerCustomEvent(o => ClearA2Drawings(), null);
			}
		}

		// Trend stack per side — each leg individually toggleable in settings.
		// ema8 leg allows an exact touch (>= / <=): crossing through kills it.
		private bool A2BuyTrendOk(double e8, double e34, double e89, double e144, double e200)
		{
			return (!A2CondEma8Above34 || e8 >= e34)
				&& (!A2CondEma34Above89 || e34 > e89)
				&& (!A2CondEma89Above144 || e89 > e144)
				&& (!A2CondEma144Above200 || e144 > e200);
		}

		private bool A2SellTrendOk(double e8, double e34, double e89, double e144, double e200)
		{
			return (!A2CondEma8Above34 || e8 <= e34)
				&& (!A2CondEma34Above89 || e34 < e89)
				&& (!A2CondEma89Above144 || e89 < e144)
				&& (!A2CondEma144Above200 || e144 < e200);
		}

		private void EvaluateA2(double high, double low, double close)
		{
			if (!cachedA2 || ema8 == null || fanEmas == null) return;
			if (CurrentBars[0] < FanPeriods[FanPeriods.Length - 1]) return; // ema200 warmup
			RunA2Bar(0, false, a2SellState, a2BuyState,
				ref a2SellRecord, ref a2BuyRecord, ref a2SellTextTag, ref a2BuyTextTag);
		}

		// One bar of both A2 sides — live (ago 0, replay false) or backfill replay.
		private void RunA2Bar(int ago, bool replay, KatA2State sellState, KatA2State buyState,
			ref KatSignalRecord sellRecord, ref KatSignalRecord buyRecord,
			ref string sellTextTag, ref string buyTextTag)
		{
			double high = Highs[0][ago];
			double low = Lows[0][ago];
			double close = Closes[0][ago];
			double e8 = ema8[ago];
			double e34 = fanEmas[0][2][ago];
			double e89 = fanEmas[0][4][ago];
			double e144 = fanEmas[0][5][ago];
			double e200 = fanEmas[0][6][ago];

			KatA2Action sellAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell,
				A2SellTrendOk(e8, e34, e89, e144, e200), high, low, close, e34, A2EntryOffsetTicks, TickSize, sellState);
			KatA2Action buyAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy,
				A2BuyTrendOk(e8, e34, e89, e144, e200), high, low, close, e34, A2EntryOffsetTicks, TickSize, buyState);

			A2HandleAction(sellAction, false, ago, replay, sellState, ref sellRecord, ref sellTextTag);
			A2HandleAction(buyAction, true, ago, replay, buyState, ref buyRecord, ref buyTextTag);

			if (sellAction != KatA2Action.None || buyAction != KatA2Action.None)
				Print(string.Format("[Kat34Scalper][A2] bar {0} sell={1}, buy={2}",
					CurrentBars[0] - ago, sellAction, buyAction));
		}

		private void A2HandleAction(KatA2Action action, bool isBuy, int ago, bool replay,
			KatA2State s, ref KatSignalRecord record, ref string textTag)
		{
			if (action == KatA2Action.None) return;
			int bar = CurrentBars[0] - ago;
			double high = Highs[0][ago];
			double low = Lows[0][ago];

			if (action == KatA2Action.NewEntry)
			{
				record = DrawSignal(isBuy, bar, high, low, s.RefExtreme, s.RefExtreme,
					A2EntryOffsetTicks, A2StopDistanceTicks, A2TargetDistanceTicks, replay, "A2");
				record.KeepAlive = true; // pending entry — lines must not fade after Line Length bars
				textTag = DrawA2Text(isBuy, ago, high, low);
				if (!replay) TrySubmitBotEntry(isBuy, s.RefExtreme, A2EntryOffsetTicks, "A2");
			}
			else if (action == KatA2Action.Migrate)
			{
				// Entry moved to this candle's better extreme — rebuild the drawing at the new bar/price.
				if (record != null)
				{
					RemoveSignalRecordDrawings(record);
					FillSignalRecord(record, isBuy, bar, high, low, s.RefExtreme, s.RefExtreme,
						A2EntryOffsetTicks, A2StopDistanceTicks, A2TargetDistanceTicks);
					RenderSignal(record);
				}
				if (textTag != null) RemoveDrawObject(textTag);
				textTag = DrawA2Text(isBuy, ago, high, low);
			}
			else if (action == KatA2Action.Cancel)
			{
				if (record != null) { RemoveSignalRecord(record); record = null; }
				if (textTag != null) { RemoveDrawObject(textTag); textTag = null; }
				if (!replay) CancelA2BotEntry(isBuy, "A2 entry cancelled (close beyond ema34 / trend lost)");
			}
			else // Filled — setup done; drawing fades per Line Length from here, label stays on the candle
			{
				if (record != null) record.KeepAlive = false;
				record = null;
				textTag = null;
			}
		}

		// "Buy A2" (below the candle, buy text color) / "Sell A2" (above, sell color) at the entry candle.
		private string DrawA2Text(bool isBuy, int ago, double high, double low)
		{
			string tag = "K34S_A2_TX_" + (isBuy ? "B" : "S") + "_" + (CurrentBars[0] - ago);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = new SolidColorBrush(isBuy ? BuyTextColor : SellTextColor);
			Draw.Text(this, tag, isBuy ? "Buy A2" : "Sell A2", ago, y, brush);
			return tag;
		}

		// A2 switched OFF: drop A2-owned signal records + every K34S_A2_* drawing (lines + labels).
		private void ClearA2Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "A2");
			RemoveModuleDrawings("K34S_A2_");
			a2SellRecord = null;
			a2BuyRecord = null;
			a2SellTextTag = null;
			a2BuyTextTag = null;
		}

		// Cancels the bot's pending entry only when it belongs to A2 on this side.
		private void CancelA2BotEntry(bool isBuy, string reason)
		{
			if (pendingOrder != null && pendingOrderOwner == "A2" && pendingIsBuy == isBuy)
				CancelPendingBotOrder(reason);
		}

		// One-shot replay over the last A2HistoryDays with temp states; drawings appear/disappear
		// exactly as live (cancelled setups are removed, only the surviving pendings remain).
		// No bot orders and no alert sounds during replay. Temp states + drawing refs sync to live.
		private void BackfillA2()
		{
			int warm = FanPeriods[FanPeriods.Length - 1];
			int start = Math.Min(FindHistoryStartBarsAgo(A2HistoryDays), CurrentBars[0] - warm);
			if (start < 0) return;
			var tmpSell = new KatA2State();
			var tmpBuy = new KatA2State();
			KatSignalRecord sellRecord = null;
			KatSignalRecord buyRecord = null;
			string sellTextTag = null;
			string buyTextTag = null;
			for (int ago = start; ago >= 0; ago--)
				RunA2Bar(ago, true, tmpSell, tmpBuy, ref sellRecord, ref buyRecord, ref sellTextTag, ref buyTextTag);
			a2SellState.CopyFrom(tmpSell);
			a2BuyState.CopyFrom(tmpBuy);
			a2SellRecord = sellRecord;
			a2BuyRecord = buyRecord;
			a2SellTextTag = sellTextTag;
			a2BuyTextTag = buyTextTag;
			Print(string.Format("[Kat34Scalper][A2] backfill done — {0} day(s), {1} bar(s) replayed; live states synced (sell active {2}, buy active {3}).",
				A2HistoryDays, start + 1, a2SellState.Active, a2BuyState.Active));
		}
	}
}
