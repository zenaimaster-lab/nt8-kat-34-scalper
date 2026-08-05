/*
 * KatSignalB2.cs — Standalone Bot Signal B2 Indicator (89uturn34).
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
	public class KatSignalB2 : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "KAT Signal B2 (89uturn34) — Independent bot signal indicator.";
				Name = "KAT Signal B2 (89uturn34)";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
			}
			else if (State == State.DataLoaded)
			{
				Print("[KatSignalB2] Loaded (TBD implementation)");
			}
		}

		protected override void OnBarUpdate()
		{
			// TBD: B2 logic
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
