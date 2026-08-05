/*
 * Kat34Scalper.AlertSignal.cs — Alert Signal module shared helpers (partial class Kat34Scalper).
 * Independent module: Alert Signals (A1, A2, ...) generate visual indicators and audio alerts on chart.
 * They are completely isolated from the Bot module and do NOT submit or manage trading orders.
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using KAT.Signals;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private void FlushAlertBackfill()
		{
			if (CurrentBars == null || CurrentBars.Length == 0 || CurrentBars[0] < 1) return;
			if (alertA1BackfillPending)
			{
				alertA1BackfillPending = false;
				BackfillAlertA1();
			}
			if (alertA2BackfillPending)
			{
				alertA2BackfillPending = false;
				BackfillAlertA2();
			}
		}
	}
}
