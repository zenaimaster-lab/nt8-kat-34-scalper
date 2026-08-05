/*
 * KatSignalB1.cs — Standalone Bot Signal B1 Indicator (34bounce8+).
 * Independent NinjaTrader 8 indicator. Can be loaded on any chart.
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
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatSignalB1 : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "KAT Signal B1 (34bounce8+) — Independent bot signal indicator.";
				Name = "KAT Signal B1 (34bounce8+)";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
			}
			else if (State == State.DataLoaded)
			{
				Print("[KatSignalB1] Loaded (TBD implementation)");
			}
		}

		protected override void OnBarUpdate()
		{
			// TBD: B1 logic
		}

		#region Properties
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
