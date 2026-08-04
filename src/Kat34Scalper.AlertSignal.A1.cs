/*
 * Kat34Scalper.AlertSignal.A1.cs — Alert Signal sub-module A1: fan 30s (partial class Kat34Scalper).
 * Independent Alert Signal A1 (fan) — runs on its OWN secondary series (BarsArray[1], default 30s)
 * with its OWN EMA 8/34/144/200 instances. Shares NOTHING with the Bot Signals (B1/B2): no common
 * series, EMAs, states, signalRecords or drawing records. Alert-only: draws a vertical line and
 * plays the global Alert Sound on environment transitions. Does NOT interact with Bot or orders.
 *
 * LONG environment:  ema8 > ema34 > ema144 > ema200 AND ema34 slope angle >= +Min Angle (rising).
 * SHORT environment: ema8 < ema34 < ema144 < ema200 AND ema34 slope angle <= -Min Angle (falling).
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
		private int a1ReplayLines;      // backfill counter

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
			if (a1Ema8 == null || a1Ema34 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;

			int dir = AlertA1DirectionAt(0);
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
		private int AlertA1DirectionAt(int ago)
		{
			double angle = Kat34ScalperLogic.SlopeAngleDeg(a1Ema34[ago], a1Ema34[ago + 1], a1Atr[ago]);
			return Kat34ScalperLogic.A1Direction(
				AlertA1CondEma8Above34, AlertA1CondEma34Above144, AlertA1CondEma144Above200, AlertA1CondAngle,
				a1Ema8[ago], a1Ema34[ago], a1Ema144[ago], a1Ema200[ago],
				angle, Math.Abs(AlertA1AngleMin));
		}

		// Vertical line anchored at the A1 bar time (time-based draw — safe from any series context).
		private void DrawAlertA1Line(int dir, int ago)
		{
			string tag = string.Format("K34S_ALERTA1_VL_{0}_{1}", dir > 0 ? "B" : "S", CurrentBars[1] - ago);
			Brush brush = new SolidColorBrush(dir > 0 ? AlertA1LongColor : AlertA1ShortColor);
			int width = Math.Max(1, Math.Min(AlertA1LineWidth, 10));
			Draw.VerticalLine(this, tag, Times[1][ago], brush, DashStyleHelper.Dash, width);
		}

		private void BackfillAlertA1()
		{
			if (!cachedAlertA1) return;
			if (CurrentBars == null || CurrentBars.Length < 2) return;
			if (a1Ema8 == null || a1Ema34 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;
			int warm = 201;
			int max = CurrentBars[1] - 1;
			if (max < warm)
			{
				a1LastDir = 0;
				Print("[Kat34Scalper][AlertA1] backfill skipped — 30s series has no/little history yet.");
				return;
			}
			int start = Math.Min(AlertA1HistoryStartBarsAgo(AlertA1HistoryDays), max - warm);
			int lastDir = 0;
			int invalidStreak = 0;
			a1ReplayLines = 0;
			for (int ago = start; ago >= 1; ago--)
			{
				int dir = AlertA1DirectionAt(ago);
				if (Kat34ScalperLogic.A1EdgeStep(dir, lastDir, invalidStreak, AlertA1BreakBars, out lastDir, out invalidStreak))
				{
					DrawAlertA1Line(lastDir, ago);
					a1ReplayLines++;
				}
			}
			a1LastDir = lastDir;          // live evaluation continues the edge-trigger state
			a1InvalidStreak = invalidStreak;
			Print(string.Format("[Kat34Scalper][AlertA1] backfill done — {0} day(s), {1} bar(s) replayed: {2} vertical line(s); live lastDir={3}.",
				AlertA1HistoryDays, start, a1ReplayLines, a1LastDir));
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
			RemoveModuleDrawings("K34S_ALERTA1_");
		}
	}
}
