/* KatSignalState.cs - standalone signal state enums (prevent NT8 DLL conflicts). */

using System;

namespace KAT.Signals
{
	public enum KatSignalKind
	{
		Sell,
		Buy
	}

	// A1 EMA34 zone timeframes (value = period seconds; NT8 renders enum names as the dropdown).
	public enum KatEmaZoneTf
	{
		S90 = 90,
		M1 = 60,
		M2 = 120,
		M3 = 180,
		M5 = 300,
		M15 = 900,
		M30 = 1800
	}

	/// <summary>
	/// Per-side A1 sequence state — the caller owns one instance per side (sell/buy).
	/// Phase: 0 = idle (waiting for price beyond the fast EMA), 1 = armed (price beyond the fast
	/// EMA, waiting for the cross back through it), 2 = pullback running (crossed, watching for the
	/// slow-EMA touch and the U-turn close — the signal fires on that close).
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
}
