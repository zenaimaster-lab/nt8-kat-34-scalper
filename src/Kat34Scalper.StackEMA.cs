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

		private void ConfigureStackEma()
		{
			AddDataSeries(Data.BarsPeriodType.Second, (int)StackEmaTimeframe1);
			AddDataSeries(Data.BarsPeriodType.Second, (int)StackEmaTimeframe2);
			AddDataSeries(Data.BarsPeriodType.Second, (int)StackEmaTimeframe3);
			AddDataSeries(Data.BarsPeriodType.Second, (int)StackEmaTimeframe4);
			AddDataSeries(Data.BarsPeriodType.Second, (int)StackEmaTimeframe5);
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
				int series = 6 + i;
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

		private int StackEmaDirectionAt(int pack, int barsAgo)
		{
			int series = 6 + pack;
			int warmup = Math.Max(StackEmaEMA8, Math.Max(StackEmaEMA21, Math.Max(StackEmaEMA34, Math.Max(StackEmaEMA55, StackEmaEMA89))));
			if (CurrentBars == null || CurrentBars.Length <= series || CurrentBars[series] < warmup) return 0;
			DateTime cutoff = StackEmaLogic.ClosedBarCutoff(Times[0][barsAgo], SeriesPeriodSeconds(0), SeriesPeriodSeconds(series));
			int targetAgo = StackEmaLogic.BarsAgoAtOrBefore(i => Times[series][i], CurrentBars[series], cutoff);
			if (targetAgo < 0 || !StackEmaLogic.HasWarmup(CurrentBars[series], targetAgo, warmup)) return 0;
			return StackEmaLogic.Direction(Closes[series][targetAgo], stackEma8[pack][targetAgo], stackEma21[pack][targetAgo], stackEma34[pack][targetAgo], stackEma55[pack][targetAgo], stackEma89[pack][targetAgo]);
		}

		private bool StackEmaFilterPassAt(int barsAgo, bool forBuy)
		{
			if (!StackEmaFilterEnabled) return true;
			int[] directions = new int[5];
			bool[] enabled = new bool[5];
			for (int i = 0; i < 5; i++)
			{
				directions[i] = StackEmaDirectionAt(i, barsAgo);
				enabled[i] = IsStackEmaVisible(i);
			}
			return StackEmaLogic.FilterPass(true, forBuy, directions, enabled);
		}

		private bool IsStackEmaSeries(int barsInProgress)
		{
			return barsInProgress >= 6 && barsInProgress <= 10;
		}
	}
}
