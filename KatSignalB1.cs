/*
 * KatSignalB1.cs — Standalone Bot Signal B1 Indicator (34bounce8+).
 * Independent NinjaTrader 8 indicator. Can be loaded on any chart.
 * Bot signal: detects 34+8+Bounce setup for potential entry points.
 */

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
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
				AlertSoundCustomPath = "";
				LongAlertSound = "Alert1.wav";
				ShortAlertSound = "Alert1.wav";
				RangingAlertSound = "None";
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

			int dir = KatSignalCore.B1Direction(true, true, false, false,
				b1Ema8[0], b1Ema34[0], b1Ema89[0], 0, 0, 0.10);

			if (dir != 0)
			{
				Draw.VerticalLine(this, string.Format("K34S_B1_{0}", CurrentBars[0]), Times[0][0], dir > 0 ? Brushes.LimeGreen : Brushes.Purple, DashStyleHelper.Dash, 1);
				PlaySignalSound(dir);
				Print(string.Format("[KatSignalB1] {0} setup detected @ bar {1}: EMA8≈EMA34 + EMA34/EMA89 alignment", dir > 0 ? "BUY" : "SELL", CurrentBars[0]));
			}
		}

		private void PlaySignalSound(int direction)
		{
			try
			{
				string sound = direction > 0 ? LongAlertSound : direction < 0 ? ShortAlertSound : RangingAlertSound;
				string userDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds");
				string installDir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
				string path = Kat34ScalperSound.ResolvePath(AlertSoundCustomPath, userDir, installDir, sound);
				if (path != null) PlaySound(path);
			}
			catch { }
		}

		private void BackfillB1()
		{
			if (!cachedB1) return;
			int start = Math.Min(100, CurrentBars[0] - 1);
			if (start < 0) return;
			for (int ago = start; ago >= 0; ago--)
			{
				int dir = KatSignalCore.B1Direction(true, true, false, false,
					b1Ema8[ago], b1Ema34[ago], b1Ema89[ago], 0, 0, 0.10);
				if (dir != 0)
				{
					Draw.VerticalLine(this, string.Format("K34S_B1_BF_{0}", CurrentBars[0] - ago), Times[0][ago], dir > 0 ? Brushes.LimeGreen : Brushes.Purple, DashStyleHelper.Dash, 1);
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

		[NinjaScriptProperty]
		[Display(Name = "Alert Sound Custom Path", Order = 1, GroupName = "Alert Sounds")]
		public string AlertSoundCustomPath { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "LONG Sound", Order = 2, GroupName = "Alert Sounds")]
		[TypeConverter(typeof(KatSignalSoundConverter))]
		public string LongAlertSound { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SHORT Sound", Order = 3, GroupName = "Alert Sounds")]
		[TypeConverter(typeof(KatSignalSoundConverter))]
		public string ShortAlertSound { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RANGING Sound", Order = 4, GroupName = "Alert Sounds")]
		[TypeConverter(typeof(KatSignalSoundConverter))]
		public string RangingAlertSound { get; set; }
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
