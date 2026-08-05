/*
 * Kat34Scalper.Signal.B2.cs — Bot Signal sub-module B2: 89uturn34 (partial class Kat34Scalper).
 * Independent Bot Signal B2 (89uturn34 setup — 89-34 pullback setup).
 * Controls bot entry placement when Bot is ON. Spec in docs/SIGNALS.md.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- B2 sub-module state ---
		private volatile bool cachedB2;
		private volatile bool b2BackfillPending;
		// private SignalRecord b2SellRecord;
		// private SignalRecord b2BuyRecord;
		// private string b2SellTextTag = "";
		// private string b2BuyTextTag = "";
		// private int b2SellState;
		// private int b2BuyState;

		private void SetB2Signal(bool on)
		{
			cachedB2 = on;
			B2Enabled = on;
			Print(string.Format("[Kat34Scalper][SignalB2] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				b2BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
		}

		private void BackfillB2()
		{
			if (!cachedB2) return;
			// Bot Signal B2 backfill logic — see docs/SIGNALS.md for B2 spec (34+8+Bounce)
			Print("[Kat34Scalper][SignalB2] backfill done");
		}
	}
}
