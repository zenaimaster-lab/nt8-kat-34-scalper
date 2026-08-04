/*
 * Kat34Scalper.Filter.cs — Filter module (partial class Kat34Scalper).
 * Gates that decide whether a signal may fire on a bar. Every gate has a *At(barsAgo)
 * variant so the signal backfill replays evaluate the same gates on historical bars.
 * New filters (MACD, RSI, ...) plug in as a new method here + one clause in PassFiltersAt.
 *   ADX, ER (trend), CI (chop), Volume, Time window.
 *   Two independent sides: BOT gates (ADX/Volume/Time/ER/CI) feed B1+B2; ALERT gates
 *   (ADX/ER/CI + the A1-only ADX rising & ADX MTF legs in the A1 module) feed A1+A2.
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
		// BOT side (the old A0-era MTF fan toggle died with A0 in v0.56 — field/button removed v0.76)
		private volatile bool cachedAdx;
		private volatile bool cachedEr;
		private volatile bool cachedCi;
		private volatile bool cachedVol;
		private volatile bool cachedTime;
		// ALERT side (independent state; ADX rising + ADX MTF A1-only legs live in the A1 module)
		private volatile bool cachedAdxA;
		private volatile bool cachedAdxRise;
		private volatile bool cachedErA;
		private volatile bool cachedCiA;

		// Live entry point (current bar) with the gate-transition diagnostic print. BOT side.
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

		// Live ALERT side (A2). A1 applies the alert gates inside AlertA1DirectionAt (backfill-aware).
		private void PassAlertFilters(out bool sellAllowed, out bool buyAllowed)
		{
			PassAlertFiltersAt(0, out sellAllowed, out buyAllowed);
		}

		// BOT market + time at any bar (barsAgo 0 = live, >0 = backfill replay).
		private void PassFiltersAt(int barsAgo, out bool sellAllowed, out bool buyAllowed)
		{
			bool pass = MarketPassAt(barsAgo, false) && TimePassAt(barsAgo);
			sellAllowed = pass;
			buyAllowed  = pass;
		}

		// ALERT market gates at any bar (no time window on the alert side).
		private void PassAlertFiltersAt(int barsAgo, out bool sellAllowed, out bool buyAllowed)
		{
			bool pass = MarketPassAt(barsAgo, true);
			sellAllowed = pass;
			buyAllowed  = pass;
		}

		private bool MarketPassAt(int barsAgo, bool alert)
		{
			if ((alert ? cachedAdxA : cachedAdx) && (adxInd == null || adxInd[barsAgo] < AdxMin)) return false;
			if ((alert ? cachedErA : cachedEr) && !ErPassAt(barsAgo)) return false;
			if ((alert ? cachedCiA : cachedCi) && !CiPassAt(barsAgo)) return false;
			if (!alert)
			{
				double volSma = cachedVol && volSmaInd != null ? volSmaInd[barsAgo] : 0;
				double adx = adxInd != null ? adxInd[barsAgo] : 0;
				if (!Kat34ScalperLogic.PassMarketFilter(adx, cachedAdx ? AdxMin : 0, Volumes[0][barsAgo], volSma, VolumeMinMult)) return false;
			}
			return true;
		}

		// Kaufman Efficiency Ratio over the last ErPeriod bars ending at barsAgo (oldest -> newest).
		private bool ErPassAt(int barsAgo)
		{
			int n = Math.Max(2, ErPeriod);
			if (CurrentBars[0] < barsAgo + n) return false;
			double[] closes = new double[n];
			for (int i = 0; i < n; i++) closes[i] = Closes[0][barsAgo + n - 1 - i];
			return Kat34ScalperLogic.EfficiencyRatio(closes) >= ErMin;
		}

		// Choppiness Index over the last CiPeriod bars ending at barsAgo (closes carry one extra prior bar).
		private bool CiPassAt(int barsAgo)
		{
			int n = Math.Max(2, CiPeriod);
			if (CurrentBars[0] < barsAgo + n) return false;
			double[] highs = new double[n];
			double[] lows = new double[n];
			double[] closes = new double[n + 1];
			closes[0] = Closes[0][barsAgo + n];
			for (int i = 0; i < n; i++)
			{
				highs[i] = Highs[0][barsAgo + n - 1 - i];
				lows[i] = Lows[0][barsAgo + n - 1 - i];
				closes[i + 1] = Closes[0][barsAgo + n - 1 - i];
			}
			return Kat34ScalperLogic.ChoppinessIndex(highs, lows, closes) <= CiMax;
		}

		private bool TimePassAt(int barsAgo)
		{
			if (!cachedTime || timeWindowDisabled) return true;
			return Kat34ScalperLogic.IsInTimeWindow(Times[0][barsAgo].TimeOfDay, timeStart, timeEnd);
		}
	}
}
