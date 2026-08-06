/* StackEmaLogic.cs - pure Stack EMA direction and MTF mapping helpers. */

using System;

namespace KatStackEMA
{
	public enum StackEmaTimeframe
	{
		S30 = 30,
		M1 = 60,
		M3 = 180,
		M5 = 300,
		M15 = 900,
		M30 = 1800,
		H1 = 3600
	}

	public static class StackEmaLogic
	{
		public static int Direction(double price, double ema8, double ema21, double ema34, double ema55, double ema89)
		{
			if (double.IsNaN(price) || double.IsInfinity(price)
				|| double.IsNaN(ema8) || double.IsNaN(ema21) || double.IsNaN(ema34)
				|| double.IsNaN(ema55) || double.IsNaN(ema89)) return 0;
			if (price > ema8 && price > ema21 && price > ema34 && price > ema55 && price > ema89) return 1;
			if (price < ema8 && price < ema21 && price < ema34 && price < ema55 && price < ema89) return -1;
			return 0;
		}

		public static bool FilterPass(bool filterEnabled, bool forBuy, int[] directions, bool[] enabled)
		{
			if (!filterEnabled) return true;
			if (directions == null || enabled == null) return false;
			int expected = forBuy ? 1 : -1;
			int count = Math.Min(directions.Length, enabled.Length);
			for (int i = 0; i < count; i++)
			{
				if (!enabled[i]) continue;
				if (directions[i] != expected) return false;
			}
			return true;
		}

		public static DateTime ClosedBarCutoff(DateTime sourceBarOpen, double sourcePeriodSeconds, double targetPeriodSeconds)
		{
			return sourceBarOpen.AddSeconds(sourcePeriodSeconds - targetPeriodSeconds);
		}

		public static int BarsAgoAtOrBefore(Func<int, DateTime> timeAt, int maxBarsAgo, DateTime time)
		{
			if (timeAt == null || maxBarsAgo < 1) return -1;
			int lo = 0, hi = maxBarsAgo;
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (timeAt(mid) <= time) hi = mid; else lo = mid + 1;
			}
			return timeAt(lo) <= time ? lo : -1;
		}

		public static bool HasWarmup(int currentBar, int barsAgo, int period)
		{
			return period > 0 && barsAgo >= 0 && currentBar - barsAgo >= period;
		}

		public static string TimeframeLabel(StackEmaTimeframe timeframe)
		{
			switch (timeframe)
			{
				case StackEmaTimeframe.S30: return "30s";
				case StackEmaTimeframe.M1: return "1m";
				case StackEmaTimeframe.M3: return "3m";
				case StackEmaTimeframe.M5: return "5m";
				case StackEmaTimeframe.M15: return "15m";
				case StackEmaTimeframe.M30: return "30m";
				case StackEmaTimeframe.H1: return "1h";
				default: return ((int)timeframe) + "s";
			}
		}
	}
}
