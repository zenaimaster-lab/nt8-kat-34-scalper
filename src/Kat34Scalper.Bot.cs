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
using System.IO;
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
			if (owner == "A3") return cachedA3;
			if (owner == "A4") return cachedA4;
			return true; // future owners manage their own gating
		}

		private Order pendingA4BuyOrder;
		private Order pendingA4SellOrder;
		private double pendingA4BuyPrice;
		private double pendingA4SellPrice;
		private bool a4InTrade;

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

		// Called from Signal A4 to manage OCO entries (BUY stop/limit and SELL stop/limit)
		private void TrySubmitA4BotOcoEntries(double buyPrice, double sellPrice)
		{
			if (!cachedBotOn || !BotEnabled || !cachedA4 || buyPrice <= 0 || sellPrice <= 0) return;
			Account acc = ResolveBotAccount();
			if (acc == null) return;

			// Do NOT submit new entry orders while already in a trade or holding an open position
			if (a4InTrade || HasOpenPosition(acc)) return;

			if (pendingA4BuyOrder != null && pendingA4BuyPrice == buyPrice
				&& pendingA4SellOrder != null && pendingA4SellPrice == sellPrice)
				return;

			if (pendingA4BuyOrder != null || pendingA4SellOrder != null)
			{
				CancelA4BotOrders("A4 OCO price update for new candle");
			}

			string ocoId = "K34S_A4_OCO_" + DateTime.Now.Ticks;
			int qty = GetEffectiveBotQuantity();

			bool useBuyStop = Kat34ScalperLogic.UseStopOrder(true, buyPrice, Closes[0][0]);
			Order buyOrder = acc.CreateOrder(Instrument, OrderAction.Buy,
				useBuyStop ? OrderType.StopMarket : OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc,
				qty, useBuyStop ? 0 : buyPrice, useBuyStop ? buyPrice : 0, ocoId, "Entry", NinjaTrader.Core.Globals.MaxDate, null);

			bool useSellStop = Kat34ScalperLogic.UseStopOrder(false, sellPrice, Closes[0][0]);
			Order sellOrder = acc.CreateOrder(Instrument, OrderAction.Sell,
				useSellStop ? OrderType.StopMarket : OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc,
				qty, useSellStop ? 0 : sellPrice, useSellStop ? sellPrice : 0, ocoId, "Entry", NinjaTrader.Core.Globals.MaxDate, null);

			pendingA4BuyOrder = buyOrder;
			pendingA4SellOrder = sellOrder;
			pendingA4BuyPrice = buyPrice;
			pendingA4SellPrice = sellPrice;

			string tpl = cachedBotAtm;
			bool hasAtm = HasAtmTemplate(tpl);
			if (hasAtm)
			{
				NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, buyOrder);
				NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, sellOrder);
			}
			else
			{
				acc.Submit(new[] { buyOrder, sellOrder });
			}

			Print(string.Format("[Kat34Scalper] BOT A4 OCO submitted ({4}ct): BUY @ {0:F5} ({1}), SELL @ {2:F5} ({3})",
				buyPrice, useBuyStop ? "stop" : "limit", sellPrice, useSellStop ? "stop" : "limit", qty));
			ShowHudStatus(string.Format("BOT A4 OCO ({2}ct): BUY @ {0:F2}, SELL @ {1:F2}", buyPrice, sellPrice, qty), Brushes.LightGreen);
		}

		// Called from the Signal module after a signal fires. refExtreme = best candidate extreme (sell: c2 low / buy: c2 high).
		// offsetTicks = the calling signal's own Entry Offset (order price must match its drawn entry line).
		// owner = signal module id ("A1"/"A2"/"A3") — a signal cancels only its own pending order.
		private void TrySubmitBotEntry(bool isBuy, double refExtreme, int offsetTicks, string owner = "A1")
		{
			if (!cachedBotOn || !BotEnabled || refExtreme == 0) return;
			if (!SignalOwnerEnabled(owner)) return;
			Account acc = ResolveBotAccount();
			if (acc == null) return;
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
			if (owner == "A4")
			{
				CancelA4BotOrders(reason);
			}
		}

		private void CancelA4BotOrders(string reason)
		{
			Account acc = ResolveBotAccount();
			if (acc == null) return;
			a4InTrade = false;
			SetSignalInTrade("A4", false);
			if (pendingA4BuyOrder != null)
			{
				try { acc.Cancel(new[] { pendingA4BuyOrder }); } catch { }
				pendingA4BuyOrder = null;
			}
			if (pendingA4SellOrder != null)
			{
				try { acc.Cancel(new[] { pendingA4SellOrder }); } catch { }
				pendingA4SellOrder = null;
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
			// Price already past the trigger -> a stop would sit on the wrong side and be rejected; use a limit.
			bool useStop = Kat34ScalperLogic.UseStopOrder(isBuy, entryPrice, Closes[0][0]);
			int qty = GetEffectiveBotQuantity();
			try
			{
				// ATM contract: the entry order name MUST be "Entry" (see KatTradeManager).
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

		private void ManageA4BotEntry()
		{
			Account acc = ResolveBotAccount();
			if (acc == null) return;

			if (a4InTrade)
			{
				if (!HasOpenPosition(acc))
				{
					a4InTrade = false;
					SetSignalInTrade("A4", false);
					Print("[Kat34Scalper] BOT: A4 position closed (flat) — ready for next OCO setup.");
					ShowHudStatus("BOT: A4 position closed", Brushes.LightGreen);
				}
			}

			if (pendingA4BuyOrder == null && pendingA4SellOrder == null) return;

			if (pendingA4BuyOrder != null && pendingA4BuyOrder.OrderState == OrderState.Filled)
			{
				a4InTrade = true;
				SetSignalInTrade("A4", true);
				Print(string.Format("[Kat34Scalper] BOT: A4 BUY filled @ {0:F5} — cancelling A4 SELL order.", pendingA4BuyPrice));
				ShowHudStatus(string.Format("BOT: A4 BUY FILLED @ {0:F2}", pendingA4BuyPrice), Brushes.LightGreen);
				if (pendingA4SellOrder != null)
				{
					try { acc.Cancel(new[] { pendingA4SellOrder }); } catch { }
					pendingA4SellOrder = null;
				}
				pendingA4BuyOrder = null;
				ClearA4Drawings();
			}
			else if (pendingA4SellOrder != null && pendingA4SellOrder.OrderState == OrderState.Filled)
			{
				a4InTrade = true;
				SetSignalInTrade("A4", true);
				Print(string.Format("[Kat34Scalper] BOT: A4 SELL filled @ {0:F5} — cancelling A4 BUY order.", pendingA4SellPrice));
				ShowHudStatus(string.Format("BOT: A4 SELL FILLED @ {0:F2}", pendingA4SellPrice), Brushes.LightGreen);
				if (pendingA4BuyOrder != null)
				{
					try { acc.Cancel(new[] { pendingA4BuyOrder }); } catch { }
					pendingA4BuyOrder = null;
				}
				pendingA4SellOrder = null;
				ClearA4Drawings();
			}

			if (pendingA4BuyOrder != null && (pendingA4BuyOrder.OrderState == OrderState.Cancelled || pendingA4BuyOrder.OrderState == OrderState.Rejected))
				pendingA4BuyOrder = null;
			if (pendingA4SellOrder != null && (pendingA4SellOrder.OrderState == OrderState.Cancelled || pendingA4SellOrder.OrderState == OrderState.Rejected))
				pendingA4SellOrder = null;
		}

		// Polls the pending order on the data thread: terminal cleanup, trend-flip cancel, migrate to a better extreme.
		private void ManageBotEntry(double high, double low, double close)
		{
			ManageA4BotEntry();

			Account acc = ResolveBotAccount();
			if (acc != null && !HasOpenPosition(acc))
			{
				if (a4InTrade || signalInTradeMap.Count > 0)
				{
					a4InTrade = false;
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
	}
}
