/*
 * Kat34Scalper.Signal.B1.cs — Bot Signal sub-module B1: 34bounce8+ (partial class Kat34Scalper).
 * Independent Bot Signal B1 (34bounce8+ setup — 34+8+Bounce ema34 touch pending entry).
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
		// --- B1 sub-module state ---
		private volatile bool cachedB1;
		private volatile bool b1BackfillPending;
		// private SignalRecord b1SellRecord;
		// private SignalRecord b1BuyRecord;
		// private string b1SellTextTag = "";
		// private string b1BuyTextTag = "";
		// private int b1SellState;
		// private int b1BuyState;

		private void SetB1Signal(bool on)
		{
			cachedB1 = on;
			B1Enabled = on;
			Print(string.Format("[Kat34Scalper][SignalB1] toggled {0}", on ? "ON" : "OFF"));
			if (on)
			{
				b1BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
		}

		private void BackfillB1()
		{
			if (!cachedB1) return;
			// Bot Signal B1 backfill logic — see docs/SIGNALS.md for B1 spec (89-34 pullback)
			Print("[Kat34Scalper][SignalB1] backfill done");
		}
	}
}
