/*
 * Kat34Scalper.AlertSignal.A1.cs — Alert Signal sub-module A1: fan 30s (partial class Kat34Scalper).
 * Independent Alert Signal A1 (fan) — runs on its OWN secondary series (BarsArray[1], default 30s)
 * with its OWN EMA 8/34/144/200 instances. Shares NOTHING with the Bot Signals (B1/B2): no common
 * series, EMAs, states, signalRecords or drawing records. Alert-only: draws a vertical line and
 * plays the global Alert Sound on environment transitions. Does NOT interact with Bot or orders.
 *
 * LONG environment:  ema8 > ema34 > ema89 > ema144 > ema200 AND ema34 slope angle >= +Min Angle (rising).
 * SHORT environment: ema8 < ema34 < ema89 < ema144 < ema200 AND ema34 slope angle <= -Min Angle (falling).
 * Edge trigger: one vertical line + one sound per invalid->valid transition.
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
	public partial class Kat34Scalper
	{
		private volatile bool cachedAlertA1 = false;
		private volatile bool cachedA1AdxMtf = false;
		private volatile bool alertA1BackfillPending;
		private int a1LastDir;          // edge-trigger state: last armed environment direction
		private int a1InvalidStreak;    // consecutive invalid bars; >= BreakBars counts as broken
		private int a1PrevDir;          // last printed gate direction (diagnostic)
		private int a1ReplayLines;      // backfill counter
		private int a1BandDir;          // open background band: episode direction (0 = ranging, no band)
		private int a1BandStartIdx;     // open background band episode start (series-1 bar index)
		private double a1BandHi;        // vertical extent of the bands (window extremes, live-extended)
		private double a1BandLo;

		private void SetAlertA1Signal(bool on)
		{
			cachedAlertA1 = on;
			AlertA1Enabled = on;
			Print(string.Format("[Kat34Scalper][AlertA1] toggled {0}", on ? "ON — backfilling History Days" : "OFF — drawings removed"));
			if (on)
			{
				alertA1BackfillPending = true;
				TriggerCustomEvent(o => FlushAlertBackfill(), null);
			}
			else
			{
				alertA1BackfillPending = false;
				TriggerCustomEvent(o => ClearAlertA1Drawings(), null);
			}
		}

		// Called from OnBarUpdate on BarsInProgress == 1 (the dedicated A1 series, OnBarClose).
		private void EvaluateAlertA1Bar()
		{
			if (!cachedAlertA1) return;
			if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[1] < 201) return; // ema200 warmup
			if (a1Ema8 == null || a1Ema34 == null || a1Ema89 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;

			int dir = AlertA1DirectionAt(0, out double angle);
			if (Highs[1][0] > a1BandHi) a1BandHi = Highs[1][0];
			if (Lows[1][0] < a1BandLo) a1BandLo = Lows[1][0];
			if (dir != a1PrevDir)
			{
				DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], a1BandHi, a1BandLo);
				if (dir == 0) DrawAlertA1RangeLine(0);
				a1BandDir = dir;
				a1BandStartIdx = CurrentBars[1];
				a1PrevDir = dir;
				Print(string.Format("[Kat34Scalper][AlertA1][GATE] bar {0} dir={1}, angle={2:F1}deg (min {3}, enabled {4}), e8={5:F2}, e34={6:F2}, e89={7:F2}, e144={8:F2}, e200={9:F2}, atr={10:F2}",
					CurrentBars[1], dir, angle, AlertA1AngleMin, AlertA1AngleEnabled,
					a1Ema8[0], a1Ema34[0], a1Ema89[0], a1Ema144[0], a1Ema200[0], a1Atr[0]));
			}
			else if (a1BandDir != 0)
			{
				DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], a1BandHi, a1BandLo); // extend the open episode
			}
			if (Kat34ScalperLogic.A1EdgeStep(dir, a1LastDir, a1InvalidStreak, AlertA1BreakBars, out a1LastDir, out a1InvalidStreak))
			{
				DrawAlertA1Line(a1LastDir, 0);
				PlayAlertSound();
				Print(string.Format("[Kat34Scalper][AlertA1] {0} environment @ bar {1} — vertical line + sound.",
					a1LastDir > 0 ? "LONG" : "SHORT", CurrentBars[1]));
			}
		}

		// Environment direction on the A1 series at barsAgo (needs barsAgo+1 for the slope;
		// slope normalized by the A1-series ATR so the angle needs no manual tuning).
		private int AlertA1DirectionAt(int ago, out double angle)
		{
			angle = Kat34ScalperLogic.SlopeAngleDeg(a1Ema34[ago], a1Ema34[ago + 1], a1Atr[ago]);
			int dir = Kat34ScalperLogic.A1Direction(
				AlertA1CondEma8Above34, AlertA1CondEma34Above89, AlertA1CondEma89Above144, AlertA1CondEma144Above200, AlertA1AngleEnabled,
				a1Ema8[ago], a1Ema34[ago], a1Ema89[ago], a1Ema144[ago], a1Ema200[ago],
				angle, Math.Abs(AlertA1AngleMin));
			if (dir != 0 && !AlertA1MarketPassAt(ago)) dir = 0;
			return dir;
		}

		// ALERT-side market gates evaluated on the primary series at the A1 bar time (backfill-aware).
		// The A1-only legs (ADX rising, ADX MTF) live here; the shared alert toggles (ADX/ER/CI) too.
		private bool AlertA1MarketPassAt(int ago)
		{
			if (cachedA1AdxMtf && !A1AdxMtfPassAt(ago)) return false;
			if (!cachedAdxA && !cachedAdxRise && !cachedErA && !cachedCiA) return true;
			int ago0 = Series0BarsAgoAt(Times[1][ago]);
			if (ago0 < 0) return true;
			if (cachedAdxA && (adxInd == null || adxInd[ago0] < AdxMin)) return false;
			if (cachedAdxRise && (adxInd == null || CurrentBars[0] < ago0 + AdxRisingBars || adxInd[ago0] <= adxInd[ago0 + AdxRisingBars])) return false;
			if (cachedErA && !ErPassAt(ago0)) return false;
			if (cachedCiA && !CiPassAt(ago0)) return false;
			return true;
		}

		// barsAgo on the primary series of the bar closed at or before t (-1 when t precedes series 0).
		private int Series0BarsAgoAt(DateTime t)
		{
			if (CurrentBars[0] < 1) return -1;
			int lo = 0, hi = CurrentBars[0];
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (Times[0][mid] <= t) hi = mid; else lo = mid + 1;
			}
			return Times[0][lo] <= t ? lo : -1;
		}

		// Independent MTF ADX regime gate (NOT part of the Global Filter): the most recent MTF bar
		// closed at or before the A1 bar time must have ADX >= AlertA1AdxMtfMin (no lookahead).
		private bool A1AdxMtfPassAt(int ago)
		{
			if (adxMtfInd == null || CurrentBars == null || CurrentBars.Length < 3 || CurrentBars[2] < 1) return true;
			DateTime t = Times[1][ago];
			int lo = 0, hi = CurrentBars[2];
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (Times[2][mid] <= t) hi = mid; else lo = mid + 1;
			}
			if (Times[2][lo] > t) return true; // MTF series starts after t — warmup, gate open
			return adxMtfInd[lo] >= AlertA1AdxMtfMin;
		}

		// HUD toggle: flip the gate and replay the A1 history so the drawings match immediately.
		private void SetAlertA1AdxMtf(bool on)
		{
			cachedA1AdxMtf = on;
			Print(string.Format("[Kat34Scalper][AlertA1] ADX MTF gate toggled {0} (min {1} on {2}m) — re-backfilling.",
				on ? "ON" : "OFF", AlertA1AdxMtfMin, AlertA1AdxMtfMinutes));
			ReBackfillAlertA1();
		}

		// Shared ALERT FILTER HUD toggles: assign then replay A1 so lines/bands match the new gates.
		private void SetAlertFilterToggle(bool on, Action<bool> assign)
		{
			assign(on);
			ReBackfillAlertA1();
		}

		private void ReBackfillAlertA1()
		{
			alertA1BackfillPending = true; // FlushAlertBackfill no-ops without this — signals would vanish
			TriggerCustomEvent(o => { ClearAlertA1Drawings(); FlushAlertBackfill(); }, null);
		}

		// Vertical line anchored at the A1 bar time (time-based draw — safe from any series context).
		private void DrawAlertA1Line(int dir, int ago)
		{
			string tag = string.Format("K34S_ALERTA1_VL_{0}_{1}", dir > 0 ? "B" : "S", CurrentBars[1] - ago);
			Brush brush = new SolidColorBrush(dir > 0 ? AlertA1LongLineColor : AlertA1ShortLineColor);
			int width = Math.Max(1, Math.Min(AlertA1LineWidth, 10));
			Draw.VerticalLine(this, tag, Times[1][ago], brush, DashStyleHelper.Dash, width);
		}

		private void BackfillAlertA1()
		{
			if (!cachedAlertA1) return;
			if (CurrentBars == null || CurrentBars.Length < 2) return;
			if (a1Ema8 == null || a1Ema34 == null || a1Ema89 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;
			int warm = 201;
			int max = CurrentBars[1] - 1;
			if (max < warm)
			{
				a1LastDir = 0;
				Print("[Kat34Scalper][AlertA1] backfill skipped — 30s series has no/little history yet.");
				return;
			}
			int start = Math.Min(AlertA1HistoryStartBarsAgo(AlertA1HistoryDays), max - warm);
			double hi = double.MinValue, lo = double.MaxValue;
			for (int ago = start; ago >= 1; ago--)
			{
				if (Highs[1][ago] > hi) hi = Highs[1][ago];
				if (Lows[1][ago] < lo) lo = Lows[1][ago];
			}
			int lastDir = 0;
			int invalidStreak = 0;
			a1ReplayLines = 0;
			int bandDir = 0;
			int bandStartIdx = CurrentBars[1] - start;
			for (int ago = start; ago >= 1; ago--)
			{
				int dir = AlertA1DirectionAt(ago, out _);
				if (dir != bandDir)
				{
					DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1] - ago, hi, lo);
					if (dir == 0) DrawAlertA1RangeLine(ago);
					bandDir = dir;
					bandStartIdx = CurrentBars[1] - ago;
				}
				if (Kat34ScalperLogic.A1EdgeStep(dir, lastDir, invalidStreak, AlertA1BreakBars, out lastDir, out invalidStreak))
				{
					DrawAlertA1Line(lastDir, ago);
					a1ReplayLines++;
				}
			}
			a1LastDir = lastDir;          // live evaluation continues the edge-trigger state
			a1InvalidStreak = invalidStreak;
			a1PrevDir = lastDir;
			a1BandDir = bandDir;
			a1BandStartIdx = bandStartIdx;
			a1BandHi = hi;
			a1BandLo = lo;
			DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], hi, lo); // open episode up to now
			Print(string.Format("[Kat34Scalper][AlertA1] backfill done — {0} day(s), {1} bar(s) replayed: {2} vertical line(s); live lastDir={3}; settings: angleEnabled={4}, minAngle={5}, atrPeriod={6}, breakBars={7}.",
				AlertA1HistoryDays, start, a1ReplayLines, a1LastDir, AlertA1AngleEnabled, AlertA1AngleMin, AlertA1AtrPeriod, AlertA1BreakBars));
		}

		// Furthest barsAgo on the A1 series still inside the "last N days" window (Times[1] based).
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
			RemoveModuleDrawings("K34S_ALERTA1_");
		}

		// Pale background band over the price panel for a LONG/SHORT episode (ranging draws nothing).
		// Time/bar-anchored Draw.Rectangle stays inside the candle panel — other panels untouched.
		private void DrawEnvBand(int dir, int barsAgoStart, int barsAgoEnd, double hi, double lo)
		{
			if (dir == 0 || barsAgoStart <= barsAgoEnd || hi <= lo) return;
			Brush fill = new SolidColorBrush(dir > 0 ? Color.FromArgb(10, 0, 255, 0) : Color.FromArgb(10, 255, 0, 0));
			string tag = string.Format("K34S_ALERTA1_BAND_{0}_{1}", dir > 0 ? "B" : "S", barsAgoStart);
			Draw.Rectangle(this, tag, false, barsAgoStart, hi, barsAgoEnd, lo, Brushes.Transparent, fill, 1);
		}

		// Gray vertical line marking the start of a ranging episode.
		private void DrawAlertA1RangeLine(int ago)
		{
			string tag = string.Format("K34S_ALERTA1_VR_{0}", CurrentBars[1] - ago);
			Draw.VerticalLine(this, tag, Times[1][ago], Brushes.Gray, DashStyleHelper.Dash, 2);
		}
	}
}
