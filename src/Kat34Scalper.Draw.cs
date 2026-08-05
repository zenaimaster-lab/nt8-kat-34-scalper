/*
 * Kat34Scalper.Draw.cs — Draw module (partial class Kat34Scalper).
 * Everything visual: signal drawings (entry/SL/TP + ATM BE/SL1/SL2 trigger lines,
 * arrows, labels), the version/timeframe label, alert sounds, and the HUD panel.
 * HUD sections are titled by the module they control: SIGNAL / FILTER / BOT / DRAW.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		#region Signal Drawings (shell stubs — drawings owned by independent signal indicators)
		// ponytail: signal drawings moved to KatA1/KatA2/KatB1/KatB2; shell keeps minimal clear helper.

		private void ClearOldSignalDrawings()
		{
			Print("[Kat34Scalper] Clear: signal drawings owned by KatA*/KatB* — remove those indicators to clear.");
			ShowHudStatus("Signals own drawings — remove KatA*/KatB* to clear", Brushes.Orange);
		}
		#endregion

		#region HUD Panel (sections titled by module: SIGNAL / FILTER / BOT / DRAW)
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
		private readonly SolidColorBrush hudBotOnBrush = new SolidColorBrush(Color.FromRgb(15, 60, 130)); // Dark blue for BOT Signals
		private readonly SolidColorBrush hudOffBrush = new SolidColorBrush(Color.FromRgb(45, 50, 65));

		// ATM quick-set buttons (TradeManager pattern: amber ON when its ATM is the current selection)
		private ComboBox atmComboBox;
		private Button[] atmSetButtons;
		private readonly SolidColorBrush atmSetOffBg = new SolidColorBrush(Color.FromRgb(45, 50, 65));
		private readonly SolidColorBrush atmSetOnBg = new SolidColorBrush(Color.FromRgb(180, 90, 20));

		private string GetAtmSetTemplate(int idx)
		{
			switch (idx)
			{
				case 0: return AtmSet1Atm;
				case 1: return AtmSet2Atm;
				case 2: return AtmSet3Atm;
				case 3: return AtmSet4Atm;
				case 4: return AtmSet5Atm;
				default: return AtmSet6Atm;
			}
		}

		private string GetAtmSetName(int idx)
		{
			switch (idx)
			{
				case 0: return AtmSet1Name;
				case 1: return AtmSet2Name;
				case 2: return AtmSet3Name;
				case 3: return AtmSet4Name;
				case 4: return AtmSet5Name;
				default: return AtmSet6Name;
			}
		}

		// Quick-set click: select the assigned ATM immediately (same as picking it from the dropdown).
		private void ApplyAtmSetSelection(int idx)
		{
			string tpl = GetAtmSetTemplate(idx);
			if (string.IsNullOrEmpty(tpl))
			{
				ShowHudStatus(string.Format("Set {0}: no ATM assigned (Indicator Settings)", GetAtmSetName(idx)), Brushes.OrangeRed);
				return;
			}
			if (atmComboBox != null)
			{
				bool found = false;
				for (int i = 0; i < atmComboBox.Items.Count; i++)
				{
					if (atmComboBox.Items[i].ToString().Equals(tpl, StringComparison.OrdinalIgnoreCase))
					{
						atmComboBox.SelectedIndex = i; // dropdown shows it; SelectionChanged sets cachedBotAtm + BotAtmTemplate
						found = true;
						break;
					}
				}
				if (!found)
				{
					ShowHudStatus(string.Format("Set {0}: ATM '{1}' not found on disk", GetAtmSetName(idx), tpl), Brushes.OrangeRed);
					return;
				}
			}
			UpdateAtmSetButtons();
		}

		// Exactly one set button is ON: the one whose assigned ATM equals the current selection.
		// "None" turns every button OFF.
		private void UpdateAtmSetButtons()
		{
			if (atmSetButtons == null) return;
			for (int i = 0; i < atmSetButtons.Length; i++)
			{
				if (atmSetButtons[i] == null) continue;
				string tpl = GetAtmSetTemplate(i);
				bool on = !string.IsNullOrEmpty(cachedBotAtm)
					&& !cachedBotAtm.Equals("None", StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrEmpty(tpl)
					&& tpl.Equals(cachedBotAtm, StringComparison.OrdinalIgnoreCase);
				atmSetButtons[i].Background = on ? atmSetOnBg : atmSetOffBg;
				atmSetButtons[i].Foreground = on ? Brushes.White : Brushes.LightGray;
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

		// Small caps title naming the module the section below controls.
		private TextBlock CreateModuleTitle(string text)
		{
			return new TextBlock
			{
				Text = text,
				Foreground = new SolidColorBrush(Color.FromRgb(110, 120, 145)),
				FontWeight = FontWeights.Bold,
				FontSize = 10,
				Margin = new Thickness(2, 0, 0, 3)
			};
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
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
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

		private Button CreateFilterToggle(string label, Func<bool> getter, Action<bool> setter, double height = 24, double fontSize = 10, Brush activeBrush = null)
		{
			Brush onBrush = activeBrush ?? hudOnBrush;
			Button btn = CreateHudButton(label, getter() ? onBrush : hudOffBrush, null, height, fontSize);
			btn.Foreground = getter() ? Brushes.White : Brushes.LightGray;
			btn.Click += (s, e) =>
			{
				setter(!getter());
				bool on = getter();
				btn.Content = label;
				btn.Background = on ? onBrush : hudOffBrush;
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
					|| src is TextBox
					|| src is ComboBox
					|| src is System.Windows.Controls.Primitives.Selector
					|| src is System.Windows.Controls.Primitives.Thumb)
					return true;
				src = GetHudParent(src);
			}
			return false;
		}

		private bool IsHudDragSource(DependencyObject source)
		{
			if (source == null || hudBorder == null) return false;
			DependencyObject current = source;
			while (current != null)
			{
				if (ReferenceEquals(current, hudBorder))
					return !IsInteractiveVisual(source);
				DependencyObject parent = GetHudParent(current);
				if (ReferenceEquals(parent, current)) break;
				current = parent;
			}
			return false;
		}

		private void OnHudPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (isHudDragging || hudBorder == null || hudCanvas == null) return;
			if (IsInteractiveVisual(e.OriginalSource as DependencyObject)) return;
			hudDragStart = e.GetPosition(hudCanvas);
			hudDragStartLeft = Canvas.GetLeft(hudBorder);
			hudDragStartTop = Canvas.GetTop(hudBorder);
			if (double.IsNaN(hudDragStartLeft)) hudDragStartLeft = 10;
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
				Tag = "Kat34ScalperPanel",
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
				Text = string.Format("⚡ KAT 34-ScalperBot v{0}", VERSION),
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
				Height = 16,
				MinHeight = 16,
				MaxHeight = 16,
				TextTrimming = TextTrimming.CharacterEllipsis,
				TextWrapping = TextWrapping.NoWrap,
				Text = string.Empty
			};
			mainPanel.Children.Add(hudStatusText);

			// --- BOT module: account, ATM template, BOT on/off ---
			var secBot = new StackPanel();

			var accCombo = new ComboBox { FontSize = 11, Height = 22, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
			if (Account.All != null)
				foreach (Account acc in Account.All)
					accCombo.Items.Add(acc.Name);
			for (int i = 0; i < accCombo.Items.Count; i++)
				if (accCombo.Items[i].ToString().Equals(cachedBotAccountName, StringComparison.OrdinalIgnoreCase))
					accCombo.SelectedIndex = i;
			if (accCombo.SelectedIndex < 0 && accCombo.Items.Count > 0) accCombo.SelectedIndex = 0;
			// Default to SIM101 if present (per user rule)
			int simIdx = -1;
			for (int i = 0; i < accCombo.Items.Count; i++)
				if (accCombo.Items[i].ToString().Equals("SIM101", StringComparison.OrdinalIgnoreCase)) { simIdx = i; break; }
			if (simIdx >= 0) accCombo.SelectedIndex = simIdx;
			else if (accCombo.SelectedIndex < 0 && accCombo.Items.Count > 0) accCombo.SelectedIndex = 0;
			if (accCombo.SelectedItem != null)
			{
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
				SyncChartTraderAccount(cachedBotAccountName);
			}
			accCombo.SelectionChanged += (s, e) =>
			{
				if (accCombo.SelectedItem == null) return;
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
				// NT8 only renders chart orders for the account selected in Chart Trader — mirror the pick there.
				SyncChartTraderAccount(cachedBotAccountName);
			};
			secBot.Children.Add(accCombo);

			atmComboBox = new ComboBox { FontSize = 11, Height = 22, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
			atmComboBox.Items.Add("None");
			try
			{
				string atmDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");
				if (Directory.Exists(atmDir))
				{
					var names = new List<string>();
					foreach (string f in Directory.GetFiles(atmDir, "*.xml"))
						names.Add(Path.GetFileNameWithoutExtension(f));
					names.Sort(StringComparer.OrdinalIgnoreCase); // filesystem order is not deterministic
					foreach (string n in names) atmComboBox.Items.Add(n);
				}
			}
			catch { }
			for (int i = 0; i < atmComboBox.Items.Count; i++)
				if (atmComboBox.Items[i].ToString().Equals(cachedBotAtm, StringComparison.OrdinalIgnoreCase))
					atmComboBox.SelectedIndex = i;
			// Force default mnq 1ct template display if present (per user rule)
			const string mnq1ct = "mnq. 1ct. 15-be20-35move15-50triggertrail5step1";
			int mnqIdx = -1;
			for (int i = 0; i < atmComboBox.Items.Count; i++)
				if (atmComboBox.Items[i].ToString().Equals(mnq1ct, StringComparison.OrdinalIgnoreCase)) { mnqIdx = i; break; }
			if (mnqIdx >= 0) atmComboBox.SelectedIndex = mnqIdx;
			else if (atmComboBox.SelectedIndex < 0) atmComboBox.SelectedIndex = 0;
			if (atmComboBox.SelectedItem != null)
			{
				cachedBotAtm = atmComboBox.SelectedItem.ToString();
				BotAtmTemplate = cachedBotAtm;
			}
			atmComboBox.SelectionChanged += (s, e) =>
			{
				if (atmComboBox.SelectedItem == null) return;
				cachedBotAtm = atmComboBox.SelectedItem.ToString();
				BotAtmTemplate = cachedBotAtm;
				UpdateAtmSetButtons();
			};
			secBot.Children.Add(atmComboBox);

			// ATM quick-set row: 6 equal buttons; click selects the ATM assigned in settings (TradeManager style)
			atmSetButtons = new Button[6];
			Grid atmSetGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
			for (int col = 0; col < 6; col++)
			{
				atmSetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				if (col < 5)
					atmSetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
			}
			for (int setIdx = 0; setIdx < 6; setIdx++)
			{
				int capturedIdx = setIdx;
				Button setBtn = CreateHudButton(GetAtmSetName(setIdx), atmSetOffBg, null, 22, 10);
				setBtn.Foreground = Brushes.LightGray;
				setBtn.Click += (s, ev) => ApplyAtmSetSelection(capturedIdx);
				Grid.SetColumn(setBtn, setIdx * 2);
				atmSetButtons[setIdx] = setBtn;
				atmSetGrid.Children.Add(setBtn);
			}
			secBot.Children.Add(atmSetGrid);
			UpdateAtmSetButtons();

			Button btnBot = CreateHudButton(cachedBotOn ? "⚡ BOT: ON" : "BOT: OFF", cachedBotOn ? hudOnBrush : hudOffBrush, null, 26, 11);
			btnBot.Foreground = cachedBotOn ? Brushes.White : Brushes.LightGray;
			btnBot.Margin = new Thickness(0);
			btnBot.Click += (s, e) =>
			{
				cachedBotOn = !cachedBotOn;
				BotEnabled = cachedBotOn;
				btnBot.Content = cachedBotOn ? "⚡ BOT: ON" : "BOT: OFF";
				btnBot.Background = cachedBotOn ? hudOnBrush : hudOffBrush;
				btnBot.Foreground = cachedBotOn ? Brushes.White : Brushes.LightGray;
				if (cachedBotOn)
					ShowHudStatus("BOT ON — every signal switched ON auto-submits entries", Brushes.LightGreen);
				else
				{
					ShowHudStatus("BOT OFF — pending entry cancelled", Brushes.OrangeRed);
					TriggerCustomEvent(o =>
					{
						pendingMigrate = false;
						CancelPendingBotOrder("BOT switched OFF");
					}, null);
				}
			};
			secBot.Children.Add(btnBot);

			// --- Market Orders (SELL market / BUY market) & Position Management (BE / Revert) ---
			Grid mktBtnGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
			mktBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			mktBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
			mktBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			SolidColorBrush buyMktBg  = new SolidColorBrush(Color.FromRgb(12, 48, 25)); // Deep dark green (#0C3019)
			SolidColorBrush sellMktBg = new SolidColorBrush(Color.FromRgb(55, 15, 18)); // Deep dark red (#370F12)

			Button btnSellMkt = CreateHudButton("SELL market", sellMktBg, null, 48, 12);
			btnSellMkt.Click += (s, ev) => TriggerCustomEvent(o => { PlaceMarketOrder(OrderAction.Sell); }, null);
			Grid.SetColumn(btnSellMkt, 0);
			mktBtnGrid.Children.Add(btnSellMkt);

			Button btnBuyMkt = CreateHudButton("BUY market", buyMktBg, null, 48, 12);
			btnBuyMkt.Click += (s, ev) => TriggerCustomEvent(o => { PlaceMarketOrder(OrderAction.Buy); }, null);
			Grid.SetColumn(btnBuyMkt, 2);
			mktBtnGrid.Children.Add(btnBuyMkt);

			secBot.Children.Add(mktBtnGrid);

			Grid beRevertGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
			beRevertGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			beRevertGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
			beRevertGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			SolidColorBrush beBg     = new SolidColorBrush(Color.FromRgb(14, 48, 62)); // Deep dark slate teal (#0E303E)
			SolidColorBrush revertBg = new SolidColorBrush(Color.FromRgb(75, 42, 10)); // Deep dark amber (#4B2A0A)

			Button btnBE = CreateHudButton("BE", beBg, null, 33, 12);
			btnBE.Click += (s, ev) => TriggerCustomEvent(o => { SetBreakeven(); }, null);
			Grid.SetColumn(btnBE, 0);
			beRevertGrid.Children.Add(btnBE);

			Button btnRevert = CreateHudButton("Revert", revertBg, null, 33, 12);
			btnRevert.Click += (s, ev) => TriggerCustomEvent(o => { RevertPosition(); }, null);
			Grid.SetColumn(btnRevert, 2);
			beRevertGrid.Children.Add(btnRevert);

			secBot.Children.Add(beRevertGrid);

			SolidColorBrush closeBg = new SolidColorBrush(Color.FromRgb(20, 20, 20)); // Very dark gray (almost black)
			Button btnClose = CreateHudButton("Close/flatten", closeBg, null, 66, 15);
			btnClose.Margin = new Thickness(0, 0, 0, 0);
			btnClose.Click += (s, ev) =>
			{
				TriggerCustomEvent(o =>
				{
					FlattenAllPositions();
				}, null);
			};
			secBot.Children.Add(btnClose);

			// --- Daily Max DD & Daily Max Profit toggle buttons (side-by-side below BOT ON/OFF, TradeManager style) ---
			Grid dailyRiskGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
			dailyRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			dailyRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
			dailyRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			SolidColorBrush dailyOffBg = new SolidColorBrush(Color.FromRgb(45, 50, 65));
			SolidColorBrush dailyOnBg  = new SolidColorBrush(Color.FromRgb(58, 19, 107)); // Darker purple (#3A136B)

			Button btnDailyMaxDD = CreateHudButton(cachedIsDailyMaxDD ? "Max DD: ON" : "Max DD: OFF",
				cachedIsDailyMaxDD ? dailyOnBg : dailyOffBg, null, 24, 10);
			btnDailyMaxDD.Foreground = cachedIsDailyMaxDD ? Brushes.White : Brushes.LightGray;

			btnDailyMaxDD.Click += (s, ev) =>
			{
				cachedIsDailyMaxDD = !cachedIsDailyMaxDD;
				DailyMaxDDEnabled = cachedIsDailyMaxDD;
				btnDailyMaxDD.Content = cachedIsDailyMaxDD ? "Max DD: ON" : "Max DD: OFF";
				btnDailyMaxDD.Background = cachedIsDailyMaxDD ? dailyOnBg : dailyOffBg;
				btnDailyMaxDD.Foreground = cachedIsDailyMaxDD ? Brushes.White : Brushes.LightGray;

				if (IsDailyRiskBreached(out string breachReason))
				{
					ShowHudStatus(breachReason, Brushes.OrangeRed);
					TriggerCustomEvent(o => { CancelPendingBotOrder(breachReason); }, null);
				}
				else
				{
					ShowHudStatus("Daily Max DD: " + (cachedIsDailyMaxDD ? "ON ($" + cachedDailyMaxDD + ")" : "OFF"), Brushes.LightGreen);
				}
			};
			Grid.SetColumn(btnDailyMaxDD, 0);
			dailyRiskGrid.Children.Add(btnDailyMaxDD);

			Button btnDailyMaxProfit = CreateHudButton(cachedIsDailyMaxProfit ? "Max Profit: ON" : "Max Profit: OFF",
				cachedIsDailyMaxProfit ? dailyOnBg : dailyOffBg, null, 24, 10);
			btnDailyMaxProfit.Foreground = cachedIsDailyMaxProfit ? Brushes.White : Brushes.LightGray;

			btnDailyMaxProfit.Click += (s, ev) =>
			{
				cachedIsDailyMaxProfit = !cachedIsDailyMaxProfit;
				DailyMaxProfitEnabled = cachedIsDailyMaxProfit;
				btnDailyMaxProfit.Content = cachedIsDailyMaxProfit ? "Max Profit: ON" : "Max Profit: OFF";
				btnDailyMaxProfit.Background = cachedIsDailyMaxProfit ? dailyOnBg : dailyOffBg;
				btnDailyMaxProfit.Foreground = cachedIsDailyMaxProfit ? Brushes.White : Brushes.LightGray;

				if (IsDailyRiskBreached(out string breachReason))
				{
					ShowHudStatus(breachReason, Brushes.OrangeRed);
					TriggerCustomEvent(o => { CancelPendingBotOrder(breachReason); }, null);
				}
				else
				{
					ShowHudStatus("Daily Max Profit: " + (cachedIsDailyMaxProfit ? "ON ($" + cachedDailyMaxProfit + ")" : "OFF"), Brushes.LightGreen);
				}
			};
			Grid.SetColumn(btnDailyMaxProfit, 2);
			dailyRiskGrid.Children.Add(btnDailyMaxProfit);

			secBot.Children.Add(dailyRiskGrid);
			mainPanel.Children.Add(CreateSectionCard(secBot, 6));

			// --- SIGNAL TRADE GATES: which independent KatB* signals the bot may execute ---
			mainPanel.Children.Add(CreateModuleTitle("SIGNAL TRADE (add KatB* on chart)"));
			var secSignal = new StackPanel();
			Grid sRow = CreateTwoColGrid();
			sRow.Margin = new Thickness(0);
			Button btnB1 = CreateFilterToggle("Trade B1", () => tradeB1, v => { tradeB1 = v; TradeB1 = v; }, 24, 10, hudBotOnBrush);
			Grid.SetColumn(btnB1, 0);
			sRow.Children.Add(btnB1);
			Button btnB2 = CreateFilterToggle("Trade B2", () => tradeB2, v => { tradeB2 = v; TradeB2 = v; }, 24, 10, hudBotOnBrush);
			Grid.SetColumn(btnB2, 2);
			sRow.Children.Add(btnB2);
			secSignal.Children.Add(sRow);
			secSignal.Children.Add(new TextBlock
			{
				Text = "Add KatA1/KatB1/KatB2 from Indicators → KAT",
				Foreground = new SolidColorBrush(Color.FromRgb(110, 120, 145)),
				FontSize = 9,
				Margin = new Thickness(2, 4, 0, 0),
				TextWrapping = TextWrapping.Wrap
			});
			mainPanel.Children.Add(CreateSectionCard(secSignal, 6));

			// --- BOT FILTER module: the only filter side since v0.79 (ALERT FILTER removed; A1 is a
			// pure fan). Gates B1/B2 — and the A2 alert placeholder — via PassFilters/PassFiltersAt.
			mainPanel.Children.Add(CreateModuleTitle("BOT FILTER"));
			var secBotFilter = new StackPanel();
			Grid bfRow1 = CreateTwoColGrid();
			Button tAdxRise = CreateFilterToggle("ADX rising", () => cachedAdxRise, v => cachedAdxRise = v);
			Grid.SetColumn(tAdxRise, 0);
			bfRow1.Children.Add(tAdxRise);
			Button tAdxMtf = CreateFilterToggle("ADX MTF", () => cachedAdxMtf, v => cachedAdxMtf = v);
			Grid.SetColumn(tAdxMtf, 2);
			bfRow1.Children.Add(tAdxMtf);
			Grid bfRow2 = CreateTwoColGrid();
			Button tEr = CreateFilterToggle("ER (trend)", () => cachedEr, v => cachedEr = v);
			Grid.SetColumn(tEr, 0);
			bfRow2.Children.Add(tEr);
			Button tCi = CreateFilterToggle("CI (chop)", () => cachedCi, v => cachedCi = v);
			Grid.SetColumn(tCi, 2);
			bfRow2.Children.Add(tCi);
			Grid bfRow3 = CreateTwoColGrid();
			Button tVol = CreateFilterToggle("Volume", () => cachedVol, v => cachedVol = v);
			Grid.SetColumn(tVol, 0);
			bfRow3.Children.Add(tVol);
			Button tTime = CreateFilterToggle("Time window", () => cachedTime, v => cachedTime = v);
			Grid.SetColumn(tTime, 2);
			bfRow3.Children.Add(tTime);
			secBotFilter.Children.Add(bfRow1);
			secBotFilter.Children.Add(bfRow2);
			secBotFilter.Children.Add(bfRow3);
			mainPanel.Children.Add(CreateSectionCard(secBotFilter, 6));

			// --- DRAW module: Clear removes all drawings from this HUD (signals + A0 + A1 stages) ---
			mainPanel.Children.Add(CreateModuleTitle("DRAW"));
			var secDraw = new StackPanel();
			Button btnClear = CreateHudButton("Clear", new SolidColorBrush(Color.FromRgb(20, 20, 20)),
				(s, e) => TriggerCustomEvent(o => ClearOldSignalDrawings(), null));
			secDraw.Children.Add(btnClear);
			mainPanel.Children.Add(CreateSectionCard(secDraw, 0));

			hudBorder.Child = mainPanel;
			StartPanelWatchdog();
		}

		// Mirrors the HUD account pick into Chart Trader's own account selector so chart order
		// rendering follows the HUD account. Locates the selector by item content (account names),
		// which survives NT8 template/layout changes better than hardcoded names.
		// Pattern proven in nt8-kat-TradeManager SyncChartTraderAccount.
		private void SyncChartTraderAccount(string accountName)
		{
			try
			{
				if (string.IsNullOrEmpty(accountName)) return;
				DependencyObject ctControl = GetChartTraderControl();
				if (ctControl == null) return;

				var combos = new List<ComboBox>();
				FindAllVisualChildren<ComboBox>(ctControl, combos);
				foreach (ComboBox combo in combos)
					foreach (object item in combo.Items)
					{
						if (item == null) continue;
						// Rithmic accounts render as "name!connection!connection" in Chart Trader's
						// selector while Account.Name stays short — match on Name first, then on
						// exact/prefixed ToString.
						string itemText = item.ToString();
						bool match = (item as Account)?.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase) == true
							|| itemText.Equals(accountName, StringComparison.OrdinalIgnoreCase)
							|| itemText.StartsWith(accountName + "!", StringComparison.OrdinalIgnoreCase);
						if (!match) continue;
						if (!ReferenceEquals(combo.SelectedItem, item))
							combo.SelectedItem = item;
						return;
					}
				// No match: Chart Trader's account selector (NinjaTrader.Gui.Tools.AccountSelector) only
				// lists accounts NT8 currently offers — connected-connection accounts, minus Backtest/Playback.
				// Report what it actually lists so the gap is diagnosable.
				var listed = new List<string>();
				foreach (ComboBox combo in combos)
					foreach (object item in combo.Items)
						if (item is Account listedAcc && !listed.Contains(listedAcc.Name))
							listed.Add(listedAcc.Name);
				Print(string.Format("[Kat34Scalper] Chart Trader sync skipped — '{0}' not in its account list (listed: {1})",
					accountName, listed.Count > 0 ? string.Join(", ", listed) : "none"));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Chart Trader account sync failed: {0}", ex.Message));
			}
		}

		private DependencyObject GetChartTraderControl()
		{
			if (ChartControl == null) return null;
			if (ChartControl.OwnerChart != null && ChartControl.OwnerChart.ChartTrader != null)
			{
				var ct = ChartControl.OwnerChart.ChartTrader;
				if (ct.Visibility == Visibility.Visible) return ct;
			}
			Window window = Window.GetWindow(ChartControl);
			if (window != null)
			{
				var ct = FindVisualChildByTypeName(window, "ChartTraderControl") ?? FindVisualChildByTypeName(window, "ChartTrader");
				if (ct is FrameworkElement fe && fe.Visibility == Visibility.Visible) return ct;
			}
			return null;
		}

		private DependencyObject FindVisualChildByTypeName(DependencyObject parent, string typeName)
		{
			if (parent == null) return null;
			int count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				DependencyObject child = VisualTreeHelper.GetChild(parent, i);
				if (child != null && child.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
					return child;
				DependencyObject result = FindVisualChildByTypeName(child, typeName);
				if (result != null) return result;
			}
			return null;
		}

		private void FindAllVisualChildren<T>(DependencyObject parent, List<T> results) where T : DependencyObject
		{
			if (parent == null) return;
			int count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				DependencyObject child = VisualTreeHelper.GetChild(parent, i);
				if (child is T typedChild)
					results.Add(typedChild);
				FindAllVisualChildren<T>(child, results);
			}
		}

		private System.Windows.Threading.DispatcherTimer panelWatchdog;

		private void StartPanelWatchdog()
		{
			if (panelWatchdog == null)
			{
				panelWatchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
				panelWatchdog.Tick += OnPanelWatchdogTick;
			}
			panelWatchdog.Start();
		}

		private void StopPanelWatchdog()
		{
			if (panelWatchdog != null)
			{
				panelWatchdog.Stop();
				panelWatchdog = null;
			}
		}

		private void OnPanelWatchdogTick(object sender, EventArgs e)
		{
			try
			{
				EnsureAccountEventSubscription();
				EvaluateDailyRiskLimits();
				TrySubmitPendingRevert();
				ScheduleAtmBracketMerge();
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Watchdog tick error: {0}", ex.Message));
			}
		}

		private void RemoveHud()
		{
			StopHudDrag();
			StopPanelWatchdog();
			RemoveAccountEventSubscription();
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
			atmComboBox = null;
			atmSetButtons = null;
		}
		#endregion
	}
}
