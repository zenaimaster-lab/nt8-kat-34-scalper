/*
 * Kat34Scalper.cs — Bot shell + orchestrator (NO embedded signal math).
 * Version: 1.04 (2026-08-05)
 * NinjaTrader 8 — KAT 34-ScalperBot.
 *
 * Architecture (v1.04):
 *   Signal indicators (independent, edit those for signal logic):
 *     indicators/KatA1.cs  Alert A1 EmaZone30s
 *     indicators/KatA2.cs  Alert A2 placeholder
 *     indicators/KatB1.cs  Bot signal B1 34bounce8+
 *     indicators/KatB2.cs  Bot signal B2 89uturn34
 *   Scalper shell (this + partials):
 *     Kat34Scalper.cs                 lifecycle, settings, OnBarUpdate orchestration
 *     src/Kat34Scalper.Orchestrator.cs read KatSignalBus → bot entries
 *     src/Kat34Scalper.Filter.cs      bot market/time gates
 *     src/Kat34Scalper.Bot.cs         orders / ATM / risk
 *     src/Kat34Scalper.Draw.cs        HUD only
 *     src/Kat34ScalperLogic.cs        pure math (shared, xunit)
 *     src/KatSignalBus.cs             IKatSignalProvider registry
 *
 * Usage: add KatB1/KatB2 (and optional KatA1) on the SAME chart as Kat34Scalper.
 * Scalper discovers them via KatSignalBus and trades enabled bot signals.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

public class Kat34ScalperAtmTemplateConverter : TypeConverter
{
	public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
	{
		var list = new List<string> { "None" };
		try
		{
			string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");
			if (Directory.Exists(dir))
			{
				var names = new List<string>();
				foreach (string f in Directory.GetFiles(dir, "*.xml"))
					names.Add(Path.GetFileNameWithoutExtension(f));
				names.Sort(StringComparer.OrdinalIgnoreCase);
				list.AddRange(names);
			}
		}
		catch { }
		return new StandardValuesCollection(list);
	}
}

public class Kat34ScalperSoundConverter : TypeConverter
{
	public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
	{
		var list = new List<string>();
		try
		{
			string userDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds");
			Directory.CreateDirectory(userDir);
			string installDir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
			list.AddRange(Kat34ScalperSound.ListSounds(userDir, installDir));
		}
		catch { }
		return new StandardValuesCollection(list);
	}
}

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper : Indicator
	{
		#region Shared State
		public const string VERSION = "1.04";
		public const string RELEASE_DATE = "2026-08-05";

		// Bot-side filter indicators only (no signal EMAs)
		private EMA fastEma;   // trend-flip guard for pending bot orders
		private EMA slowEma;
		private ADX adxInd;
		private ADX adxMtfInd; // BarsArray[1]
		private SMA volSmaInd;

		private TimeSpan timeStart;
		private TimeSpan timeEnd;
		private bool timeWindowDisabled;

		// Filter diagnostics (moved from deleted Signal.cs)
		private bool diagnosticGateInitialized;
		private bool diagnosticSellAllowed;
		private bool diagnosticBuyAllowed;
		#endregion

		#region Lifecycle
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"KAT 34-ScalperBot v" + VERSION + @" — bot shell; signals via independent KatA*/KatB* indicators.";
				Name = "Kat34Scalper";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				PaintPriceMarkers = true;
				IsSuspendedWhileInactive = true;

				// 1. Filters
				AdxPeriod = 60;
				AdxRisingBars = 5;
				AdxMtfMinutes = 3;
				AdxMtfPeriod = 14;
				AdxMtfMin = 22;
				ErPeriod = 40;
				ErMin = 0.25;
				CiPeriod = 40;
				CiMax = 50;
				VolumeSmaPeriod = 20;
				VolumeMinMult = 1.0;
				TimeFilterStart = "08:00";
				TimeFilterEnd = "17:00";
				AlertSound = "Alert1.wav";
				EmaFastPeriod = 34;
				EmaSlowPeriod = 89;

				// 2. Signal trade gates (which independent bot signals Scalper may execute)
				TradeB1 = true;
				TradeB2 = true;

				// 5. Bot
				BotEnabled = false;
				BotOrderQuantity = 1;
				BotAtmTemplate = "mnq. 1ct. 15-be20-35move15-50triggertrail5step1";
				BotAccountName = "Sim101";
				BotBufferTicks = 2;
				DailyMaxDDEnabled = false;
				DailyMaxDD = 500;
				DailyMaxProfitEnabled = false;
				DailyMaxProfit = 1000;

				// 6. ATM Quick Sets
				AtmSet1Name = "A"; AtmSet1Atm = "";
				AtmSet2Name = "B"; AtmSet2Atm = "";
				AtmSet3Name = "C"; AtmSet3Atm = "";
				AtmSet4Name = "D"; AtmSet4Atm = "";
				AtmSet5Name = "E"; AtmSet5Atm = "";
				AtmSet6Name = "F"; AtmSet6Atm = "";
			}
			else if (State == State.Configure)
			{
				// Only ADX MTF series — signal series live on KatA1/etc.
				AddDataSeries(Data.BarsPeriodType.Minute, Math.Max(1, AdxMtfMinutes));
			}
			else if (State == State.DataLoaded)
			{
				fastEma = EMA(BarsArray[0], EmaFastPeriod);
				slowEma = EMA(BarsArray[0], EmaSlowPeriod);
				adxInd = ADX(BarsArray[0], AdxPeriod);
				volSmaInd = SMA(Volumes[0], VolumeSmaPeriod);
				adxMtfInd = ADX(BarsArray[1], Math.Max(1, AdxMtfPeriod));

				timeWindowDisabled = string.Equals(TimeFilterStart, TimeFilterEnd, StringComparison.OrdinalIgnoreCase);
				if (!timeWindowDisabled)
				{
					TimeSpan.TryParse(TimeFilterStart, out timeStart);
					TimeSpan.TryParse(TimeFilterEnd, out timeEnd);
				}

				tradeB1 = TradeB1;
				tradeB2 = TradeB2;
				busKey = MakeBusKey();

				cachedBotAtm = BotAtmTemplate ?? "";
				cachedBotAccountName = BotAccountName ?? "";
				cachedBotOn = BotEnabled;
				cachedBotBufferTicks = BotBufferTicks;
				cachedIsDailyMaxDD = DailyMaxDDEnabled;
				cachedDailyMaxDD = DailyMaxDD;
				cachedIsDailyMaxProfit = DailyMaxProfitEnabled;
				cachedDailyMaxProfit = DailyMaxProfit;

				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(BuildHud);
			}
			else if (State == State.Terminated)
			{
				pendingMigrate = false;
				CancelPendingBotOrder("indicator terminated");
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(RemoveHud);
			}
		}
		#endregion

		#region Orchestration
		protected override void OnBarUpdate()
		{
			// BarsInProgress 1 = ADX MTF series only — ignore
			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;
			if (State != State.Realtime) return;

			double high = Highs[0][0];
			double low = Lows[0][0];
			double close = Closes[0][0];

			OrchestrateFromBus();          // read independent signals → bot
			ManageBotEntry(high, low, close);
		}
		#endregion

		#region Properties
		// --- 1. Filters ---
		[NinjaScriptProperty]
		[Display(Name = "ADX Period", Order = 1, GroupName = "1. Filters")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX Rising Lookback (bars)", Order = 2, GroupName = "1. Filters")]
		public int AdxRisingBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX MTF Timeframe (minutes)", Order = 3, GroupName = "1. Filters")]
		public int AdxMtfMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX MTF Period", Order = 4, GroupName = "1. Filters")]
		public int AdxMtfPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX MTF Min", Order = 5, GroupName = "1. Filters")]
		public double AdxMtfMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ER Period (bars)", Order = 6, GroupName = "1. Filters")]
		public int ErPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ER Min", Order = 7, GroupName = "1. Filters")]
		public double ErMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CI Period (bars)", Order = 8, GroupName = "1. Filters")]
		public int CiPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CI Max", Order = 9, GroupName = "1. Filters")]
		public double CiMax { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume SMA Period", Order = 10, GroupName = "1. Filters")]
		public int VolumeSmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume Min (x SMA)", Order = 11, GroupName = "1. Filters")]
		public double VolumeMinMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Time Start (HH:mm)", Order = 12, GroupName = "1. Filters")]
		public string TimeFilterStart { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Time End (HH:mm)", Order = 13, GroupName = "1. Filters")]
		public string TimeFilterEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Sound (shell default)", Order = 14, GroupName = "1. Filters",
			Description = "Reserved for shell alerts; signal indicators have their own sound settings.")]
		[TypeConverter(typeof(Kat34ScalperSoundConverter))]
		public string AlertSound { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trend Fast EMA (bot guard)", Order = 15, GroupName = "1. Filters",
			Description = "Used only for pending-order trend-flip cancel inside the bot shell.")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trend Slow EMA (bot guard)", Order = 16, GroupName = "1. Filters")]
		public int EmaSlowPeriod { get; set; }

		// --- 2. Signal trade gates ---
		[NinjaScriptProperty]
		[Display(Name = "Trade B1 (34bounce8+)", Order = 1, GroupName = "2. Signal Trade Gates",
			Description = "When ON, Scalper executes pending entries published by KatB1 on this chart.")]
		public bool TradeB1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trade B2 (89uturn34)", Order = 2, GroupName = "2. Signal Trade Gates",
			Description = "When ON, Scalper executes fires published by KatB2 on this chart.")]
		public bool TradeB2 { get; set; }

		// --- 5. Bot ---
		[NinjaScriptProperty]
		[Display(Name = "Bot Enabled", Order = 1, GroupName = "5. Bot")]
		public bool BotEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Order Quantity", Order = 2, GroupName = "5. Bot")]
		public int BotOrderQuantity
		{
			get { return botOrderQuantity; }
			set { botOrderQuantity = Math.Max(1, value); }
		}
		private int botOrderQuantity;

		[NinjaScriptProperty]
		[Display(Name = "ATM Template", Order = 3, GroupName = "5. Bot")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string BotAtmTemplate { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Account Name", Order = 4, GroupName = "5. Bot")]
		public string BotAccountName { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Buffer Ticks", Order = 5, GroupName = "5. Bot")]
		public int BotBufferTicks
		{
			get { return botBufferTicks; }
			set { botBufferTicks = Math.Max(0, value); cachedBotBufferTicks = botBufferTicks; }
		}
		private int botBufferTicks = 2;

		[NinjaScriptProperty]
		[Display(Name = "Daily Max DD Enabled", Order = 6, GroupName = "5. Bot")]
		public bool DailyMaxDDEnabled
		{
			get { return dailyMaxDDEnabled; }
			set { dailyMaxDDEnabled = value; cachedIsDailyMaxDD = value; }
		}
		private bool dailyMaxDDEnabled;

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Daily Max DD ($)", Order = 7, GroupName = "5. Bot")]
		public double DailyMaxDD
		{
			get { return dailyMaxDD; }
			set { dailyMaxDD = Math.Max(0, value); cachedDailyMaxDD = dailyMaxDD; }
		}
		private double dailyMaxDD;

		[NinjaScriptProperty]
		[Display(Name = "Daily Max Profit Enabled", Order = 8, GroupName = "5. Bot")]
		public bool DailyMaxProfitEnabled
		{
			get { return dailyMaxProfitEnabled; }
			set { dailyMaxProfitEnabled = value; cachedIsDailyMaxProfit = value; }
		}
		private bool dailyMaxProfitEnabled;

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Daily Max Profit ($)", Order = 9, GroupName = "5. Bot")]
		public double DailyMaxProfit
		{
			get { return dailyMaxProfit; }
			set { dailyMaxProfit = Math.Max(0, value); cachedDailyMaxProfit = dailyMaxProfit; }
		}
		private double dailyMaxProfit;

		// --- 6. ATM Quick Sets ---
		private string atmSet1Name = "A", atmSet2Name = "B", atmSet3Name = "C";
		private string atmSet4Name = "D", atmSet5Name = "E", atmSet6Name = "F";

		[NinjaScriptProperty]
		[Display(Name = "Set 1 Name", Order = 1, GroupName = "6. ATM Quick Sets")]
		public string AtmSet1Name { get { return atmSet1Name; } set { atmSet1Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "A"); } }
		[NinjaScriptProperty]
		[Display(Name = "Set 1 ATM", Order = 2, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet1Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 2 Name", Order = 3, GroupName = "6. ATM Quick Sets")]
		public string AtmSet2Name { get { return atmSet2Name; } set { atmSet2Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "B"); } }
		[NinjaScriptProperty]
		[Display(Name = "Set 2 ATM", Order = 4, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet2Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 3 Name", Order = 5, GroupName = "6. ATM Quick Sets")]
		public string AtmSet3Name { get { return atmSet3Name; } set { atmSet3Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "C"); } }
		[NinjaScriptProperty]
		[Display(Name = "Set 3 ATM", Order = 6, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet3Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 4 Name", Order = 7, GroupName = "6. ATM Quick Sets")]
		public string AtmSet4Name { get { return atmSet4Name; } set { atmSet4Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "D"); } }
		[NinjaScriptProperty]
		[Display(Name = "Set 4 ATM", Order = 8, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet4Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 5 Name", Order = 9, GroupName = "6. ATM Quick Sets")]
		public string AtmSet5Name { get { return atmSet5Name; } set { atmSet5Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "E"); } }
		[NinjaScriptProperty]
		[Display(Name = "Set 5 ATM", Order = 10, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet5Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 6 Name", Order = 11, GroupName = "6. ATM Quick Sets")]
		public string AtmSet6Name { get { return atmSet6Name; } set { atmSet6Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "F"); } }
		[NinjaScriptProperty]
		[Display(Name = "Set 6 ATM", Order = 12, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet6Atm { get; set; }
		#endregion
	}
}
