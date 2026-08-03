/*
 * Kat34Scalper.Bot.cs — Bot module (partial class Kat34Scalper).
 * Semi-auto: trades only while the HUD BOT button is ON and Bot Enabled is set.
 * Receives the signal's reference extreme, converts it to the right order type
 * (stop on the valid side of market, limit when price already ran past it),
 * submits through the selected ATM template (SL/TP/trailing brackets), migrates
 * the pending entry to a better extreme, cancels on trend flip / BOT OFF.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- Bot module state ---
		private volatile bool cachedBotOn;
		private volatile string cachedBotAtm = "";
		private volatile string cachedBotAccountName = "";
		private volatile bool cachedIsDailyMaxDD;
		private double cachedDailyMaxDD = 500;
		private volatile bool cachedIsDailyMaxProfit;
		private double cachedDailyMaxProfit = 1000;
		private volatile int cachedBotBufferTicks = 2;

		private DateTime lastSessionStartUtc;
		private double sessionStartRealizedPnL;
		private bool isSessionStartCaptured;
		private int dailyRiskFlattened;

		private Order pendingOrder;
		private Account pendingOrderAccount; // account that owns pendingOrder (cancel must target owner account)
		private string pendingOrderOwner = "A1"; // signal module that submitted pendingOrder ("A1"/"A2" — per-signal cancel)
		private int pendingOffsetTicks = 1; // entry offset of the owning signal (migration re-place must reuse it)
		private bool pendingIsBuy;
		private double pendingEntryPrice; // last submitted entry price (limit OR stop — Order.StopPrice is 0 on limits)
		private double pendingBestRef;    // best extreme used for migration (sell: highest qualifying low / buy: lowest high)
		private double pendingMigrateRef; // better extreme found; new order placed once the cancelled one is terminal
		private volatile bool pendingMigrate;
		private string atmLevelsName = "\0"; // never matches a real template name — forces first parse
		private Kat34ScalperAtmData atmLevels;
		private readonly System.Collections.Generic.Dictionary<string, bool> signalInTradeMap = new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		#region Daily Risk Protection
		private double CalculateDailyPnL()
		{
			Account acc = ResolveBotAccount();
			if (acc == null) return 0;

			DateTime currentSessionStartUtc = Kat34ScalperLogic.GetNySessionStartUtc(DateTime.UtcNow);
			double currentRealizedPnL = 0;
			bool realizedReadOk;
			try
			{
				currentRealizedPnL = acc.Get(AccountItem.GrossRealizedProfitLoss, Currency.UsDollar);
				realizedReadOk = true;
			}
			catch
			{
				realizedReadOk = false;
			}

			if (Kat34ScalperLogic.ShouldCaptureSessionBaseline(isSessionStartCaptured, currentSessionStartUtc, lastSessionStartUtc, realizedReadOk))
			{
				lastSessionStartUtc = currentSessionStartUtc;
				sessionStartRealizedPnL = currentRealizedPnL;
				isSessionStartCaptured = true;
			}

			double dailyRealized = realizedReadOk ? currentRealizedPnL - sessionStartRealizedPnL : 0;
			double dailyUnrealized = 0;
			try
			{
				dailyUnrealized = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
			}
			catch { }

			return dailyRealized + dailyUnrealized;
		}

		private bool IsDailyRiskBreached(out string breachReason)
		{
			breachReason = string.Empty;
			Account acc = ResolveBotAccount();
			if (acc == null) return false;

			double dailyPnL = CalculateDailyPnL();

			return Kat34ScalperLogic.EvaluateDailyRiskBreach(
				cachedIsDailyMaxDD, cachedDailyMaxDD,
				cachedIsDailyMaxProfit, cachedDailyMaxProfit,
				dailyPnL, out breachReason);
		}

		private void EvaluateDailyRiskLimits()
		{
			Account acc = ResolveBotAccount();
			if (acc == null) return;

			if (IsDailyRiskBreached(out string breachReason))
			{
				if (System.Threading.Interlocked.CompareExchange(ref dailyRiskFlattened, 1, 0) == 0)
				{
					Print(string.Format("[Kat34Scalper] EMERGENCY CANCEL triggered by Daily Risk Protection: {0}", breachReason));
					ShowHudStatus(breachReason, Brushes.OrangeRed);
					CancelPendingBotOrder(breachReason);
				}
			}
			else
			{
				System.Threading.Interlocked.Exchange(ref dailyRiskFlattened, 0);
			}
		}
		#endregion

		private bool IsSignalInTrade(string owner)
		{
			if (string.IsNullOrEmpty(owner)) return false;
			bool inTrade;
			return signalInTradeMap.TryGetValue(owner, out inTrade) && inTrade;
		}

		private void SetSignalInTrade(string owner, bool inTrade)
		{
			if (string.IsNullOrEmpty(owner)) return;
			signalInTradeMap[owner] = inTrade;
		}

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

		// Parses the selected ATM template once; re-parses only when the template name changes (HUD or settings).
		// Draw module consumes the levels for the trigger lines.
		private Kat34ScalperAtmData GetAtmData()
		{
			string tpl = cachedBotAtm ?? "";
			if (tpl != atmLevelsName)
			{
				atmLevelsName = tpl;
				atmLevels = HasAtmTemplate(tpl)
					? Kat34ScalperAtmParser.ParseFile(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy", tpl + ".xml"))
					: new Kat34ScalperAtmData();
			}
			return atmLevels;
		}

		private int GetEffectiveBotQuantity()
		{
			Kat34ScalperAtmData atm = GetAtmData();
			return (atm != null && atm.Quantity > 0) ? atm.Quantity : Math.Max(1, BotOrderQuantity);
		}

		// BOT trades exactly the signals that are ON: an owner switched OFF never submits
		// (and its pending order was already cancelled by SetAXSignal(false)).
		// BOT trades exactly the signals that are ON: an owner switched OFF never submits
		// (and its pending order was already cancelled by SetAXSignal(false)).
		private bool SignalOwnerEnabled(string owner)
		{
			if (owner == "A1") return cachedA1;
			if (owner == "A2") return cachedA2;
			return true;
		}

		private bool HasOpenPosition(Account acc)
		{
			if (acc == null || acc.Positions == null) return false;
			try
			{
				foreach (Position pos in acc.Positions)
				{
					if (pos != null && pos.Instrument != null && Instrument != null
						&& pos.Instrument.FullName == Instrument.FullName
						&& pos.MarketPosition != MarketPosition.Flat)
						return true;
				}
			}
			catch { }
			return false;
		}

		// Called from the Signal module after a signal fires. refExtreme = best candidate extreme (sell: c2 low / buy: c2 high).
		// offsetTicks = the calling signal's own Entry Offset (order price must match its drawn entry line).
		// owner = signal module id ("A1"/"A2") — a signal cancels only its own pending order.
		private void TrySubmitBotEntry(bool isBuy, double refExtreme, int offsetTicks, string owner = "A1")
		{
			if (!cachedBotOn || !BotEnabled || refExtreme == 0) return;
			if (!SignalOwnerEnabled(owner)) return;
			Account acc = ResolveBotAccount();
			if (acc == null) return;
			if (IsDailyRiskBreached(out string breachReason))
			{
				ShowHudStatus(breachReason, Brushes.OrangeRed);
				Print(string.Format("[Kat34Scalper] BOT [{0}] entry blocked: {1}", owner, breachReason));
				return;
			}
			if (IsSignalInTrade(owner) || HasOpenPosition(acc)) return;
			if (pendingOrder != null || pendingMigrate) return; // one bot order at a time
			SubmitBotOrder(isBuy, refExtreme, offsetTicks, owner);
		}

		// Cancels the bot's pending entry only when it belongs to the given signal (any side).
		// Used when the signal is switched OFF — OFF must also kill its working order and
		// stop any in-flight migration re-place.
		private void CancelSignalBotEntry(string owner, string reason)
		{
			SetSignalInTrade(owner, false);
			if (pendingOrder != null && pendingOrderOwner == owner)
			{
				pendingMigrate = false;
				CancelPendingBotOrder(reason);
			}
		}

		private void SubmitBotOrder(bool isBuy, double refExtreme, int offsetTicks, string owner = "A1")
		{
			Account acc = ResolveBotAccount();
			if (acc == null)
			{
				Print("[Kat34Scalper] BOT: no account selected — pick one on the HUD or in settings.");
				return;
			}
			double entryPrice = isBuy
				? refExtreme + offsetTicks * TickSize
				: refExtreme - offsetTicks * TickSize;
			bool useStop = Kat34ScalperLogic.UseStopOrder(isBuy, entryPrice, Closes[0][0]);
			int qty = GetEffectiveBotQuantity();
			try
			{
				Order order = acc.CreateOrder(Instrument,
					isBuy ? OrderAction.Buy : OrderAction.Sell,
					useStop ? OrderType.StopMarket : OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc,
					qty, useStop ? 0 : entryPrice, useStop ? entryPrice : 0, "", "Entry", NinjaTrader.Core.Globals.MaxDate, null);

				pendingOrder = order;
				pendingOrderOwner = owner;
				pendingOffsetTicks = offsetTicks;
				pendingIsBuy = isBuy;
				pendingBestRef = refExtreme;
				pendingEntryPrice = entryPrice;

				pendingOrderAccount = acc;

				TrackAtmStartup(order);
				string tpl = cachedBotAtm;
				bool hasAtm = HasAtmTemplate(tpl);
				if (hasAtm)
					NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, order);
				else
				{
					if (!string.IsNullOrEmpty(tpl) && !tpl.Equals("None", StringComparison.OrdinalIgnoreCase))
						Print(string.Format("[Kat34Scalper] BOT: ATM template '{0}' not found — bare stop order.", tpl));
					acc.Submit(new[] { order });
				}
				ScheduleAtmBracketMerge();
				Print(string.Format("[Kat34Scalper] BOT [{5}]: {0} {1} {6}ct @ {2:F5} submitted (account {3}, ATM {4}).",
					isBuy ? "BUY" : "SELL", useStop ? "stop" : "limit", entryPrice, acc.Name, hasAtm ? tpl : "none", owner, qty));
				ShowHudStatus(string.Format("BOT [{4}]: {0} {1} {5}ct @ {2:F2} ({3})", isBuy ? "BUY" : "SELL", useStop ? "stop" : "limit", entryPrice, hasAtm ? tpl : "no ATM", owner, qty), Brushes.LightGreen);
			}
			catch (Exception ex)
			{
				pendingOrder = null;
				pendingOrderAccount = null;
				Print(string.Format("[Kat34Scalper] BOT submit error: {0}", ex.Message));
				ShowHudStatus("BOT submit error: " + ex.Message, Brushes.OrangeRed);
			}
		}

		// Polls the pending order on the data thread: terminal cleanup, trend-flip cancel, migrate to a better extreme.
		private void ManageBotEntry(double high, double low, double close)
		{
			EvaluateDailyRiskLimits();
			TrySubmitPendingRevert();
			CleanupFlatOrphans();

			Account acc = ResolveBotAccount();
			if (acc != null && !HasOpenPosition(acc))
			{
				if (signalInTradeMap.Count > 0)
				{
					signalInTradeMap.Clear();
				}
			}

			if (pendingOrder == null)
			{

				pendingOrderAccount = null;
				// A cancelled order left a better entry behind — re-place it while the setup still holds.
				// Owner gate: a signal switched OFF mid-migration must not see its order re-placed.
				if (pendingMigrate && cachedBotOn && BotEnabled && SignalOwnerEnabled(pendingOrderOwner))
				{
					pendingMigrate = false;
					if (fastEma != null && slowEma != null
						&& (pendingIsBuy ? fastEma[0] > slowEma[0] && close > fastEma[0] : fastEma[0] < slowEma[0] && close < fastEma[0]))
						SubmitBotOrder(pendingIsBuy, pendingMigrateRef, pendingOffsetTicks, pendingOrderOwner);
				}
				return;
			}

			OrderState state = pendingOrder.OrderState;
			if (state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected)
			{
				Print(string.Format("[Kat34Scalper] BOT: entry order {0} @ {1:F5}.", state, pendingEntryPrice));
				if (state == OrderState.Filled)
				{
					SetSignalInTrade(pendingOrderOwner, true);
					ShowHudStatus(string.Format("BOT [{0}]: entry FILLED @ {1:F2} — ATM manages brackets", pendingOrderOwner, pendingEntryPrice), Brushes.LightGreen);
					ClearSignalDrawings(pendingOrderOwner);
				}
				pendingOrder = null;
				pendingOrderAccount = null;
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
				Account acc = pendingOrderAccount ?? ResolveBotAccount();
				if (acc != null)
				{
					acc.Cancel(new[] { pendingOrder });
					Print(string.Format("[Kat34Scalper] BOT: entry cancel requested ({0}).", reason));
					ShowHudStatus("BOT: entry cancel — " + reason, Brushes.OrangeRed);
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] BOT cancel error: {0}", ex.Message));
			}
		}

		private static bool IsActiveOrderState(OrderState state)
		{
			return state == OrderState.Initialized
				|| state == OrderState.Submitted
				|| state == OrderState.Accepted
				|| state == OrderState.AcceptedByRisk
				|| state == OrderState.Working
				|| state == OrderState.TriggerPending
				|| state == OrderState.ChangePending
				|| state == OrderState.ChangeSubmitted
				|| state == OrderState.PartFilled
				|| state == OrderState.Suspended
				|| state == OrderState.CancelPending
				|| state == OrderState.CancelSubmitted;
		}

		#region Market Order / BE / Revert (ported from KatTradeManager)
		private DateTime lastEntrySubmitTime = DateTime.MinValue;
		private const double EntryDebounceMs = 500;
		private int pendingRevertAction;   // 0 = none, 1 = Buy, 2 = Sell
		private int pendingRevertQuantity;
		private int pendingRevertSubmitInFlight;
		private int closeInFlight;

		// ponytail: simplified from TradeManager's QueueAccountOperation — scalper uses direct submit
		private bool IsEntryDebounced()
		{
			if ((DateTime.Now - lastEntrySubmitTime).TotalMilliseconds < EntryDebounceMs) return true;
			lastEntrySubmitTime = DateTime.Now;
			return false;
		}

		private Position GetInstrumentPosition()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null) return null;
			var positions = acc.Positions;
			try
			{
				lock (positions)
				{
					foreach (Position p in positions)
						if (p != null && p.Instrument != null && p.Instrument.FullName == Instrument.FullName)
							return p;
				}
			}
			catch { }
			return null;
		}

		private bool IsCloseInFlight()
		{
			return System.Threading.Volatile.Read(ref closeInFlight) != 0;
		}

		private void CancelWorkingOrdersForInstrument(Account acc)
		{
			if (acc == null || Instrument == null || acc.Orders == null) return;
			System.Collections.Generic.List<Order> toCancel = new System.Collections.Generic.List<Order>();
			try
			{
				lock (acc.Orders)
				{
					foreach (Order o in acc.Orders)
					{
						if (o == null || o.Instrument == null || o.Instrument.FullName != Instrument.FullName) continue;
						if (IsActiveOrderState(o.OrderState))
							toCancel.Add(o);
					}
				}
				if (toCancel.Count > 0)
				{
					foreach (Order o in toCancel)
					{
						try { acc.Cancel(new[] { o }); } catch { }
					}
					Print(string.Format("[Kat34Scalper] Cancelled {0} working order(s) for {1}.", toCancel.Count, Instrument.FullName));
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error cancelling working orders: {0}", ex.Message));
			}
		}

		private void CleanupFlatOrphans()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null || acc.Orders == null) return;

			Position pos = GetInstrumentPosition();
			if (pos != null && pos.MarketPosition != MarketPosition.Flat) return;

			System.Collections.Generic.List<Order> orphans = new System.Collections.Generic.List<Order>();
			try
			{
				lock (acc.Orders)
				{
					foreach (Order o in acc.Orders)
					{
						if (o == null || o.Instrument == null || o.Instrument.FullName != Instrument.FullName) continue;
						if (!IsActiveOrderState(o.OrderState)) continue;
						if (pendingOrder != null && o == pendingOrder) continue;
						orphans.Add(o);
					}
				}

				if (orphans.Count > 0)
				{
					foreach (Order orphan in orphans)
					{
						try { acc.Cancel(new[] { orphan }); } catch { }
					}
					Print(string.Format("[Kat34Scalper] Flat cleanup: cancelled {0} orphan working order(s).", orphans.Count));
				}
			}
			catch { }
		}

		private bool PlaceMarketOrder(OrderAction action)
		{
			return PlaceMarketOrder(action, 0);
		}

		private bool PlaceMarketOrder(OrderAction action, int quantityOverride)
		{
			Print(string.Format("[Kat34Scalper] PlaceMarketOrder click: {0}", action));
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null)
			{
				ShowHudStatus("Market order: no account", Brushes.OrangeRed);
				return false;
			}

			if (IsDailyRiskBreached(out string breachReason))
			{
				Print(string.Format("[Kat34Scalper] Market Order REJECTED by Daily Risk: {0}", breachReason));
				ShowHudStatus(breachReason, Brushes.OrangeRed);
				return false;
			}

			if (IsEntryDebounced())
			{
				Print("[Kat34Scalper] Duplicate market order ignored (debounce).");
				return false;
			}

			try
			{
				int qty = quantityOverride > 0 ? quantityOverride : GetEffectiveBotQuantity();
				Position pos = GetInstrumentPosition();
				bool isLong = pos != null && pos.MarketPosition == MarketPosition.Long;
				bool isShort = pos != null && pos.MarketPosition == MarketPosition.Short;
				bool isOpposite = (isLong && action == OrderAction.Sell) || (isShort && action == OrderAction.Buy);

				if (isOpposite)
				{
					// Opposite market order click while in position -> cancel all existing working SL/TP orders first
					CancelWorkingOrdersForInstrument(acc);

					// If quantity is closing the position (<= pos.Quantity), submit bare market close order without launching new ATM strategy
					if (qty <= pos.Quantity)
					{
						Order closeOrder = acc.CreateOrder(Instrument, action, OrderType.Market, OrderEntry.Manual,
							TimeInForce.Gtc, qty, 0, 0, "", action == OrderAction.Buy ? "MarketBuyClose" : "MarketSellClose",
							NinjaTrader.Core.Globals.MaxDate, null);
						if (closeOrder != null)
						{
							acc.Submit(new[] { closeOrder });
							Print(string.Format("[Kat34Scalper] Market close submitted: {0} qty={1}", action, qty));
							ShowHudStatus(string.Format("{0} market close executed", action), Brushes.LightGreen);
							return true;
						}
					}
				}

				string tpl = cachedBotAtm;
				bool hasAtm = HasAtmTemplate(tpl);
				string entryName = hasAtm ? "Entry" : (action == OrderAction.Buy ? "MarketBuy" : "MarketSell");

				Order order = acc.CreateOrder(Instrument, action, OrderType.Market, OrderEntry.Manual,
					TimeInForce.Gtc, qty, 0, 0, "", entryName, NinjaTrader.Core.Globals.MaxDate, null);
				if (order != null)
				{
					TrackAtmStartup(order);
					if (hasAtm)
						NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, order);
					else
						acc.Submit(new[] { order });
					ScheduleAtmBracketMerge();
					Print(string.Format("[Kat34Scalper] Market order submitted: {0} qty={1} atm={2}", action, qty, hasAtm ? tpl : "none"));
					ShowHudStatus(string.Format("{0} market order submitted", action), Brushes.LightGreen);
					return true;
				}
				Print(string.Format("[Kat34Scalper] Market order creation returned null: {0} qty={1}", action, qty));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error placing market order: {0}", ex.Message));
			}
			return false;
		}

		private void SetBreakeven()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null)
			{
				ShowHudStatus("BE: no account", Brushes.OrangeRed);
				return;
			}
			try
			{
				Position pos = GetInstrumentPosition();
				if (pos == null || pos.MarketPosition == MarketPosition.Flat)
				{
					Print("[Kat34Scalper] BE: No active position.");
					ShowHudStatus("BE: no active position", Brushes.OrangeRed);
					return;
				}

				double tickSize = Instrument.MasterInstrument.TickSize;
				bool isLong = pos.MarketPosition == MarketPosition.Long;
				double bePrice = Kat34ScalperLogic.CalculateBreakevenPrice(isLong, pos.AveragePrice, cachedBotBufferTicks, tickSize);

				// Underwater check: BE stop on wrong side of market → broker rejection
				double livePrice = 0;
				try { livePrice = Closes[0][0]; } catch { }
				if (livePrice > 0 && !Kat34ScalperLogic.IsStopOnValidSide(isLong, bePrice, livePrice))
				{
					Print(string.Format("[Kat34Scalper] BE skipped: stop {0} invalid vs market {1}.", bePrice, livePrice));
					ShowHudStatus(string.Format("BE skipped: stop {0} invalid", bePrice), Brushes.OrangeRed);
					return;
				}

				// Find existing stop orders to move
				System.Collections.Generic.List<Order> workingStops = new System.Collections.Generic.List<Order>();
				if (acc.Orders != null)
				{
					foreach (Order o in acc.Orders)
					{
						if (o == null || o.Instrument != Instrument || !IsActiveOrderState(o.OrderState)) continue;
						if (o.OrderType != OrderType.StopMarket && o.OrderType != OrderType.StopLimit) continue;
						bool isProtective = isLong
							? (o.OrderAction == OrderAction.Sell || o.OrderAction == OrderAction.SellShort)
							: (o.OrderAction == OrderAction.Buy || o.OrderAction == OrderAction.BuyToCover);
						if (isProtective) workingStops.Add(o);
					}
				}

				if (workingStops.Count > 0)
				{
					foreach (Order stop in workingStops)
						stop.StopPriceChanged = bePrice;
					acc.Change(workingStops.ToArray());
					Print(string.Format("[Kat34Scalper] Moved {0} stop(s) to BE @ {1} (buffer {2} ticks)", workingStops.Count, bePrice, cachedBotBufferTicks));
					ShowHudStatus(string.Format("BE stop moved @ {0}", bePrice), Brushes.LightGreen);
				}
				else
				{
					OrderAction slAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
					Order slOrder = acc.CreateOrder(Instrument, slAction, OrderType.StopMarket, OrderEntry.Manual,
						TimeInForce.Gtc, pos.Quantity, 0, bePrice, "", "KAT_SL_BE", NinjaTrader.Core.Globals.MaxDate, null);
					if (slOrder != null)
					{
						acc.Submit(new[] { slOrder });
						Print(string.Format("[Kat34Scalper] BE stop submitted @ {0} (buffer {1} ticks)", bePrice, cachedBotBufferTicks));
						ShowHudStatus(string.Format("BE stop submitted @ {0}", bePrice), Brushes.LightGreen);
					}
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error setting BE: {0}", ex.Message));
			}
		}

		private void RevertPosition()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null)
			{
				ShowHudStatus("Revert: no account", Brushes.OrangeRed);
				return;
			}
			try
			{
				if (IsCloseInFlight())
				{
					Print("[Kat34Scalper] Revert: close already in flight.");
					return;
				}

				Position pos = GetInstrumentPosition();
				if (pos == null || pos.MarketPosition == MarketPosition.Flat)
				{
					Print("[Kat34Scalper] Revert: no active position.");
					ShowHudStatus("Revert: no active position", Brushes.OrangeRed);
					return;
				}

				OrderAction oppositeAction = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
				int revertQty = pos.Quantity;
				System.Threading.Interlocked.Exchange(ref pendingRevertAction, oppositeAction == OrderAction.Buy ? 1 : 2);
				System.Threading.Interlocked.Exchange(ref pendingRevertQuantity, revertQty);

				// Close current position first
				System.Threading.Interlocked.Exchange(ref closeInFlight, 1);
				OrderAction closeAction = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
				try
				{
					// Cancel existing orders first
					if (acc.Orders != null)
						foreach (Order o in acc.Orders)
							if (o != null && o.Instrument == Instrument && IsActiveOrderState(o.OrderState))
								try { acc.Cancel(new[] { o }); } catch { }

					Order closeOrder = acc.CreateOrder(Instrument, closeAction, OrderType.Market, OrderEntry.Manual,
						TimeInForce.Gtc, pos.Quantity, 0, 0, "", "KAT_REVERT_CLOSE", NinjaTrader.Core.Globals.MaxDate, null);
					if (closeOrder != null)
						acc.Submit(new[] { closeOrder });
				}
				catch (Exception ex)
				{
					System.Threading.Interlocked.Exchange(ref closeInFlight, 0);
					Print(string.Format("[Kat34Scalper] Revert close error: {0}", ex.Message));
					return;
				}

				Print(string.Format("[Kat34Scalper] Revert queued: close qty={0}, then enter {1} qty={0}.", revertQty, oppositeAction));
				ShowHudStatus(string.Format("Revert: closing → {0}", oppositeAction), Brushes.LightGreen);
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error reverting: {0}", ex.Message));
			}
		}

		// Called from ManageBotEntry on each bar update — completes the revert after the close fills
		private void TrySubmitPendingRevert()
		{
			int reqAction = System.Threading.Volatile.Read(ref pendingRevertAction);
			int reqQty = System.Threading.Volatile.Read(ref pendingRevertQuantity);
			if (reqAction == 0) return;

			// Check if close is done
			Position pos = GetInstrumentPosition();
			if (pos != null && pos.MarketPosition != MarketPosition.Flat)
			{
				// Still closing — check if it's the revert close
				Account acc = ResolveBotAccount();
				if (acc != null && acc.Orders != null)
				{
					bool hasRevertClose = false;
					foreach (Order o in acc.Orders)
						if (o != null && o.Name == "KAT_REVERT_CLOSE" && IsActiveOrderState(o.OrderState))
						{ hasRevertClose = true; break; }
					if (!hasRevertClose)
						System.Threading.Interlocked.Exchange(ref closeInFlight, 0);
				}
				return;
			}

			System.Threading.Interlocked.Exchange(ref closeInFlight, 0);
			if (reqQty <= 0)
			{
				System.Threading.Interlocked.Exchange(ref pendingRevertAction, 0);
				return;
			}

			if (System.Threading.Interlocked.CompareExchange(ref pendingRevertSubmitInFlight, 1, 0) != 0)
				return;
			try
			{
				OrderAction action = reqAction == 1 ? OrderAction.Buy : OrderAction.Sell;
				if (PlaceMarketOrder(action, reqQty))
				{
					System.Threading.Interlocked.Exchange(ref pendingRevertAction, 0);
					System.Threading.Interlocked.Exchange(ref pendingRevertQuantity, 0);
					ShowHudStatus(string.Format("Revert: {0} {1}ct submitted", action, reqQty), Brushes.LightGreen);
				}
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref pendingRevertSubmitInFlight, 0);
			}
		}
		#endregion

		#region ATM Bracket MERGE Engine (ported from KatTradeManager — always ON)
		private readonly object atmScaleInLock = new object();
		private Order atmStartupOrder;
		private DateTime atmLastLifecycleActivityUtc = DateTime.MinValue;
		private bool atmPositionWasConfirmedThisEpisode;
		private const double AtmLifecycleGraceMilliseconds = 3000.0;

		private Order atmMergeStopAnchor;
		private Order atmMergeTargetAnchor;
		private MarketPosition atmMergePosition = MarketPosition.Flat;
		private int atmMergeStopQuantity;
		private int atmMergeTargetQuantity;
		private int atmMergeScheduled;

		private static bool SameOrder(Order left, Order right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null) return false;
			return !string.IsNullOrEmpty(left.OrderId) && left.OrderId == right.OrderId;
		}

		private static bool IsMergeCandidateState(OrderState state)
		{
			return state == OrderState.Initialized
				|| state == OrderState.Submitted
				|| state == OrderState.Accepted
				|| state == OrderState.AcceptedByRisk
				|| state == OrderState.Working
				|| state == OrderState.TriggerPending
				|| state == OrderState.ChangePending
				|| state == OrderState.ChangeSubmitted
				|| state == OrderState.PartFilled
				|| state == OrderState.Suspended;
		}

		private static bool HasAtmBracketName(Order order)
		{
			if (order == null || string.IsNullOrEmpty(order.Name)) return false;
			string name = order.Name;
			return name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool HasAtmEntrySignal(Order order)
		{
			return order != null
				&& !string.IsNullOrEmpty(order.FromEntrySignal)
				&& order.FromEntrySignal.IndexOf("entry", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private bool IsKnownAtmBracket(Order order)
		{
			if (order == null) return false;
			lock (atmScaleInLock)
			{
				if (ReferenceEquals(order, atmMergeStopAnchor) || ReferenceEquals(order, atmMergeTargetAnchor))
					return true;
				if (atmMergeStopAnchor != null && !string.IsNullOrEmpty(atmMergeStopAnchor.Oco)
					&& string.Equals(atmMergeStopAnchor.Oco, order.Oco, StringComparison.Ordinal))
					return true;
				if (atmMergeTargetAnchor != null && !string.IsNullOrEmpty(atmMergeTargetAnchor.Oco)
					&& string.Equals(atmMergeTargetAnchor.Oco, order.Oco, StringComparison.Ordinal))
					return true;
				return false;
			}
		}

		private bool IsAtmBracketCandidate(Order order)
		{
			if (order == null || Instrument == null || order.Instrument != Instrument) return false;
			if (IsManualExitOrder(order) || !IsMergeCandidateState(order.OrderState)) return false;
			if (order.OrderType != OrderType.StopMarket
				&& order.OrderType != OrderType.StopLimit
				&& order.OrderType != OrderType.Limit)
				return false;
			return HasAtmBracketName(order) || HasAtmEntrySignal(order) || IsKnownAtmBracket(order);
		}

		private static bool IsAtmExitAction(OrderAction action, MarketPosition position)
		{
			return position == MarketPosition.Long
				? action == OrderAction.Sell || action == OrderAction.SellShort
				: action == OrderAction.Buy || action == OrderAction.BuyToCover;
		}

		private static bool IsManualExitOrder(Order order)
		{
			return order != null
				&& !string.IsNullOrEmpty(order.Name)
				&& order.Name.StartsWith("KAT_", StringComparison.OrdinalIgnoreCase);
		}

		private void TrackAtmStartup(Order order)
		{
			if (order == null) return;
			lock (atmScaleInLock)
			{
				atmStartupOrder = order;
				atmLastLifecycleActivityUtc = DateTime.UtcNow;
				atmPositionWasConfirmedThisEpisode = false;
			}
		}

		private void ClearAtmStartup(Order expected = null)
		{
			lock (atmScaleInLock)
			{
				if (expected == null || SameOrder(atmStartupOrder, expected))
					atmStartupOrder = null;
			}
		}

		private bool IsAtmStartupPending()
		{
			Order startup;
			DateTime lastActivity;
			lock (atmScaleInLock)
			{
				startup = atmStartupOrder;
				lastActivity = atmLastLifecycleActivityUtc;
			}
			if (startup == null) return false;
			if (IsActiveOrderState(startup.OrderState))
				return true;
			if (lastActivity == DateTime.MinValue) return true;
			return (DateTime.UtcNow - lastActivity).TotalMilliseconds < AtmLifecycleGraceMilliseconds;
		}

		private void ResetAtmScaleInTracking()
		{
			lock (atmScaleInLock)
			{
				atmMergeStopAnchor = null;
				atmMergeTargetAnchor = null;
				atmMergeStopQuantity = 0;
				atmMergeTargetQuantity = 0;
				atmMergePosition = MarketPosition.Flat;
				atmStartupOrder = null;
				atmLastLifecycleActivityUtc = DateTime.MinValue;
				atmPositionWasConfirmedThisEpisode = false;
			}
		}

		private void ScheduleAtmBracketMerge()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null) return;
			if (System.Threading.Interlocked.CompareExchange(ref atmMergeScheduled, 1, 0) != 0) return;

			Action merge = () =>
			{
				try
				{
					MergeAtmBrackets();
				}
				catch (Exception ex)
				{
					Print(string.Format("[Kat34Scalper] ATM MERGE execution failed: {0}", ex.Message));
				}
				finally
				{
					System.Threading.Interlocked.Exchange(ref atmMergeScheduled, 0);
				}
			};

			try
			{
				if (ChartControl != null && ChartControl.Dispatcher != null)
					ChartControl.Dispatcher.BeginInvoke(merge);
				else
					merge();
			}
			catch
			{
				System.Threading.Interlocked.Exchange(ref atmMergeScheduled, 0);
			}
		}

		private void MergeAtmBrackets()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null) return;

			try
			{
				Position position = GetInstrumentPosition();
				System.Collections.Generic.List<Order> candidates = new System.Collections.Generic.List<Order>();
				if (acc.Orders != null)
				{
					lock (acc.Orders)
					{
						foreach (Order o in acc.Orders)
							if (IsAtmBracketCandidate(o)) candidates.Add(o);
					}
				}

				bool positionConfirmed = position != null && position.MarketPosition != MarketPosition.Flat;
				if (positionConfirmed)
				{
					lock (atmScaleInLock)
					{
						if (!atmPositionWasConfirmedThisEpisode)
							atmLastLifecycleActivityUtc = DateTime.UtcNow;
						atmPositionWasConfirmedThisEpisode = true;
					}
					ClearAtmStartup();
				}

				if (!positionConfirmed)
				{
					bool startupPending = IsAtmStartupPending();
					bool wasPositionConfirmed;
					DateTime lastActivity;
					lock (atmScaleInLock)
					{
						wasPositionConfirmed = atmPositionWasConfirmedThisEpisode;
						lastActivity = atmLastLifecycleActivityUtc;
					}

					double activityAge = lastActivity == DateTime.MinValue
						? -1
						: (DateTime.UtcNow - lastActivity).TotalMilliseconds;

					if (Kat34ScalperLogic.ShouldDeferAtmFlatCleanup(
						startupPending,
						false,
						wasPositionConfirmed,
						activityAge,
						AtmLifecycleGraceMilliseconds))
					{
						return; // inside grace period: defer flat cleanup
					}

					if (candidates.Count > 0)
					{
						foreach (Order c in candidates)
							try { acc.Cancel(new[] { c }); } catch { }
						Print(string.Format("[Kat34Scalper] ATM MERGE flat cleanup: cancelled {0} bracket(s).", candidates.Count));
					}
					ResetAtmScaleInTracking();
					return;
				}

				System.Collections.Generic.List<Order> brackets = candidates
					.Where(o => IsAtmExitAction(o.OrderAction, position.MarketPosition))
					.ToList();
				System.Collections.Generic.List<Order> staleOppositeBrackets = candidates
					.Where(o => !IsAtmExitAction(o.OrderAction, position.MarketPosition))
					.ToList();

				if (staleOppositeBrackets.Count > 0)
				{
					foreach (Order stale in staleOppositeBrackets)
						try { acc.Cancel(new[] { stale }); } catch { }
					Print(string.Format("[Kat34Scalper] ATM MERGE: cancelled {0} stale opposite bracket(s).", staleOppositeBrackets.Count));
				}

				System.Collections.Generic.List<Order> stops = brackets
					.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
					.ToList();
				System.Collections.Generic.List<Order> targets = brackets
					.Where(o => o.OrderType == OrderType.Limit)
					.ToList();

				Order stopAnchor;
				Order targetAnchor;
				lock (atmScaleInLock)
				{
					stopAnchor = atmMergeStopAnchor != null && stops.Contains(atmMergeStopAnchor)
						? atmMergeStopAnchor
						: stops.FirstOrDefault();
					targetAnchor = atmMergeTargetAnchor != null && targets.Contains(atmMergeTargetAnchor)
						? atmMergeTargetAnchor
						: targets.FirstOrDefault();
					atmMergePosition = position.MarketPosition;
					atmMergeStopAnchor = stopAnchor;
					atmMergeTargetAnchor = targetAnchor;
					atmMergeStopQuantity = position.Quantity;
					atmMergeTargetQuantity = position.Quantity;
				}

				System.Collections.Generic.List<Order> changes = new System.Collections.Generic.List<Order>();
				if (stopAnchor != null && stopAnchor.Quantity != position.Quantity)
				{
					stopAnchor.QuantityChanged = position.Quantity;
					changes.Add(stopAnchor);
				}
				if (targetAnchor != null && targetAnchor.Quantity != position.Quantity)
				{
					targetAnchor.QuantityChanged = position.Quantity;
					changes.Add(targetAnchor);
				}

				if (changes.Count > 0)
				{
					acc.Change(changes.ToArray());
					Print(string.Format("[Kat34Scalper] ATM MERGE: resized {0} anchor order(s) to canonical qty {1}.", changes.Count, position.Quantity));
				}

				System.Collections.Generic.List<Order> duplicates = stops
					.Where(o => o != stopAnchor)
					.Concat(targets.Where(o => o != targetAnchor))
					.ToList();

				if (duplicates.Count > 0)
				{
					foreach (Order dup in duplicates)
						try { acc.Cancel(new[] { dup }); } catch { }
				}

				int removedCount = duplicates.Count + staleOppositeBrackets.Count;
				if (changes.Count > 0 || removedCount > 0)
				{
					Print(string.Format("[Kat34Scalper] ATM MERGE reconciled: posQty={0} stop={1} target={2} changed={3} removed={4}",
						position.Quantity,
						stopAnchor != null ? stopAnchor.OrderType.ToString() : "none",
						targetAnchor != null ? targetAnchor.OrderType.ToString() : "none",
						changes.Count,
						removedCount));
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] ATM MERGE reconciliation failed: {0}", ex.Message));
			}
		}
		#endregion

		public void FlattenAllPositions()
		{
			Account acc = ResolveBotAccount();
			if (acc == null)
			{
				ShowHudStatus("Flatten: no account selected", Brushes.OrangeRed);
				Print("[Kat34Scalper] Flatten: no account selected.");
				return;
			}

			try
			{
				// Cancel pending bot entries
				CancelPendingBotOrder("Close/flatten clicked");

				// Cancel all active working orders on the selected account
				if (acc.Orders != null)
				{
					foreach (Order order in acc.Orders)
					{
						if (order != null && IsActiveOrderState(order.OrderState))
						{
							try { acc.Cancel(new[] { order }); } catch { }
						}
					}
				}

				// Market close all non-flat positions on the account
				if (acc.Positions != null)
				{
					foreach (Position pos in acc.Positions)
					{
						if (pos != null && pos.MarketPosition != MarketPosition.Flat)
						{
							OrderAction action = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
							try
							{
								Order closeOrder = acc.CreateOrder(pos.Instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Gtc, pos.Quantity, 0, 0, "", "KAT_CLOSE", NinjaTrader.Core.Globals.MaxDate, null);
								if (closeOrder != null)
									acc.Submit(new[] { closeOrder });
							}
							catch (Exception ex)
							{
								Print(string.Format("[Kat34Scalper] Error submitting close for {0}: {1}", pos.Instrument != null ? pos.Instrument.FullName : "unknown", ex.Message));
							}
						}
					}
				}

				ShowHudStatus("Close/flatten executed", Brushes.OrangeRed);
				Print("[Kat34Scalper] Close/flatten executed: cancelled orders & closed positions.");
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error flattening account: {0}", ex.Message));
			}
		}
	}
}

