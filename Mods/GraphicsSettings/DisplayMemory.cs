using System;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;

namespace ArcadiusGraphicsSettings
{
	// Remembers the game's own Quality, Fullscreen and Resolution settings across launches. The game never
	// restores the quality level, resets its dropdown to the first option whenever the Settings page opens,
	// and always starts fullscreen at the largest resolution.
	internal static class DisplayMemory
	{
		private static ModConfig Config => GraphicsSettingsEntry.Context.Config;

		// Set while SettingsHandler.Start runs its unconditional SetFullscreen(true).
		private static bool _starting;

		// Set while UserInterfaceBehaviour.OpenSettings resets the quality dropdown.
		private static bool _opening;

		public static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), "Start"),
				prefix: new HarmonyMethod(typeof(DisplayMemory), nameof(BeforeStart)),
				postfix: new HarmonyMethod(typeof(DisplayMemory), nameof(AfterStart)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.SetFullscreen)),
				prefix: new HarmonyMethod(typeof(DisplayMemory), nameof(BeforeSetFullscreen)),
				postfix: new HarmonyMethod(typeof(DisplayMemory), nameof(AfterSetFullscreen)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.SetScreenResolution)),
				postfix: new HarmonyMethod(typeof(DisplayMemory), nameof(AfterSetScreenResolution)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.ChangeQuality)),
				prefix: new HarmonyMethod(typeof(DisplayMemory), nameof(BeforeChangeQuality)),
				postfix: new HarmonyMethod(typeof(DisplayMemory), nameof(AfterChangeQuality)));
			harmony.Patch(AccessTools.Method("UserInterfaceBehaviour:OpenSettings"),
				prefix: new HarmonyMethod(typeof(DisplayMemory), nameof(BeforeOpenSettings)),
				postfix: new HarmonyMethod(typeof(DisplayMemory), nameof(AfterOpenSettings)));
			// Its Update only toggles fullscreen on F11, with its own state, as SettingsHandler.Update also does:
			// together one press could cancel the other.
			harmony.Patch(AccessTools.Method(typeof(PlayfabAccountSystem), "Update"),
				prefix: new HarmonyMethod(typeof(DisplayMemory), nameof(SkipUpdate)));
		}

		public static bool SkipUpdate()
		{
			return false;
		}

		// -1 until the player picks a quality level.
		private static int SavedQuality => Config.Get("qualityLevel", -1);

		public static void BeforeStart()
		{
			_starting = true;
		}

		public static void AfterStart(SettingsHandler __instance)
		{
			_starting = false;
			int quality = SavedQuality;
			if (quality >= 0 && quality < __instance.qualityLevels.Length && quality < __instance.m_Dropdown_Quality.options.Count)
			{
				__instance.m_Dropdown_Quality.SetValueWithoutNotify(quality);
				__instance.ChangeQuality(quality);
			}
		}

		// At startup, uses the saved mode (and window size) instead of fullscreen.
		public static void BeforeSetFullscreen(SettingsHandler __instance, ref bool isFullscreen)
		{
			if (!_starting)
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

		// Keeps the checkbox and F11's state in step with the mode, whichever of them changed it.
		public static void AfterSetFullscreen(SettingsHandler __instance, bool isFullscreen)
		{
			Traverse.Create(__instance).Field("isFullscreen").SetValue(isFullscreen);
			__instance.fullscreenToggle.SetIsOnWithoutNotify(isFullscreen);
			if (Config.Get("fullscreen", !isFullscreen) != isFullscreen)
			{
				Config.Set("fullscreen", isFullscreen);
			}
		}

		public static void AfterSetScreenResolution(SettingsHandler __instance, int i)
		{
			if (__instance.resolutions != null && i >= 0 && i < __instance.resolutions.Length)
			{
				Resolution resolution = __instance.resolutions[i];
				string size = resolution.width + "x" + resolution.height;
				if (Config.Get("windowResolution", "") != size)
				{
					Config.Set("windowResolution", size);
				}
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

		public static void BeforeOpenSettings()
		{
			_opening = SavedQuality >= 0;
		}

		public static bool BeforeChangeQuality()
		{
			return !_opening;
		}

		public static void AfterChangeQuality(int index)
		{
			if (!_opening && index != SavedQuality)
			{
				Config.Set("qualityLevel", index);
			}
		}

		// The game set the dropdown to QualitySettings' level (always the first, as the game has one): show the
		// saved one instead.
		public static void AfterOpenSettings(object __instance)
		{
			if (!_opening)
			{
				return;
			}
			_opening = false;
			if (Traverse.Create(__instance).Field("dropdown").GetValue() is TMPro.TMP_Dropdown dropdown && SavedQuality < dropdown.options.Count)
			{
				dropdown.SetValueWithoutNotify(SavedQuality);
				dropdown.RefreshShownValue();
			}
		}
	}
}
