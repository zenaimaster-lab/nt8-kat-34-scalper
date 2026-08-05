/*
 * Kat34Scalper.Signal.A2.cs — Signal A2 module: Alert-only 5-minute fan environment.
 * Runs on its own 5-minute secondary series (BarsArray[6]) and is independent from A1/B1/B2.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using KAT.Signals;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
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

		private void SetA2Signal(bool on) { SetAlertA2Signal(on); }

		private void SetAlertA2Signal(bool on)
		{
			cachedAlertA2 = on;
			AlertA2Enabled = on;
			Print(string.Format("[Kat34Scalper][AlertA2] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				alertA2BackfillPending = true;
				TriggerCustomEvent(o => FlushAlertBackfill(), null);
			}
			else
			{
				alertA2BackfillPending = false;
				TriggerCustomEvent(o => ClearAlertA2Drawings(), null);
			}
		}

		private void EvaluateAlertA2Bar()
		{
			if (!cachedAlertA2) return;
			if (CurrentBars == null || CurrentBars.Length <= AlertA2SeriesIndex || CurrentBars[AlertA2SeriesIndex] < 201) return;
			if (a2Ema8 == null || a2Ema34 == null || a2Ema89 == null || a2Ema144 == null || a2Ema200 == null || a2Atr == null) return;

			int rawDir = AlertA2DirectionAt(0, out double angle);
			int dir = KatSignalCore.A1DebouncedDir(rawDir, a2LastDir, a2InvalidStreak, 3);
			if (Highs[AlertA2SeriesIndex][0] > a2BandHi) a2BandHi = Highs[AlertA2SeriesIndex][0];
			if (Lows[AlertA2SeriesIndex][0] < a2BandLo) a2BandLo = Lows[AlertA2SeriesIndex][0];
			if (dir != a2PrevDir)
			{
				DrawAlertA2Band(a2BandDir, a2BandStartIdx, CurrentBars[AlertA2SeriesIndex], a2BandHi, a2BandLo);
				if (dir == 0)
				{
					DrawAlertA2RangeLine(0);
					if (State == State.Realtime) PlaySignalSound("A2", 0);
				}
				a2BandDir = dir;
				a2BandStartIdx = CurrentBars[AlertA2SeriesIndex];
				a2PrevDir = dir;
				Print(string.Format("[Kat34Scalper][AlertA2][FAN5m] bar {0} dir={1}, angle={2:F1}deg",
					CurrentBars[AlertA2SeriesIndex], dir, angle));
			}
			else if (a2BandDir != 0)
			{
				DrawAlertA2Band(a2BandDir, a2BandStartIdx, CurrentBars[AlertA2SeriesIndex], a2BandHi, a2BandLo);
			}

			bool fired = KatSignalCore.A1EdgeStep(rawDir, a2LastDir, a2InvalidStreak, 3, out a2LastDir, out a2InvalidStreak);
			if (fired)
			{
				DrawAlertA2Line(a2LastDir, 0);
				if (State == State.Realtime)
				{
					PlaySignalSound("A2", a2LastDir);
					Print(string.Format("[Kat34Scalper][AlertA2] {0} environment @ bar {1}.",
						a2LastDir > 0 ? "LONG" : "SHORT", CurrentBars[AlertA2SeriesIndex]));
				}
			}
		}

		private int AlertA2DirectionAt(int ago, out double angle)
		{
			angle = KatSignalCore.SlopeAngleDeg(a2Ema34[ago], a2Ema34[ago + 1], a2Atr[ago]);
			return KatSignalCore.A1Direction(
				true, true, true, true, true,
				a2Ema8[ago], a2Ema34[ago], a2Ema89[ago], a2Ema144[ago], a2Ema200[ago],
				angle, 5.0);
		}

		private void BackfillAlertA2()
		{
			if (!cachedAlertA2) return;
			if (CurrentBars == null || CurrentBars.Length <= AlertA2SeriesIndex) return;
			if (a2Ema8 == null || a2Ema34 == null || a2Ema89 == null || a2Ema144 == null || a2Ema200 == null || a2Atr == null) return;
			int warm = 201;
			int max = CurrentBars[AlertA2SeriesIndex] - 1;
			if (max < warm)
			{
				a2LastDir = 0;
				Print("[Kat34Scalper][AlertA2] backfill skipped — 5m series has no/little history yet.");
				return;
			}

			int start = Math.Min(AlertA2HistoryStartBarsAgo(AlertA2HistoryDays), max - warm);
			double hi = double.MinValue, lo = double.MaxValue;
			for (int ago = start; ago >= 1; ago--)
			{
				if (Highs[AlertA2SeriesIndex][ago] > hi) hi = Highs[AlertA2SeriesIndex][ago];
				if (Lows[AlertA2SeriesIndex][ago] < lo) lo = Lows[AlertA2SeriesIndex][ago];
			}

			int lastDir = 0;
			int invalidStreak = 0;
			int bandDir = 0;
			int bandStartIdx = CurrentBars[AlertA2SeriesIndex] - start;
			a2ReplayLines = 0;
			for (int ago = start; ago >= 1; ago--)
			{
				int rawDir = AlertA2DirectionAt(ago, out _);
				int dir = KatSignalCore.A1DebouncedDir(rawDir, lastDir, invalidStreak, 3);
				if (dir != bandDir)
				{
					DrawAlertA2Band(bandDir, bandStartIdx, CurrentBars[AlertA2SeriesIndex] - ago, hi, lo);
					if (dir == 0) DrawAlertA2RangeLine(ago);
					bandDir = dir;
					bandStartIdx = CurrentBars[AlertA2SeriesIndex] - ago;
				}
				bool fired = KatSignalCore.A1EdgeStep(rawDir, lastDir, invalidStreak, 3, out lastDir, out invalidStreak);
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
			DrawAlertA2Band(a2BandDir, a2BandStartIdx, CurrentBars[AlertA2SeriesIndex], hi, lo);
			Print(string.Format("[Kat34Scalper][AlertA2] backfill done — {0} day(s), {1} bar(s), {2} line(s).",
				AlertA2HistoryDays, start, a2ReplayLines));
		}

		private int AlertA2HistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Times[AlertA2SeriesIndex][0].Subtract(TimeSpan.FromDays(days));
			int max = CurrentBars[AlertA2SeriesIndex];
			int ago = 0;
			while (ago < max && Times[AlertA2SeriesIndex][ago] >= cutoff) ago++;
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
			RemoveModuleDrawings("K34S_ALERTA2_");
		}

		private void DrawAlertA2Line(int dir, int ago)
		{
			string tag = string.Format("K34S_ALERTA2_VL_{0}_{1}", dir > 0 ? "B" : "S", CurrentBars[AlertA2SeriesIndex] - ago);
			Brush brush = new SolidColorBrush(dir > 0 ? Colors.DodgerBlue : Colors.OrangeRed);
			Draw.VerticalLine(this, tag, Times[AlertA2SeriesIndex][ago], brush, DashStyleHelper.Dash, 2);
		}

		private void DrawAlertA2RangeLine(int ago)
		{
			string tag = string.Format("K34S_ALERTA2_VR_{0}", CurrentBars[AlertA2SeriesIndex] - ago);
			Draw.VerticalLine(this, tag, Times[AlertA2SeriesIndex][ago], Brushes.Gray, DashStyleHelper.Dash, 2);
		}

		private void DrawAlertA2Band(int dir, int startIdx, int endIdx, double hi, double lo)
		{
			if (!KatSignalCore.EnvBandAnchors(dir, startIdx, endIdx, hi, lo, CurrentBars[AlertA2SeriesIndex], out int agoStart, out int agoEnd)) return;
			Brush area = new SolidColorBrush(dir > 0 ? Colors.DodgerBlue : Colors.OrangeRed);
			string tag = string.Format("K34S_ALERTA2_BAND_{0}_{1}", dir > 0 ? "B" : "S", startIdx);
			Draw.Rectangle(this, tag, false, Times[AlertA2SeriesIndex][agoStart], hi, Times[AlertA2SeriesIndex][agoEnd], lo, Brushes.Transparent, area, 8);
		}
	}
}
