using HarmonyLib;
using TMPro;

namespace ArcadiusUnofficialFixes
{
	// The game never restores the Quality setting, and UserInterfaceBehaviour.OpenSettings resets its dropdown to
	// QualitySettings' level, which is always the first option (the game has one quality level and picks the
	// pipeline asset itself). The choice is saved as "qualityLevel" and restored at startup and on opening.
	internal static class QualityFix
	{
		// Set while OpenSettings resets the dropdown, so its ChangeQuality call is skipped.
		private static bool _opening;

		// -1 until the player picks a quality level.
		private static int Saved => UnofficialFixesEntry.Context.Config.Get("qualityLevel", -1);

		public static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), "Start"),
				postfix: new HarmonyMethod(typeof(QualityFix), nameof(AfterStart)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.ChangeQuality)),
				prefix: new HarmonyMethod(typeof(QualityFix), nameof(BeforeChangeQuality)),
				postfix: new HarmonyMethod(typeof(QualityFix), nameof(AfterChangeQuality)));
			// UserInterfaceBehaviour is a NetworkBehaviour, so it's named rather than referenced.
			harmony.Patch(AccessTools.Method("UserInterfaceBehaviour:OpenSettings"),
				prefix: new HarmonyMethod(typeof(QualityFix), nameof(BeforeOpenSettings)),
				postfix: new HarmonyMethod(typeof(QualityFix), nameof(AfterOpenSettings)));
		}

		public static void AfterStart(SettingsHandler __instance)
		{
			if (!UnofficialFixesEntry.RememberQuality)
			{
				return;
			}
			int quality = Saved;
			if (quality >= 0 && quality < __instance.qualityLevels.Length && quality < __instance.m_Dropdown_Quality.options.Count)
			{
				__instance.m_Dropdown_Quality.SetValueWithoutNotify(quality);
				__instance.ChangeQuality(quality);
			}
		}

		public static bool BeforeChangeQuality()
		{
			return !_opening;
		}

		public static void AfterChangeQuality(int index)
		{
			if (UnofficialFixesEntry.RememberQuality && !_opening && index != Saved)
			{
				UnofficialFixesEntry.Context.Config.Set("qualityLevel", index);
			}
		}

		public static void BeforeOpenSettings()
		{
			_opening = UnofficialFixesEntry.RememberQuality && Saved >= 0;
		}

		public static void AfterOpenSettings(object __instance)
		{
			if (!_opening)
			{
				return;
			}
			_opening = false;
			if (Traverse.Create(__instance).Field("dropdown").GetValue() is TMP_Dropdown dropdown && Saved < dropdown.options.Count)
			{
				dropdown.SetValueWithoutNotify(Saved);
				dropdown.RefreshShownValue();
			}
		}
	}
}
