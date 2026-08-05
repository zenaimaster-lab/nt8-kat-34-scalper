/*
 * KatB1.cs — Standalone Bot Signal B1 (34bounce8+) chart-only.
 * Appears under Add Indicators → KAT → KatB1.
 * Draws pending entry/SL/TP + Buy/Sell B1 labels. NO bot orders (use Kat34Scalper for bot).
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
	public class KatB1 : Indicator
	{
		private EMA ema8, ema34, ema89, ema144, ema200;
		private readonly KatA2State sellState = new KatA2State();
		private readonly KatA2State buyState = new KatA2State();
		private string sellTextTag, buyTextTag;
		private int sellBar = -1, buyBar = -1;
		private bool backfilled;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"KatB1 — 34bounce8+ signal (standalone chart-only, no bot).";
				Name = "KatB1";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				IsSuspendedWhileInactive = true;

				HistoryDays = 3;
				CondEma8Above34 = true;
				CondEma34Above89 = true;
				CondEma89Above144 = true;
				CondEma144Above200 = true;
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
				BuyTextColor = Colors.LimeGreen;
				SellTextColor = Colors.Red;
			}
			else if (State == State.DataLoaded)
			{
				ema8 = EMA(8);
				ema34 = EMA(34);
				ema89 = EMA(89);
				ema144 = EMA(144);
				ema200 = EMA(200);
				backfilled = false;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBar < 200) return;
			if (ema8 == null || ema34 == null || ema89 == null || ema144 == null || ema200 == null) return;

			if (!backfilled && (State == State.Realtime || CurrentBar >= Bars.Count - 1))
			{
				Backfill();
				backfilled = true;
			}
			if (State != State.Realtime) return;

			RunBar(0, false, sellState, buyState, ref sellBar, ref buyBar, ref sellTextTag, ref buyTextTag);
			RefreshPendingLines();
		}

		private void Backfill()
		{
			int start = Math.Min(HistoryStartBarsAgo(HistoryDays), CurrentBar - 200);
			if (start < 0) return;
			var tmpSell = new KatA2State();
			var tmpBuy = new KatA2State();
			int sBar = -1, bBar = -1;
			string sTag = null, bTag = null;
			for (int ago = start; ago >= 0; ago--)
				RunBar(ago, true, tmpSell, tmpBuy, ref sBar, ref bBar, ref sTag, ref bTag);
			sellState.CopyFrom(tmpSell);
			buyState.CopyFrom(tmpBuy);
			sellBar = sBar;
			buyBar = bBar;
			sellTextTag = sTag;
			buyTextTag = bTag;
			RefreshPendingLines();
			Print(string.Format("[KatB1] backfill — {0} day(s), sellActive={1}, buyActive={2}", HistoryDays, sellState.Active, buyState.Active));
		}

		private void RunBar(int ago, bool replay, KatA2State sSell, KatA2State sBuy,
			ref int sBar, ref int bBar, ref string sTag, ref string bTag)
		{
			double high = High[ago], low = Low[ago], close = Close[ago];
			double e8 = ema8[ago], e34 = ema34[ago], e89 = ema89[ago], e144 = ema144[ago], e200 = ema200[ago];

			bool sellTrend = (!CondEma8Above34 || e8 <= e34)
				&& (!CondEma34Above89 || e34 < e89)
				&& (!CondEma89Above144 || e89 < e144)
				&& (!CondEma144Above200 || e144 < e200);
			bool buyTrend = (!CondEma8Above34 || e8 >= e34)
				&& (!CondEma34Above89 || e34 > e89)
				&& (!CondEma89Above144 || e89 > e144)
				&& (!CondEma144Above200 || e144 > e200);

			KatA2Action sellAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, sellTrend, high, low, close, e34, EntryOffsetTicks, TickSize, sSell);
			KatA2Action buyAction = Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, buyTrend, high, low, close, e34, EntryOffsetTicks, TickSize, sBuy);

			Handle(sellAction, false, ago, sSell, ref sBar, ref sTag);
			Handle(buyAction, true, ago, sBuy, ref bBar, ref bTag);
		}

		private void Handle(KatA2Action action, bool isBuy, int ago, KatA2State s, ref int bar, ref string textTag)
		{
			if (action == KatA2Action.None) return;
			int absBar = CurrentBar - ago;
			double high = High[ago], low = Low[ago];

			if (action == KatA2Action.NewEntry || action == KatA2Action.Migrate)
			{
				bar = absBar;
				if (textTag != null) RemoveDrawObject(textTag);
				textTag = DrawLabel(isBuy, ago, high, low);
				// Live pending lines redrawn in RefreshPendingLines from state.
			}
			else if (action == KatA2Action.Cancel)
			{
				ClearSide(isBuy, ref bar, ref textTag);
			}
			else // Filled — keep history lines via snapshot, drop keep-alive pending
			{
				DrawSnapshot(isBuy, absBar, high, low, s.RefExtreme);
				bar = -1;
				textTag = null;
			}
		}

		private void RefreshPendingLines()
		{
			// Clear previous pending tags then redraw active sides.
			RemoveByPrefix("KATB1_PEND_");
			if (sellState.Active && sellBar >= 0)
				DrawLevels(false, sellBar, sellState.RefExtreme, "PEND");
			if (buyState.Active && buyBar >= 0)
				DrawLevels(true, buyBar, buyState.RefExtreme, "PEND");
		}

		private void DrawSnapshot(bool isBuy, int bar, double high, double low, double refExtreme)
		{
			DrawLevels(isBuy, bar, refExtreme, "SNAP");
		}

		private void DrawLevels(bool isBuy, int bar, double refExtreme, string kind)
		{
			int age = CurrentBar - bar;
			if (age < 0) return;
			double tick = TickSize;
			double entry = isBuy
				? refExtreme + EntryOffsetTicks * tick
				: refExtreme - EntryOffsetTicks * tick;
			double sl = isBuy ? entry - StopDistanceTicks * tick : entry + StopDistanceTicks * tick;
			double tp = isBuy ? entry + TargetDistanceTicks * tick : entry - TargetDistanceTicks * tick;

			int end = Math.Max(0, age - Math.Max(1, LineLengthBars));
			int w = Math.Max(1, Math.Min(LineWidth, 10));
			string p = string.Format("KATB1_{0}_{1}_{2}_", kind, isBuy ? "B" : "S", bar);

			Draw.Line(this, p + "E", false, age, entry, end, entry, new SolidColorBrush(isBuy ? BuyEntryColor : SellEntryColor), DashStyleHelper.Solid, w);
			Draw.Line(this, p + "SL", false, age, sl, end, sl, new SolidColorBrush(SlColor), DashStyleHelper.Dash, w);
			Draw.Line(this, p + "TP", false, age, tp, end, tp, new SolidColorBrush(TpColor), DashStyleHelper.Dash, w);
		}

		private string DrawLabel(bool isBuy, int ago, double high, double low)
		{
			string tag = "KATB1_TX_" + (isBuy ? "B" : "S") + "_" + (CurrentBar - ago);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = new SolidColorBrush(isBuy ? BuyTextColor : SellTextColor);
			Draw.Text(this, tag, isBuy ? "Buy B1" : "Sell B1", ago, y, brush);
			return tag;
		}

		private void ClearSide(bool isBuy, ref int bar, ref string textTag)
		{
			if (textTag != null) { RemoveDrawObject(textTag); textTag = null; }
			if (bar >= 0)
			{
				RemoveByPrefix(string.Format("KATB1_PEND_{0}_{1}_", isBuy ? "B" : "S", bar));
				bar = -1;
			}
		}

		private int HistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Time[0].Subtract(TimeSpan.FromDays(days));
			int ago = 0;
			while (ago < CurrentBar && Time[ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		private void RemoveByPrefix(string prefix)
		{
			try
			{
				var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					string tag = tool.Tag as string;
					if (name != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) doomed.Add(name);
					if (tag != null && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) doomed.Add(tag);
				}
				foreach (string t in doomed) RemoveDrawObject(t);
			}
			catch { }
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
		[Display(Name = "History Days", Order = 1, GroupName = "1. KatB1 — 34bounce8+")]
		public int HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 8 above EMA 34", Order = 2, GroupName = "1. KatB1 — 34bounce8+")]
		public bool CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 above EMA 89", Order = 3, GroupName = "1. KatB1 — 34bounce8+")]
		public bool CondEma34Above89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 89 above EMA 144", Order = 4, GroupName = "1. KatB1 — 34bounce8+")]
		public bool CondEma89Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 144 above EMA 200", Order = 5, GroupName = "1. KatB1 — 34bounce8+")]
		public bool CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 6, GroupName = "1. KatB1 — 34bounce8+")]
		public int EntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 7, GroupName = "1. KatB1 — 34bounce8+")]
		public int StopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 8, GroupName = "1. KatB1 — 34bounce8+")]
		public int TargetDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Length (bars)", Order = 9, GroupName = "2. Draw")]
		public int LineLengthBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width", Order = 10, GroupName = "2. Draw")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Offset (ticks)", Order = 11, GroupName = "2. Draw")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Buy Entry Color", Order = 12, GroupName = "2. Draw")]
		public Color BuyEntryColor { get; set; }
		[Browsable(false)]
		public string BuyEntryColorSerializable { get { return BuyEntryColor.ToString(); } set { BuyEntryColor = ParseColor(value, Colors.LimeGreen); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Sell Entry Color", Order = 13, GroupName = "2. Draw")]
		public Color SellEntryColor { get; set; }
		[Browsable(false)]
		public string SellEntryColorSerializable { get { return SellEntryColor.ToString(); } set { SellEntryColor = ParseColor(value, Colors.Red); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "SL Color", Order = 14, GroupName = "2. Draw")]
		public Color SlColor { get; set; }
		[Browsable(false)]
		public string SlColorSerializable { get { return SlColor.ToString(); } set { SlColor = ParseColor(value, Colors.Red); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "TP Color", Order = 15, GroupName = "2. Draw")]
		public Color TpColor { get; set; }
		[Browsable(false)]
		public string TpColorSerializable { get { return TpColor.ToString(); } set { TpColor = ParseColor(value, Colors.Green); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Buy Text Color", Order = 16, GroupName = "2. Draw")]
		public Color BuyTextColor { get; set; }
		[Browsable(false)]
		public string BuyTextColorSerializable { get { return BuyTextColor.ToString(); } set { BuyTextColor = ParseColor(value, Colors.LimeGreen); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Sell Text Color", Order = 17, GroupName = "2. Draw")]
		public Color SellTextColor { get; set; }
		[Browsable(false)]
		public string SellTextColorSerializable { get { return SellTextColor.ToString(); } set { SellTextColor = ParseColor(value, Colors.Red); } }
		#endregion
	}
}
