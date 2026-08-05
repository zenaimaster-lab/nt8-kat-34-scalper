/*
 * Kat34Scalper.AlertSignal.A1.cs — Alert Signal sub-module A1: EmaZone30s (partial class Kat34Scalper).
 * Independent Alert Signal A1 (fan) — runs on its OWN secondary series (BarsArray[1], default 30s)
 * with its OWN EMA 8/34/89/144/200 instances. Shares NOTHING with the Bot Signals (B1/B2): no common
 * series, EMAs, states, signalRecords or drawing records. Alert-only: draws a vertical line and
 * plays the global Alert Sound on environment transitions. Does NOT interact with Bot or orders.
 * Since v0.79 A1 is a PURE EMA fan — no market gates (they moved to the Bot side; the ALERT FILTER
 * HUD section is gone). Episodes, bands and edge lines run on the fan + angle; the invalid decision
 * (episode end + re-arm) waits for Break Bars consecutive invalid bars (v0.80 debounce unification).
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
		private volatile bool alertA1BackfillPending;
		private int a1LastDir;          // edge-trigger state: last armed environment direction
		private int a1InvalidStreak;    // consecutive invalid bars; >= BreakBars counts as broken
		private int a1PrevDir;          // last printed direction (diagnostic)
		private int a1ReplayLines;      // backfill counter
		private int a1BandDir;          // open background band: episode direction (0 = ranging, no band)
		private int a1BandStartIdx;     // open background band episode start (series-1 bar index)
		private double a1BandHi = double.MinValue; // vertical extent of the bands (window extremes, live-extended)
		private double a1BandLo = double.MaxValue;

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
		// Pure fan since v0.79 — no market gates (they moved to the Bot side).
		private void EvaluateAlertA1Bar()
		{
			if (!cachedAlertA1) return;
			if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[1] < 201) return; // ema200 warmup
			if (a1Ema8 == null || a1Ema34 == null || a1Ema89 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;

			int rawDir = AlertA1DirectionAt(0, out double angle);
			if (rawDir != 0 && !EmaZonePassAt(0, rawDir)) rawDir = 0;
			int dir = Kat34ScalperLogic.A1DebouncedDir(rawDir, a1LastDir, a1InvalidStreak, AlertA1BreakBars);
			if (Highs[1][0] > a1BandHi) a1BandHi = Highs[1][0];
			if (Lows[1][0] < a1BandLo) a1BandLo = Lows[1][0];
			if (dir != a1PrevDir)
			{
				DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], a1BandHi, a1BandLo);
				if (dir == 0) DrawAlertA1RangeLine(0);
				a1BandDir = dir;
				a1BandStartIdx = CurrentBars[1];
				a1PrevDir = dir;
				Print(string.Format("[Kat34Scalper][AlertA1][FAN] bar {0} dir={1}, angle={2:F1}deg (min {3}, enabled {4}), e8={5:F2}, e34={6:F2}, e89={7:F2}, e144={8:F2}, e200={9:F2}, atr={10:F2}",
					CurrentBars[1], dir, angle, AlertA1AngleMin, AlertA1AngleEnabled,
					a1Ema8[0], a1Ema34[0], a1Ema89[0], a1Ema144[0], a1Ema200[0], a1Atr[0]));
			}
			else if (a1BandDir != 0)
			{
				DrawEnvBand(a1BandDir, a1BandStartIdx, CurrentBars[1], a1BandHi, a1BandLo); // extend the open episode
			}
			bool fired = Kat34ScalperLogic.A1EdgeStep(rawDir, a1LastDir, a1InvalidStreak, AlertA1BreakBars, out a1LastDir, out a1InvalidStreak);
			if (fired)
			{
				DrawAlertA1Line(a1LastDir, 0);
				if (State == State.Realtime) // historical replay would machine-gun the sound on every load
				{
					PlayAlertSound();
					Print(string.Format("[Kat34Scalper][AlertA1] {0} environment @ bar {1} — vertical line + sound.",
						a1LastDir > 0 ? "LONG" : "SHORT", CurrentBars[1]));
				}
			}
		}

		// Fan+angle environment direction on the A1 series (needs barsAgo+1 for the slope;
		// slope normalized by the A1-series ATR so the angle needs no manual tuning).
		private int AlertA1DirectionAt(int ago, out double angle)
		{
			angle = Kat34ScalperLogic.SlopeAngleDeg(a1Ema34[ago], a1Ema34[ago + 1], a1Atr[ago]);
			return Kat34ScalperLogic.A1Direction(
				AlertA1CondEma8Above34, AlertA1CondEma34Above89, AlertA1CondEma89Above144, AlertA1CondEma144Above200, AlertA1AngleEnabled,
				a1Ema8[ago], a1Ema34[ago], a1Ema89[ago], a1Ema144[ago], a1Ema200[ago],
				angle, Math.Abs(AlertA1AngleMin));
		}

		// EMA34 zone gate (v0.84): on each configured higher TF (series 3/4/5), the last CLOSED zone
		// bar's close must sit on the episode side of that TF's EMA34 (LONG above, SHORT below) —
		// mirrored per direction. Same no-lookahead cutoff math as the ADX MTF gate; warmup = open.
		private bool EmaZonePassAt(int ago, int dir)
		{
			if (dir == 0 || zoneEma34 == null) return true;
			for (int z = 0; z < zoneEma34.Length; z++)
			{
				int s = 3 + z;
				if (zoneEma34[z] == null || CurrentBars == null || CurrentBars.Length <= s || CurrentBars[s] < 1) continue;
				DateTime cutoff = Kat34ScalperLogic.ClosedBarCutoff(Times[1][ago], SeriesPeriodSeconds(1), SeriesPeriodSeconds(s));
				int idx = Kat34ScalperLogic.BarsAgoAtOrBefore(i => Times[s][i], CurrentBars[s], cutoff);
				if (idx < 0) continue;
				if (!Kat34ScalperLogic.EmaZonePass(dir, Closes[s][idx], zoneEma34[z][idx])) return false;
			}
			return true;
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
				int rawDir = AlertA1DirectionAt(ago, out _);
				if (rawDir != 0 && !EmaZonePassAt(ago, rawDir)) rawDir = 0;
				int dir = Kat34ScalperLogic.A1DebouncedDir(rawDir, lastDir, invalidStreak, AlertA1BreakBars);
				if (dir != bandDir)
				{
					DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1] - ago, hi, lo);
					if (dir == 0) DrawAlertA1RangeLine(ago);
					bandDir = dir;
					bandStartIdx = CurrentBars[1] - ago;
				}
				bool fired = Kat34ScalperLogic.A1EdgeStep(rawDir, lastDir, invalidStreak, AlertA1BreakBars, out lastDir, out invalidStreak);
				if (fired)
				{
					DrawAlertA1Line(lastDir, ago);
					a1ReplayLines++;
				}
			}
			a1LastDir = lastDir;          // live evaluation continues the edge-trigger state
			a1InvalidStreak = invalidStreak;
			a1PrevDir = bandDir;
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
			a1BandHi = double.MinValue;
			a1BandLo = double.MaxValue;
			RemoveModuleDrawings("K34S_ALERTA1_");
		}

		// Pale background band over the price panel for a LONG/SHORT episode (ranging draws nothing).
		// Args are ABSOLUTE series-1 bar indexes (startIdx < endIdx) — the old barsAgo-style guards
		// here silently rejected every valid episode (index start is always < index end), so the
		// bands never rendered; the decision + barsAgo conversion now live in EnvBandAnchors (tested).
		private void DrawEnvBand(int dir, int startIdx, int endIdx, double hi, double lo)
		{
			if (!Kat34ScalperLogic.EnvBandAnchors(dir, startIdx, endIdx, hi, lo, CurrentBars[1], out int agoStart, out int agoEnd)) return;
			Brush area = new SolidColorBrush(dir > 0 ? Colors.Green : Colors.Red);
			string tag = string.Format("K34S_ALERTA1_BAND_{0}_{1}", dir > 0 ? "B" : "S", startIdx);
			Draw.Rectangle(this, tag, false, Times[1][agoStart], hi, Times[1][agoEnd], lo, Brushes.Transparent, area, 8);
		}

		// Gray vertical line marking the start of a ranging episode.
		private void DrawAlertA1RangeLine(int ago)
		{
			string tag = string.Format("K34S_ALERTA1_VR_{0}", CurrentBars[1] - ago);
			Draw.VerticalLine(this, tag, Times[1][ago], Brushes.Gray, DashStyleHelper.Dash, 2);
		}
	}
}
