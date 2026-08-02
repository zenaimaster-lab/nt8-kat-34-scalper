/* Kat34ScalperLogic.cs - pure signal state machine + ATM template parser, zero NT8 dependencies (unit-testable). */

using System;
using System.IO;
using System.Xml;

namespace Kat34Scalper
{
	public enum KatSignalKind
	{
		Sell,
		Buy
	}

	public enum KatTriggerMode
	{
		// Fire when, after the U-turn close, a later bar closes back through the fast EMA (retest).
		RetestBounce,
		// Fire immediately on the U-turn bar closing through the fast EMA.
		Breakdown
	}

	/// <summary>
	/// Per-side A1 sequence state — the caller owns one instance per side (sell/buy).
	/// Phase: 0 = idle (waiting for price beyond the fast EMA), 1 = armed (price beyond the fast
	/// EMA, waiting for the cross back through it), 2 = pullback running (crossed, watching for the
	/// slow-EMA touch and the U-turn close), 3 = U-turned, waiting for the retest trigger.
	/// </summary>
	public sealed class KatA1State
	{
		public int Phase;
		public bool Touched89;
		public int SeqBars;   // sequence lifetime in bars, counted from the ema34 cross bar
		public double C1;     // U-turn bar extreme (sell: its low / buy: its high)
		public double C2;     // best later candidate extreme

		public void Reset()
		{
			Phase = 0;
			Touched89 = false;
			SeqBars = 0;
			C1 = 0;
			C2 = 0;
		}

		// Backfill handoff: a replayed temp state replaces the live state so realtime
		// evaluation continues the in-flight sequence instead of re-arming from idle.
		public void CopyFrom(KatA1State other)
		{
			Phase = other.Phase;
			Touched89 = other.Touched89;
			SeqBars = other.SeqBars;
			C1 = other.C1;
			C2 = other.C2;
		}
	}

	/// <summary>Result of one A2 bar step — what the pending ema34-bounce entry did on this bar.</summary>
	public enum KatA2Action
	{
		None,     // nothing changed
		NewEntry, // first valid touch candle — place the pending stop at its extreme
		Migrate,  // later touch candle with a better extreme — move the pending entry
		Cancel,   // close beyond ema34 (or trend lost) — kill the pending entry
		Filled    // bar reached the pending entry — assume filled, setup done
	}

	/// <summary>
	/// Per-side A2 (34+8+Bounce) pending-entry state. Active = a touch candle already placed
	/// a pending stop entry; RefExtreme ratchets to better extremes only (buy: lowest touch
	/// high / sell: highest touch low). The caller owns one instance per side.
	/// </summary>
	public sealed class KatA2State
	{
		public bool Active;
		public double RefExtreme;

		public void Reset()
		{
			Active = false;
			RefExtreme = 0;
		}

		// Backfill handoff: replayed temp state replaces the live state (same contract as KatA1State).
		public void CopyFrom(KatA2State other)
		{
			Active = other.Active;
			RefExtreme = other.RefExtreme;
		}
	}

	public static class Kat34ScalperLogic
	{
		/// <summary>
		/// A0 ribbon fan. emasNow/emasPrev ordered fastest to slowest (9,21,34,55,89,144,200).
		/// Returns +1 = buy fan (ascending, spreading, wide enough), -1 = sell fan, 0 = no fan.
		/// Fan = strict order + total spread wider than lookback + at least minSpreadTicks.
		/// </summary>
		public static int FanDirection(double[] emasNow, double[] emasPrev, int minSpreadTicks, double tickSize)
		{
			if (emasNow == null || emasPrev == null || emasNow.Length < 2 || emasPrev.Length != emasNow.Length)
				return 0;

			int n = emasNow.Length;
			bool up = true, down = true;
			for (int i = 0; i < n - 1; i++)
			{
				if (emasNow[i] <= emasNow[i + 1]) up = false;
				if (emasNow[i] >= emasNow[i + 1]) down = false;
			}
			if (!up && !down) return 0;

			double spreadNow = Math.Abs(emasNow[0] - emasNow[n - 1]);
			double spreadPrev = Math.Abs(emasPrev[0] - emasPrev[n - 1]);
			if (spreadNow <= spreadPrev) return 0;          // must be spreading out
			if (spreadNow < minSpreadTicks * tickSize) return 0;
			return up ? 1 : -1;
		}

		/// <summary>
		/// A1 effective entry from the two candidates: sell takes the higher stop (max), buy the lower (min).
		/// Sell stops sit below the candidate lows; buy stops above the candidate highs.
		/// </summary>
		public static double EffectiveEntry(bool isBuy, double c1, double c2, int offsetTicks, double tickSize)
		{
			if (isBuy)
				return Math.Min(c1, c2) + offsetTicks * tickSize;
			return Math.Max(c1, c2) - offsetTicks * tickSize;
		}

		/// <summary>
		/// Bot entry order type: a stop entry is only valid on the correct side of the market
		/// (sell stop BELOW / buy stop ABOVE current price). Price already past the trigger → limit.
		/// Same rule as KatTradeManager.DetermineOrderType.
		/// </summary>
		public static bool UseStopOrder(bool isBuy, double triggerPrice, double currentPrice)
		{
			return isBuy ? triggerPrice > currentPrice : triggerPrice < currentPrice;
		}

		/// <summary>Market filter: ADX strength + relative volume. volumeSma 0 disables the volume leg.</summary>
		public static bool PassMarketFilter(double adx, double adxMin, double volume, double volumeSma, double volumeMult)
		{
			if (adx < adxMin) return false;
			if (volumeSma > 0 && volume < volumeSma * volumeMult) return false;
			return true;
		}

		/// <summary>Time window in machine-local time. start == end disables the window (always true). Overnight (start &gt; end) wraps midnight.</summary>
		public static bool IsInTimeWindow(TimeSpan time, TimeSpan start, TimeSpan end)
		{
			if (start == end) return true;
			if (start < end) return time >= start && time < end;
			return time >= start || time < end;
		}

		/// <summary>
		/// Advances the per-side state machine by one bar. Caller owns the KatA1State instance.
		/// Sell (downtrend: ema34 below ema89): price pulls back from BELOW ema34, crosses UP
		/// through ema34, touches/crosses ema89, reverses and closes back below ema34 (U-turn).
		/// Breakdown fires on that U-turn close; RetestBounce fires when a later bar closes back
		/// above ema34. The whole sequence (cross bar included) must complete within maxSeqBars,
		/// otherwise the setup expires and rearms. Buy mirrors the same sequence.
		/// C1/C2 are kept (not cleared) when a signal fires so the caller can price the entry.
		/// </summary>
		public static KatSignalKind? Update(
			KatSignalKind kind, KatTriggerMode mode, int maxSeqBars,
			bool trendOk,
			double high, double low, double close,
			double ema34, double ema89,
			KatA1State s)
		{
			if (!trendOk)
			{
				s.Reset();
				return null;
			}
			if (maxSeqBars < 1) maxSeqBars = 1;

			// Sequence lifetime: counted from the ema34 cross bar. Expired setups rearm from scratch.
			if (s.Phase >= 2)
			{
				s.SeqBars++;
				if (s.SeqBars > maxSeqBars) s.Reset();
			}

			if (kind == KatSignalKind.Sell)
			{
				// 0: idle — the pullback must start from BELOW ema34
				if (s.Phase == 0 && close < ema34) s.Phase = 1;

				// 1: armed below — the cross UP through ema34 (close basis) starts the sequence
				if (s.Phase == 1 && close > ema34)
				{
					s.Phase = 2;
					s.SeqBars = 1;
				}

				// 2: pullback running — watch the ema89 touch and the U-turn close back below ema34
				if (s.Phase == 2)
				{
					if (high >= ema89) s.Touched89 = true;
					if (close < ema34)
					{
						if (s.Touched89)
						{
							s.C1 = low;
							s.C2 = low;
							if (mode == KatTriggerMode.Breakdown)
							{
								s.Phase = 1; // back below ema34 already — armed for the next pullback
								s.Touched89 = false;
								s.SeqBars = 0;
								return KatSignalKind.Sell;
							}
							s.Phase = 3;
						}
						else
						{
							// reversed below ema34 before ever touching ema89 — failed pullback, rearmed
							s.Phase = 1;
							s.SeqBars = 0;
						}
					}
				}

				// 3: U-turned — RetestBounce fires when a later bar closes back above ema34
				if (s.Phase == 3)
				{
					if (close < ema34 && low > s.C2) s.C2 = low; // higher low — better sell entry
					if (close > ema34)
					{
						s.Phase = 0;
						s.Touched89 = false;
						s.SeqBars = 0;
						return KatSignalKind.Sell;
					}
				}
			}
			else
			{
				// Buy mirrors Sell: armed ABOVE ema34, cross DOWN through it, touch ema89 from above,
				// U-turn close back above ema34, RetestBounce fires on the close back below ema34.
				if (s.Phase == 0 && close > ema34) s.Phase = 1;

				if (s.Phase == 1 && close < ema34)
				{
					s.Phase = 2;
					s.SeqBars = 1;
				}

				if (s.Phase == 2)
				{
					if (low <= ema89) s.Touched89 = true;
					if (close > ema34)
					{
						if (s.Touched89)
						{
							s.C1 = high;
							s.C2 = high;
							if (mode == KatTriggerMode.Breakdown)
							{
								s.Phase = 1;
								s.Touched89 = false;
								s.SeqBars = 0;
								return KatSignalKind.Buy;
							}
							s.Phase = 3;
						}
						else
						{
							s.Phase = 1;
							s.SeqBars = 0;
						}
					}
				}

				if (s.Phase == 3)
				{
					if (close > ema34 && high < s.C2) s.C2 = high; // lower high — better buy entry
					if (close < ema34)
					{
						s.Phase = 0;
						s.Touched89 = false;
						s.SeqBars = 0;
						return KatSignalKind.Buy;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// A2 (34+8+Bounce) — advances one pending-entry state machine by one bar.
		/// Buy: trend stack valid (caller evaluates the enabled ema conditions), price pulls back
		/// and TOUCHES ema34 (wick low &lt;= ema34) while CLOSING above it → pending stop LONG at the
		/// touch candle's high (+ offset). A later touch candle with a lower high migrates the entry
		/// down; a higher high means the stop would already have filled. A close below ema34 (or trend
		/// loss) cancels the entry. Sell mirrors: touch = high &gt;= ema34, close below; entry at the
		/// touch candle's low (- offset); migrate up to a higher low; close above ema34 cancels.
		/// Fill check runs first (entry = RefExtreme ± offset): once price reaches the trigger the
		/// setup is done regardless of what else the bar did.
		/// </summary>
		public static KatA2Action UpdateA2(
			KatSignalKind kind, bool trendOk,
			double high, double low, double close, double ema34,
			int offsetTicks, double tickSize,
			KatA2State s)
		{
			if (kind == KatSignalKind.Buy)
			{
				double trigger = s.RefExtreme + offsetTicks * tickSize;
				if (s.Active && high >= trigger) { s.Reset(); return KatA2Action.Filled; }
				if (!trendOk || close < ema34)
				{
					if (s.Active) { s.Reset(); return KatA2Action.Cancel; }
					return KatA2Action.None;
				}
				if (low <= ema34) // wick touched ema34 and the bar closed above it
				{
					if (!s.Active) { s.Active = true; s.RefExtreme = high; return KatA2Action.NewEntry; }
					if (high < s.RefExtreme) { s.RefExtreme = high; return KatA2Action.Migrate; }
				}
				return KatA2Action.None;
			}
			else
			{
				double trigger = s.RefExtreme - offsetTicks * tickSize;
				if (s.Active && low <= trigger) { s.Reset(); return KatA2Action.Filled; }
				if (!trendOk || close > ema34)
				{
					if (s.Active) { s.Reset(); return KatA2Action.Cancel; }
					return KatA2Action.None;
				}
				if (high >= ema34) // wick touched ema34 and the bar closed below it
				{
					if (!s.Active) { s.Active = true; s.RefExtreme = low; return KatA2Action.NewEntry; }
					if (low > s.RefExtreme) { s.RefExtreme = low; return KatA2Action.Migrate; }
				}
				return KatA2Action.None;
			}
		}
	}

	/// <summary>Parsed SL/TP and trailing-SL trigger levels (ticks) from an NT8 ATM strategy template.</summary>
	public sealed class Kat34ScalperAtmData
	{
		public int StopLoss;
		public int Target;
		public int BETrigger;
		public int SL1Trigger;
		public int SL2Trigger;
	}

	/// <summary>
	/// Reads StopLoss/Target/AutoBreakEven/AutoTrail profit triggers from an ATM template .xml.
	/// Any parse failure yields zeroed data (callers fall back to indicator settings).
	/// Named Kat34Scalper* on purpose: NT8 compiles every Custom indicator into ONE assembly —
	/// reusing KatTradeManager's type names would collide.
	/// </summary>
	public static class Kat34ScalperAtmParser
	{
		public static Kat34ScalperAtmData ParseFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return new Kat34ScalperAtmData();
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(filePath);
				return ParseDocument(doc);
			}
			catch
			{
				return new Kat34ScalperAtmData();
			}
		}

		public static Kat34ScalperAtmData ParseXml(string xmlContent)
		{
			if (string.IsNullOrWhiteSpace(xmlContent)) return new Kat34ScalperAtmData();
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(xmlContent);
				return ParseDocument(doc);
			}
			catch
			{
				return new Kat34ScalperAtmData();
			}
		}

		private static Kat34ScalperAtmData ParseDocument(XmlDocument doc)
		{
			Kat34ScalperAtmData result = new Kat34ScalperAtmData();
			if (doc == null) return result;

			result.StopLoss = ReadInt(doc, "//AtmStrategy/Brackets/Bracket/StopLoss");
			result.Target = ReadInt(doc, "//AtmStrategy/Brackets/Bracket/Target");
			result.BETrigger = ReadInt(doc, "//AtmStrategy/Brackets/Bracket/StopStrategy/AutoBreakEvenProfitTrigger");

			XmlNodeList trailSteps = doc.SelectNodes("//AtmStrategy/Brackets/Bracket/StopStrategy/AutoTrailSteps/AutoTrailStep");
			if (trailSteps != null)
			{
				if (trailSteps.Count > 0) result.SL1Trigger = ReadInt(trailSteps[0], "ProfitTrigger");
				if (trailSteps.Count > 1) result.SL2Trigger = ReadInt(trailSteps[1], "ProfitTrigger");
			}
			return result;
		}

		private static int ReadInt(XmlDocument doc, string xpath)
		{
			XmlNode node = doc.SelectSingleNode(xpath);
			int value;
			return node != null && int.TryParse(node.InnerText, out value) ? value : 0;
		}

		private static int ReadInt(XmlNode parent, string name)
		{
			XmlNode node = parent == null ? null : parent.SelectSingleNode(name);
			int value;
			return node != null && int.TryParse(node.InnerText, out value) ? value : 0;
		}
	}
}
