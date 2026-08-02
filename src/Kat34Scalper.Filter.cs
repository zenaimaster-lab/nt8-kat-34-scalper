/*
 * Kat34Scalper.Filter.cs — Filter module (partial class Kat34Scalper).
 * Gates that decide whether a signal may fire on this bar. New filters (MACD, RSI, ...)
 * plug in as a new method here + one clause in PassFilters.
 *   Fan gate (uses A0 direction), MTF fan (3m/5m/15m), ADX, Volume, Time window.
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

		// Fan gate + MTF + market + time. a0Dir comes from the independent A0 fan calculation.
		private void PassFilters(int a0Dir, out bool sellAllowed, out bool buyAllowed)
		{
			bool fanOff = !FanFilterEnabled;
			bool pass = (fanOff || a0Dir != 0) && MtfPass(a0Dir) && MarketPass() && TimePass();
			sellAllowed = pass && (fanOff || a0Dir < 0);
			buyAllowed  = pass && (fanOff || a0Dir > 0);
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

		// Ribbon direction of one BarsArray series (+1 buy fan / -1 sell fan / 0 none).
		private int SeriesFanDirection(int s)
		{
			if (CurrentBars[s] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback) return 0;
			double[] now = new double[FanPeriods.Length];
			double[] prev = new double[FanPeriods.Length];
			for (int p = 0; p < FanPeriods.Length; p++)
			{
				now[p] = fanEmas[s][p][0];
				prev[p] = fanEmas[s][p][FanSpreadLookback];
			}
			return Kat34ScalperLogic.FanDirection(now, prev, FanMinSpreadTicks, TickSize);
		}

		private bool MtfPass(int dir)
		{
			if (!cachedMtf || dir == 0) return true;
			if (bip3m > 0  && SeriesFanDirection(bip3m) != dir) return false;
			if (bip5m > 0  && SeriesFanDirection(bip5m) != dir) return false;
			if (bip15m > 0 && SeriesFanDirection(bip15m) != dir) return false;
			return true;
		}

		private bool MarketPass()
		{
			double adxMin = cachedAdx ? AdxMin : 0;
			double volSma = cachedVol && volSmaInd != null ? volSmaInd[0] : 0;
			double adx = adxInd != null ? adxInd[0] : 0;
			return Kat34ScalperLogic.PassMarketFilter(adx, adxMin, Volumes[0][0], volSma, VolumeMinMult);
		}

		private bool TimePass()
		{
			if (!cachedTime || timeWindowDisabled) return true;
			return Kat34ScalperLogic.IsInTimeWindow(Times[0][0].TimeOfDay, timeStart, timeEnd);
		}
	}
}
