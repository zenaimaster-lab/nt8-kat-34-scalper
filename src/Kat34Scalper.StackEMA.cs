/* Kat34Scalper.StackEMA.cs - Stack EMA filter adapter for the Scalper host. */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using KatStackEMA;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private EMA[] stackEma8;
		private EMA[] stackEma21;
		private EMA[] stackEma34;
		private EMA[] stackEma55;
		private EMA[] stackEma89;
		private int[] stackEmaSeries = { -1, -1, -1, -1, -1 };

		private void ConfigureStackEma()
		{
			int[] existingPeriods =
			{
				Math.Max(1, AlertA1PeriodSeconds),
				(int)AlertA1EmaZoneTf1,
				(int)AlertA1EmaZoneTf2,
				(int)AlertA1EmaZoneTf3
			};
			int[] existingSeries = { 1, 3, 4, 5 };
			int[] requested =
			{
				(int)StackEmaTimeframe1,
				(int)StackEmaTimeframe2,
				(int)StackEmaTimeframe3,
				(int)StackEmaTimeframe4,
				(int)StackEmaTimeframe5
			};
			stackEmaSeries = StackEmaLogic.MapRequestedSeries(existingPeriods, existingSeries, requested, 6);
			for (int i = 0; i < stackEmaSeries.Length; i++)
			{
				if (stackEmaSeries[i] < 6) continue;
				bool alreadyAdded = false;
				for (int j = 0; j < i; j++)
					if (stackEmaSeries[j] == stackEmaSeries[i]) alreadyAdded = true;
				if (!alreadyAdded) AddDataSeries(Data.BarsPeriodType.Second, requested[i]);
			}
		}

		private void LoadStackEma()
		{
			stackEma8 = new EMA[5];
			stackEma21 = new EMA[5];
			stackEma34 = new EMA[5];
			stackEma55 = new EMA[5];
			stackEma89 = new EMA[5];
			for (int i = 0; i < 5; i++)
			{
				int series = stackEmaSeries[i];
				stackEma8[i] = EMA(BarsArray[series], StackEmaEMA8);
				stackEma21[i] = EMA(BarsArray[series], StackEmaEMA21);
				stackEma34[i] = EMA(BarsArray[series], StackEmaEMA34);
				stackEma55[i] = EMA(BarsArray[series], StackEmaEMA55);
				stackEma89[i] = EMA(BarsArray[series], StackEmaEMA89);
			}
		}

		private bool IsStackEmaVisible(int pack)
		{
			switch (pack)
			{
				case 0: return StackEmaStack1Visible;
				case 1: return StackEmaStack2Visible;
				case 2: return StackEmaStack3Visible;
				case 3: return StackEmaStack4Visible;
				default: return StackEmaStack5Visible;
			}
		}

		private bool HasVisibleStackEma()
		{
			return StackEmaStack1Visible || StackEmaStack2Visible || StackEmaStack3Visible || StackEmaStack4Visible || StackEmaStack5Visible;
		}

		private int StackEmaDirectionAt(int pack, int barsAgo)
		{
			int series = stackEmaSeries[pack];
			int warmup = Math.Max(StackEmaEMA8, Math.Max(StackEmaEMA21, Math.Max(StackEmaEMA34, Math.Max(StackEmaEMA55, StackEmaEMA89))));
			if (CurrentBars == null || CurrentBars.Length <= series || CurrentBars[series] < warmup) return 0;
			DateTime cutoff = StackEmaLogic.ClosedBarCutoff(Times[0][barsAgo], SeriesPeriodSeconds(0), SeriesPeriodSeconds(series));
			int targetAgo = StackEmaLogic.BarsAgoAtOrBefore(i => Times[series][i], CurrentBars[series], cutoff);
			if (targetAgo < 0 || !StackEmaLogic.HasWarmup(CurrentBars[series], targetAgo, warmup)) return 0;
			return StackEmaLogic.Direction(Closes[series][targetAgo], stackEma8[pack][targetAgo], stackEma21[pack][targetAgo], stackEma34[pack][targetAgo], stackEma55[pack][targetAgo], stackEma89[pack][targetAgo]);
		}

		private bool StackEmaFilterPassAt(int barsAgo, bool forBuy)
		{
			if (!StackEmaFilterEnabled || !HasVisibleStackEma()) return true;
			int[] directions = new int[5];
			bool[] enabled = new bool[5];
			for (int i = 0; i < 5; i++)
			{
				directions[i] = StackEmaDirectionAt(i, barsAgo);
				enabled[i] = IsStackEmaVisible(i);
			}
			return StackEmaLogic.FilterPass(true, forBuy, directions, enabled);
		}

	}
}
