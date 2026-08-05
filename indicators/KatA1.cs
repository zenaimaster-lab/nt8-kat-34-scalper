/*
 * KatA1.cs — Standalone Alert Signal A1 (EmaZone30s).
 * Appears under Add Indicators → KAT → KatA1.
 * Chart-only: vertical env lines + pale bands + alert sound. No bot/orders.
 * Shares pure math with Kat34Scalper via Kat34ScalperLogic (same A1 rules).
 * Kat34Scalper still embeds A1 for the all-in-one bot HUD chart.
 */

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public class KatA1 : Indicator
	{
		private EMA a1Ema8, a1Ema34, a1Ema89, a1Ema144, a1Ema200;
		private ATR a1Atr;
		private EMA[] zoneEma34;

		private int lastDir;
		private int invalidStreak;
		private int prevDir;
		private int bandDir;
		private int bandStartIdx;
		private double bandHi = double.MinValue;
		private double bandLo = double.MaxValue;
		private bool backfilled;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"KatA1 — Alert Signal A1 EmaZone30s (standalone, chart-only).";
				Name = "KatA1";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				IsSuspendedWhileInactive = true;

				HistoryDays = 3;
				PeriodSeconds = 30;
				CondEma8Above34 = true;
				CondEma34Above89 = true;
				CondEma89Above144 = true;
				CondEma144Above200 = true;
				AngleEnabled = false;
				AngleMin = 30;
				BreakBars = 3;
				AtrPeriod = 14;
				LineWidth = 2;
				LongLineColor = Colors.DarkGreen;
				ShortLineColor = Colors.DarkRed;
				EmaZoneTf1 = KatEmaZoneTf.M3;
				EmaZoneTf2 = KatEmaZoneTf.M5;
				EmaZoneTf3 = KatEmaZoneTf.M15;
				AlertSound = "Alert1.wav";
			}
			else if (State == State.Configure)
			{
				// Series 0 = chart. Series 1 = A1 fan TF. Series 2/3/4 = EMA34 zone TFs.
				AddDataSeries(Data.BarsPeriodType.Second, Math.Max(1, PeriodSeconds));
				AddDataSeries(Data.BarsPeriodType.Second, (int)EmaZoneTf1);
				AddDataSeries(Data.BarsPeriodType.Second, (int)EmaZoneTf2);
				AddDataSeries(Data.BarsPeriodType.Second, (int)EmaZoneTf3);
			}
			else if (State == State.DataLoaded)
			{
				a1Ema8 = EMA(BarsArray[1], 8);
				a1Ema34 = EMA(BarsArray[1], 34);
				a1Ema89 = EMA(BarsArray[1], 89);
				a1Ema144 = EMA(BarsArray[1], 144);
				a1Ema200 = EMA(BarsArray[1], 200);
				a1Atr = ATR(BarsArray[1], Math.Max(1, AtrPeriod));
				zoneEma34 = new[] { EMA(BarsArray[2], 34), EMA(BarsArray[3], 34), EMA(BarsArray[4], 34) };
				backfilled = false;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 1) return;
			if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[1] < 201) return;
			if (a1Ema8 == null || a1Ema34 == null || a1Ema89 == null || a1Ema144 == null || a1Ema200 == null || a1Atr == null) return;

			// One-shot history replay at end of historical load / first realtime bar.
			if (!backfilled && (State == State.Realtime || CurrentBars[1] >= BarsArray[1].Count - 1))
			{
				Backfill();
				backfilled = true;
			}

			if (State != State.Realtime && CurrentBars[1] < BarsArray[1].Count - 1) return;

			int rawDir = DirectionAt(0, out double angle);
			if (rawDir != 0 && !EmaZonePassAt(0, rawDir)) rawDir = 0;
			int dir = Kat34ScalperLogic.A1DebouncedDir(rawDir, lastDir, invalidStreak, BreakBars);

			if (Highs[1][0] > bandHi) bandHi = Highs[1][0];
			if (Lows[1][0] < bandLo) bandLo = Lows[1][0];

			if (dir != prevDir)
			{
				DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1], bandHi, bandLo);
				if (dir == 0) DrawRangeLine(0);
				bandDir = dir;
				bandStartIdx = CurrentBars[1];
				prevDir = dir;
			}
			else if (bandDir != 0)
			{
				DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1], bandHi, bandLo);
			}

			bool fired = Kat34ScalperLogic.A1EdgeStep(rawDir, lastDir, invalidStreak, BreakBars, out lastDir, out invalidStreak);
			if (fired)
			{
				DrawEnvLine(lastDir, 0);
				if (State == State.Realtime) PlayAlert();
			}
		}

		private void Backfill()
		{
			int warm = 201;
			int max = CurrentBars[1] - 1;
			if (max < warm) return;

			int start = Math.Min(HistoryStartBarsAgo(HistoryDays), max - warm);
			double hi = double.MinValue, lo = double.MaxValue;
			for (int ago = start; ago >= 1; ago--)
			{
				if (Highs[1][ago] > hi) hi = Highs[1][ago];
				if (Lows[1][ago] < lo) lo = Lows[1][ago];
			}

			int ld = 0, inv = 0, bDir = 0, bStart = CurrentBars[1] - start, lines = 0;
			for (int ago = start; ago >= 1; ago--)
			{
				int rawDir = DirectionAt(ago, out _);
				if (rawDir != 0 && !EmaZonePassAt(ago, rawDir)) rawDir = 0;
				int dir = Kat34ScalperLogic.A1DebouncedDir(rawDir, ld, inv, BreakBars);
				if (dir != bDir)
				{
					DrawEnvBand(bDir, bStart, CurrentBars[1] - ago, hi, lo);
					if (dir == 0) DrawRangeLine(ago);
					bDir = dir;
					bStart = CurrentBars[1] - ago;
				}
				bool fired = Kat34ScalperLogic.A1EdgeStep(rawDir, ld, inv, BreakBars, out ld, out inv);
				if (fired) { DrawEnvLine(ld, ago); lines++; }
			}

			lastDir = ld;
			invalidStreak = inv;
			prevDir = bDir;
			bandDir = bDir;
			bandStartIdx = bStart;
			bandHi = hi;
			bandLo = lo;
			DrawEnvBand(bandDir, bandStartIdx, CurrentBars[1], hi, lo);
			Print(string.Format("[KatA1] backfill — {0} day(s), {1} line(s), lastDir={2}", HistoryDays, lines, lastDir));
		}

		private int DirectionAt(int ago, out double angle)
		{
			angle = Kat34ScalperLogic.SlopeAngleDeg(a1Ema34[ago], a1Ema34[ago + 1], a1Atr[ago]);
			return Kat34ScalperLogic.A1Direction(
				CondEma8Above34, CondEma34Above89, CondEma89Above144, CondEma144Above200, AngleEnabled,
				a1Ema8[ago], a1Ema34[ago], a1Ema89[ago], a1Ema144[ago], a1Ema200[ago],
				angle, Math.Abs(AngleMin));
		}

		private bool EmaZonePassAt(int ago, int dir)
		{
			if (dir == 0 || zoneEma34 == null) return true;
			int a1Sec = Math.Max(1, PeriodSeconds);
			for (int z = 0; z < zoneEma34.Length; z++)
			{
				int s = 2 + z;
				if (zoneEma34[z] == null || CurrentBars == null || CurrentBars.Length <= s || CurrentBars[s] < 1) continue;
				int zoneSec = z == 0 ? (int)EmaZoneTf1 : (z == 1 ? (int)EmaZoneTf2 : (int)EmaZoneTf3);
				DateTime cutoff = Kat34ScalperLogic.ClosedBarCutoff(Times[1][ago], a1Sec, zoneSec);
				int idx = Kat34ScalperLogic.BarsAgoAtOrBefore(i => Times[s][i], CurrentBars[s], cutoff);
				if (idx < 0) continue;
				if (!Kat34ScalperLogic.EmaZonePass(dir, Closes[s][idx], zoneEma34[z][idx])) return false;
			}
			return true;
		}

		private int HistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Times[1][0].Subtract(TimeSpan.FromDays(days));
			int max = CurrentBars[1];
			int ago = 0;
			while (ago < max && Times[1][ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		private void DrawEnvLine(int dir, int ago)
		{
			string tag = string.Format("KATA1_VL_{0}_{1}", dir > 0 ? "B" : "S", CurrentBars[1] - ago);
			Brush brush = new SolidColorBrush(dir > 0 ? LongLineColor : ShortLineColor);
			int width = Math.Max(1, Math.Min(LineWidth, 10));
			Draw.VerticalLine(this, tag, Times[1][ago], brush, DashStyleHelper.Dash, width);
		}

		private void DrawRangeLine(int ago)
		{
			string tag = string.Format("KATA1_VR_{0}", CurrentBars[1] - ago);
			Draw.VerticalLine(this, tag, Times[1][ago], Brushes.Gray, DashStyleHelper.Dash, 2);
		}

		private void DrawEnvBand(int dir, int startIdx, int endIdx, double hi, double lo)
		{
			if (!Kat34ScalperLogic.EnvBandAnchors(dir, startIdx, endIdx, hi, lo, CurrentBars[1], out int agoStart, out int agoEnd)) return;
			Brush area = new SolidColorBrush(dir > 0 ? Colors.Green : Colors.Red);
			string tag = string.Format("KATA1_BAND_{0}_{1}", dir > 0 ? "B" : "S", startIdx);
			Draw.Rectangle(this, tag, false, Times[1][agoStart], hi, Times[1][agoEnd], lo, Brushes.Transparent, area, 8);
		}

		private void PlayAlert()
		{
			try
			{
				string userDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds");
				string installDir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
				string path = Kat34ScalperSound.ResolvePath(userDir, installDir, AlertSound);
				if (path != null) PlaySound(path);
			}
			catch { }
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

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 1, GroupName = "1. KatA1 — EmaZone30s")]
		public int HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Timeframe (seconds)", Order = 2, GroupName = "1. KatA1 — EmaZone30s")]
		public int PeriodSeconds { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 8 above EMA 34", Order = 3, GroupName = "1. KatA1 — EmaZone30s")]
		public bool CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 above EMA 89", Order = 4, GroupName = "1. KatA1 — EmaZone30s")]
		public bool CondEma34Above89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 89 above EMA 144", Order = 5, GroupName = "1. KatA1 — EmaZone30s")]
		public bool CondEma89Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 144 above EMA 200", Order = 6, GroupName = "1. KatA1 — EmaZone30s")]
		public bool CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 slope angle", Order = 7, GroupName = "1. KatA1 — EmaZone30s")]
		public bool AngleEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Min Angle (deg)", Order = 8, GroupName = "1. KatA1 — EmaZone30s")]
		public double AngleMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Break Bars", Order = 9, GroupName = "1. KatA1 — EmaZone30s")]
		public int BreakBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ATR Period", Order = 10, GroupName = "1. KatA1 — EmaZone30s")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width (px)", Order = 11, GroupName = "1. KatA1 — EmaZone30s")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "LONG Line Color", Order = 12, GroupName = "1. KatA1 — EmaZone30s")]
		[XmlIgnore]
		public Color LongLineColor { get; set; }

		[Browsable(false)]
		public string LongLineColorSerializable
		{
			get { return LongLineColor.ToString(); }
			set { LongLineColor = ParseColor(value, Colors.DarkGreen); }
		}

		[NinjaScriptProperty]
		[Display(Name = "SHORT Line Color", Order = 13, GroupName = "1. KatA1 — EmaZone30s")]
		[XmlIgnore]
		public Color ShortLineColor { get; set; }

		[Browsable(false)]
		public string ShortLineColorSerializable
		{
			get { return ShortLineColor.ToString(); }
			set { ShortLineColor = ParseColor(value, Colors.DarkRed); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA34 zone TF1", Order = 14, GroupName = "1. KatA1 — EmaZone30s")]
		public KatEmaZoneTf EmaZoneTf1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA34 zone TF2", Order = 15, GroupName = "1. KatA1 — EmaZone30s")]
		public KatEmaZoneTf EmaZoneTf2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA34 zone TF3", Order = 16, GroupName = "1. KatA1 — EmaZone30s")]
		public KatEmaZoneTf EmaZoneTf3 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Sound", Order = 17, GroupName = "1. KatA1 — EmaZone30s")]
		[TypeConverter(typeof(Kat34ScalperSoundConverter))]
		public string AlertSound { get; set; }
		#endregion
	}
}
