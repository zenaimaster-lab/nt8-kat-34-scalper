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
		private void PassFilters(int a0Dir, out bool sellAllowed, out bool buyAllowed)
		{
			PassFiltersAt(0, a0Dir, out sellAllowed, out buyAllowed);
			if (!diagnosticGateInitialized || diagnosticA0Dir != a0Dir ||
				diagnosticSellAllowed != sellAllowed || diagnosticBuyAllowed != buyAllowed)
			{
				diagnosticGateInitialized = true;
				diagnosticA0Dir = a0Dir;
				diagnosticSellAllowed = sellAllowed;
				diagnosticBuyAllowed = buyAllowed;
				Print(string.Format("[Kat34Scalper][GATE] bar {0} A0={1}, fanFilter={2}, cachedA0={3}, sellAllowed={4}, buyAllowed={5}, mtf={6}, adx={7}, vol={8}, time={9}",
					CurrentBar, a0Dir, FanFilterEnabled, cachedA0, sellAllowed, buyAllowed, cachedMtf, cachedAdx, cachedVol, cachedTime));
			}
		}

		// Fan gate + MTF + market + time at any bar (barsAgo 0 = live, >0 = backfill replay).
		private void PassFiltersAt(int barsAgo, int a0Dir, out bool sellAllowed, out bool buyAllowed)
		{
			bool fanOff = !FanFilterEnabled;
			bool pass = (fanOff || a0Dir != 0) && MtfPassAt(a0Dir, barsAgo) && MarketPassAt(barsAgo) && TimePassAt(barsAgo);
			sellAllowed = pass && (fanOff || a0Dir < 0);
			buyAllowed  = pass && (fanOff || a0Dir > 0);
		}

		// Ribbon direction of one BarsArray series at the current bar (+1 buy fan / -1 sell fan / 0 none).
		private int SeriesFanDirection(int s)
		{
			return SeriesFanDirectionAt(s, 0);
		}

		// Ribbon direction of one BarsArray series at any bar.
		private int SeriesFanDirectionAt(int s, int barsAgo)
		{
			if (fanEmas == null || CurrentBars[s] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback + barsAgo) return 0;
			double[] now = new double[FanPeriods.Length];
			double[] prev = new double[FanPeriods.Length];
			for (int p = 0; p < FanPeriods.Length; p++)
			{
				now[p] = fanEmas[s][p][barsAgo];
				prev[p] = fanEmas[s][p][barsAgo + FanSpreadLookback];
			}
			return Kat34ScalperLogic.FanDirection(now, prev, FanMinSpreadTicks, TickSize);
		}

		private bool MtfPassAt(int dir, int barsAgo)
		{
			if (!cachedMtf || dir == 0) return true;
			// ponytail: replay skips the MTF leg (primary barsAgo has no cheap mapping onto the
			// 3m/5m/15m series). Upgrade path: map via BarsArray[bip].GetBar(Times[0][barsAgo]).
			if (barsAgo > 0) return true;
			if (bip3m > 0  && SeriesFanDirection(bip3m) != dir) return false;
			if (bip5m > 0  && SeriesFanDirection(bip5m) != dir) return false;
			if (bip15m > 0 && SeriesFanDirection(bip15m) != dir) return false;
			return true;
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
