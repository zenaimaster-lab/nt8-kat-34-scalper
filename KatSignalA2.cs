/*
 * KatSignalA2.cs — Standalone Alert Signal A2 Indicator (EmaZone variant).
 * Independent NinjaTrader 8 indicator. Can be loaded on any chart.
 * Same pattern as A1: fan-based environment detection + debounce + backfill + drawing + sound.
 */

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using KAT.Signals;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatSignalA2 : Indicator
	{
		private EMA a2Ema8;
		private EMA a2Ema34;
		private EMA a2Ema89;
		private EMA a2Ema144;
		private EMA a2Ema200;
		private ATR a2Atr;

		// --- Signal A2 module state ---
		private volatile bool cachedAlertA2 = false;
		private volatile bool alertA2BackfillPending;
		private int a2LastDir;
		private int a2InvalidStreak;
		private int a2PrevDir;
		private int a2ReplayLines;
		private int a2BandDir;
		private int a2BandStartIdx;
		private double a2BandHi = double.MinValue;
		private double a2BandLo = double.MaxValue;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "KAT Signal A2 (EmaZone variant) — Independent alert-only indicator. Draws environment transitions + sounds.";
				Name = "KAT Signal A2 (EmaZone variant)";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines = true;
				PaintPriceMarkers = true;
				ScamNumber = 1;

				HistoryDays = 3;
				Ema8Period = 8;
				Ema34Period = 34;
				Ema89Period = 89;
				Ema144Period = 144;
				Ema200Period = 200;
				AtrPeriod = 14;
				BreakBars = 3;
				AngleMin = 5.0;
				AngleEnabled = true;

				AlertLongLineColor = Colors.DodgerBlue;
				AlertShortLineColor = Colors.OrangeRed;
				AlertLineWidth = 2;

				CondEma8Above34 = true;
				CondEma34Above89 = true;
				CondEma89Above144 = true;
				CondEma144Above200 = true;
			}
			else if (State == State.Configure)
			{
				// Add 5min secondary series (A2 series: BarsArray[1])
				AddDataSeries(Data.BarsPeriodType.Minute, 5);
			}
			else if (State == State.DataLoaded)
			{
				a2Ema8 = AddIndicator(new EMA() { Period = Ema8Period }) as EMA;
				a2Ema8.Plots[0].Brush = Brushes.Cyan;
				AddChartIndicator(a2Ema8);

				a2Ema34 = AddIndicator(new EMA() { Period = Ema34Period }) as EMA;
				a2Ema34.Plots[0].Brush = Brushes.Yellow;
				AddChartIndicator(a2Ema34);

				a2Ema89 = AddIndicator(new EMA() { Period = Ema89Period }) as EMA;
				a2Ema89.Plots[0].Brush = Brushes.Orange;
				AddChartIndicator(a2Ema89);

				a2Ema144 = AddIndicator(new EMA() { Period = Ema144Period }) as EMA;
				a2Ema144.Plots[0].Brush = Brushes.LimeGreen;
				AddChartIndicator(a2Ema144);

				a2Ema200 = AddIndicator(new EMA() { Period = Ema200Period }) as EMA;
				a2Ema200.Plots[0].Brush = Brushes.White;
				AddChartIndicator(a2Ema200);

				a2Atr = AddIndicator(new ATR() { Period = AtrPeriod }) as ATR;
				AddChartIndicator(a2Atr);

				SetAlertA2Signal(true);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 1) return;
			if (CurrentBars == null || CurrentBars.Length < 2) return;

			EvaluateAlertA2Bar();
		}

		private void SetAlertA2Signal(bool on)
		{
			cachedAlertA2 = on;
			Print(string.Format("[KatSignalA2] toggled {0}", on ? "ON — backfilling History Days" : "OFF"));
			if (on)
			{
				alertA2BackfillPending = true;
				TriggerCustomEvent(o => FlushAlertBackfill(), null);
			}
			else
			{
				alertA2BackfillPending = false;
				ClearAlertA2Drawings();
			}
		}

		private void FlushAlertBackfill()
		{
			if (CurrentBars == null || CurrentBars.Length == 0 || CurrentBars[1] < 1) return;
			if (alertA2BackfillPending)
			{
				alertA2BackfillPending = false;
				BackfillAlertA2();
			}
		}

		private void EvaluateAlertA2Bar()
		{
			if (!cachedAlertA2) return;
			if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[1] < 201) return;
			if (a2Ema8 == null || a2Ema34 == null || a2Ema89 == null || a2Ema144 == null || a2Ema200 == null || a2Atr == null) return;

			int rawDir = AlertA2DirectionAt(0, out double angle);
			int dir = KatSignalCore.A1DebouncedDir(rawDir, a2LastDir, a2InvalidStreak, BreakBars);
			if (Highs[1][0] > a2BandHi) a2BandHi = Highs[1][0];
			if (Lows[1][0] < a2BandLo) a2BandLo = Lows[1][0];
			if (dir != a2PrevDir)
			{
				DrawEnvBand(a2BandDir, a2BandStartIdx, CurrentBars[1], a2BandHi, a2BandLo);
				if (dir == 0) DrawAlertA2RangeLine(0);
				a2BandDir = dir;
				a2BandStartIdx = CurrentBars[1];
				a2PrevDir = dir;
				Print(string.Format("[KatSignalA2] bar {0} dir={1}, angle={2:F1}deg", CurrentBars[1], dir, angle));
			}
			else if (a2BandDir != 0)
			{
				DrawEnvBand(a2BandDir, a2BandStartIdx, CurrentBars[1], a2BandHi, a2BandLo);
			}
			bool fired = KatSignalCore.A1EdgeStep(rawDir, a2LastDir, a2InvalidStreak, BreakBars, out a2LastDir, out a2InvalidStreak);
			if (fired)
			{
				DrawAlertA2Line(a2LastDir, 0);
				if (State == State.Realtime)
				{
					PlayAlertSound();
					Print(string.Format("[KatSignalA2] {0} environment @ bar {1}", a2LastDir > 0 ? "LONG" : "SHORT", CurrentBars[1]));
				}
			}
		}

		private int AlertA2DirectionAt(int ago, out double angle)
		{
			angle = KatSignalCore.SlopeAngleDeg(a2Ema34[ago], a2Ema34[ago + 1], a2Atr[ago]);
			return KatSignalCore.A1Direction(
				CondEma8Above34, CondEma34Above89, CondEma89Above144, CondEma144Above200, AngleEnabled,
				a2Ema8[ago], a2Ema34[ago], a2Ema89[ago], a2Ema144[ago], a2Ema200[ago],
				angle, Math.Abs(AngleMin));
		}

		private void BackfillAlertA2()
		{
			if (!cachedAlertA2) return;
			if (CurrentBars == null || CurrentBars.Length < 2) return;
			int warm = 201, max = CurrentBars[1] - 1;
			if (max < warm)
			{
				a2LastDir = 0;
				Print("[KatSignalA2] backfill skipped — 5min series too short.");
				return;
			}
			int start = Math.Min(AlertA2HistoryStartBarsAgo(HistoryDays), max - warm);
			double hi = double.MinValue, lo = double.MaxValue;
			for (int ago = start; ago >= 1; ago--)
			{
				if (Highs[1][ago] > hi) hi = Highs[1][ago];
				if (Lows[1][ago] < lo) lo = Lows[1][ago];
			}
			int lastDir = 0, invalidStreak = 0, bandDir = 0, bandStartIdx = CurrentBars[1] - start;
			a2ReplayLines = 0;
			for (int ago = start; ago >= 1; ago--)
			{
				int rawDir = AlertA2DirectionAt(ago, out _);
				int dir = KatSignalCore.A1DebouncedDir(rawDir, lastDir, invalidStreak, BreakBars);
				if (dir != bandDir)
				{
					DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1] - ago, hi, lo);
					if (dir == 0) DrawAlertA2RangeLine(ago);
					bandDir = dir;
					bandStartIdx = CurrentBars[1] - ago;
				}
				bool fired = KatSignalCore.A1EdgeStep(rawDir, lastDir, invalidStreak, BreakBars, out lastDir, out invalidStreak);
				if (fired)
				{
					DrawAlertA2Line(lastDir, ago);
					a2ReplayLines++;
				}
			}
			a2LastDir = lastDir;
			a2InvalidStreak = invalidStreak;
			a2PrevDir = bandDir;
			a2BandDir = bandDir;
			a2BandStartIdx = bandStartIdx;
			a2BandHi = hi;
			a2BandLo = lo;
			DrawEnvBand(a2BandDir, a2BandStartIdx, CurrentBars[1], hi, lo);
			Print(string.Format("[KatSignalA2] backfill done — {0} day(s), {1} bar(s), {2} line(s).", HistoryDays, start, a2ReplayLines));
		}

		private int AlertA2HistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Times[1][0].Subtract(TimeSpan.FromDays(days));
			int max = CurrentBars[1];
			int ago = 0;
			while (ago < max && Times[1][ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		private void ClearAlertA2Drawings()
		{
			a2LastDir = 0;
			a2InvalidStreak = 0;
			a2PrevDir = 0;
			a2BandDir = 0;
			a2BandStartIdx = 0;
			a2BandHi = double.MinValue;
			a2BandLo = double.MaxValue;
			RemoveDrawings("K34S_ALERTA2_");
		}

		private void RemoveDrawings(string prefix)
		{
			for (int i = ChartObjects.Count - 1; i >= 0; i--)
			{
				if (ChartObjects[i].Name.StartsWith(prefix))
					RemoveChartObject(ChartObjects[i].Name);
			}
		}

		private void DrawAlertA2Line(int dir, int ago)
		{
			string tag = string.Format("K34S_ALERTA2_VL_{0}_{1}", dir > 0 ? "B" : "S", CurrentBars[1] - ago);
			Brush brush = new SolidColorBrush(dir > 0 ? AlertLongLineColor : AlertShortLineColor);
			int width = Math.Max(1, Math.Min(AlertLineWidth, 10));
			Draw.VerticalLine(this, tag, Times[1][ago], brush, DashStyleHelper.Dash, width);
		}

		private void DrawEnvBand(int dir, int startIdx, int endIdx, double hi, double lo)
		{
			if (!KatSignalCore.EnvBandAnchors(dir, startIdx, endIdx, hi, lo, CurrentBars[1], out int agoStart, out int agoEnd)) return;
			Brush area = new SolidColorBrush(dir > 0 ? Colors.Green : Colors.Red);
			string tag = string.Format("K34S_ALERTA2_BAND_{0}_{1}", dir > 0 ? "B" : "S", startIdx);
			Draw.Rectangle(this, tag, false, Times[1][agoStart], hi, Times[1][agoEnd], lo, Brushes.Transparent, area, 8);
		}

		private void DrawAlertA2RangeLine(int ago)
		{
			string tag = string.Format("K34S_ALERTA2_VR_{0}", CurrentBars[1] - ago);
			Draw.VerticalLine(this, tag, Times[1][ago], Brushes.Gray, DashStyleHelper.Dash, 2);
		}

		private void PlayAlertSound()
		{
			// TBD: sound playback
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "History Days", Order = 1, GroupName = "Parameters")]
		public int HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 8 Period", Order = 2, GroupName = "EMA Periods")]
		public int Ema8Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 34 Period", Order = 3, GroupName = "EMA Periods")]
		public int Ema34Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 89 Period", Order = 4, GroupName = "EMA Periods")]
		public int Ema89Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 144 Period", Order = 5, GroupName = "EMA Periods")]
		public int Ema144Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 200 Period", Order = 6, GroupName = "EMA Periods")]
		public int Ema200Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR Period", Order = 7, GroupName = "EMA Periods")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Break Bars", Order = 8, GroupName = "Signal")]
		public int BreakBars { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Angle Min (deg)", Order = 9, GroupName = "Signal")]
		public double AngleMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Angle Enabled", Order = 10, GroupName = "Signal")]
		public bool AngleEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA8>34", Order = 11, GroupName = "Signal Conditions")]
		public bool CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA34>89", Order = 12, GroupName = "Signal Conditions")]
		public bool CondEma34Above89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA89>144", Order = 13, GroupName = "Signal Conditions")]
		public bool CondEma89Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA144>200", Order = 14, GroupName = "Signal Conditions")]
		public bool CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Alert Long Line Color", Order = 15, GroupName = "Drawing")]
		public Brush AlertLongLineColor { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Long Line Color", Order = 16, GroupName = "Drawing")]
		public string AlertLongLineColorSerialized
		{
			get { return Serialize.BrushToString(AlertLongLineColor); }
			set { AlertLongLineColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Alert Short Line Color", Order = 17, GroupName = "Drawing")]
		public Brush AlertShortLineColor { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Short Line Color", Order = 18, GroupName = "Drawing")]
		public string AlertShortLineColorSerialized
		{
			get { return Serialize.BrushToString(AlertShortLineColor); }
			set { AlertShortLineColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Alert Line Width", Order = 19, GroupName = "Drawing")]
		public int AlertLineWidth { get; set; }
		#endregion
	}
}

#region NinjaScript generated code
namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Indicator
	{
		private KatSignalA2[] cacheKatSignalA2;

		public KatSignalA2 KatSignalA2()
		{
			return KatSignalA2(Close);
		}

		public KatSignalA2 KatSignalA2(ISeries<double> input)
		{
			if (cacheKatSignalA2 != null)
				for (int idx = 0; idx < cacheKatSignalA2.Length; idx++)
					if (cacheKatSignalA2[idx] != null && cacheKatSignalA2[idx].EqualsInput(input))
						return cacheKatSignalA2[idx];
			return CacheIndicator<KatSignalA2>(new KatSignalA2(), input, ref cacheKatSignalA2);
		}
	}
}
#endregion
