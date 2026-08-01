/* Kat8934Logic.cs - pure signal state machine, zero NT8 dependencies (unit-testable). */

using System;

namespace Kat8934
{
	public enum KatSignalKind
	{
		Sell,
		Buy
	}

	public enum KatTriggerMode
	{
		// Fire when, after the U-turn close, a later bar closes back through the fast EMA (retest).
		RetestBounce,
		// Fire immediately on the U-turn bar closing through the fast EMA.
		Breakdown
	}

	public static class Kat8934Logic
	{
		/// <summary>
		/// A0 ribbon fan. emasNow/emasPrev ordered fastest to slowest (9,21,34,55,89,144,200).
		/// Returns +1 = buy fan (ascending, spreading, wide enough), -1 = sell fan, 0 = no fan.
		/// Fan = strict order + total spread wider than lookback + at least minSpreadTicks.
		/// </summary>
		public static int FanDirection(double[] emasNow, double[] emasPrev, int minSpreadTicks, double tickSize)
		{
			if (emasNow == null || emasPrev == null || emasNow.Length < 2 || emasPrev.Length != emasNow.Length)
				return 0;

			int n = emasNow.Length;
			bool up = true, down = true;
			for (int i = 0; i < n - 1; i++)
			{
				if (emasNow[i] <= emasNow[i + 1]) up = false;
				if (emasNow[i] >= emasNow[i + 1]) down = false;
			}
			if (!up && !down) return 0;

			double spreadNow = Math.Abs(emasNow[0] - emasNow[n - 1]);
			double spreadPrev = Math.Abs(emasPrev[0] - emasPrev[n - 1]);
			if (spreadNow <= spreadPrev) return 0;          // must be spreading out
			if (spreadNow < minSpreadTicks * tickSize) return 0;
			return up ? 1 : -1;
		}

		/// <summary>Market filter: ADX strength + relative volume. volumeSma 0 disables the volume leg.</summary>
		public static bool PassMarketFilter(double adx, double adxMin, double volume, double volumeSma, double volumeMult)
		{
			if (adx < adxMin) return false;
			if (volumeSma > 0 && volume < volumeSma * volumeMult) return false;
			return true;
		}

		/// <summary>Time window in machine-local time. start == end disables the window (always true). Overnight (start &gt; end) wraps midnight.</summary>
		public static bool IsInTimeWindow(TimeSpan time, TimeSpan start, TimeSpan end)
		{
			if (start == end) return true;
			if (start < end) return time >= start && time < end;
			return time >= start || time < end;
		}

		/// <summary>
		/// Advances the per-side state machine by one bar. Caller owns the state flags.
		/// Sell: downtrend (ema34 below ema89) — price touches/crosses ema89, U-turns and closes
		/// back below ema34, then (RetestBounce) a later bar closes back above ema34 → Sell.
		/// Buy mirrors the same sequence.
		/// </summary>
		public static KatSignalKind? Update(
			KatSignalKind kind, KatTriggerMode mode,
			bool trendOk,
			double high, double low, double close,
			double ema34, double ema89,
			ref bool touched89, ref bool uturned)
		{
			if (!trendOk)
			{
				touched89 = false;
				uturned = false;
				return null;
			}

			if (kind == KatSignalKind.Sell)
			{
				if (!touched89 && high >= ema89) touched89 = true;
				if (touched89 && !uturned && close < ema34) uturned = true;
				if (touched89 && uturned)
				{
					if (mode == KatTriggerMode.Breakdown || close > ema34)
					{
						touched89 = false;
						uturned = false;
						return KatSignalKind.Sell;
					}
				}
			}
			else
			{
				if (!touched89 && low <= ema89) touched89 = true;
				if (touched89 && !uturned && close > ema34) uturned = true;
				if (touched89 && uturned)
				{
					if (mode == KatTriggerMode.Breakdown || close < ema34)
					{
						touched89 = false;
						uturned = false;
						return KatSignalKind.Buy;
					}
				}
			}

			return null;
		}
	}
}
