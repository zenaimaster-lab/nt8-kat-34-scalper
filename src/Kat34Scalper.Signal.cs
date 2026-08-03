/*
 * Kat34Scalper.Signal.cs — Signal module shared helpers (partial class Kat34Scalper).
 * Each signal sub-module is independent and lives in its own file:
 *   src/Kat34Scalper.Signal.A0.cs — A0: EMA-ribbon fan (default OFF, backfill History Days)
 *   src/Kat34Scalper.Signal.A1.cs — A1: 89-34 pullback (default OFF, backfill History Days)
 *   src/Kat34Scalper.Signal.A2.cs — A2: 34+8+Bounce ema34 touch (default OFF, backfill History Days)
 *   src/Kat34Scalper.Signal.A3.cs — A3: 8cross34 ema cross (default OFF, backfill History Days)
 * Stage names per signal are specified in docs/SIGNALS.md.
 * New signals (A2, A3, ...) plug in as a new Kat34Scalper.Signal.AX.cs file.
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// ponytail: no ': Indicator' here — NT8's codegen injects its generated region into EVERY
	// file that declares the base class, duplicating cacheKat34Scalper/wrappers across files
	// (CS0111/CS0102/CS0121/CS0229). Only Kat34Scalper.cs carries the base spec.
	public partial class Kat34Scalper
	{
		// --- Shared signal-module diagnostics (written by Filter.PassFilters, read by A1 prints) ---
		private bool diagnosticGateInitialized;
		private int diagnosticA0Dir;
		private bool diagnosticSellAllowed;
		private bool diagnosticBuyAllowed;

		// Furthest barsAgo still inside the "last N days" window measured from the current bar.
		private int FindHistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Times[0][0].Subtract(TimeSpan.FromDays(days));
			int max = CurrentBars[0];
			int ago = 0;
			while (ago < max && Times[0][ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		// Runs each sub-module's one-shot backfill when it was enabled (load or HUD toggle).
		// Called from OnBarUpdate at the last available bar and from HUD clicks via TriggerCustomEvent.
		private void FlushBackfill()
		{
			if (CurrentBars == null || CurrentBars.Length == 0 || CurrentBars[0] < 1) return;
			if (a1BackfillPending)
			{
				a1BackfillPending = false;
				if (fastEma != null && slowEma != null) BackfillA1();
			}
			if (a2BackfillPending)
			{
				a2BackfillPending = false;
				if (ema8 != null && fastEma != null) BackfillA2();
			}
		}

	}
}
