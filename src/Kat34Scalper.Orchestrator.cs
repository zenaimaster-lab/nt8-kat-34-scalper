/*
 * Kat34Scalper.Orchestrator.cs — reads independent signal indicators via KatSignalBus
 * and drives the Bot. Scalper shell has NO embedded signal math.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private string busKey;
		private readonly Dictionary<string, int> lastSeenGen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, double> lastPendingRef = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

		// Trade gates (HUD) — which bot signals Scalper is allowed to execute.
		private volatile bool tradeB1 = true;
		private volatile bool tradeB2 = true;

		private string MakeBusKey()
		{
			string inst = Instrument != null ? Instrument.FullName : "?";
			string bp = BarsPeriod != null ? BarsPeriod.ToString() : "?";
			int chartId = ChartControl != null ? ChartControl.GetHashCode() : 0;
			return KatSignalBus.MakeKey(inst, bp, chartId);
		}

		private bool TradeEnabled(string signalId)
		{
			if (string.Equals(signalId, "B1", StringComparison.OrdinalIgnoreCase)) return tradeB1;
			if (string.Equals(signalId, "B2", StringComparison.OrdinalIgnoreCase)) return tradeB2;
			return false;
		}

		/// <summary>
		/// Per realtime bar: poll bus snapshots, apply bot filters, submit/cancel bot entries.
		/// Signal drawings stay on the signal indicators — Scalper only orchestrates orders.
		/// </summary>
		private void OrchestrateFromBus()
		{
			if (string.IsNullOrEmpty(busKey)) busKey = MakeBusKey();

			bool sellAllowed, buyAllowed;
			PassFilters(out sellAllowed, out buyAllowed);

			List<KatSignalSnapshot> snaps = KatSignalBus.GetSnapshots(busKey);
			// Also try chartId=0 fallback (signals registered before ChartControl ready)
			if (snaps.Count == 0 && Instrument != null)
			{
				string fallback = KatSignalBus.MakeKey(Instrument.FullName,
					BarsPeriod != null ? BarsPeriod.ToString() : "?", 0);
				if (fallback != busKey)
					snaps = KatSignalBus.GetSnapshots(fallback);
			}

			foreach (KatSignalSnapshot s in snaps)
			{
				if (s == null || !s.IsBotSignal) continue;
				if (!TradeEnabled(s.SignalId))
				{
					// User disabled this signal for bot — kill its pending order
					if (pendingOrder != null && pendingOrderOwner == s.SignalId)
						CancelSignalBotEntry(s.SignalId, s.SignalId + " trade gate OFF");
					continue;
				}

				// --- Pending keep-alive (B1) ---
				if (s.HasPending)
				{
					bool sideOk = s.PendingIsBuy ? buyAllowed : sellAllowed;
					if (!sideOk)
					{
						CancelSignalBotEntry(s.SignalId, "filter blocked " + s.SignalId);
						continue;
					}

					double prevRef;
					bool refChanged = lastPendingRef.TryGetValue(s.SignalId, out prevRef)
						&& Math.Abs(prevRef - s.PendingRefExtreme) > TickSize * 0.1;
					lastPendingRef[s.SignalId] = s.PendingRefExtreme;

					if (pendingOrder == null && !pendingMigrate && !IsSignalInTrade(s.SignalId))
					{
						TrySubmitBotEntry(s.PendingIsBuy, s.PendingRefExtreme, s.PendingOffsetTicks, s.SignalId);
					}
					else if (pendingOrder != null && pendingOrderOwner == s.SignalId
						&& pendingIsBuy == s.PendingIsBuy && refChanged)
					{
						// Signal migrated extreme — request bot migrate
						bool better = s.PendingIsBuy
							? s.PendingRefExtreme < pendingBestRef
							: s.PendingRefExtreme > pendingBestRef;
						if (better)
						{
							pendingBestRef = s.PendingRefExtreme;
							pendingMigrateRef = s.PendingRefExtreme;
							pendingMigrate = true;
							CancelPendingBotOrder(s.SignalId + " migrate");
						}
					}
				}
				else
				{
					lastPendingRef.Remove(s.SignalId);
					// Pending cleared on signal side
					if (pendingOrder != null && pendingOrderOwner == s.SignalId)
						CancelSignalBotEntry(s.SignalId, s.SignalId + " pending cleared");
				}

				// --- One-shot fire (B2) ---
				if (s.HasFire)
				{
					int prevGen;
					if (lastSeenGen.TryGetValue(s.SignalId, out prevGen) && prevGen == s.Generation)
						continue; // already handled
					lastSeenGen[s.SignalId] = s.Generation;

					bool sideOk = s.FireIsBuy ? buyAllowed : sellAllowed;
					if (sideOk)
						TrySubmitBotEntry(s.FireIsBuy, s.FireRefExtreme, s.FireOffsetTicks, s.SignalId);

					// Ack KatB2 if present on bus
					foreach (IKatSignalProvider p in KatSignalBus.GetProviders(busKey))
					{
						KatB2 b2 = p as KatB2;
						if (b2 != null) b2.MarkFireConsumed(s.Generation);
					}
				}
			}
		}
	}
}
