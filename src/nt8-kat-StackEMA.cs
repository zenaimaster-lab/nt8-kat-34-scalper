/*
 * nt8-kat-StackEMA.cs - standalone five-pack Stack EMA indicator.
 * Version: 0.89 (2026-08-06)
 *
 * Each pack reads its own closed secondary bar. Positive means price is above
 * EMA 8/21/34/55/89; Negative means price is below all five; otherwise Neutral.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using System.Threading;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using KatStackEMA;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class StackEMA : Indicator
	{
		public const string VERSION = "0.89";
		public const string RELEASE_DATE = "2026-08-06";

		private readonly int[] directions = new int[5];
		private EMA[] ema8;
		private EMA[] ema21;
		private EMA[] ema34;
		private EMA[] ema55;
		private EMA[] ema89;
		private readonly TextBlock[] hudRows = new TextBlock[5];
		private Canvas hudCanvas;
		private Border hudBorder;
		private int hudUpdatePending;
		private DispatcherTimer hudWatchdog;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "nt8-kat-StackEMA: five configurable timeframe EMA stacks.";
				Name = "KAT-StackEMA";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;

				EMA89 = 89;
				EMA55 = 55;
				EMA34 = 34;
				EMA21 = 21;
				EMA8 = 8;
				Stack1Timeframe = StackEmaTimeframe.S30;
				Stack2Timeframe = StackEmaTimeframe.M1;
				Stack3Timeframe = StackEmaTimeframe.M3;
				Stack4Timeframe = StackEmaTimeframe.M5;
				Stack5Timeframe = StackEmaTimeframe.M15;
				Stack1Enabled = true;
				Stack2Enabled = true;
				Stack3Enabled = true;
				Stack4Enabled = true;
				Stack5Enabled = true;
				StackedPositive = new SolidColorBrush(Color.FromArgb(128, 0, 128, 0));
				StackedNegative = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
				NeutralColor = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128));
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Second, (int)Stack1Timeframe);
				AddDataSeries(Data.BarsPeriodType.Second, (int)Stack2Timeframe);
				AddDataSeries(Data.BarsPeriodType.Second, (int)Stack3Timeframe);
				AddDataSeries(Data.BarsPeriodType.Second, (int)Stack4Timeframe);
				AddDataSeries(Data.BarsPeriodType.Second, (int)Stack5Timeframe);
			}
			else if (State == State.DataLoaded)
			{
				ema8 = new EMA[5];
				ema21 = new EMA[5];
				ema34 = new EMA[5];
				ema55 = new EMA[5];
				ema89 = new EMA[5];
				for (int i = 0; i < 5; i++)
				{
					int series = i + 1;
					ema8[i] = EMA(BarsArray[series], EMA8);
					ema21[i] = EMA(BarsArray[series], EMA21);
					ema34[i] = EMA(BarsArray[series], EMA34);
					ema55[i] = EMA(BarsArray[series], EMA55);
					ema89[i] = EMA(BarsArray[series], EMA89);
				}
				if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() =>
				{
					BuildHud();
					StartHudWatchdog();
				});
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(RemoveHud);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBars == null || CurrentBars.Length < 6 || CurrentBars[0] < 1) return;
			for (int i = 0; i < 5; i++) directions[i] = DirectionAt(i, 0);
			if (hudCanvas != null && Interlocked.Exchange(ref hudUpdatePending, 1) == 0)
			{
				int[] snapshot = (int[])directions.Clone();
				ChartControl.Dispatcher.InvokeAsync(() =>
				{
					Interlocked.Exchange(ref hudUpdatePending, 0);
					UpdateHud(snapshot);
				});
			}
		}

		private int DirectionAt(int pack, int barsAgo)
		{
			int series = pack + 1;
			int warmup = Math.Max(EMA8, Math.Max(EMA21, Math.Max(EMA34, Math.Max(EMA55, EMA89))));
			if (CurrentBars[series] < warmup) return 0;
			DateTime cutoff = StackEmaLogic.ClosedBarCutoff(Times[0][barsAgo], SeriesPeriodSeconds(0), SeriesPeriodSeconds(series));
			int targetAgo = StackEmaLogic.BarsAgoAtOrBefore(i => Times[series][i], CurrentBars[series], cutoff);
			if (targetAgo < 0 || !StackEmaLogic.HasWarmup(CurrentBars[series], targetAgo, warmup)) return 0;
			return StackEmaLogic.Direction(Closes[series][targetAgo], ema8[pack][targetAgo], ema21[pack][targetAgo], ema34[pack][targetAgo], ema55[pack][targetAgo], ema89[pack][targetAgo]);
		}

		private double SeriesPeriodSeconds(int series)
		{
			var period = BarsArray[series].BarsPeriod;
			if (period.BarsPeriodType == Data.BarsPeriodType.Second) return Math.Max(1, period.Value);
			if (period.BarsPeriodType == Data.BarsPeriodType.Minute) return Math.Max(1, period.Value) * 60.0;
			return 0;
		}

		private bool IsEnabled(int pack)
		{
			switch (pack)
			{
				case 0: return Stack1Enabled;
				case 1: return Stack2Enabled;
				case 2: return Stack3Enabled;
				case 3: return Stack4Enabled;
				default: return Stack5Enabled;
			}
		}

		private StackEmaTimeframe TimeframeAt(int pack)
		{
			switch (pack)
			{
				case 0: return Stack1Timeframe;
				case 1: return Stack2Timeframe;
				case 2: return Stack3Timeframe;
				case 3: return Stack4Timeframe;
				default: return Stack5Timeframe;
			}
		}

		private Brush DirectionBrush(int direction)
		{
			Brush selected = direction > 0 ? StackedPositive : direction < 0 ? StackedNegative : NeutralColor;
			SolidColorBrush solid = selected as SolidColorBrush;
			if (solid == null) return selected;
			Color color = solid.Color;
			return new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B));
		}

		private void StartHudWatchdog()
		{
			if (hudWatchdog != null || ChartControl == null) return;
			hudWatchdog = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (s, e) =>
			{
				Grid host = ChartControl.Parent as Grid;
				bool attached = host != null
					&& ReferenceEquals(hudCanvas != null ? hudCanvas.Parent : null, host)
					&& ReferenceEquals(hudBorder != null ? hudBorder.Parent : null, hudCanvas);
				if (!attached && host != null)
				{
					RemoveHud();
					BuildHud();
				}
			}, ChartControl.Dispatcher);
			hudWatchdog.Start();
		}

		private void BuildHud()
		{
			Grid host = ChartControl != null ? ChartControl.Parent as Grid : null;
			if (hudBorder != null || host == null) return;
			hudCanvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ClipToBounds = false };
			System.Windows.Controls.Panel.SetZIndex(hudCanvas, 9999);
			host.Children.Add(hudCanvas);
			hudBorder = new Border
			{
				Tag = "nt8-kat-StackEMA",
				Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 33)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				Padding = new Thickness(6),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top
			};
			hudCanvas.Children.Add(hudBorder);
			Canvas.SetLeft(hudBorder, 10);
			Canvas.SetTop(hudBorder, 10);
			var rows = new StackPanel();
			for (int i = 0; i < 5; i++)
			{
				if (!IsEnabled(i)) continue;
				hudRows[i] = new TextBlock
				{
					Text = "Stack EMA " + StackEmaLogic.TimeframeLabel(TimeframeAt(i)),
					Foreground = Brushes.White,
					FontSize = 11,
					Padding = new Thickness(5, 3, 5, 3),
					Margin = new Thickness(0, 0, 0, 2)
				};
				rows.Children.Add(hudRows[i]);
			}
			hudBorder.Child = rows;
			UpdateHud();
		}

		private void UpdateHud(int[] snapshot = null)
		{
			if (snapshot == null) snapshot = directions;
			for (int i = 0; i < 5; i++)
				if (hudRows[i] != null)
				{
					hudRows[i].Background = DirectionBrush(snapshot[i]);
					hudRows[i].Text = "Stack EMA " + StackEmaLogic.TimeframeLabel(TimeframeAt(i)) + ": " + (snapshot[i] > 0 ? "Positive" : snapshot[i] < 0 ? "Negative" : "Neutral");
				}
		}

		private void RemoveHud()
		{
			if (hudWatchdog != null)
			{
				hudWatchdog.Stop();
				hudWatchdog = null;
			}
			if (hudBorder != null && hudBorder.Parent is Panel borderHost) borderHost.Children.Remove(hudBorder);
			hudBorder = null;
			if (hudCanvas != null && hudCanvas.Parent is Grid host) host.Children.Remove(hudCanvas);
			hudCanvas = null;
			for (int i = 0; i < hudRows.Length; i++) hudRows[i] = null;
		}

		#region NinjaScript Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA8", Order = 5, GroupName = "Parameters")]
		public int EMA8 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA21", Order = 4, GroupName = "Parameters")]
		public int EMA21 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA34", Order = 3, GroupName = "Parameters")]
		public int EMA34 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA55", Order = 2, GroupName = "Parameters")]
		public int EMA55 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA89", Order = 1, GroupName = "Parameters")]
		public int EMA89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 1 Timeframe", Order = 1, GroupName = "Stack Packs")]
		public StackEmaTimeframe Stack1Timeframe { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 1 Visible", Order = 2, GroupName = "Stack Packs")]
		public bool Stack1Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 2 Timeframe", Order = 3, GroupName = "Stack Packs")]
		public StackEmaTimeframe Stack2Timeframe { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 2 Visible", Order = 4, GroupName = "Stack Packs")]
		public bool Stack2Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 3 Timeframe", Order = 5, GroupName = "Stack Packs")]
		public StackEmaTimeframe Stack3Timeframe { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 3 Visible", Order = 6, GroupName = "Stack Packs")]
		public bool Stack3Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 4 Timeframe", Order = 7, GroupName = "Stack Packs")]
		public StackEmaTimeframe Stack4Timeframe { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 4 Visible", Order = 8, GroupName = "Stack Packs")]
		public bool Stack4Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 5 Timeframe", Order = 9, GroupName = "Stack Packs")]
		public StackEmaTimeframe Stack5Timeframe { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stack 5 Visible", Order = 10, GroupName = "Stack Packs")]
		public bool Stack5Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackedPositive", Order = 11, GroupName = "Parameters")]
		[XmlIgnore]
		public Brush StackedPositive { get; set; }

		[Browsable(false)]
		public string StackedPositiveSerializable
		{
			get { return Serialize.BrushToString(StackedPositive); }
			set { StackedPositive = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "StackedNegative", Order = 12, GroupName = "Parameters")]
		[XmlIgnore]
		public Brush StackedNegative { get; set; }

		[Browsable(false)]
		public string StackedNegativeSerializable
		{
			get { return Serialize.BrushToString(StackedNegative); }
			set { StackedNegative = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "NeutralColor", Order = 13, GroupName = "Parameters")]
		[XmlIgnore]
		public Brush NeutralColor { get; set; }

		[Browsable(false)]
		public string NeutralColorSerializable
		{
			get { return Serialize.BrushToString(NeutralColor); }
			set { NeutralColor = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}
