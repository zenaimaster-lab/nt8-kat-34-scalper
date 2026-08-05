/*
 * KatSignalB2.cs — Standalone Bot Signal B2 Indicator (89uturn34).
 * Independent NinjaTrader 8 indicator. Can be loaded on any chart.
 * Bot signal: detects 89-34 U-turn setup for potential entry points.
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
using KAT.Signals;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatSignalB2 : Indicator
	{
		private EMA b2Ema34;
		private EMA b2Ema89;

		private volatile bool cachedB2 = false;
		private volatile bool b2BackfillPending;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "KAT Signal B2 (89uturn34) — Independent bot signal indicator. Detects EMA89 U-turn.";
				Name = "KAT Signal B2 (89uturn34)";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;

				Ema34Period = 34;
				Ema89Period = 89;
				HistoryDays = 3;
			}
			else if (State == State.DataLoaded)
			{
				b2Ema34 = AddIndicator(new EMA() { Period = Ema34Period }) as EMA;
				b2Ema89 = AddIndicator(new EMA() { Period = Ema89Period }) as EMA;

				AddChartIndicator(b2Ema34);
				AddChartIndicator(b2Ema89);

				SetB2Signal(true);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			EvaluateB2Bar();
		}

		private void SetB2Signal(bool on)
		{
			cachedB2 = on;
			Print(string.Format("[KatSignalB2] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				b2BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
		}

		private void FlushBackfill()
		{
			if (b2BackfillPending)
			{
				b2BackfillPending = false;
				BackfillB2();
			}
		}

		private void EvaluateB2Bar()
		{
			if (!cachedB2) return;
			if (CurrentBars == null || CurrentBars[0] < 100) return;
			if (b2Ema34 == null || b2Ema89 == null) return;

			int dir = KatSignalCore.B2Direction(b2Ema34[0], b2Ema89[0], b2Ema89[1]);

			if (dir != 0)
			{
				Draw.VerticalLine(this, string.Format("K34S_B2_{0}", CurrentBars[0]), Times[0][0], dir > 0 ? Brushes.DodgerBlue : Brushes.Magenta, DashStyleHelper.Dash, 1);
				Print(string.Format("[KatSignalB2] {0} setup detected @ bar {1}: EMA89 U-turn + EMA34/EMA89 alignment", dir > 0 ? "BUY" : "SELL", CurrentBars[0]));
			}
		}

		private void BackfillB2()
		{
			if (!cachedB2) return;
			int start = Math.Min(100, CurrentBars[0] - 1);
			if (start < 0) return;
			for (int ago = start; ago >= 1; ago--)
			{
				int dir = KatSignalCore.B2Direction(b2Ema34[ago], b2Ema89[ago], b2Ema89[ago + 1]);
				if (dir != 0)
				{
					Draw.VerticalLine(this, string.Format("K34S_B2_BF_{0}", CurrentBars[0] - ago), Times[0][ago], dir > 0 ? Brushes.DodgerBlue : Brushes.Magenta, DashStyleHelper.Dash, 1);
				}
			}
			Print("[KatSignalB2] backfill done");
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 34 Period", Order = 1, GroupName = "Periods")]
		public int Ema34Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA 89 Period", Order = 2, GroupName = "Periods")]
		public int Ema89Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "History Days", Order = 3, GroupName = "Parameters")]
		public int HistoryDays { get; set; }
		#endregion
	}
}

#region NinjaScript generated code
namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Indicator
	{
		private KatSignalB2[] cacheKatSignalB2;

		public KatSignalB2 KatSignalB2()
		{
			return KatSignalB2(Close);
		}

		public KatSignalB2 KatSignalB2(ISeries<double> input)
		{
			if (cacheKatSignalB2 != null)
				for (int idx = 0; idx < cacheKatSignalB2.Length; idx++)
					if (cacheKatSignalB2[idx] != null && cacheKatSignalB2[idx].EqualsInput(input))
						return cacheKatSignalB2[idx];
			return CacheIndicator<KatSignalB2>(new KatSignalB2(), input, ref cacheKatSignalB2);
		}
	}
}
#endregion
