/*
 * Kat8934.cs
 * Version: 0.09 (2026-08-01)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy) with entry, SL, TP dash lines.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
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

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class Kat8934 : Indicator
	{
		#region Metadata & State
		public const string VERSION = "0.09";
		public const string RELEASE_DATE = "2026-08-01";

		// 1. Preparation - section reserved in settings (added later). No properties yet.
		private EMA sellFastEma;
		private EMA sellSlowEma;
		private EMA buyFastEma;
		private EMA buySlowEma;
		private bool sellTouched89;
		private bool sellUturned;
		private bool buyTouched89;
		private bool buyUturned;
		private bool versionDrawn;
		private volatile bool cachedShowArrows = true;
		private volatile bool cachedShowLabels;
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
		private bool hudVisible = true;
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

				// 2. Sell Signal defaults
				SellEnabled					= true;
				SellEmaFastPeriod			= 34;
				SellEmaSlowPeriod			= 89;
				SellTriggerMode				= Kat8934TriggerMode.RetestBounce;
				SellEntryOffsetTicks		= 1;
				SellStopDistanceTicks		= 60;
				SellTargetDistanceTicks		= 120;

				// 3. Buy Signal defaults
				BuyEnabled					= true;
				BuyEmaFastPeriod			= 34;
				BuyEmaSlowPeriod			= 89;
				BuyTriggerMode				= Kat8934TriggerMode.RetestBounce;
				BuyEntryOffsetTicks			= 1;
				BuyStopDistanceTicks		= 60;
				BuyTargetDistanceTicks		= 120;

				// 4. Lines & Text defaults
				LineLengthBars				= 7;
				LineWidth					= 2;
				SellEntryLineColor			= Colors.Red;
				BuyEntryLineColor			= Colors.LimeGreen;
				SLLineColor					= Colors.Red;
				TPLineColor					= Colors.Green;
				SellTextColor				= Colors.Red;
				BuyTextColor				= Colors.LimeGreen;
				ShowArrows					= true;
				ShowLabels					= false;
			}
			else if (State == State.DataLoaded)
			{
				sellFastEma = EMA(BarsArray[0], SellEmaFastPeriod);
				sellSlowEma = EMA(BarsArray[0], SellEmaSlowPeriod);
				buyFastEma  = EMA(BarsArray[0], BuyEmaFastPeriod);
				buySlowEma  = EMA(BarsArray[0], BuyEmaSlowPeriod);
				Print(string.Format("[Kat8934] v{0} ({1}) loaded.", VERSION, RELEASE_DATE));
				cachedShowArrows = ShowArrows;
				cachedShowLabels = ShowLabels;

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

			if (SellEnabled && sellFastEma != null && sellSlowEma != null
				&& CurrentBars[0] >= Math.Max(SellEmaFastPeriod, SellEmaSlowPeriod))
			{
				double fast = sellFastEma[0];
				double slow = sellSlowEma[0];
				if (Kat8934Logic.Update(KatSignalKind.Sell, ToLogicMode(SellTriggerMode),
					fast < slow, high, low, close, fast, slow,
					ref sellTouched89, ref sellUturned) == KatSignalKind.Sell)
				{
					DrawSignal(false, CurrentBar, high, low, SellEntryOffsetTicks, SellStopDistanceTicks, SellTargetDistanceTicks);
				}
			}

			if (BuyEnabled && buyFastEma != null && buySlowEma != null
				&& CurrentBars[0] >= Math.Max(BuyEmaFastPeriod, BuyEmaSlowPeriod))
			{
				double fast = buyFastEma[0];
				double slow = buySlowEma[0];
				if (Kat8934Logic.Update(KatSignalKind.Buy, ToLogicMode(BuyTriggerMode),
					fast > slow, high, low, close, fast, slow,
					ref buyTouched89, ref buyUturned) == KatSignalKind.Buy)
				{
					DrawSignal(true, CurrentBar, high, low, BuyEntryOffsetTicks, BuyStopDistanceTicks, BuyTargetDistanceTicks);
				}
			}
		}

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
							if (r.IsBuy)
							{
								Draw.ArrowUp(this, "K8934_B_ARROW_" + r.Bar, false, 0, r.ArrowY, Brushes.White);
								Draw.ArrowUp(this, "K8934_B_ARROW_" + r.Bar + "_2", false, 0, r.ArrowY2, Brushes.White);
							}
							else
							{
								Draw.ArrowDown(this, "K8934_S_ARROW_" + r.Bar, false, 0, r.ArrowY, Brushes.Black);
								Draw.ArrowDown(this, "K8934_S_ARROW_" + r.Bar + "_2", false, 0, r.ArrowY2, Brushes.Black);
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
					int endAgo = -LineLengthBars;
					if (cachedShowLabels)
					{
						foreach (KatSignalRecord r in signalRecords)
						{
							if (r.IsBuy)
								Draw.Text(this, "K8934_B_TEXT_" + r.Bar, "BUY", endAgo, r.TextY, new SolidColorBrush(BuyTextColor));
							else
								Draw.Text(this, "K8934_S_TEXT_" + r.Bar, "SELL", endAgo, r.TextY, new SolidColorBrush(SellTextColor));
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

			Button btnToggle = CreateHudButton("Hide", offBrush, null);
			btnToggle.Click += (s, e) =>
			{
				hudVisible = !hudVisible;
				btnToggle.Content = hudVisible ? "Hide" : "Show";
				hudBorder.Visibility = hudVisible ? Visibility.Visible : Visibility.Collapsed;
			};

			var panel = new StackPanel { Orientation = Orientation.Horizontal };
			panel.Children.Add(btnClear);
			panel.Children.Add(btnArrows);
			panel.Children.Add(btnLabels);
			panel.Children.Add(btnToggle);

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

		private void DrawSignal(bool isBuy, int bar, double high, double low, int offsetTicks, int stopTicks, int targetTicks)
		{
			double tick = TickSize;
			double entryPrice;
			double arrowY;

			if (isBuy)
			{
				entryPrice = high + offsetTicks * tick; // buy stop above signal high
				arrowY = low - tick;
			}
			else
			{
				entryPrice = low - offsetTicks * tick; // sell stop below signal low
				arrowY = high + tick;
			}

			double slPrice = isBuy ? entryPrice - stopTicks * tick : entryPrice + stopTicks * tick;
			double tpPrice = isBuy ? entryPrice + targetTicks * tick : entryPrice - targetTicks * tick;

			Brush entryBrush = new SolidColorBrush(isBuy ? BuyEntryLineColor : SellEntryLineColor);
			Brush slBrush = new SolidColorBrush(SLLineColor);
			Brush tpBrush = new SolidColorBrush(TPLineColor);
			Brush textBrush = new SolidColorBrush(isBuy ? BuyTextColor : SellTextColor);
			int endAgo = -LineLengthBars; // negative barsAgo = bars into the future
			double textY = isBuy ? entryPrice - tick : entryPrice + tick; // buy label below line, sell above

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
					Draw.Text(this, "K8934_B_TEXT_" + bar, "BUY", endAgo, textY, textBrush);
			}
			else
			{
				Draw.Line(this, "K8934_S_ENTRY_" + bar, false, 0, entryPrice, endAgo, entryPrice, entryBrush, DashStyleHelper.Solid, LineWidth);
				Draw.Line(this, "K8934_S_SL_" + bar, false, 0, slPrice, endAgo, slPrice, slBrush, DashStyleHelper.Dash, LineWidth);
				Draw.Line(this, "K8934_S_TP_" + bar, false, 0, tpPrice, endAgo, tpPrice, tpBrush, DashStyleHelper.Dash, LineWidth);
				if (cachedShowLabels)
					Draw.Text(this, "K8934_S_TEXT_" + bar, "SELL", endAgo, textY, textBrush);
			}

			if (signalRecords.Count >= MAX_SIGNAL_RECORDS)
				signalRecords.RemoveAt(0);
			signalRecords.Add(new KatSignalRecord { Bar = bar, IsBuy = isBuy, ArrowY = arrowY, ArrowY2 = isBuy ? arrowY + tick : arrowY - tick, TextY = textY });

			Print(string.Format("[Kat8934] {0} signal @ bar {1} — entry {2:F5}, SL {3:F5}, TP {4:F5}", isBuy ? "BUY" : "SELL", bar, entryPrice, slPrice, tpPrice));
		}
		#endregion

		#region NinjaScript Properties
		// 1. Preparation - reserved settings group, added later.

		[NinjaScriptProperty]
		[Display(Name = "Show Version Label", Order = 0, GroupName = "Parameters")]
		public bool ShowVersion { get; set; }

		// --- 2. Sell Signal ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "2. Sell Signal")]
		public bool SellEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 2, GroupName = "2. Sell Signal")]
		public int SellEmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 3, GroupName = "2. Sell Signal")]
		public int SellEmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Mode", Order = 4, GroupName = "2. Sell Signal",
			Description = "Retest Bounce: fire when price closes back above the fast EMA after the U-turn close below it. Breakdown: fire immediately on the U-turn close below the fast EMA.")]
		public Kat8934TriggerMode SellTriggerMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks below signal low)", Order = 5, GroupName = "2. Sell Signal")]
		public int SellEntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 6, GroupName = "2. Sell Signal")]
		public int SellStopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 7, GroupName = "2. Sell Signal")]
		public int SellTargetDistanceTicks { get; set; }

		// --- 3. Buy Signal ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "3. Buy Signal")]
		public bool BuyEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 2, GroupName = "3. Buy Signal")]
		public int BuyEmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 3, GroupName = "3. Buy Signal")]
		public int BuyEmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Mode", Order = 4, GroupName = "3. Buy Signal",
			Description = "Retest Bounce: fire when price closes back below the fast EMA after the U-turn close above it. Breakdown: fire immediately on the U-turn close above the fast EMA.")]
		public Kat8934TriggerMode BuyTriggerMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks above signal high)", Order = 5, GroupName = "3. Buy Signal")]
		public int BuyEntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 6, GroupName = "3. Buy Signal")]
		public int BuyStopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 7, GroupName = "3. Buy Signal")]
		public int BuyTargetDistanceTicks { get; set; }

		// --- 4. Lines & Text ---
		[NinjaScriptProperty]
		[Display(Name = "Line Length (bars)", Order = 1, GroupName = "4. Lines & Text",
			Description = "Entry, SL and TP lines extend this many bars forward from the signal candle.")]
		public int LineLengthBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width (px)", Order = 2, GroupName = "4. Lines & Text")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Sell Entry Line Color", Order = 3, GroupName = "4. Lines & Text",
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
		[Display(Name = "Buy Entry Line Color", Order = 4, GroupName = "4. Lines & Text",
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
		[Display(Name = "SL Line Color", Order = 5, GroupName = "4. Lines & Text")]
		[XmlIgnore]
		public Color SLLineColor { get; set; }

		[Browsable(false)]
		public string SLLineColorSerializable
		{
			get { return SLLineColor.ToString(); }
			set { SLLineColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "TP Line Color", Order = 6, GroupName = "4. Lines & Text")]
		[XmlIgnore]
		public Color TPLineColor { get; set; }

		[Browsable(false)]
		public string TPLineColorSerializable
		{
			get { return TPLineColor.ToString(); }
			set { TPLineColor = ParseColor(value, Colors.Green); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Sell Text Color", Order = 7, GroupName = "4. Lines & Text",
			Description = "SELL label and arrow color.")]
		[XmlIgnore]
		public Color SellTextColor { get; set; }

		[Browsable(false)]
		public string SellTextColorSerializable
		{
			get { return SellTextColor.ToString(); }
			set { SellTextColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Buy Text Color", Order = 8, GroupName = "4. Lines & Text",
			Description = "BUY label and arrow color.")]
		[XmlIgnore]
		public Color BuyTextColor { get; set; }

		[Browsable(false)]
		public string BuyTextColorSerializable
		{
			get { return BuyTextColor.ToString(); }
			set { BuyTextColor = ParseColor(value, Colors.LimeGreen); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Show Arrows", Order = 9, GroupName = "4. Lines & Text",
			Description = "Draw the up/down arrow on the signal candle.")]
		public bool ShowArrows { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Buy/Sell Labels", Order = 10, GroupName = "4. Lines & Text",
			Description = "Draw the BUY/SELL text next to the entry line (default off).")]
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
