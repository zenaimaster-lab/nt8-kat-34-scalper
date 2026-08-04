/*
 * Kat34Scalper.AlertSignal.A2.cs — Alert Signal sub-module A2 (partial class Kat34Scalper).
 * Independent Alert Signal A2 (placeholder template).
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
		private volatile bool cachedAlertA2 = false;
		private volatile bool alertA2BackfillPending;

		private void SetAlertA2Signal(bool on)
		{
			cachedAlertA2 = on;
			AlertA2Enabled = on;
			Print(string.Format("[Kat34Scalper][AlertA2] toggled {0}", on ? "ON — backfilling History Days" : "OFF — drawings removed"));
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

		private void EvaluateAlertA2(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedAlertA2) return;
			// Placeholder template for Alert Signal A2 evaluation logic (gated by ALERT Filter sellAllowed/buyAllowed)
		}

		private void BackfillAlertA2()
		{
			if (!cachedAlertA2) return;
			int start = Math.Min(FindHistoryStartBarsAgo(AlertA2HistoryDays), CurrentBars[0] - 1);
			if (start < 0) return;
			for (int ago = start; ago >= 0; ago--)
			{
				bool sellAllowed, buyAllowed;
				PassAlertFiltersAt(ago, out sellAllowed, out buyAllowed);
				// Backfill replay logic for Alert Signal A2
			}
			Print(string.Format("[Kat34Scalper][AlertA2] backfill done — {0} day(s), {1} bar(s) replayed.", AlertA2HistoryDays, start + 1));
		}

		private void ClearAlertA2Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "A2");
			RemoveModuleDrawings("K34S_ALERTA2_");
		}
	}
}
