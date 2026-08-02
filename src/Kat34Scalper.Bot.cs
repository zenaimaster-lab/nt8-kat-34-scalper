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
	public partial class Kat34Scalper : Indicator
	{
		// --- Bot module state ---
		private volatile bool cachedBotOn;
		private volatile string cachedBotAtm = "";
		private volatile string cachedBotAccountName = "";
		private Order pendingOrder;
		private bool pendingIsBuy;
		private double pendingEntryPrice; // last submitted entry price (limit OR stop — Order.StopPrice is 0 on limits)
		private double pendingBestRef;    // best extreme used for migration (sell: highest qualifying low / buy: lowest high)
		private double pendingMigrateRef; // better extreme found; new order placed once the cancelled one is terminal
		private volatile bool pendingMigrate;
		private string atmLevelsName = "\0"; // never matches a real template name — forces first parse
		private Kat34ScalperAtmData atmLevels;

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

		// Called from the Signal module after a signal fires. refExtreme = best candidate extreme (sell: c2 low / buy: c2 high).
		private void TrySubmitBotEntry(bool isBuy, double refExtreme)
		{
			if (!cachedBotOn || !BotEnabled || refExtreme == 0) return;
			if (pendingOrder != null || pendingMigrate) return; // one bot order at a time
			SubmitBotOrder(isBuy, refExtreme);
		}

		private void SubmitBotOrder(bool isBuy, double refExtreme)
		{
			Account acc = ResolveBotAccount();
			if (acc == null)
			{
				Print("[Kat34Scalper] BOT: no account selected — pick one on the HUD or in settings.");
				return;
			}
			double entryPrice = isBuy
				? refExtreme + EntryOffsetTicks * TickSize
				: refExtreme - EntryOffsetTicks * TickSize;
			// Price already past the trigger -> a stop would sit on the wrong side and be rejected; use a limit.
			bool useStop = Kat34ScalperLogic.UseStopOrder(isBuy, entryPrice, Closes[0][0]);
			try
			{
				// ATM contract: the entry order name MUST be "Entry" (see KatTradeManager).
				Order order = acc.CreateOrder(Instrument,
					isBuy ? OrderAction.Buy : OrderAction.Sell,
					useStop ? OrderType.StopMarket : OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc,
					BotOrderQuantity, useStop ? 0 : entryPrice, useStop ? entryPrice : 0, "", "Entry", NinjaTrader.Core.Globals.MaxDate, null);

				pendingOrder = order;
				pendingIsBuy = isBuy;
				pendingBestRef = refExtreme;
				pendingEntryPrice = entryPrice;

				string tpl = cachedBotAtm;
				if (HasAtmTemplate(tpl))
					NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, order);
				else
				{
					if (!string.IsNullOrEmpty(tpl) && !tpl.Equals("None", StringComparison.OrdinalIgnoreCase))
						Print(string.Format("[Kat34Scalper] BOT: ATM template '{0}' not found — bare stop order.", tpl));
					acc.Submit(new[] { order });
				}
				Print(string.Format("[Kat34Scalper] BOT: {0} {1} @ {2:F5} submitted (account {3}, ATM {4}).",
					isBuy ? "BUY" : "SELL", useStop ? "stop" : "limit", entryPrice, acc.Name, HasAtmTemplate(tpl) ? tpl : "none"));
				ShowHudStatus(string.Format("BOT: {0} {1} @ {2:F2} ({3})", isBuy ? "BUY" : "SELL", useStop ? "stop" : "limit", entryPrice, HasAtmTemplate(tpl) ? tpl : "no ATM"), Brushes.LightGreen);
			}
			catch (Exception ex)
			{
				pendingOrder = null;
				Print(string.Format("[Kat34Scalper] BOT submit error: {0}", ex.Message));
				ShowHudStatus("BOT submit error: " + ex.Message, Brushes.OrangeRed);
			}
		}

		// Polls the pending order on the data thread: terminal cleanup, trend-flip cancel, migrate to a better extreme.
		private void ManageBotEntry(double high, double low, double close)
		{
			if (pendingOrder == null)
			{
				// A cancelled order left a better entry behind — re-place it while the setup still holds.
				if (pendingMigrate && cachedBotOn && BotEnabled)
				{
					pendingMigrate = false;
					if (fastEma != null && slowEma != null
						&& (pendingIsBuy ? fastEma[0] > slowEma[0] && close > fastEma[0] : fastEma[0] < slowEma[0] && close < fastEma[0]))
						SubmitBotOrder(pendingIsBuy, pendingMigrateRef);
				}
				return;
			}

			OrderState state = pendingOrder.OrderState;
			if (state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected)
			{
				Print(string.Format("[Kat34Scalper] BOT: entry order {0} @ {1:F5}.", state, pendingEntryPrice));
				if (state == OrderState.Filled)
					ShowHudStatus(string.Format("BOT: entry FILLED @ {0:F2} — ATM manages brackets", pendingEntryPrice), Brushes.LightGreen);
				pendingOrder = null;
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
				Account acc = ResolveBotAccount();
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
