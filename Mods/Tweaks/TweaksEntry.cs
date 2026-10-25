using System;
using ArcadiusModLoader;
using UnityEngine;

namespace ArcadiusTweaks
{
	// Example mod: percentage multipliers from config.json, applied through the loader's game hooks.
	// Edit Mods/Tweaks/config.json and press the reload key (F9) to apply changes without restarting.
	public sealed class TweaksEntry : IMod
	{
		private ModConfig _config;

		private int _sellPercent;

		private int _xpPercent;

		public void OnLoad(ModContext context)
		{
			_config = context.Config;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			GameHooks.AddSellValueModifier((value, category) => Scale(value, _sellPercent));
			GameHooks.AddXpModifier((xp, skill) => Scale(xp, _xpPercent));
		}

		private void ReadSettings()
		{
			_sellPercent = Math.Max(0, _config.Get("sellValuePercent", 100));
			_xpPercent = Math.Max(0, _config.Get("xpPercent", 100));
		}

		private static int Scale(int value, int percent)
		{
			return percent == 100 || value <= 0 ? value : Mathf.CeilToInt(value * percent / 100f);
		}
	}
}
