/*
 * Kat8934.cs
 * Version: 0.17 (2026-08-02)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy): pullback from beyond ema34,
 * ema89 touch, U-turn close back through ema34, all within Max Sequence Bars.
 * Entry/SL/TP lines + ATM trailing-SL trigger lines (BE/SL1/SL2, KatTradeManager style).
 * A0 EMA-ribbon fan filter (9..200) with MTF (3m/5m/15m), ADX/volume and time-window gates, alert sound.
 * The version label shows the chart timeframe it computes on (always the primary series).
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat8934;
#endregion

public enum Kat8934TriggerMode
{
	[Display(Name = "Retest Bounce")]
	RetestBounce = 0,
	[Display(Name = "Breakdown")]
	Breakdown = 1
}

// Dropdown of the ATM strategy templates in NT8's templates\AtmStrategy folder (+ "None" = bare order).
public class Kat8934AtmTemplateConverter : TypeConverter
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
public class Kat8934SoundConverter : TypeConverter
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
	public class Kat8934 : Indicator
	{
		#region Metadata & State
		public const string VERSION = "0.17";
		public const string RELEASE_DATE = "2026-08-02";

		private EMA fastEma;
		private EMA slowEma;
		private readonly KatA1State sellState = new KatA1State();
		private readonly KatA1State buyState = new KatA1State();
		private string atmLevelsName = "\0"; // never matches a real template name — forces first parse
		private Kat8934AtmData atmLevels;
		private bool versionDrawn;
		private volatile bool cachedShowArrows = true;
		private volatile bool cachedShowLabels;

		// 1. Filters (A0 fan, MTF, market, time)
		private static readonly int[] FanPeriods = { 9, 21, 34, 55, 89, 144, 200 };
		private EMA[][] fanEmas; // [BarsArray index][period index] — 0=primary; MTF indexes in bip3m/bip5m/bip15m (-1 = series not added)
		private ADX adxInd;
		private SMA volSmaInd;
		private TimeSpan timeStart;
		private TimeSpan timeEnd;
		private bool timeWindowDisabled;
		private int a0Dir;        // current primary fan direction: -1 sell, 0 none, +1 buy
		private bool a0Alerted;   // A0 alert already fired for the current fan episode
		private volatile bool cachedA0 = true;
		private volatile bool cachedMtf = true;
		private volatile bool cachedAdx = true;
		private volatile bool cachedVol = true;
		private volatile bool cachedTime = true;

		// 4. Bot (semi-auto: trades only while the HUD BOT button is ON)
		private volatile bool cachedBotOn;
		private volatile string cachedBotAtm = "";
		private volatile string cachedBotAccountName = "";
		private Order pendingOrder;
		private bool pendingIsBuy;
		private double pendingBestRef;   // best extreme used for migration (sell: highest qualifying low / buy: lowest high)
		private double pendingMigrateRef; // better extreme found; new order placed once the cancelled one is terminal
		private volatile bool pendingMigrate;
		private int bip3m = -1;  // BarsArray index of the 3m series (-1 = not added)
		private int bip5m = -1;
		private int bip15m = -1;
		private const int MAX_SIGNAL_RECORDS = 200;
		private sealed class KatSignalRecord
		{
			public int Bar;
			public bool IsBuy;
			public double ArrowY;
			public double ArrowY2;
			public double TextY;
		}
		private readonly List<KatSignalRecord> signalRecords = new List<KatSignalRecord>();
		private Border hudBorder;
		private Canvas hudCanvas;
		private TextBlock hudStatusText;
		private System.Windows.Threading.DispatcherTimer hudStatusTimer;
		private bool isHudDragging;
		private bool hasHudDragPosition;
		private double hudDragLeft;
		private double hudDragTop;
		private double hudDragStartLeft;
		private double hudDragStartTop;
		private Point hudDragStart;
		private readonly SolidColorBrush hudOnBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204));
		private readonly SolidColorBrush hudOffBrush = new SolidColorBrush(Color.FromRgb(45, 50, 65));
		#endregion

		#region Indicator Lifecycle
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Kat8934 v" + VERSION + @" — EMA 34/89 rejection signals (Sell/Buy) with entry, SL and TP dash lines.";
				Name						= "Kat8934";
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
			TriggerMode					= Kat8934TriggerMode.RetestBounce;
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
				Print(string.Format("[Kat8934] Bad time filter '{0}'-'{1}' — time window disabled.", TimeFilterStart, TimeFilterEnd));

			Print(string.Format("[Kat8934] v{0} ({1}) loaded on {2} {3} — all signals compute on THIS series.",
			VERSION, RELEASE_DATE, Instrument.MasterInstrument.Name, ChartTimeframe()));
			cachedShowArrows = ShowArrows;
			cachedShowLabels = ShowLabels;
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

		#region Signal Evaluation & Drawing
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;

			if (ShowVersion && !versionDrawn)
				DrawVersionLabel();

			double high = Highs[0][0];
			double low = Lows[0][0];
			double close = Closes[0][0];

		EvaluateFilters();

		bool fanOff = !cachedA0 || !FanFilterEnabled;
		bool filtersPass = (fanOff || a0Dir != 0) && MtfPass(a0Dir) && MarketPass() && TimePass();
		bool sellAllowed = filtersPass && (fanOff || a0Dir < 0);
		bool buyAllowed  = filtersPass && (fanOff || a0Dir > 0);

		if (SignalEnabled && fastEma != null && slowEma != null
			&& CurrentBars[0] >= Math.Max(EmaFastPeriod, EmaSlowPeriod))
		{
			double fast = fastEma[0];
			double slow = slowEma[0];
			KatTriggerMode mode = ToLogicMode(TriggerMode);
		if (sellAllowed && Kat8934Logic.Update(KatSignalKind.Sell, mode, MaxSequenceBars,
			fast < slow, high, low, close, fast, slow, sellState) == KatSignalKind.Sell)
		{
			DrawSignal(false, CurrentBar, high, low, sellState.C1, sellState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
			TrySubmitBotEntry(false, sellState.C2);
		}
		if (buyAllowed && Kat8934Logic.Update(KatSignalKind.Buy, mode, MaxSequenceBars,
			fast > slow, high, low, close, fast, slow, buyState) == KatSignalKind.Buy)
		{
			DrawSignal(true, CurrentBar, high, low, buyState.C1, buyState.C2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
			TrySubmitBotEntry(true, buyState.C2);
		}
		}

		ManageBotEntry(high, low, close);
	}

	#region Bot Order Operations (semi-auto — runs only while the HUD BOT button is ON)
	private Account ResolveBotAccount()
	{
		string name = cachedBotAccountName;
		if (string.IsNullOrEmpty(name) || Account.All == null) return null;
		foreach (Account acc in Account.All)
			if (acc.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return acc;
		return null;
	}

	private bool HasAtmTemplate(string tpl)
	{
		return !string.IsNullOrEmpty(tpl)
			&& !tpl.Equals("None", StringComparison.OrdinalIgnoreCase)
			&& File.Exists(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy", tpl + ".xml"));
	}

	// Called from the data thread after a signal fires. refExtreme = best candidate extreme (sell: c2 low / buy: c2 high).
	private void TrySubmitBotEntry(bool isBuy, double refExtreme)
	{
		if (!cachedBotOn || !BotEnabled || refExtreme == 0) return;
		if (pendingOrder != null || pendingMigrate) return; // one bot order at a time
		SubmitBotOrder(isBuy, refExtreme);
	}

	private void SubmitBotOrder(bool isBuy, double refExtreme)
	{
		Account acc = ResolveBotAccount();
		if (acc == null)
		{
			Print("[Kat8934] BOT: no account selected — pick one on the HUD or in settings.");
			return;
		}
		double stopPrice = isBuy
			? refExtreme + EntryOffsetTicks * TickSize
			: refExtreme - EntryOffsetTicks * TickSize;
		try
		{
			// ATM contract: the entry order name MUST be "Entry" (see KatTradeManager).
			Order order = acc.CreateOrder(Instrument,
				isBuy ? OrderAction.Buy : OrderAction.Sell,
				OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Gtc,
				BotOrderQuantity, 0, stopPrice, "", "Entry", NinjaTrader.Core.Globals.MaxDate, null);

			pendingOrder = order;
			pendingIsBuy = isBuy;
			pendingBestRef = refExtreme;

			string tpl = cachedBotAtm;
			if (HasAtmTemplate(tpl))
				NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, order);
			else
			{
				if (!string.IsNullOrEmpty(tpl) && !tpl.Equals("None", StringComparison.OrdinalIgnoreCase))
					Print(string.Format("[Kat8934] BOT: ATM template '{0}' not found — bare stop order.", tpl));
				acc.Submit(new[] { order });
			}
			Print(string.Format("[Kat8934] BOT: {0} stop @ {1:F5} submitted (account {2}, ATM {3}).",
				isBuy ? "BUY" : "SELL", stopPrice, acc.Name, HasAtmTemplate(tpl) ? tpl : "none"));
			ShowHudStatus(string.Format("BOT: {0} stop @ {1:F2} ({2})", isBuy ? "BUY" : "SELL", stopPrice, HasAtmTemplate(tpl) ? tpl : "no ATM"), Brushes.LightGreen);
		}
		catch (Exception ex)
		{
			pendingOrder = null;
			Print(string.Format("[Kat8934] BOT submit error: {0}", ex.Message));
			ShowHudStatus("BOT submit error: " + ex.Message, Brushes.OrangeRed);
		}
	}

	// Polls the pending order on the data thread: terminal cleanup, trend-flip cancel, migrate to a better extreme.
	private void ManageBotEntry(double high, double low, double close)
	{
		if (pendingOrder == null)
		{
			// A cancelled order left a better entry behind — re-place it while the setup still holds.
			if (pendingMigrate && cachedBotOn && BotEnabled)
			{
				pendingMigrate = false;
				if (fastEma != null && slowEma != null
					&& (pendingIsBuy ? fastEma[0] > slowEma[0] && close > fastEma[0] : fastEma[0] < slowEma[0] && close < fastEma[0]))
					SubmitBotOrder(pendingIsBuy, pendingMigrateRef);
			}
			return;
		}

		OrderState state = pendingOrder.OrderState;
		if (state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected)
		{
			Print(string.Format("[Kat8934] BOT: entry order {0} @ {1:F5}.", state, pendingOrder.StopPrice));
			if (state == OrderState.Filled)
				ShowHudStatus(string.Format("BOT: entry FILLED @ {0:F2} — ATM manages brackets", pendingOrder.StopPrice), Brushes.LightGreen);
			pendingOrder = null;
			return; // filled: ATM owns the brackets from here
		}
		if (state != OrderState.Working && state != OrderState.Accepted) return;
		if (fastEma == null || slowEma == null) return;

		// Trend flipped — cancel the pending entry.
		if (pendingIsBuy ? fastEma[0] < slowEma[0] : fastEma[0] > slowEma[0])
		{
			CancelPendingBotOrder("trend flip");
			return;
		}

		// Migration: a newer bar closed on the setup side of ema34 with a better extreme.
		if (!pendingIsBuy && close < fastEma[0] && low > pendingBestRef)
		{
			pendingBestRef = low;
			pendingMigrateRef = low;
			pendingMigrate = true;
			CancelPendingBotOrder("migrate to higher sell stop");
		}
		else if (pendingIsBuy && close > fastEma[0] && high < pendingBestRef)
		{
			pendingBestRef = high;
			pendingMigrateRef = high;
			pendingMigrate = true;
			CancelPendingBotOrder("migrate to lower buy stop");
		}
	}

	private void CancelPendingBotOrder(string reason)
	{
		if (pendingOrder == null) return;
		try
		{
			Account acc = ResolveBotAccount();
			if (acc != null)
			{
				acc.Cancel(new[] { pendingOrder });
				Print(string.Format("[Kat8934] BOT: entry cancel requested ({0}).", reason));
				ShowHudStatus("BOT: entry cancel — " + reason, Brushes.OrangeRed);
			}
		}
		catch (Exception ex)
		{
			Print(string.Format("[Kat8934] BOT cancel error: {0}", ex.Message));
		}
	}
	#endregion


	#region Filters (A0 fan, MTF, market, time)
	private void EvaluateFilters()
	{
		a0Dir = 0;
		if (!cachedA0 || !FanFilterEnabled) return;
		if (fanEmas == null || CurrentBars[0] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback) return;

		a0Dir = SeriesFanDirection(0);
		if (a0Dir != 0 && !a0Alerted)
		{
			a0Alerted = true;
			PlayAlertSound();
			double y = a0Dir > 0 ? Lows[0][0] - ArrowOffsetTicks * TickSize : Highs[0][0] + ArrowOffsetTicks * TickSize;
			if (a0Dir > 0)
				Draw.TriangleUp(this, "K8934_A0_" + CurrentBar, false, 0, y, Brushes.DodgerBlue);
			else
				Draw.TriangleDown(this, "K8934_A0_" + CurrentBar, false, 0, y, Brushes.OrangeRed);
			Print(string.Format("[Kat8934] A0 {0} fan @ bar {1}", a0Dir > 0 ? "BUY" : "SELL", CurrentBar));
		}
		else if (a0Dir == 0)
		{
			a0Alerted = false; // fan collapsed — re-arm the alert
		}
	}

	private int SeriesFanDirection(int s)
	{
		if (CurrentBars[s] < FanPeriods[FanPeriods.Length - 1] + FanSpreadLookback) return 0;
		double[] now = new double[FanPeriods.Length];
		double[] prev = new double[FanPeriods.Length];
		for (int p = 0; p < FanPeriods.Length; p++)
		{
			now[p] = fanEmas[s][p][0];
			prev[p] = fanEmas[s][p][FanSpreadLookback];
		}
		return Kat8934Logic.FanDirection(now, prev, FanMinSpreadTicks, TickSize);
	}

	private bool MtfPass(int dir)
	{
		if (!cachedMtf || dir == 0) return true;
		if (bip3m > 0  && SeriesFanDirection(bip3m) != dir) return false;
		if (bip5m > 0  && SeriesFanDirection(bip5m) != dir) return false;
		if (bip15m > 0 && SeriesFanDirection(bip15m) != dir) return false;
		return true;
	}

	private bool MarketPass()
	{
		double adxMin = cachedAdx ? AdxMin : 0;
		double volSma = cachedVol && volSmaInd != null ? volSmaInd[0] : 0;
		double adx = adxInd != null ? adxInd[0] : 0;
		return Kat8934Logic.PassMarketFilter(adx, adxMin, Volumes[0][0], volSma, VolumeMinMult);
	}

	private bool TimePass()
	{
		if (!cachedTime || timeWindowDisabled) return true;
		return Kat8934Logic.IsInTimeWindow(Times[0][0].TimeOfDay, timeStart, timeEnd);
	}

	private void PlayAlertSound()
	{
		try { PlaySound(Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", AlertSound)); }
		catch { }
	}
	#endregion


		#region HUD Panel & Drawings
		// Primary-series timeframe, e.g. "30 Second" — proof the indicator computes on the chart TF it was added to.
		private string ChartTimeframe()
		{
			return BarsArray[0].BarsPeriod.Value + " " + BarsArray[0].BarsPeriod.BarsPeriodType;
		}

		private void DrawVersionLabel()
		{
			versionDrawn = true;
			Draw.TextFixed(this, "K8934_version", string.Format("Kat8934 v{0} ({1}) [{2}]", VERSION, RELEASE_DATE, ChartTimeframe()), TextPosition.TopLeft);
		}

		// Called from the data thread (marshaled via Dispatcher.InvokeAsync from HUD clicks).
		private void ClearOldSignalDrawings()
		{
			try
			{
				signalRecords.Clear();
				var doomed = new List<string>();
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					if (name != null && (name.StartsWith("K8934_S_") || name.StartsWith("K8934_B_") || name.StartsWith("K8934_A0_")))
						doomed.Add(name);
				}
				foreach (string tag in doomed)
					RemoveDrawObject(tag);
				if (ShowVersion && versionDrawn)
				{
					versionDrawn = false;
					DrawVersionLabel();
				}
				ForceRefresh();
				Print(string.Format("[Kat8934] Cleared {0} old signal drawing(s).", doomed.Count));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat8934] Clear error: {0}", ex.Message));
			}
		}

		// Applies the HUD arrow/label toggles to already-drawn signals.
		// Called from the data thread (marshaled via Dispatcher.InvokeAsync from HUD clicks).
		private void ApplyDrawMode(int bits)
		{
			try
			{
				if ((bits & 1) != 0)
				{
					if (cachedShowArrows)
					{
						foreach (KatSignalRecord r in signalRecords)
						{
							// barsAgo measured from the right edge at redraw time puts the object back on the signal candle.
							int barsAgo = CurrentBars[0] - r.Bar;
							if (r.IsBuy)
							{
								Draw.ArrowUp(this, "K8934_B_ARROW_" + r.Bar, false, barsAgo, r.ArrowY, Brushes.White);
								Draw.ArrowUp(this, "K8934_B_ARROW_" + r.Bar + "_2", false, barsAgo, r.ArrowY2, Brushes.White);
							}
							else
							{
								Draw.ArrowDown(this, "K8934_S_ARROW_" + r.Bar, false, barsAgo, r.ArrowY, Brushes.Black);
								Draw.ArrowDown(this, "K8934_S_ARROW_" + r.Bar + "_2", false, barsAgo, r.ArrowY2, Brushes.Black);
							}
						}
					}
					else
					{
						foreach (KatSignalRecord r in signalRecords)
						{
							RemoveDrawObject(r.IsBuy ? "K8934_B_ARROW_" + r.Bar : "K8934_S_ARROW_" + r.Bar);
							RemoveDrawObject(r.IsBuy ? "K8934_B_ARROW_" + r.Bar + "_2" : "K8934_S_ARROW_" + r.Bar + "_2");
						}
					}
				}

				if ((bits & 2) != 0)
				{
					if (cachedShowLabels)
					{
						foreach (KatSignalRecord r in signalRecords)
						{
							int barsAgo = CurrentBars[0] - r.Bar;
							if (r.IsBuy)
								Draw.Text(this, "K8934_B_TEXT_" + r.Bar, "BUY", barsAgo, r.TextY, new SolidColorBrush(BuyTextColor));
							else
								Draw.Text(this, "K8934_S_TEXT_" + r.Bar, "SELL", barsAgo, r.TextY, new SolidColorBrush(SellTextColor));
						}
					}
					else
					{
						foreach (KatSignalRecord r in signalRecords)
							RemoveDrawObject(r.IsBuy ? "K8934_B_TEXT_" + r.Bar : "K8934_S_TEXT_" + r.Bar);
					}
				}
				ForceRefresh();
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat8934] Draw mode error: {0}", ex.Message));
			}
		}

		// --- TradeManager-style HUD helpers (same colors, sizes and structure) ---
		private Button CreateHudButton(string text, Brush bg, RoutedEventHandler handler, double height = 24, double fontSize = 10)
		{
			Button btn = new Button
			{
				Content = text,
				Background = bg,
				Foreground = Brushes.White,
				FontWeight = FontWeights.Normal,
				FontSize = fontSize,
				Margin = new Thickness(0),
				Padding = new Thickness(2),
				Height = height,
				BorderThickness = new Thickness(0)
			};
			if (handler != null)
				btn.Click += handler;
			return btn;
		}

		private Border CreateSectionCard(FrameworkElement child, double bottomMargin)
		{
			return new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(10, 12, 18)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(5),
				Padding = new Thickness(6),
				Margin = new Thickness(0, 0, 0, bottomMargin),
				Child = child
			};
		}

		private Grid CreateTwoColGrid()
		{
			Grid g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			return g;
		}

		private void AddGridRow(Grid grid, string labelText, FrameworkElement input)
		{
			int rowIdx = grid.RowDefinitions.Count;
			grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
			TextBlock label = new TextBlock
			{
				Text = labelText,
				Foreground = Brushes.LightGray,
				VerticalAlignment = VerticalAlignment.Center,
				FontSize = 11
			};
			Grid.SetRow(label, rowIdx);
			Grid.SetColumn(label, 0);
			grid.Children.Add(label);

			input.VerticalAlignment = VerticalAlignment.Center;
			input.HorizontalAlignment = HorizontalAlignment.Stretch;
			input.Height = 22;
			Grid.SetRow(input, rowIdx);
			Grid.SetColumn(input, 1);
			grid.Children.Add(input);
		}

		private Button CreateFilterToggle(string label, Func<bool> getter, Action<bool> setter)
		{
			Button btn = CreateHudButton(getter() ? label + ": ON" : label + ": OFF", getter() ? hudOnBrush : hudOffBrush, null);
			btn.Foreground = getter() ? Brushes.White : Brushes.LightGray;
			btn.Click += (s, e) =>
			{
				setter(!getter());
				bool on = getter();
				btn.Content = on ? label + ": ON" : label + ": OFF";
				btn.Background = on ? hudOnBrush : hudOffBrush;
				btn.Foreground = on ? Brushes.White : Brushes.LightGray;
			};
			return btn;
		}

		private void ShowHudStatus(string message, Brush foreground)
		{
			if (ChartControl == null || ChartControl.Dispatcher == null) return;
			Action update = () =>
			{
				if (hudStatusText == null) return;
				hudStatusText.Text = message;
				hudStatusText.Foreground = foreground ?? Brushes.White;
				if (hudStatusTimer == null)
				{
					hudStatusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
					hudStatusTimer.Tick += (s, e) =>
					{
						if (hudStatusText != null)
						{
							hudStatusText.Text = string.Empty;
							hudStatusText.Foreground = Brushes.White;
						}
						hudStatusTimer.Stop();
					};
				}
				hudStatusTimer.Stop();
				hudStatusTimer.Start();
			};
			if (ChartControl.Dispatcher.CheckAccess()) update();
			else ChartControl.Dispatcher.BeginInvoke(update);
		}

		// --- HUD drag (TradeManager pattern: capture on the border, clamp ≥40px visible, skip interactive controls) ---
		private static DependencyObject GetHudParent(DependencyObject element)
		{
			if (element == null) return null;
			try { DependencyObject p = VisualTreeHelper.GetParent(element); if (p != null) return p; } catch { }
			try { return LogicalTreeHelper.GetParent(element); } catch { return null; }
		}

		private static bool IsInteractiveVisual(DependencyObject src)
		{
			while (src != null)
			{
				if (src is System.Windows.Controls.Primitives.ButtonBase
					|| src is ComboBox
					|| src is System.Windows.Controls.Primitives.Selector
					|| src is TextBox)
					return true;
				src = GetHudParent(src);
			}
			return false;
		}

		private void OnHudPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (isHudDragging || hudBorder == null || hudCanvas == null) return;
			if (IsInteractiveVisual(e.OriginalSource as DependencyObject)) return;
			hudDragStart = e.GetPosition(hudCanvas);
			hudDragStartLeft = Canvas.GetLeft(hudBorder);
			if (double.IsNaN(hudDragStartLeft)) hudDragStartLeft = 10;
			hudDragStartTop = Canvas.GetTop(hudBorder);
			if (double.IsNaN(hudDragStartTop)) hudDragStartTop = 10;
			isHudDragging = Mouse.Capture(hudBorder, CaptureMode.SubTree);
			e.Handled = isHudDragging;
		}

		private void OnHudPreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (!isHudDragging || hudBorder == null || hudCanvas == null) return;
			if (e.LeftButton != MouseButtonState.Pressed)
			{
				StopHudDrag();
				return;
			}
			Point cur = e.GetPosition(hudCanvas);
			double newLeft = hudDragStartLeft + (cur.X - hudDragStart.X);
			double newTop = hudDragStartTop + (cur.Y - hudDragStart.Y);
			const double minVisible = 40; // never drag the panel off-screen
			double panelW = hudBorder.ActualWidth > 0 ? hudBorder.ActualWidth : 240;
			double panelH = hudBorder.ActualHeight > 0 ? hudBorder.ActualHeight : 40;
			newLeft = Math.Min(Math.Max(newLeft, minVisible - panelW), Math.Max(0, hudCanvas.ActualWidth - minVisible));
			newTop = Math.Min(Math.Max(newTop, minVisible - panelH), Math.Max(0, hudCanvas.ActualHeight - minVisible));
			Canvas.SetLeft(hudBorder, newLeft);
			Canvas.SetTop(hudBorder, newTop);
			hasHudDragPosition = true;
			hudDragLeft = newLeft;
			hudDragTop = newTop;
			e.Handled = true;
		}

		private void OnHudPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (!isHudDragging) return;
			StopHudDrag();
			e.Handled = true;
		}

		private void StopHudDrag()
		{
			isHudDragging = false;
			if (Mouse.Captured == hudBorder) Mouse.Capture(null);
		}

		private void OnHudLostMouseCapture(object sender, MouseEventArgs e)
		{
			isHudDragging = false;
		}

		private void AttachHudDragHandlers()
		{
			if (hudBorder == null) return;
			hudBorder.AddHandler(Border.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonDown), true);
			hudBorder.AddHandler(Border.PreviewMouseMoveEvent, new MouseEventHandler(OnHudPreviewMouseMove), true);
			hudBorder.AddHandler(Border.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonUp), true);
			hudBorder.LostMouseCapture += OnHudLostMouseCapture;
		}

		private void DetachHudDragHandlers()
		{
			if (hudBorder == null) return;
			hudBorder.RemoveHandler(Border.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonDown));
			hudBorder.RemoveHandler(Border.PreviewMouseMoveEvent, new MouseEventHandler(OnHudPreviewMouseMove));
			hudBorder.RemoveHandler(Border.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonUp));
			hudBorder.LostMouseCapture -= OnHudLostMouseCapture;
		}

		private void BuildHud()
		{
			// Attach to the outer grid (ChartControl.Parent), never ChartControl itself —
			// ChartControl lays out the price panel and a child would squeeze it (side gaps).
			Grid host = ChartControl != null ? ChartControl.Parent as Grid : null;
			if (hudBorder != null || host == null) return;

			hudCanvas = new Canvas
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				ClipToBounds = false
			};
			System.Windows.Controls.Panel.SetZIndex(hudCanvas, 9999);
			host.Children.Add(hudCanvas);

			hudBorder = new Border
			{
				Tag = "Kat8934Panel",
				Background = new SolidColorBrush(Color.FromArgb(240, 20, 24, 33)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(8),
				Width = 240,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Cursor = Cursors.SizeAll
			};
			hudCanvas.Children.Add(hudBorder);
			Canvas.SetLeft(hudBorder, hasHudDragPosition ? hudDragLeft : 10);
			Canvas.SetTop(hudBorder, hasHudDragPosition ? hudDragTop : 10);
			hudBorder.Loaded += (s, ev) =>
			{
				if (!hasHudDragPosition && hudCanvas != null)
					Canvas.SetTop(hudBorder, Math.Max(0, hudCanvas.ActualHeight - hudBorder.ActualHeight - 10));
			};
			AttachHudDragHandlers();

			var mainPanel = new StackPanel();

			mainPanel.Children.Add(new TextBlock
			{
				Text = string.Format("⚡ KAT 8934 v{0}", VERSION),
				Foreground = new SolidColorBrush(Color.FromRgb(70, 130, 160)),
				FontWeight = FontWeights.Bold,
				FontSize = 12,
				Margin = new Thickness(0, 0, 0, 6),
				HorizontalAlignment = HorizontalAlignment.Left
			});

			hudStatusText = new TextBlock
			{
				Foreground = Brushes.White,
				FontSize = 10,
				Margin = new Thickness(0, 0, 0, 6),
				Height = 32,
				MinHeight = 32,
				MaxHeight = 32,
				TextWrapping = TextWrapping.Wrap,
				Text = string.Empty
			};
			mainPanel.Children.Add(hudStatusText);

			// --- Section 1: Account & ATM ---
			var sec1 = new StackPanel();
			var accGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
			accGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
			accGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var accCombo = new ComboBox { FontSize = 11, Height = 22 };
			if (Account.All != null)
				foreach (Account acc in Account.All)
					accCombo.Items.Add(acc.Name);
			for (int i = 0; i < accCombo.Items.Count; i++)
				if (accCombo.Items[i].ToString().Equals(cachedBotAccountName, StringComparison.OrdinalIgnoreCase))
					accCombo.SelectedIndex = i;
			if (accCombo.SelectedIndex < 0 && accCombo.Items.Count > 0) accCombo.SelectedIndex = 0;
			if (accCombo.SelectedItem != null)
			{
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
			}
			accCombo.SelectionChanged += (s, e) =>
			{
				if (accCombo.SelectedItem == null) return;
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
			};
			AddGridRow(accGrid, "Acc:", accCombo);
			sec1.Children.Add(accGrid);

			var atmCombo = new ComboBox { FontSize = 11, Height = 22, HorizontalAlignment = HorizontalAlignment.Stretch };
			atmCombo.Items.Add("None");
			try
			{
				string atmDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");
				if (Directory.Exists(atmDir))
				{
					var names = new List<string>();
					foreach (string f in Directory.GetFiles(atmDir, "*.xml"))
						names.Add(Path.GetFileNameWithoutExtension(f));
					names.Sort(StringComparer.OrdinalIgnoreCase); // filesystem order is not deterministic
					foreach (string n in names) atmCombo.Items.Add(n);
				}
			}
			catch { }
			for (int i = 0; i < atmCombo.Items.Count; i++)
				if (atmCombo.Items[i].ToString().Equals(cachedBotAtm, StringComparison.OrdinalIgnoreCase))
					atmCombo.SelectedIndex = i;
			if (atmCombo.SelectedIndex < 0) atmCombo.SelectedIndex = 0;
			atmCombo.SelectionChanged += (s, e) =>
			{
				if (atmCombo.SelectedItem == null) return;
				cachedBotAtm = atmCombo.SelectedItem.ToString();
				BotAtmTemplate = cachedBotAtm;
			};
			sec1.Children.Add(atmCombo);
			mainPanel.Children.Add(CreateSectionCard(sec1, 6));

			// --- Section 2: Filters ---
			var sec2 = new StackPanel();
			Grid fRow1 = CreateTwoColGrid();
			Button tA0 = CreateFilterToggle("A0 fan", () => cachedA0, v => cachedA0 = v);
			Grid.SetColumn(tA0, 0);
			fRow1.Children.Add(tA0);
			Button tMtf = CreateFilterToggle("MTF", () => cachedMtf, v => cachedMtf = v);
			Grid.SetColumn(tMtf, 2);
			fRow1.Children.Add(tMtf);
			sec2.Children.Add(fRow1);

			Grid fRow2 = CreateTwoColGrid();
			Button tAdx = CreateFilterToggle("ADX", () => cachedAdx, v => cachedAdx = v);
			Grid.SetColumn(tAdx, 0);
			fRow2.Children.Add(tAdx);
			Button tVol = CreateFilterToggle("Volume", () => cachedVol, v => cachedVol = v);
			Grid.SetColumn(tVol, 2);
			fRow2.Children.Add(tVol);
			sec2.Children.Add(fRow2);

			Button tTime = CreateFilterToggle("Time window", () => cachedTime, v => cachedTime = v);
			sec2.Children.Add(tTime);
			mainPanel.Children.Add(CreateSectionCard(sec2, 6));

			// --- Section 3: Bot & Display ---
			var sec3 = new StackPanel();
			Button btnBot = CreateHudButton("BOT: OFF", hudOffBrush, null, 26, 11);
			btnBot.Foreground = Brushes.LightGray;
			btnBot.Margin = new Thickness(0, 0, 0, 4);
			btnBot.Click += (s, e) =>
			{
				cachedBotOn = !cachedBotOn;
				btnBot.Content = cachedBotOn ? "⚡ BOT: ON" : "BOT: OFF";
				btnBot.Background = cachedBotOn ? hudOnBrush : hudOffBrush;
				btnBot.Foreground = cachedBotOn ? Brushes.White : Brushes.LightGray;
				if (cachedBotOn)
					ShowHudStatus("BOT ON — A1 signals auto-submit stop orders", Brushes.LightGreen);
				else
				{
					ShowHudStatus("BOT OFF — pending entry cancelled", Brushes.OrangeRed);
					Dispatcher.InvokeAsync(() =>
					{
						pendingMigrate = false;
						CancelPendingBotOrder("BOT switched OFF");
					});
				}
			};
			sec3.Children.Add(btnBot);

			Grid dRow = CreateTwoColGrid();
			Button btnArrows = CreateHudButton(cachedShowArrows ? "Arrow: ON" : "Arrow: OFF",
				cachedShowArrows ? hudOnBrush : hudOffBrush, null);
			btnArrows.Foreground = cachedShowArrows ? Brushes.White : Brushes.LightGray;
			btnArrows.Click += (s, e) =>
			{
				cachedShowArrows = !cachedShowArrows;
				ShowArrows = cachedShowArrows;
				btnArrows.Content = cachedShowArrows ? "Arrow: ON" : "Arrow: OFF";
				btnArrows.Background = cachedShowArrows ? hudOnBrush : hudOffBrush;
				btnArrows.Foreground = cachedShowArrows ? Brushes.White : Brushes.LightGray;
				Dispatcher.InvokeAsync(() => ApplyDrawMode(1));
			};
			Grid.SetColumn(btnArrows, 0);
			dRow.Children.Add(btnArrows);

			Button btnLabels = CreateHudButton(cachedShowLabels ? "Text: ON" : "Text: OFF",
				cachedShowLabels ? hudOnBrush : hudOffBrush, null);
			btnLabels.Foreground = cachedShowLabels ? Brushes.White : Brushes.LightGray;
			btnLabels.Click += (s, e) =>
			{
				cachedShowLabels = !cachedShowLabels;
				ShowLabels = cachedShowLabels;
				btnLabels.Content = cachedShowLabels ? "Text: ON" : "Text: OFF";
				btnLabels.Background = cachedShowLabels ? hudOnBrush : hudOffBrush;
				btnLabels.Foreground = cachedShowLabels ? Brushes.White : Brushes.LightGray;
				Dispatcher.InvokeAsync(() => ApplyDrawMode(2));
			};
			Grid.SetColumn(btnLabels, 2);
			dRow.Children.Add(btnLabels);
			sec3.Children.Add(dRow);

			Grid aRow = CreateTwoColGrid();
			Button btnA2 = CreateHudButton("A2…", hudOffBrush, null);
			btnA2.IsEnabled = false;
			Grid.SetColumn(btnA2, 0);
			aRow.Children.Add(btnA2);
			Button btnA3 = CreateHudButton("A3…", hudOffBrush, null);
			btnA3.IsEnabled = false;
			Grid.SetColumn(btnA3, 2);
			aRow.Children.Add(btnA3);
			sec3.Children.Add(aRow);

			Button btnClear = CreateHudButton("Clear", new SolidColorBrush(Color.FromRgb(20, 20, 20)),
				(s, e) => Dispatcher.InvokeAsync(() => ClearOldSignalDrawings()));
			sec3.Children.Add(btnClear);
			mainPanel.Children.Add(CreateSectionCard(sec3, 0));

			hudBorder.Child = mainPanel;
		}

		private void RemoveHud()
		{
			StopHudDrag();
			if (hudStatusTimer != null)
			{
				hudStatusTimer.Stop();
				hudStatusTimer = null;
			}
			DetachHudDragHandlers();
			if (hudBorder != null && hudBorder.Parent is Panel borderHost)
				borderHost.Children.Remove(hudBorder);
			hudBorder = null;
			if (hudCanvas != null && hudCanvas.Parent is Grid host)
				host.Children.Remove(hudCanvas);
			hudCanvas = null;
			hudStatusText = null;
		}
		#endregion

	private static KatTriggerMode ToLogicMode(Kat8934TriggerMode mode)
	{
		return mode == Kat8934TriggerMode.Breakdown ? KatTriggerMode.Breakdown : KatTriggerMode.RetestBounce;
	}

	// Parses the selected ATM template once; re-parses only when the template name changes (HUD or settings).
	private Kat8934AtmData GetAtmData()
	{
		string tpl = cachedBotAtm ?? "";
		if (tpl != atmLevelsName)
		{
			atmLevelsName = tpl;
			atmLevels = HasAtmTemplate(tpl)
				? Kat8934AtmParser.ParseFile(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy", tpl + ".xml"))
				: new Kat8934AtmData();
		}
		return atmLevels;
	}

	private void DrawSignal(bool isBuy, int bar, double high, double low, double c1, double c2, int offsetTicks, int stopTicks, int targetTicks)
	{
		double tick = TickSize;
		double entryPrice;
		double arrowY;

		// A1 dual entry: c1 = U-turn bar extreme, c2 = best later candidate (0 = none yet — fall back to the signal bar).
		double ref1 = c1 != 0 ? c1 : (isBuy ? high : low);
		double ref2 = c2 != 0 ? c2 : ref1;
		entryPrice = Kat8934Logic.EffectiveEntry(isBuy, ref1, ref2, offsetTicks, tick);
		double cand1 = isBuy ? ref1 + offsetTicks * tick : ref1 - offsetTicks * tick;
		double cand2 = isBuy ? ref2 + offsetTicks * tick : ref2 - offsetTicks * tick;
		arrowY = isBuy ? low - ArrowOffsetTicks * tick : high + ArrowOffsetTicks * tick;

		// TradeManager-style levels: SL/TP come from the selected ATM template when it defines them,
		// otherwise from the indicator settings; BE/SL1/SL2 trailing-SL triggers exist only with an ATM.
		Kat8934AtmData atm = GetAtmData();
		int slTicks = atm.StopLoss > 0 ? atm.StopLoss : stopTicks;
		int tpTicks = atm.Target > 0 ? atm.Target : targetTicks;
		double slPrice = isBuy ? entryPrice - slTicks * tick : entryPrice + slTicks * tick;
		double tpPrice = isBuy ? entryPrice + tpTicks * tick : entryPrice - tpTicks * tick;

			Brush entryBrush = new SolidColorBrush(isBuy ? BuyEntryLineColor : SellEntryLineColor);
			Brush slBrush = new SolidColorBrush(SLLineColor);
			Brush tpBrush = new SolidColorBrush(TPLineColor);
			Brush textBrush = new SolidColorBrush(isBuy ? BuyTextColor : SellTextColor);
		int endAgo = -LineLengthBars; // negative barsAgo = bars into the future
		double textY = isBuy ? entryPrice - tick : entryPrice + tick; // buy label below line, sell above

		// A1 candidate lines (C1 = U-turn bar, C2 = best later bar) — faded dotted, only when they differ.
		if (cand1 != cand2)
		{
			string side = isBuy ? "B" : "S";
			Brush faded = new SolidColorBrush(isBuy ? BuyEntryLineColor : SellEntryLineColor) { Opacity = 0.35 };
			Draw.Line(this, "K8934_" + side + "_C1_" + bar, false, 0, cand1, endAgo, cand1, faded, DashStyleHelper.Dot, 1);
			Draw.Line(this, "K8934_" + side + "_C2_" + bar, false, 0, cand2, endAgo, cand2, faded, DashStyleHelper.Dot, 1);
		}

			// barsAgo 0 = the signal candle at draw time.
			if (cachedShowArrows)
			{
				// ponytail: NT8 Draw.Arrow* has no size parameter — two overlapping arrows (1 tick apart) render a visually ~2x marker; upgrade path: custom IDrawingTool.
				double arrowY2 = isBuy ? arrowY + tick : arrowY - tick;
				if (isBuy)
				{
					Draw.ArrowUp(this, "K8934_B_ARROW_" + bar, false, 0, arrowY, Brushes.White);
					Draw.ArrowUp(this, "K8934_B_ARROW_" + bar + "_2", false, 0, arrowY2, Brushes.White);
				}
				else
				{
					Draw.ArrowDown(this, "K8934_S_ARROW_" + bar, false, 0, arrowY, Brushes.Black);
					Draw.ArrowDown(this, "K8934_S_ARROW_" + bar + "_2", false, 0, arrowY2, Brushes.Black);
				}
			}

		if (isBuy)
		{
			Draw.Line(this, "K8934_B_ENTRY_" + bar, false, 0, entryPrice, endAgo, entryPrice, entryBrush, DashStyleHelper.Solid, LineWidth);
			Draw.Line(this, "K8934_B_SL_" + bar, false, 0, slPrice, endAgo, slPrice, slBrush, DashStyleHelper.Dash, LineWidth);
			Draw.Line(this, "K8934_B_TP_" + bar, false, 0, tpPrice, endAgo, tpPrice, tpBrush, DashStyleHelper.Dash, LineWidth);
			if (cachedShowLabels)
				Draw.Text(this, "K8934_B_TEXT_" + bar, "BUY", 0, textY, textBrush);
		}
		else
		{
			Draw.Line(this, "K8934_S_ENTRY_" + bar, false, 0, entryPrice, endAgo, entryPrice, entryBrush, DashStyleHelper.Solid, LineWidth);
			Draw.Line(this, "K8934_S_SL_" + bar, false, 0, slPrice, endAgo, slPrice, slBrush, DashStyleHelper.Dash, LineWidth);
			Draw.Line(this, "K8934_S_TP_" + bar, false, 0, tpPrice, endAgo, tpPrice, tpBrush, DashStyleHelper.Dash, LineWidth);
			if (cachedShowLabels)
				Draw.Text(this, "K8934_S_TEXT_" + bar, "SELL", 0, textY, textBrush);
		}

		// Trailing-SL trigger lines from the ATM template — same style as KatTradeManager
		// (BE DeepSkyBlue dash-dot, SL1 orange dot, SL2 magenta dot, 1 px, profit side of entry).
		string sideTag = isBuy ? "B" : "S";
		int dir = isBuy ? 1 : -1;
		if (atm.BETrigger > 0)
		{
			double bePrice = entryPrice + dir * atm.BETrigger * tick;
			Draw.Line(this, "K8934_" + sideTag + "_BE_" + bar, false, 0, bePrice, endAgo, bePrice, Brushes.DeepSkyBlue, DashStyleHelper.DashDot, 1);
		}
		if (atm.SL1Trigger > 0)
		{
			double sl1Price = entryPrice + dir * atm.SL1Trigger * tick;
			Draw.Line(this, "K8934_" + sideTag + "_SL1_" + bar, false, 0, sl1Price, endAgo, sl1Price, Brushes.Orange, DashStyleHelper.Dot, 1);
		}
		if (atm.SL2Trigger > 0)
		{
			double sl2Price = entryPrice + dir * atm.SL2Trigger * tick;
			Draw.Line(this, "K8934_" + sideTag + "_SL2_" + bar, false, 0, sl2Price, endAgo, sl2Price, Brushes.Magenta, DashStyleHelper.Dot, 1);
		}

			if (signalRecords.Count >= MAX_SIGNAL_RECORDS)
				signalRecords.RemoveAt(0);
			signalRecords.Add(new KatSignalRecord
			{
				Bar = bar,
				IsBuy = isBuy,
				ArrowY = arrowY,
				ArrowY2 = isBuy ? arrowY + tick : arrowY - tick,
				TextY = textY
			});

			PlayAlertSound();
		Print(string.Format("[Kat8934] {0} signal @ bar {1} — entry {2:F5}, SL {3:F5}, TP {4:F5}", isBuy ? "BUY" : "SELL", bar, entryPrice, slPrice, tpPrice));
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
	[TypeConverter(typeof(Kat8934SoundConverter))]
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
	public Kat8934TriggerMode TriggerMode { get; set; }

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
	[TypeConverter(typeof(Kat8934AtmTemplateConverter))]
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
