using Kat8934;
using Xunit;

namespace Kat8934.Tests;

public class Kat8934LogicTests
{
	private static (bool sellTouched, bool sellUturned, KatSignalKind? signal) StepSell(
		KatTriggerMode mode, bool trendOk, double high, double low, double close, double ema34, double ema89)
	{
		bool touched = false, uturned = false;
		var signal = Kat8934Logic.Update(KatSignalKind.Sell, mode, trendOk, high, low, close, ema34, ema89, ref touched, ref uturned);
		return (touched, uturned, signal);
	}

	[Fact]
	public void Sell_NoTouch_NoSignal()
	{
		var s = StepSell(KatTriggerMode.Breakdown, true, 100, 99, 100, 101, 102);
		Assert.Null(s.signal);
		Assert.False(s.sellTouched);
	}

	[Fact]
	public void Sell_BreakdownMode_FiresOnUturnCloseBelowEma34()
	{
		bool touched = false, uturned = false;
		// bar 1: high touches EMA89
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.Breakdown, true, 102, 100, 101, 101, 101.5, ref touched, ref uturned));
		Assert.True(touched);
		// bar 2: U-turn closes below EMA34 -> fires immediately
		var signal = Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.Breakdown, true, 101, 99.5, 99.8, 100.5, 101.5, ref touched, ref uturned);
		Assert.Equal(KatSignalKind.Sell, signal);
		// state reset after fire
		Assert.False(touched);
		Assert.False(uturned);
	}

	[Fact]
	public void Sell_RetestMode_FiresOnlyWhenCloseBackAboveEma34()
	{
		bool touched = false, uturned = false;
		// bar 1: touch EMA89
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 102, 100, 101, 101, 101.5, ref touched, ref uturned));
		// bar 2: U-turn close below EMA34 -> no signal yet (retest mode)
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101, 99.5, 99.8, 100.5, 101.5, ref touched, ref uturned));
		Assert.True(uturned);
		// bar 3: retest bounce closes back above EMA34 -> Sell
		var signal = Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101.5, 99.9, 101.2, 100.5, 101.5, ref touched, ref uturned);
		Assert.Equal(KatSignalKind.Sell, signal);
		Assert.False(touched);
		Assert.False(uturned);
	}

	[Fact]
	public void Sell_NoTrend_NoSignal_AndResetsState()
	{
		bool touched = true, uturned = true; // stale state from earlier
		var signal = Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.Breakdown, false, 105, 104, 104.5, 103, 101, ref touched, ref uturned);
		Assert.Null(signal);
		Assert.False(touched);
		Assert.False(uturned);
	}

	[Fact]
	public void Buy_BreakdownMode_FiresOnUturnCloseAboveEma34()
	{
		bool touched = false, uturned = false;
		// bar 1: low touches EMA89
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.Breakdown, true, 100, 98, 99, 101, 98.5, ref touched, ref uturned));
		// bar 2: U-turn closes above EMA34 -> Buy
		var signal = Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.Breakdown, true, 100.5, 99.8, 101.2, 100.5, 98.5, ref touched, ref uturned);
		Assert.Equal(KatSignalKind.Buy, signal);
	}

	[Fact]
	public void Buy_RetestMode_FiresOnlyWhenCloseBackBelowEma34()
	{
		bool touched = false, uturned = false;
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.RetestBounce, true, 100, 98, 99, 101, 98.5, ref touched, ref uturned));
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.RetestBounce, true, 100.5, 99.8, 101.2, 100.5, 98.5, ref touched, ref uturned));
		Assert.True(uturned);
		var signal = Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.RetestBounce, true, 99.5, 99.0, 99.8, 100.5, 98.5, ref touched, ref uturned);
		Assert.Equal(KatSignalKind.Buy, signal);
		Assert.False(touched);
	}

	[Fact]
	public void Sell_OneBarTouchAndUturn_FiresImmediately_Breakdown()
	{
		bool touched = false, uturned = false;
		// single bar both touches EMA89 (high) and closes below EMA34
		var signal = Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.Breakdown, true, 103, 99, 99.5, 100.5, 101.5, ref touched, ref uturned);
		Assert.Equal(KatSignalKind.Sell, signal);
	}

	[Fact]
	public void RetestMode_KeepsWaitingWhilePriceStaysBelowEma34()
	{
		bool touched = false, uturned = false;
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 102, 100, 101, 101, 101.5, ref touched, ref uturned));
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101, 99.5, 99.8, 100.5, 101.5, ref touched, ref uturned));
		// more down bars below EMA34: still no signal
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 99.7, 99.0, 99.2, 100.5, 101.5, ref touched, ref uturned));
		Assert.Null(Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 99.6, 98.8, 99.0, 100.5, 101.5, ref touched, ref uturned));
		Assert.True(uturned);
	}

	[Fact]
	public void Buy_NoTrend_NoSignal()
	{
		bool touched = false, uturned = false;
		var signal = Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.Breakdown, false, 100, 98, 99, 101, 98.5, ref touched, ref uturned);
		Assert.Null(signal);
	}

	// --- A0 EMA-ribbon fan ---
	private static readonly double[] BuyFanNow  = { 110, 108, 106, 104, 102, 100, 98 };  // 9>21>34>55>89>144>200
	private static readonly double[] BuyFanPrev = { 106, 105, 104, 103, 102, 101, 100 }; // narrower spread (10 vs 12)

	[Fact]
	public void Fan_BuyOrderedSpreadingWide_ReturnsPlus1()
	{
		Assert.Equal(1, Kat8934Logic.FanDirection(BuyFanNow, BuyFanPrev, 20, 0.25)); // spread 12 >= 5
	}

	[Fact]
	public void Fan_SellOrderedSpreadingWide_ReturnsMinus1()
	{
		double[] now  = { 98, 100, 102, 104, 106, 108, 110 };
		double[] prev = { 100, 101, 102, 103, 104, 105, 106 };
		Assert.Equal(-1, Kat8934Logic.FanDirection(now, prev, 20, 0.25));
	}

	[Fact]
	public void Fan_Unordered_ReturnsZero()
	{
		double[] messy = { 110, 108, 112, 104, 102, 100, 98 }; // 34 above 21
		Assert.Equal(0, Kat8934Logic.FanDirection(messy, BuyFanPrev, 20, 0.25));
	}

	[Fact]
	public void Fan_NotSpreading_ReturnsZero()
	{
		Assert.Equal(0, Kat8934Logic.FanDirection(BuyFanNow, BuyFanNow, 20, 0.25)); // same spread
	}

	[Fact]
	public void Fan_TooNarrow_ReturnsZero()
	{
		double[] now  = { 101, 100.9, 100.8, 100.7, 100.6, 100.5, 100 }; // spread 1 < 5
		double[] prev = { 100.5, 100.4, 100.3, 100.2, 100.1, 100.05, 100 };
		Assert.Equal(0, Kat8934Logic.FanDirection(now, prev, 20, 0.25));
	}

	[Fact]
	public void Fan_NullOrShort_ReturnsZero()
	{
		Assert.Equal(0, Kat8934Logic.FanDirection(null, BuyFanPrev, 20, 0.25));
		Assert.Equal(0, Kat8934Logic.FanDirection(new double[] { 1 }, new double[] { 1 }, 20, 0.25));
	}

	// --- Market filter ---
	[Fact]
	public void Market_AdxTooLow_Blocked()
	{
		Assert.False(Kat8934Logic.PassMarketFilter(15, 20, 1000, 500, 1.0));
	}

	[Fact]
	public void Market_VolumeTooLow_Blocked()
	{
		Assert.False(Kat8934Logic.PassMarketFilter(25, 20, 400, 500, 1.0));
	}

	[Fact]
	public void Market_BothOk_Passes()
	{
		Assert.True(Kat8934Logic.PassMarketFilter(25, 20, 1000, 500, 1.0));
	}

	[Fact]
	public void Market_ZeroVolSma_DisablesVolumeLeg()
	{
		Assert.True(Kat8934Logic.PassMarketFilter(25, 20, 0, 0, 1.0));
	}

	// --- Time window ---
	[Fact]
	public void Time_InsideWindow_True()
	{
		Assert.True(Kat8934Logic.IsInTimeWindow(new TimeSpan(10, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
	}

	[Fact]
	public void Time_OutsideWindow_False()
	{
		Assert.False(Kat8934Logic.IsInTimeWindow(new TimeSpan(18, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
	}

	[Fact]
	public void Time_OvernightWindow_WrapsMidnight()
	{
		var start = new TimeSpan(22, 0, 0);
		var end = new TimeSpan(6, 0, 0);
		Assert.True(Kat8934Logic.IsInTimeWindow(new TimeSpan(23, 30, 0), start, end));
		Assert.True(Kat8934Logic.IsInTimeWindow(new TimeSpan(2, 0, 0), start, end));
		Assert.False(Kat8934Logic.IsInTimeWindow(new TimeSpan(12, 0, 0), start, end));
	}

	[Fact]
	public void Time_StartEqualsEnd_Disabled_AlwaysTrue()
	{
		Assert.True(Kat8934Logic.IsInTimeWindow(new TimeSpan(3, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(8, 0, 0)));
	}

	// --- A1 dual-entry candidates (C1 = U-turn bar, C2 = best later bar) ---
	[Fact]
	public void Sell_UturnBar_SetsC1AndC2ToItsLow()
	{
		bool touched = false, uturned = false;
		double c1 = 0, c2 = 0;
		// bar 1: touch ema89
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 102, 100, 101, 101, 101.5, ref touched, ref uturned, ref c1, ref c2);
		// bar 2: U-turn close below ema34, low 99.5
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101, 99.5, 99.8, 100.5, 101.5, ref touched, ref uturned, ref c1, ref c2);
		Assert.Equal(99.5, c1);
		Assert.Equal(99.5, c2);
	}

	[Fact]
	public void Sell_LaterBarWithHigherLow_UpdatesC2Only()
	{
		bool touched = false, uturned = false;
		double c1 = 0, c2 = 0;
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 102, 100, 101, 101, 101.5, ref touched, ref uturned, ref c1, ref c2);
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101, 99.5, 99.8, 100.5, 101.5, ref touched, ref uturned, ref c1, ref c2);
		// bar 3: still below ema34, higher low 99.9 -> better sell entry
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 100.4, 99.9, 100.1, 100.5, 101.5, ref touched, ref uturned, ref c1, ref c2);
		Assert.Equal(99.5, c1); // unchanged
		Assert.Equal(99.9, c2); // raised
	}

	[Fact]
	public void Sell_BarAboveEma34_DoesNotUpdateC2_ButFires()
	{
		bool touched = false, uturned = false;
		double c1 = 0, c2 = 0;
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 102, 100, 101, 101, 101.5, ref touched, ref uturned, ref c1, ref c2);
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101, 99.5, 99.8, 100.5, 101.5, ref touched, ref uturned, ref c1, ref c2);
		// retest bar closes back above ema34 -> signal; its low (100.2) must NOT become c2
		var signal = Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.RetestBounce, true, 101.5, 100.2, 101.2, 100.5, 101.5, ref touched, ref uturned, ref c1, ref c2);
		Assert.Equal(KatSignalKind.Sell, signal);
		Assert.Equal(99.5, c2);
	}

	[Fact]
	public void Buy_LaterBarWithLowerHigh_UpdatesC2Only()
	{
		bool touched = false, uturned = false;
		double c1 = 0, c2 = 0;
		Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.RetestBounce, true, 100, 98, 99, 101, 98.5, ref touched, ref uturned, ref c1, ref c2);
		Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.RetestBounce, true, 100.5, 99.8, 101.2, 100.5, 98.5, ref touched, ref uturned, ref c1, ref c2);
		Assert.Equal(100.5, c1);
		// bar 3: ema34 drifted down to 99.9 — close 100.0 still above it, lower high 100.1 -> better buy entry
		Kat8934Logic.Update(KatSignalKind.Buy, KatTriggerMode.RetestBounce, true, 100.1, 99.6, 100.0, 99.9, 98.5, ref touched, ref uturned, ref c1, ref c2);
		Assert.Equal(100.5, c1);
		Assert.Equal(100.1, c2);
	}

	[Fact]
	public void Candidates_ResetOnTrendLoss()
	{
		bool touched = true, uturned = true;
		double c1 = 99, c2 = 99;
		Kat8934Logic.Update(KatSignalKind.Sell, KatTriggerMode.Breakdown, false, 105, 104, 104.5, 103, 101, ref touched, ref uturned, ref c1, ref c2);
		Assert.Equal(0, c1);
		Assert.Equal(0, c2);
	}

	// --- EffectiveEntry ---
	[Fact]
	public void EffectiveEntry_Sell_TakesHigherStop()
	{
		// sell: below candidate lows; c2 higher -> better
		Assert.Equal(99.9 - 0.25, Kat8934Logic.EffectiveEntry(false, 99.5, 99.9, 1, 0.25));
	}

	[Fact]
	public void EffectiveEntry_Buy_TakesLowerStop()
	{
		// buy: above candidate highs; c2 lower -> better
		Assert.Equal(100.1 + 0.25, Kat8934Logic.EffectiveEntry(true, 100.5, 100.1, 1, 0.25));
	}
}
