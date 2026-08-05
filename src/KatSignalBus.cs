/* KatSignalBus.cs — chart-scoped registry so Scalper (bot shell) can read independent signal indicators.
 * Zero NT8 type deps — safe for xunit. Signal indicators register; Scalper polls snapshots.
 */

using System;
using System.Collections.Generic;

namespace Kat34Scalper
{
	/// <summary>One bot-consumable entry snapshot published by a signal indicator.</summary>
	public sealed class KatSignalSnapshot
	{
		public string SignalId;      // "A1","A2","B1","B2"
		public bool IsBotSignal;     // B* true, A* false (alert-only)
		public bool HasPending;      // keep-alive pending entry (B1 style)
		public bool PendingIsBuy;
		public double PendingRefExtreme;
		public int PendingOffsetTicks;
		public int PendingStopTicks;
		public int PendingTargetTicks;
		public int PendingBar;
		public int Generation;       // bumps on NewEntry/Migrate/Cancel/Fire
		public bool HasFire;         // one-shot fire this generation (B2 style)
		public bool FireIsBuy;
		public double FireRefExtreme;
		public int FireOffsetTicks;
		public int FireStopTicks;
		public int FireTargetTicks;
		public int FireBar;
		public int EnvDir;           // A1: -1 short / 0 range / +1 long
		public string Status;        // short HUD text
	}

	/// <summary>Contract every independent KAT signal indicator implements and publishes via the bus.</summary>
	public interface IKatSignalProvider
	{
		string SignalId { get; }
		bool IsBotSignal { get; }
		KatSignalSnapshot GetSnapshot();
	}

	/// <summary>
	/// Chart-scoped provider registry. Key = instrument|barsPeriod|chartId.
	/// Thread-safe enough for NT8 data + UI (lock around map).
	/// </summary>
	public static class KatSignalBus
	{
		private static readonly object Gate = new object();
		private static readonly Dictionary<string, List<WeakReference>> ByKey =
			new Dictionary<string, List<WeakReference>>(StringComparer.Ordinal);

		// ponytail: chartId ignored (always 0) so Scalper + signals match even when ChartControl
		// is null at DataLoaded. Ceiling: two charts same instrument+TF share the bus — upgrade
		// by re-registering when ChartControl becomes available with a real chart id.
		public static string MakeKey(string instrumentFullName, string barsPeriodLabel, int chartId)
		{
			if (string.IsNullOrEmpty(instrumentFullName)) instrumentFullName = "?";
			if (string.IsNullOrEmpty(barsPeriodLabel)) barsPeriodLabel = "?";
			return instrumentFullName + "|" + barsPeriodLabel + "|0";
		}

		public static void Register(string key, IKatSignalProvider provider)
		{
			if (string.IsNullOrEmpty(key) || provider == null) return;
			lock (Gate)
			{
				List<WeakReference> list;
				if (!ByKey.TryGetValue(key, out list))
				{
					list = new List<WeakReference>();
					ByKey[key] = list;
				}
				// avoid double-register same instance
				for (int i = list.Count - 1; i >= 0; i--)
				{
					object t = list[i].Target;
					if (t == null) { list.RemoveAt(i); continue; }
					if (ReferenceEquals(t, provider)) return;
				}
				list.Add(new WeakReference(provider));
			}
		}

		public static void Unregister(string key, IKatSignalProvider provider)
		{
			if (string.IsNullOrEmpty(key) || provider == null) return;
			lock (Gate)
			{
				List<WeakReference> list;
				if (!ByKey.TryGetValue(key, out list)) return;
				for (int i = list.Count - 1; i >= 0; i--)
				{
					object t = list[i].Target;
					if (t == null || ReferenceEquals(t, provider))
						list.RemoveAt(i);
				}
				if (list.Count == 0) ByKey.Remove(key);
			}
		}

		/// <summary>Live providers for a chart key (dead weak refs pruned).</summary>
		public static List<IKatSignalProvider> GetProviders(string key)
		{
			var result = new List<IKatSignalProvider>();
			if (string.IsNullOrEmpty(key)) return result;
			lock (Gate)
			{
				List<WeakReference> list;
				if (!ByKey.TryGetValue(key, out list)) return result;
				for (int i = list.Count - 1; i >= 0; i--)
				{
					object t = list[i].Target;
					if (t == null) { list.RemoveAt(i); continue; }
					IKatSignalProvider p = t as IKatSignalProvider;
					if (p != null) result.Add(p);
				}
				if (list.Count == 0) ByKey.Remove(key);
			}
			return result;
		}

		/// <summary>Snapshots copy for orchestrator (safe after lock released).</summary>
		public static List<KatSignalSnapshot> GetSnapshots(string key)
		{
			var snaps = new List<KatSignalSnapshot>();
			foreach (IKatSignalProvider p in GetProviders(key))
			{
				try
				{
					KatSignalSnapshot s = p.GetSnapshot();
					if (s != null) snaps.Add(s);
				}
				catch { }
			}
			return snaps;
		}
	}
}
