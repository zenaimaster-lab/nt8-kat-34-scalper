/*
 * Kat34Scalper.Signal.A1.cs — standalone Signal A1 shell.
 * The real implementation will live in a separate indicator class; Kat34Scalper keeps orchestration.
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
		private void SetA1Signal(bool on) { SetAlertA1Signal(on); }
	}
}
