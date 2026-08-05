/*
 * KatB2.cs — Standalone Bot Signal B2 (89uturn34) chart-only.
 * Appears under Add Indicators → KAT → KatB2.
 * Draws phase markers + entry/SL/TP on fire. NO bot orders (use Kat34Scalper for bot).
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatB2 : Indicator
	{
		private EMA fastEma, slowEma;
		private readonly KatA1State sellState = new KatA1State();
		private readonly KatA1State buyState = new KatA1State();
		private bool backfilled;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"KatB2 — 89uturn34 signal (standalone chart-only, no bot).";
				Name = "KatB2";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				IsSuspendedWhileInactive = true;

				HistoryDays = 3;
				EmaFastPeriod = 34;
				EmaSlowPeriod = 89;
				MaxSequenceBars = 30;
				EntryOffsetTicks = 1;
				StopDistanceTicks = 60;
				TargetDistanceTicks = 120;
				LineLengthBars = 7;
				LineWidth = 2;
				ArrowOffsetTicks = 3;
				BuyEntryColor = Colors.LimeGreen;
				SellEntryColor = Colors.Red;
				SlColor = Colors.Red;
				TpColor = Colors.Green;
			}
			else if (State == State.DataLoaded)
			{
				fastEma = EMA(EmaFastPeriod);
				slowEma = EMA(EmaSlowPeriod);
				backfilled = false;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBar < Math.Max(EmaFastPeriod, EmaSlowPeriod)) return;
			if (fastEma == null || slowEma == null) return;

			if (!backfilled && (State == State.Realtime || CurrentBar >= Bars.Count - 1))
			{
				Backfill();
				backfilled = true;
			}
			if (State != State.Realtime) return;

			RunBar(0, false, sellState, buyState);
		}

		private void Backfill()
		{
			int warm = Math.Max(EmaFastPeriod, EmaSlowPeriod);
			int start = Math.Min(HistoryStartBarsAgo(HistoryDays), CurrentBar - warm);
			if (start < 0) return;
			var tmpSell = new KatA1State();
			var tmpBuy = new KatA1State();
			for (int ago = start; ago >= 0; ago--)
				RunBar(ago, true, tmpSell, tmpBuy);
			sellState.CopyFrom(tmpSell);
			buyState.CopyFrom(tmpBuy);
			Print(string.Format("[KatB2] backfill — {0} day(s), sellPhase={1}, buyPhase={2}", HistoryDays, sellState.Phase, buyState.Phase));
		}

		private void RunBar(int ago, bool replay, KatA1State sSell, KatA1State sBuy)
		{
			double high = High[ago], low = Low[ago], close = Close[ago];
			double fast = fastEma[ago], slow = slowEma[ago];
			int sellPhaseBefore = sSell.Phase;
			int buyPhaseBefore = sBuy.Phase;
			bool sellTouchedBefore = sSell.Touched89;
			bool buyTouchedBefore = sBuy.Touched89;

			KatSignalKind? sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
				fast < slow, high, low, close, fast, slow, sSell);
			KatSignalKind? buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
				fast > slow, high, low, close, fast, slow, sBuy);

			if (sellSignal == KatSignalKind.Sell)
				DrawLevels(false, CurrentBar - ago, high, low, sSell.C1, sSell.C2);
			if (buySignal == KatSignalKind.Buy)
				DrawLevels(true, CurrentBar - ago, high, low, sBuy.C1, sBuy.C2);

			if (sSell.Phase != sellPhaseBefore)
				DrawPhase(false, ago, high, low, sSell.Phase, sSell.Touched89);
			if (sBuy.Phase != buyPhaseBefore)
				DrawPhase(true, ago, high, low, sBuy.Phase, sBuy.Touched89);
			if (!sellTouchedBefore && sSell.Touched89 && sSell.Phase == 2)
				DrawPhase(false, ago, high, low, 2, true);
			if (!buyTouchedBefore && sBuy.Touched89 && sBuy.Phase == 2)
				DrawPhase(true, ago, high, low, 2, true);
		}

		private void DrawPhase(bool isBuy, int ago, double high, double low, int phase, bool touched)
		{
			string label;
			if (phase == 1) label = "B2-arm";
			else if (phase == 2) label = touched ? "B2-pull-T" : "B2-pull";
			else return;
			string tag = "KATB2_ST_" + (isBuy ? "B" : "S") + "_" + (CurrentBar - ago);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = isBuy ? Brushes.DodgerBlue : Brushes.OrangeRed;
			Draw.Text(this, tag, label, ago, y, brush);
		}

		private void DrawLevels(bool isBuy, int bar, double high, double low, double c1, double c2)
		{
			int age = CurrentBar - bar;
			if (age < 0) return;
			double tick = TickSize;
			double ref1 = c1 != 0 ? c1 : (isBuy ? high : low);
			double ref2 = c2 != 0 ? c2 : ref1;
			double entry = Kat34ScalperLogic.EffectiveEntry(isBuy, ref1, ref2, EntryOffsetTicks, tick);
			double sl = isBuy ? entry - StopDistanceTicks * tick : entry + StopDistanceTicks * tick;
			double tp = isBuy ? entry + TargetDistanceTicks * tick : entry - TargetDistanceTicks * tick;

			int end = Math.Max(0, age - Math.Max(1, LineLengthBars));
			int w = Math.Max(1, Math.Min(LineWidth, 10));
			string p = string.Format("KATB2_{0}_{1}_", isBuy ? "B" : "S", bar);

			Draw.Line(this, p + "E", false, age, entry, end, entry, new SolidColorBrush(isBuy ? BuyEntryColor : SellEntryColor), DashStyleHelper.Solid, w);
			Draw.Line(this, p + "SL", false, age, sl, end, sl, new SolidColorBrush(SlColor), DashStyleHelper.Dash, w);
			Draw.Line(this, p + "TP", false, age, tp, end, tp, new SolidColorBrush(TpColor), DashStyleHelper.Dash, w);

			string tx = p + "TX";
			double y = isBuy ? low - ArrowOffsetTicks * tick : high + ArrowOffsetTicks * tick;
			Draw.Text(this, tx, isBuy ? "Buy B2" : "Sell B2", age, y, new SolidColorBrush(isBuy ? BuyEntryColor : SellEntryColor));
		}

		private int HistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Time[0].Subtract(TimeSpan.FromDays(days));
			int ago = 0;
			while (ago < CurrentBar && Time[ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		private static Color ParseColor(string value, Color fallback)
		{
			try
			{
				var c = ColorConverter.ConvertFromString(value);
				if (c != null) return (Color)c;
			}
			catch { }
			return fallback;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 1, GroupName = "1. KatB2 — 89uturn34")]
		public int HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 2, GroupName = "1. KatB2 — 89uturn34")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 3, GroupName = "1. KatB2 — 89uturn34")]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max Sequence Bars", Order = 4, GroupName = "1. KatB2 — 89uturn34")]
		public int MaxSequenceBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 5, GroupName = "1. KatB2 — 89uturn34")]
		public int EntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 6, GroupName = "1. KatB2 — 89uturn34")]
		public int StopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 7, GroupName = "1. KatB2 — 89uturn34")]
		public int TargetDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Length (bars)", Order = 8, GroupName = "2. Draw")]
		public int LineLengthBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width", Order = 9, GroupName = "2. Draw")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Offset (ticks)", Order = 10, GroupName = "2. Draw")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Buy Entry Color", Order = 11, GroupName = "2. Draw")]
		public Color BuyEntryColor { get; set; }
		[Browsable(false)]
		public string BuyEntryColorSerializable { get { return BuyEntryColor.ToString(); } set { BuyEntryColor = ParseColor(value, Colors.LimeGreen); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Sell Entry Color", Order = 12, GroupName = "2. Draw")]
		public Color SellEntryColor { get; set; }
		[Browsable(false)]
		public string SellEntryColorSerializable { get { return SellEntryColor.ToString(); } set { SellEntryColor = ParseColor(value, Colors.Red); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "SL Color", Order = 13, GroupName = "2. Draw")]
		public Color SlColor { get; set; }
		[Browsable(false)]
		public string SlColorSerializable { get { return SlColor.ToString(); } set { SlColor = ParseColor(value, Colors.Red); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "TP Color", Order = 14, GroupName = "2. Draw")]
		public Color TpColor { get; set; }
		[Browsable(false)]
		public string TpColorSerializable { get { return TpColor.ToString(); } set { TpColor = ParseColor(value, Colors.Green); } }
		#endregion
	}
}
