/*
 * Kat34Scalper.Signal.A2.cs — standalone Signal A2 shell.
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private void SetA2Signal(bool on) { SetAlertA2Signal(on); }
	}
}
