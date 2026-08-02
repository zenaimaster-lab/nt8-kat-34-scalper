/*
 * Kat34Scalper.cs — main module (lifecycle, settings, orchestration)
 * Version: 0.21 (2026-08-02)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy).
 *
 * Module layout (partial classes):
 *   Kat34Scalper.cs            — main: state, OnStateChange, OnBarUpdate orchestration, settings
 *   src/Kat34ScalperLogic.cs   — pure signal/filter math + ATM parser (zero NT8 deps, xunit-tested)
 *   src/Kat34Scalper.Signal.cs — Signal module: sub-module A0 (EMA-ribbon fan) + sub-module A1 (89-34 pullback)
 *   src/Kat34Scalper.Filter.cs — Filter module: MTF fan, ADX, Volume, Time window gates
 *   src/Kat34Scalper.Bot.cs    — Bot module: signal -> order (stop/limit conversion), migration, ATM brackets
 *   src/Kat34Scalper.Draw.cs   — Draw module: entry/SL/TP/trigger lines, arrows, labels, HUD (module-titled sections)
 *
 * The version label shows the chart timeframe it computes on (always the primary series).
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

public enum Kat34ScalperTriggerMode
{
	[Display(Name = "Retest Bounce")]
	RetestBounce = 0,
	[Display(Name = "Breakdown")]
	Breakdown = 1
}

// Dropdown of the ATM strategy templates in NT8's templates\AtmStrategy folder (+ "None" = bare order).
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
				names.Sort(StringComparer.OrdinalIgnoreCase); // filesystem order is not deterministic
				list.AddRange(names);
			}
		}
		catch { }
		return new StandardValuesCollection(list);
	}
}

// Dropdown of the .wav files in NT8's sounds folder (for the Alert Sound setting).
public class Kat34ScalperSoundConverter : TypeConverter
{
	public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
	{
		var list = new List<string>();
		try
		{
			string dir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
			if (Directory.Exists(dir))
				foreach (string f in Directory.GetFiles(dir, "*.wav"))
					list.Add(Path.GetFileName(f));
		}
		catch { }
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return new StandardValuesCollection(list);
	}
}

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper : Indicator
	{
		#region Shared State (owned by main; module-specific state lives in its own file)
		public const string VERSION = "0.21";
		public const string RELEASE_DATE = "2026-08-02";

		// Indicator series (primary chart TF + optional MTF BarsArrays)
		private EMA fastEma;
		private EMA slowEma;
		private static readonly int[] FanPeriods = { 9, 21, 34, 55, 89, 144, 200 };
		private EMA[][] fanEmas; // [BarsArray index][period index] — 0=primary; MTF indexes in bip3m/bip5m/bip15m (-1 = series not added)
		private ADX adxInd;
		private SMA volSmaInd;
		private int bip3m = -1;  // BarsArray index of the 3m series (-1 = not added)
		private int bip5m = -1;
		private int bip15m = -1;

		// Time-window filter parsed values
		private TimeSpan timeStart;
		private TimeSpan timeEnd;
		private bool timeWindowDisabled;
		#endregion

		#region Indicator Lifecycle
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Kat34Scalper v" + VERSION + @" — EMA 34/89 rejection signals (Sell/Buy) with entry, SL and TP dash lines.";
				Name						= "Kat34Scalper";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= false;
				IsAutoScale					= false;
				DrawHorizontalGridLines		= false;
				DrawVerticalGridLines		= false;

				// Parameters
				ShowVersion					= true;

				// 1. Filters defaults
				FanFilterEnabled			= true;
				FanMinSpreadTicks			= 20;
				FanSpreadLookback			= 5;
				Use3mFan					= false;
				Use5mFan					= false;
				Use15mFan					= false;
				AdxPeriod					= 14;
				AdxMin						= 20;
				VolumeSmaPeriod				= 20;
				VolumeMinMult				= 1.0;
				TimeFilterStart				= "08:00";
				TimeFilterEnd				= "17:00";
				AlertSound					= "Alert1.wav";

				// 4. Bot defaults
				BotEnabled					= false;
				BotOrderQuantity			= 1;
				BotAtmTemplate				= "None";
				BotAccountName				= "";

				// 2. Signal defaults (Sell and Buy share the same mirrored mechanism)
				SignalEnabled				= true;
				EmaFastPeriod				= 34;
				EmaSlowPeriod				= 89;
				MaxSequenceBars				= 30;
				TriggerMode					= Kat34ScalperTriggerMode.RetestBounce;
				EntryOffsetTicks			= 1;
				StopDistanceTicks			= 60;
				TargetDistanceTicks			= 120;

				// 3. Lines & Text defaults
				LineLengthBars				= 7;
				LineWidth					= 2;
				ArrowOffsetTicks			= 3;
				SellEntryLineColor			= Colors.Red;
				BuyEntryLineColor			= Colors.LimeGreen;
				SLLineColor					= Colors.Red;
				TPLineColor					= Colors.Green;
				SellTextColor				= Colors.Red;
				BuyTextColor				= Colors.LimeGreen;
				ShowArrows					= true;
				ShowLabels					= false;
			}
			else if (State == State.Configure)
			{
				// Only add a secondary series for timeframes actually enabled — with every MTF filter
				// OFF (the default) the chart keeps its single primary series untouched.
				int next = 1;
				bip3m  = Use3mFan  ? next++ : -1;
				bip5m  = Use5mFan  ? next++ : -1;
				bip15m = Use15mFan ? next++ : -1;
				if (bip3m > 0)  AddDataSeries(Data.BarsPeriodType.Minute, 3);
				if (bip5m > 0)  AddDataSeries(Data.BarsPeriodType.Minute, 5);
				if (bip15m > 0) AddDataSeries(Data.BarsPeriodType.Minute, 15);
			}
			else if (State == State.DataLoaded)
			{
				fastEma = EMA(BarsArray[0], EmaFastPeriod);
				slowEma = EMA(BarsArray[0], EmaSlowPeriod);

				fanEmas = new EMA[BarsArray.Length][];
				for (int s = 0; s < BarsArray.Length; s++)
				{
					fanEmas[s] = new EMA[FanPeriods.Length];
					for (int p = 0; p < FanPeriods.Length; p++)
						fanEmas[s][p] = EMA(BarsArray[s], FanPeriods[p]);
				}
				adxInd = ADX(BarsArray[0], AdxPeriod);
				volSmaInd = SMA(Volumes[0], VolumeSmaPeriod);

				timeWindowDisabled = true;
				if (TimeSpan.TryParse(TimeFilterStart, out timeStart) && TimeSpan.TryParse(TimeFilterEnd, out timeEnd))
					timeWindowDisabled = false;
				else
					Print(string.Format("[Kat34Scalper] Bad time filter '{0}'-'{1}' — time window disabled.", TimeFilterStart, TimeFilterEnd));

				Print(string.Format("[Kat34Scalper] v{0} ({1}) loaded on {2} {3} — all signals compute on THIS series.",
					VERSION, RELEASE_DATE, Instrument.MasterInstrument.Name, ChartTimeframe()));
				cachedShowArrows = ShowArrows;
				cachedShowLabels = ShowLabels;
				cachedA1 = SignalEnabled;
				cachedBotAtm = BotAtmTemplate ?? "";
				cachedBotAccountName = BotAccountName ?? "";

				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(BuildHud);
			}
			else if (State == State.Terminated)
			{
				pendingMigrate = false;
				CancelPendingBotOrder("indicator terminated"); // never orphan a live order
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(RemoveHud);
			}
		}
		#endregion

		#region Orchestration (module pipeline per bar)
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;

			if (ShowVersion && !versionDrawn)
				DrawVersionLabel();

			double high = Highs[0][0];
			double low = Lows[0][0];
			double close = Closes[0][0];

			int a0Dir = EvaluateA0Fan();                           // Signal module — sub-module A0 (EMA-ribbon fan)
			bool sellAllowed, buyAllowed;
			PassFilters(a0Dir, out sellAllowed, out buyAllowed);   // Filter module (fan gate, MTF, ADX, volume, time)
			EvaluateA1(high, low, close, sellAllowed, buyAllowed); // Signal module — sub-module A1 (89-34 pullback)
			ManageBotEntry(high, low, close);                      // Bot module (pending entry lifecycle)
		}
		#endregion

		#region NinjaScript Properties
		[NinjaScriptProperty]
		[Display(Name = "Show Version Label", Order = 0, GroupName = "Parameters")]
		public bool ShowVersion { get; set; }

		// --- 1. Filters (A0 EMA-ribbon fan 9/21/34/55/89/144/200, MTF, market, time) ---
		[NinjaScriptProperty]
		[Display(Name = "A0 Fan Filter Enabled", Order = 1, GroupName = "1. Filters",
			Description = "A1 signals need the 9/21/34/55/89/144/200 EMA ribbon fanned out in the signal direction.")]
		public bool FanFilterEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fan Min Spread (ticks)", Order = 2, GroupName = "1. Filters",
			Description = "Minimum distance between EMA 9 and EMA 200 for a valid fan.")]
		public int FanMinSpreadTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fan Spread Lookback (bars)", Order = 3, GroupName = "1. Filters",
			Description = "The ribbon must be wider now than this many bars ago (spreading out).")]
		public int FanSpreadLookback { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use 3m Fan Filter", Order = 4, GroupName = "1. Filters",
			Description = "The 3-minute ribbon must fan in the same direction.")]
		public bool Use3mFan { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use 5m Fan Filter", Order = 5, GroupName = "1. Filters",
			Description = "The 5-minute ribbon must fan in the same direction.")]
		public bool Use5mFan { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use 15m Fan Filter", Order = 6, GroupName = "1. Filters",
			Description = "The 15-minute ribbon must fan in the same direction.")]
		public bool Use15mFan { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX Period", Order = 7, GroupName = "1. Filters")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX Min", Order = 8, GroupName = "1. Filters",
			Description = "Minimum ADX — blocks sideways/no-trend markets.")]
		public double AdxMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume SMA Period", Order = 9, GroupName = "1. Filters")]
		public int VolumeSmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume Min (x SMA)", Order = 10, GroupName = "1. Filters",
			Description = "Bar volume must be at least this multiple of its SMA — blocks dead bars.")]
		public double VolumeMinMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Time Start (HH:mm, machine local)", Order = 11, GroupName = "1. Filters",
			Description = "Trading window start. Equal start/end disables the window.")]
		public string TimeFilterStart { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Time End (HH:mm, machine local)", Order = 12, GroupName = "1. Filters",
			Description = "Trading window end. Overnight windows (start > end) wrap midnight.")]
		public string TimeFilterEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Sound", Order = 13, GroupName = "1. Filters",
			Description = "Sound played on A0 fan and A1 signals.")]
		[TypeConverter(typeof(Kat34ScalperSoundConverter))]
		public string AlertSound { get; set; }

		// --- 2. Signal (Sell and Buy share the same mirrored mechanism) ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "2. Signal")]
		public bool SignalEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 2, GroupName = "2. Signal")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 3, GroupName = "2. Signal")]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max Sequence Bars", Order = 4, GroupName = "2. Signal",
			Description = "The whole sequence — pullback cross through the fast EMA, slow-EMA touch, U-turn close back through the fast EMA (and the retest trigger) — must complete within this many bars, otherwise the setup expires.")]
		public int MaxSequenceBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Mode", Order = 5, GroupName = "2. Signal",
			Description = "Retest Bounce: Sell fires when price closes back above the fast EMA after the U-turn close below it (Buy mirrored). Breakdown: fire immediately on the U-turn close.")]
		public Kat34ScalperTriggerMode TriggerMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 6, GroupName = "2. Signal",
			Description = "Sell entry below the signal low / Buy entry above the signal high.")]
		public int EntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 7, GroupName = "2. Signal",
			Description = "Fallback when the selected ATM template defines no StopLoss.")]
		public int StopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 8, GroupName = "2. Signal",
			Description = "Fallback when the selected ATM template defines no Target.")]
		public int TargetDistanceTicks { get; set; }

		// --- 4. Bot (semi-auto — trades only while the HUD BOT button is ON) ---
		[NinjaScriptProperty]
		[Display(Name = "Bot Enabled", Order = 1, GroupName = "4. Bot",
			Description = "Master switch. The bot still trades only while the HUD BOT button is ON.")]
		public bool BotEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Order Quantity", Order = 2, GroupName = "4. Bot")]
		public int BotOrderQuantity
		{
			get { return botOrderQuantity; }
			set { botOrderQuantity = Math.Max(1, value); } // CreateOrder fails on 0/negative
		}
		private int botOrderQuantity;

		[NinjaScriptProperty]
		[Display(Name = "ATM Template", Order = 3, GroupName = "4. Bot",
			Description = "ATM strategy managing the entry (brackets). 'None' submits a bare stop order.")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string BotAtmTemplate { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Account Name", Order = 4, GroupName = "4. Bot",
			Description = "Account the bot trades on (also selectable on the HUD).")]
		public string BotAccountName { get; set; }

		// --- 3. Lines & Text ---
		[NinjaScriptProperty]
		[Display(Name = "Line Length (bars)", Order = 1, GroupName = "3. Lines & Text",
			Description = "Entry, SL and TP lines extend this many bars forward from the signal candle.")]
		public int LineLengthBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width (px)", Order = 2, GroupName = "3. Lines & Text")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Arrow Offset (ticks from candle)", Order = 3, GroupName = "3. Lines & Text",
			Description = "Distance between the signal candle and the arrow.")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Sell Entry Line Color", Order = 4, GroupName = "3. Lines & Text",
			Description = "Sell entry line (solid).")]
		[XmlIgnore]
		public Color SellEntryLineColor { get; set; }

		[Browsable(false)]
		public string SellEntryLineColorSerializable
		{
			get { return SellEntryLineColor.ToString(); }
			set { SellEntryLineColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Buy Entry Line Color", Order = 5, GroupName = "3. Lines & Text",
			Description = "Buy entry line (solid).")]
		[XmlIgnore]
		public Color BuyEntryLineColor { get; set; }

		[Browsable(false)]
		public string BuyEntryLineColorSerializable
		{
			get { return BuyEntryLineColor.ToString(); }
			set { BuyEntryLineColor = ParseColor(value, Colors.LimeGreen); }
		}

		[NinjaScriptProperty]
		[Display(Name = "SL Line Color", Order = 6, GroupName = "3. Lines & Text")]
		[XmlIgnore]
		public Color SLLineColor { get; set; }

		[Browsable(false)]
		public string SLLineColorSerializable
		{
			get { return SLLineColor.ToString(); }
			set { SLLineColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "TP Line Color", Order = 7, GroupName = "3. Lines & Text")]
		[XmlIgnore]
		public Color TPLineColor { get; set; }

		[Browsable(false)]
		public string TPLineColorSerializable
		{
			get { return TPLineColor.ToString(); }
			set { TPLineColor = ParseColor(value, Colors.Green); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Sell Text Color", Order = 8, GroupName = "3. Lines & Text",
			Description = "SELL label color.")]
		[XmlIgnore]
		public Color SellTextColor { get; set; }

		[Browsable(false)]
		public string SellTextColorSerializable
		{
			get { return SellTextColor.ToString(); }
			set { SellTextColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Buy Text Color", Order = 9, GroupName = "3. Lines & Text",
			Description = "BUY label color.")]
		[XmlIgnore]
		public Color BuyTextColor { get; set; }

		[Browsable(false)]
		public string BuyTextColorSerializable
		{
			get { return BuyTextColor.ToString(); }
			set { BuyTextColor = ParseColor(value, Colors.LimeGreen); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Show Arrows", Order = 10, GroupName = "3. Lines & Text",
			Description = "Draw the up/down arrow near the signal candle.")]
		public bool ShowArrows { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Buy/Sell Labels", Order = 11, GroupName = "3. Lines & Text",
			Description = "Draw the BUY/SELL text at the signal candle (default off).")]
		public bool ShowLabels { get; set; }

		private static Color ParseColor(string value, Color fallback)
		{
			try
			{
				var c = ColorConverter.ConvertFromString(value);
				if (c != null) return (Color)c;
			}
			catch { }
			return fallback;
		}
		#endregion
	}
}
