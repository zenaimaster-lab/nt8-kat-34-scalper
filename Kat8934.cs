/*
 * Kat8934.cs
 * Version: 0.13 (2026-08-01)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy) with entry, SL, TP dash lines.
 * A0 EMA-ribbon fan filter (9..200) with MTF (3m/5m/15m), ADX/volume and time-window gates, alert sound.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
		public const string VERSION = "0.13";
		public const string RELEASE_DATE = "2026-08-01";

		private EMA fastEma;
		private EMA slowEma;
		private bool sellTouched89;
		private bool sellUturned;
		private bool buyTouched89;
		private bool buyUturned;
		private double sellC1;
		private double sellC2;
		private double buyC1;
		private double buyC2;
		private bool versionDrawn;
		private volatile bool cachedShowArrows = true;
		private volatile bool cachedShowLabels;

		// 1. Filters (A0 fan, MTF, market, time)
		private static readonly int[] FanPeriods = { 9, 21, 34, 55, 89, 144, 200 };
		private EMA[][] fanEmas; // [series index][period index] — series 0=primary, 1=3m, 2=5m, 3=15m
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
		private bool pendingMigrate;
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
			// Always added — the ON/OFF toggles gate evaluation, not the series (keeps BarsArray indexes stable).
			AddDataSeries(Data.BarsPeriodType.Minute, 3);
			AddDataSeries(Data.BarsPeriodType.Minute, 5);
			AddDataSeries(Data.BarsPeriodType.Minute, 15);
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
				Print(string.Format("[Kat8934] v{0} ({1}) loaded.", VERSION, RELEASE_DATE));
			cachedShowArrows = ShowArrows;
			cachedShowLabels = ShowLabels;
			cachedBotAtm = BotAtmTemplate ?? "";
			cachedBotAccountName = BotAccountName ?? "";

				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(BuildHud);
			}
			else if (State == State.Terminated)
			{
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
			if (sellAllowed && Kat8934Logic.Update(KatSignalKind.Sell, mode,
				fast < slow, high, low, close, fast, slow,
				ref sellTouched89, ref sellUturned, ref sellC1, ref sellC2) == KatSignalKind.Sell)
			{
				DrawSignal(false, CurrentBar, high, low, sellC1, sellC2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
				TrySubmitBotEntry(false, sellC2);
			}
			if (buyAllowed && Kat8934Logic.Update(KatSignalKind.Buy, mode,
				fast > slow, high, low, close, fast, slow,
				ref buyTouched89, ref buyUturned, ref buyC1, ref buyC2) == KatSignalKind.Buy)
			{
				DrawSignal(true, CurrentBar, high, low, buyC1, buyC2, EntryOffsetTicks, StopDistanceTicks, TargetDistanceTicks);
				TrySubmitBotEntry(true, buyC2);
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
		}
		catch (Exception ex)
		{
			pendingOrder = null;
			Print(string.Format("[Kat8934] BOT submit error: {0}", ex.Message));
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
		if (Use3mFan  && SeriesFanDirection(1) != dir) return false;
		if (Use5mFan  && SeriesFanDirection(2) != dir) return false;
		if (Use15mFan && SeriesFanDirection(3) != dir) return false;
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
		private void DrawVersionLabel()
		{
			versionDrawn = true;
			Draw.TextFixed(this, "K8934_version", string.Format("Kat8934 v{0} ({1})", VERSION, RELEASE_DATE), TextPosition.TopLeft);
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
					if (name != null && (name.StartsWith("K8934_S_") || name.StartsWith("K8934_B_")))
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

		private Button CreateHudButton(string text, Brush bg, RoutedEventHandler handler)
		{
			Button btn = new Button
			{
				Content = text,
				Background = bg,
				Foreground = Brushes.White,
				FontWeight = FontWeights.Normal,
				FontSize = 12,
				Margin = new Thickness(0, 0, 4, 0),
				Padding = new Thickness(2),
				Height = 24,
				BorderThickness = new Thickness(0)
			};
			if (handler != null)
				btn.Click += handler;
			return btn;
		}

		private Button CreateFilterToggle(string label, Brush onBrush, Brush offBrush, Func<bool> getter, Action<bool> setter)
	{
		Button btn = CreateHudButton(getter() ? label + ": ON" : label + ": OFF", getter() ? onBrush : offBrush, null);
		btn.Click += (s, e) =>
		{
			setter(!getter());
			btn.Content = getter() ? label + ": ON" : label + ": OFF";
			btn.Background = getter() ? onBrush : offBrush;
		};
		return btn;
	}

	private void BuildHud()
		{
			// Attach to the outer grid (ChartControl.Parent), never ChartControl itself —
			// ChartControl lays out the price panel and a child would squeeze it (side gaps).
			Grid host = ChartControl != null ? ChartControl.Parent as Grid : null;
			if (hudBorder != null || host == null) return;

		SolidColorBrush onBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204));
		SolidColorBrush offBrush = new SolidColorBrush(Color.FromRgb(45, 50, 65));

		Button btnClear = CreateHudButton("Clear", new SolidColorBrush(Color.FromRgb(20, 20, 20)), (s, e) => Dispatcher.InvokeAsync(() => ClearOldSignalDrawings()));

		Button btnArrows = CreateHudButton(cachedShowArrows ? "Arrow: ON" : "Arrow: OFF",
			cachedShowArrows ? onBrush : offBrush, null);
		btnArrows.Click += (s, e) =>
		{
			cachedShowArrows = !cachedShowArrows;
			ShowArrows = cachedShowArrows;
			btnArrows.Content = cachedShowArrows ? "Arrow: ON" : "Arrow: OFF";
			btnArrows.Background = cachedShowArrows ? onBrush : offBrush;
			Dispatcher.InvokeAsync(() => ApplyDrawMode(1));
		};

		Button btnLabels = CreateHudButton(cachedShowLabels ? "Text: ON" : "Text: OFF",
			cachedShowLabels ? onBrush : offBrush, null);
		btnLabels.Click += (s, e) =>
		{
			cachedShowLabels = !cachedShowLabels;
			ShowLabels = cachedShowLabels;
			btnLabels.Content = cachedShowLabels ? "Text: ON" : "Text: OFF";
			btnLabels.Background = cachedShowLabels ? onBrush : offBrush;
			Dispatcher.InvokeAsync(() => ApplyDrawMode(2));
		};

		var row1 = new StackPanel { Orientation = Orientation.Horizontal };
		row1.Children.Add(btnClear);
		row1.Children.Add(btnArrows);
		row1.Children.Add(btnLabels);

		// Row 2: filter toggles — flip the volatile cached flags; the data thread picks them up on the next bar.
		var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
		row2.Children.Add(CreateFilterToggle("A0", onBrush, offBrush, () => cachedA0, v => cachedA0 = v));
		row2.Children.Add(CreateFilterToggle("MTF", onBrush, offBrush, () => cachedMtf, v => cachedMtf = v));
		row2.Children.Add(CreateFilterToggle("ADX", onBrush, offBrush, () => cachedAdx, v => cachedAdx = v));
		row2.Children.Add(CreateFilterToggle("Vol", onBrush, offBrush, () => cachedVol, v => cachedVol = v));
		row2.Children.Add(CreateFilterToggle("Time", onBrush, offBrush, () => cachedTime, v => cachedTime = v));

		// Row 3: semi-auto bot — OFF by default; trades only while ON. OFF cancels the pending bot entry.
		var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

		Button btnBot = CreateHudButton("BOT: OFF", offBrush, null);
		btnBot.Click += (s, e) =>
		{
			cachedBotOn = !cachedBotOn;
			btnBot.Content = cachedBotOn ? "BOT: ON" : "BOT: OFF";
			btnBot.Background = cachedBotOn ? onBrush : offBrush;
			if (!cachedBotOn)
				Dispatcher.InvokeAsync(() =>
				{
					pendingMigrate = false;
					CancelPendingBotOrder("BOT switched OFF");
				});
		};
		row3.Children.Add(btnBot);

		// ATM template dropdown (sorted; "None" = bare stop order).
		var atmCombo = new ComboBox
		{
			FontSize = 11, Height = 22, Margin = new Thickness(0, 0, 4, 0),
			Background = offBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0)
		};
		atmCombo.Items.Add("None");
		try
		{
			string atmDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");
			if (Directory.Exists(atmDir))
			{
				var names = new List<string>();
				foreach (string f in Directory.GetFiles(atmDir, "*.xml"))
					names.Add(Path.GetFileNameWithoutExtension(f));
				names.Sort(StringComparer.OrdinalIgnoreCase);
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
		row3.Children.Add(atmCombo);

		// Account dropdown.
		var accCombo = new ComboBox
		{
			FontSize = 11, Height = 22, Margin = new Thickness(0, 0, 4, 0),
			Background = offBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0)
		};
		if (Account.All != null)
		{
			foreach (Account acc in Account.All)
				accCombo.Items.Add(acc.Name);
		}
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
		row3.Children.Add(accCombo);

		// Placeholders for future signal entries (A2, A3…).
		Button btnA2 = CreateHudButton("A2…", offBrush, null);
		btnA2.IsEnabled = false;
		Button btnA3 = CreateHudButton("A3…", offBrush, null);
		btnA3.IsEnabled = false;
		row3.Children.Add(btnA2);
		row3.Children.Add(btnA3);

		var panel = new StackPanel { Orientation = Orientation.Vertical };
		panel.Children.Add(row1);
		panel.Children.Add(row2);
		panel.Children.Add(row3);

			hudBorder = new Border
			{
				Child = panel,
				Background = new SolidColorBrush(Color.FromArgb(240, 20, 24, 33)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(8),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Bottom,
				Margin = new Thickness(10, 0, 0, 4)
			};
			host.Children.Add(hudBorder);
		}

		private void RemoveHud()
		{
			if (hudBorder != null)
			{
				if (hudBorder.Parent is Grid host)
					host.Children.Remove(hudBorder);
			}
			hudBorder = null;
		}
		#endregion

		private static KatTriggerMode ToLogicMode(Kat8934TriggerMode mode)
		{
			return mode == Kat8934TriggerMode.Breakdown ? KatTriggerMode.Breakdown : KatTriggerMode.RetestBounce;
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

			double slPrice = isBuy ? entryPrice - stopTicks * tick : entryPrice + stopTicks * tick;
			double tpPrice = isBuy ? entryPrice + targetTicks * tick : entryPrice - targetTicks * tick;

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
		[Display(Name = "Trigger Mode", Order = 4, GroupName = "2. Signal",
			Description = "Retest Bounce: Sell fires when price closes back above the fast EMA after the U-turn close below it (Buy mirrored). Breakdown: fire immediately on the U-turn close.")]
		public Kat8934TriggerMode TriggerMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 5, GroupName = "2. Signal",
			Description = "Sell entry below the signal low / Buy entry above the signal high.")]
		public int EntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 6, GroupName = "2. Signal")]
		public int StopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 7, GroupName = "2. Signal")]
		public int TargetDistanceTicks { get; set; }

		// --- 4. Bot (semi-auto — trades only while the HUD BOT button is ON) ---
	[NinjaScriptProperty]
	[Display(Name = "Bot Enabled", Order = 1, GroupName = "4. Bot",
		Description = "Master switch. The bot still trades only while the HUD BOT button is ON.")]
	public bool BotEnabled { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Order Quantity", Order = 2, GroupName = "4. Bot")]
	public int BotOrderQuantity { get; set; }

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
