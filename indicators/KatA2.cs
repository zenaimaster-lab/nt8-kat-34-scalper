/*
 * KatA2.cs — Standalone Alert Signal A2 (placeholder).
 * Appears under Add Indicators → KAT → KatA2.
 * Chart/alert only. Logic placeholder — same role as Kat34Scalper.AlertSignal.A2.
 */

#region Using declarations
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatA2 : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"KatA2 — Alert Signal A2 placeholder (standalone, chart-only).";
				Name = "KatA2";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				IsSuspendedWhileInactive = true;
				HistoryDays = 3;
			}
		}

		protected override void OnBarUpdate()
		{
			// Placeholder — no evaluation yet (mirrors Scalper Alert A2 stub).
		}

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 1, GroupName = "1. KatA2")]
		public int HistoryDays { get; set; }
	}
}
