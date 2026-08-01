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
}
