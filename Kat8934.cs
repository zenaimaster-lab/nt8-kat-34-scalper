/*
 * Kat8934.cs
 * Version: 0.02 (2026-08-01)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy) with entry, SL, TP dash lines.
 */

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat8934;
#endregion

public enum Kat8934TriggerMode
{
	[Display(Name = "Retest Bounce")]
	RetestBounce = 0,
	[Display(Name = "Breakdown")]
	Breakdown = 1
}

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class Kat8934 : Indicator
	{
		#region Metadata & State
		public const string VERSION = "0.02";
		public const string RELEASE_DATE = "2026-08-01";

		// 1. Chuẩn bị — section reserved in settings (added later). No properties yet.
		private EMA sellFastEma;
		private EMA sellSlowEma;
		private EMA buyFastEma;
		private EMA buySlowEma;
		private bool sellTouched89;
		private bool sellUturned;
		private bool buyTouched89;
		private bool buyUturned;
		private bool versionDrawn;
		#endregion

		#region Indicator Lifecycle
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Kat8934 v" + VERSION + @" — EMA 34/89 rejection signals (Sell/Buy) with entry, SL and TP dash lines.";
				Name						= "Kat8934";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= false;
				IsAutoScale					= false;
				DrawHorizontalGridLines		= false;
				DrawVerticalGridLines		= false;

				// Parameters
				ShowVersion					= true;

				// 2. Sell Signal defaults
				SellEnabled					= true;
				SellEmaFastPeriod			= 34;
				SellEmaSlowPeriod			= 89;
				SellTriggerMode				= Kat8934TriggerMode.RetestBounce;
				SellEntryOffsetTicks		= 1;
				SellStopDistanceTicks		= 60;
				SellTargetDistanceTicks		= 120;

				// 3. Buy Signal defaults
				BuyEnabled					= true;
				BuyEmaFastPeriod			= 34;
				BuyEmaSlowPeriod			= 89;
				BuyTriggerMode				= Kat8934TriggerMode.RetestBounce;
				BuyEntryOffsetTicks			= 1;
				BuyStopDistanceTicks		= 60;
				BuyTargetDistanceTicks		= 120;
			}
			else if (State == State.DataLoaded)
			{
				sellFastEma = EMA(BarsArray[0], SellEmaFastPeriod);
				sellSlowEma = EMA(BarsArray[0], SellEmaSlowPeriod);
				buyFastEma  = EMA(BarsArray[0], BuyEmaFastPeriod);
				buySlowEma  = EMA(BarsArray[0], BuyEmaSlowPeriod);
				Print(string.Format("[Kat8934] v{0} ({1}) loaded.", VERSION, RELEASE_DATE));
			}
		}
		#endregion

		#region Signal Evaluation & Drawing
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;

			if (ShowVersion && !versionDrawn)
			{
				versionDrawn = true;
				Draw.TextFixed(this, "K8934_version", string.Format("Kat8934 v{0} ({1})", VERSION, RELEASE_DATE), TextPosition.TopLeft);
			}

			double high = Highs[0][0];
			double low = Lows[0][0];
			double close = Closes[0][0];

			if (SellEnabled && sellFastEma != null && sellSlowEma != null
				&& CurrentBars[0] >= Math.Max(SellEmaFastPeriod, SellEmaSlowPeriod))
			{
				double fast = sellFastEma[0];
				double slow = sellSlowEma[0];
				if (Kat8934Logic.Update(KatSignalKind.Sell, ToLogicMode(SellTriggerMode),
					fast < slow, high, low, close, fast, slow,
					ref sellTouched89, ref sellUturned) == KatSignalKind.Sell)
				{
					DrawSignal(false, CurrentBar, high, low, SellEntryOffsetTicks, SellStopDistanceTicks, SellTargetDistanceTicks);
				}
			}

			if (BuyEnabled && buyFastEma != null && buySlowEma != null
				&& CurrentBars[0] >= Math.Max(BuyEmaFastPeriod, BuyEmaSlowPeriod))
			{
				double fast = buyFastEma[0];
				double slow = buySlowEma[0];
				if (Kat8934Logic.Update(KatSignalKind.Buy, ToLogicMode(BuyTriggerMode),
					fast > slow, high, low, close, fast, slow,
					ref buyTouched89, ref buyUturned) == KatSignalKind.Buy)
				{
					DrawSignal(true, CurrentBar, high, low, BuyEntryOffsetTicks, BuyStopDistanceTicks, BuyTargetDistanceTicks);
				}
			}
		}

		private static KatTriggerMode ToLogicMode(Kat8934TriggerMode mode)
		{
			return mode == Kat8934TriggerMode.Breakdown ? KatTriggerMode.Breakdown : KatTriggerMode.RetestBounce;
		}

		private void DrawSignal(bool isBuy, int bar, double high, double low, int offsetTicks, int stopTicks, int targetTicks)
		{
			double tick = TickSize;
			double entryPrice;
			double arrowY;
			double textY;

			if (isBuy)
			{
				entryPrice = high + offsetTicks * tick; // buy stop above signal high
				arrowY = low - tick;
				textY = low - 2 * tick;
			}
			else
			{
				entryPrice = low - offsetTicks * tick; // sell stop below signal low
				arrowY = high + tick;
				textY = high + 2 * tick;
			}

			double slPrice = isBuy ? entryPrice - stopTicks * tick : entryPrice + stopTicks * tick;
			double tpPrice = isBuy ? entryPrice + targetTicks * tick : entryPrice - targetTicks * tick;

			// ponytail: arrows use double-width pixel brush instead of PriceLevels to avoid needing an extra PriceLevel registration
			if (isBuy)
			{
				Draw.ArrowUp(this, "K8934_B_ARROW_" + bar, false, bar, arrowY, Brushes.Green);
				Draw.Text(this, "K8934_B_TEXT_" + bar, "BUY", bar, textY, Brushes.Green);
				Draw.Line(this, "K8934_B_ENTRY_" + bar, false, bar, entryPrice, -5, entryPrice, Brushes.Gold, DashStyleHelper.Dash, 2);
				Draw.Line(this, "K8934_B_SL_" + bar, false, bar, slPrice, -5, slPrice, Brushes.Red, DashStyleHelper.Dash, 1);
				Draw.Line(this, "K8934_B_TP_" + bar, false, bar, tpPrice, -5, tpPrice, Brushes.Green, DashStyleHelper.Dash, 1);
			}
			else
			{
				Draw.ArrowDown(this, "K8934_S_ARROW_" + bar, false, bar, arrowY, Brushes.Red);
				Draw.Text(this, "K8934_S_TEXT_" + bar, "SELL", bar, textY, Brushes.Red);
				Draw.Line(this, "K8934_S_ENTRY_" + bar, false, bar, entryPrice, -5, entryPrice, Brushes.Gold, DashStyleHelper.Dash, 2);
				Draw.Line(this, "K8934_S_SL_" + bar, false, bar, slPrice, -5, slPrice, Brushes.Red, DashStyleHelper.Dash, 1);
				Draw.Line(this, "K8934_S_TP_" + bar, false, bar, tpPrice, -5, tpPrice, Brushes.Green, DashStyleHelper.Dash, 1);
			}

			Print(string.Format("[Kat8934] {0} signal @ bar {1} — entry {2:F5}, SL {3:F5}, TP {4:F5}", isBuy ? "BUY" : "SELL", bar, entryPrice, slPrice, tpPrice));
		}
		#endregion

		#region NinjaScript Properties
		// 1. Chuẩn bị — reserved settings group, added later.

		[NinjaScriptProperty]
		[Display(Name = "Show Version Label", Order = 0, GroupName = "Parameters")]
		public bool ShowVersion { get; set; }

		// --- 2. Sell Signal ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "2. Sell Signal")]
		public bool SellEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 2, GroupName = "2. Sell Signal")]
		public int SellEmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 3, GroupName = "2. Sell Signal")]
		public int SellEmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Mode", Order = 4, GroupName = "2. Sell Signal",
			Description = "Retest Bounce: fire when price closes back above the fast EMA after the U-turn close below it. Breakdown: fire immediately on the U-turn close below the fast EMA.")]
		public Kat8934TriggerMode SellTriggerMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks below signal low)", Order = 5, GroupName = "2. Sell Signal")]
		public int SellEntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 6, GroupName = "2. Sell Signal")]
		public int SellStopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 7, GroupName = "2. Sell Signal")]
		public int SellTargetDistanceTicks { get; set; }

		// --- 3. Buy Signal ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "3. Buy Signal")]
		public bool BuyEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 2, GroupName = "3. Buy Signal")]
		public int BuyEmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 3, GroupName = "3. Buy Signal")]
		public int BuyEmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Mode", Order = 4, GroupName = "3. Buy Signal",
			Description = "Retest Bounce: fire when price closes back below the fast EMA after the U-turn close above it. Breakdown: fire immediately on the U-turn close above the fast EMA.")]
		public Kat8934TriggerMode BuyTriggerMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks above signal high)", Order = 5, GroupName = "3. Buy Signal")]
		public int BuyEntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 6, GroupName = "3. Buy Signal")]
		public int BuyStopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 7, GroupName = "3. Buy Signal")]
		public int BuyTargetDistanceTicks { get; set; }
		#endregion
	}
}
