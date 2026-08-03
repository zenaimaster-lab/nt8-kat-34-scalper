using Kat34Scalper;
using Xunit;

namespace Kat34Scalper.Tests;

public class Kat34ScalperLogicTests
{
	// --- A1 sequence: pullback from beyond ema34 -> ema89 touch -> U-turn close through ema34 ---
	// Sell trend: ema34 below ema89 (e.g. 100.5 / 101.5). Buy trend mirrored (100.5 / 99.5).

	[Fact]
	public void Sell_Breakdown_FullSequence_FiresOnUturnClose()
	{
		var s = new KatA1State();
		// bar 1: close below ema34 -> armed
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s));
		// bar 2: cross UP through ema34 (close basis) -> sequence starts, no touch yet
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.0, 100.2, 101.0, 100.5, 101.5, s));
		// bar 3: high touches ema89, still closing above ema34
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.0, 100.8, 101.2, 100.5, 101.5, s));
		Assert.True(s.Touched89);
		// bar 4: U-turn close back below ema34 -> Breakdown fires immediately
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.8, 99.9, 100.5, 101.5, s);
		Assert.Equal(KatSignalKind.Sell, signal);
		Assert.Equal(99.8, s.C1);
		Assert.Equal(99.8, s.C2);
	}

	[Fact]
	public void Sell_RequiresPullbackFromBelowEma34_NoArmNoSignal()
	{
		var s = new KatA1State();
		// price was never below ema34: a touch of ema89 from ABOVE followed by a close below ema34 is NOT a setup
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 103.0, 101.0, 102.0, 100.5, 101.5, s));
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.5, 101.2, 102.2, 100.5, 101.5, s));
		// this bar only arms (close < ema34) — it must NOT fire, the pullback never happened
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.6, 99.6, 99.8, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
	}

	[Fact]
	public void Sell_WickAboveEma34_DoesNotCountAsCross()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		// wick pokes above ema34 but close stays below -> still armed, sequence not started
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.5, 99.8, 100.4, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
		// real cross on close basis
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.0, 100.2, 101.0, 100.5, 101.5, s));
		Assert.Equal(2, s.Phase);
	}

	[Fact]
	public void Sell_ExpiresAfterMaxSequenceBars()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.2, 101.5, 100.5, 101.5, s)); // cross+touch, seq=1
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.8, 101.4, 100.5, 101.5, s)); // seq=2
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.8, 101.4, 100.5, 101.5, s)); // seq=3
		// seq would be 4 > 3 -> expired and reset (close > ema34 -> no re-arm)
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.8, 101.4, 100.5, 101.5, s));
		Assert.Equal(0, s.Phase);
		// late U-turn close below ema34: only re-arms, must NOT fire on the stale touch
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 100.8, 99.8, 99.9, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
	}

	[Fact]
	public void Sell_UturnOnLastAllowedBar_StillFires()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 2, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 2, true, 102.0, 100.2, 101.5, 100.5, 101.5, s)); // cross+touch, seq=1
		// seq=2 == max -> still alive, U-turn fires
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Sell, 2, true, 100.8, 99.8, 99.9, 100.5, 101.5, s);
		Assert.Equal(KatSignalKind.Sell, signal);
	}

	[Fact]
	public void Sell_FailedPullback_ReArmsWithoutStaleTouch()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		// cross up but high stays below ema89 (no touch), then close back below ema34 -> failed pullback, rearmed
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.2, 100.2, 101.0, 100.5, 101.5, s));
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.7, 99.9, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
		Assert.False(s.Touched89);
		// new pullback: cross + touch + U-turn -> fires
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.0, 100.2, 101.5, 100.5, 101.5, s));
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.8, 99.9, 100.5, 101.5, s);
		Assert.Equal(KatSignalKind.Sell, signal);
	}

	[Fact]
	public void Sell_TrendLoss_ResetsSequence()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.0, 100.2, 101.5, 100.5, 101.5, s)); // cross+touch
		// trend flips (ema34 no longer below ema89) -> full reset
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, false, 100.8, 99.8, 99.9, 100.5, 101.5, s));
		Assert.Equal(0, s.Phase);
		Assert.False(s.Touched89);
		// U-turn-like bar after reset: no fire
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.8, 99.9, 100.5, 101.5, s));
	}

	[Fact]
	public void Buy_Breakdown_FullSequence_FiresOnUturnClose()
	{
		var s = new KatA1State();
		// uptrend: ema34 100.5 above ema89 99.5
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 101.8, 101.0, 101.5, 100.5, 99.5, s)); // arm above ema34
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.8, 99.8, 100.0, 100.5, 99.5, s)); // cross DOWN through ema34
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.2, 99.4, 99.9, 100.5, 99.5, s)); // low touches ema89
		Assert.True(s.Touched89);
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.9, 99.9, 100.8, 100.5, 99.5, s); // U-turn close above ema34
		Assert.Equal(KatSignalKind.Buy, signal);
		Assert.Equal(100.9, s.C1);
	}

	[Fact]
	public void Buy_ExpiresAfterMaxSequenceBars()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 101.8, 101.0, 101.5, 100.5, 99.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.8, 99.4, 99.9, 100.5, 99.5, s)); // cross+touch, seq=1
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.2, 99.6, 100.0, 100.5, 99.5, s)); // seq=2
		// seq=3 > 2 -> expired; this bar closes above ema34 so it only rearms (phase 1)
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.8, 99.6, 100.6, 100.5, 99.5, s));
		Assert.Equal(1, s.Phase);
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.9, 100.1, 100.8, 100.5, 99.5, s)); // no fire on stale touch
	}

	[Fact]
	public void Buy_RequiresPullbackFromAboveEma34_NoArmNoSignal()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 99.0, 98.0, 98.5, 100.5, 99.5, s)); // below both, no arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.9, 99.8, 100.6, 100.5, 99.5, s)); // arm (close > ema34)
		Assert.Equal(1, s.Phase);
	}

	// --- A0 EMA-ribbon fan ---
	private static readonly double[] BuyFanNow  = { 110, 108, 106, 104, 102, 100, 98 };  // 9>21>34>55>89>144>200
	private static readonly double[] BuyFanPrev = { 106, 105, 104, 103, 102, 101, 100 }; // narrower spread (10 vs 12)

	[Fact]
	public void Fan_BuyOrderedSpreadingWide_ReturnsPlus1()
	{
		Assert.Equal(1, Kat34ScalperLogic.FanDirection(BuyFanNow, BuyFanPrev, 20, 0.25)); // spread 12 >= 5
	}

	[Fact]
	public void Fan_SellOrderedSpreadingWide_ReturnsMinus1()
	{
		double[] now  = { 98, 100, 102, 104, 106, 108, 110 };
		double[] prev = { 100, 101, 102, 103, 104, 105, 106 };
		Assert.Equal(-1, Kat34ScalperLogic.FanDirection(now, prev, 20, 0.25));
	}

	[Fact]
	public void Fan_Unordered_ReturnsZero()
	{
		double[] messy = { 110, 108, 112, 104, 102, 100, 98 }; // 34 above 21
		Assert.Equal(0, Kat34ScalperLogic.FanDirection(messy, BuyFanPrev, 20, 0.25));
	}

	[Fact]
	public void Fan_NotSpreading_ReturnsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.FanDirection(BuyFanNow, BuyFanNow, 20, 0.25)); // same spread
	}

	[Fact]
	public void Fan_TooNarrow_ReturnsZero()
	{
		double[] now  = { 101, 100.9, 100.8, 100.7, 100.6, 100.5, 100 }; // spread 1 < 5
		double[] prev = { 100.5, 100.4, 100.3, 100.2, 100.1, 100.05, 100 };
		Assert.Equal(0, Kat34ScalperLogic.FanDirection(now, prev, 20, 0.25));
	}

	[Fact]
	public void Fan_NullOrShort_ReturnsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.FanDirection(null, BuyFanPrev, 20, 0.25));
		Assert.Equal(0, Kat34ScalperLogic.FanDirection(new double[] { 1 }, new double[] { 1 }, 20, 0.25));
	}

	// --- Market filter ---
	[Fact]
	public void Market_AdxTooLow_Blocked()
	{
		Assert.False(Kat34ScalperLogic.PassMarketFilter(15, 20, 1000, 500, 1.0));
	}

	[Fact]
	public void Market_VolumeTooLow_Blocked()
	{
		Assert.False(Kat34ScalperLogic.PassMarketFilter(25, 20, 400, 500, 1.0));
	}

	[Fact]
	public void Market_BothOk_Passes()
	{
		Assert.True(Kat34ScalperLogic.PassMarketFilter(25, 20, 1000, 500, 1.0));
	}

	[Fact]
	public void Market_ZeroVolSma_DisablesVolumeLeg()
	{
		Assert.True(Kat34ScalperLogic.PassMarketFilter(25, 20, 0, 0, 1.0));
	}

	// --- Time window ---
	[Fact]
	public void Time_InsideWindow_True()
	{
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(10, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
	}

	[Fact]
	public void Time_OutsideWindow_False()
	{
		Assert.False(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(18, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
	}

	[Fact]
	public void Time_OvernightWindow_WrapsMidnight()
	{
		var start = new TimeSpan(22, 0, 0);
		var end = new TimeSpan(6, 0, 0);
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(23, 30, 0), start, end));
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(2, 0, 0), start, end));
		Assert.False(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(12, 0, 0), start, end));
	}

	[Fact]
	public void Time_StartEqualsEnd_Disabled_AlwaysTrue()
	{
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(3, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(8, 0, 0)));
	}

	[Fact]
	public void Time_StartInclusive_EndExclusive_Boundaries()
	{
		var start = new TimeSpan(8, 0, 0);
		var end = new TimeSpan(17, 0, 0);
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(start, start, end));
		Assert.False(Kat34ScalperLogic.IsInTimeWindow(end, start, end));
	}

	// --- Bot entry order type (stop only on the valid side of market, else limit) ---
	[Fact]
	public void BotEntry_SellStopBelowMarket_UsesStop_AboveMarket_UsesLimit()
	{
		Assert.True(Kat34ScalperLogic.UseStopOrder(false, 99.5, 100.0));  // sell stop below current -> valid stop
		Assert.False(Kat34ScalperLogic.UseStopOrder(false, 100.5, 100.0)); // price ran past -> limit
	}

	[Fact]
	public void BotEntry_BuyStopAboveMarket_UsesStop_BelowMarket_UsesLimit()
	{
		Assert.True(Kat34ScalperLogic.UseStopOrder(true, 100.5, 100.0));  // buy stop above current -> valid stop
		Assert.False(Kat34ScalperLogic.UseStopOrder(true, 99.5, 100.0));   // price ran past -> limit
	}

	[Fact]
	public void BotEntry_TriggerEqualsMarket_UsesLimit_BothSides()
	{
		Assert.False(Kat34ScalperLogic.UseStopOrder(false, 100.0, 100.0)); // sell stop must be below market
		Assert.False(Kat34ScalperLogic.UseStopOrder(true, 100.0, 100.0));  // buy stop must be above market
	}

	// --- EffectiveEntry ---
	[Fact]
	public void EffectiveEntry_Sell_TakesHigherStop()
	{
		// sell: below candidate lows; c2 higher -> better
		Assert.Equal(99.9 - 0.25, Kat34ScalperLogic.EffectiveEntry(false, 99.5, 99.9, 1, 0.25));
	}

	[Fact]
	public void EffectiveEntry_Buy_TakesLowerStop()
	{
		// buy: above candidate highs; c2 lower -> better
		Assert.Equal(100.1 + 0.25, Kat34ScalperLogic.EffectiveEntry(true, 100.5, 100.1, 1, 0.25));
	}

	// --- ATM template parser ---
	private const string SampleAtmXml =
		"<AtmStrategy><Brackets><Bracket>" +
		"<StopLoss>60</StopLoss><Target>120</Target>" +
		"<StopStrategy><AutoBreakEvenProfitTrigger>30</AutoBreakEvenProfitTrigger>" +
		"<AutoTrailSteps>" +
		"<AutoTrailStep><ProfitTrigger>45</ProfitTrigger></AutoTrailStep>" +
		"<AutoTrailStep><ProfitTrigger>80</ProfitTrigger></AutoTrailStep>" +
		"</AutoTrailSteps></StopStrategy>" +
		"</Bracket></Brackets></AtmStrategy>";

	[Fact]
	public void Atm_FullTemplate_ParsesAllLevels()
	{
		var d = Kat34ScalperAtmParser.ParseXml(SampleAtmXml);
		Assert.Equal(60, d.StopLoss);
		Assert.Equal(120, d.Target);
		Assert.Equal(30, d.BETrigger);
		Assert.Equal(45, d.SL1Trigger);
		Assert.Equal(80, d.SL2Trigger);
	}

	[Fact]
	public void Atm_MissingNodes_StayZero()
	{
		var d = Kat34ScalperAtmParser.ParseXml("<AtmStrategy><Brackets><Bracket><StopLoss>40</StopLoss></Bracket></Brackets></AtmStrategy>");
		Assert.Equal(40, d.StopLoss);
		Assert.Equal(0, d.Target);
		Assert.Equal(0, d.BETrigger);
		Assert.Equal(0, d.SL1Trigger);
	}

	[Fact]
	public void Atm_GarbageOrEmpty_ReturnsZeros()
	{
		Assert.Equal(0, Kat34ScalperAtmParser.ParseXml("not xml at all").StopLoss);
		Assert.Equal(0, Kat34ScalperAtmParser.ParseXml("").Target);
		Assert.Equal(0, Kat34ScalperAtmParser.ParseFile(@"C:\no\such\file.xml").StopLoss);
	}

	[Fact]
	public void Atm_ParseQuantity_ReadsEntryQuantityOrSumOfBrackets()
	{
		var xml1 = "<AtmStrategy><EntryQuantity>2</EntryQuantity><Brackets><Bracket><Quantity>2</Quantity></Bracket></Brackets></AtmStrategy>";
		Assert.Equal(2, Kat34ScalperAtmParser.ParseXml(xml1).Quantity);

		var xml2 = "<AtmStrategy><Brackets><Bracket><Quantity>1</Quantity></Bracket><Bracket><Quantity>2</Quantity></Bracket></Brackets></AtmStrategy>";
		Assert.Equal(3, Kat34ScalperAtmParser.ParseXml(xml2).Quantity);
	}

	// --- A2 (34+8+Bounce): pending stop entry at the touch candle's extreme ---
	// ema34 = 100.5. Buy: touch = low <= 100.5 with close > 100.5; entry trigger = RefExtreme + offset*tick.
	// Sell mirrors: touch = high >= 100.5 with close < 100.5; trigger = RefExtreme - offset*tick.

	[Fact]
	public void A2_Buy_TouchCloseAbove_NewEntryAtHigh()
	{
		var s = new KatA2State();
		// running above ema34, no touch yet -> nothing
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.5, 100.8, 101.2, 100.5, 4, 0.25, s));
		// wick dips to ema34, closes above -> NewEntry at the high
		Assert.Equal(KatA2Action.NewEntry, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s));
		Assert.True(s.Active);
		Assert.Equal(101.0, s.RefExtreme);
	}

	[Fact]
	public void A2_Buy_LowerHighTouch_MigratesEntryDown()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s); // NewEntry @101.0
		// next touch candle with a lower high -> migrate down
		Assert.Equal(KatA2Action.Migrate, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 100.8, 100.3, 100.7, 100.5, 4, 0.25, s));
		Assert.Equal(100.8, s.RefExtreme);
	}

	[Fact]
	public void A2_Buy_HigherHighBelowTrigger_KeepsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s); // trigger = 101.0 + 1.0 = 102.0
		// touch candle with a HIGHER high (101.5) but still below the trigger: not filled, not better -> no change
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.5, 100.4, 100.9, 100.5, 4, 0.25, s));
		Assert.True(s.Active);
		Assert.Equal(101.0, s.RefExtreme);
	}

	[Fact]
	public void A2_Buy_ReachingTrigger_Filled()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s); // trigger 102.0
		Assert.Equal(KatA2Action.Filled, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 102.1, 100.9, 101.8, 100.5, 4, 0.25, s));
		Assert.False(s.Active); // setup done — next touch starts fresh
	}

	[Fact]
	public void A2_Buy_CloseBelowEma34_CancelsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s);
		Assert.Equal(KatA2Action.Cancel, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 100.6, 100.0, 100.2, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
		// no entry active and a close below ema34 -> no signal at all (touch candle must CLOSE above)
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 100.6, 100.3, 100.4, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
	}

	[Fact]
	public void A2_Buy_TrendLoss_CancelsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s);
		Assert.Equal(KatA2Action.Cancel, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, false, 101.2, 100.7, 101.0, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
		// trend dead + no entry -> silent
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, false, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s));
	}

	[Fact]
	public void A2_Sell_TouchCloseBelow_NewEntryAtLow_ThenMigratesUp()
	{
		var s = new KatA2State();
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.2, 99.4, 99.8, 100.5, 4, 0.25, s)); // no touch
		Assert.Equal(KatA2Action.NewEntry, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.6, 99.8, 100.2, 100.5, 4, 0.25, s));
		Assert.Equal(99.8, s.RefExtreme);
		// next touch candle with a higher low -> migrate the sell stop up
		Assert.Equal(KatA2Action.Migrate, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.7, 99.9, 100.1, 100.5, 4, 0.25, s));
		Assert.Equal(99.9, s.RefExtreme);
	}

	[Fact]
	public void A2_Sell_ReachingTrigger_Filled()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.6, 99.8, 100.2, 100.5, 4, 0.25, s); // trigger = 99.8 - 1.0 = 98.8
		Assert.Equal(KatA2Action.Filled, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.1, 98.7, 99.0, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
	}

	[Fact]
	public void A2_Sell_CloseAboveEma34_CancelsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.6, 99.8, 100.2, 100.5, 4, 0.25, s);
		Assert.Equal(KatA2Action.Cancel, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.9, 100.3, 100.7, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
		// touch candle that closes ABOVE ema34 never places a sell entry
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.7, 100.1, 100.6, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
	}

	// --- A2 backtest: full synthetic bar series replayed through the state machine ---
	// ema34 flat at 100.0, offset 4 ticks × 0.25 = 1.0. Each bar = (high, low, close, trendOk).

	private static List<KatA2Action> ReplayA2(KatSignalKind kind, double ema34, double[][] bars)
	{
		var s = new KatA2State();
		var actions = new List<KatA2Action>();
		foreach (double[] b in bars)
			actions.Add(Kat34ScalperLogic.UpdateA2(kind, b[3] > 0.5, b[0], b[1], b[2], ema34, 4, 0.25, s));
		return actions;
	}

	[Fact]
	public void A2_Backtest_BuyRun_Touch_Migrate_Fill()
	{
		// 5 bars running above ema34 (no touch), pullback touches ema34 (NewEntry @101.0, trigger 102.0),
		// next touch with a lower high (Migrate to 100.7, trigger 101.7), rally reaches it (Filled).
		var bars = new[]
		{
			new[] { 101.5, 100.8, 101.2, 1.0 },
			new[] { 101.6, 100.9, 101.3, 1.0 },
			new[] { 101.4, 100.7, 101.1, 1.0 },
			new[] { 101.5, 100.8, 101.2, 1.0 },
			new[] { 101.3, 100.6, 101.0, 1.0 },
			new[] { 101.0,  99.9, 100.3, 1.0 }, // touch + close above -> NewEntry
			new[] { 100.7,  99.8, 100.2, 1.0 }, // touch, lower high -> Migrate
			new[] { 101.8, 100.5, 101.5, 1.0 }, // high 101.8 >= trigger 101.7 -> Filled
		};
		var actions = ReplayA2(KatSignalKind.Buy, 100.0, bars);
		Assert.Equal(
			new[] { KatA2Action.None, KatA2Action.None, KatA2Action.None, KatA2Action.None, KatA2Action.None,
				KatA2Action.NewEntry, KatA2Action.Migrate, KatA2Action.Filled },
			actions);
	}

	[Fact]
	public void A2_Backtest_BuyTouch_CloseBelow_Cancels()
	{
		// NewEntry then a close below ema34 kills the pending entry; later re-touch starts a fresh one.
		var bars = new[]
		{
			new[] { 101.0,  99.9, 100.3, 1.0 }, // NewEntry @101.0
			new[] { 100.4,  99.0,  99.5, 1.0 }, // close < 100 -> Cancel
			new[] { 100.8,  99.9, 100.4, 1.0 }, // touch + close above -> NewEntry again
			new[] { 101.5, 100.2, 101.2, 1.0 }, // trigger = 100.8+1.0=101.8 not reached -> None
			new[] { 102.0, 100.9, 101.9, 1.0 }, // high 102.0 >= 101.8 -> Filled
		};
		var actions = ReplayA2(KatSignalKind.Buy, 100.0, bars);
		Assert.Equal(
			new[] { KatA2Action.NewEntry, KatA2Action.Cancel, KatA2Action.NewEntry, KatA2Action.None, KatA2Action.Filled },
			actions);
	}

	[Fact]
	public void A2_Backtest_SellRun_TrendLossCancels_ThenSellFills()
	{
		// Sell entry placed, trend stack breaks (Cancel); trend returns, new touch, migrate up, fill.
		var bars = new[]
		{
			new[] { 99.2, 98.5, 99.0, 1.0 },  // running below, no touch
			new[] { 100.1, 99.0, 99.7, 1.0 }, // touch + close below -> NewEntry @99.0 (trigger 98.0)
			new[] { 100.3, 99.5, 99.8, 0.0 }, // trend lost -> Cancel
			new[] { 100.2, 99.1, 99.6, 1.0 }, // trend back, touch -> NewEntry @99.1 (trigger 98.1)
			new[] { 100.4, 99.3, 99.5, 1.0 }, // touch, higher low -> Migrate to 99.3 (trigger 98.3)
			new[] { 99.8, 98.2, 98.5, 1.0 },  // low 98.2 <= 98.3 -> Filled
		};
		var actions = ReplayA2(KatSignalKind.Sell, 100.0, bars);
		Assert.Equal(
			new[] { KatA2Action.None, KatA2Action.NewEntry, KatA2Action.Cancel,
				KatA2Action.NewEntry, KatA2Action.Migrate, KatA2Action.Filled },
			actions);
	}

	// --- A3 (8cross34): EMA cross direction ---
	[Fact]
	public void Cross_CrossUp_ReturnsPlus1()
	{
		Assert.Equal(1, Kat34ScalperLogic.CrossDirection(99.9, 100.0, 100.1, 100.0));
	}

	[Fact]
	public void Cross_CrossDown_ReturnsMinus1()
	{
		Assert.Equal(-1, Kat34ScalperLogic.CrossDirection(100.1, 100.0, 99.9, 100.0));
	}

	[Fact]
	public void Cross_NoCross_ReturnsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.CrossDirection(100.1, 100.0, 100.2, 100.0)); // stays above
		Assert.Equal(0, Kat34ScalperLogic.CrossDirection(99.9, 100.0, 99.8, 100.0));   // stays below
	}

	[Fact]
	public void Cross_TouchOnPreviousBar_CountsAsOldSide()
	{
		// prev fast == slow (touch, not yet crossed) -> a move above is still a cross up
		Assert.Equal(1, Kat34ScalperLogic.CrossDirection(100.0, 100.0, 100.1, 100.0));
		Assert.Equal(-1, Kat34ScalperLogic.CrossDirection(100.0, 100.0, 99.9, 100.0));
	}

	// --- ATM quick-set button labels ---
	[Fact]
	public void AtmSetName_WithinLimit_Kept()
	{
		Assert.Equal("A", Kat34ScalperLogic.NormalizeAtmSetName("A", "F"));
		Assert.Equal("ABC", Kat34ScalperLogic.NormalizeAtmSetName("ABC", "F"));
		Assert.Equal("1x", Kat34ScalperLogic.NormalizeAtmSetName("1x", "F"));
	}

	[Fact]
	public void AtmSetName_OverThreeChars_Truncated()
	{
		Assert.Equal("SCA", Kat34ScalperLogic.NormalizeAtmSetName("SCALP", "F"));
		Assert.Equal("ABC", Kat34ScalperLogic.NormalizeAtmSetName("ABCDE", "F"));
	}

	[Fact]
	public void AtmSetName_EmptyOrWhitespace_FallsBack()
	{
		Assert.Equal("B", Kat34ScalperLogic.NormalizeAtmSetName("", "B"));
		Assert.Equal("C", Kat34ScalperLogic.NormalizeAtmSetName("   ", "C"));
		Assert.Equal("D", Kat34ScalperLogic.NormalizeAtmSetName(null, "D"));
	}

	[Fact]
	public void AtmSetName_SurroundingWhitespace_Trimmed()
	{
		Assert.Equal("TP", Kat34ScalperLogic.NormalizeAtmSetName("  TP ", "F"));
		Assert.Equal("ABC", Kat34ScalperLogic.NormalizeAtmSetName(" ABCD ", "F"));
	}

	// --- A4 (OCO) price prioritization tests ---
	[Fact]
	public void A4_SelectBuyPrice_PrioritizesLowestBuy()
	{
		Assert.Equal(100.0, Kat34ScalperLogic.SelectA4BuyPrice(0, 100.0));
		Assert.Equal(98.0, Kat34ScalperLogic.SelectA4BuyPrice(100.0, 98.0));
		Assert.Equal(98.0, Kat34ScalperLogic.SelectA4BuyPrice(98.0, 102.0));
	}

	[Fact]
	public void A4_SelectSellPrice_PrioritizesHighestSell()
	{
		Assert.Equal(100.0, Kat34ScalperLogic.SelectA4SellPrice(0, 100.0));
		Assert.Equal(102.0, Kat34ScalperLogic.SelectA4SellPrice(100.0, 102.0));
		Assert.Equal(102.0, Kat34ScalperLogic.SelectA4SellPrice(102.0, 98.0));
	}

	// --- Daily Risk (Max DD & Max Profit) tests ---
	[Fact]
	public void EvaluateDailyRiskBreach_OffToggles_NeverBreach()
	{
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, false, 1000.0, -50000.0, out _));
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, false, 1000.0, 99999.0, out _));
	}

	[Fact]
	public void EvaluateDailyRiskBreach_MaxDDBreach_WhenEnabledAndBeyondLimit()
	{
		Assert.True(Kat34ScalperLogic.EvaluateDailyRiskBreach(true, 500.0, false, 1000.0, -500.0, out string reason));
		Assert.Contains("Max DD", reason);
		Assert.True(Kat34ScalperLogic.EvaluateDailyRiskBreach(true, 500.0, false, 1000.0, -750.25, out _));
	}

	[Fact]
	public void EvaluateDailyRiskBreach_MaxProfitBreach_WhenEnabledAndReached()
	{
		Assert.True(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, true, 1000.0, 1000.0, out string reason));
		Assert.Contains("Max Profit", reason);
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, true, 1000.0, 999.99, out _));
	}

	[Fact]
	public void ShouldCaptureSessionBaseline_Behavior()
	{
		DateTime session = new DateTime(2026, 7, 30, 22, 0, 0, DateTimeKind.Utc);
		Assert.False(Kat34ScalperLogic.ShouldCaptureSessionBaseline(false, session, DateTime.MinValue, false));
		Assert.True(Kat34ScalperLogic.ShouldCaptureSessionBaseline(false, session, DateTime.MinValue, true));
	}
}

