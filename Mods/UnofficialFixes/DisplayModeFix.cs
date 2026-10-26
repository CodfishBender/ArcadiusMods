using System;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;

namespace ArcadiusUnofficialFixes
{
	// SettingsHandler.Start always calls SetFullscreen(true), so the game starts fullscreen at the largest
	// resolution whatever the player chose. The mode is saved as "fullscreen" and the window size as
	// "windowResolution", and Start uses them instead.
	internal static class DisplayModeFix
	{
		private static ModConfig Config => UnofficialFixesEntry.Context.Config;

		// Set while SettingsHandler.Start runs.
		private static bool _starting;

		public static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), "Start"),
				prefix: new HarmonyMethod(typeof(DisplayModeFix), nameof(BeforeStart)),
				postfix: new HarmonyMethod(typeof(DisplayModeFix), nameof(AfterStart)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.SetFullscreen)),
				prefix: new HarmonyMethod(typeof(DisplayModeFix), nameof(BeforeSetFullscreen)),
				postfix: new HarmonyMethod(typeof(DisplayModeFix), nameof(AfterSetFullscreen)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.SetScreenResolution)),
				postfix: new HarmonyMethod(typeof(DisplayModeFix), nameof(AfterSetScreenResolution)));
		}

		public static void BeforeStart()
		{
			_starting = true;
		}

		public static void AfterStart()
		{
			_starting = false;
		}

		// At startup, uses the saved mode (and window size) instead of fullscreen. Before the first save, the
		// mode the game itself stored.
		public static void BeforeSetFullscreen(SettingsHandler __instance, ref bool isFullscreen)
		{
			if (!_starting || !UnofficialFixesEntry.RememberDisplayMode)
			{
				return;
			}
			isFullscreen = Config.Get("fullscreen", PlayerPrefs.GetInt("fullscreen1", 1) != 0);
			int index = SavedResolutionIndex(__instance.resolutions);
			if (index >= 0)
			{
				Traverse.Create(__instance).Field("currentResolutionIndex").SetValue(index);
				__instance.resolutionDropDown.SetValueWithoutNotify(index);
				__instance.resolutionDropDown.RefreshShownValue();
			}
		}

		public static void AfterSetFullscreen(bool isFullscreen)
		{
			if (UnofficialFixesEntry.RememberDisplayMode)
			{
				Config.Set("fullscreen", isFullscreen);
			}
		}

		public static void AfterSetScreenResolution(SettingsHandler __instance, int i)
		{
			if (!UnofficialFixesEntry.RememberDisplayMode || __instance.resolutions == null || i < 0 || i >= __instance.resolutions.Length)
			{
				return;
			}
			Resolution resolution = __instance.resolutions[i];
			string size = resolution.width + "x" + resolution.height;
			if (Config.Get("windowResolution", "") != size)
			{
				Config.Set("windowResolution", size);
			}
		}

		// The resolution list can differ between launches (another monitor), so the size is saved, not the index.
		// The last match has the highest refresh rate.
		private static int SavedResolutionIndex(Resolution[] resolutions)
		{
			string[] size = (Config.Get("windowResolution", "") ?? "").Split('x');
			if (resolutions == null || size.Length != 2 || !int.TryParse(size[0], out int width) || !int.TryParse(size[1], out int height))
			{
				return -1;
			}
			return Array.FindLastIndex(resolutions, r => r.width == width && r.height == height);
		}
	}
}
