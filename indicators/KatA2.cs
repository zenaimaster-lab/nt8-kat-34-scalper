/*
 * KatA2.cs — Standalone Alert Signal A2 (placeholder).
 * Appears under Add Indicators → KAT → KatA2.
 * Chart/alert only. Logic placeholder — implements IKatSignalProvider for bus wiring.
 */

#region Using declarations
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatA2 : Indicator, IKatSignalProvider
	{
		private string busKey;

		public string SignalId { get { return "A2"; } }
		public bool IsBotSignal { get { return false; } }

		public KatSignalSnapshot GetSnapshot()
		{
			return new KatSignalSnapshot
			{
				SignalId = SignalId,
				IsBotSignal = false,
				Status = "PLACEHOLDER",
				Generation = 0
			};
		}

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
			else if (State == State.DataLoaded)
			{
				EnsureBusRegistered();
			}
			else if (State == State.Terminated)
			{
				UnregisterBus();
			}
		}

		protected override void OnBarUpdate()
		{
			EnsureBusRegistered();
			// Placeholder — no evaluation yet.
		}

		private void EnsureBusRegistered()
		{
			string key = MakeBusKey();
			if (string.IsNullOrEmpty(key)) return;
			if (key == busKey) return;
			if (!string.IsNullOrEmpty(busKey)) KatSignalBus.Unregister(busKey, this);
			busKey = key;
			KatSignalBus.Register(busKey, this);
		}

		private void UnregisterBus()
		{
			if (string.IsNullOrEmpty(busKey)) return;
			KatSignalBus.Unregister(busKey, this);
			busKey = null;
		}

		private string MakeBusKey()
		{
			string inst = Instrument != null ? Instrument.FullName : "?";
			string bp = "?";
			if (BarsArray != null && BarsArray.Length > 0 && BarsArray[0] != null && BarsArray[0].BarsPeriod != null)
				bp = BarsArray[0].BarsPeriod.ToString();
			else if (BarsPeriod != null)
				bp = BarsPeriod.ToString();
			int chartId = ChartControl != null ? ChartControl.GetHashCode() : 0;
			return KatSignalBus.MakeKey(inst, bp, chartId);
		}

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 1, GroupName = "1. KatA2")]
		public int HistoryDays { get; set; }
	}
}
