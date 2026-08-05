/*
 * KatSignalB1.cs — Standalone Bot Signal B1 Indicator (34bounce8+).
 * Independent NinjaTrader 8 indicator. Can be loaded on any chart.
 * Bot signal: detects 34+8+Bounce setup for potential entry points.
 */

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatSignalB1 : Indicator
	{
		private EMA b1Ema8;
		private EMA b1Ema34;
		private EMA b1Ema89;

		private volatile bool cachedB1 = false;
		private volatile bool b1BackfillPending;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "KAT Signal B1 (34bounce8+) — Independent bot signal indicator. Detects entry setup.";
				Name = "KAT Signal B1 (34bounce8+)";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;

				Ema8Period = 8;
				Ema34Period = 34;
				Ema89Period = 89;
				HistoryDays = 3;
			}
			else if (State == State.DataLoaded)
			{
				b1Ema8 = AddIndicator(new EMA() { Period = Ema8Period }) as EMA;
				b1Ema34 = AddIndicator(new EMA() { Period = Ema34Period }) as EMA;
				b1Ema89 = AddIndicator(new EMA() { Period = Ema89Period }) as EMA;

				AddChartIndicator(b1Ema8);
				AddChartIndicator(b1Ema34);
				AddChartIndicator(b1Ema89);

				SetB1Signal(true);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			EvaluateB1Bar();
		}

		private void SetB1Signal(bool on)
		{
			cachedB1 = on;
			Print(string.Format("[KatSignalB1] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				b1BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
		}

		private void FlushBackfill()
		{
			if (b1BackfillPending)
			{
				b1BackfillPending = false;
				BackfillB1();
			}
		}

		private void EvaluateB1Bar()
		{
			if (!cachedB1) return;
			if (CurrentBars == null || CurrentBars[0] < 100) return;
			if (b1Ema8 == null || b1Ema34 == null || b1Ema89 == null) return;

			// B1 setup: EMA8 touches/bounces off EMA34 while EMA34 < EMA89 (pullback setup)
			bool b1Setup = Math.Abs(b1Ema8[0] - b1Ema34[0]) < 0.1 * b1Ema34[0]; // 10% proximity
			bool pullback = (b1Ema34[0] < b1Ema89[0]);
			
			if (b1Setup && pullback)
			{
				Draw.VerticalLine(this, string.Format("K34S_B1_{0}", CurrentBars[0]), Times[0][0], Brushes.Purple, DashStyleHelper.Dash, 1);
				Print(string.Format("[KatSignalB1] Setup detected @ bar {0}: EMA8≈EMA34, pullback mode", CurrentBars[0]));
			}
		}

		private void BackfillB1()
		{
			if (!cachedB1) return;
			int start = Math.Min(100, CurrentBars[0] - 1);
			if (start < 0) return;
			for (int ago = start; ago >= 0; ago--)
			{
				bool b1Setup = Math.Abs(b1Ema8[ago] - b1Ema34[ago]) < 0.1 * b1Ema34[ago];
				bool pullback = (b1Ema34[ago] < b1Ema89[ago]);
				if (b1Setup && pullback)
				{
					Draw.VerticalLine(this, string.Format("K34S_B1_BF_{0}", CurrentBars[0] - ago), Times[0][ago], Brushes.Purple, DashStyleHelper.Dash, 1);
				}
			}
			Print("[KatSignalB1] backfill done");
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 8 Period", Order = 1, GroupName = "Periods")]
		public int Ema8Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 34 Period", Order = 2, GroupName = "Periods")]
		public int Ema34Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 89 Period", Order = 3, GroupName = "Periods")]
		public int Ema89Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "History Days", Order = 4, GroupName = "Parameters")]
		public int HistoryDays { get; set; }
		#endregion
	}
}

#region NinjaScript generated code
namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Indicator
	{
		private KatSignalB1[] cacheKatSignalB1;

		public KatSignalB1 KatSignalB1()
		{
			return KatSignalB1(Close);
		}

		public KatSignalB1 KatSignalB1(ISeries<double> input)
		{
			if (cacheKatSignalB1 != null)
				for (int idx = 0; idx < cacheKatSignalB1.Length; idx++)
					if (cacheKatSignalB1[idx] != null && cacheKatSignalB1[idx].EqualsInput(input))
						return cacheKatSignalB1[idx];
			return CacheIndicator<KatSignalB1>(new KatSignalB1(), input, ref cacheKatSignalB1);
		}
	}
}
#endregion
