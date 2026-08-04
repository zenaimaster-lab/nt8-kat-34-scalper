/*
 * Kat34Scalper.cs — main module (lifecycle, settings, orchestration)
 * Version: 0.67 (2026-08-04)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy).
 *
 * Co-Authored-By: Oz <oz-agent@warp.dev>
 *
 * Module layout (partial classes):
 *   Kat34Scalper.cs                    — main: state, OnStateChange, OnBarUpdate orchestration, settings
 *   src/Kat34ScalperLogic.cs           — pure signal/filter math + ATM parser (zero NT8 deps, xunit-tested)
 *   src/Kat34Scalper.AlertSignal.cs    — Alert Signal module shared helpers (alert backfill)
 *   src/Kat34Scalper.AlertSignal.A1.cs — Alert Signal sub-module A1: fan 30s (independent, alert-only)
 *   src/Kat34Scalper.AlertSignal.A2.cs — Alert Signal sub-module A2: placeholder (independent, alert-only)
 *   src/Kat34Scalper.Signal.cs         — Bot Signal module shared helpers (backfill window)
 *   src/Kat34Scalper.Signal.B1.cs      — Bot Signal sub-module B1: 34bounce8+ (34+8+Bounce ema34-touch pending entry)
 *   src/Kat34Scalper.Signal.B2.cs      — Bot Signal sub-module B2: 89uturn34 (89-34 pullback setup)
 *   src/Kat34Scalper.Filter.cs         — Global Filter module: MTF, ADX, Volume, Time window gates
 *   src/Kat34Scalper.Bot.cs            — Bot module: order ops, stop/limit, ATM
 *   src/Kat34Scalper.Draw.cs           — Draw module: lines + ATM triggers + HUD
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
		public const string VERSION = "0.67";
		public const string RELEASE_DATE = "2026-08-04";


		// Indicator series (primary chart TF)
		private EMA fastEma;
		private EMA slowEma;
		private EMA ema8;
		private EMA ema144;
		private EMA ema200;
		private ADX adxInd;
		private SMA volSmaInd;

		// Alert Signal A1 (fan) — dedicated EMAs on the dedicated secondary series (BarsArray[1]).
		// Fully independent from the primary-series EMAs used by the Bot Signals (B1/B2).
		private EMA a1Ema8;
		private EMA a1Ema34;
		private EMA a1Ema144;
		private EMA a1Ema200;
		private ATR a1Atr; // angle normalization unit (45 deg = 1 ATR/bar on the A1 series)

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
				Description					= @"Kat34Scalper v" + VERSION + @" — EMA 34/89 rejection signals (Sell/Buy) with Alert Signals and Bot Signals.";
				Name						= "Kat34Scalper";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= true;
				IsSuspendedWhileInactive	= true;
				ShowVersion					= true;

				// 1. Filters defaults — every gate OFF; toggles boot OFF on every load (session-only)
				AdxPeriod					= 14;
				AdxMin						= 20;
				VolumeSmaPeriod				= 20;
				VolumeMinMult				= 1.0;
				TimeFilterStart				= "08:00";
				TimeFilterEnd				= "17:00";
				AlertSound					= "Alert1.wav";

				// 2. Alert Signal A1 (fan) defaults — ON (independent 30s series)
				AlertA1Enabled				= true;
				AlertA1HistoryDays			= 3;
				AlertA1PeriodSeconds		= 30;
				AlertA1CondEma8Above34		= true;
				AlertA1CondEma34Above144	= true;
				AlertA1CondEma144Above200	= true;
				AlertA1AngleEnabled			= false; // ponytail: default OFF — 30s-bar slope is tiny vs ATR so 30deg rarely hits; enable manually once norm feels right
				AlertA1AngleMin				= 30;
				AlertA1BreakBars			= 3;
				AlertA1AtrPeriod			= 14;
				AlertA1LineWidth			= 2;
				AlertA1LongColor			= Colors.LimeGreen;
				AlertA1ShortColor			= Colors.Red;

				// 2.5 Alert Signal A2 defaults — OFF
				AlertA2Enabled				= false;
				AlertA2HistoryDays			= 3;

				// 3. Bot Signal B1 (34bounce8+) defaults — OFF
				B1Enabled					= false;
				B1HistoryDays				= 3;
				B1CondEma8Above34			= true;
				B1CondEma34Above89			= true;
				B1CondEma89Above144			= true;
				B1CondEma144Above200		= true;
				B1EntryOffsetTicks			= 1;
				B1StopDistanceTicks			= 60;
				B1TargetDistanceTicks		= 120;

				// 3.5 Bot Signal B2 (89uturn34) defaults — OFF
				B2Enabled					= false;
				B2HistoryDays				= 3;
				EmaFastPeriod				= 34;
				EmaSlowPeriod				= 89;
				MaxSequenceBars				= 30;
				B2EntryOffsetTicks			= 1;
				B2StopDistanceTicks			= 60;
				B2TargetDistanceTicks		= 120;

				// 5. Bot defaults
				BotEnabled					= false;
				BotOrderQuantity			= 1;
				BotAtmTemplate				= "mnq. 1ct. 15-be20-35move15-50triggertrail5step1";
				BotAccountName				= "Sim101";
				BotBufferTicks				= 2;

				DailyMaxDDEnabled			= false;
				DailyMaxDD					= 500;
				DailyMaxProfitEnabled		= false;
				DailyMaxProfit				= 1000;

				// 6. ATM Quick Sets defaults — labels A–F, no ATM assigned
				AtmSet1Name					= "A";
				AtmSet1Atm					= "";
				AtmSet2Name					= "B";
				AtmSet2Atm					= "";
				AtmSet3Name					= "C";
				AtmSet3Atm					= "";
				AtmSet4Name					= "D";
				AtmSet4Atm					= "";
				AtmSet5Name					= "E";
				AtmSet5Atm					= "";
				AtmSet6Name					= "F";
				AtmSet6Atm					= "";

				// 4. Lines & Text defaults
				LineLengthBars				= 7;
				LineWidth					= 2;
				ArrowOffsetTicks			= 3;
				SellEntryLineColor			= Colors.Red;
				BuyEntryLineColor			= Colors.LimeGreen;
				SLLineColor					= Colors.Red;
				TPLineColor					= Colors.Green;
				SellTextColor				= Colors.Red;
				BuyTextColor				= Colors.LimeGreen;
			}
			else if (State == State.Configure)
			{
				// Alert Signal A1 (fan) dedicated timeframe — always added so BarsArray indexes stay
				// stable; the AlertA1Enabled toggle only gates evaluation. Series 1 = BarsArray[1].
				AddDataSeries(Data.BarsPeriodType.Second, Math.Max(1, AlertA1PeriodSeconds));
			}
			else if (State == State.DataLoaded)
			{
				fastEma = EMA(BarsArray[0], EmaFastPeriod);
				slowEma = EMA(BarsArray[0], EmaSlowPeriod);
				ema8 = EMA(BarsArray[0], 8);
				ema144 = EMA(BarsArray[0], 144);
				ema200 = EMA(BarsArray[0], 200);
				adxInd = ADX(BarsArray[0], AdxPeriod);
				volSmaInd = SMA(Volumes[0], VolumeSmaPeriod);

				// A1 (fan) — its own EMAs on the 30s series; nothing shared with B1/B2 series-0 EMAs.
				a1Ema8 = EMA(BarsArray[1], 8);
				a1Ema34 = EMA(BarsArray[1], 34);
				a1Ema144 = EMA(BarsArray[1], 144);
				a1Ema200 = EMA(BarsArray[1], 200);
				a1Atr = ATR(BarsArray[1], Math.Max(1, AlertA1AtrPeriod));

				timeWindowDisabled = string.Equals(TimeFilterStart, TimeFilterEnd, StringComparison.OrdinalIgnoreCase);
				if (!timeWindowDisabled)
				{
					TimeSpan.TryParse(TimeFilterStart, out timeStart);
					TimeSpan.TryParse(TimeFilterEnd, out timeEnd);
				}

				cachedAlertA1 = AlertA1Enabled;
				cachedAlertA2 = AlertA2Enabled;
				alertA1BackfillPending = AlertA1Enabled;
				alertA2BackfillPending = AlertA2Enabled;

				cachedB1 = B1Enabled;
				cachedB2 = B2Enabled;
				b1BackfillPending = B1Enabled;
				b2BackfillPending = B2Enabled;

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

		#region Orchestration (module pipeline per bar)
		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
				EvaluateAlertA1Bar();                                // Alert Signal sub-module A1 (fan) — dedicated 30s series
				return;
			}
			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;

			if (ShowVersion && !versionDrawn)
				DrawVersionLabel();
			ClearLegacySignalDrawings();
			RefreshSignalDrawings();

			// Backfill once per enable, at the last available bar (end of history or live bar).
			if (State == State.Realtime || CurrentBars[0] >= BarsArray[0].Count - 1)
			{
				FlushAlertBackfill();
				FlushBackfill();
			}
			if (State != State.Realtime) return;

			double high = Highs[0][0];
			double low = Lows[0][0];
			double close = Closes[0][0];

			bool sellAllowed, buyAllowed;
			PassFilters(out sellAllowed, out buyAllowed);          // Global Filter module (ADX, volume, time)
			EvaluateAlertA2(high, low, close, sellAllowed, buyAllowed); // Alert Signal sub-module A2
			EvaluateB1(high, low, close, sellAllowed, buyAllowed); // Bot Signal sub-module B1 (34bounce8+)
			EvaluateB2(high, low, close, sellAllowed, buyAllowed); // Bot Signal sub-module B2 (89uturn34)
			ManageBotEntry(high, low, close);                      // Bot module (pending entry lifecycle)
		}
		#endregion

		#region NinjaScript Properties
		[NinjaScriptProperty]
		[Display(Name = "Show Version Label", Order = 0, GroupName = "Parameters")]
		public bool ShowVersion { get; set; }

		// --- 1. Filters (market, time) ---
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
			Description = "Sound played on signals.")]
		[TypeConverter(typeof(Kat34ScalperSoundConverter))]
		public string AlertSound { get; set; }

		// --- 2. Alert Signal A1 (fan 30s — independent 30s series, alert-only, no Bot/order interaction) ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "Default ON. Alert Signal A1 (fan) generates sound alerts and vertical-line drawings only — fully independent from the Bot Signals.")]
		public bool AlertA1Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "How many days back Alert A1 signals are replayed and drawn.")]
		public int AlertA1HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Timeframe (seconds)", Order = 3, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "A1 runs on its own secondary series of this period (default 30s), regardless of the chart timeframe.")]
		public int AlertA1PeriodSeconds { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 8 above EMA 34", Order = 4, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "LONG: EMA 8 above EMA 34. SHORT mirrored. Toggle applies to both directions.")]
		public bool AlertA1CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 above EMA 144", Order = 5, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "LONG: EMA 34 above EMA 144. SHORT mirrored.")]
		public bool AlertA1CondEma34Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 144 above EMA 200", Order = 6, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "LONG: EMA 144 above EMA 200. SHORT mirrored.")]
		public bool AlertA1CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 slope angle", Order = 7, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "LONG: EMA 34 slope at least +Min Angle (rising). SHORT: at most -Min Angle (falling). Renamed in v0.67 so stale saved 'true' values from v0.65/0.66 drop and the OFF default applies.")]
		public bool AlertA1AngleEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Min Angle (deg)", Order = 8, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "Minimum EMA 34 slope angle in degrees — up for LONG, down for SHORT.")]
		public double AlertA1AngleMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Break Bars (invalid before re-arm)", Order = 9, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "After a fired environment, the condition must stay invalid this many consecutive A1 bars before it counts as broken and the next valid environment fires a new line. Prevents re-triggering on 1-2 bar wobbles.")]
		public int AlertA1BreakBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ATR Period (angle normalization)", Order = 10, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "ATR period on the A1 series used as the slope-angle normalization unit (a slope of 1 ATR per bar reads as 45 degrees). Auto-adapts per instrument — no manual price tuning.")]
		public int AlertA1AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width (px)", Order = 11, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "Vertical alert line thickness.")]
		public int AlertA1LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "LONG Line Color", Order = 12, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "Vertical line color for the LONG environment.")]
		[XmlIgnore]
		public Color AlertA1LongColor { get; set; }

		[Browsable(false)]
		public string AlertA1LongColorSerializable
		{
			get { return AlertA1LongColor.ToString(); }
			set { AlertA1LongColor = ParseColor(value, Colors.LimeGreen); }
		}

		[NinjaScriptProperty]
		[Display(Name = "SHORT Line Color", Order = 13, GroupName = "2. Alert Signal A1 — fan 30s",
			Description = "Vertical line color for the SHORT environment.")]
		[XmlIgnore]
		public Color AlertA1ShortColor { get; set; }

		[Browsable(false)]
		public string AlertA1ShortColorSerializable
		{
			get { return AlertA1ShortColor.ToString(); }
			set { AlertA1ShortColor = ParseColor(value, Colors.Red); }
		}

		// --- 2.5 Alert Signal A2 (Placeholder sub-module) ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "2.5 Alert Signal A2",
			Description = "Default OFF. Alert Signal A2 generates sound alerts and chart drawings only.")]
		public bool AlertA2Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "2.5 Alert Signal A2",
			Description = "How many days back Alert A2 signals are replayed and drawn.")]
		public int AlertA2HistoryDays { get; set; }

		// --- 3. Bot Signal B1 — 34bounce8+ ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Default OFF. When switched ON the B1 pending entries (ema34 bounce) are computed and executed by Bot if Bot is ON.")]
		public bool B1Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "How many days back the B1 setups are computed and drawn when B1 is switched ON.")]
		public int B1HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 8 above EMA 34", Order = 3, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 8 stays above (or touches) EMA 34 — never crosses down. SELL mirrored.")]
		public bool B1CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 above EMA 89", Order = 4, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 34 above EMA 89. SELL mirrored.")]
		public bool B1CondEma34Above89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 89 above EMA 144", Order = 5, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 89 above EMA 144. SELL mirrored.")]
		public bool B1CondEma89Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 144 above EMA 200", Order = 6, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 144 above EMA 200. SELL mirrored.")]
		public bool B1CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 7, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Buy entry above the touch candle's high / Sell entry below its low.")]
		public int B1EntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 8, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Fallback when the selected ATM template defines no StopLoss.")]
		public int B1StopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 9, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Fallback when the selected ATM template defines no Target.")]
		public int B1TargetDistanceTicks { get; set; }

		// --- 3.5 Bot Signal B2 — 89uturn34 ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Default OFF. When switched ON the B2 signals are computed, drawn, and executed by Bot if Bot is ON.")]
		public bool B2Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "How many days back the B2 signals are computed and drawn when B2 is switched ON.")]
		public int B2HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 3, GroupName = "3.5 Bot Signal B2 — 89uturn34")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 4, GroupName = "3.5 Bot Signal B2 — 89uturn34")]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max Sequence Bars", Order = 5, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "The whole sequence — pullback cross through the fast EMA, slow-EMA touch, U-turn close back through the fast EMA — must complete within this many bars, otherwise the setup expires.")]
		public int MaxSequenceBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 7, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Sell entry below the signal low / Buy entry above the signal high.")]
		public int B2EntryOffsetTicks { get; set; }
		[Browsable(false)]
		public int EntryOffsetTicks { get { return B2EntryOffsetTicks; } set { B2EntryOffsetTicks = value; } }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 8, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Fallback when the selected ATM template defines no StopLoss.")]
		public int B2StopDistanceTicks { get; set; }
		[Browsable(false)]
		public int StopDistanceTicks { get { return B2StopDistanceTicks; } set { B2StopDistanceTicks = value; } }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 9, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Fallback when the selected ATM template defines no Target.")]
		public int B2TargetDistanceTicks { get; set; }
		[Browsable(false)]
		public int TargetDistanceTicks { get { return B2TargetDistanceTicks; } set { B2TargetDistanceTicks = value; } }


		// --- 5. Bot (semi-auto — trades only while the HUD BOT button is ON) ---
		[NinjaScriptProperty]
		[Display(Name = "Bot Enabled", Order = 1, GroupName = "5. Bot",
			Description = "Master switch. The bot still trades only while the HUD BOT button is ON.")]
		public bool BotEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Order Quantity", Order = 2, GroupName = "5. Bot")]
		public int BotOrderQuantity
		{
			get { return botOrderQuantity; }
			set { botOrderQuantity = Math.Max(1, value); } // CreateOrder fails on 0/negative
		}
		private int botOrderQuantity;

		[NinjaScriptProperty]
		[Display(Name = "ATM Template", Order = 3, GroupName = "5. Bot",
			Description = "ATM strategy managing the entry (brackets). 'None' submits a bare stop order. Default: mnq 1ct bracket.")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string BotAtmTemplate { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Account Name", Order = 4, GroupName = "5. Bot",
			Description = "Account the bot trades on (also selectable on the HUD). Default: Sim101.")]
		public string BotAccountName { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Buffer Ticks", Order = 5, GroupName = "5. Bot", Description = "Buffer ticks for Breakeven (BE) stop loss offset.")]
		public int BotBufferTicks
		{
			get { return botBufferTicks; }
			set { botBufferTicks = Math.Max(0, value); cachedBotBufferTicks = botBufferTicks; }
		}
		private int botBufferTicks = 2;

		[NinjaScriptProperty]
		[Display(Name = "Daily Max DD Enabled", Order = 5, GroupName = "5. Bot", Description = "Enable Daily Max Drawdown limit protection.")]
		public bool DailyMaxDDEnabled
		{
			get { return dailyMaxDDEnabled; }
			set { dailyMaxDDEnabled = value; cachedIsDailyMaxDD = value; }
		}
		private bool dailyMaxDDEnabled;

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Daily Max DD ($)", Order = 6, GroupName = "5. Bot", Description = "Max daily drawdown limit in dollars (e.g. 500 for $500 max loss limit).")]
		public double DailyMaxDD
		{
			get { return dailyMaxDD; }
			set { dailyMaxDD = Math.Max(0, value); cachedDailyMaxDD = dailyMaxDD; }
		}
		private double dailyMaxDD;

		[NinjaScriptProperty]
		[Display(Name = "Daily Max Profit Enabled", Order = 7, GroupName = "5. Bot", Description = "Enable Daily Max Profit limit protection.")]
		public bool DailyMaxProfitEnabled
		{
			get { return dailyMaxProfitEnabled; }
			set { dailyMaxProfitEnabled = value; cachedIsDailyMaxProfit = value; }
		}
		private bool dailyMaxProfitEnabled;

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Daily Max Profit ($)", Order = 8, GroupName = "5. Bot", Description = "Max daily profit limit in dollars (e.g. 1000 for $1000 max profit limit).")]
		public double DailyMaxProfit
		{
			get { return dailyMaxProfit; }
			set { dailyMaxProfit = Math.Max(0, value); cachedDailyMaxProfit = dailyMaxProfit; }
		}
		private double dailyMaxProfit;

		// --- 6. ATM Quick Sets (HUD: 6 buttons under the ATM dropdown; click selects the assigned ATM) ---
		private string atmSet1Name = "A";
		private string atmSet2Name = "B";
		private string atmSet3Name = "C";
		private string atmSet4Name = "D";
		private string atmSet5Name = "E";
		private string atmSet6Name = "F";

		[NinjaScriptProperty]
		[Display(Name = "Set 1 Name", Order = 1, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet1Name
		{
			get { return atmSet1Name; }
			set { atmSet1Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "A"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 1 ATM", Order = 2, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet1Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 2 Name", Order = 3, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet2Name
		{
			get { return atmSet2Name; }
			set { atmSet2Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "B"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 2 ATM", Order = 4, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet2Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 3 Name", Order = 5, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet3Name
		{
			get { return atmSet3Name; }
			set { atmSet3Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "C"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 3 ATM", Order = 6, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet3Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 4 Name", Order = 7, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet4Name
		{
			get { return atmSet4Name; }
			set { atmSet4Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "D"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 4 ATM", Order = 8, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet4Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 5 Name", Order = 9, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet5Name
		{
			get { return atmSet5Name; }
			set { atmSet5Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "E"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 5 ATM", Order = 10, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet5Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 6 Name", Order = 11, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet6Name
		{
			get { return atmSet6Name; }
			set { atmSet6Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "F"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 6 ATM", Order = 12, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet6Atm { get; set; }

		// --- 4. Lines & Text ---
		[NinjaScriptProperty]
		[Display(Name = "Line Length (bars)", Order = 1, GroupName = "4. Lines & Text",
			Description = "Entry, SL and TP lines extend this many bars forward from the signal candle.")]
		public int LineLengthBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width (px)", Order = 2, GroupName = "4. Lines & Text")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Arrow Offset (ticks from candle)", Order = 3, GroupName = "4. Lines & Text",
			Description = "Distance between the signal candle and the arrow.")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Sell Entry Line Color", Order = 4, GroupName = "4. Lines & Text",
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
		[Display(Name = "Buy Entry Line Color", Order = 5, GroupName = "4. Lines & Text",
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
		[Display(Name = "SL Line Color", Order = 6, GroupName = "4. Lines & Text")]
		[XmlIgnore]
		public Color SLLineColor { get; set; }

		[Browsable(false)]
		public string SLLineColorSerializable
		{
			get { return SLLineColor.ToString(); }
			set { SLLineColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "TP Line Color", Order = 7, GroupName = "4. Lines & Text")]
		[XmlIgnore]
		public Color TPLineColor { get; set; }

		[Browsable(false)]
		public string TPLineColorSerializable
		{
			get { return TPLineColor.ToString(); }
			set { TPLineColor = ParseColor(value, Colors.Green); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Sell Text Color", Order = 8, GroupName = "4. Lines & Text",
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
		[Display(Name = "Buy Text Color", Order = 9, GroupName = "4. Lines & Text",
			Description = "BUY label color.")]
		[XmlIgnore]
		public Color BuyTextColor { get; set; }

		[Browsable(false)]
		public string BuyTextColorSerializable
		{
			get { return BuyTextColor.ToString(); }
			set { BuyTextColor = ParseColor(value, Colors.LimeGreen); }
		}

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
