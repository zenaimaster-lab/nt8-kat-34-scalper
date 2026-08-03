/*
 * Kat34Scalper.Filter.cs — Filter module (partial class Kat34Scalper).
 * Gates that decide whether a signal may fire on a bar. Every gate has a *At(barsAgo)
 * variant so the signal backfill replays evaluate the same gates on historical bars.
 * New filters (MACD, RSI, ...) plug in as a new method here + one clause in PassFiltersAt.
 *   Fan gate (uses A0 direction), MTF fan (3m/5m/15m), ADX, Volume, Time window.
 * Every gate is OFF by default (session-only toggles boot OFF on every load).
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- Filter module state (HUD toggles — default OFF: every gate open until user enables) ---
		private volatile bool cachedMtf;
		private volatile bool cachedAdx;
		private volatile bool cachedVol;
		private volatile bool cachedTime;

		// Live entry point (current bar) with the gate-transition diagnostic print.
		private void PassFilters(out bool sellAllowed, out bool buyAllowed)
		{
			PassFiltersAt(0, out sellAllowed, out buyAllowed);
			if (!diagnosticGateInitialized ||
				diagnosticSellAllowed != sellAllowed || diagnosticBuyAllowed != buyAllowed)
			{
				diagnosticGateInitialized = true;
				diagnosticSellAllowed = sellAllowed;
				diagnosticBuyAllowed = buyAllowed;
				Print(string.Format("[Kat34Scalper][GATE] bar {0} sellAllowed={1}, buyAllowed={2}, adx={3}, vol={4}, time={5}",
					CurrentBar, sellAllowed, buyAllowed, cachedAdx, cachedVol, cachedTime));
			}
		}

		// Market + time at any bar (barsAgo 0 = live, >0 = backfill replay).
		private void PassFiltersAt(int barsAgo, out bool sellAllowed, out bool buyAllowed)
		{
			bool pass = MarketPassAt(barsAgo) && TimePassAt(barsAgo);
			sellAllowed = pass;
			buyAllowed  = pass;
		}

		private bool MarketPassAt(int barsAgo)
		{
			double adxMin = cachedAdx ? AdxMin : 0;
			double volSma = cachedVol && volSmaInd != null ? volSmaInd[barsAgo] : 0;
			double adx = adxInd != null ? adxInd[barsAgo] : 0;
			return Kat34ScalperLogic.PassMarketFilter(adx, adxMin, Volumes[0][barsAgo], volSma, VolumeMinMult);
		}

		private bool TimePassAt(int barsAgo)
		{
			if (!cachedTime || timeWindowDisabled) return true;
			return Kat34ScalperLogic.IsInTimeWindow(Times[0][barsAgo].TimeOfDay, timeStart, timeEnd);
		}
	}
}
