using HarmonyLib;

namespace ArcadiusUnofficialFixes
{
	// F11 is handled twice, by SettingsHandler.Update and PlayfabAccountSystem.Update, each with its own
	// isFullscreen that starts false although the game starts fullscreen, and that the Fullscreen checkbox
	// doesn't update. So F11 can take two presses, or the two cancel out. PlayfabAccountSystem.Update (which does
	// nothing else) is skipped, and SettingsHandler's state and checkbox follow every mode change.
	internal static class F11Fix
	{
		public static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(PlayfabAccountSystem), "Update"),
				prefix: new HarmonyMethod(typeof(F11Fix), nameof(BeforePlayfabUpdate)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.SetFullscreen)),
				postfix: new HarmonyMethod(typeof(F11Fix), nameof(AfterSetFullscreen)));
		}

		public static bool BeforePlayfabUpdate()
		{
			return !UnofficialFixesEntry.FixF11Fullscreen;
		}

		public static void AfterSetFullscreen(SettingsHandler __instance, bool isFullscreen)
		{
			if (!UnofficialFixesEntry.FixF11Fullscreen)
			{
				return;
			}
			Traverse.Create(__instance).Field("isFullscreen").SetValue(isFullscreen);
			__instance.fullscreenToggle.SetIsOnWithoutNotify(isFullscreen);
		}
	}
}
