using ArcadiusModLoader;
using HarmonyLib;

namespace ArcadiusUnofficialFixes
{
	// Fixes for things the game gets wrong on its own, unrelated to any feature mod. Each fix has a switch in
	// config.json, read again on F9, so one that misbehaves after a game update can be turned off alone.
	// Every fix is patched in at load and checks its switch when it runs.
	public sealed class UnofficialFixesEntry : IMod
	{
		internal static ModContext Context;

		internal static bool RememberQuality;

		internal static bool RememberDisplayMode;

		internal static bool FixF11Fullscreen;

		public void OnLoad(ModContext context)
		{
			Context = context;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			Harmony harmony = context.Harmony;
			QualityFix.Patch(harmony);
			DisplayModeFix.Patch(harmony);
			F11Fix.Patch(harmony);
		}

		private static void ReadSettings()
		{
			ModConfig config = Context.Config;
			RememberQuality = config.Get("rememberQuality", true);
			RememberDisplayMode = config.Get("rememberDisplayMode", true);
			FixF11Fullscreen = config.Get("fixF11Fullscreen", true);
		}
	}
}
