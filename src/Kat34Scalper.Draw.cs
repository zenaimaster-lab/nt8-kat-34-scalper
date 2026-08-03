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
		#region Signal Drawings (lines, arrows, labels, version label, alert)
		private const int MAX_SIGNAL_RECORDS = 200;
		private sealed class KatSignalRecord
		{
			public int Bar;
			public bool IsBuy;
			public string Owner; // "A1" or future "A2" etc. — enables per-signal ON/OFF cleanup
			public double ArrowY;
			public double TextY;
			public double Candidate1;
			public double Candidate2;
			public double EntryPrice;
			public double SlPrice;
			public double TpPrice;
			public double BePrice;
			public double Sl1Price;
			public double Sl2Price;
			public bool DrawLogged;
			public bool KeepAlive; // A2 pending entry: lines render while the setup is alive, ignoring the Line Length fade
		}
		private readonly List<KatSignalRecord> signalRecords = new List<KatSignalRecord>();
		private bool versionDrawn;
		private bool legacySignalDrawingsCleared;
		// Arrow/Text feature removed per request. Only lines + ATM triggers remain.

		// Primary-series timeframe, e.g. "30 Second" — proof the indicator computes on the chart TF it was added to.
		private string ChartTimeframe()
		{
			return BarsArray[0].BarsPeriod.Value + " " + BarsArray[0].BarsPeriod.BarsPeriodType;
		}

		private void DrawVersionLabel()
		{
			versionDrawn = true;
			Draw.TextFixed(this, "K34S_version", string.Format("Kat34Scalper v{0} ({1}) [{2}]", VERSION, RELEASE_DATE, ChartTimeframe()), TextPosition.TopLeft);
		}

		private void PlayAlertSound()
		{
			try { PlaySound(Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", AlertSound)); }
			catch { }
		}
		private int SafeLineLengthBars()
		{
			return Math.Max(1, Math.Min(LineLengthBars, 500));
		}

		private int SafeLineWidth()
		{
			return Math.Max(1, Math.Min(LineWidth, 10));
		}

		private string SignalTag(KatSignalRecord record, string suffix)
		{
			string mod = string.IsNullOrEmpty(record.Owner) ? "A1" : record.Owner;
			return "K34S_" + mod + "_" + (record.IsBuy ? "B" : "S") + "_" + suffix + "_" + record.Bar;
		}

		private void RenderSignal(KatSignalRecord record)
		{
			int age = CurrentBars[0] - record.Bar;
			if (age < 0) return;
			// Per-signal ownership: if owner disabled, skip (OFF already removed its drawings)
			string owner = record.Owner ?? "A1";
			if (owner == "A1" && !cachedA1) return;
			if (owner == "A2" && !cachedA2) return;
			if (owner == "A3" && !cachedA3) return;

			Brush entryBrush = new SolidColorBrush(record.IsBuy ? BuyEntryLineColor : SellEntryLineColor);
			Brush slBrush = new SolidColorBrush(SLLineColor);
			Brush tpBrush = new SolidColorBrush(TPLineColor);
			Brush textBrush = new SolidColorBrush(record.IsBuy ? BuyTextColor : SellTextColor);
			int lineLength = SafeLineLengthBars();
			int width = SafeLineWidth();

			// Arrows + BUY/SELL text removed. Only lines + ATM triggers (BE/SL1/SL2) render.
			// KeepAlive (A2 pending entry): no age cap — the lines live until Cancel/Filled.
			if (age <= lineLength || record.KeepAlive)
			{
				if (record.Candidate1 != record.Candidate2)
				{
					Brush faded = new SolidColorBrush(record.IsBuy ? BuyEntryLineColor : SellEntryLineColor) { Opacity = 0.35 };
					Draw.Line(this, SignalTag(record, "C1"), false, age, record.Candidate1, 0, record.Candidate1, faded, DashStyleHelper.Dot, 1);
					Draw.Line(this, SignalTag(record, "C2"), false, age, record.Candidate2, 0, record.Candidate2, faded, DashStyleHelper.Dot, 1);
				}
				else
				{
					RemoveDrawObject(SignalTag(record, "C1"));
					RemoveDrawObject(SignalTag(record, "C2"));
				}

				Draw.Line(this, SignalTag(record, "ENTRY"), false, age, record.EntryPrice, 0, record.EntryPrice, entryBrush, DashStyleHelper.Solid, width);
				Draw.Line(this, SignalTag(record, "SL"), false, age, record.SlPrice, 0, record.SlPrice, slBrush, DashStyleHelper.Dash, width);
				Draw.Line(this, SignalTag(record, "TP"), false, age, record.TpPrice, 0, record.TpPrice, tpBrush, DashStyleHelper.Dash, width);
				if (record.BePrice != 0)
					Draw.Line(this, SignalTag(record, "BE"), false, age, record.BePrice, 0, record.BePrice, Brushes.DeepSkyBlue, DashStyleHelper.DashDot, 1);
				if (record.Sl1Price != 0)
					Draw.Line(this, SignalTag(record, "SL1"), false, age, record.Sl1Price, 0, record.Sl1Price, Brushes.Orange, DashStyleHelper.Dot, 1);
				if (record.Sl2Price != 0)
					Draw.Line(this, SignalTag(record, "SL2"), false, age, record.Sl2Price, 0, record.Sl2Price, Brushes.Magenta, DashStyleHelper.Dot, 1);
			}
			if (!record.DrawLogged)
			{
				record.DrawLogged = true;
				Print(string.Format("[Kat34Scalper][DRAW] record bar={0}, side={1}, age={2}, entry={3:F5}, sl={4:F5}, tp={5:F5}, lineLength={6}, tags={7}_ENTRY/{7}_SL/{7}_TP",
					record.Bar, record.IsBuy ? "BUY" : "SELL", age, record.EntryPrice, record.SlPrice, record.TpPrice,
					lineLength, "K34S_" + (record.IsBuy ? "B" : "S") + "_"));
			}
		}

		private void RefreshSignalDrawings()
		{
			foreach (KatSignalRecord record in signalRecords)
				RenderSignal(record);
		}

		private void ClearLegacySignalDrawings()
		{
			if (legacySignalDrawingsCleared) return;
			legacySignalDrawingsCleared = true;
			try
			{
				var doomed = new List<string>();
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					if (name != null && name.StartsWith("K8934_", StringComparison.Ordinal))
						doomed.Add(name);
				}
				foreach (string tag in doomed)
					RemoveDrawObject(tag);
				if (doomed.Count > 0)
					Print(string.Format("[Kat34Scalper] Removed {0} stale Kat8934 drawing(s).", doomed.Count));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Legacy drawing cleanup error: {0}", ex.Message));
			}
		}

		// replay = true during a History Days backfill pass: same drawing, no alert sound, no bot order.
		// owner = signal module id ("A1", "A2"...) for per-signal ON/OFF cleanup ownership.
		// Returns the created record so the owning signal can migrate/cancel it later (A2).
		private KatSignalRecord DrawSignal(bool isBuy, int bar, double high, double low, double c1, double c2, int offsetTicks, int stopTicks, int targetTicks, bool replay = false, string owner = "A1")
		{
			if (signalRecords.Count >= MAX_SIGNAL_RECORDS)
				signalRecords.RemoveAt(0);
			KatSignalRecord record = new KatSignalRecord { Owner = owner };
			FillSignalRecord(record, isBuy, bar, high, low, c1, c2, offsetTicks, stopTicks, targetTicks);
			signalRecords.Add(record);
			RenderSignal(record);

			if (!replay)
				PlayAlertSound();
			Print(string.Format("[Kat34Scalper][{6}][DRAW]{3} {0} signal @ bar {1} — entry {2:F5}, SL {4:F5}, TP {5:F5}", isBuy ? "BUY" : "SELL", bar, record.EntryPrice, replay ? "[replay]" : "", record.SlPrice, record.TpPrice, owner ?? "A1"));
			return record;
		}

		// Computes every price level (entry, candidates, SL/TP from ATM or settings, BE/SL1/SL2
		// triggers) and stores them on the record. Shared by DrawSignal (new signal) and the A2
		// migration (same record, new bar + better extreme — call RemoveSignalRecordDrawings first).
		private void FillSignalRecord(KatSignalRecord record, bool isBuy, int bar, double high, double low, double c1, double c2, int offsetTicks, int stopTicks, int targetTicks)
		{
			double tick = TickSize;

			// A1 dual entry: c1 = U-turn bar extreme, c2 = best later candidate (0 = none yet — fall back to the signal bar).
			double ref1 = c1 != 0 ? c1 : (isBuy ? high : low);
			double ref2 = c2 != 0 ? c2 : ref1;
			double entryPrice = Kat34ScalperLogic.EffectiveEntry(isBuy, ref1, ref2, offsetTicks, tick);
			double cand1 = isBuy ? ref1 + offsetTicks * tick : ref1 - offsetTicks * tick;
			double cand2 = isBuy ? ref2 + offsetTicks * tick : ref2 - offsetTicks * tick;

			// TradeManager-style levels: SL/TP come from the selected ATM template when it defines them,
			// otherwise from the indicator settings; BE/SL1/SL2 trailing-SL triggers exist only with an ATM.
			Kat34ScalperAtmData atm = GetAtmData();
			int slTicks = atm.StopLoss > 0 ? atm.StopLoss : stopTicks;
			int tpTicks = atm.Target > 0 ? atm.Target : targetTicks;

			// Trailing-SL trigger lines from the ATM template — same style as KatTradeManager
			// (BE DeepSkyBlue dash-dot, SL1 orange dot, SL2 magenta dot, 1 px, profit side of entry).
			int dir = isBuy ? 1 : -1;
			double bePrice = 0;
			double sl1Price = 0;
			double sl2Price = 0;
			if (atm.BETrigger > 0)
				bePrice = entryPrice + dir * atm.BETrigger * tick;
			if (atm.SL1Trigger > 0)
				sl1Price = entryPrice + dir * atm.SL1Trigger * tick;
			if (atm.SL2Trigger > 0)
				sl2Price = entryPrice + dir * atm.SL2Trigger * tick;

			record.Bar = bar;
			record.IsBuy = isBuy;
			record.ArrowY = isBuy ? low - ArrowOffsetTicks * tick : high + ArrowOffsetTicks * tick;
			record.TextY = isBuy ? entryPrice - tick : entryPrice + tick; // buy label below line, sell above
			record.Candidate1 = cand1;
			record.Candidate2 = cand2;
			record.EntryPrice = entryPrice;
			record.SlPrice = isBuy ? entryPrice - slTicks * tick : entryPrice + slTicks * tick;
			record.TpPrice = isBuy ? entryPrice + tpTicks * tick : entryPrice - tpTicks * tick;
			record.BePrice = bePrice;
			record.Sl1Price = sl1Price;
			record.Sl2Price = sl2Price;
		}

		// Removes every draw object a signal record owns (entry/SL/TP/candidates/ATM triggers).
		// Tags derive from record.Bar — call BEFORE updating the bar on a migration.
		private void RemoveSignalRecordDrawings(KatSignalRecord record)
		{
			RemoveDrawObject(SignalTag(record, "C1"));
			RemoveDrawObject(SignalTag(record, "C2"));
			RemoveDrawObject(SignalTag(record, "ENTRY"));
			RemoveDrawObject(SignalTag(record, "SL"));
			RemoveDrawObject(SignalTag(record, "TP"));
			RemoveDrawObject(SignalTag(record, "BE"));
			RemoveDrawObject(SignalTag(record, "SL1"));
			RemoveDrawObject(SignalTag(record, "SL2"));
		}

		// Removes a record's draw objects and drops it from the list (A2 cancel).
		private void RemoveSignalRecord(KatSignalRecord record)
		{
			RemoveSignalRecordDrawings(record);
			signalRecords.Remove(record);
		}

		// Removes every draw object whose tag starts with the given prefix (data thread only).
		// Used by the signal sub-modules when they are switched OFF (independence: only their own tags).
		private void RemoveModuleDrawings(string prefix)
		{
			try
			{
				var doomed = new List<string>();
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					if (name != null && name.StartsWith(prefix, StringComparison.Ordinal))
						doomed.Add(name);
				}
				foreach (string tag in doomed)
					RemoveDrawObject(tag);
				if (doomed.Count > 0)
					Print(string.Format("[Kat34Scalper] Removed {0} drawing(s) with prefix {1}.", doomed.Count, prefix));
				ForceRefresh();
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Remove module drawings error ({0}): {1}", prefix, ex.Message));
			}
		}

		// A1 switched OFF: drop A1-owned signal records + A1 stage markers + A1's own K34S_A1_* drawings only.
		// Ownership contract: each signal uses K34S_<OWNER>_<B/S>_ prefix (A1 default).
		private void ClearA1Drawings()
		{
			signalRecords.RemoveAll(r => (r.Owner ?? "A1") == "A1");
			RemoveModuleDrawings("K34S_A1ST_");
			RemoveModuleDrawings("K34S_A1_");
		}

		// Called from the data thread through TriggerCustomEvent from HUD clicks.
		private void ClearOldSignalDrawings()
		{
			try
			{
				signalRecords.Clear();
				var doomed = new List<string>();
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					if (name != null &&
						(name.StartsWith("K34S_", StringComparison.Ordinal) || name.StartsWith("K8934_", StringComparison.Ordinal)))
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
				Print(string.Format("[Kat34Scalper] Cleared {0} old signal drawing(s).", doomed.Count));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Clear error: {0}", ex.Message));
			}
		}

		// Arrow/Text feature removed. No toggle apply needed. Lines always render.
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
				Text = string.Format("⚡ KAT 34 SCALPER v{0}", VERSION),
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

			// --- SIGNAL module: independent sub-module toggles (A0 fan, A1 89-34) + future signal slots ---
			// ON backfills the module's History Days window immediately; OFF removes only that module's drawings.
			mainPanel.Children.Add(CreateModuleTitle("SIGNAL"));
			var secSignal = new StackPanel();
			Grid sRow = CreateTwoColGrid();
			Button tA0 = CreateFilterToggle("A0 fan", () => cachedA0, v => SetA0Signal(v));
			Grid.SetColumn(tA0, 0);
			sRow.Children.Add(tA0);
			Button tA1 = CreateFilterToggle("A1 89-34", () => cachedA1, v => SetA1Signal(v));
			Grid.SetColumn(tA1, 2);
			sRow.Children.Add(tA1);
			secSignal.Children.Add(sRow);

			Grid sRow2 = CreateTwoColGrid();
			sRow2.Margin = new Thickness(0);
			Button btnA2 = CreateFilterToggle("A2 34+8", () => cachedA2, v => SetA2Signal(v));
			Grid.SetColumn(btnA2, 0);
			sRow2.Children.Add(btnA2);
			Button btnA3 = CreateFilterToggle("A3 8x34", () => cachedA3, v => SetA3Signal(v));
			Grid.SetColumn(btnA3, 2);
			sRow2.Children.Add(btnA3);
			secSignal.Children.Add(sRow2);
			mainPanel.Children.Add(CreateSectionCard(secSignal, 6));

			// --- FILTER module: MTF, ADX, Volume, Time window (A0 fan gate removed) ---
			mainPanel.Children.Add(CreateModuleTitle("FILTER"));
			var secFilter = new StackPanel();
			Grid fRow1 = CreateTwoColGrid();
			Button tMtf = CreateFilterToggle("MTF", () => cachedMtf, v => cachedMtf = v);
			Grid.SetColumn(tMtf, 0);
			fRow1.Children.Add(tMtf);
			Button tAdx = CreateFilterToggle("ADX", () => cachedAdx, v => cachedAdx = v);
			Grid.SetColumn(tAdx, 2);
			fRow1.Children.Add(tAdx);
			Button tVol = CreateFilterToggle("Volume", () => cachedVol, v => cachedVol = v);
			Grid fRow2 = CreateTwoColGrid();
			fRow2.Margin = new Thickness(0);
			Grid.SetColumn(tVol, 0);
			fRow2.Children.Add(tVol);
			Button tTime = CreateFilterToggle("Time window", () => cachedTime, v => cachedTime = v);
			Grid.SetColumn(tTime, 2);
			fRow2.Children.Add(tTime);
			secFilter.Children.Add(fRow1);
			secFilter.Children.Add(fRow2);
			mainPanel.Children.Add(CreateSectionCard(secFilter, 6));

			// --- BOT module: account, ATM template, BOT on/off ---
			mainPanel.Children.Add(CreateModuleTitle("BOT"));
			var secBot = new StackPanel();
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
			}
			accCombo.SelectionChanged += (s, e) =>
			{
				if (accCombo.SelectedItem == null) return;
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
			};
			AddGridRow(accGrid, "Acc:", accCombo);
			secBot.Children.Add(accGrid);

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

			Button btnBot = CreateHudButton("BOT: OFF", hudOffBrush, null, 26, 11);
			btnBot.Foreground = Brushes.LightGray;
			btnBot.Margin = new Thickness(0);
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
					TriggerCustomEvent(o =>
					{
						pendingMigrate = false;
						CancelPendingBotOrder("BOT switched OFF");
					}, null);
				}
			};
			secBot.Children.Add(btnBot);
			mainPanel.Children.Add(CreateSectionCard(secBot, 6));

			// --- DRAW module: Clear removes all drawings from this HUD (signals + A0 + A1 stages) ---
			mainPanel.Children.Add(CreateModuleTitle("DRAW"));
			var secDraw = new StackPanel();
			Button btnClear = CreateHudButton("Clear", new SolidColorBrush(Color.FromRgb(20, 20, 20)),
				(s, e) => TriggerCustomEvent(o => ClearOldSignalDrawings(), null));
			secDraw.Children.Add(btnClear);
			mainPanel.Children.Add(CreateSectionCard(secDraw, 0));

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
			atmComboBox = null;
			atmSetButtons = null;
		}
		#endregion
	}
}
