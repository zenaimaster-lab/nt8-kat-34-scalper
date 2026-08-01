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
