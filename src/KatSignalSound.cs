/* KatSignalSound.cs - neutral NinjaScript sound dropdown converters for standalone signals. */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using KAT.Signals;

// Dropdown of .wav files from a custom folder (when the indicator exposes AlertSoundCustomPath),
// then NT8's user sounds folder, then the install sounds folder.
public class KatSignalSoundConverter : TypeConverter
{
	public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }

	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
	{
		var list = new List<string>();
		try
		{
			string customDir = "";
			object instance = context == null ? null : context.Instance;
			if (instance != null)
			{
				var p = instance.GetType().GetProperty("AlertSoundCustomPath") ?? instance.GetType().GetProperty("SignalSoundCustomPath");
				if (p != null)
					customDir = p.GetValue(instance, null) as string ?? "";
			}
			string userDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds");
			Directory.CreateDirectory(userDir);
			string installDir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
			list.AddRange(Kat34ScalperSound.ListSounds(customDir, userDir, installDir));
		}
		catch { }
		return new StandardValuesCollection(list);
	}
}

public class Kat34ScalperSoundConverter : KatSignalSoundConverter { }
