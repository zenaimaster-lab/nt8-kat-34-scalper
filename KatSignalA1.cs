/*
 * KatSignalA1.cs — Standalone Alert Signal A1 Indicator (EmaZone 30s).
 * Independent NinjaTrader 8 indicator. Can be loaded on any chart without Kat34Scalper.
 * Pure alert-only: draws vertical lines + background bands, plays alert sound on LONG/SHORT environment transitions.
 * Does NOT submit orders or interact with trading bot.
 *
 * LONG environment:  EMA8 > EMA34 > EMA89 > EMA144 > EMA200 AND EMA34 slope angle >= +Min Angle (rising).
 * SHORT environment: EMA8 < EMA34 < EMA89 < EMA144 < EMA200 AND EMA34 slope angle <= -Min Angle (falling).
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
	public class KatSignalA1 : Indicator
	{
		private EMA a1Ema8;
		private EMA a1Ema34;
		private EMA a1Ema89;
		private EMA a1Ema144;
		private EMA a1Ema200;
		private ATR a1Atr;

		// --- Signal A1 module state ---
		private volatile bool cachedAlertA1 = false;
		private volatile bool alertA1BackfillPending;
		private int a1LastDir;
		private int a1InvalidStreak;
		private int a1PrevDir;
		private int a1ReplayLines;
		private int a1BandDir;
		private int a1BandStartIdx;
		private double a1BandHi = double.MinValue;
		private double a1BandLo = double.MaxValue;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "KAT Signal A1 (EmaZone 30s) — Independent alert-only indicator. Draws environment transitions + sounds.";
				Name = "KAT Signal A1 (EmaZone 30s)";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines = true;
				PaintPriceMarkers = true;
				ScamNumber = 1;

				// Default settings (user can override)
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

				AlertLongLineColor = Colors.Blue;
				AlertShortLineColor = Colors.Red;
				AlertLineWidth = 2;

				EmaZoneTf1 = KatEmaZoneTf.M3;
				EmaZoneTf2 = KatEmaZoneTf.M5;
				EmaZoneTf3 = KatEmaZoneTf.M15;

				// Conditions
				CondEma8Above34 = true;
				CondEma34Above89 = true;
				CondEma89Above144 = true;
				CondEma144Above200 = true;
			}
			else if (State == State.Configure)
			{
				// Add 30s secondary series (A1 series: BarsArray[1])
				AddDataSeries(Data.BarsPeriodType.Second, 30);

				// Add 3 zone EMA series (BarsArray[3..5]) for EmaZone gate
				AddDataSeries(Data.BarsPeriodType.Minute, (int)EmaZoneTf1);
				AddDataSeries(Data.BarsPeriodType.Minute, (int)EmaZoneTf2);
				AddDataSeries(Data.BarsPeriodType.Minute, (int)EmaZoneTf3);
			}
			else if (State == State.DataLoaded)
			{
				// Create A1 series EMAs and ATR
				a1Ema8 = AddIndicator(new EMA() { Period = Ema8Period }) as EMA;
				a1Ema8.Plots[0].Brush = Brushes.Cyan;
				AddChartIndicator(a1Ema8);

				a1Ema34 = AddIndicator(new EMA() { Period = Ema34Period }) as EMA;
				a1Ema34.Plots[0].Brush = Brushes.Yellow;
				AddChartIndicator(a1Ema34);

				a1Ema89 = AddIndicator(new EMA() { Period = Ema89Period }) as EMA;
				a1Ema89.Plots[0].Brush = Brushes.Orange;
				AddChartIndicator(a1Ema89);

				a1Ema144 = AddIndicator(new EMA() { Period = Ema144Period }) as EMA;
				a1Ema144.Plots[0].Brush = Brushes.LimeGreen;
				AddChartIndicator(a1Ema144);

				a1Ema200 = AddIndicator(new EMA() { Period = Ema200Period }) as EMA;
				a1Ema200.Plots[0].Brush = Brushes.White;
				AddChartIndicator(a1Ema200);

				a1Atr = AddIndicator(new ATR() { Period = AtrPeriod }) as ATR;
				AddChartIndicator(a1Atr);

				// Zone EMAs (series 3/4/5)
				// TBD: zone series setup

				// Start A1 signal enabled by default
				SetAlertA1Signal(true);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 1) return; // Only evaluate on A1 series (30s)
			if (CurrentBars == null || CurrentBars.Length < 2) return;

			EvaluateAlertA1Bar();
		}

		private void SetAlertA1Signal(bool on)
		{
			cachedAlertA1 = on;
			Print(string.Format("[KatSignalA1] toggled {0}", on ? "ON — backfilling History Days" : "OFF"));
			if (on)
			{
				alertA1BackfillPending = true;
				TriggerCustomEvent(o => FlushAlertBackfill(), null);
			}
			else
			{
				alertA1BackfillPending = false;
				ClearAlertA1Drawings();
			}
		}

		private void FlushAlertBackfill()
		{
			if (CurrentBars == null || CurrentBars.Length == 0 || CurrentBars[1] < 1) return;
			if (alertA1BackfillPending)
			{
				alertA1BackfillPending = false;
				BackfillAlertA1();
			}
		}

		private void EvaluateAlertA1Bar()
		{
			if (!cachedAlertA1) return;
			if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[1] < 201) return; // ema200 warmup
			if (a1Ema8 == null || a1Ema34 == null || a1Ema89 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;

			int rawDir = AlertA1DirectionAt(0, out double angle);
			if (rawDir != 0 && !EmaZonePassAt(0, rawDir)) rawDir = 0;
			int dir = KatSignalCore.A1DebouncedDir(rawDir, a1LastDir, a1InvalidStreak, BreakBars);
			if (Highs[1][0] > a1BandHi) a1BandHi = Highs[1][0];
			if (Lows[1][0] < a1BandLo) a1BandLo = Lows[1][0];
			if (dir != a1PrevDir)
			{
				DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], a1BandHi, a1BandLo);
				if (dir == 0) DrawAlertA1RangeLine(0);
				a1BandDir = dir;
				a1BandStartIdx = CurrentBars[1];
				a1PrevDir = dir;
				Print(string.Format("[KatSignalA1] bar {0} dir={1}, angle={2:F1}deg, e8={3:F2}, e34={4:F2}, e89={5:F2}",
					CurrentBars[1], dir, angle, a1Ema8[0], a1Ema34[0], a1Ema89[0]));
			}
			else if (a1BandDir != 0)
			{
				DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], a1BandHi, a1BandLo);
			}
			bool fired = KatSignalCore.A1EdgeStep(rawDir, a1LastDir, a1InvalidStreak, BreakBars, out a1LastDir, out a1InvalidStreak);
			if (fired)
			{
				DrawAlertA1Line(a1LastDir, 0);
				if (State == State.Realtime)
				{
					PlayAlertSound();
					Print(string.Format("[KatSignalA1] {0} environment @ bar {1}", a1LastDir > 0 ? "LONG" : "SHORT", CurrentBars[1]));
				}
			}
		}

		private int AlertA1DirectionAt(int ago, out double angle)
		{
			angle = KatSignalCore.SlopeAngleDeg(a1Ema34[ago], a1Ema34[ago + 1], a1Atr[ago]);
			return KatSignalCore.A1Direction(
				CondEma8Above34, CondEma34Above89, CondEma89Above144, CondEma144Above200, AngleEnabled,
				a1Ema8[ago], a1Ema34[ago], a1Ema89[ago], a1Ema144[ago], a1Ema200[ago],
				angle, Math.Abs(AngleMin));
		}

		private bool EmaZonePassAt(int ago, int dir)
		{
			// TBD: zone gate logic (zone EMA34 check on series 3/4/5)
			return true; // gate open for now
		}

		private void BackfillAlertA1()
		{
			if (!cachedAlertA1) return;
			if (CurrentBars == null || CurrentBars.Length < 2) return;
			int warm = 201, max = CurrentBars[1] - 1;
			if (max < warm)
			{
				a1LastDir = 0;
				Print("[KatSignalA1] backfill skipped — 30s series too short.");
				return;
			}
			int start = Math.Min(AlertA1HistoryStartBarsAgo(HistoryDays), max - warm);
			double hi = double.MinValue, lo = double.MaxValue;
			for (int ago = start; ago >= 1; ago--)
			{
				if (Highs[1][ago] > hi) hi = Highs[1][ago];
				if (Lows[1][ago] < lo) lo = Lows[1][ago];
			}
			int lastDir = 0, invalidStreak = 0, bandDir = 0, bandStartIdx = CurrentBars[1] - start;
			a1ReplayLines = 0;
			for (int ago = start; ago >= 1; ago--)
			{
				int rawDir = AlertA1DirectionAt(ago, out _);
				if (rawDir != 0 && !EmaZonePassAt(ago, rawDir)) rawDir = 0;
				int dir = KatSignalCore.A1DebouncedDir(rawDir, lastDir, invalidStreak, BreakBars);
				if (dir != bandDir)
				{
					DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1] - ago, hi, lo);
					if (dir == 0) DrawAlertA1RangeLine(ago);
					bandDir = dir;
					bandStartIdx = CurrentBars[1] - ago;
				}
				bool fired = KatSignalCore.A1EdgeStep(rawDir, lastDir, invalidStreak, BreakBars, out lastDir, out invalidStreak);
				if (fired)
				{
					DrawAlertA1Line(lastDir, ago);
					a1ReplayLines++;
				}
			}
			a1LastDir = lastDir;
			a1InvalidStreak = invalidStreak;
			a1PrevDir = bandDir;
			a1BandDir = bandDir;
			a1BandStartIdx = bandStartIdx;
			a1BandHi = hi;
			a1BandLo = lo;
			DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], hi, lo);
			Print(string.Format("[KatSignalA1] backfill done — {0} day(s), {1} bar(s), {2} line(s).", HistoryDays, start, a1ReplayLines));
		}

		private int AlertA1HistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Times[1][0].Subtract(TimeSpan.FromDays(days));
			int max = CurrentBars[1];
			int ago = 0;
			while (ago < max && Times[1][ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		private void ClearAlertA1Drawings()
		{
			a1LastDir = 0;
			a1InvalidStreak = 0;
			a1PrevDir = 0;
			a1BandDir = 0;
			a1BandStartIdx = 0;
			a1BandHi = double.MinValue;
			a1BandLo = double.MaxValue;
			RemoveDrawings("K34S_ALERTA1_");
		}

		private void RemoveDrawings(string prefix)
		{
			for (int i = ChartObjects.Count - 1; i >= 0; i--)
			{
				if (ChartObjects[i].Name.StartsWith(prefix))
					RemoveChartObject(ChartObjects[i].Name);
			}
		}

		private void DrawAlertA1Line(int dir, int ago)
		{
			string tag = string.Format("K34S_ALERTA1_VL_{0}_{1}", dir > 0 ? "B" : "S", CurrentBars[1] - ago);
			Brush brush = new SolidColorBrush(dir > 0 ? AlertLongLineColor : AlertShortLineColor);
			int width = Math.Max(1, Math.Min(AlertLineWidth, 10));
			Draw.VerticalLine(this, tag, Times[1][ago], brush, DashStyleHelper.Dash, width);
		}

		private void DrawEnvBand(int dir, int startIdx, int endIdx, double hi, double lo)
		{
			if (!KatSignalCore.EnvBandAnchors(dir, startIdx, endIdx, hi, lo, CurrentBars[1], out int agoStart, out int agoEnd)) return;
			Brush area = new SolidColorBrush(dir > 0 ? Colors.Green : Colors.Red);
			string tag = string.Format("K34S_ALERTA1_BAND_{0}_{1}", dir > 0 ? "B" : "S", startIdx);
			Draw.Rectangle(this, tag, false, Times[1][agoStart], hi, Times[1][agoEnd], lo, Brushes.Transparent, area, 8);
		}

		private void DrawAlertA1RangeLine(int ago)
		{
			string tag = string.Format("K34S_ALERTA1_VR_{0}", CurrentBars[1] - ago);
			Draw.VerticalLine(this, tag, Times[1][ago], Brushes.Gray, DashStyleHelper.Dash, 2);
		}

		private void PlayAlertSound()
		{
			// TBD: sound playback
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "History Days", Description = "Backfill window in days", Order = 1, GroupName = "Parameters")]
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
		[Display(Name = "Break Bars", Description = "Debounce bars for re-arm", Order = 8, GroupName = "Signal")]
		public int BreakBars { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Angle Min (deg)", Order = 9, GroupName = "Signal")]
		public double AngleMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Angle Enabled", Order = 10, GroupName = "Signal")]
		public bool AngleEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EmaZone TF 1", Order = 11, GroupName = "EmaZone Gate")]
		public KatEmaZoneTf EmaZoneTf1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EmaZone TF 2", Order = 12, GroupName = "EmaZone Gate")]
		public KatEmaZoneTf EmaZoneTf2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EmaZone TF 3", Order = 13, GroupName = "EmaZone Gate")]
		public KatEmaZoneTf EmaZoneTf3 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA8>34", Order = 14, GroupName = "Signal Conditions")]
		public bool CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA34>89", Order = 15, GroupName = "Signal Conditions")]
		public bool CondEma34Above89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA89>144", Order = 16, GroupName = "Signal Conditions")]
		public bool CondEma89Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond EMA144>200", Order = 17, GroupName = "Signal Conditions")]
		public bool CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Alert Long Line Color", Order = 18, GroupName = "Drawing")]
		public Brush AlertLongLineColor { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Long Line Color", Order = 19, GroupName = "Drawing")]
		public string AlertLongLineColorSerialized
		{
			get { return Serialize.BrushToString(AlertLongLineColor); }
			set { AlertLongLineColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Alert Short Line Color", Order = 20, GroupName = "Drawing")]
		public Brush AlertShortLineColor { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Short Line Color", Order = 21, GroupName = "Drawing")]
		public string AlertShortLineColorSerialized
		{
			get { return Serialize.BrushToString(AlertShortLineColor); }
			set { AlertShortLineColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Alert Line Width", Order = 22, GroupName = "Drawing")]
		public int AlertLineWidth { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Indicator
	{
		private KatSignalA1[] cacheKatSignalA1;

		public KatSignalA1 KatSignalA1()
		{
			return KatSignalA1(Close);
		}

		public KatSignalA1 KatSignalA1(ISeries<double> input)
		{
			if (cacheKatSignalA1 != null)
				for (int idx = 0; idx < cacheKatSignalA1.Length; idx++)
					if (cacheKatSignalA1[idx] != null && cacheKatSignalA1[idx].EqualsInput(input))
						return cacheKatSignalA1[idx];

			return CacheIndicator<KatSignalA1>(new KatSignalA1(), input, ref cacheKatSignalA1);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns.KAT
{
	public partial class Column
	{
		public Indicators.KAT.KatSignalA1 KatSignalA1()
		{
			return indicator.KatSignalA1(Close);
		}

		public Indicators.KAT.KatSignalA1 KatSignalA1(ISeries<double> input)
		{
			return indicator.KatSignalA1(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies.KAT
{
	public partial class Strategy
	{
		public Indicators.KAT.KatSignalA1 KatSignalA1()
		{
			return indicator.KatSignalA1(Close);
		}

		public Indicators.KAT.KatSignalA1 KatSignalA1(ISeries<double> input)
		{
			return indicator.KatSignalA1(input);
		}
	}
}

#endregion
