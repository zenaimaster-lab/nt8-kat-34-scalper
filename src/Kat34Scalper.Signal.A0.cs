/*
 * Kat34Scalper.Signal.A0.cs — Signal sub-module A0: EMA-ribbon fan (partial class Kat34Scalper).
 * Independent: own toggle, own settings group ("2. Signal A0 — EMA Fan"), own drawings.
 * Stages (docs/SIGNALS.md):
 *   A0 idle   — ribbon not fanned (no marker).
 *   A0 fanned — 9/21/34/55/89/144/200 EMAs strictly ordered + spreading + wide enough:
 *               first bar of an episode draws a triangle (buy blue below / sell orange above)
 *               and plays the alert (live bar only). Re-arms when the fan collapses.
 * Default OFF. When switched ON, BackfillA0 computes + draws the fan over "History Days" (default 3).
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
		// --- A0 sub-module state ---
		private volatile bool cachedA0 = false;    // HUD toggle: A0 marker/alert on/off (default OFF)
		private volatile bool a0BackfillPending;   // set on enable; consumed once by FlushBackfill
		private bool a0Alerted;                    // alert already fired for the current fan episode

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every A0 drawing (module is independent — nothing else is touched).
		private void SetA0Signal(bool on)
		{
			cachedA0 = on;
			A0Enabled = on;
			if (on)
			{
				a0BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				a0BackfillPending = false;
				a0Alerted = false;
				TriggerCustomEvent(o => RemoveModuleDrawings("K34S_A0_"), null);
			}
		}

		// Returns the current primary fan direction (-1 sell / 0 none / +1 buy) — always computed,
		// the Filter module consumes it. Marker/alert rendering is gated by cachedA0.
		private int EvaluateA0Fan()
		{
			if (fanEmas == null || CurrentBars[0] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback) return 0;

			int dir = SeriesFanDirection(0);
			if (dir != 0 && cachedA0 && !a0Alerted)
			{
				a0Alerted = true;
				PlayAlertSound();
				double y = dir > 0 ? Lows[0][0] - ArrowOffsetTicks * TickSize : Highs[0][0] + ArrowOffsetTicks * TickSize;
				if (dir > 0)
					Draw.TriangleUp(this, "K34S_A0_" + CurrentBar, false, 0, y, Brushes.DodgerBlue);
				else
					Draw.TriangleDown(this, "K34S_A0_" + CurrentBar, false, 0, y, Brushes.OrangeRed);
				Print(string.Format("[Kat34Scalper] A0 {0} fan @ bar {1}", dir > 0 ? "BUY" : "SELL", CurrentBar));
			}
			else if (dir == 0 || !cachedA0)
			{
				a0Alerted = false; // fan collapsed or A0 signal disabled — re-arm on next enabled episode
			}
			return dir;
		}

		// One-shot replay over the last A0HistoryDays: draws a triangle at every fan episode start.
		// No alert sounds during replay; a0Alerted ends in sync with the last replayed bar.
		private void BackfillA0()
		{
			int warm = FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback;
			int start = Math.Min(FindHistoryStartBarsAgo(A0HistoryDays), CurrentBars[0] - warm);
			if (start < 0) return;
			int prevDir = 0;
			for (int ago = start; ago >= 0; ago--)
			{
				int dir = SeriesFanDirectionAt(0, ago);
				if (dir != 0 && dir != prevDir)
				{
					double y = dir > 0 ? Lows[0][ago] - ArrowOffsetTicks * TickSize : Highs[0][ago] + ArrowOffsetTicks * TickSize;
					string tag = "K34S_A0_" + (CurrentBars[0] - ago);
					if (dir > 0)
						Draw.TriangleUp(this, tag, false, ago, y, Brushes.DodgerBlue);
					else
						Draw.TriangleDown(this, tag, false, ago, y, Brushes.OrangeRed);
				}
				prevDir = dir;
			}
			a0Alerted = prevDir != 0; // an ongoing episode must not re-alert live
			Print(string.Format("[Kat34Scalper][A0] backfill done — {0} day(s), {1} bar(s) replayed.", A0HistoryDays, start + 1));
		}
	}
}
