/*
 * Kat34Scalper.AlertSignal.A1.cs — Alert Signal sub-module A1 (partial class Kat34Scalper).
 * Independent Alert Signal A1 (placeholder template).
 * Alert-only module: plays alert sound and draws entry/SL/TP or alert markers on chart.
 * Does NOT interact with Bot execution or order placement.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
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

		private void EvaluateAlertA1(double high, double low, double close)
		{
			if (!cachedAlertA1) return;
			// Placeholder template for Alert Signal A1 evaluation logic
		}

		private void BackfillAlertA1()
		{
			if (!cachedAlertA1) return;
			int start = Math.Min(FindHistoryStartBarsAgo(AlertA1HistoryDays), CurrentBars[0] - 1);
			if (start < 0) return;
			Print(string.Format("[Kat34Scalper][AlertA1] backfill done — {0} day(s), {1} bar(s) replayed.", AlertA1HistoryDays, start + 1));
		}

		private void ClearAlertA1Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "A1");
			RemoveModuleDrawings("K34S_ALERTA1_");
		}
	}
}
